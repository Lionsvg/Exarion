// ===========================================================================
//  Tests de la physique en rotation (R4).
//
//  Les tests de PhysicsWorldTests verifient qu'une pile TIENT. Ceux-ci
//  verifient l'inverse : qu'une caisse qui ne devrait PAS tenir tombe.
//
//  C'est la seule preuve qui compte pour cette etape. Un solveur sans bras
//  de levier fait tenir une caisse posee sur un coin de table : elle flotte,
//  parfaitement horizontale, appuyee sur un centimetre carre. Rien dans les
//  anciens tests ne l'aurait detecte.
// ===========================================================================

using System;
using System.Numerics;
using NUnit.Framework;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class RotationTests
    {
        private const float Dt = 1f / 64f;

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

        /// <summary>
        /// Angle entre le haut du corps et le haut du monde, de 0 a 180 degres.
        ///
        /// Surtout PAS de valeur absolue ici : elle replierait "renverse a
        /// 150 degres" en "penche de 30 degres", deux etats qui n'ont rien a
        /// voir. L'erreur a failli faire passer un poteau couche par terre
        /// pour un poteau debout.
        /// </summary>
        private static float Tilt(RigidBody body)
        {
            Vector3 up = Vector3.Transform(Vector3.UnitY, body.Orientation);
            return MathF.Acos(Math.Clamp(up.Y, -1f, 1f)) * 180f / MathF.PI;
        }

        /// <summary>Socle fige de 2 m de cote, dont le dessus est a y = 1.</summary>
        private static RigidBody Pedestal(PhysicsWorld w)
        {
            var pedestal = w.Spawn(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 2f));
            pedestal.IsStatic = true;
            return pedestal;
        }

        // --- LE test de l'etape ---------------------------------------------

        [Test]
        public void Une_Caisse_En_Porte_A_Faux_Bascule_Et_Tombe()
        {
            // Socle jusqu'a x = 1, caisse centree en x = 1,35 : les neuf
            // dixiemes de son poids sont dans le vide. Elle doit basculer.
            //
            // Sans bras de levier, elle resterait posee bien a plat sur les
            // 15 cm qui portent - et le moteur aurait l'air faux sans qu'un
            // seul test ne bronche.
            var w = MakeWorld();
            Pedestal(w);

            var crate = w.Spawn(new Vector3(1.35f, 1.5f, 0f), Vector3.One);

            Run(w, 400);

            ClassicAssert.Less(crate.Position.Y, 1.0f, "elle doit avoir quitte le socle");
            ClassicAssert.AreEqual(0.5f, crate.Position.Y, 0.1f, "et fini par terre");
            ClassicAssert.Greater(Tilt(crate), 45f, "elle a bascule, elle n'a pas glisse a plat");
        }

        [Test]
        public void Une_Caisse_Bien_Posee_Ne_Bascule_Pas()
        {
            // Le controle indispensable du test precedent : si TOUT basculait,
            // il serait vert pour la mauvaise raison.
            var w = MakeWorld();
            Pedestal(w);

            var crate = w.Spawn(new Vector3(0f, 1.5f, 0f), Vector3.One);

            Run(w, 400);

            ClassicAssert.AreEqual(1.5f, crate.Position.Y, 0.05f, "elle reste sur le socle");
            ClassicAssert.Less(Tilt(crate), 1f, "et parfaitement a plat");
            ClassicAssert.IsTrue(crate.Sleeping, "et elle s'endort");
        }

        // --- Redressement ------------------------------------------------------

        [Test]
        public void Une_Caisse_Lachee_De_Travers_Se_Remet_A_Plat()
        {
            var w = MakeWorld();

            var crate = w.Spawn(new Vector3(0f, 2f, 0f), Vector3.One);
            crate.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 30f * MathF.PI / 180f);

            Run(w, 500);

            ClassicAssert.Less(Tilt(crate), 2f, "un cube penche de 30 deg retombe sur sa face");
            ClassicAssert.AreEqual(0.5f, crate.Position.Y, 0.05f);
            ClassicAssert.IsTrue(crate.Sleeping);
        }

        [Test]
        public void Au_Dela_De_45_Degres_Elle_Bascule_Sur_La_Face_Suivante()
        {
            // Le pendant du test precedent, et la preuve que le redressement
            // n'est pas un aimant vers l'horizontale : passe la diagonale,
            // le cube tombe de l'AUTRE cote.
            var w = MakeWorld();

            var crate = w.Spawn(new Vector3(0f, 2f, 0f), Vector3.One);
            crate.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 50f * MathF.PI / 180f);

            Run(w, 500);

            ClassicAssert.Greater(Tilt(crate), 85f, "elle bascule sur la face d'a cote");
            ClassicAssert.AreEqual(0.5f, crate.Position.Y, 0.05f);
        }

        // --- Bras de levier ------------------------------------------------------

        [Test]
        public void Une_Poussee_En_Haut_Renverse_Un_Poteau()
        {
            var w = MakeWorld();
            var post = w.Spawn(new Vector3(0f, 1f, 0f), new Vector3(0.4f, 2f, 0.4f));

            Run(w, 60);   // le laisser se poser

            post.ApplyImpulseAt(new Vector3(60f, 0f, 0f), post.Position + new Vector3(0f, 0.95f, 0f));

            Run(w, 400);

            ClassicAssert.Greater(Tilt(post), 60f, "un poteau pousse en haut se renverse");
        }

        [Test]
        public void Une_Caisse_Poussee_Au_Centre_Ne_Tourne_Pas()
        {
            // Meme impulsion, appliquee au centre de masse : elle glisse,
            // elle ne pivote pas. C'est la definition du bras de levier.
            var w = MakeWorld();
            var crate = w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One);

            Run(w, 60);
            crate.ApplyImpulseAt(new Vector3(60f, 0f, 0f), crate.Position);

            ClassicAssert.AreEqual(0f, crate.AngularVelocity.Length(), 1e-5f);
            ClassicAssert.Greater(crate.Velocity.X, 1f);
        }

        // --- Empilement avec rotation ---------------------------------------------

        [Test]
        public void Une_Pile_De_Caisses_Tournees_Tient_Et_Sendort()
        {
            // Trois caisses en lacet croissant : les faces ne coincident plus,
            // et le polygone de contact devient un octogone. C'est le cas que
            // R2 avait prevu, et il faut qu'il tienne.
            var w = MakeWorld();
            var boxes = new RigidBody[3];

            for (int i = 0; i < boxes.Length; i++)
            {
                boxes[i] = w.Spawn(new Vector3(0f, 0.5f + i * 1.02f, 0f), Vector3.One);
                boxes[i].Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, i * 30f * MathF.PI / 180f);
            }

            Run(w, 500);

            foreach (var b in boxes)
            {
                ClassicAssert.IsTrue(b.Sleeping, $"la caisse {b.Id} tremble encore");
                ClassicAssert.Less(Tilt(b), 2f, "aucune ne doit pencher");
            }

            ClassicAssert.Greater(boxes[2].Position.Y, 2.4f, "la pile garde sa hauteur");
        }

        // --- Determinisme ------------------------------------------------------------

        [Test]
        public void Une_Simulation_Avec_Rotation_Reste_Reproductible()
        {
            // Le determinisme etait deja teste sans rotation. Les quaternions
            // et le solveur iteratif sont exactement le genre de chose qui le
            // casse - et le reseau en dependra.
            PhysicsWorld Build()
            {
                var w = MakeWorld();
                for (int i = 0; i < 5; i++)
                {
                    var b = w.Spawn(new Vector3(i * 0.35f, 1f + i * 1.3f, i * 0.2f), Vector3.One, 10f + i);
                    b.Orientation = Quaternion.CreateFromAxisAngle(
                        Vector3.Normalize(new Vector3(1f, i + 1f, 2f)), i * 17f * MathF.PI / 180f);
                }
                return w;
            }

            var a = Build();
            var b = Build();

            a.Bodies[0].ApplyImpulseAt(new Vector3(120f, 40f, 30f), a.Bodies[0].Position + new Vector3(0.3f, 0.4f, 0f));
            b.Bodies[0].ApplyImpulseAt(new Vector3(120f, 40f, 30f), b.Bodies[0].Position + new Vector3(0.3f, 0.4f, 0f));

            Run(a, 300);
            Run(b, 300);

            for (int i = 0; i < a.Bodies.Count; i++)
            {
                ClassicAssert.AreEqual(a.Bodies[i].Position.X, b.Bodies[i].Position.X, 1e-6f);
                ClassicAssert.AreEqual(a.Bodies[i].Position.Y, b.Bodies[i].Position.Y, 1e-6f);
                ClassicAssert.AreEqual(a.Bodies[i].Orientation.X, b.Bodies[i].Orientation.X, 1e-6f);
                ClassicAssert.AreEqual(a.Bodies[i].Orientation.W, b.Bodies[i].Orientation.W, 1e-6f);
            }
        }

        // --- Ce que le solveur voit -----------------------------------------------------

        [Test]
        public void Une_Caisse_Posee_A_Plat_Sappuie_Sur_Quatre_Points()
        {
            // Le lien concret entre R2 et R4 : le polygone de contact devient
            // le nombre de contraintes que le solveur resout. Un seul point
            // suffirait a separer les caisses - et la pile s'ecroulerait.
            //
            // On mesure entre deux CAISSES, pas contre le sol. Une caisse
            // posee a plat sur le decor ne le chevauche pas : le balayage
            // l'arrete un millimetre avant, donc le solveur ne voit rien. Il
            // ne decouvre le decor que lorsqu'un corps y penetre vraiment -
            // typiquement une caisse inclinee, dont un coin plonge sous la
            // surface pendant que son emprise alignee, elle, ne bouge pas.
            //
            // On regarde le MAXIMUM sur la duree : une fois endormies, les
            // caisses sortent du solveur et le compte retombe a zero.
            var w = MakeWorld();
            w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One);
            var top = w.Spawn(new Vector3(0f, 2f, 0f), Vector3.One);

            int most = 0;
            for (int i = 0; i < 120; i++)
            {
                w.Step(Dt);
                most = Math.Max(most, w.ContactCount);
            }

            ClassicAssert.GreaterOrEqual(most, 4, "un appui plat, c'est un polygone - pas un point");
            ClassicAssert.AreEqual(1.5f, top.Position.Y, 0.06f, "et la pile tient");
        }

        [Test]
        public void Une_Caisse_Endormie_Ne_Coute_Plus_Rien()
        {
            var w = MakeWorld();
            var bottom = w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One);
            var top = w.Spawn(new Vector3(0f, 1.6f, 0f), Vector3.One);

            Run(w, 400);

            ClassicAssert.IsTrue(bottom.Sleeping);
            ClassicAssert.IsTrue(top.Sleeping);
            ClassicAssert.AreEqual(0, w.ContactCount, "deux corps endormis sortent du solveur");
        }

        // --- Amortissement (R5) --------------------------------------------------------

        [Test]
        public void Une_Toupie_Finit_Par_Sarreter()
        {
            // Ce test a d'abord ete ecrit a l'ENVERS, pour figer un defaut :
            // une caisse lancee en toupie tournait indefiniment. Le
            // frottement du solveur est plafonne par l'impulsion normale, et
            // contre le decor cette impulsion est nulle - le balayage annule
            // la vitesse verticale avant que le solveur ne voie le contact.
            //
            // C'est l'amortissement qui evacue cette rotation, pas le
            // frottement. La distinction compte : sur une caisse posee sur
            // une AUTRE caisse, c'est bien le frottement qui agit.
            var w = MakeWorld();
            var crate = w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One);

            Run(w, 60);
            crate.AngularVelocity = new Vector3(0f, 6f, 0f);
            crate.Wake();

            Run(w, 400);

            ClassicAssert.Less(crate.AngularVelocity.Length(), 0.05f, "elle doit avoir cesse de tourner");
            ClassicAssert.IsTrue(crate.Sleeping, "et donc pouvoir s'endormir");
        }

        [Test]
        public void Le_Decor_Freine_Vraiment_La_Rotation()
        {
            // LA preuve de R7, et le seul test qui la donne.
            //
            // Une_Toupie_Finit_Par_Sarreter passait deja - mais grace a
            // l'AMORTISSEMENT de R5, pas grace au frottement. Ici on coupe
            // l'amortissement : si la toupie s'arrete encore, c'est que le
            // sol la freine vraiment, par la loi de Coulomb.
            //
            // C'etait impossible avant R7 : le balayage annulait la vitesse
            // verticale avant le solveur, l'impulsion normale valait zero, et
            // le frottement n'avait rien pour mordre. La caisse tournait a
            // 6,000 rad/s indefiniment.
            var w = MakeWorld();
            var crate = w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One);
            crate.AngularDamping = 0f;
            crate.LinearDamping = 0f;

            Run(w, 60);
            crate.AngularVelocity = new Vector3(0f, 6f, 0f);
            crate.Wake();

            Run(w, 400);

            ClassicAssert.Less(crate.AngularVelocity.Length(), 1f,
                "sans amortissement, c'est le frottement du sol qui doit la freiner");
        }

        [Test]
        public void Une_Caisse_Endormie_Se_Comporte_Comme_Un_Mur()
        {
            // Un corps endormi n'est plus integre : lui donner une vitesse ne
            // le deplace pas. Le solveur doit donc le traiter comme un corps
            // fige, sinon il devient un PUITS - il encaisse la moitie de
            // chaque impulsion et ne la rend jamais.
            //
            // Mesure du defaut, avant correction : la caisse du dessus gardait
            // 0,2366 m/s indefiniment, soit exactement l'increment de gravite
            // d'un pas. Jamais arretee, elle ne s'endormait jamais.
            var w = MakeWorld();
            var bottom = w.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.One);

            Run(w, 60);
            ClassicAssert.IsTrue(bottom.Sleeping, "celle du bas doit dormir avant l'arrivee de l'autre");

            var top = w.Spawn(new Vector3(0f, 1.6f, 0f), Vector3.One);

            Run(w, 400);

            ClassicAssert.Less(top.Velocity.Length(), 0.05f, "elle doit vraiment s'arreter");
            ClassicAssert.IsTrue(top.Sleeping);
            ClassicAssert.AreEqual(1.5f, top.Position.Y, 0.06f);
        }

        [Test]
        public void Une_Caisse_En_Vol_Garde_Sa_Rotation()
        {
            // Le controle de l'amortissement : il doit calmer ce qui traine
            // au sol, pas figer ce qui vole. Une caisse lancee en l'air doit
            // continuer de tourner franchement.
            var w = MakeWorld();
            var crate = w.Spawn(new Vector3(0f, 20f, 0f), Vector3.One);
            crate.AngularVelocity = new Vector3(0f, 8f, 0f);

            Run(w, 32);   // un demi-quart de seconde de chute libre

            ClassicAssert.Greater(crate.AngularVelocity.Length(), 5f,
                "l'amortissement ne doit pas la figer en plein vol");
        }

        [Test]
        public void Un_Corps_Endormi_Reste_Endormi_Sous_Une_Pile_Calme()
        {
            // Le piege du reveil : deux caisses qui vibrotent chacune a 0,07
            // en sens contraire affichent 0,14 de vitesse relative. Juger le
            // reveil la-dessus les faisait se reveiller mutuellement sans fin.
            // On juge donc le VOISIN sur sa vitesse propre, avec un seuil qui
            // domine l'increment de gravite d'un pas.
            var w = MakeWorld();
            var boxes = new RigidBody[4];

            for (int i = 0; i < boxes.Length; i++)
                boxes[i] = w.Spawn(new Vector3(0f, 0.5f + i * 1.02f, 0f), Vector3.One);

            Run(w, 400);
            foreach (var b in boxes)
                ClassicAssert.IsTrue(b.Sleeping);

            // Et elles restent endormies : rien ne doit les relancer.
            Run(w, 400);
            foreach (var b in boxes)
                ClassicAssert.IsTrue(b.Sleeping, $"la caisse {b.Id} s'est reveillee toute seule");
        }
    }
}
