// ===========================================================================
//  Tests de la spatialisation (S3) et de l'occlusion (S4).
//
//  Meme remarque qu'AudioTests : je ne peux pas ecouter, donc ce fichier est
//  la seule barriere. Ce qu'on verifie ici, ce sont les proprietes qu'un
//  humain entendrait immediatement mais qu'aucune assertion de structure ne
//  voit :
//
//   - un son a droite doit sortir a droite, et pas l'inverse ;
//   - il doit faiblir quand on s'eloigne, sans jamais exploser quand on lui
//     marche dessus ;
//   - une source qui approche doit monter en hauteur, pas descendre ;
//   - un mur doit couper les AIGUS, pas seulement le volume.
// ===========================================================================

using System;
using System.Numerics;
using NUnit.Framework;
using Exarion.Core.Audio;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class SpatialAudioTests
    {
        /// <summary>Auditeur a l'origine, regardant vers -Z, droite vers +X.</summary>
        private static AudioListener Ears(Vector3 velocity = default) => new()
        {
            Position = Vector3.Zero,
            Forward = -Vector3.UnitZ,
            Right = Vector3.UnitX,
            Velocity = velocity,
        };

        // ===================================================================
        //  Direction
        // ===================================================================

        [Test]
        public void Un_Son_A_Droite_Sort_A_Droite()
        {
            // Le signe du panoramique est le genre d'erreur qui ne casse rien,
            // ne leve rien, et rend le jeu injouable : on entend l'ennemi du
            // mauvais cote.
            AudioFalloff falloff = AudioFalloff.Default;

            Spatializer.Compute(Ears(), falloff, new Vector3(10f, 0f, 0f), default,
                                out _, out float right, out _);

            Spatializer.Compute(Ears(), falloff, new Vector3(-10f, 0f, 0f), default,
                                out _, out float left, out _);

            Assert.That(right, Is.GreaterThan(0.9f), "une source a +X doit sortir a droite");
            Assert.That(left, Is.LessThan(-0.9f), "une source a -X doit sortir a gauche");
        }

        [Test]
        public void Un_Son_Droit_Devant_Est_Centre()
        {
            Spatializer.Compute(Ears(), AudioFalloff.Default, new Vector3(0f, 0f, -10f), default,
                                out _, out float pan, out _);

            ClassicAssert.AreEqual(0f, pan, 1e-4f);
        }

        [Test]
        public void L_Avant_Et_L_Arriere_Sont_INDISTINGUABLES()
        {
            // ⚠️ Ce test ne verifie pas que ca marche : il FIGE une limite
            // connue, pour qu'elle ne soit pas redecouverte comme un bug.
            //
            // Le panoramique d'amplitude n'a que deux haut-parleurs et une
            // seule information, gauche/droite. Une source devant et une
            // source derriere donnent rigoureusement le meme resultat.
            //
            // Les distinguer demande une HRTF. C'est une limite REELLE pour un
            // FPS tactique - savoir si l'ennemi est devant ou derriere est une
            // information de jeu.
            AudioFalloff falloff = AudioFalloff.Default;

            Spatializer.Compute(Ears(), falloff, new Vector3(0f, 0f, -10f), default,
                                out float frontGain, out float frontPan, out _);

            Spatializer.Compute(Ears(), falloff, new Vector3(0f, 0f, 10f), default,
                                out float backGain, out float backPan, out _);

            ClassicAssert.AreEqual(frontPan, backPan, 1e-5f, "limite connue : pas de front/arriere");
            ClassicAssert.AreEqual(frontGain, backGain, 1e-5f);
        }

        [Test]
        public void Une_Source_Collee_A_L_Oreille_Est_Recentree()
        {
            // Sans ce recentrage, une source a dix centimetres saute d'une
            // enceinte a l'autre au moindre mouvement de tete : la direction
            // change du tout au tout pour un deplacement minuscule.
            var falloff = AudioFalloff.Default;   // reference = 1,5 m

            Spatializer.Compute(Ears(), falloff, new Vector3(0.05f, 0f, 0f), default,
                                out _, out float close, out _);

            Spatializer.Compute(Ears(), falloff, new Vector3(5f, 0f, 0f), default,
                                out _, out float far, out _);

            Assert.That(MathF.Abs(close), Is.LessThan(0.1f), "trop pres pour etre lateralise");
            Assert.That(far, Is.GreaterThan(0.9f));
        }

        // ===================================================================
        //  Distance
        // ===================================================================

        [Test]
        public void Le_Son_Faiblit_Avec_La_Distance()
        {
            var falloff = AudioFalloff.Default;

            float previous = float.MaxValue;

            foreach (float d in new[] { 0f, 1.5f, 3f, 6f, 12f, 30f, 60f })
            {
                float gain = Spatializer.Attenuation(d, falloff);

                Assert.That(gain, Is.LessThanOrEqualTo(previous + 1e-6f), $"remonte a {d} m");
                Assert.That(gain, Is.InRange(0f, 1f));

                previous = gain;
            }

            // A la distance de reference, plein volume ; au double du rayon de
            // reference, la moitie. C'est la definition du modele inverse.
            ClassicAssert.AreEqual(1f, Spatializer.Attenuation(1.5f, falloff), 1e-4f);
            ClassicAssert.AreEqual(0.5f, Spatializer.Attenuation(3f, falloff), 1e-4f);
        }

        [Test]
        public void Une_Source_Sur_L_Auditeur_N_Explose_Pas()
        {
            // ⚠️ Sans borne basse, le modele inverse divise par zero : gain
            // infini, donc un craquement a plein volume. Et ce n'est pas un cas
            // theorique - c'est ce qui arrive des qu'on joue un son a la
            // position du joueur, ce que fait tout jeu pour ses propres pas.
            float gain = Spatializer.Attenuation(0f, AudioFalloff.Default);

            Assert.That(float.IsFinite(gain), Is.True);
            ClassicAssert.AreEqual(1f, gain, 1e-4f);
        }

        [Test]
        public void Au_Dela_Du_Maximum_Le_Son_Ne_Disparait_Pas()
        {
            var falloff = AudioFalloff.Default;   // maximum = 60 m

            float atMax = Spatializer.Attenuation(60f, falloff);
            float far = Spatializer.Attenuation(5000f, falloff);

            ClassicAssert.AreEqual(atMax, far, 1e-6f);
            Assert.That(far, Is.GreaterThan(0f), "un plancher tenu vaut mieux qu'un silence");
        }

        // ===================================================================
        //  Doppler
        // ===================================================================

        [Test]
        public void Une_Source_Qui_Approche_Monte_En_Hauteur()
        {
            var falloff = AudioFalloff.Default;

            // Source devant, a -Z, qui fonce vers nous : sa vitesse est vers +Z.
            var source = new Vector3(0f, 0f, -30f);

            Spatializer.Compute(Ears(), falloff, source, new Vector3(0f, 0f, 30f),
                                out _, out _, out float approaching);

            Spatializer.Compute(Ears(), falloff, source, new Vector3(0f, 0f, -30f),
                                out _, out _, out float leaving);

            Assert.That(approaching, Is.GreaterThan(1.05f), "elle approche, ca doit monter");
            Assert.That(leaving, Is.LessThan(0.95f), "elle s'eloigne, ca doit descendre");
        }

        [Test]
        public void Un_Auditeur_Qui_Se_Deplace_Entend_Aussi_Le_Doppler()
        {
            // La moitie de l'effet est oubliee si l'on ne regarde que la
            // source. Un joueur qui court vers une sirene immobile doit
            // l'entendre monter.
            var source = new Vector3(0f, 0f, -30f);

            Spatializer.Compute(Ears(new Vector3(0f, 0f, -30f)), AudioFalloff.Default,
                                source, default, out _, out _, out float pitch);

            Assert.That(pitch, Is.GreaterThan(1.05f));
        }

        [Test]
        public void Une_Vitesse_Absurde_Ne_Fait_Pas_Exploser_La_Hauteur()
        {
            // A la vitesse du son, la formule diverge. Une valeur aberrante
            // - un corps ejecte par une explosion, un pas de simulation rate -
            // suffit a y arriver.
            Spatializer.Compute(Ears(), AudioFalloff.Default, new Vector3(0f, 0f, -10f),
                                new Vector3(0f, 0f, 100000f), out _, out _, out float pitch);

            Assert.That(float.IsFinite(pitch), Is.True);
            Assert.That(pitch, Is.LessThan(20f), $"hauteur de {pitch:0.0}");
        }

        [Test]
        public void Le_Doppler_Se_Desactive()
        {
            var falloff = AudioFalloff.Default;
            falloff.DopplerScale = 0f;

            Spatializer.Compute(Ears(), falloff, new Vector3(0f, 0f, -30f),
                                new Vector3(0f, 0f, 100f), out _, out _, out float pitch);

            ClassicAssert.AreEqual(1f, pitch, 1e-6f);
        }

        // ===================================================================
        //  Occlusion
        // ===================================================================

        [Test]
        public void Un_Mur_Entre_Les_Deux_Est_Detecte()
        {
            var world = new CollisionWorld();
            world.AddBox(new Vector3(0f, 0f, -5f), new Vector3(10f, 6f, 0.5f));

            float blocked = Spatializer.Occlusion(world, Vector3.Zero, new Vector3(0f, 0f, -10f));
            float clear = Spatializer.Occlusion(world, Vector3.Zero, new Vector3(0f, 0f, 10f));

            ClassicAssert.AreEqual(1f, blocked, 1e-6f, "le mur devait bloquer");
            ClassicAssert.AreEqual(0f, clear, 1e-6f, "de l'autre cote, rien ne bloque");
        }

        [Test]
        public void Un_Mur_DERRIERE_La_Source_Ne_Bloque_Rien()
        {
            // Le rayon doit s'arreter A la source. S'il continue au-dela, tout
            // son ayant un mur quelque part derriere lui serait etouffe -
            // c'est-a-dire presque tous, dans un couloir.
            var world = new CollisionWorld();
            world.AddBox(new Vector3(0f, 0f, -20f), new Vector3(10f, 6f, 0.5f));

            float occlusion = Spatializer.Occlusion(world, Vector3.Zero, new Vector3(0f, 0f, -10f));

            ClassicAssert.AreEqual(0f, occlusion, 1e-6f);
        }

        [Test]
        public void Le_Filtre_D_Occlusion_Coupe_Vraiment_Les_Aigus()
        {
            // ⚠️ LE test de S4. Un mur ne doit pas seulement baisser le volume :
            // il doit enlever les AIGUS. C'est ca que l'oreille interprete comme
            // "de l'autre cote d'une cloison" ; un simple baisser de volume
            // donne un son LOINTAIN, ce qui n'a rien a voir.
            //
            // On mesure sur deux sinusoides : une grave qui doit passer, une
            // aigue qui doit etre coupee.
            const int rate = 48000;

            float open = Spatializer.LowPassCoefficient(0f, rate);
            float blocked = Spatializer.LowPassCoefficient(1f, rate);

            ClassicAssert.AreEqual(1f, open, 1e-6f, "sans mur, pas de filtre");
            Assert.That(blocked, Is.LessThan(0.15f), "le filtre est trop ouvert pour etouffer");

            float lowKept = ThroughFilter(SoundGen.Tone(200f, 0.3f, rate, 1f, 0.001f), blocked);
            float highKept = ThroughFilter(SoundGen.Tone(8000f, 0.3f, rate, 1f, 0.001f), blocked);

            Assert.That(lowKept, Is.GreaterThan(0.5f), $"les graves doivent passer ({lowKept:0.00})");
            Assert.That(highKept, Is.LessThan(0.15f), $"les aigus doivent tomber ({highKept:0.00})");

            // Et le rapport est ce qui compte : c'est LUI qu'on entend.
            Assert.That(lowKept / highKept, Is.GreaterThan(5f));
        }

        [Test]
        public void Le_Mur_Attenue_Aussi_Le_Volume()
        {
            ClassicAssert.AreEqual(1f, Spatializer.OcclusionGain(0f), 1e-6f);
            Assert.That(Spatializer.OcclusionGain(1f), Is.LessThan(0.6f));
        }

        // ===================================================================
        //  Monde sonore
        // ===================================================================

        [Test]
        public void Une_Source_Suivie_Se_Met_A_Jour_Quand_L_Auditeur_Tourne()
        {
            var mixer = new AudioMixer(48000);
            var world = new AudioWorld(mixer);

            AudioClip clip = SoundGen.Tone(440f, 5f, 48000);

            int handle = world.PlayAt(clip, new Vector3(10f, 0f, 0f), loop: true);
            Assert.That(handle, Is.GreaterThanOrEqualTo(0));
            ClassicAssert.AreEqual(1, world.TrackedCount);

            var output = new float[64 * AudioMixer.Channels];
            mixer.Mix(output);

            float rightFirst = Energy(output, 1);
            Assert.That(rightFirst, Is.GreaterThan(Energy(output, 0)), "la source est a droite");

            // L'auditeur fait demi-tour : sa droite pointe maintenant vers -X.
            AudioListener ears = world.Listener;
            ears.Right = -Vector3.UnitX;
            ears.Forward = Vector3.UnitZ;
            world.Listener = ears;

            world.Update();
            mixer.Mix(output);

            Assert.That(Energy(output, 0), Is.GreaterThan(Energy(output, 1)),
                        "apres demi-tour, la source doit passer a gauche");
        }

        [Test]
        public void Une_Source_Terminee_Cesse_D_Etre_Suivie()
        {
            var mixer = new AudioMixer(48000);
            var world = new AudioWorld(mixer);

            world.PlayAt(SoundGen.Tone(440f, 0.5f, 48000), new Vector3(5f, 0f, 0f));
            ClassicAssert.AreEqual(1, world.TrackedCount);

            // De quoi epuiser largement le clip.
            for (int i = 0; i < 40; i++)
                mixer.Mix(new float[1024 * AudioMixer.Channels]);

            world.Update();

            ClassicAssert.AreEqual(0, world.TrackedCount, "la source devait etre oubliee");
        }

        [Test]
        public void Un_Son_Bref_N_Est_Pas_Suivi_Du_Tout()
        {
            // Suivre un son de 150 ms couterait un lancer de rayon par pas
            // pour un resultat que personne n'entendrait : l'auditeur n'a pas
            // le temps de bouger.
            var mixer = new AudioMixer(48000);
            var world = new AudioWorld(mixer);

            world.PlayAt(SoundGen.Impact(0.15f, 48000), new Vector3(5f, 0f, 0f));

            ClassicAssert.AreEqual(0, world.TrackedCount);
            ClassicAssert.AreEqual(1, mixer.ActiveVoices, "il doit quand meme jouer");
        }

        // ===================================================================

        /// <summary>Amplitude qui subsiste apres le passe-bas, rapportee a l'entree.</summary>
        private static float ThroughFilter(AudioClip clip, float coefficient)
        {
            float state = 0f;
            float inPeak = 0f, outPeak = 0f;

            // On ignore le premier dixieme : le filtre part de zero et met
            // quelques echantillons a s'etablir.
            int skip = clip.Samples.Length / 10;

            for (int i = 0; i < clip.Samples.Length; i++)
            {
                float x = clip.Samples[i];
                state += (x - state) * coefficient;

                if (i < skip)
                    continue;

                inPeak = MathF.Max(inPeak, MathF.Abs(x));
                outPeak = MathF.Max(outPeak, MathF.Abs(state));
            }

            return inPeak > 0f ? outPeak / inPeak : 0f;
        }

        private static float Energy(float[] interleaved, int channel)
        {
            float sum = 0f;

            for (int i = channel; i < interleaved.Length; i += AudioMixer.Channels)
                sum += interleaved[i] * interleaved[i];

            return sum;
        }
    }
}
