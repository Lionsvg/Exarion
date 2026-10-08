// ===========================================================================
//  Les effets du bac a sable : le reservoir de particules et son generateur
//
//  ⚠️ UN SEUL GENERATEUR POUR TOUS LES EFFETS, A GRAINE FIXE. Deux captures
//  headless doivent rendre la meme image, sinon aucune comparaison de rendu
//  n'a de sens. Deux generateurs, ou une graine tiree de l'horloge, et la
//  premiere explosion ne retomberait jamais deux fois de la meme facon.
//
//  L'ORDRE des tirages compte autant que la graine : un effet ajoute avant
//  un autre decale tous ceux qui suivent. C'est pourquoi tous les appels
//  passent par ici, et pas par des generateurs locaux.
// ===========================================================================

using System.Numerics;
using Exarion.Core.Deterministic;
using Exarion.Core.Effects;

namespace Exarion.Game.Gameplay
{
    internal sealed class SandboxFx
    {
        /// <summary>
        /// Reservoir fixe de 4 096 particules (2.3). Il REFUSE quand il est
        /// plein plutot que de recycler : voir ParticleSystem.Dropped.
        /// </summary>
        public ParticleSystem Particles { get; } = new(4096);

        // Graine FIXE : voir l'en-tete.
        private Rng _rng = new(20260816u);

        /// <summary>« particles » : afficher et simuler les particules.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>« fx_impact » : ampleur des impacts de balle.</summary>
        public float ImpactScale { get; set; } = 1f;

        public void Explosion(Vector3 at, float scale)
            => ParticleEffects.Explosion(Particles, ref _rng, at, scale);

        public void Impact(Vector3 at, Vector3 normal, float scale)
            => ParticleEffects.Impact(Particles, ref _rng, at, normal, scale);

        public void Smoke(Vector3 at, int count)
            => ParticleEffects.Smoke(Particles, ref _rng, at, count);

        public void Sparks(Vector3 at, Vector3 direction, int count, float speed)
            => ParticleEffects.Sparks(Particles, ref _rng, at, direction, count, speed);
    }
}
