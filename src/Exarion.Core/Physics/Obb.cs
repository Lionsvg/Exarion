// ===========================================================================
//  Exarion - Boite orientee (OBB)
//
//  Une AABB ne peut pas tourner : une caisse ne bascule jamais sur un coin.
//  L'OBB ajoute une orientation, et c'est ce qui permet la rotation des
//  corps rigides.
//
//  Le prix : la detection de collision devient nettement plus difficile.
//  Deux AABB se comparent en 6 comparaisons ; deux OBB demandent le
//  THEOREME DES AXES SEPARATEURS sur 15 axes candidats.
//
//  Enonce du theoreme : deux convexes sont disjoints si et seulement s'il
//  existe un axe sur lequel leurs projections ne se recouvrent pas. Pour
//  deux boites, il suffit de tester :
//     - les 3 axes de A
//     - les 3 axes de B
//     - les 9 produits vectoriels entre un axe de A et un axe de B
//  Ces 9 derniers couvrent les contacts arete contre arete, ceux qu'on
//  oublie toujours et qui laissent les objets s'interpenetrer.
//
//  Theoreme classique et public ; implementation ecrite ici.
// ===========================================================================

using System;
using System.Numerics;

namespace Exarion.Core.Physics
{
    public readonly struct Obb
    {
        public readonly Vector3 Center;

        /// <summary>Demi-dimensions, dans le repere propre de la boite.</summary>
        public readonly Vector3 HalfExtents;

        public readonly Quaternion Orientation;

        public Obb(Vector3 center, Vector3 halfExtents, Quaternion orientation)
        {
            Center = center;
            HalfExtents = Vector3.Abs(halfExtents);
            Orientation = Quaternion.Normalize(orientation);
        }

        public static Obb FromSize(Vector3 center, Vector3 size, Quaternion orientation)
            => new Obb(center, size * 0.5f, orientation);

        /// <summary>Une AABB est une OBB sans rotation.</summary>
        public static Obb FromAabb(in Aabb box)
            => new Obb(box.Center, box.Extents, Quaternion.Identity);

        public Vector3 AxisX => Vector3.Transform(Vector3.UnitX, Orientation);
        public Vector3 AxisY => Vector3.Transform(Vector3.UnitY, Orientation);
        public Vector3 AxisZ => Vector3.Transform(Vector3.UnitZ, Orientation);

        public Vector3 GetAxis(int index) => index switch
        {
            0 => AxisX,
            1 => AxisY,
            _ => AxisZ,
        };

        public float GetExtent(int index) => index switch
        {
            0 => HalfExtents.X,
            1 => HalfExtents.Y,
            _ => HalfExtents.Z,
        };

        /// <summary>
        /// Demi-largeur de la projection de la boite sur un axe UNITAIRE.
        /// C'est la quantite centrale du theoreme des axes separateurs.
        /// </summary>
        public float ProjectedRadius(Vector3 unitAxis)
        {
            return MathF.Abs(Vector3.Dot(unitAxis, AxisX)) * HalfExtents.X
                 + MathF.Abs(Vector3.Dot(unitAxis, AxisY)) * HalfExtents.Y
                 + MathF.Abs(Vector3.Dot(unitAxis, AxisZ)) * HalfExtents.Z;
        }

        /// <summary>Les 8 sommets, en coordonnees monde.</summary>
        public void GetCorners(Span<Vector3> corners)
        {
            if (corners.Length < 8)
                throw new ArgumentException("Il faut 8 emplacements.", nameof(corners));

            Vector3 x = AxisX * HalfExtents.X;
            Vector3 y = AxisY * HalfExtents.Y;
            Vector3 z = AxisZ * HalfExtents.Z;

            corners[0] = Center - x - y - z;
            corners[1] = Center + x - y - z;
            corners[2] = Center + x - y + z;
            corners[3] = Center - x - y + z;
            corners[4] = Center - x + y - z;
            corners[5] = Center + x + y - z;
            corners[6] = Center + x + y + z;
            corners[7] = Center - x + y + z;
        }

        /// <summary>
        /// AABB englobante. Sert au tri grossier : comparer des AABB est
        /// bien plus rapide que d'appliquer le theoreme des axes.
        /// </summary>
        public Aabb GetBounds()
        {
            Vector3 r = new Vector3(
                MathF.Abs(AxisX.X) * HalfExtents.X + MathF.Abs(AxisY.X) * HalfExtents.Y + MathF.Abs(AxisZ.X) * HalfExtents.Z,
                MathF.Abs(AxisX.Y) * HalfExtents.X + MathF.Abs(AxisY.Y) * HalfExtents.Y + MathF.Abs(AxisZ.Y) * HalfExtents.Z,
                MathF.Abs(AxisX.Z) * HalfExtents.X + MathF.Abs(AxisY.Z) * HalfExtents.Y + MathF.Abs(AxisZ.Z) * HalfExtents.Z);

            return new Aabb(Center - r, Center + r);
        }

        /// <summary>Passe un point du monde vers le repere propre de la boite.</summary>
        public Vector3 ToLocal(Vector3 worldPoint)
        {
            Vector3 d = worldPoint - Center;
            return new Vector3(Vector3.Dot(d, AxisX), Vector3.Dot(d, AxisY), Vector3.Dot(d, AxisZ));
        }

        /// <summary>Passe un point du repere propre vers le monde.</summary>
        public Vector3 ToWorld(Vector3 localPoint)
            => Center + AxisX * localPoint.X + AxisY * localPoint.Y + AxisZ * localPoint.Z;

        /// <summary>Point de la boite le plus proche d'un point donne.</summary>
        public Vector3 ClosestPoint(Vector3 worldPoint)
        {
            Vector3 local = ToLocal(worldPoint);

            local.X = Math.Clamp(local.X, -HalfExtents.X, HalfExtents.X);
            local.Y = Math.Clamp(local.Y, -HalfExtents.Y, HalfExtents.Y);
            local.Z = Math.Clamp(local.Z, -HalfExtents.Z, HalfExtents.Z);

            return ToWorld(local);
        }

        public bool Contains(Vector3 worldPoint)
        {
            Vector3 l = ToLocal(worldPoint);

            return MathF.Abs(l.X) <= HalfExtents.X
                && MathF.Abs(l.Y) <= HalfExtents.Y
                && MathF.Abs(l.Z) <= HalfExtents.Z;
        }

        public Obb Translated(Vector3 delta) => new Obb(Center + delta, HalfExtents, Orientation);
        public Obb Rotated(Quaternion rotation) => new Obb(Center, HalfExtents, rotation * Orientation);

        public override string ToString()
            => $"OBB(c={Center}, e={HalfExtents})";
    }
}
