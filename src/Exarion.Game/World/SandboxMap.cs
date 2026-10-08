// ===========================================================================
//  Le decor de reference, integre au moteur
//
//  Une grande salle, des plateformes a differentes hauteurs, un escalier, et
//  de quoi tester le franchissement de marches.
//
//  ⚠️ C'EST LE DECOR DE TOUTES LES MESURES. Sans --map, c'est lui qui sert,
//  jamais un fichier trouve sur le disque : un decor de reference qui change
//  tout seul rendrait les mesures incomparables d'une session a l'autre. Le
//  modifier, c'est modifier la base de toutes les comparaisons de CLAUDE.md.
// ===========================================================================

using System.Numerics;
using Exarion.Core.Assets;

namespace Exarion.Game.World
{
    internal static class SandboxMap
    {
        /// <summary>Source affichee pour ce decor : il ne vient d'aucun fichier.</summary>
        public const string Source = "(bac a sable, integre au moteur)";

        /// <summary>
        /// Construit le decor.
        ///
        /// Les intensites sont celles des reglages COURANTS : une carte
        /// reconstruite apres « light_sun 3 » est ecrite avec 3, exactement
        /// comme avant l'extraction de cette methode.
        /// </summary>
        public static MapData Build(Vector3 sunDirection, float sunIntensity, float lampIntensity)
        {
            const float half = 30f;
            const float ceiling = 9f;
            const float wall = 0.6f;

            var w = new MapData { Name = "bac a sable", Spawn = new Vector3(0f, 1f, 20f) };

            var floorColor = new Vector3(0.34f, 0.35f, 0.38f);
            var wallColor = new Vector3(0.50f, 0.51f, 0.54f);
            var platformColor = new Vector3(0.42f, 0.50f, 0.58f);
            var stepColor = new Vector3(0.55f, 0.48f, 0.38f);
            var pillarColor = new Vector3(0.60f, 0.40f, 0.38f);

            void Add(Vector3 center, Vector3 size, Vector3 color) => w.AddBox(center, size, color);

            // Coque
            Add(new Vector3(0f, -0.5f, 0f), new Vector3(half * 2f, 1f, half * 2f), floorColor);

            // Plafond sur la MOITIE NORD seulement. La moitie sud reste a
            // ciel ouvert : sans elle, la lumiere directionnelle est bloquee
            // et les ombres portees sont invisibles - on ne peut donc ni les
            // regler ni les verifier. Constate par capture d'ecran.
            Add(new Vector3(0f, ceiling + 0.5f, -half * 0.5f),
                new Vector3(half * 2f, 1f, half), wallColor);

            float mid = ceiling * 0.5f;
            Add(new Vector3(-half - wall * 0.5f, mid, 0f), new Vector3(wall, ceiling, half * 2f), wallColor);
            Add(new Vector3(half + wall * 0.5f, mid, 0f), new Vector3(wall, ceiling, half * 2f), wallColor);
            Add(new Vector3(0f, mid, -half - wall * 0.5f), new Vector3(half * 2f, ceiling, wall), wallColor);
            Add(new Vector3(0f, mid, half + wall * 0.5f), new Vector3(half * 2f, ceiling, wall), wallColor);

            // Escalier : 8 marches de 30 cm, pour verifier le franchissement.
            for (int i = 0; i < 8; i++)
            {
                float y = 0.15f + i * 0.3f;
                float z = -6f - i * 1.2f;
                Add(new Vector3(-14f, y, z), new Vector3(6f, 0.3f + i * 0.3f, 1.2f), stepColor);
            }

            // Plateforme haute au bout de l'escalier
            Add(new Vector3(-14f, 1.2f, -20f), new Vector3(10f, 0.4f, 8f), platformColor);

            // Plateformes intermediaires, a sauter
            Add(new Vector3(10f, 0.8f, -8f), new Vector3(5f, 0.4f, 5f), platformColor);
            Add(new Vector3(17f, 1.6f, -14f), new Vector3(5f, 0.4f, 5f), platformColor);
            Add(new Vector3(22f, 2.4f, -20f), new Vector3(5f, 0.4f, 5f), platformColor);

            // Piliers : pour tester le glissement le long des murs
            for (int i = 0; i < 5; i++)
            {
                float x = -8f + i * 4f;
                Add(new Vector3(x, mid, 12f), new Vector3(1.2f, ceiling, 1.2f), pillarColor);
            }

            // Rampe en marches basses : test du franchissement automatique
            for (int i = 0; i < 6; i++)
                Add(new Vector3(8f + i * 1.5f, 0.1f + i * 0.2f, 20f), new Vector3(1.5f, 0.2f + i * 0.4f, 6f), stepColor);

            // Un enclos ouvert, pour empiler des caisses
            Add(new Vector3(0f, 0.6f, -14f), new Vector3(8f, 1.2f, 0.4f), wallColor);
            Add(new Vector3(-4f, 0.6f, -11f), new Vector3(0.4f, 1.2f, 6f), wallColor);
            Add(new Vector3(4f, 0.6f, -11f), new Vector3(0.4f, 1.2f, 6f), wallColor);

            // ---------------------------------------------------------------
            //  La pile de depart et le mannequin, DECLARES par la carte.
            //
            //  Ils etaient ajoutes par le programme apres n'importe quelle
            //  carte. Ici, aux memes coordonnees qu'avant : la pile de quatre
            //  en (0, 0, 8), le mannequin en (0, 0, 15) tourne vers le point de
            //  depart (lacet 180 : il regarde vers +Z).
            // ---------------------------------------------------------------
            w.Entities.Add(new MapEntity(EntityTypes.Crate, new Vector3(0f, 0f, 8f)).Set("count", "4"));
            w.Entities.Add(new MapEntity(EntityTypes.Mannequin, new Vector3(0f, 0f, 15f), yaw: 180f));

            // ---------------------------------------------------------------
            //  Les lumieres font partie de la CARTE, pas du programme.
            //
            //  Les deux projecteurs sont a 2,2 et 2 fois les lampes. Ces
            //  facteurs existaient deja dans le code d'eclairage d'origine -
            //  on ne fait que les sortir du programme.
            // ---------------------------------------------------------------

            w.Lights.Add(MapLight.Directional(
                sunDirection, new Vector3(0.62f, 0.70f, 0.85f), sunIntensity));

            var warm = new Vector3(1.0f, 0.78f, 0.48f);
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                    w.Lights.Add(MapLight.Point(new Vector3(x * 18f, 6.5f, z * 18f),
                                                warm, lampIntensity, range: 26f));

            // Projecteur rouge sur l'enclos a caisses.
            w.Lights.Add(MapLight.Spot(
                new Vector3(0f, 7.5f, -11f), new Vector3(0f, -1f, 0f),
                new Vector3(1f, 0.42f, 0.35f), lampIntensity * 2.2f,
                range: 18f, innerDegrees: 22f, outerDegrees: 34f));

            // Projecteur froid sur la plateforme haute.
            w.Lights.Add(MapLight.Spot(
                new Vector3(-14f, 8f, -20f), new Vector3(0.15f, -1f, 0.1f),
                new Vector3(0.55f, 0.75f, 1f), lampIntensity * 2f,
                range: 20f, innerDegrees: 20f, outerDegrees: 32f));

            return w;
        }
    }
}
