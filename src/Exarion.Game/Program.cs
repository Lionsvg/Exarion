// ===========================================================================
//  Exarion Engine - Bac a sable : point d'entree
//
//  Lit la ligne de commande, lance l'application, et ecrit un rapport si
//  elle plante. Tout le reste vit dans SandboxApp et ses sous-systemes.
//
//  Options : --seconds N  --shot-at N  --screenshot fichier  --exec "commande"
//            --map nom  --tickrate N  --sensitivity N  --novsync  --shadows 0|1
//            --audio-dump fichier.wav  --audio-seconds N
// ===========================================================================

using System;
using System.IO;

namespace Exarion.Game
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            LaunchOptions launch = LaunchOptions.Parse(args);

            if (args.Length > 0)
                Console.WriteLine($"  Arguments : [{string.Join("] [", args)}]");
            if (launch.ScreenshotPath != null)
                Console.WriteLine($"  Capture automatique demandee : {launch.ScreenshotPath}");

            var app = new SandboxApp(launch);

            // Filet de securite : une exception levee dans un gestionnaire
            // d'evenement (clavier, souris) ne remonte pas forcement par le
            // try ci-dessous. On l'attrape aussi au niveau du domaine.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ue)
                    WriteCrashLog(ue, "exception non geree", app);
            };

            try
            {
                app.Run();
            }
            catch (Exception ex)
            {
                WriteCrashLog(ex, "boucle principale", app);
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// Ecrit un rapport de plantage a cote de l'executable.
        ///
        /// Sans ca, un crash se resume a "la fenetre s'est fermee" : aucune
        /// pile d'appels, aucun contexte, rien a analyser.
        /// </summary>
        private static void WriteCrashLog(Exception ex, string origin, SandboxApp app)
        {
            string report =
                "=== EXARION - RAPPORT DE PLANTAGE ===" + Environment.NewLine +
                $"Date      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                $"Origine   : {origin}" + Environment.NewLine +
                $"Temps sim : {app.SimTime:0.00} s" + Environment.NewLine +
                $"Caisses   : {app.CrateCount}" + Environment.NewLine +
                $"Console   : {(app.ConsoleOpen ? "ouverte" : "fermee")}" + Environment.NewLine +
                Environment.NewLine +
                ex.ToString() + Environment.NewLine;

            Console.Error.WriteLine(report);

            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "plantage.txt");
                File.AppendAllText(path, report + Environment.NewLine);
                Console.Error.WriteLine($"Rapport ecrit : {path}");
            }
            catch
            {
                // Si meme l'ecriture du rapport echoue, on ne peut plus rien.
            }
        }
    }
}
