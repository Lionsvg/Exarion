// ===========================================================================
//  Exarion - Monde de collision
//
//  Balayage d'une boite mobile contre des boites statiques, puis
//  deplacement avec glissement le long des surfaces.
//
//  METHODE : somme de Minkowski + methode des tranches (slab method).
//  On gonfle chaque obstacle des demi-dimensions du mobile, ce qui ramene
//  le probleme "boite contre boite en mouvement" a "rayon contre boite" -
//  bien plus simple et exact. Le rayon part du centre du mobile.
//
//  C'est une technique classique et publique, implementee ici a partir de
//  sa description mathematique.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Exarion.Core.Physics
{
    public readonly struct SweepHit
    {
        public readonly bool Hit;
        /// <summary>Fraction du deplacement parcourue avant l'impact, dans [0,1].</summary>
        public readonly float Time;
        /// <summary>Normale de la surface touchee, unitaire et opposee au mouvement.</summary>
        public readonly Vector3 Normal;

        public SweepHit(bool hit, float time, Vector3 normal)
        {
            Hit = hit;
            Time = time;
            Normal = normal;
        }

        public static readonly SweepHit None = new SweepHit(false, 1f, Vector3.Zero);
    }

    public sealed class CollisionWorld
    {
        /// <summary>Marge de securite : on s'arrete juste avant la surface.</summary>
        public const float SkinWidth = 0.001f;

        private const float Epsilon = 1e-7f;

        private readonly List<Aabb> _boxes = new();

        public IReadOnlyList<Aabb> Boxes => _boxes;

        public void Add(in Aabb box) => _boxes.Add(box);

        public void AddBox(Vector3 center, Vector3 size) => _boxes.Add(Aabb.FromCenterSize(center, size));

        public void Clear() => _boxes.Clear();

        /// <summary>Vrai si la boite chevauche un obstacle. Sert aux tests de blocage.</summary>
        public bool Overlaps(in Aabb box)
        {
            for (int i = 0; i < _boxes.Count; i++)
            {
                if (box.Intersects(_boxes[i]))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Balaye la boite le long de delta et retourne le premier impact.
        /// Time = 1 et Hit = false si le trajet est libre.
        /// </summary>
        public SweepHit Sweep(in Aabb mover, Vector3 delta)
        {
            if (delta.LengthSquared() < Epsilon)
                return SweepHit.None;

            Vector3 origin = mover.Center;
            Vector3 extents = mover.Extents;

            // Boite englobant tout le trajet : rejet grossier bon marche.
            Aabb broad = mover.Swept(delta);

            var best = SweepHit.None;

            for (int i = 0; i < _boxes.Count; i++)
            {
                Aabb obstacle = _boxes[i];

                if (!broad.Intersects(obstacle))
                    continue;

                // Minkowski : l'obstacle gonfle du mobile -> rayon contre boite.
                Aabb expanded = obstacle.Expanded(extents);

                if (RayVsBox(origin, delta, expanded, out float t, out Vector3 normal) && t < best.Time)
                    best = new SweepHit(true, t, normal);
            }

            return best;
        }

        /// <summary>
        /// Calcule de combien deplacer une boite pour la sortir du decor.
        ///
        /// INDISPENSABLE des qu'on resout des contacts entre corps : separer
        /// deux caisses deplace leurs positions directement, sans balayage,
        /// et peut donc enfoncer l'une dans le sol. Sans cette etape de
        /// rattrapage, elle passe au travers et tombe a l'infini.
        ///
        /// Separation sur l'axe de moindre penetration, boite par boite.
        /// </summary>
        /// <returns>Vrai s'il y avait au moins un chevauchement.</returns>
        public bool Depenetrate(in Aabb box, out Vector3 correction)
        {
            correction = Vector3.Zero;
            bool any = false;

            for (int i = 0; i < _boxes.Count; i++)
            {
                Aabb obstacle = _boxes[i];
                Aabb current = box.Translated(correction);

                if (!current.Intersects(obstacle))
                    continue;

                float px = MathF.Min(current.Max.X, obstacle.Max.X) - MathF.Max(current.Min.X, obstacle.Min.X);
                float py = MathF.Min(current.Max.Y, obstacle.Max.Y) - MathF.Max(current.Min.Y, obstacle.Min.Y);
                float pz = MathF.Min(current.Max.Z, obstacle.Max.Z) - MathF.Max(current.Min.Z, obstacle.Min.Z);

                if (px <= 0f || py <= 0f || pz <= 0f)
                    continue;

                Vector3 centerA = current.Center;
                Vector3 centerB = obstacle.Center;

                if (px <= py && px <= pz)
                    correction.X += centerA.X < centerB.X ? -px : px;
                else if (py <= pz)
                    correction.Y += centerA.Y < centerB.Y ? -py : py;
                else
                    correction.Z += centerA.Z < centerB.Z ? -pz : pz;

                any = true;
            }

            return any;
        }

        /// <summary>
        /// Lance un rayon contre le decor. Sert au tir hitscan : la balle
        /// s'arrete au premier mur rencontre.
        /// </summary>
        /// <returns>Vrai si le rayon touche quelque chose avant maxDistance.</returns>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance,
                            out float distance, out Vector3 normal)
        {
            distance = maxDistance;
            normal = Vector3.Zero;

            float lenSq = direction.LengthSquared();
            if (lenSq < Epsilon || maxDistance <= 0f)
                return false;

            Vector3 delta = Vector3.Normalize(direction) * maxDistance;
            bool found = false;
            float bestT = 1f;

            for (int i = 0; i < _boxes.Count; i++)
            {
                if (RayVsBox(origin, delta, _boxes[i], out float t, out Vector3 n) && t < bestT)
                {
                    bestT = t;
                    normal = n;
                    found = true;
                }
            }

            if (found)
                distance = bestT * maxDistance;

            return found;
        }

        /// <summary>
        /// Methode des tranches. Retourne la fraction d'entree et la normale
        /// de la face franchie en dernier.
        /// </summary>
        private static bool RayVsBox(Vector3 origin, Vector3 dir, in Aabb box, out float tEnter, out Vector3 normal)
        {
            tEnter = 0f;
            normal = Vector3.Zero;

            float near = float.NegativeInfinity;
            float far = float.PositiveInfinity;
            int axis = -1;
            float axisSign = 0f;

            for (int i = 0; i < 3; i++)
            {
                float o = Component(origin, i);
                float d = Component(dir, i);
                float min = Component(box.Min, i);
                float max = Component(box.Max, i);

                if (MathF.Abs(d) < Epsilon)
                {
                    // Trajet parallele a cette paire de faces : si on est deja
                    // hors de la tranche, on ne la franchira jamais.
                    if (o < min || o > max)
                        return false;

                    continue;
                }

                float t1 = (min - o) / d;
                float t2 = (max - o) / d;

                // On entre TOUJOURS par la face amont : si on va vers +, c'est
                // la face 'min' (normale -1) ; si on va vers -, c'est la face
                // 'max' (normale +1). Donc la normale vaut -signe(direction),
                // quel que soit l'ordre de t1 et t2.
                // (Inverser ce signe lors de l'echange etait un bug : le joueur
                //  n'etait jamais detecte au sol en tombant.)
                float sign = -MathF.Sign(d);

                if (t1 > t2)
                    (t1, t2) = (t2, t1);

                if (t1 > near)
                {
                    near = t1;
                    axis = i;
                    axisSign = sign;
                }

                if (t2 < far)
                    far = t2;

                if (near > far)
                    return false;
            }

            // Deja au-dela, ou trop loin pour ce pas.
            if (far < 0f || near > 1f)
                return false;

            // near < 0 : on demarre DANS l'obstacle. On ne bloque pas, sinon
            // le joueur reste coince definitivement au moindre chevauchement.
            if (near < 0f)
                return false;

            if (axis < 0)
                return false;

            tEnter = near;
            normal = axis switch
            {
                0 => new Vector3(axisSign, 0f, 0f),
                1 => new Vector3(0f, axisSign, 0f),
                _ => new Vector3(0f, 0f, axisSign),
            };

            return true;
        }

        private static float Component(Vector3 v, int i) => i == 0 ? v.X : (i == 1 ? v.Y : v.Z);

        /// <summary>
        /// Deplace la boite en glissant le long des surfaces rencontrees.
        /// Plusieurs passes : sans quoi on s'arrete net dans un angle au lieu
        /// de glisser le long du second mur.
        /// </summary>
        /// <param name="maxPasses">3 suffit : sol + mur + mur.</param>
        public Vector3 MoveAndSlide(in Aabb mover, Vector3 delta, out Vector3 lastNormal, int maxPasses = 4)
        {
            Vector3 position = Vector3.Zero;   // deplacement cumule
            Vector3 remaining = delta;
            lastNormal = Vector3.Zero;

            for (int pass = 0; pass < maxPasses; pass++)
            {
                if (remaining.LengthSquared() < Epsilon)
                    break;

                Aabb current = mover.Translated(position);
                SweepHit hit = Sweep(current, remaining);

                if (!hit.Hit)
                {
                    position += remaining;
                    break;
                }

                // On avance jusqu'a la surface, en gardant la marge de securite.
                float travel = MathF.Max(0f, hit.Time - SkinWidth);
                position += remaining * travel;
                lastNormal = hit.Normal;

                // Glissement : on retire la composante qui rentre dans la surface.
                Vector3 leftover = remaining * (1f - travel);
                remaining = leftover - hit.Normal * Vector3.Dot(leftover, hit.Normal);
            }

            return position;
        }
    }
}
