// ===========================================================================
//  Exarion - Conteneur GLB (glTF binaire)
//
//  Premiere brique de l'import de modeles. Elle ne comprend RIEN a la 3D :
//  elle ouvre l'emballage, rien de plus.
//
//  Un fichier .glb est d'une simplicite rare pour un format 3D :
//
//     [ en-tete 12 octets ]  magie "glTF", version, longueur totale
//     [ morceau JSON      ]  la scene entiere, decrite en texte
//     [ morceau BIN       ]  un seul bloc d'octets : sommets, indices,
//                            poids, images... tout est dedans
//
//  Le JSON ne contient aucune donnee lourde. Il decrit des VUES sur le bloc
//  binaire - decalage, longueur, type - un peu comme une table des matieres.
//  C'est ce qui rend le format lisible sans bibliotheque.
//
//  Pourquoi l'ecrire plutot que prendre une bibliotheque : glTF 2.0 est une
//  specification Khronos publique et stable, et le conteneur tient en une
//  page. La regle du projet est que le moteur soit a nous ; ici elle ne
//  coute presque rien.
//
//  System.Text.Json fait partie du runtime .NET : ce n'est pas une
//  dependance externe.
// ===========================================================================

using System;
using System.Buffers.Binary;

namespace Exarion.Core.Assets
{
    /// <summary>Le contenu brut d'un fichier .glb, emballage retire.</summary>
    public sealed class Glb
    {
        /// <summary>"glTF" en petit-boutiste.</summary>
        private const uint Magic = 0x46546C67;

        private const uint ChunkJson = 0x4E4F534A;   // "JSON"
        private const uint ChunkBinary = 0x004E4942; // "BIN\0"

        /// <summary>Description de la scene. Toute la structure est la.</summary>
        public string Json { get; }

        /// <summary>
        /// Le bloc de donnees. Vide si le fichier n'en a pas - c'est permis,
        /// la scene peut alors etre entierement decrite en JSON.
        /// </summary>
        public ReadOnlyMemory<byte> Binary { get; }

        public uint Version { get; }

        private Glb(uint version, string json, ReadOnlyMemory<byte> binary)
        {
            Version = version;
            Json = json;
            Binary = binary;
        }

        /// <summary>
        /// Ouvre un .glb. Leve une exception explicite plutot que de rendre
        /// des donnees a moitie lues : un modele mal charge se manifesterait
        /// bien plus loin, sous forme de triangles absurdes.
        /// </summary>
        public static Glb Parse(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 12)
                throw new InvalidDataException("Fichier trop court pour un GLB.");

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            if (magic != Magic)
                throw new InvalidDataException("Ce n'est pas un fichier GLB : signature absente.");

            uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
            if (version != 2)
                throw new InvalidDataException($"GLB version {version} : seule la version 2 est geree.");

            uint declared = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);

            // On fait confiance a la longueur declaree, mais pas aveuglement :
            // un fichier tronque en cours de telechargement la garde intacte.
            if (declared > bytes.Length)
                throw new InvalidDataException(
                    $"GLB tronque : {declared} octets annonces, {bytes.Length} presents.");

            string? json = null;
            ReadOnlyMemory<byte> binary = default;

            int position = 12;

            while (position + 8 <= declared)
            {
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[position..]);
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(position + 4)..]);

                position += 8;

                if (position + length > declared)
                    throw new InvalidDataException("GLB : un morceau deborde de la fin du fichier.");

                ReadOnlySpan<byte> data = bytes.Slice(position, (int)length);

                switch (type)
                {
                    case ChunkJson:
                        // Le remplissage du morceau JSON se fait avec des
                        // ESPACES, pas des zeros : on peut decoder tel quel.
                        json = System.Text.Encoding.UTF8.GetString(data).TrimEnd();
                        break;

                    case ChunkBinary:
                        binary = data.ToArray();
                        break;

                    // Un morceau inconnu doit etre IGNORE, pas rejete : la
                    // specification autorise les extensions a en ajouter.
                }

                position += (int)length;
            }

            if (json is null)
                throw new InvalidDataException("GLB sans morceau JSON : illisible.");

            return new Glb(version, json, binary);
        }

        public static Glb Load(string path) => Parse(File.ReadAllBytes(path));

        public override string ToString()
            => $"GLB v{Version} ({Json.Length} caracteres de JSON, {Binary.Length} octets de donnees)";
    }
}
