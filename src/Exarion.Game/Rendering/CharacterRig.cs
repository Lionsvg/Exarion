// ===========================================================================
//  Le personnage importe et ses animations (M2 -> M6, 2.2, E14, 3.4)
//
//  Un seul modele glTF, trois usages : le mannequin decoratif de la scene, un
//  exemplaire par PNJ, un exemplaire par joueur distant. Chacun a SON
//  Animator - un seul partage donnerait a tous la meme pose au meme instant,
//  trois gardes marchant au pas, jambe gauche synchronisee.
//
//  ⚠️ LE MODELE EST FACULTATIF. Le moteur demarre sans lui ; tout ce qui suit
//  doit donc survivre a un _model nul.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Exarion.Core.Animation;
using Exarion.Core.Assets;
using Exarion.Core.Entities;
using Exarion.Core.Net;
using Exarion.Engine.Graphics;
using Silk.NET.OpenGL;
using Texture = Exarion.Engine.Graphics.Texture;

namespace Exarion.Game.Rendering
{
    internal sealed class CharacterRig
    {
        // --- Personnage importe (M2, M3) ---
        public Mesh? Mesh { get; private set; }
        private Texture? _texture;
        public Material Material { get; private set; } = Material.Default;

        // --- Animation (M5, M6, 2.2) ---
        public GltfModel? Model { get; private set; }
        private Animator? _animator;

        /// <summary>Matrices d'os du mannequin pour l'image en cours, ou null.</summary>
        public Matrix4x4[]? Bones { get; private set; }

        /// <summary>« character_anim » : cycle de repli si les noms ne sont pas reconnus.</summary>
        public int AnimationIndex { get; set; }

        /// <summary>« character_speed » : vitesse de l'animation, 0 fige la pose.</summary>
        public float Speed { get; set; } = 1f;

        /// <summary>
        /// Horloge de l'animation : lisse (elle avance ENTRE deux pas fixes) et
        /// pourtant deterministe (elle ne lit pas le temps reel). Sans la
        /// premiere propriete l'animation saccade des que le rendu depasse 64
        /// images par seconde ; sans la seconde, deux captures headless a
        /// --seconds 3 ne donneraient pas la meme pose. Posee une fois par
        /// image, avant tout calcul de pose.
        /// </summary>
        public float AnimationClock { get; set; }

        private float _lastAnimationClock;

        /// <summary>« character_blend » : melange marche (0) vers course (1).</summary>
        public float Blend { get; set; }

        /// <summary>« character_offset » : decalage du cycle de course, en fractions de pas.</summary>
        public float Offset { get; set; }

        /// <summary>
        /// Phase imposee, ou -1 pour laisser courir. Sert a comparer deux
        /// melanges au MEME instant du pas - sinon les cadences different et
        /// les deux captures ne montrent pas la meme chose.
        /// </summary>
        public float Phase { get; set; } = -1f;

        /// <summary>Position du mannequin. « character_z » regle son Z.</summary>
        public Vector3 Position = new(0f, 0f, 15f);

        /// <summary>
        /// Lacet du mannequin, dans le repere du MODELE : il regarde vers +Z a
        /// lacet nul, alors que le lacet zero du moteur regarde vers -Z.
        /// « character_yaw » le regle directement.
        /// </summary>
        public float Yaw { get; set; }

        /// <summary>
        /// Vrai si la carte declare un mannequin. Il apparaissait sur TOUTES les
        /// cartes, au meme endroit : voir EntityTypes.Mannequin.
        /// </summary>
        public bool MannequinVisible { get; private set; }

        /// <summary>
        /// Pose le mannequin la ou la carte le declare, ou le cache.
        ///
        /// ⚠️ Le lacet de l'entite suit la convention du MOTEUR (0 regarde vers
        /// -Z), celui du modele la convention glTF (0 regarde vers +Z) : d'ou
        /// le demi-tour. Meme correction que pour les joueurs distants - sans
        /// elle, le mannequin tournerait le dos a ce qu'on lui fait regarder.
        ///
        /// ⚠️ Appele a chaque reconstruction du decor : un reglage fait a la
        /// console (character_z, character_yaw) ne survit donc pas a un
        /// changement de carte. C'est voulu - c'est la carte qui place ses
        /// accessoires, la console ne sert qu'a les essayer.
        /// </summary>
        public void PlaceMannequin(MapEntity? entity)
        {
            MannequinVisible = entity != null;

            if (entity is null)
                return;

            Position = entity.Position;

            // Ramene dans [-pi, pi[ : 180 + pi donne alors EXACTEMENT 0, et non
            // 2 pi, dont le sinus flottant vaut 1,7e-7. Le decor de reference
            // declare son mannequin a 180 : sans ce repli, il aurait tourne
            // d'un cheveu par rapport a toutes les captures anterieures.
            float yaw = entity.Yaw * MathF.PI / 180f + MathF.PI;

            if (yaw >= MathF.PI)
                yaw -= 2f * MathF.PI;

            Yaw = yaw;
        }

        // --- PNJ (E14) ---

        /// <summary>
        /// Un Animator PAR PNJ.
        ///
        /// ⚠️ C'est aussi le premier vrai usage du melange de poses (2.2). La
        /// note de l'epoque disait « le branchement tient en une ligne le jour
        /// ou il y aura une entite » : c'est cette ligne, le melange remplace
        /// par la vitesse de chaque agent.
        /// </summary>
        private readonly List<Animator> _agentAnimators = new();

        /// <summary>
        /// Matrices d'os de chaque PNJ pour l'image en cours.
        ///
        /// ⚠️ Calculees UNE FOIS par image et partagees entre les passes. Les
        /// recalculer dans chaque passe donnerait a l'ombre une pose differente
        /// du corps - exactement le defaut corrige en M6 pour le personnage.
        /// </summary>
        private readonly List<Matrix4x4[]?> _agentBones = new();
        private float _lastAgentClock;

        public IReadOnlyList<Animator> AgentAnimators => _agentAnimators;
        public IReadOnlyList<Matrix4x4[]?> AgentBones => _agentBones;

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

        private readonly List<Animator> _netAnimators = new();
        private readonly List<Matrix4x4[]?> _netBones = new();
        private readonly Dictionary<byte, Vector3> _netPrevious = new();
        private readonly Dictionary<byte, float> _netSpeed = new();
        private float _lastNetClock;

        public IReadOnlyList<Matrix4x4[]?> NetBones => _netBones;

        /// <summary>
        /// Charge le personnage glTF du dossier Model3D, s'il est la.
        ///
        /// L'absence du modele ne doit PAS empecher le moteur de demarrer :
        /// c'est un asset de developpement, pas une dependance. On le signale
        /// et on continue.
        /// </summary>
        public void Load(GL gl)
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
                Model = model;
                _animator = new Animator(model);

                SelectClips();

                // Blanc pur : la couleur de sommet MULTIPLIE la texture. Une
                // teinte grise assombrirait toute la texture d'autant.
                Mesh = MeshBuilder.FromGltf(gl, model, Vector3.One);

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

                    _texture = new Texture(gl, decoded);
                    Material = Material.Textured(_texture, roughness: 0.65f);

                    Console.WriteLine($"  Texture    : {decoded.Width}x{decoded.Height}, decodee par Png maison.");
                }
                else
                {
                    // Sans texture on garde une couleur unie plutot que de
                    // laisser le decor se projeter sur le personnage.
                    Material = Material.Matte(new Vector3(0.78f, 0.78f, 0.80f));
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
        /// Choisit le couple de cycles a melanger.
        ///
        /// Le cycle A (melange 0) est la MARCHE, le cycle B (melange 1) la
        /// COURSE. On les cherche par nom, avec repli sur les indices : un
        /// autre modele n'aura pas forcement les memes libelles, et le moteur
        /// ne doit pas dependre de la maniere dont un artiste a nomme ses
        /// exports.
        /// </summary>
        public void SelectClips()
        {
            GltfModel? model = Model;

            if (model is null || _animator is null)
                return;

            if (model.Animations.Count == 0)
                return;

            GltfAnimation? walk = model.FindAnimation("Walking") ?? model.FindAnimation("Walk");
            GltfAnimation? run = model.FindAnimation("Running") ?? model.FindAnimation("Run");

            // Repli : le premier cycle sert de marche, le second de course.
            walk ??= model.Animations[AnimationIndex % model.Animations.Count];
            run ??= model.Animations.Count > 1 ? model.Animations[1] : null;

            _animator.SetClips(walk, run);
        }

        /// <summary>
        /// Fait avancer l'animation du mannequin et prepare ses matrices d'os.
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
        public void UpdatePose()
        {
            if (Model is null || _animator is null || Model.Skins.Count == 0)
                return;

            float dt = (AnimationClock - _lastAnimationClock) * Speed;
            _lastAnimationClock = AnimationClock;

            _animator.Weight = Blend;
            _animator.OffsetB = Offset;

            // Phase imposee : on la pose ET on annule l'avance, sinon
            // Advance la repousserait aussitot.
            if (Phase >= 0f)
            {
                _animator.Phase = Phase;
                dt = 0f;
            }

            _animator.Advance(dt);

            Bones = _animator.SkinMatrices();
        }

        /// <summary>Transformation du mannequin. Ses pieds sont a sa position.</summary>
        public Matrix4x4 Matrix
            => Matrix4x4.CreateRotationY(Yaw) * Matrix4x4.CreateTranslation(Position);

        /// <summary>
        /// Un Animator par PNJ, recree quand leur nombre change.
        ///
        /// On ne recree PAS a chaque image : un Animator porte une phase, et la
        /// remettre a zero soixante-quatre fois par seconde figerait tous les
        /// PNJ sur leur premiere pose.
        /// </summary>
        public void RebuildAgentAnimators(IReadOnlyList<Agent> agents)
        {
            _agentAnimators.Clear();

            if (Model is null)
                return;

            for (int i = 0; i < agents.Count; i++)
            {
                var animator = new Animator(Model);

                GltfAnimation? walk = Model.FindAnimation("Walking");
                GltfAnimation? run = Model.FindAnimation("Running");

                animator.SetClips(walk, run);

                // Chaque PNJ demarre a une phase DIFFERENTE. Sans ce decalage,
                // ils poseraient tous le meme pied au meme instant meme avec
                // des Animator separes - le defaut que les Animator separes
                // etaient censes eviter.
                animator.Phase = i * 0.37f;

                _agentAnimators.Add(animator);
            }
        }

        /// <summary>
        /// Fait avancer l'animation de chaque PNJ.
        ///
        /// ⚠️⚠️ C'EST LE PREMIER VRAI USAGE DU MELANGE DE POSES (2.2). La note de
        /// l'epoque disait « rien ne pilote le melange automatiquement, le
        /// branchement tient en une ligne le jour ou il y aura une entite ».
        /// C'est cette ligne : le melange vaut la vitesse rapportee a la
        /// vitesse de course.
        /// </summary>
        public void UpdateAgentPoses(IReadOnlyList<Agent> agents)
        {
            while (_agentBones.Count < agents.Count) _agentBones.Add(null);
            while (_agentBones.Count > agents.Count) _agentBones.RemoveAt(_agentBones.Count - 1);

            if (Model is null || Model.Skins.Count == 0)
                return;

            float dt = (AnimationClock - _lastAgentClock) * Speed;
            _lastAgentClock = AnimationClock;

            for (int i = 0; i < agents.Count && i < _agentAnimators.Count; i++)
            {
                Agent agent = agents[i];
                Animator animator = _agentAnimators[i];

                // Marche a l'arret, course a pleine vitesse, et tout ce qu'il y
                // a entre les deux. La cadence du pas suit, parce que c'est
                // l'Animator qui melange les durees des deux cycles.
                float top = MathF.Max(0.5f, agent.Motor.Settings.RunSpeed);

                animator.Weight = Math.Clamp(agent.Speed / top, 0f, 1f);
                animator.OffsetB = Offset;

                animator.Advance(dt);

                _agentBones[i] = animator.SkinMatrices();
            }
        }

        /// <summary>
        /// Transformation d'un PNJ. Les pieds sont a sa position.
        ///
        /// ⚠️⚠️ LE DEMI-TOUR, COMME POUR LES JOUEURS DISTANTS. Le lacet du PNJ
        /// suit la convention du moteur (0 regarde vers -Z) ; le modele glTF
        /// regarde vers +Z au repos. Sans le pi, les PNJ COURAIENT A RECULONS
        /// depuis E14 - constate le 07/10 sur une capture : un garde en
        /// poursuite montrait son DOS a celui qu'il poursuivait. Les joueurs
        /// distants avaient deja la correction (NetPlayerMatrix) ; les PNJ,
        /// ecrits avant, ne l'avaient jamais eue.
        /// </summary>
        public static Matrix4x4 AgentMatrix(Agent agent)
            => Matrix4x4.CreateRotationY(agent.Yaw + MathF.PI)
             * Matrix4x4.CreateTranslation(agent.Position);

        /// <param name="client">Le client reseau, ou null hors ligne.</param>
        /// <param name="runSpeed">Vitesse de course du joueur local : un joueur distant s'y rapporte.</param>
        public void UpdateNetPoses(NetClient? client, float runSpeed)
        {
            if (client is null)
            {
                _netBones.Clear();
                return;
            }

            var players = client.Players;

            while (_netBones.Count < players.Count) _netBones.Add(null);
            while (_netBones.Count > players.Count) _netBones.RemoveAt(_netBones.Count - 1);

            if (Model is null || Model.Skins.Count == 0)
                return;

            while (_netAnimators.Count < players.Count)
            {
                var animator = new Animator(Model);
                animator.SetClips(Model.FindAnimation("Walking"),
                                  Model.FindAnimation("Running"));

                // Phase decalee, comme les PNJ.
                animator.Phase = _netAnimators.Count * 0.41f;
                _netAnimators.Add(animator);
            }

            float dt = (AnimationClock - _lastNetClock) * Speed;
            _lastNetClock = AnimationClock;

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

                animator.Weight = Math.Clamp(smoothed / MathF.Max(0.5f, runSpeed), 0f, 1f);
                animator.OffsetB = Offset;
                animator.Advance(dt);

                _netBones[i] = animator.SkinMatrices();
            }
        }

        /// <summary>
        /// Transformation d'un joueur distant. Ses pieds sont a sa position.
        ///
        /// ⚠️ Le lacet du modele est tourne de pi : le personnage glTF regarde
        /// vers +Z au repos, alors que le lacet zero du moteur regarde vers -Z.
        /// Sans ca, tout le monde court a reculons.
        /// </summary>
        public static Matrix4x4 NetPlayerMatrix(in PlayerState state)
            => Matrix4x4.CreateRotationY(state.Yaw + MathF.PI)
             * Matrix4x4.CreateTranslation(state.Position);
    }
}
