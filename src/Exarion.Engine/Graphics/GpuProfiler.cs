// ===========================================================================
//  Exarion - Profileur GPU
//
//  POURQUOI. Le profileur de physique existe depuis 4.3 et a servi tout de
//  suite - il a montre que les pics de 15 ms venaient de la compilation a la
//  volee et non du solveur. Le RENDU, lui, n'a jamais ete mesure : le cout de
//  la SSAO est un aveu ecrit dans CLAUDE.md, celui du bloom et des particules
//  n'a jamais ete regarde.
//
//  Et on ne peut PAS le deduire du framerate. A 3 000 images par seconde, un
//  effet qui coute 0,3 ms se perd dans le bruit ; pire, les img/s ne sont
//  comparables que dos a dos dans une meme session, piege deja paye plusieurs
//  fois sur ce projet.
//
//  ---------------------------------------------------------------------
//  ⚠️⚠️ LE PIEGE CENTRAL : LIRE UN RESULTAT TROP TOT DETRUIT LA MESURE.
//
//  Le processeur graphique travaille avec plusieurs images de retard sur les
//  commandes qu'on lui envoie. Demander le resultat d'une requete juste apres
//  l'avoir posee force le pilote a ATTENDRE que le GPU ait tout rattrape.
//
//  Ce n'est pas seulement lent : la mesure devient FAUSSE. En synchronisant
//  les deux processeurs a chaque image, on supprime le recouvrement qui
//  existe normalement entre eux, et chaque passe parait plus chere qu'elle
//  ne l'est. On lirait donc un profil de la situation qu'on a creee en
//  mesurant, pas du jeu.
//
//  D'ou l'anneau : on pose les requetes de l'image N et on ne lit que celles
//  de l'image N-3, largement terminees. Aucune attente, jamais.
//  ---------------------------------------------------------------------
//
//  ⚠️ On utilise des HORODATAGES (GL_TIMESTAMP) et non GL_TIME_ELAPSED.
//  Ce dernier ne supporte pas l'imbrication - une seule requete de ce type
//  peut etre active a la fois - alors que les horodatages se posent
//  librement et se soustraient apres coup.
//
//  ⚠️ Ca mesure le temps GPU. Le temps que le processeur passe a PREPARER
//  les commandes est autre chose, et se mesure au chronometre ordinaire.
//  Une passe peut etre gratuite pour le GPU et couteuse a soumettre.
// ===========================================================================

using System;
using Silk.NET.OpenGL;

namespace Exarion.Engine.Graphics
{
    /// <summary>Les passes qu'on sait chronometrer. Un enum, donc zero allocation.</summary>
    public enum GpuZone
    {
        Shadow,
        SsaoGeometry,
        SsaoOcclusion,
        Sky,
        Geometry,
        Particles,
        Bloom,
        Composite,
        Hud,
    }

    public sealed class GpuProfiler : IDisposable
    {
        /// <summary>
        /// Images de retard avant lecture.
        ///
        /// ⚠️ TROIS NE SUFFISAIENT PAS, et le compteur de releves l'a montre :
        /// a 1 500 images par seconde, trois images ne font que 2 ms, et la
        /// plupart des resultats n'etaient pas encore prets. Sur une fenetre
        /// d'une seconde, on ne relevait parfois que 6 mesures sur 1 500.
        ///
        /// Le danger n'est pas la faiblesse de l'echantillon, c'est son BIAIS :
        /// les seules images dont le resultat arrivait a temps etaient celles
        /// ou le GPU avait pris de l'avance, donc les moins chargees. La
        /// moyenne penchait donc systematiquement du bon cote.
        ///
        /// Seize images font environ 10 ms au meme framerate : tout est pret.
        /// Le cout est de 288 objets de requete, ce qui ne coute rien.
        /// </summary>
        private const int Latency = 16;

        private static readonly string[] Names =
        {
            "ombre", "ssao geom", "ssao calc", "ciel", "geometrie",
            "particules", "bloom", "composition", "hud",
        };

        private static readonly int ZoneCount = Names.Length;

        private readonly GL _gl;

        /// <summary>Deux horodatages par zone et par image de l'anneau.</summary>
        private readonly uint[] _queries;

        /// <summary>Zones effectivement chronometrees, par image de l'anneau.</summary>
        private readonly bool[] _used;

        /// <summary>Vrai quand une image de l'anneau a deja servi.</summary>
        private readonly bool[] _ready;

        private readonly double[] _sum;
        private readonly double[] _max;

        /// <summary>
        /// Relevés effectifs, PAR ZONE.
        ///
        /// ⚠️ Un seul compteur global etait un BUG, et il a produit des
        /// chiffres absurdes : 0,005 ms de GPU total a 1 530 images par
        /// seconde, soit une image de 0,65 ms rendue en un centieme de son
        /// temps. Explication : quand un resultat n'est pas encore disponible
        /// on le saute - c'est voulu, lire de force synchroniserait tout - mais
        /// le compteur global, lui, montait a chaque image. On divisait donc
        /// par le nombre d'IMAGES et non par le nombre de MESURES.
        ///
        /// Le defaut s'aggravait avec le framerate : plus les images defilent
        /// vite, plus la proportion de resultats en retard est grande, et plus
        /// la moyenne s'effondre. D'ou des chiffres qui BAISSAIENT quand le jeu
        /// accelerait - le signal qui a mis la puce a l'oreille.
        /// </summary>
        private readonly int[] _counts;

        private readonly double[] _average;
        private readonly double[] _worst;

        private int _slot = -1;
        private GpuZone _openZone;
        private bool _zoneOpen;

        public GpuProfiler(GL gl)
        {
            _gl = gl;

            _queries = new uint[Latency * ZoneCount * 2];
            _used = new bool[Latency * ZoneCount];
            _ready = new bool[Latency];

            _sum = new double[ZoneCount];
            _counts = new int[ZoneCount];
            _max = new double[ZoneCount];
            _average = new double[ZoneCount];
            _worst = new double[ZoneCount];

            for (int i = 0; i < _queries.Length; i++)
                _queries[i] = _gl.GenQuery();
        }

        public bool Enabled { get; set; }

        public static string NameOf(GpuZone zone) => Names[(int)zone];

        public static int Zones => ZoneCount;

        /// <summary>Moyenne de la zone sur la derniere fenetre, en millisecondes.</summary>
        public double Average(GpuZone zone) => _average[(int)zone];

        /// <summary>Pire valeur de la zone sur la derniere fenetre.</summary>
        public double Worst(GpuZone zone) => _worst[(int)zone];

        public double AverageTotal
        {
            get
            {
                double total = 0;
                for (int i = 0; i < ZoneCount; i++) total += _average[i];
                return total;
            }
        }

        public void BeginFrame()
        {
            if (!Enabled)
                return;

            _slot = (_slot + 1) % Latency;

            for (int z = 0; z < ZoneCount; z++)
                _used[_slot * ZoneCount + z] = false;
        }

        public void Begin(GpuZone zone)
        {
            if (!Enabled || _slot < 0 || _zoneOpen)
                return;

            _gl.QueryCounter(_queries[QueryIndex(_slot, zone, 0)], QueryCounterTarget.Timestamp);

            _openZone = zone;
            _zoneOpen = true;
        }

        public void End()
        {
            if (!Enabled || !_zoneOpen)
                return;

            _gl.QueryCounter(_queries[QueryIndex(_slot, _openZone, 1)], QueryCounterTarget.Timestamp);

            _used[_slot * ZoneCount + (int)_openZone] = true;
            _zoneOpen = false;
        }

        /// <summary>
        /// Releve les resultats de l'image la plus ancienne de l'anneau.
        /// Aucune attente : elle est terminee depuis longtemps.
        /// </summary>
        public unsafe void EndFrame()
        {
            if (!Enabled || _slot < 0)
                return;

            // Une zone laissee ouverte fausserait tout l'anneau.
            if (_zoneOpen)
                End();

            _ready[_slot] = true;

            int oldest = (_slot + 1) % Latency;

            if (!_ready[oldest])
                return;

            for (int z = 0; z < ZoneCount; z++)
            {
                if (!_used[oldest * ZoneCount + z])
                    continue;

                uint start = _queries[QueryIndex(oldest, (GpuZone)z, 0)];
                uint stop = _queries[QueryIndex(oldest, (GpuZone)z, 1)];

                // Garde-fou : si le resultat n'est pas pret, on PASSE. Le lire
                // quand meme rendrait la main au prix d'une synchronisation,
                // ce que tout ce fichier existe pour eviter.
                _gl.GetQueryObject(stop, QueryObjectParameterName.ResultAvailable, out int available);

                if (available == 0)
                    continue;

                ulong a = 0, b = 0;
                _gl.GetQueryObject(start, QueryObjectParameterName.Result, &a);
                _gl.GetQueryObject(stop, QueryObjectParameterName.Result, &b);

                // Les horodatages sont en NANOSECONDES.
                double ms = (b - a) / 1_000_000.0;

                _sum[z] += ms;
                _counts[z]++;

                if (ms > _max[z]) _max[z] = ms;
            }
        }

        /// <summary>Cloture la fenetre de mesure et prepare les moyennes.</summary>
        public void Flush()
        {
            for (int z = 0; z < ZoneCount; z++)
            {
                // On divise par le nombre de MESURES de cette zone, jamais par
                // le nombre d'images. Voir le commentaire de _counts : c'est
                // exactement la confusion qui a produit des chiffres cent fois
                // trop bas.
                _average[z] = _counts[z] > 0 ? _sum[z] / _counts[z] : 0.0;
                _worst[z] = _max[z];

                _sum[z] = 0;
                _max[z] = 0;
                _lastCounts[z] = _counts[z];
                _counts[z] = 0;
            }
        }

        /// <summary>
        /// Releves effectifs de la zone sur la derniere fenetre.
        ///
        /// A AFFICHER dans le rapport : c'est le seul moyen de voir qu'une
        /// moyenne repose sur trois mesures au lieu de mille, et donc qu'il
        /// faut allonger l'anneau.
        /// </summary>
        public int SampleCount(GpuZone zone) => _lastCounts[(int)zone];

        private readonly int[] _lastCounts = new int[ZoneCount];

        private static int QueryIndex(int slot, GpuZone zone, int end)
            => ((slot * ZoneCount) + (int)zone) * 2 + end;

        public void Dispose()
        {
            for (int i = 0; i < _queries.Length; i++)
                if (_queries[i] != 0)
                    _gl.DeleteQuery(_queries[i]);
        }
    }
}
