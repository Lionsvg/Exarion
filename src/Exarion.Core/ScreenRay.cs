// ===========================================================================
//  Exarion - Rayon passant par un pixel de l'ecran
//
//  ---------------------------------------------------------------------
//  POURQUOI CETTE FONCTION EXISTE
//
//  Jusqu'ici tout se visait au CENTRE de l'ecran : le joueur tire droit
//  devant, l'editeur selectionnait droit devant. Le curseur etait capture et
//  invisible, donc « ou je regarde » et « ou je pointe » etaient le meme
//  point.
//
//  Un editeur ne peut pas fonctionner comme ca. On veut un curseur libre,
//  qui pointe un objet a l'ecran sans tourner la camera - donc il faut
//  savoir transformer une position de curseur en direction dans le monde.
//  ---------------------------------------------------------------------
//
//  Ecrit dans Core, en fonction PURE : aucun rendu, aucune camera, aucune
//  fenetre. C'est de la trigonometrie, et ca se verifie en ligne de commande.
//  Camera.RayThroughPixel n'est qu'une facade par-dessus.
// ===========================================================================

using System;
using System.Numerics;

namespace Exarion.Core
{
    public static class ScreenRay
    {
        /// <summary>
        /// Direction UNITAIRE du rayon qui part de l'oeil et traverse un pixel.
        /// </summary>
        /// <param name="pixelX">Abscisse du pixel, 0 a gauche.</param>
        /// <param name="pixelY">
        /// Ordonnee du pixel, 0 EN HAUT. ⚠️ C'est la convention des systemes
        /// de fenetrage, et l'inverse de celle d'OpenGL. Les positions de
        /// souris arrivent dans ce sens ; se tromper donne une selection qui
        /// marche au centre de l'ecran et se decale de plus en plus vers les
        /// bords - un defaut qu'on met longtemps a attribuer a un signe.
        /// </param>
        /// <param name="width">Largeur en pixels du tampon rendu.</param>
        /// <param name="height">Hauteur en pixels du tampon rendu.</param>
        /// <param name="forward">Direction de la camera, unitaire.</param>
        /// <param name="right">Axe lateral de la camera, unitaire.</param>
        /// <param name="up">Axe vertical de la camera, unitaire.</param>
        /// <param name="horizontalFovDegrees">Champ HORIZONTAL, comme la camera.</param>
        public static Vector3 Through(float pixelX, float pixelY,
                                      int width, int height,
                                      Vector3 forward, Vector3 right, Vector3 up,
                                      float horizontalFovDegrees)
        {
            // Une taille nulle donnerait une division par zero, et un rayon
            // NaN se propagerait dans toute la selection sans rien dire.
            if (width <= 0 || height <= 0)
                return forward;

            // Coordonnees normalisees, dans [-1, 1]. Le +0,5 vise le CENTRE du
            // pixel : sans lui l'image entiere est decalee d'un demi-pixel, ce
            // qui est invisible mais faux.
            float ndcX = 2f * ((pixelX + 0.5f) / width) - 1f;

            // ⚠️ L'INVERSION EST ICI, et une seule fois. Le pixel 0 est en
            // haut, alors que l'axe vertical de la camera monte.
            float ndcY = 1f - 2f * ((pixelY + 0.5f) / height);

            // Le champ est donne a l'HORIZONTALE (convention "Hor+" du moteur :
            // un ecran large voit plus sur les cotes, pas moins en hauteur). Le
            // demi-champ vertical s'en deduit par le format d'image - et surtout
            // PAS l'inverse, sinon la selection serait juste en 16:9 et fausse
            // partout ailleurs.
            float tanHalfH = MathF.Tan(horizontalFovDegrees * MathF.PI / 180f * 0.5f);
            float aspect = width / (float)height;
            float tanHalfV = tanHalfH / MathF.Max(0.0001f, aspect);

            Vector3 direction = forward
                              + right * (ndcX * tanHalfH)
                              + up * (ndcY * tanHalfV);

            float length = direction.Length();

            // Un repere degenere (axes nuls) ne doit pas rendre NaN.
            return length > 1e-6f ? direction / length : forward;
        }
    }
}
