// ===========================================================================
//  Console : l'editeur de niveaux (4.1)
//
//  Ces commandes existent d'abord pour la VERIFICATION : sans elles,
//  l'editeur ne serait atteignable qu'au clavier, donc impossible a capturer
//  en mode headless - donc impossible a verifier sans un humain devant
//  l'ecran.
// ===========================================================================

using System;
using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Console;
using Exarion.Core.Editor;
using Exarion.Engine.Graphics;
using Exarion.Game.Editing;

namespace Exarion.Game.Commands
{
    internal static class EditorCommands
    {
        /// <param name="aimPoint">Point vise au sol, la ou « ed_entity » pose par defaut.</param>
        public static void Register(ConsoleSystem c, EditorController editor, Camera camera, Func<Vector3> aimPoint)
        {
            c.Register("ed_select", args =>
            {
                if (editor.State is not { } ed) { c.Print("  editeur inactif"); return; }

                if (args.Length >= 1 && int.TryParse(args[0], out int index))
                    ed.Select(index);
                else
                    ed.Deselect();

                c.Print(ed.HasSelection
                    ? $"  boite {ed.Selected} selectionnee"
                    : "  rien de selectionne");
            }, "ed_select [n] - selectionne une boite, sans argument deselectionne");

            // ⚠️ Cette commande existe d'abord pour la VERIFICATION. Sans elle,
            // la forme ne se changerait qu'au clavier, donc les formes seraient
            // inverifiables en headless - meme raison que toutes les autres
            // commandes ed_*.
            c.Register("ed_shape", args =>
            {
                if (editor.State is not { } ed) { c.Print("  editeur inactif"); return; }

                if (args.Length == 0)
                {
                    c.Print($"  forme des prochains objets : {MapShapes.Name(ed.NewShape)}");
                    c.Print("  formes : " + string.Join(", ", Array.ConvertAll(MapShapes.All, MapShapes.Name)));

                    if (ed.HasSelection)
                        c.Print($"  selection : {MapShapes.Name(ed.Map.Boxes[ed.Selected].Shape)}");

                    return;
                }

                if (!MapShapes.TryParse(args[0], out MapShape shape))
                {
                    c.Print($"  forme inconnue \"{args[0]}\"");
                    return;
                }

                ed.NewShape = shape;

                // La selection suit, s'il y en a une : c'est le comportement
                // attendu quand on regarde un objet en tapant la commande.
                if (ed.SetShape(shape))
                    editor.GeometryDirty = true;

                c.Print($"  forme {MapShapes.Name(shape)}" +
                        (ed.HasSelection ? " - appliquee a la selection" : ""));

                c.Print("  ⚠ la COLLISION reste la boite englobante : une rampe ne se monte pas");
            }, "ed_shape [bloc|sphere|cylindre|rampe|cone] - forme des objets crees, et de la selection");

            c.Register("ed_axis", args =>
            {
                if (editor.State is not { } ed) { c.Print("  editeur inactif"); return; }

                ed.Axis = args.Length >= 1 && args[0].Length > 0
                    ? char.ToLowerInvariant(args[0][0]) switch
                    {
                        'y' => EditAxis.Y,
                        'z' => EditAxis.Z,
                        _ => EditAxis.X,
                    }
                    : EditAxis.X;

                c.Print($"  axe {ed.Axis}");
            }, "ed_axis x|y|z");

            c.Register("ed_move", args =>
            {
                if (editor.State is not { } ed) { c.Print("  editeur inactif"); return; }

                int steps = args.Length >= 1 && int.TryParse(args[0], out int n) ? n : 1;

                if (ed.Move(ed.Axis, steps)) editor.GeometryDirty = true;
                c.Print($"  deplacee de {steps} pas sur {ed.Axis}");
            }, "ed_move [pas] - deplace la selection sur l'axe actif");

            c.Register("ed_size", args =>
            {
                if (editor.State is not { } ed) { c.Print("  editeur inactif"); return; }

                int steps = args.Length >= 1 && int.TryParse(args[0], out int n) ? n : 1;

                if (ed.Resize(ed.Axis, steps)) editor.GeometryDirty = true;
                c.Print($"  redimensionnee de {steps} pas sur {ed.Axis}");
            }, "ed_size [pas] - redimensionne la selection sur l'axe actif");

            c.Register("ed_undo", _ =>
            {
                if (editor.State is not { } ed) { c.Print("  editeur inactif"); return; }
                c.Print(ed.Undo() ? "  annule" : "  rien a annuler");
                editor.GeometryDirty = true;
            }, "Annule la derniere operation");

            c.Register("ed_redo", _ =>
            {
                if (editor.State is not { } ed) { c.Print("  editeur inactif"); return; }
                c.Print(ed.Redo() ? "  retabli" : "  rien a retablir");
                editor.GeometryDirty = true;
            }, "Retablit l'operation annulee");

            c.Register("ed_save", _ => editor.Save(), "Enregistre la carte editee");

            c.Register("ed_entity", args =>
            {
                if (editor.State is not { } ed) { c.Print("  editeur inactif"); return; }

                if (args.Length == 0)
                {
                    c.Print("  types : " + string.Join(", ", EntityTypes.All));
                    return;
                }

                Vector3 at = args.Length >= 4
                    ? CommandHelpers.ParsePoint(args[1..], aimPoint())
                    : aimPoint();

                int index = ed.AddEntity(args[0], at,
                                         camera.Yaw * 180f / MathF.PI,
                                         camera.Pitch * 180f / MathF.PI);

                c.Print($"  entite {index} : {args[0]} en ({at.X:0.##}, {at.Y:0.##}, {at.Z:0.##})");
            }, "ed_entity <type> [x y z] - pose une entite. Sans argument, liste les types");

            c.Register("ed_ent_select", args =>
            {
                if (editor.State is not { } ed) { c.Print("  editeur inactif"); return; }

                if (args.Length >= 1 && int.TryParse(args[0], out int index))
                    ed.SelectEntity(index);
                else
                    ed.Deselect();

                c.Print(ed.SelectedEntity is MapEntity e
                    ? $"  entite {ed.Selected} : {e.Type}"
                    : "  rien de selectionne");
            }, "ed_ent_select [n] - selectionne une entite");

            c.Register("ed_prop", args =>
            {
                if (editor.State is not { } ed) { c.Print("  editeur inactif"); return; }

                if (args.Length < 2)
                {
                    if (ed.SelectedEntity is MapEntity current)
                        foreach (var pair in current.Properties)
                            c.Print($"  {pair.Key} = {pair.Value}");
                    else
                        c.Print("  aucune entite selectionnee");

                    return;
                }

                c.Print(ed.SetProperty(args[0], args[1])
                    ? $"  {args[0]} = {args[1]}"
                    : "  aucune entite selectionnee");
            }, "ed_prop <cle> <valeur> - ecrit une propriete. Sans argument, les liste");

            c.Register("ed_list", _ =>
            {
                if (editor.State is not { } ed) { c.Print("  editeur inactif"); return; }

                var entities = ed.Map.Entities;

                if (entities.Count == 0) { c.Print("  aucune entite"); return; }

                for (int i = 0; i < entities.Count; i++)
                    c.Print($"  {i,3}  {entities[i].Type,-22} " +
                            $"{entities[i].Position.X:0.##} {entities[i].Position.Y:0.##} {entities[i].Position.Z:0.##}");
            }, "Liste les entites de la carte");

            c.Bind("ed_grid", 1f, 0.125f, 8f, "Pas d'accrochage de l'editeur, en metres",
                   x =>
                   {
                       if (editor.State is not { } ed) return;

                       // On prend le pas disponible le plus proche : la liste est
                       // volontairement discrete, un pas libre casserait
                       // l'alignement de tout ce qui existe deja.
                       int best = 0;
                       for (int i = 1; i < EditorState.GridSteps.Length; i++)
                           if (MathF.Abs(EditorState.GridSteps[i] - x) < MathF.Abs(EditorState.GridSteps[best] - x))
                               best = i;

                       while (ed.GridSize > EditorState.GridSteps[best]) ed.CycleGrid(-1);
                       while (ed.GridSize < EditorState.GridSteps[best]) ed.CycleGrid(+1);

                       editor.Grid.Invalidate();
                   });

            c.Bind("ed_show_grid", 1f, 0f, 1f, "Afficher la trame au sol", x => editor.Grid.Visible = x >= 0.5f);
        }
    }
}
