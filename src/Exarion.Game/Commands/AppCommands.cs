// ===========================================================================
//  Console : les etats de l'application et les reglages sauvegardes (2.4)
// ===========================================================================

using System;
using System.IO;
using Exarion.Core.Console;

namespace Exarion.Game.Commands
{
    internal static class AppCommands
    {
        public const string DefaultConfigName = "exarion.cfg";

        /// <summary>
        /// « menu », « play », « editor ».
        ///
        /// Ces commandes existent d'abord pour la VERIFICATION : sans elles,
        /// l'editeur ne serait atteignable qu'au clavier, donc impossible a
        /// capturer en mode headless - donc impossible a verifier sans un
        /// humain devant l'ecran.
        /// </summary>
        /// <param name="editorReady">Vrai si l'editeur a bien pu s'ouvrir (il faut une carte).</param>
        public static void RegisterStates(ConsoleSystem c, Action enterMenu, Action enterPlay,
                                          Action enterEditor, Func<bool> editorReady)
        {
            c.Register("menu", _ => { enterMenu(); c.Print("  menu"); }, "Revient au menu");
            c.Register("play", _ => { enterPlay(); c.Print("  partie"); }, "Reprend la partie");

            c.Register("editor", _ =>
            {
                enterEditor();
                c.Print(editorReady() ? "  editeur" : "  aucune carte a editer");
            }, "Passe dans l'editeur de niveau");
        }

        /// <summary>« save » et « load » : les reglages MODIFIES, dans un .cfg.</summary>
        public static void RegisterConfig(ConsoleSystem c)
        {
            c.Register("save", args =>
            {
                string path = ConfigPath(args);

                try
                {
                    ConsoleConfig.SaveTo(c, path);
                    c.Print($"  reglages ecrits dans {Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    c.Print($"  ecriture impossible : {ex.Message}");
                }
            }, "save [fichier] - ecrit les reglages modifies");

            c.Register("load", args =>
            {
                string path = ConfigPath(args);

                try
                {
                    int applied = ConsoleConfig.LoadFrom(c, path);

                    c.Print(applied < 0
                        ? $"  {Path.GetFileName(path)} introuvable"
                        : $"  {applied} reglage(s) appliques depuis {Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    c.Print($"  lecture impossible : {ex.Message}");
                }
            }, "load [fichier] - relit les reglages");
        }

        /// <summary>
        /// Chargement automatique du .cfg par defaut.
        ///
        /// ⚠️ APRES toutes les inscriptions - une valeur ne s'applique qu'a une
        /// ConVar qui existe - et AVANT les --exec, pour qu'une commande passee
        /// en ligne de commande puisse toujours avoir le dernier mot.
        /// </summary>
        public static void LoadDefaultConfig(ConsoleSystem c)
        {
            try
            {
                int applied = ConsoleConfig.LoadFrom(c, ConfigPath(Array.Empty<string>()));

                if (applied > 0)
                    Console.WriteLine($"  Reglages : {applied} valeur(s) relues depuis {DefaultConfigName}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Reglages : lecture impossible ({ex.Message})");
            }
        }

        /// <summary>
        /// Chemin du fichier de reglages, a cote de l'executable.
        ///
        /// Un vrai produit le mettrait dans le profil utilisateur. Ici, a
        /// cote du binaire : c'est previsible, ca se trouve, et le dossier
        /// est deja ignore par git - un fichier de reglages personnels n'a
        /// rien a faire dans le depot.
        /// </summary>
        public static string ConfigPath(string[] args)
        {
            string name = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
                ? args[0]
                : DefaultConfigName;

            if (!name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
                name += ".cfg";

            return Path.Combine(AppContext.BaseDirectory, name);
        }
    }
}
