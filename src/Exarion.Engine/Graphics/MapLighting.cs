// ===========================================================================
//  Exarion - Lumieres d'une carte vers le rendu
//
//  Decouvert par le JEU D'EXEMPLE : il devait recopier le switch a trois cas
//  qui existait deja dans le bac a sable. Une traduction que TOUS les jeux
//  auraient reecrite, et qui n'a rien de specifique a aucun d'eux.
//
//  ⚠️ Pourquoi une classe a part et pas une methode de SceneRenderer : le
//  rendu n'a pas a connaitre le format de carte. Il sait afficher des Light ;
//  savoir d'ou elles viennent est le travail de quelqu'un d'autre. Une
//  methode d'extension donne la meme facilite d'ecriture au point d'appel
//  (scene.LoadLights(map)) sans mettre MapData dans SceneRenderer.
// ===========================================================================

using System;
using Exarion.Core.Assets;

namespace Exarion.Engine.Graphics
{
    public static class MapLighting
    {
        /// <summary>
        /// Remplace les lumieres du rendu par celles de la carte.
        ///
        /// ⚠️ L'ORDRE DE LA CARTE COMPTE. SceneRenderer choisit la PREMIERE
        /// lumiere directionnelle comme source d'ombre - c'est elle qui portera
        /// le shadow mapping. Deux soleils dans une carte : le second n'aura
        /// pas d'ombre, et ca ne se voit pas tout de suite.
        /// </summary>
        /// <param name="sunScale">
        /// Facteur sur les directionnelles. Sert aux reglages en direct : le bac
        /// a sable expose light_sun, un jeu fini vaut 1.
        /// </param>
        /// <param name="lampScale">Idem pour les ponctuelles et les spots.</param>
        /// <returns>
        /// Nombre de lumieres REELLEMENT chargees. Peut etre inferieur au compte
        /// de la carte : SceneRenderer plafonne, et une carte qui depasse perd
        /// silencieusement ses dernieres lumieres si personne ne lit ce retour.
        /// </returns>
        public static int LoadLights(this SceneRenderer scene, MapData map,
                                     float sunScale = 1f, float lampScale = 1f)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(map);

            scene.ClearLights();

            int loaded = 0;

            foreach (MapLight light in map.Lights)
            {
                bool ok = light.Kind switch
                {
                    MapLightKind.Point => scene.AddLight(Light.Point(
                        light.Position, light.Color, light.Intensity * lampScale, light.Range)),

                    MapLightKind.Spot => scene.AddLight(Light.Spot(
                        light.Position, light.Direction, light.Color,
                        light.Intensity * lampScale, light.Range,
                        light.InnerDegrees, light.OuterDegrees)),

                    _ => scene.AddLight(Light.Directional(
                        light.Direction, light.Color, light.Intensity * sunScale)),
                };

                if (ok)
                    loaded++;
            }

            return loaded;
        }
    }
}
