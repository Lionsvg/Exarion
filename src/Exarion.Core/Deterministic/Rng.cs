// ===========================================================================
//  Exarion - Generateur pseudo-aleatoire deterministe
//
//  POURQUOI ne pas utiliser System.Random :
//
//  La dispersion d'une balle est aleatoire. Mais en reseau, le client et le
//  serveur doivent tirer EXACTEMENT la meme balle, sinon le client voit
//  toucher et le serveur voit rater. La solution universelle est un
//  generateur deterministe, initialise par une graine partagee - typiquement
//  (numero de tick, identifiant du joueur, numero de balle).
//
//  System.Random ne garantit pas la meme suite d'une version de .NET a
//  l'autre, ni entre plateformes. Celui-ci, si : c'est un xorshift32, dont
//  l'algorithme tient en trois lignes et ne changera jamais.
//
//  Reference : George Marsaglia, "Xorshift RNGs" (2003). Algorithme public.
// ===========================================================================

using System;
using System.Numerics;

namespace Exarion.Core.Deterministic
{
    public struct Rng
    {
        private uint _state;

        public Rng(uint seed)
        {
            // L'etat zero est un point fixe du xorshift : il ne produirait
            // que des zeros. On le remplace.
            _state = seed == 0u ? 0x9E3779B9u : seed;
        }

        /// <summary>
        /// Graine composite. Deux tirs distincts doivent donner deux graines
        /// distinctes, et le meme tir doit toujours donner la meme.
        /// </summary>
        public static Rng FromShot(long tick, int playerId, int shotIndex)
        {
            unchecked
            {
                uint h = 2166136261u;                 // FNV-1a
                h = (h ^ (uint)tick) * 16777619u;
                h = (h ^ (uint)(tick >> 32)) * 16777619u;
                h = (h ^ (uint)playerId) * 16777619u;
                h = (h ^ (uint)shotIndex) * 16777619u;
                return new Rng(h);
            }
        }

        public uint NextUInt()
        {
            unchecked
            {
                uint x = _state;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                _state = x;
                return x;
            }
        }

        /// <summary>Flottant dans [0,1).</summary>
        public float NextFloat()
        {
            // 24 bits de mantisse : on prend les 24 bits de poids fort.
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        public float NextFloat(float min, float max) => min + NextFloat() * (max - min);

        /// <summary>
        /// Point uniforme dans le disque unite. La racine carree est
        /// indispensable : sans elle, les points s'agglutinent au centre.
        /// </summary>
        public Vector2 NextInsideUnitCircle()
        {
            float angle = NextFloat() * MathF.Tau;
            float radius = MathF.Sqrt(NextFloat());
            return new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
        }
    }
}
