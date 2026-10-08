// ===========================================================================
//  Tests du catalogue des proprietes (EntitySchema)
//
//  Le panneau de proprietes AFFICHE ces defauts, en gris, comme « ce que fait
//  le moteur quand la propriete est absente ». Un defaut affiche faux se lit
//  donc comme l'etat du moteur - et au 08/10, cinq l'etaient : la taille des
//  volumes (4 affiche, 3 lu), l'epaisseur des portes (0,2 contre 0,3), les
//  deux entrees des PNJ (Trigger/Untrigger contre Alert/Calm).
//
//  LA METHODE : pour chaque type, une entite SANS propriete et la meme avec
//  TOUS les defauts du catalogue ecrits en clair doivent donner exactement le
//  meme comportement. Aucune valeur n'est recopiee dans ce fichier : il
//  compare le moteur a lui-meme, a travers le catalogue. Un catalogue juste
//  est le seul qui passe.
//
//  ⚠️ CE QUE CA NE VOIT PAS : une cle que le code lit et que le catalogue
//  OUBLIE. Absente des deux cotes, elle ne change rien a la comparaison.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core.Assets;
using Exarion.Core.Editor;
using Exarion.Core.Entities;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class EntitySchemaTests
    {
        private const float Step = 1f / 64f;

        /// <summary>L'entite avec CHAQUE defaut du catalogue ecrit en clair.</summary>
        private static MapEntity WithDefaults(string type, Vector3 at)
        {
            var entity = new MapEntity(type, at);

            // Un defaut vide n'a rien a ecrire : absente et vide se lisent
            // pareil par Get, et Set("") n'aurait pas de sens dans un fichier.
            foreach (PropertySpec spec in EntitySchema.For(type))
                if (spec.Default.Length > 0)
                    entity.Set(spec.Key, spec.Default);

            return entity;
        }

        private static MapEntity Make(bool defaults, string type, Vector3 at)
            => defaults ? WithDefaults(type, at) : new MapEntity(type, at);

        private static CollisionWorld Ground()
        {
            var world = new CollisionWorld();
            world.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(100f, 1f, 100f));
            return world;
        }

        [Test]
        public void Chaque_Type_Connu_A_Son_Catalogue()
        {
            // Un type sans catalogue n'affiche RIEN dans le panneau tant qu'on
            // ne lui a pas ecrit de propriete : rien ne dit alors comment il se
            // regle. C'etait le reproche d'origine.
            foreach (string type in EntityTypes.All)
                ClassicAssert.IsTrue(EntitySchema.Knows(type), $"{type} n'a pas de catalogue");
        }

        // ===================================================================
        //  Portes
        // ===================================================================

        /// <summary>Ce que fait une porte : sa boite, sa course, et les sorties qu'elle emet.</summary>
        private static List<string> DoorBehaviour(bool defaults)
        {
            MapEntity door = Make(defaults, EntityTypes.Door, new Vector3(1f, 0f, 2f))
                .Set("name", "porte").Set("target_open", "ailleurs").Set("target_close", "ailleurs");

            var map = new MapData();
            map.Entities.Add(door);

            var entities = new EntityWorld();
            entities.Load(map);

            var movers = new MoverWorld();
            movers.Load(map);
            movers.RegisterInto(entities);

            Mover m = movers.Movers[0];
            var log = new List<string>
            {
                $"taille {m.Size} ferme {m.ClosedCenter} ouvert {m.OpenCenter}",
                $"vitesse {m.Speed} attente {m.Wait} etat {m.State} degeneree {m.Degenerate}",
            };

            void Record(EntityEvent e) => log.Add($"{e.Output} -> {e.Target}.{e.Input}");

            m.Receive(Mover.InputOpen);

            for (int i = 0; i < 64 * 10; i++)
            {
                movers.Step(Step);
                movers.DeliverOutputs(entities, Record);

                if (i == 64 * 5)
                    m.Receive(Mover.InputClose);
            }

            return log;
        }

        [Test]
        public void Les_Defauts_Affiches_D_Une_Porte_Sont_Ceux_Du_Moteur()
        {
            List<string> bare = DoorBehaviour(defaults: false);
            List<string> written = DoorBehaviour(defaults: true);

            TestContext.Out.WriteLine(string.Join(Environment.NewLine, bare));

            // Les deux fins de course doivent avoir ete emises : sinon la
            // comparaison des entrees ne prouverait rien.
            ClassicAssert.IsTrue(bare.Exists(l => l.StartsWith(Mover.OnFullyOpen)), "OnOuvert jamais emise");
            ClassicAssert.IsTrue(bare.Exists(l => l.StartsWith(Mover.OnFullyClosed)), "OnFerme jamais emise");

            Assert.That(written, Is.EqualTo(bare));
        }

        // ===================================================================
        //  Volumes
        // ===================================================================

        private static List<string> VolumeBehaviour(bool defaults, string type)
        {
            MapEntity volume = Make(defaults, type, new Vector3(0f, 1f, 0f)).Set("target", "ailleurs");

            var map = new MapData();
            map.Entities.Add(volume);

            var world = new EntityWorld();
            world.Load(map);

            var log = new List<string> { $"volume {EntityWorld.VolumeOf(volume).Min} {EntityWorld.VolumeOf(volume).Max}" };

            Aabb inside = Aabb.ForCharacter(new Vector3(0f, 0f, 0f), 0.8f, 1.8f);
            Aabb outside = Aabb.ForCharacter(new Vector3(30f, 0f, 0f), 0.8f, 1.8f);

            // Entrer, sortir, rentrer : « once » se juge a la deuxieme entree.
            foreach (Aabb player in new[] { inside, outside, inside, outside })
            {
                world.Update(player);

                foreach (EntityEvent e in world.Events)
                    log.Add($"{e.Output} -> {e.Target}.{e.Input}");
            }

            log.Add("checkpoints : " + string.Join(",", world.Checkpoints));
            return log;
        }

        [TestCase(EntityTypes.Trigger)]
        [TestCase(EntityTypes.Checkpoint)]
        public void Les_Defauts_Affiches_D_Un_Volume_Sont_Ceux_Du_Moteur(string type)
        {
            List<string> bare = VolumeBehaviour(false, type);
            List<string> written = VolumeBehaviour(true, type);

            TestContext.Out.WriteLine(string.Join(Environment.NewLine, bare));
            Assert.That(written, Is.EqualTo(bare));
        }

        // ===================================================================
        //  PNJ et points de passage
        // ===================================================================

        private static List<string> NpcBehaviour(bool defaults)
        {
            MapEntity npc = Make(defaults, EntityTypes.Npc, Vector3.Zero).Set("target", "ailleurs");

            var map = new MapData();
            map.Entities.Add(npc);
            map.Entities.Add(Make(defaults, EntityTypes.Waypoint, new Vector3(0f, 0f, -6f)));
            map.Entities.Add(Make(defaults, EntityTypes.Waypoint, new Vector3(6f, 0f, -6f)));

            var world = new AgentWorld();
            world.Load(map, Ground());

            Agent agent = world.Agents[0];
            var log = new List<string> { $"etat {agent.State} vitesse {agent.Motor.Settings.RunSpeed}" };

            // La portee et le champ de vision, sondes AUTOUR de leurs limites :
            // droit devant de 5 a 30 m, et a 10 m sous des angles de 40 a 70 deg.
            foreach (float d in new[] { 5f, 15f, 19.5f, 20.5f, 25f, 30f })
                log.Add($"voit a {d} m : {world.CanSee(agent, new Vector3(0f, 1.6f, -d))}");

            foreach (float deg in new[] { 40f, 50f, 54f, 56f, 60f, 70f })
            {
                float r = deg * MathF.PI / 180f;
                log.Add($"voit a {deg} deg : {world.CanSee(agent, new Vector3(-MathF.Sin(r) * 10f, 1.6f, -MathF.Cos(r) * 10f))}");
            }

            // Repere (le joueur droit devant), perdu (il disparait), puis la
            // ronde reprend et atteint ses points : les TROIS sorties passent.
            Vector3 seen = new(0f, 1.6f, -12f), gone = new(0f, -100f, 0f);

            for (int i = 0; i < 64 * 12; i++)
            {
                world.Update(Step, i < 64 ? seen : gone);

                foreach (EntityEvent e in world.Events)
                    log.Add($"{i} {e.Output} -> {e.Target}.{e.Input}");
            }

            log.Add($"fin en {agent.Position.X:0.000} {agent.Position.Z:0.000}");
            return log;
        }

        [Test]
        public void Les_Defauts_Affiches_D_Un_PNJ_Sont_Ceux_Du_Moteur()
        {
            List<string> bare = NpcBehaviour(defaults: false);
            List<string> written = NpcBehaviour(defaults: true);

            TestContext.Out.WriteLine(string.Join(Environment.NewLine, bare));

            foreach (string output in new[] { AgentWorld.OnSpotPlayer, AgentWorld.OnLosePlayer, AgentWorld.OnReachGoal })
                ClassicAssert.IsTrue(bare.Exists(l => l.Contains(output)), $"{output} jamais emise");

            Assert.That(written, Is.EqualTo(bare));
        }

        // ===================================================================
        //  Entites logiques
        // ===================================================================

        private static List<string> LogicBehaviour(bool defaults, string type)
        {
            MapEntity node = Make(defaults, type, Vector3.Zero).Set("name", "n").Set("target", "ailleurs");

            var map = new MapData();
            map.Entities.Add(node);

            var entities = new EntityWorld();
            entities.Load(map);

            var logic = new LogicWorld();
            logic.Load(map);
            logic.RegisterInto(entities);

            LogicNode n = logic.Nodes[0];
            var log = new List<string>
            {
                $"actif {n.Enabled} delai {n.Delay} unique {n.Once} intervalle {n.Interval} max {n.Max} depart {n.Start} valeur {n.Value}",
            };

            // Les entrees de chaque genre, envoyees a tous : un genre ignore
            // celles des autres (EntreeInconnue), ce qui se compare aussi.
            string[] inputs =
            {
                LogicNode.InputTrigger, LogicNode.InputAdd, LogicNode.InputAdd, LogicNode.InputAdd,
                LogicNode.InputTrigger, LogicNode.InputStop, LogicNode.InputStart, LogicNode.InputAdd,
            };

            for (int i = 0; i < 64 * 6; i++)
            {
                if (i % 40 == 0 && i / 40 < inputs.Length)
                    log.Add($"{i} {inputs[i / 40]} : {entities.Send("n", inputs[i / 40])}");

                logic.Step(Step, entities);

                foreach (EntityEvent e in logic.Events)
                    log.Add($"{i} {e.Output} -> {e.Target}.{e.Input}");
            }

            return log;
        }

        [TestCase(EntityTypes.LogicRelay)]
        [TestCase(EntityTypes.LogicTimer)]
        [TestCase(EntityTypes.LogicCounter)]
        public void Les_Defauts_Affiches_D_Une_Entite_Logique_Sont_Ceux_Du_Moteur(string type)
        {
            List<string> bare = LogicBehaviour(false, type);
            List<string> written = LogicBehaviour(true, type);

            TestContext.Out.WriteLine(string.Join(Environment.NewLine, bare));

            ClassicAssert.IsTrue(bare.Exists(l => l.Contains(" -> ailleurs.")), "aucune sortie : la comparaison ne prouverait rien");
            Assert.That(written, Is.EqualTo(bare));
        }

        // ===================================================================
        //  Ambiance : lumieres et sons commutables
        // ===================================================================

        private static string EnvBehaviour(bool defaults, string type)
        {
            var map = new MapData();
            map.Entities.Add(Make(defaults, type, new Vector3(1f, 2f, 3f)).Set("name", "n"));

            var env = new EnvWorld();
            env.Load(map);

            return type == EntityTypes.EnvLight
                ? $"{env.Lights[0].Color} {env.Lights[0].Intensity} {env.Lights[0].Range} {env.Lights[0].On}"
                : $"{env.Sounds[0].Hz} {env.Sounds[0].Volume} {env.Sounds[0].Playing}";
        }

        [TestCase(EntityTypes.EnvLight)]
        [TestCase(EntityTypes.EnvSound)]
        public void Les_Defauts_Affiches_D_Une_Entite_D_Ambiance_Sont_Ceux_Du_Moteur(string type)
        {
            Assert.That(EnvBehaviour(true, type), Is.EqualTo(EnvBehaviour(false, type)));
        }

        // ===================================================================
        //  Caisses
        // ===================================================================

        [Test]
        public void Les_Defauts_Affiches_D_Une_Pile_De_Caisses_Sont_Ceux_Du_Moteur()
        {
            List<CrateSpawn> Spawns(bool defaults)
            {
                var map = new MapData();
                map.Entities.Add(Make(defaults, EntityTypes.Crate, new Vector3(2f, 0f, 3f)));
                return new List<CrateSpawn>(CrateStacks.From(map));
            }

            List<CrateSpawn> bare = Spawns(false), written = Spawns(true);

            ClassicAssert.AreEqual(bare.Count, written.Count);

            for (int i = 0; i < bare.Count; i++)
            {
                ClassicAssert.AreEqual(bare[i].Center, written[i].Center);
                ClassicAssert.AreEqual(bare[i].Size, written[i].Size);
                ClassicAssert.AreEqual(bare[i].Mass, written[i].Mass);
            }
        }
    }
}
