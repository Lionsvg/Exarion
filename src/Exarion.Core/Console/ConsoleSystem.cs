// ===========================================================================
//  Exarion - Variables et console
//
//  Permet de regler le moteur SANS RECOMPILER : gravite, sensibilite,
//  friction, tickrate, vitesse de course...
//
//  C'est l'outil qui compte le plus pour le reglage du feeling. Le feeling
//  ne se calcule pas, il s'essaie - et l'essayer suppose de pouvoir changer
//  une valeur en une seconde, pas en trente.
//
//  Aucune dependance : la console vit dans Core et se teste sans fenetre.
//  La couche moteur ne fait que l'afficher et lui envoyer des touches.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Exarion.Core.Console
{
    /// <summary>Une variable reglable a chaud.</summary>
    public sealed class ConVar
    {
        public string Name { get; }
        public string Description { get; }
        public float Default { get; }
        public float Min { get; }
        public float Max { get; }

        private float _value;

        /// <summary>Notifie le moteur qu'il doit appliquer la nouvelle valeur.</summary>
        public event Action<ConVar>? Changed;

        public ConVar(string name, float defaultValue, float min, float max, string description = "")
        {
            Name = name.ToLowerInvariant();
            Description = description;
            Default = defaultValue;
            Min = min;
            Max = max;
            _value = Math.Clamp(defaultValue, min, max);
        }

        public float Value
        {
            get => _value;
            set
            {
                float clamped = Math.Clamp(value, Min, Max);
                if (Math.Abs(clamped - _value) < float.Epsilon)
                    return;

                _value = clamped;
                Changed?.Invoke(this);
            }
        }

        public bool BoolValue => _value >= 0.5f;
        public int IntValue => (int)MathF.Round(_value);

        public void Reset() => Value = Default;
    }

    /// <summary>Une action declenchable par son nom.</summary>
    public sealed class ConCommand
    {
        public string Name { get; }
        public string Description { get; }
        public Action<string[]> Action { get; }

        public ConCommand(string name, Action<string[]> action, string description = "")
        {
            Name = name.ToLowerInvariant();
            Action = action;
            Description = description;
        }
    }

    public sealed class ConsoleSystem
    {
        private readonly Dictionary<string, ConVar> _vars = new();
        private readonly Dictionary<string, ConCommand> _commands = new();
        private readonly List<string> _output = new();
        private readonly List<string> _history = new();

        /// <summary>Lignes affichees. Les plus recentes en fin de liste.</summary>
        public IReadOnlyList<string> Output => _output;

        /// <summary>Commandes deja tapees, pour la fleche du haut.</summary>
        public IReadOnlyList<string> History => _history;

        public IReadOnlyCollection<ConVar> Variables => _vars.Values;
        public IReadOnlyCollection<ConCommand> Commands => _commands.Values;

        public int MaxOutputLines { get; set; } = 200;

        /// <summary>
        /// Chaque ligne imprimee, au moment ou elle l'est.
        ///
        /// ⚠️ C'EST LA SEULE FACON FIABLE DE SUIVRE LA SORTIE. Output est
        /// BORNE : passe MaxOutputLines, les plus anciennes lignes partent, et
        /// son compte ne bouge plus. Le bac a sable recopiait la sortie des
        /// --exec en comparant ce compte avant et apres chaque commande - au
        /// bout de 200 lignes, plus rien ne sortait, sans erreur. Constate le
        /// 07/10 : un inventaire de 126 commandes s'arretait a la 66e.
        /// </summary>
        public event Action<string>? LinePrinted;

        public ConVar Register(ConVar variable)
        {
            _vars[variable.Name] = variable;
            return variable;
        }

        public ConVar Register(string name, float defaultValue, float min, float max, string description = "")
            => Register(new ConVar(name, defaultValue, min, max, description));

        public ConCommand Register(ConCommand command)
        {
            _commands[command.Name] = command;
            return command;
        }

        public ConCommand Register(string name, Action<string[]> action, string description = "")
            => Register(new ConCommand(name, action, description));

        public ConVar? Find(string name)
            => name != null && _vars.TryGetValue(name.ToLowerInvariant(), out var v) ? v : null;

        public void Print(string line)
        {
            _output.Add(line);

            if (_output.Count > MaxOutputLines)
                _output.RemoveRange(0, _output.Count - MaxOutputLines);

            LinePrinted?.Invoke(line);
        }

        public void Clear() => _output.Clear();

        // ------------------------------------------------------------------
        //  Commandes DIFFEREES : « delay 3 ent_send porte Ouvrir »
        //
        //  ⚠️ POURQUOI C'EST DANS LE MOTEUR. Tous les --exec partent a t = 0,
        //  avant que quoi que ce soit n'ait bouge : impossible, en headless,
        //  de tirer APRES une connexion, d'ouvrir une porte a mi-course, de
        //  basculer un reglage au milieu d'une mesure. Le seul recours etait
        //  d'ecrire une commande dediee par besoin (walk, character_phase...).
        //  Une commande differee les couvre toutes.
        //
        //  Le temps est celui de la SIMULATION (Advance, au pas fixe), pas
        //  l'horloge murale : une fenetre masquee tourne au ralenti (mesure
        //  sous macOS : 4 pas/s), et « dans 3 s » doit vouloir dire 3 s de jeu.
        // ------------------------------------------------------------------

        private readonly List<(double Due, long Order, string Line)> _scheduled = new();
        private readonly List<(double Due, long Order, string Line)> _dueNow = new();
        private double _clock;
        private long _order;

        /// <summary>Commandes differees en attente.</summary>
        public int ScheduledCount => _scheduled.Count;

        /// <summary>Programme une ligne de console dans « seconds » secondes de simulation.</summary>
        public void Schedule(double seconds, string line)
        {
            if (string.IsNullOrWhiteSpace(line) || !double.IsFinite(seconds) || seconds < 0.0)
                return;

            _scheduled.Add((_clock + seconds, _order++, line.Trim()));
        }

        /// <summary>
        /// Fait avancer l'horloge des commandes differees, et execute celles
        /// qui sont dues - dans l'ordre de leur echeance, puis de leur
        /// programmation. A appeler au PAS FIXE, quel que soit l'etat du jeu.
        ///
        /// ⚠️ Une commande programmee PENDANT cette execution (« delay 0
        /// delay 0 ... ») attend l'appel suivant : on n'execute que ce qui etait
        /// du AU DEBUT. Meme regle que la logique - une chaine ne peut pas
        /// tourner en rond dans un seul appel.
        /// </summary>
        public void Advance(double dt)
        {
            if (!(dt > 0.0))
                return;

            _clock += dt;

            if (_scheduled.Count == 0)
                return;

            _dueNow.Clear();

            for (int i = 0; i < _scheduled.Count;)
            {
                if (_scheduled[i].Due <= _clock + 1e-9)
                {
                    _dueNow.Add(_scheduled[i]);
                    _scheduled.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }

            _dueNow.Sort((a, b) => a.Due != b.Due ? a.Due.CompareTo(b.Due) : a.Order.CompareTo(b.Order));

            // Une COPIE : Execute peut programmer d'autres lignes, et la liste
            // de travail ne doit pas changer sous la boucle.
            foreach ((double _, long _, string line) in _dueNow.ToArray())
                Execute(line);
        }

        private void ScheduleFromLine(string[] parts)
        {
            // « 2,5 » est accepte comme « 2.5 » : la console est tapee par des
            // gens, sur des claviers francais.
            if (parts.Length < 3
                || !double.TryParse(parts[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                || !double.IsFinite(seconds) || seconds < 0.0)
            {
                Print("  delay <secondes> <commande> - execute la commande plus tard (temps de simulation)");
                return;
            }

            string line = string.Join(' ', parts, 2, parts.Length - 2);
            Schedule(seconds, line);

            Print($"  dans {seconds.ToString("0.###", CultureInfo.InvariantCulture)} s : {line}");
        }

        /// <summary>
        /// Execute une ligne. Formes reconnues :
        ///   nom                -> affiche la valeur
        ///   nom valeur         -> change la valeur
        ///   reset nom          -> valeur par defaut
        ///   list [filtre]      -> liste les variables
        ///   help [nom]         -> description
        ///   commande [args...] -> declenche une action
        /// </summary>
        public void Execute(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            line = line.Trim();
            _history.Add(line);
            Print("> " + line);

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string head = parts[0].ToLowerInvariant();

            switch (head)
            {
                case "list":
                    ListVariables(parts.Length > 1 ? parts[1] : null);
                    return;

                case "help":
                    ShowHelp(parts.Length > 1 ? parts[1] : null);
                    return;

                case "reset":
                    ResetVariable(parts.Length > 1 ? parts[1] : null);
                    return;

                case "clear":
                    Clear();
                    return;

                case "delay":
                    ScheduleFromLine(parts);
                    return;
            }

            if (_commands.TryGetValue(head, out ConCommand? command))
            {
                string[] args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

                try
                {
                    command.Action(args);
                }
                catch (Exception ex)
                {
                    Print("  erreur : " + ex.Message);
                }

                return;
            }

            if (_vars.TryGetValue(head, out ConVar? variable))
            {
                if (parts.Length == 1)
                {
                    Print($"  {variable.Name} = {Format(variable.Value)}   " +
                          $"(defaut {Format(variable.Default)}, de {Format(variable.Min)} a {Format(variable.Max)})");
                    return;
                }

                if (TryParse(parts[1], out float value))
                {
                    float before = variable.Value;

                    // Le gestionnaire Changed appelle du code moteur : allocation
                    // de texture, reconstruction de tampon... Il PEUT echouer.
                    // Sans ce try, une exception remonte jusqu'au gestionnaire
                    // clavier et fait tomber tout le jeu - pour une simple
                    // valeur mal choisie dans la console.
                    try
                    {
                        variable.Value = value;
                    }
                    catch (Exception ex)
                    {
                        Print($"  {variable.Name} : refuse - {ex.Message}");

                        // On tente de revenir a l'etat precedent.
                        try { variable.Value = before; } catch { }
                        return;
                    }

                    if (Math.Abs(variable.Value - value) > 1e-4f)
                        Print($"  {variable.Name} = {Format(variable.Value)}  (borne)");
                    else
                        Print($"  {variable.Name} : {Format(before)} -> {Format(variable.Value)}");
                }
                else
                {
                    Print($"  '{parts[1]}' n'est pas un nombre");
                }

                return;
            }

            Print($"  inconnu : '{head}'   (tape 'list' ou 'help')");
        }

        private void ListVariables(string? filter)
        {
            var names = new List<string>(_vars.Keys);
            names.Sort(StringComparer.Ordinal);

            int shown = 0;
            foreach (string name in names)
            {
                if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                ConVar v = _vars[name];
                Print($"  {name} = {Format(v.Value)}");
                shown++;
            }

            var commandNames = new List<string>(_commands.Keys);
            commandNames.Sort(StringComparer.Ordinal);

            foreach (string name in commandNames)
            {
                if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                Print($"  {name} ()");
                shown++;
            }

            if (shown == 0)
                Print("  rien ne correspond");
        }

        private void ShowHelp(string? name)
        {
            if (name == null)
            {
                Print("  list [filtre]      liste variables et commandes");
                Print("  help <nom>         description d'une entree");
                Print("  <nom>              affiche une valeur");
                Print("  <nom> <valeur>     change une valeur");
                Print("  reset <nom>        remet la valeur par defaut");
                Print("  clear              vide l'affichage");
                return;
            }

            string key = name.ToLowerInvariant();

            if (_vars.TryGetValue(key, out ConVar? v))
            {
                Print($"  {v.Name} : {v.Description}");
                Print($"    valeur {Format(v.Value)}, defaut {Format(v.Default)}, " +
                      $"de {Format(v.Min)} a {Format(v.Max)}");
                return;
            }

            if (_commands.TryGetValue(key, out ConCommand? c))
            {
                Print($"  {c.Name} : {c.Description}");
                return;
            }

            Print($"  inconnu : '{name}'");
        }

        private void ResetVariable(string? name)
        {
            if (name == null)
            {
                int failed = 0;

                foreach (ConVar v in _vars.Values)
                {
                    // Une variable qui refuse ne doit pas empecher les autres
                    // de revenir a leur valeur par defaut.
                    try { v.Reset(); }
                    catch { failed++; }
                }

                Print(failed == 0
                    ? "  toutes les variables remises par defaut"
                    : $"  variables remises par defaut, {failed} refus");
                return;
            }

            ConVar? target = Find(name);
            if (target == null)
            {
                Print($"  inconnu : '{name}'");
                return;
            }

            try
            {
                target.Reset();
            }
            catch (Exception ex)
            {
                Print($"  {target.Name} : reinitialisation impossible - {ex.Message}");
                return;
            }
            Print($"  {target.Name} = {Format(target.Value)}  (defaut)");
        }

        /// <summary>Completion : plus long prefixe commun des noms correspondants.</summary>
        public string Complete(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return prefix;

            string lower = prefix.ToLowerInvariant();
            var matches = new List<string>();

            foreach (string name in _vars.Keys)
                if (name.StartsWith(lower, StringComparison.Ordinal)) matches.Add(name);

            foreach (string name in _commands.Keys)
                if (name.StartsWith(lower, StringComparison.Ordinal)) matches.Add(name);

            if (matches.Count == 0)
                return prefix;

            if (matches.Count == 1)
                return matches[0];

            matches.Sort(StringComparer.Ordinal);

            string first = matches[0];
            string last = matches[^1];
            int common = 0;
            while (common < first.Length && common < last.Length && first[common] == last[common])
                common++;

            foreach (string m in matches)
                Print("  " + m);

            return first[..common];
        }

        private static bool TryParse(string text, out float value)
            => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

        private static string Format(float v)
            => v == MathF.Floor(v) && MathF.Abs(v) < 1e7f
                ? ((int)v).ToString(CultureInfo.InvariantCulture)
                : v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
