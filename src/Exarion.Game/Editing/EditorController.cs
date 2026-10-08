// ===========================================================================
//  L'editeur de niveaux (4.1) : etat, clavier, souris, vol libre
//
//  ⚠️⚠️ CE N'EST PAS UN EDITEUR A LA HAMMER. C'est le coeur de cette boucle -
//  selectionner, deplacer, redimensionner sur une grille, annuler,
//  enregistrer - plus un panneau de proprietes.
//
//  Toutes les REGLES d'edition vivent dans Core (EditorState, PropertyPanel)
//  et s'y testent sans fenetre. Ici, on ne fait que traduire des touches et
//  des clics en appels - c'est justement la que les defauts invisibles aux
//  tests se sont caches (voir AimOrigin et Ctrl, plus bas).
// ===========================================================================

using System;
using System.IO;
using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Console;
using Exarion.Core.Editor;
using Exarion.Core.Physics;
using Exarion.Engine.Graphics;
using Exarion.Game.Gameplay;
using Exarion.Game.Input;
using Silk.NET.Input;

namespace Exarion.Game.Editing
{
    internal sealed class EditorController
    {
        private readonly Camera _camera;
        private readonly InputDevices _input;
        private readonly ConsoleSystem _console;
        private readonly Func<CollisionWorld> _level;

        public EditorController(Camera camera, InputDevices input, ConsoleSystem console, Func<CollisionWorld> level)
        {
            _camera = camera;
            _input = input;
            _console = console;
            _level = level;
        }

        /// <summary>Etat de l'editeur. Cree au premier passage dans l'editeur.</summary>
        public EditorState? State { get; private set; }

        /// <summary>
        /// Panneau de proprietes, a droite. Cree en meme temps que l'editeur.
        ///
        /// ⚠️ Il porte le clavier quand il est focalise : c'est pour ca que
        /// OnKeyDown et Simulate l'interrogent AVANT toute autre chose. Sans ca,
        /// taper « speed » ferait voler la camera vers l'arriere (S) en meme
        /// temps qu'on ecrit.
        /// </summary>
        public PropertyPanel? Panel { get; private set; }

        public EditorGrid Grid { get; } = new();

        /// <summary>Position de l'oeil en vol libre. L'editeur n'utilise pas le CharacterMotor.</summary>
        public Vector3 Eye = new(0f, 6f, 24f);

        /// <summary>
        /// Une modification a eu lieu : le decor doit etre reconstruit.
        ///
        /// ⚠️ On ne reconstruit PAS a chaque appui mais une fois par image. Une
        /// fleche maintenue produit vingt operations par seconde, et chacune
        /// reconstruirait le maillage et le monde de collision.
        /// </summary>
        public bool GeometryDirty { get; set; }

        /// <summary>Vrai tant que le clic droit est maintenu.</summary>
        public bool Looking { get; set; }

        private float _repeatAt;
        private bool _stroke;
        private bool _prevPick;

        /// <summary>Taille du tampon rendu et de la fenetre, relevees au rendu.</summary>
        public int FrameWidth = 1280, FrameHeight = 720;
        public int WindowWidth = 1280, WindowHeight = 720;

        /// <summary>
        /// Prepare l'editeur sur la carte, et pose l'oeil.
        ///
        /// On edite la carte EN PLACE : la carte du jeu et State.Map sont le
        /// meme objet, donc la reconstruction voit toujours l'etat courant.
        /// </summary>
        public void Enter(MapData map, Vector3 eye)
        {
            if (State is null)
                State = new EditorState(map);
            else if (!ReferenceEquals(State.Map, map))
                State.Reset(map);

            // Le panneau suit l'editeur. Il est recree si l'editeur change
            // d'instance : un panneau qui garderait l'ancien montrerait les
            // champs d'une carte qu'on n'edite plus.
            Panel ??= new PropertyPanel(State);

            // ⚠️ On rend le clavier a l'editeur en ENTRANT. Sans ca, revenir du
            // menu avec le panneau focalise laisserait la camera muette, et
            // rien a l'ecran n'expliquerait pourquoi ZQSD ne fait plus voler.
            Panel.Unfocus();

            // On part de la position du joueur, un peu en hauteur : arriver a
            // l'origine du monde ferait perdre le contexte de ce qu'on editait.
            Eye = eye;
        }

        /// <summary>
        /// Direction visee : SOUS LE CURSEUR.
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
        public Vector3 AimDirection()
        {
            if (Looking || _input.Mouse is null)
                return _camera.Forward;

            Vector2 pos = _input.Mouse.Position;

            float scaleX = FrameWidth / (float)Math.Max(1, WindowWidth);
            float scaleY = FrameHeight / (float)Math.Max(1, WindowHeight);

            return _camera.RayThroughPixel(pos.X * scaleX, pos.Y * scaleY, FrameWidth, FrameHeight);
        }

        /// <summary>
        /// Point vise au sol, depuis l'oeil de l'EDITEUR.
        ///
        /// ⚠️ Pas depuis l'oeil du joueur : le corps simule reste fige la ou on
        /// l'a quitte, pendant que la camera vole. « Poser le point de depart »
        /// le placait par rapport a une position abandonnee, a des dizaines de
        /// metres de ce qu'on regarde. Defaut trouve en relisant le code, pas
        /// en jouant.
        ///
        /// ⚠️ On suit la MEME direction que la selection. C'est ce qui fait
        /// qu'une entite posee a F2..F7 atterrit sous le curseur et pas au
        /// centre de l'ecran - sinon poser et selectionner viseraient deux
        /// endroits differents, ce qui est pire que les deux comportements.
        /// </summary>
        public Vector3 AimPoint() => Aim.PointOnFloor(_level(), Eye, AimDirection());

        /// <summary>
        /// La saisie du panneau passe par KeyChar, PAS par KeyDown.
        ///
        /// ⚠️ KeyDown donne une touche PHYSIQUE : sur un clavier francais,
        /// Key.Semicolon produit « ; » ou « . » selon Maj, et Key.Q produit un
        /// « a ». Deduire le caractere de la touche donnerait un nom de cible
        /// faux sur tout clavier non americain - et juste sur le mien.
        /// </summary>
        public void Type(char c)
        {
            if (Panel is { Focused: true, Editing: true })
                Panel.Type(c);
        }

        /// <summary>Une touche enfoncee dans l'editeur.</summary>
        /// <param name="exitToMenu">Echap, quand rien de plus local ne la reclame.</param>
        public void OnKeyDown(Key key, Action exitToMenu)
        {
            if (State is null) return;

            // ⚠️ LE PANNEAU PASSE EN PREMIER, et il peut tout avaler. Tant
            // qu'une valeur est en cours de frappe, « C » doit ecrire un C et
            // surtout PAS dupliquer l'objet selectionne. L'ordre inverse
            // dupliquerait un mur a chaque lettre d'un nom de cible.
            if (Panel is { Focused: true } && PanelKey(key))
                return;

            bool shift = _input.Shift;
            bool ctrl = _input.Ctrl;

            switch (key)
            {
                case Key.Escape: exitToMenu(); return;

                // Tab prend le clavier pour le panneau. C'est la touche de
                // Roblox Studio et de tous les editeurs a formulaire : elle ne
                // sert a rien d'autre ici, et elle ne rentre en conflit ni avec
                // le vol ni avec les raccourcis.
                case Key.Tab: Panel?.ToggleFocus(); return;

                // Changer la forme des PROCHAINS objets crees. Maj pour
                // remonter, comme la grille sur G.
                case Key.B:
                    if (State.HasSelection)
                    {
                        State.SetShape(State.CycleNewShape(shift ? -1 : +1));
                        GeometryDirty = true;
                    }
                    else
                    {
                        State.CycleNewShape(shift ? -1 : +1);
                    }
                    return;

                case Key.Number1: State.Axis = EditAxis.X; return;
                case Key.Number2: State.Axis = EditAxis.Y; return;
                case Key.Number3: State.Axis = EditAxis.Z; return;

                case Key.G:
                    State.CycleGrid(shift ? -1 : +1);
                    Grid.Invalidate();
                    return;

                case Key.H: Grid.Visible = !Grid.Visible; return;

                case Key.Z when ctrl:
                    if (State.Undo()) GeometryDirty = true;
                    return;

                case Key.Y when ctrl:
                    if (State.Redo()) GeometryDirty = true;
                    return;

                case Key.S when ctrl: Save(); return;

                case Key.Enter:
                case Key.KeypadEnter:
                    CreateBoxAhead();
                    return;

                case Key.Delete:
                    if (State.Delete()) GeometryDirty = true;
                    return;

                case Key.C:
                    if (State.Duplicate() >= 0) GeometryDirty = true;
                    return;

                case Key.P:
                    // Le point de depart se pose la ou on regarde, au sol.
                    State.SetSpawn(AimPoint(), _camera.Yaw * 180f / MathF.PI,
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
                case Key.F8: PlaceEntity(EntityTypes.Crate); return;
                case Key.F9: PlaceEntity(EntityTypes.Mannequin); return;

                // ⚠️ Non verifiees au clavier : F10 est la touche de menu de
                // Windows, F12 celle du debogueur quand il est attache. La voie
                // verifiable est la console : « ed_entity logic_relay ».
                case Key.F10: PlaceEntity(EntityTypes.LogicRelay); return;
                case Key.F11: PlaceEntity(EntityTypes.LogicTimer); return;
                case Key.F12: PlaceEntity(EntityTypes.LogicCounter); return;
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
        private bool PanelKey(Key key)
        {
            if (Panel is null)
                return false;

            switch (key)
            {
                case Key.Tab:
                    Panel.ToggleFocus();
                    return true;

                case Key.Escape:
                    // Trois sens pour une touche, du plus local au plus global :
                    // abandonner la saisie, puis rendre le clavier, puis
                    // seulement quitter vers le menu. L'ordre inverse ferait
                    // sortir de l'editeur sur une faute de frappe.
                    if (Panel.Editing) Panel.CancelEdit();
                    else Panel.Unfocus();
                    return true;

                case Key.Up: Panel.MoveCursor(-1); return true;
                case Key.Down: Panel.MoveCursor(+1); return true;

                case Key.Left:
                    if (Panel.CycleValue(-1)) GeometryDirty = true;
                    return true;

                case Key.Right:
                    if (Panel.CycleValue(+1)) GeometryDirty = true;
                    return true;

                case Key.Enter:
                case Key.KeypadEnter:
                    if (Panel.Editing)
                    {
                        if (Panel.CommitEdit())
                            GeometryDirty = true;
                    }
                    else
                    {
                        Panel.BeginEdit();
                    }
                    return true;

                case Key.Backspace:
                    Panel.Backspace();
                    return true;
            }

            return Panel.Editing;
        }

        /// <summary>
        /// Pose une entite a l'endroit vise, orientee comme la camera.
        ///
        /// L'orientation compte pour un point d'apparition - c'est la direction
        /// dans laquelle le joueur regardera. Autant la prendre du regard de
        /// l'editeur : c'est deja ce qu'on est en train de cadrer.
        /// </summary>
        public void PlaceEntity(string type)
        {
            if (State is null) return;

            State.AddEntity(type, AimPoint(),
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
        public void CreateBoxAhead()
        {
            if (State is null) return;

            Vector3 eye = Eye;
            Vector3 target = eye + _camera.Forward * 8f;

            if (_level().Raycast(eye, _camera.Forward, 80f, out float distance, out Vector3 normal))
            {
                // On decolle de la surface le long de sa NORMALE : sinon la
                // nouvelle boite naitrait a moitie dans le mur visé.
                target = eye + _camera.Forward * distance + normal * 0.01f;
            }

            float step = MathF.Max(State.GridSize, 0.5f);
            var size = new Vector3(step * 2f);

            // Le coin bas de la boite se pose sur le point vise.
            State.Create(target - new Vector3(size.X * 0.5f, 0f, size.Z * 0.5f), size);
            GeometryDirty = true;
        }

        /// <summary>Enregistre la carte editee dans le dossier des cartes, sous un nom de fichier sur.</summary>
        public void Save()
        {
            if (State is null) return;

            try
            {
                string name = MapLibrary.SafeFileName(State.Map.Name);
                string path = Path.Combine(MapLibrary.FindFolder(), name + MapFile.Extension);

                MapFile.SaveTo(State.Map, path);
                State.MarkSaved();

                Console.WriteLine("  Editeur : carte enregistree dans " + path);
                _console.Print("  enregistree : " + path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("  Editeur : enregistrement impossible (" + ex.Message + ")");
                _console.Print("  enregistrement impossible : " + ex.Message);
            }
        }

        /// <summary>
        /// Vol libre et manipulation continue. Appele au PAS FIXE : une
        /// vitesse de vol dependante du framerate serait injouable d'une
        /// machine a l'autre.
        /// </summary>
        public void Simulate(float dt, float simTime)
        {
            if (State is null || _input.Keyboard is null)
                return;

            IKeyboard keyboard = _input.Keyboard;

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
            // donc le delta a chaque bascule (HasLastMouse = false).
            // ------------------------------------------------------------
            bool looking = _input.IsButtonDown(MouseButton.Right);

            if (looking != Looking)
            {
                Looking = looking;

                _input.SetCursor(looking ? CursorMode.Raw : CursorMode.Normal);

                // Le delta de CETTE image traverse la teleportation : il ne veut
                // rien dire, on le jette.
                _input.HasLastMouse = false;
                _input.MouseDelta = Vector2.Zero;
            }

            if (Looking)
                _camera.AddLook(-_input.MouseDelta.X * _input.Sensitivity, -_input.MouseDelta.Y * _input.Sensitivity);

            _input.MouseDelta = Vector2.Zero;

            // Le panneau se reconstruit une fois par image : la carte bouge
            // sous lui (annulation, deplacement au clavier) et un panneau qui
            // garderait ses valeurs afficherait un etat qui n'existe plus.
            Panel?.Refresh();

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
            bool ctrlHeld = keyboard.IsKeyPressed(Key.ControlLeft)
                         || keyboard.IsKeyPressed(Key.ControlRight);

            // ⚠️ MEME RAISON QUE CTRL, un cran plus loin : tant qu'on TAPE dans
            // le panneau, ZQSD et X sont des lettres. Sans cette coupure, saisir
            // « speed » ferait reculer la camera de plusieurs metres pendant la
            // frappe - et on ne verrait pas l'objet qu'on est en train de regler.
            bool typing = Panel is { Focused: true, Editing: true };

            if (!ctrlHeld && !typing)
            {
                float speed = keyboard.IsKeyPressed(Key.ShiftLeft) ? 34f : 11f;

                Vector3 move = Vector3.Zero;
                Vector3 forward = _camera.Forward;
                Vector3 right = _camera.Right;

                if (keyboard.IsKeyPressed(Key.Z) || keyboard.IsKeyPressed(Key.W)) move += forward;
                if (keyboard.IsKeyPressed(Key.S)) move -= forward;
                if (keyboard.IsKeyPressed(Key.D)) move += right;
                if (keyboard.IsKeyPressed(Key.Q) || keyboard.IsKeyPressed(Key.A)) move -= right;
                if (keyboard.IsKeyPressed(Key.Space)) move += Vector3.UnitY;

                // Descendre est sur X et non sur Ctrl : Ctrl est desormais
                // reserve aux raccourcis, voir juste au-dessus.
                if (keyboard.IsKeyPressed(Key.X)) move -= Vector3.UnitY;

                if (move.LengthSquared() > 1e-6f)
                    Eye += Vector3.Normalize(move) * speed * dt;
            }

            // --- selection au clic ---
            //
            // Detection de FRONT : un clic maintenu ne doit pas reselectionner
            // soixante fois par seconde. Ici c'est sans consequence, mais le
            // jour ou la selection declenchera autre chose, ce sera vital.
            bool pick = _input.IsButtonDown(MouseButton.Left);

            if (pick && !_prevPick)
            {
                // ⚠️ ON VISE SOUS LE CURSEUR, PAS AU CENTRE DE L'ECRAN. Avec un
                // curseur libre, viser le centre est un piege : on clique sur un
                // mur et c'est celui d'en face qui se selectionne. C'est la
                // moitie indispensable du curseur libre - l'autre est le clic
                // droit pour pivoter.
                Vector3 aim = AimDirection();

                int box = State.Pick(Eye, aim);
                int entity = State.PickEntity(Eye, aim);

                float boxDistance = State.DistanceToBox(box, Eye, aim);
                float entityDistance = State.DistanceToEntity(entity, Eye, aim);

                // ⚠️ ON PREND LE PLUS PROCHE des deux, et l'entite gagne a
                // egalite. Un marqueur d'entite est presque toujours POSE SUR
                // une boite : privilegier la boite rendrait les entites
                // pratiquement impossibles a designer.
                if (entity >= 0 && (box < 0 || entityDistance <= boxDistance + 1e-4f))
                    State.SelectEntity(entity);
                else if (box >= 0)
                    State.Select(box);
                else
                    // Cliquer dans le vide DESELECTIONNE. Sans ca, impossible
                    // de revenir a "rien" sans toucher au clavier.
                    State.Deselect();
            }

            _prevPick = pick;

            // --- manipulation par les fleches ---
            //
            // ⚠️ Un appui donne UN pas, puis la repetition demarre apres un
            // delai. Sans le delai, impossible de deplacer d'un seul pas ; sans
            // la repetition, deplacer de dix metres demanderait dix appuis.
            int move1 = (keyboard.IsKeyPressed(Key.Right) ? 1 : 0)
                      - (keyboard.IsKeyPressed(Key.Left) ? 1 : 0);
            int size1 = (keyboard.IsKeyPressed(Key.Up) ? 1 : 0)
                      - (keyboard.IsKeyPressed(Key.Down) ? 1 : 0);

            if (move1 == 0 && size1 == 0)
            {
                if (_stroke)
                {
                    // ⚠️ Fermer l'operation continue au RELACHEMENT : c'est ce
                    // qui fait qu'un deplacement maintenu ne compte que pour
                    // une annulation. Voir EditorState.BeginStroke.
                    State.EndStroke();
                    _stroke = false;
                }

                _repeatAt = 0f;
                return;
            }

            bool first = !_stroke;

            if (first)
            {
                State.BeginStroke();
                _stroke = true;
                _repeatAt = simTime + 0.32f;
            }
            else if (simTime < _repeatAt)
            {
                return;
            }
            else
            {
                _repeatAt = simTime + 0.05f;
            }

            bool changed = false;

            if (move1 != 0) changed |= State.Move(State.Axis, move1);
            if (size1 != 0) changed |= State.Resize(State.Axis, size1);

            if (changed)
                GeometryDirty = true;
        }
    }
}
