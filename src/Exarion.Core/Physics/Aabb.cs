// ===========================================================================
//  Exarion - Boite alignee sur les axes
//
//  Toute la collision du jeu repose sur ce type. Les FPS tactiques n'ont
//  pas besoin de physique rigide pour le joueur : ils utilisent un
//  "character controller" qui glisse le long des murs. Des boites alignees
//  suffisent, et elles sont bien plus rapides et previsibles.
//
//  System.Numerics fait partie du runtime .NET : ce n'est pas une
//  dependance externe, et Vector3 y est accelere materiellement.
// ===========================================================================

using System;
using System.Numerics;

namespace Exarion.Core.Physics
{
    public readonly struct Aabb : IEquatable<Aabb>
    {
        public readonly Vector3 Min;
        public readonly Vector3 Max;

        public Aabb(Vector3 min, Vector3 max)
        {
            // On tolere des bornes inversees plutot que de lever : c'est
            // frequent quand une boite est construite depuis deux coins.
            Min = Vector3.Min(min, max);
            Max = Vector3.Max(min, max);
        }

        public static Aabb FromCenterSize(Vector3 center, Vector3 size)
        {
            Vector3 half = Vector3.Abs(size) * 0.5f;
            return new Aabb(center - half, center + half);
        }

        /// <summary>Boite d'un personnage : origine aux PIEDS, pas au centre.</summary>
        public static Aabb ForCharacter(Vector3 feet, float width, float height)
        {
            float half = width * 0.5f;
            return new Aabb(
                new Vector3(feet.X - half, feet.Y, feet.Z - half),
                new Vector3(feet.X + half, feet.Y + height, feet.Z + half));
        }

        public Vector3 Center => (Min + Max) * 0.5f;
        public Vector3 Size => Max - Min;
        public Vector3 Extents => (Max - Min) * 0.5f;

        public bool Intersects(in Aabb other)
        {
            return Min.X < other.Max.X && Max.X > other.Min.X
                && Min.Y < other.Max.Y && Max.Y > other.Min.Y
                && Min.Z < other.Max.Z && Max.Z > other.Min.Z;
        }

        public bool Contains(Vector3 p)
        {
            return p.X >= Min.X && p.X <= Max.X
                && p.Y >= Min.Y && p.Y <= Max.Y
                && p.Z >= Min.Z && p.Z <= Max.Z;
        }

        public Aabb Translated(Vector3 delta) => new Aabb(Min + delta, Max + delta);

        /// <summary>
        /// Agrandit la boite. Sert a la somme de Minkowski : gonfler l'obstacle
        /// des demi-dimensions du mobile ramene un balayage boite-contre-boite
        /// a un simple rayon-contre-boite.
        /// </summary>
        public Aabb Expanded(Vector3 by) => new Aabb(Min - by, Max + by);

        /// <summary>Boite englobant le trajet complet : sert au tri grossier.</summary>
        public Aabb Swept(Vector3 delta)
        {
            Aabb moved = Translated(delta);
            return new Aabb(Vector3.Min(Min, moved.Min), Vector3.Max(Max, moved.Max));
        }

        public bool Equals(Aabb other) => Min.Equals(other.Min) && Max.Equals(other.Max);
        public override bool Equals(object? obj) => obj is Aabb a && Equals(a);
        public override int GetHashCode() => HashCode.Combine(Min, Max);
        public override string ToString() => $"[{Min} -> {Max}]";
    }
}
