// ===========================================================================
//  Exarion - Format de carte (.map)
//
//  Un format TEXTE, une entite par ligne, ecrit et relu a la main.
//
//  ---------------------------------------------------------------------
//  POURQUOI DU TEXTE ET PAS DU BINAIRE
//
//  Un format binaire serait plus compact et plus rapide a lire. A l'echelle
//  d'un decor - quelques centaines de boites - ces deux avantages ne valent
//  rien, et on paierait le prix fort : un fichier illisible sans outil,
//  indiffable dans un depot, et impossible a corriger a la main quand une
//  valeur est fausse. Le texte se lit, se compare et se repare. C'est aussi
//  ce qui permet d'ecrire une carte de test EN DUR dans un test unitaire.
//  ---------------------------------------------------------------------
//  UNE LIGNE MAL FORMEE EST FATALE. C'est un choix, et il est l'INVERSE de
//  celui de ConsoleConfig.
//
//  Un reglage de console qu'on ne sait pas relire, on l'ignore : on perd une
//  preference, le moteur tourne. Une BOITE qu'on ne sait pas relire, on ne
//  peut pas l'ignorer : il manquerait un mur, et le defaut se manifesterait
//  comme un joueur qui traverse le decor - trois heures plus tard, sans
//  aucun lien visible avec le fichier. Une carte se charge donc en entier
//  ou pas du tout, et l'erreur porte son NUMERO DE LIGNE.
//  ---------------------------------------------------------------------
//  LA CULTURE. Le piege deja paye dans ce projet, et il etait REEL sur cette
//  machine : sans CultureInfo.InvariantCulture, 9.81f s'ecrit "9,81" en
//  francais et se relit comme 981, ou pas du tout. Un fichier de carte est
//  fait pour voyager d'un poste a l'autre : la culture invariante est
//  imperative des deux cotes, ecriture ET lecture.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace Exarion.Core.Assets
{
    /// <summary>
    /// Erreur de lecture d'une carte. Porte le numero de ligne : sans lui,
    /// "carte invalide" sur un fichier de 300 lignes n'aide personne.
    /// </summary>
    public sealed class MapFormatException : Exception
    {
        public int Line { get; }

        public MapFormatException(int line, string message)
            : base(line > 0 ? $"ligne {line} : {message}" : message)
        {
            Line = line;
        }
    }

    public static class MapFile
    {
        public const string Magic = "exarion_map";

        /// <summary>
        /// Version ECRITE par ce moteur.
        ///
        /// Passee a 2 le 17/08 avec l'arrivee des entites. Le bump est
        /// necessaire dans ce sens-la : un fichier contenant des lignes
        /// « entity » serait REFUSE par un lecteur de version 1, qui rejette
        /// tout mot-cle inconnu. Le declarer version 1 serait donc mentir.
        ///
        /// Passee a 3 le 17/08 avec les FORMES. Meme raisonnement : une ligne
        /// « box ... sphere » a onze champs, et le lecteur de version 2 en
        /// exige exactement dix - il refuserait le fichier.
        /// </summary>
        public const int Version = 3;

        /// <summary>
        /// Plus ancienne version encore LUE.
        ///
        /// ⚠️ Sans cette borne basse, monter la version aurait rendu illisibles
        /// toutes les cartes existantes - y compris les deux du depot. Un
        /// moteur doit lire ses anciens fichiers ; c'est l'inverse qui n'a
        /// jamais ete promis.
        /// </summary>
        public const int MinimumVersion = 1;

        public const string Extension = ".map";

        // ------------------------------------------------------------------
        //  Ecriture
        // ------------------------------------------------------------------

        public static string Save(MapData map)
        {
            ArgumentNullException.ThrowIfNull(map);

            var b = new StringBuilder();

            b.Append(Magic).Append(' ').Append(Version.ToString(CultureInfo.InvariantCulture)).AppendLine();
            b.AppendLine("# Carte Exarion. Modifiable a la main.");
            b.AppendLine("# box    centre(x y z)  taille(x y z)  couleur(r v b, LINEAIRE)  [forme]");
            b.AppendLine("#        forme : bloc (defaut), sphere, cylindre, rampe, cone");
            b.AppendLine("# spawn  pieds(x y z)   lacet  tangage");
            b.AppendLine("# entity type  position(x y z)  lacet tangage  [cle=valeur ...]");
            b.AppendLine();

            b.Append("name ").AppendLine(SanitizeName(map.Name));
            b.Append("spawn ").Append(Vec(map.Spawn)).Append(' ')
             .Append(Num(map.SpawnYaw)).Append(' ').Append(Num(map.SpawnPitch)).AppendLine();
            b.AppendLine();

            if (map.Boxes.Count > 0)
            {
                b.Append("# ").Append(map.Boxes.Count.ToString(CultureInfo.InvariantCulture))
                 .AppendLine(" boites").AppendLine();

                foreach (MapBox box in map.Boxes)
                {
                    b.Append("box ").Append(Vec(box.Center))
                     .Append("  ").Append(Vec(box.Size))
                     .Append("  ").Append(Vec(box.Color));

                    // ⚠️ LE BLOC N'ECRIT RIEN. Ce n'est pas de l'economie de
                    // place : c'est ce qui garantit qu'une carte sans forme
                    // exotique se reenregistre OCTET POUR OCTET comme avant
                    // l'arrivee des formes. La preuve de 4.2 - bac a sable ->
                    // map_save -> --map -> map_save identiques - tomberait si
                    // chaque ligne gagnait un « bloc » de plus.
                    if (box.Shape != MapShape.Block)
                        b.Append("  ").Append(MapShapes.Name(box.Shape));

                    b.AppendLine();
                }

                b.AppendLine();
            }

            foreach (MapLight light in map.Lights)
            {
                switch (light.Kind)
                {
                    case MapLightKind.Directional:
                        b.Append("light_dir ").Append(Vec(light.Direction))
                         .Append("  ").Append(Vec(light.Color))
                         .Append("  ").Append(Num(light.Intensity)).AppendLine();
                        break;

                    case MapLightKind.Point:
                        b.Append("light_point ").Append(Vec(light.Position))
                         .Append("  ").Append(Vec(light.Color))
                         .Append("  ").Append(Num(light.Intensity))
                         .Append(' ').Append(Num(light.Range)).AppendLine();
                        break;

                    case MapLightKind.Spot:
                        b.Append("light_spot ").Append(Vec(light.Position))
                         .Append("  ").Append(Vec(light.Direction))
                         .Append("  ").Append(Vec(light.Color))
                         .Append("  ").Append(Num(light.Intensity))
                         .Append(' ').Append(Num(light.Range))
                         .Append(' ').Append(Num(light.InnerDegrees))
                         .Append(' ').Append(Num(light.OuterDegrees)).AppendLine();
                        break;
                }
            }

            if (map.Entities.Count > 0)
            {
                b.AppendLine();
                b.Append("# ").Append(map.Entities.Count.ToString(CultureInfo.InvariantCulture))
                 .AppendLine(" entites").AppendLine();

                foreach (MapEntity entity in map.Entities)
                {
                    b.Append("entity ").Append(entity.Type)
                     .Append("  ").Append(Vec(entity.Position))
                     .Append("  ").Append(Num(entity.Yaw))
                     .Append(' ').Append(Num(entity.Pitch));

                    // ⚠️ L'ordre des proprietes DOIT etre stable, sinon deux
                    // enregistrements de la meme carte donnent deux fichiers
                    // differents et l'aller-retour octet pour octet de 4.2
                    // tombe. C'est pour ca que Properties est un
                    // SortedDictionary et pas un Dictionary : l'ordre d'un
                    // Dictionary n'est PAS garanti, meme sans modification.
                    foreach (KeyValuePair<string, string> pair in entity.Properties)
                        b.Append("  ").Append(pair.Key).Append('=').Append(EscapeValue(pair.Value));

                    b.AppendLine();
                }
            }

            return b.ToString();
        }

        /// <summary>
        /// Rend un flottant SANS PERTE.
        ///
        /// La tentation est d'arrondir a deux ou trois decimales pour faire
        /// joli. Ce serait deplacer les murs a chaque aller-retour : une
        /// marche calculee a 2,2500002 deviendrait 2,25, puis le fichier
        /// relu ne decrirait plus tout a fait la carte enregistree. Un format
        /// qui ne rend pas exactement ce qu'on lui a donne n'est pas un
        /// format, c'est une approximation.
        ///
        /// Depuis .NET Core 3.0, le rendu par defaut d'un float est la plus
        /// COURTE chaine qui se relit a l'identique : on a donc les deux,
        /// l'exactitude et "60" plutot que "60.000000".
        /// </summary>
        private static string Num(float value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Vec(Vector3 v) => $"{Num(v.X)} {Num(v.Y)} {Num(v.Z)}";

        /// <summary>
        /// Rend une valeur de propriete sur un seul mot.
        ///
        /// ⚠️ LE FORMAT DECOUPE SUR LES ESPACES, et retire tout ce qui suit un
        /// « # ». Une valeur contenant l'un ou l'autre casserait la relecture
        /// de la ligne - pas seulement de cette propriete. On remplace donc, au
        /// lieu d'ecrire un fichier qu'on ne saura pas relire.
        ///
        /// C'est une LIMITE ASSUMEE du format, pas une omission : gerer les
        /// guillemets demanderait un vrai decoupeur, alors qu'aucune propriete
        /// utile aujourd'hui - un nom de cible, un nombre, un drapeau - n'a
        /// besoin d'espaces.
        /// </summary>
        private static string EscapeValue(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "_";

            var sb = new StringBuilder(value.Length);

            foreach (char c in value)
                sb.Append(char.IsWhiteSpace(c) || c == '#' || c == '=' ? '_' : c);

            return sb.ToString();
        }

        /// <summary>
        /// Le nom tient sur UNE ligne, sinon la suite du nom se relirait comme
        /// une commande. Cas tordu, mais il coute une ligne a rendre impossible.
        /// </summary>
        private static string SanitizeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "sans nom";

            return name.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        // ------------------------------------------------------------------
        //  Lecture
        // ------------------------------------------------------------------

        public static MapData Parse(string content)
        {
            ArgumentNullException.ThrowIfNull(content);

            var map = new MapData();
            string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            bool headerSeen = false;

            for (int i = 0; i < lines.Length; i++)
            {
                int lineNumber = i + 1;

                // On coupe le commentaire AVANT de decouper les mots : sinon
                // "box 0 0 0 1 1 1 1 1 1 # sol" ferait neuf champs plus trois.
                string line = lines[i];
                int hash = line.IndexOf('#');
                if (hash >= 0)
                    line = line.Substring(0, hash);

                line = line.Trim();
                if (line.Length == 0)
                    continue;

                string[] f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                if (!headerSeen)
                {
                    // L'en-tete doit etre la PREMIERE chose utile du fichier.
                    // Sans lui, on accepterait n'importe quel fichier texte et
                    // on rendrait une carte vide, ce qui se lit comme "la carte
                    // est vide" au lieu de "ce n'est pas une carte".
                    if (f.Length != 2 || !string.Equals(f[0], Magic, StringComparison.Ordinal))
                        throw new MapFormatException(lineNumber,
                            $"en-tete attendu \"{Magic} {Version}\"");

                    if (!int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
                        throw new MapFormatException(lineNumber, $"version illisible \"{f[1]}\"");

                    if (version < MinimumVersion || version > Version)
                        throw new MapFormatException(lineNumber,
                            $"version {version} non geree (ce moteur lit de la " +
                            $"{MinimumVersion} a la {Version})");

                    headerSeen = true;
                    continue;
                }

                switch (f[0])
                {
                    case "name":
                        // Le nom peut contenir des espaces : on reprend la ligne
                        // brute apres le mot-cle plutot que de recoller les mots.
                        map.Name = line.Substring(f[0].Length).Trim();
                        if (map.Name.Length == 0)
                            map.Name = "sans nom";
                        break;

                    case "spawn":
                        Expect(lineNumber, f, minimum: 4, maximum: 6, "spawn x y z [lacet] [tangage]");
                        map.Spawn = ReadVec(lineNumber, f, 1);
                        map.SpawnYaw = f.Length >= 5 ? ReadNum(lineNumber, f[4]) : 0f;
                        map.SpawnPitch = f.Length >= 6 ? ReadNum(lineNumber, f[5]) : 0f;
                        break;

                    case "entity":
                    {
                        // entity <type> x y z lacet tangage [cle=valeur ...]
                        Expect(lineNumber, f, minimum: 5, maximum: int.MaxValue,
                               "entity type x y z [lacet] [tangage] [cle=valeur ...]");

                        var entity = new MapEntity(f[1], ReadVec(lineNumber, f, 2));

                        int next = 5;

                        // Lacet et tangage sont OPTIONNELS, et se distinguent
                        // d'une propriete par l'absence de « = » : une carte
                        // ecrite a la main doit pouvoir s'arreter apres la
                        // position.
                        if (f.Length > next && !f[next].Contains('='))
                            entity.Yaw = ReadNum(lineNumber, f[next++]);

                        if (f.Length > next && !f[next].Contains('='))
                            entity.Pitch = ReadNum(lineNumber, f[next++]);

                        for (int k = next; k < f.Length; k++)
                        {
                            int equals = f[k].IndexOf('=');

                            // Un champ sans « = » a ce stade est une faute de
                            // frappe, pas une propriete vide : on refuse plutot
                            // que de l'avaler en silence. Un declencheur dont
                            // une propriete manque ne se declenche pas, et rien
                            // ne le dit.
                            if (equals <= 0)
                                throw new MapFormatException(lineNumber,
                                    $"propriete attendue sous la forme cle=valeur, recu \"{f[k]}\"");

                            entity.Set(f[k][..equals], f[k][(equals + 1)..]);
                        }

                        map.Entities.Add(entity);
                        break;
                    }

                    case "box":
                    {
                        Expect(lineNumber, f, minimum: 10, maximum: 11,
                               "box cx cy cz  sx sy sz  r v b  [forme]");

                        Vector3 size = ReadVec(lineNumber, f, 4);

                        // Une boite de taille nulle ou negative n'est pas un
                        // detail cosmetique : elle donne une AABB retournee,
                        // que le balayage traite comme un obstacle traversable
                        // par un cote et solide par l'autre. Introuvable a
                        // l'oeil, donc refusee ici.
                        if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
                            throw new MapFormatException(lineNumber,
                                $"taille de boite nulle ou negative ({Vec(size)})");

                        // La forme est OPTIONNELLE : son absence vaut « bloc »,
                        // ce qui est exactement le decor des versions 1 et 2.
                        //
                        // ⚠️ Un nom de forme inconnu est FATAL, comme le reste
                        // de ce fichier. Retomber sur le bloc en silence est
                        // tentant - « on perd juste un arrondi » - mais une
                        // rampe muee en cube bouche un passage, et on
                        // chercherait le defaut dans la geometrie et non dans
                        // une faute de frappe vieille de trois semaines.
                        var shape = MapShape.Block;

                        if (f.Length == 11 && !MapShapes.TryParse(f[10], out shape))
                            throw new MapFormatException(lineNumber,
                                $"forme inconnue \"{f[10]}\" (attendu : " +
                                string.Join(", ", Array.ConvertAll(MapShapes.All, MapShapes.Name)) + ")");

                        map.Boxes.Add(new MapBox(ReadVec(lineNumber, f, 1), size,
                                                 ReadVec(lineNumber, f, 7), shape));
                        break;
                    }

                    case "light_dir":
                        Expect(lineNumber, f, minimum: 8, maximum: 8,
                               "light_dir dx dy dz  r v b  intensite");
                        map.Lights.Add(MapLight.Directional(
                            ReadVec(lineNumber, f, 1),
                            ReadVec(lineNumber, f, 4),
                            ReadNum(lineNumber, f[7])));
                        break;

                    case "light_point":
                        Expect(lineNumber, f, minimum: 9, maximum: 9,
                               "light_point x y z  r v b  intensite portee");
                        map.Lights.Add(MapLight.Point(
                            ReadVec(lineNumber, f, 1),
                            ReadVec(lineNumber, f, 4),
                            ReadNum(lineNumber, f[7]),
                            ReadNum(lineNumber, f[8])));
                        break;

                    case "light_spot":
                        Expect(lineNumber, f, minimum: 14, maximum: 14,
                               "light_spot x y z  dx dy dz  r v b  intensite portee interieur exterieur");
                        map.Lights.Add(MapLight.Spot(
                            ReadVec(lineNumber, f, 1),
                            ReadVec(lineNumber, f, 4),
                            ReadVec(lineNumber, f, 7),
                            ReadNum(lineNumber, f[10]),
                            ReadNum(lineNumber, f[11]),
                            ReadNum(lineNumber, f[12]),
                            ReadNum(lineNumber, f[13])));
                        break;

                    default:
                        throw new MapFormatException(lineNumber, $"mot-cle inconnu \"{f[0]}\"");
                }
            }

            if (!headerSeen)
                throw new MapFormatException(0, $"fichier vide ou sans en-tete \"{Magic}\"");

            return map;
        }

        private static void Expect(int line, string[] fields, int minimum, int maximum, string usage)
        {
            if (fields.Length < minimum || fields.Length > maximum)
                throw new MapFormatException(line,
                    $"{fields.Length - 1} valeur(s) pour \"{fields[0]}\". Attendu : {usage}");
        }

        private static Vector3 ReadVec(int line, string[] fields, int start) =>
            new Vector3(ReadNum(line, fields[start]),
                        ReadNum(line, fields[start + 1]),
                        ReadNum(line, fields[start + 2]));

        private static float ReadNum(int line, string text)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                throw new MapFormatException(line, $"nombre illisible \"{text}\"");

            // NaN et infini passent les analyseurs et ne cassent qu'au rendu,
            // sous forme de geometrie disparue sans message.
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new MapFormatException(line, $"nombre invalide \"{text}\"");

            return value;
        }

        // ------------------------------------------------------------------
        //  Fichiers
        // ------------------------------------------------------------------

        public static void SaveTo(MapData map, string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(path, Save(map));
        }

        public static MapData LoadFrom(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            return Parse(File.ReadAllText(path));
        }
    }
}
