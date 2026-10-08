// ===========================================================================
//  Tests de l'audio (2.1, S1 et S2).
//
//  ⚠️ CE FICHIER COMPTE PLUS QUE LES AUTRES, parce que je ne peux PAS
//  ecouter. Pour tout le reste, une capture d'ecran rattrapait ce qu'un test
//  laissait passer ; ici il n'y a rien derriere. Un defaut qu'aucun test
//  n'attrape ne sera trouve qu'a l'oreille, par quelqu'un d'autre.
//
//  Les pieges couverts, tous silencieux :
//   - le debordement a l'ecriture d'un WAV, qui transforme un son fort en
//     craquement violent ;
//   - un morceau intercalaire dans le fichier, qui fait lire du texte comme
//     du son ;
//   - le panoramique lineaire, qui affaiblit tout ce qui passe au centre ;
//   - le reechantillonnage oublie, qui rend tous les sons trop aigus ;
//   - un identifiant de voix perime, qui coupe le son d'un autre ;
//   - une sinusoide sans enveloppe, qui claque a chaque note.
// ===========================================================================

using System;
using NUnit.Framework;
using Exarion.Core.Audio;

namespace Exarion.Tests
{
    public class AudioTests
    {
        // ===================================================================
        //  WAV
        // ===================================================================

        [Test]
        public void Un_Aller_Retour_Wav_Restitue_Le_Son()
        {
            float[] source = { 0f, 0.5f, -0.5f, 0.25f, -0.25f, 1f, -1f, 0.125f };

            AudioClip clip = Wav.Decode(Wav.Encode(source, 2, 44100));

            ClassicAssert.AreEqual(2, clip.Channels);
            ClassicAssert.AreEqual(44100, clip.SampleRate);
            ClassicAssert.AreEqual(source.Length, clip.Samples.Length);

            for (int i = 0; i < source.Length; i++)
                ClassicAssert.AreEqual(source[i], clip.Samples[i], 1e-4f, $"echantillon {i}");
        }

        [Test]
        public void Un_Son_Trop_Fort_Sature_Au_Lieu_De_Deborder()
        {
            // ⚠️ LE piege de l'ecriture WAV. Un melange de plusieurs sons
            // depasse facilement 1,0. Sans bornage, la conversion vers l'entier
            // 16 bits DEBORDE : +1,5 devient un grand NEGATIF, et le signal
            // saute d'un extreme a l'autre a chaque echantillon. On n'entend
            // pas un son sature, on entend une explosion de bruit.
            float[] source = { 1.5f, -1.5f, 3f, -3f };

            AudioClip clip = Wav.Decode(Wav.Encode(source, 1, 48000));

            ClassicAssert.AreEqual(1f, clip.Samples[0], 1e-3f);
            ClassicAssert.AreEqual(-1f, clip.Samples[1], 1e-3f);
            ClassicAssert.AreEqual(1f, clip.Samples[2], 1e-3f);
            ClassicAssert.AreEqual(-1f, clip.Samples[3], 1e-3f);
        }

        [Test]
        public void Un_Morceau_Intercalaire_Ne_Fait_Pas_Lire_N_Importe_Quoi()
        {
            // Beaucoup d'outils glissent un morceau "LIST" - le nom du logiciel,
            // la date - entre le format et les donnees. Un lecteur qui saute a
            // l'octet 44 lit alors ce TEXTE comme du son et rend du bruit.
            byte[] normal = Wav.Encode(new[] { 0.5f, -0.5f, 0.25f, -0.25f }, 1, 48000);

            byte[] listChunk = System.Text.Encoding.ASCII.GetBytes("LIST")
                .Concat(BitConverter.GetBytes(8u))
                .Concat(System.Text.Encoding.ASCII.GetBytes("INFOtest"))
                .ToArray();

            // On insere juste apres le morceau "fmt " (qui finit a l'octet 36).
            byte[] patched = normal[..36].Concat(listChunk).Concat(normal[36..]).ToArray();

            // La taille RIFF doit suivre, sinon le fichier est invalide.
            BitConverter.GetBytes((uint)(patched.Length - 8)).CopyTo(patched, 4);

            AudioClip clip = Wav.Decode(patched);

            ClassicAssert.AreEqual(4, clip.Samples.Length);
            ClassicAssert.AreEqual(0.5f, clip.Samples[0], 1e-3f);
            ClassicAssert.AreEqual(-0.25f, clip.Samples[3], 1e-3f);
        }

        [Test]
        public void Un_Fichier_Qui_N_Est_Pas_Un_Wav_Est_Refuse_Proprement()
        {
            byte[] garbage = System.Text.Encoding.ASCII.GetBytes("Ceci n'est pas un fichier audio.");

            Assert.Throws<System.IO.InvalidDataException>(() => Wav.Decode(garbage));
        }

        // ===================================================================
        //  Melangeur
        // ===================================================================

        [Test]
        public void Le_Panoramique_Garde_Un_Volume_Constant_Au_Centre()
        {
            // ⚠️ LE piege du melangeur. Le reflexe est gauche = (1-p)/2 et
            // droite = (1+p)/2 : au centre les deux valent 0,5. Mais l'oreille
            // percoit la PUISSANCE, qui va comme le carre de l'amplitude, et
            // 0,5 + 0,5 en amplitude ne font que 0,5 en puissance. Un son qui
            // traverse l'ecran s'affaiblit donc en passant au milieu.
            //
            // Avec la racine, la somme des carres vaut 1 partout.
            AudioClip dc = Constant(1f, 64);

            foreach (float pan in new[] { -1f, -0.5f, 0f, 0.37f, 1f })
            {
                var mixer = new AudioMixer(48000);
                mixer.Play(dc, gain: 1f, pan: pan);

                var output = new float[8];
                mixer.Mix(output);

                float left = output[0];
                float right = output[1];

                ClassicAssert.AreEqual(1f, left * left + right * right, 1e-4f,
                    $"puissance non constante a un panoramique de {pan}");
            }

            // Et au centre, precisement : 0,707 et non 0,5.
            var centered = new AudioMixer(48000);
            centered.Play(dc, gain: 1f, pan: 0f);

            var buffer = new float[2];
            centered.Mix(buffer);

            ClassicAssert.AreEqual(0.7071f, buffer[0], 1e-3f);
            ClassicAssert.AreEqual(0.7071f, buffer[1], 1e-3f);
        }

        [Test]
        public void Un_Clip_A_Une_Autre_Frequence_Est_Reechantillonne()
        {
            // Sans reechantillonnage, un son enregistre a 24 000 joue sur une
            // carte a 48 000 defile deux fois trop vite : tout devient aigu et
            // deux fois plus court. C'est le defaut le plus audible de la
            // liste, et pourtant il ne casse aucune assertion de structure.
            //
            // Rampe de 100 trames a 24 000 : a 48 000 elle doit durer 200
            // trames, donc avancer d'une demi-trame par trame de sortie.
            var ramp = new float[100];
            for (int i = 0; i < ramp.Length; i++)
                ramp[i] = i / 100f;

            var clip = new AudioClip(ramp, 1, 24000);

            var mixer = new AudioMixer(48000);
            mixer.Play(clip, gain: 1f, pan: -1f);   // tout a gauche : gain 1 exactement

            var output = new float[40 * AudioMixer.Channels];
            mixer.Mix(output);

            // Trame de sortie 20 -> trame 10 du clip -> valeur 0,10.
            ClassicAssert.AreEqual(0.10f, output[20 * 2], 1e-3f);
            ClassicAssert.AreEqual(0.05f, output[10 * 2], 1e-3f);
        }

        [Test]
        public void Deux_Voix_S_Additionnent()
        {
            AudioClip dc = Constant(0.25f, 32);

            var mixer = new AudioMixer(48000);
            mixer.Play(dc, gain: 1f, pan: -1f);
            mixer.Play(dc, gain: 1f, pan: -1f);

            var output = new float[4];
            mixer.Mix(output);

            ClassicAssert.AreEqual(0.5f, output[0], 1e-4f);
            ClassicAssert.AreEqual(2, mixer.ActiveVoices);
        }

        [Test]
        public void Une_Voix_Terminee_Libere_Sa_Place()
        {
            AudioClip dc = Constant(1f, 4);

            var mixer = new AudioMixer(48000, maxVoices: 2);
            mixer.Play(dc);

            ClassicAssert.AreEqual(1, mixer.ActiveVoices);

            mixer.Mix(new float[16 * AudioMixer.Channels]);   // largement de quoi finir

            ClassicAssert.AreEqual(0, mixer.ActiveVoices, "la voix aurait du se liberer");
        }

        [Test]
        public void Une_Voix_En_Boucle_Ne_Se_Termine_Jamais()
        {
            AudioClip dc = Constant(1f, 4);

            var mixer = new AudioMixer(48000);
            mixer.Play(dc, loop: true);

            mixer.Mix(new float[400 * AudioMixer.Channels]);

            ClassicAssert.AreEqual(1, mixer.ActiveVoices);
        }

        [Test]
        public void Un_Identifiant_Perime_N_Arrete_Pas_Le_Son_Suivant()
        {
            // ⚠️ Piege classique et desagreable : le jeu garde l'identifiant
            // d'un son court, l'appelle Stop bien apres, et coupe le son qui a
            // pris la place. Ca ne se produit que sous charge, donc jamais
            // pendant les tests manuels.
            AudioClip court = Constant(1f, 4);
            AudioClip longue = Constant(1f, 100000);

            var mixer = new AudioMixer(48000, maxVoices: 1);

            int stale = mixer.Play(court);
            mixer.Mix(new float[16 * AudioMixer.Channels]);   // la voix se termine

            int fresh = mixer.Play(longue, loop: true);
            Assert.That(fresh, Is.Not.EqualTo(stale), "les identifiants doivent differer");

            mixer.Stop(stale);

            ClassicAssert.AreEqual(1, mixer.ActiveVoices, "l'ancien identifiant a coupe le nouveau son");

            mixer.Stop(fresh);
            ClassicAssert.AreEqual(0, mixer.ActiveVoices);
        }

        [Test]
        public void Le_Melange_Est_Borne()
        {
            AudioClip dc = Constant(1f, 64);

            var mixer = new AudioMixer(48000);

            for (int i = 0; i < 20; i++)
                mixer.Play(dc, gain: 1f, pan: -1f);

            var output = new float[8];
            mixer.Mix(output);

            foreach (float sample in output)
                Assert.That(MathF.Abs(sample), Is.LessThanOrEqualTo(1f), "echantillon hors bornes");

            // Et le pic reel est rapporte, lui, sans bornage : c'est ce qui
            // permet de savoir qu'on sature.
            Assert.That(mixer.LastPeak, Is.GreaterThan(10f));
        }

        [Test]
        public void Le_Tampon_Est_Efface_Avant_Le_Melange()
        {
            // Le pilote ne garantit pas un tampon propre. Sans effacement, le
            // contenu de l'appel precedent se rejouerait sous le nouveau son -
            // un echo permanent, tres difficile a rattacher a sa cause.
            var mixer = new AudioMixer(48000);

            var output = new float[8];
            Array.Fill(output, 0.9f);

            mixer.Mix(output);

            foreach (float sample in output)
                ClassicAssert.AreEqual(0f, sample, 1e-6f);
        }

        [Test]
        public void Le_Manque_De_Voix_Est_Compte()
        {
            AudioClip dc = Constant(1f, 100000);

            var mixer = new AudioMixer(48000, maxVoices: 2);

            for (int i = 0; i < 5; i++)
                mixer.Play(dc, loop: true);

            ClassicAssert.AreEqual(2, mixer.ActiveVoices);
            ClassicAssert.AreEqual(3, mixer.Dropped);
        }

        // ===================================================================
        //  Sons generes
        // ===================================================================

        [Test]
        public void Une_Note_Ne_Claque_Ni_Au_Debut_Ni_A_La_Fin()
        {
            // ⚠️ Une sinusoide qui demarre net presente une DISCONTINUITE : le
            // signal saute de 0 a sa valeur en un echantillon. Un saut brutal
            // contient toutes les frequences a la fois, et s'entend comme un
            // claquement souvent plus fort que la note elle-meme.
            AudioClip tone = SoundGen.Tone(440f, 0.25f);

            Assert.That(MathF.Abs(tone.Samples[0]), Is.LessThan(0.01f), "claquement au debut");
            Assert.That(MathF.Abs(tone.Samples[^1]), Is.LessThan(0.01f), "claquement a la fin");

            // Sans quoi le test passerait sur un clip silencieux.
            float peak = 0f;
            foreach (float s in tone.Samples)
                peak = MathF.Max(peak, MathF.Abs(s));

            Assert.That(peak, Is.GreaterThan(0.4f), "la note doit quand meme sonner");
        }

        [Test]
        public void Une_Note_Est_A_La_Bonne_Frequence()
        {
            // Une sinusoide a 440 Hz traverse le zero 880 fois par seconde.
            // C'est mesurable sans oreille, et ca attrape aussi bien une erreur
            // de facteur 2 pi qu'une confusion periode/frequence.
            AudioClip tone = SoundGen.Tone(440f, 1f, sampleRate: 48000, fade: 0.001f);

            int crossings = 0;

            for (int i = 1; i < tone.Samples.Length; i++)
                if (tone.Samples[i - 1] <= 0f && tone.Samples[i] > 0f)
                    crossings++;

            // Une montee par periode : 440 attendues, a une pres selon la phase.
            Assert.That(crossings, Is.InRange(438, 442), $"{crossings} periodes au lieu de 440");
        }

        [Test]
        public void Un_Impact_Decroit_Vraiment()
        {
            // Un bruit qui ne decroit pas n'est pas un choc, c'est un souffle.
            AudioClip hit = SoundGen.Impact(0.2f);

            float Energy(int from, int to)
            {
                float sum = 0f;
                for (int i = from; i < to; i++) sum += hit.Samples[i] * hit.Samples[i];
                return sum / (to - from);
            }

            int n = hit.Samples.Length;

            float start = Energy(0, n / 10);
            float end = Energy(n * 9 / 10, n);

            Assert.That(start, Is.GreaterThan(end * 50f), "la decroissance est trop molle");
        }

        // ===================================================================

        /// <summary>Clip mono dont tous les echantillons valent la meme chose.</summary>
        private static AudioClip Constant(float value, int frames)
        {
            var samples = new float[frames];
            Array.Fill(samples, value);
            return new AudioClip(samples, 1, 48000);
        }

        /// <summary>
        /// ⚠️ Un bip fait pour BOUCLER : ses deux bouts valent zero, sinon la
        /// boucle claque a chaque tour. Et le silence fait bien partie du clip :
        /// c'est lui qui donne le rythme.
        /// </summary>
        [Test]
        public void Un_Bip_Boucle_Sans_Claquement()
        {
            AudioClip beep = SoundGen.Beep(440f, 0.25f, 0.25f, 48000);

            ClassicAssert.AreEqual(24000, beep.FrameCount, "0,25 s de son + 0,25 s de silence");
            ClassicAssert.AreEqual(0f, beep.Samples[0], 1e-6f);
            ClassicAssert.AreEqual(0f, beep.Samples[^1], 1e-6f);

            // Le silence est un vrai silence...
            for (int i = 12000; i < 24000; i++)
                ClassicAssert.AreEqual(0f, beep.Samples[i], $"echantillon {i}");

            // ... et le bip, un vrai son.
            float peak = 0f;
            for (int i = 0; i < 12000; i++)
                peak = MathF.Max(peak, MathF.Abs(beep.Samples[i]));

            ClassicAssert.AreEqual(0.5f, peak, 0.01f);
        }
    }
}
