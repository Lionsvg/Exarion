// ===========================================================================
//  Exarion 1 - Catalogue d'achat
//
//  Les prix viennent du prototype Source, eux-memes calques sur CS.
//  Les identifiants d'armes sont desormais les notres : plus aucune
//  reference a HL2 (weapon_ar2, weapon_smg1...), ces assets appartenaient
//  a Valve et ne sont pas portables.
// ===========================================================================

using System.Collections.Generic;

namespace Exarion.Core
{
    public enum WeaponSlot
    {
        Melee = 0,
        Secondary,   // pistolets
        Primary,     // fusils, SMG, sniper
        Utility,     // grenades
    }

    public sealed class BuyItem
    {
        public string Id;
        public string Label;
        public int Price;
        public WeaponSlot Slot;
        public int AmmoOnBuy;

        public BuyItem(string id, string label, int price, WeaponSlot slot, int ammo)
        {
            Id = id;
            Label = label;
            Price = price;
            Slot = slot;
            AmmoOnBuy = ammo;
        }
    }

    public static class BuyCatalog
    {
        /// <summary>Equipement de depart, offert a chaque manche.</summary>
        public const string StartingMelee = "knife";
        public const string StartingPistol = "pistol_9mm";
        public const int StartingPistolAmmo = 60;

        public static readonly IReadOnlyList<BuyItem> Items = new List<BuyItem>
        {
            //          id                libelle             prix   emplacement          munitions
            new BuyItem("pistol_heavy",   "Pistolet lourd",     700, WeaponSlot.Secondary,  35),
            new BuyItem("smg",            "SMG",               1250, WeaponSlot.Primary,    90),
            new BuyItem("shotgun",        "Fusil a pompe",     1800, WeaponSlot.Primary,    32),
            new BuyItem("rifle",          "Fusil d'assaut",    2700, WeaponSlot.Primary,    90),
            new BuyItem("sniper",         "Sniper",            4750, WeaponSlot.Primary,    30),
            new BuyItem("grenade_he",     "Grenade",            300, WeaponSlot.Utility,     1),
            new BuyItem("grenade_smoke",  "Fumigene",           300, WeaponSlot.Utility,     1),
            new BuyItem("grenade_flash",  "Flash",              200, WeaponSlot.Utility,     1),
            new BuyItem("ammo_refill",    "Munitions",          200, WeaponSlot.Utility,     0),
        };

        public static BuyItem Find(string id)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Id == id)
                    return Items[i];
            }
            return null;
        }

        /// <summary>Index dans le menu (0-based) -> article. null si hors bornes.</summary>
        public static BuyItem ByIndex(int index)
        {
            if (index < 0 || index >= Items.Count)
                return null;
            return Items[index];
        }
    }
}
