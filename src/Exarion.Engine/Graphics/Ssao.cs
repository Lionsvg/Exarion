// ===========================================================================
//  Exarion - Occlusion ambiante en espace ecran (SSAO)
//
//  A QUOI CA SERT. L'ambiante de notre eclairage est une constante : chaque
//  point recoit la meme lumiere du ciel, qu'il soit en plein air ou au fond
//  d'un recoin. Resultat, les objets ne se posent nulle part - une caisse
//  sur le sol semble toujours flotter un peu, faute de la penombre qui se
//  forme naturellement dans un angle.
//
//  L'IDEE. Pour chaque pixel, on regarde le voisinage : si beaucoup de
//  points autour de lui sont plus proches de la camera que lui, c'est qu'il
//  est enfonce dans un creux, et on l'assombrit. C'est une approximation
//  grossiere de l'occlusion reelle, mais elle ne coute rien d'autre qu'un
//  tampon de profondeur.
//
//  ---------------------------------------------------------------------
//  DECOUPAGE, ET POURQUOI IL FAUT TROIS PASSES
//
//    S1  On rend la scene une fois de plus, en ne gardant que la NORMALE
//        et la PROFONDEUR, en espace vue. C'est ce fichier.
//    S2  On calcule l'occlusion a partir de ce tampon.
//    S3  On la floute - elle est bruitee par construction - et on la
//        multiplie a l'ambiante du rendu final.
//
//  On ne peut pas se contenter du tampon de profondeur du rendu final :
//  l'occlusion doit etre connue AVANT d'eclairer, et l'anticrenelage MSAA
//  rend ce tampon penible a relire.
//  ---------------------------------------------------------------------
//
//  Technique publique (Crytek 2007, puis la variante hemispherique de
//  Filion & McNaughton). Implementation ecrite ici.
// ===========================================================================

using System;
using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Exarion.Engine.Graphics
{
    public sealed class Ssao : IDisposable
    {
        // -------------------------------------------------------------------
        //  S1 : normales et profondeur, en espace VUE
        // -------------------------------------------------------------------

        private const string GeometryVertex = @"#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 4) in vec4 aJoints;
layout (location = 5) in vec4 aWeights;

uniform mat4 uModelView;
uniform mat4 uNormalMatrix;
uniform mat4 uProjection;

const int MAX_BONES = 48;
uniform mat4 uBones[MAX_BONES];
uniform int  uSkinned;

out vec3  vViewNormal;
out float vViewDepth;

void main()
{
    vec3 position = aPos;
    vec3 normal = aNormal;

    if (uSkinned != 0)
    {
        mat4 skin = aWeights.x * uBones[int(aJoints.x)]
                  + aWeights.y * uBones[int(aJoints.y)]
                  + aWeights.z * uBones[int(aJoints.z)]
                  + aWeights.w * uBones[int(aJoints.w)];

        position = (skin * vec4(aPos, 1.0)).xyz;
        normal = mat3(skin) * aNormal;
    }

    vec4 viewPos = uModelView * vec4(position, 1.0);

    vViewNormal = mat3(uNormalMatrix) * normal;

    // La camera regarde vers -Z en espace vue : on stocke la distance
    // POSITIVE, plus simple a manipuler ensuite.
    vViewDepth = -viewPos.z;

    gl_Position = uProjection * viewPos;
}";

        private const string GeometryFragment = @"#version 330 core

in vec3  vViewNormal;
in float vViewDepth;

out vec4 oNormalDepth;

void main()
{
    // RVB = normale en espace vue, A = distance a la camera en metres.
    // Un tampon flottant est indispensable : la normale a des composantes
    // negatives, et la profondeur depasse largement 1.
    oNormalDepth = vec4(normalize(vViewNormal), vViewDepth);
}";

        // -------------------------------------------------------------------
        //  S2 : calcul de l'occlusion
        // -------------------------------------------------------------------

        private const string OcclusionFragment = @"#version 330 core

in vec2 vUv;
out float oOcclusion;

uniform sampler2D uNormalDepth;
uniform sampler2D uNoise;

uniform mat4  uProjection;
uniform vec2  uNoiseScale;     // ecran / taille du bruit
uniform float uTanHalfFovY;
uniform float uAspect;

const int MAX_SAMPLES = 32;
uniform vec3  uKernel[MAX_SAMPLES];
uniform int   uSampleCount;
uniform float uRadius;
uniform float uBias;
uniform float uStrength;

// Reconstruit la position en espace VUE a partir de l'ecran et de la
// distance. Moins couteux et plus precis que de stocker la position :
// trois flottants economises par pixel, et aucune perte.
vec3 viewPosition(vec2 uv, float depth)
{
    vec2 ndc = uv * 2.0 - 1.0;

    return vec3(ndc.x * uAspect * uTanHalfFovY * depth,
                ndc.y * uTanHalfFovY * depth,
                -depth);
}

void main()
{
    vec4 center = texture(uNormalDepth, vUv);
    float depth = center.a;

    // Profondeur nulle = le ciel. Il n'occulte rien et ne recoit rien.
    if (depth <= 0.0)
    {
        oOcclusion = 1.0;
        return;
    }

    vec3 origin = viewPosition(vUv, depth);
    vec3 normal = normalize(center.xyz);

    // ----------------------------------------------------------------
    // BASE TOURNEE AU HASARD, ET C'EST TOUT L'INTERET DU BRUIT.
    //
    // Avec un noyau fixe, tous les pixels echantillonnent les memes
    // directions : l'erreur devient un MOTIF regulier, parfaitement
    // visible. En tournant la base d'un angle different a chaque pixel,
    // la meme erreur devient du BRUIT - moche, mais qu'un flou efface.
    // On echange une structure contre du grain.
    // ----------------------------------------------------------------
    vec3 random = texture(uNoise, vUv * uNoiseScale).xyz;

    // Gram-Schmidt : on redresse le vecteur aleatoire pour qu'il soit
    // perpendiculaire a la normale.
    vec3 tangent = normalize(random - normal * dot(random, normal));
    vec3 bitangent = cross(normal, tangent);
    mat3 basis = mat3(tangent, bitangent, normal);

    float occluded = 0.0;
    int count = min(uSampleCount, MAX_SAMPLES);

    for (int i = 0; i < count; ++i)
    {
        vec3 samplePos = origin + basis * uKernel[i] * uRadius;

        // Projeter le point pour savoir OU le lire dans le tampon.
        vec4 clip = uProjection * vec4(samplePos, 1.0);
        vec3 ndc = clip.xyz / clip.w;
        vec2 uv = ndc.xy * 0.5 + 0.5;

        if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
            continue;

        float stored = texture(uNormalDepth, uv).a;
        if (stored <= 0.0)
            continue;   // ciel

        float sampleDepth = -samplePos.z;

        // La geometrie du tampon est-elle DEVANT le point teste ? Le biais
        // evite qu'une surface plane s'occulte elle-meme a cause de la
        // precision du tampon - le meme probleme que l'acne des ombres.
        if (stored < sampleDepth - uBias)
        {
            // Attenuation en distance : sans elle, un mur lointain aligne
            // derriere un objet proche l'assombrirait entierement. C'est
            // le halo classique de la SSAO naive.
            float range = uRadius / max(abs(depth - stored), 0.0001);
            occluded += clamp(range, 0.0, 1.0);
        }
    }

    float ao = 1.0 - (occluded / float(count)) * uStrength;
    oOcclusion = clamp(ao, 0.0, 1.0);
}";

        // -------------------------------------------------------------------
        //  S3 : le flou qui annule le bruit
        // -------------------------------------------------------------------

        private const string BlurFragment = @"#version 330 core

in vec2 vUv;
out float oOcclusion;

uniform sampler2D uOcclusion;
uniform vec2 uTexelSize;

void main()
{
    // ----------------------------------------------------------------
    // UN FLOU DE LA TAILLE EXACTE DE LA MOSAIQUE DE BRUIT.
    //
    // Ce n'est pas un adoucissement approximatif : chaque pixel de la
    // mosaique 4x4 a tourne la base d'un angle different, donc moyenner
    // une fenetre de 4x4 fait la moyenne des QUATRE ORIENTATIONS. Le
    // grain s'annule exactement, et il ne reste que le signal.
    //
    // Un flou plus large baverait sur les silhouettes ; un plus etroit
    // laisserait un motif residuel.
    // ----------------------------------------------------------------
    float sum = 0.0;

    for (int x = -2; x < 2; ++x)
    {
        for (int y = -2; y < 2; ++y)
        {
            vec2 offset = vec2(float(x), float(y)) * uTexelSize;
            sum += texture(uOcclusion, vUv + offset).r;
        }
    }

    oOcclusion = sum / 16.0;
}";

        // -------------------------------------------------------------------
        //  Visualisation, pour verifier le tampon a l'oeil
        // -------------------------------------------------------------------

        private const string BlitVertex = @"#version 330 core

// Un seul triangle qui couvre l'ecran, genere depuis gl_VertexID. Aucun
// tampon de sommets a allouer - la meme astuce que le ciel.
out vec2 vUv;

void main()
{
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    vUv = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}";

        private const string BlitFragment = @"#version 330 core

in vec2 vUv;
out vec4 oColor;

uniform sampler2D uSource;
uniform sampler2D uOcclusion;
uniform int   uMode;      // 1 = normales, 2 = profondeur, 3 = occlusion
uniform float uRange;     // distance affichee en blanc, en metres

void main()
{
    vec4 s = texture(uSource, vUv);

    if (uMode == 3)
    {
        oColor = vec4(vec3(texture(uOcclusion, vUv).r), 1.0);
        return;
    }

    if (uMode == 2)
    {
        // Normaliser sur le PLAN LOINTAIN (500 m) rendait l'image
        // entierement blanche : une scene d'interieur tient dans 30 m, soit
        // 6 % de l'echelle. On affiche donc sur une plage utile.
        oColor = vec4(vec3(clamp(s.a / max(uRange, 0.1), 0.0, 1.0)), 1.0);
        return;
    }

    // Normales : de [-1,1] vers [0,1] pour les rendre visibles.
    oColor = vec4(s.xyz * 0.5 + 0.5, 1.0);
}";

        private readonly GL _gl;
        private readonly ShaderProgram _geometryShader;
        private readonly ShaderProgram _occlusionShader;
        private readonly ShaderProgram _blurShader;
        private readonly ShaderProgram _blitShader;
        private readonly uint _emptyVao;

        private uint _framebuffer;
        private uint _normalDepth;
        private uint _depthBuffer;

        private uint _occlusionFbo;
        private uint _occlusionTexture;
        private uint _blurFbo;
        private uint _blurTexture;
        private uint _noiseTexture;

        private readonly Vector3[] _kernel;

        /// <summary>Cote de la mosaique de bruit. 4x4 suffit et tient en cache.</summary>
        private const int NoiseSize = 4;

        /// <summary>Doit suivre MAX_SAMPLES du shader.</summary>
        public const int MaxSamples = 32;

        /// <summary>
        /// Rayon d'echantillonnage, en metres. C'est LE reglage qui compte :
        /// il fixe l'echelle des details assombris.
        ///
        /// Trop petit, on ne voit rien - mesure : a 0,45 m l'effet etait a
        /// peine perceptible dans une scene ou les piliers font 1 m. Trop
        /// grand, des halos apparaissent autour des silhouettes.
        /// </summary>
        public float Radius { get; set; } = 0.8f;

        /// <summary>Marge contre l'auto-occlusion d'une surface plane.</summary>
        public float Bias { get; set; } = 0.03f;

        /// <summary>Intensite. 0 = aucun effet. Reglage de RESSENTI.</summary>
        public float Strength { get; set; } = 1.3f;

        public int SampleCount { get; set; } = 24;

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>L'occlusion FLOUTEE : celle que le rendu final consomme.</summary>
        public uint OcclusionTexture => _blurTexture;

        /// <summary>Doit suivre MAX_BONES des shaders ci-dessus.</summary>
        public const int MaxBones = 48;

        /// <summary>Texture RVBA16F : normale de vue en RVB, distance en A.</summary>
        public uint NormalDepthTexture => _normalDepth;

        public Ssao(GL gl, int width, int height)
        {
            _gl = gl;
            _geometryShader = new ShaderProgram(gl, GeometryVertex, GeometryFragment);
            _occlusionShader = new ShaderProgram(gl, BlitVertex, OcclusionFragment);
            _blurShader = new ShaderProgram(gl, BlitVertex, BlurFragment);
            _blitShader = new ShaderProgram(gl, BlitVertex, BlitFragment);

            // OpenGL core exige un VAO lie, meme sans attribut.
            _emptyVao = _gl.GenVertexArray();

            _kernel = BuildKernel(MaxSamples);
            BuildNoise();

            Resize(width, height);
        }

        /// <summary>
        /// Noyau d'echantillonnage : des points dans une DEMI-sphere, celle
        /// qui regarde dans le sens de la normale.
        ///
        /// Une sphere entiere serait un contresens : la moitie des points
        /// tomberait DERRIERE la surface, toujours occultee, et tout serait
        /// assombri de moitie uniformement.
        ///
        /// Les points sont resserres vers le CENTRE. Ce qui occulte
        /// vraiment un point, c'est ce qui le touche de pres ; repartir
        /// uniformement gaspillerait la moitie des echantillons au loin, ou
        /// l'attenuation les annule de toute facon.
        /// </summary>
        private static Vector3[] BuildKernel(int count)
        {
            // Graine fixe : deux lancements doivent donner la meme image, et
            // une comparaison de captures n'a aucun sens sinon.
            var rng = new Random(20260816);
            var kernel = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                var v = new Vector3(
                    (float)rng.NextDouble() * 2f - 1f,
                    (float)rng.NextDouble() * 2f - 1f,
                    (float)rng.NextDouble());          // z >= 0 : la demi-sphere

                if (v.LengthSquared() < 1e-6f)
                    v = Vector3.UnitZ;

                v = Vector3.Normalize(v) * (float)rng.NextDouble();

                // Interpolation quadratique : dense pres du centre, clairseme
                // au bord.
                float t = i / (float)count;
                kernel[i] = v * (0.1f + 0.9f * t * t);
            }

            return kernel;
        }

        /// <summary>
        /// Petite mosaique de vecteurs aleatoires, repetee sur tout l'ecran.
        /// Elle sert a tourner la base d'echantillonnage differemment a
        /// chaque pixel - voir le commentaire du shader.
        /// </summary>
        private unsafe void BuildNoise()
        {
            var rng = new Random(4242);
            var pixels = new float[NoiseSize * NoiseSize * 3];

            for (int i = 0; i < NoiseSize * NoiseSize; i++)
            {
                float angle = (float)rng.NextDouble() * MathF.Tau;

                // Z a zero : la rotation doit se faire DANS le plan tangent,
                // sinon la base penche et les echantillons sortent de la
                // demi-sphere.
                pixels[i * 3 + 0] = MathF.Cos(angle);
                pixels[i * 3 + 1] = MathF.Sin(angle);
                pixels[i * 3 + 2] = 0f;
            }

            _noiseTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _noiseTexture);

            fixed (float* p = pixels)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb16f,
                    NoiseSize, NoiseSize, 0, PixelFormat.Rgb, PixelType.Float, p);
            }

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            // REPEAT : c'est le principe meme, la mosaique pave l'ecran.
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        }

        public unsafe void Resize(int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            if (width == Width && height == Height && _framebuffer != 0)
                return;

            Release();

            Width = width;
            Height = height;

            _normalDepth = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _normalDepth);

            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f,
                (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.Float, null);

            // Pas de filtrage : on lit des normales et des distances, pas des
            // couleurs. Interpoler deux normales opposees donnerait n'importe
            // quoi sur les silhouettes.
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            // Tampon de profondeur : sans lui, les faces arriere ecraseraient
            // les faces avant selon leur ordre de dessin.
            _depthBuffer = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthBuffer);
            _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24,
                (uint)width, (uint)height);

            _framebuffer = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, _normalDepth, 0);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                RenderbufferTarget.Renderbuffer, _depthBuffer);

            var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete)
                throw new InvalidOperationException($"Tampon SSAO incomplet : {status}");

            // --- Tampon d'occlusion : un seul canal suffit ---
            _occlusionTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _occlusionTexture);

            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8,
                (uint)width, (uint)height, 0, PixelFormat.Red, PixelType.UnsignedByte, null);

            // Filtrage LINEAIRE ici, contrairement au tampon de normales :
            // c'est une valeur continue, et l'interpoler a du sens.
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            _occlusionFbo = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _occlusionFbo);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, _occlusionTexture, 0);

            status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete)
                throw new InvalidOperationException($"Tampon d'occlusion incomplet : {status}");

            // --- Tampon du flou. Deux textures sont indispensables : on ne
            // peut pas lire et ecrire la meme au meme moment.
            _blurTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _blurTexture);

            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8,
                (uint)width, (uint)height, 0, PixelFormat.Red, PixelType.UnsignedByte, null);

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            _blurFbo = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _blurFbo);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, _blurTexture, 0);

            status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete)
                throw new InvalidOperationException($"Tampon de flou incomplet : {status}");

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        /// <summary>
        /// Calcule l'occlusion a partir du tampon de S1. A appeler apres
        /// EndGeometry, avant le rendu final.
        /// </summary>
        public void ComputeOcclusion(Camera camera, Vector2D<int> screenSize)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _occlusionFbo);
            _gl.Viewport(0, 0, (uint)Width, (uint)Height);

            // Pas de test de profondeur : c'est un traitement d'image.
            _gl.Disable(EnableCap.DepthTest);

            _occlusionShader.Use();

            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _normalDepth);
            _occlusionShader.SetUniform("uNormalDepth", 0);

            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, _noiseTexture);
            _occlusionShader.SetUniform("uNoise", 1);

            _occlusionShader.SetUniform("uProjection", camera.GetProjectionMatrix());
            _occlusionShader.SetUniform("uNoiseScale",
                new Vector2(Width / (float)NoiseSize, Height / (float)NoiseSize));

            float fovY = camera.VerticalFovDegrees * MathF.PI / 180f;
            _occlusionShader.SetUniform("uTanHalfFovY", MathF.Tan(fovY * 0.5f));
            _occlusionShader.SetUniform("uAspect", camera.AspectRatio);

            int count = Math.Clamp(SampleCount, 1, MaxSamples);
            for (int i = 0; i < count; i++)
                _occlusionShader.SetUniform($"uKernel[{i}]", _kernel[i]);

            _occlusionShader.SetUniform("uSampleCount", count);
            _occlusionShader.SetUniform("uRadius", Radius);
            _occlusionShader.SetUniform("uBias", Bias);
            _occlusionShader.SetUniform("uStrength", Strength);

            _gl.BindVertexArray(_emptyVao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

            // --- Flou, dans la foulee : meme triangle, autre cible ---
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _blurFbo);

            _blurShader.Use();
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _occlusionTexture);
            _blurShader.SetUniform("uOcclusion", 0);
            _blurShader.SetUniform("uTexelSize", new Vector2(1f / Width, 1f / Height));

            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            _gl.BindVertexArray(0);

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Viewport(0, 0, (uint)Math.Max(1, screenSize.X), (uint)Math.Max(1, screenSize.Y));
            _gl.Enable(EnableCap.DepthTest);

            // L'unite 0 est celle de la carte d'ombre au rendu final : on la
            // laisse propre pour ne pas dependre de l'ordre des passes.
            _gl.ActiveTexture(TextureUnit.Texture0);
        }

        /// <summary>Ouvre la passe de profondeur et normales.</summary>
        public void BeginGeometry(Camera camera)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            _gl.Viewport(0, 0, (uint)Width, (uint)Height);

            // Profondeur ZERO pour le fond : c'est ainsi que la passe
            // suivante reconnaitra le ciel, et le laissera tranquille.
            _gl.ClearColor(0f, 0f, 0f, 0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            _gl.Enable(EnableCap.DepthTest);

            _geometryShader.Use();
            _geometryShader.SetUniform("uProjection", camera.GetProjectionMatrix());

            _view = camera.GetViewMatrix();
        }

        private Matrix4x4 _view = Matrix4x4.Identity;

        public void Draw(Mesh mesh, in Matrix4x4 model)
        {
            Matrix4x4 modelView = model * _view;

            _geometryShader.SetUniform("uModelView", modelView);
            _geometryShader.SetUniform("uNormalMatrix",
                Matrix4x4.Invert(modelView, out Matrix4x4 inverse) ? Matrix4x4.Transpose(inverse) : modelView);

            mesh.Draw();
        }

        public void SetBones(ReadOnlySpan<Matrix4x4> bones)
        {
            if (bones.Length == 0)
            {
                _geometryShader.SetUniform("uSkinned", 0);
                return;
            }

            int count = Math.Min(bones.Length, MaxBones);

            for (int i = 0; i < count; i++)
                _geometryShader.SetUniform($"uBones[{i}]", bones[i]);

            _geometryShader.SetUniform("uSkinned", 1);
        }

        public void ClearBones() => _geometryShader.SetUniform("uSkinned", 0);

        /// <summary>Referme la passe et restaure l'ecran.</summary>
        public void EndGeometry(Vector2D<int> screenSize)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Viewport(0, 0, (uint)Math.Max(1, screenSize.X), (uint)Math.Max(1, screenSize.Y));
        }

        /// <summary>
        /// Affiche le tampon plein ecran. Sert a verifier a l'oeil ce qu'on
        /// vient de calculer - une normale de travers ne se voit pas
        /// autrement qu'en la regardant.
        /// </summary>
        public void DebugBlit(int mode, float range)
        {
            _gl.Disable(EnableCap.DepthTest);

            _blitShader.Use();

            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _normalDepth);
            _blitShader.SetUniform("uSource", 0);

            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.BindTexture(TextureTarget.Texture2D, _occlusionTexture);
            _blitShader.SetUniform("uOcclusion", 1);

            _blitShader.SetUniform("uMode", mode);
            _blitShader.SetUniform("uRange", range);

            _gl.BindVertexArray(_emptyVao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            _gl.BindVertexArray(0);

            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.Enable(EnableCap.DepthTest);
        }

        private void Release()
        {
            if (_framebuffer != 0) _gl.DeleteFramebuffer(_framebuffer);
            if (_normalDepth != 0) _gl.DeleteTexture(_normalDepth);
            if (_depthBuffer != 0) _gl.DeleteRenderbuffer(_depthBuffer);
            if (_occlusionFbo != 0) _gl.DeleteFramebuffer(_occlusionFbo);
            if (_blurFbo != 0) _gl.DeleteFramebuffer(_blurFbo);
            if (_blurTexture != 0) _gl.DeleteTexture(_blurTexture);
            if (_occlusionTexture != 0) _gl.DeleteTexture(_occlusionTexture);

            _framebuffer = 0;
            _normalDepth = 0;
            _depthBuffer = 0;
            _occlusionFbo = 0;
            _blurFbo = 0;
            _blurTexture = 0;
            _occlusionTexture = 0;
        }

        public void Dispose()
        {
            Release();

            if (_noiseTexture != 0) _gl.DeleteTexture(_noiseTexture);

            _gl.DeleteVertexArray(_emptyVao);
            _geometryShader.Dispose();
            _occlusionShader.Dispose();
            _blurShader.Dispose();
            _blitShader.Dispose();
        }
    }
}
