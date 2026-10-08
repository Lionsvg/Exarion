// ===========================================================================
//  Le joueur en partie : regard, deplacement, touches du bac a sable, tir
//
//  Ce que le joueur VEUT faire a ce pas. Le deplacement lui-meme appartient
//  au CharacterMotor du monde - le MEME que les PNJ et le client reseau.
// ===========================================================================

using System;
using System.Numerics;
using Exarion.Core;
using Exarion.Core.Physics;
using Exarion.Engine.Graphics;
using Exarion.Game.Input;
using Exarion.Game.Sound;
using Exarion.Game.World;
using Silk.NET.Input;

namespace Exarion.Game.Gameplay
{
    internal sealed class PlayerController
    {
        /// <summary>Taille par defaut des caisses, et defaut de « crate_size ».</summary>
        public const float DefaultCrateSize = 0.9f;

        private const int MaxCrates = 400;

        private readonly Camera _camera;
        private readonly InputDevices _input;
        private readonly GameWorld _world;
        private readonly SandboxFx _fx;
        private readonly SandboxAudio _audio;
        private readonly Func<Vector3> _aimPoint;
        private readonly Func<bool> _toggleWireframe;

        private readonly RecoilTracker _recoil = new();
        private float _nextShotTime;
        private bool _prevSpawn, _prevStack, _prevBoom, _prevClear, _prevWire;

        /// <param name="aimPoint">Point vise au sol (pile, explosion).</param>
        /// <param name="toggleWireframe">Bascule la vue des collisions et rend son nouvel etat.</param>
        public PlayerController(Camera camera, InputDevices input, GameWorld world, SandboxFx fx,
                                SandboxAudio audio, Func<Vector3> aimPoint, Func<bool> toggleWireframe)
        {
            _camera = camera;
            _input = input;
            _world = world;
            _fx = fx;
            _audio = audio;
            _aimPoint = aimPoint;
            _toggleWireframe = toggleWireframe;
        }

        public WeaponStats Weapon { get; } = Arsenal.Rifle;

        /// <summary>Instant jusqu'auquel le marqueur de touche reste affiche.</summary>
        /// <summary>Jusqu'a quand afficher le marqueur de touche. Le jeu le pose aussi sur un verdict du serveur.</summary>
        public float HitMarkerUntil { get; set; }

        /// <summary>
        /// Marche automatique : un OUTIL DE CAPTURE. Les --exec partent tous a
        /// t=0 et il n'y a pas de clavier en headless : sans elle, impossible de
        /// prouver qu'un joueur distant BOUGE.
        /// </summary>
        public bool AutoWalk { get; set; }

        // Reglables depuis la console : voir PlayerCommands.
        public float CrateMass { get; set; } = 18f;
        public float CrateSize { get; set; } = DefaultCrateSize;
        public float BulletForce { get; set; } = 260f;
        public float BoomRadius { get; set; } = 7f;
        public float BoomForce { get; set; } = 900f;

        /// <summary>
        /// Le regard et l'entree de ce pas.
        ///
        /// Consomme le delta de souris. Les touches a effet unique (F, V, G,
        /// C, K) sont lues ICI, au pas fixe, avec detection de front.
        /// </summary>
        public MoveInput ReadInput(float dt)
        {
            _camera.AddLook(-_input.MouseDelta.X * _input.Sensitivity, -_input.MouseDelta.Y * _input.Sensitivity);
            _input.MouseDelta = Vector2.Zero;

            IKeyboard? keyboard = _input.Keyboard;

            // Fleches : tourner la camera au clavier, EN PLUS de la souris.
            //
            // Utile pour inspecter une scene sans lacher le clavier, et pour
            // tester sur un poste ou la capture de souris gene.
            if (keyboard != null)
            {
                float turn = 0f, tilt = 0f;

                if (keyboard.IsKeyPressed(Key.Left)) turn += 1f;
                if (keyboard.IsKeyPressed(Key.Right)) turn -= 1f;
                if (keyboard.IsKeyPressed(Key.Up)) tilt += 1f;
                if (keyboard.IsKeyPressed(Key.Down)) tilt -= 1f;

                if (turn != 0f || tilt != 0f)
                {
                    // Par SECONDE, pas par image : sans le dt, la vitesse de
                    // rotation dependrait du framerate.
                    float step = _input.KeyLookSpeed * dt;
                    _camera.AddLook(turn * step, tilt * step);
                }
            }

            var input = new MoveInput { Yaw = _camera.Yaw };

            if (keyboard != null)
            {
                float x = 0f, y = 0f;
                if (keyboard.IsKeyPressed(Key.D)) x += 1f;
                if (keyboard.IsKeyPressed(Key.Q) || keyboard.IsKeyPressed(Key.A)) x -= 1f;
                if (keyboard.IsKeyPressed(Key.Z) || keyboard.IsKeyPressed(Key.W)) y += 1f;
                if (keyboard.IsKeyPressed(Key.S)) y -= 1f;

                input.Move = new Vector2(x, y);
                input.Jump = keyboard.IsKeyPressed(Key.Space);
                input.Crouch = keyboard.IsKeyPressed(Key.ControlLeft);
                input.Walk = keyboard.IsKeyPressed(Key.ShiftLeft);

                HandleSandboxKeys(keyboard);
            }

            // La marche automatique ECRASE le clavier : c'est un outil de
            // capture, on veut un mouvement reproductible et rien d'autre.
            if (AutoWalk)
                input.Move = new Vector2(0f, 1f);

            return input;
        }

        private void HandleSandboxKeys(IKeyboard keyboard)
        {
            bool spawn = keyboard.IsKeyPressed(Key.F);
            if (spawn && !_prevSpawn) SpawnCrateAhead();
            _prevSpawn = spawn;

            bool stack = keyboard.IsKeyPressed(Key.V);
            if (stack && !_prevStack) SpawnStack(_aimPoint(), 5);
            _prevStack = stack;

            bool boom = keyboard.IsKeyPressed(Key.G);
            if (boom && !_prevBoom)
            {
                Vector3 at = _aimPoint() + new Vector3(0f, 0.5f, 0f);
                _world.Physics.Explode(at, BoomRadius, BoomForce);

                if (_fx.Enabled)
                    _fx.Explosion(at, BoomRadius * 0.45f);

                _audio.PlayAt(_audio.BoomClip, at);
            }
            _prevBoom = boom;

            bool clear = keyboard.IsKeyPressed(Key.C);
            if (clear && !_prevClear)
            {
                _world.Physics.Clear();
                Console.WriteLine("  Caisses effacees.");
            }
            _prevClear = clear;

            bool wire = keyboard.IsKeyPressed(Key.K);
            if (wire && !_prevWire)
            {
                bool on = _toggleWireframe();
                Console.WriteLine($"  Vue des collisions : {(on ? "ON" : "OFF")}");
            }
            _prevWire = wire;
        }

        public void SpawnCrateAhead()
        {
            if (_world.Physics.Bodies.Count >= MaxCrates)
                return;

            Vector3 at = _world.EyePosition + _camera.Forward * 2.2f;
            var body = _world.Physics.Spawn(at, new Vector3(CrateSize), CrateMass);

            // On lui donne l'elan du regard : plus agreable que de la poser inerte.
            body.ApplyImpulse(_camera.Forward * 90f);
        }

        public void SpawnStack(Vector3 groundPoint, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (_world.Physics.Bodies.Count >= MaxCrates)
                    return;

                var at = new Vector3(groundPoint.X,
                                     groundPoint.Y + CrateSize * 0.5f + i * (CrateSize + 0.02f),
                                     groundPoint.Z);
                _world.Physics.Spawn(at, new Vector3(CrateSize), CrateMass);
            }
        }

        /// <summary>Chaque tir du joueur, avec sa direction. Le jeu le relaie au serveur quand il est en ligne.</summary>
        public Action<Vector3>? Fired { get; set; }

        /// <param name="consoleOpen">Console ouverte : on ne tire pas en tapant une commande.</param>
        public void HandleShooting(float simTime, bool consoleOpen)
        {
            bool firing = !consoleOpen && _input.Mouse != null && _input.Mouse.IsButtonPressed(MouseButton.Left);

            if (!firing)
            {
                _recoil.Tick(Weapon, simTime);
                return;
            }

            if (simTime < _nextShotTime)
                return;

            _nextShotTime = simTime + Weapon.SecondsBetweenShots;
            _recoil.OnShot(Weapon, simTime);

            Vector3 eye = _world.EyePosition;
            Vector3 dir = _camera.Forward;

            // En ligne, le SERVEUR juge aussi ce tir (3.7) ; les effets locaux
            // ci-dessous restent immediats - attendre l'aller-retour pour voir
            // l'etincelle serait la latence complete.
            Fired?.Invoke(dir);

            // Distance jusqu'au decor : une balle ne traverse pas les murs.
            float limit = 250f;
            var wallNormal = Vector3.Zero;
            bool hitWall = _world.Level.Raycast(eye, dir, limit, out float wallDistance, out wallNormal);

            if (hitWall)
                limit = wallDistance;

            RigidBody? crate = _world.Physics.Raycast(eye, dir, limit, out float crateDistance);
            if (crate != null && crateDistance < limit)
            {
                // Une balle de fusil transporte peu de quantite de mouvement,
                // mais dans un bac a sable on veut le voir. Valeur assumee.
                crate.ApplyImpulse(dir * BulletForce);
                HitMarkerUntil = simTime + 0.1f;

                // Sur une caisse, on ne connait pas la face touchee : on renvoie
                // les etincelles vers le tireur. C'est faux en toute rigueur, et
                // parfaitement credible - une gerbe part toujours vers l'avant.
                Emit(eye + dir * crateDistance, -dir);
                return;
            }

            if (hitWall)
                Emit(eye + dir * wallDistance, wallNormal);
        }

        /// <summary>Impact de balle : etincelles, poussiere, et le bruit qui va avec.</summary>
        private void Emit(Vector3 point, Vector3 normal)
        {
            if (_fx.Enabled)
                _fx.Impact(point, normal, _fx.ImpactScale);

            // Spatialise depuis S3 : le son sort du cote de l'impact, faiblit
            // avec la distance, et s'etouffe si un mur s'interpose.
            _audio.PlayAt(_audio.ImpactClip, point, 0.8f);
        }
    }
}
