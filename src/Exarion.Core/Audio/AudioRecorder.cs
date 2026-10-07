// ===========================================================================
//  Exarion - Enregistreur de la sortie audio
//
//  C'est la capture d'ecran du son. Elle sert deux publics :
//
//   - MOI, qui ne peux pas ecouter. Un WAV se mesure : amplitude, equilibre
//     gauche/droite, decroissance avec la distance, contenu spectral.
//   - TOI, qui peux ouvrir le fichier et entendre exactement ce que la carte
//     a recu, sans avoir a reproduire la situation en jouant.
//
//  ⚠️ Write est appele SUR LE FIL AUDIO. D'ou le tampon de taille fixe,
//  alloue une fois : allouer ici provoquerait la coupure qu'on essaie
//  justement d'enregistrer. Quand c'est plein, on s'arrete - on n'agrandit
//  pas, et on ne recycle pas.
// ===========================================================================

using System;
using System.IO;
using System.Threading;

namespace Exarion.Core.Audio
{
    public sealed class AudioRecorder
    {
        private readonly float[] _buffer;
        private int _count;

        public AudioRecorder(int seconds, int sampleRate, int channels = AudioMixer.Channels)
        {
            if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));

            SampleRate = sampleRate;
            Channels = channels;

            _buffer = new float[seconds * sampleRate * channels];
        }

        public int SampleRate { get; }

        public int Channels { get; }

        public int SampleCount => Volatile.Read(ref _count);

        public bool Full => SampleCount >= _buffer.Length;

        public float Seconds => (float)SampleCount / (SampleRate * Channels);

        /// <summary>Ajoute un tampon. APPELE SUR LE FIL AUDIO.</summary>
        public void Write(ReadOnlySpan<float> samples)
        {
            int count = Volatile.Read(ref _count);
            int room = _buffer.Length - count;

            if (room <= 0)
                return;

            int take = Math.Min(room, samples.Length);

            samples[..take].CopyTo(_buffer.AsSpan(count));

            // Le compteur monte APRES la copie : le fil de jeu qui lit
            // SampleCount ne verra jamais des echantillons pas encore ecrits.
            Volatile.Write(ref _count, count + take);
        }

        /// <summary>
        /// Ecrit le WAV. A appeler depuis le fil de JEU, apres avoir arrete
        /// l'enregistrement - jamais depuis le rappel audio, ou une ecriture
        /// disque bloquerait la carte son.
        /// </summary>
        public void Save(string path)
            => File.WriteAllBytes(path, Wav.Encode(_buffer.AsSpan(0, SampleCount), Channels, SampleRate));
    }
}
