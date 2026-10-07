// ===========================================================================
//  Exarion - Rendu de scene eclairee
//
//  Pipeline avant (forward) : chaque surface evalue toutes les lumieres.
//  Simple, suffisant tant qu'on reste sous quelques dizaines de sources.
//  Un pipeline differe ne se justifiera qu'avec des centaines de lumieres.
//
//  DEUX POINTS QUI CHANGENT TOUT VISUELLEMENT :
//
//  1. ESPACE LINEAIRE. Les couleurs qu'on ecrit ("gris 0.5") sont en sRGB,
//     un espace percptuel non lineaire. Additionner des lumieres dans cet
//     espace donne un rendu delave et des ombres grises. On convertit donc
//     en lineaire avant d'eclairer, et on revient en sRGB a la fin.
//
//  2. ATTENUATION FENETREE. 1/d^2 ne s'annule jamais : chaque lumiere
//     devrait etre evaluee partout. On multiplie par une fenetre qui
//     s'annule proprement a la portee.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;

namespace Exarion.Engine.Graphics
{
    public sealed class SceneRenderer : IDisposable
    {
        public const int MaxLights = 32;

        private const string VertexSource = @"#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec3 aColor;
layout (location = 3) in vec2 aUv;
layout (location = 4) in vec4 aJoints;
layout (location = 5) in vec4 aWeights;

// 48 os : de quoi tenir un personnage humain avec des doigts. La limite
// n'est pas arbitraire - OpenGL 3.3 ne garantit que 1024 composantes de
// uniform dans le shader de sommets, et 48 matrices en mangent 768.
const int MAX_BONES = 48;

uniform mat4 uBones[MAX_BONES];
uniform int  uSkinned;

uniform mat4 uModel;
uniform mat4 uNormalMatrix;
uniform mat4 uViewProj;
uniform vec3 uTint;

out vec3 vWorldPos;
out vec3 vNormal;
out vec3 vColor;
out vec2 vUv;

// ---------------------------------------------------------------------
// LE SKINNING TIENT EN UNE LIGNE, ET C'EST TOUT L'INTERET DE LA METHODE.
//
// Chaque os propose une deformation du sommet ; on en fait la MOYENNE
// PONDEREE par les poids. Un sommet au milieu du coude est tire a moitie
// par le bras et a moitie par l'avant-bras, et se place naturellement
// entre les deux.
//
// On melange les MATRICES, pas les positions : c'est equivalent ici, et
// bien moins couteux - une seule transformation au lieu de quatre.
// ---------------------------------------------------------------------
mat4 skinMatrix()
{
    return aWeights.x * uBones[int(aJoints.x)]
         + aWeights.y * uBones[int(aJoints.y)]
         + aWeights.z * uBones[int(aJoints.z)]
         + aWeights.w * uBones[int(aJoints.w)];
}

void main()
{
    vec3 position = aPos;
    vec3 normal = aNormal;

    if (uSkinned != 0)
    {
        mat4 skin = skinMatrix();

        position = (skin * vec4(aPos, 1.0)).xyz;

        // La normale subit la meme deformation, mais sans la translation :
        // une direction n'a pas de position. D'ou mat3.
        normal = mat3(skin) * aNormal;
    }

    vec4 world = uModel * vec4(position, 1.0);
    vWorldPos = world.xyz;

    // La transposee de l'inverse, calculee cote CPU.
    //
    // Utiliser uModel directement ne marche que pour une rotation et une
    // mise a l'echelle UNIFORME : une echelle inegale penche les normales
    // du mauvais cote, et l'eclairage devient faux sans que rien ne
    // paraisse casse. Voir SceneRenderer.Draw.
    vNormal = mat3(uNormalMatrix) * normal;

    vColor = aColor * uTint;
    vUv = aUv;
    gl_Position = uViewProj * world;
}";

        private const string FragmentSource = @"#version 330 core

#define MAX_LIGHTS 32

struct Light
{
    int   type;          // 0 directionnelle, 1 ponctuelle, 2 projecteur
    float intensity;
    float range;
    float innerCos;
    float outerCos;
    vec3  position;
    vec3  direction;
    vec3  color;
};

uniform Light uLights[MAX_LIGHTS];
uniform int   uLightCount;
// Ambiante HEMISPHERIQUE : ce qui regarde vers le haut recoit la couleur du
// ciel, ce qui regarde vers le bas celle du sol. Une ambiante uniforme aplatit
// tout ; celle-ci suffit a donner l'impression d'un objet POSE dans un lieu.
uniform vec3  uAmbientSky;
uniform vec3  uAmbientGround;

// --- Occlusion ambiante ---
//
// Elle ne s'applique QU'A L'AMBIANTE, jamais aux lumieres. Une lampe qui
// eclaire un recoin doit l'eclairer : l'occlusion decrit ce que le CIEL ne
// peut pas atteindre, pas ce qui est dans le noir. L'appliquer partout
// donne l'aspect terne et sale caracteristique d'une SSAO mal branchee.
uniform sampler2D uAo;
uniform int  uAoEnabled;
uniform vec2 uScreenSize;
uniform vec3  uCameraPos;
uniform float uExposure;

// --- Materiau ---
uniform float uRoughness;
uniform float uMetallic;

const float PI = 3.14159265359;

// --- Texture ---
uniform sampler2D uAlbedo;
uniform int   uUseTexture;
uniform float uUvPerMeter;

// Deux facons de plaquer une texture, et il FAUT les deux.
//
//   0 - projection planaire depuis le monde. Le decor est fait de boites
//       mises a l'echelle : leurs UV de sommets seraient etirees, et un mur
//       de 20 m n'aurait pas les memes carreaux qu'une caisse de 1 m.
//   1 - UV du maillage. Un personnage a une texture DEPLIEE, ou chaque
//       triangle vise un endroit precis de l'image. La projeter depuis le
//       monde barbouillerait le visage avec le carrelage du sol.
uniform int   uUseMeshUv;

// --- Ombres ---
uniform sampler2D uShadowMap;
uniform mat4  uLightViewProj;
uniform int   uShadowLightIndex;   // -1 = aucune ombre
uniform float uShadowBias;
uniform float uShadowTexel;        // 1 / resolution
uniform int   uShadowSoftness;     // rayon du filtre PCF, en texels
uniform float uShadowStrength;     // 0 = pas d'ombre, 1 = ombre pleine

in vec3 vWorldPos;
in vec3 vNormal;
in vec2 vUv;
in vec3 vColor;

out vec4 FragColor;

// sRGB -> lineaire. Approximation en puissance 2.2, suffisante ici.
vec3 toLinear(vec3 c) { return pow(c, vec3(2.2)); }
vec3 toSrgb(vec3 c)   { return pow(c, vec3(1.0 / 2.2)); }

// ---------------------------------------------------------------------------
//  Modele PBR : Cook-Torrance avec distribution GGX.
//
//  La reflexion speculaire se decompose en trois termes :
//    D  quelle proportion des microfacettes est orientee pour reflechir
//       vers l'oeil. C'est lui qui donne sa forme au reflet.
//    G  quelle proportion n'est ni masquee ni ombree par ses voisines.
//       Compte surtout aux angles rasants et sur les surfaces rugueuses.
//    F  Fresnel : tout devient miroir quand on le regarde de biais.
//       C'est l'effet qui manque le plus a un Blinn-Phong.
// ---------------------------------------------------------------------------

// Distribution des normales de microfacettes (Trowbridge-Reitz / GGX).
float distributionGGX(float NdotH, float roughness)
{
    float a  = roughness * roughness;
    float a2 = a * a;
    float d  = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / max(PI * d * d, 1e-7);
}

// Masquage-ombrage, approximation de Schlick-GGX combinee par Smith.
float geometrySmith(float NdotV, float NdotL, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    float gv = NdotV / (NdotV * (1.0 - k) + k);
    float gl = NdotL / (NdotL * (1.0 - k) + k);
    return gv * gl;
}

// Fresnel, approximation de Schlick.
vec3 fresnelSchlick(vec3 f0, float cosTheta)
{
    return f0 + (1.0 - f0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// Retourne 1 = pleine lumiere, 0 = ombre totale.
float shadowFactor(vec3 N, vec3 L)
{
    vec4 lightSpace = uLightViewProj * vec4(vWorldPos, 1.0);
    vec3 proj = lightSpace.xyz / lightSpace.w;
    proj = proj * 0.5 + 0.5;              // [-1,1] -> [0,1]

    // Au-dela du volume couvert : pleine lumiere plutot qu'ombre.
    if (proj.z > 1.0 || proj.z < 0.0)
        return 1.0;

    // Biais proportionnel a l'inclinaison : une surface rasante souffre
    // bien plus de l'acne qu'une surface face a la lumiere.
    float slope = 1.0 - max(dot(N, L), 0.0);
    float bias  = max(uShadowBias * slope, uShadowBias * 0.08);

    // PCF : on moyenne les texels voisins pour adoucir les bords.
    float sum = 0.0;
    float count = 0.0;

    for (int x = -uShadowSoftness; x <= uShadowSoftness; ++x)
    {
        for (int y = -uShadowSoftness; y <= uShadowSoftness; ++y)
        {
            vec2 offset = vec2(float(x), float(y)) * uShadowTexel;
            float stored = texture(uShadowMap, proj.xy + offset).r;
            sum   += (proj.z - bias > stored) ? 0.0 : 1.0;
            count += 1.0;
        }
    }

    float lit = sum / max(count, 1.0);
    return mix(1.0, lit, uShadowStrength);
}

// Projection planaire depuis la position MONDE, selon l'axe dominant de la
// normale. Consequence : la densite de texels est identique partout, quelle
// que soit la taille de l'objet. Une caisse de 1 m et un mur de 20 m ont
// exactement les memes carreaux - c'est ce qui rend l'echelle lisible.
// Calcule ici plutot que par sommet : les caisses sont un cube unitaire mis
// a l'echelle, leurs UV de sommets seraient donc etirees.
vec2 worldUv(vec3 p, vec3 n)
{
    vec3 a = abs(n);

    if (a.y >= a.x && a.y >= a.z) return p.xz * uUvPerMeter;   // sol, plafond
    if (a.x >= a.z)               return p.zy * uUvPerMeter;   // murs en X
    return p.xy * uUvPerMeter;                                 // murs en Z
}

void main()
{
    vec3 N = normalize(vNormal);

    vec3 albedo = toLinear(vColor);
    if (uUseTexture != 0)
    {
        vec2 uv = uUseMeshUv != 0 ? vUv : worldUv(vWorldPos, N);
        albedo *= toLinear(texture(uAlbedo, uv).rgb);
    }
    vec3 V = normalize(uCameraPos - vWorldPos);

    float NdotV = max(dot(N, V), 1e-4);
    float roughness = clamp(uRoughness, 0.04, 1.0);   // 0 pur = reflet infiniment fin
    float metallic = clamp(uMetallic, 0.0, 1.0);

    // Reflectance a incidence normale : ~4 % pour un dielectrique, la
    // couleur meme du materiau pour un metal.
    vec3 f0 = mix(vec3(0.04), albedo, metallic);

    // Un metal ne diffuse pas : toute sa reponse est speculaire.
    vec3 diffuseColor = albedo * (1.0 - metallic);

    float hemisphere = N.y * 0.5 + 0.5;
    vec3  ambient = mix(toLinear(uAmbientGround), toLinear(uAmbientSky), hemisphere);

    // L'occlusion se lit en coordonnees ECRAN : elle a ete calculee dans
    // un tampon plein ecran, pas attachee a la geometrie.
    if (uAoEnabled != 0)
        ambient *= texture(uAo, gl_FragCoord.xy / uScreenSize).r;

    vec3 result = ambient * diffuseColor;

    for (int i = 0; i < uLightCount; ++i)
    {
        vec3  L;
        float attenuation = 1.0;

        if (uLights[i].type == 0)
        {
            L = -uLights[i].direction;
        }
        else
        {
            vec3  toLight = uLights[i].position - vWorldPos;
            float dist    = length(toLight);
            L = toLight / max(dist, 0.0001);

            // Inverse du carre, fenetre pour s'annuler a la portee.
            float ratio  = dist / max(uLights[i].range, 0.0001);
            float r2     = ratio * ratio;
            float window = clamp(1.0 - r2 * r2, 0.0, 1.0);
            attenuation  = (window * window) / (dist * dist + 1.0);

            if (uLights[i].type == 2)
            {
                float cosAngle = dot(-L, uLights[i].direction);
                float span     = max(uLights[i].innerCos - uLights[i].outerCos, 0.0001);
                float cone     = clamp((cosAngle - uLights[i].outerCos) / span, 0.0, 1.0);
                attenuation   *= cone * cone;
            }
        }

        float NdotL = max(dot(N, L), 0.0);
        if (NdotL <= 0.0 || attenuation <= 0.0)
            continue;

        // Seule la lumiere designee projette des ombres : une carte par
        // source coute cher, et une directionnelle suffit a lire le relief.
        if (i == uShadowLightIndex)
            attenuation *= shadowFactor(N, L);

        if (attenuation <= 0.0)
            continue;

        vec3 radiance = toLinear(uLights[i].color) * uLights[i].intensity * attenuation;

        vec3  H     = normalize(L + V);
        float NdotH = max(dot(N, H), 0.0);
        float VdotH = max(dot(V, H), 0.0);

        float D = distributionGGX(NdotH, roughness);
        float G = geometrySmith(NdotV, NdotL, roughness);
        vec3  F = fresnelSchlick(f0, VdotH);

        vec3 specular = (D * G * F) / max(4.0 * NdotV * NdotL, 1e-4);

        // Conservation de l'energie : ce qui est reflechi specularement
        // n'est plus disponible pour la diffusion.
        vec3 kd = (vec3(1.0) - F) * (1.0 - metallic);

        result += radiance * (kd * diffuseColor / PI + specular) * NdotL;
    }

    // ----------------------------------------------------------------
    // ON SORT EN LINEAIRE, SANS RIEN ECRETER.
    //
    // L'exposition, le tone mapping ACES et la conversion sRGB etaient ici.
    // Ils sont passes dans HdrTarget.Compose, et pour deux raisons :
    //
    //   - une lampe a 8 fois la luminosite du papier doit RESTER a 8. En
    //     ecretant ici, le bloom ne pourrait plus distinguer une source
    //     vive d'un mur clair ;
    //   - le ciel avait sa propre conversion et echappait a la courbe.
    //     Une seule passe finale, une seule courbe, pour toute l'image.
    // ----------------------------------------------------------------
    FragColor = vec4(result, 1.0);
}";

        private readonly GL _gl;
        private readonly ShaderProgram _shader;
        private readonly List<Light> _lights = new();

        /// <summary>Ambiante recue par les surfaces tournees vers le haut, en sRGB.</summary>
        public Vector3 AmbientSky = new(0.30f, 0.36f, 0.46f);

        /// <summary>Ambiante recue par les surfaces tournees vers le bas, en sRGB.</summary>
        public Vector3 AmbientGround = new(0.16f, 0.15f, 0.14f);

        /// <summary>Regle les deux d'un coup, en gardant leur teinte relative.</summary>
        public Vector3 Ambient
        {
            get => (AmbientSky + AmbientGround) * 0.5f;
            set
            {
                float level = (value.X + value.Y + value.Z) / 3f;
                AmbientSky = new Vector3(0.86f, 1.03f, 1.31f) * level;
                AmbientGround = new Vector3(0.92f, 0.86f, 0.80f) * level * 0.5f;
            }
        }

        public float Exposure = 1.4f;

        /// <summary>Materiau applique aux dessins qui n'en precisent pas.</summary>
        public Material DefaultMaterial = Material.Default;

        // --- Texture ---
        /// <summary>Texture d'albedo appliquee a tout. null = couleurs unies.</summary>
        public Texture? Albedo { get; set; }

        /// <summary>Repetitions de texture par metre. 1/8 = une case par metre.</summary>
        public float UvPerMeter = 1f / 8f;

        // --- Ombres ---
        /// <summary>Carte d'ombre a utiliser, ou null.</summary>
        public ShadowMap? Shadows { get; set; }

        /// <summary>Decalage de profondeur contre l'acne. Trop grand = ombres decollees.</summary>
        public float ShadowBias = 0.0025f;

        /// <summary>Rayon du filtre PCF, en texels. 1 = 3x3, 2 = 5x5.</summary>
        public int ShadowSoftness = 1;

        /// <summary>0 = pas d'ombre, 1 = ombre pleine.</summary>
        public float ShadowStrength = 1f;

        public IReadOnlyList<Light> Lights => _lights;

        /// <summary>Index de la premiere lumiere directionnelle, ou -1.</summary>
        public int ShadowCastingLightIndex
        {
            get
            {
                for (int i = 0; i < _lights.Count; i++)
                    if (_lights[i].Type == LightType.Directional)
                        return i;

                return -1;
            }
        }

        public SceneRenderer(GL gl)
        {
            _gl = gl;
            _shader = new ShaderProgram(gl, VertexSource, FragmentSource);
        }

        public void ClearLights() => _lights.Clear();

        /// <summary>Ajoute une lumiere. Au-dela de MaxLights, elle est ignoree.</summary>
        public bool AddLight(in Light light)
        {
            if (_lights.Count >= MaxLights)
                return false;

            _lights.Add(light);
            return true;
        }

        /// <summary>Prepare le rendu : envoie camera, ambiante et lumieres.</summary>
        public void Begin(Camera camera)
        {
            _shader.Use();
            _shader.SetUniform("uViewProj", camera.GetViewProjection());
            _shader.SetUniform("uCameraPos", camera.Position);
            _shader.SetUniform("uAmbientSky", AmbientSky);
            _shader.SetUniform("uAmbientGround", AmbientGround);
            _shader.SetUniform("uExposure", Exposure);
            _shader.SetUniform("uLightCount", _lights.Count);

            for (int i = 0; i < _lights.Count; i++)
                UploadLight(i, _lights[i]);

            UploadTextureState();
            UploadShadowState();
            UploadAmbientOcclusion();
        }

        /// <summary>Texture d'occlusion ambiante, ou 0 pour la desactiver.</summary>
        public uint AmbientOcclusionTexture { get; set; }

        /// <summary>Taille du tampon d'ecran, pour lire l'occlusion au bon endroit.</summary>
        public Vector2 ScreenSize { get; set; } = new(1280f, 720f);

        private void UploadAmbientOcclusion()
        {
            _shader.SetUniform("uAoEnabled", AmbientOcclusionTexture != 0 ? 1 : 0);
            _shader.SetUniform("uScreenSize", ScreenSize);

            if (AmbientOcclusionTexture == 0)
                return;

            // Unite 2 : 0 est la carte d'ombre, 1 la texture d'albedo.
            _gl.ActiveTexture(TextureUnit.Texture2);
            _gl.BindTexture(TextureTarget.Texture2D, AmbientOcclusionTexture);
            _shader.SetUniform("uAo", 2);

            _gl.ActiveTexture(TextureUnit.Texture0);
        }

        private void UploadTextureState()
        {
            _shader.SetUniform("uUseTexture", Albedo != null ? 1 : 0);
            _shader.SetUniform("uUvPerMeter", UvPerMeter);

            if (Albedo == null)
                return;

            // Unite 1 : l'unite 0 est reservee a la carte d'ombre.
            Albedo.Bind(TextureUnit.Texture1);
            _shader.SetUniform("uAlbedo", 1);
        }

        private void UploadShadowState()
        {
            int index = Shadows != null && ShadowStrength > 0f ? ShadowCastingLightIndex : -1;

            _shader.SetUniform("uShadowLightIndex", index);

            if (index < 0 || Shadows == null)
                return;

            _shader.SetUniform("uLightViewProj", Shadows.LightViewProjection);
            _shader.SetUniform("uShadowBias", ShadowBias);
            _shader.SetUniform("uShadowTexel", 1f / Shadows.Resolution);
            _shader.SetUniform("uShadowSoftness", Math.Clamp(ShadowSoftness, 0, 4));
            _shader.SetUniform("uShadowStrength", Math.Clamp(ShadowStrength, 0f, 1f));

            // La carte de profondeur occupe l'unite de texture 0.
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, Shadows.DepthTexture);
            _shader.SetUniform("uShadowMap", 0);
        }

        private void UploadLight(int index, in Light l)
        {
            string p = $"uLights[{index}].";

            _shader.SetUniform(p + "type", (int)l.Type);
            _shader.SetUniform(p + "intensity", l.Intensity);
            _shader.SetUniform(p + "range", l.Range);
            _shader.SetUniform(p + "position", l.Position);
            _shader.SetUniform(p + "direction", l.Direction);
            _shader.SetUniform(p + "color", l.Color);

            // Les cosinus sont calcules ici plutot que dans le shader :
            // inutile de refaire un cosinus par pixel.
            float inner = MathF.Cos(l.InnerConeDegrees * MathF.PI / 180f);
            float outer = MathF.Cos(l.OuterConeDegrees * MathF.PI / 180f);

            _shader.SetUniform(p + "innerCos", inner);
            _shader.SetUniform(p + "outerCos", outer);
        }

        /// <summary>Dessine un maillage. model = identite pour de la geometrie deja en coordonnees monde.</summary>
        public void Draw(Mesh mesh, in Matrix4x4 model, Vector3 tint)
            => Draw(mesh, model, tint, DefaultMaterial);

        /// <summary>
        /// Matrice de transformation des NORMALES : la transposee de
        /// l'inverse de la matrice modele.
        ///
        /// Pourquoi elle ne peut pas etre la matrice modele : une mise a
        /// l'echelle inegale ecrase la geometrie sur un axe, et les normales
        /// doivent pencher dans le sens INVERSE pour rester perpendiculaires
        /// a la surface. Les appliquer la matrice modele les penche du bon
        /// cote pour la forme, et du mauvais pour la lumiere.
        ///
        /// ⚠️ Aucun effet sur une BOITE, quelle que soit son echelle : ses
        /// normales sont toutes des axes propres, et mettre (1,0,0) a
        /// l'echelle donne (sx,0,0), qui se renormalise en (1,0,0). Le
        /// defaut ne se verra donc qu'avec de vrais modeles - etape 1.5 -
        /// et il sera alors invisible a l'oeil nu : un eclairage un peu
        /// faux, pas une image cassee. D'ou la correction faite d'avance.
        /// </summary>
        private static Matrix4x4 NormalMatrix(in Matrix4x4 model)
        {
            // Une matrice non inversible veut dire une echelle nulle sur un
            // axe : la normale n'a plus de sens. On rend la matrice modele
            // plutot que du NaN.
            if (!Matrix4x4.Invert(model, out Matrix4x4 inverse))
                return model;

            return Matrix4x4.Transpose(inverse);
        }

        /// <summary>Nombre d'os que le shader peut recevoir. Doit suivre MAX_BONES.</summary>
        public const int MaxBones = 48;

        /// <summary>
        /// Envoie les matrices d'os pour le prochain dessin. Les passer a
        /// null revient a dessiner un maillage rigide.
        ///
        /// A appeler AVANT Draw : les uniforms restent jusqu'au prochain
        /// changement, et un maillage dessine avec les os d'un autre serait
        /// spectaculairement faux.
        /// </summary>
        public void SetBones(ReadOnlySpan<Matrix4x4> bones)
        {
            if (bones.Length == 0)
            {
                _shader.SetUniform("uSkinned", 0);
                return;
            }

            int count = Math.Min(bones.Length, MaxBones);

            for (int i = 0; i < count; i++)
                _shader.SetUniform($"uBones[{i}]", bones[i]);

            _shader.SetUniform("uSkinned", 1);
        }

        public void ClearBones() => _shader.SetUniform("uSkinned", 0);

        public void Draw(Mesh mesh, in Matrix4x4 model, Vector3 tint, in Material material)
        {
            _shader.SetUniform("uModel", model);
            _shader.SetUniform("uNormalMatrix", NormalMatrix(model));
            _shader.SetUniform("uTint", tint * material.Albedo);
            _shader.SetUniform("uRoughness", material.Roughness);
            _shader.SetUniform("uMetallic", material.Metallic);

            // La texture se decide PAR DESSIN, pas une fois pour la scene :
            // le decor et un personnage n'ont ni la meme image ni la meme
            // facon de la plaquer.
            Texture? map = material.Map ?? Albedo;

            _shader.SetUniform("uUseTexture", map != null ? 1 : 0);
            _shader.SetUniform("uUseMeshUv", material.UseMeshUv ? 1 : 0);

            if (map != null)
            {
                // Unite 1 : l'unite 0 est reservee a la carte d'ombre.
                map.Bind(TextureUnit.Texture1);
                _shader.SetUniform("uAlbedo", 1);
            }

            mesh.Draw();
        }

        public void Draw(Mesh mesh) => Draw(mesh, Matrix4x4.Identity, Vector3.One);

        public void Dispose() => _shader.Dispose();
    }
}
