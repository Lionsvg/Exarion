// ===========================================================================
//  Exarion - Carte d'ombre directionnelle
//
//  PRINCIPE : on rend la scene une premiere fois DEPUIS LA LUMIERE, en ne
//  gardant que la profondeur. Au rendu final, chaque pixel est reprojete
//  dans cet espace : si quelque chose etait plus proche de la lumiere que
//  lui, il est dans l'ombre.
//
//  ---------------------------------------------------------------------
//  LES TROIS DEFAUTS CLASSIQUES, ET CE QU'ON FAIT CONTRE
//  ---------------------------------------------------------------------
//
//  ACNE  Des rayures sombres sur les surfaces eclairees. Cause : la carte
//        a une resolution finie, donc la profondeur stockee ne correspond
//        jamais exactement a celle du pixel. Parade : un BIAIS, augmente
//        quand la surface est rasante par rapport a la lumiere.
//
//  PETER-PANNING  L'ombre se decolle de l'objet, qui semble leviter. C'est
//        le prix d'un biais trop grand. Parade : ne rendre que les FACES
//        ARRIERE dans la passe d'ombre, ce qui decale l'erreur a l'interieur
//        des objets, la ou personne ne la voit.
//
//  BORDS DURS  Une ombre en escalier. Parade : PCF - on echantillonne
//        plusieurs texels voisins et on moyenne.
//
//  Tout est reglable depuis la console : sans ca, chaque essai coute une
//  recompilation, et ces defauts se reglent a l'oeil.
// ===========================================================================

using System;
using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Exarion.Engine.Graphics
{
    public sealed class ShadowMap : IDisposable
    {
        private const string DepthVertex = @"#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 4) in vec4 aJoints;
layout (location = 5) in vec4 aWeights;

uniform mat4 uModel;
uniform mat4 uLightViewProj;

// Le meme skinning que dans SceneRenderer, et il FAUT le refaire ici.
//
// Cette passe a son propre shader : sans ces lignes, l'ombre d'un
// personnage reste figee dans sa pose de liaison pendant qu'il court -
// bras ecartes, immobile, sous un corps qui bouge. Deux geometries
// differentes pour le meme objet.
const int MAX_BONES = 48;

uniform mat4 uBones[MAX_BONES];
uniform int  uSkinned;

void main()
{
    vec3 position = aPos;

    if (uSkinned != 0)
    {
        mat4 skin = aWeights.x * uBones[int(aJoints.x)]
                  + aWeights.y * uBones[int(aJoints.y)]
                  + aWeights.z * uBones[int(aJoints.z)]
                  + aWeights.w * uBones[int(aJoints.w)];

        position = (skin * vec4(aPos, 1.0)).xyz;
    }

    gl_Position = uLightViewProj * uModel * vec4(position, 1.0);
}";

        private const string DepthFragment = @"#version 330 core
void main()
{
    // Rien a ecrire : seule la profondeur nous interesse, et OpenGL
    // l'ecrit automatiquement dans le tampon attache.
}";

        private readonly GL _gl;
        private readonly ShaderProgram _depthShader;
        private uint _framebuffer;
        private uint _depthTexture;

        public uint Resolution { get; private set; }

        /// <summary>Matrice qui transforme le monde vers l'espace de la lumiere.</summary>
        public Matrix4x4 LightViewProjection { get; private set; } = Matrix4x4.Identity;

        /// <summary>Demi-cote de la zone couverte, en metres.</summary>
        public float Extent { get; set; } = 45f;

        /// <summary>Distance entre la lumiere virtuelle et le centre de la zone.</summary>
        public float Distance { get; set; } = 60f;

        public float NearPlane { get; set; } = 1f;
        public float FarPlane { get; set; } = 160f;

        /// <summary>
        /// Ne rendre que les faces arriere pendant la passe d'ombre.
        /// Reduit fortement l'acne sans exiger un gros biais.
        /// </summary>
        public bool CullFrontFaces { get; set; } = true;

        public uint DepthTexture => _depthTexture;

        public ShadowMap(GL gl, uint resolution = 2048)
        {
            _gl = gl;
            _depthShader = new ShaderProgram(gl, DepthVertex, DepthFragment);
            Resize(resolution);
        }

        public void Resize(uint resolution)
        {
            resolution = Math.Clamp(resolution, 256u, 8192u);

            if (resolution == Resolution && _framebuffer != 0)
                return;

            Release();
            Resolution = resolution;

            _depthTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _depthTexture);

            unsafe
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24,
                    Resolution, Resolution, 0, PixelFormat.DepthComponent, PixelType.Float, null);
            }

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            // Hors de la carte = pleine lumiere. Sans ce bord blanc, tout ce
            // qui depasse la zone couverte serait dans l'ombre.
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

            Span<float> border = stackalloc float[] { 1f, 1f, 1f, 1f };
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, border);

            _framebuffer = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D, _depthTexture, 0);

            // Aucune cible couleur : on ne rend que la profondeur.
            _gl.DrawBuffer(DrawBufferMode.None);
            _gl.ReadBuffer(ReadBufferMode.None);

            var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete)
                throw new InvalidOperationException($"Tampon d'ombre incomplet : {status}");

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        /// <summary>
        /// Ouvre la passe d'ombre. Tout ce qui est dessine ensuite alimente
        /// la carte de profondeur.
        /// </summary>
        public void Begin(Vector3 lightDirection, Vector3 center)
        {
            LightViewProjection = BuildLightMatrix(lightDirection, center);

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            _gl.Viewport(0, 0, Resolution, Resolution);
            _gl.Clear(ClearBufferMask.DepthBufferBit);

            if (CullFrontFaces)
            {
                _gl.Enable(EnableCap.CullFace);
                _gl.CullFace(TriangleFace.Front);
            }

            _depthShader.Use();
            _depthShader.SetUniform("uLightViewProj", LightViewProjection);
        }

        public void Draw(Mesh mesh, in Matrix4x4 model)
        {
            _depthShader.SetUniform("uModel", model);
            mesh.Draw();
        }

        /// <summary>
        /// Envoie les matrices d'os pour les prochains dessins de cette
        /// passe. Vide = maillage rigide.
        ///
        /// Doit recevoir EXACTEMENT les memes matrices que SceneRenderer :
        /// une ombre calculee sur une autre pose serait celle d'un autre
        /// personnage.
        /// </summary>
        public void SetBones(ReadOnlySpan<Matrix4x4> bones)
        {
            if (bones.Length == 0)
            {
                _depthShader.SetUniform("uSkinned", 0);
                return;
            }

            int count = Math.Min(bones.Length, MaxBones);

            for (int i = 0; i < count; i++)
                _depthShader.SetUniform($"uBones[{i}]", bones[i]);

            _depthShader.SetUniform("uSkinned", 1);
        }

        public void ClearBones() => _depthShader.SetUniform("uSkinned", 0);

        /// <summary>Doit suivre MAX_BONES du shader ci-dessus.</summary>
        public const int MaxBones = 48;

        /// <summary>Referme la passe et restaure l'ecran.</summary>
        public void End(Vector2D<int> screenSize)
        {
            if (CullFrontFaces)
                _gl.Disable(EnableCap.CullFace);

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Viewport(0, 0, (uint)Math.Max(1, screenSize.X), (uint)Math.Max(1, screenSize.Y));
        }

        /// <summary>
        /// Construit la matrice monde -> espace lumiere.
        ///
        /// Une lumiere directionnelle n'a pas de position : on en invente une,
        /// reculee de Distance le long du rayon, et on projette en
        /// ORTHOGRAPHIQUE - des rayons paralleles ne convergent pas.
        /// </summary>
        public Matrix4x4 BuildLightMatrix(Vector3 lightDirection, Vector3 center)
        {
            Vector3 dir = Vector3.Normalize(lightDirection);

            // Un vecteur "haut" non parallele a la direction, sinon la
            // matrice de vue degenere.
            Vector3 up = MathF.Abs(dir.Y) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;

            Vector3 eye = center - dir * Distance;
            Matrix4x4 view = Matrix4x4.CreateLookAt(eye, center, up);

            return view * OrthographicForOpenGL(Extent, NearPlane, FarPlane);
        }

        /// <summary>
        /// Projection orthographique symetrique, profondeur dans [-1,1].
        ///
        /// Meme piege que la perspective : Matrix4x4.CreateOrthographic de
        /// System.Numerics produit [0,1] (convention DirectX). L'utiliser
        /// ici ecraserait toute la carte d'ombre dans la moitie du tampon.
        /// Ecrite en convention vecteur-ligne, comme le reste.
        /// </summary>
        public static Matrix4x4 OrthographicForOpenGL(float extent, float near, float far)
        {
            float range = far - near;

            return new Matrix4x4
            {
                M11 = 1f / extent,
                M22 = 1f / extent,
                M33 = -2f / range,
                M43 = -(far + near) / range,
                M44 = 1f,
            };
        }

        private void Release()
        {
            if (_framebuffer != 0)
            {
                _gl.DeleteFramebuffer(_framebuffer);
                _framebuffer = 0;
            }

            if (_depthTexture != 0)
            {
                _gl.DeleteTexture(_depthTexture);
                _depthTexture = 0;
            }
        }

        public void Dispose()
        {
            Release();
            _depthShader.Dispose();
        }
    }
}
