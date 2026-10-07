// ===========================================================================
//  Exarion - Lecture et ecriture de PNG
//
//  POURQUOI MAISON : ImageSharp est sous licence Six Labors Split, pas
//  librement commerciale. StbImageSharp serait MIT, mais .NET embarque deja
//  ZLibStream - c'est-a-dire la seule partie vraiment difficile du format.
//  Le reste n'est que du decoupage de blocs et du defiltrage de lignes.
//
//  Zero dependance, et testable : on encode puis on decode, et on verifie
//  qu'on retrouve les memes pixels.
//
//  PORTEE : profondeur 8 bits, types de couleur 0 (gris), 2 (RVB),
//  4 (gris+alpha), 6 (RVBA). Pas d'entrelacement Adam7 - rarissime, et
//  ca doublerait la complexite pour rien.
//
//  Format, d'apres la specification PNG (norme W3C, publique) :
//    signature 8 octets
//    puis des blocs : longueur(4) type(4) donnees(n) crc(4), gros-boutiste
//    IHDR  dimensions et format
//    IDAT  lignes filtrees, compressees en zlib
//    IEND  fin
// ===========================================================================

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Exarion.Core.Assets
{
    /// <summary>Image en RVBA 8 bits par canal, ligne du haut en premier.</summary>
    public sealed class Image32
    {
        public readonly int Width;
        public readonly int Height;
        /// <summary>4 octets par pixel : R, V, B, A.</summary>
        public readonly byte[] Pixels;

        public Image32(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Dimensions invalides.");

            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }

        public Image32(int width, int height, byte[] pixels)
        {
            if (pixels.Length != width * height * 4)
                throw new ArgumentException("Taille de tampon incoherente.");

            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public void SetPixel(int x, int y, byte r, byte g, byte b, byte a = 255)
        {
            int i = (y * Width + x) * 4;
            Pixels[i] = r;
            Pixels[i + 1] = g;
            Pixels[i + 2] = b;
            Pixels[i + 3] = a;
        }

        public (byte r, byte g, byte b, byte a) GetPixel(int x, int y)
        {
            int i = (y * Width + x) * 4;
            return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
        }
    }

    public static class Png
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        // ===================================================================
        //  DECODAGE
        // ===================================================================

        public static Image32 Decode(byte[] data)
        {
            if (data == null || data.Length < 8)
                throw new InvalidDataException("Fichier trop court pour etre un PNG.");

            for (int i = 0; i < Signature.Length; i++)
            {
                if (data[i] != Signature[i])
                    throw new InvalidDataException("Signature PNG absente.");
            }

            int width = 0, height = 0, bitDepth = 0, colorType = 0;
            bool headerSeen = false;
            using var idat = new MemoryStream();

            int offset = 8;
            while (offset + 8 <= data.Length)
            {
                int length = ReadInt32(data, offset);
                string type = System.Text.Encoding.ASCII.GetString(data, offset + 4, 4);
                int dataStart = offset + 8;

                if (length < 0 || dataStart + length + 4 > data.Length)
                    throw new InvalidDataException($"Bloc '{type}' tronque.");

                switch (type)
                {
                    case "IHDR":
                        width = ReadInt32(data, dataStart);
                        height = ReadInt32(data, dataStart + 4);
                        bitDepth = data[dataStart + 8];
                        colorType = data[dataStart + 9];

                        if (data[dataStart + 12] != 0)
                            throw new NotSupportedException("PNG entrelace (Adam7) non pris en charge.");

                        headerSeen = true;
                        break;

                    case "IDAT":
                        idat.Write(data, dataStart, length);
                        break;

                    case "IEND":
                        offset = data.Length;   // on arrete la
                        break;
                }

                offset = dataStart + length + 4;   // + CRC
            }

            if (!headerSeen)
                throw new InvalidDataException("Bloc IHDR manquant.");

            if (bitDepth != 8)
                throw new NotSupportedException($"Profondeur {bitDepth} bits non prise en charge (8 attendu).");

            int channels = colorType switch
            {
                0 => 1,   // gris
                2 => 3,   // RVB
                4 => 2,   // gris + alpha
                6 => 4,   // RVBA
                _ => throw new NotSupportedException($"Type de couleur {colorType} non pris en charge."),
            };

            idat.Position = 0;
            byte[] raw = Inflate(idat, height * (1 + width * channels));

            return Unfilter(raw, width, height, channels);
        }

        /// <summary>Decompresse le flux zlib des blocs IDAT.</summary>
        private static byte[] Inflate(Stream compressed, int expectedSize)
        {
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var output = new MemoryStream(Math.Max(expectedSize, 1024));

            zlib.CopyTo(output);
            return output.ToArray();
        }

        /// <summary>
        /// Annule les filtres ligne par ligne et convertit en RVBA.
        ///
        /// Chaque ligne commence par un octet de filtre. Les cinq filtres
        /// predisent chaque octet a partir du voisin de gauche (a), du
        /// dessus (b) et de la diagonale haut-gauche (c).
        /// </summary>
        private static Image32 Unfilter(byte[] raw, int width, int height, int channels)
        {
            int stride = width * channels;
            int expected = height * (1 + stride);

            if (raw.Length < expected)
                throw new InvalidDataException("Donnees d'image incompletes.");

            var image = new Image32(width, height);
            var previous = new byte[stride];
            var current = new byte[stride];

            int src = 0;
            for (int y = 0; y < height; y++)
            {
                byte filter = raw[src++];
                Buffer.BlockCopy(raw, src, current, 0, stride);
                src += stride;

                for (int i = 0; i < stride; i++)
                {
                    int a = i >= channels ? current[i - channels] : 0;
                    int b = previous[i];
                    int c = i >= channels ? previous[i - channels] : 0;

                    int prediction = filter switch
                    {
                        0 => 0,                       // aucun
                        1 => a,                       // gauche
                        2 => b,                       // dessus
                        3 => (a + b) / 2,             // moyenne
                        4 => Paeth(a, b, c),          // Paeth
                        _ => throw new InvalidDataException($"Filtre inconnu : {filter}"),
                    };

                    current[i] = (byte)((current[i] + prediction) & 0xFF);
                }

                WriteRow(image, y, current, width, channels);

                (previous, current) = (current, previous);
            }

            return image;
        }

        /// <summary>
        /// Predicteur de Paeth : choisit celui des trois voisins dont la
        /// valeur est la plus proche de leur combinaison lineaire.
        /// </summary>
        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);

            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }

        private static void WriteRow(Image32 image, int y, byte[] row, int width, int channels)
        {
            for (int x = 0; x < width; x++)
            {
                int s = x * channels;
                byte r, g, b, a;

                switch (channels)
                {
                    case 1:
                        r = g = b = row[s];
                        a = 255;
                        break;
                    case 2:
                        r = g = b = row[s];
                        a = row[s + 1];
                        break;
                    case 3:
                        r = row[s]; g = row[s + 1]; b = row[s + 2];
                        a = 255;
                        break;
                    default:
                        r = row[s]; g = row[s + 1]; b = row[s + 2]; a = row[s + 3];
                        break;
                }

                image.SetPixel(x, y, r, g, b, a);
            }
        }

        // ===================================================================
        //  ENCODAGE
        //
        //  Sert aux tests (aller-retour) et aux captures d'ecran. On ecrit
        //  toujours en RVBA sans filtre : plus gros, mais trivialement juste.
        // ===================================================================

        public static byte[] Encode(Image32 image)
        {
            using var output = new MemoryStream();
            output.Write(Signature, 0, Signature.Length);

            // IHDR
            var header = new byte[13];
            WriteInt32(header, 0, image.Width);
            WriteInt32(header, 4, image.Height);
            header[8] = 8;    // 8 bits par canal
            header[9] = 6;    // RVBA
            header[10] = 0;   // compression deflate
            header[11] = 0;   // filtrage standard
            header[12] = 0;   // pas d'entrelacement
            WriteChunk(output, "IHDR", header);

            // IDAT : chaque ligne precedee de son octet de filtre (0 = aucun)
            int stride = image.Width * 4;
            var filtered = new byte[image.Height * (1 + stride)];

            for (int y = 0; y < image.Height; y++)
            {
                int dst = y * (1 + stride);
                filtered[dst] = 0;
                Buffer.BlockCopy(image.Pixels, y * stride, filtered, dst + 1, stride);
            }

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(filtered, 0, filtered.Length);

            WriteChunk(output, "IDAT", compressed.ToArray());
            WriteChunk(output, "IEND", Array.Empty<byte>());

            return output.ToArray();
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            output.Write(length);

            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            output.Write(typeBytes, 0, 4);
            output.Write(data, 0, data.Length);

            // Le CRC couvre le type ET les donnees.
            uint crc = Crc32(typeBytes, data);
            Span<byte> crcBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
            output.Write(crcBytes);
        }

        private static uint[]? _crcTable;

        private static uint Crc32(byte[] a, byte[] b)
        {
            if (_crcTable == null)
            {
                _crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;

                    _crcTable[n] = c;
                }
            }

            uint crc = 0xFFFFFFFFu;

            foreach (byte v in a)
                crc = _crcTable[(crc ^ v) & 0xFF] ^ (crc >> 8);

            foreach (byte v in b)
                crc = _crcTable[(crc ^ v) & 0xFF] ^ (crc >> 8);

            return crc ^ 0xFFFFFFFFu;
        }

        private static int ReadInt32(byte[] data, int offset)
            => BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));

        private static void WriteInt32(byte[] data, int offset, int value)
            => BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset, 4), value);
    }
}
