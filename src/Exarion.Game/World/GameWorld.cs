// ===========================================================================
//  Le monde simule du bac a sable
//
//  Tout ce qui DECOULE de la carte et avance au pas fixe : monde de
//  collision, physique des caisses, corps du joueur, entites vivantes, PNJ,
//  portes. Aucun appel graphique ici - le maillage du decor est refait a
//  part, par celui qui possede le contexte OpenGL.
//
//  ⚠️ TOUT ce qui derive du decor doit etre refait au chargement, et l'oubli
//  d'un seul point donne un defaut silencieux : la physique qui garde
//  l'ancien monde de collision, les particules qui rebondissent sur des murs
//  disparus, le decor marchable du joueur qui date de la carte precedente.
//  C'est pour ca qu'il n'y a qu'UNE methode qui le fait : Rebuild.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Effects;
using Exarion.Core.Entities;
using Exarion.Core.Physics;

namespace Exarion.Game.World
{
    internal sealed class GameWorld
    {
        private readonly ParticleSystem _particles;

        // --- Reglages de la physique, gardes HORS du monde physique ---
        //
        // ⚠️⚠️ LE MONDE PHYSIQUE EST RECREE A CHAQUE CHARGEMENT DE CARTE (les
        // caisses appartenaient a l'ancien decor). Les ConVar ecrivaient dans
        // l'instance du moment : apres un map_load, « gravity 20 » continuait
        // d'afficher 20 et le JOUEUR tombait a 20, pendant que les caisses
        // retombaient a 15,24 - le piege des ConVar qui mentent, une fois de
        // plus. Constate le 07/10 avec la commande « phys ».
        private Vector3 _gravity;
        private int _solverIterations;
        private int _velocityIterations;
        private float _sleepSeconds;

        public GameWorld(ParticleSystem particles)
        {
            _particles = particles;

            // Les defauts sont ceux du moteur physique, lus et non recopies :
            // une valeur dupliquee ici finirait par diverger de la sienne.
            _gravity = Physics.Gravity;
            _solverIterations = Physics.SolverIterations;
            _velocityIterations = Physics.VelocityIterations;
            _sleepSeconds = Physics.SleepSeconds;
        }

        /// <summary>« gravity » cote caisses. Reveille tout : un corps endormi ne verrait pas le changement.</summary>
        public void SetGravity(float metersPerSecondSquared)
        {
            _gravity = new Vector3(0f, -metersPerSecondSquared, 0f);
            Physics.Gravity = _gravity;
            Physics.WakeAll();
        }

        /// <summary>« phys_iterations ».</summary>
        public void SetSolverIterations(int passes)
        {
            _solverIterations = passes;
            Physics.SolverIterations = passes;
        }

        /// <summary>« phys_velocity_iterations ».</summary>
        public void SetVelocityIterations(int passes)
        {
            _velocityIterations = passes;
            Physics.VelocityIterations = passes;
        }

        /// <summary>« phys_sleep ».</summary>
        public void SetSleepSeconds(float seconds)
        {
            _sleepSeconds = seconds;
            Physics.SleepSeconds = seconds;
        }

        /// <summary>Carte courante. Source unique du decor, des lumieres et du point de depart.</summary>
        public MapData? Map { get; private set; }

        /// <summary>Fichier d'ou vient la carte, ou SandboxMap.Source si elle vient du code.</summary>
        public string MapSource { get; private set; } = SandboxMap.Source;

        /// <summary>Le decor seul : ce que voient les balles, le son et le client reseau.</summary>
        public CollisionWorld Level { get; private set; } = new();

        /// <summary>
        /// Ce sur quoi le JOUEUR bute : le decor, plus les caisses, plus les
        /// portes.
        ///
        /// Le controleur de personnage ne connait que des boites alignees, et
        /// c'est voulu - un FPS tactique n'a pas besoin de physique rigide
        /// pour le joueur. Mais il ne voyait que le decor : les caisses lui
        /// etaient transparentes. On les traversait en les bousculant, et
        /// monter dessus etait impossible par construction.
        ///
        /// Reconstruit a chaque pas, parce que les caisses bougent.
        /// </summary>
        public CollisionWorld Walkable { get; } = new();

        public PhysicsWorld Physics { get; private set; } = new(new CollisionWorld());

        /// <summary>Le corps du joueur. Le MEME moteur que les PNJ et le client reseau.</summary>
        public CharacterMotor Motor { get; } = new();

        /// <summary>Pieds du joueur au pas precedent : le rendu interpole entre les deux.</summary>
        public Vector3 PreviousFeet { get; set; }

        // --- Entites vivantes (E13) ---
        public EntityWorld Entities { get; } = new();

        // --- PNJ (E14) ---
        public AgentWorld Agents { get; } = new();

        // --- Geometrie mobile : portes, ascenseurs, ponts (E17) ---
        public MoverWorld Movers { get; } = new();

        // --- Logique : relais, minuteries, compteurs ---
        public LogicWorld Logic { get; } = new();

        // --- Ambiance : lumieres et sons commutables ---
        public EnvWorld Env { get; } = new();

        /// <summary>« carry_debug » : trace le portage du joueur par les plateformes.</summary>
        public bool CarryDebug { get; set; }

        /// <summary>« nav_show » : le chemin de chaque PNJ, dessine dans le decor.</summary>
        public bool ShowNavigation { get; set; }

        /// <summary>Position des yeux du joueur.</summary>
        public Vector3 EyePosition => Motor.Position + new Vector3(0f, Motor.Height - 0.18f, 0f);

        /// <summary>
        /// Reconstruit tout ce qui DECOULE de la geometrie.
        ///
        /// Separe du placement au point de depart parce que l'editeur en a
        /// besoin a chaque modification, et que lui ne doit SURTOUT pas se
        /// faire replacer au point de depart a chaque boite deplacee.
        /// </summary>
        public void Rebuild(MapData map, string source)
        {
            var colors = new List<Vector3>();

            Map = map;
            MapSource = source;
            Level = map.ToCollisionWorld(colors);

            // Les caisses en vol appartenaient a l'ancien decor : les garder
            // reviendrait a les laisser dans des murs qui ont bouge. Les
            // REGLAGES, eux, survivent : voir SetGravity.
            Physics = new PhysicsWorld(Level)
            {
                Gravity = _gravity,
                SolverIterations = _solverIterations,
                VelocityIterations = _velocityIterations,
                SleepSeconds = _sleepSeconds,
            };
            _particles.World = Level;

            SpawnMapCrates();

            // ⚠️ Le monde d'entites est RECHARGE, pas conserve : un indice de
            // volume renvoie a une autre entite d'une carte a l'autre, et un
            // declencheur consomme bloquerait un declencheur sans rapport.
            Entities.Load(map);

            // Les PNJ se reconstruisent aussi : garder un garde en ronde sur un
            // chemin qui n'existe plus le ferait traverser le nouveau decor.
            Agents.Load(map, Level);

            // Les portes aussi, et APRES Entities.Load qui vide les cibles.
            Movers.Load(map);
            Movers.RegisterInto(Entities);

            // La logique de meme : sa file est videe, et elle se reinscrit.
            Logic.Load(map);
            Logic.RegisterInto(Entities);

            // L'ambiance et les PNJ nommes sont des cibles eux aussi.
            Env.Load(map);
            Env.RegisterInto(Entities);
            Agents.RegisterInto(Entities);

            RebuildWalkable();
        }

        /// <summary>
        /// Remet les gardes a leur poste, les declencheurs et les portes a zero.
        ///
        /// ⚠️ LES PNJ SONT INSTANCIES ICI, en entrant en partie, pas seulement
        /// au chargement de la carte. AgentWorld prend un instantane : une
        /// entite ajoutee dans l'editeur n'existerait pas en partie sans ce
        /// rechargement - constate, la console repondait « aucun PNJ dans la
        /// carte » juste apres en avoir pose deux.
        /// </summary>
        public void RestartEntities()
        {
            if (Map is null)
                return;

            // Les caisses reviennent a ce que la carte declare, comme les gardes
            // a leur poste : « Jouer » recommence le niveau.
            Physics.Clear();
            SpawnMapCrates();

            Agents.Load(Map, Level);

            Entities.Load(Map);

            // ⚠️ L'ORDRE EST OBLIGATOIRE : Load efface les cibles inscrites
            // (sinon une porte de la carte precedente resterait joignable
            // sous son nom). Les movers se reinscrivent donc APRES.
            Movers.Load(Map);
            Movers.RegisterInto(Entities);

            // Et la logique : « Jouer » remet les minuteries et les compteurs a
            // zero, et oublie les sorties encore en attente.
            Logic.Load(Map);
            Logic.RegisterInto(Entities);

            // L'ambiance revient a ce que la carte declare (lampes, sons), et les
            // PNJ - recharges plus haut, AVANT Entities.Load - s'inscrivent ici.
            Env.Load(Map);
            Env.RegisterInto(Entities);
            Agents.RegisterInto(Entities);
        }

        /// <summary>
        /// Les caisses que la carte declare (prop_crate), et elles seules.
        ///
        /// ⚠️ Jusqu'au 07/10, quatre caisses tombaient en (0, 0, 8) apres TOUTE
        /// carte chargee au demarrage : voir EntityTypes.Crate.
        /// </summary>
        private void SpawnMapCrates()
        {
            if (Map is null)
                return;

            foreach (CrateSpawn crate in CrateStacks.From(Map))
                Physics.Spawn(crate.Center, new Vector3(crate.Size), crate.Mass);
        }

        /// <summary>
        /// Le mannequin decoratif que declare la carte, s'il y en a un : sa
        /// position et la direction ou il regarde, en degres.
        /// </summary>
        public MapEntity? Mannequin()
        {
            if (Map is null)
                return null;

            foreach (MapEntity entity in Map.Entities)
                if (string.Equals(entity.Type, EntityTypes.Mannequin, StringComparison.Ordinal))
                    return entity;

            return null;
        }

        /// <summary>
        /// Place le joueur sur un point d'apparition de la carte, et rend
        /// l'orientation qu'il doit prendre, en degres.
        ///
        /// ⚠️ La propriete `spawn` historique reste le REPLI, elle ne disparait
        /// pas : une carte qui ne declare aucune entite d'apparition ne doit pas
        /// laisser le joueur a l'origine du monde, ou il serait la plupart du
        /// temps DANS le sol.
        ///
        /// Le choix est deterministe - toujours le premier - et pas aleatoire.
        /// Un tirage rendrait les captures headless incomparables d'une session
        /// a l'autre, ce qui casserait toute la methode de verification.
        ///
        /// ⚠️ Les angles viennent de la MEME source que la position. Prendre la
        /// position de l'entite et les angles de la carte donnerait un joueur
        /// bien place qui regarde ailleurs - defaut discret, et exactement le
        /// genre qu'on met une heure a attribuer.
        /// </summary>
        public (float YawDegrees, float PitchDegrees) PlaceAtSpawn(MapData map)
        {
            Vector3 feet = map.Spawn;
            float yaw = map.SpawnYaw;
            float pitch = map.SpawnPitch;

            foreach (MapEntity spawn in map.SpawnPoints())
            {
                feet = spawn.Position;
                yaw = spawn.Yaw;
                pitch = spawn.Pitch;
                break;
            }

            Motor.Position = feet;
            Motor.Velocity = Vector3.Zero;
            PreviousFeet = Motor.Position;

            return (yaw, pitch);
        }

        /// <summary>
        /// Reconstruit le monde sur lequel le joueur bute : decor + caisses + portes.
        ///
        /// On prend l'emprise ALIGNEE de la boite orientee, pas la taille
        /// brute du corps. Pour une caisse droite les deux sont identiques ;
        /// pour une caisse basculee, la seconde serait trop petite et on lui
        /// passerait au travers par un coin.
        ///
        /// C'est genereux - on marche sur l'emprise, pas sur la caisse
        /// inclinee elle-meme - mais c'est le prix d'un joueur en boite
        /// alignee, et ca reste infiniment mieux que de traverser.
        /// </summary>
        public void RebuildWalkable()
        {
            Walkable.Clear();

            var level = Level.Boxes;
            for (int i = 0; i < level.Count; i++)
                Walkable.Add(level[i]);

            var bodies = Physics.Bodies;
            for (int i = 0; i < bodies.Count; i++)
                Walkable.Add(bodies[i].GetObb().GetBounds());

            // ⚠️ LES PORTES ET ASCENSEURS ENTRENT ICI, et l'ordre compte : cet
            // appel doit venir APRES Movers.Step, sinon la boite de collision
            // date du pas precedent. A 2 m/s une porte se deplace de 3 cm par
            // pas - assez pour qu'un joueur colle contre elle la traverse.
            Movers.AppendTo(Walkable);
        }

        /// <summary>
        /// Fait avancer les portes et rapporte leurs sorties de fin de course.
        ///
        /// Un mover emet OnOuvert / OnFerme quand il arrive : c'est ce qui
        /// permet d'enchainer (une porte qui s'ouvre declenche un garde). Ces
        /// sorties passent par le MEME flux d'evenements que les volumes.
        /// </summary>
        public void StepMovers(float dt, Action<EntityEvent> report)
        {
            // Ce qu'une porte ne doit pas traverser : le joueur, les PNJ, les
            // caisses. Les boites de CE pas - celles d'avant le mouvement.
            _blockers.Clear();
            _blockers.Add(Motor.GetBox());

            foreach (Agent agent in Agents.Agents)
                _blockers.Add(agent.Motor.GetBox());

            foreach (RigidBody body in Physics.Bodies)
                _blockers.Add(body.GetObb().GetBounds());

            Movers.Step(dt, _blockers);
            Movers.DeliverOutputs(Entities, report);
        }

        private readonly List<Aabb> _blockers = new();

        /// <summary>
        /// Deplace le joueur avec la plateforme sur laquelle il se tient.
        ///
        /// ⚠️ SANS CECI UN ASCENSEUR EST INUTILISABLE : la plateforme monte, le
        /// joueur reste en l'air, retombe dessus, et recommence - il vibre au
        /// lieu de monter. Ce n'est pas un raffinement.
        /// </summary>
        public void CarryPlayer()
        {
            bool carried = Movers.CarryRider(ref Motor.Position, Motor.GetBox());

            if (CarryDebug)
            {
                // On refait EXACTEMENT ce que fait CheckGrounded. C'est cette
                // ligne qui a trouve le defaut du portage : elle montre que le
                // balayage repondait hit=False alors qu'une plateforme etait
                // sous les pieds. A garder pour la prochaine fois.
                SweepHit hit = Walkable.Sweep(Motor.GetBox(), new Vector3(0f, -0.05f, 0f));

                Console.WriteLine($"  [carry] pieds {Motor.Position.Y:0.0000} porte={carried} " +
                                  $"balayage(hit={hit.Hit} t={hit.Time:0.000} ny={hit.Normal.Y:0.00}) " +
                                  $"grounded={Motor.Grounded} vy={Motor.Velocity.Y:0.000}");
            }
        }

        /// <summary>
        /// Les trois premieres etapes de chaque pas, et leur ordre n'est pas
        /// negociable : les portes avancent, leur boite entre dans le monde de
        /// collision, PUIS on porte le joueur. Il se deplace ensuite lui-meme.
        /// </summary>
        public void PrepareStep(float dt, Action<EntityEvent> report)
        {
            // ⚠️ LA LOGIQUE AVANCE ICI, PAS DANS UpdateEntities, et pour la meme
            // raison que les portes : ce pas-ci tourne aussi console ouverte et
            // derriere le menu. Une minuterie qui s'arreterait des qu'on tape
            // « ent_send relais Declencher » serait inverifiable en headless.
            // Et AVANT les portes : une sortie livree a une porte la fait
            // bouger dans ce meme pas.
            Logic.Step(dt, Entities);
            Report(Logic.Events, report);

            StepMovers(dt, report);

            PreviousFeet = Motor.Position;
            RebuildWalkable();
            CarryPlayer();
        }

        /// <summary>
        /// Fait vivre les entites : volumes, checkpoints, evenements, PNJ.
        ///
        /// Au PAS FIXE. Au rendu, un declencheur serait teste trois mille fois
        /// par seconde au lieu de soixante-quatre, pour un resultat identique -
        /// on ne traverse pas un volume de trois metres en un millieme de
        /// seconde.
        /// </summary>
        public void UpdateEntities(float dt, Action<EntityEvent> report)
        {
            Entities.Update(Motor.GetBox());
            Report(Entities.Events, report);

            // Les PNJ voient depuis les YEUX du joueur, pas ses pieds : c'est ce
            // qui rend une ligne de vue coherente de part et d'autre.
            //
            // ⚠️ Entities est passe pour que leurs sorties soient LIVREES. Jusqu'au
            // 08/10 elles n'etaient que rapportees : « OnSpotPlayer -> porte.Ouvrir »
            // s'affichait, et la porte ne bougeait pas.
            //
            // ⚠️ Et Walkable - decor, caisses, portes, reconstruit ce pas-ci par
            // PrepareStep - pour qu'un garde bute sur une porte fermee et ne voie
            // pas au travers. Il ne connaissait que le decor.
            Agents.Update(dt, EyePosition, Entities, Walkable);
            Report(Agents.Events, report);
        }

        private static void Report(IReadOnlyList<EntityEvent> events, Action<EntityEvent> report)
        {
            for (int i = 0; i < events.Count; i++)
                report(events[i]);
        }
    }
}
