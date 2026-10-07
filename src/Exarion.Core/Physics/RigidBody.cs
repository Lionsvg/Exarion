// ===========================================================================
//  Exarion - Corps dynamique
//
//  Une caisse qui tombe, glisse, s'empile et se fait pousser.
//
//  Le corps porte deux etats parallels :
//
//    - LINEAIRE   : Position, Velocity, Mass. C'est ce que PhysicsWorld
//                   simule aujourd'hui, en boites alignees sur les axes.
//    - ANGULAIRE  : Orientation, AngularVelocity, inertie. Ecrit et teste
//                   ici (etape R3), mais PAS ENCORE BRANCHE : la boucle de
//                   simulation reste en AABB jusqu'a R4.
//
//  Ce decalage est voulu. La rotation touche au coeur du solveur, et le
//  garde-fou du projet - une pile de caisses qui ne tremble pas - doit
//  rester vert a chaque etape. On ecrit et on prouve la mecanique d'abord,
//  on la branche ensuite.
//
//  Convention : la vitesse angulaire est exprimee dans le MONDE, en radians
//  par seconde. Sa direction est l'axe, sa longueur la vitesse.
// ===========================================================================

using System.Numerics;

namespace Exarion.Core.Physics
{
    public sealed class RigidBody
    {
        public int Id;

        /// <summary>Centre du corps, et centre de masse.</summary>
        public Vector3 Position;

        /// <summary>Dimensions completes (pas les demi-dimensions).</summary>
        public Vector3 Size = Vector3.One;

        public Vector3 Velocity;

        /// <summary>Orientation du corps. Identite = aligne sur les axes du monde.</summary>
        public Quaternion Orientation = Quaternion.Identity;

        /// <summary>
        /// Vitesse angulaire dans le repere du MONDE, en radians par seconde.
        /// </summary>
        public Vector3 AngularVelocity;

        public float Mass = 20f;

        /// <summary>Rebond, dans [0,1]. 0 = la caisse ne rebondit pas du tout.</summary>
        public float Restitution = 0.12f;

        /// <summary>Frottement au sol, dans [0,1]. Plus haut = s'arrete plus vite.</summary>
        public float Friction = 4.5f;

        /// <summary>
        /// Amortissement de la rotation, par seconde.
        ///
        /// Le frottement de contact freine deja une caisse qui tourne, mais
        /// il laisse une agitation residuelle : un solveur iteratif ne
        /// converge jamais tout a fait, et ce qui reste ne descend pas seul
        /// jusqu'a zero. Cette agitation suffit a empecher une pile de
        /// s'endormir - donc a la faire simuler indefiniment.
        ///
        /// Ce n'est pas de la physique, c'est un aveu : on retire a la main
        /// le peu d'energie que le solveur n'arrive pas a evacuer.
        /// </summary>
        public float AngularDamping = 1.5f;

        /// <summary>
        /// Amortissement de la vitesse, par seconde. Meme role, meme aveu.
        /// Volontairement plus faible que l'amortissement angulaire : une
        /// caisse lancee doit garder son elan.
        /// </summary>
        public float LinearDamping = 0.4f;

        public bool Grounded;

        /// <summary>
        /// Un corps endormi n'est plus simule. Sans ca, une pile de caisses
        /// vibre indefiniment et consomme du temps pour rien.
        /// </summary>
        public bool Sleeping;

        /// <summary>Temps passe sous le seuil de reveil, en secondes.</summary>
        public float StillTime;

        /// <summary>Un corps statique ne bouge jamais mais bloque les autres.</summary>
        public bool IsStatic;

        /// <summary>Emprise alignee sur les axes du monde. Ignore l'orientation.</summary>
        public Aabb GetBox() => Aabb.FromCenterSize(Position, Size);

        /// <summary>Emprise ORIENTEE, celle qui servira au solveur a partir de R4.</summary>
        public Obb GetObb() => Obb.FromSize(Position, Size, Orientation);

        public float InverseMass => (IsStatic || Mass <= 0f) ? 0f : 1f / Mass;

        /// <summary>
        /// Inverse du tenseur d'inertie, en diagonale, dans le repere propre
        /// du corps. Zero sur un axe veut dire "ne tourne pas autour de lui".
        /// </summary>
        public Vector3 InverseInertiaLocal
            => IsStatic ? Vector3.Zero : Inertia.BoxInverse(Size, Mass);

        /// <summary>
        /// Action de l'inverse du tenseur exprime dans le MONDE. Prend un
        /// moment (un couple, ou le moment d'une impulsion) et rend
        /// l'acceleration - ou la variation de vitesse - angulaire.
        /// </summary>
        public Vector3 ApplyInverseInertia(Vector3 moment)
            => Inertia.Apply(InverseInertiaLocal, Orientation, moment);

        /// <summary>
        /// Vitesse d'un point du corps, en coordonnees monde. Un point loin
        /// du centre va plus vite que le centre : c'est ce terme qui fera
        /// deraper les caisses en R4.
        /// </summary>
        public Vector3 GetVelocityAt(Vector3 worldPoint)
            => Velocity + Vector3.Cross(AngularVelocity, worldPoint - Position);

        public void Wake()
        {
            Sleeping = false;
            StillTime = 0f;
        }

        /// <summary>
        /// Applique une impulsion (masse x vitesse) AU CENTRE DE MASSE : elle
        /// ne fait donc pas tourner le corps. Reveille le corps.
        /// </summary>
        public void ApplyImpulse(Vector3 impulse)
        {
            if (IsStatic)
                return;

            Velocity += impulse * InverseMass;
            Wake();
        }

        /// <summary>
        /// Applique une impulsion EN UN POINT. C'est le bras de levier - la
        /// distance entre le point et le centre de masse - qui transforme une
        /// poussee en rotation. Frapper une caisse en plein milieu la
        /// deplace ; la frapper sur un coin la fait pivoter.
        /// </summary>
        public void ApplyImpulseAt(Vector3 impulse, Vector3 worldPoint)
        {
            if (IsStatic)
                return;

            Velocity += impulse * InverseMass;
            AngularVelocity += ApplyInverseInertia(Vector3.Cross(worldPoint - Position, impulse));

            Wake();
        }

        /// <summary>Impulsion purement rotative, sans effet sur la trajectoire.</summary>
        public void ApplyAngularImpulse(Vector3 angularImpulse)
        {
            if (IsStatic)
                return;

            AngularVelocity += ApplyInverseInertia(angularImpulse);
            Wake();
        }

        /// <summary>
        /// Fait tourner le corps pendant un pas.
        ///
        /// La vitesse angulaire n'est PAS modifiee ici : le terme
        /// gyroscopique w x (I*w), celui qui fait culbuter une raquette de
        /// tennis lancee en l'air, est volontairement absent. Il est instable
        /// en integration explicite, et sur une caisse d'un metre il est
        /// invisible. Consequence assumee : un corps libre garde sa vitesse
        /// angulaire au lieu de vaciller.
        /// </summary>
        public void IntegrateOrientation(float dt)
        {
            if (IsStatic || dt <= 0f)
                return;

            Orientation = Inertia.Integrate(Orientation, AngularVelocity, dt);
        }
    }
}
