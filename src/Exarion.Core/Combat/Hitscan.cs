// ===========================================================================
//  Exarion - Resolution d'un tir hitscan
//
//  La direction finale d'une balle combine deux choses de nature opposee :
//
//    RECUL     deterministe, memorisable. La n-ieme balle d'une rafale part
//              toujours au meme endroit. C'est la competence du joueur.
//    DISPERSION aleatoire, dans un cone dont l'angle depend de la posture.
//              C'est ce qui punit le tir en mouvement.
//
//  L'aleatoire passe par Rng, deterministe et initialise par (tick, joueur,
//  numero de balle) : le client et le serveur tirent la MEME balle. Sans ca,
//  le client verrait toucher la ou le serveur voit rater.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Deterministic;
using Exarion.Core.Physics;

namespace Exarion.Core.Combat
{
    public readonly struct ShotResult
    {
        /// <summary>Direction reellement empruntee, recul et dispersion compris.</summary>
        public readonly Vector3 Direction;
        public readonly bool HitAnything;
        public readonly Vector3 ImpactPoint;
        public readonly float Distance;

        /// <summary>-1 si aucune cible touchee (decor ou vide).</summary>
        public readonly int TargetId;
        public readonly HitZone Zone;
        public readonly int Damage;
        public readonly bool Killed;

        public bool HitTarget => TargetId >= 0;

        public ShotResult(Vector3 direction, bool hitAnything, Vector3 impact, float distance,
                          int targetId, HitZone zone, int damage, bool killed)
        {
            Direction = direction;
            HitAnything = hitAnything;
            ImpactPoint = impact;
            Distance = distance;
            TargetId = targetId;
            Zone = zone;
            Damage = damage;
            Killed = killed;
        }
    }

    public static class Hitscan
    {
        private const float DegToRad = MathF.PI / 180f;

        /// <summary>Portee maximale d'une balle, en metres.</summary>
        public const float MaxRange = 250f;

        /// <summary>
        /// Compose la direction de tir : visee + recul deterministe + dispersion.
        /// Extrait de Fire pour pouvoir etre teste isolement.
        /// </summary>
        public static Vector3 ComputeDirection(Vector3 forward, Vector3 right, Vector3 up,
                                               WeaponStats weapon, StanceState stance,
                                               int recoilIndex, ref Rng rng)
        {
            Vec2 recoil = weapon.Recoil.GetOffset(recoilIndex);
            float spreadDeg = Ballistics.GetSpread(weapon, stance);

            Vector2 scatter = rng.NextInsideUnitCircle() * spreadDeg;

            // recoil.Y positif = le canon monte, donc la balle part vers le haut.
            float yawDeg = recoil.X + scatter.X;
            float pitchDeg = recoil.Y + scatter.Y;

            Vector3 dir = forward
                        + right * MathF.Tan(yawDeg * DegToRad)
                        + up * MathF.Tan(pitchDeg * DegToRad);

            return Vector3.Normalize(dir);
        }

        /// <summary>
        /// Tire une balle et resout l'impact. Le decor arrete la balle :
        /// une cible derriere un mur n'est jamais touchee.
        /// </summary>
        /// <param name="shooterId">Ignore dans la recherche de cible : on ne se tire pas dessus.</param>
        public static ShotResult Fire(Vector3 origin, Vector3 forward, Vector3 right, Vector3 up,
                                      WeaponStats weapon, StanceState stance, int recoilIndex,
                                      ref Rng rng,
                                      CollisionWorld world, IReadOnlyList<CombatTarget> targets,
                                      int shooterId)
        {
            Vector3 dir = ComputeDirection(forward, right, up, weapon, stance, recoilIndex, ref rng);

            // 1) Jusqu'ou va-t-on avant de rencontrer le decor ?
            float limit = MaxRange;
            bool hitWorld = world.Raycast(origin, dir, MaxRange, out float wallDistance, out _);
            if (hitWorld)
                limit = wallDistance;

            // 2) Quelle cible est touchee en premier, en deca de ce mur ?
            int bestId = -1;
            float bestDistance = limit;
            HitZone bestZone = HitZone.Chest;
            CombatTarget? bestTarget = null;

            for (int i = 0; i < targets.Count; i++)
            {
                CombatTarget t = targets[i];

                if (t == null || !t.IsAlive || t.Id == shooterId)
                    continue;

                if (t.Raycast(origin, dir, bestDistance, out HitZone zone, out float d) && d < bestDistance)
                {
                    bestDistance = d;
                    bestZone = zone;
                    bestId = t.Id;
                    bestTarget = t;
                }
            }

            // 3) Degats.
            if (bestTarget != null)
            {
                int damage = Ballistics.ComputeDamage(weapon, bestZone, bestDistance, bestTarget.HasArmor);
                bool killed = bestTarget.ApplyDamage(damage);

                return new ShotResult(dir, true, origin + dir * bestDistance, bestDistance,
                                      bestId, bestZone, damage, killed);
            }

            if (hitWorld)
                return new ShotResult(dir, true, origin + dir * wallDistance, wallDistance,
                                      -1, HitZone.Chest, 0, false);

            return new ShotResult(dir, false, origin + dir * MaxRange, MaxRange,
                                  -1, HitZone.Chest, 0, false);
        }
    }
}
