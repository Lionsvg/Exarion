// ===========================================================================
//  Exarion - Recettes d'effets
//
//  Le systeme de particules ne sait que faire voler des points. Ce fichier
//  dit a quoi ressemble un impact de balle, une bouffee de fumee, une
//  explosion. C'est le seul endroit ou vivent les valeurs "de gout" - durees,
//  vitesses, teintes - et il est donc le seul a modifier pour changer le
//  rendu d'un effet.
//
//  ⚠️ Rng se passe TOUJOURS par reference. C'est une structure a etat mutable :
//  passee par valeur, chaque appel repartirait de la meme graine et les
//  quarante etincelles d'un impact seraient rigoureusement identiques.
//
//  ---------------------------------------------------------------------
//  ⚠️⚠️ LES COULEURS SONT EN LINEAIRE HDR, PAS EN COULEURS D'ECRAN.
//
//  Piege mesure, et il a produit une fumee BLANCHE au premier essai. Une
//  particule traverse ensuite toute la chaine de 1.8 : exposition x1,4, puis
//  la courbe ACES, puis la conversion sRGB. Un "gris 0,15", qu'on ecrit
//  spontanement en pensant a un gris sombre, ressort a 0,59 a l'ecran -
//  quatre fois trop clair, et le nuage se lit comme un cumulus.
//
//  Pour viser une valeur d'ecran v, il faut ecrire ici, en gros :
//      lineaire = ACES-1(v^2,2) / 1,4
//  soit 0,037 pour un gris d'ecran de 0,25. Les valeurs ci-dessous ont ete
//  obtenues ainsi, puis verifiees par capture.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Numerics;
using Exarion.Core.Deterministic;

namespace Exarion.Core.Effects
{
    public static class ParticleEffects
    {
        /// <summary>
        /// Impact de balle sur une surface : des etincelles vives, et la
        /// poussiere qu'elles arrachent.
        ///
        /// Les deux ensembles sont necessaires. Les etincelles seules font
        /// une gerbe qui flotte dans le vide ; la poussiere seule fait un
        /// impact mou. C'est le contraste entre les deux - une chose tres
        /// lumineuse et fine, une chose terne et large - qui donne l'idee
        /// d'un projectile qui frappe.
        /// </summary>
        public static void Impact(ParticleSystem system, ref Rng rng,
                                  Vector3 point, Vector3 normal, float scale = 1f)
        {
            Sparks(system, ref rng, point, normal, (int)(14 * scale), 6.5f * scale);
            Dust(system, ref rng, point, normal, (int)(6 * scale), 0.9f * scale);
        }

        /// <summary>
        /// Etincelles : petites, tres lumineuses, elles rebondissent.
        ///
        /// Leur couleur depasse largement 1,0 - c'est ce qui leur fait un halo
        /// dans le bloom. Une etincelle a 1,0 serait un point blanc terne,
        /// exactement comme un mur eclaire.
        /// </summary>
        public static void Sparks(ParticleSystem system, ref Rng rng,
                                  Vector3 point, Vector3 normal, int count, float speed)
        {
            Basis(normal, out Vector3 tangentA, out Vector3 tangentB);

            for (int i = 0; i < count; i++)
            {
                Vector3 direction = Cone(ref rng, normal, tangentA, tangentB, 0.85f);

                // La chaleur varie d'une etincelle a l'autre : une gerbe
                // parfaitement uniforme se lit comme un decalque.
                float heat = rng.NextFloat(0.55f, 1f);

                system.Spawn(new Particle
                {
                    Position = point + normal * 0.02f,
                    Velocity = direction * speed * rng.NextFloat(0.35f, 1f),
                    Color = new Vector3(7.5f, 3.4f, 0.7f) * heat,
                    Lifetime = rng.NextFloat(0.22f, 0.55f),
                    SizeStart = 0.030f,
                    SizeEnd = 0.004f,
                    GravityScale = 1f,
                    Drag = 1.6f,
                    Bounce = 0.35f,
                    FadeIn = 0f,
                    Additive = true,
                });
            }
        }

        /// <summary>
        /// Poussiere arrachee : terne, elle s'etale et s'arrete vite.
        /// Ne rebondit pas - elle n'a pas assez d'elan pour qu'on le voie,
        /// et chaque rebond coute un rayon contre le decor.
        /// </summary>
        public static void Dust(ParticleSystem system, ref Rng rng,
                                Vector3 point, Vector3 normal, int count, float speed)
        {
            Basis(normal, out Vector3 tangentA, out Vector3 tangentB);

            for (int i = 0; i < count; i++)
            {
                Vector3 direction = Cone(ref rng, normal, tangentA, tangentB, 1.4f);
                float grey = rng.NextFloat(0.075f, 0.135f);

                system.Spawn(new Particle
                {
                    Position = point + normal * 0.03f,
                    Velocity = direction * speed * rng.NextFloat(0.4f, 1f),
                    Color = new Vector3(grey, grey * 0.94f, grey * 0.86f),
                    Lifetime = rng.NextFloat(0.35f, 0.8f),
                    SizeStart = 0.10f,
                    SizeEnd = 0.42f,
                    GravityScale = 0.15f,
                    Drag = 4.5f,
                    Bounce = 0f,
                    FadeIn = 0.12f,
                    Additive = false,
                });
            }
        }

        /// <summary>
        /// Fumee : lente, elle grossit, monte un peu et s'efface.
        ///
        /// La pesanteur est NEGATIVE - la fumee est plus legere que l'air.
        /// La mettre a zero donnerait un nuage qui flotte sur place, ce qui se
        /// lit tout de suite comme un decor et non comme un gaz.
        /// </summary>
        public static void Smoke(ParticleSystem system, ref Rng rng,
                                 Vector3 point, int count, float radius = 0.25f, float scale = 1f)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = Sphere(ref rng) * radius;
                float grey = rng.NextFloat(0.026f, 0.052f);

                system.Spawn(new Particle
                {
                    Position = point + offset,
                    Velocity = offset * 1.2f + new Vector3(0f, rng.NextFloat(0.3f, 0.9f), 0f),
                    Color = new Vector3(grey, grey, grey * 1.04f),
                    Lifetime = rng.NextFloat(1.4f, 2.8f),
                    SizeStart = 0.35f * scale,
                    SizeEnd = 1.8f * scale,
                    GravityScale = -0.05f,
                    Drag = 1.1f,
                    Bounce = 0f,
                    FadeIn = 0.25f,
                    Additive = false,
                });
            }
        }

        /// <summary>
        /// Explosion : un noyau lumineux bref, des debris qui rebondissent, et
        /// une fumee qui reste bien apres.
        ///
        /// Les trois ne durent PAS le meme temps, et c'est tout l'effet : le
        /// noyau s'eteint en un dixieme de seconde, la fumee tient deux
        /// secondes. Une explosion dont tout disparait ensemble ne ressemble
        /// a rien.
        /// </summary>
        public static void Explosion(ParticleSystem system, ref Rng rng, Vector3 center, float scale = 1f)
        {
            // --- Noyau : tres lumineux, tres court ---
            for (int i = 0; i < (int)(26 * scale); i++)
            {
                Vector3 direction = Sphere(ref rng);

                system.Spawn(new Particle
                {
                    Position = center + direction * 0.1f * scale,
                    Velocity = direction * rng.NextFloat(3f, 9f) * scale,
                    Color = new Vector3(9f, 4.6f, 1.1f) * rng.NextFloat(0.6f, 1f),
                    Lifetime = rng.NextFloat(0.12f, 0.30f),
                    SizeStart = 0.55f * scale,
                    SizeEnd = 0.08f * scale,
                    GravityScale = 0.1f,
                    Drag = 5.5f,
                    Bounce = 0f,
                    FadeIn = 0f,
                    Additive = true,
                });
            }

            // --- Debris : ils partent loin et rebondissent ---
            for (int i = 0; i < (int)(18 * scale); i++)
            {
                Vector3 direction = Vector3.Normalize(Sphere(ref rng) + new Vector3(0f, 0.6f, 0f));
                float grey = rng.NextFloat(0.050f, 0.090f);

                system.Spawn(new Particle
                {
                    Position = center,
                    Velocity = direction * rng.NextFloat(4f, 11f) * scale,
                    Color = new Vector3(grey, grey * 0.92f, grey * 0.82f),
                    Lifetime = rng.NextFloat(0.8f, 1.8f),
                    SizeStart = 0.06f * scale,
                    SizeEnd = 0.03f * scale,
                    GravityScale = 1f,
                    Drag = 0.35f,
                    Bounce = 0.4f,
                    FadeIn = 0f,
                    Additive = false,
                });
            }

            Smoke(system, ref rng, center, (int)(14 * scale), 0.5f * scale, scale);
        }

        // -------------------------------------------------------------------

        /// <summary>
        /// Direction tiree dans un cone autour de la normale.
        /// spread = 0 suit la normale, 1 ouvre a environ 45 degres, au-dela on
        /// s'approche de l'hemisphere.
        /// </summary>
        private static Vector3 Cone(ref Rng rng, Vector3 axis, Vector3 tangentA, Vector3 tangentB, float spread)
        {
            Vector2 disc = rng.NextInsideUnitCircle();

            Vector3 direction = axis + (tangentA * disc.X + tangentB * disc.Y) * spread;

            float length = direction.Length();

            // Un cone tres ouvert peut, tres rarement, annuler exactement
            // l'axe. On retombe alors sur l'axe plutot que de propager un NaN.
            return length > 1e-5f ? direction / length : axis;
        }

        /// <summary>Direction uniforme sur la sphere unite.</summary>
        private static Vector3 Sphere(ref Rng rng)
        {
            // Methode de l'inversion : z uniforme dans [-1,1] et angle
            // uniforme donnent une repartition UNIFORME sur la sphere. Tirer
            // deux angles uniformement, lui, agglutinerait les points aux
            // poles - un defaut invisible sur une explosion, mais bien reel.
            float z = rng.NextFloat(-1f, 1f);
            float angle = rng.NextFloat(0f, MathF.Tau);
            float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));

            return new Vector3(MathF.Cos(angle) * r, z, MathF.Sin(angle) * r);
        }

        /// <summary>
        /// Deux tangentes orthonormees a un axe.
        ///
        /// ⚠️ LE PIEGE : croiser systematiquement avec Y donne un vecteur NUL
        /// quand l'axe EST Y - c'est-a-dire pour toute particule emise depuis
        /// un sol, le cas le plus frequent de tous. On croise donc avec l'axe
        /// dont l'axe donne est le PLUS ELOIGNE.
        /// </summary>
        private static void Basis(Vector3 axis, out Vector3 tangentA, out Vector3 tangentB)
        {
            axis = axis.LengthSquared() > 1e-8f ? Vector3.Normalize(axis) : Vector3.UnitY;

            Vector3 reference = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;

            tangentA = Vector3.Normalize(Vector3.Cross(axis, reference));
            tangentB = Vector3.Cross(axis, tangentA);
        }
    }
}
