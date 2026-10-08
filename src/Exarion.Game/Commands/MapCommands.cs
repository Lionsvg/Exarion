// ===========================================================================
//  Console : les cartes (4.2)
// ===========================================================================

using System;
using System.IO;
using Exarion.Core.Assets;
using Exarion.Core.Console;
using Exarion.Game.World;

namespace Exarion.Game.Commands
{
    internal static class MapCommands
    {
        /// <param name="canApply">Vrai quand le rendu existe : une carte appliquee doit etre redessinee.</param>
        /// <param name="apply">Applique une carte LUE EN ENTIER : decor, depart, eclairage.</param>
        /// <param name="buildSandbox">Le decor de reference integre, aux reglages courants.</param>
        public static void Register(ConsoleSystem c, GameWorld world, Func<bool> canApply,
                                    Action<MapData, string> apply, Func<MapData> buildSandbox)
        {
            c.Register("map", _ =>
            {
                if (world.Map is not { } map) { c.Print("  aucune carte"); return; }

                c.Print($"  {map.Name}");
                c.Print($"  {world.MapSource}");
                c.Print($"  {map.Boxes.Count} boites, {map.Lights.Count} lumieres");
                c.Print($"  depart ({map.Spawn.X:0.##}, {map.Spawn.Y:0.##}, {map.Spawn.Z:0.##})");
                c.Print($"  dossier : {MapLibrary.FindFolder()}");
            }, "Etat de la carte chargee");

            c.Register("map_list", _ =>
            {
                string folder = MapLibrary.FindFolder();

                if (!Directory.Exists(folder)) { c.Print($"  aucun dossier {folder}"); return; }

                string[] files = Directory.GetFiles(folder, "*" + MapFile.Extension);

                if (files.Length == 0) { c.Print("  aucune carte"); return; }

                foreach (string file in files)
                    c.Print("  " + Path.GetFileNameWithoutExtension(file));
            }, "Liste les cartes du dossier maps");

            c.Register("map_save", args =>
            {
                if (world.Map is not { } map) { c.Print("  aucune carte a enregistrer"); return; }

                string name = args.Length >= 1 ? args[0] : map.Name.Replace(' ', '_');

                try
                {
                    string path = MapLibrary.Resolve(name);
                    MapFile.SaveTo(map, path);
                    c.Print($"  {map.Boxes.Count} boites et {map.Lights.Count} lumieres ecrites");
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
                if (!canApply()) { c.Print("  rendu non initialise"); return; }

                try
                {
                    string path = MapLibrary.Resolve(args[0]);
                    MapData loaded = MapFile.LoadFrom(path);

                    // La carte est lue EN ENTIER avant d'etre appliquee : si le
                    // fichier est fautif, l'exception part d'ici et le monde
                    // courant n'a pas ete touche. Un chargement a moitie fait
                    // laisserait un decor mi-ancien mi-nouveau, invisible et
                    // impossible a diagnostiquer.
                    apply(loaded, path);

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
                string source = world.MapSource;

                if (!canApply() || !File.Exists(source)) { c.Print("  la carte courante ne vient pas d'un fichier"); return; }

                try
                {
                    MapData loaded = MapFile.LoadFrom(source);
                    apply(loaded, source);
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
                if (!canApply()) { c.Print("  rendu non initialise"); return; }

                apply(buildSandbox(), SandboxMap.Source);
                c.Print("  bac a sable integre recharge");
            }, "Revient au decor de reference integre au moteur");
        }
    }
}
