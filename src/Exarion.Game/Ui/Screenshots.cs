// ===========================================================================
//  Captures d'ecran : a la demande (« screenshot ») et automatique (--screenshot)
//
//  ⚠️ L'ECRITURE DU PNG VIT DANS LE MOTEUR (Graphics/Screenshot.cs), et c'est
//  le JEU D'EXEMPLE qui l'a exige : il en avait besoin et ne pouvait pas y
//  acceder sans recopier ces trente lignes. Il ne reste ici que ce qui
//  appartient vraiment au bac a sable : QUAND capturer, et l'echo dans sa
//  console de debug.
// ===========================================================================

using System;
using System.IO;
using Exarion.Core.Console;
using Exarion.Engine.Graphics;
using Silk.NET.OpenGL;

namespace Exarion.Game.Ui
{
    internal sealed class Screenshots
    {
        private readonly ConsoleSystem _console;
        private readonly float _autoTime;
        private string? _autoPath;

        private bool _requested;
        private string _target = "capture.png";

        /// <param name="autoPath">--screenshot, ou null.</param>
        /// <param name="autoTime">--shot-at, en secondes de SIMULATION.</param>
        public Screenshots(ConsoleSystem console, string? autoPath, float autoTime)
        {
            _console = console;
            _autoPath = autoPath;
            _autoTime = autoTime;
        }

        /// <summary>Demande une capture pour la fin de la prochaine image.</summary>
        public void Request(string target)
        {
            _target = target.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? target : target + ".png";
            _requested = true;
        }

        /// <summary>
        /// Captures d'ecran, APRES le HUD.
        ///
        /// Separees du dessin de l'interface parce que le menu et l'editeur en
        /// sortent tot : sans ca, --screenshot ne produirait jamais rien dans
        /// ces deux etats, et sans le moindre message - exactement le genre de
        /// silence que la note sur --shot-at dans CLAUDE.md decrit deja.
        /// </summary>
        public void Finish(GL gl, int width, int height, float simTime)
        {
            if (_requested)
            {
                _requested = false;
                Save(gl, width, height, _target);
            }

            if (_autoPath != null && simTime > _autoTime)
            {
                Save(gl, width, height, _autoPath);
                _autoPath = null;
            }
        }

        private void Save(GL gl, int width, int height, string path)
        {
            string? full = Screenshot.Save(gl, width, height, path);

            if (full is null)
                return;

            Console.WriteLine($"  Capture : {full}  ({width}x{height})");
            _console.Print($"  capture -> {Path.GetFileName(full)}");
        }
    }
}
