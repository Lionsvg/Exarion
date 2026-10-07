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
    }

    public sealed class AgentWorld
    {
        public const string OnSpotPlayer = "OnSpotPlayer";
        public const string OnLosePlayer = "OnLosePlayer";
        public const string OnReachGoal = "OnReachGoal";

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

        private readonly List<Agent> _agents = new();
        private readonly List<EntityEvent> _events = new();

        /// <summary>Chemins : nom -> points de passage, dans l'ordre.</summary>
        private readonly Dictionary<string, List<MapEntity>> _paths = new(StringComparer.Ordinal);

        private CollisionWorld? _world;

        public IReadOnlyList<Agent> Agents => _agents;

        /// <summary>Evenements de la derniere mise a jour. Vide la plupart du temps.</summary>
        public IReadOnlyList<EntityEvent> Events => _events;

        public int TotalEvents { get; private set; }

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
        public void Update(float dt, Vector3 playerEye)
        {
            _events.Clear();

            if (dt <= 0f)
                return;

            for (int i = 0; i < _agents.Count; i++)
                Step(_agents[i], dt, playerEye);
        }

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
                Emit(agent, OnSpotPlayer);
            }
            else if (agent.State == AgentState.Chase && agent.TimeSinceSeen > LoseSeconds)
            {
                // On repart en ronde si on en a une, sinon on rentre au poste.
                agent.State = PathOf(agent.Definition).Count > 0
                    ? AgentState.Patrol
                    : AgentState.Return;

                Emit(agent, OnLosePlayer);
            }

            Vector3? target = TargetOf(agent, playerEye);

            var input = new MoveInput();

            if (target is Vector3 goal)
            {
                Vector3 delta = goal - agent.Position;
                delta.Y = 0f;

                float distance = delta.Length();

                if (distance > ArriveRadius)
                {
                    // Convention de lacet du projet : 0 regarde vers -Z, et il
                    // croit vers la gauche. La direction avant est donc
                    // (-sin, -cos), d'ou ce atan2 - et pas l'inverse.
                    agent.Yaw = MathF.Atan2(-delta.X / distance, -delta.Z / distance);

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
            agent.Motor.Step(input, dt, _world ?? EmptyWorld);
        }

        private static readonly CollisionWorld EmptyWorld = new();

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
            if (_world != null && _world.Raycast(eye, direction, distance, out _, out _))
                return false;

            return true;
        }

        private void Emit(Agent agent, string output)
        {
            // Meme convention que les volumes : la cible et l'entree viennent
            // de proprietes, et une sortie non cablee emet quand meme.
            string target = agent.Definition.Get("target");
            string input = agent.Definition.Get(InputKeyFor(output), DefaultInputFor(output));

            _events.Add(new EntityEvent(agent.Definition, output, target, input));
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
