// ===========================================================================
//  Exarion 1 - Arsenal
//
//  Les patterns de recul sont des suites FIXES : la balle n de chaque rafale
//  part toujours au meme endroit. Le joueur peut donc les apprendre et les
//  compenser a la souris - c'est la competence centrale d'un jeu facon CS.
//
//  Forme des patterns, en unites de degres (X = horizontal, Y = vertical) :
//    - fusil d'assaut : monte fort sur ~9 balles, part a gauche, revient a
//      droite. C'est le "T inverse" que tout joueur de CS connait par coeur.
//    - SMG : montee reguliere, faible amplitude, tres controlable.
//    - sniper / shotgun : coup unique, gros recul, pas de pattern a apprendre.
//
//  TOUTES ces valeurs sont des points de depart. Elles n'ont jamais ete
//  testees en jeu et seront forcement retouchees au playtest.
// ===========================================================================

using System.Collections.Generic;

namespace Exarion.Core
{
    public static class Arsenal
    {
        // -------------------------------------------------------------------
        // Patterns
        // -------------------------------------------------------------------

        /// <summary>Fusil d'assaut : montee, puis gauche, puis droite.</summary>
        private static RecoilPattern RiflePattern()
        {
            return new RecoilPattern(new[]
            {
                new Vec2( 0.00f, 0.00f),   // 1re balle : parfaitement precise
                new Vec2( 0.05f, 0.55f),
                new Vec2( 0.02f, 1.20f),
                new Vec2(-0.05f, 1.85f),
                new Vec2(-0.10f, 2.45f),
                new Vec2(-0.15f, 2.95f),
                new Vec2(-0.20f, 3.35f),
                new Vec2(-0.30f, 3.65f),
                new Vec2(-0.45f, 3.85f),   // fin de la montee
                new Vec2(-0.85f, 3.95f),   // depart a gauche
                new Vec2(-1.35f, 4.00f),
                new Vec2(-1.80f, 4.00f),
                new Vec2(-2.05f, 3.95f),
                new Vec2(-1.85f, 3.90f),   // retour
                new Vec2(-1.30f, 3.85f),
                new Vec2(-0.55f, 3.85f),
                new Vec2( 0.35f, 3.90f),   // passage a droite
                new Vec2( 1.20f, 3.95f),
                new Vec2( 1.90f, 4.00f),
                new Vec2( 2.35f, 4.05f),
                new Vec2( 2.50f, 4.10f),
                new Vec2( 2.30f, 4.15f),
                new Vec2( 1.75f, 4.20f),
                new Vec2( 1.00f, 4.25f),
                new Vec2( 0.20f, 4.30f),
                new Vec2(-0.55f, 4.35f),
                new Vec2(-1.15f, 4.40f),
                new Vec2(-1.50f, 4.45f),
                new Vec2(-1.60f, 4.50f),
                new Vec2(-1.55f, 4.55f),
            });
        }

        /// <summary>SMG : montee douce et reguliere, faible derive.</summary>
        private static RecoilPattern SmgPattern()
        {
            var pts = new List<Vec2>();
            for (int i = 0; i < 25; i++)
            {
                float t = i / 24f;
                float y = 2.6f * (1f - (1f - t) * (1f - t));      // montee qui sature
                float x = (i % 2 == 0 ? 1f : -1f) * 0.25f * t;    // leger zigzag
                pts.Add(new Vec2(x, y));
            }
            return new RecoilPattern(pts.ToArray());
        }

        /// <summary>Coup unique : un seul point, gros recul vertical.</summary>
        private static RecoilPattern SingleShotPattern(float kick)
        {
            return new RecoilPattern(new[] { Vec2.Zero, new Vec2(0f, kick) });
        }

        // -------------------------------------------------------------------
        // Armes
        // -------------------------------------------------------------------

        public static readonly WeaponStats Knife = new WeaponStats
        {
            Id = "knife", Label = "Couteau",
            BaseDamage = 55f, ArmorPenetration = 0.85f,
            FireRateRpm = 120f, MagazineSize = 0, ReserveAmmo = 0,
            FalloffStart = 2f, FalloffEnd = 2.5f, FalloffMinMultiplier = 0f,
            SpreadCrouching = 0f, SpreadStill = 0f, SpreadWalking = 0f,
            SpreadRunning = 0f, SpreadAirborne = 0f,
            Recoil = new RecoilPattern(new[] { Vec2.Zero }),
        };

        public static readonly WeaponStats Pistol9mm = new WeaponStats
        {
            Id = "pistol_9mm", Label = "Pistolet 9mm",
            BaseDamage = 26f, ArmorPenetration = 0.60f,
            FireRateRpm = 400f, MagazineSize = 20, ReserveAmmo = 60,
            ReloadSeconds = 2.0f,
            FalloffStart = 15f, FalloffEnd = 40f, FalloffMinMultiplier = 0.55f,
            SpreadCrouching = 0.05f, SpreadStill = 0.10f, SpreadWalking = 1.1f,
            SpreadRunning = 3.8f, SpreadAirborne = 9f,
            Recoil = SingleShotPattern(1.1f), RecoilResetSeconds = 0.25f,
        };

        public static readonly WeaponStats PistolHeavy = new WeaponStats
        {
            Id = "pistol_heavy", Label = "Pistolet lourd",
            BaseDamage = 63f, ArmorPenetration = 0.70f,
            FireRateRpm = 260f, MagazineSize = 7, ReserveAmmo = 35,
            ReloadSeconds = 2.2f,
            FalloffStart = 25f, FalloffEnd = 60f, FalloffMinMultiplier = 0.60f,
            SpreadCrouching = 0.06f, SpreadStill = 0.12f, SpreadWalking = 1.6f,
            SpreadRunning = 4.5f, SpreadAirborne = 11f,
            Recoil = SingleShotPattern(2.6f), RecoilResetSeconds = 0.35f,
        };

        public static readonly WeaponStats Smg = new WeaponStats
        {
            Id = "smg", Label = "SMG",
            BaseDamage = 27f, ArmorPenetration = 0.60f,
            FireRateRpm = 750f, MagazineSize = 30, ReserveAmmo = 90,
            ReloadSeconds = 2.2f,
            FalloffStart = 15f, FalloffEnd = 35f, FalloffMinMultiplier = 0.50f,
            SpreadCrouching = 0.03f, SpreadStill = 0.06f, SpreadWalking = 0.7f,
            SpreadRunning = 3.0f, SpreadAirborne = 8f,
            Recoil = SmgPattern(), RecoilResetSeconds = 0.35f,
        };

        public static readonly WeaponStats Shotgun = new WeaponStats
        {
            Id = "shotgun", Label = "Fusil a pompe",
            BaseDamage = 26f, ArmorPenetration = 0.75f,   // par plomb
            FireRateRpm = 85f, MagazineSize = 8, ReserveAmmo = 32,
            ReloadSeconds = 3.5f,
            FalloffStart = 6f, FalloffEnd = 18f, FalloffMinMultiplier = 0.15f,
            SpreadCrouching = 0.5f, SpreadStill = 0.7f, SpreadWalking = 1.4f,
            SpreadRunning = 4.0f, SpreadAirborne = 10f,
            Recoil = SingleShotPattern(4.0f), RecoilResetSeconds = 0.5f,
        };

        public static readonly WeaponStats Rifle = new WeaponStats
        {
            Id = "rifle", Label = "Fusil d'assaut",
            BaseDamage = 36f, ArmorPenetration = 0.775f,
            FireRateRpm = 600f, MagazineSize = 30, ReserveAmmo = 90,
            ReloadSeconds = 2.5f,
            FalloffStart = 30f, FalloffEnd = 70f, FalloffMinMultiplier = 0.65f,
            SpreadCrouching = 0.02f, SpreadStill = 0.04f, SpreadWalking = 0.9f,
            SpreadRunning = 3.5f, SpreadAirborne = 9f,
            Recoil = RiflePattern(), RecoilResetSeconds = 0.4f,
        };

        public static readonly WeaponStats Sniper = new WeaponStats
        {
            Id = "sniper", Label = "Sniper",
            BaseDamage = 115f, ArmorPenetration = 0.97f,  // one-shot torse
            FireRateRpm = 41f, MagazineSize = 10, ReserveAmmo = 30,
            ReloadSeconds = 3.7f,
            FalloffStart = 100f, FalloffEnd = 200f, FalloffMinMultiplier = 0.90f,
            SpreadCrouching = 0.0f, SpreadStill = 0.0f, SpreadWalking = 4.0f,
            SpreadRunning = 9.0f, SpreadAirborne = 20f,
            Recoil = SingleShotPattern(6.0f), RecoilResetSeconds = 0.8f,
        };

        private static readonly Dictionary<string, WeaponStats> _byId = new Dictionary<string, WeaponStats>
        {
            { Knife.Id, Knife },
            { Pistol9mm.Id, Pistol9mm },
            { PistolHeavy.Id, PistolHeavy },
            { Smg.Id, Smg },
            { Shotgun.Id, Shotgun },
            { Rifle.Id, Rifle },
            { Sniper.Id, Sniper },
        };

        /// <summary>Retourne null si l'identifiant est inconnu.</summary>
        public static WeaponStats? Get(string? id)
        {
            return id != null && _byId.TryGetValue(id, out var w) ? w : null;
        }

        public static IEnumerable<WeaponStats> All => _byId.Values;
    }
}
