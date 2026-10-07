// ===========================================================================
//  Exarion - Solveur de contacts a impulsions sequentielles
//
//  C'est l'etape ou les caisses se mettent enfin a basculer.
//
//  ---------------------------------------------------------------------
//  POURQUOI UN SOLVEUR, ET PAS UNE FORMULE
//
//  Une caisse posee sur deux autres touche en huit points a la fois. Chaque
//  point veut sa propre correction, et corriger l'un derange les sept
//  autres. Il n'existe pas de formule qui resolve tout d'un coup - ou
//  plutot si, mais elle demande d'inverser une matrice qui change a chaque
//  pas.
//
//  On fait donc l'inverse : on parcourt les contacts un par un, plusieurs
//  fois de suite. Chaque passe ameliore la solution precedente. Trois
//  passes suffisent pour une pile de caisses ; on ne cherche pas la
//  perfection, on cherche que ca tienne et que ca ne tremble pas.
//
//  ---------------------------------------------------------------------
//  LES DEUX IDEES QUI FONT QUE CA MARCHE
//
//  1. ACCUMULER, PUIS BORNER. Un contact ne peut que POUSSER, jamais tirer.
//     La tentation est de borner chaque petite impulsion a zero - et c'est
//     faux : une passe qui voudrait corriger le trop-plein d'une passe
//     precedente en serait empechee. On borne le TOTAL accumule, et on
//     applique la difference. Le solveur peut alors se raviser.
//
//  2. SEPARER VITESSE ET POSITION. Deux problemes distincts vivent dans un
//     contact : "ils se rapprochent, il faut les arreter" (vitesse) et
//     "ils sont deja imbriques, il faut les degager" (position).
//
//     Les melanger est le piege classique - et c'etait le defaut du solveur
//     precedent. Deplacer un corps pour le degager sans toucher a sa
//     vitesse, c'est lui donner de l'energie gratuite : la pile gonfle,
//     rebondit, et ne s'endort jamais. Ici la passe de position corrige
//     positions et orientations SANS jamais toucher aux vitesses.
//
//  ---------------------------------------------------------------------
//  CE QUE LA ROTATION CHANGE
//
//  Avant, une impulsion s'appliquait au centre de masse et ne faisait que
//  deplacer. Maintenant elle s'applique AU POINT DE CONTACT, et la distance
//  entre ce point et le centre - le bras de levier - la transforme en
//  rotation. C'est toute la difference entre une caisse qui glisse et une
//  caisse qui bascule.
//
//  La "masse effective" d'un contact n'est donc plus 1/mA + 1/mB : il faut
//  y ajouter la resistance a la rotation, qui depend du bras de levier et
//  du tenseur d'inertie. Voir EffectiveMass.
//
//  Technique classique et publique (impulsions sequentielles), ecrite ici.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Exarion.Core.Physics
{
    public sealed class ContactSolver
    {
        /// <summary>
        /// Chevauchement tolere. Chercher le contact exact fait vibrer :
        /// le corps oscille entre "touche" et "touche pas". Une marge de
        /// 2 mm laisse le contact s'etablir tranquillement.
        /// </summary>
        public const float AllowedPenetration = 0.002f;

        /// <summary>
        /// Fraction du chevauchement corrigee par passe. Tout corriger d'un
        /// coup fait sursauter la pile ; 20 % converge en quelques pas.
        /// </summary>
        private const float PositionRelaxation = 0.2f;

        /// <summary>Correction maximale par passe : garde-fou anti-explosion.</summary>
        private const float MaxPositionCorrection = 0.2f;

        /// <summary>
        /// En deca de cette vitesse d'approche, pas de rebond. Sans ce seuil,
        /// une caisse posee rebondit indefiniment sur des micro-vitesses.
        /// </summary>
        private const float RestitutionThreshold = 1f;

        /// <summary>
        /// Vitesse propre du VOISIN a partir de laquelle un corps endormi se
        /// reveille. Reglee par PhysicsWorld a chaque pas.
        ///
        /// Deux pieges se referment sur ce seuil, et ils tirent en sens
        /// contraire :
        ///
        ///   - le juger sur la vitesse RELATIVE des deux corps ne marche pas.
        ///     Deux caisses qui vibrotent chacune a 0,07 en sens contraire
        ///     affichent 0,14 de relatif : chacune est assez calme pour
        ///     s'endormir et assez "vive" pour reveiller l'autre. La pile
        ///     oscille entre les deux etats sans jamais se poser.
        ///
        ///   - le juger sur la vitesse PROPRE ne marche qu'au-dessus de
        ///     l'increment de gravite. Le solveur travaille juste apres son
        ///     application : a cet instant, une caisse posee depuis dix
        ///     secondes "tombe" deja a 0,24 m/s. Sous ce seuil, tout le monde
        ///     est declare en mouvement en permanence.
        /// </summary>
        public float WakeSpeed = 0.5f;


        /// <summary>
        /// Marge de detection du decor, en metres. Les boites du decor sont
        /// gonflees d'autant, ce qui fait voir le contact AVANT le toucher.
        /// Sans elle le solveur ne voit jamais une caisse posee a plat : le
        /// balayage l'arrete un millimetre avant la surface, et deux boites
        /// qui se frolent ne se chevauchent pas.
        ///
        /// ELLE DOIT RESTER PETITE, et la raison n'est pas evidente. Le
        /// contact speculatif autorise a combler le vide en un pas, donc une
        /// vitesse d'approche de marge/dt. Elle doit rester SOUS le seuil de
        /// sommeil, sinon une pile peut pomper a cette vitesse sans jamais
        /// pouvoir s'endormir : 0,14 x 1/64 = 2,2 mm.
        /// </summary>
        public const float StaticMargin = 0.002f;

        private Contact[] _contacts = new Contact[64];
        private int _count;

        public int ContactCount => _count;

        /// <summary>Un point d'appui, avec tout ce qu'il faut pour le resoudre.</summary>
        private struct Contact
        {
            public RigidBody A;
            public RigidBody B;

            /// <summary>Normale unitaire, de A vers B.</summary>
            public Vector3 Normal;

            /// <summary>Les deux directions de glissement, perpendiculaires a la normale.</summary>
            public Vector3 Tangent1;
            public Vector3 Tangent2;

            /// <summary>Point de contact dans le repere propre de chaque corps.</summary>
            public Vector3 LocalA;
            public Vector3 LocalB;

            /// <summary>Bras de levier, du centre de masse vers le point.</summary>
            public Vector3 LeverA;
            public Vector3 LeverB;

            /// <summary>Negatif = imbriques. C'est la valeur au moment de la collecte.</summary>
            public float Separation;

            /// <summary>Inverses des masses effectives, pre-calcules une seule fois.</summary>
            public float NormalMass;
            public float Tangent1Mass;
            public float Tangent2Mass;

            /// <summary>Impulsions cumulees. C'est sur elles qu'on borne.</summary>
            public float NormalImpulse;
            public float Tangent1Impulse;
            public float Tangent2Impulse;

            /// <summary>Vitesse de separation visee, pour le rebond.</summary>
            public float RestitutionTarget;

            public float Friction;
        }

        public void Clear() => _count = 0;

        // -------------------------------------------------------------------
        //  Collecte
        // -------------------------------------------------------------------

        /// <summary>
        /// Construit la liste des contacts. UNE SEULE FOIS par pas : refaire
        /// la detection a chaque passe couterait quinze fois plus cher, et
        /// surtout les impulsions cumulees seraient perdues entre les passes.
        /// </summary>
        /// <param name="world">
        /// Le decor, sous forme de corps figes. Il DOIT passer par le meme
        /// solveur que les corps mobiles - voir la note ci-dessous.
        /// </param>
        public void Collect(IReadOnlyList<RigidBody> bodies, IReadOnlyList<RigidBody> world)
        {
            _count = 0;

            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];

            // --- Corps mobiles entre eux ---
            for (int i = 0; i < bodies.Count; i++)
            {
                RigidBody a = bodies[i];

                for (int j = i + 1; j < bodies.Count; j++)
                {
                    RigidBody b = bodies[j];

                    // Deux corps figes, ou deux corps endormis : rien a faire.
                    if (a.IsStatic && b.IsStatic)
                        continue;

                    if (a.Sleeping && b.Sleeping)
                        continue;

                    Pair(a, b, points);
                }
            }

            // --- Corps mobiles contre le decor ---
            //
            // ------------------------------------------------------------
            // POURQUOI LE DECOR EST ICI, ET PAS TRAITE A PART.
            //
            // Premiere version : le decor restait au balayage de boites
            // alignees, hors du solveur. Une caisse posee sur une AUTRE
            // caisse, elle-meme posee au sol, ne tenait pas - elle glissait
            // sur le cote et finissait par tomber.
            //
            // La raison : le solveur partage l'impulsion entre les deux
            // caisses. Celle du bas recoit sa part vers le bas, et c'est le
            // SOL, hors solveur, qui l'absorbe. Du point de vue du solveur
            // la contrainte n'est donc jamais satisfaite : il la reresout a
            // chaque pas, injecte du couple, et les caisses fusent de cote.
            //
            // Mesure qui a tranche : avec la caisse du bas rendue STATIQUE,
            // la pile etait parfaitement stable et s'endormait. Le probleme
            // n'etait ni le decalage, ni le frottement, ni le nombre de
            // passes - c'etait la chaine de contacts coupee en deux.
            //
            // Le balayage continu reste indispensable par ailleurs : c'est
            // lui, et lui seul, qui empeche une caisse lachee de 60 m de
            // traverser le sol en un pas.
            // ------------------------------------------------------------
            for (int i = 0; i < bodies.Count; i++)
            {
                RigidBody body = bodies[i];

                if (body.IsStatic || body.Sleeping)
                    continue;

                // ------------------------------------------------------------
                // LE CONTACT SPECULATIF A ETE TENTE ICI, PUIS RETIRE.
                //
                // But : rendre au solveur le frottement contre le decor. Il
                // est plafonne par l'impulsion normale, or celle-ci est nulle
                // contre le sol - le balayage annule la vitesse verticale
                // avant que le solveur ne voie le contact. Une caisse lancee
                // en toupie tourne donc indefiniment.
                //
                // La correction consiste a gonfler les boites du decor d'une
                // marge, a retrancher cette marge de la profondeur, et a
                // viser une vitesse d'approche de -separation/dt. Ca MARCHE :
                // la toupie passe de 6,000 a 0,000 rad/s.
                //
                // Mais ca casse le sommeil des piles, et le garde-fou du
                // projet passe avant. Mesure : les vitesses residuelles
                // culminaient a 0,70 m/s, soit exactement marge/dt (0,01 x 64
                // = 0,64) - la pile pompait a la vitesse que le contact
                // speculatif lui autorisait. Reduire la marge a 2 mm n'a pas
                // suffi, donc le diagnostic est incomplet.
                //
                // En attendant, c'est l'amortissement (R5) qui evacue la
                // rotation residuelle, et le frottement du decor reste inerte.
                // ------------------------------------------------------------
                for (int j = 0; j < world.Count; j++)
                    Pair(world[j], body, points, StaticMargin);
            }
        }

        private void Pair(RigidBody a, RigidBody b, Span<ContactPoint> points, float margin = 0f)
        {
            Obb boxA = a.GetObb();
            Obb boxB = b.GetObb();

            ObbContact hit = ObbCollision.Intersect(boxA, boxB);
            if (!hit.Touching)
                return;

            int found = ObbManifold.Build(boxA, boxB, hit, points);

            for (int p = 0; p < found; p++)
                Add(a, b, hit.Normal, points[p], margin);
        }

        private void Add(RigidBody a, RigidBody b, Vector3 normal, in ContactPoint point, float margin)
        {
            if (_count == _contacts.Length)
                Array.Resize(ref _contacts, _contacts.Length * 2);

            ref Contact c = ref _contacts[_count++];

            c.A = a;
            c.B = b;
            c.Normal = normal;

            // Positif = il reste un vide, negatif = deja imbriques.
            c.Separation = margin - point.Depth;

            c.LeverA = point.Position - a.Position;
            c.LeverB = point.Position - b.Position;

            // Le point est memorise dans le repere de chaque corps : la passe
            // de position deplace et TOURNE les corps, donc il faut pouvoir
            // le suivre. Un point garde en coordonnees monde deviendrait faux
            // des la premiere correction.
            c.LocalA = ToLocal(a, point.Position);
            c.LocalB = ToLocal(b, point.Position);

            BuildTangents(normal, out c.Tangent1, out c.Tangent2);

            c.NormalMass = InverseEffectiveMass(a, b, c.LeverA, c.LeverB, normal);
            c.Tangent1Mass = InverseEffectiveMass(a, b, c.LeverA, c.LeverB, c.Tangent1);
            c.Tangent2Mass = InverseEffectiveMass(a, b, c.LeverA, c.LeverB, c.Tangent2);

            c.NormalImpulse = 0f;
            c.Tangent1Impulse = 0f;
            c.Tangent2Impulse = 0f;

            c.Friction = MathF.Sqrt(MathF.Max(a.Friction, 0f) * MathF.Max(b.Friction, 0f)) * 0.12f;

            // Rebond : seulement sur un vrai impact. Une caisse qui se pose
            // n'a pas a rebondir sur son propre poids.
            Vector3 relative = RelativeVelocity(a, b, c.LeverA, c.LeverB);
            float approach = Vector3.Dot(relative, normal);

            c.RestitutionTarget = approach < -RestitutionThreshold
                ? -MathF.Min(a.Restitution, b.Restitution) * approach
                : 0f;

            // --- Reveil -------------------------------------------------
            //
            // On regarde la vitesse relative ENTIERE, pas seulement
            // l'approche. Une caisse tiree par-dessous glisse sans qu'on se
            // rapproche d'un millimetre : la juger sur la seule approche la
            // laisserait dormir pendant que le sol file dessous.
            //
            // Le seuil doit rester BAS. C'est ici, et nulle part ailleurs,
            // qu'une caisse endormie apprend qu'on la bouscule : sans gravite
            // ni integration, sa vitesse ne peut venir que du solveur, et le
            // frottement ne lui en donnera aucune tant qu'elle dort - il est
            // plafonne par une impulsion normale qu'elle n'a pas.
            //
            // Et on ne reveille QUE ce qui dort. Appeler Wake sur un corps
            // deja eveille remettrait son compteur d'immobilite a zero a
            // chaque pas : plus aucune pile ne s'endormirait.
            if (a.Sleeping && !a.IsStatic && IsMoving(b)) a.Wake();
            if (b.Sleeping && !b.IsStatic && IsMoving(a)) b.Wake();
        }

        private bool IsMoving(RigidBody body)
            => body.Velocity.LengthSquared() > WakeSpeed * WakeSpeed;

        // -------------------------------------------------------------------
        //  Passe de vitesse
        // -------------------------------------------------------------------

        /// <summary>
        /// Empeche les corps de continuer a se rapprocher, et fait accrocher
        /// les surfaces. Ne touche jamais aux positions.
        /// </summary>
        public void SolveVelocities(int iterations, float dt)
        {
            float inverseDt = dt > 0f ? 1f / dt : 0f;

            for (int pass = 0; pass < iterations; pass++)
            {
                for (int i = 0; i < _count; i++)
                    SolveVelocity(ref _contacts[i], inverseDt);
            }
        }

        private static void SolveVelocity(ref Contact c, float inverseDt)
        {
            RigidBody a = c.A, b = c.B;

            // --- Non-penetration, le long de la normale ---
            float speed = Vector3.Dot(RelativeVelocity(a, b, c.LeverA, c.LeverB), c.Normal);

            // Cible de vitesse de separation. Le terme speculatif
            // -separation/dt est en place mais inerte tant que StaticMargin
            // vaut zero : aucun contact n'est alors vu avant le toucher.
            float target = c.RestitutionTarget > 0f
                ? c.RestitutionTarget
                : (c.Separation > 0f ? -c.Separation * inverseDt : 0f);

            float delta = (target - speed) * c.NormalMass;

            // On borne le TOTAL, pas l'increment : c'est ce qui autorise une
            // passe a corriger l'exces d'une passe precedente. Borner
            // l'increment figerait la premiere erreur.
            float total = MathF.Max(0f, c.NormalImpulse + delta);
            delta = total - c.NormalImpulse;
            c.NormalImpulse = total;

            Apply(a, b, c.Normal * delta, c.LeverA, c.LeverB);

            // --- Frottement, dans les deux directions de glissement ---
            //
            // Loi de Coulomb : le frottement est plafonne par la force qui
            // appuie. Cette force, on ne l'estime plus par le poids - piege
            // du solveur precedent - on la CONNAIT : c'est l'impulsion
            // normale qu'on vient d'accumuler.
            float limit = c.Friction * c.NormalImpulse;

            SolveFriction(ref c, c.Tangent1, c.Tangent1Mass, limit, ref c.Tangent1Impulse);
            SolveFriction(ref c, c.Tangent2, c.Tangent2Mass, limit, ref c.Tangent2Impulse);
        }

        private static void SolveFriction(ref Contact c, Vector3 tangent, float tangentMass,
                                          float limit, ref float accumulated)
        {
            float speed = Vector3.Dot(RelativeVelocity(c.A, c.B, c.LeverA, c.LeverB), tangent);

            float delta = -speed * tangentMass;

            float total = Math.Clamp(accumulated + delta, -limit, limit);
            delta = total - accumulated;
            accumulated = total;

            Apply(c.A, c.B, tangent * delta, c.LeverA, c.LeverB);
        }

        // -------------------------------------------------------------------
        //  Passe de position
        // -------------------------------------------------------------------

        /// <summary>
        /// Degage les corps imbriques en deplaçant ET en tournant, sans
        /// jamais toucher aux vitesses. C'est ce qui evite d'injecter de
        /// l'energie - le defaut qui faisait gonfler les piles.
        /// </summary>
        /// <returns>Le chevauchement le plus profond restant, en metres.</returns>
        public float SolvePositions()
        {
            float worst = 0f;

            for (int i = 0; i < _count; i++)
            {
                ref Contact c = ref _contacts[i];
                RigidBody a = c.A, b = c.B;

                // Le point a bouge avec les corps : on le relit dans leur
                // repere plutot que de faire confiance a l'ancienne valeur.
                Vector3 pointA = ToWorld(a, c.LocalA);
                Vector3 pointB = ToWorld(b, c.LocalB);

                Vector3 leverA = pointA - a.Position;
                Vector3 leverB = pointB - b.Position;

                float separation = c.Separation + Vector3.Dot(pointB - pointA, c.Normal);

                if (separation < worst)
                    worst = separation;

                // On ne corrige que ce qui depasse la marge toleree, et
                // seulement une fraction : corriger tout d'un coup ferait
                // sursauter la pile.
                float error = Math.Clamp(
                    PositionRelaxation * (separation + AllowedPenetration),
                    -MaxPositionCorrection, 0f);

                if (error >= 0f)
                    continue;

                float inverseMass = InverseEffectiveMass(a, b, leverA, leverB, c.Normal);
                if (inverseMass <= 0f)
                    continue;

                Vector3 push = c.Normal * (-error * inverseMass);

                Displace(a, -push, leverA);
                Displace(b, push, leverB);
            }

            return worst;
        }

        /// <summary>
        /// Deplace et tourne un corps comme le ferait une impulsion, mais sur
        /// sa POSITION. Aucune vitesse n'est modifiee.
        /// </summary>
        private static void Displace(RigidBody body, Vector3 push, Vector3 lever)
        {
            if (body.IsStatic || body.Sleeping)
                return;

            body.Position += push * body.InverseMass;

            // Le meme bras de levier qu'en vitesse, applique cette fois a
            // l'orientation : c'est ce qui redresse une caisse coincee de
            // travers au lieu de la pousser betement sur le cote.
            Vector3 turn = body.ApplyInverseInertia(Vector3.Cross(lever, push));

            // Integrate prend une vitesse et une duree ; ici on lui donne
            // directement un ANGLE, avec une duree de 1.
            body.Orientation = Inertia.Integrate(body.Orientation, turn, 1f);
        }

        // -------------------------------------------------------------------
        //  Outils
        // -------------------------------------------------------------------

        /// <summary>
        /// Inverse de la masse effective d'un contact selon une direction.
        ///
        /// Les deux premiers termes sont la masse ordinaire. Les deux autres
        /// sont la NOUVEAUTE de cette etape : plus le point de contact est
        /// loin du centre de masse, plus l'impulsion part en rotation au lieu
        /// de deplacer - donc plus le contact parait "mou" dans cette
        /// direction. C'est exactement ce que mesure (r x d) . I(-1) (r x d).
        /// </summary>
        private static float InverseEffectiveMass(RigidBody a, RigidBody b,
                                                  Vector3 leverA, Vector3 leverB, Vector3 direction)
        {
            Vector3 turnA = Vector3.Cross(leverA, direction);
            Vector3 turnB = Vector3.Cross(leverB, direction);

            float mass = InverseMassOf(a) + InverseMassOf(b)
                       + Vector3.Dot(turnA, InverseInertiaOf(a, turnA))
                       + Vector3.Dot(turnB, InverseInertiaOf(b, turnB));

            return mass > 1e-9f ? 1f / mass : 0f;
        }

        // -------------------------------------------------------------------
        //  UN CORPS ENDORMI EST UN MUR.
        //
        //  Il n'est plus integre : lui donner une vitesse ne le deplace pas.
        //  Le traiter comme mobile en fait un PUITS - il encaisse la moitie
        //  de chaque impulsion et ne la rend jamais.
        //
        //  Symptome mesure : une caisse posee sur une caisse endormie gardait
        //  0,2366 m/s indefiniment, soit exactement l'increment de gravite
        //  d'un pas. Jamais arretee, elle ne s'endormait jamais - et le
        //  dessous, lui, dormait tranquillement.
        //
        //  Un corps endormi a donc masse et inertie infinies, exactement
        //  comme un corps statique. Il se reveille par le seul canal prevu :
        //  un voisin qui bouge vraiment.
        // -------------------------------------------------------------------

        private static float InverseMassOf(RigidBody body)
            => body.Sleeping ? 0f : body.InverseMass;

        private static Vector3 InverseInertiaOf(RigidBody body, Vector3 moment)
            => body.Sleeping ? Vector3.Zero : body.ApplyInverseInertia(moment);

        /// <summary>Vitesse de B par rapport a A, AU POINT de contact.</summary>
        private static Vector3 RelativeVelocity(RigidBody a, RigidBody b, Vector3 leverA, Vector3 leverB)
        {
            return b.Velocity + Vector3.Cross(b.AngularVelocity, leverB)
                 - a.Velocity - Vector3.Cross(a.AngularVelocity, leverA);
        }

        /// <summary>
        /// Applique une impulsion egale et opposee aux deux corps.
        ///
        /// Volontairement PAS RigidBody.ApplyImpulseAt : celui-ci reveille le
        /// corps. Un contact au repos est resolu a chaque pas ; s'il
        /// reveillait, plus aucune pile ne s'endormirait jamais.
        /// </summary>
        private static void Apply(RigidBody a, RigidBody b, Vector3 impulse, Vector3 leverA, Vector3 leverB)
        {
            if (!a.IsStatic && !a.Sleeping)
            {
                a.Velocity -= impulse * a.InverseMass;
                a.AngularVelocity -= a.ApplyInverseInertia(Vector3.Cross(leverA, impulse));
            }

            if (!b.IsStatic && !b.Sleeping)
            {
                b.Velocity += impulse * b.InverseMass;
                b.AngularVelocity += b.ApplyInverseInertia(Vector3.Cross(leverB, impulse));
            }
        }

        /// <summary>
        /// Deux directions de glissement perpendiculaires a la normale.
        ///
        /// On croise la normale avec l'axe du monde dont elle est le plus
        /// LOIN : croiser deux vecteurs presque paralleles donnerait un
        /// resultat minuscule et une direction de frottement aleatoire.
        /// </summary>
        private static void BuildTangents(Vector3 normal, out Vector3 tangent1, out Vector3 tangent2)
        {
            Vector3 reference = MathF.Abs(normal.X) < 0.7f ? Vector3.UnitX : Vector3.UnitY;

            tangent1 = Vector3.Normalize(Vector3.Cross(normal, reference));
            tangent2 = Vector3.Cross(normal, tangent1);
        }

        private static Vector3 ToLocal(RigidBody body, Vector3 world)
            => Vector3.Transform(world - body.Position, Quaternion.Conjugate(body.Orientation));

        private static Vector3 ToWorld(RigidBody body, Vector3 local)
            => body.Position + Vector3.Transform(local, body.Orientation);
    }
}
