// ===========================================================================
//  Exarion - Ecriture et lecture binaire des paquets (3.1)
//
//  ---------------------------------------------------------------------
//  POURQUOI ECRIRE SA PROPRE SERIALISATION
//
//  Un paquet de jeu part soixante-quatre fois par seconde, vers chaque
//  joueur. C'est le seul endroit du moteur ou compter les OCTETS a un sens :
//  a 10 joueurs et 64 Hz, cent octets de trop font 64 ko/s de plus a
//  l'emission, par client.
//
//  JSON ou un serialiseur generique feraient trois a dix fois la taille, et
//  alloueraient a chaque paquet. Ici tout s'ecrit dans un tampon fourni par
//  l'appelant : zero allocation en regime etabli.
//  ---------------------------------------------------------------------
//
//  ⚠️⚠️ TOUT EST EN LITTLE-ENDIAN EXPLICITE. Ne PAS se reposer sur
//  BitConverter tel quel : il suit l'ordre de la machine, donc deux machines
//  d'architectures differentes ne se comprendraient pas - et le defaut ne se
//  verrait que le jour ou quelqu'un joue sur ARM. On ecrit octet par octet.
//
//  ⚠️ AUCUNE LECTURE NE PEUT DEBORDER. Un paquet arrive du RESEAU : il peut
//  etre tronque, corrompu, ou fabrique expres. Toute lecture hors limites
//  leve le drapeau Failed et rend zero, plutot que de lever une exception qui
//  tuerait le serveur - c'est la meme regle que les handlers de ConVar.
// ===========================================================================

using System;
using System.Numerics;

namespace Exarion.Core.Net
{
    /// <summary>Ecrit des valeurs dans un tampon d'octets.</summary>
    public struct NetWriter
    {
        private readonly byte[] _data;
        private int _position;

        public NetWriter(byte[] buffer)
        {
            _data = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _position = 0;
            Overflowed = false;
        }

        /// <summary>Octets ecrits jusqu'ici.</summary>
        public int Length => _position;

        /// <summary>
        /// Vrai si une ecriture n'a pas tenu.
        ///
        /// ⚠️ On ne leve PAS d'exception : un paquet trop gros est une erreur de
        /// conception a corriger, pas une raison de tuer la partie en cours. On
        /// le signale, et l'appelant decide.
        /// </summary>
        public bool Overflowed { get; private set; }

        private bool Room(int bytes)
        {
            if (_position + bytes <= _data.Length)
                return true;

            Overflowed = true;
            return false;
        }

        public void Byte(byte value)
        {
            if (!Room(1)) return;
            _data[_position++] = value;
        }

        public void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

        public void UShort(ushort value)
        {
            if (!Room(2)) return;
            _data[_position++] = (byte)(value & 0xFF);
            _data[_position++] = (byte)(value >> 8);
        }

        public void UInt(uint value)
        {
            if (!Room(4)) return;
            _data[_position++] = (byte)(value & 0xFF);
            _data[_position++] = (byte)((value >> 8) & 0xFF);
            _data[_position++] = (byte)((value >> 16) & 0xFF);
            _data[_position++] = (byte)((value >> 24) & 0xFF);
        }

        public void Int(int value) => UInt(unchecked((uint)value));

        public void Float(float value) => UInt(BitConverter.SingleToUInt32Bits(value));

        public void Vector3(Vector3 value)
        {
            Float(value.X);
            Float(value.Y);
            Float(value.Z);
        }

        /// <summary>
        /// Un angle sur DEUX octets au lieu de quatre.
        ///
        /// ⚠️ Ce n'est pas de l'avarice : une orientation part dans chaque
        /// instantane, pour chaque joueur. La precision obtenue est de 360/65536
        /// = 0,0055 degre, soit trois ordres de grandeur sous ce qu'un joueur
        /// peut percevoir. Garder 32 bits serait payer double pour rien.
        /// </summary>
        public void Angle(float radians)
        {
            // On ramene dans [0, 2pi[ AVANT de quantifier : un angle negatif
            // donnerait un entier negatif, donc un cast hors domaine.
            //
            // ⚠️ HONNETETE SUR CETTE LIGNE : aucun test ne la discrimine.
            // Verifie par mutation - en la retirant, les 26 tests reseau passent
            // toujours, y compris celui sur les angles negatifs. La raison est
            // que sur cette machine le cast flottant vers ushort d'une valeur
            // negative ENROULE par hasard sur la bonne valeur.
            //
            // On la garde quand meme, et pour une raison precise : le C# declare
            // le resultat d'une conversion flottant vers entier hors domaine
            // NON SPECIFIE en contexte unchecked. Ce qui marche ici n'est donc
            // pas garanti ailleurs, et un joueur oriente au hasard sur une autre
            // plateforme serait introuvable.
            float turns = radians / MathF.Tau;
            turns -= MathF.Floor(turns);

            UShort((ushort)(turns * 65536f));
        }

        /// <summary>Chaine courte, precedee de sa longueur sur un octet.</summary>
        public void ShortString(string? value)
        {
            value ??= "";

            // 255 octets maximum : les noms de joueurs et de cartes tiennent
            // largement dedans, et une longueur sur un octet evite un cas
            // d'erreur de plus.
            int count = Math.Min(value.Length, 255);

            Byte((byte)count);

            for (int i = 0; i < count; i++)
            {
                // ⚠️ ASCII seulement, et c'est VOLONTAIRE : un nom envoye par le
                // reseau ne doit pas pouvoir contenir n'importe quel octet. Tout
                // ce qui sort de l'ASCII imprimable devient '?'.
                char c = value[i];
                Byte(c is >= (char)32 and < (char)127 ? (byte)c : (byte)'?');
            }
        }
    }

    /// <summary>Relit ce que NetWriter a ecrit.</summary>
    public struct NetReader
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _position;

        public NetReader(byte[] buffer, int length)
        {
            _data = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _end = Math.Min(length, _data.Length);
            _position = 0;
            Failed = false;
        }

        /// <summary>
        /// Vrai si une lecture a depasse la fin du paquet.
        ///
        /// ⚠️⚠️ C'EST LE DRAPEAU QUI PROTEGE LE SERVEUR. Un paquet vient du
        /// reseau : il peut etre tronque par la route, ou fabrique expres pour
        /// faire lire n'importe quoi. Une fois leve, toutes les lectures rendent
        /// zero - donc un message tronque decode en valeurs neutres, et
        /// l'appelant le jette au lieu d'agir sur des donnees inventees.
        /// </summary>
        public bool Failed { get; private set; }

        public int Remaining => Math.Max(0, _end - _position);

        private bool Available(int bytes)
        {
            if (!Failed && _position + bytes <= _end)
                return true;

            Failed = true;
            return false;
        }

        public byte Byte() => Available(1) ? _data[_position++] : (byte)0;

        public bool Bool() => Byte() != 0;

        public ushort UShort()
        {
            if (!Available(2)) return 0;

            ushort value = (ushort)(_data[_position] | (_data[_position + 1] << 8));
            _position += 2;
            return value;
        }

        public uint UInt()
        {
            if (!Available(4)) return 0;

            uint value = (uint)(_data[_position]
                              | (_data[_position + 1] << 8)
                              | (_data[_position + 2] << 16)
                              | (_data[_position + 3] << 24));
            _position += 4;
            return value;
        }

        public int Int() => unchecked((int)UInt());

        public float Float()
        {
            float value = BitConverter.UInt32BitsToSingle(UInt());

            // ⚠️ UN NaN OU UN INFINI VENANT DU RESEAU EMPOISONNE LA SIMULATION.
            // Il se propage silencieusement dans les positions, la physique, et
            // finit par tout figer sans qu'aucune ligne ne le signale. On le
            // traite comme une lecture ratee : c'en est une.
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Failed = true;
                return 0f;
            }

            return value;
        }

        public Vector3 Vector3()
        {
            float x = Float(), y = Float(), z = Float();
            return new Vector3(x, y, z);
        }

        public float Angle() => UShort() / 65536f * MathF.Tau;

        public string ShortString()
        {
            int count = Byte();

            if (!Available(count))
                return "";

            var chars = new char[count];

            for (int i = 0; i < count; i++)
                chars[i] = (char)_data[_position + i];

            _position += count;
            return new string(chars);
        }
    }
}
