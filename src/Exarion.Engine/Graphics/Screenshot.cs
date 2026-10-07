// ===========================================================================
//  Exarion - Capture d'ecran
//
//  Extraite du bac a sable le 17/08, et decouverte par le JEU D'EXEMPLE :
//  celui-ci en avait besoin, et il n'y avait aucun moyen d'y acceder sans
//  recopier le code de Program.cs. C'est exactement le genre de chose qu'un
//  second client d'un moteur fait remonter, et qu'un seul client ne montre
//  jamais.
//
//  Elle utilise l'encodeur PNG maison. Elle sert au reglage visuel, a la
//  documentation, et surtout a verifier un rendu sans avoir a le decrire.
// ===========================================================================

using System;
using System.IO;
using Silk.NET.OpenGL;
using Exarion.Core.Assets;

namespace Exarion.Engine.Graphics
{
    public static class Screenshot
    {
        /// <summary>
        /// Enregistre le tampon d'affichage en PNG. Ne leve jamais : une
        /// capture ratee ne doit pas arreter un jeu qui tourne.
        /// </summary>
        /// <returns>Chemin complet du fichier, ou null en cas d'echec.</returns>
        public static unsafe string? Save(GL gl, int width, int height, string path)
        {
            try
            {
                if (width <= 0 || height <= 0)
                    return null;

                var pixels = new byte[width * height * 4];

                // ⚠️ Alignement a UN octet. Sans ca, une largeur non multiple de
                // quatre decale toutes les lignes et l'image part en diagonale.
                gl.PixelStore(PixelStoreParameter.PackAlignment, 1);

                fixed (byte* p = pixels)
                    gl.ReadPixels(0, 0, (uint)width, (uint)height,
                                  PixelFormat.Rgba, PixelType.UnsignedByte, p);

                // ⚠️ OpenGL a son origine EN BAS a gauche, le PNG en HAUT. On
                // recopie donc ligne par ligne a l'envers.
                var image = new Image32(width, height);
                int stride = width * 4;

                for (int y = 0; y < height; y++)
                    System.Buffer.BlockCopy(pixels, (height - 1 - y) * stride, image.Pixels, y * stride, stride);

                string full = Path.GetFullPath(path);
                string? folder = Path.GetDirectoryName(full);

                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);

                File.WriteAllBytes(full, Png.Encode(image));

                return full;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("  Capture impossible : " + ex.Message);
                return null;
            }
        }
    }
}
