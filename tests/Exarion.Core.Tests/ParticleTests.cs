// ===========================================================================
//  Tests du systeme de particules (2.3).
//
//  Un systeme de particules est ingrat a verifier : il est fait pour etre
//  joli, et "joli" ne s'affirme pas. Ce qui se teste, en revanche, c'est
//  tout ce qui le fait echouer en silence :
//
//   - le recyclage du reservoir, dont le bug classique fait sauter un pas a
//     une particule sur deux sans que rien ne se voie ;
//   - l'amortissement, qui renvoie les particules en arriere s'il est ecrit
//     naivement ;
//   - le debordement, qui avale les nouveaux effets sans prevenir ;
//   - la base orthonormee, qui explose sur une normale verticale - donc sur
//     tout impact au sol, le cas le plus frequent de tous.
// ===========================================================================

using System;
using System.Numerics;
using NUnit.Framework;
using Exarion.Core.Deterministic;
using Exarion.Core.Effects;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class ParticleTests
    {
        // ===================================================================
        //  Reservoir
        // ===================================================================

        [Test]
        public void Une_Particule_Morte_Libere_Sa_Place_Sans_Faire_Sauter_Un_Pas()
        {
            // LE test de ce fichier.
            //
            // La suppression se fait par echange avec la derniere vivante. Si
            // l'indice de parcours est incremente apres l'echange, la
            // particule qui vient d'arriver a cette place n'est jamais
            // traitee : elle saute un pas entier. Rien ne le signale - le
            // compte est bon, l'affichage est plausible, et une particule sur
            // deux avance a la moitie de la vitesse.
            //
            // On rend le defaut visible en donnant a TOUTES les survivantes la
            // meme vitesse : apres un pas, elles doivent etre au meme endroit.
            var system = new ParticleSystem(32) { Gravity = Vector3.Zero };

            for (int i = 0; i < 10; i++)
            {
                system.Spawn(new Particle
                {
                    Position = Vector3.Zero,
                    Velocity = Vector3.UnitX,
                    Lifetime = i % 2 == 0 ? 0.01f : 10f,   // les paires meurent
                });
            }

            ClassicAssert.AreEqual(10, system.Count);

            system.Step(0.02f);

            ClassicAssert.AreEqual(5, system.Count, "les cinq paires devaient mourir");

            foreach (Particle p in system.Live)
                ClassicAssert.AreEqual(0.02f, p.Position.X, 1e-6f, "une survivante a saute son pas");
        }

        [Test]
        public void Le_Debordement_Est_Compte_Et_Pas_Silencieux()
        {
            // Un reservoir qui deborde sans le dire est ce qui fait qu'un
            // systeme de particules "arrete de marcher" un jour, sans raison.
            var system = new ParticleSystem(3);

            for (int i = 0; i < 5; i++)
                system.Spawn(new Particle { Lifetime = 1f });

            ClassicAssert.AreEqual(3, system.Count);
            ClassicAssert.AreEqual(2, system.Dropped);
        }

        [Test]
        public void Une_Particule_Nait_Sans_Passe()
        {
            // Sans cette precaution, Previous vaut (0,0,0) et la particule est
            // interpolee depuis l'origine du monde : sur sa premiere image,
            // elle traverse toute la scene.
            var system = new ParticleSystem(4);
            var at = new Vector3(3f, 4f, 5f);

            system.Spawn(new Particle { Position = at, Lifetime = 1f });

            Particle p = system.Live[0];

            ClassicAssert.AreEqual(at.X, p.At(0f).X, 1e-6f);
            ClassicAssert.AreEqual(at.X, p.At(1f).X, 1e-6f);
        }

        [Test]
        public void Un_Age_Fourni_A_La_Naissance_Est_Ignore()
        {
            // Une recette qui laisserait trainer un Age non nul ferait naitre
            // une particule deja a moitie eteinte.
            var system = new ParticleSystem(4);

            system.Spawn(new Particle { Lifetime = 2f, Age = 1.9f });

            ClassicAssert.AreEqual(0f, system.Live[0].Age, 1e-6f);
        }

        // ===================================================================
        //  Integration
        // ===================================================================

        [Test]
        public void Un_Amortissement_Fort_Ne_Renvoie_Jamais_La_Particule_En_Arriere()
        {
            // La forme naive v *= (1 - drag * dt) devient NEGATIVE des que
            // drag * dt depasse 1. Ici 40 x 0,05 = 2 : la particule repartirait
            // a l'envers, puis oscillerait de plus en plus fort.
            var system = new ParticleSystem(4) { Gravity = Vector3.Zero };

            system.Spawn(new Particle
            {
                Velocity = new Vector3(10f, 0f, 0f),
                Lifetime = 10f,
                Drag = 40f,
            });

            float previous = 10f;

            for (int i = 0; i < 12; i++)
            {
                system.Step(0.05f);

                float speed = system.Live[0].Velocity.X;

                Assert.That(speed, Is.GreaterThan(0f), "la vitesse a change de signe");
                Assert.That(speed, Is.LessThan(previous), "la vitesse doit decroitre");

                previous = speed;
            }
        }

        [Test]
        public void La_Pesanteur_S_Applique_A_L_Echelle_Demandee()
        {
            var system = new ParticleSystem(4) { Gravity = new Vector3(0f, -10f, 0f) };

            system.Spawn(new Particle { Lifetime = 10f, GravityScale = 1f });
            system.Spawn(new Particle { Lifetime = 10f, GravityScale = 0f });
            system.Spawn(new Particle { Lifetime = 10f, GravityScale = -0.5f });

            system.Step(0.1f);

            ClassicAssert.AreEqual(-1f, system.Live[0].Velocity.Y, 1e-5f);
            ClassicAssert.AreEqual(0f, system.Live[1].Velocity.Y, 1e-5f);
            ClassicAssert.AreEqual(0.5f, system.Live[2].Velocity.Y, 1e-5f, "la fumee monte");
        }

        // ===================================================================
        //  Collision
        // ===================================================================

        [Test]
        public void Une_Particule_Rebondit_Sur_Le_Decor()
        {
            var world = new CollisionWorld();
            world.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(10f, 1f, 10f));   // dessus a y = 0

            var system = new ParticleSystem(4) { Gravity = Vector3.Zero, World = world };

            system.Spawn(new Particle
            {
                Position = new Vector3(0f, 1f, 0f),
                Velocity = new Vector3(0f, -10f, 0f),
                Lifetime = 10f,
                Bounce = 0.5f,
            });

            system.Step(0.15f);   // 1,5 m de trajet pour 1 m de chute

            Particle p = system.Live[0];

            Assert.That(p.Position.Y, Is.GreaterThan(0f), "elle est passee sous le sol");
            Assert.That(p.Position.Y, Is.LessThan(0.02f), "elle devait s'arreter au contact");
            ClassicAssert.AreEqual(5f, p.Velocity.Y, 1e-4f, "moitie de la vitesse, vers le haut");
        }

        [Test]
        public void Une_Particule_Sans_Rebond_Traverse_Le_Decor()
        {
            // Choix assume : chaque rebond coute un rayon contre tout le decor.
            // La poussiere et la fumee n'en ont pas besoin, et representent
            // l'essentiel des particules a l'ecran.
            var world = new CollisionWorld();
            world.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(10f, 1f, 10f));

            var system = new ParticleSystem(4) { Gravity = Vector3.Zero, World = world };

            system.Spawn(new Particle
            {
                Position = new Vector3(0f, 1f, 0f),
                Velocity = new Vector3(0f, -10f, 0f),
                Lifetime = 10f,
                Bounce = 0f,
            });

            system.Step(0.15f);

            ClassicAssert.AreEqual(-0.5f, system.Live[0].Position.Y, 1e-4f);
        }

        // ===================================================================
        //  Apparence
        // ===================================================================

        [Test]
        public void L_Opacite_Part_De_Zero_Et_Y_Revient()
        {
            var smoke = new Particle { Lifetime = 1f, FadeIn = 0.25f };

            ClassicAssert.AreEqual(0f, smoke.Alpha, 1e-6f, "invisible a la naissance");

            smoke.Age = 0.25f;
            ClassicAssert.AreEqual(0.75f, smoke.Alpha, 1e-6f, "pleine apparition");

            smoke.Age = 1f;
            ClassicAssert.AreEqual(0f, smoke.Alpha, 1e-6f, "eteinte en fin de vie");

            // Une etincelle, elle, est vive tout de suite.
            var spark = new Particle { Lifetime = 1f, FadeIn = 0f };
            ClassicAssert.AreEqual(1f, spark.Alpha, 1e-6f);
        }

        [Test]
        public void La_Taille_Va_Du_Depart_A_L_Arrivee()
        {
            var p = new Particle { Lifetime = 2f, SizeStart = 0.1f, SizeEnd = 0.5f, Age = 1f };

            ClassicAssert.AreEqual(0.3f, p.Size, 1e-6f);
        }

        // ===================================================================
        //  Recettes
        // ===================================================================

        [Test]
        public void Un_Impact_Reste_Fini_Quelle_Que_Soit_La_Normale()
        {
            var system = new ParticleSystem(256);
            var rng = new Rng(7u);

            foreach (Vector3 normal in new[]
                     {
                         Vector3.UnitY, -Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ,
                         Vector3.Normalize(new Vector3(0.3f, 0.95f, 0.1f)),
                     })
            {
                ParticleEffects.Impact(system, ref rng, Vector3.Zero, normal);
            }

            Assert.That(system.Count, Is.GreaterThan(0));

            foreach (Particle p in system.Live)
            {
                Assert.That(float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z), Is.True);
                Assert.That(float.IsFinite(p.Velocity.X + p.Velocity.Y + p.Velocity.Z), Is.True);
                Assert.That(p.Velocity.Length(), Is.GreaterThan(0f));
            }
        }

        [Test]
        public void Un_Impact_Au_Sol_Fait_Bien_Une_GERBE()
        {
            // ⚠️ LE piege des recettes, et le test qui l'attrape VRAIMENT.
            //
            // Construire une base orthonormee en croisant l'axe avec Y donne
            // un vecteur NUL quand l'axe EST Y - c'est-a-dire pour tout impact
            // sur un sol, le cas le plus frequent de tous.
            //
            // Un premier test cherchait des NaN. Il ne trouvait rien, et
            // c'etait une FAUSSE assurance : le garde-fou de Cone() voit la
            // longueur NaN, la compare a un seuil - toute comparaison avec NaN
            // est fausse - et retombe sur l'axe. Aucun NaN ne sort donc du
            // systeme. Ce qui en sort, c'est bien pire parce que c'est
            // plausible : quarante etincelles rigoureusement PARALLELES, une
            // tige au lieu d'une gerbe.
            //
            // Verifie par mutation : avec la base naive, ce test tombe et
            // celui sur les NaN passe.
            var system = new ParticleSystem(256);
            var rng = new Rng(7u);

            ParticleEffects.Sparks(system, ref rng, Vector3.Zero, Vector3.UnitY, 40, 6f);

            int offAxis = 0;

            foreach (Particle p in system.Live)
                if (Vector3.Dot(Vector3.Normalize(p.Velocity), Vector3.UnitY) < 0.95f)
                    offAxis++;

            Assert.That(offAxis, Is.GreaterThan(20),
                        $"seulement {offAxis} etincelles sur 40 s'ecartent de la normale");
        }

        [Test]
        public void Deux_Impacts_De_Suite_Ne_Sont_Pas_Le_Meme_Decalque()
        {
            // ⚠️ Rng est une STRUCTURE a etat mutable, et c'est un piege reel
            // en C# : oublier `ref` compile parfaitement.
            //
            // Le defaut ne se voit PAS a l'interieur d'un appel - la copie
            // locale avance normalement, les vingt etincelles d'une meme gerbe
            // different bien. Verifie par mutation : un test sur une seule
            // gerbe passe avec `ref` retire.
            //
            // Ce qui casse, c'est le generateur de l'APPELANT, qui ne bouge
            // plus. La deuxieme rafale est alors la copie exacte de la
            // premiere : dans le jeu, toutes les balles d'un chargeur
            // feraient rigoureusement la meme gerbe.
            var system = new ParticleSystem(256);
            var rng = new Rng(11u);

            ParticleEffects.Sparks(system, ref rng, Vector3.Zero, Vector3.UnitY, 20, 6f);

            var first = new Vector3[20];
            for (int i = 0; i < 20; i++)
                first[i] = system.Live[i].Velocity;

            ParticleEffects.Sparks(system, ref rng, Vector3.Zero, Vector3.UnitY, 20, 6f);

            int identical = 0;
            for (int i = 0; i < 20; i++)
                if (system.Live[20 + i].Velocity == first[i])
                    identical++;

            ClassicAssert.AreEqual(0, identical, "la deuxieme rafale recopie la premiere");

            // Et a l'interieur d'une meme gerbe, rien ne se superpose non plus.
            var seen = new System.Collections.Generic.HashSet<Vector3>();
            foreach (Vector3 v in first)
                seen.Add(v);

            ClassicAssert.AreEqual(20, seen.Count, "des etincelles superposees");
        }

        [Test]
        public void Les_Etincelles_Partent_Du_Bon_Cote_De_La_Surface()
        {
            // Un cone mal oriente enverrait la moitie des etincelles DANS le
            // mur, ou elles disparaitraient - l'impact aurait l'air deux fois
            // plus faible sans qu'on comprenne pourquoi.
            var system = new ParticleSystem(256);
            var rng = new Rng(3u);
            var normal = Vector3.UnitX;

            ParticleEffects.Sparks(system, ref rng, Vector3.Zero, normal, 60, 6f);

            int wrongWay = 0;

            foreach (Particle p in system.Live)
                if (Vector3.Dot(Vector3.Normalize(p.Velocity), normal) < 0f)
                    wrongWay++;

            // Un cone large en renvoie quelques-unes en rasant : on tolere.
            Assert.That(wrongWay, Is.LessThan(6), $"{wrongWay} etincelles sur 60 partent dans le mur");
        }

        [Test]
        public void Une_Explosion_Melange_Additif_Et_Opaque()
        {
            // Le noyau EMET de la lumiere, la fumee la BLOQUE. Les deux dans
            // le meme mode de melange donneraient soit une fumee lumineuse,
            // soit un noyau terne.
            var system = new ParticleSystem(512);
            var rng = new Rng(5u);

            ParticleEffects.Explosion(system, ref rng, Vector3.Zero);

            int additive = 0, opaque = 0;

            foreach (Particle p in system.Live)
                if (p.Additive) additive++; else opaque++;

            Assert.That(additive, Is.GreaterThan(0));
            Assert.That(opaque, Is.GreaterThan(0));
        }

        [Test]
        public void Le_Noyau_D_Explosion_Depasse_Le_Blanc()
        {
            // Sans ca, pas de halo : le bloom (1.8) ne retient que ce qui
            // depasse le seuil, et une explosion a 1,0 est exactement aussi
            // lumineuse qu'un mur eclaire.
            var system = new ParticleSystem(512);
            var rng = new Rng(5u);

            ParticleEffects.Explosion(system, ref rng, Vector3.Zero);

            float brightest = 0f;

            foreach (Particle p in system.Live)
                brightest = MathF.Max(brightest, p.Color.X);

            Assert.That(brightest, Is.GreaterThan(2f), $"le plus lumineux plafonne a {brightest:0.00}");
        }

        // ===================================================================
        //  Determinisme
        // ===================================================================

        [Test]
        public void Deux_Simulations_De_Meme_Graine_Sont_Identiques()
        {
            // C'est la propriete sur laquelle repose tout le choix du pas
            // fixe : une capture headless doit rendre la meme image a chaque
            // fois, sinon aucune comparaison de rendu n'a de sens.
            static ParticleSystem Run()
            {
                var world = new CollisionWorld();
                world.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));

                var system = new ParticleSystem(512) { World = world };
                var rng = new Rng(1234u);

                ParticleEffects.Explosion(system, ref rng, new Vector3(0f, 1f, 0f));

                for (int i = 0; i < 40; i++)
                    system.Step(1f / 64f);

                return system;
            }

            ParticleSystem a = Run();
            ParticleSystem b = Run();

            ClassicAssert.AreEqual(a.Count, b.Count);
            Assert.That(a.Count, Is.GreaterThan(10), "il doit rester de quoi comparer");

            for (int i = 0; i < a.Count; i++)
            {
                ClassicAssert.AreEqual(a.Live[i].Position.X, b.Live[i].Position.X, 0f);
                ClassicAssert.AreEqual(a.Live[i].Position.Y, b.Live[i].Position.Y, 0f);
                ClassicAssert.AreEqual(a.Live[i].Position.Z, b.Live[i].Position.Z, 0f);
            }
        }

        [Test]
        public void Les_Debris_Finissent_Par_Se_Poser()
        {
            // Verification de bon sens : apres deux secondes, plus rien ne
            // doit etre sous le sol. Une collision mal branchee laisserait
            // les debris continuer indefiniment vers le bas.
            var world = new CollisionWorld();
            world.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));

            var system = new ParticleSystem(512) { World = world };
            var rng = new Rng(99u);

            ParticleEffects.Explosion(system, ref rng, new Vector3(0f, 1.5f, 0f));

            for (int i = 0; i < 128; i++)
                system.Step(1f / 64f);

            foreach (Particle p in system.Live)
                if (p.Bounce > 0f)
                    Assert.That(p.Position.Y, Is.GreaterThan(-0.05f), "un debris est passe sous le sol");
        }
    }
}
