// ===========================================================================
//  Console : le joueur, la vue et les caisses
//
//  Tout ce qui touche au FEELING est ici : c'est le seul moyen de le regler
//  en essayant, plutot qu'en recompilant a chaque essai.
// ===========================================================================

using System;
using System.Globalization;
using System.Numerics;
using Exarion.Core.Console;
using Exarion.Core.Physics;
using Exarion.Engine.Graphics;
using Exarion.Game.Gameplay;
using Exarion.Game.Input;
using Exarion.Game.World;

namespace Exarion.Game.Commands
{
    internal static class PlayerCommands
    {
        /// <param name="aimPoint">Point vise au sol : la ou « stack » et « boom » agissent.</param>
        public static void Register(ConsoleSystem c, GameWorld world, PlayerController player,
                                    Camera camera, InputDevices input, Func<Vector3> aimPoint)
        {
            MotorSettings m = world.Motor.Settings;

            // --- Deplacement ---
            c.Bind("speed_run", m.RunSpeed, 0.5f, 30f, "Vitesse de course en m/s", x => m.RunSpeed = x);
            c.Bind("speed_walk", m.WalkSpeed, 0.5f, 30f, "Vitesse de marche en m/s", x => m.WalkSpeed = x);
            c.Bind("speed_crouch", m.CrouchSpeed, 0.2f, 30f, "Vitesse accroupi en m/s", x => m.CrouchSpeed = x);
            c.Bind("jump_height", m.JumpHeight, 0.1f, 10f, "Hauteur de saut en m", x => m.JumpHeight = x);
            c.Bind("gravity", m.Gravity, 0f, 100f, "Pesanteur en m/s2 (joueur ET caisses)", x =>
            {
                m.Gravity = x;
                world.SetGravity(x);
            });
            c.Bind("accel_ground", m.GroundAcceleration, 0.5f, 100f, "Acceleration au sol", x => m.GroundAcceleration = x);
            c.Bind("accel_air", m.AirAcceleration, 0f, 50f, "Acceleration en l'air", x => m.AirAcceleration = x);
            c.Bind("friction", m.Friction, 0f, 50f, "Friction au sol du joueur", x => m.Friction = x);
            c.Bind("step_height", m.StepHeight, 0f, 2f, "Hauteur de marche franchie", x => m.StepHeight = x);

            // --- Vue ---
            c.Bind("fov", camera.HorizontalFovDegrees, 60f, 130f, "Champ de vision HORIZONTAL",
                   x => camera.HorizontalFovDegrees = x);
            c.Bind("sensitivity", input.Sensitivity, 0.0002f, 0.02f, "Sensibilite souris",
                   x => input.Sensitivity = x);
            c.Bind("look_speed", 130f, 10f, 500f, "Vitesse des fleches, en degres par seconde",
                   x => input.KeyLookSpeed = x * MathF.PI / 180f);

            // --- Caisses ---
            c.Bind("crate_mass", 18f, 0.5f, 500f, "Masse des nouvelles caisses", x => player.CrateMass = x);
            c.Bind("crate_size", PlayerController.DefaultCrateSize, 0.2f, 4f, "Taille des nouvelles caisses",
                   x => player.CrateSize = x);
            c.Bind("bullet_force", 260f, 0f, 5000f, "Poussee d'une balle sur une caisse", x => player.BulletForce = x);
            c.Bind("boom_radius", 7f, 0.5f, 50f, "Rayon d'explosion", x => player.BoomRadius = x);
            c.Bind("boom_force", 900f, 0f, 10000f, "Force d'explosion", x => player.BoomForce = x);

            c.Register("spawn", args =>
            {
                int n = args.Length > 0 && int.TryParse(args[0], out int k) ? Math.Clamp(k, 1, 100) : 1;
                for (int i = 0; i < n; i++) player.SpawnCrateAhead();
                c.Print($"  {n} caisse(s) posee(s), total {world.Physics.Bodies.Count}");
            }, "spawn [n] - pose des caisses devant soi");

            c.Register("stack", args =>
            {
                int n = args.Length > 0 && int.TryParse(args[0], out int k) ? Math.Clamp(k, 1, 60) : 5;
                player.SpawnStack(aimPoint(), n);
                c.Print($"  pile de {n}, total {world.Physics.Bodies.Count}");
            }, "stack [n] - lache une pile a l'endroit vise");

            c.Register("boom", _ =>
            {
                Vector3 at = aimPoint() + new Vector3(0f, 0.5f, 0f);
                world.Physics.Explode(at, player.BoomRadius, player.BoomForce);
                c.Print($"  explosion rayon {player.BoomRadius:0.#} force {player.BoomForce:0}");
            }, "boom - explosion a l'endroit vise");

            c.Register("crates", args =>
            {
                if (args.Length > 0 && args[0] == "clear")
                {
                    world.Physics.Clear();
                    c.Print("  caisses effacees");
                    return;
                }

                int awake = 0;
                foreach (var b in world.Physics.Bodies) if (!b.Sleeping) awake++;
                c.Print($"  {world.Physics.Bodies.Count} caisses, {awake} encore simulees");
            }, "crates [clear] - compte ou efface les caisses");

            // --- Position et regard ---
            c.Register("tp", args =>
            {
                CharacterMotor motor = world.Motor;

                if (args.Length >= 3
                    && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                    && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    motor.Position = new Vector3(x, y, z);
                }
                else
                {
                    motor.Position = new Vector3(0f, 1f, 20f);
                }

                motor.Velocity = Vector3.Zero;
                world.PreviousFeet = motor.Position;
                c.Print($"  position ({motor.Position.X:0.#}, {motor.Position.Y:0.#}, {motor.Position.Z:0.#})");
            }, "tp [x y z] - se teleporter, ou revenir au depart");

            // Piloter la camera depuis la console rend les captures
            // reproductibles : sans ca, impossible de comparer deux rendus.
            c.Register("look", args =>
            {
                if (args.Length >= 1
                    && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw))
                {
                    camera.Yaw = yaw * MathF.PI / 180f;
                }

                if (args.Length >= 2
                    && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float pitch))
                {
                    camera.Pitch = Math.Clamp(pitch * MathF.PI / 180f, -Camera.MaxPitch, Camera.MaxPitch);
                }

                c.Print($"  vue : lacet {camera.Yaw * 180f / MathF.PI:0.#} deg, " +
                        $"tangage {camera.Pitch * 180f / MathF.PI:0.#} deg");
            }, "look <lacet> [tangage] - oriente la camera, en degres");

            // ⚠️ MARCHE AUTOMATIQUE : c'est un OUTIL DE VERIFICATION, au meme
            // titre que « look » juste au-dessus. Les --exec partent tous a
            // t=0 et il n'y a pas de clavier en headless : sans cette commande,
            // impossible de prouver qu'un joueur distant BOUGE - on ne pourrait
            // montrer que deux personnages immobiles.
            c.Register("walk", args =>
            {
                player.AutoWalk = args.Length == 0
                    || (float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v != 0f);

                c.Print($"  marche automatique {(player.AutoWalk ? "ON" : "OFF")}");
            }, "walk [0|1] - avance tout droit sans clavier, pour les captures");
        }
    }
}
