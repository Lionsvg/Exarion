// ===========================================================================
//  Exarion - Bloom
//
//  Le halo qui deborde d'une source vive. Ce n'est pas un effet decoratif :
//  c'est ce que fait un objectif reel - et un oeil - devant une lumiere trop
//  intense pour lui. Sans lui, une lampe et un mur blanc se ressemblent.
//
//  TROIS ETAPES :
//    1. EXTRAIRE ce qui depasse le blanc. C'est la que le tampon HDR paie :
//       en LDR, tout serait ecrete a 1,0 et une lampe brillerait comme du
//       papier.
//    2. FLOUTER largement, par un flou gaussien SEPARABLE : une passe
//       horizontale puis une verticale. Un flou 2D de rayon N coute N x N
//       lectures par pixel ; separe, il en coute N + N. A rayon 9, c'est
//       81 contre 18.
//    3. AJOUTER au rendu, AVANT le tone mapping - c'est HdrTarget qui s'en
//       charge.
//
//  Tout se fait en DEMI-RESOLUTION. Un halo est flou par nature : personne
//  ne verra la difference, et ca divise le cout par quatre.
// ===========================================================================

using System;
using System.Numerics;
using Silk.NET.OpenGL;

namespace Exarion.Engine.Graphics
{
    public sealed class Bloom : IDisposable
    {
        private const string FullscreenVertex = @"#version 330 core

out vec2 vUv;

void main()
{
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    vUv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}";

        private const string ExtractFragment = @"#version 330 core

in vec2 vUv;
out vec4 oColor;

uniform sampler2D uScene;
uniform float uThreshold;
uniform float uKnee;
uniform float uExposure;

void main()
{
    // L'EXPOSITION D'ABORD, et ce n'est pas un detail.
    //
    // Le tampon contient des valeurs brutes, avant exposition. Sans cette
    // ligne, le seuil se compare a une echelle qui n'est pas celle de
    // l'image finale : mesure faite, il ne restait que 62 pixels au-dessus
    // de 1,0 sur 921 600, et le halo etait invisible.
    //
    // En exposant d'abord, le seuil devient lisible : 1,0 designe le point
    // ou le tone mapping commence a rouler vers le blanc.
    vec3 c = texture(uScene, vUv).rgb * uExposure;

    // Luminance perceptuelle : le vert compte pour 71 %, le bleu pour 7 %.
    // Une moyenne simple ferait deborder les bleus autant que les jaunes,
    // ce qui ne correspond ni a un objectif ni a un oeil.
    float brightness = dot(c, vec3(0.2126, 0.7152, 0.0722));

    // Seuil ADOUCI. Un seuil net fait apparaitre et disparaitre le halo
    // d'un coup quand la luminosite oscille autour de la valeur - un
    // clignotement tres visible en mouvement. Le genou etale la transition.
    float soft = clamp(brightness - uThreshold + uKnee, 0.0, 2.0 * uKnee);
    soft = soft * soft / (4.0 * uKnee + 0.0001);

    float contribution = max(soft, brightness - uThreshold) / max(brightness, 0.0001);

    oColor = vec4(c * contribution, 1.0);
}";

        private const string BlurFragment = @"#version 330 core

in vec2 vUv;
out vec4 oColor;

uniform sampler2D uSource;
uniform vec2 uDirection;   // (1,0) horizontal, (0,1) vertical

// Noyau gaussien a 9 prises, replie sur 5 poids par symetrie.
const float weights[5] = float[](0.227027, 0.194594, 0.121621, 0.054054, 0.016216);

void main()
{
    vec2 texel = 1.0 / vec2(textureSize(uSource, 0));
    vec3 sum = texture(uSource, vUv).rgb * weights[0];

    for (int i = 1; i < 5; ++i)
    {
        vec2 offset = uDirection * texel * float(i);
        sum += texture(uSource, vUv + offset).rgb * weights[i];
        sum += texture(uSource, vUv - offset).rgb * weights[i];
    }

    oColor = vec4(sum, 1.0);
}";

        private readonly GL _gl;
        private readonly ShaderProgram _extract;
        private readonly ShaderProgram _blur;
        private readonly uint _emptyVao;

        private readonly uint[] _fbo = new uint[2];
        private readonly uint[] _texture = new uint[2];

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>Luminance a partir de laquelle un pixel deborde.</summary>
        public float Threshold { get; set; } = 1f;

        /// <summary>Largeur de la transition autour du seuil.</summary>
        public float Knee { get; set; } = 0.5f;

        /// <summary>Nombre d'aller-retours de flou. Plus = halo plus large.</summary>
        public int Passes { get; set; } = 5;

        /// <summary>Le halo, pret a etre ajoute au rendu.</summary>
        public uint Texture => _texture[0];

        public Bloom(GL gl, int width, int height)
        {
            _gl = gl;
            _extract = new ShaderProgram(gl, FullscreenVertex, ExtractFragment);
            _blur = new ShaderProgram(gl, FullscreenVertex, BlurFragment);
            _emptyVao = _gl.GenVertexArray();

            Resize(width, height);
        }

        public unsafe void Resize(int width, int height)
        {
            // Demi-resolution : un halo est flou par nature.
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);

            if (width == Width && height == Height && _fbo[0] != 0)
                return;

            Release();

            Width = width;
            Height = height;

            for (int i = 0; i < 2; i++)
            {
                _texture[i] = _gl.GenTexture();
                _gl.BindTexture(TextureTarget.Texture2D, _texture[i]);

                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f,
                    (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.Float, null);

                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

                // ClampToEdge, imperativement : en Repeat, une lampe au bord
                // de l'ecran ferait deborder son halo de l'autre cote.
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

                _fbo[i] = _gl.GenFramebuffer();
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo[i]);
                _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.Texture2D, _texture[i], 0);

                var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                if (status != GLEnum.FramebufferComplete)
                    throw new InvalidOperationException($"Tampon de bloom incomplet : {status}");
            }

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        /// <summary>Extrait puis floute. La texture resultante est <see cref="Texture"/>.</summary>
        public void Generate(uint sceneTexture, float exposure)
        {
            _gl.Disable(EnableCap.DepthTest);
            _gl.Viewport(0, 0, (uint)Width, (uint)Height);
            _gl.BindVertexArray(_emptyVao);

            // --- 1. Extraction, vers le tampon 0 ---
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo[0]);

            _extract.Use();
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, sceneTexture);
            _extract.SetUniform("uScene", 0);
            _extract.SetUniform("uThreshold", Threshold);
            _extract.SetUniform("uKnee", MathF.Max(Knee, 0.0001f));
            _extract.SetUniform("uExposure", exposure);

            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

            // --- 2. Flou separable, en va-et-vient entre les deux tampons ---
            _blur.Use();
            _blur.SetUniform("uSource", 0);

            int source = 0;

            for (int pass = 0; pass < Math.Max(1, Passes) * 2; pass++)
            {
                int destination = 1 - source;

                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo[destination]);
                _gl.BindTexture(TextureTarget.Texture2D, _texture[source]);

                // Horizontal aux passes paires, vertical aux impaires : c'est
                // la separation qui rend le flou abordable.
                _blur.SetUniform("uDirection", (pass % 2 == 0) ? new Vector2(1f, 0f) : new Vector2(0f, 1f));

                _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

                source = destination;
            }

            // Le va-et-vient doit se terminer sur le tampon 0, celui que
            // Texture expose. Un nombre PAIR de passes le garantit.
            _gl.BindVertexArray(0);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Enable(EnableCap.DepthTest);
        }

        private void Release()
        {
            for (int i = 0; i < 2; i++)
            {
                if (_fbo[i] != 0) _gl.DeleteFramebuffer(_fbo[i]);
                if (_texture[i] != 0) _gl.DeleteTexture(_texture[i]);

                _fbo[i] = 0;
                _texture[i] = 0;
            }
        }

        public void Dispose()
        {
            Release();
            _gl.DeleteVertexArray(_emptyVao);
            _extract.Dispose();
            _blur.Dispose();
        }
    }
}
