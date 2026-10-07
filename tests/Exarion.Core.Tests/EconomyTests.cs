// ===========================================================================
//  Tests du porte-monnaie et de l'achat.
//
//  L'enjeu principal : verifier qu'un achat refuse ne modifie RIEN.
//  Un etat a moitie applique (arme donnee sans debit, ou debit sans arme)
//  serait exploitable.
// ===========================================================================

using NUnit.Framework;
using Exarion.Core;

namespace Exarion.Tests
{
    public class EconomyTests
    {
        private Wallet _w;
        private Loadout _l;

        [SetUp]
        public void SetUp()
        {
            _w = new Wallet(800, 16000);
            _l = new Loadout();
            _l.ResetToStarting();
        }

        // --- Porte-monnaie ------------------------------------------------

        [Test]
        public void Le_Solde_Ne_Descend_Jamais_Sous_Zero()
        {
            var w = new Wallet(100, 16000);
            w.Add(-500);
            ClassicAssert.AreEqual(0, w.Balance);
        }

        [Test]
        public void Le_Solde_Est_Plafonne()
        {
            var w = new Wallet(15000, 16000);
            w.Add(5000);
            ClassicAssert.AreEqual(16000, w.Balance);
        }

        [Test]
        public void Une_Depense_Impossible_Ne_Change_Pas_Le_Solde()
        {
            var w = new Wallet(500, 16000);

            ClassicAssert.IsFalse(w.TrySpend(700));
            ClassicAssert.AreEqual(500, w.Balance, "le solde doit etre intact apres un refus");
        }

        [Test]
        public void Une_Depense_Valide_Debite_Exactement()
        {
            var w = new Wallet(1000, 16000);

            ClassicAssert.IsTrue(w.TrySpend(700));
            ClassicAssert.AreEqual(300, w.Balance);
        }

        // --- Equipement de depart ------------------------------------------

        [Test]
        public void Le_Loadout_De_Depart_Est_Couteau_Plus_Pistolet()
        {
            ClassicAssert.AreEqual(BuyCatalog.StartingMelee, _l.Melee);
            ClassicAssert.AreEqual(BuyCatalog.StartingPistol, _l.Secondary);
            ClassicAssert.IsNull(_l.Primary, "aucune arme principale offerte");
            ClassicAssert.AreEqual(0, _l.Utility.Count, "aucune grenade offerte");
            ClassicAssert.AreEqual(BuyCatalog.StartingPistolAmmo, _l.GetAmmo(BuyCatalog.StartingPistol));
        }

        // --- Achats refuses -------------------------------------------------

        [Test]
        public void Article_Inconnu_Refuse()
        {
            var r = BuyService.TryBuy(_w, _l, "lance_roquettes_nucleaire", canBuyNow: true);

            ClassicAssert.AreEqual(PurchaseResult.UnknownItem, r);
            ClassicAssert.AreEqual(800, _w.Balance, "aucun debit");
        }

        [Test]
        public void Achat_Hors_Phase_Refuse_Sans_Rien_Changer()
        {
            var r = BuyService.TryBuy(_w, _l, "rifle", canBuyNow: false);

            ClassicAssert.AreEqual(PurchaseResult.NotAllowedNow, r);
            ClassicAssert.AreEqual(800, _w.Balance);
            ClassicAssert.IsNull(_l.Primary, "aucune arme ne doit etre donnee");
        }

        [Test]
        public void Achat_Trop_Cher_Refuse_Sans_Rien_Changer()
        {
            // 800 en poche, le fusil en vaut 2700.
            var r = BuyService.TryBuy(_w, _l, "rifle", canBuyNow: true);

            ClassicAssert.AreEqual(PurchaseResult.NotEnoughMoney, r);
            ClassicAssert.AreEqual(800, _w.Balance);
            ClassicAssert.IsNull(_l.Primary);
        }

        // --- Achats valides -------------------------------------------------

        [Test]
        public void Acheter_Un_Fusil_Debite_Et_Equipe()
        {
            _w.Add(5000);   // 5800

            var r = BuyService.TryBuy(_w, _l, "rifle", canBuyNow: true);

            ClassicAssert.AreEqual(PurchaseResult.Success, r);
            ClassicAssert.AreEqual(5800 - 2700, _w.Balance);
            ClassicAssert.AreEqual("rifle", _l.Primary);
            ClassicAssert.Greater(_l.GetAmmo("rifle"), 0, "l'arme doit arriver avec des munitions");
        }

        [Test]
        public void Une_Nouvelle_Arme_Principale_Remplace_Lancienne()
        {
            _w.Add(10000);

            BuyService.TryBuy(_w, _l, "smg", canBuyNow: true);
            ClassicAssert.AreEqual("smg", _l.Primary);

            BuyService.TryBuy(_w, _l, "rifle", canBuyNow: true);
            ClassicAssert.AreEqual("rifle", _l.Primary, "on ne porte qu'une arme principale");
        }

        [Test]
        public void Racheter_La_Meme_Arme_Avec_Le_Plein_Est_Refuse()
        {
            _w.Add(10000);

            BuyService.TryBuy(_w, _l, "rifle", canBuyNow: true);
            int after = _w.Balance;

            var r = BuyService.TryBuy(_w, _l, "rifle", canBuyNow: true);

            ClassicAssert.AreEqual(PurchaseResult.AlreadyOwned, r);
            ClassicAssert.AreEqual(after, _w.Balance, "pas de debit inutile");
        }

        [Test]
        public void Un_Pistolet_Achete_Remplace_Le_Pistolet_De_Depart()
        {
            _w.Add(1000);

            var r = BuyService.TryBuy(_w, _l, "pistol_heavy", canBuyNow: true);

            ClassicAssert.AreEqual(PurchaseResult.Success, r);
            ClassicAssert.AreEqual("pistol_heavy", _l.Secondary);
        }

        // --- Grenades --------------------------------------------------------

        [Test]
        public void Les_Grenades_Saccumulent_Jusquau_Plafond()
        {
            _w.Add(5000);

            ClassicAssert.AreEqual(PurchaseResult.Success, BuyService.TryBuy(_w, _l, "grenade_he", true));
            ClassicAssert.AreEqual(1, _l.GetUtilityCount("grenade_he"));

            ClassicAssert.AreEqual(PurchaseResult.Success, BuyService.TryBuy(_w, _l, "grenade_he", true));
            ClassicAssert.AreEqual(2, _l.GetUtilityCount("grenade_he"));

            var r = BuyService.TryBuy(_w, _l, "grenade_he", true);
            ClassicAssert.AreEqual(PurchaseResult.AlreadyOwned, r, "plafonne a 2");
            ClassicAssert.AreEqual(2, _l.GetUtilityCount("grenade_he"));
        }

        [Test]
        public void Les_Types_De_Grenades_Ont_Des_Plafonds_Independants()
        {
            _w.Add(5000);

            BuyService.TryBuy(_w, _l, "grenade_he", true);
            BuyService.TryBuy(_w, _l, "grenade_he", true);
            BuyService.TryBuy(_w, _l, "grenade_smoke", true);

            ClassicAssert.AreEqual(2, _l.GetUtilityCount("grenade_he"));
            ClassicAssert.AreEqual(1, _l.GetUtilityCount("grenade_smoke"));
        }

        // --- Munitions --------------------------------------------------------

        [Test]
        public void La_Recharge_Refait_Le_Plein_Des_Deux_Armes()
        {
            _w.Add(10000);
            BuyService.TryBuy(_w, _l, "rifle", canBuyNow: true);

            // On vide artificiellement les reserves.
            _l.Ammo["rifle"] = 5;
            _l.Ammo[BuyCatalog.StartingPistol] = 0;

            var r = BuyService.TryBuy(_w, _l, "ammo_refill", canBuyNow: true);

            ClassicAssert.AreEqual(PurchaseResult.Success, r);
            ClassicAssert.AreEqual(Arsenal.Rifle.ReserveAmmo, _l.GetAmmo("rifle"));
            ClassicAssert.AreEqual(Arsenal.Pistol9mm.ReserveAmmo, _l.GetAmmo(BuyCatalog.StartingPistol));
        }

        // --- Scenario complet -------------------------------------------------

        [Test]
        public void Scenario_Achat_Complet_Sur_Un_Round_Riche()
        {
            _w.SetTo(5000);

            ClassicAssert.AreEqual(PurchaseResult.Success, BuyService.TryBuy(_w, _l, "rifle", true));         // -2700
            ClassicAssert.AreEqual(PurchaseResult.Success, BuyService.TryBuy(_w, _l, "pistol_heavy", true));  // -700
            ClassicAssert.AreEqual(PurchaseResult.Success, BuyService.TryBuy(_w, _l, "grenade_he", true));    // -300
            ClassicAssert.AreEqual(PurchaseResult.Success, BuyService.TryBuy(_w, _l, "grenade_flash", true)); // -200

            ClassicAssert.AreEqual(5000 - 2700 - 700 - 300 - 200, _w.Balance);
            ClassicAssert.AreEqual("rifle", _l.Primary);
            ClassicAssert.AreEqual("pistol_heavy", _l.Secondary);
            ClassicAssert.AreEqual(1, _l.GetUtilityCount("grenade_he"));
            ClassicAssert.AreEqual(1, _l.GetUtilityCount("grenade_flash"));
        }

        [Test]
        public void Scenario_Eco_Round_On_Ne_Peut_Presque_Rien_Acheter()
        {
            _w.SetTo(800);   // apres une defaite seche

            ClassicAssert.AreEqual(PurchaseResult.NotEnoughMoney, BuyService.TryBuy(_w, _l, "rifle", true));
            ClassicAssert.AreEqual(PurchaseResult.NotEnoughMoney, BuyService.TryBuy(_w, _l, "sniper", true));
            ClassicAssert.AreEqual(PurchaseResult.Success, BuyService.TryBuy(_w, _l, "pistol_heavy", true));

            ClassicAssert.AreEqual(100, _w.Balance);
        }
    }
}
