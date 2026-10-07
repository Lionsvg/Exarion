// ===========================================================================
//  Exarion - Panneau de proprietes de l'editeur (facon Roblox Studio)
//
//  Jusqu'ici, regler un objet demandait la console : « ed_prop speed 3 ». Ca
//  marche, c'est meme ce qui a permis de tout verifier en headless, mais ca
//  suppose de connaitre par coeur le nom des cles - et rien a l'ecran ne les
//  donne. Poser un PNJ ne montrait aucune de ses neuf proprietes.
//
//  Ce panneau est la liste des champs de la selection, navigable au clavier.
//
//  ---------------------------------------------------------------------
//  POURQUOI IL VIT DANS Core, ET PAS DANS LE JEU
//
//  Regle n°1. Un panneau d'edition, c'est une machine a etats : focalise ou
//  non, en saisie ou non, avec un tampon qu'on valide ou qu'on abandonne.
//  C'est exactement le genre de code ou l'on casse quelque chose sans le
//  voir - taper puis annuler puis changer de selection doit rendre la carte
//  intacte. Ici, ca se teste en ligne de commande, sans ouvrir de fenetre.
//
//  Le jeu ne fait plus que deux choses : traduire les touches en appels, et
//  dessiner Fields. Aucune regle d'edition de son cote.
//  ---------------------------------------------------------------------
//  ⚠️ LA VALEUR AFFICHEE ET LA VALEUR EDITEE NE SONT PAS LA MEME CHAINE.
//
//  L'affichage arrondit a quatre decimales, sinon une marche calculee a
//  2,2500002 remplit sa colonne de chiffres illisibles. Mais le tampon de
//  saisie part de la valeur COMPLETE : sinon, ouvrir un champ et le valider
//  sans rien changer DEPLACERAIT le mur de 2e-7. Repete a chaque passage,
//  c'est exactement la derive cumulative que le format .map s'interdit en
//  ecrivant ses flottants sans perte.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Exarion.Core.Assets;

namespace Exarion.Core.Editor
{
    public enum FieldKind
    {
        /// <summary>Titre de groupe. Ne se selectionne pas, ne s'edite pas.</summary>
        Header,

        /// <summary>Chaine libre : un nom, une cible, un axe.</summary>
        Text,

        /// <summary>Nombre. La virgule est acceptee a la saisie, jamais a l'ecriture.</summary>
        Number,

        /// <summary>Une des cinq formes. Se fait defiler plutot que se taper.</summary>
        Shape,
    }

    /// <summary>Une ligne du panneau.</summary>
    public readonly struct PropertyField
    {
        public readonly string Id;
        public readonly string Label;

        /// <summary>Ce qui s'affiche : arrondi, et jamais vide (le defaut sinon).</summary>
        public readonly string Display;

        /// <summary>Ce qui part dans le tampon de saisie : exact, et vide si la propriete est absente.</summary>
        public readonly string Raw;

        public readonly FieldKind Kind;

        /// <summary>
        /// Vrai quand la propriete est ABSENTE et qu'on montre le defaut du
        /// moteur. Le rendu la grise, comme Roblox Studio grise ce qui n'est
        /// pas surcharge : sans cette distinction, on croit avoir regle une
        /// valeur qu'on n'a jamais ecrite.
        /// </summary>
        public readonly bool IsDefault;

        public readonly string Hint;

        public PropertyField(string id, string label, string display, string raw,
                             FieldKind kind, bool isDefault = false, string hint = "")
        {
            Id = id;
            Label = label;
            Display = display;
            Raw = raw;
            Kind = kind;
            IsDefault = isDefault;
            Hint = hint;
        }

        public bool Selectable => Kind != FieldKind.Header;
    }

    public sealed class PropertyPanel
    {
        private readonly EditorState _editor;
        private readonly List<PropertyField> _fields = new();

        /// <summary>Identifiant du champ en cours de saisie. Sert a survivre a un Refresh.</summary>
        private string _editedId = "";

        /// <summary>
        /// Vrai tant que la valeur ouverte est « toute selectionnee ».
        ///
        /// ⚠️⚠️ SANS CE DRAPEAU, LE PANNEAU EST INUTILISABLE, et le defaut est
        /// exactement du genre qu'on ne voit pas en relisant le code. Le tampon
        /// doit partir de la valeur EXACTE - c'est ce qui empeche la derive de
        /// 2e-7 a chaque ouverture-validation. Mais s'il part de « 2 » et que
        /// la frappe AJOUTE, taper « 7.5 » sur un champ valant 2 donne 27,5.
        ///
        /// Le compromis est celui d'Excel et de Roblox Studio : la valeur est
        /// affichee entiere et selectionnee, la premiere frappe la REMPLACE, et
        /// une correction au retour arriere passe en edition normale. On a donc
        /// les deux : pas de derive, et pas de valeur a retaper.
        /// </summary>
        private bool _replaceOnType;

        public PropertyPanel(EditorState editor)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            Refresh();
        }

        public IReadOnlyList<PropertyField> Fields => _fields;

        /// <summary>Vrai quand le clavier pilote le panneau et non la camera.</summary>
        public bool Focused { get; private set; }

        /// <summary>Ligne courante. Toujours sur une ligne selectionnable, ou -1.</summary>
        public int Index { get; private set; } = -1;

        public bool Editing { get; private set; }

        /// <summary>Texte en cours de frappe. N'a de sens que si Editing.</summary>
        public string Buffer { get; private set; } = "";

        /// <summary>Dernier refus de saisie, a montrer a l'auteur. Vide sinon.</summary>
        public string Message { get; private set; } = "";

        public PropertyField? Current
            => Index >= 0 && Index < _fields.Count ? _fields[Index] : null;

        // ===================================================================
        //  Construction de la liste
        // ===================================================================

        /// <summary>
        /// Reconstruit les champs depuis la selection courante.
        ///
        /// Appele a chaque image : la carte peut changer sous le panneau (une
        /// annulation, un deplacement au clavier), et un panneau qui garderait
        /// ses valeurs afficherait un etat qui n'existe plus.
        ///
        /// ⚠️ LA SAISIE EN COURS SURVIT, mais SEULEMENT si son champ existe
        /// encore. Reconstruire en effacant le tampon rendrait le panneau
        /// inutilisable - il se reconstruit soixante fois par seconde, donc on
        /// perdrait chaque caractere. Et le garder alors que le champ a disparu
        /// ecrirait la valeur tapee dans un champ qui n'a rien a voir.
        /// </summary>
        public void Refresh()
        {
            string previousId = Current?.Id ?? "";

            _fields.Clear();

            if (_editor.HasSelection)
                BuildBox();
            else if (_editor.SelectedEntity is MapEntity entity)
                BuildEntity(entity);
            else
                _fields.Add(new PropertyField("", "AUCUNE SELECTION", "", "", FieldKind.Header));

            // On retrouve la MEME ligne apres reconstruction, par son
            // identifiant. Se fier a l'indice ferait sauter le curseur des
            // qu'une propriete apparait au-dessus de lui.
            int restored = IndexOf(previousId);
            Index = restored >= 0 ? restored : FirstSelectable();

            if (Editing && IndexOf(_editedId) < 0)
                CancelEdit();
        }

        private void BuildBox()
        {
            MapBox box = _editor.Map.Boxes[_editor.Selected];

            _fields.Add(Header($"BOITE {_editor.Selected}"));

            _fields.Add(new PropertyField("shape", "Forme", MapShapes.Name(box.Shape),
                MapShapes.Name(box.Shape), FieldKind.Shape,
                hint: "gauche/droite pour changer - la COLLISION reste la boite englobante"));

            _fields.Add(Header("POSITION"));
            _fields.Add(Number("cx", "X", box.Center.X));
            _fields.Add(Number("cy", "Y", box.Center.Y));
            _fields.Add(Number("cz", "Z", box.Center.Z));

            _fields.Add(Header("TAILLE"));
            _fields.Add(Number("sx", "X", box.Size.X, "minimum " + Fmt(EditorState.MinimumSize)));
            _fields.Add(Number("sy", "Y", box.Size.Y, "minimum " + Fmt(EditorState.MinimumSize)));
            _fields.Add(Number("sz", "Z", box.Size.Z, "minimum " + Fmt(EditorState.MinimumSize)));

            _fields.Add(Header("COULEUR"));
            _fields.Add(Number("cr", "R", box.Color.X, "LINEAIRE, pas couleur d'ecran"));
            _fields.Add(Number("cg", "V", box.Color.Y, "LINEAIRE, pas couleur d'ecran"));
            _fields.Add(Number("cb", "B", box.Color.Z, "LINEAIRE, pas couleur d'ecran"));
        }

        private void BuildEntity(MapEntity entity)
        {
            _fields.Add(Header($"ENTITE {_editor.Selected}"));

            _fields.Add(new PropertyField("type", "Type", entity.Type, entity.Type, FieldKind.Text,
                hint: EntitySchema.Knows(entity.Type)
                    ? "type connu du moteur"
                    : "TYPE INCONNU - conserve dans le fichier, mais sans effet"));

            _fields.Add(Header("POSITION"));
            _fields.Add(Number("px", "X", entity.Position.X));
            _fields.Add(Number("py", "Y", entity.Position.Y));
            _fields.Add(Number("pz", "Z", entity.Position.Z));

            _fields.Add(Header("ORIENTATION"));
            _fields.Add(Number("yaw", "Lacet", entity.Yaw, "degres"));
            _fields.Add(Number("pitch", "Tangage", entity.Pitch, "degres"));

            _fields.Add(Header("PROPRIETES"));

            var shown = new HashSet<string>(StringComparer.Ordinal);

            // D'abord les proprietes que le moteur LIT, presentes ou non :
            // c'est tout l'interet du catalogue. Un PNJ fraichement pose
            // affiche ses neuf reglages avec leurs defauts.
            foreach (PropertySpec spec in EntitySchema.For(entity.Type))
            {
                shown.Add(spec.Key);

                bool present = entity.Properties.TryGetValue(spec.Key, out string? value);

                _fields.Add(new PropertyField(
                    "p:" + spec.Key, spec.Key,
                    present ? value! : spec.Default,
                    present ? value! : "",
                    FieldKind.Text,
                    isDefault: !present,
                    hint: spec.Hint));
            }

            // Puis celles que la carte porte et que le moteur ne connait pas.
            // Les cacher ferait disparaitre du panneau une propriete qui est
            // bel et bien dans le fichier - et on la reecrirait en double.
            foreach (KeyValuePair<string, string> pair in entity.Properties)
            {
                if (shown.Contains(pair.Key))
                    continue;

                _fields.Add(new PropertyField("p:" + pair.Key, pair.Key, pair.Value, pair.Value,
                    FieldKind.Text, hint: "propriete hors catalogue - le moteur ne la lit pas"));
            }
        }

        private static PropertyField Header(string label)
            => new PropertyField("", label, "", "", FieldKind.Header);

        private static PropertyField Number(string id, string label, float value, string hint = "")
            => new PropertyField(id, label, Fmt(value),
                                 value.ToString(CultureInfo.InvariantCulture), FieldKind.Number,
                                 hint: hint);

        /// <summary>Affichage court. La saisie, elle, part de la valeur exacte.</summary>
        private static string Fmt(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        private int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id))
                return -1;

            for (int i = 0; i < _fields.Count; i++)
                if (string.Equals(_fields[i].Id, id, StringComparison.Ordinal))
                    return i;

            return -1;
        }

        private int FirstSelectable()
        {
            for (int i = 0; i < _fields.Count; i++)
                if (_fields[i].Selectable)
                    return i;

            return -1;
        }

        // ===================================================================
        //  Navigation
        // ===================================================================

        /// <summary>
        /// Prend ou rend le clavier. Rend le nouvel etat.
        ///
        /// Le panneau doit se DEFOCALISER, et pas seulement se fermer : tant
        /// qu'il a le clavier, ZQSD tape des lettres au lieu de faire voler la
        /// camera. Sans bascule explicite, on se retrouve enferme dans le
        /// panneau sans savoir pourquoi la camera ne repond plus.
        /// </summary>
        public bool ToggleFocus()
        {
            Focused = !Focused;

            if (!Focused)
                CancelEdit();
            else if (Index < 0)
                Index = FirstSelectable();

            Message = "";
            return Focused;
        }

        public void Unfocus()
        {
            Focused = false;
            CancelEdit();
        }

        /// <summary>Descend (+1) ou remonte (-1), en sautant les titres de groupe.</summary>
        public void MoveCursor(int delta)
        {
            if (Editing || _fields.Count == 0 || delta == 0)
                return;

            int step = Math.Sign(delta);
            int i = Index;

            // Au plus un tour complet : sans cette borne, une liste faite
            // uniquement de titres bouclerait indefiniment.
            for (int guard = 0; guard < _fields.Count; guard++)
            {
                i += step;

                if (i < 0) i = _fields.Count - 1;
                else if (i >= _fields.Count) i = 0;

                if (_fields[i].Selectable)
                {
                    Index = i;
                    Message = "";
                    return;
                }
            }
        }

        // ===================================================================
        //  Saisie
        // ===================================================================

        public bool BeginEdit()
        {
            if (Current is not PropertyField field || !field.Selectable)
                return false;

            // Une forme ne se tape pas : cinq valeurs possibles, on les fait
            // defiler. Ouvrir un tampon de texte dessus obligerait a ecrire
            // « cylindre » sans faute pour changer de forme.
            if (field.Kind == FieldKind.Shape)
                return false;

            Editing = true;
            _editedId = field.Id;
            Buffer = field.Raw;
            Message = "";

            // La valeur s'ouvre « toute selectionnee » : voir _replaceOnType.
            _replaceOnType = true;

            return true;
        }

        public void Type(char c)
        {
            // Les caracteres de controle n'ont rien a faire dans une valeur, et
            // le decoupeur du format .map coupe sur les espaces : les laisser
            // entrer produirait un fichier qu'on ne saurait pas relire.
            if (!Editing || char.IsControl(c) || char.IsWhiteSpace(c))
                return;

            if (_replaceOnType)
            {
                Buffer = "";
                _replaceOnType = false;
            }

            // Une valeur de propriete est un nom de cible ou un nombre. La
            // borne evite qu'une touche restee enfoncee ne fabrique une ligne
            // de carte demesuree.
            if (Buffer.Length < 64)
                Buffer += c;
        }

        public void Backspace()
        {
            if (!Editing)
                return;

            // Retour arriere sur une valeur entierement selectionnee : elle
            // s'efface d'un coup, comme partout ailleurs. C'est aussi ce qui
            // permet de VIDER une propriete pour la rendre a son defaut, en
            // une frappe au lieu de dix.
            if (_replaceOnType)
            {
                Buffer = "";
                _replaceOnType = false;
                return;
            }

            if (Buffer.Length > 0)
                Buffer = Buffer[..^1];
        }

        public void CancelEdit()
        {
            Editing = false;
            Buffer = "";
            _editedId = "";
            _replaceOnType = false;
        }

        /// <summary>
        /// Valide la saisie. Rend faux si la valeur est refusee - le tampon
        /// reste alors ouvert, avec le message d'erreur.
        ///
        /// ⚠️ Refuser sans FERMER est deliberé. Vider le champ sur une faute
        /// de frappe ferait perdre les vingt caracteres d'un nom de cible pour
        /// une virgule mal placee.
        /// </summary>
        public bool CommitEdit()
        {
            if (!Editing || Current is not PropertyField field)
                return false;

            if (!Apply(field, Buffer, out string error))
            {
                Message = error;
                return false;
            }

            CancelEdit();
            Refresh();

            return true;
        }

        /// <summary>
        /// Fait defiler une valeur a choix ferme (aujourd'hui : la forme).
        ///
        /// Rendue publique parce que c'est le seul moyen de changer de forme :
        /// BeginEdit refuse ce champ.
        /// </summary>
        public bool CycleValue(int direction)
        {
            if (Editing || Current is not PropertyField field || field.Kind != FieldKind.Shape)
                return false;

            if (!_editor.HasSelection)
                return false;

            MapBox box = _editor.Map.Boxes[_editor.Selected];

            int index = Array.IndexOf(MapShapes.All, box.Shape);
            if (index < 0) index = 0;

            index = (index + Math.Sign(direction) + MapShapes.All.Length) % MapShapes.All.Length;

            bool changed = _editor.SetShape(MapShapes.All[index]);
            Refresh();

            return changed;
        }

        // ===================================================================
        //  Application
        // ===================================================================

        private bool Apply(in PropertyField field, string text, out string error)
        {
            error = "";

            if (field.Id.StartsWith("p:", StringComparison.Ordinal))
                return ApplyProperty(field.Id[2..], text, out error);

            if (_editor.HasSelection)
                return ApplyBox(field, text, out error);

            if (_editor.HasEntity)
                return ApplyEntity(field, text, out error);

            error = "rien de selectionne";
            return false;
        }

        private bool ApplyProperty(string key, string text, out string error)
        {
            error = "";

            if (!_editor.HasEntity)
            {
                error = "aucune entite selectionnee";
                return false;
            }

            // Un champ vide REND la propriete a son defaut moteur au lieu
            // d'ecrire "". Voir EditorState.RemoveProperty : les deux ne sont
            // pas la meme chose, et c'est le vide qu'on tape pour dire
            // « laisse le moteur decider ».
            if (text.Length == 0)
            {
                _editor.RemoveProperty(key);
                return true;
            }

            _editor.SetProperty(key, text);
            return true;
        }

        private bool ApplyBox(in PropertyField field, string text, out string error)
        {
            error = "";

            if (field.Kind != FieldKind.Number)
            {
                error = "champ non editable";
                return false;
            }

            if (!TryReadNumber(text, out float value))
            {
                error = $"nombre illisible \"{text}\"";
                return false;
            }

            MapBox box = _editor.Map.Boxes[_editor.Selected];

            Vector3 center = box.Center, size = box.Size, color = box.Color;

            switch (field.Id)
            {
                case "cx": center.X = value; break;
                case "cy": center.Y = value; break;
                case "cz": center.Z = value; break;
                case "sx": size.X = value; break;
                case "sy": size.Y = value; break;
                case "sz": size.Z = value; break;
                case "cr": color.X = value; break;
                case "cg": color.Y = value; break;
                case "cb": color.Z = value; break;
                default: error = "champ inconnu"; return false;
            }

            _editor.SetBox(center, size, color, box.Shape);
            return true;
        }

        private bool ApplyEntity(in PropertyField field, string text, out string error)
        {
            error = "";

            MapEntity entity = _editor.Map.Entities[_editor.Selected];

            if (field.Id == "type")
            {
                if (text.Length == 0)
                {
                    error = "un type ne peut pas etre vide";
                    return false;
                }

                _editor.SetEntityType(text);
                return true;
            }

            if (!TryReadNumber(text, out float value))
            {
                error = $"nombre illisible \"{text}\"";
                return false;
            }

            Vector3 position = entity.Position;
            float yaw = entity.Yaw, pitch = entity.Pitch;

            switch (field.Id)
            {
                case "px": position.X = value; break;
                case "py": position.Y = value; break;
                case "pz": position.Z = value; break;
                case "yaw": yaw = value; break;
                case "pitch": pitch = value; break;
                default: error = "champ inconnu"; return false;
            }

            _editor.SetEntityTransform(position, yaw, pitch);
            return true;
        }

        /// <summary>
        /// Lit un nombre tape a la main.
        ///
        /// ⚠️ LA VIRGULE EST ACCEPTEE A LA SAISIE, ET SEULEMENT LA. Ce clavier
        /// est francais : « 2,5 » est ce qu'on tape spontanement, et le refuser
        /// serait hostile. Mais on relit en culture INVARIANTE apres avoir
        /// remplace la virgule - jamais en culture courante - parce que la
        /// valeur part ensuite dans un .map, ou 2,5 se relirait comme 25 sur
        /// un poste anglais. C'est le meme piege qu'en 2.4, pris du bon cote.
        /// </summary>
        public static bool TryReadNumber(string? text, out float value)
        {
            value = 0f;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = text.Replace(',', '.');

            if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;

            // NaN et infini passent float.TryParse ("NaN", "Infinity") et ne
            // cassent qu'au rendu, sous forme de geometrie disparue sans
            // message. Meme refus que dans MapFile.
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                value = 0f;
                return false;
            }

            return true;
        }
    }
}
