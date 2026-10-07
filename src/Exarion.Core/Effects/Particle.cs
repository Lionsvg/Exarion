// ===========================================================================
//  Exarion - Une particule
//
//  Tout ce qu'il faut pour decrire un grain de poussiere, une etincelle ou
//  une bouffee de fumee. Une STRUCTURE, pas une classe : le systeme en tient
//  des milliers dans un seul tableau, et autant d'objets separes couteraient
//  une indirection par particule et un travail permanent au ramasse-miettes.
//
//  ---------------------------------------------------------------------
//  POURQUOI LES PARAMETRES SONT PAR PARTICULE ET NON PAR EMETTEUR
//
//  Un emetteur qui garderait "gravite = 0,2, rebond = 0,4" imposerait un
//  tableau par type d'effet, donc un appel de dessin par type. En les
//  portant sur la particule, tous les effets vivent dans le MEME tableau et
//  se dessinent en une fois. Le cout est de 19 flottants par particule -
//  76 octets, soit 300 Ko pour un reservoir de 4 000.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Numerics;

namespace Exarion.Core.Effects
{
    public struct Particle
    {
        public Vector3 Position;

        /// <summary>
        /// Position au debut du pas. Le rendu interpole entre les deux.
        ///
        /// La simulation tourne a pas fixe - c'est ce qui rend une capture
        /// headless reproductible. Mais 64 pas par seconde, sur une etincelle
        /// a 10 m/s, ce sont des bonds de 15 cm : sans interpolation, l'effet
        /// serait saccade des que le rendu depasse la cadence de simulation.
        /// </summary>
        public Vector3 Previous;

        public Vector3 Velocity;

        /// <summary>
        /// Couleur, en HDR. Une etincelle depasse franchement 1,0 - c'est ce
        /// qui lui permet de faire un halo dans le bloom (1.8). Une fumee,
        /// elle, reste sous 1.
        /// </summary>
        public Vector3 Color;

        public float Age;
        public float Lifetime;

        public float SizeStart;
        public float SizeEnd;

        /// <summary>Multiplicateur de pesanteur. 0 = flotte, 1 = tombe normalement.</summary>
        public float GravityScale;

        /// <summary>Frottement de l'air, par seconde.</summary>
        public float Drag;

        /// <summary>Restitution au contact du decor. 0 = aucune collision testee.</summary>
        public float Bounce;

        /// <summary>
        /// Fraction de vie pendant laquelle la particule apparait.
        /// 0 pour une etincelle, qui est vive tout de suite ; 0,2 pour une
        /// fumee, qui se forme.
        /// </summary>
        public float FadeIn;

        /// <summary>
        /// Melange additif. Vrai pour tout ce qui EMET de la lumiere -
        /// etincelles, flammes ; faux pour ce qui la bloque - fumee, debris.
        ///
        /// Ce n'est pas qu'une question de gout : l'additif est independant de
        /// l'ordre de dessin, la transparence ordinaire non.
        /// </summary>
        public bool Additive;

        /// <summary>Avancement dans la vie, de 0 a 1.</summary>
        public readonly float Life => Lifetime > 0f ? Age / Lifetime : 1f;

        public readonly bool Alive => Age < Lifetime;

        public readonly float Size
        {
            get
            {
                float t = Life;
                return SizeStart + (SizeEnd - SizeStart) * t;
            }
        }

        /// <summary>
        /// Opacite : une montee sur FadeIn, puis une descente lineaire
        /// jusqu'a zero en fin de vie.
        ///
        /// Une particule qui disparaitrait d'un coup se verrait clignoter -
        /// c'est le defaut le plus visible d'un systeme de particules, et il
        /// ne coute qu'une multiplication a corriger.
        /// </summary>
        public readonly float Alpha
        {
            get
            {
                float t = Life;

                if (t >= 1f)
                    return 0f;

                float rise = FadeIn > 1e-4f ? MathF.Min(1f, t / FadeIn) : 1f;

                return rise * (1f - t);
            }
        }

        /// <summary>Position au rendu, entre deux pas de simulation.</summary>
        public readonly Vector3 At(float alpha) => Vector3.Lerp(Previous, Position, alpha);
    }
}
