// ===========================================================================
//  Tests du coeur balistique.
//
//  Verifient surtout des PROPRIETES DE GAMEPLAY, pas juste des formules :
//  un headshot de fusil doit tuer meme avec armure, la premiere balle doit
//  etre parfaitement precise, le pattern doit etre reproductible.
// ===========================================================================

using NUnit.Framework;
using Exarion.Core;

namespace Exarion.Tests
{
    public class BallisticsTests
    {
        private const float Eps = 0.001f;

        // --- Zones ------------------------------------------------------

        [Test]
        public void La_Tete_Vaut_Quatre_Fois_Le_Torse()
        {
            Assert.AreEqual(4.0f, Ballistics.GetZoneMultiplier(HitZone.Head), Eps);
            Assert.AreEqual(1.0f, Ballistics.GetZoneMultiplier(HitZone.Chest), Eps);
            Assert.AreEqual(1.25f, Ballistics.GetZoneMultiplier(HitZone.Stomach), Eps);
            Assert.AreEqual(0.75f, Ballistics.GetZoneMultiplier(HitZone.Legs), Eps);
        }

        // --- Attenuation ------------------------------------------------

        [Test]
        public void Pas_Dattenuation_Avant_La_Distance_De_Depart()
        {
            var w = Arsenal.Rifle;
            Assert.AreEqual(1f, Ballistics.GetFalloffMultiplier(w, 0f), Eps);
            Assert.AreEqual(1f, Ballistics.GetFalloffMultiplier(w, w.FalloffStart), Eps);
        }

        [Test]
        public void Attenuation_Plafonnee_Au_Dela_De_La_Distance_Max()
        {
            var w = Arsenal.Rifle;
            Assert.AreEqual(w.FalloffMinMultiplier, Ballistics.GetFalloffMultiplier(w, w.FalloffEnd), Eps);
            Assert.AreEqual(w.FalloffMinMultiplier, Ballistics.GetFalloffMultiplier(w, 500f), Eps);
        }

        [Test]
        public void Attenuation_Lineaire_Entre_Les_Deux_Bornes()
        {
            var w = Arsenal.Rifle;   // 30 m -> 70 m, plancher 0.65
            float mid = (w.FalloffStart + w.FalloffEnd) * 0.5f;
            float expected = 1f + (w.FalloffMinMultiplier - 1f) * 0.5f;

            Assert.AreEqual(expected, Ballistics.GetFalloffMultiplier(w, mid), Eps);
        }

        // --- Degats : proprietes de gameplay ----------------------------

        [Test]
        public void Headshot_De_Fusil_Tue_Meme_Avec_Armure()
        {
            int dmg = Ballistics.ComputeDamage(Arsenal.Rifle, HitZone.Head, 10f, targetHasArmor: true);
            Assert.Greater(dmg, 100, "un headshot de fusil doit tuer un joueur a 100 PV, armure comprise");
        }

        [Test]
        public void Le_Sniper_Tue_En_Un_Coup_Au_Torse()
        {
            int dmg = Ballistics.ComputeDamage(Arsenal.Sniper, HitZone.Chest, 30f, targetHasArmor: true);
            Assert.GreaterOrEqual(dmg, 100, "le sniper doit tuer d'une balle au torse");
        }

        [Test]
        public void Le_Pistolet_De_Depart_Ne_Tue_Pas_En_Un_Coup_Au_Torse()
        {
            int dmg = Ballistics.ComputeDamage(Arsenal.Pistol9mm, HitZone.Chest, 10f, targetHasArmor: false);
            Assert.Less(dmg, 100, "le pistolet de depart doit rester faible");
        }

        [Test]
        public void Larmure_Reduit_Les_Degats()
        {
            int sans = Ballistics.ComputeDamage(Arsenal.Rifle, HitZone.Chest, 10f, targetHasArmor: false);
            int avec = Ballistics.ComputeDamage(Arsenal.Rifle, HitZone.Chest, 10f, targetHasArmor: true);

            Assert.Less(avec, sans);
        }

        [Test]
        public void Les_Jambes_Font_Moins_Mal_Que_Le_Torse()
        {
            int torse = Ballistics.ComputeDamage(Arsenal.Rifle, HitZone.Chest, 10f, false);
            int jambes = Ballistics.ComputeDamage(Arsenal.Rifle, HitZone.Legs, 10f, false);

            Assert.Less(jambes, torse);
        }

        [Test]
        public void Les_Degats_Ne_Sont_Jamais_Negatifs()
        {
            foreach (var w in Arsenal.All)
            {
                int dmg = Ballistics.ComputeDamage(w, HitZone.Legs, 10000f, true);
                Assert.GreaterOrEqual(dmg, 0, $"{w.Id} produit des degats negatifs");
            }
        }

        // --- Dispersion --------------------------------------------------

        [Test]
        public void Accroupi_Est_Plus_Precis_Quimmobile_Qui_Est_Plus_Precis_Quen_Marche()
        {
            var w = Arsenal.Rifle;

            float crouch = Ballistics.GetSpread(w, StanceState.Crouching);
            float still = Ballistics.GetSpread(w, StanceState.Still);
            float walk = Ballistics.GetSpread(w, StanceState.Walking);
            float run = Ballistics.GetSpread(w, StanceState.Running);
            float air = Ballistics.GetSpread(w, StanceState.Airborne);

            Assert.Less(crouch, still);
            Assert.Less(still, walk);
            Assert.Less(walk, run);
            Assert.Less(run, air, "tirer en l'air doit etre le pire cas");
        }

        [Test]
        public void Toutes_Les_Armes_A_Feu_Respectent_La_Hierarchie_De_Dispersion()
        {
            foreach (var w in Arsenal.All)
            {
                if (w.Id == "knife")
                    continue;   // le couteau n'a pas de dispersion

                Assert.LessOrEqual(Ballistics.GetSpread(w, StanceState.Crouching),
                                   Ballistics.GetSpread(w, StanceState.Still), $"{w.Id}");
                Assert.Less(Ballistics.GetSpread(w, StanceState.Still),
                            Ballistics.GetSpread(w, StanceState.Running), $"{w.Id}");
            }
        }

        // --- Recul --------------------------------------------------------

        [Test]
        public void La_Premiere_Balle_Est_Parfaitement_Precise()
        {
            foreach (var w in Arsenal.All)
            {
                Vec2 first = w.Recoil.GetOffset(0);
                Assert.AreEqual(0f, first.Magnitude, Eps,
                    $"{w.Id} : la premiere balle doit partir pile sur le reticule");
            }
        }

        [Test]
        public void Le_Pattern_Est_Reproductible()
        {
            var w = Arsenal.Rifle;

            for (int i = 0; i < w.Recoil.Length; i++)
                Assert.AreEqual(w.Recoil.GetOffset(i), w.Recoil.GetOffset(i),
                    "le meme index doit toujours donner le meme point");
        }

        [Test]
        public void Le_Pattern_Sature_Au_Dela_Du_Dernier_Point()
        {
            var w = Arsenal.Rifle;
            Vec2 last = w.Recoil.GetOffset(w.Recoil.Length - 1);

            Assert.AreEqual(last, w.Recoil.GetOffset(w.Recoil.Length));
            Assert.AreEqual(last, w.Recoil.GetOffset(9999));
        }

        [Test]
        public void Le_Fusil_Monte_Puis_Part_Sur_Les_Cotes()
        {
            var w = Arsenal.Rifle;

            // Les 9 premieres balles montent de facon monotone.
            for (int i = 1; i < 9; i++)
            {
                Assert.Greater(w.Recoil.GetOffset(i).Y, w.Recoil.GetOffset(i - 1).Y,
                    $"balle {i} : la montee doit etre continue");
            }

            // Puis la derive horizontale devient significative.
            float maxLateral = 0f;
            for (int i = 9; i < w.Recoil.Length; i++)
            {
                float ax = w.Recoil.GetOffset(i).X;
                if (ax < 0f) ax = -ax;
                if (ax > maxLateral) maxLateral = ax;
            }

            Assert.Greater(maxLateral, 1.5f, "le pattern doit vraiment partir sur les cotes");
        }

        [Test]
        public void Le_Tracker_Avance_Puis_Se_Reinitialise_Apres_Une_Pause()
        {
            var w = Arsenal.Rifle;
            var t = new RecoilTracker();

            t.OnShot(w, 0.0f);
            Assert.AreEqual(0, t.ShotIndex, "premiere balle d'une rafale");

            t.OnShot(w, 0.1f);
            Assert.AreEqual(1, t.ShotIndex);

            t.OnShot(w, 0.2f);
            Assert.AreEqual(2, t.ShotIndex);

            // Pause plus longue que RecoilResetSeconds (0.4 s).
            t.OnShot(w, 1.0f);
            Assert.AreEqual(0, t.ShotIndex, "la rafale doit repartir de zero");
        }

        [Test]
        public void Le_Tracker_Se_Reinitialise_Aussi_Sans_Tirer()
        {
            var w = Arsenal.Rifle;
            var t = new RecoilTracker();

            t.OnShot(w, 0f);
            t.OnShot(w, 0.1f);
            Assert.AreEqual(1, t.ShotIndex);

            t.Tick(w, 0.2f);
            Assert.AreEqual(1, t.ShotIndex, "trop tot pour se reinitialiser");

            t.Tick(w, 0.6f);
            Assert.AreEqual(0, t.ShotIndex);
        }

        // --- Coherence de l'arsenal ---------------------------------------

        [Test]
        public void Larsenal_Est_Coherent()
        {
            foreach (var w in Arsenal.All)
            {
                Assert.IsNotEmpty(w.Id);
                Assert.IsNotEmpty(w.Label);
                Assert.Greater(w.BaseDamage, 0f, $"{w.Id}");
                Assert.Greater(w.FireRateRpm, 0f, $"{w.Id}");
                Assert.Greater(w.SecondsBetweenShots, 0f, $"{w.Id}");
                Assert.GreaterOrEqual(w.FalloffEnd, w.FalloffStart, $"{w.Id}");
                Assert.IsNotNull(w.Recoil, $"{w.Id}");
                Assert.Greater(w.Recoil.Length, 0, $"{w.Id}");
            }
        }

        [Test]
        public void Chaque_Arme_Achetable_Existe_Dans_Larsenal()
        {
            foreach (var item in BuyCatalog.Items)
            {
                if (item.Slot == WeaponSlot.Utility)
                    continue;   // grenades : pas d'entree d'arsenal

                Assert.IsNotNull(Arsenal.Get(item.Id),
                    $"l'article '{item.Id}' est vendu mais absent de l'arsenal");
            }
        }
    }
}
