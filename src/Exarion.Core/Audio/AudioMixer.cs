// ===========================================================================
//  Exarion - Melangeur
//
//  Additionne N voix dans un tampon stereo entrelace. C'est tout ce que fait
//  une carte son : elle ne connait qu'un flux, et tout ce qu'on entend en
//  meme temps a ete additionne avant.
//
//  ---------------------------------------------------------------------
//  ⚠️⚠️ CE CODE TOURNE SUR LE FIL AUDIO. IL N'EST PAS COMME LE RESTE.
//
//  Le pilote reclame un tampon toutes les quelques millisecondes et n'attend
//  pas. Trois interdits, et ils ne se negocient pas :
//
//   - AUCUN VERROU. Si le fil de jeu detient le verrou pendant une pause du
//     ramasse-miettes, le tampon arrive vide : on entend un claquement.
//   - AUCUNE ALLOCATION. Meme raison, en pire : c'est elle qui declenche la
//     pause.
//   - AUCUNE EXCEPTION. Elle traverserait la frontiere native et tuerait le
//     processus.
//
//  D'ou la conception : un tableau de voix de taille FIXE, et un seul drapeau
//  par voix, ecrit avec une barriere memoire. Le fil de jeu remplit d'abord
//  tous les champs, PUIS leve le drapeau ; le fil audio lit d'abord le
//  drapeau, PUIS les champs. Sans cette barriere, le processeur a le droit de
//  reordonner les ecritures et le fil audio peut voir une voix "active" dont
//  le clip n'est pas encore ecrit - donc un dereferencement nul, une fois par
//  heure, impossible a reproduire.
//
//  ⚠️ Un SEUL fil a le droit d'appeler Play : celui du jeu. Deux producteurs
//  se disputeraient la meme place libre.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Threading;

namespace Exarion.Core.Audio
{
    public sealed class AudioMixer
    {
        /// <summary>Sortie stereo. Le nombre de canaux n'est pas negociable ici.</summary>
        public const int Channels = 2;

        private struct Voice
        {
            public AudioClip? Clip;

            /// <summary>Position dans le clip, en trames. EN DOUBLE, voir Mix.</summary>
            public double Cursor;

            /// <summary>
            /// Trames de clip consommees par trame de sortie, AVANT l'effet
            /// Doppler. Depend uniquement des deux frequences.
            /// </summary>
            public double Step;

            /// <summary>Multiplicateur de hauteur. 1 = son d'origine.</summary>
            public float Pitch;

            public float Gain;
            public float Pan;
            public bool Loop;

            /// <summary>
            /// Coefficient du passe-bas d'ordre 1. 1 = pas de filtre, 0,02 =
            /// tres etouffe. Sert a l'occlusion (S4).
            /// </summary>
            public float Cutoff;

            /// <summary>
            /// Memoire du filtre. ELLE DOIT SURVIVRE D'UN TAMPON A L'AUTRE :
            /// la remettre a zero a chaque appel ferait un CLAQUEMENT toutes
            /// les quelques millisecondes, a la frontiere des tampons.
            /// </summary>
            public float LowState;
        }

        private readonly Voice[] _voices;

        /// <summary>0 = libre, 1 = en cours. Lu et ecrit avec barriere.</summary>
        private readonly int[] _active;

        /// <summary>
        /// Incremente a chaque reutilisation d'une place. Sans ca, un
        /// identifiant garde trop longtemps arreterait le son qui a pris la
        /// place - un bug qui ne se manifeste que sous charge.
        /// </summary>
        private readonly int[] _generation;

        public AudioMixer(int sampleRate, int maxVoices = 64)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (maxVoices <= 0) throw new ArgumentOutOfRangeException(nameof(maxVoices));

            SampleRate = sampleRate;

            _voices = new Voice[maxVoices];
            _active = new int[maxVoices];
            _generation = new int[maxVoices];
        }

        public int SampleRate { get; }

        public int MaxVoices => _voices.Length;

        public float MasterGain { get; set; } = 1f;

        /// <summary>Sons refuses faute de voix libre. Exposee pour la meme raison que ParticleSystem.Dropped.</summary>
        public int Dropped { get; private set; }

        /// <summary>Plus fort echantillon du dernier melange, avant bornage. Sert a mesurer.</summary>
        public float LastPeak { get; private set; }

        public int ActiveVoices
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _active.Length; i++)
                    if (Volatile.Read(ref _active[i]) != 0) n++;
                return n;
            }
        }

        /// <summary>
        /// Lance un son. Rend un identifiant, ou -1 si toutes les voix sont prises.
        /// </summary>
        /// <param name="pan">-1 tout a gauche, 0 au centre, +1 tout a droite.</param>
        public int Play(AudioClip clip, float gain = 1f, float pan = 0f, bool loop = false,
                        float pitch = 1f, float cutoff = 1f)
        {
            if (clip is null || clip.FrameCount == 0)
                return -1;

            for (int i = 0; i < _voices.Length; i++)
            {
                if (Volatile.Read(ref _active[i]) != 0)
                    continue;

                _voices[i].Clip = clip;
                _voices[i].Cursor = 0.0;
                _voices[i].Gain = gain;
                _voices[i].Pan = Math.Clamp(pan, -1f, 1f);
                _voices[i].Loop = loop;
                _voices[i].Pitch = ClampPitch(pitch);
                _voices[i].Cutoff = Math.Clamp(cutoff, 0.001f, 1f);

                // La memoire du filtre appartient a la voix precedente : la
                // garder ferait demarrer le nouveau son sur une valeur qui
                // n'a rien a voir avec lui, donc un claquement.
                _voices[i].LowState = 0f;

                // Reechantillonnage : un clip a 44 100 joue sur une carte a
                // 48 000 doit avancer moins vite d'un facteur 44,1/48.
                // L'oublier fait jouer tous les sons trop aigus - defaut
                // classique, et audible immediatement sur une voix.
                _voices[i].Step = (double)clip.SampleRate / SampleRate;

                int generation = ++_generation[i];

                // LA barriere. Tout ce qui precede doit etre visible avant que
                // le drapeau ne passe a 1.
                Volatile.Write(ref _active[i], 1);

                return (i << 16) | (generation & 0xFFFF);
            }

            Dropped++;
            return -1;
        }

        /// <summary>Arrete une voix. Sans effet si l'identifiant est perime.</summary>
        public void Stop(int handle)
        {
            if (handle < 0)
                return;

            int slot = handle >> 16;

            if (slot < 0 || slot >= _voices.Length)
                return;

            if ((_generation[slot] & 0xFFFF) != (handle & 0xFFFF))
                return;

            Volatile.Write(ref _active[slot], 0);
        }

        public void StopAll()
        {
            for (int i = 0; i < _active.Length; i++)
                Volatile.Write(ref _active[i], 0);
        }

        /// <summary>
        /// Regle une voix deja lancee. Sert a suivre une source qui bouge :
        /// le fil de JEU calcule tout, le fil audio ne fait qu'appliquer.
        /// </summary>
        public void Set(int handle, float gain, float pan, float pitch = 1f, float cutoff = 1f)
        {
            if (handle < 0)
                return;

            int slot = handle >> 16;

            if (slot < 0 || slot >= _voices.Length)
                return;

            if ((_generation[slot] & 0xFFFF) != (handle & 0xFFFF))
                return;

            if (Volatile.Read(ref _active[slot]) == 0)
                return;

            _voices[slot].Gain = gain;
            _voices[slot].Pan = Math.Clamp(pan, -1f, 1f);
            _voices[slot].Pitch = ClampPitch(pitch);
            _voices[slot].Cutoff = Math.Clamp(cutoff, 0.001f, 1f);
        }

        /// <summary>Vrai si la voix joue encore. Sert a nettoyer les suivis.</summary>
        public bool IsPlaying(int handle)
        {
            if (handle < 0)
                return false;

            int slot = handle >> 16;

            if (slot < 0 || slot >= _voices.Length)
                return false;

            if ((_generation[slot] & 0xFFFF) != (handle & 0xFFFF))
                return false;

            return Volatile.Read(ref _active[slot]) != 0;
        }

        /// <summary>
        /// Borne la hauteur. Un Doppler mal borne peut rendre un facteur
        /// enorme quand une source fonce vers l'auditeur : le curseur
        /// avancerait de milliers de trames par echantillon, et le son
        /// deviendrait un claquement.
        /// </summary>
        private static float ClampPitch(float pitch)
            => float.IsFinite(pitch) ? Math.Clamp(pitch, 0.25f, 4f) : 1f;

        /// <summary>
        /// Remplit un tampon stereo entrelace. APPELE SUR LE FIL AUDIO.
        /// </summary>
        public void Mix(Span<float> output)
        {
            // Le pilote ne garantit pas un tampon propre. Ne pas l'effacer
            // ferait rejouer le contenu precedent sous le nouveau son.
            output.Clear();

            int frames = output.Length / Channels;

            if (frames == 0)
                return;

            for (int v = 0; v < _voices.Length; v++)
            {
                if (Volatile.Read(ref _active[v]) == 0)
                    continue;

                ref Voice voice = ref _voices[v];
                AudioClip? clip = voice.Clip;

                if (clip is null)
                {
                    Volatile.Write(ref _active[v], 0);
                    continue;
                }

                // ------------------------------------------------------------
                // PANORAMIQUE A PUISSANCE CONSTANTE, pas lineaire.
                //
                // Le reflexe est gauche = (1-p)/2, droite = (1+p)/2. Au centre
                // les deux valent 0,5, et le son parait AFFAIBLI : l'oreille
                // percoit la puissance, qui va comme le carre de l'amplitude.
                // 0,5 + 0,5 en amplitude ne font que 0,5 en puissance.
                //
                // Avec la racine, au centre les deux valent 0,707 et la somme
                // des carres fait exactement 1 : un son qui traverse l'ecran
                // garde un volume constant.
                // ------------------------------------------------------------
                float left = voice.Gain * MathF.Sqrt(0.5f * (1f - voice.Pan));
                float right = voice.Gain * MathF.Sqrt(0.5f * (1f + voice.Pan));

                int total = clip.FrameCount;
                double cursor = voice.Cursor;

                // La hauteur MULTIPLIE le pas de lecture : c'est exactement ce
                // que fait l'effet Doppler dans la realite - une source qui
                // s'approche comprime les ondes, donc on lit le clip plus vite.
                double step = voice.Step * voice.Pitch;

                float cutoff = voice.Cutoff;
                float low = voice.LowState;
                bool filtered = cutoff < 0.999f;

                bool finished = false;

                for (int f = 0; f < frames; f++)
                {
                    if (cursor >= total)
                    {
                        if (!voice.Loop)
                        {
                            finished = true;
                            break;
                        }

                        cursor -= total;

                        // Un pas gigantesque pourrait sauter plusieurs fois la
                        // fin. On ne boucle pas indefiniment pour autant.
                        if (cursor >= total)
                            cursor = 0.0;
                    }

                    int index = (int)cursor;
                    float fraction = (float)(cursor - index);

                    // Interpolation lineaire entre deux echantillons. Elle
                    // ajoute un peu de repliement dans les aigus - un
                    // reechantillonneur serieux utiliserait un noyau plus
                    // large - mais sur un bruit de pas ou une detonation, la
                    // difference est inaudible et le cout est nul.
                    float a = clip.MonoAt(index);
                    float b = clip.MonoAt(index + 1);
                    float sample = a + (b - a) * fraction;

                    // ------------------------------------------------------------
                    // PASSE-BAS D'ORDRE 1 : l'occlusion (S4).
                    //
                    // y += a * (x - y). Une seule multiplication et une seule
                    // addition par echantillon, et pourtant c'est bien un vrai
                    // filtre : il laisse passer les graves et coupe les aigus a
                    // 6 dB par octave.
                    //
                    // C'est exactement ce que fait un mur. Un son etouffe par
                    // une cloison n'est pas seulement plus faible : il a PERDU
                    // SES AIGUS, et c'est ce que l'oreille interprete comme
                    // "de l'autre cote". Baisser simplement le volume donne un
                    // son lointain, pas un son cache - la difference est
                    // enorme dans un jeu ou l'on ecoute les pas.
                    // ------------------------------------------------------------
                    if (filtered)
                    {
                        low += (sample - low) * cutoff;
                        sample = low;
                    }

                    int o = f * Channels;
                    output[o] += sample * left;
                    output[o + 1] += sample * right;

                    cursor += step;
                }

                voice.Cursor = cursor;
                voice.LowState = low;

                if (finished)
                    Volatile.Write(ref _active[v], 0);
            }

            float peak = 0f;
            float master = MasterGain;

            for (int i = 0; i < output.Length; i++)
            {
                float value = output[i] * master;

                float magnitude = MathF.Abs(value);
                if (magnitude > peak) peak = magnitude;

                // ⚠️ Bornage OBLIGATOIRE. Vingt sons additionnes depassent
                // largement 1,0, et un flottant hors bornes envoye a la carte
                // devient un craquement violent - bien pire qu'un son sature.
                output[i] = value > 1f ? 1f : value < -1f ? -1f : value;
            }

            LastPeak = peak;
        }
    }
}
