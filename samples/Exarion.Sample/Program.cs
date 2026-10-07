// ===========================================================================
//  Exarion - Jeu d'exemple
//
//  ---------------------------------------------------------------------
//  POURQUOI CE FICHIER EXISTE
//
//  Un moteur qui n'a qu'un seul jeu peut etre arbitrairement couple a ce
//  jeu : personne ne s'en apercoit, parce que rien ne l'exerce autrement.
//  Ce fichier est le SECOND client, et c'est la seule preuve que
//  Exarion.Engine est utilisable de l'exterieur.
//
//  Il ne reference QUE Exarion.Core et Exarion.Engine. Jamais Exarion.Game.
//  Le jour ou il en aurait besoin, c'est que le moteur est incomplet.
//  ---------------------------------------------------------------------
//
//  Ce qu'il fait : charge une carte, laisse marcher dedans, et affiche la
//  vitesse. C'est tout - et c'est exactement le minimum qu'un jeu doit
//  pouvoir faire sans recopier trois mille lignes de bac a sable.
//
//  ⚠️ CE FICHIER EST AUSSI UNE MESURE. Sa longueur dit ce que le moteur ne
//  prend pas encore en charge : chaque ligne ici est une ligne que tous les
//  jeux devront reecrire, donc un candidat a remonter dans le moteur.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.Input;
using Exarion.Core.Assets;
using Exarion.Core.Physics;
using Exarion.Engine;
using Exarion.Engine.Graphics;

namespace Exarion.Sample
{
    internal sealed class SampleGame : IWorldRenderer
    {
        private readonly Camera _camera = new();
        private readonly CharacterMotor _motor = new();

        private RenderPipeline _render = null!;
        private CollisionWorld _level = new();
        private Mesh _levelMesh = null!;

        private IKeyboard? _keyboard;
        private IMouse? _mouse;
        private Vector2 _lastMouse;
        private bool _hasMouse;

        private Vector3 _previousFeet;
        private readonly Vector3 _sun = Vector3.Normalize(new Vector3(-0.35f, -0.85f, -0.4f));

        private static void Main(string[] args)
        {
            var options = new EngineOptions { Title = "Exarion - exemple", Width = 1280, Height = 720 };

            // Une seule option reprise du bac a sable : de quoi verifier ce
            // fichier en mode headless, comme tout le reste du projet.
            string? shot = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--seconds" && i + 1 < args.Length)
                    options.AutoCloseAfterSeconds = double.Parse(args[++i],
                        System.Globalization.CultureInfo.InvariantCulture);
                else if (args[i] == "--screenshot" && i + 1 < args.Length)
                    shot = args[++i];
            }

            new SampleGame().Run(options, shot);
        }

        private string? _shot;

        private void Run(EngineOptions options, string? shot)
        {
            _shot = shot;

            using var engine = new EngineHost(options);

            engine.Load += () => Load(engine);
            engine.Simulate += (dt, _) => Simulate((float)dt);
            engine.Render += alpha => Render(engine, alpha);
            engine.Unload += () => _render.Dispose();

            engine.Run();
        }

        private void Load(EngineHost engine)
        {
            _keyboard = engine.Input.Keyboards.Count > 0 ? engine.Input.Keyboards[0] : null;
            _mouse = engine.Input.Mice.Count > 0 ? engine.Input.Mice[0] : null;

            var frame = engine.Window.FramebufferSize;

            // ⚠️ NEUF OBJETS DE RENDU, UNE LIGNE. C'est la seule chose que ce
            // fichier avait besoin d'obtenir du moteur, et c'etait justement ce
            // qui manquait : avant le pipeline, il aurait fallu recopier
            // l'enchainement des passes du bac a sable.
            _render = new RenderPipeline(engine.Gl, frame.X, frame.Y);
            _render.Scene.Shadows = _render.Shadows;
            _render.Scene.Albedo = new Texture(engine.Gl, TextureGen.DevGrid(size: 512, cells: 8));

            MapData map = LoadMap();
            ApplyMap(engine, map);

            Console.WriteLine($"  Exemple : {map.Name} — {map.Boxes.Count} boites, {map.Lights.Count} lumieres.");
            Console.WriteLine("  ZQSD pour marcher, souris pour regarder, Echap pour quitter.");
        }

        /// <summary>
        /// Cherche une carte dans maps/, sinon en fabrique une.
        ///
        /// Un exemple ne doit PAS dependre d'un fichier : quelqu'un qui clone le
        /// depot doit pouvoir le lancer tel quel.
        /// </summary>
        private static MapData LoadMap()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);

            while (dir is not null)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "maps", "essai" + MapFile.Extension);

                if (System.IO.File.Exists(candidate))
                    return MapFile.LoadFrom(candidate);

                dir = dir.Parent;
            }

            var map = new MapData { Name = "salle de repli", Spawn = new Vector3(0f, 1f, 8f) };

            map.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f), new Vector3(0.3f, 0.31f, 0.34f));
            map.AddBox(new Vector3(0f, 1.5f, -6f), new Vector3(4f, 3f, 1f), new Vector3(0.5f, 0.4f, 0.38f));
            map.Lights.Add(MapLight.Directional(new Vector3(-0.35f, -0.85f, -0.4f), new Vector3(0.62f, 0.7f, 0.85f), 1.1f));

            return map;
        }

        private void ApplyMap(EngineHost engine, MapData map)
        {
            var colors = new List<Vector3>();

            _level = map.ToCollisionWorld(colors);
            _levelMesh = MeshBuilder.FromCollisionWorld(engine.Gl, _level, colors);

            _motor.Position = map.Spawn;
            _previousFeet = _motor.Position;
            _camera.Yaw = map.SpawnYaw * MathF.PI / 180f;
            _camera.Pitch = map.SpawnPitch * MathF.PI / 180f;

            // Les lumieres de la carte, en une ligne. Ces quinze lignes de
            // traduction etaient ici avant : ecrire ce fichier a servi a les
            // faire remonter dans le moteur, ou il n'en existe qu'une copie.
            _render.Scene.LoadLights(map);
        }

        private void Simulate(float dt)
        {
            if (_keyboard is null)
                return;

            if (_keyboard.IsKeyPressed(Key.Escape))
                Environment.Exit(0);

            // Souris : on suit le DELTA, jamais la position absolue.
            if (_mouse != null)
            {
                Vector2 position = _mouse.Position;

                if (_hasMouse)
                    _camera.AddLook(-(position.X - _lastMouse.X) * 0.0022f,
                                    -(position.Y - _lastMouse.Y) * 0.0022f);

                _lastMouse = position;
                _hasMouse = true;
            }

            var input = new MoveInput { Yaw = _camera.Yaw };

            float x = 0f, y = 0f;
            if (_keyboard.IsKeyPressed(Key.D)) x += 1f;
            if (_keyboard.IsKeyPressed(Key.Q) || _keyboard.IsKeyPressed(Key.A)) x -= 1f;
            if (_keyboard.IsKeyPressed(Key.Z) || _keyboard.IsKeyPressed(Key.W)) y += 1f;
            if (_keyboard.IsKeyPressed(Key.S)) y -= 1f;

            input.Move = new Vector2(x, y);
            input.Jump = _keyboard.IsKeyPressed(Key.Space);
            input.Crouch = _keyboard.IsKeyPressed(Key.ControlLeft);

            _previousFeet = _motor.Position;
            _motor.Step(input, dt, _level);
        }

        private void Render(EngineHost engine, float alpha)
        {
            // Interpolation entre deux pas fixes : sans elle, le rendu saccade
            // des qu'il depasse la cadence de simulation.
            Vector3 feet = Vector3.Lerp(_previousFeet, _motor.Position, alpha);
            _camera.Position = feet + new Vector3(0f, _motor.Height - 0.18f, 0f);

            var frame = engine.Window.FramebufferSize;
            _camera.AspectRatio = (float)frame.X / MathF.Max(1, frame.Y);

            _render.Render(frame, _camera, _sun, this, ReadOnlySpan<Core.Effects.Particle>.Empty, alpha);

            // Le temps vient du moteur. Compter soi-meme un _time += dt marchait
            // ici, mais c'est exactement le compteur qu'il ne faut PAS ecrire
            // pour animer : voir FixedTimestep.RenderTime, qui explique pourquoi.
            // Autant que l'exemple montre le bon reflexe.
            if (_shot is string target && engine.Time.SimulatedTime > 1.0)
            {
                Console.WriteLine("  Capture : " + Screenshot.Save(engine.Gl, frame.X, frame.Y, target));
                _shot = null;
            }
        }

        // --- Les trois seules methodes que le pipeline reclame ---------------

        public void DrawGeometry(GeometrySink sink) => sink(_levelMesh, Matrix4x4.Identity, null);

        public void DrawOpaque(SceneRenderer scene) => scene.Draw(_levelMesh, Matrix4x4.Identity, Vector3.One);

        public void DrawHud(HudRenderer hud, int width, int height)
        {
            hud.Begin(width, height);
            hud.Crosshair(4f, 7f, 2f, new Vector4(0.25f, 1f, 0.35f, 0.9f));
            hud.TextBoxed(20f, height - 34f, 2f,
                $"{_motor.HorizontalSpeed:0.0} M/S   {_motor.Stance.ToString().ToUpperInvariant()}",
                new Vector4(0.72f, 0.76f, 0.82f, 0.95f));
            hud.End();
        }
    }
}
