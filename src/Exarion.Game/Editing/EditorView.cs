// ===========================================================================
//  Ce que l'editeur dessine : aides dans la scene, surcouche, panneau
//
//  ⚠️ AUCUNE REGLE D'EDITION ICI. On LIT EditorState et PropertyPanel et on
//  dessine des rectangles, rien d'autre. Tout ce qui decide - quel champ
//  existe, ce qu'une saisie vaut, si elle est acceptee - vit dans Core et s'y
//  teste sans ouvrir de fenetre (regle n°1).
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Exarion.Core.Assets;
using Exarion.Core.Editor;
using Exarion.Core.Entities;
using Exarion.Engine.Graphics;
using Exarion.Game.Rendering;
using Silk.NET.OpenGL;

namespace Exarion.Game.Editing
{
    internal static class EditorView
    {
        // ===================================================================
        //  Dans la scene : grille, surlignage de la selection, marqueurs
        // ===================================================================

        public static void DrawScene(GL gl, SceneRenderer scene, EditorController editor, MarkerRenderer markers)
        {
            EditorState? state = editor.State;

            if (state is null)
                return;

            if (editor.Grid.Visible)
            {
                editor.Grid.Ensure(gl, state.GridSize);

                if (editor.Grid.Mesh != null)
                    scene.Draw(editor.Grid.Mesh, Matrix4x4.Identity, Vector3.One);
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
            if (state.HasSelection)
            {
                MapBox box = state.Map.Boxes[state.Selected];

                markers.DrawShape(scene, box.Shape, box.Center, box.Size + new Vector3(0.02f), 0f,
                                  new Vector3(1.6f, 1.1f, 0.25f));
            }

            // Marqueurs d'entites : une entite n'a AUCUNE geometrie, sans
            // marqueur elle est invisible et impossible a cliquer.
            var entities = state.Map.Entities;

            for (int i = 0; i < entities.Count; i++)
                markers.DrawEntityMarker(scene, entities[i], state.HasEntity && state.Selected == i);

            DrawWiring(scene, markers, entities);
        }

        private static readonly Vector3 WireTint = new(1.6f, 1.4f, 0.3f);
        private static readonly Vector3 BrokenWireTint = new(1.8f, 0.35f, 0.3f);

        /// <summary>Les proprietes qui visent une cible par son nom.</summary>
        private static readonly string[] WireKeys = { "target", "target_open", "target_close" };

        /// <summary>
        /// Les FILS du cablage : une latte jaune de chaque entite vers la cible
        /// qu'elle vise, une bille au bout qui recoit. Une latte ROUGE dressee
        /// au-dessus d'une entite dit « je vise un nom que personne ne porte ».
        ///
        /// ⚠️ C'est la faute de carte la plus courante - porte_hal au lieu de
        /// porte_hall - et jusqu'ici elle ne se voyait qu'en JOUANT, dans le
        /// fil d'evenements (CibleAbsente). Le cablage d'un niveau etait
        /// invisible dans l'editeur : on ne pouvait que relire les noms.
        ///
        /// La cible est resolue comme le moteur la resout : seules les entites
        /// qui RECOIVENT (portes, logique, lumieres, sons, PNJ nommes, dans cet
        /// ordre d'inscription), et la premiere qui porte le nom gagne. Viser un
        /// volume par son nom ne mene nulle part en jeu - le fil est donc rouge.
        /// </summary>
        private static void DrawWiring(SceneRenderer scene, MarkerRenderer markers, IReadOnlyList<MapEntity> entities)
        {
            var lift = new Vector3(0f, 0.5f, 0f);

            foreach (MapEntity source in entities)
            {
                foreach (string key in WireKeys)
                {
                    string target = source.Get(key);

                    if (target.Length == 0)
                        continue;

                    MapEntity? receiver = FindReceiver(entities, target);

                    if (receiver is null)
                    {
                        markers.DrawSegment(scene, source.Position + lift,
                                            source.Position + lift + new Vector3(0f, 1.2f, 0f), 0.05f, BrokenWireTint);
                        continue;
                    }

                    markers.DrawSegment(scene, source.Position + lift, receiver.Position + lift, 0.04f, WireTint);
                    markers.DrawShape(scene, MapShape.Sphere, receiver.Position + lift, new Vector3(0.16f), 0f, WireTint);
                }
            }
        }

        /// <summary>
        /// MEME ORDRE que l'inscription en jeu (GameWorld) : portes, logique,
        /// lumieres, sons, PNJ. A doublon de nom, le premier inscrit gagne - le
        /// fil doit montrer celui-la, pas un autre.
        /// </summary>
        private static readonly Func<string, bool>[] ReceiverOrder =
        {
            t => t == EntityTypes.Door,
            LogicWorld.IsLogic,
            t => t == EntityTypes.EnvLight,
            t => t == EntityTypes.EnvSound,
            t => t == EntityTypes.Npc,
        };

        private static MapEntity? FindReceiver(IReadOnlyList<MapEntity> entities, string name)
        {
            foreach (Func<string, bool> kind in ReceiverOrder)
                foreach (MapEntity e in entities)
                    if (kind(e.Type) && e.Get("name") == name)
                        return e;

            return null;
        }

        // ===================================================================
        //  Surcouche : barre d'etat, selection, aide-memoire
        // ===================================================================

        public static void DrawHud(HudRenderer hud, EditorController editor, int w, int h)
        {
            EditorState? state = editor.State;

            if (state is null) return;

            var white = new Vector4(0.92f, 0.95f, 1f, 1f);
            var dim = new Vector4(0.58f, 0.64f, 0.72f, 1f);
            var warn = new Vector4(1f, 0.72f, 0.35f, 1f);

            hud.Rect(0f, 0f, w, 30f, new Vector4(0.05f, 0.07f, 0.1f, 0.78f));

            string axis = state.Axis switch { EditAxis.X => "X", EditAxis.Y => "Y", _ => "Z" };

            hud.Text(14f, 8f, 1.9f,
                $"EDITEUR   AXE {axis}   GRILLE {state.GridSize:0.###} M   " +
                $"FORME {MapShapes.Name(state.NewShape).ToUpperInvariant()}   " +
                $"{state.Map.Boxes.Count} OBJETS   ANNULER {state.UndoDepth}/{state.RedoDepth}",
                white);

            if (state.Dirty)
                hud.Text(w - 150f, 8f, 1.9f, "NON ENREGISTRE", warn);

            // Croix de visee : elle sert a designer une boite, elle doit donc
            // rester visible dans l'editeur.
            hud.Crosshair(5f, 8f, 2f, new Vector4(0.3f, 1f, 0.5f, 0.9f));

            if (state.HasSelection)
            {
                MapBox box = state.Map.Boxes[state.Selected];

                hud.TextBoxed(14f, h - 196f, 1.8f,
                    $"BOITE {state.Selected}   CENTRE {box.Center.X:0.##} {box.Center.Y:0.##} {box.Center.Z:0.##}",
                    white);
                hud.TextBoxed(14f, h - 170f, 1.8f,
                    $"TAILLE {box.Size.X:0.##} X {box.Size.Y:0.##} X {box.Size.Z:0.##}", dim);
            }
            else if (state.SelectedEntity is MapEntity picked)
            {
                hud.TextBoxed(14f, h - 196f, 1.8f,
                    $"ENTITE {state.Selected}   {picked.Type.ToUpperInvariant()}   " +
                    $"{picked.Position.X:0.##} {picked.Position.Y:0.##} {picked.Position.Z:0.##}   " +
                    $"LACET {picked.Yaw:0}",
                    white);

                // Les proprietes sont ce qui distingue une entite d'un simple
                // point : les cacher rendrait l'edition aveugle.
                var props = new StringBuilder();

                foreach (var pair in picked.Properties)
                    props.Append(pair.Key).Append('=').Append(pair.Value).Append("   ");

                hud.TextBoxed(14f, h - 170f, 1.8f,
                    props.Length == 0 ? "AUCUNE PROPRIETE — ed_prop cle valeur" : props.ToString().ToUpperInvariant(),
                    dim);
            }
            else
            {
                hud.TextBoxed(14f, h - 170f, 1.8f,
                    "RIEN DE SELECTIONNE — CLIC GAUCHE SOUS LE CURSEUR POUR DESIGNER", dim);
            }

            // ⚠️⚠️ CHAQUE LIGNE DOIT TENIR A GAUCHE DU PANNEAU DE PROPRIETES (330 px),
            // qui couvre toute la hauteur a droite. Jusqu'au 08/10 les deux
            // premieres lignes faisaient 1 200 px : « TAB PANNEAU », « CTRL+Z/Y »,
            // « CTRL+S » et « P DEPART » passaient DESSOUS, illisibles - constate
            // sur une capture. A l'echelle 1,7, un caractere fait 10,2 px : 90
            // caracteres au plus.
            //
            // ⚠️ Le clic droit est annonce EN PREMIER et en clair. C'est la
            // commande la moins devinable de l'editeur : sans elle on croit que
            // la camera ne tourne pas, et rien a l'ecran ne le dirait.
            hud.TextBoxed(14f, h - 140f, 1.7f,
                "CLIC DROIT REGARDER  CLIC GAUCHE SELECTIONNER  ZQSD VOLER  ESPACE/X MONTER-DESCENDRE",
                dim);
            hud.TextBoxed(14f, h - 114f, 1.7f,
                "MAJ VITE  1/2/3 AXE  FLECHES DEPLACER/TAILLE  ENTREE CREER  B FORME  TAB PANNEAU",
                dim);
            hud.TextBoxed(14f, h - 88f, 1.7f,
                "SUPPR EFFACER  C DUPLIQUER  G GRILLE  H TRAME  CTRL+Z/Y  CTRL+S  P DEPART",
                dim);
            hud.TextBoxed(14f, h - 62f, 1.7f,
                "F2 DEPART  F3 CHECKPOINT  F4 VOLUME  F5 PNJ  F6 PASSAGE  F7 PORTE  F8 CAISSES  F9 MANNEQUIN",
                new Vector4(0.45f, 0.85f, 0.6f, 1f));
            hud.TextBoxed(14f, h - 36f, 1.7f,
                "F10 RELAIS  F11 MINUTERIE  F12 COMPTEUR   LATTES JAUNES CABLAGE, ROUGES CIBLE ABSENTE",
                new Vector4(0.45f, 0.85f, 0.6f, 1f));

            if (editor.Panel != null)
                DrawPropertyPanel(hud, editor.Panel, w, h);
        }

        // ===================================================================
        //  Panneau de proprietes, a droite
        // ===================================================================

        private const float PanelWidth = 330f;
        private const float PanelRowHeight = 21f;
        private const float PanelTextScale = 1.7f;

        private static void DrawPropertyPanel(HudRenderer hud, PropertyPanel panel, int w, int h)
        {
            float x = w - PanelWidth;
            float top = 30f;

            // La zone d'aide du bas est reservee AVANT de calculer combien de
            // lignes tiennent : la calculer apres ferait passer la derniere
            // ligne sous le texte d'aide, donc illisible.
            float footer = 66f;
            float listTop = top + 30f;
            float listBottom = h - footer;

            int visible = Math.Max(1, (int)((listBottom - listTop) / PanelRowHeight));

            hud.Rect(x, top, PanelWidth, h - top, new Vector4(0.05f, 0.06f, 0.08f, 0.93f));
            hud.Rect(x, top, PanelWidth, 26f,
                panel.Focused ? new Vector4(0.16f, 0.30f, 0.46f, 1f) : new Vector4(0.11f, 0.13f, 0.17f, 1f));

            hud.Text(x + 10f, top + 8f, PanelTextScale, "PROPRIETES",
                new Vector4(0.92f, 0.95f, 1f, 1f));

            // L'etat du clavier doit se LIRE : sans ca, on tape dans le vide en
            // croyant piloter la camera, ou l'inverse. C'est le premier defaut
            // qu'un panneau modal produit.
            string state = panel.Editing ? "SAISIE" : panel.Focused ? "TAB : RENDRE" : "TAB : PRENDRE";

            hud.Text(x + PanelWidth - 10f - BitmapFont.MeasureWidth(state, 1.5f), top + 9f, 1.5f, state,
                panel.Editing ? new Vector4(1f, 0.85f, 0.4f, 1f) : new Vector4(0.62f, 0.70f, 0.80f, 1f));

            var fields = panel.Fields;

            // Defilement : on garde la ligne courante a l'ecran. Un panneau qui
            // ne suit pas son curseur rend les dernieres proprietes d'un PNJ
            // atteignables mais invisibles - on les regle a l'aveugle.
            int first = 0;

            if (panel.Index >= 0 && fields.Count > visible)
                first = Math.Clamp(panel.Index - visible / 2, 0, fields.Count - visible);

            for (int row = 0; row < visible && first + row < fields.Count; row++)
            {
                int i = first + row;
                PropertyField field = fields[i];

                float y = listTop + row * PanelRowHeight;
                bool current = i == panel.Index;

                if (field.Kind == FieldKind.Header)
                {
                    hud.Rect(x, y, PanelWidth, PanelRowHeight, new Vector4(0.10f, 0.12f, 0.16f, 1f));
                    hud.Text(x + 10f, y + 6f, 1.5f, field.Label.ToUpperInvariant(),
                        new Vector4(0.55f, 0.72f, 0.90f, 1f));
                    continue;
                }

                if (current)
                {
                    hud.Rect(x, y, PanelWidth, PanelRowHeight,
                        panel.Editing ? new Vector4(0.12f, 0.26f, 0.16f, 1f)
                       : panel.Focused ? new Vector4(0.17f, 0.32f, 0.50f, 1f)
                                       : new Vector4(0.14f, 0.15f, 0.19f, 1f));
                }
                else if ((i & 1) == 0)
                {
                    hud.Rect(x, y, PanelWidth, PanelRowHeight, new Vector4(0.07f, 0.08f, 0.11f, 0.6f));
                }

                hud.Text(x + 10f, y + 6f, PanelTextScale, field.Label.ToUpperInvariant(),
                    new Vector4(0.60f, 0.66f, 0.75f, 1f));

                string value;
                Vector4 valueColor;

                if (current && panel.Editing)
                {
                    // Le curseur est un vrai caractere et non un rectangle
                    // clignotant : il se voit sur une capture, donc la saisie
                    // se verifie en headless comme le reste.
                    value = panel.Buffer + "_";
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

                hud.Text(x + PanelWidth - 10f - BitmapFont.MeasureWidth(value, PanelTextScale),
                         y + 6f, PanelTextScale, value.ToUpperInvariant(), valueColor);
            }

            // --- Bas du panneau : ce que fait le champ, ou ce qui a rate -----
            float hintY = h - footer + 6f;

            if (panel.Message.Length > 0)
            {
                hud.Text(x + 10f, hintY, 1.5f, panel.Message.ToUpperInvariant(),
                    new Vector4(1f, 0.5f, 0.4f, 1f));
            }
            else if (panel.Current is PropertyField hinted && hinted.Hint.Length > 0)
            {
                DrawWrapped(hud, x + 10f, hintY, PanelWidth - 20f, 1.5f, hinted.Hint.ToUpperInvariant(),
                    new Vector4(0.58f, 0.64f, 0.72f, 1f), lines: 2);
            }

            hud.Text(x + 10f, h - 20f, 1.5f,
                panel.Editing ? "ENTREE VALIDER   ECHAP ANNULER"
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
        private static void DrawWrapped(HudRenderer hud, float x, float y, float width, float scale,
                                        string text, Vector4 color, int lines)
        {
            if (text.Length == 0)
                return;

            int perLine = Math.Max(4, (int)(width / (BitmapFont.Advance * scale)));
            string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var line = new StringBuilder();
            int drawn = 0;

            foreach (string word in words)
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > perLine)
                {
                    hud.Text(x, y + drawn * (BitmapFont.LineHeight(scale) + 4f), scale, line.ToString(), color);
                    line.Clear();

                    if (++drawn >= lines)
                        return;
                }

                if (line.Length > 0)
                    line.Append(' ');

                line.Append(word);
            }

            if (line.Length > 0 && drawn < lines)
                hud.Text(x, y + drawn * (BitmapFont.LineHeight(scale) + 4f), scale, line.ToString(), color);
        }
    }
}
