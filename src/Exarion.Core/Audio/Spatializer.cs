// ===========================================================================
//  Exarion - Spatialisation (S3) et occlusion (S4)
//
//  Fonctions PURES : une position, une ecoute, et on rend un gain, un
//  panoramique, une hauteur. Aucun etat, donc entierement testable en ligne
//  de commande - c'est tout l'interet de garder ca hors de miniaudio.
//
//  ---------------------------------------------------------------------
//  ⚠️⚠️ CE QUE CETTE METHODE NE PEUT PAS FAIRE : DISTINGUER L'AVANT DE
//  L'ARRIERE.
//
//  Le panoramique d'amplitude ne dispose que de deux haut-parleurs et d'une
//  seule information : la gauche et la droite. Une source droit devant et une
//  source droit derriere donnent le MEME resultat - centre, meme volume.
//
//  Ce n'est pas un defaut d'implementation, c'est la limite de la technique.
//  Les distinguer demande une HRTF : filtrer le son par la reponse de la tete
//  et du pavillon de l'oreille, qui colorent differemment ce qui vient de
//  devant et de derriere. C'est un autre chantier, et il exige des donnees
//  de mesure.
//
//  Pour un FPS tactique, c'est une limite REELLE : savoir si l'ennemi est
//  devant ou derriere est une information de jeu. A traiter un jour.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Numerics;
using Exarion.Core.Physics;

namespace Exarion.Core.Audio
{
    /// <summary>Ou l'on ecoute, et dans quel sens.</summary>
    public struct AudioListener
    {
        public Vector3 Position;

        /// <summary>Direction du regard, unitaire.</summary>
        public Vector3 Forward;

        /// <summary>Direction laterale, unitaire. C'est elle qui donne le panoramique.</summary>
        public Vector3 Right;

        /// <summary>Vitesse, pour l'effet Doppler. En metres par seconde.</summary>
        public Vector3 Velocity;

        public static AudioListener Default => new()
        {
            Position = Vector3.Zero,
            Forward = -Vector3.UnitZ,
            Right = Vector3.UnitX,
            Velocity = Vector3.Zero,
        };
    }

    /// <summary>Comment le son faiblit avec la distance.</summary>
    public struct AudioFalloff
    {
        /// <summary>En deca, le son est a plein volume. En metres.</summary>
        public float ReferenceDistance;

        /// <summary>Au-dela, il ne faiblit plus. Evite un silence total au loin.</summary>
        public float MaxDistance;

        /// <summary>0 = pas d'attenuation, 1 = attenuation physique, plus = plus raide.</summary>
        public float Rolloff;

        /// <summary>Force de l'effet Doppler. 0 le desactive.</summary>
        public float DopplerScale;

        public static AudioFalloff Default => new()
        {
            ReferenceDistance = 1.5f,
            MaxDistance = 60f,
            Rolloff = 1f,
            DopplerScale = 1f,
        };
    }

    public static class Spatializer
    {
        /// <summary>Vitesse du son dans l'air, en metres par seconde.</summary>
        public const float SpeedOfSound = 343f;

        /// <summary>
        /// Calcule ce qu'il faut envoyer au melangeur pour une source placee
        /// dans le monde.
        /// </summary>
        public static void Compute(in AudioListener listener, in AudioFalloff falloff,
                                   Vector3 sourcePosition, Vector3 sourceVelocity,
                                   out float gain, out float pan, out float pitch)
        {
            Vector3 toSource = sourcePosition - listener.Position;
            float distance = toSource.Length();

            gain = Attenuation(distance, falloff);
            pan = Pan(listener, toSource, distance, falloff.ReferenceDistance);
            pitch = Doppler(listener, falloff, toSource, distance, sourceVelocity);
        }

        /// <summary>
        /// Attenuation inverse a la distance, bornee des deux cotes.
        ///
        /// Modele classique et public (celui d'OpenAL, « inverse distance
        /// clamped »), reimplemente ici :
        ///     gain = ref / (ref + rolloff * (d - ref))
        ///
        /// ⚠️ La BORNE BASSE n'est pas une precaution, elle est indispensable :
        /// sans elle, une source qui passe exactement sur l'auditeur donne une
        /// division par zero, donc un gain infini, donc un craquement a plein
        /// volume. Ca arrive des qu'un son est joue a la position du joueur.
        ///
        /// ⚠️ La BORNE HAUTE evite qu'un son devienne rigoureusement inaudible
        /// tout en continuant a occuper une voix. Dans un jeu ou l'on ecoute
        /// les pas, on prefere un plancher tenu a un silence progressif.
        /// </summary>
        public static float Attenuation(float distance, in AudioFalloff falloff)
        {
            float reference = MathF.Max(0.01f, falloff.ReferenceDistance);
            float max = MathF.Max(reference, falloff.MaxDistance);

            float d = Math.Clamp(distance, reference, max);

            return reference / (reference + falloff.Rolloff * (d - reference));
        }

        /// <summary>
        /// Panoramique : la projection de la direction sur l'axe lateral.
        ///
        /// ⚠️ TRES PRES, ON RECENTRE. A dix centimetres de l'oreille, la
        /// direction change du tout au tout au moindre mouvement, et le son
        /// sauterait d'une enceinte a l'autre. Physiquement, une source collee
        /// a la tete s'entend des deux cotes : on ramene donc progressivement
        /// le panoramique au centre en dessous de la distance de reference.
        /// </summary>
        public static float Pan(in AudioListener listener, Vector3 toSource, float distance,
                                float referenceDistance)
        {
            if (distance < 1e-4f)
                return 0f;

            float lateral = Vector3.Dot(toSource / distance, listener.Right);

            float closeness = referenceDistance > 1e-4f
                ? MathF.Min(1f, distance / referenceDistance)
                : 1f;

            return Math.Clamp(lateral * closeness, -1f, 1f);
        }

        /// <summary>
        /// Effet Doppler : le rapport des vitesses le long de l'axe qui relie
        /// la source a l'auditeur.
        ///
        ///     hauteur = (c - v_auditeur) / (c - v_source)
        ///
        /// avec les vitesses comptees POSITIVES quand elles rapprochent. Une
        /// source qui fonce vers nous comprime les ondes : le denominateur
        /// diminue, la hauteur monte. C'est le sifflement d'un train qui passe.
        ///
        /// ⚠️ Le denominateur DOIT rester loin de zero : une source atteignant
        /// la vitesse du son donnerait une hauteur infinie. Le cas est absurde
        /// dans un jeu, mais une valeur de vitesse aberrante suffit a le
        /// declencher.
        /// </summary>
        public static float Doppler(in AudioListener listener, in AudioFalloff falloff,
                                    Vector3 toSource, float distance, Vector3 sourceVelocity)
        {
            if (falloff.DopplerScale <= 0f || distance < 1e-4f)
                return 1f;

            Vector3 direction = toSource / distance;

            // Composantes de rapprochement, positives quand ca se rapproche.
            float listenerTowards = Vector3.Dot(listener.Velocity, direction);
            float sourceTowards = -Vector3.Dot(sourceVelocity, direction);

            float c = SpeedOfSound / falloff.DopplerScale;

            float denominator = c - sourceTowards;

            if (denominator < c * 0.2f)
                denominator = c * 0.2f;

            return (c + listenerTowards) / denominator;
        }

        // ===================================================================
        //  S4 - Occlusion
        // ===================================================================

        /// <summary>
        /// Cherche un obstacle entre la source et l'auditeur.
        /// Rend 0 si la vue est degagee, 1 si elle est bouchee.
        ///
        /// ⚠️ C'est un test TOUT OU RIEN, et c'est assume. Une occlusion
        /// progressive demanderait plusieurs rayons autour du trajet, donc
        /// autant de fois le cout. A une source pres, la difference ne
        /// s'entend pas : ce qui compte, c'est que le son PERDE SES AIGUS
        /// quand un mur s'interpose, pas la finesse du dosage.
        ///
        /// ⚠️ A appeler depuis le fil de JEU. Le fil audio ne doit jamais
        /// parcourir le decor.
        /// </summary>
        public static float Occlusion(CollisionWorld? world, Vector3 listener, Vector3 source)
        {
            if (world is null)
                return 0f;

            Vector3 delta = source - listener;
            float distance = delta.Length();

            if (distance < 1e-3f)
                return 0f;

            return world.Raycast(listener, delta, distance, out _, out _) ? 1f : 0f;
        }

        /// <summary>
        /// Coefficient du passe-bas correspondant a une occlusion donnee.
        ///
        /// Un passe-bas d'ordre 1 a pour coefficient :
        ///     a = 1 - exp(-2 pi fc / fs)
        ///
        /// On interpole la FREQUENCE DE COUPURE et non le coefficient : l'
        /// oreille entend les frequences de facon logarithmique, et interpoler
        /// le coefficient rendrait presque tout l'effet sur le dernier dixieme.
        /// </summary>
        public static float LowPassCoefficient(float occlusion, int sampleRate,
                                               float openHz = 20000f, float blockedHz = 700f)
        {
            occlusion = Math.Clamp(occlusion, 0f, 1f);

            if (occlusion <= 0f)
                return 1f;

            // Interpolation geometrique : a mi-chemin entre 20 kHz et 700 Hz
            // on veut 3,7 kHz, pas 10 kHz.
            float cutoff = openHz * MathF.Pow(blockedHz / openHz, occlusion);

            float a = 1f - MathF.Exp(-2f * MathF.PI * cutoff / sampleRate);

            return Math.Clamp(a, 0.001f, 1f);
        }

        /// <summary>Attenuation supplementaire due a l'obstacle.</summary>
        public static float OcclusionGain(float occlusion, float blockedGain = 0.45f)
            => 1f + (blockedGain - 1f) * Math.Clamp(occlusion, 0f, 1f);
    }
}
