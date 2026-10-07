// ===========================================================================
//  Exarion - Un son en memoire
//
//  Des echantillons, un nombre de canaux, une frequence. Rien de plus : un
//  clip ne sait pas s'il joue, ne connait pas sa position dans le monde, et
//  ne garde aucun curseur. C'est important - le meme bruit de pas doit
//  pouvoir jouer vingt fois en meme temps, a vingt endroits differents, sans
//  etre charge vingt fois.
//
//  L'etat de lecture vit donc dans le melangeur, pas ici.
// ===========================================================================

using System;

namespace Exarion.Core.Audio
{
    public sealed class AudioClip
    {
        /// <summary>Echantillons ENTRELACES, dans [-1, 1].</summary>
        public float[] Samples { get; }

        public int Channels { get; }

        public int SampleRate { get; }

        public AudioClip(float[] samples, int channels, int sampleRate)
        {
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

            Samples = samples ?? throw new ArgumentNullException(nameof(samples));
            Channels = channels;
            SampleRate = sampleRate;
        }

        /// <summary>Nombre de trames : un instant, tous canaux confondus.</summary>
        public int FrameCount => Samples.Length / Channels;

        public float DurationSeconds => (float)FrameCount / SampleRate;

        /// <summary>
        /// Echantillon MONO a une trame donnee.
        ///
        /// Un son de jeu spatialise doit etre mono : c'est le moteur qui
        /// decide de quel cote il sort. Un clip stereo porte deja sa propre
        /// repartition, qu'on ecraserait. On fait donc la moyenne des canaux -
        /// choix assume, et le seul qui ne perde pas d'energie sur un signal
        /// centre.
        /// </summary>
        public float MonoAt(int frame)
        {
            if (frame < 0 || frame >= FrameCount)
                return 0f;

            if (Channels == 1)
                return Samples[frame];

            float sum = 0f;
            int start = frame * Channels;

            for (int c = 0; c < Channels; c++)
                sum += Samples[start + c];

            return sum / Channels;
        }
    }
}
