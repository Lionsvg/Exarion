// ===========================================================================
//  Console : le rendu - halo, occlusion, lumieres, ciel, materiau, ombres
//
//  Tout est reglable : ces defauts se corrigent a l'oeil, et recompiler a
//  chaque essai rendrait le reglage impraticable.
// ===========================================================================

using System;
using System.Numerics;
using Exarion.Core.Console;
using Exarion.Engine.Graphics;
using Exarion.Game.Rendering;
using Exarion.Game.Ui;
using Texture = Exarion.Engine.Graphics.Texture;

namespace Exarion.Game.Commands
{
    internal static class RenderCommands
    {
        /// <param name="render">Le pipeline : il existe quand la console est construite.</param>
        /// <param name="gridTexture">La texture de developpement, pour « tex_enabled ».</param>
        /// <param name="shadowStrength">--shadows : valeur de depart ET defaut de « shadow_strength ».</param>
        public static void Register(ConsoleSystem c, RenderSettings settings, SceneLighting lighting,
                                    RenderPipeline render, Texture? gridTexture, float shadowStrength,
                                    Screenshots screenshots)
        {
            SceneRenderer scene = render.Scene;

            // --- Bloom (1.8) ---
            c.Bind("bloom", 1f, 0f, 1f, "Halo autour des sources vives",
                   x => settings.Bloom = x >= 0.5f);
            c.Bind("bloom_threshold", 1f, 0.05f, 6f, "Luminance a partir de laquelle ca deborde",
                   x => render.Bloom.Threshold = x);
            c.Bind("bloom_knee", 0.5f, 0.01f, 2f, "Douceur du seuil, contre le clignotement",
                   x => render.Bloom.Knee = x);
            c.Bind("bloom_intensity", 1f, 0f, 4f, "Force du halo ajoute",
                   x => render.Hdr.BloomIntensity = x);
            c.Bind("bloom_passes", 5f, 1f, 12f, "Largeur du halo",
                   x => render.Bloom.Passes = (int)x);

            // --- Occlusion ambiante (S1 -> S3) ---
            c.Bind("ssao", 1f, 0f, 1f, "Occlusion ambiante : la comparaison qui compte",
                   x => settings.Ssao = x >= 0.5f);
            c.Bind("ssao_debug", 0f, 0f, 3f,
                   "Voir le tampon SSAO : 0 rien, 1 normales, 2 profondeur, 3 occlusion",
                   x => settings.SsaoDebug = (int)x);
            c.Bind("ssao_debug_range", 30f, 1f, 200f,
                   "Distance affichee en blanc dans la vue de profondeur",
                   x => settings.SsaoDebugRange = x);
            // Les valeurs initiales doivent COINCIDER avec celles de la classe
            // Ssao : le setter n'est appele qu'au changement, et une console
            // qui affiche autre chose que la realite est un piege.
            c.Bind("ssao_radius", 0.8f, 0.02f, 4f, "Rayon d'echantillonnage SSAO, en metres",
                   x => render.Ssao.Radius = x);
            c.Bind("ssao_bias", 0.03f, 0f, 0.5f, "Marge contre l'auto-occlusion",
                   x => render.Ssao.Bias = x);
            c.Bind("ssao_strength", 1.3f, 0f, 3f, "Intensite de l'occlusion",
                   x => render.Ssao.Strength = x);
            c.Bind("ssao_samples", 24f, 1f, 32f, "Echantillons par pixel",
                   x => render.Ssao.SampleCount = (int)x);

            // --- Eclairage ---
            c.Bind("light_enabled", 1f, 0f, 1f, "Allumer les lumieres",
                   x => { lighting.Enabled = x >= 0.5f; lighting.Dirty = true; });
            c.Bind("light_sun", lighting.SunIntensity, 0f, 10f, "Intensite de la lumiere generale",
                   x => { lighting.SunIntensity = x; lighting.Dirty = true; });
            c.Bind("light_lamps", lighting.LampIntensity, 0f, 200f, "Intensite des lampes",
                   x => { lighting.LampIntensity = x; lighting.Dirty = true; });
            c.Bind("light_ambient", 0.30f, 0f, 1f, "Ambiante globale (ciel + sol)",
                   x => scene.Ambient = new Vector3(x));
            c.Bind("sky_sun_size", 24f, 1f, 400f, "Concentration du halo solaire",
                   x => render.Sky.SunSize = x);
            c.Bind("sky_brightness", 1f, 0f, 4f, "Luminosite du ciel",
                   x =>
                   {
                       render.Sky.Zenith = new Vector3(0.045f, 0.075f, 0.16f) * x;
                       render.Sky.Horizon = new Vector3(0.22f, 0.26f, 0.34f) * x;
                       render.Sky.SunColor = new Vector3(1.0f, 0.85f, 0.62f) * x;
                   });

            // --- Materiau (PBR metallic-roughness) ---
            c.Bind("mat_roughness", 0.75f, 0.02f, 1f,
                   "Rugosite : 0 = miroir, 1 = mat",
                   x => scene.DefaultMaterial.Roughness = x);
            c.Bind("mat_metallic", 0f, 0f, 1f,
                   "Metallicite : 0 = beton/plastique, 1 = metal",
                   x => scene.DefaultMaterial.Metallic = x);
            c.Bind("exposure", 1f, 0.05f, 8f, "Exposition globale",
                   x => scene.Exposure = x);

            // --- Ombres ---
            c.Bind("shadow_strength", shadowStrength, 0f, 1f, "Intensite des ombres. 0 = desactivees",
                   x => scene.ShadowStrength = x);
            scene.ShadowStrength = shadowStrength;
            c.Bind("shadow_bias", 0.0025f, 0.0f, 0.05f,
                   "Decalage anti-acne. Trop grand = ombres decollees de l'objet",
                   x => scene.ShadowBias = x);
            c.Bind("shadow_softness", 1f, 0f, 4f, "Rayon du filtre PCF en texels. 1 = 3x3, 2 = 5x5",
                   x => scene.ShadowSoftness = (int)x);
            c.Bind("shadow_extent", 45f, 5f, 200f, "Demi-cote de la zone couverte, en metres",
                   x => render.Shadows.Extent = x);
            c.Bind("shadow_res", 2048f, 256f, 8192f, "Resolution de la carte d'ombre",
                   x => render.Shadows.Resize((uint)x));
            c.Bind("shadow_cullfront", 1f, 0f, 1f,
                   "Ne rendre que les faces arriere dans la passe d'ombre (reduit l'acne)",
                   x => render.Shadows.CullFrontFaces = x >= 0.5f);

            // --- Textures ---
            c.Bind("tex_enabled", 1f, 0f, 1f, "Appliquer la texture de developpement",
                   x => scene.Albedo = x >= 0.5f ? gridTexture : null);
            c.Bind("tex_scale", 8f, 0.5f, 64f, "Metres par repetition de texture",
                   x => scene.UvPerMeter = 1f / x);

            // --- Affichage ---
            c.Bind("wireframe", 0f, 0f, 1f, "Vue des collisions en fil de fer", x => settings.Wireframe = x >= 0.5f);
            c.Bind("showstats", 1f, 0f, 1f, "Compteurs a l'ecran", x => settings.ShowStats = x >= 0.5f);

            c.Register("screenshot", args =>
            {
                screenshots.Request(args.Length > 0 ? args[0] : "capture.png");
                c.Print("  capture demandee...");
            }, "screenshot [fichier] - enregistre l'image courante en PNG");
        }
    }
}
