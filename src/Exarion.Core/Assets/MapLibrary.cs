// ===========================================================================
//  MapLibrary - ou vivent les cartes, et comment un nom devient un fichier
//
//  Sorti du bac a sable le 07/10/2026. Ces quatre fonctions vivaient dans
//  Program.cs, ou rien ne pouvait les tester : or l'une d'elles decide de ce
//  qui a le droit de devenir un nom de fichier, et c'est exactement le genre
//  de code qu'on veut voir echouer en ligne de commande plutot que sur le
//  disque de quelqu'un.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Exarion.Core.Assets
{
    public static class MapLibrary
    {
        /// <summary>Nom du dossier des cartes, cherche en remontant depuis l'executable.</summary>
        public const string FolderName = "maps";

        /// <summary>Le dossier des cartes vu depuis l'executable.</summary>
        public static string FindFolder() => FindFolder(AppContext.BaseDirectory);

        /// <summary>
        /// Remonte depuis <paramref name="start"/> jusqu'a trouver un dossier
        /// « maps ».
        ///
        /// On remonte parce que l'executable vit dans bin/Release/net9.0 et le
        /// contenu a la racine du depot. Aucun dossier trouve : on rend celui
        /// qu'on creerait a cote du point de depart - jamais null, pour que
        /// l'appelant puisse au moins afficher ou il a cherche.
        /// </summary>
        public static string FindFolder(string start)
        {
            var dir = new DirectoryInfo(start);

            while (dir is not null)
            {
                string candidate = Path.Combine(dir.FullName, FolderName);
                if (Directory.Exists(candidate))
                    return candidate;

                dir = dir.Parent;
            }

            return Path.Combine(start, FolderName);
        }

        /// <summary>Chemin d'une carte : voir l'autre surcharge.</summary>
        public static string Resolve(string name) => Resolve(name, FindFolder());

        /// <summary>
        /// Accepte aussi bien « arene » que « arene.map » ou un chemin complet.
        ///
        /// ⚠️ Un nom qui contient un separateur est pris TEL QUEL, sans passer
        /// par le dossier des cartes : c'est ce qui permet d'ouvrir une carte
        /// n'importe ou. L'extension n'est ajoutee que si elle manque.
        /// </summary>
        public static string Resolve(string name, string folder)
        {
            ArgumentNullException.ThrowIfNull(name);

            string file = Path.GetExtension(name).Length > 0 ? name : name + MapFile.Extension;

            if (name.Contains(Path.DirectorySeparatorChar) || name.Contains('/'))
                return file;

            return Path.Combine(folder, file);
        }

        /// <summary>Noms des cartes du dossier des cartes. Voir l'autre surcharge.</summary>
        public static List<string> ListNames() => ListNames(FindFolder());

        /// <summary>
        /// Noms des cartes d'un dossier, sans extension, tries sans tenir compte
        /// de la casse. Jamais null : un dossier absent donne une liste vide.
        ///
        /// Le tri n'est pas cosmetique : sans lui, l'ordre est celui du systeme
        /// de fichiers, qui differe d'une machine a l'autre - et le menu
        /// « Choisir une carte » ne presenterait pas les memes entrees au meme
        /// rang.
        /// </summary>
        public static List<string> ListNames(string folder)
        {
            var names = new List<string>();

            if (!Directory.Exists(folder))
                return names;

            foreach (string file in Directory.GetFiles(folder, "*" + MapFile.Extension))
                names.Add(Path.GetFileNameWithoutExtension(file));

            names.Sort(StringComparer.OrdinalIgnoreCase);

            return names;
        }

        /// <summary>
        /// Un nom de carte devient un nom de FICHIER : il ne peut pas contenir
        /// n'importe quoi. « bac a sable » passe, « ../../etc » non.
        ///
        /// ⚠️ Tout ce qui n'est ni lettre, ni chiffre, ni tiret, ni souligne
        /// devient un souligne, et les soulignes des bords sont retires. C'est
        /// ce retrait qui transforme « ../../etc » en « etc » plutot qu'en
        /// « ______etc ». Un nom vide apres nettoyage devient « sans_nom » : un
        /// fichier « .map » sans nom serait invisible dans la plupart des
        /// explorateurs.
        /// </summary>
        public static string SafeFileName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            var sb = new StringBuilder(name.Length);

            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');

            string cleaned = sb.ToString().Trim('_');

            return cleaned.Length == 0 ? "sans_nom" : cleaned;
        }
    }
}
