// ===========================================================================
//  La ligne de commande du bac a sable
//
//  Lue UNE fois, avant que la fenetre n'existe. Tout ce qui sert a piloter le
//  moteur sans yeux humains passe par ici : capture a un instant donne,
//  commandes de console jouees au demarrage, carte de depart, enregistrement
//  du son.
// ===========================================================================

using System.Collections.Generic;
using System.Globalization;
using Exarion.Engine;

namespace Exarion.Game
{
    internal sealed class LaunchOptions
    {
        /// <summary>Reglages de la fenetre et de la boucle, passes tels quels au moteur.</summary>
        public EngineOptions Engine { get; } =
            new() { Title = "Exarion Engine - bac a sable", Width = 1280, Height = 720 };

        /// <summary>
        /// Sensibilite de depart. Elle devient aussi le DEFAUT de la ConVar
        /// « sensitivity » : c'est voulu, une valeur passee au lancement doit
        /// pouvoir se remettre par « reset ».
        /// </summary>
        public float MouseSensitivity { get; private set; } = 0.0022f;

        /// <summary>Intensite des ombres au depart, et defaut de « shadow_strength ».</summary>
        public float ShadowStrength { get; private set; } = 1f;

        /// <summary>Capture automatique, ou null.</summary>
        public string? ScreenshotPath { get; private set; }

        /// <summary>
        /// Instant de la capture, en secondes de SIMULATION.
        ///
        /// Il valait 1 s en dur, et c'etait trop tot pour tout ce qui BOUGE :
        /// une pile lachee est encore en vol, une explosion n'a pas fini de
        /// retomber. On capturait donc l'etat transitoire au lieu du resultat.
        /// </summary>
        public float ScreenshotTime { get; private set; } = 1f;

        /// <summary>Carte demandee par --map, vide pour le bac a sable integre.</summary>
        public string StartupMap { get; private set; } = "";

        /// <summary>
        /// Enregistrement de ce que la carte son recoit, en WAV, ou null.
        ///
        /// C'est la capture d'ecran du son : sans elle, un defaut audio ne se
        /// decouvre qu'a l'oreille, donc jamais pendant le developpement.
        /// </summary>
        public string? AudioDumpPath { get; private set; }

        public int AudioDumpSeconds { get; private set; } = 5;

        /// <summary>
        /// Commandes de console jouees au demarrage (--exec, repetable).
        /// Permet de tester un reglage sans recompiler ni taper.
        /// </summary>
        public List<string> StartupCommands { get; } = new();

        public static LaunchOptions Parse(string[] args)
        {
            var o = new LaunchOptions();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--seconds" when i + 1 < args.Length:
                        o.Engine.AutoCloseAfterSeconds = ParseDouble(args[++i]);
                        break;
                    case "--tickrate" when i + 1 < args.Length:
                        o.Engine.TickRate = ParseDouble(args[++i]);
                        break;
                    case "--sensitivity" when i + 1 < args.Length:
                        o.MouseSensitivity = (float)ParseDouble(args[++i]);
                        break;
                    case "--novsync":
                        o.Engine.VSync = false;
                        break;
                    // Sert a comparer le cout des ombres dans des conditions
                    // strictement identiques : --shadows 0 puis --shadows 1.
                    case "--shadows" when i + 1 < args.Length:
                        o.ShadowStrength = (float)ParseDouble(args[++i]);
                        break;
                    case "--screenshot" when i + 1 < args.Length:
                        o.ScreenshotPath = args[++i];
                        break;
                    case "--shot-at" when i + 1 < args.Length:
                        o.ScreenshotTime = (float)ParseDouble(args[++i]);
                        break;
                    case "--map" when i + 1 < args.Length:
                        o.StartupMap = args[++i];
                        break;
                    case "--audio-dump" when i + 1 < args.Length:
                        o.AudioDumpPath = args[++i];
                        break;
                    case "--audio-seconds" when i + 1 < args.Length:
                        o.AudioDumpSeconds = Math.Max(1, (int)ParseDouble(args[++i]));
                        break;
                    case "--exec" when i + 1 < args.Length:
                        o.StartupCommands.Add(args[++i]);
                        break;
                }
            }

            return o;
        }

        /// <summary>
        /// ⚠️ CULTURE INVARIANTE. Sur un poste francais, « 5.2 » lu avec la
        /// culture courante vaudrait 52 ou rien du tout. Un nombre illisible
        /// vaut 0 - donc, pour --seconds, PAS de limite : c'est ce qui a fait
        /// tourner indefiniment un lancement « --seconds 5,2 » le 07/10.
        /// </summary>
        private static double ParseDouble(string s)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;
    }
}
