// ===========================================================================
//  Exarion 1 - Porte-monnaie, inventaire, validation d'achat
//
//  Regle de securite heritee du prototype Source, et non negociable :
//  le client n'envoie qu'un IDENTIFIANT D'ARTICLE. Le prix, le droit
//  d'acheter et l'effet sont decides ici, cote serveur. Un client modifie
//  ne doit jamais pouvoir s'offrir une arme gratuite.
// ===========================================================================

using System.Collections.Generic;

namespace Exarion.Core
{
    public sealed class Wallet
    {
        public int Balance { get; private set; }
        public int Max { get; }

        public Wallet(int start, int max)
        {
            Max = max;
            Balance = Clamp(start);
        }

        private int Clamp(int v) => v < 0 ? 0 : (v > Max ? Max : v);

        public void Add(int amount) => Balance = Clamp(Balance + amount);

        public void SetTo(int amount) => Balance = Clamp(amount);

        public bool CanAfford(int price) => Balance >= price;

        /// <summary>Debite si les fonds suffisent. Retourne false sans rien changer sinon.</summary>
        public bool TrySpend(int price)
        {
            if (price < 0 || Balance < price)
                return false;

            Balance -= price;
            return true;
        }
    }

    public enum PurchaseResult
    {
        Success = 0,
        /// <summary>Article inconnu du catalogue.</summary>
        UnknownItem,
        /// <summary>Hors phase d'achat, ou joueur mort, ou sans equipe.</summary>
        NotAllowedNow,
        NotEnoughMoney,
        /// <summary>Deja possede et rien a recharger.</summary>
        AlreadyOwned,
    }

    /// <summary>
    /// Equipement porte. Un seul primaire et un seul secondaire : acheter
    /// un fusil quand on en a deja un remplace l'ancien, comme dans CS.
    /// </summary>
    public sealed class Loadout
    {
        public string Melee = BuyCatalog.StartingMelee;
        public string Secondary = BuyCatalog.StartingPistol;
        public string Primary = null;

        /// <summary>Grenades portees, par identifiant.</summary>
        public readonly Dictionary<string, int> Utility = new Dictionary<string, int>();

        /// <summary>Munitions en reserve, par identifiant d'arme.</summary>
        public readonly Dictionary<string, int> Ammo = new Dictionary<string, int>();

        /// <summary>Plafond de grenades du meme type.</summary>
        public const int MaxPerUtility = 2;

        public void ResetToStarting()
        {
            Melee = BuyCatalog.StartingMelee;
            Secondary = BuyCatalog.StartingPistol;
            Primary = null;
            Utility.Clear();
            Ammo.Clear();
            Ammo[BuyCatalog.StartingPistol] = BuyCatalog.StartingPistolAmmo;
        }

        public bool Has(string weaponId)
            => Melee == weaponId || Secondary == weaponId || Primary == weaponId || Utility.ContainsKey(weaponId);

        public int GetUtilityCount(string id)
            => Utility.TryGetValue(id, out int n) ? n : 0;

        public int GetAmmo(string weaponId)
            => Ammo.TryGetValue(weaponId, out int n) ? n : 0;

        public void AddAmmo(string weaponId, int amount)
        {
            if (string.IsNullOrEmpty(weaponId) || amount <= 0)
                return;

            Ammo[weaponId] = GetAmmo(weaponId) + amount;
        }
    }

    public static class BuyService
    {
        /// <summary>
        /// Tente un achat. Ne modifie wallet et loadout QUE si le resultat
        /// est Success : pas d'etat a moitie applique.
        /// </summary>
        /// <param name="canBuyNow">
        /// Vrai si la phase d'achat est ouverte et le joueur eligible.
        /// C'est a l'appelant (serveur) de le calculer.
        /// </param>
        public static PurchaseResult TryBuy(Wallet wallet, Loadout loadout, string itemId, bool canBuyNow)
        {
            if (wallet == null || loadout == null)
                return PurchaseResult.UnknownItem;

            BuyItem item = BuyCatalog.Find(itemId);
            if (item == null)
                return PurchaseResult.UnknownItem;

            if (!canBuyNow)
                return PurchaseResult.NotAllowedNow;

            // --- Recharge de munitions : refuse si rien a recharger ---------
            if (item.Id == "ammo_refill")
            {
                if (!wallet.CanAfford(item.Price))
                    return PurchaseResult.NotEnoughMoney;

                wallet.TrySpend(item.Price);
                RefillAllAmmo(loadout);
                return PurchaseResult.Success;
            }

            // --- Grenades : plafonnees par type ----------------------------
            if (item.Slot == WeaponSlot.Utility)
            {
                if (loadout.GetUtilityCount(item.Id) >= Loadout.MaxPerUtility)
                    return PurchaseResult.AlreadyOwned;

                if (!wallet.CanAfford(item.Price))
                    return PurchaseResult.NotEnoughMoney;

                wallet.TrySpend(item.Price);
                loadout.Utility[item.Id] = loadout.GetUtilityCount(item.Id) + 1;
                return PurchaseResult.Success;
            }

            // --- Armes : une par emplacement, remplacement autorise ---------
            bool sameWeaponAlready =
                (item.Slot == WeaponSlot.Primary && loadout.Primary == item.Id) ||
                (item.Slot == WeaponSlot.Secondary && loadout.Secondary == item.Id);

            // Racheter la meme arme ne sert qu'a refaire le plein.
            if (sameWeaponAlready)
            {
                WeaponStats stats = Arsenal.Get(item.Id);
                int maxReserve = stats != null ? stats.ReserveAmmo : item.AmmoOnBuy;

                if (loadout.GetAmmo(item.Id) >= maxReserve)
                    return PurchaseResult.AlreadyOwned;
            }

            if (!wallet.CanAfford(item.Price))
                return PurchaseResult.NotEnoughMoney;

            wallet.TrySpend(item.Price);

            if (item.Slot == WeaponSlot.Primary)
                loadout.Primary = item.Id;
            else if (item.Slot == WeaponSlot.Secondary)
                loadout.Secondary = item.Id;

            loadout.AddAmmo(item.Id, item.AmmoOnBuy);
            return PurchaseResult.Success;
        }

        private static void RefillAllAmmo(Loadout loadout)
        {
            RefillOne(loadout, loadout.Primary);
            RefillOne(loadout, loadout.Secondary);
        }

        private static void RefillOne(Loadout loadout, string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId))
                return;

            WeaponStats stats = Arsenal.Get(weaponId);
            if (stats == null)
                return;

            loadout.Ammo[weaponId] = stats.ReserveAmmo;
        }
    }
}
