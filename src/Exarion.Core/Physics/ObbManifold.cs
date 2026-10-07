// ===========================================================================
//  Exarion - Points de contact entre boites orientees
//
//  Le theoreme des axes separateurs (voir ObbCollision) donne UNE normale et
//  UNE profondeur. C'est assez pour separer deux corps, pas pour les faire
//  tenir en equilibre.
//
//  Pourquoi : une caisse posee a plat sur une autre se touche par toute une
//  FACE. Avec un seul point d'appui, la moindre force la fait pivoter autour
//  de ce point - elle tangue au lieu de rester posee. Il faut le POLYGONE de
//  contact, c'est-a-dire la zone commune aux deux faces. C'est ce polygone,
//  et lui seul, qui decidera en R4 si une pile tient ou s'ecroule.
//
//  Methode retenue - technique classique et publique, ecrite ici :
//
//    1. FACE DE REFERENCE : sur le corps d'ou vient la normale, la face
//       qui regarde l'autre corps.
//    2. FACE INCIDENTE : sur l'autre corps, la face la plus opposee a cette
//       normale, donc celle qui lui fait face.
//    3. DECOUPAGE : on taille la face incidente par les 4 plans lateraux de
//       la face de reference (algorithme de Sutherland-Hodgman). Ce qui
//       reste est exactement la zone de recouvrement.
//    4. TRI : on ne garde que les morceaux reellement sous la face de
//       reference. Les autres sont au-dessus, donc ne touchent rien.
//
//  Cas particulier : quand la separation vient d'un produit vectoriel, le
//  contact est arete contre arete. Deux aretes qui se croisent ne se
//  touchent qu'en UN point - inutile de decouper quoi que ce soit, on
//  cherche les points les plus proches des deux segments.
//
//  Tout se fait sans allocation : le solveur tourne 64 fois par seconde.
// ===========================================================================

using System;
using System.Numerics;

namespace Exarion.Core.Physics
{
    /// <summary>Un point d'appui entre deux corps.</summary>
    public readonly struct ContactPoint
    {
        /// <summary>Position en coordonnees monde.</summary>
        public readonly Vector3 Position;

        /// <summary>
        /// Enfoncement en ce point, mesure le long de la normale du contact.
        /// Toujours positif ou nul. Un point d'un polygone d'appui peut
        /// toucher sans s'enfoncer : sa profondeur vaut alors zero.
        /// </summary>
        public readonly float Depth;

        public ContactPoint(Vector3 position, float depth)
        {
            Position = position;
            Depth = depth;
        }

        public override string ToString() => $"({Position}, d={Depth:0.####})";
    }

    public static class ObbManifold
    {
        /// <summary>
        /// Un quadrilatere coupe par 4 plans gagne au pire un sommet par
        /// plan : 4 + 4 = 8. C'est le cas d'une caisse tournee a 45 degres
        /// posee sur une autre, dont la zone commune est un octogone.
        /// </summary>
        public const int MaxPoints = 8;

        /// <summary>
        /// Deux points plus proches que 0,1 mm sont le meme point. Les
        /// garder tous les deux donnerait deux contraintes identiques au
        /// solveur, qui appliquerait la correction en double : ca tremble.
        /// </summary>
        private const float DuplicateDistanceSquared = 1e-8f;

        /// <summary>
        /// On garde les points qui affleurent, jusqu'a 1 mm au-dessus de la
        /// face. Au repos les points d'appui frolent le zero : les jeter
        /// ferait retrecir le polygone d'un pas a l'autre, donc trembler.
        /// </summary>
        private const float KeepTolerance = 1e-3f;

        /// <summary>
        /// Construit les points de contact d'une paire deja testee par
        /// <see cref="ObbCollision.Intersect"/>.
        /// </summary>
        /// <param name="points">
        /// Tampon d'au moins <see cref="MaxPoints"/> elements, typiquement
        /// un <c>stackalloc</c> : aucune allocation n'a lieu ici.
        /// </param>
        /// <returns>Le nombre de points ecrits.</returns>
        public static int Build(in Obb a, in Obb b, in ObbContact contact, Span<ContactPoint> points)
        {
            if (points.Length < MaxPoints)
                throw new ArgumentException($"Il faut {MaxPoints} emplacements.", nameof(points));

            if (!contact.Touching)
                return 0;

            switch (contact.Feature)
            {
                // La normale sort de A : A porte la face de reference.
                case ContactFeature.FaceA:
                    return BuildFaceContact(a, b, contact.AxisA, contact.Normal, contact.Depth, points);

                // La normale sort de B, mais elle est orientee de A vers B :
                // vue depuis B, la face de reference regarde donc a l'oppose.
                case ContactFeature.FaceB:
                    return BuildFaceContact(b, a, contact.AxisB, -contact.Normal, contact.Depth, points);

                case ContactFeature.Edge:
                    return BuildEdgeContact(a, b, contact, points);

                default:
                    return 0;
            }
        }

        // -------------------------------------------------------------------
        //  Contact face contre face
        // -------------------------------------------------------------------

        /// <param name="referenceNormal">
        /// Normale unitaire SORTANTE de la face de reference, donc dirigee
        /// vers le corps incident.
        /// </param>
        private static int BuildFaceContact(in Obb reference, in Obb incident, int referenceAxis,
                                            Vector3 referenceNormal, float contactDepth,
                                            Span<ContactPoint> points)
        {
            if (referenceAxis < 0)
                return 0;

            // Laquelle des deux faces de l'axe ? Celle qui regarde du meme
            // cote que la normale.
            float referenceSign = Vector3.Dot(referenceNormal, reference.GetAxis(referenceAxis)) >= 0f ? 1f : -1f;
            float referenceExtent = reference.GetExtent(referenceAxis);

            // --- 1. Face incidente ---
            //
            // On cherche la face du corps incident la plus opposee a la
            // normale. Le plus grand |produit scalaire| designe l'axe ; le
            // signe du produit scalaire designe laquelle de ses deux faces.
            int incidentAxis = 0;
            float bestAlignment = -1f;

            for (int k = 0; k < 3; k++)
            {
                float alignment = MathF.Abs(Vector3.Dot(incident.GetAxis(k), referenceNormal));

                if (alignment > bestAlignment)
                {
                    bestAlignment = alignment;
                    incidentAxis = k;
                }
            }

            float incidentSign = Vector3.Dot(incident.GetAxis(incidentAxis), referenceNormal) >= 0f ? -1f : 1f;

            // --- 2. Les 4 sommets de cette face ---
            int u = (incidentAxis + 1) % 3;
            int v = (incidentAxis + 2) % 3;

            Vector3 faceCenter = incident.Center
                               + incident.GetAxis(incidentAxis) * (incidentSign * incident.GetExtent(incidentAxis));

            Vector3 du = incident.GetAxis(u) * incident.GetExtent(u);
            Vector3 dv = incident.GetAxis(v) * incident.GetExtent(v);

            // Tout le decoupage se fait dans le repere de la boite de
            // reference : ses 4 plans lateraux y deviennent de simples
            // comparaisons |coordonnee| <= demi-dimension.
            Span<Vector3> polygon = stackalloc Vector3[MaxPoints];
            Span<Vector3> scratch = stackalloc Vector3[MaxPoints];

            // L'ordre doit etre CYCLIQUE : un ordre quelconque decrirait un
            // polygone croise, que le decoupage traiterait de travers.
            polygon[0] = reference.ToLocal(faceCenter + du + dv);
            polygon[1] = reference.ToLocal(faceCenter - du + dv);
            polygon[2] = reference.ToLocal(faceCenter - du - dv);
            polygon[3] = reference.ToLocal(faceCenter + du - dv);
            int count = 4;

            // --- 3. Decoupage par les 4 cotes de la face de reference ---
            int su = (referenceAxis + 1) % 3;
            int sv = (referenceAxis + 2) % 3;

            count = Clip(polygon, count, scratch, su, +1f, reference.GetExtent(su));
            count = Clip(scratch, count, polygon, su, -1f, reference.GetExtent(su));
            count = Clip(polygon, count, scratch, sv, +1f, reference.GetExtent(sv));
            count = Clip(scratch, count, polygon, sv, -1f, reference.GetExtent(sv));

            // --- 4. On ne garde que ce qui touche vraiment ---
            int kept = 0;
            int deepest = -1;
            float deepestSeparation = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                // Distance signee au plan de la face : negative = enfonce.
                float separation = referenceSign * Component(polygon[i], referenceAxis) - referenceExtent;

                if (separation < deepestSeparation)
                {
                    deepestSeparation = separation;
                    deepest = i;
                }

                if (separation > KeepTolerance)
                    continue;

                Vector3 world = reference.ToWorld(polygon[i]);

                if (IsDuplicate(points, kept, world))
                    continue;

                points[kept++] = new ContactPoint(world, MathF.Max(0f, -separation));
            }

            // Filet de securite : le SAT a vu un contact, on ne repart jamais
            // les mains vides. Sans ca une collision serait ignoree en
            // silence, ce qui est bien pire qu'un point approximatif.
            if (kept == 0)
            {
                Vector3 fallback = deepest >= 0
                    ? reference.ToWorld(polygon[deepest])
                    : reference.ClosestPoint(incident.Center);

                points[0] = new ContactPoint(fallback, contactDepth);
                return 1;
            }

            return kept;
        }

        /// <summary>
        /// Sutherland-Hodgman : coupe un polygone convexe par un demi-espace.
        ///
        /// Le principe tient en une phrase. On parcourt les aretes ; pour
        /// chacune on garde le sommet de depart s'il est du bon cote, et on
        /// ajoute le point de traversee si l'arete change de cote.
        ///
        /// Le demi-espace conserve est <c>sign * coordonnee[axis] &lt;= extent</c>.
        /// </summary>
        private static int Clip(ReadOnlySpan<Vector3> source, int count, Span<Vector3> destination,
                                int axis, float sign, float extent)
        {
            int written = 0;

            for (int i = 0; i < count; i++)
            {
                Vector3 current = source[i];
                Vector3 next = source[(i + 1) % count];

                float dCurrent = sign * Component(current, axis) - extent;
                float dNext = sign * Component(next, axis) - extent;

                if (dCurrent <= 0f && written < destination.Length)
                    destination[written++] = current;

                // Changement de cote : on insere le point exact de traversee.
                if (dCurrent * dNext < 0f && written < destination.Length)
                {
                    float t = dCurrent / (dCurrent - dNext);
                    destination[written++] = current + (next - current) * t;
                }
            }

            return written;
        }

        private static bool IsDuplicate(ReadOnlySpan<ContactPoint> points, int count, Vector3 candidate)
        {
            for (int i = 0; i < count; i++)
            {
                if ((points[i].Position - candidate).LengthSquared() < DuplicateDistanceSquared)
                    return true;
            }

            return false;
        }

        // -------------------------------------------------------------------
        //  Contact arete contre arete
        // -------------------------------------------------------------------

        private static int BuildEdgeContact(in Obb a, in Obb b, in ObbContact contact, Span<ContactPoint> points)
        {
            if (contact.AxisA < 0 || contact.AxisB < 0)
                return 0;

            Vector3 normal = contact.Normal;   // de A vers B

            // Sur chaque corps, l'arete en cause est parallele a l'axe
            // retenu par le produit vectoriel, et c'est la plus avancee vers
            // l'autre corps.
            Vector3 centerA = SupportEdgeCenter(a, contact.AxisA, normal);
            Vector3 centerB = SupportEdgeCenter(b, contact.AxisB, -normal);

            ClosestPointsBetweenSegments(
                centerA, a.GetAxis(contact.AxisA), a.GetExtent(contact.AxisA),
                centerB, b.GetAxis(contact.AxisB), b.GetExtent(contact.AxisB),
                out Vector3 pointA, out Vector3 pointB);

            // Les deux aretes se sont enfoncees l'une dans l'autre : le point
            // juste est a mi-chemin.
            points[0] = new ContactPoint((pointA + pointB) * 0.5f, contact.Depth);
            return 1;
        }

        /// <summary>
        /// Milieu de l'arete parallele a <paramref name="edgeAxis"/> la plus
        /// avancee dans <paramref name="direction"/>. Sur les deux AUTRES
        /// axes on prend le cote qui pointe vers l'autre corps ; le long de
        /// l'arete il n'y a rien a choisir, elle s'etend des deux cotes.
        /// </summary>
        private static Vector3 SupportEdgeCenter(in Obb box, int edgeAxis, Vector3 direction)
        {
            Vector3 center = box.Center;

            for (int k = 0; k < 3; k++)
            {
                if (k == edgeAxis)
                    continue;

                Vector3 axis = box.GetAxis(k);
                float sign = Vector3.Dot(axis, direction) >= 0f ? 1f : -1f;

                center += axis * (sign * box.GetExtent(k));
            }

            return center;
        }

        /// <summary>
        /// Points les plus proches entre deux segments, donnes par leur
        /// milieu, leur direction UNITAIRE et leur demi-longueur.
        ///
        /// On annule les deux derivees de la distance au carre, ce qui donne
        /// un systeme de deux equations, puis on ramene les solutions dans
        /// les bornes des segments.
        /// </summary>
        private static void ClosestPointsBetweenSegments(
            Vector3 centerA, Vector3 dirA, float halfA,
            Vector3 centerB, Vector3 dirB, float halfB,
            out Vector3 pointA, out Vector3 pointB)
        {
            Vector3 r = centerA - centerB;

            float b = Vector3.Dot(dirA, dirB);
            float c = Vector3.Dot(dirA, r);
            float f = Vector3.Dot(dirB, r);

            // Les directions etant unitaires, le determinant se reduit a ceci.
            float denominator = 1f - b * b;

            // Aretes paralleles : n'importe quel point convient, le SAT
            // n'aurait de toute facon pas retenu cet axe (degenere).
            float tA = denominator > 1e-6f ? (b * f - c) / denominator : 0f;
            tA = Math.Clamp(tA, -halfA, halfA);

            float tB = Math.Clamp(tA * b + f, -halfB, halfB);

            // Si tB a ete rogne par la borne du segment, tA n'est plus
            // optimal : une passe de rattrapage suffit.
            tA = Math.Clamp(tB * b - c, -halfA, halfA);

            pointA = centerA + dirA * tA;
            pointB = centerB + dirB * tB;
        }

        private static float Component(Vector3 v, int i) => i == 0 ? v.X : (i == 1 ? v.Y : v.Z);
    }
}
