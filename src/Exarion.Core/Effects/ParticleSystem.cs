// ===========================================================================
//  Exarion - Systeme de particules
//
//  Un reservoir de taille FIXE, alloue une fois. Les particules vivantes sont
//  compactees en tete du tableau : le rendu peut donc lire une tranche
//  continue, sans test de validite par element.
//
//  Aucune allocation apres la construction. Une explosion qui declencherait
//  un ramasse-miettes au pire moment est exactement le genre de saccade qu'on
//  ne veut pas dans un jeu de tir.
//
//  ---------------------------------------------------------------------
//  LA SIMULATION EST A PAS FIXE, COMME LA PHYSIQUE.
//
//  On aurait pu la faire au rendu - c'est visuel, apres tout, et c'est ce
//  qu'on a fait pour l'animation (2.2). La difference est qu'une animation
//  n'a pas d'etat qui s'accumule : elle est une fonction du temps. Une
//  particule, si. La simuler au rendu ferait dependre sa trajectoire du
//  nombre d'images par seconde, et deux captures --seconds 3 ne donneraient
//  plus la meme image.
//
//  Le prix a payer : des bonds de 15 cm a 10 m/s. D'ou Particle.Previous et
//  l'interpolation au rendu.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Numerics;
using Exarion.Core.Physics;

namespace Exarion.Core.Effects
{
    public sealed class ParticleSystem
    {
        /// <summary>
        /// Recul applique apres un rebond. Sans lui, la particule repart de
        /// l'interieur de la surface et le rayon du pas suivant n'y trouve
        /// plus rien : elle traverserait.
        /// </summary>
        private const float ContactSkin = 0.002f;

        private readonly Particle[] _pool;
        private int _count;

        public ParticleSystem(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _pool = new Particle[capacity];
        }

        public Vector3 Gravity = new(0f, -9.81f, 0f);

        /// <summary>Decor contre lequel rebondir. Null = les particules le traversent.</summary>
        public CollisionWorld? World { get; set; }

        /// <summary>
        /// Particules refusees faute de place, depuis le debut.
        ///
        /// Exposee A DESSEIN. Un reservoir qui deborde en silence est ce qui
        /// fait qu'un systeme de particules "arrete de marcher" sans raison
        /// apparente : les nouveaux effets sont avales, et rien ne le dit.
        /// </summary>
        public int Dropped { get; private set; }

        public int Count => _count;

        public int Capacity => _pool.Length;

        /// <summary>Les particules vivantes, en une tranche continue.</summary>
        public ReadOnlySpan<Particle> Live => _pool.AsSpan(0, _count);

        /// <summary>
        /// Ajoute une particule. Rend faux si le reservoir est plein.
        ///
        /// ON REFUSE, on ne recycle pas. Recycler la plus vieille serait plus
        /// joli - un impact tout neuf compte plus qu'une fumee qui s'eteint -
        /// mais la trouver coute un parcours complet a chaque ajout, et le
        /// tableau n'est plus trie par age des la premiere mort. On prefere
        /// un refus franc, compte, et un reservoir dimensionne.
        /// </summary>
        public bool Spawn(in Particle particle)
        {
            if (_count >= _pool.Length)
            {
                Dropped++;
                return false;
            }

            _pool[_count] = particle;

            // Une particule qui nait pendant le pas n'a pas de passe : sans
            // ca, elle serait interpolee depuis l'origine du monde et
            // traverserait tout l'ecran sur sa premiere image.
            _pool[_count].Previous = particle.Position;
            _pool[_count].Age = 0f;

            _count++;
            return true;
        }

        public void Clear()
        {
            _count = 0;
            Dropped = 0;
        }

        public void Step(float dt)
        {
            if (dt <= 0f)
                return;

            int i = 0;

            while (i < _count)
            {
                ref Particle p = ref _pool[i];

                p.Age += dt;

                if (p.Age >= p.Lifetime)
                {
                    // Echange avec la derniere vivante. ON N'INCREMENTE PAS i :
                    // l'element qui vient d'arriver a cette place n'a pas
                    // encore ete traite. L'oublier est LE bug classique de ce
                    // motif, et il ne se voit pas - une particule sur deux
                    // saute simplement un pas.
                    _pool[i] = _pool[--_count];
                    continue;
                }

                p.Previous = p.Position;

                p.Velocity += Gravity * p.GravityScale * dt;

                // ------------------------------------------------------------
                // AMORTISSEMENT IMPLICITE, ET C'EST IMPORTANT.
                //
                // La forme naive v *= (1 - drag * dt) devient NEGATIVE des que
                // drag * dt depasse 1 : la particule repart en arriere, puis
                // oscille. Avec une trainee forte (une fumee freine vite) et un
                // pas un peu long, ca arrive pour de bon.
                //
                // v / (1 + drag * dt) tend vers zero sans jamais changer de
                // signe, quelle que soit la valeur de drag.
                // ------------------------------------------------------------
                if (p.Drag > 0f)
                    p.Velocity /= 1f + p.Drag * dt;

                Vector3 delta = p.Velocity * dt;

                if (p.Bounce > 0f && World is not null)
                    Move(ref p, delta);
                else
                    p.Position += delta;

                i++;
            }
        }

        /// <summary>
        /// Deplacement avec un rebond eventuel contre le decor.
        ///
        /// Un seul rebond par pas : une particule qui toucherait deux surfaces
        /// dans le meme seizieme de seconde est un cas de coin invisible, et
        /// le boucler couterait un rayon de plus a chaque particule.
        /// </summary>
        private void Move(ref Particle p, Vector3 delta)
        {
            float travel = delta.Length();

            if (travel < 1e-5f)
                return;

            if (World!.Raycast(p.Position, delta, travel, out float distance, out Vector3 normal))
            {
                p.Position += delta * (distance / travel) + normal * ContactSkin;
                p.Velocity = Vector3.Reflect(p.Velocity, normal) * p.Bounce;
                return;
            }

            p.Position += delta;
        }
    }
}
