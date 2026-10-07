// ===========================================================================
//  Tests du decodeur PNG maison.
//
//  Strategie : on ENCODE une image connue, on la DECODE, et on verifie
//  qu'on retrouve exactement les memes pixels. Pas besoin de fichier de
//  reference binaire dans le depot, et l'aller-retour couvre les deux
//  moities du format.
//
//  Les cas de filtrage (Sub, Up, Average, Paeth) sont testes en fabriquant
//  a la main des flux qui les utilisent : notre encodeur, lui, n'ecrit que
//  le filtre 0.
// ===========================================================================

using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using Exarion.Core.Assets;

namespace Exarion.Tests
{
    public class PngTests
    {
        private static Image32 MakeTestImage(int w, int h)
        {
            var img = new Image32(w, h);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    img.SetPixel(x, y,
                        (byte)(x * 7 + y * 3),
                        (byte)(x * 13 % 256),
                        (byte)(y * 29 % 256),
                        (byte)(200 + (x + y) % 56));
                }
            }

            return img;
        }

        // --- Aller-retour ------------------------------------------------------

        [Test]
        public void Encoder_Puis_Decoder_Redonne_Les_Memes_Pixels()
        {
            var original = MakeTestImage(37, 23);   // dimensions volontairement non rondes

            byte[] encoded = Png.Encode(original);
            Image32 decoded = Png.Decode(encoded);

            ClassicAssert.AreEqual(original.Width, decoded.Width);
            ClassicAssert.AreEqual(original.Height, decoded.Height);
            CollectionAssert.AreEqual(original.Pixels, decoded.Pixels);
        }

        [Test]
        public void Une_Image_Dun_Seul_Pixel_Passe()
        {
            var img = new Image32(1, 1);
            img.SetPixel(0, 0, 12, 34, 56, 78);

            Image32 decoded = Png.Decode(Png.Encode(img));

            ClassicAssert.AreEqual((byte)12, decoded.GetPixel(0, 0).r);
            ClassicAssert.AreEqual((byte)78, decoded.GetPixel(0, 0).a);
        }

        [Test]
        public void Lalpha_Est_Preserve()
        {
            var img = new Image32(4, 4);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    img.SetPixel(x, y, 255, 255, 255, (byte)(x * 60));

            Image32 decoded = Png.Decode(Png.Encode(img));

            ClassicAssert.AreEqual((byte)0, decoded.GetPixel(0, 0).a);
            ClassicAssert.AreEqual((byte)180, decoded.GetPixel(3, 0).a);
        }

        [Test]
        public void Une_Grande_Image_Passe()
        {
            var img = MakeTestImage(512, 512);

            byte[] encoded = Png.Encode(img);
            Image32 decoded = Png.Decode(encoded);

            CollectionAssert.AreEqual(img.Pixels, decoded.Pixels);
            ClassicAssert.Less(encoded.Length, 512 * 512 * 4,
                "la compression doit au moins faire mieux que le brut");
        }

        // --- Entrees invalides ---------------------------------------------------

        [Test]
        public void Un_Fichier_Vide_Est_Refuse()
        {
            Assert.Throws<InvalidDataException>(() => Png.Decode(Array.Empty<byte>()));
        }

        [Test]
        public void Une_Mauvaise_Signature_Est_Refusee()
        {
            var bogus = new byte[64];
            Assert.Throws<InvalidDataException>(() => Png.Decode(bogus));
        }

        [Test]
        public void Un_Png_Tronque_Est_Refuse()
        {
            byte[] full = Png.Encode(MakeTestImage(16, 16));
            byte[] truncated = full[..(full.Length / 2)];

            Assert.Catch(() => Png.Decode(truncated), "un fichier coupe doit lever, pas produire n'importe quoi");
        }

        // --- Filtres : construits a la main -----------------------------------------

        /// <summary>Fabrique un PNG RVB 8 bits avec le filtre demande sur chaque ligne.</summary>
        private static byte[] BuildPngWithFilter(int width, int height, byte filter, byte[] rawRgb)
        {
            int stride = width * 3;
            var filtered = new byte[height * (1 + stride)];

            var previous = new byte[stride];

            for (int y = 0; y < height; y++)
            {
                int dst = y * (1 + stride);
                filtered[dst] = filter;

                for (int i = 0; i < stride; i++)
                {
                    int value = rawRgb[y * stride + i];
                    int a = i >= 3 ? rawRgb[y * stride + i - 3] : 0;
                    int b = previous[i];
                    int c = i >= 3 ? previous[i - 3] : 0;

                    int prediction = filter switch
                    {
                        1 => a,
                        2 => b,
                        3 => (a + b) / 2,
                        4 => Paeth(a, b, c),
                        _ => 0,
                    };

                    filtered[dst + 1 + i] = (byte)((value - prediction) & 0xFF);
                }

                Buffer.BlockCopy(rawRgb, y * stride, previous, 0, stride);
            }

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(filtered, 0, filtered.Length);

            using var output = new MemoryStream();
            output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            var header = new byte[13];
            WriteBe(header, 0, width);
            WriteBe(header, 4, height);
            header[8] = 8;   // 8 bits
            header[9] = 2;   // RVB
            WriteChunk(output, "IHDR", header);
            WriteChunk(output, "IDAT", compressed.ToArray());
            WriteChunk(output, "IEND", Array.Empty<byte>());

            return output.ToArray();
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }

        private static void WriteBe(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte)value;
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            WriteBe(len, 0, data.Length);
            s.Write(len);

            byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(t, 0, 4);
            s.Write(data, 0, data.Length);

            // CRC : notre decodeur ne le verifie pas, on peut mettre zero.
            s.Write(new byte[4]);
        }

        [TestCase((byte)0, TestName = "Filtre 0 - aucun")]
        [TestCase((byte)1, TestName = "Filtre 1 - Sub")]
        [TestCase((byte)2, TestName = "Filtre 2 - Up")]
        [TestCase((byte)3, TestName = "Filtre 3 - Average")]
        [TestCase((byte)4, TestName = "Filtre 4 - Paeth")]
        public void Chaque_Filtre_Est_Correctement_Annule(byte filter)
        {
            const int w = 9, h = 7;
            var raw = new byte[w * h * 3];
            for (int i = 0; i < raw.Length; i++)
                raw[i] = (byte)((i * 37 + 11) % 256);

            byte[] png = BuildPngWithFilter(w, h, filter, raw);
            Image32 decoded = Png.Decode(png);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var p = decoded.GetPixel(x, y);
                    int s = (y * w + x) * 3;

                    ClassicAssert.AreEqual(raw[s], p.r, $"R en ({x},{y}) avec le filtre {filter}");
                    ClassicAssert.AreEqual(raw[s + 1], p.g, $"V en ({x},{y}) avec le filtre {filter}");
                    ClassicAssert.AreEqual(raw[s + 2], p.b, $"B en ({x},{y}) avec le filtre {filter}");
                    ClassicAssert.AreEqual((byte)255, p.a, "un RVB doit devenir opaque");
                }
            }
        }
    }

    public class TextureGenTests
    {
        [Test]
        public void La_Grille_A_La_Bonne_Taille_Et_Est_Opaque()
        {
            Image32 grid = TextureGen.DevGrid(size: 128, cells: 8);

            ClassicAssert.AreEqual(128, grid.Width);
            ClassicAssert.AreEqual(128, grid.Height);

            for (int i = 3; i < grid.Pixels.Length; i += 4)
                ClassicAssert.AreEqual((byte)255, grid.Pixels[i], "aucun pixel ne doit etre transparent");
        }

        [Test]
        public void Les_Lignes_De_La_Grille_Sont_Plus_Sombres_Que_Le_Fond()
        {
            Image32 grid = TextureGen.DevGrid(size: 256, cells: 8);

            // Une case fait 32 px : (0,0) est sur une ligne, (16,16) au centre.
            var onLine = grid.GetPixel(0, 0);
            var inCell = grid.GetPixel(16, 16);

            ClassicAssert.Less(onLine.r, inCell.r, "la ligne doit trancher sur le fond");
        }

        [Test]
        public void La_Grille_Est_Reproductible()
        {
            Image32 a = TextureGen.DevGrid(size: 64, cells: 4, seed: 42);
            Image32 b = TextureGen.DevGrid(size: 64, cells: 4, seed: 42);

            CollectionAssert.AreEqual(a.Pixels, b.Pixels, "meme graine, meme texture");
        }

        [Test]
        public void Le_Damier_Alterne_Bien()
        {
            Image32 c = TextureGen.Checker(size: 64, cells: 8);   // cases de 8 px

            byte first = c.GetPixel(0, 0).r;
            byte next = c.GetPixel(8, 0).r;

            ClassicAssert.AreNotEqual(first, next, "deux cases voisines doivent differer");
            ClassicAssert.AreEqual(first, c.GetPixel(16, 0).r, "une case sur deux se repete");
        }

        [Test]
        public void Une_Texture_Generee_Survit_A_Un_Aller_Retour_Png()
        {
            Image32 grid = TextureGen.DevGrid(size: 64, cells: 4);

            Image32 decoded = Png.Decode(Png.Encode(grid));

            CollectionAssert.AreEqual(grid.Pixels, decoded.Pixels);
        }
    }
}
