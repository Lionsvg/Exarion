// ===========================================================================
//  Le son du bac a sable (2.1, S3, S4)
//
//  Le melangeur vit dans Core et se teste en ligne de commande ; le
//  peripherique est la seule chose qui touche miniaudio. Cette classe les
//  assemble : carte son, melangeur, monde sonore, sons fabriques, et
//  l'enregistrement WAV qui sert de « capture d'ecran du son ».
// ===========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Exarion.Core.Audio;
using Exarion.Core.Entities;
using Exarion.Core.Physics;
using Exarion.Engine.Audio;

namespace Exarion.Game.Sound
{
    internal sealed class SandboxAudio
    {
        public AudioDevice? Device { get; private set; }
        public AudioMixer? Mixer { get; private set; }
        public AudioWorld? World { get; private set; }

        private AudioRecorder? _recorder;

        public AudioClip? ImpactClip { get; private set; }
        public AudioClip? BoomClip { get; private set; }
        public AudioClip? ToneClip { get; private set; }

        /// <summary>Le son pose par « snd_place », -1 s'il n'y en a pas.</summary>
        public int PlacedSound { get; set; } = -1;

        /// <summary>
        /// Ouvre la carte son et prepare les sons.
        ///
        /// L'ABSENCE DE CARTE SON NE DOIT PAS TUER LE MOTEUR. Une machine sans
        /// peripherique audio, une session distante, un service : le rendu et
        /// la physique n'ont aucune raison de s'arreter pour ca. On signale et
        /// on continue, exactement comme pour un modele 3D manquant.
        /// </summary>
        public void Start(CollisionWorld level, string? dumpPath, int dumpSeconds)
        {
            try
            {
                Device = new AudioDevice(Fill);

                // La FREQUENCE VIENT DE LA CARTE, elle ne se decide pas ici :
                // demander 48 000 sur un peripherique a 44 100 donne 44 100.
                // Construire le melangeur sur la valeur demandee ferait jouer
                // tous les sons trop aigus.
                Mixer = new AudioMixer(Device.SampleRate);

                // Le decor sert a l'occlusion : un rayon par source suivie et
                // par pas, sur le fil de JEU. Le fil audio ne parcourt jamais
                // la geometrie.
                World = new AudioWorld(Mixer) { World = level };

                ImpactClip = SoundGen.Impact(0.16f, Device.SampleRate, 0.7f, seed: 3u);
                BoomClip = SoundGen.Impact(0.9f, Device.SampleRate, 1f, seed: 11u);
                ToneClip = SoundGen.Tone(440f, 0.4f, Device.SampleRate);

                Console.WriteLine($"  Audio      : {Device.Name}, {Device.SampleRate} Hz, stereo.");

                if (dumpPath != null)
                {
                    _recorder = new AudioRecorder(dumpSeconds, Device.SampleRate);
                    Console.WriteLine($"  Enregistrement audio : {dumpSeconds} s vers {dumpPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Audio      : indisponible ({ex.Message})");
                Device = null;
                Mixer = null;
            }
        }

        /// <summary>
        /// Remplit le tampon de la carte son. APPELE SUR LE FIL AUDIO.
        ///
        /// Ni verrou, ni allocation, ni exception - voir AudioMixer.
        /// </summary>
        private void Fill(Span<float> buffer)
        {
            AudioMixer? mixer = Mixer;

            // Le rappel peut demarrer AVANT que le melangeur n'existe : le
            // peripherique est cree en premier, puisque c'est lui qui donne la
            // frequence. Sans ce garde-fou, la premiere image du jeu se joue
            // sur un champ nul.
            if (mixer is null)
            {
                buffer.Clear();
                return;
            }

            mixer.Mix(buffer);

            _recorder?.Write(buffer);
        }

        /// <summary>
        /// Le decor que l'occlusion doit parcourir.
        ///
        /// ⚠️⚠️ A RAPPELER A CHAQUE CHANGEMENT DE DECOR. La carte son demarre
        /// AVANT la premiere carte, sur un monde de collision encore vide ; et
        /// chaque chargement cree un NOUVEAU monde de collision. Sans cet
        /// appel, l'occlusion tirait ses rayons dans le monde vide du
        /// demarrage : aucun mur n'etouffait plus rien, sans que rien ne le
        /// dise. Mesure du 07/10 : un 6 kHz derriere un pilier donnait un RMS
        /// de 0,0299 occlusion ACTIVE et 0,0301 occlusion coupee.
        /// </summary>
        public void SetWorld(CollisionWorld level)
        {
            if (World != null)
                World.World = level;
        }

        // Une voix par son d'ambiance qui joue. La cle est l'OBJET : un
        // chargement de carte cree de nouveaux EnvSound, et les voix des
        // anciens doivent se taire - par identite, pas par nom.
        private readonly Dictionary<EnvSound, int> _envVoices = new();
        private readonly Dictionary<float, AudioClip> _beeps = new();
        private readonly List<EnvSound> _envGone = new();
        private int _envVersion = -1;

        /// <summary>
        /// Fait correspondre les voix a l'etat des env_sound : une voix en
        /// boucle pour chaque son qui joue, aucune pour les autres.
        ///
        /// Ne fait rien tant que la version de l'ambiance ne bouge pas : un
        /// son ne demarre ni ne s'arrete entre deux changements d'etat.
        /// </summary>
        public void SyncEnvSounds(EnvWorld env)
        {
            if (World is null || Device is null || env.Version == _envVersion)
                return;

            _envVersion = env.Version;

            // D'abord les voix des sons qui n'existent plus (autre carte).
            _envGone.Clear();

            foreach (EnvSound sound in _envVoices.Keys)
                if (!Contains(env.Sounds, sound))
                    _envGone.Add(sound);

            foreach (EnvSound sound in _envGone)
            {
                World.Stop(_envVoices[sound]);
                _envVoices.Remove(sound);
            }

            foreach (EnvSound sound in env.Sounds)
            {
                bool voiced = _envVoices.TryGetValue(sound, out int handle);

                if (sound.Playing && !voiced)
                {
                    int h = World.PlayAt(BeepFor(sound.Hz), sound.Position, sound.Volume, loop: true);

                    // Plus de voix libre : on le saura au prochain changement
                    // d'etat plutot que de reessayer a chaque pas.
                    if (h >= 0)
                        _envVoices[sound] = h;
                }
                else if (!sound.Playing && voiced)
                {
                    World.Stop(handle);
                    _envVoices.Remove(sound);
                }
            }
        }

        private static bool Contains(IReadOnlyList<EnvSound> list, EnvSound sound)
        {
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], sound))
                    return true;

            return false;
        }

        /// <summary>Un bip par hauteur, fabrique une fois : 0,25 s de son, 0,25 s de silence.</summary>
        private AudioClip BeepFor(float hz)
        {
            if (!_beeps.TryGetValue(hz, out AudioClip? clip))
            {
                clip = SoundGen.Beep(hz, 0.25f, 0.25f, Device!.SampleRate);
                _beeps[hz] = clip;
            }

            return clip;
        }

        /// <summary>Joue un son, sans rien faire s'il n'y a pas de carte.</summary>
        public void Play(AudioClip? clip, float gain = 1f, float pan = 0f)
        {
            if (clip != null)
                Mixer?.Play(clip, gain, pan);
        }

        /// <summary>Joue un son A UN ENDROIT du monde : distance, cote, occlusion.</summary>
        public void PlayAt(AudioClip? clip, Vector3 position, float gain = 1f)
        {
            if (clip != null)
                World?.PlayAt(clip, position, gain);
        }

        /// <summary>
        /// Recale l'auditeur, puis met a jour les sources.
        ///
        /// ⚠️ Appele au PAS FIXE et non au rendu : les lancers de rayon
        /// d'occlusion couteraient sinon deux a trois fois plus cher a
        /// 3 000 images par seconde, pour un resultat identique - une oreille
        /// ne distingue pas deux positions separees de 15 millisecondes.
        /// </summary>
        public void UpdateListener(Vector3 eye, Vector3 forward, Vector3 right, Vector3 velocity)
        {
            if (World is null)
                return;

            World.Listener = new AudioListener
            {
                Position = eye,
                Forward = forward,
                Right = right,
                Velocity = velocity,
            };

            World.Update();
        }

        /// <summary>
        /// Ferme la carte son, puis ecrit l'enregistrement s'il y en a un.
        ///
        /// La carte son S'ARRETE AVANT qu'on ecrive le fichier. Sauvegarder
        /// pendant que le rappel ecrit encore donnerait un compte qui bouge
        /// sous les pieds du lecteur.
        /// </summary>
        public void Shutdown(string? dumpPath)
        {
            Device?.Dispose();
            Device = null;

            if (_recorder != null && dumpPath != null)
            {
                try
                {
                    _recorder.Save(dumpPath);

                    Console.WriteLine($"  Audio : {_recorder.Seconds:0.00} s ecrites dans " +
                                      $"{Path.GetFullPath(dumpPath)}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("  Enregistrement audio impossible : " + ex.Message);
                }
            }
        }
    }
}
