// ===========================================================================
//  Exarion Engine - Bac a sable
//
//  Ce n'est PAS le jeu. C'est un terrain d'essai pour le moteur : on y
//  verifie que la boucle tient la cadence, que la collision ne laisse rien
//  passer, que les corps dynamiques s'empilent sans trembler, et que le
//  rendu affiche ce qu'on croit.
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
//    Echap           quitter
//
//  Options : --seconds N  --tickrate N  --sensitivity N  --novsync
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Exarion.Core;
using Exarion.Core.Animation;
using Exarion.Core.Assets;
using Exarion.Core.Audio;
using Exarion.Engine.Audio;
using Exarion.Core.Console;
using Exarion.Core.Deterministic;
using Exarion.Core.Editor;
using Exarion.Core.Effects;
using Exarion.Core.Entities;
using Exarion.Core.Net;
using Exarion.Core.UI;
using Exarion.Core.Physics;
using Exarion.Engine;
using Exarion.Engine.Graphics;
using Exarion.Engine.Net;

// Silk.NET.OpenGL definit aussi un type Texture (l'enumeration d'etat GL).
// On leve l'ambiguite en faveur du notre.
using Texture = Exarion.Engine.Graphics.Texture;

namespace Exarion.Game
{
    internal static class Program
    {
        // --- Rendu ---
        // ⚠️ LE PIPELINE POSSEDE MAINTENANT TOUS LES RENDUS.
        // Les anciens champs deviennent des raccourcis en LECTURE SEULE vers
        // lui : trois mille lignes de bac a sable s'en servent, et les
        // reecrire d'un bloc aurait ete le meilleur moyen de casser quelque
        // chose sans le voir.
        private static RenderPipeline? _render;

        private static SceneRenderer? _scene => _render?.Scene;
        private static SkyRenderer? _sky => _render?.Sky;
        private static ShadowMap? _shadows => _render?.Shadows;
        private static Texture? _gridTexture;
        private static bool _lightsDirty = true;

        // --- Occlusion ambiante (S1) ---
        // --- Rendu HDR et bloom (1.8) ---
        private static HdrTarget? _hdr => _render?.Hdr;
        private static Bloom? _bloom => _render?.Bloom;
        private static bool _bloomEnabled = true;

        private static Ssao? _ssao => _render?.Ssao;
        private static bool _ssaoEnabled = true;
        private static int _ssaoDebug;
        private static float _ssaoDebugRange = 30f;

        // --- Profilage du pas physique ---
        //
        //  Mesurer les images par seconde ne dit RIEN du cout de la physique :
        //  a 3 000 img/s le rendu noie tout le reste. On chronometre donc le
        //  pas lui-meme, et on le compare a son budget - 15,6 ms a 64 pas/s.
        //  C'est le seul chiffre qui dise si le solveur tient la charge.
        private static readonly System.Diagnostics.Stopwatch _physicsClock = new();
        private static double _physicsMsSum;
        private static double _physicsMsMax;
        private static int _physicsSamples;
        private static bool _perfEnabled;
        private static float _perfNextReport;

        // --- Capture d'ecran ---
        private static readonly List<string> _startupCommands = new();
        private static string? _autoScreenshotPath;
        private static float _autoScreenshotTime = 1f;
        private static bool _screenshotRequested;
        private static string _screenshotTarget = "capture.png";

        /// <summary>Direction du soleil. Partagee par l'eclairage et la carte d'ombre.</summary>
        private static Vector3 _sunDirection = Vector3.Normalize(new Vector3(-0.35f, -0.85f, -0.4f));
        private static HudRenderer? _hud => _render?.Hud;
        private static Mesh? _levelMesh;
        private static Mesh? _unitCube;

        /// <summary>Carte courante. Source unique du decor, des lumieres et du point de depart.</summary>
        private static MapData? _map;

        /// <summary>Fichier d'ou vient la carte, vide si elle vient du code.</summary>
        private static string _mapSource = "(bac a sable, integre au moteur)";

        /// <summary>Carte demandee par --map, vide pour le bac a sable integre.</summary>
        private static string _startupMap = "";

        /// <summary>Contexte OpenGL, garde pour pouvoir recharger une carte a chaud.</summary>
        private static GL? _gl;

        // Intensites que la CARTE declare. Servent de reference aux reglages
        // globaux light_sun / light_lamps - voir RebuildLights().
        private static float _mapSunReference;
        private static float _mapLampReference;
        private static readonly Camera _camera = new();
        private static bool _wireframe;

        // --- Simulation ---
        private static CollisionWorld _level = new();

        /// <summary>
        /// Ce sur quoi le JOUEUR bute : le decor, plus les caisses.
        ///
        /// Le controleur de personnage ne connait que des boites alignees, et
        /// c'est voulu - un FPS tactique n'a pas besoin de physique rigide
        /// pour le joueur. Mais il ne voyait que le decor : les caisses lui
        /// etaient transparentes. On les traversait en les bousculant, et
        /// monter dessus etait impossible par construction.
        ///
        /// Reconstruit a chaque pas, parce que les caisses bougent.
        /// </summary>
        private static readonly CollisionWorld _walkable = new();
        private static PhysicsWorld _physics = new(new CollisionWorld());
        private static CharacterMotor _motor = new();
        private static readonly RecoilTracker _recoil = new();

        private static WeaponStats _weapon = Arsenal.Rifle;
        private static float _nextShotTime;
        private static float _simTime;

        private static float _hitMarkerUntil;
        private static Vector3 _previousFeet;

        // --- Console ---
        private static readonly DebugConsole _console = new();
        private static bool _showStats = true;

        // ===================================================================
        //  Etats de l'application (4.1)
        //
        //  Trois etats, et un seul actif a la fois. Le point important est que
        //  les ENTREES sont dispatchees selon l'etat : sans ca, la fleche qui
        //  deplace une boite dans l'editeur ferait aussi tourner la camera en
        //  partie, et le clic qui selectionne tirerait une balle.
        // ===================================================================

        private enum AppMode { Menu, Playing, Editing }

        private static AppMode _mode = AppMode.Menu;

        private static readonly MenuModel _menu = new("Jouer", "Editeur de niveau", "Choisir une carte", "Quitter");
        private static readonly MenuModel _mapMenu = new();
        private static bool _mapPicker;

        /// <summary>Etat de l'editeur. Cree au premier passage dans l'editeur.</summary>
        private static EditorState? _editor;

        /// <summary>
        /// Panneau de proprietes, a droite. Cree en meme temps que l'editeur.
        ///
        /// ⚠️ Il porte le clavier quand il est focalise : c'est pour ca que
        /// EditorKey et EditorFly l'interrogent AVANT toute autre chose. Sans
        /// ca, taper « speed » ferait voler la camera vers l'arriere (S) en
        /// meme temps qu'on ecrit.
        /// </summary>
        private static PropertyPanel? _panel;

        // --- Entites vivantes (E13) ---
        private static readonly EntityWorld _entities = new();

        // --- PNJ (E14) ---
        private static readonly AgentWorld _agents = new();

        // --- Geometrie mobile : portes, ascenseurs, ponts (E17) ---
        private static readonly MoverWorld _movers = new();

        // -------------------------------------------------------------------
        //  RESEAU (phase 3.1 -> 3.4)
        //
        //  ⚠️ Le serveur et le client peuvent tourner DANS LE MEME PROCESSUS :
        //  c'est le mode « heberger », ou l'hote joue sur son propre serveur.
        //  Deux sockets distinctes, deux ports - le client passe par le reseau
        //  comme n'importe qui. Le raccourci « l'hote lit directement l'etat du
        //  serveur » donnerait a l'hote un comportement different de celui des
        //  autres joueurs, donc des defauts qu'on ne verrait jamais en local.
        // -------------------------------------------------------------------

        private static UdpTransport? _serverTransport;
        private static UdpTransport? _clientTransport;
        private static NetServer? _netServer;
        private static NetClient? _netClient;

        /// <summary>Horloge du reseau : temps de simulation, jamais DateTime.Now.</summary>
        private static double NetTime => _simTime;

        private static string _netName = "joueur";

        /// <summary>
        /// Un Animator PAR PNJ.
        ///
        /// ⚠️ Un seul partage donnerait a tous les PNJ la MEME pose au meme
        /// instant : trois gardes marchant au pas, jambe gauche synchronisee.
        /// C'est immediatement faux a l'oeil.
        ///
        /// ⚠️ C'est aussi le premier vrai usage du melange de poses (2.2). La
        /// note de l'epoque disait « le branchement tient en une ligne le jour
        /// ou il y aura une entite » : c'est cette ligne, _characterBlend
        /// remplace par la vitesse de chaque agent.
        /// </summary>
        private static readonly List<Animator> _agentAnimators = new();

        /// <summary>Dernier declenchement, pour l'afficher un instant a l'ecran.</summary>
        private static string _lastEvent = "";
        private static float _lastEventUntil;

        /// <summary>Position de l'oeil en vol libre. L'editeur n'utilise pas le CharacterMotor.</summary>
        private static Vector3 _editorEye = new(0f, 6f, 24f);

        /// <summary>
        /// Une modification a eu lieu : le decor doit etre reconstruit.
        ///
        /// ⚠️ On ne reconstruit PAS a chaque appui mais une fois par image. Une
        /// fleche maintenue produit vingt operations par seconde, et chacune
        /// reconstruirait le maillage et le monde de collision.
        /// </summary>
        private static bool _editorGeometryDirty;

        private static float _editorRepeatAt;
        private static bool _editorStroke;
        private static bool _prevEditorPick;

        private static readonly Mesh?[] _gridMesh = new Mesh?[1];
        private static float _gridMeshStep;
        private static bool _showGrid = true;

        /// <summary>
        /// Une forme unitaire par MapShape, indexee par la valeur de l'enum.
        ///
        /// Sert aux marqueurs d'entites et a l'apercu de la forme courante.
        /// Le tableau est dimensionne sur l'enum : ajouter une forme sans
        /// l'instancier ici donnerait un marqueur invisible, pas un plantage -
        /// on garde donc le test de nullite au dessin.
        /// </summary>
        private static readonly Mesh?[] _unitShapes = new Mesh?[MapShapes.All.Length];

        // --- Entrees ---
        private static IKeyboard? _keyboard;
        private static IMouse? _mouse;
        private static Vector2 _mouseDelta;
        private static Vector2 _lastMousePos;
        private static bool _hasLastMouse;
        private static float _mouseSensitivity = 0.0022f;

        /// <summary>Vitesse de rotation aux fleches, en radians par seconde.</summary>
        private static float _keyLookSpeed = 130f * MathF.PI / 180f;
        private static bool _prevSpawn, _prevStack, _prevBoom, _prevClear, _prevWire;

        // --- Personnage importe (M2, M3) ---
        private static Mesh? _characterMesh;
        private static Texture? _characterTexture;
        private static Material _characterMaterial = Material.Default;

        // --- Animation du personnage (M5, M6, 2.2) ---
        private static GltfModel? _characterModel;
        private static Animator? _animator;
        private static Matrix4x4[]? _characterBones;
        private static int _characterAnimation;
        private static float _characterSpeed = 1f;

        // Horloge de l'animation : lisse (elle avance ENTRE deux pas fixes) et
        // pourtant deterministe (elle ne lit pas le temps reel). Sans la
        // premiere propriete l'animation saccade des que le rendu depasse 64
        // images par seconde ; sans la seconde, deux captures headless a
        // --seconds 3 ne donneraient pas la meme pose.
        private static float _animationClock;
        private static float _lastAnimationClock;

        // Melange marche <-> course, et son reglage de decalage de pied.
        private static float _characterBlend;
        private static float _characterOffset;

        // Phase imposee, ou -1 pour laisser courir. Sert a comparer deux
        // melanges au MEME instant du pas - sinon les cadences different et
        // les deux captures ne montrent pas la meme chose.
        private static float _characterPhase = -1f;
        private static Vector3 _characterPosition = new(0f, 0f, 15f);
        // Le modele regarde vers +Z. Sans rotation il fait donc deja face au
        // point de depart du joueur, qui est plus loin sur +Z.
        private static float _characterYaw;

        // --- Particules (2.3) ---
        private static readonly ParticleSystem _particles = new(4096);
        private static ParticleRenderer? _particleRenderer => _render?.Particles;
        private static bool _particlesEnabled = true;
        private static float _impactScale = 1f;

        // --- Audio (2.1) ---
        //
        // Le melangeur vit dans Core et se teste en ligne de commande ; le
        // peripherique est la seule chose qui touche miniaudio.
        private static AudioMixer? _mixer;
        private static AudioDevice? _audio;
        private static AudioWorld? _sound;
        private static AudioRecorder? _recorder;
        private static int _placedSound = -1;

        private static AudioClip? _clipImpact;
        private static AudioClip? _clipBoom;
        private static AudioClip? _clipTone;

        private static string? _audioDumpPath;
        private static int _audioDumpSeconds = 5;

        // --- Profileur GPU (dette de 4.3) ---
        private static GpuProfiler? _gpu => _render?.Gpu;

        private static readonly System.Diagnostics.Stopwatch _particlesClock = new();
        private static double _particlesMsSum;
        private static double _particlesMsMax;

        // Generateur des effets. Graine FIXE : deux captures headless doivent
        // rendre la meme image, sinon aucune comparaison de rendu n'a de sens.
        private static Rng _fxRng = new(20260816u);

        private const float CrateSize = 0.9f;
        private const int MaxCrates = 400;

        // Reglables depuis la console : voir RegisterConsole().
        private static float _crateMass = 18f;
        private static float _crateSize = CrateSize;
        private static float _bulletForce = 260f;
        private static float _boomRadius = 7f;
        private static float _boomForce = 900f;

        // Eclairage, reglable depuis la console.
        private static bool _lightsEnabled = true;
        private static float _sunIntensity = 1.1f;

        // 140, pas 14 : l'attenuation en 1/d^2 est brutale. A 10 m d'une
        // lampe, il ne reste qu'un centieme de son intensite. Verifie par
        // capture d'ecran - a 14 la salle etait quasiment noire.
        private static float _lampIntensity = 140f;
        private static float _startShadowStrength = 1f;

        private static int Main(string[] args)
        {
            var options = new EngineOptions { Title = "Exarion Engine - bac a sable", Width = 1280, Height = 720 };
            ParseArguments(args, options);

            if (args.Length > 0)
                Console.WriteLine($"  Arguments : [{string.Join("] [", args)}]");
            if (_autoScreenshotPath != null)
                Console.WriteLine($"  Capture automatique demandee : {_autoScreenshotPath}");

            using var engine = new EngineHost(options);

            engine.Load += () => OnLoad(engine);
            engine.Simulate += (dt, tick) => OnSimulate((float)dt);
            engine.Render += alpha => OnRender(engine, alpha);
            engine.Unload += OnUnload;
            engine.Resized += size =>
            {
                if (size.Y > 0)
                    _camera.AspectRatio = size.X / (float)size.Y;
            };

            // Filet de securite : une exception levee dans un gestionnaire
            // d'evenement (clavier, souris) ne remonte pas forcement par le
            // try ci-dessous. On l'attrape aussi au niveau du domaine.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ue)
                    WriteCrashLog(ue, "exception non geree");
            };

            try
            {
                engine.Run();
            }
            catch (Exception ex)
            {
                WriteCrashLog(ex, "boucle principale");
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
        private static void WriteCrashLog(Exception ex, string origin)
        {
            string report =
                "=== EXARION - RAPPORT DE PLANTAGE ===" + Environment.NewLine +
                $"Date      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                $"Origine   : {origin}" + Environment.NewLine +
                $"Temps sim : {_simTime:0.00} s" + Environment.NewLine +
                $"Caisses   : {_physics?.Bodies.Count ?? -1}" + Environment.NewLine +
                $"Console   : {(_console.IsOpen ? "ouverte" : "fermee")}" + Environment.NewLine +
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

        // -------------------------------------------------------------------

        private static void OnLoad(EngineHost engine)
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
            _render.CreateParticleRenderer(_particles.Capacity);

            // Cube unitaire reutilise pour toutes les caisses : un seul
            // maillage en memoire, une matrice par instance au dessin.
            var cubeVerts = new List<Vertex>();
            var cubeIndices = new List<uint>();
            MeshBuilder.AddBox(cubeVerts, cubeIndices,
                Aabb.FromCenterSize(Vector3.Zero, Vector3.One), new Vector3(0.72f, 0.58f, 0.36f));
            _unitCube = new Mesh(engine.Gl,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cubeVerts),
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cubeIndices));

            // Une forme unitaire par type, construite UNE fois. Les marqueurs
            // de l'editeur les redessinent avec une matrice, exactement comme
            // les caisses reutilisent le cube : reconstruire un maillage par
            // marqueur a chaque image couterait une allocation GPU par entite
            // et par image.
            foreach (MapShape shape in MapShapes.All)
                _unitShapes[(int)shape] = MeshBuilder.UnitShape(engine.Gl, shape, Vector3.One);

            // Texture de developpement : une case par metre, generee en
            // memoire. Aucun fichier a charger ni a distribuer.
            _gridTexture = new Texture(engine.Gl, TextureGen.DevGrid(size: 512, cells: 8));
            _render.Scene.Albedo = _gridTexture;

            StartAudio();

            LoadCharacter(engine.Gl);

            // Le moteur de personnage doit exister AVANT la carte : c'est
            // elle qui lui donne sa position de depart.
            _motor = new CharacterMotor();

            var fb = engine.Window.FramebufferSize;
            _camera.AspectRatio = fb.X / (float)Math.Max(1, fb.Y);

            _keyboard = engine.Input.Keyboards.Count > 0 ? engine.Input.Keyboards[0] : null;
            _mouse = engine.Input.Mice.Count > 0 ? engine.Input.Mice[0] : null;

            if (_mouse != null)
            {
                _mouse.Cursor.CursorMode = CursorMode.Raw;
                _mouse.MouseMove += (_, pos) =>
                {
                    if (_hasLastMouse)
                        _mouseDelta += pos - _lastMousePos;

                    _lastMousePos = pos;
                    _hasLastMouse = true;
                };
            }

            if (_keyboard != null)
            {
                _console.Attach(_keyboard);

                // ⚠️ LA SAISIE DU PANNEAU PASSE PAR KeyChar, PAS PAR KeyDown,
                // exactement comme la console. KeyDown donne une touche
                // PHYSIQUE : sur un clavier francais, Key.Semicolon produit
                // « ; » ou « . » selon Maj, et Key.Q produit un « a ». Deduire
                // le caractere de la touche donnerait un nom de cible faux sur
                // tout clavier non americain - et juste sur le mien.
                _keyboard.KeyChar += (_, c) =>
                {
                    if (_console.IsOpen || _mode != AppMode.Editing)
                        return;

                    if (_panel is { Focused: true, Editing: true })
                        _panel.Type(c);
                };

                // ⚠️ TOUT passe par KeyDown et non par IsKeyPressed pour ce qui
                // doit se declencher UNE FOIS. Une action d'editeur lue par
                // sondage se repeterait soixante fois par seconde : creer une
                // boite en poserait soixante, et l'annulation en garderait
                // soixante traces.
                _keyboard.KeyDown += (_, key, _) =>
                {
                    if (_console.IsOpen)
                        return;

                    switch (_mode)
                    {
                        case AppMode.Menu:
                            MenuKey(engine, key);
                            return;

                        case AppMode.Editing:
                            EditorKey(key);
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
            _console.OpenedOrClosed += open =>
            {
                if (_mouse != null)
                    _mouse.Cursor.CursorMode = open ? CursorMode.Normal : CursorMode.Raw;
            };

            RegisterConsole();

            // APRES RegisterConsole : la carte aligne light_sun et light_lamps
            // sur ce qu'elle declare, et ces ConVar doivent donc exister. Sans
            // cet ordre, la console afficherait les defauts du moteur pendant
            // qu'une carte chargee eclaire tout autrement - exactement le
            // piege des ConVar qui mentent, deja paye deux fois.
            LoadStartupMap(engine.Gl);

            SpawnStack(new Vector3(0f, 0f, 8f), 4);

            RunStartupCommands();

            Console.WriteLine($"  Carte : {_map!.Name} — {_level.Boxes.Count} boites, " +
                              $"{_levelMesh!.IndexCount / 3} triangles, {_map.Lights.Count} lumieres, " +
                              $"{_map.Entities.Count} entites.");
            Console.WriteLine($"          {_mapSource}");

            // La position EFFECTIVE, pas celle declaree : c'est le seul moyen de
            // verifier qu'une entite d'apparition a bien pris le pas sur la
            // propriete spawn de la carte.
            Console.WriteLine($"  Depart : ({_motor.Position.X:0.##}, {_motor.Position.Y:0.##}, " +
                              $"{_motor.Position.Z:0.##})  lacet {_camera.Yaw * 180f / MathF.PI:0.#}");

            Console.WriteLine("  F poser une caisse | V une pile | G explosion | C effacer");
            Console.WriteLine("  K vue des collisions | Clic tirer");
            Console.WriteLine("  F1 CONSOLE (tape 'help')  |  Echap menu");
            Console.WriteLine("--------------------------------------------------");
        }

        // -------------------------------------------------------------------
        //  Variables et commandes exposees a la console.
        //
        //  Tout ce qui touche au FEELING est ici : c'est le seul moyen de le
        //  regler en essayant, plutot qu'en recompilant a chaque essai.
        // -------------------------------------------------------------------

        private const string DefaultConfigName = "exarion.cfg";

        /// <summary>
        /// Chemin du fichier de reglages, a cote de l'executable.
        ///
        /// Un vrai produit le mettrait dans le profil utilisateur. Ici, a
        /// cote du binaire : c'est previsible, ca se trouve, et le dossier
        /// est deja ignore par git - un fichier de reglages personnels n'a
        /// rien a faire dans le depot.
        /// </summary>
        private static string ConfigPath(string[] args)
        {
            string name = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
                ? args[0]
                : DefaultConfigName;

            if (!name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
                name += ".cfg";

            return Path.Combine(AppContext.BaseDirectory, name);
        }

        private static void RegisterConsole()
        {
            ConsoleSystem c = _console.System;
            MotorSettings m = _motor.Settings;

            void Bind(string name, float value, float min, float max, string desc, Action<float> apply)
            {
                var v = c.Register(name, value, min, max, desc);
                v.Changed += cv => apply(cv.Value);
            }

            // --- Deplacement ---
            Bind("speed_run", m.RunSpeed, 0.5f, 30f, "Vitesse de course en m/s", x => m.RunSpeed = x);
            Bind("speed_walk", m.WalkSpeed, 0.5f, 30f, "Vitesse de marche en m/s", x => m.WalkSpeed = x);
            Bind("speed_crouch", m.CrouchSpeed, 0.2f, 30f, "Vitesse accroupi en m/s", x => m.CrouchSpeed = x);
            Bind("jump_height", m.JumpHeight, 0.1f, 10f, "Hauteur de saut en m", x => m.JumpHeight = x);
            Bind("gravity", m.Gravity, 0f, 100f, "Pesanteur en m/s2 (joueur ET caisses)", x =>
            {
                m.Gravity = x;
                _physics.Gravity = new Vector3(0f, -x, 0f);
                _physics.WakeAll();
            });
            Bind("accel_ground", m.GroundAcceleration, 0.5f, 100f, "Acceleration au sol", x => m.GroundAcceleration = x);
            Bind("accel_air", m.AirAcceleration, 0f, 50f, "Acceleration en l'air", x => m.AirAcceleration = x);
            Bind("friction", m.Friction, 0f, 50f, "Friction au sol du joueur", x => m.Friction = x);
            Bind("step_height", m.StepHeight, 0f, 2f, "Hauteur de marche franchie", x => m.StepHeight = x);

            // --- Vue ---
            Bind("fov", _camera.HorizontalFovDegrees, 60f, 130f, "Champ de vision HORIZONTAL",
                 x => _camera.HorizontalFovDegrees = x);
            Bind("sensitivity", _mouseSensitivity, 0.0002f, 0.02f, "Sensibilite souris",
                 x => _mouseSensitivity = x);
            Bind("look_speed", 130f, 10f, 500f, "Vitesse des fleches, en degres par seconde",
                 x => _keyLookSpeed = x * MathF.PI / 180f);

            // --- Caisses ---
            Bind("crate_mass", 18f, 0.5f, 500f, "Masse des nouvelles caisses", x => _crateMass = x);
            Bind("crate_size", CrateSize, 0.2f, 4f, "Taille des nouvelles caisses", x => _crateSize = x);
            Bind("bullet_force", 260f, 0f, 5000f, "Poussee d'une balle sur une caisse", x => _bulletForce = x);
            Bind("boom_radius", 7f, 0.5f, 50f, "Rayon d'explosion", x => _boomRadius = x);
            Bind("boom_force", 900f, 0f, 10000f, "Force d'explosion", x => _boomForce = x);
            Bind("phys_iterations", _physics.SolverIterations, 1f, 40f,
                 "Passes de degagement entre caisses", x => _physics.SolverIterations = (int)x);
            Bind("phys_velocity_iterations", _physics.VelocityIterations, 1f, 60f,
                 "Passes de vitesse du solveur", x => _physics.VelocityIterations = (int)x);
            Bind("phys_sleep", _physics.SleepSeconds, 0.05f, 5f,
                 "Delai avant qu'une caisse s'endorme", x => _physics.SleepSeconds = x);
            Bind("perf", 0f, 0f, 1f,
                 "Cout du pas physique, une ligne par seconde", x => _perfEnabled = x >= 0.5f);

            // --- Occlusion ambiante ---
            // --- Bloom ---
            Bind("bloom", 1f, 0f, 1f, "Halo autour des sources vives",
                 x => _bloomEnabled = x >= 0.5f);
            Bind("bloom_threshold", 1f, 0.05f, 6f, "Luminance a partir de laquelle ca deborde",
                 x => { if (_bloom != null) _bloom.Threshold = x; });
            Bind("bloom_knee", 0.5f, 0.01f, 2f, "Douceur du seuil, contre le clignotement",
                 x => { if (_bloom != null) _bloom.Knee = x; });
            Bind("bloom_intensity", 1f, 0f, 4f, "Force du halo ajoute",
                 x => { if (_hdr != null) _hdr.BloomIntensity = x; });
            Bind("bloom_passes", 5f, 1f, 12f, "Largeur du halo",
                 x => { if (_bloom != null) _bloom.Passes = (int)x; });

            Bind("ssao", 1f, 0f, 1f, "Occlusion ambiante : la comparaison qui compte",
                 x => _ssaoEnabled = x >= 0.5f);
            Bind("ssao_debug", 0f, 0f, 3f,
                 "Voir le tampon SSAO : 0 rien, 1 normales, 2 profondeur, 3 occlusion",
                 x => _ssaoDebug = (int)x);
            Bind("ssao_debug_range", 30f, 1f, 200f,
                 "Distance affichee en blanc dans la vue de profondeur",
                 x => _ssaoDebugRange = x);
            // Les valeurs initiales doivent COINCIDER avec celles de la classe
            // Ssao : le setter n'est appele qu'au changement, et une console
            // qui affiche autre chose que la realite est un piege.
            Bind("ssao_radius", 0.8f, 0.02f, 4f, "Rayon d'echantillonnage SSAO, en metres",
                 x => { if (_ssao != null) _ssao.Radius = x; });
            Bind("ssao_bias", 0.03f, 0f, 0.5f, "Marge contre l'auto-occlusion",
                 x => { if (_ssao != null) _ssao.Bias = x; });
            Bind("ssao_strength", 1.3f, 0f, 3f, "Intensite de l'occlusion",
                 x => { if (_ssao != null) _ssao.Strength = x; });
            Bind("ssao_samples", 24f, 1f, 32f, "Echantillons par pixel",
                 x => { if (_ssao != null) _ssao.SampleCount = (int)x; });

            // --- Personnage importe ---
            //
            // Pouvoir le tourner en direct est le seul moyen de verifier une
            // geometrie sans la juger sur une seule face.
            Bind("character_yaw", 0f, -180f, 180f, "Orientation du personnage, en degres",
                 x => _characterYaw = x * MathF.PI / 180f);
            Bind("character_z", 15f, -60f, 60f, "Position du personnage sur Z",
                 x => _characterPosition.Z = x);
            Bind("character_anim", 0f, 0f, 8f, "Cycle de repli si les noms ne sont pas reconnus",
                 x => { _characterAnimation = (int)x; SelectClips(); });
            Bind("character_speed", 1f, 0f, 4f, "Vitesse de l'animation. 0 fige la pose",
                 x => _characterSpeed = x);

            // Melange de poses (2.2). 0 = marche, 1 = course, et TOUT ce qu'il
            // y a entre les deux - qu'aucun animateur n'a eu a dessiner.
            Bind("character_blend", 0f, 0f, 1f, "Melange marche (0) vers course (1)",
                 x => _characterBlend = x);

            // Le decalage de pied ne se calcule pas : il depend de la jambe
            // sur laquelle chaque export demarre. Seul un oeil peut le regler.
            Bind("character_offset", 0f, 0f, 1f, "Decalage du cycle de course, en fractions de pas",
                 x => _characterOffset = x);

            // -1 laisse courir l'animation. Toute autre valeur fige la phase,
            // ce qui est le SEUL moyen de comparer deux melanges au meme
            // instant de la foulee - leurs cadences etant differentes.
            Bind("character_phase", -1f, -1f, 1f, "Phase imposee du pas, -1 pour laisser courir",
                 x => _characterPhase = x);

            // --- Particules (2.3) ---
            Bind("particles", 1f, 0f, 1f, "Afficher et simuler les particules",
                 x => _particlesEnabled = x >= 0.5f);
            Bind("fx_impact", 1f, 0.2f, 4f, "Ampleur des impacts de balle",
                 x => _impactScale = x);

            // Les effets se declenchent normalement a la souris ou au clavier.
            // Ces commandes existent pour le mode headless : sans elles, aucune
            // capture d'ecran ne peut montrer une particule.
            c.Register("fx_explode", args =>
            {
                Vector3 at = ParsePoint(args, AimPointOnFloor() + new Vector3(0f, 0.6f, 0f));
                float scale = args.Length >= 4 ? ParseFloat(args[3], 1f) : 1f;

                ParticleEffects.Explosion(_particles, ref _fxRng, at, scale);
                c.Print($"  explosion en ({at.X:0.##}, {at.Y:0.##}, {at.Z:0.##}), echelle {scale:0.##}");
            }, "Explosion. fx_explode [x y z] [echelle]");

            c.Register("fx_impact_at", args =>
            {
                Vector3 at = ParsePoint(args, AimPointOnFloor());

                ParticleEffects.Impact(_particles, ref _fxRng, at, Vector3.UnitY, _impactScale);
                c.Print($"  impact en ({at.X:0.##}, {at.Y:0.##}, {at.Z:0.##})");
            }, "Impact vers le haut. fx_impact_at [x y z]");

            c.Register("fx_smoke", args =>
            {
                Vector3 at = ParsePoint(args, AimPointOnFloor() + new Vector3(0f, 0.8f, 0f));
                int count = args.Length >= 4 ? (int)ParseFloat(args[3], 20f) : 20;

                ParticleEffects.Smoke(_particles, ref _fxRng, at, count);
                c.Print($"  fumee en ({at.X:0.##}, {at.Y:0.##}, {at.Z:0.##}), {count} bouffees");
            }, "Fumee. fx_smoke [x y z] [nombre]");

            c.Register("fx_clear", _ =>
            {
                _particles.Clear();
                c.Print("  particules effacees");
            }, "Vide le reservoir de particules");

            c.Register("fx", _ =>
                c.Print($"  {_particles.Count} / {_particles.Capacity} particules, " +
                        $"{_particleRenderer?.LastDrawn ?? 0} dessinees, " +
                        $"{_particles.Dropped} refusees faute de place"),
                "Etat du reservoir de particules");

            // --- Etats et editeur (4.1) ---
            //
            // Ces commandes existent d'abord pour la VERIFICATION : sans elles,
            // l'editeur ne serait atteignable qu'au clavier, donc impossible a
            // capturer en mode headless - donc impossible a verifier sans un
            // humain devant l'ecran.
            c.Register("menu", _ => { EnterMenu(); c.Print("  menu"); }, "Revient au menu");
            c.Register("play", _ => { EnterPlay(); c.Print("  partie"); }, "Reprend la partie");

            c.Register("editor", _ =>
            {
                EnterEditor();
                c.Print(_editor is null ? "  aucune carte a editer" : "  editeur");
            }, "Passe dans l'editeur de niveau");

            c.Register("ed_select", args =>
            {
                if (_editor is null) { c.Print("  editeur inactif"); return; }

                if (args.Length >= 1 && int.TryParse(args[0], out int index))
                    _editor.Select(index);
                else
                    _editor.Deselect();

                c.Print(_editor.HasSelection
                    ? $"  boite {_editor.Selected} selectionnee"
                    : "  rien de selectionne");
            }, "ed_select [n] - selectionne une boite, sans argument deselectionne");

            // ⚠️ Cette commande existe d'abord pour la VERIFICATION. Sans elle,
            // la forme ne se changerait qu'au clavier, donc les formes seraient
            // inverifiables en headless - meme raison que toutes les autres
            // commandes ed_*.
            c.Register("ed_shape", args =>
            {
                if (_editor is null) { c.Print("  editeur inactif"); return; }

                if (args.Length == 0)
                {
                    c.Print($"  forme des prochains objets : {MapShapes.Name(_editor.NewShape)}");
                    c.Print("  formes : " + string.Join(", ", Array.ConvertAll(MapShapes.All, MapShapes.Name)));

                    if (_editor.HasSelection)
                        c.Print($"  selection : {MapShapes.Name(_editor.Map.Boxes[_editor.Selected].Shape)}");

                    return;
                }

                if (!MapShapes.TryParse(args[0], out MapShape shape))
                {
                    c.Print($"  forme inconnue \"{args[0]}\"");
                    return;
                }

                _editor.NewShape = shape;

                // La selection suit, s'il y en a une : c'est le comportement
                // attendu quand on regarde un objet en tapant la commande.
                if (_editor.SetShape(shape))
                    _editorGeometryDirty = true;

                c.Print($"  forme {MapShapes.Name(shape)}" +
                        (_editor.HasSelection ? " - appliquee a la selection" : ""));

                c.Print("  ⚠ la COLLISION reste la boite englobante : une rampe ne se monte pas");
            }, "ed_shape [bloc|sphere|cylindre|rampe|cone] - forme des objets crees, et de la selection");

            c.Register("ed_axis", args =>
            {
                if (_editor is null) { c.Print("  editeur inactif"); return; }

                _editor.Axis = args.Length >= 1 && args[0].Length > 0
                    ? char.ToLowerInvariant(args[0][0]) switch
                    {
                        'y' => EditAxis.Y,
                        'z' => EditAxis.Z,
                        _ => EditAxis.X,
                    }
                    : EditAxis.X;

                c.Print($"  axe {_editor.Axis}");
            }, "ed_axis x|y|z");

            c.Register("ed_move", args =>
            {
                if (_editor is null) { c.Print("  editeur inactif"); return; }

                int steps = args.Length >= 1 && int.TryParse(args[0], out int n) ? n : 1;

                if (_editor.Move(_editor.Axis, steps)) _editorGeometryDirty = true;
                c.Print($"  deplacee de {steps} pas sur {_editor.Axis}");
            }, "ed_move [pas] - deplace la selection sur l'axe actif");

            c.Register("ed_size", args =>
            {
                if (_editor is null) { c.Print("  editeur inactif"); return; }

                int steps = args.Length >= 1 && int.TryParse(args[0], out int n) ? n : 1;

                if (_editor.Resize(_editor.Axis, steps)) _editorGeometryDirty = true;
                c.Print($"  redimensionnee de {steps} pas sur {_editor.Axis}");
            }, "ed_size [pas] - redimensionne la selection sur l'axe actif");

            c.Register("ed_undo", _ =>
            {
                if (_editor is null) { c.Print("  editeur inactif"); return; }
                c.Print(_editor.Undo() ? "  annule" : "  rien a annuler");
                _editorGeometryDirty = true;
            }, "Annule la derniere operation");

            c.Register("ed_redo", _ =>
            {
                if (_editor is null) { c.Print("  editeur inactif"); return; }
                c.Print(_editor.Redo() ? "  retabli" : "  rien a retablir");
                _editorGeometryDirty = true;
            }, "Retablit l'operation annulee");

            c.Register("ed_save", _ => SaveEditedMap(), "Enregistre la carte editee");

            c.Register("ed_entity", args =>
            {
                if (_editor is null) { c.Print("  editeur inactif"); return; }

                if (args.Length == 0)
                {
                    c.Print("  types : " + string.Join(", ", EntityTypes.All));
                    return;
                }

                Vector3 at = args.Length >= 4
                    ? ParsePoint(args[1..], AimPointOnFloor())
                    : AimPointOnFloor();

                int index = _editor.AddEntity(args[0], at,
                                              _camera.Yaw * 180f / MathF.PI,
                                              _camera.Pitch * 180f / MathF.PI);

                c.Print($"  entite {index} : {args[0]} en ({at.X:0.##}, {at.Y:0.##}, {at.Z:0.##})");
            }, "ed_entity <type> [x y z] - pose une entite. Sans argument, liste les types");

            c.Register("ed_ent_select", args =>
            {
                if (_editor is null) { c.Print("  editeur inactif"); return; }

                if (args.Length >= 1 && int.TryParse(args[0], out int index))
                    _editor.SelectEntity(index);
                else
                    _editor.Deselect();

                c.Print(_editor.SelectedEntity is MapEntity e
                    ? $"  entite {_editor.Selected} : {e.Type}"
                    : "  rien de selectionne");
            }, "ed_ent_select [n] - selectionne une entite");

            c.Register("ed_prop", args =>
            {
                if (_editor is null) { c.Print("  editeur inactif"); return; }

                if (args.Length < 2)
                {
                    if (_editor.SelectedEntity is MapEntity current)
                        foreach (var pair in current.Properties)
                            c.Print($"  {pair.Key} = {pair.Value}");
                    else
                        c.Print("  aucune entite selectionnee");

                    return;
                }

                c.Print(_editor.SetProperty(args[0], args[1])
                    ? $"  {args[0]} = {args[1]}"
                    : "  aucune entite selectionnee");
            }, "ed_prop <cle> <valeur> - ecrit une propriete. Sans argument, les liste");

            c.Register("ent", _ =>
            {
                c.Print($"  {_entities.Map.Entities.Count} entites, " +
                        $"{_entities.TotalEvents} declenchements depuis le chargement");

                if (_entities.Checkpoints.Count > 0)
                    c.Print("  checkpoints : " + string.Join(" > ", _entities.Checkpoints));
                else
                    c.Print("  aucun checkpoint franchi");
            }, "Etat des entites vivantes");

            c.Register("npc", _ =>
            {
                var agents = _agents.Agents;

                if (agents.Count == 0) { c.Print("  aucun PNJ dans la carte"); return; }

                for (int i = 0; i < agents.Count; i++)
                {
                    Agent a = agents[i];

                    c.Print($"  {i,2}  {(a.Name.Length > 0 ? a.Name : "(sans nom)"),-12} " +
                            $"{a.State,-6}  {(a.SeesPlayer ? "VOIT" : "    ")}  " +
                            $"pos {a.Position.X:0.#} {a.Position.Y:0.#} {a.Position.Z:0.#}  " +
                            $"{a.Speed:0.0} m/s  point {a.Waypoint}");
                }

                c.Print($"  {_agents.TotalEvents} evenements de PNJ depuis le chargement");
            }, "Etat de chaque PNJ");

            c.Register("ent_rearm", _ =>
            {
                _entities.Rearm();
                c.Print("  declencheurs rearmes, checkpoints effaces");
            }, "Rearme tous les declencheurs");

            // --- E17 : les entites qui AGISSENT ------------------------------
            //
            // ⚠️ Ces trois commandes existent d'abord POUR LA VERIFICATION.
            // Sans elles, tester une porte demanderait de courir jusqu'a son
            // declencheur, ce qui est impossible en headless : les --exec
            // partent tous a t=0, avant que le joueur ait bouge.

            c.Register("ent_send", args =>
            {
                if (args.Length < 2)
                {
                    c.Print("  ent_send <cible> <entree>");
                    return;
                }

                EntityDelivery delivery = _entities.Send(args[0], args[1]);

                // On DIT ce qui s'est passe, y compris les echecs, et on les
                // distingue : une cible absente et une entree inconnue sont
                // deux fautes de frappe differentes.
                c.Print(delivery switch
                {
                    EntityDelivery.Delivree => $"  {args[0]}.{args[1]} : delivre",
                    EntityDelivery.CibleAbsente => $"  {args[0]} : CIBLE INTROUVABLE",
                    EntityDelivery.EntreeInconnue => $"  {args[0]} existe mais ne comprend pas « {args[1]} »",
                    _ => "  rien a viser",
                });
            }, "ent_send <cible> <entree> - envoie une entree a la main (Ouvrir, Fermer, Basculer)");

            c.Register("movers", _ =>
            {
                var movers = _movers.Movers;

                if (movers.Count == 0) { c.Print("  aucune porte dans la carte"); return; }

                for (int i = 0; i < movers.Count; i++)
                {
                    Mover m = movers[i];

                    c.Print($"  {i,2}  {(m.Name.Length > 0 ? m.Name : "(sans nom)"),-14} " +
                            $"{m.State,-10} {m.Fraction * 100f,5:0.0} %  " +
                            $"centre {m.Center.X:0.##} {m.Center.Y:0.##} {m.Center.Z:0.##}  " +
                            $"{m.Speed:0.0} m/s" + (m.Degenerate ? "  [DISTANCE NULLE]" : ""));
                }

                c.Print($"  {_movers.MovingCount} en mouvement");
            }, "Etat de chaque porte / ascenseur");

            // ================================================================
            //  RESEAU (phase 3)
            // ================================================================

            c.Register("host", args =>
            {
                int port = args.Length >= 1 && int.TryParse(args[0], out int p) ? p : 27015;

                StopNetwork();

                try
                {
                    _serverTransport = new UdpTransport(port);
                    _netServer = new NetServer(_serverTransport) { FixedDelta = _fixedDelta };
                    _netServer.LoadMap(_map ?? BuildSandboxMap());

                    c.Print($"  serveur ouvert sur le port {_serverTransport.Port}");
                    c.Print($"  carte : {_netServer.Map.Name}");

                    // ⚠️ L'HOTE SE CONNECTE A SON PROPRE SERVEUR PAR LE RESEAU,
                    // sur 127.0.0.1. Il pourrait lire l'etat du serveur
                    // directement - et ce serait un piege : l'hote aurait un
                    // comportement different des autres joueurs, donc des defauts
                    // qui n'apparaitraient que chez eux.
                    ConnectTo("127.0.0.1", _serverTransport.Port, c);
                }
                catch (Exception ex)
                {
                    StopNetwork();
                    c.Print($"  impossible d'ouvrir le port {port} : {ex.Message}");
                }
            }, "host [port] - heberge une partie (27015 par defaut) et s'y connecte");

            c.Register("connect", args =>
            {
                if (args.Length < 1)
                {
                    c.Print("  connect <hote> [port]");
                    return;
                }

                int port = args.Length >= 2 && int.TryParse(args[1], out int p) ? p : 27015;

                // On garde le serveur s'il tourne : on peut heberger et se
                // connecter ailleurs, c'est utile pour tester.
                _netClient?.Dispose();
                _netClient = null;
                _clientTransport?.Dispose();
                _clientTransport = null;

                ConnectTo(args[0], port, c);
            }, "connect <hote> [port] - rejoint un serveur");

            c.Register("disconnect", _ =>
            {
                StopNetwork();
                c.Print("  reseau ferme");
            }, "Ferme la connexion et le serveur local");

            c.Register("net", _ =>
            {
                if (_netServer is null && _netClient is null)
                {
                    c.Print("  hors ligne. « host » pour heberger, « connect <ip> » pour rejoindre");
                    return;
                }

                if (_netServer is { } server)
                {
                    c.Print($"  SERVEUR  port {_serverTransport?.Port}  pas {server.Tick}  " +
                            $"{server.ClientCount} client(s)");
                    c.Print($"           emis {server.BytesSent / 1024.0:0.0} ko  " +
                            $"instantanes {server.SnapshotsSent}  " +
                            $"entrees {server.InputsApplied}  repetees {server.InputsRepeated}");

                    var names = server.PlayerNames();

                    for (int i = 0; i < names.Count; i++)
                        c.Print($"           joueur {i + 1} : {names[i]}");
                }

                if (_netClient is { } client)
                {
                    c.Print($"  CLIENT   {client.State}  id {client.LocalId}  carte « {client.ServerMap} »");

                    if (client.State == NetClientState.Refuse)
                        c.Print($"           refuse : {client.RejectReason}");

                    c.Print($"           pas serveur {client.ServerTick}  accuse {client.AckedInputTick}  " +
                            $"aller-retour {(client.RoundTrip >= 0 ? $"{client.RoundTrip * 1000.0:0} ms" : "-")}");

                    c.Print($"           prediction {(client.PredictionEnabled ? "ON" : "OFF")}  " +
                            $"corrections {client.Corrections}  rejeux {client.Replays}  " +
                            $"ecart {client.LastPredictionError * 100f:0.0} cm  " +
                            $"pire {client.WorstPredictionError * 100f:0.0} cm");

                    c.Print($"           instantanes recus {client.SnapshotsReceived}  " +
                            $"jetes {client.SnapshotsDiscarded}  " +
                            $"en tampon {client.BufferedSnapshots}  " +
                            $"emis {client.BytesSent / 1024.0:0.0} ko");

                    for (int i = 0; i < client.Players.Count; i++)
                    {
                        PlayerState s = client.Players[i];

                        c.Print($"           vu id {s.Id}{(s.Id == client.LocalId ? " (moi)" : "")}  " +
                                $"{s.Position.X:0.00} {s.Position.Y:0.00} {s.Position.Z:0.00}");
                    }
                }
            }, "Etat du reseau : serveur, client, debit, aller-retour");

            c.Register("net_predict", args =>
            {
                if (_netClient is null) { c.Print("  hors ligne"); return; }

                if (args.Length >= 1
                    && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    _netClient.PredictionEnabled = v != 0f;

                // ⚠️ Existe pour COMPARER : couper la prediction en direct est
                // le seul moyen de sentir ce qu'elle apporte. Sans elle, chaque
                // pas se voit un aller-retour apres l'appui.
                c.Print($"  net_predict {(_netClient.PredictionEnabled ? 1 : 0)}");
            }, "net_predict [0|1] - coupe ou remet la prediction, pour comparer");

            c.Register("net_name", args =>
            {
                if (args.Length >= 1)
                    _netName = args[0];

                c.Print($"  net_name {_netName}");
            }, "net_name <nom> - nom annonce au serveur");

            c.Register("carry_debug", _ =>
            {
                _carryDebug = !_carryDebug;
                c.Print("  carry_debug " + (_carryDebug ? "1" : "0"));
            }, "Trace le portage du joueur par les plateformes");

            c.Register("ent_targets", _ =>
            {
                var receivers = _entities.Receivers;

                if (receivers.Count == 0) { c.Print("  aucune cible inscrite"); return; }

                for (int i = 0; i < receivers.Count; i++)
                    c.Print($"  {receivers[i].Name}");
            }, "Liste les noms qu'une sortie peut viser");

            c.Register("ed_list", _ =>
            {
                if (_editor is null) { c.Print("  editeur inactif"); return; }

                var entities = _editor.Map.Entities;

                if (entities.Count == 0) { c.Print("  aucune entite"); return; }

                for (int i = 0; i < entities.Count; i++)
                    c.Print($"  {i,3}  {entities[i].Type,-22} " +
                            $"{entities[i].Position.X:0.##} {entities[i].Position.Y:0.##} {entities[i].Position.Z:0.##}");
            }, "Liste les entites de la carte");

            Bind("ed_grid", 1f, 0.125f, 8f, "Pas d'accrochage de l'editeur, en metres",
                 x =>
                 {
                     if (_editor is null) return;

                     // On prend le pas disponible le plus proche : la liste est
                     // volontairement discrete, un pas libre casserait
                     // l'alignement de tout ce qui existe deja.
                     int best = 0;
                     for (int i = 1; i < EditorState.GridSteps.Length; i++)
                         if (MathF.Abs(EditorState.GridSteps[i] - x) < MathF.Abs(EditorState.GridSteps[best] - x))
                             best = i;

                     while (_editor.GridSize > EditorState.GridSteps[best]) _editor.CycleGrid(-1);
                     while (_editor.GridSize < EditorState.GridSteps[best]) _editor.CycleGrid(+1);

                     RequestGridMesh();
                 });

            Bind("ed_show_grid", 1f, 0f, 1f, "Afficher la trame au sol", x => _showGrid = x >= 0.5f);

            // --- Profileur GPU ---
            //
            // Coupe par defaut : deux horodatages par passe et par image, ce
            // n'est pas gratuit, et on ne mesure pas en permanence.
            Bind("gpu", 0f, 0f, 1f, "Chronometre chaque passe de rendu (rapport avec perf 1)",
                 x => { if (_gpu != null) _gpu.Enabled = x >= 0.5f; });

            // --- Audio (2.1) ---
            // ⚠️ La valeur initiale doit COINCIDER avec le champ d'AudioMixer.
            // Le setter n'est appele qu'au changement : avec 0,7 ici et 1,0
            // dans le melangeur, la console affichait 0,7 et le son sortait a
            // 1,0. Piege deja rencontre avec les ConVar de la SSAO, et attrape
            // ici par la MESURE du fichier WAV - pic de 0,30 la ou 0,7 de
            // volume en aurait donne 0,21.
            Bind("snd_volume", 1f, 0f, 1f, "Volume general",
                 x => { if (_mixer != null) _mixer.MasterGain = x; });

            c.Register("snd", _ =>
            {
                if (_audio is null || _mixer is null)
                {
                    c.Print("  audio indisponible");
                    return;
                }

                c.Print($"  {_audio.Name}, {_audio.SampleRate} Hz");
                c.Print($"  {_audio.Callbacks} tampons remplis, {_audio.Faults} erreurs");
                c.Print($"  {_mixer.ActiveVoices} / {_mixer.MaxVoices} voix, " +
                        $"{_mixer.Dropped} refusees, pic {_mixer.LastPeak:0.00}");
            }, "Etat de la carte son et du melangeur");

            c.Register("snd_test", args =>
            {
                float frequency = args.Length >= 1 ? ParseFloat(args[0], 440f) : 440f;
                float pan = args.Length >= 2 ? ParseFloat(args[1], 0f) : 0f;

                if (_audio is null) { c.Print("  audio indisponible"); return; }

                PlaySound(SoundGen.Tone(frequency, 0.5f, _audio.SampleRate), 0.6f, pan);
                c.Print($"  note a {frequency:0} Hz, panoramique {pan:0.00}");
            }, "Joue une note. snd_test [hz] [panoramique -1..1]");

            c.Register("snd_impact", args =>
            {
                float pan = args.Length >= 1 ? ParseFloat(args[0], 0f) : 0f;
                PlaySound(_clipImpact, 0.9f, pan);
                c.Print($"  impact, panoramique {pan:0.00}");
            }, "Joue un impact. snd_impact [panoramique]");

            // --- Spatialisation (S3) et occlusion (S4) ---
            Bind("snd_spatial", 1f, 0f, 1f, "Spatialiser les sons du monde",
                 x => { if (_sound != null) _sound.SpatializationEnabled = x >= 0.5f; });
            Bind("snd_occlusion", 1f, 0f, 1f, "Etouffer les sons derriere un mur",
                 x => { if (_sound != null) _sound.OcclusionEnabled = x >= 0.5f; });
            Bind("snd_ref", 1.5f, 0.1f, 20f, "Distance en deca de laquelle le son est a plein volume",
                 x => { if (_sound != null) { var f = _sound.Falloff; f.ReferenceDistance = x; _sound.Falloff = f; } });
            Bind("snd_max", 60f, 5f, 500f, "Distance au-dela de laquelle le son ne faiblit plus",
                 x => { if (_sound != null) { var f = _sound.Falloff; f.MaxDistance = x; _sound.Falloff = f; } });
            Bind("snd_rolloff", 1f, 0f, 5f, "Raideur de l'attenuation. 1 = physique",
                 x => { if (_sound != null) { var f = _sound.Falloff; f.Rolloff = x; _sound.Falloff = f; } });
            Bind("snd_doppler", 1f, 0f, 4f, "Force de l'effet Doppler. 0 le desactive",
                 x => { if (_sound != null) { var f = _sound.Falloff; f.DopplerScale = x; _sound.Falloff = f; } });

            // Un son qui BOUCLE, pose dans le monde. C'est le seul moyen de
            // juger la spatialisation a l'oreille : il faut pouvoir tourner
            // autour et s'en eloigner.
            c.Register("snd_place", args =>
            {
                if (_sound is null || _audio is null) { c.Print("  audio indisponible"); return; }

                _sound.Stop(_placedSound);

                Vector3 at = ParsePoint(args, AimPointOnFloor() + new Vector3(0f, 1f, 0f));

                // La frequence est reglable : c'est indispensable pour VERIFIER
                // l'occlusion, qui coupe les aigus. Un grave passerait le mur
                // presque intact et ne prouverait rien.
                float hz = args.Length >= 4 ? ParseFloat(args[3], 330f) : 330f;

                _placedSound = _sound.PlayAt(SoundGen.Tone(hz, 2f, _audio.SampleRate, 0.5f, 0.15f),
                                             at, gain: 1f, loop: true);

                c.Print($"  source a {hz:0} Hz posee en ({at.X:0.##}, {at.Y:0.##}, {at.Z:0.##})");
            }, "Pose un son en boucle. snd_place [x y z] [hz]");

            c.Register("snd_stop", _ =>
            {
                _sound?.Stop(_placedSound);
                _placedSound = -1;
                c.Print("  source arretee");
            }, "Arrete le son pose");

            // --- Eclairage ---
            Bind("light_enabled", 1f, 0f, 1f, "Allumer les lumieres",
                 x => { _lightsEnabled = x >= 0.5f; _lightsDirty = true; });
            Bind("light_sun", _sunIntensity, 0f, 10f, "Intensite de la lumiere generale",
                 x => { _sunIntensity = x; _lightsDirty = true; });
            Bind("light_lamps", _lampIntensity, 0f, 200f, "Intensite des lampes",
                 x => { _lampIntensity = x; _lightsDirty = true; });
            Bind("light_ambient", 0.30f, 0f, 1f, "Ambiante globale (ciel + sol)",
                 x => { if (_scene != null) _scene.Ambient = new Vector3(x); });
            Bind("sky_sun_size", 24f, 1f, 400f, "Concentration du halo solaire",
                 x => { if (_sky != null) _sky.SunSize = x; });
            Bind("sky_brightness", 1f, 0f, 4f, "Luminosite du ciel",
                 x =>
                 {
                     if (_sky == null) return;
                     _sky.Zenith = new Vector3(0.045f, 0.075f, 0.16f) * x;
                     _sky.Horizon = new Vector3(0.22f, 0.26f, 0.34f) * x;
                     _sky.SunColor = new Vector3(1.0f, 0.85f, 0.62f) * x;
                 });
            // --- Materiau (PBR metallic-roughness) ---
            Bind("mat_roughness", 0.75f, 0.02f, 1f,
                 "Rugosite : 0 = miroir, 1 = mat",
                 x => { if (_scene != null) _scene.DefaultMaterial.Roughness = x; });
            Bind("mat_metallic", 0f, 0f, 1f,
                 "Metallicite : 0 = beton/plastique, 1 = metal",
                 x => { if (_scene != null) _scene.DefaultMaterial.Metallic = x; });
            Bind("exposure", 1f, 0.05f, 8f, "Exposition globale",
                 x => { if (_scene != null) _scene.Exposure = x; });

            // --- Ombres ---
            // Tout est reglable : ces defauts se corrigent a l'oeil, et
            // recompiler a chaque essai rendrait le reglage impraticable.
            Bind("shadow_strength", _startShadowStrength, 0f, 1f, "Intensite des ombres. 0 = desactivees",
                 x => { if (_scene != null) _scene.ShadowStrength = x; });
            if (_scene != null) _scene.ShadowStrength = _startShadowStrength;
            Bind("shadow_bias", 0.0025f, 0.0f, 0.05f,
                 "Decalage anti-acne. Trop grand = ombres decollees de l'objet",
                 x => { if (_scene != null) _scene.ShadowBias = x; });
            Bind("shadow_softness", 1f, 0f, 4f, "Rayon du filtre PCF en texels. 1 = 3x3, 2 = 5x5",
                 x => { if (_scene != null) _scene.ShadowSoftness = (int)x; });
            Bind("shadow_extent", 45f, 5f, 200f, "Demi-cote de la zone couverte, en metres",
                 x => { if (_shadows != null) _shadows.Extent = x; });
            Bind("shadow_res", 2048f, 256f, 8192f, "Resolution de la carte d'ombre",
                 x => { if (_shadows != null) _shadows.Resize((uint)x); });
            Bind("shadow_cullfront", 1f, 0f, 1f,
                 "Ne rendre que les faces arriere dans la passe d'ombre (reduit l'acne)",
                 x => { if (_shadows != null) _shadows.CullFrontFaces = x >= 0.5f; });

            // --- Textures ---
            Bind("tex_enabled", 1f, 0f, 1f, "Appliquer la texture de developpement",
                 x => { if (_scene != null) _scene.Albedo = x >= 0.5f ? _gridTexture : null; });
            Bind("tex_scale", 8f, 0.5f, 64f, "Metres par repetition de texture",
                 x => { if (_scene != null) _scene.UvPerMeter = 1f / x; });

            c.Register("screenshot", args =>
            {
                _screenshotTarget = args.Length > 0 ? args[0] : "capture.png";
                if (!_screenshotTarget.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    _screenshotTarget += ".png";

                _screenshotRequested = true;
                c.Print("  capture demandee...");
            }, "screenshot [fichier] - enregistre l'image courante en PNG");

            // --- Affichage ---
            Bind("wireframe", 0f, 0f, 1f, "Vue des collisions en fil de fer", x => _wireframe = x >= 0.5f);
            Bind("showstats", 1f, 0f, 1f, "Compteurs a l'ecran", x => _showStats = x >= 0.5f);

            // --- Commandes ---
            c.Register("spawn", args =>
            {
                int n = args.Length > 0 && int.TryParse(args[0], out int k) ? Math.Clamp(k, 1, 100) : 1;
                for (int i = 0; i < n; i++) SpawnCrateAhead();
                c.Print($"  {n} caisse(s) posee(s), total {_physics.Bodies.Count}");
            }, "spawn [n] - pose des caisses devant soi");

            c.Register("stack", args =>
            {
                int n = args.Length > 0 && int.TryParse(args[0], out int k) ? Math.Clamp(k, 1, 60) : 5;
                SpawnStack(AimPointOnFloor(), n);
                c.Print($"  pile de {n}, total {_physics.Bodies.Count}");
            }, "stack [n] - lache une pile a l'endroit vise");

            c.Register("boom", _ =>
            {
                Vector3 at = AimPointOnFloor() + new Vector3(0f, 0.5f, 0f);
                _physics.Explode(at, _boomRadius, _boomForce);
                c.Print($"  explosion rayon {_boomRadius:0.#} force {_boomForce:0}");
            }, "boom - explosion a l'endroit vise");

            c.Register("crates", args =>
            {
                if (args.Length > 0 && args[0] == "clear")
                {
                    _physics.Clear();
                    c.Print("  caisses effacees");
                    return;
                }

                int awake = 0;
                foreach (var b in _physics.Bodies) if (!b.Sleeping) awake++;
                c.Print($"  {_physics.Bodies.Count} caisses, {awake} encore simulees");
            }, "crates [clear] - compte ou efface les caisses");

            // --- Cartes (4.2) ---

            c.Register("map", _ =>
            {
                if (_map is null) { c.Print("  aucune carte"); return; }

                c.Print($"  {_map.Name}");
                c.Print($"  {_mapSource}");
                c.Print($"  {_map.Boxes.Count} boites, {_map.Lights.Count} lumieres");
                c.Print($"  depart ({_map.Spawn.X:0.##}, {_map.Spawn.Y:0.##}, {_map.Spawn.Z:0.##})");
                c.Print($"  dossier : {MapsFolder()}");
            }, "Etat de la carte chargee");

            c.Register("map_list", _ =>
            {
                string folder = MapsFolder();

                if (!Directory.Exists(folder)) { c.Print($"  aucun dossier {folder}"); return; }

                string[] files = Directory.GetFiles(folder, "*" + MapFile.Extension);

                if (files.Length == 0) { c.Print("  aucune carte"); return; }

                foreach (string file in files)
                    c.Print("  " + Path.GetFileNameWithoutExtension(file));
            }, "Liste les cartes du dossier maps");

            c.Register("map_save", args =>
            {
                if (_map is null) { c.Print("  aucune carte a enregistrer"); return; }

                string name = args.Length >= 1 ? args[0] : _map.Name.Replace(' ', '_');

                try
                {
                    string path = ResolveMapPath(name);
                    MapFile.SaveTo(_map, path);
                    c.Print($"  {_map.Boxes.Count} boites et {_map.Lights.Count} lumieres ecrites");
                    c.Print($"  {path}");
                }
                catch (Exception ex)
                {
                    c.Print($"  echec : {ex.Message}");
                }
            }, "map_save [nom] - enregistre la carte courante");

            c.Register("map_load", args =>
            {
                if (args.Length < 1) { c.Print("  map_load <nom>"); return; }
                if (_gl is null) { c.Print("  rendu non initialise"); return; }

                try
                {
                    string path = ResolveMapPath(args[0]);
                    MapData loaded = MapFile.LoadFrom(path);

                    // La carte est lue EN ENTIER avant d'etre appliquee : si le
                    // fichier est fautif, l'exception part d'ici et le monde
                    // courant n'a pas ete touche. Un chargement a moitie fait
                    // laisserait un decor mi-ancien mi-nouveau, invisible et
                    // impossible a diagnostiquer.
                    ApplyMap(_gl, loaded, path);

                    c.Print($"  {loaded.Name} : {loaded.Boxes.Count} boites, {loaded.Lights.Count} lumieres");
                }
                catch (Exception ex)
                {
                    c.Print($"  echec : {ex.Message}");
                    c.Print("  la carte courante est intacte");
                }
            }, "map_load <nom> - recharge une carte a chaud");

            c.Register("map_reload", _ =>
            {
                if (_gl is null || !File.Exists(_mapSource)) { c.Print("  la carte courante ne vient pas d'un fichier"); return; }

                try
                {
                    MapData loaded = MapFile.LoadFrom(_mapSource);
                    ApplyMap(_gl, loaded, _mapSource);
                    c.Print($"  {loaded.Name} rechargee : {loaded.Boxes.Count} boites");
                }
                catch (Exception ex)
                {
                    c.Print($"  echec : {ex.Message}");
                    c.Print("  la carte courante est intacte");
                }
            }, "Recharge le fichier de la carte courante");

            c.Register("map_sandbox", _ =>
            {
                if (_gl is null) { c.Print("  rendu non initialise"); return; }

                ApplyMap(_gl, BuildSandboxMap(), "(bac a sable, integre au moteur)");
                c.Print("  bac a sable integre recharge");
            }, "Revient au decor de reference integre au moteur");

            c.Register("tp", args =>
            {
                if (args.Length >= 3
                    && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                    && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    _motor.Position = new Vector3(x, y, z);
                }
                else
                {
                    _motor.Position = new Vector3(0f, 1f, 20f);
                }

                _motor.Velocity = Vector3.Zero;
                _previousFeet = _motor.Position;
                c.Print($"  position ({_motor.Position.X:0.#}, {_motor.Position.Y:0.#}, {_motor.Position.Z:0.#})");
            }, "tp [x y z] - se teleporter, ou revenir au depart");

            // Piloter la camera depuis la console rend les captures
            // reproductibles : sans ca, impossible de comparer deux rendus.
            c.Register("look", args =>
            {
                if (args.Length >= 1
                    && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw))
                {
                    _camera.Yaw = yaw * MathF.PI / 180f;
                }

                if (args.Length >= 2
                    && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float pitch))
                {
                    _camera.Pitch = Math.Clamp(pitch * MathF.PI / 180f, -Camera.MaxPitch, Camera.MaxPitch);
                }

                c.Print($"  vue : lacet {_camera.Yaw * 180f / MathF.PI:0.#} deg, " +
                        $"tangage {_camera.Pitch * 180f / MathF.PI:0.#} deg");
            }, "look <lacet> [tangage] - oriente la camera, en degres");

            // ⚠️ MARCHE AUTOMATIQUE : c'est un OUTIL DE VERIFICATION, au meme
            // titre que « look » juste au-dessus. Les --exec partent tous a
            // t=0 et il n'y a pas de clavier en headless : sans cette commande,
            // impossible de prouver qu'un joueur distant BOUGE - on ne pourrait
            // montrer que deux personnages immobiles.
            c.Register("walk", args =>
            {
                _autoWalk = args.Length == 0
                    || (float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v != 0f);

                c.Print($"  marche automatique {(_autoWalk ? "ON" : "OFF")}");
            }, "walk [0|1] - avance tout droit sans clavier, pour les captures");

            // --- Sauvegarde des reglages ---
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

            c.Print("Exarion Engine - console. Tape 'help' ou 'list'.");

            // Chargement automatique. AVANT les --exec, pour qu'une commande
            // passee en ligne de commande puisse toujours avoir le dernier mot.
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
        /// </summary>
        private static void RunStartupCommands()
        {
            ConsoleSystem c = _console.System;

            foreach (string line in _startupCommands)
            {
                int before = c.Output.Count;
                c.Execute(line);

                for (int i = before; i < c.Output.Count; i++)
                    Console.WriteLine("  exec " + c.Output[i]);
            }
        }

        // -------------------------------------------------------------------

        private static void OnSimulate(float dt)
        {
            _simTime += dt;

            // Le pas fixe, retenu pour le serveur reseau : il doit tourner a la
            // MEME cadence que la simulation locale, sinon les deux decrivent le
            // meme monde a deux vitesses.
            _fixedDelta = dt;

            _console.Update(dt);

            // Console ouverte : le jeu ne recoit RIEN. Sans ca, taper une
            // commande fait tirer et courir en meme temps.
            if (_console.IsOpen)
            {
                _mouseDelta = Vector2.Zero;

                // ⚠️ Les portes continuent de bouger console ouverte, et c'est
                // NECESSAIRE : la console est le seul moyen d'en actionner une
                // en headless. Les figer rendrait « ent_send porte Ouvrir »
                // muet, donc E17 inverifiable sans jouer.
                StepMovers(dt);

                _previousFeet = _motor.Position;
                RebuildWalkable();
                CarryPlayer();
                _motor.Step(default, dt, _walkable);
                _physics.Step(dt);
                _particles.Step(dt);
                return;
            }

            // ⚠️ L'editeur et le menu ne partagent RIEN avec la boucle de jeu.
            // Laisser passer les entrees de jeu ferait tirer au clic de
            // selection et courir le personnage pendant qu'on deplace un mur.
            if (_mode == AppMode.Editing)
            {
                SimulateEditor(dt);
                _particles.Step(dt);
                UpdateListener();
                return;
            }

            if (_mode == AppMode.Menu)
            {
                // Le monde continue de tourner derriere le voile : le menu
                // montre la carte chargee, ce qui evite de charger a l'aveugle.
                _mouseDelta = Vector2.Zero;
                StepMovers(dt);
                _previousFeet = _motor.Position;
                RebuildWalkable();
                CarryPlayer();
                _motor.Step(default, dt, _walkable);
                _physics.Step(dt);
                _particles.Step(dt);
                UpdateListener();
                return;
            }

            _camera.AddLook(-_mouseDelta.X * _mouseSensitivity, -_mouseDelta.Y * _mouseSensitivity);
            _mouseDelta = Vector2.Zero;

            // Fleches : tourner la camera au clavier, EN PLUS de la souris.
            //
            // Utile pour inspecter une scene sans lacher le clavier, et pour
            // tester sur un poste ou la capture de souris gene.
            if (_keyboard != null)
            {
                float turn = 0f, tilt = 0f;

                if (_keyboard.IsKeyPressed(Key.Left)) turn += 1f;
                if (_keyboard.IsKeyPressed(Key.Right)) turn -= 1f;
                if (_keyboard.IsKeyPressed(Key.Up)) tilt += 1f;
                if (_keyboard.IsKeyPressed(Key.Down)) tilt -= 1f;

                if (turn != 0f || tilt != 0f)
                {
                    // Par SECONDE, pas par image : sans le dt, la vitesse de
                    // rotation dependrait du framerate.
                    float step = _keyLookSpeed * dt;
                    _camera.AddLook(turn * step, tilt * step);
                }
            }

            var input = new MoveInput { Yaw = _camera.Yaw };

            if (_keyboard != null)
            {
                float x = 0f, y = 0f;
                if (_keyboard.IsKeyPressed(Key.D)) x += 1f;
                if (_keyboard.IsKeyPressed(Key.Q) || _keyboard.IsKeyPressed(Key.A)) x -= 1f;
                if (_keyboard.IsKeyPressed(Key.Z) || _keyboard.IsKeyPressed(Key.W)) y += 1f;
                if (_keyboard.IsKeyPressed(Key.S)) y -= 1f;

                input.Move = new Vector2(x, y);
                input.Jump = _keyboard.IsKeyPressed(Key.Space);
                input.Crouch = _keyboard.IsKeyPressed(Key.ControlLeft);
                input.Walk = _keyboard.IsKeyPressed(Key.ShiftLeft);

                HandleSandboxKeys();
            }

            // La marche automatique ECRASE le clavier : c'est un outil de
            // capture, on veut un mouvement reproductible et rien d'autre.
            if (_autoWalk)
                input.Move = new Vector2(0f, 1f);

            // ⚠️ LES PORTES AVANCENT AVANT LE JOUEUR, et cet ordre est le coeur
            // de E17. Les trois lignes qui suivent doivent rester dans cet
            // ordre : la porte bouge, sa boite entre dans le monde de collision,
            // PUIS on porte le joueur, PUIS il se deplace lui-meme.
            StepMovers(dt);

            _previousFeet = _motor.Position;
            RebuildWalkable();
            CarryPlayer();
            _motor.Step(input, dt, _walkable);

            // La vitesse VOULUE, surtout pas la vitesse reelle : un joueur qui
            // appuie de face contre une caisse est bloque, donc immobile - et
            // il pousse pourtant de tout son poids. Voir PhysicsWorld.PushWith.
            _physics.PushWith(_motor.GetBox(), _motor.WishVelocity, strength: 3.5f);

            _physicsClock.Restart();
            _physics.Step(dt);
            _physicsClock.Stop();

            ReportPhysicsCost(_physicsClock.Elapsed.TotalMilliseconds, dt);

            HandleShooting();

            // Les particules avancent au PAS FIXE, comme la physique. Les
            // simuler au rendu ferait dependre leur trajectoire du nombre
            // d'images par seconde, et deux captures --seconds 3 ne donneraient
            // plus la meme image.
            _particlesClock.Restart();
            _particles.Step(dt);
            _particlesClock.Stop();

            double ms = _particlesClock.Elapsed.TotalMilliseconds;
            _particlesMsSum += ms;
            _particlesMsMax = Math.Max(_particlesMsMax, ms);

            UpdateListener();

            UpdateEntities(dt);

            // Le reseau EN DERNIER : l'entree envoyee est celle de ce pas, et le
            // serveur a deja avance. Envoyer avant la simulation ferait partir
            // l'entree du pas precedent, soit 15 ms de retard gratuit.
            StepNetwork(dt, input);
        }

        /// <summary>
        /// Fait vivre les entites : volumes, checkpoints, evenements.
        ///
        /// Au PAS FIXE. Au rendu, un declencheur serait teste trois mille fois
        /// par seconde au lieu de soixante-quatre, pour un resultat identique -
        /// on ne traverse pas un volume de trois metres en un millieme de
        /// seconde.
        /// </summary>
        private static void UpdateEntities(float dt)
        {
            _entities.Update(_motor.GetBox());
            ReportEvents(_entities.Events);

            // Les PNJ voient depuis les YEUX du joueur, pas ses pieds : c'est ce
            // qui rend une ligne de vue coherente de part et d'autre.
            _agents.Update(dt, EyePosition);
            ReportEvents(_agents.Events);
        }

        private static void ReportEvents(System.Collections.Generic.IReadOnlyList<EntityEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
                ReportEvent(events[i]);
        }

        private static void ReportEvent(EntityEvent e)
        {
            // Un declenchement DOIT se voir et s'entendre, sinon on ne sait pas
            // si le cablage marche.
            _lastEvent = e.ToString();
            _lastEventUntil = _simTime + 2.5f;

            Console.WriteLine($"  [entite] {_lastEvent}");

            PlaySoundAt(_clipImpact, e.Source.Position, 0.5f);

            if (_particlesEnabled)
                ParticleEffects.Sparks(_particles, ref _fxRng, e.Source.Position, Vector3.UnitY, 10, 4f);
        }

        /// <summary>
        /// Accumule le cout du pas physique et en sort une ligne par seconde.
        ///
        /// On rapporte la MOYENNE et le PIRE : c'est le pire qui fait sauter
        /// un pas, pas la moyenne. Le budget est la duree d'un pas - depasser
        /// veut dire que la simulation prend du retard sur le temps reel.
        /// </summary>
        private static void ReportPhysicsCost(double milliseconds, float dt)
        {
            _physicsMsSum += milliseconds;
            _physicsMsMax = Math.Max(_physicsMsMax, milliseconds);
            _physicsSamples++;

            if (!_perfEnabled || _simTime < _perfNextReport || _physicsSamples == 0)
                return;

            int awake = 0;
            var bodies = _physics.Bodies;
            for (int i = 0; i < bodies.Count; i++)
                if (!bodies[i].Sleeping && !bodies[i].IsStatic) awake++;

            double average = _physicsMsSum / _physicsSamples;
            double budget = dt * 1000.0;

            Console.WriteLine(
                $"[perf] physique {average:0.000} ms/pas (pire {_physicsMsMax:0.000})  " +
                $"budget {budget:0.0} ms -> {average / budget * 100.0:0.0} %  " +
                $"caisses {bodies.Count} dont {awake} actives  contacts {_physics.ContactCount}  " +
                $"passes {_physics.VelocityIterations}v/{_physics.SolverIterations}p");

            // Les particules sont chronometrees a part : melangees a la
            // physique, on ne saurait plus a qui attribuer un pic.
            Console.WriteLine(
                $"[perf] particules {_particlesMsSum / _physicsSamples:0.000} ms/pas " +
                $"(pire {_particlesMsMax:0.000})  " +
                $"{_particles.Count} vivantes, {_particleRenderer?.LastDrawn ?? 0} dessinees, " +
                $"{_particles.Dropped} refusees");

            ReportGpuCost();

            _physicsMsSum = 0;
            _physicsMsMax = 0;
            _particlesMsSum = 0;
            _particlesMsMax = 0;
            _physicsSamples = 0;
            _perfNextReport = _simTime + 1f;
        }

        /// <summary>
        /// Sort une ligne par passe de rendu, en millisecondes GPU.
        ///
        /// ⚠️ NE JAMAIS juger le rendu aux images par seconde. A 3 000 img/s,
        /// une passe a 0,3 ms se perd dans le bruit, et les img/s ne sont
        /// comparables que dos a dos dans une meme session - piege deja paye
        /// plusieurs fois sur ce projet.
        /// </summary>
        private static void ReportGpuCost()
        {
            if (_gpu is null || !_gpu.Enabled)
                return;

            _gpu.Flush();

            var line = new System.Text.StringBuilder("[gpu] ");

            for (int z = 0; z < GpuProfiler.Zones; z++)
            {
                var zone = (GpuZone)z;
                double average = _gpu.Average(zone);

                // Une passe desactivee n'a rien mesure : l'afficher a 0,000
                // noierait les chiffres qui comptent.
                if (average <= 0.0005)
                    continue;

                line.Append($"{GpuProfiler.NameOf(zone)} {average:0.000}  ");
            }

            // Le nombre de relevés est AFFICHE a dessein : une moyenne calculee
            // sur trois mesures n'a pas la meme valeur qu'une moyenne sur mille,
            // et c'est ce chiffre qui dit s'il faut allonger l'anneau.
            line.Append($"|  total {_gpu.AverageTotal:0.000} ms  " +
                        $"({_gpu.SampleCount(GpuZone.Geometry)} releves)");

            Console.WriteLine(line.ToString());
        }

        /// <summary>
        /// Reconstruit le monde sur lequel le joueur bute : decor + caisses.
        ///
        /// On prend l'emprise ALIGNEE de la boite orientee, pas la taille
        /// brute du corps. Pour une caisse droite les deux sont identiques ;
        /// pour une caisse basculee, la seconde serait trop petite et on lui
        /// passerait au travers par un coin.
        ///
        /// C'est genereux - on marche sur l'emprise, pas sur la caisse
        /// inclinee elle-meme - mais c'est le prix d'un joueur en boite
        /// alignee, et ca reste infiniment mieux que de traverser.
        /// </summary>
        private static void RebuildWalkable()
        {
            _walkable.Clear();

            var level = _level.Boxes;
            for (int i = 0; i < level.Count; i++)
                _walkable.Add(level[i]);

            var bodies = _physics.Bodies;
            for (int i = 0; i < bodies.Count; i++)
                _walkable.Add(bodies[i].GetObb().GetBounds());

            // ⚠️ LES PORTES ET ASCENSEURS ENTRENT ICI, et l'ordre compte : cet
            // appel doit venir APRES _movers.Step, sinon la boite de collision
            // date du pas precedent. A 2 m/s une porte se deplace de 3 cm par
            // pas - assez pour qu'un joueur colle contre elle la traverse.
            _movers.AppendTo(_walkable);
        }

        /// <summary>
        /// Fait avancer les portes et rapporte leurs sorties de fin de course.
        ///
        /// Un mover emet OnOuvert / OnFerme quand il arrive : c'est ce qui
        /// permet d'enchainer (une porte qui s'ouvre declenche un garde). Ces
        /// sorties passent par le MEME flux d'evenements que les volumes.
        /// </summary>
        private static void StepMovers(float dt)
        {
            _movers.Step(dt);

            var movers = _movers.Movers;

            for (int i = 0; i < movers.Count; i++)
            {
                if (movers[i].PendingOutput.Length == 0)
                    continue;

                string output = movers[i].PendingOutput;

                // La cible d'une fin de course se declare a part : une porte
                // qui viserait « target » se re-viserait elle-meme en boucle.
                string target = movers[i].Entity.Get(
                    output == Mover.OnFullyOpen ? "target_open" : "target_close");

                string input = movers[i].Entity.Get(
                    output == Mover.OnFullyOpen ? "input_open" : "input_close", "Trigger");

                EntityDelivery delivery = _entities.Send(target, input);

                ReportEvent(new EntityEvent(movers[i].Entity, output, target, input, delivery));
            }
        }

        /// <summary>
        /// Deplace le joueur avec la plateforme sur laquelle il se tient.
        ///
        /// ⚠️ SANS CECI UN ASCENSEUR EST INUTILISABLE : la plateforme monte, le
        /// joueur reste en l'air, retombe dessus, et recommence - il vibre au
        /// lieu de monter. Ce n'est pas un raffinement.
        /// </summary>
        private static void CarryPlayer()
        {
            bool carried = _movers.CarryRider(ref _motor.Position, _motor.GetBox());

            if (_carryDebug)
            {
                // On refait EXACTEMENT ce que fait CheckGrounded. C'est cette
                // ligne qui a trouve le defaut du portage : elle montre que le
                // balayage repondait hit=False alors qu'une plateforme etait
                // sous les pieds. A garder pour la prochaine fois.
                SweepHit hit = _walkable.Sweep(_motor.GetBox(), new Vector3(0f, -0.05f, 0f));

                Console.WriteLine($"  [carry] pieds {_motor.Position.Y:0.0000} porte={carried} " +
                                  $"balayage(hit={hit.Hit} t={hit.Time:0.000} ny={hit.Normal.Y:0.00}) " +
                                  $"grounded={_motor.Grounded} vy={_motor.Velocity.Y:0.000}");
            }
        }

        private static bool _carryDebug;

        // -------------------------------------------------------------------
        //  RESEAU : un pas
        //
        //  ⚠️ APPELE AU PAS FIXE, pas au rendu. Le serveur consomme une entree
        //  par pas : lui en envoyer 300 par seconde en jetterait 236, et le
        //  debit montant serait cinq fois trop gros pour rien.
        // -------------------------------------------------------------------

        private static void StepNetwork(float dt, in MoveInput input)
        {
            _netServer?.Step(NetTime, dt);

            if (_netClient is null)
                return;

            _netClient.Poll(NetTime);

            // ⚠️ On n'envoie que CONNECTE. Envoyer pendant la poignee de main
            // ferait des paquets que le serveur jette, et masquerait l'echec de
            // connexion derriere du trafic.
            if (_netClient.State == NetClientState.Connecte)
            {
                _netClient.SendInput(input);

                // Une mesure d'aller-retour par seconde : assez pour afficher une
                // valeur utile, trop peu pour peser sur le debit.
                if (_simTime >= _nextPing)
                {
                    _netClient.SendPing();
                    _nextPing = _simTime + 1f;
                }
            }
        }

        private static float _nextPing;
        private static float _fixedDelta = 1f / 64f;

        /// <summary>Marche automatique : outil de capture, voir la commande « walk ».</summary>
        private static bool _autoWalk;

        /// <summary>
        /// Ouvre une socket client et lance la poignee de main.
        ///
        /// ⚠️ La resolution DNS se fait ICI, hors de la boucle de jeu :
        /// Dns.GetHostEntry BLOQUE, parfois plusieurs secondes. Appelee depuis
        /// OnSimulate, elle figerait l'image.
        /// </summary>
        private static void ConnectTo(string host, int port, ConsoleSystem c)
        {
            NetAddress address = UdpTransport.Resolve(host, port);

            if (address.IsEmpty)
            {
                c.Print($"  hote introuvable : {host}");
                return;
            }

            try
            {
                // Port 0 : le systeme choisit. Reutiliser le port du serveur
                // ferait echouer l'ouverture quand on heberge et joue en meme
                // temps, sur la meme machine.
                _clientTransport = new UdpTransport(0);

                _netClient = new NetClient(_clientTransport)
                {
                    // ⚠️⚠️ LE CLIENT PREDIT DANS LE MEME DECOR QUE LE SERVEUR.
                    // C'est la condition de la prediction (3.5) : le client
                    // simule lui-meme, avec le MEME CharacterMotor et le MEME
                    // monde de collision. Un decor different ferait diverger la
                    // prediction en permanence, et le joueur serait tire en
                    // arriere plusieurs fois par seconde sans qu'on comprenne
                    // pourquoi.
                    //
                    // ⚠️ On passe _level, le monde deja construit pour le jeu
                    // local. Si le serveur tourne une AUTRE carte, la prediction
                    // sera fausse - le nom de sa carte est affiche par « net »
                    // pour qu'on puisse s'en apercevoir. Le chargement
                    // automatique de la carte du serveur n'est PAS fait.
                    PredictionWorld = _level,
                };

                _netClient.Connect(address, _netName);

                c.Print($"  connexion a {address}, depuis le port {_clientTransport.Port}");
            }
            catch (Exception ex)
            {
                _netClient = null;
                _clientTransport?.Dispose();
                _clientTransport = null;

                c.Print($"  connexion impossible : {ex.Message}");
            }
        }

        /// <summary>Ferme proprement serveur et client. Idempotent.</summary>
        private static void StopNetwork()
        {
            _netClient?.Dispose();
            _netClient = null;
            _clientTransport?.Dispose();
            _clientTransport = null;

            _netServer?.Dispose();
            _netServer = null;
            _serverTransport?.Dispose();
            _serverTransport = null;
        }

        private static void HandleSandboxKeys()
        {
            if (_keyboard == null) return;

            bool spawn = _keyboard.IsKeyPressed(Key.F);
            if (spawn && !_prevSpawn) SpawnCrateAhead();
            _prevSpawn = spawn;

            bool stack = _keyboard.IsKeyPressed(Key.V);
            if (stack && !_prevStack) SpawnStack(AimPointOnFloor(), 5);
            _prevStack = stack;

            bool boom = _keyboard.IsKeyPressed(Key.G);
            if (boom && !_prevBoom)
            {
                Vector3 at = AimPointOnFloor() + new Vector3(0f, 0.5f, 0f);
                _physics.Explode(at, _boomRadius, _boomForce);

                if (_particlesEnabled)
                    ParticleEffects.Explosion(_particles, ref _fxRng, at, _boomRadius * 0.45f);

                PlaySoundAt(_clipBoom, at);
            }
            _prevBoom = boom;

            bool clear = _keyboard.IsKeyPressed(Key.C);
            if (clear && !_prevClear)
            {
                _physics.Clear();
                Console.WriteLine("  Caisses effacees.");
            }
            _prevClear = clear;

            bool wire = _keyboard.IsKeyPressed(Key.K);
            if (wire && !_prevWire)
            {
                _wireframe = !_wireframe;
                Console.WriteLine($"  Vue des collisions : {(_wireframe ? "ON" : "OFF")}");
            }
            _prevWire = wire;
        }

        private static Vector3 EyePosition => _motor.Position + new Vector3(0f, _motor.Height - 0.18f, 0f);

        /// <summary>Vrai tant que le clic droit est maintenu dans l'editeur.</summary>
        private static bool _editorLooking;

        /// <summary>Taille du tampon rendu et de la fenetre, relevees au rendu.</summary>
        private static int _frameW = 1280, _frameH = 720;
        private static int _windowW = 1280, _windowH = 720;

        /// <summary>
        /// Direction visee dans l'editeur : SOUS LE CURSEUR.
        ///
        /// ⚠️ Pendant le clic droit le curseur est capture et n'a plus de
        /// position significative : on retombe alors sur l'axe de la camera,
        /// qui est exactement ce qu'on regarde.
        ///
        /// ⚠️⚠️ LA POSITION DE SOURIS EST EN COORDONNEES DE FENETRE, LE RAYON
        /// SE CALCULE EN PIXELS DE TAMPON. Les deux diffèrent en DPI eleve, et
        /// l'ecart grandit vers les bords - donc la selection serait juste au
        /// centre et de plus en plus fausse en s'en eloignant. C'est le piege
        /// « FramebufferSize != Window.Size » deja paye une fois.
        /// </summary>
        private static Vector3 EditorAimDirection()
        {
            if (_editorLooking || _mouse is null)
                return _camera.Forward;

            Vector2 pos = _mouse.Position;

            float scaleX = _frameW / (float)Math.Max(1, _windowW);
            float scaleY = _frameH / (float)Math.Max(1, _windowH);

            return _camera.RayThroughPixel(pos.X * scaleX, pos.Y * scaleY, _frameW, _frameH);
        }

        /// <summary>
        /// Direction visee, quel que soit l'etat de l'application.
        ///
        /// En partie c'est le centre de l'ecran - un FPS tire au reticule. Dans
        /// l'editeur c'est le curseur.
        /// </summary>
        private static Vector3 AimDirection
            => _mode == AppMode.Editing ? EditorAimDirection() : _camera.Forward;

        /// <summary>
        /// D'ou part le regard.
        ///
        /// ⚠️ Dans l'editeur ce n'est PAS l'oeil du joueur : le corps simule
        /// reste fige la ou on l'a quitte, pendant que la camera vole. Sans
        /// cette distinction, "poser le point de depart" le placait par rapport
        /// a une position abandonnee, a des dizaines de metres de ce qu'on
        /// regarde. Defaut trouve en relisant le code, pas en jouant.
        /// </summary>
        private static Vector3 AimOrigin => _mode == AppMode.Editing ? _editorEye : EyePosition;

        /// <summary>Point vise sur le decor, ou 6 m devant si on vise le vide.</summary>
        private static Vector3 AimPointOnFloor()
        {
            Vector3 eye = AimOrigin;

            // ⚠️ On suit la MEME direction que la selection. C'est ce qui fait
            // qu'une entite posee a F2..F7 atterrit sous le curseur et pas au
            // centre de l'ecran - sinon poser et selectionner viseraient deux
            // endroits differents, ce qui est pire que les deux comportements.
            Vector3 aim = AimDirection;

            if (_level.Raycast(eye, aim, 60f, out float d, out _))
                return eye + aim * MathF.Max(1.5f, d - 0.6f);

            return eye + aim * 6f;
        }

        private static void SpawnCrateAhead()
        {
            if (_physics.Bodies.Count >= MaxCrates)
                return;

            Vector3 at = EyePosition + _camera.Forward * 2.2f;
            var body = _physics.Spawn(at, new Vector3(_crateSize), _crateMass);

            // On lui donne l'elan du regard : plus agreable que de la poser inerte.
            body.ApplyImpulse(_camera.Forward * 90f);
        }

        private static void SpawnStack(Vector3 groundPoint, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (_physics.Bodies.Count >= MaxCrates)
                    return;

                var at = new Vector3(groundPoint.X,
                                     groundPoint.Y + _crateSize * 0.5f + i * (_crateSize + 0.02f),
                                     groundPoint.Z);
                _physics.Spawn(at, new Vector3(_crateSize), _crateMass);
            }
        }

        private static void HandleShooting()
        {
            bool firing = !_console.IsOpen && _mouse != null && _mouse.IsButtonPressed(MouseButton.Left);

            if (!firing)
            {
                _recoil.Tick(_weapon, _simTime);
                return;
            }

            if (_simTime < _nextShotTime)
                return;

            _nextShotTime = _simTime + _weapon.SecondsBetweenShots;
            _recoil.OnShot(_weapon, _simTime);

            Vector3 eye = EyePosition;
            Vector3 dir = _camera.Forward;

            // Distance jusqu'au decor : une balle ne traverse pas les murs.
            float limit = 250f;
            var wallNormal = Vector3.Zero;
            bool hitWall = _level.Raycast(eye, dir, limit, out float wallDistance, out wallNormal);

            if (hitWall)
                limit = wallDistance;

            RigidBody? crate = _physics.Raycast(eye, dir, limit, out float crateDistance);
            if (crate != null && crateDistance < limit)
            {
                // Une balle de fusil transporte peu de quantite de mouvement,
                // mais dans un bac a sable on veut le voir. Valeur assumee.
                crate.ApplyImpulse(dir * _bulletForce);
                _hitMarkerUntil = _simTime + 0.1f;

                // Sur une caisse, on ne connait pas la face touchee : on renvoie
                // les etincelles vers le tireur. C'est faux en toute rigueur, et
                // parfaitement credible - une gerbe part toujours vers l'avant.
                Emit(eye + dir * crateDistance, -dir);
                return;
            }

            if (hitWall)
                Emit(eye + dir * wallDistance, wallNormal);
        }

        /// <summary>
        /// Ouvre la carte son et prepare les sons.
        ///
        /// L'ABSENCE DE CARTE SON NE DOIT PAS TUER LE MOTEUR. Une machine sans
        /// peripherique audio, une session distante, un service : le rendu et
        /// la physique n'ont aucune raison de s'arreter pour ca. On signale et
        /// on continue, exactement comme pour un modele 3D manquant.
        /// </summary>
        private static void StartAudio()
        {
            try
            {
                _audio = new AudioDevice(FillAudio);

                // La FREQUENCE VIENT DE LA CARTE, elle ne se decide pas ici :
                // demander 48 000 sur un peripherique a 44 100 donne 44 100.
                // Construire le melangeur sur la valeur demandee ferait jouer
                // tous les sons trop aigus.
                _mixer = new AudioMixer(_audio.SampleRate);

                // Le decor sert a l'occlusion : un rayon par source suivie et
                // par pas, sur le fil de JEU. Le fil audio ne parcourt jamais
                // la geometrie.
                _sound = new AudioWorld(_mixer) { World = _level };

                _clipImpact = SoundGen.Impact(0.16f, _audio.SampleRate, 0.7f, seed: 3u);
                _clipBoom = SoundGen.Impact(0.9f, _audio.SampleRate, 1f, seed: 11u);
                _clipTone = SoundGen.Tone(440f, 0.4f, _audio.SampleRate);

                Console.WriteLine($"  Audio      : {_audio.Name}, {_audio.SampleRate} Hz, stereo.");

                if (_audioDumpPath != null)
                {
                    _recorder = new AudioRecorder(_audioDumpSeconds, _audio.SampleRate);
                    Console.WriteLine($"  Enregistrement audio : {_audioDumpSeconds} s vers {_audioDumpPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Audio      : indisponible ({ex.Message})");
                _audio = null;
                _mixer = null;
            }
        }

        /// <summary>
        /// Remplit le tampon de la carte son. APPELE SUR LE FIL AUDIO.
        ///
        /// Ni verrou, ni allocation, ni exception - voir AudioMixer.
        /// </summary>
        private static void FillAudio(Span<float> buffer)
        {
            AudioMixer? mixer = _mixer;

            // Le rappel peut demarrer AVANT que le melangeur n'existe : le
            // peripherique est cree en premier, puisque c'est lui qui donne la
            // frequence. Sans ce garde-fou, la premiere image du jeu se joue
            // sur un champ nul.
            if (mixer is null)
            {
                buffer.Clear();
                return;
            }

            mixer.Mix(buffer);

            _recorder?.Write(buffer);
        }

        /// <summary>Joue un son, sans rien faire s'il n'y a pas de carte.</summary>
        private static void PlaySound(AudioClip? clip, float gain = 1f, float pan = 0f)
        {
            if (clip != null)
                _mixer?.Play(clip, gain, pan);
        }

        /// <summary>Joue un son A UN ENDROIT du monde : distance, cote, occlusion.</summary>
        private static void PlaySoundAt(AudioClip? clip, Vector3 position, float gain = 1f)
        {
            if (clip != null)
                _sound?.PlayAt(clip, position, gain);
        }

        /// <summary>
        /// Recale l'auditeur sur le joueur, puis met a jour les sources.
        ///
        /// ⚠️ Appele au PAS FIXE et non au rendu : les lancers de rayon
        /// d'occlusion couteraient sinon deux a trois fois plus cher a
        /// 3 000 images par seconde, pour un resultat identique - une oreille
        /// ne distingue pas deux positions separees de 15 millisecondes.
        /// </summary>
        private static void UpdateListener()
        {
            if (_sound is null)
                return;

            _sound.Listener = new AudioListener
            {
                Position = EyePosition,
                Forward = _camera.Forward,
                Right = _camera.Right,
                Velocity = _motor.Velocity,
            };

            _sound.Update();
        }

        private static float ParseFloat(string text, float fallback)
            => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value : fallback;

        /// <summary>
        /// Trois coordonnees, ou le point vise par defaut.
        ///
        /// Le point vise suffit en jeu ; les coordonnees explicites sont
        /// indispensables en mode headless, ou personne ne vise rien.
        /// </summary>
        private static Vector3 ParsePoint(string[] args, Vector3 fallback)
        {
            if (args.Length < 3)
                return fallback;

            return new Vector3(ParseFloat(args[0], fallback.X),
                               ParseFloat(args[1], fallback.Y),
                               ParseFloat(args[2], fallback.Z));
        }

        /// <summary>Impact de balle : etincelles, poussiere, et le bruit qui va avec.</summary>
        private static void Emit(Vector3 point, Vector3 normal)
        {
            if (_particlesEnabled)
                ParticleEffects.Impact(_particles, ref _fxRng, point, normal, _impactScale);

            // Spatialise depuis S3 : le son sort du cote de l'impact, faiblit
            // avec la distance, et s'etouffe si un mur s'interpose.
            PlaySoundAt(_clipImpact, point, 0.8f);
        }

        // -------------------------------------------------------------------

        private static void OnRender(EngineHost engine, float alpha)
        {
            if (_mode == AppMode.Editing)
            {
                // En vol libre la camera EST l'oeil : pas d'interpolation entre
                // deux pas, puisqu'il n'y a pas de corps simule derriere.
                _camera.Position = _editorEye;

                // La reconstruction se fait ICI, une fois par image, et jamais
                // dans le gestionnaire d'entrees : une fleche maintenue produit
                // vingt operations par seconde et reconstruirait vingt fois le
                // maillage et le monde de collision.
                if (_editorGeometryDirty && _editor != null)
                {
                    RebuildGeometry(engine.Gl, _editor.Map, _mapSource);
                    _editorGeometryDirty = false;
                }
            }
            else
            {
                Vector3 feet = Vector3.Lerp(_previousFeet, _motor.Position, alpha);
                _camera.Position = feet + new Vector3(0f, _motor.Height - 0.18f, 0f);
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
            _animationClock = _simTime + alpha * (float)(engine.Time.FixedDeltaNanoseconds / 1e9);

            if (_render is null)
                return;

            if (_lightsDirty)
            {
                RebuildLights();
                _lightsDirty = false;
            }

            // Les reglages de console pilotent le pipeline. On les recopie ICI,
            // une fois par image, plutot que depuis chaque setter : le pipeline
            // n'a alors qu'une source de verite, et une ConVar ajoutee plus tard
            // ne peut pas oublier de le prevenir.
            _render.SsaoEnabled = _ssaoEnabled;
            _render.BloomEnabled = _bloomEnabled;
            _render.ParticlesEnabled = _particlesEnabled;
            _render.Wireframe = _wireframe;
            _render.SsaoDebug = _ssaoDebug;
            _render.SsaoDebugRange = _ssaoDebugRange;

            // ⚠️ LES POSES SE CALCULENT AVANT TOUTES LES PASSES, et une seule
            // fois. L'ombre, la profondeur et le corps doivent decrire le meme
            // personnage au meme instant - defaut corrige en M6, a ne pas
            // reintroduire en deplacant ces appels dans DrawGeometry.
            UpdateCharacterPose();
            UpdateAgentPoses();
            UpdateNetPoses();

            // Le decalage visuel laisse par la derniere correction se resorbe au
            // RENDU, pas au pas fixe : c'est de l'affichage pur, et le lisser au
            // pas fixe le ferait avancer par paliers de 15 ms.
            _netClient?.SmoothCorrection(1f / 60f);

            // ⚠️ La taille du TAMPON et celle de la FENETRE diffèrent en DPI
            // eleve, et la simulation a besoin de la premiere pour convertir une
            // position de curseur en rayon. On la retient ici, ou elle est
            // disponible, plutot que de la redemander au hasard.
            var frameSize = engine.Window.FramebufferSize;

            _frameW = frameSize.X;
            _frameH = frameSize.Y;
            _windowW = engine.Window.Size.X;
            _windowH = engine.Window.Size.Y;

            _render.Render(frameSize, _camera, _sunDirection,
                           Sandbox, _particles.Live, alpha);

            FinishHud(engine, frameSize.X, frameSize.Y);
        }

        /// <summary>
        /// Le bac a sable vu par le pipeline : sa geometrie, son dessin final,
        /// son interface. C'est TOUT ce qu'un jeu doit fournir au moteur.
        /// </summary>
        private static readonly SandboxWorld Sandbox = new();

        private sealed class SandboxWorld : IWorldRenderer
        {
            public void DrawGeometry(GeometrySink sink) => ForEachDrawable(
                (mesh, model, bones) => sink(mesh, model, bones));

            public void DrawOpaque(SceneRenderer scene) => DrawSandboxOpaque(scene);

            public void DrawHud(HudRenderer hud, int width, int height) => DrawSandboxHud(hud, width, height);
        }

        /// <summary>
        /// Le dessin final du bac a sable : decor, caisses, personnage, PNJ, et
        /// les aides de l'editeur.
        ///
        /// Plus une seule ligne de PIPELINE ici - ni tampon, ni ordre de passe,
        /// ni chronometre. Que du contenu de jeu.
        /// </summary>
        private static void DrawSandboxOpaque(SceneRenderer scene)
        {
            // Decor : maillage deja en coordonnees monde, pas de transformation.
            scene.Draw(_levelMesh!, Matrix4x4.Identity, Vector3.One);

            // Caisses : un cube unitaire redessine avec une matrice par corps.
            var bodies = _physics.Bodies;

            for (int i = 0; i < bodies.Count; i++)
            {
                RigidBody b = bodies[i];

                // Les caisses endormies sont assombries : on voit d'un coup
                // d'oeil ce que le moteur simule encore.
                scene.Draw(_unitCube!, ModelMatrix(b), b.Sleeping ? new Vector3(0.62f) : Vector3.One);
            }

            // Portes et ascenseurs. Teintees selon leur etat, pour la meme
            // raison que les caisses endormies et les PNJ en poursuite : sans
            // ca, on ne peut pas VOIR ce que le moteur fait sans lire la
            // console, et une capture headless ne prouve rien.
            var movers = _movers.Movers;

            for (int i = 0; i < movers.Count; i++)
            {
                Vector3 tint = movers[i].State switch
                {
                    MoverState.Ouverture => new Vector3(0.95f, 1.30f, 0.85f),
                    MoverState.Fermeture => new Vector3(1.30f, 0.95f, 0.80f),
                    MoverState.Ouvert => new Vector3(0.80f, 1.00f, 0.85f),
                    _ => new Vector3(0.72f, 0.76f, 0.86f),
                };

                scene.Draw(_unitCube!, MoverMatrix(movers[i]), tint);
            }

            // Personnage importe : texture depliee sur ses UV, deformation par
            // ses os. Les poses ont ete calculees AVANT toutes les passes.
            if (_characterMesh != null)
            {
                if (_characterBones != null)
                    scene.SetBones(_characterBones);

                scene.Draw(_characterMesh, CharacterMatrix(), Vector3.One, _characterMaterial);

                // ⚠️ PAS DE PNJ SIMULE DANS L'EDITEUR. Ils ne s'y animent pas
                // (SimulateEditor n'appelle pas AgentWorld.Update) mais
                // AgentWorld est rechargé a chaque reconstruction de geometrie :
                // ils apparaitraient donc en pose de liaison, bras ecartes,
                // aux cotes de leur propre marqueur. Un objet dessine deux fois
                // sous deux apparences est pire que pas d'objet du tout.
                var agents = _mode == AppMode.Editing
                    ? (System.Collections.Generic.IReadOnlyList<Agent>)Array.Empty<Agent>()
                    : _agents.Agents;

                for (int i = 0; i < agents.Count && i < _agentBones.Count; i++)
                {
                    if (_agentBones[i] is Matrix4x4[] bones)
                        scene.SetBones(bones);

                    // Un PNJ qui poursuit est teinte de rouge : le seul moyen de
                    // VOIR son etat sans lire la console.
                    Vector3 tint = agents[i].State == AgentState.Chase
                        ? new Vector3(1.35f, 0.75f, 0.72f)
                        : Vector3.One;

                    scene.Draw(_characterMesh, AgentMatrix(agents[i]), tint, _characterMaterial);
                }

                // Les joueurs distants. Teintes en bleu pour se distinguer des
                // PNJ : sans ca, on ne saurait pas si la silhouette d'en face est
                // un garde ou un vrai joueur, ce qui est le premier interet d'une
                // capture en reseau.
                if (_netClient is { } client)
                {
                    var players = client.Players;

                    for (int i = 0; i < players.Count && i < _netBones.Count; i++)
                    {
                        if (!ShouldDrawNetPlayer(players[i]))
                            continue;

                        if (_netBones[i] is Matrix4x4[] bones)
                            scene.SetBones(bones);

                        scene.Draw(_characterMesh, NetPlayerMatrix(players[i]),
                                   new Vector3(0.72f, 0.95f, 1.4f), _characterMaterial);
                    }
                }

                // INDISPENSABLE : les uniforms survivent au dessin. Sans ca, le
                // prochain maillage rigide serait deforme par ces os.
                scene.ClearBones();
            }

            if (_mode != AppMode.Editing || _editor is null || _gl is null)
                return;

            // --- Aides visuelles de l'editeur -------------------------------
            if (_showGrid)
            {
                EnsureGridMesh(_gl, _editor.GridSize);

                if (_gridMesh[0] != null)
                    scene.Draw(_gridMesh[0]!, Matrix4x4.Identity, Vector3.One);
            }

            // La selection est marquee par une copie LEGEREMENT PLUS GROSSE
            // dessinee par-dessus : on ne peut pas teinter un morceau du decor,
            // qui est un seul maillage. Le gonflement de 2 cm evite le combat
            // de profondeur.
            //
            // ⚠️⚠️ ELLE REPREND LA FORME DE L'OBJET, et ce n'etait pas le cas
            // a la premiere capture. Le surlignage etait reste un cube : une
            // sphere selectionnee DISPARAISSAIT dans un cube orange, et on ne
            // voyait plus l'objet qu'on etait en train de regler. Defaut
            // trouve a l'ecran et nulle part ailleurs - aucun test de Core ne
            // peut voir ce que le surlignage dessine.
            if (_editor.HasSelection)
            {
                MapBox box = _editor.Map.Boxes[_editor.Selected];

                DrawShape(scene, box.Shape, box.Center, box.Size + new Vector3(0.02f), 0f,
                          new Vector3(1.6f, 1.1f, 0.25f));
            }

            // Marqueurs d'entites : une entite n'a AUCUNE geometrie, sans
            // marqueur elle est invisible et impossible a cliquer.
            var markers = _editor.Map.Entities;

            for (int i = 0; i < markers.Count; i++)
                DrawEntityMarker(scene, markers[i], _editor.HasEntity && _editor.Selected == i);
        }

        // ===================================================================
        //  Marqueurs d'entites - une SILHOUETTE par type
        //
        //  ⚠️⚠️ LE DEFAUT QUE CECI CORRIGE, ET IL EST DE CONCEPTION.
        //  Jusqu'au 17/08, les six types partageaient le MEME cube de 60 cm,
        //  distingues par la seule couleur. Poser un PNJ et poser un
        //  checkpoint donnaient donc deux objets identiques, et le volume
        //  reellement couvert par un declencheur - sa propriete « size »,
        //  jusqu'a 3 m de cote par defaut - n'apparaissait NULLE PART.
        //
        //  On ne pouvait pas voir ce qu'on editait : le cube ne disait ni la
        //  nature, ni la portee, ni l'orientation de ce qu'il representait.
        //
        //  Tout est bati a partir des primitives de MapShape. Aucun modele a
        //  charger, donc un marqueur ne peut pas manquer parce qu'un fichier
        //  d'asset est absent - contrairement au personnage glTF, qui lui est
        //  facultatif au demarrage.
        // ===================================================================

        /// <summary>Une forme unitaire mise a l'echelle, tournee autour de Y, puis posee.</summary>
        private static void DrawShape(SceneRenderer scene, MapShape shape, Vector3 center,
                                      Vector3 size, float yawDegrees, Vector3 tint)
        {
            Mesh? mesh = _unitShapes[(int)shape];

            // Un maillage manquant fait un marqueur invisible, pas un plantage :
            // l'editeur doit survivre a une forme qu'on aurait oublie d'instancier.
            if (mesh is null)
                return;

            scene.Draw(mesh,
                Matrix4x4.CreateScale(size)
                * Matrix4x4.CreateRotationY(yawDegrees * MathF.PI / 180f)
                * Matrix4x4.CreateTranslation(center),
                tint);
        }

        /// <summary>
        /// Variante a matrice libre, pour le seul cas qui a besoin d'autre
        /// chose qu'un lacet : la pointe de fleche, un cone couche.
        /// </summary>
        private static void DrawShape(SceneRenderer scene, MapShape shape, Matrix4x4 model, Vector3 tint)
        {
            Mesh? mesh = _unitShapes[(int)shape];

            if (mesh is not null)
                scene.Draw(mesh, model, tint);
        }

        /// <summary>
        /// Une piece d'un assemblage, exprimee dans le repere LOCAL de
        /// l'entite puis tournee avec elle.
        ///
        /// ⚠️ L'ordre est echelle -> translation locale -> rotation -> position.
        /// Tourner APRES avoir translate dans le monde ferait pivoter chaque
        /// membre autour de l'origine de la carte au lieu de son entite : le
        /// personnage se disloquerait des qu'on le sort du centre du monde.
        /// </summary>
        private static void DrawPart(SceneRenderer scene, MapShape shape, Vector3 localOffset,
                                     Vector3 size, float yawRadians, Vector3 origin, Vector3 tint)
        {
            Mesh? mesh = _unitShapes[(int)shape];

            if (mesh is null)
                return;

            scene.Draw(mesh,
                Matrix4x4.CreateScale(size)
                * Matrix4x4.CreateTranslation(localOffset)
                * Matrix4x4.CreateRotationY(yawRadians)
                * Matrix4x4.CreateTranslation(origin),
                tint);
        }

        /// <summary>
        /// Direction horizontale d'un lacet, en degres.
        ///
        /// ⚠️ MEME CONVENTION QUE Camera.Forward : a lacet nul on regarde vers
        /// -Z. Recalculer ca a la main ailleurs finirait par diverger, et une
        /// fleche d'apparition qui montre l'inverse du regard reel est pire
        /// qu'une absence de fleche.
        /// </summary>
        private static Vector3 YawDirection(float degrees)
        {
            float r = degrees * MathF.PI / 180f;
            return new Vector3(-MathF.Sin(r), 0f, -MathF.Cos(r));
        }

        /// <summary>Les 12 aretes d'une boite, en fines lattes. Le moteur n'a pas de primitive de ligne.</summary>
        private static void DrawWireBox(SceneRenderer scene, Vector3 center, Vector3 size,
                                        float thickness, Vector3 tint)
        {
            Vector3 h = size * 0.5f;

            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    DrawShape(scene, MapShape.Block,
                        center + new Vector3(sx * h.X, 0f, sz * h.Z),
                        new Vector3(thickness, size.Y, thickness), 0f, tint);

            for (int sy = -1; sy <= 1; sy += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                    DrawShape(scene, MapShape.Block,
                        center + new Vector3(0f, sy * h.Y, sz * h.Z),
                        new Vector3(size.X, thickness, thickness), 0f, tint);

                for (int sx = -1; sx <= 1; sx += 2)
                    DrawShape(scene, MapShape.Block,
                        center + new Vector3(sx * h.X, sy * h.Y, 0f),
                        new Vector3(thickness, thickness, size.Z), 0f, tint);
            }
        }

        /// <summary>Fleche horizontale : un fut et une pointe conique couchee.</summary>
        private static void DrawArrow(SceneRenderer scene, Vector3 from, float yawDegrees,
                                      float length, float thickness, Vector3 tint)
        {
            Vector3 dir = YawDirection(yawDegrees);

            DrawShape(scene, MapShape.Block, from + dir * (length * 0.5f),
                      new Vector3(thickness, thickness, length), yawDegrees, tint);

            // Le cone pointe vers +Y dans son repere : on le couche d'un quart
            // de tour autour de X pour l'envoyer vers -Z, puis le lacet
            // l'oriente comme le reste.
            DrawShape(scene, MapShape.Cone,
                Matrix4x4.CreateScale(new Vector3(thickness * 3.5f, thickness * 5f, thickness * 3.5f))
                * Matrix4x4.CreateRotationX(-MathF.PI / 2f)
                * Matrix4x4.CreateRotationY(yawDegrees * MathF.PI / 180f)
                * Matrix4x4.CreateTranslation(from + dir * (length + thickness * 2.5f)),
                tint);
        }

        /// <summary>
        /// Silhouette humaine, batie de primitives.
        ///
        /// ⚠️ CE N'EST PAS LE MODELE glTF, et c'est voulu. Le personnage est un
        /// asset FACULTATIF - le moteur demarre sans lui - alors qu'un marqueur
        /// d'editeur ne peut pas se permettre de manquer. Il serait en outre
        /// dessine en pose de liaison, bras ecartes, ce qui ressemble a un bug
        /// plutot qu'a un repere.
        ///
        /// Les proportions visent 1,80 m : c'est ce qui permet de juger a l'oeil
        /// si un PNJ passe sous une porte.
        /// </summary>
        private static void DrawHumanoid(SceneRenderer scene, Vector3 feet, float yawDegrees, Vector3 tint)
        {
            float r = yawDegrees * MathF.PI / 180f;

            for (int side = -1; side <= 1; side += 2)
            {
                DrawPart(scene, MapShape.Cylinder, new Vector3(side * 0.13f, 0.43f, 0f),
                         new Vector3(0.22f, 0.86f, 0.22f), r, feet, tint);

                DrawPart(scene, MapShape.Cylinder, new Vector3(side * 0.30f, 1.15f, 0f),
                         new Vector3(0.16f, 0.62f, 0.16f), r, feet, tint);
            }

            DrawPart(scene, MapShape.Block, new Vector3(0f, 1.18f, 0f),
                     new Vector3(0.46f, 0.66f, 0.26f), r, feet, tint);

            DrawPart(scene, MapShape.Sphere, new Vector3(0f, 1.64f, 0f),
                     new Vector3(0.26f), r, feet, tint);

            // Le nez : sans lui, la silhouette est symetrique et ne dit pas de
            // quel cote le garde regarde - or c'est exactement ce qu'on vient
            // regler avec « fov ».
            DrawPart(scene, MapShape.Cone, new Vector3(0f, 1.62f, -0.16f),
                     new Vector3(0.09f, 0.14f, 0.09f), r, feet, tint);
        }

        /// <summary>
        /// Les deux bords du champ de vision d'un PNJ, a hauteur d'yeux.
        ///
        /// ⚠️ C'est la seule chose qui rende « sight » et « fov » JUGEABLES.
        /// Deux nombres dans un panneau ne disent pas si le garde couvre le
        /// couloir ; deux lattes posees dans le decor, si. Elles partent des
        /// YEUX et pas des pieds, pour la meme raison que la ligne de vue
        /// elle-meme (voir AgentWorld) - depuis les pieds, un muret rendrait
        /// le trace faux sans rien changer au comportement reel.
        /// </summary>
        private static void DrawSightCone(SceneRenderer scene, MapEntity entity, Vector3 feet,
                                          float yawDegrees, Vector3 tint)
        {
            float sight = MathF.Min(entity.GetFloat("sight", 20f), 40f);
            float half = MathF.Min(entity.GetFloat("fov", 110f), 360f) * 0.5f;

            if (sight <= 0f)
                return;

            Vector3 eyes = feet + new Vector3(0f, 1.6f, 0f);

            for (int side = -1; side <= 1; side += 2)
            {
                float edge = yawDegrees + side * half;

                DrawShape(scene, MapShape.Block,
                          eyes + YawDirection(edge) * (sight * 0.5f),
                          new Vector3(0.05f, 0.05f, sight), edge, tint);
            }
        }

        /// <summary>
        /// La COURSE d'une porte : d'ou elle part, jusqu'ou elle va.
        ///
        /// La boite de la porte elle-meme est deja dessinee par MoverWorld,
        /// dans l'editeur comme en jeu. Ce qui manquait est invisible par
        /// nature : « axis » et « distance » ne se voient qu'une fois la porte
        /// ouverte, donc jamais pendant qu'on l'edite.
        /// </summary>
        private static void DrawDoorTravel(SceneRenderer scene, MapEntity entity, Vector3 position, Vector3 tint)
        {
            float distance = entity.GetFloat("distance", 3f);

            Vector3 axis = entity.Get("axis", "y").Trim().ToLowerInvariant() switch
            {
                "x" => Vector3.UnitX,
                "z" => Vector3.UnitZ,
                _ => Vector3.UnitY,
            };

            Vector3 travel = axis * distance;
            float length = MathF.Abs(distance);

            if (length < 0.01f)
                return;

            Vector3 thickness = Vector3.One * 0.06f + axis * (length - 0.06f);

            DrawShape(scene, MapShape.Block, position + travel * 0.5f, thickness, 0f, tint);
            DrawShape(scene, MapShape.Sphere, position + travel, new Vector3(0.22f), 0f, tint);
        }

        /// <summary>
        /// Le marqueur d'une entite, choisi sur son TYPE.
        ///
        /// ⚠️ Un type inconnu retombe sur le cube historique, magenta. Le
        /// format conserve exprime les types qu'il ne connait pas (voir
        /// MapEntity) : le marqueur doit faire pareil, sinon une carte ecrite
        /// pour une version ulterieure aurait des entites INVISIBLES et
        /// impossibles a designer - donc impossibles a supprimer.
        /// </summary>
        private static void DrawEntityMarker(SceneRenderer scene, MapEntity entity, bool active)
        {
            Vector3 tint = active ? new Vector3(2f, 1.6f, 0.3f) : EntityTint(entity.Type);
            Vector3 p = entity.Position;

            switch (entity.Type)
            {
                case EntityTypes.PlayerSpawn:
                    // Un socle plat plutot qu'un cube : une apparition est un
                    // ENDROIT, pas un objet, et le socle montre son emprise au
                    // sol sans masquer le decor qu'on est en train de cadrer.
                    DrawShape(scene, MapShape.Cylinder, p + new Vector3(0f, 0.05f, 0f),
                              new Vector3(1.3f, 0.1f, 1.3f), 0f, tint);
                    DrawShape(scene, MapShape.Cone, p + new Vector3(0f, 0.75f, 0f),
                              new Vector3(0.45f, 1.1f, 0.45f), 0f, tint);
                    DrawArrow(scene, p + new Vector3(0f, 0.14f, 0f), entity.Yaw, 1.5f, 0.07f, tint);
                    return;

                case EntityTypes.Checkpoint:
                case EntityTypes.Trigger:
                {
                    // ⚠️ ON DESSINE LE VOLUME REEL, calcule par la MEME fonction
                    // que celle qui teste le joueur. Un marqueur de taille fixe
                    // mentirait sur la portee du declencheur - et c'est
                    // precisement ce qu'on venait de reprocher au cube.
                    Aabb volume = EntityWorld.VolumeOf(entity);

                    DrawWireBox(scene, (volume.Min + volume.Max) * 0.5f,
                                volume.Max - volume.Min, 0.06f, tint);

                    DrawShape(scene, MapShape.Sphere, p, new Vector3(0.34f), 0f, tint);
                    return;
                }

                case EntityTypes.Npc:
                    DrawHumanoid(scene, p, entity.Yaw, tint);
                    DrawSightCone(scene, entity, p, entity.Yaw, tint);
                    return;

                case EntityTypes.Waypoint:
                    // Un jalon : une bille sur un mat. Assez fin pour qu'une
                    // ronde de six points ne bouche pas la vue du couloir.
                    DrawShape(scene, MapShape.Cylinder, p + new Vector3(0f, 0.4f, 0f),
                              new Vector3(0.07f, 0.8f, 0.07f), 0f, tint);
                    DrawShape(scene, MapShape.Sphere, p + new Vector3(0f, 0.95f, 0f),
                              new Vector3(0.3f), 0f, tint);
                    return;

                case EntityTypes.Door:
                    DrawDoorTravel(scene, entity, p, tint);
                    return;

                default:
                    DrawShape(scene, MapShape.Block, p + new Vector3(0f, 0.3f, 0f),
                              new Vector3(EditorState.EntityMarkerSize), entity.Yaw, tint);
                    return;
            }
        }


        /// <summary>
        /// Charge le personnage glTF du dossier Model3D, s'il est la.
        ///
        /// L'absence du modele ne doit PAS empecher le moteur de demarrer :
        /// c'est un asset de developpement, pas une dependance. On le signale
        /// et on continue.
        /// </summary>
        private static void LoadCharacter(GL gl)
        {
            try
            {
                string? path = FindCharacterFile();

                if (path is null)
                {
                    Console.WriteLine("  Personnage : aucun .glb trouve dans Model3D/, ignore.");
                    return;
                }

                GltfModel model = GltfModel.Load(path);
                _characterModel = model;
                _animator = new Animator(model);

                SelectClips();

                // Blanc pur : la couleur de sommet MULTIPLIE la texture. Une
                // teinte grise assombrirait toute la texture d'autant.
                _characterMesh = MeshBuilder.FromGltf(gl, model, Vector3.One);

                if (model.Skins.Count > 0)
                {
                    Console.WriteLine($"  Squelette  : {model.Skins[0].BoneCount} os, " +
                                      $"{model.Animations.Count} animation(s) : " +
                                      string.Join(", ", model.Animations.Select(a => $"{a.Name} ({a.Duration:0.00} s)")));
                }

                Console.WriteLine($"  Personnage : {Path.GetFileName(path)} — {model.VertexCount} sommets, " +
                                  $"{model.TriangleCount} triangles, {model.Bounds.Size.Y:0.00} m de haut.");

                GltfImage? image = model.BaseColorImageOf(model.Primitives[0]);

                if (image is { IsPng: true })
                {
                    Image32 decoded = Png.Decode(image.Data);

                    _characterTexture = new Texture(gl, decoded);
                    _characterMaterial = Material.Textured(_characterTexture, roughness: 0.65f);

                    Console.WriteLine($"  Texture    : {decoded.Width}x{decoded.Height}, decodee par Png maison.");
                }
                else
                {
                    // Sans texture on garde une couleur unie plutot que de
                    // laisser le decor se projeter sur le personnage.
                    _characterMaterial = Material.Matte(new Vector3(0.78f, 0.78f, 0.80f));
                    Console.WriteLine("  Texture    : absente ou non PNG, couleur unie.");
                }
            }
            catch (Exception ex)
            {
                // Un modele illisible ne doit pas tuer le demarrage non plus.
                Console.WriteLine($"  Personnage : lecture impossible ({ex.Message})");
            }
        }

        private static string? FindCharacterFile()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir is not null)
            {
                var models = new DirectoryInfo(Path.Combine(dir.FullName, "Model3D"));

                if (models.Exists)
                {
                    FileInfo[] files = models.GetFiles("*.glb", SearchOption.AllDirectories);

                    // Le fichier des animations fusionnees est le plus complet.
                    foreach (FileInfo file in files)
                        if (file.Name.Contains("Merged", StringComparison.OrdinalIgnoreCase))
                            return file.FullName;

                    if (files.Length > 0)
                        return files[0].FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }

        /// <summary>
        /// Parcourt toute la geometrie de la scene, une fois.
        ///
        /// Les passes qui ne font QUE de la geometrie - ombre, profondeur -
        /// dessinent exactement la meme chose avec leur propre shader. Sans
        /// ce point unique, la moindre divergence entre deux copies de cette
        /// boucle produit une ombre ou une occlusion decalee, et c'est
        /// pratiquement introuvable a l'oeil.
        ///
        /// La passe finale ne l'utilise PAS : elle a besoin, en plus, de la
        /// teinte et du materiau de chaque corps.
        /// </summary>
        private static void ForEachDrawable(Action<Mesh, Matrix4x4, Matrix4x4[]?> draw)
        {
            draw(_levelMesh!, Matrix4x4.Identity, null);

            var bodies = _physics.Bodies;
            for (int i = 0; i < bodies.Count; i++)
                draw(_unitCube!, ModelMatrix(bodies[i]), null);

            // Les portes passent par LE MEME point que le reste : elles heritent
            // donc de l'ombre et de l'occlusion sans une ligne de plus. Une
            // porte sans ombre se verrait tout de suite comme un decor colle.
            var movers = _movers.Movers;
            for (int i = 0; i < movers.Count; i++)
                draw(_unitCube!, MoverMatrix(movers[i]), null);

            if (_characterMesh != null)
                draw(_characterMesh, CharacterMatrix(), _characterBones);

            // Les PNJ passent par LE MEME point que le reste : ils heritent donc
            // des ombres et de l'occlusion ambiante sans une ligne de plus. Les
            // dessiner ailleurs aurait donne des personnages sans ombre, et il
            // aurait fallu comprendre pourquoi.
            if (_characterMesh != null)
            {
                var agents = _agents.Agents;

                for (int i = 0; i < agents.Count && i < _agentAnimators.Count; i++)
                    draw(_characterMesh, AgentMatrix(agents[i]), _agentBones[i]);
            }

            // Les joueurs distants passent par LE MEME point : ils heritent donc
            // de l'ombre et de l'occlusion sans une ligne de plus.
            if (_characterMesh != null && _netClient is { } client)
            {
                var players = client.Players;

                for (int i = 0; i < players.Count && i < _netBones.Count; i++)
                {
                    if (!ShouldDrawNetPlayer(players[i]))
                        continue;

                    draw(_characterMesh, NetPlayerMatrix(players[i]), _netBones[i]);
                }
            }
        }

        /// <summary>
        /// Transformation d'une porte.
        ///
        /// ⚠️ UNE SEULE fonction pour les deux passes (ombre et rendu final),
        /// meme regle qu'en R6 pour les caisses : deux matrices calculees
        /// separement finissent par diverger, et une ombre decollee de sa porte
        /// ne se voit pas tout de suite.
        /// </summary>
        private static Matrix4x4 MoverMatrix(Mover mover)
            => Matrix4x4.CreateScale(mover.Size)
             * Matrix4x4.CreateTranslation(mover.Center);

        /// <summary>Transformation d'un PNJ. Les pieds sont a sa position.</summary>
        private static Matrix4x4 AgentMatrix(Agent agent)
            => Matrix4x4.CreateRotationY(agent.Yaw)
             * Matrix4x4.CreateTranslation(agent.Position);

        /// <summary>
        /// Matrices d'os de chaque PNJ pour l'image en cours.
        ///
        /// ⚠️ Calculees UNE FOIS par image et partagees entre les passes. Les
        /// recalculer dans chaque passe donnerait a l'ombre une pose differente
        /// du corps - exactement le defaut corrige en M6 pour le personnage.
        /// </summary>
        private static readonly List<Matrix4x4[]?> _agentBones = new();

        /// <summary>
        /// Fait avancer l'animation de chaque PNJ.
        ///
        /// ⚠️⚠️ C'EST LE PREMIER VRAI USAGE DU MELANGE DE POSES (2.2). La note de
        /// l'epoque disait « rien ne pilote le melange automatiquement, le
        /// branchement tient en une ligne le jour ou il y aura une entite ».
        /// C'est cette ligne : le melange vaut la vitesse rapportee a la
        /// vitesse de course.
        /// </summary>
        private static void UpdateAgentPoses()
        {
            var agents = _agents.Agents;

            while (_agentBones.Count < agents.Count) _agentBones.Add(null);
            while (_agentBones.Count > agents.Count) _agentBones.RemoveAt(_agentBones.Count - 1);

            if (_characterModel is null || _characterModel.Skins.Count == 0)
                return;

            float dt = (_animationClock - _lastAgentClock) * _characterSpeed;
            _lastAgentClock = _animationClock;

            for (int i = 0; i < agents.Count && i < _agentAnimators.Count; i++)
            {
                Agent agent = agents[i];
                Animator animator = _agentAnimators[i];

                // Marche a l'arret, course a pleine vitesse, et tout ce qu'il y
                // a entre les deux. La cadence du pas suit, parce que c'est
                // l'Animator qui melange les durees des deux cycles.
                float top = MathF.Max(0.5f, agent.Motor.Settings.RunSpeed);

                animator.Weight = Math.Clamp(agent.Speed / top, 0f, 1f);
                animator.OffsetB = _characterOffset;

                animator.Advance(dt);

                _agentBones[i] = animator.SkinMatrices();
            }
        }

        private static float _lastAgentClock;

        // -------------------------------------------------------------------
        //  JOUEURS DISTANTS : pose et animation
        //
        //  ⚠️ Un Animator PAR joueur distant, meme raison qu'un Animator par
        //  PNJ : un seul partage donnerait a tous les joueurs la MEME pose au
        //  meme instant, ce qui est immediatement faux a l'oeil.
        //
        //  ⚠️⚠️ LEUR VITESSE EST MESUREE, PAS RECUE. Le protocole ne replique
        //  que ce qui est visible - position et orientation - et la vitesse s'en
        //  deduit. La repliquer couterait quatre octets par joueur et par
        //  instantane pour une valeur que le client peut calculer.
        // -------------------------------------------------------------------

        private static readonly List<Animator> _netAnimators = new();
        private static readonly List<Matrix4x4[]?> _netBones = new();
        private static readonly Dictionary<byte, Vector3> _netPrevious = new();
        private static readonly Dictionary<byte, float> _netSpeed = new();

        private static void UpdateNetPoses()
        {
            if (_netClient is null)
            {
                _netBones.Clear();
                return;
            }

            var players = _netClient.Players;

            while (_netBones.Count < players.Count) _netBones.Add(null);
            while (_netBones.Count > players.Count) _netBones.RemoveAt(_netBones.Count - 1);

            if (_characterModel is null || _characterModel.Skins.Count == 0)
                return;

            while (_netAnimators.Count < players.Count)
            {
                var animator = new Animator(_characterModel);
                animator.SetClips(_characterModel.FindAnimation("Walking"),
                                  _characterModel.FindAnimation("Running"));

                // Phase decalee, comme les PNJ.
                animator.Phase = _netAnimators.Count * 0.41f;
                _netAnimators.Add(animator);
            }

            float dt = (_animationClock - _lastNetClock) * _characterSpeed;
            _lastNetClock = _animationClock;

            for (int i = 0; i < players.Count && i < _netAnimators.Count; i++)
            {
                PlayerState state = players[i];

                // Vitesse HORIZONTALE mesuree entre deux images. On lisse : la
                // position vient d'une interpolation, et la deriver brutalement
                // donnerait une vitesse qui saute a chaque nouvel instantane.
                float measured = 0f;

                if (_netPrevious.TryGetValue(state.Id, out Vector3 previous) && dt > 1e-6f)
                {
                    Vector3 delta = state.Position - previous;
                    measured = new Vector2(delta.X, delta.Z).Length() / dt;
                }

                _netPrevious[state.Id] = state.Position;

                float smoothed = _netSpeed.TryGetValue(state.Id, out float last)
                    ? last + (measured - last) * 0.2f
                    : measured;

                _netSpeed[state.Id] = smoothed;

                Animator animator = _netAnimators[i];

                animator.Weight = Math.Clamp(smoothed / MathF.Max(0.5f, _motor.Settings.RunSpeed), 0f, 1f);
                animator.OffsetB = _characterOffset;
                animator.Advance(dt);

                _netBones[i] = animator.SkinMatrices();
            }
        }

        private static float _lastNetClock;

        /// <summary>
        /// Transformation d'un joueur distant. Ses pieds sont a sa position.
        ///
        /// ⚠️ Le lacet du modele est tourne de pi : le personnage glTF regarde
        /// vers +Z au repos, alors que le lacet zero du moteur regarde vers -Z.
        /// Sans ca, tout le monde court a reculons.
        /// </summary>
        private static Matrix4x4 NetPlayerMatrix(in PlayerState state)
            => Matrix4x4.CreateRotationY(state.Yaw + MathF.PI)
             * Matrix4x4.CreateTranslation(state.Position);

        /// <summary>
        /// Faut-il dessiner ce joueur ?
        ///
        /// ⚠️ PAS SOI-MEME. On est en vue subjective : dessiner son propre corps
        /// mettrait un torse en plein ecran. C'est aussi ce qui evite de voir sa
        /// propre position corrigee par le serveur sauter devant les yeux.
        /// </summary>
        private static bool ShouldDrawNetPlayer(in PlayerState state)
            => _netClient is not null && state.Id != _netClient.LocalId;

        /// <summary>
        /// Choisit le couple de cycles a melanger.
        ///
        /// Le cycle A (melange 0) est la MARCHE, le cycle B (melange 1) la
        /// COURSE. On les cherche par nom, avec repli sur les indices : un
        /// autre modele n'aura pas forcement les memes libelles, et le moteur
        /// ne doit pas dependre de la maniere dont un artiste a nomme ses
        /// exports.
        /// </summary>
        private static void SelectClips()
        {
            GltfModel? model = _characterModel;

            if (model is null || _animator is null)
                return;

            if (model.Animations.Count == 0)
                return;

            GltfAnimation? walk = model.FindAnimation("Walking") ?? model.FindAnimation("Walk");
            GltfAnimation? run = model.FindAnimation("Running") ?? model.FindAnimation("Run");

            // Repli : le premier cycle sert de marche, le second de course.
            walk ??= model.Animations[_characterAnimation % model.Animations.Count];
            run ??= model.Animations.Count > 1 ? model.Animations[1] : null;

            _animator.SetClips(walk, run);
        }

        /// <summary>
        /// Fait avancer l'animation et prepare les matrices d'os.
        ///
        /// Fait au RENDU et non a la simulation : c'est de l'affichage pur,
        /// et l'echantillonner au pas fixe le ferait saccader des que le
        /// rendu va plus vite que 64 images par seconde.
        ///
        /// ⚠️ APPELE PLUSIEURS FOIS PAR IMAGE - la passe d'ombre, la passe de
        /// profondeur et la passe finale doivent decrire le MEME personnage au
        /// MEME instant. L'avance se calcule donc depuis une horloge, jamais
        /// depuis un delta recu : les appels suivants trouvent un ecart nul et
        /// ne font rien. Avec un delta, l'animation tournerait au triple de sa
        /// vitesse selon le nombre de passes activees.
        /// </summary>
        private static void UpdateCharacterPose()
        {
            if (_characterModel is null || _animator is null || _characterModel.Skins.Count == 0)
                return;

            float dt = (_animationClock - _lastAnimationClock) * _characterSpeed;
            _lastAnimationClock = _animationClock;

            _animator.Weight = _characterBlend;
            _animator.OffsetB = _characterOffset;

            // Phase imposee : on la pose ET on annule l'avance, sinon
            // Advance la repousserait aussitot.
            if (_characterPhase >= 0f)
            {
                _animator.Phase = _characterPhase;
                dt = 0f;
            }

            _animator.Advance(dt);

            _characterBones = _animator.SkinMatrices();
        }

        private static Matrix4x4 CharacterMatrix()
            => Matrix4x4.CreateRotationY(_characterYaw) * Matrix4x4.CreateTranslation(_characterPosition);

        /// <summary>
        /// Matrice de transformation d'un corps.
        ///
        /// L'ordre n'est pas negociable : mise a l'echelle dans le repere
        /// PROPRE du corps, PUIS rotation, PUIS translation. Tourner avant de
        /// mettre a l'echelle reviendrait a appliquer des dimensions du monde
        /// a une boite inclinee - une caisse allongee se deformerait en
        /// tournant, au lieu de tourner.
        ///
        /// Une seule fonction pour les DEUX passes, ombre et rendu final.
        /// Deux matrices calculees separement finissent toujours par diverger,
        /// et une ombre decollee de sa caisse est de ces defauts qu'on ne
        /// remarque pas sur une capture.
        /// </summary>
        private static Matrix4x4 ModelMatrix(RigidBody body)
            => Matrix4x4.CreateScale(body.Size)
             * Matrix4x4.CreateFromQuaternion(body.Orientation)
             * Matrix4x4.CreateTranslation(body.Position);

        // ===================================================================
        //  Menu (4.1)
        // ===================================================================

        /// <summary>Noms des cartes du dossier, sans extension. Jamais null.</summary>
        private static List<string> ListMapNames()
        {
            var names = new List<string>();
            string folder = MapsFolder();

            if (!Directory.Exists(folder))
                return names;

            foreach (string file in Directory.GetFiles(folder, "*" + MapFile.Extension))
                names.Add(Path.GetFileNameWithoutExtension(file));

            names.Sort(StringComparer.OrdinalIgnoreCase);

            return names;
        }

        private static void EnterMenu()
        {
            _mode = AppMode.Menu;
            _mapPicker = false;
            _mouseDelta = Vector2.Zero;
        }

        private static void EnterPlay()
        {
            _mode = AppMode.Playing;
            _mouseDelta = Vector2.Zero;
            _hasLastMouse = false;

            // On reprend la souris : en partie elle vise, elle ne pointe pas.
            // ⚠️ Sans ca, sortir de l'editeur laisserait un curseur visible et
            // une camera qui ne tourne plus - et rien ne dirait pourquoi.
            _editorLooking = false;

            if (_mouse != null && !_console.IsOpen)
                _mouse.Cursor.CursorMode = CursorMode.Raw;

            // Le MEME chemin qu'au chargement d'une carte : sinon "Jouer"
            // depuis le menu ignorerait les entites d'apparition, et on
            // n'aurait pas deux comportements mais deux bugs a trouver.
            if (_map != null)
            {
                ApplySpawn(_map);

                // ⚠️ LES PNJ SONT INSTANCIES ICI, pas au chargement de la carte.
                // AgentWorld prend un instantane : une entite ajoutee dans
                // l'editeur n'existerait pas en partie sans ce rechargement -
                // constate, la console repondait « aucun PNJ dans la carte »
                // juste apres en avoir pose deux.
                //
                // C'est aussi le bon comportement : entrer en partie remet les
                // gardes a leur poste, ce qu'on veut apres avoir edite.
                _agents.Load(_map, _level);
                RebuildAgentAnimators();

                _entities.Load(_map);

                // ⚠️ L'ORDRE EST OBLIGATOIRE : Load efface les cibles inscrites
                // (sinon une porte de la carte precedente resterait joignable
                // sous son nom). Les movers se reinscrivent donc APRES.
                _movers.Load(_map);
                _movers.RegisterInto(_entities);
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
        private static void EnterEditor()
        {
            if (_map is null)
                return;

            _mode = AppMode.Editing;
            _mouseDelta = Vector2.Zero;
            _hasLastMouse = false;

            // ⚠️ LE CURSEUR REDEVIENT VISIBLE. En partie il est capture, ce qui
            // est juste pour un FPS et absurde pour un editeur : sans curseur on
            // ne peut ni designer un objet ni atteindre le panneau. Le clic
            // droit le recapture le temps de pivoter.
            _editorLooking = false;

            if (_mouse != null)
                _mouse.Cursor.CursorMode = CursorMode.Normal;

            // On edite la carte EN PLACE : _map et _editor.Map sont le meme
            // objet, donc la reconstruction voit toujours l'etat courant.
            if (_editor is null)
                _editor = new EditorState(_map);
            else if (!ReferenceEquals(_editor.Map, _map))
                _editor.Reset(_map);

            // Le panneau suit l'editeur. Il est recree si l'editeur change
            // d'instance : un panneau qui garderait l'ancien montrerait les
            // champs d'une carte qu'on n'edite plus.
            _panel ??= new PropertyPanel(_editor);

            // ⚠️ On rend le clavier a l'editeur en ENTRANT. Sans ca, revenir du
            // menu avec le panneau focalise laisserait la camera muette, et
            // rien a l'ecran n'expliquerait pourquoi ZQSD ne fait plus voler.
            _panel.Unfocus();

            _physics.Clear();
            _particles.Clear();

            // On part de la position du joueur, un peu en hauteur : arriver a
            // l'origine du monde ferait perdre le contexte de ce qu'on editait.
            _editorEye = EyePosition + new Vector3(0f, 2.5f, 0f);
        }

        private static void MenuKey(EngineHost engine, Key key)
        {
            MenuModel menu = _mapPicker ? _mapMenu : _menu;

            switch (key)
            {
                case Key.Up: menu.Move(-1); return;
                case Key.Down: menu.Move(+1); return;

                case Key.Escape:
                    // Dans le sous-menu, Echap remonte ; au menu principal il
                    // reprend la partie plutot que de quitter - quitter est une
                    // entree explicite, on ne perd pas son travail par reflexe.
                    if (_mapPicker) _mapPicker = false;
                    else if (_map != null) _mode = AppMode.Playing;
                    return;

                case Key.Enter:
                case Key.KeypadEnter:
                    ActivateMenu(engine, menu);
                    return;
            }
        }

        private static void ActivateMenu(EngineHost engine, MenuModel menu)
        {
            if (_mapPicker)
            {
                if (!menu.IsEmpty && _gl != null)
                {
                    try
                    {
                        ApplyMap(_gl, MapFile.LoadFrom(ResolveMapPath(menu.Current)), ResolveMapPath(menu.Current));
                        _editor?.Reset(_map!);
                        EnterPlay();
                    }
                    catch (Exception ex)
                    {
                        // Une carte illisible ne doit pas fermer le jeu : on
                        // reste au menu et on le dit dans la console.
                        Console.Error.WriteLine("  Carte : " + ex.Message);
                        _console.System.Print("  carte illisible : " + ex.Message);
                    }
                }

                _mapPicker = false;
                return;
            }

            switch (menu.Selected)
            {
                case 0: EnterPlay(); return;
                case 1: EnterEditor(); return;

                case 2:
                    _mapMenu.SetItems(ListMapNames());
                    _mapPicker = true;
                    return;

                case 3: engine.Window.Close(); return;
            }
        }

        private static void DrawMenu(int w, int h)
        {
            if (_hud is null) return;

            // Voile sombre : le decor reste visible derriere, mais le texte se
            // lit. Un menu opaque perdrait le contexte de la carte chargee.
            _hud.Rect(0f, 0f, w, h, new Vector4(0.02f, 0.03f, 0.05f, 0.72f));

            float cx = w * 0.5f;

            _hud.TextCentered(cx, h * 0.20f, 5f, "EXARION", new Vector4(0.85f, 0.92f, 1f, 1f));
            _hud.TextCentered(cx, h * 0.20f + 52f, 2f,
                _mapPicker ? "CHOISIR UNE CARTE" : "MOTEUR 3D",
                new Vector4(0.45f, 0.55f, 0.7f, 1f));

            MenuModel menu = _mapPicker ? _mapMenu : _menu;
            float y = h * 0.40f;

            if (menu.IsEmpty)
            {
                _hud.TextCentered(cx, y, 2.5f, "AUCUNE CARTE DANS maps/", new Vector4(1f, 0.6f, 0.4f, 1f));
            }
            else
            {
                for (int i = 0; i < menu.Count; i++)
                {
                    bool active = i == menu.Selected;

                    // Le curseur est un CHEVRON et un fond, pas seulement une
                    // couleur : sur un ecran mal calibre, deux teintes de gris
                    // ne se distinguent pas.
                    if (active)
                        _hud.Rect(cx - 210f, y - 6f, 420f, 34f, new Vector4(0.16f, 0.34f, 0.5f, 0.75f));

                    string label = menu.Items[i].ToUpperInvariant();

                    _hud.TextCentered(cx, y, 2.6f, active ? "> " + label + " <" : label,
                        active ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(0.6f, 0.65f, 0.72f, 1f));

                    y += 44f;
                }
            }

            _hud.TextCentered(cx, h - 64f, 1.8f,
                "FLECHES CHOISIR   ENTREE VALIDER   ECHAP REPRENDRE",
                new Vector4(0.5f, 0.55f, 0.62f, 1f));

            if (_map != null)
                _hud.TextCentered(cx, h - 36f, 1.8f,
                    $"CARTE : {_map.Name.ToUpperInvariant()}   {_map.Boxes.Count} BOITES",
                    new Vector4(0.4f, 0.48f, 0.56f, 1f));
        }

        // ===================================================================
        //  Editeur (4.1)
        // ===================================================================

        private static void EditorKey(Key key)
        {
            if (_editor is null) return;

            // ⚠️ LE PANNEAU PASSE EN PREMIER, et il peut tout avaler. Tant
            // qu'une valeur est en cours de frappe, « C » doit ecrire un C et
            // surtout PAS dupliquer l'objet selectionne. L'ordre inverse
            // dupliquerait un mur a chaque lettre d'un nom de cible.
            if (_panel is { Focused: true } && PanelKey(key))
                return;

            bool shift = _keyboard?.IsKeyPressed(Key.ShiftLeft) == true
                      || _keyboard?.IsKeyPressed(Key.ShiftRight) == true;
            bool ctrl = _keyboard?.IsKeyPressed(Key.ControlLeft) == true
                     || _keyboard?.IsKeyPressed(Key.ControlRight) == true;

            switch (key)
            {
                case Key.Escape: EnterMenu(); return;

                // Tab prend le clavier pour le panneau. C'est la touche de
                // Roblox Studio et de tous les editeurs a formulaire : elle ne
                // sert a rien d'autre ici, et elle ne rentre en conflit ni avec
                // le vol ni avec les raccourcis.
                case Key.Tab: _panel?.ToggleFocus(); return;

                // Changer la forme des PROCHAINS objets crees. Maj pour
                // remonter, comme la grille sur G.
                case Key.B:
                    if (_editor.HasSelection)
                    {
                        _editor.SetShape(_editor.CycleNewShape(shift ? -1 : +1));
                        _editorGeometryDirty = true;
                    }
                    else
                    {
                        _editor.CycleNewShape(shift ? -1 : +1);
                    }
                    return;

                case Key.Number1: _editor.Axis = EditAxis.X; return;
                case Key.Number2: _editor.Axis = EditAxis.Y; return;
                case Key.Number3: _editor.Axis = EditAxis.Z; return;

                case Key.G:
                    _editor.CycleGrid(shift ? -1 : +1);
                    RequestGridMesh();
                    return;

                case Key.H: _showGrid = !_showGrid; return;

                case Key.Z when ctrl:
                    if (_editor.Undo()) _editorGeometryDirty = true;
                    return;

                case Key.Y when ctrl:
                    if (_editor.Redo()) _editorGeometryDirty = true;
                    return;

                case Key.S when ctrl: SaveEditedMap(); return;

                case Key.Enter:
                case Key.KeypadEnter:
                    CreateBoxAhead();
                    return;

                case Key.Delete:
                    if (_editor.Delete()) _editorGeometryDirty = true;
                    return;

                case Key.C:
                    if (_editor.Duplicate() >= 0) _editorGeometryDirty = true;
                    return;

                case Key.P:
                    // Le point de depart se pose la ou on regarde, au sol.
                    _editor.SetSpawn(AimPointOnFloor(), _camera.Yaw * 180f / MathF.PI,
                                     _camera.Pitch * 180f / MathF.PI);
                    return;

                // --- Entites ---
                //
                // Les touches de fonction plutot que des lettres : elles ne
                // rentrent en conflit ni avec le vol ni avec les raccourcis, et
                // on peut en aligner autant qu'il y a de types.
                // ⚠️ On demarre a F2 : F1 ouvre la console. Poser une entite
                // en meme temps qu'on ouvre la console serait un mystere
                // durable - une entite apparaitrait sans qu'on sache pourquoi.
                case Key.F2: PlaceEntity(EntityTypes.PlayerSpawn); return;
                case Key.F3: PlaceEntity(EntityTypes.Checkpoint); return;
                case Key.F4: PlaceEntity(EntityTypes.Trigger); return;
                case Key.F5: PlaceEntity(EntityTypes.Npc); return;
                case Key.F6: PlaceEntity(EntityTypes.Waypoint); return;
                case Key.F7: PlaceEntity(EntityTypes.Door); return;
            }
        }

        /// <summary>
        /// Les touches quand le panneau a le clavier. Rend vrai si la touche
        /// est CONSOMMEE.
        ///
        /// ⚠️ LA REGLE DE CONSOMMATION EST LE COEUR DE CETTE METHODE, et elle
        /// n'est pas symetrique :
        ///
        ///  - en SAISIE, on avale tout ce qui n'est pas traite ici. Une lettre
        ///    doit s'ecrire, jamais declencher un raccourci ;
        ///  - focalise mais PAS en saisie, on ne prend que la navigation et on
        ///    laisse passer le reste. Sinon Ctrl+S et Ctrl+Z deviendraient
        ///    inaccessibles des qu'on regarde une propriete.
        /// </summary>
        private static bool PanelKey(Key key)
        {
            if (_panel is null)
                return false;

            switch (key)
            {
                case Key.Tab:
                    _panel.ToggleFocus();
                    return true;

                case Key.Escape:
                    // Trois sens pour une touche, du plus local au plus global :
                    // abandonner la saisie, puis rendre le clavier, puis
                    // seulement quitter vers le menu. L'ordre inverse ferait
                    // sortir de l'editeur sur une faute de frappe.
                    if (_panel.Editing) _panel.CancelEdit();
                    else _panel.Unfocus();
                    return true;

                case Key.Up: _panel.MoveCursor(-1); return true;
                case Key.Down: _panel.MoveCursor(+1); return true;

                case Key.Left:
                    if (_panel.CycleValue(-1)) _editorGeometryDirty = true;
                    return true;

                case Key.Right:
                    if (_panel.CycleValue(+1)) _editorGeometryDirty = true;
                    return true;

                case Key.Enter:
                case Key.KeypadEnter:
                    if (_panel.Editing)
                    {
                        if (_panel.CommitEdit())
                            _editorGeometryDirty = true;
                    }
                    else
                    {
                        _panel.BeginEdit();
                    }
                    return true;

                case Key.Backspace:
                    _panel.Backspace();
                    return true;
            }

            return _panel.Editing;
        }

        /// <summary>
        /// Pose une entite a l'endroit vise, orientee comme la camera.
        ///
        /// L'orientation compte pour un point d'apparition - c'est la direction
        /// dans laquelle le joueur regardera. Autant la prendre du regard de
        /// l'editeur : c'est deja ce qu'on est en train de cadrer.
        /// </summary>
        private static void PlaceEntity(string type)
        {
            if (_editor is null) return;

            _editor.AddEntity(type, AimPointOnFloor(),
                              _camera.Yaw * 180f / MathF.PI,
                              _camera.Pitch * 180f / MathF.PI);
        }

        /// <summary>
        /// Cree une boite devant la camera, posee sur ce qu'on regarde.
        ///
        /// On vise un POINT du decor plutot qu'une distance fixe : une boite
        /// qui apparait a six metres dans le vide est inutilisable, il faut
        /// ensuite la ramener a la main.
        /// </summary>
        private static void CreateBoxAhead()
        {
            if (_editor is null) return;

            Vector3 eye = _editorEye;
            Vector3 target = eye + _camera.Forward * 8f;

            if (_level.Raycast(eye, _camera.Forward, 80f, out float distance, out Vector3 normal))
            {
                // On decolle de la surface le long de sa NORMALE : sinon la
                // nouvelle boite naitrait a moitie dans le mur visé.
                target = eye + _camera.Forward * distance + normal * 0.01f;
            }

            float step = MathF.Max(_editor.GridSize, 0.5f);
            var size = new Vector3(step * 2f);

            // Le coin bas de la boite se pose sur le point vise.
            _editor.Create(target - new Vector3(size.X * 0.5f, 0f, size.Z * 0.5f), size);
            _editorGeometryDirty = true;
        }

        private static void SaveEditedMap()
        {
            if (_editor is null) return;

            try
            {
                string name = SafeMapName(_editor.Map.Name);
                string path = Path.Combine(MapsFolder(), name + MapFile.Extension);

                MapFile.SaveTo(_editor.Map, path);
                _editor.MarkSaved();

                Console.WriteLine("  Editeur : carte enregistree dans " + path);
                _console.System.Print("  enregistree : " + path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("  Editeur : enregistrement impossible (" + ex.Message + ")");
                _console.System.Print("  enregistrement impossible : " + ex.Message);
            }
        }

        /// <summary>
        /// Un nom de carte devient un nom de FICHIER : il ne peut pas contenir
        /// n'importe quoi. "bac a sable" passe, "../../etc" non.
        /// </summary>
        private static string SafeMapName(string name)
        {
            var sb = new System.Text.StringBuilder();

            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');

            string cleaned = sb.ToString().Trim('_');

            return cleaned.Length == 0 ? "sans_nom" : cleaned;
        }

        /// <summary>
        /// Vol libre et manipulation continue. Appele au PAS FIXE : une
        /// vitesse de vol dependante du framerate serait injouable d'une
        /// machine a l'autre.
        /// </summary>
        private static void SimulateEditor(float dt)
        {
            if (_editor is null || _keyboard is null)
                return;

            // ------------------------------------------------------------
            // LA SOURIS DANS L'EDITEUR : LIBRE, ET CLIC DROIT POUR REGARDER
            //
            // ⚠️ AVANT, LA SOURIS TOURNAIT LA CAMERA EN PERMANENCE, et c'etait
            // le defaut qui rendait l'editeur penible : le curseur etait
            // capture et invisible, donc « ou je regarde » et « ou je pointe »
            // etaient forcement le meme point. Impossible de viser un objet sur
            // le cote, impossible d'atteindre le panneau de proprietes.
            //
            // Comportement de Hammer, d'Unreal et de Blender : curseur LIBRE, et
            // on maintient le clic DROIT pour pivoter.
            //
            // ⚠️⚠️ LE PIEGE EST LE CHANGEMENT DE MODE DE CURSEUR. Passer de
            // Normal a Raw teleporte la position signalee par le systeme : le
            // delta accumule sur cette image vaut alors des centaines de pixels,
            // et la camera fait un tour complet au premier clic droit. On JETTE
            // donc le delta a chaque bascule (_hasLastMouse = false).
            // ------------------------------------------------------------
            bool looking = _mouse?.IsButtonPressed(MouseButton.Right) == true;

            if (looking != _editorLooking)
            {
                _editorLooking = looking;

                if (_mouse != null)
                    _mouse.Cursor.CursorMode = looking ? CursorMode.Raw : CursorMode.Normal;

                // Le delta de CETTE image traverse la teleportation : il ne veut
                // rien dire, on le jette.
                _hasLastMouse = false;
                _mouseDelta = Vector2.Zero;
            }

            if (_editorLooking)
                _camera.AddLook(-_mouseDelta.X * _mouseSensitivity, -_mouseDelta.Y * _mouseSensitivity);

            _mouseDelta = Vector2.Zero;

            // Le panneau se reconstruit une fois par image : la carte bouge
            // sous lui (annulation, deplacement au clavier) et un panneau qui
            // garderait ses valeurs afficherait un etat qui n'existe plus.
            _panel?.Refresh();

            // ------------------------------------------------------------
            // VOL LIBRE
            //
            // ⚠️ CTRL COUPE LE DEPLACEMENT. Les raccourcis partagent leurs
            // lettres avec les touches de vol : Ctrl+Z faisait voler en avant
            // pendant qu'on annulait, Ctrl+S reculer pendant qu'on
            // enregistrait. Le raccourci fonctionnait, mais la camera partait
            // avec — et on ne voyait donc pas le resultat de son annulation.
            //
            // Consequence assumee : Ctrl gauche perd son role de "descendre"
            // tant qu'il est enfonce. Impossible de distinguer les deux
            // intentions, et un raccourci qui rate est plus grave qu'une
            // descente qui demande de lacher Ctrl.
            //
            // Defaut trouve en RELISANT le code, pas en jouant.
            // ------------------------------------------------------------
            bool ctrlHeld = _keyboard.IsKeyPressed(Key.ControlLeft) || _keyboard.IsKeyPressed(Key.ControlRight);

            // ⚠️ MEME RAISON QUE CTRL, un cran plus loin : tant qu'on TAPE dans
            // le panneau, ZQSD et X sont des lettres. Sans cette coupure, saisir
            // « speed » ferait reculer la camera de plusieurs metres pendant la
            // frappe - et on ne verrait pas l'objet qu'on est en train de regler.
            bool typing = _panel is { Focused: true, Editing: true };

            if (!ctrlHeld && !typing)
            {
                float speed = _keyboard.IsKeyPressed(Key.ShiftLeft) ? 34f : 11f;

                Vector3 move = Vector3.Zero;
                Vector3 forward = _camera.Forward;
                Vector3 right = _camera.Right;

                if (_keyboard.IsKeyPressed(Key.Z) || _keyboard.IsKeyPressed(Key.W)) move += forward;
                if (_keyboard.IsKeyPressed(Key.S)) move -= forward;
                if (_keyboard.IsKeyPressed(Key.D)) move += right;
                if (_keyboard.IsKeyPressed(Key.Q) || _keyboard.IsKeyPressed(Key.A)) move -= right;
                if (_keyboard.IsKeyPressed(Key.Space)) move += Vector3.UnitY;

                // Descendre est sur X et non sur Ctrl : Ctrl est desormais
                // reserve aux raccourcis, voir juste au-dessus.
                if (_keyboard.IsKeyPressed(Key.X)) move -= Vector3.UnitY;

                if (move.LengthSquared() > 1e-6f)
                    _editorEye += Vector3.Normalize(move) * speed * dt;
            }

            // --- selection au clic ---
            //
            // Detection de FRONT : un clic maintenu ne doit pas reselectionner
            // soixante fois par seconde. Ici c'est sans consequence, mais le
            // jour ou la selection declenchera autre chose, ce sera vital.
            bool pick = _mouse?.IsButtonPressed(MouseButton.Left) == true;

            if (pick && !_prevEditorPick)
            {
                // ⚠️ ON VISE SOUS LE CURSEUR, PAS AU CENTRE DE L'ECRAN. Avec un
                // curseur libre, viser le centre est un piege : on clique sur un
                // mur et c'est celui d'en face qui se selectionne. C'est la
                // moitie indispensable du curseur libre - l'autre est le clic
                // droit pour pivoter.
                Vector3 aim = EditorAimDirection();

                int box = _editor.Pick(_editorEye, aim);
                int entity = _editor.PickEntity(_editorEye, aim);

                float boxDistance = _editor.DistanceToBox(box, _editorEye, aim);
                float entityDistance = _editor.DistanceToEntity(entity, _editorEye, aim);

                // ⚠️ ON PREND LE PLUS PROCHE des deux, et l'entite gagne a
                // egalite. Un marqueur d'entite est presque toujours POSE SUR
                // une boite : privilegier la boite rendrait les entites
                // pratiquement impossibles a designer.
                if (entity >= 0 && (box < 0 || entityDistance <= boxDistance + 1e-4f))
                    _editor.SelectEntity(entity);
                else if (box >= 0)
                    _editor.Select(box);
                else
                    // Cliquer dans le vide DESELECTIONNE. Sans ca, impossible
                    // de revenir a "rien" sans toucher au clavier.
                    _editor.Deselect();
            }

            _prevEditorPick = pick;

            // --- manipulation par les fleches ---
            //
            // ⚠️ Un appui donne UN pas, puis la repetition demarre apres un
            // delai. Sans le delai, impossible de deplacer d'un seul pas ; sans
            // la repetition, deplacer de dix metres demanderait dix appuis.
            int move1 = (_keyboard.IsKeyPressed(Key.Right) ? 1 : 0) - (_keyboard.IsKeyPressed(Key.Left) ? 1 : 0);
            int size1 = (_keyboard.IsKeyPressed(Key.Up) ? 1 : 0) - (_keyboard.IsKeyPressed(Key.Down) ? 1 : 0);

            if (move1 == 0 && size1 == 0)
            {
                if (_editorStroke)
                {
                    // ⚠️ Fermer l'operation continue au RELACHEMENT : c'est ce
                    // qui fait qu'un deplacement maintenu ne compte que pour
                    // une annulation. Voir EditorState.BeginStroke.
                    _editor.EndStroke();
                    _editorStroke = false;
                }

                _editorRepeatAt = 0f;
                return;
            }

            bool first = !_editorStroke;

            if (first)
            {
                _editor.BeginStroke();
                _editorStroke = true;
                _editorRepeatAt = _simTime + 0.32f;
            }
            else if (_simTime < _editorRepeatAt)
            {
                return;
            }
            else
            {
                _editorRepeatAt = _simTime + 0.05f;
            }

            bool changed = false;

            if (move1 != 0) changed |= _editor.Move(_editor.Axis, move1);
            if (size1 != 0) changed |= _editor.Resize(_editor.Axis, size1);

            if (changed)
                _editorGeometryDirty = true;
        }

        /// <summary>
        /// Couleur du marqueur, par type d'entite.
        ///
        /// Des teintes VIVES et au-dela de 1 : ce sont des reperes d'edition,
        /// pas du decor. Elles doivent se distinguer d'un mur quelle que soit
        /// la lumiere de la scene.
        /// </summary>
        private static Vector3 EntityTint(string type) => type switch
        {
            EntityTypes.PlayerSpawn => new Vector3(0.3f, 1.5f, 0.5f),   // vert
            EntityTypes.Checkpoint => new Vector3(0.35f, 0.9f, 1.6f),   // bleu
            EntityTypes.Trigger => new Vector3(1.5f, 0.9f, 0.3f),       // ambre
            EntityTypes.Npc => new Vector3(1.5f, 0.4f, 0.6f),           // rose
            EntityTypes.Waypoint => new Vector3(0.9f, 0.9f, 1.2f),      // blanc bleute
            EntityTypes.Door => new Vector3(0.4f, 1.4f, 1.3f),          // cyan
            _ => new Vector3(1.2f, 0.5f, 1.2f),                         // magenta = type inconnu
        };

        private static void RequestGridMesh() => _gridMeshStep = -1f;

        /// <summary>
        /// Grille de l'editeur : de fines lattes posees au sol.
        ///
        /// Le moteur ne dessine que des triangles - il n'a pas de primitive de
        /// ligne. Une latte de 2 cm de large fait donc l'affaire, et coute
        /// 12 triangles : 81 lattes, moins de mille triangles au total.
        /// Reconstruite SEULEMENT quand le pas change.
        /// </summary>
        private static void EnsureGridMesh(GL gl, float step)
        {
            if (_gridMesh[0] != null && MathF.Abs(_gridMeshStep - step) < 1e-6f)
                return;

            _gridMesh[0]?.Dispose();

            var verts = new List<Vertex>();
            var indices = new List<uint>();

            const int half = 20;
            float extent = half * step;
            float thickness = MathF.Max(0.012f, step * 0.012f);

            var minor = new Vector3(0.22f, 0.26f, 0.32f);
            var major = new Vector3(0.55f, 0.62f, 0.72f);

            for (int i = -half; i <= half; i++)
            {
                float p = i * step;

                // Un trait sur cinq est plus clair : sans reperes, une grille
                // uniforme ne permet pas de compter les pas.
                Vector3 tint = i % 5 == 0 ? major : minor;

                MeshBuilder.AddBox(verts, indices,
                    Aabb.FromCenterSize(new Vector3(p, 0.01f, 0f), new Vector3(thickness, 0.004f, extent * 2f)), tint);

                MeshBuilder.AddBox(verts, indices,
                    Aabb.FromCenterSize(new Vector3(0f, 0.01f, p), new Vector3(extent * 2f, 0.004f, thickness)), tint);
            }

            _gridMesh[0] = new Mesh(gl,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(verts),
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(indices));

            _gridMeshStep = step;
        }

        private static void DrawEditorOverlay(int w, int h)
        {
            if (_hud is null || _editor is null) return;

            var white = new Vector4(0.92f, 0.95f, 1f, 1f);
            var dim = new Vector4(0.58f, 0.64f, 0.72f, 1f);
            var warn = new Vector4(1f, 0.72f, 0.35f, 1f);

            _hud.Rect(0f, 0f, w, 30f, new Vector4(0.05f, 0.07f, 0.1f, 0.78f));

            string axis = _editor.Axis switch { EditAxis.X => "X", EditAxis.Y => "Y", _ => "Z" };

            _hud.Text(14f, 8f, 1.9f,
                $"EDITEUR   AXE {axis}   GRILLE {_editor.GridSize:0.###} M   " +
                $"FORME {MapShapes.Name(_editor.NewShape).ToUpperInvariant()}   " +
                $"{_editor.Map.Boxes.Count} OBJETS   ANNULER {_editor.UndoDepth}/{_editor.RedoDepth}",
                white);

            if (_editor.Dirty)
                _hud.Text(w - 150f, 8f, 1.9f, "NON ENREGISTRE", warn);

            // Croix de visee : elle sert a designer une boite, elle doit donc
            // rester visible dans l'editeur.
            _hud.Crosshair(5f, 8f, 2f, new Vector4(0.3f, 1f, 0.5f, 0.9f));

            if (_editor.HasSelection)
            {
                MapBox box = _editor.Map.Boxes[_editor.Selected];

                _hud.TextBoxed(14f, h - 144f, 1.8f,
                    $"BOITE {_editor.Selected}   CENTRE {box.Center.X:0.##} {box.Center.Y:0.##} {box.Center.Z:0.##}",
                    white);
                _hud.TextBoxed(14f, h - 118f, 1.8f,
                    $"TAILLE {box.Size.X:0.##} X {box.Size.Y:0.##} X {box.Size.Z:0.##}", dim);
            }
            else if (_editor.SelectedEntity is MapEntity picked)
            {
                _hud.TextBoxed(14f, h - 144f, 1.8f,
                    $"ENTITE {_editor.Selected}   {picked.Type.ToUpperInvariant()}   " +
                    $"{picked.Position.X:0.##} {picked.Position.Y:0.##} {picked.Position.Z:0.##}   " +
                    $"LACET {picked.Yaw:0}",
                    white);

                // Les proprietes sont ce qui distingue une entite d'un simple
                // point : les cacher rendrait l'edition aveugle.
                var props = new System.Text.StringBuilder();

                foreach (var pair in picked.Properties)
                    props.Append(pair.Key).Append('=').Append(pair.Value).Append("   ");

                _hud.TextBoxed(14f, h - 118f, 1.8f,
                    props.Length == 0 ? "AUCUNE PROPRIETE — ed_prop cle valeur" : props.ToString().ToUpperInvariant(),
                    dim);
            }
            else
            {
                _hud.TextBoxed(14f, h - 118f, 1.8f,
                    "RIEN DE SELECTIONNE — CLIC GAUCHE SOUS LE CURSEUR POUR DESIGNER", dim);
            }

            // ⚠️ Le clic droit est annonce EN PREMIER et en clair. C'est la
            // commande la moins devinable de l'editeur : sans elle on croit que
            // la camera ne tourne pas, et rien a l'ecran ne le dirait.
            _hud.TextBoxed(14f, h - 88f, 1.7f,
                "CLIC DROIT REGARDER  CLIC GAUCHE SELECTIONNER  ZQSD VOLER  ESPACE/X MONTER-DESCENDRE  MAJ VITE  1/2/3 AXE  TAB PANNEAU",
                dim);
            _hud.TextBoxed(14f, h - 62f, 1.7f,
                "FLECHES DEPLACER/TAILLE  ENTREE CREER  B FORME  SUPPR EFFACER  C DUPLIQUER  G GRILLE  H TRAME  CTRL+Z/Y  CTRL+S  P DEPART",
                dim);
            _hud.TextBoxed(14f, h - 36f, 1.7f,
                "ENTITES  F2 APPARITION  F3 CHECKPOINT  F4 DECLENCHEUR  F5 PNJ  F6 POINT DE PASSAGE  F7 PORTE",
                new Vector4(0.45f, 0.85f, 0.6f, 1f));

            DrawPropertyPanel(w, h);
        }

        // ===================================================================
        //  Panneau de proprietes, a droite
        //
        //  ⚠️ AUCUNE REGLE D'EDITION ICI. Cette methode LIT PropertyPanel et
        //  dessine des rectangles, rien d'autre. Tout ce qui decide - quel
        //  champ existe, ce qu'une saisie vaut, si elle est acceptee - vit dans
        //  Core et s'y teste sans ouvrir de fenetre (regle n°1).
        // ===================================================================

        private const float PanelWidth = 330f;
        private const float PanelRowHeight = 21f;
        private const float PanelTextScale = 1.7f;

        private static void DrawPropertyPanel(int w, int h)
        {
            if (_hud is null || _panel is null)
                return;

            float x = w - PanelWidth;
            float top = 30f;

            // La zone d'aide du bas est reservee AVANT de calculer combien de
            // lignes tiennent : la calculer apres ferait passer la derniere
            // ligne sous le texte d'aide, donc illisible.
            float footer = 66f;
            float listTop = top + 30f;
            float listBottom = h - footer;

            int visible = Math.Max(1, (int)((listBottom - listTop) / PanelRowHeight));

            _hud.Rect(x, top, PanelWidth, h - top, new Vector4(0.05f, 0.06f, 0.08f, 0.93f));
            _hud.Rect(x, top, PanelWidth, 26f,
                _panel.Focused ? new Vector4(0.16f, 0.30f, 0.46f, 1f) : new Vector4(0.11f, 0.13f, 0.17f, 1f));

            _hud.Text(x + 10f, top + 8f, PanelTextScale, "PROPRIETES",
                new Vector4(0.92f, 0.95f, 1f, 1f));

            // L'etat du clavier doit se LIRE : sans ca, on tape dans le vide en
            // croyant piloter la camera, ou l'inverse. C'est le premier defaut
            // qu'un panneau modal produit.
            string state = _panel.Editing ? "SAISIE" : _panel.Focused ? "TAB : RENDRE" : "TAB : PRENDRE";

            _hud.Text(x + PanelWidth - 10f - BitmapFont.MeasureWidth(state, 1.5f), top + 9f, 1.5f, state,
                _panel.Editing ? new Vector4(1f, 0.85f, 0.4f, 1f) : new Vector4(0.62f, 0.70f, 0.80f, 1f));

            var fields = _panel.Fields;

            // Defilement : on garde la ligne courante a l'ecran. Un panneau qui
            // ne suit pas son curseur rend les dernieres proprietes d'un PNJ
            // atteignables mais invisibles - on les regle a l'aveugle.
            int first = 0;

            if (_panel.Index >= 0 && fields.Count > visible)
                first = Math.Clamp(_panel.Index - visible / 2, 0, fields.Count - visible);

            for (int row = 0; row < visible && first + row < fields.Count; row++)
            {
                int i = first + row;
                PropertyField field = fields[i];

                float y = listTop + row * PanelRowHeight;
                bool current = i == _panel.Index;

                if (field.Kind == FieldKind.Header)
                {
                    _hud.Rect(x, y, PanelWidth, PanelRowHeight, new Vector4(0.10f, 0.12f, 0.16f, 1f));
                    _hud.Text(x + 10f, y + 6f, 1.5f, field.Label.ToUpperInvariant(),
                        new Vector4(0.55f, 0.72f, 0.90f, 1f));
                    continue;
                }

                if (current)
                {
                    _hud.Rect(x, y, PanelWidth, PanelRowHeight,
                        _panel.Editing ? new Vector4(0.12f, 0.26f, 0.16f, 1f)
                       : _panel.Focused ? new Vector4(0.17f, 0.32f, 0.50f, 1f)
                                        : new Vector4(0.14f, 0.15f, 0.19f, 1f));
                }
                else if ((i & 1) == 0)
                {
                    _hud.Rect(x, y, PanelWidth, PanelRowHeight, new Vector4(0.07f, 0.08f, 0.11f, 0.6f));
                }

                _hud.Text(x + 10f, y + 6f, PanelTextScale, field.Label.ToUpperInvariant(),
                    new Vector4(0.60f, 0.66f, 0.75f, 1f));

                string value;
                Vector4 valueColor;

                if (current && _panel.Editing)
                {
                    // Le curseur est un vrai caractere et non un rectangle
                    // clignotant : il se voit sur une capture, donc la saisie
                    // se verifie en headless comme le reste.
                    value = _panel.Buffer + "_";
                    valueColor = new Vector4(1f, 1f, 0.75f, 1f);
                }
                else
                {
                    value = field.Display.Length == 0 ? "-" : field.Display;

                    // ⚠️ Une valeur GRISE veut dire « absente du fichier, c'est
                    // le defaut du moteur ». Sans cette nuance, on croit avoir
                    // regle une propriete qu'on n'a jamais ecrite - et on
                    // cherche pourquoi la copier sur une autre carte ne donne
                    // pas le meme resultat.
                    valueColor = field.IsDefault
                        ? new Vector4(0.45f, 0.49f, 0.56f, 1f)
                        : new Vector4(0.93f, 0.96f, 1f, 1f);
                }

                // Coupe a droite plutot que de deborder du panneau : un nom de
                // cible long ecrirait sinon par-dessus la scene.
                int maxChars = (int)((PanelWidth - 130f) / (BitmapFont.Advance * PanelTextScale));

                if (value.Length > maxChars && maxChars > 1)
                    value = value[..(maxChars - 1)] + ">";

                _hud.Text(x + PanelWidth - 10f - BitmapFont.MeasureWidth(value, PanelTextScale),
                          y + 6f, PanelTextScale, value.ToUpperInvariant(), valueColor);
            }

            // --- Bas du panneau : ce que fait le champ, ou ce qui a rate -----
            float hintY = h - footer + 6f;

            if (_panel.Message.Length > 0)
            {
                _hud.Text(x + 10f, hintY, 1.5f, _panel.Message.ToUpperInvariant(),
                    new Vector4(1f, 0.5f, 0.4f, 1f));
            }
            else if (_panel.Current is PropertyField hinted && hinted.Hint.Length > 0)
            {
                DrawWrapped(x + 10f, hintY, PanelWidth - 20f, 1.5f, hinted.Hint.ToUpperInvariant(),
                    new Vector4(0.58f, 0.64f, 0.72f, 1f), lines: 2);
            }

            _hud.Text(x + 10f, h - 20f, 1.5f,
                _panel.Editing ? "ENTREE VALIDER   ECHAP ANNULER"
                               : "HAUT/BAS CHOISIR   ENTREE MODIFIER   GAUCHE/DROITE FORME",
                new Vector4(0.45f, 0.75f, 0.95f, 1f));
        }

        /// <summary>
        /// Texte coupe en plusieurs lignes, sur la largeur donnee.
        ///
        /// Coupe aux ESPACES : couper au caractere pres rendrait les
        /// explications illisibles, et elles sont la seule chose qui dise a
        /// quoi sert « input_exit ».
        /// </summary>
        private static void DrawWrapped(float x, float y, float width, float scale,
                                        string text, Vector4 color, int lines)
        {
            if (_hud is null || text.Length == 0)
                return;

            int perLine = Math.Max(4, (int)(width / (BitmapFont.Advance * scale)));
            string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var line = new System.Text.StringBuilder();
            int drawn = 0;

            foreach (string word in words)
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > perLine)
                {
                    _hud.Text(x, y + drawn * (BitmapFont.LineHeight(scale) + 4f), scale, line.ToString(), color);
                    line.Clear();

                    if (++drawn >= lines)
                        return;
                }

                if (line.Length > 0)
                    line.Append(' ');

                line.Append(word);
            }

            if (line.Length > 0 && drawn < lines)
                _hud.Text(x, y + drawn * (BitmapFont.LineHeight(scale) + 4f), scale, line.ToString(), color);
        }

        /// <summary>
        /// L'interface du bac a sable.
        ///
        /// Le pipeline la reclame, en couleurs d'affichage, apres la
        /// composition. Elle ne s'occupe plus de la capture d'ecran : celle-ci
        /// se fait apres l'image ENTIERE, dans FinishHud.
        /// </summary>
        private static void DrawSandboxHud(HudRenderer hud, int w, int h)
        {
            hud.Begin(w, h);

            // Chaque etat a sa surcouche, et une seule. Empiler le viseur de
            // jeu par-dessus le menu donnerait une interface illisible.
            if (_mode == AppMode.Editing)
            {
                DrawEditorOverlay(w, h);
                _console.Render(hud, w, h);
                hud.End();
                return;
            }

            if (_mode == AppMode.Menu)
            {
                DrawMenu(w, h);
                _console.Render(hud, w, h);
                hud.End();
                return;
            }

            if (!_console.IsOpen)
            {
                float spreadDeg = Ballistics.GetSpread(_weapon, _motor.Stance);
                hud.Crosshair(4f + spreadDeg * 26f, 7f, 2f, new Vector4(0.25f, 1f, 0.35f, 0.9f));

                if (_simTime < _hitMarkerUntil)
                    hud.HitMarker(11f, 2f, new Vector4(1f, 0.9f, 0.3f, 0.95f));
            }

            if (_showStats)
            {
                int awake = 0;
                for (int i = 0; i < _physics.Bodies.Count; i++)
                    if (!_physics.Bodies[i].Sleeping) awake++;

                var dim = new Vector4(0.72f, 0.76f, 0.82f, 0.95f);
                var hot = new Vector4(1f, 0.75f, 0.35f, 0.95f);

                hud.TextBoxed(20f, h - 96f, 2f,
                    $"CAISSES {_physics.Bodies.Count}   ACTIVES {awake}", awake > 0 ? hot : dim);

                // ⚠️ L'ALTITUDE EST AFFICHEE POUR QU'UNE CAPTURE SE MESURE.
                // C'est le seul moyen de prouver qu'un ascenseur porte
                // vraiment son passager : les --exec partent tous a t=0, donc
                // on ne peut pas relire la position du joueur en console une
                // fois la plateforme arrivee. Meme raison que les chiffres des
                // PNJ juste en dessous.
                hud.TextBoxed(20f, h - 62f, 2f,
                    $"VITESSE {_motor.HorizontalSpeed:0.0} M/S   {_motor.Stance.ToString().ToUpperInvariant()}" +
                    $"   Y {_motor.Position.Y:0.00}", dim);

                hud.TextBoxed(20f, h - 28f, 2f, "F1 CONSOLE", new Vector4(0.5f, 0.85f, 0.6f, 0.9f));

                // Checkpoints franchis et dernier declenchement : sans ca, un
                // cablage qui marche est indiscernable d'un cablage muet.
                if (_entities.Checkpoints.Count > 0)
                    hud.TextBoxed(20f, 20f, 2f,
                        $"CHECKPOINTS {_entities.Checkpoints.Count} : " +
                        string.Join(" > ", _entities.Checkpoints).ToUpperInvariant(),
                        new Vector4(0.45f, 0.85f, 1f, 0.95f));

                if (_simTime < _lastEventUntil && _lastEvent.Length > 0)
                    hud.TextBoxed(20f, 54f, 2f, _lastEvent.ToUpperInvariant(),
                        new Vector4(1f, 0.85f, 0.4f, 0.95f));

                // ⚠️ ETAT DU RESEAU SUR LE HUD, pour la meme raison que les PNJ
                // et les portes : une capture doit pouvoir se MESURER. Sans ces
                // chiffres, une capture montrant deux personnages ne prouve pas
                // qu'ils viennent du reseau.
                if (_netClient is { } netClient)
                {
                    var netColor = new Vector4(0.5f, 0.8f, 1f, 0.95f);

                    hud.TextBoxed(20f, h - 130f, 1.8f,
                        $"RESEAU {netClient.State.ToString().ToUpperInvariant()}  ID {netClient.LocalId}  " +
                        $"PAS {netClient.ServerTick}  " +
                        $"RTT {(netClient.RoundTrip >= 0 ? $"{netClient.RoundTrip * 1000.0:0} MS" : "-")}  " +
                        $"VUS {netClient.Players.Count}",
                        netColor);

                    // ⚠️ L'etat de la PREDICTION sur le HUD, pour la meme raison
                    // que tout le reste : une capture doit se mesurer. Un ecart
                    // qui grimpe ou des corrections qui s'emballent se voient
                    // ici avant de se sentir a la manette.
                    hud.TextBoxed(20f, h - 182f, 1.8f,
                        $"PREDICTION {(netClient.PredictionEnabled ? "ON" : "OFF")}  " +
                        $"CORR {netClient.Corrections}  " +
                        $"ECART {netClient.LastPredictionError * 100f:0.0} CM  " +
                        $"PIRE {netClient.WorstPredictionError * 100f:0.0} CM",
                        netColor);

                    if (_netServer is { } netServer)
                        hud.TextBoxed(20f, h - 156f, 1.8f,
                            $"SERVEUR  {netServer.ClientCount} CLIENT(S)  PAS {netServer.Tick}  " +
                            $"{netServer.BytesSent / 1024} KO EMIS",
                            netColor);

                    // Position de chaque joueur distant : c'est ce qui rend la
                    // capture verifiable au chiffre.
                    int line = 0;

                    for (int i = 0; i < netClient.Players.Count && line < 4; i++)
                    {
                        PlayerState s = netClient.Players[i];

                        hud.TextBoxed(w - 340f, 130f + line * 26f, 1.7f,
                            $"{(s.Id == netClient.LocalId ? "MOI" : "JOUEUR " + s.Id)}  " +
                            $"{s.Position.X:0.00} {s.Position.Y:0.00} {s.Position.Z:0.00}",
                            s.Id == netClient.LocalId ? dim : netColor);

                        line++;
                    }
                }

                // Etat de chaque porte, pour la meme raison : une capture doit
                // pouvoir dire OU en est le mouvement, pas seulement qu'il a eu
                // lieu. Le pourcentage est ce qui distingue « ouverte » de
                // « bloquee a 40 % ».
                var doors = _movers.Movers;

                for (int i = 0; i < doors.Count && i < 4; i++)
                {
                    Mover m = doors[i];

                    hud.TextBoxed(w - 330f, 20f + i * 26f, 1.7f,
                        $"{(m.Name.Length > 0 ? m.Name : "PORTE " + i).ToUpperInvariant()}  " +
                        $"{m.State.ToString().ToUpperInvariant()}  {m.Fraction * 100f:0} %  Y {m.Center.Y:0.00}",
                        m.State is MoverState.Ouverture or MoverState.Fermeture ? hot : dim);
                }

                // Etat de chaque PNJ. Affiche parce qu'une capture d'ecran doit
                // pouvoir se MESURER : sans ces chiffres, on ne peut pas
                // verifier qu'un garde a bien avance entre deux instants.
                var agents = _agents.Agents;

                for (int i = 0; i < agents.Count && i < 6; i++)
                {
                    Agent a = agents[i];

                    hud.TextBoxed(20f, 88f + i * 26f, 1.7f,
                        $"{(a.Name.Length > 0 ? a.Name : "PNJ " + i).ToUpperInvariant()}  " +
                        $"{a.State.ToString().ToUpperInvariant()}  " +
                        $"{(a.SeesPlayer ? "VOIT " : "")}" +
                        $"{a.Position.X:0.0} {a.Position.Z:0.0}  {a.Speed:0.0} M/S  PT {a.Waypoint}",
                        a.State == AgentState.Chase
                            ? new Vector4(1f, 0.55f, 0.45f, 0.95f)
                            : new Vector4(0.7f, 0.85f, 0.7f, 0.9f));
                }
            }

            _console.Render(hud, w, h);

            hud.End();
        }

        /// <summary>
        /// Captures d'ecran, APRES le HUD.
        ///
        /// Extraite de DrawHud parce que le menu et l'editeur en sortent tot :
        /// sans ca, --screenshot ne produirait jamais rien dans ces deux etats,
        /// et sans le moindre message - exactement le genre de silence que la
        /// note sur --shot-at dans CLAUDE.md decrit deja.
        /// </summary>
        private static void FinishHud(EngineHost engine, int w, int h)
        {
            if (_screenshotRequested)
            {
                _screenshotRequested = false;
                SaveScreenshot(engine.Gl, w, h, _screenshotTarget);
            }

            if (_autoScreenshotPath != null && _simTime > _autoScreenshotTime)
            {
                SaveScreenshot(engine.Gl, w, h, _autoScreenshotPath);
                _autoScreenshotPath = null;
            }
        }

        // -------------------------------------------------------------------
        //  Capture d'ecran
        //
        //  ⚠️ LE CODE A DEMENAGE DANS LE MOTEUR (Graphics/Screenshot.cs), et
        //  c'est le JEU D'EXEMPLE qui l'a exige : il en avait besoin et ne
        //  pouvait pas y acceder sans recopier ces trente lignes. Il ne reste
        //  ici que ce qui appartient vraiment au bac a sable : l'echo dans sa
        //  console de debug.
        //
        //  Garder deux copies aurait ete pire que ne rien extraire : une
        //  correction sur l'une (l'alignement, l'inversion des lignes) ne
        //  serait jamais arrivee jusqu'a l'autre.
        // -------------------------------------------------------------------

        private static void SaveScreenshot(GL gl, int width, int height, string path)
        {
            string? full = Screenshot.Save(gl, width, height, path);

            if (full is null)
                return;

            Console.WriteLine($"  Capture : {full}  ({width}x{height})");
            _console.System.Print($"  capture -> {Path.GetFileName(full)}");
        }

        private static void OnUnload()
        {
            // La carte son S'ARRETE AVANT qu'on ecrive le fichier. Sauvegarder
            // pendant que le rappel ecrit encore donnerait un compte qui bouge
            // sous les pieds du lecteur.
            _audio?.Dispose();
            _audio = null;

            if (_recorder != null && _audioDumpPath != null)
            {
                try
                {
                    _recorder.Save(_audioDumpPath);

                    Console.WriteLine($"  Audio : {_recorder.Seconds:0.00} s ecrites dans " +
                                      $"{Path.GetFullPath(_audioDumpPath)}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("  Enregistrement audio impossible : " + ex.Message);
                }
            }

            _unitCube?.Dispose();

            for (int i = 0; i < _unitShapes.Length; i++)
                _unitShapes[i]?.Dispose();
            _levelMesh?.Dispose();
            _gridTexture?.Dispose();
            // Un seul Dispose pour les neuf objets de rendu. C'est aussi une
            // garantie : on ne peut plus en oublier un en ajoutant une passe.
            _render?.Dispose();
            _render = null;
        }

        // -------------------------------------------------------------------
        //  Eclairage du bac a sable.
        //
        //  Une directionnelle froide fait office de lumiere generale, et des
        //  ponctuelles chaudes marquent les zones. Le contraste chaud/froid
        //  est ce qui rend un greybox lisible : sans lui, tout se confond.
        // -------------------------------------------------------------------

        /// <summary>
        /// Reconstruit l'eclairage A PARTIR DE LA CARTE.
        ///
        /// ---------------------------------------------------------------
        /// COMMENT LA CARTE ET LES DEUX REGLAGES GLOBAUX COHABITENT
        ///
        /// La carte porte des intensites ABSOLUES - c'est ce qu'attend un
        /// format reutilisable. Mais `light_sun` et `light_lamps` doivent
        /// continuer de regler l'eclairage a chaud, sinon on perd le seul
        /// moyen de trouver ces valeurs (elles se jugent a l'oeil, pas au
        /// calcul).
        ///
        /// Les deux se concilient par un RAPPORT : au chargement, les deux
        /// ConVar prennent la valeur que la carte declare, et l'eclairage
        /// est ensuite mis a l'echelle de leur ecart a cette reference. Au
        /// chargement le facteur vaut donc exactement 1 - la carte s'affiche
        /// telle qu'elle a ete ecrite - et tourner le bouton fait varier
        /// toutes les lampes ensemble, en gardant leurs rapports.
        ///
        /// C'est aussi ce qui garde la console HONNETE : `light_lamps`
        /// affiche ce que la carte chargee utilise vraiment. Le piege des
        /// ConVar qui affichent autre chose que la realite a deja coute une
        /// session sur la SSAO, puis une autre sur snd_volume.
        /// ---------------------------------------------------------------
        /// </summary>
        private static void RebuildLights()
        {
            if (_scene == null)
                return;

            _scene.ClearLights();

            if (!_lightsEnabled || _map == null)
                return;

            float sunScale = _mapSunReference > 0f ? _sunIntensity / _mapSunReference : 1f;
            float lampScale = _mapLampReference > 0f ? _lampIntensity / _mapLampReference : 1f;

            // ⚠️ LA TRADUCTION A DEMENAGE DANS LE MOTEUR (Graphics/MapLighting).
            // Le jeu d'exemple recopiait ce switch a l'identique, ce qui est la
            // definition d'un couplage : un troisieme jeu l'aurait recopie aussi.
            // Ne reste ici que ce qui est propre au bac a sable, ses deux
            // facteurs de reglage en direct.
            _scene.LoadLights(_map, sunScale, lampScale);
        }

        /// <summary>
        /// Prend la carte comme nouvelle reference d'eclairage et aligne les
        /// deux ConVar dessus. Ecrire la ConVar declenche son handler, qui
        /// reaffecte la meme valeur et marque l'eclairage a refaire : c'est
        /// voulu, et c'est ce qui garantit qu'aucun chemin ne reste sur une
        /// ancienne valeur.
        /// </summary>
        private static void AdoptMapLighting(MapData map)
        {
            _mapSunReference = 0f;
            _mapLampReference = 0f;

            foreach (MapLight light in map.Lights)
            {
                if (_mapSunReference <= 0f && light.Kind == MapLightKind.Directional)
                    _mapSunReference = light.Intensity;

                if (_mapLampReference <= 0f && light.Kind == MapLightKind.Point)
                    _mapLampReference = light.Intensity;
            }

            if (_mapSunReference > 0f)
                AdoptUnlessTuned("light_sun", _mapSunReference, ref _sunIntensity);

            if (_mapLampReference > 0f)
                AdoptUnlessTuned("light_lamps", _mapLampReference, ref _lampIntensity);
        }

        /// <summary>
        /// Aligne un reglage global sur ce que la carte declare - SAUF s'il a
        /// deja ete regle a la main.
        ///
        /// Un reglage d'eclairage se trouve a l'oeil, en tatonnant, et se
        /// range dans exarion.cfg pour ne pas etre refait. Le charger puis
        /// l'ecraser au premier chargement de carte reviendrait a jeter
        /// exactement ce que 2.4 servait a garder. Une valeur laissee au
        /// defaut, en revanche, n'exprime aucune preference : la carte la
        /// prend.
        /// </summary>
        private static void AdoptUnlessTuned(string name, float declared, ref float field)
        {
            ConVar? variable = _console.System.Find(name);

            // Console pas encore construite : on prend ce que dit la carte.
            if (variable is null)
            {
                field = declared;
                return;
            }

            if (variable.Value != variable.Default)
            {
                // Reglage explicite : il gagne. L'eclairage sera mis a
                // l'echelle du rapport a la reference de la carte.
                field = variable.Value;
                return;
            }

            // La ConVar est bornee : on relit sa valeur apres ecriture, sinon
            // une carte declarant une intensite hors bornes serait affichee
            // autrement qu'elle n'est appliquee.
            variable.Value = declared;
            field = variable.Value;
        }

        // -------------------------------------------------------------------
        //  Decor du bac a sable : une grande salle, des plateformes a
        //  differentes hauteurs, un escalier, et de quoi tester le
        //  franchissement de marches.
        // -------------------------------------------------------------------

        // -------------------------------------------------------------------
        //  Application d'une carte, a chaud.
        //
        //  ⚠️ TOUT ce qui derive du decor doit etre refait ici, et l'oubli
        //  d'un seul de ces points donne un defaut silencieux :
        //
        //   - le MAILLAGE, sinon on voit l'ancien decor et on entre dans le
        //     nouveau. C'est le plus deroutant : l'image ment.
        //   - la PHYSIQUE, qui garde le monde de collision en champ.
        //   - le monde des PARTICULES, sinon les impacts rebondissent sur
        //     des murs qui n'existent plus.
        //   - le decor MARCHABLE du joueur (RebuildWalkable).
        //   - l'ECLAIRAGE, qui vient desormais de la carte.
        //
        //  L'ancien maillage est LIBERE : c'est de la memoire GPU, et un
        //  rechargement a chaud sert justement a etre repete cent fois.
        // -------------------------------------------------------------------

        /// <summary>
        /// Dossier des cartes. On remonte depuis l'executable jusqu'a trouver
        /// un dossier "maps", exactement comme pour Model3D : l'executable vit
        /// dans bin/Release/net9.0, le contenu a la racine du depot.
        /// </summary>
        private static string MapsFolder()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir is not null)
            {
                string candidate = Path.Combine(dir.FullName, "maps");
                if (Directory.Exists(candidate))
                    return candidate;

                dir = dir.Parent;
            }

            // Aucun dossier trouve : on le creera a cote de l'executable.
            return Path.Combine(AppContext.BaseDirectory, "maps");
        }

        /// <summary>
        /// Accepte aussi bien "arene" que "arene.map" ou un chemin complet.
        /// </summary>
        private static string ResolveMapPath(string name)
        {
            if (name.Contains(Path.DirectorySeparatorChar) || name.Contains('/'))
                return Path.GetExtension(name).Length > 0 ? name : name + MapFile.Extension;

            string file = Path.GetExtension(name).Length > 0 ? name : name + MapFile.Extension;
            return Path.Combine(MapsFolder(), file);
        }

        /// <summary>
        /// Carte du demarrage. Sans --map, c'est le bac a sable INTEGRE qui
        /// sert : un fichier present sur le disque ne doit pas remplacer le
        /// decor de reference en silence, sinon plus aucune mesure n'est
        /// comparable d'une session a l'autre.
        /// </summary>
        private static void LoadStartupMap(GL gl)
        {
            if (!string.IsNullOrEmpty(_startupMap))
            {
                try
                {
                    string path = ResolveMapPath(_startupMap);
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

            ApplyMap(gl, BuildSandboxMap(), "(bac a sable, integre au moteur)");
        }

        private static void ApplyMap(GL gl, MapData map, string source)
        {
            RebuildGeometry(gl, map, source);

            ApplySpawn(map);

            AdoptMapLighting(map);
            _lightsDirty = true;
        }

        /// <summary>
        /// Place le joueur sur un point d'apparition de la carte.
        ///
        /// ⚠️ La propriete `spawn` historique reste le REPLI, elle ne disparait
        /// pas : une carte qui ne declare aucune entite d'apparition ne doit pas
        /// laisser le joueur a l'origine du monde, ou il serait la plupart du
        /// temps DANS le sol.
        ///
        /// Le choix est deterministe - toujours le premier - et pas aleatoire.
        /// Un tirage rendrait les captures headless incomparables d'une session
        /// a l'autre, ce qui casserait toute la methode de verification.
        /// </summary>
        private static void ApplySpawn(MapData map)
        {
            Vector3 feet = map.Spawn;
            float yaw = map.SpawnYaw;
            float pitch = map.SpawnPitch;

            foreach (MapEntity spawn in map.SpawnPoints())
            {
                feet = spawn.Position;
                yaw = spawn.Yaw;
                pitch = spawn.Pitch;
                break;
            }

            _motor.Position = feet;
            _motor.Velocity = Vector3.Zero;
            _previousFeet = _motor.Position;

            // ⚠️ Les angles viennent de la MEME source que la position. Prendre
            // la position de l'entite et les angles de la carte donnerait un
            // joueur bien place qui regarde ailleurs - defaut discret, et
            // exactement le genre qu'on met une heure a attribuer.
            _camera.Yaw = yaw * MathF.PI / 180f;
            _camera.Pitch = pitch * MathF.PI / 180f;
        }

        /// <summary>
        /// Reconstruit tout ce qui DECOULE de la geometrie : monde de collision,
        /// maillage, physique, decor marchable.
        ///
        /// Separe d'ApplyMap parce que l'editeur en a besoin a chaque
        /// modification, et que lui ne doit SURTOUT pas se faire replacer au
        /// point de depart a chaque boite deplacee.
        /// </summary>
        private static void RebuildGeometry(GL gl, MapData map, string source)
        {
            var colors = new List<Vector3>();

            _map = map;
            _mapSource = source;
            _level = map.ToCollisionWorld(colors);

            _levelMesh?.Dispose();

            // ⚠️ On construit depuis la CARTE, plus depuis le monde de
            // collision : ce dernier ne porte que des AABB, il a donc deja
            // perdu la forme de chaque element. C'est aussi l'endroit precis
            // ou le visuel et la collision se separent - voir l'en-tete de
            // MeshBuilder, qui promettait l'inverse jusqu'au 17/08.
            _levelMesh = MeshBuilder.FromMap(gl, map);

            // Les caisses en vol appartenaient a l'ancien decor : les garder
            // reviendrait a les laisser dans des murs qui ont bouge.
            _physics = new PhysicsWorld(_level);
            _particles.World = _level;

            // ⚠️ Le monde d'entites est RECHARGE, pas conserve : un indice de
            // volume renvoie a une autre entite d'une carte a l'autre, et un
            // declencheur consomme bloquerait un declencheur sans rapport.
            _entities.Load(map);

            // Les PNJ se reconstruisent aussi : garder un garde en ronde sur un
            // chemin qui n'existe plus le ferait traverser le nouveau decor.
            _agents.Load(map, _level);
            RebuildAgentAnimators();

            // Les portes aussi, et APRES _entities.Load qui vide les cibles.
            _movers.Load(map);
            _movers.RegisterInto(_entities);

            RebuildWalkable();
        }

        /// <summary>
        /// Un Animator par PNJ, recree quand leur nombre change.
        ///
        /// On ne recree PAS a chaque image : un Animator porte une phase, et la
        /// remettre a zero soixante-quatre fois par seconde figerait tous les
        /// PNJ sur leur premiere pose.
        /// </summary>
        private static void RebuildAgentAnimators()
        {
            _agentAnimators.Clear();

            if (_characterModel is null)
                return;

            for (int i = 0; i < _agents.Agents.Count; i++)
            {
                var animator = new Animator(_characterModel);

                GltfAnimation? walk = _characterModel.FindAnimation("Walking");
                GltfAnimation? run = _characterModel.FindAnimation("Running");

                animator.SetClips(walk, run);

                // Chaque PNJ demarre a une phase DIFFERENTE. Sans ce decalage,
                // ils poseraient tous le meme pied au meme instant meme avec
                // des Animator separes - le defaut que les Animator separes
                // etaient censes eviter.
                animator.Phase = i * 0.37f;

                _agentAnimators.Add(animator);
            }
        }

        private static MapData BuildSandboxMap()
        {
            const float half = 30f;
            const float ceiling = 9f;
            const float wall = 0.6f;

            var w = new MapData { Name = "bac a sable", Spawn = new Vector3(0f, 1f, 20f) };

            var floorColor = new Vector3(0.34f, 0.35f, 0.38f);
            var wallColor = new Vector3(0.50f, 0.51f, 0.54f);
            var platformColor = new Vector3(0.42f, 0.50f, 0.58f);
            var stepColor = new Vector3(0.55f, 0.48f, 0.38f);
            var pillarColor = new Vector3(0.60f, 0.40f, 0.38f);

            void Add(Vector3 center, Vector3 size, Vector3 color) => w.AddBox(center, size, color);

            // Coque
            Add(new Vector3(0f, -0.5f, 0f), new Vector3(half * 2f, 1f, half * 2f), floorColor);

            // Plafond sur la MOITIE NORD seulement. La moitie sud reste a
            // ciel ouvert : sans elle, la lumiere directionnelle est bloquee
            // et les ombres portees sont invisibles - on ne peut donc ni les
            // regler ni les verifier. Constate par capture d'ecran.
            Add(new Vector3(0f, ceiling + 0.5f, -half * 0.5f),
                new Vector3(half * 2f, 1f, half), wallColor);

            float mid = ceiling * 0.5f;
            Add(new Vector3(-half - wall * 0.5f, mid, 0f), new Vector3(wall, ceiling, half * 2f), wallColor);
            Add(new Vector3(half + wall * 0.5f, mid, 0f), new Vector3(wall, ceiling, half * 2f), wallColor);
            Add(new Vector3(0f, mid, -half - wall * 0.5f), new Vector3(half * 2f, ceiling, wall), wallColor);
            Add(new Vector3(0f, mid, half + wall * 0.5f), new Vector3(half * 2f, ceiling, wall), wallColor);

            // Escalier : 8 marches de 30 cm, pour verifier le franchissement.
            for (int i = 0; i < 8; i++)
            {
                float y = 0.15f + i * 0.3f;
                float z = -6f - i * 1.2f;
                Add(new Vector3(-14f, y, z), new Vector3(6f, 0.3f + i * 0.3f, 1.2f), stepColor);
            }

            // Plateforme haute au bout de l'escalier
            Add(new Vector3(-14f, 1.2f, -20f), new Vector3(10f, 0.4f, 8f), platformColor);

            // Plateformes intermediaires, a sauter
            Add(new Vector3(10f, 0.8f, -8f), new Vector3(5f, 0.4f, 5f), platformColor);
            Add(new Vector3(17f, 1.6f, -14f), new Vector3(5f, 0.4f, 5f), platformColor);
            Add(new Vector3(22f, 2.4f, -20f), new Vector3(5f, 0.4f, 5f), platformColor);

            // Piliers : pour tester le glissement le long des murs
            for (int i = 0; i < 5; i++)
            {
                float x = -8f + i * 4f;
                Add(new Vector3(x, mid, 12f), new Vector3(1.2f, ceiling, 1.2f), pillarColor);
            }

            // Rampe en marches basses : test du franchissement automatique
            for (int i = 0; i < 6; i++)
                Add(new Vector3(8f + i * 1.5f, 0.1f + i * 0.2f, 20f), new Vector3(1.5f, 0.2f + i * 0.4f, 6f), stepColor);

            // Un enclos ouvert, pour empiler des caisses
            Add(new Vector3(0f, 0.6f, -14f), new Vector3(8f, 1.2f, 0.4f), wallColor);
            Add(new Vector3(-4f, 0.6f, -11f), new Vector3(0.4f, 1.2f, 6f), wallColor);
            Add(new Vector3(4f, 0.6f, -11f), new Vector3(0.4f, 1.2f, 6f), wallColor);

            // ---------------------------------------------------------------
            //  Les lumieres font partie de la CARTE, pas du programme.
            //
            //  Les intensites ecrites ici sont exactement celles que
            //  RebuildLights() calculait en dur : soleil a _sunIntensity,
            //  lampes a _lampIntensity, et les deux projecteurs a 2,2 et 2
            //  fois les lampes. Ces facteurs existaient deja dans le code -
            //  on ne fait que les sortir du programme.
            // ---------------------------------------------------------------

            w.Lights.Add(MapLight.Directional(
                _sunDirection, new Vector3(0.62f, 0.70f, 0.85f), _sunIntensity));

            var warm = new Vector3(1.0f, 0.78f, 0.48f);
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                    w.Lights.Add(MapLight.Point(new Vector3(x * 18f, 6.5f, z * 18f),
                                                warm, _lampIntensity, range: 26f));

            // Projecteur rouge sur l'enclos a caisses.
            w.Lights.Add(MapLight.Spot(
                new Vector3(0f, 7.5f, -11f), new Vector3(0f, -1f, 0f),
                new Vector3(1f, 0.42f, 0.35f), _lampIntensity * 2.2f,
                range: 18f, innerDegrees: 22f, outerDegrees: 34f));

            // Projecteur froid sur la plateforme haute.
            w.Lights.Add(MapLight.Spot(
                new Vector3(-14f, 8f, -20f), new Vector3(0.15f, -1f, 0.1f),
                new Vector3(0.55f, 0.75f, 1f), _lampIntensity * 2f,
                range: 20f, innerDegrees: 20f, outerDegrees: 32f));

            return w;
        }

        // -------------------------------------------------------------------

        private static void ParseArguments(string[] args, EngineOptions options)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--seconds" when i + 1 < args.Length:
                        options.AutoCloseAfterSeconds = ParseDouble(args[++i]);
                        break;
                    case "--tickrate" when i + 1 < args.Length:
                        options.TickRate = ParseDouble(args[++i]);
                        break;
                    case "--sensitivity" when i + 1 < args.Length:
                        _mouseSensitivity = (float)ParseDouble(args[++i]);
                        break;
                    case "--novsync":
                        options.VSync = false;
                        break;
                    // Sert a comparer le cout des ombres dans des conditions
                    // strictement identiques : --shadows 0 puis --shadows 1.
                    case "--shadows" when i + 1 < args.Length:
                        _startShadowStrength = (float)ParseDouble(args[++i]);
                        break;
                    // Capture automatique, puis on peut quitter avec --seconds.
                    // Permet de verifier un rendu sans intervention.
                    case "--screenshot" when i + 1 < args.Length:
                        _autoScreenshotPath = args[++i];
                        break;
                    // Instant de la capture, en secondes de simulation.
                    //
                    // Il valait 1 s en dur, et c'etait trop tot pour tout ce
                    // qui BOUGE : une pile lachee est encore en vol, une
                    // explosion n'a pas fini de retomber. On capturait donc
                    // systematiquement l'etat transitoire au lieu du resultat.
                    case "--shot-at" when i + 1 < args.Length:
                        _autoScreenshotTime = (float)ParseDouble(args[++i]);
                        break;
                    // Enregistre ce que la carte son recoit, en WAV.
                    //
                    // C'est la capture d'ecran du son : sans elle, un defaut
                    // audio ne se decouvre qu'a l'oreille, donc jamais pendant
                    // le developpement.
                    case "--map" when i + 1 < args.Length:
                        _startupMap = args[++i];
                        break;
                    case "--audio-dump" when i + 1 < args.Length:
                        _audioDumpPath = args[++i];
                        break;
                    case "--audio-seconds" when i + 1 < args.Length:
                        _audioDumpSeconds = Math.Max(1, (int)ParseDouble(args[++i]));
                        break;
                    // Execute une commande de console au demarrage. Repetable.
                    // Permet de tester un reglage sans recompiler ni taper.
                    case "--exec" when i + 1 < args.Length:
                        _startupCommands.Add(args[++i]);
                        break;
                }
            }
        }

        private static double ParseDouble(string s)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;
    }
}