// ===========================================================================
//  Exarion - Coeur de l'editeur de niveaux (4.1)
//
//  Toute la LOGIQUE d'edition vit ici : selection, accrochage a la grille,
//  deplacement, redimensionnement, creation, suppression, annulation. Aucun
//  OpenGL, aucune fenetre, aucune touche de clavier - c'est la couche
//  Exarion.Game qui traduit les entrees en appels de ces methodes.
//
//  Consequence directe de la regle n°1 : l'historique d'annulation, qui est
//  la partie la plus facile a casser d'un editeur, se teste en ligne de
//  commande. On peut faire cinquante operations et cinquante annulations dans
//  un test et verifier qu'on retombe exactement sur la carte de depart.
//
//  ---------------------------------------------------------------------
//  L'ANNULATION SE FAIT PAR INSTANTANES, PAS PAR COMMANDES INVERSES.
//
//  Le motif habituel enregistre chaque operation avec son inverse : deplacer
//  de +2 s'annule par -2. C'est economique, et c'est aussi la ou vivent tous
//  les bugs d'annulation : il suffit qu'une operation ait ETE BORNEE - un
//  redimensionnement bute sur la taille minimale - pour que son "inverse" ne
//  la defasse plus exactement, et la carte derive silencieusement.
//
//  Une carte fait quelques centaines de boites, soit quelques dizaines de
//  kilooctets. On garde donc une COPIE COMPLETE avant chaque operation. C'est
//  grossier, c'est impossible a faire diverger, et sur cette echelle le cout
//  ne se mesure pas.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Combat;

namespace Exarion.Core.Editor
{
    public enum EditAxis { X, Y, Z }

    /// <summary>Ce sur quoi portent les operations.</summary>
    public enum EditTarget { None, Box, Entity }

    public sealed class EditorState
    {
        /// <summary>
        /// Profondeur d'historique. Au-dela on oublie le plus ancien : un
        /// historique sans borne est une fuite de memoire deguisee.
        /// </summary>
        public const int MaxHistory = 64;

        /// <summary>
        /// Taille minimale d'une boite. Une boite de taille nulle ou negative
        /// casse a la fois la collision (un intervalle inverse ne croise
        /// jamais rien) et le maillage (des faces retournees).
        /// </summary>
        public const float MinimumSize = 0.05f;

        /// <summary>Pas d'accrochage disponibles, du plus fin au plus grossier.</summary>
        public static readonly float[] GridSteps = { 0.125f, 0.25f, 0.5f, 1f, 2f, 4f, 8f };

        private readonly List<MapData> _undo = new();
        private readonly List<MapData> _redo = new();

        private bool _strokeOpen;
        private int _gridIndex = 3;   // 1 m

        public EditorState(MapData map)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
        }

        /// <summary>
        /// Taille du marqueur d'une entite, pour la designer au rayon.
        ///
        /// Une entite n'a pas de volume : sans marqueur, elle serait impossible
        /// a cliquer. 60 cm est un compromis - assez grand pour viser, assez
        /// petit pour ne pas masquer la boite derriere.
        /// </summary>
        public const float EntityMarkerSize = 0.6f;

        public MapData Map { get; private set; }

        /// <summary>Nature de ce qui est selectionne.</summary>
        public EditTarget Target { get; private set; } = EditTarget.None;

        /// <summary>
        /// Indice dans Boxes OU dans Entities, selon Target.
        ///
        /// Un seul indice pour les deux listes : les operations sont les memes -
        /// deplacer, supprimer, dupliquer - et deux champs separes ouvriraient
        /// la porte a un etat incoherent, du genre "une boite ET une entite
        /// selectionnees".
        /// </summary>
        public int Selected { get; private set; } = -1;

        /// <summary>Vrai si une BOITE est selectionnee.</summary>
        public bool HasSelection =>
            Target == EditTarget.Box && Selected >= 0 && Selected < Map.Boxes.Count;

        /// <summary>Vrai si une ENTITE est selectionnee.</summary>
        public bool HasEntity =>
            Target == EditTarget.Entity && Selected >= 0 && Selected < Map.Entities.Count;

        public bool HasAnything => HasSelection || HasEntity;

        /// <summary>Entite selectionnee, ou null.</summary>
        public MapEntity? SelectedEntity => HasEntity ? Map.Entities[Selected] : null;

        public EditAxis Axis { get; set; } = EditAxis.X;

        public float GridSize => GridSteps[_gridIndex];

        /// <summary>Vrai si la carte a change depuis le dernier enregistrement.</summary>
        public bool Dirty { get; private set; }

        public int UndoDepth => _undo.Count;
        public int RedoDepth => _redo.Count;

        /// <summary>Couleur des boites creees. Reglable depuis la console.</summary>
        public Vector3 NewColor { get; set; } = new Vector3(0.45f, 0.46f, 0.50f);

        /// <summary>
        /// Forme des elements crees ensuite.
        ///
        /// C'est un etat d'OUTIL, pas un etat de selection : on choisit une
        /// forme puis on en pose plusieurs, comme on choisit un pinceau. La
        /// remettre a « bloc » apres chaque creation obligerait a la
        /// rechoisir a chaque mur d'une rangee d'arches.
        /// </summary>
        public MapShape NewShape { get; set; } = MapShape.Block;

        // ===================================================================
        //  Selection
        // ===================================================================

        /// <summary>Selectionne une BOITE. Conserve pour ne pas casser l'usage courant.</summary>
        public void Select(int index)
        {
            if (index >= 0 && index < Map.Boxes.Count)
            {
                Selected = index;
                Target = EditTarget.Box;
            }
            else
            {
                Deselect();
            }
        }

        public void SelectEntity(int index)
        {
            if (index >= 0 && index < Map.Entities.Count)
            {
                Selected = index;
                Target = EditTarget.Entity;
            }
            else
            {
                Deselect();
            }
        }

        public void Deselect()
        {
            Selected = -1;
            Target = EditTarget.None;
        }

        /// <summary>
        /// Boite touchee par un rayon, la plus PROCHE. Rend -1 si le rayon ne
        /// touche rien.
        ///
        /// On reutilise RayVsAabb de Combat, deja ecrit et teste pour le tir.
        /// Ecrire un second lancer de rayon donnerait deux implementations a
        /// maintenir, et surtout deux comportements a faire divergerdans les
        /// cas limites - un rayon qui demarre DANS une boite, par exemple.
        /// </summary>
        public int Pick(Vector3 origin, Vector3 direction, float maxDistance = 200f)
        {
            int best = -1;
            float bestDistance = maxDistance;

            for (int i = 0; i < Map.Boxes.Count; i++)
            {
                MapBox box = Map.Boxes[i];
                var bounds = Physics.Aabb.FromCenterSize(box.Center, box.Size);

                if (CombatTarget.RayVsAabb(origin, direction, bestDistance, bounds, out float d) && d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Entite touchee par un rayon, la plus proche. Rend -1 sinon.
        ///
        /// Separee de Pick a dessein : le jeu appelle les deux et garde le plus
        /// proche. Fusionner les deux ferait rendre un couple (nature, indice)
        /// a tous les appelants, y compris ceux qui ne veulent que des boites.
        /// </summary>
        public int PickEntity(Vector3 origin, Vector3 direction, float maxDistance = 200f)
        {
            int best = -1;
            float bestDistance = maxDistance;
            var marker = new Vector3(EntityMarkerSize);

            for (int i = 0; i < Map.Entities.Count; i++)
            {
                var bounds = Physics.Aabb.FromCenterSize(Map.Entities[i].Position, marker);

                if (CombatTarget.RayVsAabb(origin, direction, bestDistance, bounds, out float d) && d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Distance au premier impact, ou -1. Sert a arbitrer boite contre entite.</summary>
        public float DistanceToBox(int index, Vector3 origin, Vector3 direction, float maxDistance = 200f)
        {
            if (index < 0 || index >= Map.Boxes.Count)
                return -1f;

            MapBox box = Map.Boxes[index];
            var bounds = Physics.Aabb.FromCenterSize(box.Center, box.Size);

            return CombatTarget.RayVsAabb(origin, direction, maxDistance, bounds, out float d) ? d : -1f;
        }

        public float DistanceToEntity(int index, Vector3 origin, Vector3 direction, float maxDistance = 200f)
        {
            if (index < 0 || index >= Map.Entities.Count)
                return -1f;

            var bounds = Physics.Aabb.FromCenterSize(Map.Entities[index].Position, new Vector3(EntityMarkerSize));

            return CombatTarget.RayVsAabb(origin, direction, maxDistance, bounds, out float d) ? d : -1f;
        }

        // ===================================================================
        //  Grille
        // ===================================================================

        public void CycleGrid(int direction)
            => _gridIndex = Math.Clamp(_gridIndex + Math.Sign(direction), 0, GridSteps.Length - 1);

        /// <summary>Arrondit chaque composante au pas de grille le plus proche.</summary>
        public Vector3 Snap(Vector3 value)
        {
            float step = GridSize;

            return new Vector3(MathF.Round(value.X / step) * step,
                               MathF.Round(value.Y / step) * step,
                               MathF.Round(value.Z / step) * step);
        }

        // ===================================================================
        //  Historique
        // ===================================================================

        /// <summary>
        /// Ouvre une operation CONTINUE : tout ce qui suit jusqu'a EndStroke
        /// ne comptera que pour UNE annulation.
        ///
        /// ⚠️ C'est le bug classique de l'annulation. Une touche maintenue
        /// appelle Move soixante fois par seconde ; sans regroupement,
        /// annuler un deplacement d'un metre demanderait soixante Ctrl+Z.
        /// </summary>
        public void BeginStroke()
        {
            if (_strokeOpen)
                return;

            Snapshot();
            _strokeOpen = true;
        }

        public void EndStroke() => _strokeOpen = false;

        private void Snapshot()
        {
            // Pendant une operation continue, l'instantane est deja pris.
            if (_strokeOpen)
                return;

            _undo.Add(Map.Clone());

            if (_undo.Count > MaxHistory)
                _undo.RemoveAt(0);

            // ⚠️ Une nouvelle operation APRES une annulation invalide tout ce
            // qui avait ete annule. Oublier cette ligne donne un "retablir"
            // qui ressuscite une branche abandonnee de l'historique - et une
            // carte qui n'a jamais existe.
            _redo.Clear();
        }

        public bool Undo()
        {
            if (_undo.Count == 0)
                return false;

            _strokeOpen = false;

            _redo.Add(Map.Clone());

            Map = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);

            ClampSelection();
            Dirty = true;

            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0)
                return false;

            _strokeOpen = false;

            _undo.Add(Map.Clone());

            Map = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);

            ClampSelection();
            Dirty = true;

            return true;
        }

        /// <summary>
        /// Apres une annulation, la carte peut avoir moins de boites qu'avant :
        /// une selection qui pointait sur la derniere deviendrait hors bornes.
        /// </summary>
        private void ClampSelection()
        {
            int count = Target == EditTarget.Entity ? Map.Entities.Count : Map.Boxes.Count;

            if (Selected >= count)
                Selected = count - 1;

            if (Selected < 0)
                Target = EditTarget.None;
        }

        // ===================================================================
        //  Operations
        // ===================================================================

        /// <summary>Deplace la selection d'un nombre entier de pas de grille.</summary>
        public bool Move(EditAxis axis, int steps)
        {
            if (steps == 0)
                return false;

            // Une entite se deplace comme une boite : c'est tout l'interet
            // d'avoir un seul indice et une seule notion de cible.
            if (HasEntity)
            {
                Snapshot();

                MapEntity entity = Map.Entities[Selected];
                entity.Position = Add(entity.Position, axis, steps * GridSize);

                Dirty = true;
                return true;
            }

            if (!HasSelection)
                return false;

            Snapshot();

            MapBox box = Map.Boxes[Selected];
            Vector3 center = box.Center;

            // On avance d'un MULTIPLE du pas, jamais d'une valeur libre : c'est
            // ce qui garantit qu'une boite alignee reste alignee. Accrocher le
            // resultat a la grille aurait l'effet inverse - une boite dont les
            // faces sont a 0,5 se ferait recentrer et perdrait son alignement.
            center = Add(center, axis, steps * GridSize);

            Map.Boxes[Selected] = new MapBox(center, box.Size, box.Color, box.Shape);
            Dirty = true;

            return true;
        }

        /// <summary>
        /// Redimensionne la selection en deplacant UNE face.
        ///
        /// C'est la semantique de Hammer : on tire une face, le cote oppose ne
        /// bouge pas. Grossir symetriquement autour du centre serait plus
        /// simple a ecrire mais desaligne les deux faces des que la taille
        /// n'est pas un multiple du double du pas.
        /// </summary>
        /// <param name="farSide">Vrai pour tirer la face du cote positif.</param>
        public bool Resize(EditAxis axis, int steps, bool farSide = true)
        {
            if (!HasSelection || steps == 0)
                return false;

            Snapshot();

            MapBox box = Map.Boxes[Selected];

            Vector3 min = box.Center - box.Size * 0.5f;
            Vector3 max = box.Center + box.Size * 0.5f;

            float delta = steps * GridSize;

            if (farSide)
                max = Add(max, axis, delta);
            else
                min = Add(min, axis, -delta);

            float low = Component(min, axis);
            float high = Component(max, axis);
            float length = high - low;

            // ------------------------------------------------------------
            // ⚠️ Bornage OBLIGATOIRE. Une taille nulle ou negative casse a la
            // fois la collision (un intervalle inverse ne croise jamais rien,
            // donc le joueur traverse) et le maillage (faces retournees). Et
            // c'est trivial a declencher : maintenir la touche de reduction
            // deux secondes.
            //
            // ⚠️ On impose la LONGUEUR exacte, on ne la recalcule pas par
            // high - low. Attrape par un test qui exigeait >= 0,05 et
            // mesurait 0,0499999523 apres 200 reductions : a chaque appel on
            // reconstruit min/max depuis le centre, et l'erreur d'arrondi
            // s'accumule. Une borne qui derive n'est pas une borne.
            // ------------------------------------------------------------
            if (length < MinimumSize)
            {
                length = MinimumSize;

                // La face TIREE bouge, la face opposee reste ou elle est.
                if (farSide)
                    high = low + length;
                else
                    low = high - length;
            }

            Vector3 center = Set((min + max) * 0.5f, axis, (low + high) * 0.5f);
            Vector3 size = Set(max - min, axis, length);

            Map.Boxes[Selected] = new MapBox(center, size, box.Color, box.Shape);
            Dirty = true;

            return true;
        }

        /// <summary>
        /// Cree une boite et la selectionne. Rend son indice.
        ///
        /// Le COIN est accroche a la grille, pas le centre : accrocher le
        /// centre d'une boite de taille impaire poserait ses faces entre deux
        /// lignes, et plus rien ne s'emboiterait proprement.
        /// </summary>
        public int Create(Vector3 nearCorner, Vector3 size)
        {
            Snapshot();

            Vector3 min = Snap(nearCorner);

            size = new Vector3(MathF.Max(MinimumSize, size.X),
                               MathF.Max(MinimumSize, size.Y),
                               MathF.Max(MinimumSize, size.Z));

            Map.Boxes.Add(new MapBox(min + size * 0.5f, size, NewColor, NewShape));

            Selected = Map.Boxes.Count - 1;
            Target = EditTarget.Box;
            Dirty = true;

            return Selected;
        }

        /// <summary>
        /// Ajoute une entite au point demande et la selectionne.
        ///
        /// La position est accrochee comme le reste : une apparition posee entre
        /// deux lignes de grille est un piege pour l'alignement du decor autour.
        /// </summary>
        public int AddEntity(string type, Vector3 position, float yaw = 0f, float pitch = 0f)
        {
            Snapshot();

            Map.Entities.Add(new MapEntity(type, Snap(position), yaw, pitch));

            Selected = Map.Entities.Count - 1;
            Target = EditTarget.Entity;
            Dirty = true;

            return Selected;
        }

        // ===================================================================
        //  Ecriture DIRECTE d'une valeur
        //
        //  Move et Resize avancent d'un nombre entier de pas de grille : c'est
        //  ce qu'il faut au clavier, et c'est ce qui garde le decor aligne.
        //  Mais un panneau de proprietes fait l'inverse - on tape 2,75 et on
        //  attend 2,75. Les deux chemins coexistent donc, et les deux passent
        //  par Snapshot : une valeur tapee s'annule comme le reste.
        // ===================================================================

        /// <summary>
        /// Remplace entierement la boite selectionnee.
        ///
        /// ⚠️ La taille est BORNEE ici aussi. Le panneau accepte n'importe
        /// quel nombre, y compris 0 ou -3, et une AABB retournee n'est jamais
        /// croisee par Intersects : le mur deviendrait traversable sans que
        /// rien ne le dise. Meme raison que le bornage de Resize.
        /// </summary>
        public bool SetBox(Vector3 center, Vector3 size, Vector3 color, MapShape shape)
        {
            if (!HasSelection)
                return false;

            Snapshot();

            size = new Vector3(MathF.Max(MinimumSize, size.X),
                               MathF.Max(MinimumSize, size.Y),
                               MathF.Max(MinimumSize, size.Z));

            Map.Boxes[Selected] = new MapBox(center, size, color, shape);
            Dirty = true;

            return true;
        }

        /// <summary>Repositionne et reoriente l'entite selectionnee.</summary>
        public bool SetEntityTransform(Vector3 position, float yaw, float pitch)
        {
            if (!HasEntity)
                return false;

            Snapshot();

            MapEntity entity = Map.Entities[Selected];
            entity.Position = position;
            entity.Yaw = yaw;
            entity.Pitch = pitch;

            Dirty = true;
            return true;
        }

        /// <summary>
        /// Change le TYPE de l'entite selectionnee, proprietes conservees.
        ///
        /// Les proprietes ne sont pas effacees a dessein : passer un
        /// trigger_volume en trigger_checkpoint garde « size » et « once »,
        /// qui veulent dire la meme chose des deux cotes. Celles qui ne
        /// servent plus sont inertes, et le format les conserve - c'est deja
        /// sa regle pour les types inconnus.
        /// </summary>
        public bool SetEntityType(string type)
        {
            if (!HasEntity || string.IsNullOrWhiteSpace(type))
                return false;

            MapEntity entity = Map.Entities[Selected];

            if (string.Equals(entity.Type, type, StringComparison.Ordinal))
                return false;

            Snapshot();

            entity.Type = type;
            Dirty = true;

            return true;
        }

        /// <summary>
        /// Retire une propriete de l'entite selectionnee.
        ///
        /// C'est ce qui rend une propriete effacable depuis le panneau. Sans
        /// ca, vider un champ ecrirait la chaine vide - donc « _ » dans le
        /// fichier, donc une propriete presente et vide, qui n'est PAS la
        /// meme chose qu'une propriete absente : l'absente prend le defaut du
        /// moteur, la vide impose "".
        /// </summary>
        public bool RemoveProperty(string key)
        {
            if (!HasEntity || string.IsNullOrWhiteSpace(key))
                return false;

            MapEntity entity = Map.Entities[Selected];

            if (!entity.Properties.ContainsKey(key))
                return false;

            Snapshot();

            entity.Properties.Remove(key);
            Dirty = true;

            return true;
        }

        /// <summary>Ecrit une propriete sur l'entite selectionnee.</summary>
        public bool SetProperty(string key, string value)
        {
            if (!HasEntity)
                return false;

            Snapshot();

            Map.Entities[Selected].Set(key, value);
            Dirty = true;

            return true;
        }

        public bool Delete()
        {
            if (HasEntity)
            {
                Snapshot();

                Map.Entities.RemoveAt(Selected);

                Selected = Math.Min(Selected, Map.Entities.Count - 1);
                if (Selected < 0) Target = EditTarget.None;

                Dirty = true;
                return true;
            }

            if (!HasSelection)
                return false;

            Snapshot();

            Map.Boxes.RemoveAt(Selected);

            // On garde la selection sur le VOISIN plutot que de tout
            // deselectionner : supprimer une serie de boites est l'usage le
            // plus courant, et repointer a chaque fois serait penible.
            Selected = Math.Min(Selected, Map.Boxes.Count - 1);
            Dirty = true;

            return true;
        }

        /// <summary>Duplique la selection, decalee sur l'axe actif.</summary>
        public int Duplicate()
        {
            if (HasEntity)
            {
                Snapshot();

                MapEntity copy = Map.Entities[Selected].Clone();
                copy.Position = Add(copy.Position, Axis, GridSize * 2f);

                Map.Entities.Add(copy);

                Selected = Map.Entities.Count - 1;
                Dirty = true;

                return Selected;
            }

            if (!HasSelection)
                return -1;

            Snapshot();

            MapBox box = Map.Boxes[Selected];

            // Le decalage vaut la TAILLE de la boite, pas un pas de grille :
            // dupliquer un mur doit le poser a cote du precedent, pas dedans.
            float offset = Component(box.Size, Axis);

            Map.Boxes.Add(new MapBox(Add(box.Center, Axis, offset), box.Size, box.Color, box.Shape));

            Selected = Map.Boxes.Count - 1;
            Dirty = true;

            return Selected;
        }

        public bool Paint(Vector3 color)
        {
            if (!HasSelection)
                return false;

            Snapshot();

            MapBox box = Map.Boxes[Selected];
            Map.Boxes[Selected] = new MapBox(box.Center, box.Size, color, box.Shape);
            Dirty = true;

            return true;
        }

        /// <summary>
        /// Change la forme de la boite selectionnee, sans toucher a son volume.
        ///
        /// Le centre et la taille sont conserves a dessein : c'est ce qui
        /// permet d'essayer les cinq formes sur un meme emplacement et de
        /// garder celle qui va, sans avoir a realigner quoi que ce soit.
        /// </summary>
        public bool SetShape(MapShape shape)
        {
            if (!HasSelection)
                return false;

            MapBox box = Map.Boxes[Selected];

            if (box.Shape == shape)
                return false;

            Snapshot();

            Map.Boxes[Selected] = new MapBox(box.Center, box.Size, box.Color, shape);
            Dirty = true;

            return true;
        }

        /// <summary>Passe a la forme suivante, pour un raccourci clavier unique.</summary>
        public MapShape CycleNewShape(int direction)
        {
            int index = Array.IndexOf(MapShapes.All, NewShape);
            if (index < 0) index = 0;

            index = (index + Math.Sign(direction) + MapShapes.All.Length) % MapShapes.All.Length;

            NewShape = MapShapes.All[index];
            return NewShape;
        }

        /// <summary>Le point de depart du joueur se pose comme le reste.</summary>
        public void SetSpawn(Vector3 feet, float yaw, float pitch)
        {
            Snapshot();

            Map.Spawn = Snap(feet);
            Map.SpawnYaw = yaw;
            Map.SpawnPitch = pitch;
            Dirty = true;
        }

        /// <summary>A appeler apres un enregistrement reussi.</summary>
        public void MarkSaved() => Dirty = false;

        /// <summary>Repart d'une autre carte. L'historique est vide.</summary>
        public void Reset(MapData map)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));

            _undo.Clear();
            _redo.Clear();

            _strokeOpen = false;
            Selected = -1;
            Dirty = false;
        }

        // ===================================================================

        private static float Component(Vector3 v, EditAxis axis)
            => axis switch { EditAxis.X => v.X, EditAxis.Y => v.Y, _ => v.Z };

        private static Vector3 Add(Vector3 v, EditAxis axis, float amount)
            => axis switch
            {
                EditAxis.X => new Vector3(v.X + amount, v.Y, v.Z),
                EditAxis.Y => new Vector3(v.X, v.Y + amount, v.Z),
                _ => new Vector3(v.X, v.Y, v.Z + amount),
            };

        private static Vector3 Set(Vector3 v, EditAxis axis, float value)
            => axis switch
            {
                EditAxis.X => new Vector3(value, v.Y, v.Z),
                EditAxis.Y => new Vector3(v.X, value, v.Z),
                _ => new Vector3(v.X, v.Y, value),
            };
    }
}
