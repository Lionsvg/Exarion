// ===========================================================================
//  Exarion - Peripherique de sortie audio
//
//  LA SEULE CLASSE DU PROJET QUI TOUCHE miniaudio. Elle ouvre une carte son,
//  et rien de plus : toutes les quelques millisecondes le pilote reclame un
//  tampon, et on le fait remplir par le melangeur, qui lui vit dans
//  Exarion.Core et se teste en ligne de commande.
//
//  C'est exactement le partage qu'on a avec Silk.NET pour le rendu : la
//  bibliotheque tierce ouvre la fenetre et parle au GPU, tout ce qui merite
//  d'etre pense est ecrit ici.
//
//  ---------------------------------------------------------------------
//  ⚠️ LE RAPPEL VIENT DU CODE NATIF, SUR UN AUTRE FIL.
//
//  Trois consequences, toutes obligatoires :
//
//   1. La fonction de rappel est [UnmanagedCallersOnly] et STATIQUE. Un
//      delegue ordinaire fonctionnerait, mais il faut alors le garder en vie
//      soi-meme : si le ramasse-miettes le collecte, le pilote appelle une
//      adresse morte et le processus meurt sans message.
//
//   2. On retrouve l'instance par pUserData, le champ que miniaudio nous
//      rend tel quel. Un GCHandle epingle l'objet et donne une adresse
//      stable qui traverse la frontiere native.
//
//   3. TOUT est sous try/catch. Une exception qui remonte dans du code natif
//      ne se rattrape pas : elle tue le processus. C'est le meme piege que
//      les gestionnaires de ConVar, deja rencontre.
//
//  ⚠️ La structure MaDevice est allouee en memoire NON GEREE. miniaudio en
//  garde l'adresse pour toute la duree de vie du peripherique ; un champ
//  d'objet gere pourrait etre deplace par un compactage du tas.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hexa.NET.MiniAudio;

namespace Exarion.Engine.Audio
{
    public sealed unsafe class AudioDevice : IDisposable
    {
        private readonly MaDevice* _device;
        private GCHandle _self;

        private readonly Action<Span<float>> _fill;

        private long _callbacks;
        private long _faults;

        /// <summary>
        /// Ouvre la carte son par defaut.
        /// </summary>
        /// <param name="fill">
        /// Remplit un tampon stereo entrelace. APPELE SUR LE FIL AUDIO :
        /// ni verrou, ni allocation, ni exception. Voir AudioMixer.
        /// </param>
        public AudioDevice(Action<Span<float>> fill, int sampleRate = 48000)
        {
            _fill = fill ?? throw new ArgumentNullException(nameof(fill));

            SampleRate = sampleRate;

            MaDeviceConfig config = MiniAudio.DeviceConfigInit(MaDeviceType.Playback);

            // Flottant 32 bits : c'est deja le format du melangeur, donc
            // aucune conversion. En 16 bits il faudrait convertir a chaque
            // tampon, et surtout on perdrait la marge au-dessus de 1,0 avant
            // le bornage final.
            config.Playback.Format = MaFormat.F32;
            config.Playback.Channels = 2;
            config.SampleRate = (uint)sampleRate;

            config.DataCallback = (void*)(delegate* unmanaged[Cdecl]<MaDevice*, void*, void*, uint, void>)&OnData;

            _self = GCHandle.Alloc(this);
            config.PUserData = (void*)GCHandle.ToIntPtr(_self);

            _device = (MaDevice*)NativeMemory.AllocZeroed((nuint)sizeof(MaDevice));

            MaResult result = MiniAudio.DeviceInit(null, &config, _device);

            if (result != MaResult.Success)
            {
                NativeMemory.Free(_device);
                _device = null;
                _self.Free();

                throw new InvalidOperationException($"Ouverture de la carte son impossible : {result}");
            }

            // La carte impose SA frequence, elle ne la subit pas : demander
            // 48 000 sur un peripherique a 44 100 donne 44 100. Si on gardait
            // la valeur demandee, tous les sons joueraient trop aigus.
            SampleRate = (int)_device->SampleRate;

            Name = ReadName();

            MaResult started = MiniAudio.DeviceStart(_device);

            if (started != MaResult.Success)
            {
                Dispose();
                throw new InvalidOperationException($"Demarrage de la carte son impossible : {started}");
            }

            IsRunning = true;
        }

        /// <summary>Frequence REELLE du peripherique, pas celle demandee.</summary>
        public int SampleRate { get; private set; }

        public string Name { get; private set; } = "?";

        public bool IsRunning { get; private set; }

        /// <summary>Nombre de tampons remplis. Zero apres une seconde = rien ne sort.</summary>
        public long Callbacks => Interlocked.Read(ref _callbacks);

        /// <summary>Exceptions rattrapees dans le rappel. Doit rester a zero.</summary>
        public long Faults => Interlocked.Read(ref _faults);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        private static void OnData(MaDevice* device, void* output, void* input, uint frameCount)
        {
            if (device is null || output is null)
                return;

            var handle = GCHandle.FromIntPtr((IntPtr)device->PUserData);

            if (handle.Target is not AudioDevice self)
                return;

            self.Fill(output, frameCount);
        }

        private void Fill(void* output, uint frameCount)
        {
            Interlocked.Increment(ref _callbacks);

            try
            {
                _fill(new Span<float>(output, (int)frameCount * 2));
            }
            catch
            {
                // On compte et on se tait. Laisser filer l'exception vers le
                // code natif tuerait le processus sans trace ; ecrire dans la
                // console depuis le fil audio provoquerait exactement le
                // blocage qu'on cherche a eviter.
                Interlocked.Increment(ref _faults);

                new Span<float>(output, (int)frameCount * 2).Clear();
            }
        }

        private string ReadName()
        {
            const int capacity = 256;
            byte* buffer = stackalloc byte[capacity];
            nuint length;

            MaResult result = MiniAudio.DeviceGetName(_device, MaDeviceType.Playback,
                                                      buffer, capacity, &length);

            if (result != MaResult.Success || length == 0)
                return "peripherique par defaut";

            return System.Text.Encoding.UTF8.GetString(buffer, (int)Math.Min(length, capacity - 1));
        }

        public void Dispose()
        {
            if (_device is not null)
            {
                MiniAudio.DeviceUninit(_device);
                NativeMemory.Free(_device);
            }

            if (_self.IsAllocated)
                _self.Free();

            IsRunning = false;
        }
    }
}
