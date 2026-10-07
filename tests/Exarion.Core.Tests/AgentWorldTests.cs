// ===========================================================================
//  Tests des PNJ (E14).
//
//  Trois pieges dominent, et ce sont tous des pieges de STABILITE plutot que
//  de calcul :
//
//   - LA LIGNE DE VUE PART DES YEUX. Tiree depuis les pieds, elle est
//     arretee par la moindre marche : le PNJ est aveugle dans un decor
//     normal, et on cherche le defaut dans le champ de vision.
//
//   - L'HYSTERESIS DE POURSUITE. A la limite de portee la vue s'accroche et
//     se perd d'une image a l'autre. Sans delai d'abandon, le PNJ basculerait
//     entre poursuite et retour 64 fois par seconde, en emettant autant
//     d'evenements.
//
//   - LE RAYON D'ARRIVEE. Plus petit que la distance parcourue en un pas, le
//     PNJ depasse son point, revient, redepasse : il vibre sur place sans
//     jamais arriver.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Exarion.Core.Assets;
using Exarion.Core.Entities;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class AgentWorldTests
    {
        private const float Step = 1f / 64f;

        /// <summary>Sol plat de 100 m, rien d'autre.</summary>
        private static CollisionWorld Ground()
        {
            var world = new CollisionWorld();
            world.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(100f, 1f, 100f));
            return world;
        }

        private static MapEntity Npc(Vector3 at, params (string, string)[] props)
        {
            var e = new MapEntity(EntityTypes.Npc, at);
            foreach ((string k, string v) in props) e.Set(k, v);
            return e;
        }

        private static MapEntity Waypoint(Vector3 at, string path, int order)
            => new MapEntity(EntityTypes.Waypoint, at).Set("path", path).Set("order", order.ToString());

        /// <summary>Fait tourner le monde n pas, avec le joueur a un endroit fixe.</summary>
        private static void Run(AgentWorld world, int steps, Vector3 playerEye)
        {
            for (int i = 0; i < steps; i++)
                world.Update(Step, playerEye);
        }

        // ===================================================================
        //  Perception
        // ===================================================================

        [Test]
        public void Un_Mur_Bloque_La_Vue()
        {
            var level = Ground();
            level.AddBox(new Vector3(0f, 2f, -5f), new Vector3(20f, 4f, 0.5f));   // mur devant

            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("sight", "40"), ("fov", "360")));

            var world = new AgentWorld();
            world.Load(map, level);

            // Joueur derriere le mur.
            Assert.That(world.CanSee(world.Agents[0], new Vector3(0f, 1.6f, -12f)), Is.False);

            // Meme distance, mais de l'autre cote : rien ne bloque.
            Assert.That(world.CanSee(world.Agents[0], new Vector3(0f, 1.6f, 12f)), Is.True);
        }

        [Test]
        public void La_Vue_Part_DES_YEUX_Et_Pas_Des_Pieds()
        {
            // ⚠️ LE test de ce fichier. Un muret de 50 cm ne doit PAS aveugler
            // un garde de 1,80 m. Un rayon tire des pieds vers les pieds serait
            // arrete par toutes les marches d'un niveau normal.
            var level = Ground();
            level.AddBox(new Vector3(0f, 0.25f, -5f), new Vector3(20f, 0.5f, 0.5f));   // muret

            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("sight", "40"), ("fov", "360")));

            var world = new AgentWorld();
            world.Load(map, level);

            Assert.That(world.CanSee(world.Agents[0], new Vector3(0f, 1.6f, -12f)), Is.True,
                        "un muret de 50 cm aveugle le garde");
        }

        [Test]
        public void Hors_De_Portee_Le_PNJ_Ne_Voit_Rien()
        {
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("sight", "10"), ("fov", "360")));

            var world = new AgentWorld();
            world.Load(map, Ground());

            Assert.That(world.CanSee(world.Agents[0], new Vector3(0f, 1.6f, -8f)), Is.True);
            Assert.That(world.CanSee(world.Agents[0], new Vector3(0f, 1.6f, -30f)), Is.False);
        }

        [Test]
        public void Le_Champ_De_Vision_Est_Respecte()
        {
            var map = new MapData();

            // Lacet 0 = regarde vers -Z. Champ de 90 degres, donc 45 de chaque cote.
            map.Entities.Add(Npc(Vector3.Zero, ("sight", "40"), ("fov", "90")));

            var world = new AgentWorld();
            world.Load(map, Ground());

            Agent a = world.Agents[0];

            Assert.That(world.CanSee(a, new Vector3(0f, 1.6f, -10f)), Is.True, "droit devant");
            Assert.That(world.CanSee(a, new Vector3(0f, 1.6f, 10f)), Is.False, "droit derriere");
            Assert.That(world.CanSee(a, new Vector3(10f, 1.6f, 0f)), Is.False, "plein cote");
        }

        [Test]
        public void Le_Champ_De_Vision_Est_HORIZONTAL()
        {
            // Un joueur juste au-dessus, sur une marche, doit rester visible.
            // Comparer les directions en 3D le rendrait invisible alors qu'il
            // est pourtant droit devant.
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("sight", "40"), ("fov", "60")));

            var world = new AgentWorld();
            world.Load(map, Ground());

            Assert.That(world.CanSee(world.Agents[0], new Vector3(0f, 5f, -6f)), Is.True);
        }

        [Test]
        public void Une_Portee_Nulle_Rend_Le_PNJ_Aveugle()
        {
            // Utile pour un decor : un PNJ figure sans comportement.
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("sight", "0")));

            var world = new AgentWorld();
            world.Load(map, Ground());

            Assert.That(world.CanSee(world.Agents[0], new Vector3(0f, 1.6f, -1f)), Is.False);
        }

        // ===================================================================
        //  Poursuite et hysteresis
        // ===================================================================

        [Test]
        public void Voir_Le_Joueur_Declenche_OnSpotPlayer_UNE_FOIS()
        {
            var map = new MapData();
            map.Entities.Add(Npc(new Vector3(0f, 0f, 0f),
                ("sight", "40"), ("fov", "360"), ("behaviour", "guard")));

            var world = new AgentWorld();
            world.Load(map, Ground());

            Run(world, 60, new Vector3(0f, 1.6f, -8f));

            ClassicAssert.AreEqual(1, world.TotalEvents, "OnSpotPlayer a ete emis plusieurs fois");
            ClassicAssert.AreEqual(AgentState.Chase, world.Agents[0].State);
        }

        [Test]
        public void Perdre_Le_Joueur_Attend_Le_Delai_Avant_D_Abandonner()
        {
            // ⚠️ L'hysteresis. Sans elle, la bascule Chase/Return a la limite de
            // portee emettrait deux evenements par image.
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("sight", "40"), ("fov", "360"), ("behaviour", "guard")));

            var world = new AgentWorld();
            world.Load(map, Ground());

            Run(world, 2, new Vector3(0f, 1.6f, -8f));       // vu
            ClassicAssert.AreEqual(AgentState.Chase, world.Agents[0].State);

            // Le joueur disparait, mais pas assez longtemps.
            Run(world, 32, new Vector3(0f, 1.6f, -500f));    // 0,5 s
            ClassicAssert.AreEqual(AgentState.Chase, world.Agents[0].State, "abandon trop rapide");

            // Au-dela du delai, on abandonne.
            Run(world, 200, new Vector3(0f, 1.6f, -500f));   // ~3,1 s
            Assert.That(world.Agents[0].State, Is.Not.EqualTo(AgentState.Chase));
        }

        [Test]
        public void Une_Vue_Qui_Clignote_N_Emet_Pas_Une_Avalanche()
        {
            // Le joueur alterne visible/invisible a chaque pas - exactement ce
            // que produit la limite de portee. Sans hysteresis, on aurait deux
            // evenements par periode.
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("sight", "40"), ("fov", "360"), ("behaviour", "guard")));

            var world = new AgentWorld();
            world.Load(map, Ground());

            for (int i = 0; i < 200; i++)
                world.Update(Step, i % 2 == 0
                    ? new Vector3(0f, 1.6f, -8f)
                    : new Vector3(0f, 1.6f, -500f));

            Assert.That(world.TotalEvents, Is.LessThanOrEqualTo(2),
                        $"{world.TotalEvents} evenements pour une vue qui clignote");
        }

        // ===================================================================
        //  Ronde
        // ===================================================================

        [Test]
        public void Un_PNJ_En_Ronde_Avance_Vers_Son_Point()
        {
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("path", "ronde"), ("speed", "3"), ("sight", "0")));
            map.Entities.Add(Waypoint(new Vector3(0f, 0f, -10f), "ronde", 0));

            var world = new AgentWorld();
            world.Load(map, Ground());

            ClassicAssert.AreEqual(AgentState.Patrol, world.Agents[0].State);

            Run(world, 64, new Vector3(0f, 1.6f, 500f));   // une seconde

            // A 3 m/s il doit avoir avance de plusieurs metres vers -Z.
            Assert.That(world.Agents[0].Position.Z, Is.LessThan(-1.5f),
                        $"il n'a avance que de {-world.Agents[0].Position.Z:0.00} m");
        }

        [Test]
        public void Un_PNJ_En_Ronde_Enchaine_Ses_Points_Et_BOUCLE()
        {
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("path", "ronde"), ("speed", "6"), ("sight", "0")));
            map.Entities.Add(Waypoint(new Vector3(0f, 0f, -6f), "ronde", 0));
            map.Entities.Add(Waypoint(new Vector3(6f, 0f, -6f), "ronde", 1));

            var world = new AgentWorld();
            world.Load(map, Ground());

            int goals = 0;

            for (int i = 0; i < 64 * 12; i++)
            {
                world.Update(Step, new Vector3(0f, 1.6f, 500f));

                foreach (EntityEvent e in world.Events)
                    if (e.Output == AgentWorld.OnReachGoal) goals++;
            }

            // Deux points en douze secondes a 6 m/s : il doit avoir boucle.
            Assert.That(goals, Is.GreaterThanOrEqualTo(3), $"seulement {goals} points atteints");
        }

        [Test]
        public void L_Ordre_Des_Points_Vient_De_La_Propriete_order()
        {
            // Et pas de l'ordre du fichier : sans tri, une ronde changerait de
            // sens d'un enregistrement a l'autre.
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("path", "r"), ("speed", "6"), ("sight", "0")));

            // Ecrits a l'envers dans le fichier.
            map.Entities.Add(Waypoint(new Vector3(0f, 0f, 20f), "r", 1));
            map.Entities.Add(Waypoint(new Vector3(0f, 0f, -20f), "r", 0));

            var world = new AgentWorld();
            world.Load(map, Ground());

            Run(world, 64, new Vector3(0f, 1.6f, 500f));

            // Il doit partir vers le point d'ordre 0, donc vers -Z.
            Assert.That(world.Agents[0].Position.Z, Is.LessThan(-1f));
        }

        [Test]
        public void Un_Chemin_VIDE_Ne_Fait_Rien_Planter()
        {
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("path", "inexistant"), ("sight", "0")));

            var world = new AgentWorld();
            world.Load(map, Ground());

            ClassicAssert.AreEqual(AgentState.Idle, world.Agents[0].State, "sans chemin, on reste au poste");
            Assert.DoesNotThrow(() => Run(world, 100, new Vector3(0f, 1.6f, 500f)));
            ClassicAssert.AreEqual(0, world.TotalEvents);
        }

        [Test]
        public void Un_Chemin_A_UN_SEUL_Point_Ne_Boucle_Pas_A_L_Infini()
        {
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("path", "r"), ("speed", "6"), ("sight", "0")));
            map.Entities.Add(Waypoint(new Vector3(0f, 0f, -4f), "r", 0));

            var world = new AgentWorld();
            world.Load(map, Ground());

            Assert.DoesNotThrow(() => Run(world, 64 * 4, new Vector3(0f, 1.6f, 500f)));

            // Arrive, il tourne sur son unique point sans exploser le compteur.
            Assert.That(world.TotalEvents, Is.LessThan(64 * 4),
                        "un point unique a emis a chaque pas");
        }

        // ===================================================================
        //  Retour au poste
        // ===================================================================

        [Test]
        public void Sans_Ronde_Le_PNJ_Rentre_A_Son_Poste_Apres_La_Poursuite()
        {
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero,
                ("sight", "60"), ("fov", "360"), ("speed", "6"), ("behaviour", "guard")));

            var world = new AgentWorld();
            world.Load(map, Ground());

            // Poursuite prolongee : il s'eloigne de son poste.
            Run(world, 64 * 2, new Vector3(0f, 1.6f, -20f));
            Assert.That(world.Agents[0].Position.Z, Is.LessThan(-2f), "il n'a pas poursuivi");

            // Le joueur disparait : au-dela du delai, il doit rentrer.
            Run(world, 64 * 8, new Vector3(0f, 1.6f, 900f));

            Assert.That(world.Agents[0].Position.Length(), Is.LessThan(2f),
                        $"il est reste a {world.Agents[0].Position.Length():0.0} m de son poste");
        }

        // ===================================================================
        //  Cablage
        // ===================================================================

        [Test]
        public void Les_Sorties_Portent_Leur_Cible_Et_Leur_Entree()
        {
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero,
                ("sight", "40"), ("fov", "360"), ("behaviour", "guard"),
                ("target", "alarme"), ("input_spot", "Declencher")));

            var world = new AgentWorld();
            world.Load(map, Ground());

            // ⚠️ UN SEUL pas. Events decrit la DERNIERE mise a jour et se vide
            // a chaque appel : verifier apres deux pas trouve une liste vide,
            // ce qui se lit comme "rien ne s'est passe". Erreur commise en
            // ecrivant ce test - l'API est correcte, le test etait faux.
            Run(world, 1, new Vector3(0f, 1.6f, -8f));

            ClassicAssert.AreEqual(1, world.Events.Count);
            ClassicAssert.AreEqual(AgentWorld.OnSpotPlayer, world.Events[0].Output);
            ClassicAssert.AreEqual("alarme", world.Events[0].Target);
            ClassicAssert.AreEqual("Declencher", world.Events[0].Input);
        }

        [Test]
        public void Un_PNJ_Se_Retrouve_Par_Son_Nom()
        {
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("name", "garde_1")));
            map.Entities.Add(Npc(new Vector3(9f, 0f, 0f), ("name", "garde_2")));

            var world = new AgentWorld();
            world.Load(map, Ground());

            Assert.That(world.Find("garde_2"), Is.Not.Null);
            ClassicAssert.AreEqual(9f, world.Find("garde_2")!.Home.X, 1e-4f);
            Assert.That(world.Find("personne"), Is.Null);
        }

        // ===================================================================
        //  Determinisme et robustesse
        // ===================================================================

        [Test]
        public void Deux_Simulations_Identiques_Donnent_Le_Meme_Resultat()
        {
            // Le determinisme est la base de toute la methode de verification du
            // projet : sans lui, deux captures headless ne se comparent plus.
            static Vector3 Run20()
            {
                var map = new MapData();
                map.Entities.Add(Npc(Vector3.Zero, ("path", "r"), ("speed", "4"), ("sight", "0")));
                map.Entities.Add(Waypoint(new Vector3(7f, 0f, -7f), "r", 0));
                map.Entities.Add(Waypoint(new Vector3(-7f, 0f, -7f), "r", 1));

                var world = new AgentWorld();
                world.Load(map, Ground());

                for (int i = 0; i < 64 * 5; i++)
                    world.Update(Step, new Vector3(0f, 1.6f, 900f));

                return world.Agents[0].Position;
            }

            Vector3 a = Run20();
            Vector3 b = Run20();

            ClassicAssert.AreEqual(a.X, b.X, 0f);
            ClassicAssert.AreEqual(a.Y, b.Y, 0f);
            ClassicAssert.AreEqual(a.Z, b.Z, 0f);
        }

        [Test]
        public void Recharger_Une_Carte_Repart_De_Zero()
        {
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("sight", "40"), ("fov", "360"), ("behaviour", "guard")));

            var world = new AgentWorld();
            world.Load(map, Ground());
            Run(world, 4, new Vector3(0f, 1.6f, -8f));

            ClassicAssert.AreEqual(AgentState.Chase, world.Agents[0].State);

            world.Load(map, Ground());

            ClassicAssert.AreEqual(AgentState.Idle, world.Agents[0].State,
                                   "l'etat de poursuite a survecu au rechargement");
            Assert.That(world.Agents[0].SeesPlayer, Is.False);
        }

        [Test]
        public void Une_Carte_Sans_PNJ_Ne_Fait_Rien()
        {
            var map = new MapData();
            map.Entities.Add(new MapEntity(EntityTypes.PlayerSpawn, Vector3.Zero));

            var world = new AgentWorld();
            world.Load(map, Ground());

            ClassicAssert.AreEqual(0, world.Agents.Count);
            Assert.DoesNotThrow(() => Run(world, 50, Vector3.Zero));
        }

        [Test]
        public void Sans_Decor_Le_PNJ_Ne_Plante_Pas()
        {
            // Une carte peut n'avoir aucune geometrie pendant l'edition.
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("path", "r"), ("sight", "0")));
            map.Entities.Add(Waypoint(new Vector3(0f, 0f, -5f), "r", 0));

            var world = new AgentWorld();
            world.Load(map, null);

            Assert.DoesNotThrow(() => Run(world, 64, new Vector3(0f, 1.6f, 900f)));
        }

        [Test]
        public void Chaque_PNJ_A_Ses_Propres_Reglages_De_Vitesse()
        {
            var map = new MapData();
            map.Entities.Add(Npc(Vector3.Zero, ("path", "a"), ("speed", "1"), ("sight", "0")));
            map.Entities.Add(Npc(new Vector3(20f, 0f, 0f), ("path", "b"), ("speed", "5"), ("sight", "0")));
            map.Entities.Add(Waypoint(new Vector3(0f, 0f, -40f), "a", 0));
            map.Entities.Add(Waypoint(new Vector3(20f, 0f, -40f), "b", 0));

            var world = new AgentWorld();
            world.Load(map, Ground());

            Run(world, 64 * 2, new Vector3(0f, 1.6f, 900f));

            float slow = -world.Agents[0].Position.Z;
            float fast = -world.Agents[1].Position.Z;

            Assert.That(fast, Is.GreaterThan(slow * 2f),
                        $"le rapide a fait {fast:0.0} m, le lent {slow:0.0} m");
        }
    }
}
