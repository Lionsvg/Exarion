// ===========================================================================
//  Exarion - Cible de rendu HDR
//
//  POURQUOI. Jusqu'ici, chaque shader ecrivait directement a l'ecran, en
//  appliquant lui-meme l'exposition, le tone mapping et la conversion sRGB.
//  Ca marche, mais ca perd tout ce qui depasse le blanc : une lampe a 8 fois
//  la luminosite du papier ressort a 1,0, exactement comme le papier.
//
//  Le bloom a precisement besoin de cette information. Sans elle, impossible
//  de distinguer une source vive d'un mur clair - les deux seraient a 1,0 et
//  produiraient le meme halo.
//
//  On rend donc la scene dans un tampon FLOTTANT, ou 8,0 reste 8,0. Le tone
//  mapping devient une passe finale, appliquee UNE SEULE FOIS a l'image
//  entiere. C'est aussi plus juste : le ciel et la geometrie subissent
//  desormais la meme courbe.
//
//  ---------------------------------------------------------------------
//  LE MSAA COMPLIQUE LES CHOSES, ET IL FAUT DEUX TAMPONS
//
//  L'anticrenelage exige un tampon MULTI-ECHANTILLONNE, qui ne se lit pas
//  comme une texture ordinaire. On en garde donc deux :
//     - celui ou on dessine, multi-echantillonne ;
//     - celui qu'on lit, ordinaire, obtenu par RESOLUTION du premier.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Exarion.Engine.Graphics
{
    public sealed class HdrTarget : IDisposable
    {
        private const string CompositeVertex = @"#version 330 core

out vec2 vUv;

void main()
{
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    vUv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}";

        private const string CompositeFragment = @"#version 330 core

in vec2 vUv;
out vec4 FragColor;

uniform sampler2D uScene;
uniform sampler2D uBloom;
uniform float uExposure;
uniform float uBloomIntensity;

vec3 toSrgb(vec3 c) { return pow(max(c, 0.0), vec3(1.0 / 2.2)); }

void main()
{
    vec3 color = texture(uScene, vUv).rgb;

    // Le bloom s'AJOUTE avant le tone mapping. L'ajouter apres donnerait un
    // voile laiteux au lieu d'un halo : la courbe doit voir la lumiere
    // totale pour la rouler correctement vers le blanc.
    color += texture(uBloom, vUv).rgb * uBloomIntensity;

    color *= uExposure;

    // Tone mapping ACES (approximation de Narkowicz). Applique ICI et une
    // seule fois : avant, chaque shader avait le sien, et le ciel echappait
    // a la courbe.
    const float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
    color = clamp((color * (a * color + b)) / (color * (c * color + d) + e), 0.0, 1.0);

    FragColor = vec4(toSrgb(color), 1.0);
}";

        private readonly GL _gl;
        private readonly ShaderProgram _composite;
        private readonly uint _emptyVao;

        private uint _msaaFbo, _msaaColor, _msaaDepth;
        private uint _resolveFbo, _resolveColor;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public uint Samples { get; }

        /// <summary>La texture lisible, apres resolution du multi-echantillonnage.</summary>
        public uint ResolvedTexture => _resolveColor;

        /// <summary>Texture de bloom a ajouter. 0 = aucune.</summary>
        public uint BloomTexture { get; set; }

        public float BloomIntensity { get; set; } = 1f;

        public HdrTarget(GL gl, int width, int height, uint samples = 4)
        {
            _gl = gl;
            Samples = Math.Max(1u, samples);

            _composite = new ShaderProgram(gl, CompositeVertex, CompositeFragment);
            _emptyVao = _gl.GenVertexArray();

            Resize(width, height);
        }

        public unsafe void Resize(int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            if (width == Width && height == Height && _msaaFbo != 0)
                return;

            Release();

            Width = width;
            Height = height;

            // --- Tampon de dessin, multi-echantillonne ---
            _msaaColor = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2DMultisample, _msaaColor);
            _gl.TexImage2DMultisample(TextureTarget.Texture2DMultisample, Samples,
                InternalFormat.Rgba16f, (uint)width, (uint)height, true);

            _msaaDepth = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaDepth);
            _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, Samples,
                InternalFormat.DepthComponent24, (uint)width, (uint)height);

            _msaaFbo = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2DMultisample, _msaaColor, 0);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                RenderbufferTarget.Renderbuffer, _msaaDepth);

            var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete)
                throw new InvalidOperationException($"Tampon HDR multi-echantillonne incomplet : {status}");

            // --- Tampon de lecture, ordinaire ---
            _resolveColor = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _resolveColor);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f,
                (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.Float, null);

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            _resolveFbo = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _resolveFbo);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, _resolveColor, 0);

            status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete)
                throw new InvalidOperationException($"Tampon HDR de resolution incomplet : {status}");

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        /// <summary>Tout ce qui suit est dessine en HDR, hors ecran.</summary>
        public void Begin()
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFbo);
            _gl.Viewport(0, 0, (uint)Width, (uint)Height);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        /// <summary>
        /// Resout le multi-echantillonnage vers la texture lisible. A appeler
        /// avant toute lecture - un tampon multi-echantillonne ne s'echantillonne
        /// pas comme une texture ordinaire.
        /// </summary>
        public void Resolve()
        {
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFbo);
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _resolveFbo);

            _gl.BlitFramebuffer(0, 0, Width, Height, 0, 0, Width, Height,
                ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        /// <summary>
        /// Exposition, bloom, tone mapping, sRGB : tout ce qui etait
        /// eparpille dans les shaders, en une passe.
        /// </summary>
        public void Compose(Vector2D<int> screenSize, float exposure)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Viewport(0, 0, (uint)Math.Max(1, screenSize.X), (uint)Math.Max(1, screenSize.Y));

            _gl.Disable(EnableCap.DepthTest);

            _composite.Use();

            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _resolveColor);
            _composite.SetUniform("uScene", 0);

            // Sans bloom, on lit la texture de scene elle-meme avec une
            // intensite nulle : un sampler non lie donne du noir sur
            // certains pilotes et un avertissement sur d'autres.
            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, BloomTexture != 0 ? BloomTexture : _resolveColor);
            _composite.SetUniform("uBloom", 1);

            _composite.SetUniform("uExposure", exposure);
            _composite.SetUniform("uBloomIntensity", BloomTexture != 0 ? BloomIntensity : 0f);

            _gl.BindVertexArray(_emptyVao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            _gl.BindVertexArray(0);

            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.Enable(EnableCap.DepthTest);
        }

        private void Release()
        {
            if (_msaaFbo != 0) _gl.DeleteFramebuffer(_msaaFbo);
            if (_msaaColor != 0) _gl.DeleteTexture(_msaaColor);
            if (_msaaDepth != 0) _gl.DeleteRenderbuffer(_msaaDepth);
            if (_resolveFbo != 0) _gl.DeleteFramebuffer(_resolveFbo);
            if (_resolveColor != 0) _gl.DeleteTexture(_resolveColor);

            _msaaFbo = _msaaColor = _msaaDepth = 0;
            _resolveFbo = _resolveColor = 0;
        }

        public void Dispose()
        {
            Release();
            _gl.DeleteVertexArray(_emptyVao);
            _composite.Dispose();
        }
    }
}
