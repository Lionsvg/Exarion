// ===========================================================================
//  Exarion - Cible et zones de degats
//
//  Un personnage est decoupe en 4 boites empilees. La tete est
//  volontairement ETROITE (45 % de la largeur du corps) : un headshot doit
//  se meriter. C'est un reglage de gameplay, pas un detail technique - une
//  tete large rend le jeu trivial, une tete trop fine le rend frustrant.
//
//  Les proportions sont exprimees en fraction de la hauteur, pour rester
//  correctes quand le personnage s'accroupit.
// ===========================================================================

using System.Numerics;
using Exarion.Core.Physics;

namespace Exarion.Core.Combat
{
    public sealed class CombatTarget
    {
        /// <summary>Fractions de la hauteur totale delimitant les zones.</summary>
        public const float LegsTop = 0.46f;
        public const float StomachTop = 0.60f;
        public const float ChestTop = 0.875f;

        /// <summary>Largeur de la tete, en fraction de la largeur du corps.</summary>
        public const float HeadWidthRatio = 0.45f;

        public int Id;
        public Team Team = Team.None;

        /// <summary>Position des PIEDS.</summary>
        public Vector3 Position;

        public float Height = 1.83f;
        public float Width = 0.8f;

        public int MaxHealth = 100;
        public int Health = 100;
        public bool HasArmor;

        public bool IsAlive => Health > 0;

        /// <summary>Boite englobante complete. Sert au rejet grossier.</summary>
        public Aabb GetBounds() => Aabb.ForCharacter(Position, Width, Height);

        /// <summary>Boite d'une zone donnee, en coordonnees monde.</summary>
        public Aabb GetZoneBox(HitZone zone)
        {
            float w = Width;
            float y0, y1;

            switch (zone)
            {
                case HitZone.Legs:
                    y0 = 0f; y1 = LegsTop;
                    break;
                case HitZone.Stomach:
                    y0 = LegsTop; y1 = StomachTop;
                    break;
                case HitZone.Chest:
                    y0 = StomachTop; y1 = ChestTop;
                    break;
                default:   // Head
                    y0 = ChestTop; y1 = 1f;
                    w = Width * HeadWidthRatio;
                    break;
            }

            float half = w * 0.5f;
            return new Aabb(
                new Vector3(Position.X - half, Position.Y + y0 * Height, Position.Z - half),
                new Vector3(Position.X + half, Position.Y + y1 * Height, Position.Z + half));
        }

        /// <summary>Ordre de test : de la plus petite zone a la plus grande.</summary>
        private static readonly HitZone[] Zones =
        {
            HitZone.Head, HitZone.Chest, HitZone.Stomach, HitZone.Legs,
        };

        /// <summary>
        /// Lance un rayon contre les zones et retourne la plus proche touchee.
        /// </summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance,
                            out HitZone zone, out float distance)
        {
            zone = HitZone.Chest;
            distance = maxDistance;

            if (!IsAlive)
                return false;

            // Un seul monde temporaire par zone serait couteux : on reutilise
            // le meme code de tranches via un CollisionWorld local minimal.
            bool found = false;

            for (int i = 0; i < Zones.Length; i++)
            {
                Aabb box = GetZoneBox(Zones[i]);

                if (RayVsAabb(origin, direction, maxDistance, box, out float t) && t < distance)
                {
                    distance = t;
                    zone = Zones[i];
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Rayon contre boite, methode des tranches. Duplique volontairement
        /// la logique de CollisionWorld : ici on n'a pas de liste de boites,
        /// et on veut eviter d'allouer un monde par cible a chaque balle.
        /// </summary>
        internal static bool RayVsAabb(Vector3 origin, Vector3 direction, float maxDistance,
                                       in Aabb box, out float distance)
        {
            distance = maxDistance;

            float lenSq = direction.LengthSquared();
            if (lenSq < 1e-12f)
                return false;

            Vector3 dir = Vector3.Normalize(direction);

            float near = 0f;
            float far = maxDistance;

            for (int axis = 0; axis < 3; axis++)
            {
                float o = axis == 0 ? origin.X : (axis == 1 ? origin.Y : origin.Z);
                float d = axis == 0 ? dir.X : (axis == 1 ? dir.Y : dir.Z);
                float min = axis == 0 ? box.Min.X : (axis == 1 ? box.Min.Y : box.Min.Z);
                float max = axis == 0 ? box.Max.X : (axis == 1 ? box.Max.Y : box.Max.Z);

                if (System.MathF.Abs(d) < 1e-7f)
                {
                    if (o < min || o > max)
                        return false;

                    continue;
                }

                float t1 = (min - o) / d;
                float t2 = (max - o) / d;
                if (t1 > t2)
                    (t1, t2) = (t2, t1);

                if (t1 > near) near = t1;
                if (t2 < far) far = t2;

                if (near > far)
                    return false;
            }

            if (near < 0f || near > maxDistance)
                return false;

            distance = near;
            return true;
        }

        /// <summary>Applique des degats. Retourne true si la cible en meurt.</summary>
        public bool ApplyDamage(int amount)
        {
            if (amount <= 0 || !IsAlive)
                return false;

            Health -= amount;
            if (Health <= 0)
            {
                Health = 0;
                return true;
            }

            return false;
        }

        public void Revive()
        {
            Health = MaxHealth;
        }
    }
}
