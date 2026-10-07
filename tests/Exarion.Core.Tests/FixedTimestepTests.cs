// ===========================================================================
//  Tests de la boucle a pas fixe.
//
//  Le test qui compte : Deux_Framerates_Differents_Simulent_Exactement_Pareil.
//  Sans cette garantie, la reconciliation reseau est impossible.
//
//  Ces tests ont deja trouve un vrai bug : la premiere version accumulait
//  des deltas en virgule flottante et perdait un pas par seconde. D'ou le
//  passage au temps absolu en nanosecondes entieres.
// ===========================================================================

using NUnit.Framework;
using Exarion.Core;

namespace Exarion.Tests
{
    public class FixedTimestepTests
    {
        [Test]
        public void Le_Pas_Correspond_Au_Tickrate()
        {
            var t = new FixedTimestep(tickRate: 64.0);

            ClassicAssert.AreEqual(15_625_000L, t.FixedDeltaNanoseconds, "1e9 / 64 doit tomber juste");
            ClassicAssert.AreEqual(64.0, t.TickRate, 1e-9);
        }

        [Test]
        public void Une_Seconde_Produit_Exactement_Le_Tickrate_En_Pas()
        {
            var t = new FixedTimestep(tickRate: 64.0, maxStepsPerFrame: 10000);

            ClassicAssert.AreEqual(64, t.AdvanceToSeconds(1.0));
            ClassicAssert.AreEqual(64L, t.TotalSteps);
        }

        [Test]
        public void Cinquante_Hertz_Aussi()
        {
            // C'est ce cas precis qui echouait avec l'accumulation de deltas :
            // 1/50 n'est pas exact en binaire, on obtenait 49 pas.
            var t = new FixedTimestep(tickRate: 50.0, maxStepsPerFrame: 10000);

            ClassicAssert.AreEqual(50, t.AdvanceToSeconds(1.0));
            ClassicAssert.AreEqual(50L, t.TotalSteps);
            ClassicAssert.AreEqual(1.0, t.SimulatedTime, 1e-12);
        }

        [Test]
        public void Un_Temps_Trop_Court_Ne_Simule_Rien()
        {
            var t = new FixedTimestep(tickRate: 64.0);

            // 1 ms alors qu'un pas dure 15.625 ms.
            ClassicAssert.AreEqual(0, t.AdvanceToSeconds(0.001));
            ClassicAssert.Greater(t.PendingNanoseconds, 0L, "le temps doit rester en attente, pas etre jete");
        }

        [Test]
        public void Le_Temps_En_Attente_Est_Conserve_Dun_Appel_A_Lautre()
        {
            var t = new FixedTimestep(tickRate: 100.0, maxStepsPerFrame: 100);   // pas = 10 ms

            ClassicAssert.AreEqual(1, t.AdvanceToSeconds(0.015));   // 15 ms -> 1 pas
            ClassicAssert.AreEqual(1, t.AdvanceToSeconds(0.021));   // 21 ms -> 1 pas de plus
            ClassicAssert.AreEqual(0, t.AdvanceToSeconds(0.029));   // 29 ms -> toujours 2 au total
            ClassicAssert.AreEqual(1, t.AdvanceToSeconds(0.031));   // 31 ms -> 3e pas
            ClassicAssert.AreEqual(3L, t.TotalSteps);
        }

        [Test]
        public void Alpha_Reste_Entre_Zero_Et_Un()
        {
            var t = new FixedTimestep(tickRate: 64.0, maxStepsPerFrame: 1000);

            // Des durees d'image irregulieres, comme dans la vraie vie.
            double[] frames = { 0.016, 0.004, 0.033, 0.008, 0.021, 0.001, 0.050 };

            double now = 0.0;
            foreach (double f in frames)
            {
                now += f;
                t.AdvanceToSeconds(now);

                ClassicAssert.GreaterOrEqual(t.Alpha, 0f);
                ClassicAssert.Less(t.Alpha, 1f, "alpha doit rester sous 1 apres consommation");
            }
        }

        [Test]
        public void Le_Plafond_Evite_La_Spirale_De_La_Mort()
        {
            var t = new FixedTimestep(tickRate: 64.0, maxStepsPerFrame: 5);

            // Une image catastrophique de 2 secondes : 128 pas demandes.
            int steps = t.AdvanceToSeconds(2.0);

            ClassicAssert.AreEqual(5, steps, "on ne simule jamais plus que le plafond");
            ClassicAssert.IsTrue(t.LastFrameWasClamped);
            ClassicAssert.AreEqual(5L, t.TotalSteps, "seuls 5 pas ont reellement tourne");
            ClassicAssert.AreEqual(123L, t.SkippedSteps, "les 123 autres sont sautes, pas rattrapes");
        }

        [Test]
        public void Apres_Un_Decrochage_Lhorloge_Reste_Alignee_Sur_Le_Temps_Reel()
        {
            var t = new FixedTimestep(tickRate: 64.0, maxStepsPerFrame: 5);

            t.AdvanceToSeconds(2.0);

            // L'horloge de jeu doit avoir rattrape le temps reel, sinon le
            // retard s'accumulerait indefiniment.
            ClassicAssert.AreEqual(2.0, t.SimulatedTime, 0.02);
            ClassicAssert.Less(t.PendingNanoseconds, t.FixedDeltaNanoseconds);
        }

        // -------------------------------------------------------------------
        //  L'HORLOGE DU RENDU (RenderTime)
        //
        //  Elle existe pour les animations, et le piege qu'elle evite ne se
        //  voit pas a l'oeil : une animation avancee d'un delta a chaque appel
        //  tourne au triple de sa vitesse, parce qu'une image echantillonne la
        //  pose trois fois (ombre, occlusion, passe finale). Ces tests
        //  verifient les trois proprietes qui rendent ca impossible.
        // -------------------------------------------------------------------

        [Test]
        public void Lhorloge_De_Rendu_Avance_Entre_Deux_Pas()
        {
            // CE QUI COMPTE : sans ca, l'animation saccade des que le rendu
            // depasse la cadence de simulation. Le temps simule, lui, ne bouge
            // pas entre deux pas - il avance par paliers de 1/64 s.
            var t = new FixedTimestep(tickRate: 64.0);

            t.AdvanceToSeconds(0.015625);           // pile un pas
            double simule = t.SimulatedTime;
            double rendu = t.RenderTime;

            ClassicAssert.AreEqual(simule, rendu, 1e-9,
                "pile sur un pas, les deux horloges coincident");

            // On avance le temps REEL d'un demi-pas : pas assez pour un
            // nouveau pas de simulation.
            ClassicAssert.AreEqual(0, t.AdvanceToSeconds(0.015625 + 0.0078125),
                "un demi-pas ne declenche aucune simulation");

            ClassicAssert.AreEqual(simule, t.SimulatedTime, 1e-9,
                "le temps simule n'a pas bouge, c'est normal");

            ClassicAssert.Greater(t.RenderTime, simule + 0.007,
                "MAIS l'horloge de rendu a avance d'environ un demi-pas");
            ClassicAssert.Less(t.RenderTime, simule + 0.015625,
                "sans jamais depasser le pas suivant");
        }

        [Test]
        public void Lhorloge_De_Rendu_Ne_Bouge_Pas_Quand_On_La_Relit()
        {
            // ⚠️ LA PROPRIETE VITALE, et celle qu'un compteur "temps += dt"
            // n'a PAS. Une image lit la pose trois fois ; les trois doivent
            // decrire le meme personnage au meme instant.
            //
            // ⚠️ HONNETETE SUR CE TEST : c'est un GARDE-FOU, pas un test qui
            // discrimine. Les trois mutations essayees sur RenderTime (alpha
            // oublie, signe inverse, alpha double) ne le font PAS tomber -
            // une expression pure est idempotente par construction. Il sert a
            // attraper le jour ou quelqu'un remplacera cette expression par
            // quelque chose qui avance un compteur.
            var t = new FixedTimestep(tickRate: 64.0);

            t.AdvanceToSeconds(0.02);   // un pas + un reste, donc Alpha > 0

            double premier = t.RenderTime;

            for (int i = 0; i < 10; i++)
                ClassicAssert.AreEqual(premier, t.RenderTime, 0.0,
                    "relire l'horloge ne doit RIEN faire avancer");
        }

        [Test]
        public void Lhorloge_De_Rendu_Ne_Recule_Jamais()
        {
            // Une horloge qui recule ferait repartir une animation en arriere
            // sur une seule image - un a-coup impossible a diagnostiquer.
            // Le cas piegeux est le PASSAGE d'un pas : Alpha retombe a 0 pile
            // au moment ou SimulatedTime saute d'un pas, et la somme doit
            // rester monotone malgre ces deux marches en sens contraire.
            var t = new FixedTimestep(tickRate: 64.0);

            double precedent = t.RenderTime;

            // Pas de 1 ms : on traverse donc chaque frontiere de pas.
            for (int i = 1; i <= 500; i++)
            {
                t.AdvanceToSeconds(i / 1000.0);

                double maintenant = t.RenderTime;

                ClassicAssert.GreaterOrEqual(maintenant, precedent,
                    $"l'horloge a recule a l'iteration {i} : {precedent} -> {maintenant}");

                precedent = maintenant;
            }

            ClassicAssert.AreEqual(0.5, precedent, 0.002,
                "et au bout du compte elle suit le temps reel");
        }

        [Test]
        public void Une_Image_Normale_Nest_Pas_Plafonnee()
        {
            var t = new FixedTimestep(tickRate: 64.0, maxStepsPerFrame: 5);

            t.AdvanceToSeconds(0.016);

            ClassicAssert.IsFalse(t.LastFrameWasClamped);
            ClassicAssert.AreEqual(0L, t.SkippedSteps);
        }

        [Test]
        public void Deux_Framerates_Differents_Simulent_Exactement_Pareil()
        {
            // LE TEST QUI COMPTE.
            // Machine A a 60 images/s, machine B a 144. Au bout de 10 secondes
            // de temps reel, les deux doivent avoir simule le meme nombre de pas.
            var a = new FixedTimestep(tickRate: 64.0, maxStepsPerFrame: 10000);
            var b = new FixedTimestep(tickRate: 64.0, maxStepsPerFrame: 10000);

            const double duration = 10.0;

            for (int i = 1; i <= 600; i++)
                a.AdvanceToSeconds(System.Math.Min(i / 60.0, duration));
            a.AdvanceToSeconds(duration);

            for (int i = 1; i <= 1440; i++)
                b.AdvanceToSeconds(System.Math.Min(i / 144.0, duration));
            b.AdvanceToSeconds(duration);

            ClassicAssert.AreEqual(a.TotalSteps, b.TotalSteps,
                "deux framerates differents doivent produire la meme simulation");
            ClassicAssert.AreEqual(640L, a.TotalSteps, "10 s a 64 Hz = 640 pas");
        }

        [Test]
        public void Un_Framerate_Irregulier_Donne_Le_Meme_Resultat_Quun_Framerate_Stable()
        {
            var stable = new FixedTimestep(tickRate: 64.0, maxStepsPerFrame: 10000);
            var chaotique = new FixedTimestep(tickRate: 64.0, maxStepsPerFrame: 10000);

            for (int i = 1; i <= 300; i++)
                stable.AdvanceToSeconds(System.Math.Min(i / 60.0, 5.0));
            stable.AdvanceToSeconds(5.0);

            // Images tres irregulieres, mais meme temps absolu final.
            double[] jitter = { 0.004, 0.041, 0.009, 0.027, 0.013, 0.038, 0.006 };
            double now = 0.0;
            int k = 0;
            while (now < 5.0)
            {
                now = System.Math.Min(now + jitter[k++ % jitter.Length], 5.0);
                chaotique.AdvanceToSeconds(now);
            }

            ClassicAssert.AreEqual(stable.TotalSteps, chaotique.TotalSteps,
                "le jitter ne doit pas changer la simulation");
        }

        [Test]
        public void Une_Horloge_Qui_Recule_Est_Ignoree()
        {
            var t = new FixedTimestep(tickRate: 64.0, maxStepsPerFrame: 1000);

            t.AdvanceToSeconds(1.0);
            long before = t.TotalSteps;

            ClassicAssert.AreEqual(0, t.AdvanceToSeconds(0.5), "un retour en arriere ne simule rien");
            ClassicAssert.AreEqual(before, t.TotalSteps);
        }

        [Test]
        public void Reset_Remet_Tout_A_Zero()
        {
            var t = new FixedTimestep(tickRate: 64.0, maxStepsPerFrame: 10000);
            t.AdvanceToSeconds(1.0);

            t.Reset();

            ClassicAssert.AreEqual(0L, t.TotalSteps);
            ClassicAssert.AreEqual(0L, t.SkippedSteps);
            ClassicAssert.AreEqual(0.0, t.SimulatedTime, 1e-12);
            ClassicAssert.AreEqual(0f, t.Alpha);
        }

        [Test]
        public void Un_Tickrate_Invalide_Est_Refuse()
        {
            // Assert.Throws existe toujours en NUnit 4 : ce n'est pas une
            // assertion du modele classique supprime.
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FixedTimestep(tickRate: 0.0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FixedTimestep(tickRate: -64.0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FixedTimestep(tickRate: double.NaN));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FixedTimestep(64.0, maxStepsPerFrame: 0));
        }

        [Test]
        public void Un_Timespan_Se_Passe_Directement()
        {
            // C'est ainsi que la boucle du moteur l'utilisera : Stopwatch.Elapsed.
            var t = new FixedTimestep(tickRate: 64.0, maxStepsPerFrame: 10000);

            int steps = t.AdvanceTo(System.TimeSpan.FromSeconds(1.0));

            ClassicAssert.AreEqual(64, steps);
        }
    }
}
