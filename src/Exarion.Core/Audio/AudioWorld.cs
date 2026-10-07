// ===========================================================================
//  Exarion - Sons places dans le monde
//
//  Fait le lien entre des positions et le melangeur. Le partage des roles est
//  le point important :
//
//    FIL DE JEU   : calcule gain, panoramique, hauteur, occlusion - donc les
//                   distances, les produits scalaires et les lancers de rayon.
//    FIL AUDIO    : applique. Il ne connait ni le monde, ni l'auditeur.
//
//  C'est ce qui permet a l'occlusion de parcourir tout le decor sans jamais
//  risquer de bloquer la carte son.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Physics;

namespace Exarion.Core.Audio
{
    public sealed class AudioWorld
    {
        private struct Tracked
        {
            public int Handle;
            public Vector3 Position;
            public Vector3 Velocity;

            /// <summary>Volume propre du son, avant tout effet de distance.</summary>
            public float Gain;
        }

        private readonly AudioMixer _mixer;
        private readonly List<Tracked> _tracked;

        public AudioWorld(AudioMixer mixer)
        {
            _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));

            // Capacite reservee : ajouter un son ne doit pas faire grossir la
            // liste en plein tir.
            _tracked = new List<Tracked>(mixer.MaxVoices);
        }

        public AudioListener Listener = AudioListener.Default;

        public AudioFalloff Falloff = AudioFalloff.Default;

        /// <summary>Decor pour l'occlusion. Null = rien ne bloque le son.</summary>
        public CollisionWorld? World { get; set; }

        public bool OcclusionEnabled { get; set; } = true;

        public bool SpatializationEnabled { get; set; } = true;

        public int TrackedCount => _tracked.Count;

        /// <summary>Joue un son a un endroit du monde.</summary>
        public int PlayAt(AudioClip clip, Vector3 position, float gain = 1f,
                          bool loop = false, Vector3 velocity = default)
        {
            Resolve(position, velocity, gain, out float finalGain, out float pan,
                    out float pitch, out float cutoff);

            int handle = _mixer.Play(clip, finalGain, pan, loop, pitch, cutoff);

            if (handle < 0)
                return -1;

            // On ne suit que ce qui a besoin de l'etre. Un son ponctuel de
            // 150 ms n'a rien a gagner a etre recalcule : l'auditeur n'aura pas
            // bouge de facon perceptible, et le suivi couterait un lancer de
            // rayon par pas pour rien.
            if (loop || clip.DurationSeconds > 0.25f)
                _tracked.Add(new Tracked
                {
                    Handle = handle,
                    Position = position,
                    Velocity = velocity,
                    Gain = gain,
                });

            return handle;
        }

        /// <summary>Deplace une source suivie. Sans effet si elle a fini.</summary>
        public void MoveTo(int handle, Vector3 position, Vector3 velocity = default)
        {
            for (int i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i].Handle != handle)
                    continue;

                Tracked t = _tracked[i];
                t.Position = position;
                t.Velocity = velocity;
                _tracked[i] = t;
                return;
            }
        }

        public void Stop(int handle)
        {
            _mixer.Stop(handle);

            for (int i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i].Handle == handle)
                {
                    _tracked.RemoveAt(i);
                    return;
                }
            }
        }

        public void StopAll()
        {
            _mixer.StopAll();
            _tracked.Clear();
        }

        /// <summary>
        /// Recalcule toutes les sources suivies. A appeler au pas fixe, depuis
        /// le fil de jeu.
        /// </summary>
        public void Update()
        {
            // A L'ENVERS : on retire des elements en chemin, et un parcours
            // vers l'avant sauterait le suivant a chaque suppression.
            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                Tracked t = _tracked[i];

                if (!_mixer.IsPlaying(t.Handle))
                {
                    _tracked.RemoveAt(i);
                    continue;
                }

                Resolve(t.Position, t.Velocity, t.Gain, out float gain, out float pan,
                        out float pitch, out float cutoff);

                _mixer.Set(t.Handle, gain, pan, pitch, cutoff);
            }
        }

        private void Resolve(Vector3 position, Vector3 velocity, float baseGain,
                             out float gain, out float pan, out float pitch, out float cutoff)
        {
            if (!SpatializationEnabled)
            {
                gain = baseGain;
                pan = 0f;
                pitch = 1f;
                cutoff = 1f;
                return;
            }

            Spatializer.Compute(Listener, Falloff, position, velocity,
                                out float distanceGain, out pan, out pitch);

            float occlusion = OcclusionEnabled
                ? Spatializer.Occlusion(World, Listener.Position, position)
                : 0f;

            // Le mur fait DEUX choses : il etouffe (le filtre) et il attenue
            // (le gain). N'en faire qu'une donne soit un son lointain mais
            // clair, soit un son etouffe mais aussi fort - ni l'un ni l'autre
            // ne se lit comme "de l'autre cote d'une cloison".
            gain = baseGain * distanceGain * Spatializer.OcclusionGain(occlusion);
            cutoff = Spatializer.LowPassCoefficient(occlusion, _mixer.SampleRate);
        }
    }
}
