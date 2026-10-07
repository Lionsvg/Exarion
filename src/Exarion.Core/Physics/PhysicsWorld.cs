// ===========================================================================
//  Exarion - Monde physique
//
//  Fait tomber, glisser, empiler et pousser des corps dynamiques.
//
//  ORDRE D'UN PAS :
//    1. gravite et frottement
//    2. deplacement et rotation contre le decor STATIQUE (balayage)
//    3. detection des contacts entre corps DYNAMIQUES, une seule fois
//    4. passes de vitesse    : ils cessent de se rapprocher, et accrochent
//    5. passes de position   : ils se degagent l'un de l'autre
//    6. mise en sommeil de ce qui ne bouge plus
//
//  Les etapes 4 et 5 sont iteratives : separer A de B peut enfoncer A dans
//  C. Trois passes suffisent pour une pile ; on ne cherche pas la
//  convergence parfaite, on cherche que ca ne tremble pas.
//
//  ---------------------------------------------------------------------
//  DEUX MONDES, DEUX TRAITEMENTS - et c'est temporaire.
//
//  Le decor statique (CollisionWorld) est traite en boites ALIGNEES, par
//  balayage continu : c'est lui qui garantit qu'une caisse lachee de 60 m
//  ne traverse jamais le sol. Les corps dynamiques entre eux passent par le
//  solveur a impulsions, en boites ORIENTEES.
//
//  Consequence assumee a ce stade : une caisse basculee s'enfonce
//  legerement dans le decor par un coin, parce que le decor ne voit que son
//  emprise alignee. C'est l'objet de l'etape R7.
//  ---------------------------------------------------------------------
//
//  Deterministe : l'ordre de parcours est toujours celui de la liste, et le
//  nombre d'iterations est fixe. Deux machines simulent la meme chose.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Exarion.Core.Physics
{
    public sealed class PhysicsWorld
    {
        /// <summary>Le decor immobile. Les corps s'y appuient sans jamais le deplacer.</summary>
        public CollisionWorld Static { get; }

        private readonly List<RigidBody> _bodies = new();
        private readonly ContactSolver _solver = new();

        /// <summary>
        /// Le decor vu comme des corps de masse infinie. Le solveur ne sait
        /// manipuler que des RigidBody ; les boites du decor sont donc
        /// enveloppees une fois pour toutes, et reconstruites seulement si le
        /// decor change.
        /// </summary>
        private readonly List<RigidBody> _worldProxies = new();
        private int _worldRevision = -1;

        private int _nextId = 1;

        public IReadOnlyList<RigidBody> Bodies => _bodies;

        /// <summary>Nombre de points d'appui du dernier pas. Sert au debug et aux tests.</summary>
        public int ContactCount => _solver.ContactCount;

        public Vector3 Gravity = new(0f, -15.24f, 0f);

        /// <summary>Sous cette vitesse et pendant SleepSeconds, un corps s'endort.</summary>
        public float SleepVelocity = 0.14f;
        public float SleepSeconds = 0.45f;

        /// <summary>
        /// Seuil de rotation pour s'endormir, en radians par seconde. Sans
        /// lui, une caisse qui tourne lentement mais dont le centre ne bouge
        /// pas s'endormirait en pleine rotation.
        /// </summary>
        public float SleepAngularVelocity = 0.2f;

        /// <summary>
        /// Passes de degagement. Chaque passe ne resorbe qu'une fraction du
        /// chevauchement, et une caisse qui s'endort FIGE ce qui reste : il
        /// faut donc avoir fini de degager avant que la pile ne s'endorme.
        ///
        /// MESURE, sur cinq caisses lachees au meme endroit - chevauchement
        /// restant apres 500 pas :
        ///     3 passes -> 9,9 cm     6 passes -> 3,8 cm
        ///    10 passes -> 1,5 cm    20 passes -> 1,3 cm
        /// Au-dela de 10 on ne gagne plus rien : c'est le sommeil qui decide.
        /// </summary>
        public int SolverIterations = 10;

        /// <summary>
        /// Passes de vitesse. BEAUCOUP plus nombreuses que les passes de
        /// position, et ce n'est pas un caprice.
        ///
        /// L'ancien solveur n'avait qu'UN point de contact par paire : trois
        /// passes suffisaient. Un appui plat en a quatre, et leurs
        /// corrections angulaires s'annulent mutuellement - chaque point ne
        /// transmet au centre de masse qu'un huitieme de ce qu'il corrige.
        /// Il faut donc bien plus de passes pour que le total arrive.
        /// Mesure a l'appui : voir MESURE dans ContactSolver.
        /// </summary>
        public int VelocityIterations = 12;

        /// <summary>Marge de penetration toleree : evite les tremblements.</summary>
        public const float AllowedPenetration = ContactSolver.AllowedPenetration;

        /// <summary>
        /// Sous cette vitesse d'impact, aucun rebond. Meme seuil que le
        /// solveur : les deux chemins doivent traiter un contact pose de la
        /// meme facon, sinon le corps rebondit d'un cote et pas de l'autre.
        /// </summary>
        private const float RestitutionThreshold = 1f;

        public PhysicsWorld(CollisionWorld staticWorld)
        {
            Static = staticWorld;
        }

        public RigidBody Spawn(Vector3 position, Vector3 size, float mass = 20f)
        {
            var body = new RigidBody
            {
                Id = _nextId++,
                Position = position,
                Size = size,
                Mass = mass,
            };

            _bodies.Add(body);
            return body;
        }

        public bool Remove(RigidBody body) => _bodies.Remove(body);

        public void Clear() => _bodies.Clear();

        /// <summary>Reveille tout. Utile apres avoir modifie le decor.</summary>
        public void WakeAll()
        {
            for (int i = 0; i < _bodies.Count; i++)
                _bodies[i].Wake();
        }

        // -------------------------------------------------------------------

        public void Step(float dt)
        {
            if (dt <= 0f)
                return;

            IntegrateAgainstStatic(dt);

            // Detection UNE SEULE FOIS par pas. La refaire entre deux passes
            // couterait quinze fois plus cher, et surtout les impulsions
            // accumulees par le solveur - ce qui fait toute sa stabilite -
            // seraient perdues a chaque fois.
            RefreshWorldProxies();

            // Le seuil de reveil doit dominer l'increment de gravite d'un
            // pas : le solveur regarde les vitesses juste apres son
            // application, et une caisse posee "tombe" alors deja de
            // g x dt. Sans cette marge, tout corps eveille passe pour
            // mobile et reveille en permanence ses voisins endormis.
            _solver.WakeSpeed = MathF.Max(SleepVelocity, Gravity.Length() * dt) * 2f;

            _solver.Collect(_bodies, _worldProxies);
            _solver.SolveVelocities(VelocityIterations, dt);

            ApplyDamping(dt);

            for (int pass = 0; pass < SolverIterations; pass++)
            {
                _solver.SolvePositions();

                // ETAPE INDISPENSABLE : degager deux caisses l'une de l'autre
                // deplace leurs positions directement, sans balayage. Celle du
                // dessous peut donc etre enfoncee dans le sol par celle du
                // dessus. Sans ce rattrapage, elle traverse et tombe a l'infini.
                DepenetrateFromStatic();
            }

            UpdateSleeping(dt);
        }

        /// <summary>
        /// Reconstruit les mandataires du decor s'il a change. CollisionWorld
        /// ne propose qu'ajouter et vider : suivre le nombre de boites suffit
        /// donc a detecter toute modification.
        /// </summary>
        private void RefreshWorldProxies()
        {
            if (_worldRevision == Static.Boxes.Count)
                return;

            _worldProxies.Clear();

            for (int i = 0; i < Static.Boxes.Count; i++)
            {
                Aabb box = Static.Boxes[i];

                _worldProxies.Add(new RigidBody
                {
                    Id = -(i + 1),
                    Position = box.Center,

                    // Gonflees de la marge de detection, sur chaque face. Le
                    // solveur retranche cette marge de la profondeur, donc la
                    // surface de repos reste la vraie. Marge nulle
                    // actuellement : voir la note dans ContactSolver.Collect.
                    Size = box.Size + new Vector3(2f * ContactSolver.StaticMargin),
                    IsStatic = true,
                });
            }

            _worldRevision = Static.Boxes.Count;
        }

        private void IntegrateAgainstStatic(float dt)
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                RigidBody b = _bodies[i];

                if (b.IsStatic || b.Sleeping)
                    continue;

                b.Velocity += Gravity * dt;

                Vector3 delta = b.Velocity * dt;
                Aabb box = b.GetBox();

                Vector3 moved = Static.MoveAndSlide(box, delta, out Vector3 normal);
                b.Position += moved;

                // La rotation s'integre ici, au meme moment que la position.
                // Le decor ne la voit pas encore - il travaille en boites
                // alignees - c'est la limite connue jusqu'a R7.
                b.IntegrateOrientation(dt);

                // ------------------------------------------------------------
                // LE BALAYAGE NE TOUCHE PLUS AUX VITESSES (R7).
                //
                // Il bloquait le deplacement ET annulait la vitesse normale.
                // La seconde moitie etait fatale au frottement : le solveur
                // decouvrait un contact deja immobile, en deduisait une
                // impulsion normale nulle, et la loi de Coulomb ne lui
                // laissait alors aucun frottement a appliquer. Une caisse
                // lancee en toupie tournait indefiniment.
                //
                // Le solveur voit desormais le decor EN AVANCE - contact
                // speculatif, voir ContactSolver.StaticMargin - et c'est lui
                // qui arrete le corps, avec une vraie impulsion normale.
                //
                // Le balayage garde son role, le seul qui compte ici :
                // empecher une caisse lachee de 60 m de traverser le sol en
                // un seul pas. Aucun contact speculatif ne rattrape ca.
                // ------------------------------------------------------------

                b.Grounded = IsSupported(b);
            }
        }

        /// <summary>Le corps repose-t-il sur quelque chose (decor ou autre corps) ?</summary>
        private bool IsSupported(RigidBody b)
        {
            Aabb box = b.GetBox();
            var probe = new Vector3(0f, -0.04f, 0f);

            SweepHit hit = Static.Sweep(box, probe);
            if (hit.Hit && hit.Normal.Y > 0.7f)
                return true;

            Aabb lowered = box.Translated(probe);

            for (int i = 0; i < _bodies.Count; i++)
            {
                RigidBody other = _bodies[i];
                if (other == b)
                    continue;

                Aabb ob = other.GetBox();

                // Il faut que l'autre soit DESSOUS, pas juste a cote.
                if (lowered.Intersects(ob) && ob.Max.Y <= box.Min.Y + 0.06f)
                    return true;
            }

            return false;
        }

        // -------------------------------------------------------------------

        private void DepenetrateFromStatic()
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                RigidBody b = _bodies[i];

                if (b.IsStatic)
                    continue;

                if (!Static.Depenetrate(b.GetBox(), out Vector3 correction))
                    continue;

                b.Position += correction;

                // Sortir vers le haut veut dire qu'on reposait dans le sol :
                // la vitesse descendante n'a plus lieu d'etre.
                if (correction.Y > 0f && b.Velocity.Y < 0f)
                    b.Velocity.Y = 0f;

                if (correction.X != 0f) b.Velocity.X = 0f;
                if (correction.Z != 0f) b.Velocity.Z = 0f;
            }
        }

        // -------------------------------------------------------------------

        /// <summary>
        /// Retire le peu d'energie que le solveur n'evacue pas.
        ///
        /// Deux conditions, et chacune a coute un test.
        ///
        /// APRES la passe de vitesse : amortir avant reviendrait a amortir la
        /// gravite, et les caisses tomberaient au ralenti.
        ///
        /// SEULEMENT AU CONTACT : l'amortissement compense ce qu'un solveur
        /// iteratif laisse trainer dans un contact. En vol il n'y a pas de
        /// contact, donc rien a compenser - et l'appliquer quand meme fige la
        /// rotation d'une caisse lancee. Mesure : a 1,5 par seconde, une
        /// caisse en l'air perdait la moitie de sa rotation en un demi-seconde.
        /// </summary>
        private void ApplyDamping(float dt)
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                RigidBody b = _bodies[i];

                if (b.IsStatic || b.Sleeping || !b.Grounded)
                    continue;

                b.Velocity *= MathF.Max(0f, 1f - b.LinearDamping * dt);
                b.AngularVelocity *= MathF.Max(0f, 1f - b.AngularDamping * dt);
            }
        }

        private void UpdateSleeping(float dt)
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                RigidBody b = _bodies[i];

                if (b.IsStatic || b.Sleeping)
                    continue;

                bool still = b.Velocity.LengthSquared() < SleepVelocity * SleepVelocity
                          && b.AngularVelocity.LengthSquared() < SleepAngularVelocity * SleepAngularVelocity;

                if (still && b.Grounded)
                {
                    b.StillTime += dt;

                    if (b.StillTime >= SleepSeconds)
                    {
                        b.Sleeping = true;
                        b.Velocity = Vector3.Zero;

                        // Sans effet aujourd'hui - la boucle ne fait rien
                        // tourner avant R4 - mais un corps endormi qui garde
                        // une vitesse angulaire repartirait en vrille au
                        // reveil. Autant fermer la porte tout de suite.
                        b.AngularVelocity = Vector3.Zero;
                    }
                }
                else
                {
                    b.StillTime = 0f;
                }
            }
        }

        // -------------------------------------------------------------------
        //  Interactions
        // -------------------------------------------------------------------

        /// <summary>Corps dynamique le plus proche touche par un rayon.</summary>
        public RigidBody? Raycast(Vector3 origin, Vector3 direction, float maxDistance, out float distance)
        {
            distance = maxDistance;
            RigidBody? best = null;

            for (int i = 0; i < _bodies.Count; i++)
            {
                RigidBody b = _bodies[i];

                if (Combat.CombatTarget.RayVsAabb(origin, direction, distance, b.GetBox(), out float d) && d < distance)
                {
                    distance = d;
                    best = b;
                }
            }

            return best;
        }

        /// <summary>
        /// Souffle tous les corps autour d'un point. Sert aux grenades, et
        /// c'est surtout tres satisfaisant dans un bac a sable.
        /// </summary>
        public void Explode(Vector3 center, float radius, float force)
        {
            if (radius <= 0f)
                return;

            for (int i = 0; i < _bodies.Count; i++)
            {
                RigidBody b = _bodies[i];
                if (b.IsStatic)
                    continue;

                Vector3 offset = b.Position - center;
                float distance = offset.Length();

                if (distance > radius)
                    continue;

                Vector3 dir = distance < 1e-4f ? Vector3.UnitY : offset / distance;

                // Attenuation lineaire, plus une composante verticale : sans
                // elle les caisses glissent au sol au lieu de decoller.
                float falloff = 1f - distance / radius;
                dir = Vector3.Normalize(dir + new Vector3(0f, 0.6f, 0f));

                b.ApplyImpulse(dir * (force * falloff));
            }
        }

        /// <summary>
        /// Pousse les corps qu'un personnage bouscule.
        ///
        /// ---------------------------------------------------------------
        /// PASSER LA VITESSE VOULUE, PAS LA VITESSE REELLE.
        ///
        /// Deuxieme defaut trouve a la main, et le plus retors : coller une
        /// caisse de face ne la bougeait PAS, alors que la longer de cote la
        /// decalait.
        ///
        /// Explication : depuis que les caisses sont solides, un joueur qui
        /// appuie de face est BLOQUE - sa vitesse reelle tombe a zero, et
        /// cette methode en concluait qu'il ne poussait rien. De biais, il
        /// gardait sa composante tangentielle, donc la caisse partait de
        /// travers. Exactement l'inverse de ce qu'on attend.
        ///
        /// Il faut donc <see cref="CharacterMotor.WishVelocity"/> : ce que le
        /// joueur VEUT faire, avant que le decor n'ait son mot a dire.
        /// ---------------------------------------------------------------
        /// ON NE POUSSE PAS CE SUR QUOI ON MARCHE.
        ///
        /// Premier defaut du meme essai : impossible de monter sur une
        /// caisse, elle filait sous les pieds. Rendre les caisses solides ne
        /// suffisait pas - une fois debout dessus, le moindre pas continuait
        /// de les expulser. Il faut les deux : l'obstacle, ET cette exception.
        /// ---------------------------------------------------------------
        /// </summary>
        /// <param name="pusherVelocity">
        /// Vitesse VOULUE du pousseur, pas sa vitesse apres collision.
        /// </param>
        public void PushWith(in Aabb pusher, Vector3 pusherVelocity, float strength)
        {
            var wish = new Vector3(pusherVelocity.X, 0f, pusherVelocity.Z);

            float speed = wish.Length();
            if (speed < 0.1f)
                return;

            Vector3 wishDirection = wish / speed;

            // Le personnage est maintenant BLOQUE par les caisses : leurs
            // boites se touchent au lieu de se chevaucher, et Intersects est
            // strict. Sans cette allonge, plus rien ne serait jamais pousse.
            Aabb reach = pusher.Expanded(new Vector3(0.06f));

            for (int i = 0; i < _bodies.Count; i++)
            {
                RigidBody b = _bodies[i];
                if (b.IsStatic)
                    continue;

                Aabb box = b.GetObb().GetBounds();

                if (!reach.Intersects(box))
                    continue;

                // Sous les pieds : c'est un appui, pas un obstacle.
                if (box.Max.Y <= pusher.Min.Y + 0.1f)
                    continue;

                // On ne pousse que ce vers quoi on va. Sans ce filtre, longer
                // une caisse la ferait deraper sur le cote alors qu'on ne fait
                // que passer a cote.
                Vector3 toBody = box.Center - pusher.Center;
                toBody.Y = 0f;

                float reachSquared = toBody.LengthSquared();
                if (reachSquared < 1e-8f)
                    continue;

                toBody /= MathF.Sqrt(reachSquared);

                float into = Vector3.Dot(wishDirection, toBody);
                if (into <= 0f)
                    continue;   // on s'en eloigne

                b.ApplyImpulse(toBody * (speed * into * strength));
            }
        }
    }
}
