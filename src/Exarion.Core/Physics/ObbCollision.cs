// ===========================================================================
//  Exarion - Collision entre boites orientees
//
//  Applique le theoreme des axes separateurs (SAT) sur les 15 axes
//  candidats, et retourne l'axe de MOINDRE recouvrement : c'est lui qui
//  donne la normale de contact et la profondeur de penetration, donc la
//  facon la moins couteuse de separer les deux corps.
//
//  ---------------------------------------------------------------------
//  UN PIEGE MAJEUR : les 9 axes croises ne sont PAS unitaires.
//
//  Un produit vectoriel entre deux axes proches d'etre paralleles donne un
//  vecteur minuscule. Comparer des profondeurs mesurees sur des axes de
//  longueurs differentes revient a comparer des metres avec des centimetres :
//  on choisit alors un axe absurde, et les objets se separent de travers.
//
//  Ici chaque axe est NORMALISE avant comparaison, et ceux qui sont trop
//  courts (axes quasi paralleles, donc redondants) sont ignores.
//  ---------------------------------------------------------------------
//  UN SECOND PIEGE : le tremblement par bascule d'axe.
//
//  Deux caisses posees a plat l'une sur l'autre donnent EXACTEMENT la meme
//  profondeur sur l'axe vertical de A et sur celui de B. C'est alors le
//  bruit de calcul qui tranche - et il peut changer d'avis d'un pas a
//  l'autre. Le polygone de contact bascule d'un corps a l'autre, le solveur
//  recoit des points differents a chaque fois, et la pile vibre au lieu de
//  s'endormir.
//
//  Correction : une hysteresis. Les axes de A sont prioritaires ; ceux de B
//  ne prennent la main que s'ils font NETTEMENT mieux, et les axes croises
//  seulement s'ils battent les deux. En cas d'egalite, le gagnant est
//  toujours le meme : le resultat devient reproductible.
//  ---------------------------------------------------------------------
//
//  Implementation ecrite a partir de l'enonce du theoreme.
// ===========================================================================

using System;
using System.Numerics;

namespace Exarion.Core.Physics
{
    /// <summary>
    /// D'ou vient la normale de contact. C'est cette information qui permet
    /// ensuite de construire les points de contact : une face contre une
    /// face donne un polygone, deux aretes ne donnent qu'un point.
    /// </summary>
    public enum ContactFeature
    {
        /// <summary>Pas de contact.</summary>
        None = 0,

        /// <summary>La normale vient d'un axe propre de A : c'est une face de A.</summary>
        FaceA = 1,

        /// <summary>La normale vient d'un axe propre de B : c'est une face de B.</summary>
        FaceB = 2,

        /// <summary>La normale vient d'un produit vectoriel : contact arete contre arete.</summary>
        Edge = 3,
    }

    /// <summary>Resultat d'un test de recouvrement entre deux boites.</summary>
    public readonly struct ObbContact
    {
        public readonly bool Touching;

        /// <summary>Normale unitaire, orientee de A vers B.</summary>
        public readonly Vector3 Normal;

        /// <summary>Profondeur de penetration le long de la normale, en metres.</summary>
        public readonly float Depth;

        /// <summary>Nature du contact : face de A, face de B, ou arete contre arete.</summary>
        public readonly ContactFeature Feature;

        /// <summary>
        /// Axe de A implique (0=X, 1=Y, 2=Z), ou -1 s'il n'y en a pas.
        /// Pour <see cref="ContactFeature.FaceA"/> c'est la face ; pour
        /// <see cref="ContactFeature.Edge"/> c'est la direction de l'arete.
        /// </summary>
        public readonly int AxisA;

        /// <summary>Idem pour B.</summary>
        public readonly int AxisB;

        public ObbContact(bool touching, Vector3 normal, float depth,
                          ContactFeature feature = ContactFeature.None,
                          int axisA = -1, int axisB = -1)
        {
            Touching = touching;
            Normal = normal;
            Depth = depth;
            Feature = feature;
            AxisA = axisA;
            AxisB = axisB;
        }

        public static readonly ObbContact None = new(false, Vector3.Zero, 0f);

        public override string ToString()
            => Touching ? $"{Feature} n={Normal} d={Depth:0.####}" : "aucun contact";
    }

    public static class ObbCollision
    {
        /// <summary>En deca, un axe croise est considere degenere et ignore.</summary>
        private const float MinAxisLengthSquared = 1e-6f;

        /// <summary>
        /// Hysteresis anti-bascule : pour voler la priorite a la categorie
        /// precedente, un axe doit faire au moins 5 % ET 1 mm de mieux.
        /// Sans ca, deux caisses a plat changent d'axe de reference au gre
        /// du bruit de calcul, et la pile tremble.
        /// </summary>
        private const float PriorityRelativeMargin = 0.95f;
        private const float PriorityAbsoluteMargin = 0.001f;

        /// <summary>
        /// Teste le recouvrement de deux boites orientees.
        /// Retourne l'axe de moindre penetration, et d'ou il vient.
        /// </summary>
        public static ObbContact Intersect(in Obb a, in Obb b)
        {
            // Rejet grossier : comparer deux AABB coute presque rien, et
            // elimine la grande majorite des paires.
            if (!a.GetBounds().Intersects(b.GetBounds()))
                return ObbContact.None;

            Vector3 toB = b.Center - a.Center;

            // --- Les 3 axes propres de A ---
            float faceDepthA = float.MaxValue;
            Vector3 faceNormalA = Vector3.Zero;
            int faceIndexA = -1;

            for (int i = 0; i < 3; i++)
            {
                if (!Overlap(a.GetAxis(i), a, b, toB, out float depth, out Vector3 normal))
                    return ObbContact.None;

                if (depth < faceDepthA)
                {
                    faceDepthA = depth;
                    faceNormalA = normal;
                    faceIndexA = i;
                }
            }

            // --- Les 3 axes propres de B ---
            float faceDepthB = float.MaxValue;
            Vector3 faceNormalB = Vector3.Zero;
            int faceIndexB = -1;

            for (int i = 0; i < 3; i++)
            {
                if (!Overlap(b.GetAxis(i), a, b, toB, out float depth, out Vector3 normal))
                    return ObbContact.None;

                if (depth < faceDepthB)
                {
                    faceDepthB = depth;
                    faceNormalB = normal;
                    faceIndexB = i;
                }
            }

            // --- Les 9 produits vectoriels : contacts arete contre arete ---
            float edgeDepth = float.MaxValue;
            Vector3 edgeNormal = Vector3.Zero;
            int edgeIndexA = -1;
            int edgeIndexB = -1;

            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    Vector3 axis = Vector3.Cross(a.GetAxis(i), b.GetAxis(j));

                    if (!Overlap(axis, a, b, toB, out float depth, out Vector3 normal))
                        return ObbContact.None;

                    if (depth < edgeDepth)
                    {
                        edgeDepth = depth;
                        edgeNormal = normal;
                        edgeIndexA = i;
                        edgeIndexB = j;
                    }
                }
            }

            // Les 3 axes de A sont toujours valides : s'ils ne le sont pas,
            // c'est que la boite est degeneree. On refuse le contact plutot
            // que de retourner une normale nulle.
            if (faceIndexA < 0)
                return ObbContact.None;

            // --- Choix final, avec l'hysteresis decrite en tete de fichier ---
            float bestDepth = faceDepthA;
            Vector3 bestNormal = faceNormalA;
            var feature = ContactFeature.FaceA;
            int axisA = faceIndexA;
            int axisB = -1;

            if (faceIndexB >= 0 && Beats(faceDepthB, bestDepth))
            {
                bestDepth = faceDepthB;
                bestNormal = faceNormalB;
                feature = ContactFeature.FaceB;
                axisA = -1;
                axisB = faceIndexB;
            }

            if (edgeIndexA >= 0 && Beats(edgeDepth, bestDepth))
            {
                bestDepth = edgeDepth;
                bestNormal = edgeNormal;
                feature = ContactFeature.Edge;
                axisA = edgeIndexA;
                axisB = edgeIndexB;
            }

            return new ObbContact(true, bestNormal, bestDepth, feature, axisA, axisB);
        }

        /// <summary>
        /// Un candidat ne detrone le tenant que s'il fait nettement mieux.
        /// On cherche un MINIMUM, donc "mieux" veut dire "plus petit".
        /// </summary>
        private static bool Beats(float candidate, float holder)
            => candidate < holder * PriorityRelativeMargin - PriorityAbsoluteMargin;

        /// <summary>
        /// Mesure le recouvrement sur un axe candidat.
        /// Retourne false si l'axe SEPARE les deux boites, auquel cas on peut
        /// conclure immediatement : il n'y a pas contact.
        /// </summary>
        private static bool Overlap(Vector3 axis, in Obb a, in Obb b, Vector3 toB,
                                    out float depth, out Vector3 normal)
        {
            depth = float.MaxValue;
            normal = Vector3.Zero;

            float lengthSquared = axis.LengthSquared();

            // Axe degenere : les deux axes sources sont quasi paralleles, donc
            // cet axe croise n'apporte rien que les axes propres ne couvrent
            // deja. L'ignorer est correct ; le normaliser serait catastrophique.
            if (lengthSquared < MinAxisLengthSquared)
                return true;

            axis /= MathF.Sqrt(lengthSquared);

            float radiusA = a.ProjectedRadius(axis);
            float radiusB = b.ProjectedRadius(axis);
            float distance = Vector3.Dot(toB, axis);

            float overlap = radiusA + radiusB - MathF.Abs(distance);

            if (overlap < 0f)
                return false;   // axe separateur : les boites ne se touchent pas

            depth = overlap;

            // La normale doit toujours pointer de A vers B, sinon la
            // separation pousserait les corps l'un DANS l'autre.
            normal = distance < 0f ? -axis : axis;
            return true;
        }

        /// <summary>
        /// Lance un rayon contre une boite orientee.
        ///
        /// Astuce : on ramene le rayon dans le repere propre de la boite,
        /// ce qui la rend alignee sur les axes. On retombe alors sur la
        /// methode des tranches, deja utilisee pour les AABB.
        /// </summary>
        public static bool Raycast(in Obb box, Vector3 origin, Vector3 direction, float maxDistance,
                                   out float distance, out Vector3 normal)
        {
            distance = maxDistance;
            normal = Vector3.Zero;

            float lengthSquared = direction.LengthSquared();
            if (lengthSquared < 1e-12f || maxDistance <= 0f)
                return false;

            Vector3 dir = direction / MathF.Sqrt(lengthSquared);

            // Passage dans le repere de la boite.
            Vector3 localOrigin = box.ToLocal(origin);
            var localDir = new Vector3(
                Vector3.Dot(dir, box.AxisX),
                Vector3.Dot(dir, box.AxisY),
                Vector3.Dot(dir, box.AxisZ));

            float near = 0f;
            float far = maxDistance;
            int axisIndex = -1;
            float axisSign = 0f;

            for (int i = 0; i < 3; i++)
            {
                float o = Component(localOrigin, i);
                float d = Component(localDir, i);
                float e = box.GetExtent(i);

                if (MathF.Abs(d) < 1e-7f)
                {
                    if (o < -e || o > e)
                        return false;

                    continue;
                }

                float t1 = (-e - o) / d;
                float t2 = (e - o) / d;
                float sign = -MathF.Sign(d);

                if (t1 > t2)
                    (t1, t2) = (t2, t1);

                if (t1 > near)
                {
                    near = t1;
                    axisIndex = i;
                    axisSign = sign;
                }

                if (t2 < far)
                    far = t2;

                if (near > far)
                    return false;
            }

            if (axisIndex < 0 || near < 0f || near > maxDistance)
                return false;

            distance = near;

            // La normale est un axe LOCAL : on la ramene dans le monde.
            normal = box.GetAxis(axisIndex) * axisSign;
            return true;
        }

        private static float Component(Vector3 v, int i) => i == 0 ? v.X : (i == 1 ? v.Y : v.Z);
    }
}
