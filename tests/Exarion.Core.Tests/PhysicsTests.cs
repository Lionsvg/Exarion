// ===========================================================================
//  Tests de collision et de deplacement.
//
//  Tout se joue sans fenetre : on peut verifier qu'on ne traverse pas les
//  murs, que la gravite pose bien le joueur au sol et que le saut monte a
//  la bonne hauteur, en quelques millisecondes.
// ===========================================================================

using System.Numerics;
using NUnit.Framework;
using Exarion.Core;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class AabbTests
    {
        [Test]
        public void Les_Bornes_Inversees_Sont_Remises_A_Lendroit()
        {
            var b = new Aabb(new Vector3(5f, 5f, 5f), new Vector3(-1f, -1f, -1f));

            ClassicAssert.AreEqual(-1f, b.Min.X, 1e-5f);
            ClassicAssert.AreEqual(5f, b.Max.X, 1e-5f);
        }

        [Test]
        public void Centre_Et_Taille_Sont_Coherents()
        {
            var b = Aabb.FromCenterSize(new Vector3(2f, 3f, 4f), new Vector3(2f, 4f, 6f));

            ClassicAssert.AreEqual(2f, b.Center.X, 1e-5f);
            ClassicAssert.AreEqual(4f, b.Size.Y, 1e-5f);
            ClassicAssert.AreEqual(1f, b.Extents.X, 1e-5f);
        }

        [Test]
        public void La_Boite_De_Personnage_Part_Des_Pieds()
        {
            var b = Aabb.ForCharacter(new Vector3(0f, 0f, 0f), width: 0.8f, height: 1.8f);

            ClassicAssert.AreEqual(0f, b.Min.Y, 1e-5f, "les pieds sont au sol");
            ClassicAssert.AreEqual(1.8f, b.Max.Y, 1e-5f, "la tete est a hauteur");
            ClassicAssert.AreEqual(-0.4f, b.Min.X, 1e-5f);
        }

        [Test]
        public void Le_Chevauchement_Est_Detecte()
        {
            var a = Aabb.FromCenterSize(Vector3.Zero, new Vector3(2f, 2f, 2f));
            var touching = Aabb.FromCenterSize(new Vector3(1.5f, 0f, 0f), new Vector3(2f, 2f, 2f));
            var apart = Aabb.FromCenterSize(new Vector3(3f, 0f, 0f), new Vector3(2f, 2f, 2f));

            ClassicAssert.IsTrue(a.Intersects(touching));
            ClassicAssert.IsFalse(a.Intersects(apart));
        }
    }

    public class CollisionWorldTests
    {
        private static Aabb Unit(Vector3 center) => Aabb.FromCenterSize(center, Vector3.One);

        [Test]
        public void Un_Trajet_Libre_Ne_Touche_Rien()
        {
            var w = new CollisionWorld();
            w.AddBox(new Vector3(10f, 0f, 0f), Vector3.One);

            SweepHit hit = w.Sweep(Unit(Vector3.Zero), new Vector3(1f, 0f, 0f));

            ClassicAssert.IsFalse(hit.Hit);
            ClassicAssert.AreEqual(1f, hit.Time, 1e-5f);
        }

        [Test]
        public void Un_Mur_Est_Touche_Au_Bon_Moment()
        {
            var w = new CollisionWorld();
            // Mur dont la face gauche est en x = 4.
            w.Add(new Aabb(new Vector3(4f, -5f, -5f), new Vector3(6f, 5f, 5f)));

            // Cube unitaire centre en 0 : son bord droit est en x = 0.5.
            // Il touchera donc apres 3.5 m de trajet.
            SweepHit hit = w.Sweep(Unit(Vector3.Zero), new Vector3(7f, 0f, 0f));

            ClassicAssert.IsTrue(hit.Hit);
            ClassicAssert.AreEqual(0.5f, hit.Time, 1e-4f, "3.5 m parcourus sur 7 m demandes");
            ClassicAssert.AreEqual(-1f, hit.Normal.X, 1e-4f, "la normale s'oppose au mouvement");
        }

        [Test]
        public void Le_Sol_Est_Touche_En_Tombant()
        {
            var w = new CollisionWorld();
            w.Add(new Aabb(new Vector3(-10f, -1f, -10f), new Vector3(10f, 0f, 10f)));

            SweepHit hit = w.Sweep(Unit(new Vector3(0f, 5f, 0f)), new Vector3(0f, -10f, 0f));

            ClassicAssert.IsTrue(hit.Hit);
            ClassicAssert.AreEqual(1f, hit.Normal.Y, 1e-4f, "la normale du sol pointe vers le haut");
        }

        [Test]
        public void Un_Trajet_Parallele_A_Cote_Ne_Touche_Pas()
        {
            var w = new CollisionWorld();
            w.Add(new Aabb(new Vector3(-10f, 10f, -10f), new Vector3(10f, 11f, 10f)));   // plafond haut

            SweepHit hit = w.Sweep(Unit(Vector3.Zero), new Vector3(20f, 0f, 0f));

            ClassicAssert.IsFalse(hit.Hit, "on passe largement sous le plafond");
        }

        [Test]
        public void Le_Glissement_Longe_Le_Mur_Au_Lieu_De_Sarreter()
        {
            var w = new CollisionWorld();
            // Mur perpendiculaire a X, en x = 2.
            w.Add(new Aabb(new Vector3(2f, -5f, -20f), new Vector3(4f, 5f, 20f)));

            // On pousse en diagonale : la composante X doit etre bloquee,
            // la composante Z doit passer.
            Vector3 moved = w.MoveAndSlide(Unit(Vector3.Zero), new Vector3(5f, 0f, 5f), out Vector3 normal);

            ClassicAssert.Less(moved.X, 1.6f, "la composante vers le mur est bloquee");
            ClassicAssert.Greater(moved.Z, 4.5f, "la composante le long du mur passe");
            ClassicAssert.AreEqual(-1f, normal.X, 1e-4f);
        }

        [Test]
        public void On_Ne_Reste_Pas_Coince_Si_On_Demarre_Dans_Un_Obstacle()
        {
            var w = new CollisionWorld();
            w.AddBox(Vector3.Zero, new Vector3(4f, 4f, 4f));

            // Le mobile est entierement a l'interieur : il doit pouvoir sortir.
            Vector3 moved = w.MoveAndSlide(Unit(Vector3.Zero), new Vector3(10f, 0f, 0f), out _);

            ClassicAssert.Greater(moved.X, 0f, "un chevauchement ne doit pas figer le joueur");
        }
    }

    public class CharacterMotorTests
    {
        /// <summary>
        /// Piece fermee, plafond a 6 m, sol a y = 0.
        /// Par defaut 200 x 200 m : un joueur qui court 3 s parcourt 14 m, il
        /// ne faut pas qu'il atteigne un mur pendant les tests de vitesse.
        /// </summary>
        private static CollisionWorld MakeRoom(float half = 100f)
        {
            var w = new CollisionWorld();
            w.Add(new Aabb(new Vector3(-half, -1f, -half), new Vector3(half, 0f, half)));      // sol
            w.Add(new Aabb(new Vector3(-half, 6f, -half), new Vector3(half, 7f, half)));       // plafond
            w.Add(new Aabb(new Vector3(-half - 1f, 0f, -half), new Vector3(-half, 6f, half))); // mur -X
            w.Add(new Aabb(new Vector3(half, 0f, -half), new Vector3(half + 1f, 6f, half)));   // mur +X
            w.Add(new Aabb(new Vector3(-half, 0f, -half - 1f), new Vector3(half, 6f, -half))); // mur -Z
            w.Add(new Aabb(new Vector3(-half, 0f, half), new Vector3(half, 6f, half + 1f)));   // mur +Z
            return w;
        }

        private const float Dt = 1f / 64f;

        private static void Simulate(CharacterMotor m, CollisionWorld w, MoveInput input, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                m.Step(input, Dt, w);
        }

        [Test]
        public void Le_Joueur_Tombe_Et_Se_Pose_Sur_Le_Sol()
        {
            var w = MakeRoom();
            var m = new CharacterMotor { Position = new Vector3(0f, 4f, 0f) };

            Simulate(m, w, default, 200);

            ClassicAssert.AreEqual(0f, m.Position.Y, 0.05f, "les pieds doivent reposer sur le sol");
            ClassicAssert.IsTrue(m.Grounded);
            ClassicAssert.AreEqual(0f, m.Velocity.Y, 0.5f, "la chute doit s'arreter");
        }

        [Test]
        public void Le_Joueur_Ne_Traverse_Jamais_Le_Sol()
        {
            var w = MakeRoom();
            var m = new CharacterMotor { Position = new Vector3(0f, 40f, 0f) };

            // Chute de 40 m : la vitesse a l'impact est elevee, c'est le cas
            // ou une collision naive laisse traverser.
            for (int i = 0; i < 400; i++)
            {
                m.Step(default, Dt, w);
                ClassicAssert.Greater(m.Position.Y, -0.5f, $"traverse le sol au tick {i}");
            }
        }

        [Test]
        public void Avancer_Fait_Avancer_Dans_La_Bonne_Direction()
        {
            var w = MakeRoom();
            var m = new CharacterMotor { Position = new Vector3(0f, 0.1f, 0f) };

            // Yaw = 0 regarde vers -Z.
            var input = new MoveInput { Move = new Vector2(0f, 1f), Yaw = 0f };
            Simulate(m, w, input, 64);

            ClassicAssert.Less(m.Position.Z, -1f, "on doit avancer vers -Z");
            ClassicAssert.AreEqual(0f, m.Position.X, 0.1f, "sans deriver lateralement");
        }

        [Test]
        public void La_Vitesse_De_Course_Se_Stabilise_Sur_La_Valeur_Reglee()
        {
            var w = MakeRoom();
            var m = new CharacterMotor { Position = new Vector3(0f, 0.1f, 0f) };

            var input = new MoveInput { Move = new Vector2(0f, 1f), Yaw = 0f };
            Simulate(m, w, input, 192);   // 3 s : largement le temps d'atteindre le palier

            ClassicAssert.AreEqual(m.Settings.RunSpeed, m.HorizontalSpeed, 0.15f);
            ClassicAssert.AreEqual(StanceState.Running, m.Stance);
        }

        [Test]
        public void La_Marche_Est_Plus_Lente_Que_La_Course()
        {
            var w = MakeRoom();
            var run = new CharacterMotor { Position = new Vector3(0f, 0.1f, 0f) };
            var walk = new CharacterMotor { Position = new Vector3(0f, 0.1f, 0f) };

            Simulate(run, w, new MoveInput { Move = new Vector2(0f, 1f) }, 192);
            Simulate(walk, w, new MoveInput { Move = new Vector2(0f, 1f), Walk = true }, 192);

            ClassicAssert.Less(walk.HorizontalSpeed, run.HorizontalSpeed);
            ClassicAssert.AreEqual(StanceState.Walking, walk.Stance);
        }

        [Test]
        public void La_Friction_Arrete_Le_Joueur_Quand_On_Lache_Les_Touches()
        {
            var w = MakeRoom();
            var m = new CharacterMotor { Position = new Vector3(0f, 0.1f, 0f) };

            Simulate(m, w, new MoveInput { Move = new Vector2(0f, 1f) }, 128);
            ClassicAssert.Greater(m.HorizontalSpeed, 3f, "on court");

            Simulate(m, w, default, 128);   // plus aucune entree

            ClassicAssert.Less(m.HorizontalSpeed, 0.1f, "la friction doit arreter le joueur");
            ClassicAssert.AreEqual(StanceState.Still, m.Stance);
        }

        [Test]
        public void La_Diagonale_Ne_Va_Pas_Plus_Vite_Que_La_Ligne_Droite()
        {
            var w = MakeRoom();
            var straight = new CharacterMotor { Position = new Vector3(0f, 0.1f, 0f) };
            var diagonal = new CharacterMotor { Position = new Vector3(0f, 0.1f, 0f) };

            Simulate(straight, w, new MoveInput { Move = new Vector2(0f, 1f) }, 192);
            Simulate(diagonal, w, new MoveInput { Move = new Vector2(1f, 1f) }, 192);

            ClassicAssert.AreEqual(straight.HorizontalSpeed, diagonal.HorizontalSpeed, 0.1f,
                "le bug historique du 'diagonal running' ne doit pas exister");
        }

        [Test]
        public void Le_Saut_Monte_A_La_Hauteur_Reglee()
        {
            var w = MakeRoom();
            var m = new CharacterMotor { Position = new Vector3(0f, 0.1f, 0f) };

            Simulate(m, w, default, 32);   // se poser d'abord
            float ground = m.Position.Y;

            m.Step(new MoveInput { Jump = true }, Dt, w);

            float peak = m.Position.Y;
            for (int i = 0; i < 64; i++)
            {
                m.Step(default, Dt, w);
                if (m.Position.Y > peak) peak = m.Position.Y;
            }

            float climbed = peak - ground;
            ClassicAssert.AreEqual(m.Settings.JumpHeight, climbed, 0.12f,
                $"saut mesure a {climbed:0.###} m pour {m.Settings.JumpHeight} m regles");
        }

        [Test]
        public void On_Ne_Traverse_Pas_Un_Mur()
        {
            var w = MakeRoom(half: 10f);   // petite piece : on veut atteindre le mur
            var m = new CharacterMotor { Position = new Vector3(0f, 0.1f, 0f) };

            // 10 secondes plein gaz vers -Z : le mur est en z = -10.
            Simulate(m, w, new MoveInput { Move = new Vector2(0f, 1f), Yaw = 0f }, 640);

            ClassicAssert.Greater(m.Position.Z, -10.5f, "le joueur est passe a travers le mur");
            ClassicAssert.Less(m.Position.Z, -8.5f, "le joueur doit quand meme atteindre le mur");
        }

        [Test]
        public void Saccroupir_Reduit_La_Hauteur_Et_La_Vitesse()
        {
            var w = MakeRoom();
            var m = new CharacterMotor { Position = new Vector3(0f, 0.1f, 0f) };

            Simulate(m, w, new MoveInput { Move = new Vector2(0f, 1f), Crouch = true }, 192);

            ClassicAssert.AreEqual(m.Settings.CrouchHeight, m.Height, 0.02f);
            ClassicAssert.AreEqual(StanceState.Crouching, m.Stance);
            ClassicAssert.Less(m.HorizontalSpeed, m.Settings.WalkSpeed);
        }

        [Test]
        public void On_Ne_Se_Releve_Pas_Sous_Un_Plafond_Bas()
        {
            var w = new CollisionWorld();
            w.Add(new Aabb(new Vector3(-10f, -1f, -10f), new Vector3(10f, 0f, 10f)));   // sol
            w.Add(new Aabb(new Vector3(-10f, 1.4f, -10f), new Vector3(10f, 2.4f, 10f))); // plafond a 1.4 m

            var m = new CharacterMotor { Position = new Vector3(0f, 0.05f, 0f) };

            Simulate(m, w, new MoveInput { Crouch = true }, 64);
            ClassicAssert.AreEqual(m.Settings.CrouchHeight, m.Height, 0.02f);

            // On relache accroupi : impossible de se relever, 1.83 m ne passe pas.
            Simulate(m, w, default, 64);

            ClassicAssert.Less(m.Height, 1.45f, "on ne doit pas traverser le plafond en se relevant");
        }

        [Test]
        public void Une_Marche_Basse_Est_Franchie_Automatiquement()
        {
            var w = new CollisionWorld();
            // Sol largement dimensionne : sinon le joueur sort du bord et tombe.
            w.Add(new Aabb(new Vector3(-50f, -1f, -50f), new Vector3(50f, 0f, 50f)));
            // Marche de 30 cm a partir de z = -3, assez profonde pour rester dessus.
            w.Add(new Aabb(new Vector3(-50f, 0f, -20f), new Vector3(50f, 0.3f, -3f)));

            var m = new CharacterMotor { Position = new Vector3(0f, 0.1f, 0f) };

            Simulate(m, w, new MoveInput { Move = new Vector2(0f, 1f), Yaw = 0f }, 128);

            ClassicAssert.Greater(m.Position.Y, 0.25f, "le joueur doit avoir monte la marche");
            ClassicAssert.Less(m.Position.Z, -3f, "et l'avoir depassee");
        }

        [Test]
        public void Deux_Simulations_Identiques_Donnent_Le_Meme_Resultat()
        {
            // Determinisme : indispensable pour la reconciliation reseau.
            var w1 = MakeRoom();
            var w2 = MakeRoom();
            var a = new CharacterMotor { Position = new Vector3(0f, 2f, 0f) };
            var b = new CharacterMotor { Position = new Vector3(0f, 2f, 0f) };

            var inputs = new[]
            {
                new MoveInput { Move = new Vector2(0f, 1f), Yaw = 0.3f },
                new MoveInput { Move = new Vector2(1f, 1f), Yaw = 0.9f, Jump = true },
                new MoveInput { Move = new Vector2(-1f, 0f), Yaw = 1.7f, Crouch = true },
                new MoveInput { Move = new Vector2(0f, -1f), Yaw = 2.4f },
            };

            for (int i = 0; i < 400; i++)
            {
                var input = inputs[i % inputs.Length];
                a.Step(input, Dt, w1);
                b.Step(input, Dt, w2);
            }

            ClassicAssert.AreEqual(a.Position.X, b.Position.X, 1e-6f);
            ClassicAssert.AreEqual(a.Position.Y, b.Position.Y, 1e-6f);
            ClassicAssert.AreEqual(a.Position.Z, b.Position.Z, 1e-6f);
        }
    }
}
