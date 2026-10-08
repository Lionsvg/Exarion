// ===========================================================================
//  Console : le son (2.1, S3, S4)
// ===========================================================================

using System;
using System.Numerics;
using Exarion.Core.Audio;
using Exarion.Core.Console;
using Exarion.Game.Sound;

namespace Exarion.Game.Commands
{
    internal static class AudioCommands
    {
        /// <param name="aimPoint">Point vise au sol : la ou « snd_place » pose sa source.</param>
        public static void Register(ConsoleSystem c, SandboxAudio audio, Func<Vector3> aimPoint)
        {
            // ⚠️ La valeur initiale doit COINCIDER avec le champ d'AudioMixer.
            // Le setter n'est appele qu'au changement : avec 0,7 ici et 1,0
            // dans le melangeur, la console affichait 0,7 et le son sortait a
            // 1,0. Piege deja rencontre avec les ConVar de la SSAO, et attrape
            // ici par la MESURE du fichier WAV - pic de 0,30 la ou 0,7 de
            // volume en aurait donne 0,21.
            c.Bind("snd_volume", 1f, 0f, 1f, "Volume general",
                   x => { if (audio.Mixer != null) audio.Mixer.MasterGain = x; });

            c.Register("snd", _ =>
            {
                if (audio.Device is null || audio.Mixer is null)
                {
                    c.Print("  audio indisponible");
                    return;
                }

                c.Print($"  {audio.Device.Name}, {audio.Device.SampleRate} Hz");
                c.Print($"  {audio.Device.Callbacks} tampons remplis, {audio.Device.Faults} erreurs");
                c.Print($"  {audio.Mixer.ActiveVoices} / {audio.Mixer.MaxVoices} voix, " +
                        $"{audio.Mixer.Dropped} refusees, pic {audio.Mixer.LastPeak:0.00}");
            }, "Etat de la carte son et du melangeur");

            c.Register("snd_test", args =>
            {
                float frequency = args.Length >= 1 ? CommandHelpers.ParseFloat(args[0], 440f) : 440f;
                float pan = args.Length >= 2 ? CommandHelpers.ParseFloat(args[1], 0f) : 0f;

                if (audio.Device is null) { c.Print("  audio indisponible"); return; }

                audio.Play(SoundGen.Tone(frequency, 0.5f, audio.Device.SampleRate), 0.6f, pan);
                c.Print($"  note a {frequency:0} Hz, panoramique {pan:0.00}");
            }, "Joue une note. snd_test [hz] [panoramique -1..1]");

            c.Register("snd_impact", args =>
            {
                float pan = args.Length >= 1 ? CommandHelpers.ParseFloat(args[0], 0f) : 0f;
                audio.Play(audio.ImpactClip, 0.9f, pan);
                c.Print($"  impact, panoramique {pan:0.00}");
            }, "Joue un impact. snd_impact [panoramique]");

            // --- Spatialisation (S3) et occlusion (S4) ---
            c.Bind("snd_spatial", 1f, 0f, 1f, "Spatialiser les sons du monde",
                   x => { if (audio.World != null) audio.World.SpatializationEnabled = x >= 0.5f; });
            c.Bind("snd_occlusion", 1f, 0f, 1f, "Etouffer les sons derriere un mur",
                   x => { if (audio.World != null) audio.World.OcclusionEnabled = x >= 0.5f; });
            c.Bind("snd_ref", 1.5f, 0.1f, 20f, "Distance en deca de laquelle le son est a plein volume",
                   x => { if (audio.World is { } w) { var f = w.Falloff; f.ReferenceDistance = x; w.Falloff = f; } });
            c.Bind("snd_max", 60f, 5f, 500f, "Distance au-dela de laquelle le son ne faiblit plus",
                   x => { if (audio.World is { } w) { var f = w.Falloff; f.MaxDistance = x; w.Falloff = f; } });
            c.Bind("snd_rolloff", 1f, 0f, 5f, "Raideur de l'attenuation. 1 = physique",
                   x => { if (audio.World is { } w) { var f = w.Falloff; f.Rolloff = x; w.Falloff = f; } });
            c.Bind("snd_doppler", 1f, 0f, 4f, "Force de l'effet Doppler. 0 le desactive",
                   x => { if (audio.World is { } w) { var f = w.Falloff; f.DopplerScale = x; w.Falloff = f; } });

            // Un son qui BOUCLE, pose dans le monde. C'est le seul moyen de
            // juger la spatialisation a l'oreille : il faut pouvoir tourner
            // autour et s'en eloigner.
            c.Register("snd_place", args =>
            {
                if (audio.World is null || audio.Device is null) { c.Print("  audio indisponible"); return; }

                audio.World.Stop(audio.PlacedSound);

                Vector3 at = CommandHelpers.ParsePoint(args, aimPoint() + new Vector3(0f, 1f, 0f));

                // La frequence est reglable : c'est indispensable pour VERIFIER
                // l'occlusion, qui coupe les aigus. Un grave passerait le mur
                // presque intact et ne prouverait rien.
                float hz = args.Length >= 4 ? CommandHelpers.ParseFloat(args[3], 330f) : 330f;

                audio.PlacedSound = audio.World.PlayAt(SoundGen.Tone(hz, 2f, audio.Device.SampleRate, 0.5f, 0.15f),
                                                       at, gain: 1f, loop: true);

                c.Print($"  source a {hz:0} Hz posee en ({at.X:0.##}, {at.Y:0.##}, {at.Z:0.##})");
            }, "Pose un son en boucle. snd_place [x y z] [hz]");

            c.Register("snd_stop", _ =>
            {
                audio.World?.Stop(audio.PlacedSound);
                audio.PlacedSound = -1;
                c.Print("  source arretee");
            }, "Arrete le son pose");
        }
    }
}
