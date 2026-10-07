// ===========================================================================
//  Exarion - Sauvegarde des variables de console
//
//  Le moteur a une soixantaine de variables reglables a chaud. Sans ce
//  fichier, chaque reglage trouve a l'essai - une vitesse de deplacement,
//  un rayon d'occlusion, une intensite de halo - est perdu a la fermeture.
//  Or ces reglages sont precisement ceux qui ne se calculent pas : ils se
//  trouvent a la main, et il serait absurde de les refaire a chaque session.
//
//  ---------------------------------------------------------------------
//  ON N'ECRIT QUE CE QUI DIFFERE DU DEFAUT.
//
//  Un fichier qui liste les soixante variables est illisible : impossible
//  de voir d'un coup d'oeil ce qu'on a change. Pire, il FIGE les valeurs
//  par defaut : une amelioration du moteur n'atteindrait jamais quelqu'un
//  qui a deja un fichier. En n'ecrivant que les ecarts, les nouveaux
//  defauts se propagent tout seuls.
//  ---------------------------------------------------------------------
//
//  Format volontairement trivial - un nom, une valeur, par ligne - pour
//  rester lisible et modifiable a la main dans un editeur de texte.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Exarion.Core.Console
{
    public static class ConsoleConfig
    {
        /// <summary>
        /// Rend le contenu du fichier de configuration.
        ///
        /// La culture INVARIANTE est imperative. Sur une machine francaise,
        /// 15.24 s'ecrirait "15,24" - et se relirait comme 1524, ou pas du
        /// tout. Le fichier doit pouvoir voyager d'un poste a l'autre.
        /// </summary>
        public static string Save(ConsoleSystem console)
        {
            var builder = new StringBuilder();

            builder.AppendLine("# Exarion - reglages de console");
            builder.AppendLine("# Seules les valeurs MODIFIEES sont ecrites.");
            builder.AppendLine("# Modifiable a la main : un nom, une valeur, par ligne.");
            builder.AppendLine();

            var changed = new List<ConVar>();

            foreach (ConVar variable in console.Variables)
            {
                // Comparaison exacte assumee : une variable qu'on a reglee
                // puis remise a sa valeur d'origine n'a pas a etre ecrite.
                if (variable.Value != variable.Default)
                    changed.Add(variable);
            }

            changed.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            foreach (ConVar variable in changed)
                builder.AppendLine($"{variable.Name} {variable.Value.ToString("0.######", CultureInfo.InvariantCulture)}");

            if (changed.Count == 0)
                builder.AppendLine("# (aucun reglage ne differe du defaut)");

            return builder.ToString();
        }

        /// <summary>
        /// Applique un contenu de configuration.
        ///
        /// Une ligne inconnue ou mal formee est IGNOREE, jamais fatale : un
        /// fichier ecrit par une version anterieure du moteur doit continuer
        /// a fonctionner, meme si une variable a disparu entre-temps.
        /// </summary>
        /// <returns>Nombre de variables effectivement appliquees.</returns>
        public static int Apply(ConsoleSystem console, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return 0;

            int applied = 0;

            foreach (string raw in content.Split('\n'))
            {
                string line = raw.Trim();

                if (line.Length == 0 || line[0] == '#')
                    continue;

                int space = line.IndexOf(' ');
                if (space <= 0)
                    continue;

                string name = line[..space].Trim();
                string value = line[(space + 1)..].Trim();

                ConVar? variable = console.Find(name);
                if (variable is null)
                    continue;

                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    continue;

                // ConVar borne lui-meme : un fichier bricole a la main ne
                // peut pas sortir le moteur de ses limites.
                variable.Value = parsed;
                applied++;
            }

            return applied;
        }

        public static void SaveTo(ConsoleSystem console, string path)
            => File.WriteAllText(path, Save(console));

        /// <summary>Charge si le fichier existe. Rend -1 s'il n'existe pas.</summary>
        public static int LoadFrom(ConsoleSystem console, string path)
            => File.Exists(path) ? Apply(console, File.ReadAllText(path)) : -1;
    }
}
