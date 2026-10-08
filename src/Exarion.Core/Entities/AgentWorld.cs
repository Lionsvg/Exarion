// ===========================================================================
//  Exarion - PNJ : perception, deplacement, machine a etats (E14)
//
//  ---------------------------------------------------------------------
//  L'INTENTION : QUE L'AUTEUR DU NIVEAU N'ECRIVE PAS UNE LIGNE DE CODE.
//
//  Tout se declare en PROPRIETES sur l'entite, dans l'editeur :
//
//      npc_dummy   behaviour=patrol  path=ronde_nord  speed=2.2
//                  sight=22  fov=110  name=garde_1
//
//  et tout se cable en SORTIES, comme n'importe quel declencheur :
//
//      OnSpotPlayer  -> alarme,     Declencher
//      OnLosePlayer  -> alarme,     Arreter
//      OnReachGoal   -> ascenseur,  Appeler
//
//  Un langage de script serait plus expressif. Il serait aussi une chose de
//  plus a apprendre, a deboguer et a faire planter. Ce qui se declare se
//  lit dans le fichier, se compare, et ne peut pas partir en boucle infinie.
//  ---------------------------------------------------------------------
//
//  RIEN N'EST REECRIT ICI DE CE QUI EXISTE DEJA. Un PNJ est un
//  CharacterMotor - le meme que le joueur - plus un cerveau qui lui fabrique
//  des MoveInput. Il herite donc gratuitement du deplacement facon Quake, de
//  la gravite, du franchissement de marches et de la collision, tous testes
//  depuis des semaines. Ecrire un deuxieme deplacement aurait donne deux
//  comportements a maintenir et a faire diverger.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Navigation;
using Exarion.Core.Physics;

namespace Exarion.Core.Entities
{
    public enum AgentState
    {
        /// <summary>Immobile a son poste.</summary>
        Idle,

        /// <summary>Suit son chemin de points de passage.</summary>
        Patrol,

        /// <summary>A vu le joueur et le suit.</summary>
        Chase,

        /// <summary>Rentre a son poste apres avoir perdu le joueur.</summary>
        Return,
    }

    public sealed class Agent
    {
        internal Agent(MapEntity definition, MotorSettings settings)
        {
            Definition = definition;
            Motor = new CharacterMotor(settings) { Position = definition.Position };
            Home = definition.Position;

            // L'orientation de depart est celle posee dans l'editeur : un garde
            // doit regarder ou l'auteur l'a tourne, pas vers -Z par defaut.
            Yaw = definition.Yaw * MathF.PI / 180f;
        }

        public MapEntity Definition { get; }
        public CharacterMotor Motor { get; }

        /// <summary>Poste d'origine. On y revient apres une poursuite.</summary>
        public Vector3 Home { get; }

        public AgentState State { get; internal set; } = AgentState.Idle;

        /// <summary>Orientation, en radians. Meme convention que la camera.</summary>
        public float Yaw { get; internal set; }

        public int Waypoint { get; internal set; }

        public bool SeesPlayer { get; internal set; }

        /// <summary>Temps ecoule depuis la derniere vue du joueur.</summary>
        public float TimeSinceSeen { get; internal set; } = float.MaxValue;

        public Vector3 Position => Motor.Position;

        public float Speed => Motor.HorizontalSpeed;

        /// <summary>Hauteur des yeux. Sert a la ligne de vue.</summary>
        public Vector3 Eye => Motor.Position + new Vector3(0f, Motor.Height * 0.9f, 0f);

        public string Name => Definition.Get("name");

        // --- Chemin (E16) ---------------------------------------------------

        internal readonly List<Vector3> PathPoints = new();
        internal int PathIndex;
        internal float ReplanIn;

        /// <summary>Le chemin suivi, en positions de pieds. Vide quand il marche droit.</summary>
        public IReadOnlyList<Vector3> Path => PathPoints;

        /// <summary>Indice du point du chemin vise en ce moment.</summary>
        public int PathCursor => PathIndex;

        /// <summary>Comment il se dirige en ce moment. Affiche par « nav ».</summary>
        public AgentNavMode NavMode { get; internal set; }

        /// <summary>
        /// Vrai quand sa cible est HORS D'ATTEINTE : il s'en approche au plus
        /// pres, puis l'attend tourne vers elle - au lieu de pousser contre le
        /// mur qui l'en separe.
        /// </summary>
        public bool NoPath => NavMode == AgentNavMode.Approach;
    }

    /// <summary>Comment un PNJ se dirige vers sa cible.</summary>
    public enum AgentNavMode
    {
        /// <summary>En ligne droite : rien ne barre la route (ou pas de grille).</summary>
        Direct,

        /// <summary>Le long d'un chemin calcule, qui mene a la cible.</summary>
        Path,

        /// <summary>Cible hors d'atteinte : chemin vers le point le plus proche, puis attente.</summary>
        Approach,

        /// <summary>
        /// Le PNJ n'est sur aucun sol de la grille (pousse dans un recoin,
        /// tombe hors du decor) : il retombe sur la ligne droite d'avant E16,
        /// qui a au moins une chance de l'en sortir.
        /// </summary>
        OffGrid,
    }

    public sealed class AgentWorld
    {
        public const string OnSpotPlayer = "OnSpotPlayer";
        public const string OnLosePlayer = "OnLosePlayer";
        public const string OnReachGoal = "OnReachGoal";

        // Entrees : un PNJ NOMME est une cible, comme une porte. C'est ce qui
        // permet de « reveiller » un garde quand le joueur entre dans une
        // salle, sans une ligne de code. En francais, comme les autres
        // recepteurs du moteur.
        public const string InputPatrol = "Patrouiller";
        public const string InputStop = "Arreter";
        public const string InputReturn = "Rentrer";

        /// <summary>
        /// Rayon d'arrivee sur un point de passage.
        ///
        /// ⚠️ IL DOIT DEPASSER LA DISTANCE PARCOURUE EN UN PAS. A 4,76 m/s et
        /// 64 pas par seconde, c'est 7,4 cm ; un rayon plus petit ferait
        /// depasser le point, puis revenir, puis redepasser - le PNJ vibrerait
        /// sur place sans jamais « arriver ».
        /// </summary>
        public const float ArriveRadius = 0.6f;

        /// <summary>
        /// Delai avant d'abandonner la poursuite.
        ///
        /// ⚠️ CE N'EST PAS DU CONFORT, C'EST UNE HYSTERESIS. A la limite de
        /// portee, la vue s'accroche et se perd d'une image a l'autre : sans
        /// delai, le PNJ basculerait entre Chase et Return et emettrait
        /// OnSpotPlayer/OnLosePlayer soixante-quatre fois par seconde. Meme
        /// classe de defaut que la memoire des volumes de declenchement.
        /// </summary>
        public const float LoseSeconds = 2.5f;

        /// <summary>
        /// Delai entre deux calculs de chemin d'un meme PNJ.
        ///
        /// Assez court pour suivre un joueur qui court (2,4 m a 4,76 m/s), assez
        /// long pour qu'une poursuite ne relance pas un A* a CHAQUE pas : le
        /// joueur change de case de grille vingt fois par seconde.
        /// </summary>
        public const float ReplanSeconds = 0.5f;

        /// <summary>
        /// Distance a laquelle un point du chemin est considere atteint. Plus
        /// grande que le pas parcouru en un pas fixe (7 cm), meme raison que
        /// ArriveRadius ; plus petite qu'une case et demie, sinon le PNJ couperait
        /// les virages au ras des murs.
        /// </summary>
        public const float NodeReachRadius = 0.35f;

        /// <summary>
        /// Ecart de hauteur au-dela duquel une cible n'est PAS atteinte, meme a
        /// la verticale.
        ///
        /// ⚠️ SANS LUI, UNE PASSERELLE EST UN PIEGE. L'arrivee ne regardait que
        /// la distance au sol : un point de ronde pose sur une passerelle etait
        /// « atteint » des que le garde passait DESSOUS, et un joueur debout
        /// au-dessus de lui etait « rejoint ». 1,5 m laisse passer un joueur qui
        /// saute ou se tient sur une caisse, pas un etage.
        ///
        /// Seulement quand le calcul de chemin est actif : sans lui, le PNJ ne
        /// peut de toute facon pas monter, et nav_enabled 0 doit rester le
        /// comportement d'avant E16 a l'identique.
        /// </summary>
        public const float ArriveHeight = 1.5f;

        /// <summary>Hauteur des yeux du joueur au-dessus de ses pieds (1,83 - 0,18).</summary>
        private const float PlayerEyeAboveFeet = 1.65f;

        private readonly List<Agent> _agents = new();
        private readonly List<EntityEvent> _events = new();

        /// <summary>Chemins : nom -> points de passage, dans l'ordre.</summary>
        private readonly Dictionary<string, List<MapEntity>> _paths = new(StringComparer.Ordinal);

        private CollisionWorld? _world;

        public IReadOnlyList<Agent> Agents => _agents;

        /// <summary>Evenements de la derniere mise a jour. Vide la plupart du temps.</summary>
        public IReadOnlyList<EntityEvent> Events => _events;

        public int TotalEvents { get; private set; }

        /// <summary>La grille de navigation de la carte, ou null s'il n'y a ni PNJ ni decor.</summary>
        public NavGrid? Navigation { get; private set; }

        /// <summary>Temps de construction de la grille, en millisecondes. Affiche par « nav ».</summary>
        public double NavigationBuildMilliseconds { get; private set; }

        /// <summary>
        /// « nav_enabled » : faux, les PNJ marchent en ligne droite comme avant
        /// E16. Existe pour COMPARER - c'est le seul moyen de voir, sur la meme
        /// carte, ce que le calcul de chemin change.
        /// </summary>
        public bool NavigationEnabled { get; set; } = true;

        /// <summary>
        /// Reconstruit les PNJ depuis la carte.
        ///
        /// ⚠️ On repart de zero a chaque chargement. Garder les PNJ ferait
        /// qu'un garde continue sa ronde sur un chemin qui n'existe plus, en
        /// traversant un decor qui a change.
        /// </summary>
        public void Load(MapData map, CollisionWorld? world)
        {
            ArgumentNullException.ThrowIfNull(map);

            _world = world;
            _agents.Clear();
            _events.Clear();
            _paths.Clear();

            BuildPaths(map);

            foreach (MapEntity entity in map.Entities)
            {
                if (!string.Equals(entity.Type, EntityTypes.Npc, StringComparison.Ordinal))
                    continue;

                // Chaque PNJ a SES reglages de deplacement. Un garde lent et un
                // chien rapide partagent le meme code et rien d'autre.
                var settings = new MotorSettings();
                settings.RunSpeed = MathF.Max(0.2f, entity.GetFloat("speed", 2.2f));
                settings.WalkSpeed = settings.RunSpeed;

                var agent = new Agent(entity, settings);

                agent.State = Behaviour(entity) == "patrol" && PathOf(entity).Count > 0
                    ? AgentState.Patrol
                    : AgentState.Idle;

                _agents.Add(agent);
            }

            // La grille decrit le corps d'un PNJ : MEMES dimensions que son
            // CharacterMotor (seules les vitesses changent d'un PNJ a l'autre).
            // Construite seulement s'il y a quelqu'un pour s'en servir.
            Navigation = null;
            NavigationBuildMilliseconds = 0;

            if (_agents.Count > 0 && world != null)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                Navigation = NavGrid.Build(world, NavAgentShape.From(new MotorSettings()));
                NavigationBuildMilliseconds = clock.Elapsed.TotalMilliseconds;
            }
        }

        /// <summary>
        /// Inscrit chaque PNJ nomme comme cible d'entrees. A appeler APRES
        /// EntityWorld.Load, qui efface les cibles inscrites - meme regle que
        /// les portes et la logique.
        /// </summary>
        public void RegisterInto(EntityWorld world)
        {
            ArgumentNullException.ThrowIfNull(world);

            foreach (Agent agent in _agents)
                world.Register(new AgentInput(this, agent));
        }

        /// <summary>
        /// Le PNJ vu comme un recepteur. Un adaptateur plutot qu'Agent lui-meme :
        /// une entree change l'ETAT, et les transitions d'etat appartiennent au
        /// monde des PNJ (remise a zero du chemin, rondes connues de lui seul).
        /// </summary>
        private sealed class AgentInput : IEntityInput
        {
            private readonly AgentWorld _world;
            private readonly Agent _agent;

            public AgentInput(AgentWorld world, Agent agent)
            {
                _world = world;
                _agent = agent;
            }

            public string Name => _agent.Name;

            public bool Receive(string input) => _world.Command(_agent, input);
        }

        private bool Command(Agent agent, string input)
        {
            if (string.Equals(input, InputPatrol, StringComparison.OrdinalIgnoreCase))
            {
                // ⚠️ Sans ronde, il RESTE au repos. Le passer en Patrol sans
                // point a viser le laisserait immobile dans un etat que le HUD
                // afficherait comme une ronde en cours.
                if (PathOf(agent.Definition).Count > 0)
                {
                    agent.State = AgentState.Patrol;
                    ResetNavigation(agent);
                }

                return true;
            }

            if (string.Equals(input, InputStop, StringComparison.OrdinalIgnoreCase))
            {
                agent.State = AgentState.Idle;
                ResetNavigation(agent);
                return true;
            }

            if (string.Equals(input, InputReturn, StringComparison.OrdinalIgnoreCase))
            {
                agent.State = AgentState.Return;
                ResetNavigation(agent);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Regroupe les points de passage par chemin.
        ///
        /// L'ordre vient de la propriete « order » si elle existe, sinon de
        /// l'ordre du fichier. Sans tri STABLE, une ronde changerait de sens
        /// d'un enregistrement a l'autre.
        /// </summary>
        private void BuildPaths(MapData map)
        {
            for (int i = 0; i < map.Entities.Count; i++)
            {
                MapEntity entity = map.Entities[i];

                if (!string.Equals(entity.Type, EntityTypes.Waypoint, StringComparison.Ordinal))
                    continue;

                string path = entity.Get("path", "defaut");

                if (!_paths.TryGetValue(path, out List<MapEntity>? list))
                {
                    list = new List<MapEntity>();
                    _paths[path] = list;
                }

                list.Add(entity);
            }

            foreach (List<MapEntity> list in _paths.Values)
                list.Sort((a, b) => a.GetInt("order", 0).CompareTo(b.GetInt("order", 0)));
        }

        private static string Behaviour(MapEntity entity)
            => entity.Get("behaviour", "patrol").ToLowerInvariant();

        private List<MapEntity> PathOf(MapEntity entity)
            => _paths.TryGetValue(entity.Get("path", "defaut"), out List<MapEntity>? list)
                ? list : EmptyPath;

        private static readonly List<MapEntity> EmptyPath = new();

        /// <summary>
        /// Fait avancer tous les PNJ d'un pas.
        /// </summary>
        /// <param name="playerEye">Position des yeux du joueur, pas de ses pieds.</param>
        /// <param name="router">
        /// Le monde des entites, qui LIVRE les sorties a leur cible. Sans lui,
        /// les evenements sont emis mais n'atteignent personne (NonCablee) -
        /// ce qui etait le cas en jeu jusqu'au 08/10, sans que rien ne le dise.
        /// </param>
        /// <param name="solid">
        /// Ce qui est SOLIDE a ce pas : decor, caisses, portes - le monde que
        /// le jeu reconstruit pour le joueur. Sert au deplacement ET a la ligne
        /// de vue. Null : le decor seul.
        ///
        /// ⚠️ Jusqu'au 08/10, les PNJ ne connaissaient que le DECOR : un garde
        /// traversait une porte fermee, et voyait le joueur au travers. Le
        /// calcul de chemin, lui, reste sur le decor : une porte s'ouvre et se
        /// ferme, la grille ne se reconstruit pas a chaque fois.
        /// </param>
        public void Update(float dt, Vector3 playerEye, EntityWorld? router = null, CollisionWorld? solid = null)
        {
            _events.Clear();

            if (dt <= 0f)
                return;

            _router = router;
            _solid = solid;

            for (int i = 0; i < _agents.Count; i++)
                Step(_agents[i], dt, playerEye);

            _router = null;
            _solid = null;
        }

        private EntityWorld? _router;
        private CollisionWorld? _solid;

        /// <summary>Le monde contre lequel on bouge et on regarde : le solide du pas, a defaut le decor.</summary>
        private CollisionWorld? Solid => _solid ?? _world;

        private void Step(Agent agent, float dt, Vector3 playerEye)
        {
            bool wasSeeing = agent.SeesPlayer;

            agent.SeesPlayer = CanSee(agent, playerEye);

            if (agent.SeesPlayer)
                agent.TimeSinceSeen = 0f;
            else if (agent.TimeSinceSeen < float.MaxValue)
                agent.TimeSinceSeen += dt;

            if (agent.SeesPlayer && !wasSeeing && agent.State != AgentState.Chase)
            {
                agent.State = AgentState.Chase;
                ResetNavigation(agent);
                Emit(agent, OnSpotPlayer);
            }
            else if (agent.State == AgentState.Chase && agent.TimeSinceSeen > LoseSeconds)
            {
                // On repart en ronde si on en a une, sinon on rentre au poste.
                agent.State = PathOf(agent.Definition).Count > 0
                    ? AgentState.Patrol
                    : AgentState.Return;

                ResetNavigation(agent);
                Emit(agent, OnLosePlayer);
            }

            Vector3? target = TargetOf(agent, playerEye);

            var input = new MoveInput();

            if (target is Vector3 goal)
            {
                Vector3 delta = goal - agent.Position;
                delta.Y = 0f;

                float distance = delta.Length();
                Vector3 navGoal = NavigationGoal(agent, goal, playerEye);

                bool reached = distance <= ArriveRadius
                               && (!NavigationActive || MathF.Abs(navGoal.Y - agent.Position.Y) <= ArriveHeight);

                if (!reached)
                {
                    // E16 : OU aller ce pas-ci. La cible elle-meme si on peut y
                    // marcher droit ; sinon le prochain point du chemin ; null
                    // si elle est hors d'atteinte et qu'on est au plus pres.
                    Vector3? steer = SteeringPoint(agent, navGoal, dt);

                    Vector3 toward = (steer ?? goal) - agent.Position;
                    toward.Y = 0f;
                    float length = toward.Length();

                    if (length > 1e-4f)
                    {
                        // Convention de lacet du projet : 0 regarde vers -Z, et il
                        // croit vers la gauche. La direction avant est donc
                        // (-sin, -cos), d'ou ce atan2 - et pas l'inverse.
                        agent.Yaw = MathF.Atan2(-toward.X / length, -toward.Z / length);
                    }

                    // Hors d'atteinte : il se tourne vers sa cible et l'attend.
                    // Avant E16 il marchait droit dessus et poussait contre le
                    // mur - a 0,0 m/s, contre une estrade de 1,80 m.
                    if (steer.HasValue)
                        input.Move = new Vector2(0f, 1f);
                }
                else
                {
                    Arrived(agent);
                }
            }

            input.Yaw = agent.Yaw;

            // Le PNJ se deplace avec le MEME moteur que le joueur : gravite,
            // franchissement de marches, glissement le long des murs.
            agent.Motor.Step(input, dt, Solid ?? EmptyWorld);
        }

        private static readonly CollisionWorld EmptyWorld = new();

        /// <summary>
        /// Ou le chemin doit mener. En poursuite, aux PIEDS reels du joueur -
        /// la cible de deplacement, elle, est ramenee a la hauteur du PNJ pour
        /// la ligne droite, ce qui designerait le sol SOUS une estrade ou se
        /// tient le joueur, et non son dessus.
        /// </summary>
        private static Vector3 NavigationGoal(Agent agent, Vector3 goal, Vector3 playerEye)
            => agent.State == AgentState.Chase
                ? new Vector3(playerEye.X, playerEye.Y - PlayerEyeAboveFeet, playerEye.Z)
                : goal;

        private bool NavigationActive => NavigationEnabled && Navigation != null;

        /// <summary>
        /// La cible vient de changer (poursuite commencee ou abandonnee, point
        /// de ronde atteint) : le chemin vers l'ancienne ne vaut plus rien, et
        /// le suivant doit etre calcule TOUT DE SUITE - pas a la prochaine
        /// echeance, qui laisserait le garde suivre une demi-seconde la ronde
        /// qu'il vient d'abandonner pour poursuivre le joueur.
        /// </summary>
        private static void ResetNavigation(Agent agent)
        {
            agent.PathPoints.Clear();
            agent.NavMode = AgentNavMode.Direct;
            agent.ReplanIn = 0f;
        }

        /// <summary>
        /// Le point vers lequel avancer ce pas-ci, ou null s'il faut attendre :
        /// cible hors d'atteinte, et deja au plus pres.
        ///
        /// ⚠️ LA LIGNE DROITE D'ABORD. Si on peut marcher droit jusqu'a la
        /// cible, on le fait - c'est le comportement d'avant E16, a l'identique,
        /// et c'est aussi le chemin le plus court. Le calcul de chemin ne sert
        /// que quand quelque chose barre la route.
        /// </summary>
        private Vector3? SteeringPoint(Agent agent, Vector3 goal, float dt)
        {
            NavGrid? nav = NavigationEnabled ? Navigation : null;

            if (nav is null)
            {
                ResetNavigation(agent);
                return goal;
            }

            agent.ReplanIn -= dt;

            // On recalcule en quittant la ligne droite, puis toutes les
            // ReplanSeconds : la cible bouge (poursuite), et le PNJ a pu etre
            // pousse hors de son chemin.
            //
            // ⚠️ JAMAIS « FAUTE DE CHEMIN ». La premiere version recalculait
            // tant que la liste etait vide - or elle reste vide quand la cible
            // est inaccessible. Resultat : un A* complet a CHAQUE pas, 18 ms
            // mesures sur 60 x 60 m, soit plus d'une image entiere pour un
            // seul garde qui regarde un joueur monte sur une estrade.
            if (agent.NavMode == AgentNavMode.Direct || agent.ReplanIn <= 0f)
            {
                // ⚠️ La ligne droite n'est re-testee qu'ICI, pas a chaque pas
                // d'un chemin. Au ras d'un coin elle s'ouvre et se ferme d'un
                // pas a l'autre au gre du glissement contre le mur : chaque
                // fermeture relancerait un A*. Le chemin, deja lisse, ne perd
                // presque rien a etre suivi une demi-seconde de plus.
                if (nav.LineWalkable(agent.Position, goal))
                {
                    ResetNavigation(agent);
                    return goal;
                }

                agent.ReplanIn = ReplanSeconds;
                agent.PathIndex = 1;

                agent.NavMode = nav.FindPath(agent.Position, goal, agent.PathPoints) switch
                {
                    PathStatus.Complete => AgentNavMode.Path,
                    PathStatus.Partial => AgentNavMode.Approach,
                    _ => AgentNavMode.OffGrid,
                };
            }

            if (agent.NavMode == AgentNavMode.OffGrid)
                return goal;

            List<Vector3> points = agent.PathPoints;

            // On vise le premier point pas encore atteint. Le premier du chemin
            // est la case de depart : on le saute d'office (PathIndex = 1).
            while (agent.PathIndex < points.Count - 1
                   && FlatDistance(agent.Position, points[agent.PathIndex]) < NodeReachRadius)
                agent.PathIndex++;

            // Au bout du chemin. S'il menait a la cible, on finit droit dessus ;
            // s'il ne menait qu'au plus pres, on s'arrete la et on attend.
            // Continuer a viser le dernier point le ferait tourner autour, a
            // quelques centimetres - le meme piege que ArriveRadius.
            if (agent.PathIndex >= points.Count - 1 && FlatDistance(agent.Position, points[^1]) < NodeReachRadius)
                return agent.NavMode == AgentNavMode.Approach ? null : goal;

            return points[Math.Min(agent.PathIndex, points.Count - 1)];
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
            => new Vector2(a.X - b.X, a.Z - b.Z).Length();

        /// <summary>Ou le PNJ veut aller, ou null s'il n'a rien a faire.</summary>
        private Vector3? TargetOf(Agent agent, Vector3 playerEye)
        {
            switch (agent.State)
            {
                case AgentState.Chase:
                    // On vise les PIEDS et non les yeux : viser les yeux ferait
                    // marcher le PNJ vers un point en hauteur, donc en
                    // permanence contre le mur sous le joueur.
                    return new Vector3(playerEye.X, agent.Position.Y, playerEye.Z);

                case AgentState.Patrol:
                {
                    List<MapEntity> path = PathOf(agent.Definition);

                    if (path.Count == 0)
                        return null;

                    // Le modulo protege d'un chemin raccourci entre-temps par
                    // l'editeur : sans lui, l'indice sortirait de la liste.
                    return path[agent.Waypoint % path.Count].Position;
                }

                case AgentState.Return:
                    return agent.Home;

                default:
                    return null;
            }
        }

        private void Arrived(Agent agent)
        {
            // La cible va changer (point suivant, ou repos) : le chemin vers
            // l'ancienne ne sert plus. Sans ce vidage, le PNJ suivrait une demi-
            // seconde le chemin du point qu'il vient d'atteindre.
            ResetNavigation(agent);

            switch (agent.State)
            {
                case AgentState.Patrol:
                {
                    List<MapEntity> path = PathOf(agent.Definition);

                    if (path.Count == 0)
                        return;

                    agent.Waypoint = (agent.Waypoint + 1) % path.Count;
                    Emit(agent, OnReachGoal);
                    return;
                }

                case AgentState.Return:
                    // Arrive au poste : on se rendort. Sans ce passage a Idle,
                    // le PNJ resterait en Return et emettrait OnReachGoal a
                    // chaque pas - encore la meme classe de defaut.
                    agent.State = AgentState.Idle;
                    Emit(agent, OnReachGoal);
                    return;
            }
        }

        /// <summary>
        /// Le PNJ voit-il le joueur ?
        ///
        /// Trois conditions, et l'ordre compte pour le cout : la distance est
        /// gratuite, l'angle presque, le rayon est le seul vrai calcul.
        /// </summary>
        public bool CanSee(Agent agent, Vector3 playerEye)
        {
            float sight = agent.Definition.GetFloat("sight", 20f);

            if (sight <= 0f)
                return false;

            Vector3 eye = agent.Eye;
            Vector3 delta = playerEye - eye;
            float distance = delta.Length();

            if (distance > sight)
                return false;

            if (distance < 1e-4f)
                return true;

            Vector3 direction = delta / distance;

            float fov = agent.Definition.GetFloat("fov", 110f);

            if (fov < 360f)
            {
                // Champ de vision HORIZONTAL : un garde reconnait quelqu'un
                // devant lui, pas au-dessus. Comparer en 3D le rendrait aveugle
                // a un joueur pourtant bien en face, sur une marche.
                var facing = new Vector3(-MathF.Sin(agent.Yaw), 0f, -MathF.Cos(agent.Yaw));
                var flat = new Vector3(direction.X, 0f, direction.Z);

                if (flat.LengthSquared() > 1e-8f)
                {
                    flat = Vector3.Normalize(flat);

                    float cosHalf = MathF.Cos(MathF.Min(179.9f, fov * 0.5f) * MathF.PI / 180f);

                    if (Vector3.Dot(facing, flat) < cosHalf)
                        return false;
                }
            }

            // ⚠️ LA LIGNE DE VUE PART DES YEUX, DES DEUX COTES. Un rayon tire
            // depuis les pieds du PNJ vers les pieds du joueur est arrete par
            // la moindre marche et par tout muret : le PNJ serait aveugle dans
            // un decor normal, et on chercherait le defaut dans le champ de
            // vision.
            if (Solid is CollisionWorld solid && solid.Raycast(eye, direction, distance, out _, out _))
                return false;

            return true;
        }

        private void Emit(Agent agent, string output)
        {
            // Meme convention que les volumes : la cible et l'entree viennent
            // de proprietes, et une sortie non cablee emet quand meme.
            string target = agent.Definition.Get("target");
            string input = agent.Definition.Get(InputKeyFor(output), DefaultInputFor(output));

            EntityDelivery delivery = _router?.Send(target, input) ?? EntityDelivery.NonCablee;

            _events.Add(new EntityEvent(agent.Definition, output, target, input, delivery));
            TotalEvents++;
        }

        private static string InputKeyFor(string output) => output switch
        {
            OnSpotPlayer => "input_spot",
            OnLosePlayer => "input_lose",
            _ => "input_goal",
        };

        private static string DefaultInputFor(string output) => output switch
        {
            OnSpotPlayer => "Alert",
            OnLosePlayer => "Calm",
            _ => "Reached",
        };

        /// <summary>PNJ portant ce nom, ou null.</summary>
        public Agent? Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            foreach (Agent agent in _agents)
                if (string.Equals(agent.Name, name, StringComparison.Ordinal))
                    return agent;

            return null;
        }
    }
}
