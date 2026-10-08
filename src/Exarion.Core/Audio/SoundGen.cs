// ===========================================================================
//  Exarion - Sons generes en memoire
//
//  Le projet n'a AUCUN fichier audio, exactement comme il n'avait aucune
//  texture avant TextureGen. On genere donc de quoi travailler : rien a
//  charger, rien a distribuer, rien dont la licence soit a verifier.
//
//  Ce ne sont pas des sons de jeu - une vraie detonation s'enregistre. Ce
//  sont des sons de MISE AU POINT : on sait exactement ce qu'ils contiennent,
//  donc on peut verifier ce qui ressort a l'autre bout de la chaine.
// ===========================================================================

using System;
using Exarion.Core.Deterministic;

namespace Exarion.Core.Audio
{
    public static class SoundGen
    {
        /// <summary>
        /// Sinusoide pure, avec une enveloppe.
        ///
        /// ⚠️ L'ENVELOPPE N'EST PAS UNE COQUETTERIE. Une sinusoide qui demarre
        /// et s'arrete net presente une DISCONTINUITE : le signal saute de 0 a
        /// sa valeur en un echantillon. L'oreille entend un CLAQUEMENT, souvent
        /// plus fort que la note elle-meme, parce qu'un saut brutal contient
        /// toutes les frequences a la fois.
        ///
        /// Quelques millisecondes de montee et de descente suffisent.
        /// </summary>
        public static AudioClip Tone(float frequency, float seconds, int sampleRate = 48000,
                                     float amplitude = 0.5f, float fade = 0.01f)
        {
            int count = Math.Max(1, (int)(seconds * sampleRate));
            var samples = new float[count];

            double phaseStep = 2.0 * Math.PI * frequency / sampleRate;
            int fadeFrames = Math.Max(1, (int)(fade * sampleRate));

            for (int i = 0; i < count; i++)
            {
                float envelope = Envelope(i, count, fadeFrames);
                samples[i] = (float)Math.Sin(phaseStep * i) * amplitude * envelope;
            }

            return new AudioClip(samples, 1, sampleRate);
        }

        /// <summary>
        /// Bruit a decroissance rapide : un impact.
        ///
        /// Une detonation, un choc, un pas, c'est du bruit large bande qui
        /// s'eteint vite. Une sinusoide ne ressemblera jamais a un impact,
        /// quelle que soit sa frequence - c'est le bruit qui porte l'idee de
        /// choc, pas la hauteur.
        /// </summary>
        public static AudioClip Impact(float seconds = 0.18f, int sampleRate = 48000,
                                       float amplitude = 0.8f, uint seed = 1u)
        {
            int count = Math.Max(1, (int)(seconds * sampleRate));
            var samples = new float[count];
            var rng = new Rng(seed);

            // Un filtre passe-bas d'ordre 1 arrondit le bruit blanc, qui est
            // sinon trop sifflant pour evoquer un choc de matiere.
            float previous = 0f;
            const float smoothing = 0.45f;

            for (int i = 0; i < count; i++)
            {
                float white = rng.NextFloat(-1f, 1f);
                previous += (white - previous) * smoothing;

                // Decroissance exponentielle : le son perd 60 dB sur la duree.
                float decay = MathF.Exp(-7f * i / count);

                samples[i] = previous * decay * amplitude;
            }

            return new AudioClip(samples, 1, sampleRate);
        }

        /// <summary>
        /// Balayage de frequence. Utile pour verifier un filtre : on sait
        /// exactement quelle frequence passe a quel instant.
        /// </summary>
        public static AudioClip Sweep(float fromHz, float toHz, float seconds,
                                      int sampleRate = 48000, float amplitude = 0.5f)
        {
            int count = Math.Max(1, (int)(seconds * sampleRate));
            var samples = new float[count];
            int fadeFrames = Math.Max(1, (int)(0.01f * sampleRate));

            // On integre la phase au lieu de calculer sin(2 pi f t) avec f
            // variable : cette derniere forme produit des sauts de phase, donc
            // des craquements, des que la frequence change.
            double phase = 0.0;

            for (int i = 0; i < count; i++)
            {
                double t = (double)i / count;
                double frequency = fromHz + (toHz - fromHz) * t;

                phase += 2.0 * Math.PI * frequency / sampleRate;

                samples[i] = (float)Math.Sin(phase) * amplitude * Envelope(i, count, fadeFrames);
            }

            return new AudioClip(samples, 1, sampleRate);
        }

        /// <summary>
        /// Un BIP suivi d'un silence, fait pour etre joue EN BOUCLE : une
        /// alarme, un signal.
        ///
        /// ⚠️ POURQUOI PAS Tone EN BOUCLE. Tone porte une enveloppe (voir plus
        /// haut) : bouclee, elle retombe a zero a chaque tour, et le son
        /// « respire » au rythme de la boucle - un defaut qui s'entend sans se
        /// comprendre. Ici le silence fait partie du clip : le premier et le
        /// dernier echantillon valent zero, la boucle se raccorde donc sans
        /// discontinuite, et le rythme est voulu.
        /// </summary>
        public static AudioClip Beep(float frequency, float onSeconds, float offSeconds,
                                     int sampleRate = 48000, float amplitude = 0.5f)
        {
            float[] tone = Tone(frequency, onSeconds, sampleRate, amplitude).Samples;
            int silence = Math.Max(1, (int)(offSeconds * sampleRate));

            var samples = new float[tone.Length + silence];
            Array.Copy(tone, samples, tone.Length);

            return new AudioClip(samples, 1, sampleRate);
        }

        /// <summary>Montee au debut, descente a la fin, plat au milieu.</summary>
        private static float Envelope(int index, int count, int fadeFrames)
        {
            if (fadeFrames * 2 >= count)
                fadeFrames = Math.Max(1, count / 2);

            if (index < fadeFrames)
                return (float)index / fadeFrames;

            int remaining = count - 1 - index;

            if (remaining < fadeFrames)
                return (float)remaining / fadeFrames;

            return 1f;
        }
    }
}
