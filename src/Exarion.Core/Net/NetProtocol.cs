// ===========================================================================
//  Exarion - Protocole (3.2)
//
//  ---------------------------------------------------------------------
//  POURQUOI UDP, ET PAS TCP
//
//  TCP garantit l'ordre et la livraison, et c'est precisement ce qui le rend
//  inutilisable ici. Quand un paquet se perd, TCP retient TOUS les suivants
//  jusqu'a l'avoir retransmis - c'est le blocage de tete de file. Dans un
//  jeu, ca veut dire que la position du joueur d'il y a 200 ms bloque celle
//  d'il y a 10 ms, alors que la premiere n'a plus AUCUN interet : on veut la
//  plus recente, tout de suite, et tant pis pour les anciennes.
//
//  D'ou : UDP, et on choisit ce qui merite d'etre fiable.
//     - un INSTANTANE perdu est simplement oublie. Le suivant arrive dans
//       15 ms et decrit un etat plus recent. Le retransmettre serait nuisible.
//     - une ENTREE perdue est envoyee en REDONDANCE : chaque paquet porte les
//       N dernieres entrees. Un trou se rebouche donc sans aller-retour, ce
//       qui est la seule facon de ne pas payer 2 x la latence.
//     - la POIGNEE DE MAIN, elle, est reessayee jusqu'a reponse : c'est le
//       seul echange ou la perte n'est pas rattrapable par la suite.
//  ---------------------------------------------------------------------
//
//  ⚠️ Serveur AUTORITAIRE. Le client n'envoie jamais sa position, seulement
//  ses ENTREES. Envoyer la position revient a laisser n'importe qui se
//  teleporter - c'est le defaut de conception le plus courant, et le plus
//  irreparable une fois que le jeu est sorti.
// ===========================================================================

using System;
using System.Numerics;
using Exarion.Core.Physics;

namespace Exarion.Core.Net
{
    public static class NetMessage
    {
        /// <summary>Le client se presente. Reessaye jusqu'a reponse.</summary>
        public const byte Hello = 1;

        /// <summary>Le serveur accepte et attribue un identifiant.</summary>
        public const byte Welcome = 2;

        /// <summary>Le serveur refuse (version, serveur plein).</summary>
        public const byte Reject = 3;

        /// <summary>Entrees du client, avec redondance.</summary>
        public const byte Input = 4;

        /// <summary>Etat du monde vu par le serveur.</summary>
        public const byte Snapshot = 5;

        /// <summary>Depart propre, des deux cotes.</summary>
        public const byte Bye = 6;

        /// <summary>Mesure d'aller-retour.</summary>
        public const byte Ping = 7;
        public const byte Pong = 8;

        /// <summary>Le client tire. Le serveur tranche, en rembobinant (3.7).</summary>
        public const byte Fire = 9;

        /// <summary>Le serveur annonce le resultat d'un tir.</summary>
        public const byte HitReport = 10;
    }

    public static class NetConfig
    {
        /// <summary>
        /// Version du protocole.
        ///
        /// ⚠️ A INCREMENTER a chaque changement de format. Sans cette
        /// verification, deux versions differentes se connectent et decodent
        /// des octets qui ne veulent plus rien dire : le defaut se manifeste
        /// comme des joueurs qui tremblent ou volent, et on cherche dans le
        /// netcode au lieu de la version.
        /// </summary>
        public const ushort Version = 1;

        /// <summary>
        /// Taille maximale d'un paquet, en octets.
        ///
        /// ⚠️ 1200 et pas 65507 : au-dela de la MTU (typiquement 1500 moins les
        /// en-tetes), IP FRAGMENTE le datagramme. Un seul fragment perdu detruit
        /// alors tout le paquet, donc la probabilite de perte effective explose.
        /// 1200 laisse de la marge pour les tunnels et le VPN.
        /// </summary>
        public const int MaxPacketSize = 1200;

        /// <summary>Nombre maximum de joueurs. 10 = deux equipes de cinq.</summary>
        public const int MaxPlayers = 10;

        /// <summary>
        /// Entrees renvoyees dans chaque paquet.
        ///
        /// ⚠️ C'EST LA PARADE A LA PERTE, et elle coute 12 octets par entree.
        /// Avec 8, il faut perdre huit paquets consecutifs pour que le serveur
        /// manque une entree - a 5 % de perte, c'est une chance sur 39 milliards.
        /// Demander une retransmission couterait un aller-retour complet, soit
        /// exactement ce qu'on cherche a eviter.
        /// </summary>
        public const int InputRedundancy = 8;

        /// <summary>Sans nouvelle pendant ce temps, le client est considere parti.</summary>
        public const double TimeoutSeconds = 10.0;

        /// <summary>Intervalle entre deux tentatives de connexion.</summary>
        public const double HelloRetrySeconds = 0.25;
    }

    /// <summary>
    /// L'etat d'un joueur tel que le serveur le publie.
    ///
    /// ⚠️ On ne replique QUE ce qui est visible. La vitesse, l'etat au sol
    /// interne, le temps de saut restant : le client n'en a pas besoin pour
    /// afficher quelqu'un d'autre, et chaque champ se paie 64 fois par seconde
    /// et par joueur.
    /// </summary>
    public struct PlayerState
    {
        public byte Id;

        /// <summary>Position des PIEDS, comme partout dans le moteur.</summary>
        public Vector3 Position;

        public float Yaw;
        public float Pitch;

        public bool Crouched;

        /// <summary>Vrai si ce joueur est connecte. Un trou se replique comme absent.</summary>
        public bool Active;

        /// <summary>Octets ecrits par joueur. Mesure, pas estimation.</summary>
        public const int SizeInBytes = 1 + 12 + 2 + 2 + 1;

        public void Write(ref NetWriter w)
        {
            w.Byte(Id);
            w.Vector3(Position);
            w.Angle(Yaw);
            w.Angle(Pitch);
            w.Byte(Crouched ? (byte)1 : (byte)0);
        }

        public static PlayerState Read(ref NetReader r)
        {
            var state = new PlayerState { Active = true };

            state.Id = r.Byte();
            state.Position = r.Vector3();
            state.Yaw = r.Angle();
            state.Pitch = r.Angle();
            state.Crouched = r.Byte() != 0;

            return state;
        }
    }

    /// <summary>
    /// L'etat AUTORITAIRE du joueur qui recoit l'instantane, en plus de sa
    /// position publique.
    ///
    /// ⚠️⚠️ POURQUOI LA VITESSE N'EST ENVOYEE QU'AU PROPRIETAIRE. Pour REJOUER
    /// ses entrees (3.6), le client doit repartir de l'etat EXACT du serveur -
    /// et la position seule ne suffit pas : un joueur en l'air, en train de
    /// sauter ou de tomber, a une vitesse verticale qui decide de tout le reste
    /// de sa trajectoire. Repartir avec une vitesse nulle ferait diverger le
    /// rejeu a chaque correction.
    ///
    /// Les AUTRES joueurs n'en ont pas besoin : on les affiche en interpolant
    /// entre deux positions connues, jamais en les simulant. Leur envoyer la
    /// vitesse couterait 12 octets par joueur et par instantane pour rien.
    /// </summary>
    public struct LocalState
    {
        public Vector3 Velocity;
        public float Height;

        /// <summary>Octets : vitesse et hauteur.</summary>
        public const int SizeInBytes = 12 + 4;

        public void Write(ref NetWriter w)
        {
            w.Vector3(Velocity);
            w.Float(Height);
        }

        public static LocalState Read(ref NetReader r)
        {
            var state = new LocalState();
            state.Velocity = r.Vector3();
            state.Height = r.Float();
            return state;
        }
    }

    /// <summary>Une entree horodatee par le pas du client qui l'a produite.</summary>
    public struct TimedInput
    {
        public uint Tick;
        public MoveInput Input;

        /// <summary>Octets par entree : le pas, la direction, le lacet, les drapeaux.</summary>
        public const int SizeInBytes = 4 + 8 + 2 + 1;

        public void Write(ref NetWriter w)
        {
            w.UInt(Tick);

            // ⚠️ La direction est deja bornee a [-1,1] par le client, mais elle
            // vient du RESEAU : on ne peut pas le supposer. Le serveur la
            // rebornera - voir NetServer.ApplyInput. Ici on ecrit tel quel.
            w.Float(Input.Move.X);
            w.Float(Input.Move.Y);
            w.Angle(Input.Yaw);

            byte flags = 0;
            if (Input.Jump) flags |= 1;
            if (Input.Crouch) flags |= 2;
            if (Input.Walk) flags |= 4;
            w.Byte(flags);
        }

        public static TimedInput Read(ref NetReader r)
        {
            var timed = new TimedInput { Tick = r.UInt() };

            float x = r.Float();
            float y = r.Float();

            timed.Input.Move = new Vector2(x, y);
            timed.Input.Yaw = r.Angle();

            byte flags = r.Byte();
            timed.Input.Jump = (flags & 1) != 0;
            timed.Input.Crouch = (flags & 2) != 0;
            timed.Input.Walk = (flags & 4) != 0;

            return timed;
        }

        /// <summary>
        /// Rend l'entree SURE pour la simulation.
        ///
        /// ⚠️⚠️ TOUT CE QUI VIENT DU RESEAU EST HOSTILE JUSQU'A PREUVE DU
        /// CONTRAIRE. Un client modifie peut envoyer Move = (1000, 1000) et
        /// courir cent fois plus vite : la vitesse du moteur est proportionnelle
        /// a la longueur de ce vecteur. Le borner n'est pas de la defiance
        /// envers le joueur, c'est la definition d'un serveur autoritaire.
        /// </summary>
        public static MoveInput Sanitize(MoveInput input)
        {
            Vector2 move = input.Move;
            float length = move.Length();

            // Au-dela de 1, on ramene sur le cercle unite : on garde la
            // DIRECTION voulue, on refuse seulement l'amplitude.
            if (length > 1f)
                move /= length;
            else if (float.IsNaN(length))
                move = Vector2.Zero;

            input.Move = move;

            if (float.IsNaN(input.Yaw) || float.IsInfinity(input.Yaw))
                input.Yaw = 0f;

            return input;
        }
    }
}
