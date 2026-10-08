// ===========================================================================
//  Le bac a sable vu par le pipeline de rendu
//
//  Sa geometrie, son dessin final, son interface : c'est TOUT ce qu'un jeu
//  doit fournir au moteur (IWorldRenderer). Plus une seule ligne de PIPELINE
//  ici - ni tampon, ni ordre de passe, ni chronometre. Que du contenu.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Exarion.Core.Assets;
using Exarion.Core.Entities;
using Exarion.Core.Net;
using Exarion.Core.Physics;
using Exarion.Engine.Graphics;
using Exarion.Game.Editing;
using Exarion.Game.Networking;
using Exarion.Game.World;
using Silk.NET.OpenGL;

namespace Exarion.Game.Rendering
{
    internal sealed class SandboxScene : IWorldRenderer, IDisposable
    {
        private readonly GameWorld _world;
        private readonly CharacterRig _rig;
        private readonly NetSession _net;
        private readonly EditorController _editor;
        private readonly Func<AppMode> _mode;
        private readonly Action<HudRenderer, int, int> _drawHud;

        private GL? _gl;
        private Mesh? _unitCube;

        public SandboxScene(GameWorld world, CharacterRig rig, NetSession net, EditorController editor,
                            Func<AppMode> mode, Action<HudRenderer, int, int> drawHud)
        {
            _world = world;
            _rig = rig;
            _net = net;
            _editor = editor;
            _mode = mode;
            _drawHud = drawHud;
        }

        /// <summary>Le decor, deja en coordonnees monde. Refait a chaque chargement.</summary>
        public Mesh? LevelMesh { get; private set; }

        /// <summary>Formes unitaires des marqueurs d'entites.</summary>
        public MarkerRenderer? Markers { get; private set; }

        /// <summary>
        /// Les maillages qui ne dependent d'aucune carte.
        ///
        /// Cube unitaire reutilise pour toutes les caisses et toutes les portes :
        /// un seul maillage en memoire, une matrice par instance au dessin.
        /// </summary>
        public void CreateResources(GL gl)
        {
            _gl = gl;

            var cubeVerts = new List<Vertex>();
            var cubeIndices = new List<uint>();
            MeshBuilder.AddBox(cubeVerts, cubeIndices,
                Aabb.FromCenterSize(Vector3.Zero, Vector3.One), new Vector3(0.72f, 0.58f, 0.36f));
            _unitCube = new Mesh(gl,
                CollectionsMarshal.AsSpan(cubeVerts),
                CollectionsMarshal.AsSpan(cubeIndices));

            // Une forme unitaire par type, construite UNE fois : voir MarkerRenderer.
            Markers = new MarkerRenderer(gl);
        }

        /// <summary>
        /// Refait le maillage du decor.
        ///
        /// ⚠️ On construit depuis la CARTE, plus depuis le monde de collision :
        /// ce dernier ne porte que des AABB, il a donc deja perdu la forme de
        /// chaque element. C'est aussi l'endroit precis ou le visuel et la
        /// collision se separent - voir l'en-tete de MeshBuilder.
        ///
        /// L'ancien maillage est LIBERE : c'est de la memoire GPU, et un
        /// rechargement a chaud sert justement a etre repete cent fois.
        /// </summary>
        public void RebuildLevelMesh(GL gl, MapData map)
        {
            LevelMesh?.Dispose();
            LevelMesh = MeshBuilder.FromMap(gl, map);
        }

        public void Dispose()
        {
            _unitCube?.Dispose();
            Markers?.Dispose();
            LevelMesh?.Dispose();
        }

        // ===================================================================
        //  IWorldRenderer
        // ===================================================================

        public void DrawGeometry(GeometrySink sink) => ForEachDrawable(
            (mesh, model, bones) => sink(mesh, model, bones));

        public void DrawHud(HudRenderer hud, int width, int height) => _drawHud(hud, width, height);

        /// <summary>
        /// Le dessin final : decor, caisses, portes, personnage, PNJ, joueurs
        /// distants, et les aides de l'editeur.
        /// </summary>
        public void DrawOpaque(SceneRenderer scene)
        {
            AppMode mode = _mode();

            // Decor : maillage deja en coordonnees monde, pas de transformation.
            scene.Draw(LevelMesh!, Matrix4x4.Identity, Vector3.One);

            // Caisses : un cube unitaire redessine avec une matrice par corps.
            var bodies = _world.Physics.Bodies;

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
            var movers = _world.Movers.Movers;

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
            if (_rig.Mesh != null)
            {
                if (ShowsMannequin(mode))
                {
                    if (_rig.Bones != null)
                        scene.SetBones(_rig.Bones);

                    scene.Draw(_rig.Mesh, _rig.Matrix, Vector3.One, _rig.Material);
                }

                IReadOnlyList<Agent> agents = AgentsToDraw(mode);

                for (int i = 0; i < agents.Count && i < _rig.AgentBones.Count; i++)
                {
                    if (_rig.AgentBones[i] is Matrix4x4[] bones)
                        scene.SetBones(bones);

                    // Un PNJ qui poursuit est teinte de rouge : le seul moyen de
                    // VOIR son etat sans lire la console.
                    Vector3 tint = agents[i].State == AgentState.Chase
                        ? new Vector3(1.35f, 0.75f, 0.72f)
                        : Vector3.One;

                    scene.Draw(_rig.Mesh, CharacterRig.AgentMatrix(agents[i]), tint, _rig.Material);
                }

                // Les joueurs distants. Teintes en bleu pour se distinguer des
                // PNJ : sans ca, on ne saurait pas si la silhouette d'en face est
                // un garde ou un vrai joueur, ce qui est le premier interet d'une
                // capture en reseau.
                if (_net.Client is { } client)
                {
                    var players = client.Players;

                    for (int i = 0; i < players.Count && i < _rig.NetBones.Count; i++)
                    {
                        if (!ShouldDrawNetPlayer(players[i]))
                            continue;

                        if (_rig.NetBones[i] is Matrix4x4[] bones)
                            scene.SetBones(bones);

                        scene.Draw(_rig.Mesh, CharacterRig.NetPlayerMatrix(players[i]),
                                   new Vector3(0.72f, 0.95f, 1.4f), _rig.Material);
                    }
                }

                // INDISPENSABLE : les uniforms survivent au dessin. Sans ca, le
                // prochain maillage rigide serait deforme par ces os.
                scene.ClearBones();
            }
            else if (Markers != null)
            {
                // ⚠️⚠️ SANS MODELE, UNE SILHOUETTE - pas le vide. Le modele glTF
                // est facultatif (la version publique n'en a pas) : jusqu'au
                // 07/10, PNJ et joueurs distants etaient alors simules mais
                // INVISIBLES. Un garde qu'on ne voit pas et qui vous repere est
                // pire qu'absent ; un joueur distant invisible rend le reseau
                // inverifiable a l'ecran.
                IReadOnlyList<Agent> agents = AgentsToDraw(mode);

                for (int i = 0; i < agents.Count; i++)
                {
                    Vector3 tint = agents[i].State == AgentState.Chase ? FallbackChase : FallbackAgent;
                    Markers.DrawHumanoid(scene, agents[i].Position, agents[i].Yaw * 180f / MathF.PI, tint);
                }

                if (_net.Client is { } client)
                {
                    var players = client.Players;

                    for (int i = 0; i < players.Count; i++)
                        if (ShouldDrawNetPlayer(players[i]))
                            Markers.DrawHumanoid(scene, players[i].Position, players[i].Yaw * 180f / MathF.PI,
                                                 FallbackNetPlayer);
                }
            }

            if (_world.ShowNavigation && Markers != null && mode != AppMode.Editing)
                DrawNavigation(scene, Markers);

            if (mode != AppMode.Editing || _editor.State is null || _gl is null)
                return;

            // --- Aides visuelles de l'editeur : grille, selection, marqueurs ---
            EditorView.DrawScene(_gl, scene, _editor, Markers!);
        }

        private static readonly Vector3 NavPathTint = new(0.35f, 1.10f, 1.45f);
        private static readonly Vector3 NavApproachTint = new(1.45f, 0.80f, 0.30f);

        /// <summary>
        /// « nav_show 1 » : ce qu'il reste a parcourir du chemin de chaque PNJ,
        /// en lattes posees dans le decor - bleu s'il mene a la cible, orange
        /// s'il ne mene qu'au plus pres d'une cible inaccessible.
        ///
        /// ⚠️ Passe finale SEULEMENT, pas ForEachDrawable : une aide de debogage
        /// qui projette une ombre ou assombrit l'occlusion change l'image qu'on
        /// est justement en train de juger.
        /// </summary>
        private void DrawNavigation(SceneRenderer scene, MarkerRenderer markers)
        {
            var agents = _world.Agents.Agents;
            var lift = new Vector3(0f, 0.08f, 0f);   // au ras du sol, sans s'y enfoncer

            for (int i = 0; i < agents.Count; i++)
            {
                Agent agent = agents[i];
                IReadOnlyList<Vector3> path = agent.Path;

                if (path.Count == 0)
                    continue;

                Vector3 tint = agent.NoPath ? NavApproachTint : NavPathTint;
                Vector3 from = agent.Position + lift;

                for (int p = Math.Max(1, agent.PathCursor); p < path.Count; p++)
                {
                    Vector3 to = path[p] + lift;

                    markers.DrawSegment(scene, from, to, 0.06f, tint);
                    markers.DrawShape(scene, MapShape.Sphere, to, new Vector3(0.18f), 0f, tint);
                    from = to;
                }
            }
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
        private void ForEachDrawable(Action<Mesh, Matrix4x4, Matrix4x4[]?> draw)
        {
            AppMode mode = _mode();

            draw(LevelMesh!, Matrix4x4.Identity, null);

            var bodies = _world.Physics.Bodies;
            for (int i = 0; i < bodies.Count; i++)
                draw(_unitCube!, ModelMatrix(bodies[i]), null);

            // Les portes passent par LE MEME point que le reste : elles heritent
            // donc de l'ombre et de l'occlusion sans une ligne de plus. Une
            // porte sans ombre se verrait tout de suite comme un decor colle.
            var movers = _world.Movers.Movers;
            for (int i = 0; i < movers.Count; i++)
                draw(_unitCube!, MoverMatrix(movers[i]), null);

            if (_rig.Mesh != null && ShowsMannequin(mode))
                draw(_rig.Mesh, _rig.Matrix, _rig.Bones);

            // Les PNJ passent par LE MEME point que le reste : ils heritent donc
            // des ombres et de l'occlusion ambiante sans une ligne de plus. Les
            // dessiner ailleurs aurait donne des personnages sans ombre, et il
            // aurait fallu comprendre pourquoi.
            //
            // ⚠️ LA MEME LISTE QUE LA PASSE FINALE (AgentsToDraw). Jusqu'au
            // 07/10, cette boucle-ci dessinait les PNJ meme dans l'editeur, ou
            // la passe finale les cache : des OMBRES de gardes sans gardes.
            if (_rig.Mesh != null)
            {
                IReadOnlyList<Agent> agents = AgentsToDraw(mode);

                for (int i = 0; i < agents.Count && i < _rig.AgentAnimators.Count; i++)
                    draw(_rig.Mesh, CharacterRig.AgentMatrix(agents[i]), _rig.AgentBones[i]);
            }

            // Les joueurs distants passent par LE MEME point : ils heritent donc
            // de l'ombre et de l'occlusion sans une ligne de plus.
            if (_rig.Mesh != null && _net.Client is { } client)
            {
                var players = client.Players;

                for (int i = 0; i < players.Count && i < _rig.NetBones.Count; i++)
                {
                    if (!ShouldDrawNetPlayer(players[i]))
                        continue;

                    draw(_rig.Mesh, CharacterRig.NetPlayerMatrix(players[i]), _rig.NetBones[i]);
                }
            }

            // Sans modele, les silhouettes de repli ont aussi leur ombre : les
            // MEMES pieces que la passe finale (ForEachHumanoidPart).
            if (_rig.Mesh == null && Markers != null)
            {
                void Part(Mesh mesh, Matrix4x4 model) => draw(mesh, model, null);

                IReadOnlyList<Agent> agents = AgentsToDraw(mode);

                for (int i = 0; i < agents.Count; i++)
                    Markers.ForEachHumanoidPart(agents[i].Position, agents[i].Yaw, Part);

                if (_net.Client is { } netClient)
                {
                    var players = netClient.Players;

                    for (int i = 0; i < players.Count; i++)
                        if (ShouldDrawNetPlayer(players[i]))
                            Markers.ForEachHumanoidPart(players[i].Position, players[i].Yaw, Part);
                }
            }
        }

        // Teintes des silhouettes de repli. Le rouge de poursuite et le bleu
        // des joueurs distants reprennent ceux du modele : un garde qui vous
        // poursuit doit se reconnaitre de la meme facon, modele ou pas.
        private static readonly Vector3 FallbackAgent = new(0.78f, 0.74f, 0.70f);
        private static readonly Vector3 FallbackChase = new(1.35f, 0.50f, 0.45f);
        private static readonly Vector3 FallbackNetPlayer = new(0.55f, 0.75f, 1.25f);

        /// <summary>
        /// Les PNJ a dessiner : tous, sauf dans l'editeur.
        ///
        /// ⚠️ PAS DE PNJ SIMULE DANS L'EDITEUR. Ils ne s'y animent pas
        /// (l'editeur n'appelle pas AgentWorld.Update) mais AgentWorld est
        /// recharge a chaque reconstruction de geometrie : ils apparaitraient
        /// donc en pose de liaison, bras ecartes, aux cotes de leur propre
        /// marqueur. Un objet dessine deux fois sous deux apparences est pire
        /// que pas d'objet du tout.
        /// </summary>
        private IReadOnlyList<Agent> AgentsToDraw(AppMode mode)
            => mode == AppMode.Editing ? Array.Empty<Agent>() : _world.Agents.Agents;

        /// <summary>
        /// Le mannequin, s'il est declare par la carte - et pas dans l'editeur,
        /// pour la meme raison que les PNJ : son marqueur y est deja.
        /// </summary>
        private bool ShowsMannequin(AppMode mode) => _rig.MannequinVisible && mode != AppMode.Editing;

        /// <summary>
        /// Faut-il dessiner ce joueur ?
        ///
        /// ⚠️ PAS SOI-MEME. On est en vue subjective : dessiner son propre corps
        /// mettrait un torse en plein ecran. C'est aussi ce qui evite de voir sa
        /// propre position corrigee par le serveur sauter devant les yeux.
        /// </summary>
        private bool ShouldDrawNetPlayer(in PlayerState state)
            => _net.Client is not null && state.Id != _net.Client.LocalId;

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
    }
}
