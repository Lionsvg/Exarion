// ===========================================================================
//  Exarion - Lecture et ecriture de WAV
//
//  POURQUOI MAISON. Le conteneur RIFF tient en trente lignes : un en-tete,
//  un morceau de format, un morceau de donnees. Y ajouter une dependance
//  pour ca serait absurde, et le decodeur PNG a deja prouve que ce genre de
//  format se refait tres bien.
//
//  ---------------------------------------------------------------------
//  ⚠️ CE FICHIER EST AUSSI MON SEUL MOYEN DE VERIFIER LE SON.
//
//  Je n'ai pas d'oreilles. Une capture d'ecran m'a permis de verifier
//  l'eclairage, les ombres, la SSAO et le bloom ; le WAV joue exactement le
//  meme role pour l'audio. Un fichier se MESURE : amplitude, equilibre
//  gauche/droite selon la position de la source, decroissance avec la
//  distance, contenu spectral apres un filtre.
//
//  Ce qui ne se mesurera jamais, c'est le RESSENTI. Il faudra ecouter.
//  ---------------------------------------------------------------------
//
//  On ECRIT en entier 16 bits, le format que tout lit. On LIT le 16 bits et
//  le flottant 32 bits, les deux seules choses qui sortent d'un outil moderne.
// ===========================================================================

using System;
using System.Buffers.Binary;
using System.IO;

namespace Exarion.Core.Audio
{
    public static class Wav
    {
        private const ushort FormatPcm = 1;
        private const ushort FormatFloat = 3;

        /// <summary>
        /// Encode des echantillons ENTRELACES en WAV 16 bits.
        ///
        /// Entrelaces veut dire gauche, droite, gauche, droite - et non deux
        /// blocs separes. C'est la disposition qu'attendent les cartes son,
        /// donc autant ne jamais en changer.
        /// </summary>
        public static byte[] Encode(ReadOnlySpan<float> samples, int channels, int sampleRate)
        {
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

            int dataBytes = samples.Length * 2;
            var bytes = new byte[44 + dataBytes];
            Span<byte> span = bytes;

            Ascii(span[..4], "RIFF");
            BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)(36 + dataBytes));
            Ascii(span[8..12], "WAVE");

            Ascii(span[12..16], "fmt ");
            BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 16u);          // taille du morceau
            BinaryPrimitives.WriteUInt16LittleEndian(span[20..], FormatPcm);
            BinaryPrimitives.WriteUInt16LittleEndian(span[22..], (ushort)channels);
            BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)sampleRate);
            BinaryPrimitives.WriteUInt32LittleEndian(span[28..], (uint)(sampleRate * channels * 2));
            BinaryPrimitives.WriteUInt16LittleEndian(span[32..], (ushort)(channels * 2));
            BinaryPrimitives.WriteUInt16LittleEndian(span[34..], 16);           // bits par echantillon

            Ascii(span[36..40], "data");
            BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)dataBytes);

            for (int i = 0; i < samples.Length; i++)
                BinaryPrimitives.WriteInt16LittleEndian(span[(44 + i * 2)..], ToPcm16(samples[i]));

            return bytes;
        }

        /// <summary>
        /// Convertit un flottant en entier 16 bits signe.
        ///
        /// ⚠️ LE BORNAGE EST OBLIGATOIRE. Un melange de plusieurs sons depasse
        /// facilement 1,0 ; sans borne, la conversion DEBORDE et un son un peu
        /// fort se transforme en craquement violent - le plus grand positif
        /// devient le plus grand negatif d'un echantillon a l'autre.
        ///
        /// ⚠️ L'echelle N'EST PAS SYMETRIQUE : un entier 16 bits va de -32768 a
        /// +32767. On multiplie donc par 32767 et non par 32768, sinon un
        /// signal a exactement -1,0 sort correct mais +1,0 deborde d'une unite.
        /// </summary>
        private static short ToPcm16(float value)
        {
            if (value > 1f) value = 1f;
            else if (value < -1f) value = -1f;

            return (short)MathF.Round(value * 32767f);
        }

        /// <summary>
        /// Lit un WAV et rend des echantillons entrelaces, en flottants.
        ///
        /// On PARCOURT les morceaux au lieu de supposer que "data" suit "fmt ".
        /// De nombreux outils glissent un morceau "LIST" avec le nom du
        /// logiciel entre les deux ; un lecteur qui saute a l'octet 44 lit
        /// alors ce texte comme du son, et rend du bruit blanc.
        /// </summary>
        public static AudioClip Decode(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 12)
                throw new InvalidDataException("WAV tronque.");

            if (!Matches(bytes[..4], "RIFF") || !Matches(bytes[8..12], "WAVE"))
                throw new InvalidDataException("Ce n'est pas un fichier WAV.");

            int channels = 0, sampleRate = 0, bits = 0;
            ushort format = 0;
            ReadOnlySpan<byte> data = default;
            bool haveFormat = false;

            int offset = 12;

            while (offset + 8 <= bytes.Length)
            {
                ReadOnlySpan<byte> id = bytes.Slice(offset, 4);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);

                int body = offset + 8;

                // Une taille mensongere ne doit pas faire lire hors du tableau.
                if (size > (uint)(bytes.Length - body))
                    size = (uint)(bytes.Length - body);

                if (Matches(id, "fmt ") && size >= 16)
                {
                    format = BinaryPrimitives.ReadUInt16LittleEndian(bytes[body..]);
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(body + 2)..]);
                    sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[(body + 4)..]);
                    bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(body + 14)..]);
                    haveFormat = true;
                }
                else if (Matches(id, "data"))
                {
                    data = bytes.Slice(body, (int)size);
                }

                // Les morceaux sont alignes sur deux octets : un morceau de
                // taille impaire est suivi d'un octet de bourrage qui ne
                // compte pas dans sa taille annoncee.
                offset = body + (int)size + ((size & 1) != 0 ? 1 : 0);
            }

            if (!haveFormat || channels <= 0 || sampleRate <= 0)
                throw new InvalidDataException("WAV sans morceau de format exploitable.");

            float[] samples = format switch
            {
                FormatPcm when bits == 16 => FromPcm16(data),
                FormatFloat when bits == 32 => FromFloat32(data),
                _ => throw new InvalidDataException(
                        $"WAV non gere : format {format}, {bits} bits. Attendu : PCM 16 ou flottant 32."),
            };

            return new AudioClip(samples, channels, sampleRate);
        }

        private static float[] FromPcm16(ReadOnlySpan<byte> data)
        {
            var samples = new float[data.Length / 2];

            for (int i = 0; i < samples.Length; i++)
            {
                short raw = BinaryPrimitives.ReadInt16LittleEndian(data[(i * 2)..]);

                // On divise par 32768 a la lecture et on multiplie par 32767 a
                // l'ecriture. Ce n'est PAS une etourderie : a la lecture il
                // faut que -32768 rende exactement -1,0 sans deborder, a
                // l'ecriture il faut que +1,0 tienne dans +32767.
                samples[i] = raw / 32768f;
            }

            return samples;
        }

        private static float[] FromFloat32(ReadOnlySpan<byte> data)
        {
            var samples = new float[data.Length / 4];

            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(data[(i * 4)..]));

            return samples;
        }

        private static void Ascii(Span<byte> destination, string text)
        {
            for (int i = 0; i < text.Length && i < destination.Length; i++)
                destination[i] = (byte)text[i];
        }

        private static bool Matches(ReadOnlySpan<byte> bytes, string text)
        {
            if (bytes.Length < text.Length)
                return false;

            for (int i = 0; i < text.Length; i++)
                if (bytes[i] != (byte)text[i])
                    return false;

            return true;
        }
    }
}
