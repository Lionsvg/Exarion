// ===========================================================================
//  Console : le monde simule - entites, PNJ, portes, physique
// ===========================================================================

using Exarion.Core.Console;
using Exarion.Core.Entities;
using Exarion.Core.Physics;
using Exarion.Game.World;

namespace Exarion.Game.Commands
{
    internal static class WorldCommands
    {
        public static void Register(ConsoleSystem c, GameWorld world)
        {
            // --- Physique des caisses ---
            //
            // ⚠️ Par GameWorld, jamais directement sur world.Physics : le monde
            // physique est recree a chaque carte, ses reglages doivent survivre.
            c.Bind("phys_iterations", world.Physics.SolverIterations, 1f, 40f,
                   "Passes de degagement entre caisses", x => world.SetSolverIterations((int)x));
            c.Bind("phys_velocity_iterations", world.Physics.VelocityIterations, 1f, 60f,
                   "Passes de vitesse du solveur", x => world.SetVelocityIterations((int)x));
            c.Bind("phys_sleep", world.Physics.SleepSeconds, 0.05f, 5f,
                   "Delai avant qu'une caisse s'endorme", x => world.SetSleepSeconds(x));

            // Les reglages EFFECTIFS, lus sur le monde physique courant - pas sur
            // les ConVar. C'est la difference entre les deux qui trahit une
            // ConVar qui ment.
            c.Register("phys", _ =>
            {
                PhysicsWorld p = world.Physics;

                c.Print($"  gravite {-p.Gravity.Y:0.##} m/s2 (joueur {world.Motor.Settings.Gravity:0.##})");
                c.Print($"  passes {p.VelocityIterations}v/{p.SolverIterations}p  sommeil {p.SleepSeconds:0.##} s");
                c.Print($"  {p.Bodies.Count} corps, {p.ContactCount} contacts");
            }, "Reglages effectifs du monde physique courant");

            // --- Entites vivantes (E13) et PNJ (E14) ---
            c.Register("ent", _ =>
            {
                c.Print($"  {world.Entities.Map.Entities.Count} entites, " +
                        $"{world.Entities.TotalEvents} declenchements depuis le chargement");

                if (world.Entities.Checkpoints.Count > 0)
                    c.Print("  checkpoints : " + string.Join(" > ", world.Entities.Checkpoints));
                else
                    c.Print("  aucun checkpoint franchi");
            }, "Etat des entites vivantes");

            c.Register("npc", _ =>
            {
                var agents = world.Agents.Agents;

                if (agents.Count == 0) { c.Print("  aucun PNJ dans la carte"); return; }

                for (int i = 0; i < agents.Count; i++)
                {
                    Agent a = agents[i];

                    c.Print($"  {i,2}  {(a.Name.Length > 0 ? a.Name : "(sans nom)"),-12} " +
                            $"{a.State,-6}  {(a.SeesPlayer ? "VOIT" : "    ")}  " +
                            $"pos {a.Position.X:0.#} {a.Position.Y:0.#} {a.Position.Z:0.#}  " +
                            $"{a.Speed:0.0} m/s  point {a.Waypoint}");
                }

                c.Print($"  {world.Agents.TotalEvents} evenements de PNJ depuis le chargement");
            }, "Etat de chaque PNJ");

            // --- E16 : calcul de chemin ---------------------------------------
            //
            // ⚠️ nav_enabled ne reconstruit rien : la grille reste construite, on
            // cesse seulement de s'en servir. C'est ce qui permet de basculer EN
            // PARTIE et de voir, sur la meme carte et au meme instant, ce que le
            // calcul de chemin change.
            c.Bind("nav_enabled", 1f, 0f, 1f, "Calcul de chemin des PNJ (0 = ligne droite d'avant E16)",
                   x => world.Agents.NavigationEnabled = x >= 0.5f);

            c.Bind("nav_show", 0f, 0f, 1f, "Dessine le chemin de chaque PNJ (bleu : vers la cible, orange : au plus pres)",
                   x => world.ShowNavigation = x >= 0.5f);

            c.Register("nav", _ =>
            {
                Exarion.Core.Navigation.NavGrid? grid = world.Agents.Navigation;

                if (grid is null)
                {
                    c.Print("  pas de grille : aucun PNJ dans la carte");
                    return;
                }

                c.Print($"  grille {grid.Width}x{grid.Depth} a {grid.CellSize * 100f:0} cm : {grid.NodeCount} sols, " +
                        $"{grid.EdgeCount} passages, construite en {world.Agents.NavigationBuildMilliseconds:0.0} ms");

                // Sans la table, une cible inaccessible coute une inondation de
                // la grille : il faut que ca se VOIE.
                c.Print($"  {grid.ComponentCount} composantes, table d'accessibilite " +
                        (grid.HasReachTable ? "oui" : "NON (trop de composantes)"));

                // Le compteur de recherches est le temoin du defaut de la
                // premiere version : un A* a chaque pas en ferait 64 par seconde
                // et par PNJ.
                c.Print($"  {grid.Searches} recherches depuis le chargement, la derniere a visite {grid.LastExpansions} sols" +
                        (world.Agents.NavigationEnabled ? "" : "  [nav_enabled 0]"));

                var agents = world.Agents.Agents;

                for (int i = 0; i < agents.Count; i++)
                {
                    Agent a = agents[i];
                    string name = a.Name.Length > 0 ? a.Name : "(sans nom)";

                    string detail = a.Path.Count > 0
                        ? $"point {a.PathCursor}/{a.Path.Count - 1}, fin en {a.Path[^1].X:0.#} {a.Path[^1].Y:0.#} {a.Path[^1].Z:0.#}"
                        : "";

                    c.Print($"  {i,2}  {name,-12} {a.NavMode,-8} {detail}");
                }
            }, "Grille de navigation et chemin de chaque PNJ");

            c.Register("ent_rearm", _ =>
            {
                world.Entities.Rearm();
                c.Print("  declencheurs rearmes, checkpoints effaces");
            }, "Rearme tous les declencheurs");

            // --- E17 : les entites qui AGISSENT ------------------------------
            //
            // ⚠️ Ces commandes existent d'abord POUR LA VERIFICATION. Sans
            // elles, tester une porte demanderait de courir jusqu'a son
            // declencheur, ce qui est impossible en headless : les --exec
            // partent tous a t=0, avant que le joueur ait bouge.

            c.Register("ent_send", args =>
            {
                if (args.Length < 2)
                {
                    c.Print("  ent_send <cible> <entree>");
                    return;
                }

                EntityDelivery delivery = world.Entities.Send(args[0], args[1]);

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
                var movers = world.Movers.Movers;

                if (movers.Count == 0) { c.Print("  aucune porte dans la carte"); return; }

                for (int i = 0; i < movers.Count; i++)
                {
                    Mover m = movers[i];

                    c.Print($"  {i,2}  {(m.Name.Length > 0 ? m.Name : "(sans nom)"),-14} " +
                            $"{m.State,-10} {m.Fraction * 100f,5:0.0} %  " +
                            $"centre {m.Center.X:0.##} {m.Center.Y:0.##} {m.Center.Z:0.##}  " +
                            $"{m.Speed:0.0} m/s" + (m.Degenerate ? "  [DISTANCE NULLE]" : ""));
                }

                c.Print($"  {world.Movers.MovingCount} en mouvement");
            }, "Etat de chaque porte / ascenseur");

            c.Register("carry_debug", _ =>
            {
                world.CarryDebug = !world.CarryDebug;
                c.Print("  carry_debug " + (world.CarryDebug ? "1" : "0"));
            }, "Trace le portage du joueur par les plateformes");

            // --- Entites logiques : relais, minuteries, compteurs ---
            c.Register("logic", _ =>
            {
                var nodes = world.Logic.Nodes;

                if (nodes.Count == 0) { c.Print("  aucune entite logique dans la carte"); return; }

                for (int i = 0; i < nodes.Count; i++)
                {
                    LogicNode n = nodes[i];
                    string name = n.Name.Length > 0 ? n.Name : "(sans nom)";

                    string state = n.Kind switch
                    {
                        LogicKind.Relay => (n.Enabled ? "actif" : "INACTIF") + $", delai {n.Delay:0.##} s" + (n.Once ? ", unique" : ""),
                        LogicKind.Timer => n.Enabled ? $"tourne, {n.Interval:0.##} s, prochaine a {n.NextFire:0.00} s" : "ARRETEE",
                        _ => $"{n.Value}/{n.Max}",
                    };

                    string target = n.Entity.Get("target");

                    c.Print($"  {i,2}  {name,-12} {n.Kind,-8} {state}" +
                            (target.Length > 0
                                ? $"  -> {target}.{n.Entity.Get("input", LogicWorld.DefaultOutputInput)}"
                                : "  (non cablee)"));
                }

                // Le refus est AFFICHE : une file pleine qui se tairait ferait
                // « arreter de marcher » un niveau sans raison visible.
                c.Print($"  {world.Logic.PendingCount} sortie(s) en attente, {world.Logic.Dropped} refusee(s), " +
                        $"{world.Logic.TotalEvents} livree(s) ; t = {world.Logic.Now:0.00} s");
            }, "Relais, minuteries et compteurs : etat, cablage, file");

            // --- Ambiance : lumieres et sons commutables ---
            c.Register("env", _ =>
            {
                var lights = world.Env.Lights;
                var sounds = world.Env.Sounds;

                if (lights.Count == 0 && sounds.Count == 0) { c.Print("  aucune lumiere ni son commutable dans la carte"); return; }

                foreach (EnvLight l in lights)
                    c.Print($"  lumiere {(l.Name.Length > 0 ? l.Name : "(sans nom)"),-12} {(l.On ? "ALLUMEE" : "eteinte")}  " +
                            $"intensite {l.Intensity:0.#}  portee {l.Range:0.#} m");

                foreach (EnvSound s in sounds)
                    c.Print($"  son     {(s.Name.Length > 0 ? s.Name : "(sans nom)"),-12} {(s.Playing ? "JOUE" : "muet")}  " +
                            $"{s.Hz:0} Hz  volume {s.Volume:0.##}");

                c.Print($"  version {world.Env.Version}");
            }, "Lumieres et sons commutables : etat");

            c.Register("ent_targets", _ =>
            {
                var receivers = world.Entities.Receivers;

                if (receivers.Count == 0) { c.Print("  aucune cible inscrite"); return; }

                for (int i = 0; i < receivers.Count; i++)
                    c.Print($"  {receivers[i].Name}");
            }, "Liste les noms qu'une sortie peut viser");
        }
    }
}
