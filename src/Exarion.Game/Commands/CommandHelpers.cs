// ===========================================================================
//  Outils communs a toutes les commandes de console du bac a sable
// ===========================================================================

using System;
using System.Globalization;
using System.Numerics;
using Exarion.Core.Console;

namespace Exarion.Game.Commands
{
    internal static class CommandHelpers
    {
        /// <summary>
        /// Une ConVar et ce qu'elle pilote, en une ligne.
        ///
        /// ⚠️ LA VALEUR INITIALE DOIT COINCIDER AVEC LA REALITE. Le gestionnaire
        /// n'est appele qu'au CHANGEMENT : une ConVar declaree a 0,7 devant un
        /// champ qui vaut 1,0 affiche 0,7 et applique 1,0. Piege paye sur la
        /// SSAO, puis sur snd_volume - attrape la seconde fois par la mesure du
        /// fichier WAV, rien d'autre.
        /// </summary>
        public static void Bind(this ConsoleSystem c, string name, float value, float min, float max,
                                string description, Action<float> apply)
        {
            ConVar variable = c.Register(name, value, min, max, description);
            variable.Changed += cv => apply(cv.Value);
        }

        /// <summary>Un nombre en culture INVARIANTE, ou la valeur de repli.</summary>
        public static float ParseFloat(string text, float fallback)
            => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value : fallback;

        /// <summary>
        /// Trois coordonnees, ou le point vise par defaut.
        ///
        /// Le point vise suffit en jeu ; les coordonnees explicites sont
        /// indispensables en mode headless, ou personne ne vise rien.
        /// </summary>
        public static Vector3 ParsePoint(string[] args, Vector3 fallback)
        {
            if (args.Length < 3)
                return fallback;

            return new Vector3(ParseFloat(args[0], fallback.X),
                               ParseFloat(args[1], fallback.Y),
                               ParseFloat(args[2], fallback.Z));
        }
    }
}
