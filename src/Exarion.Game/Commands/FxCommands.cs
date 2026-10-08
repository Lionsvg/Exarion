// ===========================================================================
//  Console : particules (2.3) et profilage
// ===========================================================================

using System;
using System.Numerics;
using Exarion.Core.Console;
using Exarion.Engine.Graphics;
using Exarion.Game.Diagnostics;
using Exarion.Game.Gameplay;

namespace Exarion.Game.Commands
{
    internal static class FxCommands
    {
        /// <param name="aimPoint">Point vise au sol, par defaut des commandes d'effets.</param>
        /// <param name="drawn">Particules dessinees a la derniere image.</param>
        public static void Register(ConsoleSystem c, SandboxFx fx, Func<Vector3> aimPoint, Func<int> drawn)
        {
            c.Bind("particles", 1f, 0f, 1f, "Afficher et simuler les particules",
                   x => fx.Enabled = x >= 0.5f);
            c.Bind("fx_impact", 1f, 0.2f, 4f, "Ampleur des impacts de balle",
                   x => fx.ImpactScale = x);

            // Les effets se declenchent normalement a la souris ou au clavier.
            // Ces commandes existent pour le mode headless : sans elles, aucune
            // capture d'ecran ne peut montrer une particule.
            c.Register("fx_explode", args =>
            {
                Vector3 at = CommandHelpers.ParsePoint(args, aimPoint() + new Vector3(0f, 0.6f, 0f));
                float scale = args.Length >= 4 ? CommandHelpers.ParseFloat(args[3], 1f) : 1f;

                fx.Explosion(at, scale);
                c.Print($"  explosion en ({at.X:0.##}, {at.Y:0.##}, {at.Z:0.##}), echelle {scale:0.##}");
            }, "Explosion. fx_explode [x y z] [echelle]");

            c.Register("fx_impact_at", args =>
            {
                Vector3 at = CommandHelpers.ParsePoint(args, aimPoint());

                fx.Impact(at, Vector3.UnitY, fx.ImpactScale);
                c.Print($"  impact en ({at.X:0.##}, {at.Y:0.##}, {at.Z:0.##})");
            }, "Impact vers le haut. fx_impact_at [x y z]");

            c.Register("fx_smoke", args =>
            {
                Vector3 at = CommandHelpers.ParsePoint(args, aimPoint() + new Vector3(0f, 0.8f, 0f));
                int count = args.Length >= 4 ? (int)CommandHelpers.ParseFloat(args[3], 20f) : 20;

                fx.Smoke(at, count);
                c.Print($"  fumee en ({at.X:0.##}, {at.Y:0.##}, {at.Z:0.##}), {count} bouffees");
            }, "Fumee. fx_smoke [x y z] [nombre]");

            c.Register("fx_clear", _ =>
            {
                fx.Particles.Clear();
                c.Print("  particules effacees");
            }, "Vide le reservoir de particules");

            c.Register("fx", _ =>
                c.Print($"  {fx.Particles.Count} / {fx.Particles.Capacity} particules, " +
                        $"{drawn()} dessinees, " +
                        $"{fx.Particles.Dropped} refusees faute de place"),
                "Etat du reservoir de particules");
        }

        /// <summary>« perf » et « gpu » : les deux chronometres.</summary>
        public static void RegisterProfiling(ConsoleSystem c, PerfMonitor perf, Func<GpuProfiler?> gpu)
        {
            c.Bind("perf", 0f, 0f, 1f,
                   "Cout du pas physique, une ligne par seconde", x => perf.Enabled = x >= 0.5f);

            // Coupe par defaut : deux horodatages par passe et par image, ce
            // n'est pas gratuit, et on ne mesure pas en permanence.
            c.Bind("gpu", 0f, 0f, 1f, "Chronometre chaque passe de rendu (rapport avec perf 1)",
                   x => { if (gpu() is { } profiler) profiler.Enabled = x >= 0.5f; });
        }
    }
}
