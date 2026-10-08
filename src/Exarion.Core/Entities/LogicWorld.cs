// ===========================================================================
//  Exarion - Entites LOGIQUES : relais, minuterie, compteur
//
//  Jusqu'ici une seule entite AGISSAIT : func_door. Le cablage sorties ->
//  entrees marchait de bout en bout, mais il n'y avait rien a poser ENTRE un
//  declencheur et une porte : pas de « attendre deux secondes », pas de « au
//  troisieme passage », pas de « toutes les quatre secondes ». C'est ce que
//  Source appelle logic_relay, logic_timer et math_counter, et c'est ce qui
//  fait d'un cablage un petit programme - sans langage de script, donc
//  toujours de la DONNEE : ca se lit dans le fichier et ca se teste ici.
//
//  ---------------------------------------------------------------------
//  ⚠️⚠️ UNE BOUCLE NE DOIT PAS POUVOIR GELER LE JEU.
//
//  Deux relais qui se visent l'un l'autre sans delai, livres dans le meme
//  pas, s'appelleraient a l'infini : le jeu se figerait sans un message.
//  Toute sortie logique passe donc par une FILE, et une sortie demandee
//  pendant la livraison part au plus tot au pas SUIVANT. Une boucle devient
//  un battement a 64 Hz, visible dans le fil d'evenements, au lieu d'un gel.
//
//  Et la file est BORNEE (MaxQueued). Une sortie ne vise qu'une cible, donc
//  rien ne se multiplie de lui-meme ; mais une minuterie rapide qui nourrit
//  un relais lent remplirait la file sans fin. Au-dela, la sortie est
//  REFUSEE et COMPTEE (Dropped) - la meme regle que le reservoir de
//  particules : un debordement silencieux est ce qui fait qu'un systeme
//  « arrete de marcher » sans raison.
//  ---------------------------------------------------------------------
//
//  Le temps est celui du PAS FIXE, en nombre de pas : jamais une somme de
//  deltas, qui deriverait. Une minuterie de 4 s a 64 pas/s tombe tous les
//  256 pas, exactement, la millieme fois comme la premiere.
// ===========================================================================

using System;
using System.Collections.Generic;
using Exarion.Core.Assets;

namespace Exarion.Core.Entities
{
    public enum LogicKind
    {
        /// <summary>Relaie une entree vers sa cible, avec un delai eventuel.</summary>
        Relay,

        /// <summary>Emet a intervalle regulier.</summary>
        Timer,

        /// <summary>Compte, et emet en atteignant son maximum.</summary>
        Counter,
    }

    /// <summary>
    /// Une entite logique : elle RECOIT des entrees comme une porte, et EMET
    /// sa sortie - toujours par la file de son LogicWorld.
    /// </summary>
    public sealed class LogicNode : IEntityInput
    {
        // Entrees comprises. En francais et sans accent, comme celles des
        // portes : c'est la langue des RECEPTEURS du moteur.
        public const string InputTrigger = "Declencher";
        public const string InputEnable = "Activer";
        public const string InputDisable = "Desactiver";
        public const string InputToggle = "Basculer";
        public const string InputCancel = "Annuler";
        public const string InputStart = "Demarrer";
        public const string InputStop = "Arreter";
        public const string InputAdd = "Ajouter";
        public const string InputSubtract = "Retirer";
        public const string InputReset = "Remettre";

        /// <summary>Sortie du relais.</summary>
        public const string OnTriggered = "OnDeclenche";

        /// <summary>Sortie de la minuterie, a chaque echeance.</summary>
        public const string OnTimer = "OnMinuterie";

        /// <summary>Sortie du compteur, quand il ATTEINT son maximum.</summary>
        public const string OnHitMax = "OnMax";

        public const float DefaultInterval = 1f;
        public const int DefaultMax = 3;

        /// <summary>
        /// Plus courte minuterie acceptee. En dessous, elle voudrait tomber
        /// plusieurs fois par pas ; elle ne tombe de toute facon qu'une fois
        /// par pas (voir Tick), mais une valeur nulle ou negative ne doit pas
        /// passer pour « le plus vite possible » sans que ce soit ecrit.
        /// </summary>
        public const float MinimumInterval = 0.01f;

        private readonly LogicWorld _world;
        private double _nextFire;

        internal LogicNode(LogicWorld world, MapEntity entity, LogicKind kind)
        {
            _world = world;
            Entity = entity;
            Kind = kind;
            Name = entity.Get("name");

            Delay = MathF.Max(0f, entity.GetFloat("delay", 0f));
            Once = entity.GetInt("once", 0) != 0;
            Interval = MathF.Max(MinimumInterval, entity.GetFloat("interval", DefaultInterval));
            Max = Math.Max(1, entity.GetInt("max", DefaultMax));
            Start = Math.Clamp(entity.GetInt("start", 0), 0, Max);
            Value = Start;

            // Un compteur n'a pas d'etat actif/inactif : il compte toujours.
            Enabled = kind == LogicKind.Counter || entity.GetInt("start_disabled", 0) == 0;

            if (kind == LogicKind.Timer && Enabled)
                _nextFire = _world.Now + Interval;
        }

        public MapEntity Entity { get; }
        public LogicKind Kind { get; }
        public string Name { get; }

        /// <summary>Relais : secondes entre l'entree et la sortie. Zero = au pas suivant.</summary>
        public float Delay { get; }

        /// <summary>Relais : se desactive apres avoir declenche une fois.</summary>
        public bool Once { get; }

        /// <summary>Minuterie : secondes entre deux sorties.</summary>
        public float Interval { get; }

        /// <summary>Compteur : valeur a laquelle il emet OnMax.</summary>
        public int Max { get; }

        /// <summary>Compteur : valeur de depart, et de remise a zero.</summary>
        public int Start { get; }

        /// <summary>Compteur : valeur courante, toujours dans [0, Max].</summary>
        public int Value { get; private set; }

        /// <summary>Relais : accepte de declencher. Minuterie : tourne.</summary>
        public bool Enabled { get; private set; }

        /// <summary>Minuterie : instant (en secondes de simulation) de la prochaine sortie.</summary>
        public double NextFire => _nextFire;

        public bool Receive(string input)
        {
            switch (Kind)
            {
                case LogicKind.Relay: return ReceiveRelay(input);
                case LogicKind.Timer: return ReceiveTimer(input);
                default: return ReceiveCounter(input);
            }
        }

        private static bool Is(string input, string name)
            => string.Equals(input, name, StringComparison.OrdinalIgnoreCase);

        private bool ReceiveRelay(string input)
        {
            if (Is(input, InputTrigger))
            {
                // Un relais desactive ENTEND l'entree - elle est connue, donc
                // livree - mais ne la relaie pas. Repondre « entree inconnue »
                // ferait chercher une faute de frappe qui n'existe pas.
                if (!Enabled)
                    return true;

                _world.Schedule(this, OnTriggered, Delay);

                if (Once)
                    Enabled = false;

                return true;
            }

            if (Is(input, InputEnable)) { Enabled = true; return true; }
            if (Is(input, InputDisable)) { Enabled = false; return true; }
            if (Is(input, InputToggle)) { Enabled = !Enabled; return true; }

            if (Is(input, InputCancel))
            {
                _world.CancelPending(this);
                return true;
            }

            return false;
        }

        private bool ReceiveTimer(string input)
        {
            if (Is(input, InputStart))
            {
                Begin();
                return true;
            }

            if (Is(input, InputStop)) { Enabled = false; return true; }

            if (Is(input, InputToggle))
            {
                if (Enabled) Enabled = false;
                else Begin();

                return true;
            }

            // Une sortie tout de suite, sans toucher a la cadence.
            if (Is(input, InputTrigger))
            {
                _world.Schedule(this, OnTimer, 0f);
                return true;
            }

            return false;
        }

        /// <summary>
        /// ⚠️ Redemarrer une minuterie qui tourne deja ne la remet PAS a zero.
        /// Sinon un declencheur qui renvoie « Demarrer » a chaque passage
        /// repousserait l'echeance indefiniment, et la minuterie ne tomberait
        /// jamais tant qu'on reste dans le couloir.
        /// </summary>
        private void Begin()
        {
            if (Enabled)
                return;

            Enabled = true;
            _nextFire = _world.Now + Interval;
        }

        private bool ReceiveCounter(string input)
        {
            if (Is(input, InputAdd))
            {
                // On n'emet qu'en ATTEIGNANT le maximum, pas a chaque ajout
                // au-dela : sinon trois passages de trop rouvriraient trois
                // fois une porte deja ouverte.
                if (Value < Max)
                {
                    Value++;

                    if (Value == Max)
                        _world.Schedule(this, OnHitMax, 0f);
                }

                return true;
            }

            if (Is(input, InputSubtract))
            {
                if (Value > 0)
                    Value--;

                return true;
            }

            if (Is(input, InputReset))
            {
                Value = Start;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Minuterie : emet si l'echeance est atteinte.
        ///
        /// ⚠️ UNE SORTIE PAR PAS AU PLUS, et l'echeance suivante se calcule
        /// depuis la PRECEDENTE (pas depuis maintenant) : pas de derive. Si la
        /// minuterie est plus rapide que le pas, on saute les echeances
        /// manquees au lieu de les rattraper toutes d'un coup - rattraper
        /// noierait la file en un seul pas.
        /// </summary>
        internal void Tick(double now)
        {
            if (Kind != LogicKind.Timer || !Enabled || now + LogicWorld.Epsilon < _nextFire)
                return;

            _world.ScheduleNow(this, OnTimer);

            _nextFire += Interval;

            if (_nextFire <= now + LogicWorld.Epsilon)
                _nextFire = now + Interval;
        }
    }

    public sealed class LogicWorld
    {
        /// <summary>Taille de la file des sorties en attente. Au-dela, refus compte.</summary>
        public const int MaxQueued = 256;

        /// <summary>Entree envoyee par une sortie logique quand la carte n'en declare pas.</summary>
        public const string DefaultOutputInput = "Trigger";

        internal const double Epsilon = 1e-9;

        private readonly struct Pending
        {
            public readonly double Due;
            public readonly LogicNode Source;
            public readonly string Output;
            public readonly string Target;
            public readonly string Input;

            public Pending(double due, LogicNode source, string output, string target, string input)
            {
                Due = due;
                Source = source;
                Output = output;
                Target = target;
                Input = input;
            }
        }

        private readonly List<LogicNode> _nodes = new();
        private readonly List<Pending> _queue = new();
        private readonly List<Pending> _due = new();
        private readonly List<EntityEvent> _events = new();

        // Pas fixe courant. Connu a chaque Step ; 1/64 avant le premier, pour
        // qu'une entree recue des le chargement (ent_send au demarrage) parte
        // quand meme au pas suivant et pas « maintenant ».
        private double _dt = 1.0 / 64.0;

        public IReadOnlyList<LogicNode> Nodes => _nodes;

        /// <summary>Sorties livrees au dernier pas. Vide la plupart du temps.</summary>
        public IReadOnlyList<EntityEvent> Events => _events;

        /// <summary>Sorties en attente dans la file.</summary>
        public int PendingCount => _queue.Count;

        /// <summary>Sorties REFUSEES faute de place. Affiche par « logic ».</summary>
        public int Dropped { get; private set; }

        public int TotalEvents { get; private set; }

        /// <summary>Pas executes depuis le chargement.</summary>
        public long Steps { get; private set; }

        /// <summary>Temps de simulation, en secondes : un nombre de pas, jamais une somme de deltas.</summary>
        public double Now => Steps * _dt;

        public static bool IsLogic(string type)
            => string.Equals(type, EntityTypes.LogicRelay, StringComparison.Ordinal)
            || string.Equals(type, EntityTypes.LogicTimer, StringComparison.Ordinal)
            || string.Equals(type, EntityTypes.LogicCounter, StringComparison.Ordinal);

        /// <summary>
        /// Reconstruit depuis une carte. Tout etat precedent est perdu - file
        /// comprise : une sortie promise par la carte precedente ne doit pas
        /// tomber sur une entite de la suivante qui porterait le meme nom.
        /// </summary>
        public void Load(MapData map)
        {
            ArgumentNullException.ThrowIfNull(map);

            _nodes.Clear();
            _queue.Clear();
            _events.Clear();
            Dropped = 0;
            TotalEvents = 0;
            Steps = 0;

            foreach (MapEntity entity in map.Entities)
            {
                LogicKind? kind = entity.Type switch
                {
                    EntityTypes.LogicRelay => LogicKind.Relay,
                    EntityTypes.LogicTimer => LogicKind.Timer,
                    EntityTypes.LogicCounter => LogicKind.Counter,
                    _ => null,
                };

                if (kind is LogicKind k)
                    _nodes.Add(new LogicNode(this, entity, k));
            }
        }

        /// <summary>
        /// Inscrit chaque entite logique comme cible. A appeler APRES
        /// EntityWorld.Load, qui efface les cibles inscrites - meme regle que
        /// les portes.
        /// </summary>
        public void RegisterInto(EntityWorld world)
        {
            ArgumentNullException.ThrowIfNull(world);

            foreach (LogicNode node in _nodes)
                world.Register(node);
        }

        public LogicNode? Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            foreach (LogicNode node in _nodes)
                if (string.Equals(node.Name, name, StringComparison.Ordinal))
                    return node;

            return null;
        }

        /// <summary>
        /// Avance d'un pas fixe : les minuteries tombent, puis les sorties
        /// arrivees a echeance sont livrees, dans l'ordre ou elles ont ete
        /// demandees.
        /// </summary>
        public void Step(float dt, EntityWorld router)
        {
            ArgumentNullException.ThrowIfNull(router);

            _events.Clear();

            if (dt <= 0f)
                return;

            _dt = dt;
            Steps++;

            double now = Now;

            foreach (LogicNode node in _nodes)
                node.Tick(now);

            // On SORT de la file tout ce qui est du avant de livrer quoi que
            // ce soit : une livraison peut en demander d'autres, qui partiront
            // au plus tot au pas suivant (Schedule ajoute toujours un pas).
            _due.Clear();

            for (int i = 0; i < _queue.Count;)
            {
                if (_queue[i].Due <= now + Epsilon)
                {
                    _due.Add(_queue[i]);
                    _queue.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }

            foreach (Pending p in _due)
            {
                EntityDelivery delivery = router.Send(p.Target, p.Input);
                _events.Add(new EntityEvent(p.Source.Entity, p.Output, p.Target, p.Input, delivery));
                TotalEvents++;
            }
        }

        /// <summary>
        /// Demande une sortie, au plus tot au pas SUIVANT - c'est la regle qui
        /// rend les boucles inoffensives (voir l'en-tete).
        ///
        /// ⚠️ HONNETETE : CETTE REGLE EST UNE DEUXIEME CEINTURE. Step sort de
        /// la file tout ce qui est du AVANT de livrer, si bien qu'une sortie
        /// demandee pendant la livraison ne peut pas partir dans le meme pas,
        /// meme a delai nul. Verifie par mutation : sans le « au moins un
        /// pas » ici, les 13 tests passent, boucle comprise. On garde les
        /// deux : retirer l'une ne gele rien, retirer les deux gelerait le jeu
        /// - et ce test-la ne s'ecrit pas, il ne rendrait jamais la main.
        /// </summary>
        internal void Schedule(LogicNode source, string output, float delaySeconds)
            => Enqueue(source, output, Now + Math.Max(delaySeconds, _dt));

        /// <summary>
        /// Sortie d'une minuterie : livree dans CE pas. Elle est demandee
        /// pendant Step, avant la livraison, et par le moteur lui-meme - pas
        /// par une livraison - donc elle ne peut pas boucler.
        /// </summary>
        internal void ScheduleNow(LogicNode source, string output) => Enqueue(source, output, Now);

        private void Enqueue(LogicNode source, string output, double due)
        {
            if (_queue.Count >= MaxQueued)
            {
                Dropped++;
                return;
            }

            string target = source.Entity.Get("target");
            string input = source.Entity.Get("input", DefaultOutputInput);

            _queue.Add(new Pending(due, source, output, target, input));
        }

        /// <summary>Relais : « Annuler » retire ses sorties encore en attente.</summary>
        internal void CancelPending(LogicNode source)
            => _queue.RemoveAll(p => ReferenceEquals(p.Source, source));
    }
}
