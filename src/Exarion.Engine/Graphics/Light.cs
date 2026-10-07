// ===========================================================================
//  Exarion - Lumieres
//
//  Trois types, comme dans tout moteur temps reel :
//
//    DIRECTIONNELLE  le soleil. Pas de position, pas d'attenuation.
//    PONCTUELLE      une ampoule. Rayonne dans toutes les directions.
//    PROJECTEUR      une ampoule dans un cone. Position + direction + angle.
//
//  ATTENUATION : on utilise l'inverse du carre de la distance, FENETRE par
//  une coupure douce a la portee. La loi physique pure (1/d^2) ne s'annule
//  jamais completement : sans fenetrage, chaque lumiere devrait etre
//  evaluee pour chaque pixel de la scene. La fenetre permet de les ignorer
//  au-dela de leur portee sans que la coupure se voie.
//
//      attenuation = saturate(1 - (d/portee)^4)^2 / (d^2 + 1)
// ===========================================================================

using System.Numerics;

namespace Exarion.Engine.Graphics
{
    public enum LightType
    {
        Directional = 0,
        Point = 1,
        Spot = 2,
    }

    public struct Light
    {
        public LightType Type;

        /// <summary>Ignoree pour une directionnelle.</summary>
        public Vector3 Position;

        /// <summary>Direction du rayonnement. Ignoree pour une ponctuelle.</summary>
        public Vector3 Direction;

        public Vector3 Color;

        /// <summary>Multiplicateur de couleur.</summary>
        public float Intensity;

        /// <summary>Portee en metres. Ignoree pour une directionnelle.</summary>
        public float Range;

        /// <summary>Demi-angle du cone interieur, en degres. Projecteur seulement.</summary>
        public float InnerConeDegrees;

        /// <summary>Demi-angle du cone exterieur, en degres. Projecteur seulement.</summary>
        public float OuterConeDegrees;

        public static Light Directional(Vector3 direction, Vector3 color, float intensity = 1f)
            => new Light
            {
                Type = LightType.Directional,
                Direction = Vector3.Normalize(direction),
                Color = color,
                Intensity = intensity,
                Range = 0f,
            };

        public static Light Point(Vector3 position, Vector3 color, float intensity = 1f, float range = 15f)
            => new Light
            {
                Type = LightType.Point,
                Position = position,
                Direction = -Vector3.UnitY,
                Color = color,
                Intensity = intensity,
                Range = range,
            };

        public static Light Spot(Vector3 position, Vector3 direction, Vector3 color,
                                 float intensity = 1f, float range = 20f,
                                 float innerDegrees = 18f, float outerDegrees = 28f)
            => new Light
            {
                Type = LightType.Spot,
                Position = position,
                Direction = Vector3.Normalize(direction),
                Color = color,
                Intensity = intensity,
                Range = range,
                InnerConeDegrees = innerDegrees,
                OuterConeDegrees = outerDegrees,
            };
    }
}
