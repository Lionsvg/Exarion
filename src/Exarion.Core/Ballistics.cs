// ===========================================================================
//  Exarion 1 - Coeur balistique
//
//  Trois mecaniques, toutes reprises de la logique CS :
//
//  1. RECUL FIXE ET MEMORISABLE. La n-ieme balle d'une rafale part toujours
//     au meme endroit. C'est ce qui rend le "spray control" apprenable, et
//     c'est la difference fondamentale avec un recul aleatoire.
//
//  2. DISPERSION SELON L'ETAT. Immobile et accroupi = precis. En marche =
//     degrade. En l'air = inutilisable. C'est ce qui force le jeu de position
//     et l'arret avant le tir.
//
//  3. ZONES DE DEGATS. Tete x4. Le headshot doit tuer, sinon le jeu n'a pas
//     le meme rythme.
//
//  C# pur, testable hors moteur.
// ===========================================================================

using System;

namespace Exarion.Core
{
    public enum HitZone
    {
        Head = 0,
        Chest,
        Stomach,
        Legs,
    }

    /// <summary>Etat de deplacement du tireur au moment du tir.</summary>
    public enum StanceState
    {
        Crouching = 0,
        Still,
        Walking,     // marche lente (touche walk)
        Running,
        Airborne,
    }

    /// <summary>
    /// Pattern de recul : suite fixe de deplacements du reticule, un par balle.
    /// Au-dela du dernier point, on reste sur le dernier (l'arme "sature").
    /// </summary>
    public sealed class RecoilPattern
    {
        private readonly Vec2[] _points;

        public RecoilPattern(Vec2[] points)
        {
            if (points == null || points.Length == 0)
                throw new ArgumentException("Un pattern de recul doit avoir au moins un point.", nameof(points));

            _points = points;
        }

        public int Length => _points.Length;

        /// <summary>Deplacement cumule du reticule pour la balle numero shotIndex (0 = premiere).</summary>
        public Vec2 GetOffset(int shotIndex)
        {
            if (shotIndex < 0)
                shotIndex = 0;
            if (shotIndex >= _points.Length)
                shotIndex = _points.Length - 1;

            return _points[shotIndex];
        }
    }

    public sealed class WeaponStats
    {
        public string Id = "";
        public string Label = "";

        // --- Degats ---
        public float BaseDamage = 30f;
        /// <summary>Part des degats conservee contre un adversaire avec armure. 1 = ignore l'armure.</summary>
        public float ArmorPenetration = 0.775f;

        // --- Cadence ---
        public float FireRateRpm = 600f;
        public float SecondsBetweenShots => 60f / Math.Max(1f, FireRateRpm);

        // --- Munitions ---
        public int MagazineSize = 30;
        public int ReserveAmmo = 90;
        public float ReloadSeconds = 2.5f;

        // --- Attenuation avec la distance (en metres) ---
        public float FalloffStart = 25f;
        public float FalloffEnd = 60f;
        /// <summary>Multiplicateur de degats au-dela de FalloffEnd.</summary>
        public float FalloffMinMultiplier = 0.55f;

        // --- Dispersion (en degres de demi-angle) ---
        public float SpreadCrouching = 0.02f;
        public float SpreadStill = 0.04f;
        public float SpreadWalking = 0.9f;
        public float SpreadRunning = 3.5f;
        public float SpreadAirborne = 9f;

        // --- Recul ---
        public RecoilPattern Recoil = new RecoilPattern(new[] { Vec2.Zero });
        /// <summary>Delai sans tirer apres lequel le pattern repart de zero.</summary>
        public float RecoilResetSeconds = 0.4f;
    }

    public static class Ballistics
    {
        /// <summary>
        /// Multiplicateurs par zone. La tete a x4 : un fusil a 30 de degats de
        /// base tue net a courte portee, ce qui est le rythme recherche.
        /// </summary>
        public static float GetZoneMultiplier(HitZone zone)
        {
            switch (zone)
            {
                case HitZone.Head: return 4.0f;
                case HitZone.Chest: return 1.0f;
                case HitZone.Stomach: return 1.25f;
                case HitZone.Legs: return 0.75f;
                default: return 1.0f;
            }
        }

        /// <summary>Dispersion de base pour un etat donne, en degres.</summary>
        public static float GetSpread(WeaponStats w, StanceState stance)
        {
            switch (stance)
            {
                case StanceState.Crouching: return w.SpreadCrouching;
                case StanceState.Still: return w.SpreadStill;
                case StanceState.Walking: return w.SpreadWalking;
                case StanceState.Running: return w.SpreadRunning;
                case StanceState.Airborne: return w.SpreadAirborne;
                default: return w.SpreadStill;
            }
        }

        /// <summary>
        /// Multiplicateur d'attenuation a une distance donnee.
        /// Plein degats jusqu'a FalloffStart, decroissance lineaire ensuite,
        /// plancher a FalloffMinMultiplier.
        /// </summary>
        public static float GetFalloffMultiplier(WeaponStats w, float distanceMeters)
        {
            if (distanceMeters <= w.FalloffStart)
                return 1f;

            if (distanceMeters >= w.FalloffEnd)
                return w.FalloffMinMultiplier;

            float span = w.FalloffEnd - w.FalloffStart;
            if (span <= 0f)
                return w.FalloffMinMultiplier;

            float t = (distanceMeters - w.FalloffStart) / span;
            return 1f + (w.FalloffMinMultiplier - 1f) * t;
        }

        /// <summary>
        /// Degats finaux d'un impact. Arrondi a l'entier inferieur, comme CS :
        /// les joueurs raisonnent en points de vie entiers.
        /// </summary>
        public static int ComputeDamage(WeaponStats w, HitZone zone, float distanceMeters, bool targetHasArmor)
        {
            float dmg = w.BaseDamage;
            dmg *= GetZoneMultiplier(zone);
            dmg *= GetFalloffMultiplier(w, distanceMeters);

            if (targetHasArmor)
                dmg *= w.ArmorPenetration;

            if (dmg < 0f)
                dmg = 0f;

            return (int)Math.Floor(dmg);
        }
    }

    /// <summary>
    /// Suit la position du joueur dans le pattern de recul.
    /// Etat par arme et par joueur ; c'est le serveur qui fait autorite.
    /// </summary>
    public sealed class RecoilTracker
    {
        private float _lastShotTime = float.NegativeInfinity;

        public int ShotIndex { get; private set; }

        /// <summary>A appeler a chaque tir. Fait avancer le pattern.</summary>
        public void OnShot(WeaponStats w, float now)
        {
            // Si on a relache assez longtemps, la rafale repart de zero.
            if (now - _lastShotTime >= w.RecoilResetSeconds)
                ShotIndex = 0;
            else
                ShotIndex++;

            _lastShotTime = now;
        }

        /// <summary>A appeler chaque frame quand on ne tire pas.</summary>
        public void Tick(WeaponStats w, float now)
        {
            if (now - _lastShotTime >= w.RecoilResetSeconds)
                ShotIndex = 0;
        }

        public Vec2 GetCurrentOffset(WeaponStats w) => w.Recoil.GetOffset(ShotIndex);

        public void Reset()
        {
            ShotIndex = 0;
            _lastShotTime = float.NegativeInfinity;
        }
    }
}
