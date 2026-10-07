// ===========================================================================
//  Exarion - Textures procedurales
//
//  Un greybox n'a pas besoin de fichiers d'images : il a besoin qu'on
//  LISE L'ECHELLE. Une grille dont chaque case vaut un metre permet
//  d'evaluer une distance, une hauteur de saut ou une largeur de couloir
//  d'un coup d'oeil - c'est exactement ce que font les textures de
//  developpement de tous les moteurs.
//
//  Genere en memoire, donc aucun asset a charger, versionner ou distribuer.
// ===========================================================================

using System;
using Exarion.Core.Deterministic;

namespace Exarion.Core.Assets
{
    public static class TextureGen
    {
        /// <summary>
        /// Grille de developpement : fond uni, lignes fines a chaque case,
        /// lignes plus marquees tous les 4 carreaux, et un grain leger pour
        /// eviter l'aspect plastique.
        /// </summary>
        /// <param name="size">Cote de la texture, en pixels. Puissance de 2 conseillee.</param>
        /// <param name="cells">Nombre de cases par cote.</param>
        public static Image32 DevGrid(int size = 512, int cells = 8,
                                      float baseR = 0.52f, float baseG = 0.54f, float baseB = 0.58f,
                                      uint seed = 1337)
        {
            var image = new Image32(size, size);
            var rng = new Rng(seed);

            int cellPixels = Math.Max(1, size / Math.Max(1, cells));
            int thin = Math.Max(1, size / 256);
            int thick = Math.Max(2, size / 128);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = baseR, g = baseG, b = baseB;

                    int mx = x % cellPixels;
                    int my = y % cellPixels;
                    bool onThin = mx < thin || my < thin;

                    // Ligne renforcee tous les 4 carreaux : donne un repere
                    // a plus grande echelle sans surcharger.
                    int cx = x / cellPixels;
                    int cy = y / cellPixels;
                    bool onThick = (cx % 4 == 0 && mx < thick) || (cy % 4 == 0 && my < thick);

                    if (onThick)
                    {
                        r *= 0.55f; g *= 0.58f; b *= 0.66f;
                    }
                    else if (onThin)
                    {
                        r *= 0.78f; g *= 0.80f; b *= 0.85f;
                    }

                    // Grain : +/- 3 %. Casse l'uniformite sans se voir.
                    float grain = 0.97f + rng.NextFloat() * 0.06f;
                    r *= grain; g *= grain; b *= grain;

                    image.SetPixel(x, y, ToByte(r), ToByte(g), ToByte(b));
                }
            }

            return image;
        }

        /// <summary>Damier classique. Utile pour reperer un etirement d'UV.</summary>
        public static Image32 Checker(int size = 256, int cells = 8,
                                      byte lightValue = 220, byte darkValue = 90)
        {
            var image = new Image32(size, size);
            int cellPixels = Math.Max(1, size / Math.Max(1, cells));

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool light = ((x / cellPixels) + (y / cellPixels)) % 2 == 0;
                    byte v = light ? lightValue : darkValue;
                    image.SetPixel(x, y, v, v, v);
                }
            }

            return image;
        }

        /// <summary>Texture unie. Sert de repli quand aucune image n'est fournie.</summary>
        public static Image32 Solid(int size, byte r, byte g, byte b, byte a = 255)
        {
            var image = new Image32(size, size);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    image.SetPixel(x, y, r, g, b, a);

            return image;
        }

        private static byte ToByte(float v)
            => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
    }
}
