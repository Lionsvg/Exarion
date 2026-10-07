// ===========================================================================
//  Exarion - Compensation de latence (3.7)
//
//  ---------------------------------------------------------------------
//  LE PROBLEME, ET POURQUOI IL N'A PAS DE SOLUTION PARFAITE
//
//  Un tireur voit ses adversaires DANS LE PASSE - c'est le prix de
//  l'interpolation (3.4), et il s'ajoute a la moitie de son ping. A 60 ms de
//  latence et 31 ms d'interpolation, il vise une silhouette vieille de
//  ~90 ms. A 5 m/s, la cible a deja parcouru 45 cm : viser la tete et rater
//  systematiquement.
//
//  La compensation consiste a REMBOBINER : le serveur remet les adversaires
//  la ou le tireur les voyait, fait le lancer de rayon, puis les remet a
//  leur place. Ce que le joueur voit devient ce qui compte.
//
//  ⚠️⚠️ LE PRIX EST REEL ET IL FAUT LE DIRE : « se faire tirer dessus derriere
//  un mur ». La victime s'est mise a couvert, mais le serveur juge le tir sur
//  une position d'il y a 90 ms, ou elle etait encore a decouvert. C'est un
//  ARBITRAGE, pas un defaut : sans compensation on punit le tireur, avec on
//  punit celui qui se cache. Tous les FPS competitifs choisissent le tireur,
//  parce que rater un tir bien vise est plus frustrant que mourir juste apres
//  s'etre mis a couvert.
//
//  ⚠️ On BORNE le rembobinage. Un client qui annonce un temps de rendu vieux
//  d'une seconde ferait juger son tir sur un monde depuis longtemps disparu :
//  il tuerait des gens la ou ils ne sont plus depuis longtemps.
//  ---------------------------------------------------------------------
//
//  Ecrit dans Core, sans reseau : l'historique est une liste de positions, le
//  rembobinage une interpolation, et le tir un lancer de rayon. Tout se teste
//  en ligne de commande.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Combat;
using Exarion.Core.Physics;

namespace Exarion.Core.Net
{
    /// <summary>Ou etait un joueur, et quand.</summary>
    public readonly struct HistorySample
    {
        public readonly uint Tick;
        public readonly Vector3 Position;
        public readonly float Height;

        public HistorySample(uint tick, Vector3 position, float height)
        {
            Tick = tick;
            Position = position;
            Height = height;
        }
    }

    /// <summary>
    /// L'historique des positions d'un joueur, pour le rembobinage.
    /// </summary>
    public sealed class PlayerHistory
    {
        private readonly List<HistorySample> _samples = new();

        /// <summary>
        /// Duree conservee, en pas de simulation.
        ///
        /// ⚠️ Une seconde a 64 Hz. C'est la BORNE du rembobinage : au-dela, on
        /// refuse. 64 pas couvrent 500 ms de ping plus l'interpolation, ce qui
        /// depasse deja tout ce qui est jouable - garder plus reviendrait a
        /// laisser quelqu'un tirer sur un monde disparu.
        /// </summary>
        public const int MaxSamples = 64;

        public int Count => _samples.Count;

        public void Clear() => _samples.Clear();

        public void Record(uint tick, Vector3 position, float height)
        {
            _samples.Add(new HistorySample(tick, position, height));

            while (_samples.Count > MaxSamples)
                _samples.RemoveAt(0);
        }

        /// <summary>Le pas le plus ancien encore disponible.</summary>
        public uint OldestTick => _samples.Count > 0 ? _samples[0].Tick : 0;

        /// <summary>
        /// Ou etait ce joueur au pas demande.
        ///
        /// ⚠️ On INTERPOLE entre deux echantillons plutot que de prendre le plus
        /// proche. Le tireur voyait une position interpolee (3.4) : lui repondre
        /// avec une position echantillonnee introduirait une erreur allant
        /// jusqu'a un pas entier de deplacement, soit 7 cm a la vitesse de
        /// course. C'est l'ordre de grandeur d'une tete.
        /// </summary>
        public bool TryRewind(double tick, out Vector3 position, out float height)
        {
            position = Vector3.Zero;
            height = 0f;

            if (_samples.Count == 0)
                return false;

            // Trop ancien : on REFUSE plutot que d'extrapoler. Extrapoler
            // reviendrait a inventer une position, donc a valider un tir sur une
            // cible imaginaire.
            if (tick < _samples[0].Tick)
                return false;

            // Plus recent que tout ce qu'on a : on rend le present. Ca arrive
            // legitimement, un client peut viser sur un instantane a peine recu.
            if (tick >= _samples[^1].Tick)
            {
                position = _samples[^1].Position;
                height = _samples[^1].Height;
                return true;
            }

            for (int i = 1; i < _samples.Count; i++)
            {
                if (_samples[i].Tick < tick)
                    continue;

                HistorySample a = _samples[i - 1];
                HistorySample b = _samples[i];

                double span = b.Tick - a.Tick;
                float t = span > 0.0 ? (float)((tick - a.Tick) / span) : 0f;

                position = Vector3.Lerp(a.Position, b.Position, t);
                height = a.Height + (b.Height - a.Height) * t;
                return true;
            }

            return false;
        }
    }

    /// <summary>Le resultat d'un tir arbitre par le serveur.</summary>
    public readonly struct ShotResult
    {
        /// <summary>Identifiant du joueur touche, ou 0.</summary>
        public readonly byte VictimId;

        public readonly Vector3 Point;
        public readonly float Distance;

        /// <summary>Vrai si le rembobinage a change le verdict. Sert a MESURER 3.7.</summary>
        public readonly bool RewindMattered;

        public bool Hit => VictimId != 0;

        public ShotResult(byte victimId, Vector3 point, float distance, bool rewindMattered)
        {
            VictimId = victimId;
            Point = point;
            Distance = distance;
            RewindMattered = rewindMattered;
        }

        public static readonly ShotResult Miss = new(0, Vector3.Zero, 0f, false);
    }

    public static class LagCompensation
    {
        /// <summary>Demi-largeur d'une boite de joueur, en metres.</summary>
        public const float PlayerHalfWidth = 0.4f;

        /// <summary>
        /// Cherche qui est touche, en rembobinant les cibles.
        /// </summary>
        /// <param name="shooterId">Le tireur, exclu de la recherche.</param>
        /// <param name="rewindTick">Pas de serveur que le tireur voyait.</param>
        public static ShotResult Resolve(byte shooterId, Vector3 origin, Vector3 direction,
                                         float maxDistance,
                                         IReadOnlyList<byte> ids,
                                         IReadOnlyList<PlayerHistory> histories,
                                         double rewindTick,
                                         CollisionWorld world)
        {
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(histories);

            float length = direction.Length();

            if (length < 1e-6f || float.IsNaN(length))
                return ShotResult.Miss;

            direction /= length;

            // ⚠️ LE DECOR D'ABORD. Une balle ne traverse pas un mur : sans cette
            // borne, on toucherait quelqu'un a travers la geometrie des qu'il est
            // aligne. Le decor n'est PAS rembobine - il ne bouge pas.
            float limit = maxDistance;

            if (world is not null && world.Raycast(origin, direction, maxDistance, out float wall, out _))
                limit = wall;

            byte best = 0;
            float bestDistance = float.MaxValue;
            bool mattered = false;

            for (int i = 0; i < ids.Count && i < histories.Count; i++)
            {
                if (ids[i] == shooterId)
                    continue;

                if (!histories[i].TryRewind(rewindTick, out Vector3 past, out float height))
                    continue;

                Aabb box = Aabb.ForCharacter(past, PlayerHalfWidth * 2f, height);

                if (!CombatTarget.RayVsAabb(origin, direction, limit, box, out float distance))
                    continue;

                if (distance > limit || distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = ids[i];

                // Le rembobinage a-t-il change quelque chose ? On refait le
                // meme test sur la position ACTUELLE. C'est ce qui permet de
                // MESURER l'effet de 3.7 au lieu de le supposer.
                if (histories[i].TryRewind(double.MaxValue, out Vector3 now, out float nowHeight))
                {
                    Aabb current = Aabb.ForCharacter(now, PlayerHalfWidth * 2f, nowHeight);

                    bool wouldHitNow = CombatTarget.RayVsAabb(origin, direction, limit, current, out float d2)
                                       && d2 <= limit;

                    mattered = !wouldHitNow;
                }
            }

            return best == 0
                ? ShotResult.Miss
                : new ShotResult(best, origin + direction * bestDistance, bestDistance, mattered);
        }
    }
}
