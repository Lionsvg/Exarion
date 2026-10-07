// ===========================================================================
//  Tests de la physique dynamique.
//
//  Une physique de bac a sable doit tenir trois promesses, verifiees ici :
//    1. rien ne traverse le sol, jamais
//    2. les caisses s'empilent et RESTENT empilees (pas de tremblement)
//    3. tout finit par s'immobiliser, sinon le CPU brule pour rien
// ===========================================================================

using System.Numerics;
using NUnit.Framework;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class PhysicsWorldTests
    {
        private const float Dt = 1f / 64f;

        /// <summary>Sol plat de 100 x 100 m, surface a y = 0.</summary>
        private static PhysicsWorld MakeWorld()
        {
            var stat = new CollisionWorld();
            stat.Add(new Aabb(new Vector3(-50f, -1f, -50f), new Vector3(50f, 0f, 50f)));
            return new PhysicsWorld(stat);
        }

        private static void Run(PhysicsWorld w, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                w.Step(Dt);
        }

        // --- Chute et repos ---------------------------------------------------

        [Test]
        public void Une_Caisse_Tombe_Et_Se_Pose_Sur_Le_Sol()
        {
            var w = MakeWorld();
            var box = w.Spawn(new Vector3(0f, 5f, 0f), Vector3.One);

            Run(w, 200);

            // Caisse de 1 m : son centre repose a 0.5 m.
            ClassicAssert.AreEqual(0.5f, box.Position.Y, 0.05f);
            ClassicAssert.IsTrue(box.Grounded);
        }

        [Test]
        public void Une_Caisse_Ne_Traverse_Jamais_Le_Sol()
        {
            var w = MakeWorld();
            var box = w.Spawn(new Vector3(0f, 60f, 0f), Vector3.One);

            for (int i = 0; i < 400; i++)
            {
                w.Step(Dt);
                ClassicAssert.Greater(box.Position.Y, -0.2f, $"traverse le sol au tick {i}");
            }
        }

        [Test]
        public void Une_Caisse_Finit_Par_Sendormir()
        {
            var w = MakeWorld();
            var box = w.Spawn(new Vector3(0f, 3f, 0f), Vector3.One);

            Run(w, 300);

            ClassicAssert.IsTrue(box.Sleeping, "une caisse immobile doit cesser d'etre simulee");
            ClassicAssert.AreEqual(0f, box.Velocity.Length(), 1e-4f);
        }

        [Test]
        public void Une_Impulsion_Reveille_Une_Caisse_Endormie()
        {
            var w = MakeWorld();
            var box = w.Spawn(new Vector3(0f, 1f, 0f), Vector3.One);

            Run(w, 300);
            ClassicAssert.IsTrue(box.Sleeping);

            box.ApplyImpulse(new Vector3(100f, 0f, 0f));

            ClassicAssert.IsFalse(box.Sleeping);
            ClassicAssert.Greater(box.Velocity.X, 0f);
        }

        // --- Empilement ---------------------------------------------------------

        [Test]
        public void Deux_Caisses_Sempilent()
        {
            var w = MakeWorld();
            var bottom = w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One);
            var top = w.Spawn(new Vector3(0.05f, 4f, 0.05f), Vector3.One);

            Run(w, 400);

            ClassicAssert.AreEqual(0.5f, bottom.Position.Y, 0.08f, "celle du bas reste au sol");
            ClassicAssert.AreEqual(1.5f, top.Position.Y, 0.15f, "celle du haut repose dessus");
        }

        [Test]
        public void Une_Pile_Reste_Stable_Et_Ne_Tremble_Pas()
        {
            var w = MakeWorld();
            var boxes = new RigidBody[4];
            for (int i = 0; i < boxes.Length; i++)
                boxes[i] = w.Spawn(new Vector3(0f, 0.5f + i * 1.02f, 0f), Vector3.One);

            Run(w, 400);

            // Toutes doivent dormir : une pile qui vibre ne s'endort jamais.
            foreach (var b in boxes)
                ClassicAssert.IsTrue(b.Sleeping, $"la caisse {b.Id} tremble encore");

            // Et rester dans l'ordre, sans interpenetration.
            for (int i = 1; i < boxes.Length; i++)
            {
                ClassicAssert.Greater(boxes[i].Position.Y, boxes[i - 1].Position.Y + 0.85f,
                    "les caisses ne doivent pas s'enfoncer les unes dans les autres");
            }
        }

        [Test]
        public void Les_Caisses_Ne_Se_Chevauchent_Pas_Au_Repos()
        {
            var w = MakeWorld();

            // On les fait tomber au meme endroit : elles doivent se separer.
            for (int i = 0; i < 5; i++)
                w.Spawn(new Vector3(0f, 2f + i * 1.5f, 0f), Vector3.One);

            Run(w, 500);

            var bodies = w.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    Aabb a = bodies[i].GetBox();
                    Aabb b = bodies[j].GetBox();

                    if (!a.Intersects(b))
                        continue;

                    // Un chevauchement residuel est tolere, mais minuscule.
                    float py = System.MathF.Min(a.Max.Y, b.Max.Y) - System.MathF.Max(a.Min.Y, b.Min.Y);
                    ClassicAssert.Less(py, 0.05f, "chevauchement trop important");
                }
            }
        }

        // --- Frottement de contact ------------------------------------------------

        [Test]
        public void Une_Caisse_Posee_Sur_Une_Autre_Est_Entrainee_Par_Elle()
        {
            var w = MakeWorld();
            var bottom = w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One, mass: 20f);
            var top = w.Spawn(new Vector3(0f, 1.5f, 0f), Vector3.One, mass: 20f);

            Run(w, 120);   // laisser la pile se poser

            bottom.ApplyImpulse(new Vector3(400f, 0f, 0f));

            // On releve le PIC, pas la vitesse finale.
            //
            // Ce test lisait la vitesse 40 pas plus tard, et il a fini par
            // mentir : la caisse du bas file d'un metre en trois pas, le
            // contact est perdu, celle du dessus tombe au sol - et depuis R7
            // le frottement du DECOR l'y arrete net. La vitesse finale ne
            // disait donc plus rien de l'entrainement, seulement que le sol
            // freine. Elle passait avant grace a une fonctionnalite absente.
            float peak = 0f;
            for (int i = 0; i < 40; i++)
            {
                w.Step(Dt);
                peak = MathF.Max(peak, top.Velocity.X);
            }

            // Sans frottement de contact, celle du dessus resterait sur place.
            ClassicAssert.Greater(peak, 0.05f, "le frottement doit entrainer la caisse du dessus");
            ClassicAssert.Greater(top.Position.X, 0.05f, "et l'avoir reellement deplacee");
        }

        [Test]
        public void Le_Frottement_Ne_Peut_Pas_Inverser_Le_Mouvement()
        {
            // Un frottement mal borne accelere au lieu de freiner.
            var w = MakeWorld();
            var a = w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One, mass: 20f);
            var b = w.Spawn(new Vector3(0f, 1.5f, 0f), Vector3.One, mass: 20f);

            Run(w, 120);
            a.ApplyImpulse(new Vector3(300f, 0f, 0f));

            for (int i = 0; i < 200; i++)
            {
                w.Step(Dt);
                ClassicAssert.Less(MathF.Abs(b.Velocity.X), 20f, $"vitesse aberrante au tick {i}");
                ClassicAssert.Less(MathF.Abs(a.Velocity.X), 40f, $"vitesse aberrante au tick {i}");
            }
        }

        // --- Masse et impulsions ------------------------------------------------

        [Test]
        public void Une_Caisse_Legere_Part_Plus_Loin_Quune_Lourde()
        {
            var w = MakeWorld();
            var light = w.Spawn(new Vector3(-10f, 0.5f, 0f), Vector3.One, mass: 5f);
            var heavy = w.Spawn(new Vector3(10f, 0.5f, 0f), Vector3.One, mass: 200f);

            var impulse = new Vector3(0f, 0f, 300f);
            light.ApplyImpulse(impulse);
            heavy.ApplyImpulse(impulse);

            ClassicAssert.Greater(light.Velocity.Z, heavy.Velocity.Z * 5f,
                "a impulsion egale, la masse doit compter");
        }

        [Test]
        public void Un_Corps_Statique_Ne_Bouge_Pas()
        {
            var w = MakeWorld();
            var wall = w.Spawn(new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 2f));
            wall.IsStatic = true;

            wall.ApplyImpulse(new Vector3(9999f, 9999f, 9999f));
            Run(w, 100);

            ClassicAssert.AreEqual(1f, wall.Position.Y, 1e-4f);
            ClassicAssert.AreEqual(0f, wall.Velocity.Length(), 1e-4f);
        }

        [Test]
        public void Une_Caisse_Poussee_Contre_Une_Statique_Sarrete()
        {
            var w = MakeWorld();
            var wall = w.Spawn(new Vector3(5f, 1f, 0f), new Vector3(1f, 2f, 6f));
            wall.IsStatic = true;

            var box = w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One, mass: 10f);
            box.ApplyImpulse(new Vector3(400f, 0f, 0f));

            Run(w, 300);

            ClassicAssert.Less(box.Position.X, 5f, "la caisse ne doit pas traverser le bloc statique");
        }

        // --- Le joueur et les caisses -----------------------------------------------
        //
        //  Defaut trouve en JOUANT, pas par un test : impossible de monter sur
        //  une caisse, elle filait sous les pieds. Deux causes qui se
        //  cumulaient - le controleur ne voyait pas les caisses, et la poussee
        //  chassait celle sur laquelle on se tenait.

        /// <summary>Le decor plus les caisses : ce sur quoi le joueur bute.</summary>
        private static CollisionWorld Walkable(PhysicsWorld w)
        {
            var walkable = new CollisionWorld();

            foreach (Aabb box in w.Static.Boxes)
                walkable.Add(box);

            foreach (RigidBody b in w.Bodies)
                walkable.Add(b.GetObb().GetBounds());

            return walkable;
        }

        [Test]
        public void Le_Joueur_Tient_Debout_Sur_Une_Caisse()
        {
            var w = MakeWorld();
            w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One);

            var motor = new CharacterMotor { Position = new Vector3(0f, 3f, 0f) };

            for (int i = 0; i < 200; i++)
            {
                motor.Step(default, Dt, Walkable(w));
                w.Step(Dt);
            }

            ClassicAssert.AreEqual(1f, motor.Position.Y, 0.05f, "les pieds reposent sur le dessus de la caisse");
            ClassicAssert.IsTrue(motor.Grounded);
        }

        [Test]
        public void Sans_Les_Caisses_Le_Joueur_Les_Traverse()
        {
            // Le controle du test precedent : il ne doit pas passer pour la
            // mauvaise raison. Si on ne donne au controleur que le decor, il
            // tombe au sol comme si la caisse n'existait pas.
            var w = MakeWorld();
            w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One);

            var motor = new CharacterMotor { Position = new Vector3(0f, 3f, 0f) };

            for (int i = 0; i < 200; i++)
            {
                motor.Step(default, Dt, w.Static);
                w.Step(Dt);
            }

            ClassicAssert.AreEqual(0f, motor.Position.Y, 0.05f, "il traverse et atterrit par terre");
        }

        [Test]
        public void On_Ne_Pousse_Pas_La_Caisse_Sur_Laquelle_On_Marche()
        {
            var w = MakeWorld();
            var crate = w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One);

            Run(w, 60);   // la laisser se poser et s'endormir

            // Joueur debout sur le dessus de la caisse, avancant a 4 m/s.
            Aabb feet = Aabb.ForCharacter(new Vector3(0f, 1f, 0f), 0.8f, 1.83f);
            w.PushWith(feet, new Vector3(4f, 0f, 0f), strength: 3.5f);

            ClassicAssert.AreEqual(0f, crate.Velocity.X, 1e-4f, "le sol ne doit pas se derober");
        }

        [Test]
        public void On_Pousse_Bien_Une_Caisse_Qu_On_Bouscule()
        {
            // L'exception ci-dessus ne doit pas tuer la regle : une caisse
            // rencontree de face part toujours.
            var w = MakeWorld();
            var crate = w.Spawn(new Vector3(1f, 0.5f, 0f), Vector3.One);

            Run(w, 60);

            // Joueur au sol, juste derriere la caisse.
            Aabb body = Aabb.ForCharacter(new Vector3(0.1f, 0f, 0f), 0.8f, 1.83f);
            w.PushWith(body, new Vector3(4f, 0f, 0f), strength: 3.5f);

            ClassicAssert.Greater(crate.Velocity.X, 0.1f, "elle doit partir vers l'avant");
        }

        /// <summary>Un pas complet joueur + physique, comme dans Program.cs.</summary>
        private static void StepWithPlayer(PhysicsWorld w, CharacterMotor motor, in MoveInput input)
        {
            motor.Step(input, Dt, Walkable(w));

            // La vitesse VOULUE, pas la vitesse reelle : c'est tout l'objet
            // des deux tests ci-dessous.
            w.PushWith(motor.GetBox(), motor.WishVelocity, strength: 3.5f);
            w.Step(Dt);
        }

        /// <summary>Lacet qui fait avancer le joueur vers +X (0 regarde vers -Z).</summary>
        private const float YawTowardsPlusX = -MathF.PI / 2f;

        [Test]
        public void Un_Joueur_Qui_Pousse_De_Face_Deplace_La_Caisse()
        {
            // LE bug remonte a la main : coller la caisse de face ne la
            // bougeait pas. Le joueur etant BLOQUE par elle, sa vitesse reelle
            // valait zero - et la poussee se calculait dessus.
            var w = MakeWorld();
            var crate = w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One, mass: 20f);

            Run(w, 60);
            float start = crate.Position.X;

            var motor = new CharacterMotor { Position = new Vector3(-1.5f, 0f, 0f) };
            var input = new MoveInput { Move = new Vector2(0f, 1f), Yaw = YawTowardsPlusX };

            for (int i = 0; i < 120; i++)
                StepWithPlayer(w, motor, input);

            ClassicAssert.Greater(crate.Position.X - start, 0.3f, "elle doit avancer devant le joueur");
            ClassicAssert.Less(motor.Position.X, crate.Position.X, "et le joueur rester derriere elle");
        }

        [Test]
        public void Longer_Une_Caisse_Ne_La_Fait_Pas_Deraper()
        {
            // Le controle, et l'autre moitie du bug : la caisse se decalait
            // quand on passait sur le cote. On ne pousse desormais que la
            // composante dirigee VERS le corps.
            var w = MakeWorld();
            var crate = w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One, mass: 20f);

            Run(w, 60);
            float startZ = crate.Position.Z;

            // Joueur decale en Z, avancant parallelement a la caisse.
            var motor = new CharacterMotor { Position = new Vector3(-3f, 0f, 1.05f) };
            var input = new MoveInput { Move = new Vector2(0f, 1f), Yaw = YawTowardsPlusX };

            for (int i = 0; i < 120; i++)
                StepWithPlayer(w, motor, input);

            ClassicAssert.Less(MathF.Abs(crate.Position.Z - startZ), 0.2f,
                "passer a cote ne doit pas la chasser lateralement");
        }

        // --- Explosion ------------------------------------------------------------

        [Test]
        public void Une_Explosion_Ecarte_Les_Caisses()
        {
            var w = MakeWorld();
            var near = w.Spawn(new Vector3(2f, 0.5f, 0f), Vector3.One, mass: 10f);
            var far = w.Spawn(new Vector3(20f, 0.5f, 0f), Vector3.One, mass: 10f);

            w.Explode(Vector3.Zero, radius: 8f, force: 400f);

            ClassicAssert.Greater(near.Velocity.Length(), 1f, "la caisse proche doit decoller");
            ClassicAssert.AreEqual(0f, far.Velocity.Length(), 1e-4f, "celle hors de portee ne bouge pas");
            ClassicAssert.Greater(near.Velocity.X, 0f, "elle doit partir a l'oppose du centre");
            ClassicAssert.Greater(near.Velocity.Y, 0f, "et decoller du sol");
        }

        [Test]
        public void Lattenuation_Fait_Que_Le_Plus_Proche_Part_Le_Plus_Fort()
        {
            var w = MakeWorld();
            var close = w.Spawn(new Vector3(1f, 0.5f, 0f), Vector3.One, mass: 10f);
            var mid = w.Spawn(new Vector3(6f, 0.5f, 0f), Vector3.One, mass: 10f);

            w.Explode(Vector3.Zero, radius: 10f, force: 500f);

            ClassicAssert.Greater(close.Velocity.Length(), mid.Velocity.Length());
        }

        // --- Rayon ------------------------------------------------------------------

        [Test]
        public void Le_Rayon_Touche_La_Caisse_La_Plus_Proche()
        {
            var w = MakeWorld();
            var near = w.Spawn(new Vector3(0f, 0.5f, 5f), Vector3.One);
            w.Spawn(new Vector3(0f, 0.5f, 20f), Vector3.One);

            RigidBody? hit = w.Raycast(new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0f, 1f), 50f, out float d);

            ClassicAssert.AreSame(near, hit);
            ClassicAssert.AreEqual(4.5f, d, 0.05f, "la face avant est a 5 - 0.5");
        }

        [Test]
        public void Le_Rayon_Ne_Touche_Rien_Sil_Ny_A_Rien()
        {
            var w = MakeWorld();
            w.Spawn(new Vector3(30f, 0.5f, 0f), Vector3.One);

            RigidBody? hit = w.Raycast(new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0f, 1f), 50f, out _);

            ClassicAssert.IsNull(hit);
        }

        // --- Determinisme --------------------------------------------------------------

        [Test]
        public void Deux_Simulations_Identiques_Donnent_Le_Meme_Resultat()
        {
            PhysicsWorld Build()
            {
                var w = MakeWorld();
                for (int i = 0; i < 6; i++)
                    w.Spawn(new Vector3(i * 0.3f, 1f + i * 1.4f, i * 0.2f), Vector3.One, 10f + i);
                return w;
            }

            var a = Build();
            var b = Build();

            a.Bodies[0].ApplyImpulse(new Vector3(120f, 40f, 30f));
            b.Bodies[0].ApplyImpulse(new Vector3(120f, 40f, 30f));

            Run(a, 300);
            Run(b, 300);

            for (int i = 0; i < a.Bodies.Count; i++)
            {
                ClassicAssert.AreEqual(a.Bodies[i].Position.X, b.Bodies[i].Position.X, 1e-6f);
                ClassicAssert.AreEqual(a.Bodies[i].Position.Y, b.Bodies[i].Position.Y, 1e-6f);
                ClassicAssert.AreEqual(a.Bodies[i].Position.Z, b.Bodies[i].Position.Z, 1e-6f);
            }
        }

        [Test]
        public void Beaucoup_De_Caisses_Restent_Stables()
        {
            // Test de robustesse : 40 caisses lachees en vrac ne doivent ni
            // exploser, ni partir a l'infini, ni traverser le sol.
            var w = MakeWorld();

            for (int i = 0; i < 40; i++)
            {
                float x = (i % 5) * 1.1f - 2.2f;
                float z = (i / 5 % 4) * 1.1f - 1.65f;
                w.Spawn(new Vector3(x, 1f + i * 0.9f, z), Vector3.One);
            }

            Run(w, 600);

            foreach (var b in w.Bodies)
            {
                ClassicAssert.Greater(b.Position.Y, -0.5f, "aucune caisse ne doit passer sous le sol");
                ClassicAssert.Less(b.Position.Length(), 200f, "aucune caisse ne doit partir a l'infini");
                ClassicAssert.IsFalse(float.IsNaN(b.Position.X), "aucune valeur invalide");
            }
        }
    }
}
