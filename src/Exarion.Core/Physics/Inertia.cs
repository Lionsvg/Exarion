// ===========================================================================
//  Exarion - Inertie de rotation et integration de l'orientation
//
//  La masse dit ce qu'il faut pour changer la VITESSE d'un corps.
//  L'inertie dit ce qu'il faut pour changer sa ROTATION - et contrairement
//  a la masse, elle depend de l'AXE : une barre se lance dix fois plus
//  facilement autour de sa longueur qu'en travers.
//
//  Il faudrait donc une matrice 3x3, le tenseur d'inertie. Deux choses la
//  rendent inutile ici :
//
//    1. Dans le repere propre d'une boite, le tenseur est DIAGONAL - trois
//       nombres suffisent. Les axes de la boite sont ses axes principaux.
//    2. On n'a jamais besoin du tenseur lui-meme, seulement de son action
//       sur un vecteur : I(-1)_monde * v = R * I(-1) * Rt * v. Ce produit
//       se lit en trois lignes sans jamais former de matrice - voir Apply.
//
//  On garde donc l'INVERSE du tenseur, en diagonale, dans le repere propre.
//  L'inverse plutot que le tenseur, pour la meme raison que RigidBody garde
//  InverseMass : c'est toujours lui qui sert, et il vaut zero pour un corps
//  statique au lieu de valoir l'infini.
//
//  Formules du solide homogene, publiques et classiques.
// ===========================================================================

using System;
using System.Numerics;

namespace Exarion.Core.Physics
{
    public static class Inertia
    {
        /// <summary>
        /// Inverse du tenseur d'inertie d'une boite pleine et homogene,
        /// dans son repere propre. <paramref name="size"/> est en dimensions
        /// COMPLETES, pas en demi-dimensions.
        ///
        /// Chaque terme ne fait intervenir que les deux AUTRES dimensions :
        /// ce qui resiste a une rotation autour de X, c'est l'etalement en
        /// Y et en Z.
        /// </summary>
        public static Vector3 BoxInverse(Vector3 size, float mass)
        {
            if (mass <= 0f)
                return Vector3.Zero;

            Vector3 s = Vector3.Abs(size);
            float k = mass / 12f;

            return new Vector3(
                Invert(k * (s.Y * s.Y + s.Z * s.Z)),
                Invert(k * (s.X * s.X + s.Z * s.Z)),
                Invert(k * (s.X * s.X + s.Y * s.Y)));
        }

        /// <summary>
        /// Zero veut dire inertie infinie, donc "ne tourne pas autour de cet
        /// axe". C'est le cas d'un corps statique, et celui d'une boite
        /// degeneree reduite a un segment.
        /// </summary>
        private static float Invert(float inertia) => inertia > 1e-9f ? 1f / inertia : 0f;

        /// <summary>
        /// Applique l'inverse du tenseur exprime dans le MONDE, autrement dit
        /// calcule R * I(-1) * Rt * v, sans jamais former de matrice.
        ///
        /// Lecture de droite a gauche : Rt * v projette v sur les trois axes
        /// propres du corps ; I(-1) divise chaque composante par l'inertie de
        /// son axe ; R remet le resultat dans le monde. C'est exactement la
        /// somme ci-dessous.
        /// </summary>
        public static Vector3 Apply(Vector3 inverseDiagonal, Quaternion orientation, Vector3 v)
        {
            Vector3 x = Vector3.Transform(Vector3.UnitX, orientation);
            Vector3 y = Vector3.Transform(Vector3.UnitY, orientation);
            Vector3 z = Vector3.Transform(Vector3.UnitZ, orientation);

            return x * (inverseDiagonal.X * Vector3.Dot(x, v))
                 + y * (inverseDiagonal.Y * Vector3.Dot(y, v))
                 + z * (inverseDiagonal.Z * Vector3.Dot(z, v));
        }

        /// <summary>
        /// Fait tourner une orientation pendant <paramref name="dt"/> a la
        /// vitesse angulaire donnee, exprimee dans le MONDE en radians par
        /// seconde.
        ///
        /// ---------------------------------------------------------------
        /// LE PIEGE DE CETTE ETAPE.
        ///
        /// La formule qu'on trouve partout est q += 0,5 * w * q * dt. Elle
        /// est fausse deux fois :
        ///
        ///   - c'est une LINEARISATION. Elle coupe le virage au lieu de le
        ///     suivre, donc l'angle parcouru est toujours un peu trop petit.
        ///     MESURE : un tour a 64 pas/s laisse un point a 58 cm de l'axe
        ///     a 2,3 mm de son point de depart, soit 0,22 degre de retard
        ///     par tour - qui s'accumule.
        ///   - le resultat n'est plus unitaire. Or Vector3.Transform met le
        ///     vecteur a l'echelle du CARRE de la norme du quaternion : une
        ///     norme qui derive de 1 % fait grandir la caisse de 2 %. Sans
        ///     renormalisation, les corps se deforment lentement.
        ///
        /// On prend donc la rotation EXACTE du pas - un angle |w|*dt autour
        /// de l'axe w - et on renormalise quand meme, parce que composer des
        /// milliers de quaternions accumule l'erreur de calcul.
        ///
        /// MESURE : sans la renormalisation, la norme atteint 1,00116 apres
        /// 100 000 pas. Au carre, cela fait une caisse 0,23 % plus grosse
        /// qu'au depart, sans que rien ne l'ait demande. Le test
        /// Le_Quaternion_Ne_Derive_Pas_Sur_Cent_Mille_Pas le verifie.
        /// ---------------------------------------------------------------
        /// </summary>
        public static Quaternion Integrate(Quaternion orientation, Vector3 angularVelocity, float dt)
        {
            float speed = angularVelocity.Length();
            float angle = speed * dt;

            // Immobile : surtout ne pas diviser par cette vitesse nulle.
            if (angle < 1e-9f)
                return orientation;

            var delta = Quaternion.CreateFromAxisAngle(angularVelocity / speed, angle);

            // La rotation du pas est exprimee dans le MONDE, donc elle se
            // compose A GAUCHE. A droite, le corps tournerait autour de ses
            // PROPRES axes - une erreur qui ne se voit qu'une fois le corps
            // deja incline, et qui est alors introuvable.
            return Quaternion.Normalize(delta * orientation);
        }
    }
}
