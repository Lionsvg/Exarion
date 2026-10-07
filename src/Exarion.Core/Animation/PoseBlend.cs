// ===========================================================================
//  Exarion - Melange de deux poses de squelette
//
//  Une animation seule suffit a faire bouger un personnage. Elle ne suffit
//  pas a faire un jeu : entre l'arret, la marche et la course il faut des
//  etats INTERMEDIAIRES, et personne n'anime a la main les quinze vitesses
//  entre 1,4 m/s et 5 m/s. On melange deux cycles existants.
//
//  Ce fichier ne contient que le melange proprement dit - la primitive.
//  La synchronisation, elle, vit dans Animator, parce que c'est un probleme
//  de temps et non de geometrie.
// ===========================================================================

using System.Numerics;
using Exarion.Core.Assets;

namespace Exarion.Core.Animation
{
    public static class PoseBlend
    {
        /// <summary>
        /// Melange deux poses noeud par noeud. 0 rend <paramref name="a"/>,
        /// 1 rend <paramref name="b"/>.
        ///
        /// La destination peut etre l'une des sources : chaque noeud est lu
        /// puis ecrit avant de passer au suivant.
        /// </summary>
        public static void Mix(NodePose[] a, NodePose[] b, float weight, NodePose[] result)
        {
            // Les extremes sont frequents - a l'arret comme en pleine course,
            // le poids reste colle a 0 ou a 1 pendant des secondes. On evite
            // 26 slerps par image pour un resultat qu'on connait deja.
            if (weight <= 0f) { Copy(a, result); return; }
            if (weight >= 1f) { Copy(b, result); return; }

            int count = result.Length;
            if (a.Length < count) count = a.Length;
            if (b.Length < count) count = b.Length;

            for (int i = 0; i < count; i++)
            {
                result[i].Translation = Vector3.Lerp(a[i].Translation, b[i].Translation, weight);
                result[i].Scale = Vector3.Lerp(a[i].Scale, b[i].Scale, weight);

                // ------------------------------------------------------------
                // SLERP, PAS LERP. Meme raison qu'a l'echantillonnage d'un
                // canal : melanger deux quaternions en ligne droite fait
                // passer le resultat PAR L'INTERIEUR de la sphere. La rotation
                // accelere au milieu du trajet et le membre se detend d'un
                // coup - visible surtout sur une epaule, ou l'angle est grand.
                //
                // Le chemin le plus court (q et -q decrivent la meme rotation)
                // est deja traite par Quaternion.Slerp, qui inverse le second
                // quand leur produit scalaire est negatif. Verifie par mutation
                // lors de M5 : la negation manuelle qu'on avait ecrite etait du
                // code mort.
                // ------------------------------------------------------------
                result[i].Rotation = Quaternion.Normalize(
                    Quaternion.Slerp(a[i].Rotation, b[i].Rotation, weight));
            }
        }

        /// <summary>Recopie une pose. Sert de cas degenere au melange.</summary>
        public static void Copy(NodePose[] source, NodePose[] destination)
        {
            if (ReferenceEquals(source, destination))
                return;

            int count = destination.Length;
            if (source.Length < count) count = source.Length;

            for (int i = 0; i < count; i++)
                destination[i] = source[i];
        }
    }
}
