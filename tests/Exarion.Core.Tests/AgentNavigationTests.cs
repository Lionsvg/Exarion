// ===========================================================================
//  Tests des PNJ qui CALCULENT leur chemin (E16)
//
//  ⚠️ CE SONT DES TESTS D'INTEGRATION, et c'est voulu. La grille a ses propres
//  tests (NavGridTests) ; ici, un VRAI CharacterMotor suit le chemin dans un
//  VRAI decor, pas a pas. C'est la lecon de l'ascenseur (E17) : 27 tests
//  verts sur MoverWorld, et un joueur qui ne montait pas, parce que personne
//  ne mettait les deux pieces ensemble.
//
//  Chaque test a sa contre-epreuve : la meme scene avec nav_enabled 0, ou le
//  PNJ doit echouer comme avant E16. Sans elle, un test qui passe pourrait ne
//  rien devoir au calcul de chemin.
// ===========================================================================

using System;
using System.Numerics;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core.Assets;
using Exarion.Core.Entities;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class AgentNavigationTests
    {
        private const float Step = 1f / 64f;

        private static CollisionWorld Ground()
        {
            var world = new CollisionWorld();
            world.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f));
            return world;
        }

        /// <summary>Joueur hors de vue et immobile, tres loin sous le sol : il ne derange rien.</summary>
        private static readonly Vector3 NobodyEye = new(0f, -100f, 0f);

        private static int RunUntilEvent(AgentWorld world, Vector3 playerEye, string output, float seconds)
        {
            int steps = (int)(seconds / Step);

            for (int i = 0; i < steps; i++)
            {
                world.Update(Step, playerEye);

                foreach (EntityEvent e in world.Events)
                    if (e.Output == output)
                        return i;
            }

            return -1;
        }

        /// <summary>
        /// Une ronde de deux points separes par un mur de 20 m. En ligne droite
        /// le garde bute dans le mur et n'arrive jamais ; avec le calcul de
        /// chemin, il le contourne et atteint le point.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void Un_Garde_Contourne_Le_Mur_Qui_Coupe_Sa_Ronde(bool navigation)
        {
            var level = Ground();
            level.AddBox(new Vector3(0f, 1.5f, 0f), new Vector3(20f, 3f, 0.5f));

            var map = new MapData();
            map.Entities.Add(new MapEntity(EntityTypes.Npc, new Vector3(0f, 0f, 6f))
                .Set("path", "ronde").Set("speed", "3").Set("sight", "0"));
            map.Entities.Add(new MapEntity(EntityTypes.Waypoint, new Vector3(0f, 0f, -6f)).Set("path", "ronde").Set("order", "0"));
            map.Entities.Add(new MapEntity(EntityTypes.Waypoint, new Vector3(0f, 0f, 6f)).Set("path", "ronde").Set("order", "1"));

            var world = new AgentWorld { NavigationEnabled = navigation };
            world.Load(map, level);

            int reached = RunUntilEvent(world, NobodyEye, AgentWorld.OnReachGoal, 20f);

            if (navigation)
            {
                ClassicAssert.GreaterOrEqual(reached, 0, "avec le calcul de chemin, le garde doit atteindre l'autre cote");
                TestContext.Out.WriteLine($"point atteint en {reached * Step:0.0} s");
            }
            else
            {
                // La contre-epreuve : sans lui, le garde reste colle au mur.
                ClassicAssert.Less(reached, 0, "en ligne droite, le mur aurait du l'arreter");
            }
        }

        /// <summary>
        /// ⚠️ LE CAS DU 17/08 : le joueur sur une estrade de 1,80 m. Le garde le
        /// voit, le poursuit, et s'arretait net contre la paroi. Avec un escalier
        /// sur le flanc, il doit monter le rejoindre.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void La_Poursuite_Monte_Sur_L_Estrade_Par_L_Escalier(bool navigation)
        {
            var level = Ground();
            level.AddBox(new Vector3(0f, 0.9f, -6f), new Vector3(6f, 1.8f, 6f));   // estrade, dessus a 1,80 m

            for (int i = 0; i < 6; i++)
            {
                float h = 0.3f * (i + 1);
                level.AddBox(new Vector3(4.5f, h * 0.5f, 3f - i * 1.2f), new Vector3(3f, h, 1.2f));
            }

            var map = new MapData();
            map.Entities.Add(new MapEntity(EntityTypes.Npc, new Vector3(0f, 0f, 8f), yaw: 0f)
                .Set("behaviour", "idle").Set("speed", "3").Set("sight", "40").Set("fov", "360"));

            var world = new AgentWorld { NavigationEnabled = navigation };
            world.Load(map, level);

            // Le joueur se tient sur l'estrade : pieds a 1,80 m, yeux a 3,45 m.
            Vector3 playerEye = new(-1f, 1.8f + 1.65f, -7f);

            for (int i = 0; i < (int)(15f / Step); i++)
                world.Update(Step, playerEye);

            Agent garde = world.Agents[0];
            float flat = new Vector2(garde.Position.X - playerEye.X, garde.Position.Z - playerEye.Z).Length();

            TestContext.Out.WriteLine($"garde en {garde.Position}, a {flat:0.0} m du joueur, etat {garde.State}");

            if (navigation)
            {
                ClassicAssert.AreEqual(1.8f, garde.Position.Y, 0.1f, "le garde doit etre MONTE sur l'estrade");
                ClassicAssert.Less(flat, 2f, "et avoir rejoint le joueur");
            }
            else
            {
                ClassicAssert.Less(garde.Position.Y, 0.5f, "en ligne droite, il reste au pied de l'estrade");
            }
        }

        /// <summary>
        /// Une cible hors d'atteinte : estrade SANS escalier. Le garde vient au
        /// pied de l'estrade, au plus pres, et y ATTEND tourne vers le joueur -
        /// il ne pousse pas contre la paroi. La contre-epreuve, elle, pousse :
        /// c'est la que la vitesse VOULUE les distingue, la vitesse reelle
        /// etant nulle dans les deux cas (un mur bloque aussi bien qu'un arret).
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void Hors_D_Atteinte_Le_Garde_S_Approche_Puis_Attend_Au_Lieu_De_Pousser(bool navigation)
        {
            var level = Ground();
            level.AddBox(new Vector3(0f, 0.9f, -6f), new Vector3(6f, 1.8f, 6f));

            var map = new MapData();
            map.Entities.Add(new MapEntity(EntityTypes.Npc, new Vector3(0f, 0f, 8f))
                .Set("behaviour", "idle").Set("speed", "3").Set("sight", "40").Set("fov", "360"));

            var world = new AgentWorld { NavigationEnabled = navigation };
            world.Load(map, level);

            Vector3 playerEye = new(0f, 1.8f + 1.65f, -6f);

            for (int i = 0; i < (int)(6f / Step); i++)
                world.Update(Step, playerEye);

            Agent garde = world.Agents[0];
            Vector3 p = garde.Position;
            float dx = MathF.Max(0f, MathF.Abs(p.X) - 3f);
            float dz = MathF.Max(0f, MathF.Max(-9f - p.Z, p.Z + 3f));
            float fromPlatform = MathF.Sqrt(dx * dx + dz * dz);
            float wish = garde.Motor.WishVelocity.Length();

            TestContext.Out.WriteLine($"garde en {p}, a {fromPlatform:0.00} m de l'estrade, vitesse voulue {wish:0.00} m/s, " +
                                  $"mode {garde.NavMode}, {world.Navigation!.Searches} recherches en 6 s");

            ClassicAssert.AreEqual(AgentState.Chase, garde.State);
            ClassicAssert.Less(fromPlatform, 1.2f, "il s'est approche jusqu'au pied de l'estrade");

            if (navigation)
            {
                ClassicAssert.IsTrue(garde.NoPath, "la cible est sur une estrade sans acces");
                ClassicAssert.Less(wish, 0.05f, "il attend, il ne pousse pas");

                // ⚠️ Le defaut de la premiere version : un A* a CHAQUE pas tant
                // que la cible restait inaccessible. 6 s a 0,5 s d'intervalle
                // font 12 recalculs ; a chaque pas, ce serait 384.
                ClassicAssert.LessOrEqual(world.Navigation.Searches, 6f / AgentWorld.ReplanSeconds + 2);

                // Il regarde vers le joueur : lacet 0 = vers -Z, direction avant (-sin, -cos).
                Vector2 forward = new(-MathF.Sin(garde.Yaw), -MathF.Cos(garde.Yaw));
                Vector2 toPlayer = Vector2.Normalize(new Vector2(playerEye.X - p.X, playerEye.Z - p.Z));
                ClassicAssert.Greater(Vector2.Dot(forward, toPlayer), 0.95f, "tourne vers le joueur");
            }
            else
            {
                ClassicAssert.Greater(wish, 1f, "en ligne droite, il pousse contre la paroi");
            }
        }

        /// <summary>
        /// ⚠️ Repere en plein contournement, le garde doit lacher sa ronde AU
        /// PAS MEME. Sans remise a zero du chemin au changement d'etat, il
        /// suivait encore jusqu'a une demi-seconde le chemin vers son point de
        /// ronde - en tournant le dos au joueur qu'il venait de voir.
        /// </summary>
        [Test]
        public void Un_Garde_Qui_Repere_Le_Joueur_Lache_Sa_Ronde_Aussitot()
        {
            var level = Ground();
            level.AddBox(new Vector3(0f, 1.5f, 0f), new Vector3(20f, 3f, 0.5f));

            var map = new MapData();
            map.Entities.Add(new MapEntity(EntityTypes.Npc, new Vector3(0f, 0f, 6f))
                .Set("path", "ronde").Set("speed", "3").Set("sight", "40").Set("fov", "360"));
            map.Entities.Add(new MapEntity(EntityTypes.Waypoint, new Vector3(0f, 0f, -6f)).Set("path", "ronde").Set("order", "0"));
            map.Entities.Add(new MapEntity(EntityTypes.Waypoint, new Vector3(0f, 0f, 6f)).Set("path", "ronde").Set("order", "1"));

            var world = new AgentWorld();
            world.Load(map, level);
            Agent garde = world.Agents[0];

            // Dix pas de ronde, joueur invisible : le garde part contourner le mur.
            for (int i = 0; i < 10; i++)
                world.Update(Step, NobodyEye);

            ClassicAssert.AreEqual(AgentNavMode.Path, garde.NavMode, "le garde contourne le mur");

            // Le joueur apparait DERRIERE lui, en terrain degage.
            Vector3 playerEye = new(0f, 1.65f, 14f);

            for (int i = 0; i < 6; i++)
                world.Update(Step, playerEye);

            Vector2 forward = new(-MathF.Sin(garde.Yaw), -MathF.Cos(garde.Yaw));
            Vector2 toPlayer = Vector2.Normalize(new Vector2(playerEye.X - garde.Position.X, playerEye.Z - garde.Position.Z));

            ClassicAssert.AreEqual(AgentState.Chase, garde.State);
            ClassicAssert.Greater(Vector2.Dot(forward, toPlayer), 0.9f, "six pas apres l'avoir vu, il doit se diriger vers le joueur");
        }

        /// <summary>
        /// ⚠️ Un point de ronde sur une PASSERELLE, juste au-dessus du garde.
        /// L'arrivee ne regardait que la distance au sol : le point etait
        /// « atteint » des le depart, garde au sol, et la ronde ne montait
        /// jamais. Il doit maintenant prendre l'escalier.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void Un_Point_De_Ronde_Sur_Une_Passerelle_Se_Rejoint_Par_L_Escalier(bool navigation)
        {
            var level = Ground();
            level.AddBox(new Vector3(0f, 2.8f, 0f), new Vector3(3f, 0.4f, 12f));   // passerelle : dessous 2,60 m, dessus 3 m

            // Dix marches de 30 cm au bout sud de la passerelle, en descendant vers le sud.
            for (int i = 0; i < 10; i++)
            {
                float h = 3f - 0.3f * i;
                level.AddBox(new Vector3(0f, h * 0.5f, 6.5f + i), new Vector3(3f, h, 1f));
            }

            var map = new MapData();
            map.Entities.Add(new MapEntity(EntityTypes.Npc, new Vector3(0f, 0f, -3f))
                .Set("path", "haut").Set("speed", "3").Set("sight", "0"));
            map.Entities.Add(new MapEntity(EntityTypes.Waypoint, new Vector3(0f, 3f, -3f)).Set("path", "haut").Set("order", "0"));
            map.Entities.Add(new MapEntity(EntityTypes.Waypoint, new Vector3(6f, 0f, -3f)).Set("path", "haut").Set("order", "1"));

            var world = new AgentWorld { NavigationEnabled = navigation };
            world.Load(map, level);

            int reached = RunUntilEvent(world, NobodyEye, AgentWorld.OnReachGoal, 30f);
            Agent garde = world.Agents[0];

            TestContext.Out.WriteLine($"premier point « atteint » apres {reached * Step:0.0} s, garde en {garde.Position}");
            ClassicAssert.GreaterOrEqual(reached, 0);

            if (navigation)
                ClassicAssert.AreEqual(3f, garde.Position.Y, 0.1f, "le garde doit etre MONTE sur la passerelle");
            else
                ClassicAssert.Less(garde.Position.Y, 0.5f, "sans calcul de chemin, l'arrivee d'avant E16 : au sol, dessous");
        }
    }
}
