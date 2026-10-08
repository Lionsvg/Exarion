// ===========================================================================
//  Tests des entites d'ambiance : une lumiere qui s'allume, un son qui joue,
//  et un PNJ qu'on reveille.
//
//  Ici, SEULEMENT L'ETAT : ce qui s'affiche et ce qui s'entend se verifie
//  par capture et par enregistrement WAV. Mais l'etat est la moitie qui se
//  casse en silence - une entree mal orthographiee, une version qui ne bouge
//  pas, et le jeu n'a aucune raison de redessiner quoi que ce soit.
// ===========================================================================

using System.Numerics;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core.Assets;
using Exarion.Core.Entities;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class EnvWorldTests
    {
        private const float Dt = 1f / 64f;

        private static (EntityWorld Entities, EnvWorld Env) Load(params MapEntity[] entities)
        {
            var map = new MapData();
            map.Entities.AddRange(entities);

            var world = new EntityWorld();
            world.Load(map);

            var env = new EnvWorld();
            env.Load(map);
            env.RegisterInto(world);

            return (world, env);
        }

        // ===================================================================
        //  Lumieres
        // ===================================================================

        [Test]
        public void Une_Lumiere_S_Allume_Et_S_Eteint()
        {
            (EntityWorld world, EnvWorld env) = Load(
                new MapEntity(EntityTypes.EnvLight, new Vector3(1f, 3f, 2f)).Set("name", "lampe").Set("start_off", "1"));

            EnvLight lamp = env.Lights[0];
            ClassicAssert.IsFalse(lamp.On);

            ClassicAssert.AreEqual(EntityDelivery.Delivree, world.Send("lampe", EnvLight.InputOn));
            ClassicAssert.IsTrue(lamp.On);

            world.Send("lampe", EnvLight.InputToggle);
            ClassicAssert.IsFalse(lamp.On);

            ClassicAssert.AreEqual(EntityDelivery.EntreeInconnue, world.Send("lampe", "Allume"),
                                   "une faute de frappe doit se VOIR, pas allumer quand meme");
        }

        /// <summary>
        /// ⚠️ La version est ce qui fait redessiner l'eclairage. Elle doit
        /// bouger a chaque CHANGEMENT - et seulement la : un « Allumer » recu a
        /// chaque passage sur une lampe deja allumee reconstruirait l'eclairage
        /// 64 fois par seconde pour rien.
        /// </summary>
        [Test]
        public void La_Version_Bouge_Au_Changement_Et_Seulement_La()
        {
            (EntityWorld world, EnvWorld env) = Load(
                new MapEntity(EntityTypes.EnvLight, Vector3.Zero).Set("name", "lampe"));

            int v0 = env.Version;

            world.Send("lampe", EnvLight.InputOn);   // deja allumee
            ClassicAssert.AreEqual(v0, env.Version, "rien n'a change");

            world.Send("lampe", EnvLight.InputOff);
            ClassicAssert.AreEqual(v0 + 1, env.Version);

            world.Send("lampe", EnvLight.InputOff);
            ClassicAssert.AreEqual(v0 + 1, env.Version);
        }

        [TestCase("0.2,0.4,1", 0.2f, 0.4f, 1f)]
        [TestCase(" 2 , 1 , 0.5 ", 2f, 1f, 0.5f)]
        public void La_Couleur_Se_Lit_En_Virgules(string text, float r, float g, float b)
        {
            Vector3 c = EnvWorld.ParseColor(text, Vector3.One);

            ClassicAssert.AreEqual(r, c.X, 1e-5f);
            ClassicAssert.AreEqual(g, c.Y, 1e-5f);
            ClassicAssert.AreEqual(b, c.Z, 1e-5f);
        }

        /// <summary>
        /// Une couleur illisible retombe sur le DEFAUT, jamais sur du noir : une
        /// lampe noire, c'est une lampe qu'on croit cassee.
        /// </summary>
        [TestCase("")]
        [TestCase("rouge")]
        [TestCase("1,2")]
        [TestCase("1,2,3,4")]
        [TestCase("1,-2,3")]
        [TestCase("1,NaN,3")]
        public void Une_Couleur_Illisible_Retombe_Sur_Le_Defaut(string text)
        {
            Assert.That(EnvWorld.ParseColor(text, EnvLight.DefaultColor), Is.EqualTo(EnvLight.DefaultColor));
        }

        // ===================================================================
        //  Sons
        // ===================================================================

        [Test]
        public void Un_Son_Joue_Et_Se_Tait()
        {
            (EntityWorld world, EnvWorld env) = Load(
                new MapEntity(EntityTypes.EnvSound, Vector3.Zero).Set("name", "sirene").Set("hz", "880"));

            EnvSound s = env.Sounds[0];

            ClassicAssert.IsFalse(s.Playing, "un son se tait par defaut : une carte qui hurle au chargement surprend");
            ClassicAssert.AreEqual(880f, s.Hz);

            world.Send("sirene", EnvSound.InputPlay);
            ClassicAssert.IsTrue(s.Playing);

            world.Send("sirene", EnvSound.InputStop);
            ClassicAssert.IsFalse(s.Playing);
        }

        [Test]
        public void La_Frequence_Et_Le_Volume_Sont_Bornes()
        {
            (_, EnvWorld env) = Load(
                new MapEntity(EntityTypes.EnvSound, Vector3.Zero).Set("hz", "5").Set("volume", "3"),
                new MapEntity(EntityTypes.EnvSound, Vector3.Zero).Set("hz", "90000").Set("volume", "-1"));

            ClassicAssert.AreEqual(20f, env.Sounds[0].Hz);
            ClassicAssert.AreEqual(1f, env.Sounds[0].Volume);
            ClassicAssert.AreEqual(12000f, env.Sounds[1].Hz);
            ClassicAssert.AreEqual(0f, env.Sounds[1].Volume);
        }

        // ===================================================================
        //  Bout en bout : un volume allume la lampe et declenche le son
        // ===================================================================

        [Test]
        public void Un_Volume_Allume_Une_Lampe_Par_Un_Relais()
        {
            var map = new MapData();
            map.Entities.Add(new MapEntity(EntityTypes.Trigger, new Vector3(0f, 1f, 0f))
                .Set("size", "3").Set("target", "relais").Set("input", LogicNode.InputTrigger));
            map.Entities.Add(new MapEntity(EntityTypes.LogicRelay, Vector3.Zero)
                .Set("name", "relais").Set("target", "lampe").Set("input", EnvLight.InputToggle));
            map.Entities.Add(new MapEntity(EntityTypes.EnvLight, new Vector3(0f, 3f, 0f))
                .Set("name", "lampe").Set("start_off", "1"));

            var entities = new EntityWorld();
            entities.Load(map);

            var logic = new LogicWorld();
            logic.Load(map);
            logic.RegisterInto(entities);

            var env = new EnvWorld();
            env.Load(map);
            env.RegisterInto(entities);

            entities.Update(Aabb.ForCharacter(Vector3.Zero, 0.8f, 1.8f));
            ClassicAssert.IsFalse(env.Lights[0].On, "le relais part au pas suivant");

            logic.Step(Dt, entities);
            ClassicAssert.IsTrue(env.Lights[0].On);
        }

        // ===================================================================
        //  Reveiller un PNJ
        // ===================================================================

        [Test]
        public void Un_PNJ_Endormi_Se_Reveille_Et_Part_En_Ronde()
        {
            var level = new CollisionWorld();
            level.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));

            var map = new MapData();
            map.Entities.Add(new MapEntity(EntityTypes.Npc, Vector3.Zero)
                .Set("name", "garde").Set("behaviour", "idle").Set("path", "ronde").Set("sight", "0"));
            map.Entities.Add(new MapEntity(EntityTypes.Waypoint, new Vector3(0f, 0f, -8f)).Set("path", "ronde"));

            var entities = new EntityWorld();
            entities.Load(map);

            var agents = new AgentWorld();
            agents.Load(map, level);
            agents.RegisterInto(entities);

            Vector3 nobody = new(0f, -100f, 0f);

            for (int i = 0; i < 64; i++)
                agents.Update(Dt, nobody, entities);

            ClassicAssert.AreEqual(AgentState.Idle, agents.Agents[0].State);
            ClassicAssert.Less(agents.Agents[0].Position.Length(), 0.01f, "endormi, il ne bouge pas");

            ClassicAssert.AreEqual(EntityDelivery.Delivree, entities.Send("garde", AgentWorld.InputPatrol));

            for (int i = 0; i < 64; i++)
                agents.Update(Dt, nobody, entities);

            ClassicAssert.AreEqual(AgentState.Patrol, agents.Agents[0].State);
            ClassicAssert.Less(agents.Agents[0].Position.Z, -1f, "il est parti vers son point de ronde");

            entities.Send("garde", AgentWorld.InputStop);
            Vector3 stopped = agents.Agents[0].Position;

            for (int i = 0; i < 64; i++)
                agents.Update(Dt, nobody, entities);

            ClassicAssert.AreEqual(AgentState.Idle, agents.Agents[0].State);
            ClassicAssert.Less(Vector3.Distance(stopped, agents.Agents[0].Position), 0.5f, "arrete, il s'immobilise");
        }

        [Test]
        public void Un_PNJ_Sans_Ronde_Ne_Part_Pas_En_Ronde()
        {
            // « Patrouiller » sans chemin ne doit pas le laisser dans un etat
            // Patrol sans cible : il resterait la, en ronde, immobile - un etat
            // que le HUD afficherait comme une ronde qui marche.
            var map = new MapData();
            map.Entities.Add(new MapEntity(EntityTypes.Npc, Vector3.Zero).Set("name", "garde").Set("behaviour", "idle"));

            var entities = new EntityWorld();
            entities.Load(map);

            var agents = new AgentWorld();
            agents.Load(map, null);
            agents.RegisterInto(entities);

            entities.Send("garde", AgentWorld.InputPatrol);

            ClassicAssert.AreEqual(AgentState.Idle, agents.Agents[0].State);
        }
    }
}
