// ===========================================================================
//  Exarion - Controleur de personnage
//
//  Modele de deplacement de la lignee Quake : friction, puis acceleration
//  PROJETEE sur la direction voulue. Ce n'est pas "velocity = input * vitesse".
//  La difference est ce qui donne son inertie et son controle aerien
//  caracteristiques a un FPS tactique.
//
//  L'acceleration ne s'applique qu'a hauteur de ce qui manque pour atteindre
//  la vitesse voulue DANS CETTE DIRECTION :
//
//      manquant = vitesseVoulue - produitScalaire(vitesse, directionVoulue)
//      ajout    = min(acceleration * vitesseVoulue * dt, manquant)
//
//  En l'air, l'acceleration est minuscule : on ne change pas de trajectoire
//  en sautant, mais on garde un controle fin. C'est cette formule exacte qui
//  produit historiquement le strafe-jump.
//
//  Techniques publiques et documentees depuis 1996 ; code ecrit ici.
//  C# pur : se teste sans ouvrir de fenetre.
// ===========================================================================

using System;
using System.Numerics;

namespace Exarion.Core.Physics
{
    public struct MoveInput
    {
        /// <summary>X = lateral, Y = avant/arriere, chacun dans [-1,1].</summary>
        public Vector2 Move;
        /// <summary>Orientation du joueur, en radians.</summary>
        public float Yaw;
        public bool Jump;
        public bool Crouch;
        /// <summary>Marche lente : plus silencieuse et plus precise.</summary>
        public bool Walk;
    }

    public sealed class MotorSettings
    {
        // --- Vitesses, en m/s. Converties depuis CS (1 unite Source = 0.01905 m).
        public float RunSpeed = 4.76f;      // CS : 250 u/s
        public float WalkSpeed = 2.48f;     // CS : 130 u/s
        public float CrouchSpeed = 1.71f;   // CS :  90 u/s

        // --- Dynamique
        public float Gravity = 15.24f;      // CS : 800 u/s^2
        public float JumpHeight = 1.03f;    // CS : ~54 u
        public float GroundAcceleration = 10f;
        public float AirAcceleration = 1.2f;
        public float Friction = 6f;
        public float StopSpeed = 1.9f;

        // --- Gabarit
        public float Width = 0.8f;
        public float StandHeight = 1.83f;
        public float CrouchHeight = 1.24f;
        public float CrouchTransitionSeconds = 0.2f;
        /// <summary>Hauteur de marche franchie automatiquement.</summary>
        public float StepHeight = 0.45f;

        /// <summary>
        /// Pente maximale consideree comme sol. cos(45 deg) ~ 0.707 :
        /// au-dela on glisse au lieu de marcher.
        /// </summary>
        public float MinGroundNormalY = 0.707f;
    }

    public sealed class CharacterMotor
    {
        public MotorSettings Settings { get; }

        /// <summary>Position des PIEDS, pas du centre.</summary>
        public Vector3 Position;
        public Vector3 Velocity;

        /// <summary>
        /// Vitesse VOULUE au dernier pas, horizontale, avant collision.
        ///
        /// C'est elle qui dit si le joueur appuie contre quelque chose.
        /// <see cref="Velocity"/> ne le dit pas : un joueur qui pousse de tout
        /// son poids contre une caisse a une vitesse reelle NULLE, puisque la
        /// caisse le bloque. Juger la poussee sur la vitesse reelle donnait un
        /// resultat absurde - rien de face, et une caisse qui se derobe des
        /// qu'on la longeait.
        /// </summary>
        public Vector3 WishVelocity { get; private set; }

        public bool Grounded { get; private set; }
        public float Height { get; private set; }
        public StanceState Stance { get; private set; } = StanceState.Still;

        /// <summary>Vitesse horizontale, en m/s. Sert au HUD et aux sons de pas.</summary>
        public float HorizontalSpeed => new Vector2(Velocity.X, Velocity.Z).Length();

        public CharacterMotor(MotorSettings? settings = null)
        {
            Settings = settings ?? new MotorSettings();
            Height = Settings.StandHeight;
        }

        public Aabb GetBox() => Aabb.ForCharacter(Position, Settings.Width, Height);

        // -------------------------------------------------------------------
        //  SAUVEGARDE ET RESTAURATION DE L'ETAT
        //
        //  ⚠️⚠️ C'EST CE QUI REND LA PREDICTION RESEAU POSSIBLE (3.5/3.6). Le
        //  client doit pouvoir revenir a l'etat que le serveur lui dicte, puis
        //  REJOUER toutes ses entrees posterieures. Sans restauration exacte, le
        //  rejeu part d'un etat different et diverge un peu plus a chaque
        //  correction - ca se manifeste comme un joueur qui « glisse » ou qui
        //  tremble, et on cherche dans le reseau alors que c'est ici.
        //
        //  ⚠️ TROIS CHAMPS, ET SEULEMENT TROIS. Grounded et Stance ne sont PAS
        //  sauvegardes, et ce n'est pas un oubli : ils sont RECALCULES a chaque
        //  Step (Grounded au debut par CheckGrounded, Stance a la fin par
        //  UpdateStance). Les restaurer ne servirait a rien et laisserait croire
        //  qu'ils portent de l'information entre deux pas.
        //
        //  ⚠️ La HAUTEUR, elle, doit y etre. Elle transite lentement entre
        //  debout et accroupi : la reinitialiser a chaque correction ferait
        //  sauter la taille du joueur, donc sa camera, plusieurs fois par
        //  seconde.
        // -------------------------------------------------------------------

        /// <summary>Etat complet et suffisant pour reprendre la simulation.</summary>
        public readonly struct MotorState
        {
            public readonly Vector3 Position;
            public readonly Vector3 Velocity;
            public readonly float Height;

            public MotorState(Vector3 position, Vector3 velocity, float height)
            {
                Position = position;
                Velocity = velocity;
                Height = height;
            }
        }

        public MotorState Save() => new(Position, Velocity, Height);

        public void Restore(in MotorState state)
        {
            Position = state.Position;
            Velocity = state.Velocity;

            // Une hauteur nulle ou aberrante viendrait du reseau : on la borne
            // au gabarit, sinon la boite du joueur serait degeneree et il
            // traverserait le decor.
            Height = Math.Clamp(state.Height, Settings.CrouchHeight, Settings.StandHeight);
        }

        /// <summary>Un pas de simulation. dt doit etre le pas FIXE, jamais un delta d'image.</summary>
        public void Step(in MoveInput input, float dt, CollisionWorld world)
        {
            UpdateHeight(input, dt, world);

            Vector3 wishDir = ComputeWishDirection(input);
            float wishSpeed = ComputeWishSpeed(input);

            // Ce que le joueur VEUT faire, avant que le decor n'ait son mot a
            // dire. Sa vitesse reelle sera nulle s'il pousse contre un mur -
            // ce qui ne veut pas dire qu'il ne pousse pas.
            WishVelocity = wishDir * wishSpeed;

            Grounded = CheckGrounded(world);

            if (Grounded)
            {
                if (Velocity.Y < 0f)
                    Velocity.Y = 0f;

                ApplyFriction(dt);
                Accelerate(wishDir, wishSpeed, Settings.GroundAcceleration, dt);

                if (input.Jump)
                {
                    // v = sqrt(2 g h) : la vitesse initiale qui atteint exactement h.
                    Velocity.Y = MathF.Sqrt(2f * Settings.Gravity * Settings.JumpHeight);
                    Grounded = false;
                }
            }
            else
            {
                Accelerate(wishDir, wishSpeed, Settings.AirAcceleration, dt);
                Velocity.Y -= Settings.Gravity * dt;
            }

            MoveWithCollision(dt, world);
            UpdateStance(input);
        }

        // -------------------------------------------------------------------

        private void UpdateHeight(in MoveInput input, float dt, CollisionWorld world)
        {
            float target = input.Crouch ? Settings.CrouchHeight : Settings.StandHeight;

            // On ne se releve pas sous un plafond : sinon on traverse la geometrie.
            if (target > Height)
            {
                var standing = Aabb.ForCharacter(Position, Settings.Width, target);
                if (world.Overlaps(standing))
                    target = Height;
            }

            float rate = (Settings.StandHeight - Settings.CrouchHeight)
                       / MathF.Max(0.01f, Settings.CrouchTransitionSeconds);

            Height = MoveTowards(Height, target, rate * dt);
        }

        private Vector3 ComputeWishDirection(in MoveInput input)
        {
            // Yaw = 0 regarde vers -Z (convention OpenGL main droite).
            float sin = MathF.Sin(input.Yaw);
            float cos = MathF.Cos(input.Yaw);

            Vector3 forward = new Vector3(-sin, 0f, -cos);
            Vector3 right = new Vector3(cos, 0f, -sin);

            Vector3 dir = right * input.Move.X + forward * input.Move.Y;

            // Normalise pour que la diagonale n'aille pas plus vite.
            float lenSq = dir.LengthSquared();
            return lenSq > 1f ? Vector3.Normalize(dir) : dir;
        }

        private float ComputeWishSpeed(in MoveInput input)
        {
            if (IsCrouched)
                return Settings.CrouchSpeed;

            return input.Walk ? Settings.WalkSpeed : Settings.RunSpeed;
        }

        private bool IsCrouched => Height < (Settings.StandHeight + Settings.CrouchHeight) * 0.5f;

        /// <summary>
        /// Friction facon Quake : la perte est proportionnelle a la vitesse,
        /// avec un plancher (StopSpeed) qui permet de s'arreter net.
        /// </summary>
        private void ApplyFriction(float dt)
        {
            float speed = HorizontalSpeed;
            if (speed < 1e-4f)
            {
                Velocity.X = 0f;
                Velocity.Z = 0f;
                return;
            }

            float control = MathF.Max(speed, Settings.StopSpeed);
            float drop = control * Settings.Friction * dt;

            float newSpeed = MathF.Max(0f, speed - drop) / speed;
            Velocity.X *= newSpeed;
            Velocity.Z *= newSpeed;
        }

        private void Accelerate(Vector3 wishDir, float wishSpeed, float accel, float dt)
        {
            if (wishSpeed <= 0f || wishDir.LengthSquared() < 1e-8f)
                return;

            var horizontal = new Vector3(Velocity.X, 0f, Velocity.Z);
            float current = Vector3.Dot(horizontal, wishDir);
            float missing = wishSpeed - current;

            if (missing <= 0f)
                return;

            float add = MathF.Min(accel * wishSpeed * dt, missing);

            Velocity.X += add * wishDir.X;
            Velocity.Z += add * wishDir.Z;
        }

        private bool CheckGrounded(CollisionWorld world)
        {
            // On sonde legerement sous les pieds. La marge doit rester petite,
            // sinon on est "au sol" en plein saut descendant.
            var box = GetBox();
            SweepHit hit = world.Sweep(box, new Vector3(0f, -0.05f, 0f));

            return hit.Hit && hit.Normal.Y >= Settings.MinGroundNormalY;
        }

        private void MoveWithCollision(float dt, CollisionWorld world)
        {
            Vector3 delta = Velocity * dt;
            Aabb box = GetBox();

            // 1) Deplacement horizontal, avec glissement le long des murs.
            var horizontal = new Vector3(delta.X, 0f, delta.Z);
            Vector3 movedH = world.MoveAndSlide(box, horizontal, out Vector3 wallNormal);

            // 2) Si on a ete freine au sol, tenter de franchir une marche.
            if (Grounded && horizontal.LengthSquared() > 1e-8f)
            {
                float blocked = 1f - movedH.Length() / MathF.Max(1e-6f, horizontal.Length());
                if (blocked > 0.05f)
                {
                    Vector3 stepped = TryStepUp(world, box, horizontal);
                    if (stepped.LengthSquared() > movedH.LengthSquared())
                        movedH = stepped;
                }
            }

            Position += movedH;

            // Un mur absorbe la vitesse qui rentrait dedans, sinon on continue
            // de pousser et on colle de facon peu naturelle.
            if (wallNormal != Vector3.Zero)
            {
                var vh = new Vector3(Velocity.X, 0f, Velocity.Z);
                float into = Vector3.Dot(vh, wallNormal);
                if (into < 0f)
                {
                    vh -= wallNormal * into;
                    Velocity.X = vh.X;
                    Velocity.Z = vh.Z;
                }
            }

            // 3) Deplacement vertical.
            box = GetBox();
            Vector3 movedV = world.MoveAndSlide(box, new Vector3(0f, delta.Y, 0f), out Vector3 vNormal);
            Position += movedV;

            if (vNormal != Vector3.Zero)
            {
                // Sol touche en descendant, ou plafond en montant : on annule
                // la vitesse verticale plutot que de l'accumuler.
                if ((vNormal.Y > 0f && Velocity.Y < 0f) || (vNormal.Y < 0f && Velocity.Y > 0f))
                    Velocity.Y = 0f;
            }
        }

        /// <summary>
        /// Monter, avancer, redescendre. Sans ca, on reste bloque sur la
        /// moindre marche de 10 cm, ce qui est tres desagreable a jouer.
        /// </summary>
        private Vector3 TryStepUp(CollisionWorld world, in Aabb box, Vector3 horizontal)
        {
            var up = new Vector3(0f, Settings.StepHeight, 0f);

            Vector3 climbed = world.MoveAndSlide(box, up, out _);
            if (climbed.Y < Settings.StepHeight * 0.5f)
                return Vector3.Zero;   // pas la place de monter

            Aabb raised = box.Translated(climbed);
            Vector3 forward = world.MoveAndSlide(raised, horizontal, out _);
            if (forward.LengthSquared() < 1e-6f)
                return Vector3.Zero;   // rien gagne

            Aabb advanced = raised.Translated(forward);
            Vector3 down = world.MoveAndSlide(advanced, -up, out Vector3 groundNormal);

            // Il faut retomber sur du sol, sinon on flotte dans le vide.
            if (groundNormal.Y < Settings.MinGroundNormalY)
                return Vector3.Zero;

            return climbed + forward + down;
        }

        private void UpdateStance(in MoveInput input)
        {
            if (!Grounded)
            {
                Stance = StanceState.Airborne;
                return;
            }

            if (IsCrouched)
            {
                Stance = StanceState.Crouching;
                return;
            }

            float speed = HorizontalSpeed;

            if (speed < 0.25f)
                Stance = StanceState.Still;
            else if (input.Walk || speed < Settings.WalkSpeed + 0.3f)
                Stance = StanceState.Walking;
            else
                Stance = StanceState.Running;
        }

        private static float MoveTowards(float current, float target, float maxDelta)
        {
            float diff = target - current;
            if (MathF.Abs(diff) <= maxDelta)
                return target;

            return current + MathF.Sign(diff) * maxDelta;
        }
    }
}
