// ===========================================================================
//  Exarion Engine - Bac a sable
//
//  Ce n'est PAS le jeu. C'est un terrain d'essai pour le moteur : on y
//  verifie que la boucle tient la cadence, que la collision ne laisse rien
//  passer, que les corps dynamiques s'empilent sans trembler, et que le
//  rendu affiche ce qu'on croit.
//
//  Cette classe ASSEMBLE et ORDONNE : elle cree les sous-systemes, branche
//  les entrees selon l'etat (menu, partie, editeur), et deroule chaque pas
//  et chaque image dans l'ordre qui marche. Ce que fait chaque piece vit
//  dans son propre fichier :
//
//    World/       la carte et tout ce qui en decoule, au pas fixe
//    Gameplay/    le joueur, les effets, la visee
//    Editing/     l'editeur de niveaux
//    Rendering/   ce que le pipeline dessine, l'eclairage, le personnage
//    Ui/          menu, surcouche de jeu, captures
//    Sound/       la carte son
//    Networking/  serveur et client
//    Commands/    la console, une famille de commandes par fichier
//
//  Commandes :
//    ZQSD / souris   se deplacer, viser
//    Espace          sauter          Ctrl   s'accroupir      Maj   marcher
//    Clic gauche     tirer (pousse les caisses)
//    F               poser une caisse devant soi
//    V               lacher une pile de caisses
//    G               explosion a l'endroit vise
//    C               tout effacer
//    K               vue des collisions (fil de fer)
//    Echap           menu
// ===========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Console;
using Exarion.Core.Entities;
using Exarion.Core.Net;
using Exarion.Core.Physics;
using Exarion.Engine;
using Exarion.Engine.Graphics;
using Exarion.Game.Commands;
using Exarion.Game.Diagnostics;
using Exarion.Game.Editing;
using Exarion.Game.Gameplay;
using Exarion.Game.Input;
using Exarion.Game.Networking;
using Exarion.Game.Rendering;
using Exarion.Game.Sound;
using Exarion.Game.Ui;
using Exarion.Game.World;
using Silk.NET.Input;
using Silk.NET.OpenGL;

// Silk.NET.OpenGL definit aussi un type Texture (l'enumeration d'etat GL).
// On leve l'ambiguite en faveur du notre.
using Texture = Exarion.Engine.Graphics.Texture;

namespace Exarion.Game
{
    internal sealed class SandboxApp
    {
        private readonly LaunchOptions _launch;

        // --- Partages par tout le monde ---
        private readonly Camera _camera = new();
        private readonly DebugConsole _console = new();
        private readonly InputDevices _input = new();

        // --- Le monde, au pas fixe ---
        private readonly SandboxFx _fx = new();
        private readonly GameWorld _world;
        private readonly SandboxAudio _audio = new();
        private readonly NetSession _net = new();
        private readonly EventFeed _events;

        // --- Ce qui le pilote ---
        private readonly PlayerController _player;
        private readonly EditorController _editor;
        private readonly MenuScreen _menu;

        // --- Ce qui le montre ---
        private readonly RenderSettings _settings = new();
        private readonly SceneLighting _lighting = new();
        private readonly CharacterRig _rig = new();
        private readonly SandboxScene _scene;
        private readonly SandboxHud _hud;
        private readonly Screenshots _screenshots;
        private readonly PerfMonitor _perf = new();

        private EngineHost? _engine;
        private RenderPipeline? _render;

        /// <summary>Contexte OpenGL, garde pour pouvoir recharger une carte a chaud.</summary>
        private GL? _gl;

        private Texture? _gridTexture;

        private AppMode _mode = AppMode.Menu;
        private float _simTime;

        /// <summary>
        /// Le pas fixe, retenu pour le serveur reseau : il doit tourner a la
        /// MEME cadence que la simulation locale, sinon les deux decrivent le
        /// meme monde a deux vitesses.
        /// </summary>
        private float _fixedDelta = 1f / 64f;

        public SandboxApp(LaunchOptions launch)
        {
            _launch = launch;
            _input.Sensitivity = launch.MouseSensitivity;

            _world = new GameWorld(_fx.Particles);
            _events = new EventFeed(_audio, _fx);

            _editor = new EditorController(_camera, _input, _console.System, () => _world.Level);

            _player = new PlayerController(_camera, _input, _world, _fx, _audio, AimPointOnFloor,
                                           () => _settings.Wireframe = !_settings.Wireframe)
            {
                // En ligne, chaque tir part aussi au serveur, qui le juge (3.7).
                Fired = dir => _net.Fire(dir),
            };

            _menu = new MenuScreen(_console.System,
                play: EnterPlay,
                edit: EnterEditor,
                quit: () => _engine?.Window.Close(),
                resume: () => _mode = AppMode.Playing,
                loadMap: LoadMapAndPlay,
                canLoad: () => _gl != null);

            _scene = new SandboxScene(_world, _rig, _net, _editor, () => _mode, DrawHud);
            _hud = new SandboxHud(_world, _player, _net, _events, _settings);
            _screenshots = new Screenshots(_console.System, launch.ScreenshotPath, launch.ScreenshotTime);
        }

        // --- Pour le rapport de plantage ---
        public float SimTime => _simTime;
        public int CrateCount => _world.Physics.Bodies.Count;
        public bool ConsoleOpen => _console.IsOpen;

        public void Run()
        {
            using var engine = new EngineHost(_launch.Engine);
            _engine = engine;

            engine.Load += () => OnLoad(engine);
            engine.Simulate += (dt, tick) => OnSimulate((float)dt);
            engine.Render += alpha => OnRender(engine, alpha);
            engine.Unload += OnUnload;
            engine.Resized += size =>
            {
                if (size.Y > 0)
                    _camera.AspectRatio = size.X / (float)size.Y;
            };

            engine.Run();
        }

        // ===================================================================
        //  Chargement
        // ===================================================================

        private void OnLoad(EngineHost engine)
        {
            _gl = engine.Gl;

            // ⚠️ NEUF OBJETS DE RENDU EN UNE LIGNE. C'est tout l'objet du
            // pipeline : le jeu ne cree plus la carte d'ombre, le tampon HDR, le
            // halo, l'occlusion, le ciel ni l'interface, et surtout il ne decide
            // plus de l'ORDRE dans lequel ils s'enchainent. Un second jeu
            // n'aurait pas a recopier cet enchainement - donc a le faire
            // diverger.
            var frame = engine.Window.FramebufferSize;
            _render = new RenderPipeline(engine.Gl, frame.X, frame.Y);
            _render.Scene.Shadows = _render.Shadows;
            _render.CreateParticleRenderer(_fx.Particles.Capacity);

            // Cube des caisses et des portes, formes des marqueurs.
            _scene.CreateResources(engine.Gl);

            // Texture de developpement : une case par metre, generee en
            // memoire. Aucun fichier a charger ni a distribuer.
            _gridTexture = new Texture(engine.Gl, TextureGen.DevGrid(size: 512, cells: 8));
            _render.Scene.Albedo = _gridTexture;

            _audio.Start(_world.Level, _launch.AudioDumpPath, _launch.AudioDumpSeconds);

            _rig.Load(engine.Gl);

            var fb = engine.Window.FramebufferSize;
            _camera.AspectRatio = fb.X / (float)Math.Max(1, fb.Y);

            _input.Attach(engine.Input.Keyboards.Count > 0 ? engine.Input.Keyboards[0] : null,
                          engine.Input.Mice.Count > 0 ? engine.Input.Mice[0] : null);

            if (_input.Keyboard is { } keyboard)
            {
                _console.Attach(keyboard);

                // ⚠️ LA SAISIE DU PANNEAU PASSE PAR KeyChar, PAS PAR KeyDown,
                // exactement comme la console. Voir EditorController.Type.
                keyboard.KeyChar += (_, c) =>
                {
                    if (_console.IsOpen || _mode != AppMode.Editing)
                        return;

                    _editor.Type(c);
                };

                // ⚠️ TOUT passe par KeyDown et non par IsKeyPressed pour ce qui
                // doit se declencher UNE FOIS. Une action d'editeur lue par
                // sondage se repeterait soixante fois par seconde : creer une
                // boite en poserait soixante, et l'annulation en garderait
                // soixante traces.
                keyboard.KeyDown += (_, key, _) =>
                {
                    if (_console.IsOpen)
                        return;

                    switch (_mode)
                    {
                        case AppMode.Menu:
                            _menu.OnKeyDown(key, _world.Map != null);
                            return;

                        case AppMode.Editing:
                            _editor.OnKeyDown(key, EnterMenu);
                            return;

                        default:
                            // Echap en partie n'a plus a quitter : il ramene au
                            // menu, qui a une entree pour ca.
                            if (key == Key.Escape)
                                EnterMenu();
                            return;
                    }
                };
            }

            // La souris est liberee quand la console s'ouvre : sinon on ne
            // peut plus rien faire d'autre que tourner sur soi-meme.
            _console.OpenedOrClosed += open => _input.SetCursor(open ? CursorMode.Normal : CursorMode.Raw);

            RegisterConsole(_render);

            // APRES RegisterConsole : la carte aligne light_sun et light_lamps
            // sur ce qu'elle declare, et ces ConVar doivent donc exister. Sans
            // cet ordre, la console afficherait les defauts du moteur pendant
            // qu'une carte chargee eclaire tout autrement - exactement le
            // piege des ConVar qui mentent, deja paye deux fois.
            // Les caisses de depart viennent de la CARTE (prop_crate) : elles
            // etaient lachees ici, apres n'importe quelle carte.
            LoadStartupMap(engine.Gl);

            RunStartupCommands();

            MapData map = _world.Map!;

            Console.WriteLine($"  Carte : {map.Name} — {_world.Level.Boxes.Count} boites, " +
                              $"{_scene.LevelMesh!.IndexCount / 3} triangles, {map.Lights.Count} lumieres, " +
                              $"{map.Entities.Count} entites.");
            Console.WriteLine($"          {_world.MapSource}");

            // La position EFFECTIVE, pas celle declaree : c'est le seul moyen de
            // verifier qu'une entite d'apparition a bien pris le pas sur la
            // propriete spawn de la carte.
            Vector3 feet = _world.Motor.Position;
            Console.WriteLine($"  Depart : ({feet.X:0.##}, {feet.Y:0.##}, " +
                              $"{feet.Z:0.##})  lacet {_camera.Yaw * 180f / MathF.PI:0.#}");

            Console.WriteLine("  F poser une caisse | V une pile | G explosion | C effacer");
            Console.WriteLine("  K vue des collisions | Clic tirer");
            Console.WriteLine("  F1 CONSOLE (tape 'help')  |  Echap menu");
            Console.WriteLine("--------------------------------------------------");
        }

        /// <summary>
        /// Toute la console, famille par famille. Le chargement automatique
        /// du .cfg vient en DERNIER : une valeur ne s'applique qu'a une ConVar
        /// qui existe.
        /// </summary>
        private void RegisterConsole(RenderPipeline render)
        {
            ConsoleSystem c = _console.System;

            PlayerCommands.Register(c, _world, _player, _camera, _input, AimPointOnFloor);
            FxCommands.RegisterProfiling(c, _perf, () => _render?.Gpu);
            RenderCommands.Register(c, _settings, _lighting, render, _gridTexture, _launch.ShadowStrength, _screenshots);
            CharacterCommands.Register(c, _rig);
            FxCommands.Register(c, _fx, AimPointOnFloor, () => _render?.Particles?.LastDrawn ?? 0);
            AppCommands.RegisterStates(c, EnterMenu, EnterPlay, EnterEditor, () => _editor.State != null);
            EditorCommands.Register(c, _editor, _camera, AimPointOnFloor);
            WorldCommands.Register(c, _world);
            NetCommands.Register(c, _net, () => _world.Map ?? BuildSandboxMap(), () => _world.Level, () => _fixedDelta,
                                 () => _world.Map is null ? SandboxMap.Source : _world.MapSource,
                                 () => _camera.Forward);
            AudioCommands.Register(c, _audio, AimPointOnFloor);
            MapCommands.Register(c, _world, () => _gl != null, (map, source) => ApplyMap(_gl!, map, source),
                                 BuildSandboxMap);
            AppCommands.RegisterConfig(c);

            c.Print("Exarion Engine - console. Tape 'help' ou 'list'.");

            AppCommands.LoadDefaultConfig(c);
        }

        /// <summary>
        /// Commandes passees en ligne de commande via --exec.
        ///
        /// ⚠️ Elles s'executent APRES le chargement de la carte, pas a la fin
        /// de RegisterConsole. Sinon `--exec "map_save ..."` s'executerait sur
        /// une carte qui n'existe pas encore - constate, la commande repondait
        /// "aucune carte a enregistrer". Plus generalement, une commande de la
        /// ligne de commande doit toujours avoir le DERNIER mot : sur les
        /// reglages relus du .cfg comme sur ce que la carte impose.
        ///
        /// On recopie la sortie de la console vers le terminal : sans ca,
        /// impossible de verifier quoi que ce soit en mode headless.
        ///
        /// ⚠️ PAR L'EVENEMENT LinePrinted, PAS EN COMPTANT Output. Le tampon de
        /// la console est borne a 200 lignes : en comparant son compte avant et
        /// apres chaque commande, plus rien ne sortait une fois le tampon plein
        /// - un inventaire de 126 commandes s'arretait a la 66e, sans erreur.
        /// </summary>
        private void RunStartupCommands()
        {
            ConsoleSystem c = _console.System;

            void Echo(string line) => Console.WriteLine("  exec " + line);

            c.LinePrinted += Echo;

            try
            {
                foreach (string line in _launch.StartupCommands)
                    c.Execute(line);
            }
            finally
            {
                c.LinePrinted -= Echo;
            }
        }

        // ===================================================================
        //  Un pas de simulation
        // ===================================================================

        private void OnSimulate(float dt)
        {
            _simTime += dt;
            _fixedDelta = dt;

            _console.Update(dt);

            // Les commandes differees (« delay ») partent au pas fixe, dans TOUS
            // les modes. Leur sortie est recopiee sur la sortie standard, comme
            // celle des --exec : c'est la seule facon de la lire en headless.
            // On ne s'abonne que s'il y a quelque chose a executer.
            if (_console.System.ScheduledCount > 0)
            {
                _console.System.LinePrinted += EchoDelayed;
                _console.System.Advance(dt);
                _console.System.LinePrinted -= EchoDelayed;
            }
            else
            {
                _console.System.Advance(dt);
            }

            // Les sons d'ambiance suivent leur etat dans TOUS les modes, console
            // ouverte comprise : c'est par la console qu'on les actionne en
            // headless, et un « ent_send sirene Jouer » muet serait inverifiable.
            _audio.SyncEnvSounds(_world.Env);

            CharacterMotor motor = _world.Motor;

            // Console ouverte : le jeu ne recoit RIEN. Sans ca, taper une
            // commande fait tirer et courir en meme temps.
            if (_console.IsOpen)
            {
                _input.MouseDelta = Vector2.Zero;

                // ⚠️ Les portes continuent de bouger console ouverte, et c'est
                // NECESSAIRE : la console est le seul moyen d'en actionner une
                // en headless. Les figer rendrait « ent_send porte Ouvrir »
                // muet, donc E17 inverifiable sans jouer.
                _world.PrepareStep(dt, ReportEvent);
                motor.Step(default, dt, _world.Walkable);
                _world.Physics.Step(dt);
                _fx.Particles.Step(dt);
                return;
            }

            // ⚠️ L'editeur et le menu ne partagent RIEN avec la boucle de jeu.
            // Laisser passer les entrees de jeu ferait tirer au clic de
            // selection et courir le personnage pendant qu'on deplace un mur.
            if (_mode == AppMode.Editing)
            {
                _editor.Simulate(dt, _simTime);
                _fx.Particles.Step(dt);
                UpdateListener();
                return;
            }

            if (_mode == AppMode.Menu)
            {
                // Le monde continue de tourner derriere le voile : le menu
                // montre la carte chargee, ce qui evite de charger a l'aveugle.
                _input.MouseDelta = Vector2.Zero;
                _world.PrepareStep(dt, ReportEvent);
                motor.Step(default, dt, _world.Walkable);
                _world.Physics.Step(dt);
                _fx.Particles.Step(dt);
                UpdateListener();
                return;
            }

            // Regard, deplacement voulu, touches du bac a sable : voir PlayerController.
            MoveInput input = _player.ReadInput(dt);

            // ⚠️ LES PORTES AVANCENT AVANT LE JOUEUR, et cet ordre est le coeur
            // de E17 : la porte bouge, sa boite entre dans le monde de
            // collision, PUIS on porte le joueur (PrepareStep), PUIS il se
            // deplace lui-meme.
            _world.PrepareStep(dt, ReportEvent);
            motor.Step(input, dt, _world.Walkable);

            // La vitesse VOULUE, surtout pas la vitesse reelle : un joueur qui
            // appuie de face contre une caisse est bloque, donc immobile - et
            // il pousse pourtant de tout son poids. Voir PhysicsWorld.PushWith.
            _world.Physics.PushWith(motor.GetBox(), motor.WishVelocity, strength: 3.5f);

            _perf.StartPhysics();
            _world.Physics.Step(dt);
            double physicsMs = _perf.StopPhysics();

            _perf.ReportPhysics(physicsMs, dt, _simTime, _world.Physics, _fx.Particles,
                                _render?.Particles?.LastDrawn ?? 0, _render?.Gpu);

            _player.HandleShooting(_simTime, _console.IsOpen);

            // Les particules avancent au PAS FIXE, comme la physique. Les
            // simuler au rendu ferait dependre leur trajectoire du nombre
            // d'images par seconde, et deux captures --seconds 3 ne donneraient
            // plus la meme image.
            _perf.StartParticles();
            _fx.Particles.Step(dt);
            _perf.StopParticles();

            UpdateListener();

            _world.UpdateEntities(dt, ReportEvent);

            // Le reseau EN DERNIER : l'entree envoyee est celle de ce pas, et le
            // serveur a deja avance. Envoyer avant la simulation ferait partir
            // l'entree du pas precedent, soit 15 ms de retard gratuit.
            //
            // Des que le Welcome est arrive, la carte locale est confrontee a
            // celle du serveur - ENTRE la reception et la premiere prediction.
            _net.Step(_simTime, dt, input, SyncServerMap);

            ReportShots();
        }

        private int _shotReportsSeen;

        /// <summary>
        /// Annonce chaque verdict de tir recu du serveur : console, sortie
        /// standard (le headless le lit la), et le HUD le garde affiche.
        /// </summary>
        private void ReportShots()
        {
            if (_net.Client is not { } client)
            {
                _shotReportsSeen = 0;
                return;
            }

            if (client.ShotReports == _shotReportsSeen)
                return;

            _shotReportsSeen = client.ShotReports;

            string line = NetSession.Describe(client.LastShot);
            Console.WriteLine("  Reseau : " + line);
            _console.System.Print("  " + line);

            if (client.LastShot.Hit)
                _player.HitMarkerUntil = _simTime + 0.25f;
        }

        private void ReportEvent(EntityEvent e) => _events.Report(e, _simTime);

        private static void EchoDelayed(string line) => Console.WriteLine("  delai " + line);

        /// <summary>L'auditeur suit l'oeil du joueur. Voir SandboxAudio.UpdateListener.</summary>
        private void UpdateListener()
            => _audio.UpdateListener(_world.EyePosition, _camera.Forward, _camera.Right, _world.Motor.Velocity);

        // ===================================================================
        //  Visee
        // ===================================================================

        /// <summary>
        /// Direction visee, quel que soit l'etat de l'application.
        ///
        /// En partie c'est le centre de l'ecran - un FPS tire au reticule. Dans
        /// l'editeur c'est le curseur (voir EditorController.AimDirection).
        /// </summary>
        private Vector3 AimDirection
            => _mode == AppMode.Editing ? _editor.AimDirection() : _camera.Forward;

        /// <summary>
        /// D'ou part le regard.
        ///
        /// ⚠️ Dans l'editeur ce n'est PAS l'oeil du joueur : le corps simule
        /// reste fige la ou on l'a quitte, pendant que la camera vole. Sans
        /// cette distinction, "poser le point de depart" le placait par rapport
        /// a une position abandonnee, a des dizaines de metres de ce qu'on
        /// regarde. Defaut trouve en relisant le code, pas en jouant.
        /// </summary>
        private Vector3 AimOrigin => _mode == AppMode.Editing ? _editor.Eye : _world.EyePosition;

        /// <summary>Point vise sur le decor, ou 6 m devant si on vise le vide.</summary>
        private Vector3 AimPointOnFloor() => Aim.PointOnFloor(_world.Level, AimOrigin, AimDirection);

        // ===================================================================
        //  Une image
        // ===================================================================

        private void OnRender(EngineHost engine, float alpha)
        {
            if (_mode == AppMode.Editing)
            {
                // En vol libre la camera EST l'oeil : pas d'interpolation entre
                // deux pas, puisqu'il n'y a pas de corps simule derriere.
                _camera.Position = _editor.Eye;

                // La reconstruction se fait ICI, une fois par image, et jamais
                // dans le gestionnaire d'entrees : une fleche maintenue produit
                // vingt operations par seconde et reconstruirait vingt fois le
                // maillage et le monde de collision.
                if (_editor.GeometryDirty && _editor.State != null)
                {
                    RebuildGeometry(engine.Gl, _editor.State.Map, _world.MapSource);
                    _editor.GeometryDirty = false;
                }
            }
            else
            {
                Vector3 feet = Vector3.Lerp(_world.PreviousFeet, _world.Motor.Position, alpha);
                _camera.Position = feet + new Vector3(0f, _world.Motor.Height - 0.18f, 0f);
            }

            // Horloge d'animation. Le raisonnement qui la justifie vit
            // desormais dans le moteur, sur FixedTimestep.RenderTime : c'est
            // une regle du pas fixe, pas une astuce de ce jeu, et le jeu
            // d'exemple aurait du la redecouvrir tout seul.
            //
            // ⚠️ LE BAC A SABLE N'UTILISE PAS RenderTime, ET C'EST VOULU.
            // Mesure du 17/08 : y basculer double l'ecart entre deux captures
            // identiques (231 -> 511 pixels). RenderTime part de SimulatedTime,
            // qui SAUTE EN AVANT quand la machine decroche, et le nombre de pas
            // sautes varie d'un lancement a l'autre (24 puis 15 sur le meme
            // essai). L'horloge d'un jeu peut se permettre ca ; celle qui sert
            // a comparer deux captures, non. _simTime compte les pas REELLEMENT
            // executes : il ne depend pas de la charge de la machine.
            _rig.AnimationClock = _simTime + alpha * (float)(engine.Time.FixedDeltaNanoseconds / 1e9);

            if (_render is null)
                return;

            // ⚠️ Une lampe allumee EN PARTIE (env_light) change l'eclairage sans
            // que rien d'autre ne bouge : on compare la version de l'ambiance a
            // celle deja appliquee. Sans ce test, l'interrupteur marchait dans
            // Core et la lampe ne s'allumait qu'au chargement de carte suivant.
            if (_lighting.Dirty || _lighting.EnvVersion != _world.Env.Version)
            {
                _lighting.Rebuild(_render.Scene, _world.Map, _world.Env);
                _lighting.Dirty = false;
            }

            // Les reglages de console pilotent le pipeline : voir RenderSettings.
            _render.SsaoEnabled = _settings.Ssao;
            _render.BloomEnabled = _settings.Bloom;
            _render.ParticlesEnabled = _fx.Enabled;
            _render.Wireframe = _settings.Wireframe;
            _render.SsaoDebug = _settings.SsaoDebug;
            _render.SsaoDebugRange = _settings.SsaoDebugRange;

            // ⚠️ LES POSES SE CALCULENT AVANT TOUTES LES PASSES, et une seule
            // fois. L'ombre, la profondeur et le corps doivent decrire le meme
            // personnage au meme instant - defaut corrige en M6, a ne pas
            // reintroduire en deplacant ces appels dans DrawGeometry.
            _rig.UpdatePose();
            _rig.UpdateAgentPoses(_world.Agents.Agents);
            _rig.UpdateNetPoses(_net.Client, _world.Motor.Settings.RunSpeed);

            // Le decalage visuel laisse par la derniere correction se resorbe au
            // RENDU, pas au pas fixe : c'est de l'affichage pur, et le lisser au
            // pas fixe le ferait avancer par paliers de 15 ms.
            _net.Client?.SmoothCorrection(1f / 60f);

            // ⚠️ La taille du TAMPON et celle de la FENETRE diffèrent en DPI
            // eleve, et l'editeur a besoin des deux pour convertir une position
            // de curseur en rayon. On les retient ici, ou elles sont
            // disponibles, plutot que de les redemander au hasard.
            var frameSize = engine.Window.FramebufferSize;

            _editor.FrameWidth = frameSize.X;
            _editor.FrameHeight = frameSize.Y;
            _editor.WindowWidth = engine.Window.Size.X;
            _editor.WindowHeight = engine.Window.Size.Y;

            _render.Render(frameSize, _camera, _lighting.SunDirection,
                           _scene, _fx.Particles.Live, alpha);

            _screenshots.Finish(engine.Gl, frameSize.X, frameSize.Y, _simTime);
        }

        /// <summary>
        /// L'interface : le pipeline la reclame, en couleurs d'affichage, apres
        /// la composition. Chaque etat a sa surcouche, et une seule - empiler le
        /// viseur de jeu par-dessus le menu donnerait une interface illisible.
        /// </summary>
        private void DrawHud(HudRenderer hud, int w, int h)
        {
            hud.Begin(w, h);

            if (_mode == AppMode.Editing)
                EditorView.DrawHud(hud, _editor, w, h);
            else if (_mode == AppMode.Menu)
                _menu.Draw(hud, w, h, _world.Map);
            else
                _hud.Draw(hud, w, h, _simTime, _console.IsOpen);

            _console.Render(hud, w, h);

            hud.End();
        }

        private void OnUnload()
        {
            _audio.Shutdown(_launch.AudioDumpPath);

            _scene.Dispose();
            _gridTexture?.Dispose();

            // Un seul Dispose pour les neuf objets de rendu. C'est aussi une
            // garantie : on ne peut plus en oublier un en ajoutant une passe.
            _render?.Dispose();
            _render = null;
        }

        // ===================================================================
        //  Etats : menu, partie, editeur
        // ===================================================================

        private void EnterMenu()
        {
            _mode = AppMode.Menu;
            _menu.MapPicker = false;
            _input.MouseDelta = Vector2.Zero;
        }

        private void EnterPlay()
        {
            _mode = AppMode.Playing;
            _input.MouseDelta = Vector2.Zero;
            _input.HasLastMouse = false;

            // On reprend la souris : en partie elle vise, elle ne pointe pas.
            // ⚠️ Sans ca, sortir de l'editeur laisserait un curseur visible et
            // une camera qui ne tourne plus - et rien ne dirait pourquoi.
            _editor.Looking = false;

            if (!_console.IsOpen)
                _input.SetCursor(CursorMode.Raw);

            // Le MEME chemin qu'au chargement d'une carte : sinon "Jouer"
            // depuis le menu ignorerait les entites d'apparition, et on
            // n'aurait pas deux comportements mais deux bugs a trouver.
            if (_world.Map is { } map)
            {
                ApplySpawn(map);

                // C'est aussi le bon comportement : entrer en partie remet les
                // gardes a leur poste, ce qu'on veut apres avoir edite. Voir
                // GameWorld.RestartEntities.
                _world.RestartEntities();
                _rig.RebuildAgentAnimators(_world.Agents.Agents);
            }
        }

        /// <summary>
        /// Passe dans l'editeur.
        ///
        /// ⚠️ LES CAISSES SONT EFFACEES. Elles tombent librement, et le decor
        /// va bouger sous elles : les garder les laisserait coincees dans des
        /// murs deplaces, ou en chute libre a travers un sol qui n'est plus la.
        /// Ce n'est pas une limite, c'est ce qu'on veut.
        /// </summary>
        private void EnterEditor()
        {
            if (_world.Map is not { } map)
                return;

            _mode = AppMode.Editing;
            _input.MouseDelta = Vector2.Zero;
            _input.HasLastMouse = false;

            // ⚠️ LE CURSEUR REDEVIENT VISIBLE. En partie il est capture, ce qui
            // est juste pour un FPS et absurde pour un editeur : sans curseur on
            // ne peut ni designer un objet ni atteindre le panneau. Le clic
            // droit le recapture le temps de pivoter.
            _editor.Looking = false;
            _input.SetCursor(CursorMode.Normal);

            _world.Physics.Clear();
            _fx.Particles.Clear();

            _editor.Enter(map, _world.EyePosition + new Vector3(0f, 2.5f, 0f));
        }

        /// <summary>« Choisir une carte » : charge, remet l'editeur sur la nouvelle carte, entre en partie.</summary>
        private void LoadMapAndPlay(string name)
        {
            string path = MapLibrary.Resolve(name);

            ApplyMap(_gl!, MapFile.LoadFrom(path), path);
            _editor.State?.Reset(_world.Map!);
            EnterPlay();
        }

        // ===================================================================
        //  Application d'une carte, a chaud
        //
        //  ⚠️ TOUT ce qui derive du decor doit etre refait, et l'oubli d'un seul
        //  point donne un defaut silencieux : le MAILLAGE (sinon on voit
        //  l'ancien decor et on entre dans le nouveau - l'image ment), la
        //  PHYSIQUE, le monde des PARTICULES, le decor MARCHABLE, l'ECLAIRAGE.
        // ===================================================================

        /// <summary>Le decor de reference, aux reglages d'eclairage courants.</summary>
        private MapData BuildSandboxMap()
            => SandboxMap.Build(_lighting.SunDirection, _lighting.SunIntensity, _lighting.LampIntensity);

        /// <summary>
        /// Carte du demarrage. Sans --map, c'est le bac a sable INTEGRE qui
        /// sert : un fichier present sur le disque ne doit pas remplacer le
        /// decor de reference en silence, sinon plus aucune mesure n'est
        /// comparable d'une session a l'autre.
        /// </summary>
        private void LoadStartupMap(GL gl)
        {
            if (!string.IsNullOrEmpty(_launch.StartupMap))
            {
                try
                {
                    string path = MapLibrary.Resolve(_launch.StartupMap);
                    ApplyMap(gl, MapFile.LoadFrom(path), path);
                    return;
                }
                catch (Exception ex)
                {
                    // Une carte illisible ne doit pas empecher le moteur de
                    // demarrer - mais elle doit se VOIR, pas se deviner.
                    Console.WriteLine($"  Carte : lecture impossible ({ex.Message})");
                    Console.WriteLine("          le bac a sable integre est utilise a la place.");
                }
            }

            ApplyMap(gl, BuildSandboxMap(), SandboxMap.Source);
        }

        private void ApplyMap(GL gl, MapData map, string source)
        {
            RebuildGeometry(gl, map, source);

            ApplySpawn(map);

            _lighting.AdoptMap(map, _console.System);

            // Connecte, une carte changee doit etre re-confrontee a celle du
            // serveur : voir SyncServerMap.
            _net.MapChecked = false;
        }

        /// <summary>
        /// ⚠️⚠️ LE CLIENT JOUE SUR LA CARTE DU SERVEUR. La prediction (3.5) simule
        /// dans le decor local : s'il differe de celui du serveur, le joueur est
        /// corrige en permanence sans que rien ne dise pourquoi. Jusqu'au 08/10,
        /// « net » affichait le nom de la carte du serveur, et c'etait tout.
        ///
        /// Ici : on garde la carte courante si son decor concorde, on charge
        /// celle du serveur si on l'a, sinon on se DECONNECTE en le disant -
        /// jouer quand meme serait jouer faux. Et le monde de prediction suit la
        /// carte : il etait capture a la connexion, et un rechargement le
        /// laissait sur l'ancien decor.
        /// </summary>
        private void SyncServerMap()
        {
            if (_net.Client is not { State: NetClientState.Connecte } client || _net.MapChecked || _gl is null)
                return;

            _net.MapChecked = true;

            MapSyncAction action = MapSync.Decide(client.ServerMap, client.ServerMapFingerprint, _world.Map,
                                                  LocalMapsNamed, out MapData? toLoad, out string reason);

            _net.MapStatus = reason;
            Console.WriteLine("  Reseau : " + reason);
            _console.System.Print("  " + reason);

            if (action == MapSyncAction.Load && toLoad is not null)
            {
                // La SOURCE du candidat retenu, par identite : un nom ne suffit
                // pas a distinguer le bac a sable integre d'un fichier homonyme.
                string source = SandboxMap.Source;

                foreach ((MapData candidate, string from) in _mapCandidates)
                    if (ReferenceEquals(candidate, toLoad))
                        source = from;

                ApplyMap(_gl, toLoad, source);
                EnterPlay();

                // ApplyMap vient de le remettre a faux : la carte chargee EST
                // celle du serveur, inutile de la re-verifier au pas suivant.
                _net.MapChecked = true;
            }
            else if (action == MapSyncAction.Refuse)
            {
                _net.DropClient();
                return;
            }

            client.PredictionWorld = _world.Level;
        }

        /// <summary>
        /// Les cartes locales qui portent ce nom : le bac a sable INTEGRE
        /// d'abord, s'il s'appelle ainsi, puis le fichier homonyme. Un fichier
        /// illisible n'est pas un candidat - il ne fait pas tomber la partie.
        /// </summary>
        private IEnumerable<MapData> LocalMapsNamed(string name)
        {
            _mapCandidates.Clear();

            MapData builtIn = BuildSandboxMap();

            if (string.Equals(builtIn.Name, name, StringComparison.Ordinal))
                _mapCandidates.Add((builtIn, SandboxMap.Source));

            try
            {
                string path = MapLibrary.Resolve(name);

                if (File.Exists(path))
                    _mapCandidates.Add((MapFile.LoadFrom(path), path));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Reseau : « {name} » illisible ici ({ex.Message})");
            }

            foreach ((MapData map, _) in _mapCandidates)
                yield return map;
        }

        private readonly List<(MapData Map, string Source)> _mapCandidates = new();

        /// <summary>
        /// Place le joueur sur un point d'apparition de la carte, et la camera
        /// dans l'orientation qu'il declare. Voir GameWorld.PlaceAtSpawn.
        /// </summary>
        private void ApplySpawn(MapData map)
        {
            (float yaw, float pitch) = _world.PlaceAtSpawn(map);

            _camera.Yaw = yaw * MathF.PI / 180f;
            _camera.Pitch = pitch * MathF.PI / 180f;
        }

        /// <summary>
        /// Reconstruit tout ce qui DECOULE de la geometrie : le monde simule,
        /// le maillage du decor, les animateurs des PNJ.
        ///
        /// Separe d'ApplyMap parce que l'editeur en a besoin a chaque
        /// modification, et que lui ne doit SURTOUT pas se faire replacer au
        /// point de depart a chaque boite deplacee.
        /// </summary>
        private void RebuildGeometry(GL gl, MapData map, string source)
        {
            _world.Rebuild(map, source);
            _scene.RebuildLevelMesh(gl, map);
            _rig.RebuildAgentAnimators(_world.Agents.Agents);

            // Le mannequin est celui de la carte, ou aucun.
            _rig.PlaceMannequin(_world.Mannequin());

            // Le son aussi decoule du decor : voir SandboxAudio.SetWorld.
            _audio.SetWorld(_world.Level);
        }
    }
}
