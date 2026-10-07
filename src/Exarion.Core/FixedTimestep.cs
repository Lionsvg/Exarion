// ===========================================================================
//  Exarion - Boucle a pas fixe
//
//  POURQUOI c'est la premiere chose ecrite du moteur :
//
//  Un FPS competitif ne peut PAS simuler au rythme des images. Si la
//  simulation avance de "temps ecoule depuis la derniere image", deux
//  joueurs avec des machines differentes n'obtiennent pas le meme resultat
//  pour les memes entrees. La physique diverge, et la reconciliation
//  reseau devient impossible.
//
//  ---------------------------------------------------------------------
//  DEUX DECISIONS DE CONCEPTION, apprises a la dure
//  ---------------------------------------------------------------------
//
//  1. TEMPS ABSOLU, PAS DES DELTAS.
//     La version naive accumule les deltas d'images :
//         accumulateur += deltaImage
//     Ca DERIVE. 1/50 n'est pas representable exactement en binaire ;
//     apres 50 soustractions il manque une fraction, et le 50e pas ne part
//     jamais. Mesure : 49 pas au lieu de 50 sur une seconde.
//     Ici on passe le temps ECOULE DEPUIS LE DEMARRAGE. L'erreur ne peut
//     plus s'accumuler puisqu'on ne fait jamais de soustraction repetee.
//
//  2. NANOSECONDES ENTIERES, PAS DES DOUBLES.
//     Toute l'horlogerie interne est en long. Aucun arrondi, aucune
//     derive possible, et c'est directement compatible avec Stopwatch.
//
//  Cette classe est du C# pur : elle se teste sans ouvrir de fenetre.
// ===========================================================================

using System;

namespace Exarion.Core
{
    public sealed class FixedTimestep
    {
        private const long NanosecondsPerSecond = 1_000_000_000L;
        /// <summary>Un tick de TimeSpan vaut 100 ns.</summary>
        private const long NanosecondsPerTimeSpanTick = 100L;

        private readonly long _fixedDeltaNs;

        /// <summary>Position de l'horloge de jeu, en ns. Toujours un multiple de _fixedDeltaNs.</summary>
        private long _simulatedNs;

        /// <summary>Dernier temps absolu demande, pour calculer Alpha.</summary>
        private long _targetNs;

        /// <summary>
        /// Pas REELLEMENT executes. Distinct de _simulatedNs / _fixedDeltaNs :
        /// quand la machine decroche, l'horloge de jeu saute en avant pour
        /// rester alignee sur le temps reel, mais ces pas-la ne sont pas simules.
        /// Un ecart entre les deux est le symptome d'une machine trop lente.
        /// </summary>
        private long _totalSteps;

        /// <summary>
        /// Nombre maximum de pas rattrapes en une image. Protege de la
        /// "spirale de la mort" : si la simulation est plus lente que le temps
        /// reel, rattraper indefiniment aggrave le retard a chaque image.
        /// Au-dela, on abandonne du temps plutot que de figer le jeu.
        /// </summary>
        public int MaxStepsPerFrame { get; }

        /// <summary>Vrai si le dernier appel a du abandonner du temps.</summary>
        public bool LastFrameWasClamped { get; private set; }

        public FixedTimestep(double tickRate = 64.0, int maxStepsPerFrame = 5)
        {
            if (tickRate <= 0.0 || double.IsNaN(tickRate) || double.IsInfinity(tickRate))
                throw new ArgumentOutOfRangeException(nameof(tickRate), "Le tickrate doit etre un nombre positif.");
            if (maxStepsPerFrame < 1)
                throw new ArgumentOutOfRangeException(nameof(maxStepsPerFrame), "Il faut au moins un pas par image.");

            _fixedDeltaNs = (long)Math.Round(NanosecondsPerSecond / tickRate);
            if (_fixedDeltaNs < 1)
                throw new ArgumentOutOfRangeException(nameof(tickRate), "Tickrate trop eleve : le pas serait inferieur a 1 ns.");

            MaxStepsPerFrame = maxStepsPerFrame;
        }

        /// <summary>Duree d'un pas, en secondes. C'est le dt a passer a la simulation.</summary>
        public double FixedDelta => _fixedDeltaNs / (double)NanosecondsPerSecond;

        /// <summary>Duree d'un pas, en nanosecondes. Exacte, sans arrondi.</summary>
        public long FixedDeltaNanoseconds => _fixedDeltaNs;

        /// <summary>Cadence de simulation, en pas par seconde.</summary>
        public double TickRate => NanosecondsPerSecond / (double)_fixedDeltaNs;

        /// <summary>Nombre de pas reellement simules depuis le demarrage.</summary>
        public long TotalSteps => _totalSteps;

        /// <summary>
        /// Pas sautes parce que la machine n'a pas suivi. Doit rester a 0.
        /// S'il grimpe, c'est que la simulation coute trop cher.
        /// </summary>
        public long SkippedSteps => (_simulatedNs / _fixedDeltaNs) - _totalSteps;

        /// <summary>Temps de jeu simule, en secondes. Horloge de reference du gameplay.</summary>
        public double SimulatedTime => _simulatedNs / (double)NanosecondsPerSecond;

        /// <summary>Temps simule en nanosecondes, exact.</summary>
        public long SimulatedNanoseconds => _simulatedNs;

        /// <summary>Temps reel accumule mais pas encore simule, en nanosecondes.</summary>
        public long PendingNanoseconds => _targetNs - _simulatedNs;

        /// <summary>
        /// Facteur d'interpolation du rendu, dans [0,1[.
        /// 0 = on est pile sur le dernier pas simule.
        /// Le rendu doit afficher : precedent + (actuel - precedent) * Alpha
        /// </summary>
        public float Alpha
        {
            get
            {
                long pending = PendingNanoseconds;
                if (pending <= 0)
                    return 0f;

                float a = (float)(pending / (double)_fixedDeltaNs);
                return a >= 1f ? 0.999999f : a;
            }
        }

        /// <summary>
        /// L'HORLOGE DU RENDU : SimulatedTime + la fraction de pas en cours.
        ///
        /// ⚠️ C'EST CELLE-CI QU'IL FAUT POUR ANIMER, et le piege a coute une
        /// session complete. Trois proprietes qu'elle est seule a avoir :
        ///
        ///  - elle est LISSE : elle avance entre deux pas fixes. Prendre
        ///    SimulatedTime ferait saccader toute animation des que le rendu
        ///    depasse la cadence de simulation ;
        ///  - elle est DETERMINISTE : deux captures au meme instant donnent la
        ///    meme pose, parce qu'elle ne depend que du temps absolu ;
        ///  - elle est IDEMPOTENTE : on peut la LIRE plusieurs fois dans la
        ///    meme image sans que rien n'avance. C'est vital, parce qu'une
        ///    image echantillonne la meme pose trois fois (ombre, occlusion,
        ///    passe finale) et que les trois doivent decrire le meme
        ///    personnage au meme instant.
        ///
        /// ⚠️ Ne JAMAIS accumuler un delta a la place. Une animation avancee
        /// d'un delta par appel tournerait au TRIPLE de sa vitesse, pour la
        /// raison ci-dessus - et l'erreur ne se voit pas dans un test unitaire.
        /// </summary>
        public double RenderTime => SimulatedTime + Alpha * FixedDelta;

        /// <summary>
        /// Avance jusqu'au temps absolu donne et retourne le nombre de pas
        /// de simulation a executer maintenant.
        /// </summary>
        /// <param name="elapsedNanoseconds">
        /// Temps ecoule depuis le demarrage, en nanosecondes. ABSOLU, pas un delta.
        /// </param>
        public int AdvanceTo(long elapsedNanoseconds)
        {
            LastFrameWasClamped = false;

            // Une horloge ne recule pas. Si ca arrive, on ignore.
            if (elapsedNanoseconds > _targetNs)
                _targetNs = elapsedNanoseconds;

            long pending = _targetNs - _simulatedNs;
            if (pending < _fixedDeltaNs)
                return 0;

            long available = pending / _fixedDeltaNs;
            int steps;

            if (available > MaxStepsPerFrame)
            {
                // La machine a decroche. On fait avancer l'horloge de jeu pour
                // rester aligne sur le temps reel, mais on ne simule que
                // MaxStepsPerFrame pas : le reste est saute, pas rattrape.
                steps = MaxStepsPerFrame;
                _simulatedNs += (available - MaxStepsPerFrame) * _fixedDeltaNs;
                LastFrameWasClamped = true;
            }
            else
            {
                steps = (int)available;
            }

            _simulatedNs += (long)steps * _fixedDeltaNs;
            _totalSteps += steps;
            return steps;
        }

        /// <summary>Surcharge pratique : Stopwatch.Elapsed se passe directement.</summary>
        public int AdvanceTo(TimeSpan elapsed)
            => AdvanceTo(elapsed.Ticks * NanosecondsPerTimeSpanTick);

        /// <summary>Surcharge pratique pour les tests, en secondes absolues.</summary>
        public int AdvanceToSeconds(double elapsedSeconds)
            => AdvanceTo((long)Math.Round(elapsedSeconds * NanosecondsPerSecond));

        public void Reset()
        {
            _simulatedNs = 0;
            _targetNs = 0;
            _totalSteps = 0;
            LastFrameWasClamped = false;
        }
    }
}
