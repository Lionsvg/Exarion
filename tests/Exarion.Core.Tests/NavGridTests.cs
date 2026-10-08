// ===========================================================================
//  Tests de la grille de navigation (E16)
//
//  La grille repond a deux questions - « ou le PNJ peut-il se tenir ? » et
//  « comment aller de la a la ? » - et les deux se trompent de facon
//  SILENCIEUSE : un chemin qui passe par un mur ne leve rien, il donne un PNJ
//  qui pousse contre ce mur en croyant avancer. D'ou l'invariant verifie plus
//  bas sur tous les chemins : aucun point ne fait chevaucher le corps et le
//  decor.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core.Navigation;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class NavGridTests
    {
        private static readonly NavAgentShape Body = NavAgentShape.From(new MotorSettings());

        /// <summary>Sol de 40 m de cote, dessus a y = 0.</summary>
        private static CollisionWorld Floor()
        {
            var world = new CollisionWorld();
            world.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
            return world;
        }

        /// <summary>Longueur d'un chemin, segment par segment.</summary>
        private static float Length(List<Vector3> path)
        {
            float total = 0f;
            for (int i = 1; i < path.Count; i++)
                total += Vector3.Distance(path[i - 1], path[i]);
            return total;
        }

        /// <summary>
        /// ⚠️ L'INVARIANT. Le corps, pose a chaque point du chemin ET tout le
        /// long de chaque segment, ne doit chevaucher aucune boite - au-dessus
        /// d'une hauteur de marche, puisque ce qui est plus bas se gravit.
        /// </summary>
        private static void AssertBodyNeverInsideWalls(CollisionWorld world, List<Vector3> path)
        {
            for (int s = 1; s < path.Count; s++)
            {
                Vector3 a = path[s - 1], b = path[s];
                int samples = Math.Max(1, (int)(Vector3.Distance(a, b) / 0.05f));

                for (int t = 0; t <= samples; t++)
                {
                    Vector3 p = Vector3.Lerp(a, b, t / (float)samples);

                    // La hauteur varie le long d'une marche : on prend le plus
                    // haut des deux sols, le corps passe au-dessus de la marche.
                    p.Y = MathF.Max(a.Y, b.Y) + Body.StepHeight;

                    // Un peu plus etroit que le corps : la ligne de vue est
                    // echantillonnee, et un frottement d'un centimetre contre un
                    // mur est absorbe par le glissement du CharacterMotor.
                    Aabb body = Aabb.ForCharacter(p, Body.Width - 0.12f, Body.Height - Body.StepHeight);

                    ClassicAssert.IsFalse(world.Overlaps(body), $"le corps traverse le decor en {p}");
                }
            }
        }

        // ===================================================================
        //  Ou peut-on se tenir ?
        // ===================================================================

        [Test]
        public void Un_Sol_Plat_Est_Marchable_Partout_Sauf_Contre_Les_Bords()
        {
            var grid = NavGrid.Build(Floor(), Body);

            ClassicAssert.Greater(grid.NodeCount, 20000, "40 x 40 m a 25 cm : environ 25 000 sols");
            ClassicAssert.GreaterOrEqual(grid.FindNode(new Vector3(0f, 0f, 0f)), 0);
        }

        [Test]
        public void Un_Plafond_Trop_Bas_N_Est_Pas_Un_Endroit_Ou_Se_Tenir()
        {
            var world = Floor();
            world.AddBox(new Vector3(0f, 1.5f + 0.25f, 0f), new Vector3(6f, 0.5f, 6f));   // plafond a 1,50 m

            var grid = NavGrid.Build(world, Body);
            int n = grid.FindNode(new Vector3(0f, 0f, 0f));

            // Sous le plafond, aucun sol au niveau 0 : le plus proche est le
            // DESSUS du plafond, ou le sol au-dela de son bord.
            ClassicAssert.IsTrue(n < 0 || MathF.Abs(grid.PositionOf(n).X) > 2.5f || grid.PositionOf(n).Y > 1f,
                                 $"un sol sous un plafond de 1,50 m pour un corps de 1,83 m : {(n < 0 ? "aucun" : grid.PositionOf(n).ToString())}");
        }

        /// <summary>
        /// Plusieurs etages dans une colonne : le sol sous une estrade haute ET
        /// son dessus. Des pieds poses sur l'estrade doivent trouver le dessus,
        /// des pieds au sol le sol.
        /// </summary>
        [Test]
        public void Une_Colonne_A_Plusieurs_Etages()
        {
            var world = Floor();
            world.AddBox(new Vector3(0f, 2.75f, 0f), new Vector3(6f, 0.5f, 6f));   // dalle de 2,50 a 3,00 m

            var grid = NavGrid.Build(world, Body);

            ClassicAssert.AreEqual(0f, grid.PositionOf(grid.FindNode(new Vector3(0f, 0f, 0f))).Y, 1e-4f);
            ClassicAssert.AreEqual(3f, grid.PositionOf(grid.FindNode(new Vector3(0f, 3f, 0f))).Y, 1e-4f);
        }

        // ===================================================================
        //  Comment aller de la a la ?
        // ===================================================================

        [Test]
        public void En_Terrain_Degage_Le_Chemin_Est_Une_Ligne_Droite()
        {
            var grid = NavGrid.Build(Floor(), Body);
            var path = new List<Vector3>();

            ClassicAssert.AreEqual(PathStatus.Complete, grid.FindPath(new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 5f), path));

            // Le lissage a tire le fil : depart et arrivee, rien entre les deux.
            ClassicAssert.AreEqual(2, path.Count, string.Join(" ", path));
        }

        /// <summary>
        /// LE cas qui justifie la grille : un mur entre le PNJ et sa cible, avec
        /// un passage sur le cote. La ligne droite est barree, le chemin
        /// contourne, et le corps ne touche jamais le mur.
        /// </summary>
        [Test]
        public void Un_Mur_Se_Contourne()
        {
            var world = Floor();
            world.AddBox(new Vector3(0f, 1.5f, 0f), new Vector3(20f, 3f, 0.5f));   // mur de x -10 a 10, en z = 0

            var grid = NavGrid.Build(world, Body);
            var path = new List<Vector3>();
            Vector3 from = new(0f, 0f, 5f), to = new(0f, 0f, -5f);

            ClassicAssert.IsFalse(grid.LineWalkable(from, to), "la ligne droite traverse le mur");
            ClassicAssert.AreEqual(PathStatus.Complete, grid.FindPath(from, to, path));

            // Contourner un mur de 20 m depuis son milieu : bien plus que 10 m.
            ClassicAssert.Greater(Length(path), 20f);

            AssertBodyNeverInsideWalls(world, path);
        }

        /// <summary>
        /// Une piece sans porte : pas de chemin vers l'interieur. Mais le
        /// chemin PARTIEL mene au pied du mur, dehors - jamais a travers.
        /// </summary>
        [Test]
        public void Une_Piece_Fermee_N_A_Pas_De_Chemin_Mais_On_S_En_Approche()
        {
            var world = Floor();

            // Une boite creuse de 6 x 6 m, murs de 3 m, autour de (10, 10).
            world.AddBox(new Vector3(10f, 1.5f, 7f), new Vector3(6f, 3f, 0.5f));
            world.AddBox(new Vector3(10f, 1.5f, 13f), new Vector3(6f, 3f, 0.5f));
            world.AddBox(new Vector3(7f, 1.5f, 10f), new Vector3(0.5f, 3f, 6f));
            world.AddBox(new Vector3(13f, 1.5f, 10f), new Vector3(0.5f, 3f, 6f));

            var grid = NavGrid.Build(world, Body);
            var path = new List<Vector3>();
            Vector3 outside = new(-5f, 0f, -5f), inside = new(10f, 0f, 10f);

            ClassicAssert.IsFalse(grid.CanReach(outside, inside));
            ClassicAssert.AreEqual(PathStatus.Partial, grid.FindPath(outside, inside, path));

            Vector3 end = path[^1];
            TestContext.Out.WriteLine($"fin du chemin partiel : {end}, a {Vector3.Distance(end, inside):0.00} m du centre");

            ClassicAssert.IsTrue(MathF.Abs(end.X - 10f) > 3.2f || MathF.Abs(end.Z - 10f) > 3.2f, $"fin du chemin DANS la piece : {end}");
            ClassicAssert.Less(Vector3.Distance(end, inside), 5f, "il s'est approche du mur");
            AssertBodyNeverInsideWalls(world, path);
        }

        /// <summary>
        /// ⚠️ LE CAS LE PLUS CHER, ET LE PLUS FREQUENT : une cible inaccessible.
        /// Un A* qui echoue visite TOUT ce qui est accessible depuis le depart -
        /// mesure : 18 ms sur 60 x 60 m. La table des composantes le sait avant
        /// de chercher, et la recherche d'approche est bornee.
        /// </summary>
        [Test]
        public void Une_Cible_Inaccessible_Ne_Fait_Pas_Fouiller_Toute_La_Grille()
        {
            var world = new CollisionWorld();
            world.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            world.AddBox(new Vector3(0f, 0.9f, -6f), new Vector3(6f, 1.8f, 6f));   // estrade sans escalier

            var grid = NavGrid.Build(world, Body);
            Vector3 ground = new(0f, 0f, 8f), top = new(0f, 1.8f, -6f);

            ClassicAssert.IsTrue(grid.HasReachTable);
            ClassicAssert.IsFalse(grid.CanReach(ground, top), "on ne monte pas sur l'estrade");
            ClassicAssert.IsTrue(grid.CanReach(top, ground), "on en descend : le graphe est ORIENTE");

            var path = new List<Vector3>();
            var clock = Stopwatch.StartNew();
            PathStatus status = grid.FindPath(ground, top, path);
            clock.Stop();

            Vector3 end = path[^1];
            float dx = MathF.Max(0f, MathF.Abs(end.X) - 3f);
            float dz = MathF.Max(0f, MathF.Max(-9f - end.Z, end.Z + 3f));
            float fromPlatform = MathF.Sqrt(dx * dx + dz * dz);

            TestContext.Out.WriteLine($"{grid.ComponentCount} composantes ; approche en {clock.Elapsed.TotalMilliseconds:0.00} ms, " +
                                  $"{grid.LastExpansions} sols visites sur {grid.NodeCount} ; fin {end}, a {fromPlatform:0.00} m de l'estrade");

            ClassicAssert.AreEqual(PathStatus.Partial, status);
            ClassicAssert.LessOrEqual(grid.LastExpansions, NavGrid.ApproachBudget + 1, "la recherche doit etre BORNEE");
            ClassicAssert.Less(fromPlatform, 1f, "le chemin partiel mene au pied de l'estrade");
            ClassicAssert.AreEqual(0f, end.Y, 1e-4f, "au sol");
        }

        /// <summary>
        /// ⚠️ La ligne droite doit ARRIVER au niveau de la cible. Sans ce test, un
        /// joueur sur une passerelle est « en ligne droite » de tout le sol qui
        /// passe dessous, et le PNJ marche jusque sous ses pieds.
        /// </summary>
        [Test]
        public void Une_Passerelle_N_Est_Pas_En_Ligne_Droite_Du_Sol_Dessous()
        {
            var world = Floor();
            world.AddBox(new Vector3(0f, 2.8f, 0f), new Vector3(3f, 0.4f, 12f));   // dessous a 2,60 m, dessus a 3 m

            var grid = NavGrid.Build(world, Body);

            ClassicAssert.IsTrue(grid.LineWalkable(new Vector3(6f, 0f, 0f), new Vector3(0f, 0f, 0f)), "on passe dessous");
            ClassicAssert.IsFalse(grid.LineWalkable(new Vector3(6f, 0f, 0f), new Vector3(0f, 3f, 0f)), "on n'y monte pas en ligne droite");
        }

        /// <summary>
        /// En terrain degage, l'A* doit aller droit au but. Sur un trajet en
        /// biais, des milliers de cases ont EXACTEMENT le meme cout estime : sans
        /// departage et sans heuristique octile, il les visite toutes.
        /// </summary>
        [Test]
        public void En_Terrain_Degage_L_A_Etoile_Ne_Fouille_Pas_Tout()
        {
            var world = Floor();
            world.AddBox(new Vector3(0f, 1.5f, 0f), new Vector3(0.5f, 3f, 0.5f));   // un poteau : la ligne droite reste barree

            var grid = NavGrid.Build(world, Body);
            var path = new List<Vector3>();

            ClassicAssert.AreEqual(PathStatus.Complete, grid.FindPath(new Vector3(-18f, 0f, -9f), new Vector3(18f, 0f, 9f), path));
            TestContext.Out.WriteLine($"{grid.LastExpansions} sols visites sur {grid.NodeCount}");

            ClassicAssert.Less(grid.LastExpansions, 1000, "36 x 18 m : environ 150 cases sur le trajet");
        }

        /// <summary>
        /// Un passage plus etroit que le corps (0,6 m pour 0,8 m de large) est
        /// infranchissable ; le meme a 1,6 m passe.
        /// </summary>
        [Test]
        public void Un_Passage_Trop_Etroit_Est_Infranchissable()
        {
            CollisionWorld WallWithGap(float gap)
            {
                var w = Floor();
                float half = gap * 0.5f;

                // Les ailes DEPASSENT le sol de 40 m : sinon le chemin passe
                // en bout de mur, sur la bande de sol qui reste - c'est ce que
                // la premiere version de ce test laissait faire.
                float wing = 25f;

                w.AddBox(new Vector3(-half - wing * 0.5f, 1.5f, 0f), new Vector3(wing, 3f, 0.5f));
                w.AddBox(new Vector3(half + wing * 0.5f, 1.5f, 0f), new Vector3(wing, 3f, 0.5f));
                return w;
            }

            var path = new List<Vector3>();
            Vector3 from = new(0f, 0f, 5f), to = new(0f, 0f, -5f);

            ClassicAssert.AreEqual(PathStatus.Partial, NavGrid.Build(WallWithGap(0.6f), Body).FindPath(from, to, path), "0,6 m pour 0,8 m de large");

            var world = WallWithGap(1.6f);
            ClassicAssert.AreEqual(PathStatus.Complete, NavGrid.Build(world, Body).FindPath(from, to, path), "1,6 m");
            AssertBodyNeverInsideWalls(world, path);
        }

        /// <summary>
        /// Une estrade de 1,80 m SANS marches ne se monte pas - c'est la ou le
        /// garde du 17/08 s'arretait net - mais se descend (chute de moins de
        /// 2 m). Avec un escalier de marches de 30 cm sur le cote, elle se monte.
        /// </summary>
        [Test]
        public void Une_Estrade_Se_Monte_Par_L_Escalier_Seulement()
        {
            var world = Floor();
            world.AddBox(new Vector3(0f, 0.9f, -6f), new Vector3(6f, 1.8f, 6f));   // estrade, dessus a 1,80 m

            var grid = NavGrid.Build(world, Body);
            var path = new List<Vector3>();
            Vector3 ground = new(0f, 0f, 6f), top = new(0f, 1.8f, -6f);

            ClassicAssert.AreEqual(PathStatus.Partial, grid.FindPath(ground, top, path), "monter 1,80 m sans marche");
            ClassicAssert.AreEqual(PathStatus.Complete, grid.FindPath(top, ground, path), "redescendre 1,80 m");

            // Six marches de 30 cm le long du flanc est de l'estrade, vers le nord.
            for (int i = 0; i < 6; i++)
            {
                float h = 0.3f * (i + 1);
                world.AddBox(new Vector3(4.5f, h * 0.5f, 3f - i * 1.2f), new Vector3(3f, h, 1.2f));
            }

            grid = NavGrid.Build(world, Body);

            ClassicAssert.AreEqual(PathStatus.Complete, grid.FindPath(ground, top, path), "monter par l'escalier");
            ClassicAssert.AreEqual(1.8f, path[^1].Y, 1e-4f);
            AssertBodyNeverInsideWalls(world, path);
        }

        /// <summary>
        /// ⚠️ PAS DE COUPE DE COIN. Deux murs qui se touchent par un coin, en
        /// diagonale : aucun corps ne passe entre eux, le chemin non plus.
        /// </summary>
        [Test]
        public void Le_Chemin_Ne_Passe_Pas_Entre_Deux_Murs_Qui_Se_Touchent_Par_Un_Coin()
        {
            var world = Floor();
            world.AddBox(new Vector3(-5f, 1.5f, -0.5f), new Vector3(10f, 3f, 1f));   // de x -10 a 0, z -1 a 0
            world.AddBox(new Vector3(5f, 1.5f, 0.5f), new Vector3(10f, 3f, 1f));     // de x 0 a 10, z 0 a 1

            var grid = NavGrid.Build(world, Body);
            var path = new List<Vector3>();

            ClassicAssert.AreEqual(PathStatus.Complete, grid.FindPath(new Vector3(-3f, 0f, 4f), new Vector3(3f, 0f, -4f), path));
            AssertBodyNeverInsideWalls(world, path);
        }

        /// <summary>
        /// Le cout de construction, MESURE et affiche : un decor de 60 x 60 m
        /// et quarante boites, comme le bac a sable. La borne est large - elle
        /// attrape une explosion de complexite, pas une machine lente.
        /// </summary>
        [Test]
        public void La_Construction_Reste_Raisonnable()
        {
            var world = new CollisionWorld();
            world.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));

            var rng = new Random(1234);
            for (int i = 0; i < 40; i++)
                world.AddBox(new Vector3(rng.Next(-25, 25), 1f, rng.Next(-25, 25)),
                             new Vector3(1f + rng.Next(0, 4), 2f, 1f + rng.Next(0, 4)));

            var clock = Stopwatch.StartNew();
            var grid = NavGrid.Build(world, Body);
            clock.Stop();

            var path = new List<Vector3>();
            var pathClock = Stopwatch.StartNew();
            bool found = grid.FindPath(new Vector3(-28f, 0f, -28f), new Vector3(28f, 0f, 28f), path) == PathStatus.Complete;
            pathClock.Stop();

            TestContext.Out.WriteLine($"grille {grid.Width}x{grid.Depth}, {grid.NodeCount} sols, " +
                                  $"construite en {clock.Elapsed.TotalMilliseconds:0.0} ms ; " +
                                  $"chemin de {path.Count} points en {pathClock.Elapsed.TotalMilliseconds:0.00} ms, " +
                                  $"{grid.LastExpansions} sols visites, {grid.EdgeCount} passages, {grid.ComponentCount} composantes");

            ClassicAssert.IsTrue(found);
            ClassicAssert.Less(clock.Elapsed.TotalMilliseconds, 2000.0);
            ClassicAssert.Less(pathClock.Elapsed.TotalMilliseconds, 500.0);
            AssertBodyNeverInsideWalls(world, path);
        }
    }
}
