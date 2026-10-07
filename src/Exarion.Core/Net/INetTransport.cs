// ===========================================================================
//  Exarion - Transport de paquets (3.1)
//
//  ---------------------------------------------------------------------
//  POURQUOI UNE INTERFACE, ET PAS DIRECTEMENT UNE SOCKET
//
//  C'est LA decision qui rend le netcode testable, et elle vaut la peine
//  d'etre justifiee parce qu'elle contredit le reflexe « moins de couches ».
//
//  Un defaut de netcode ne se voit pas tout de suite : il se manifeste comme
//  « ca tremble parfois », trois semaines plus tard, chez un joueur qui a
//  200 ms de latence et 3 % de perte. Reproduire ca avec de vraies sockets
//  demande deux machines et de la chance.
//
//  Derriere cette interface, LoopbackTransport rejoue exactement ces
//  conditions - latence, gigue, perte, duplication, desordre - dans un test
//  qui tourne en quelques millisecondes et donne le MEME resultat a chaque
//  fois. C'est la seule facon de tenir la consigne de la roadmap : « chaque
//  etape doit etre testee automatiquement avant la suivante ».
//
//  Consequence : Exarion.Core ne contient AUCUN appel reseau. La socket UDP
//  vit dans Engine, exactement comme miniaudio est isole derriere AudioDevice.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;

namespace Exarion.Core.Net
{
    /// <summary>
    /// Adresse d'un correspondant, opaque pour le noyau reseau.
    ///
    /// ⚠️ Une CHAINE et non une IP : Core ne doit pas connaitre System.Net. Le
    /// transport UDP y met « 192.168.1.10:27015 », le transport de test y met
    /// « client2 ». Le noyau ne fait que comparer et router.
    /// </summary>
    public readonly struct NetAddress : IEquatable<NetAddress>
    {
        public readonly string Value;

        public NetAddress(string value) => Value = value ?? "";

        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public bool Equals(NetAddress other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is NetAddress a && Equals(a);
        public override int GetHashCode() => Value?.GetHashCode(StringComparison.Ordinal) ?? 0;
        public override string ToString() => Value;

        public static bool operator ==(NetAddress a, NetAddress b) => a.Equals(b);
        public static bool operator !=(NetAddress a, NetAddress b) => !a.Equals(b);
    }

    /// <summary>Un paquet recu.</summary>
    public readonly struct NetPacket
    {
        public readonly NetAddress From;

        /// <summary>Tampon des donnees. ⚠️ Valide jusqu'au prochain Receive.</summary>
        public readonly byte[] Data;

        public readonly int Length;

        public NetPacket(NetAddress from, byte[] data, int length)
        {
            From = from;
            Data = data;
            Length = length;
        }
    }

    public interface INetTransport : IDisposable
    {
        /// <summary>
        /// Envoie un paquet. Sans garantie : il peut se perdre, arriver en
        /// double, ou dans le desordre. C'est UDP, et c'est voulu - voir la
        /// note sur TCP dans NetProtocol.
        /// </summary>
        void Send(NetAddress to, byte[] data, int length);

        /// <summary>
        /// Prend le prochain paquet recu, s'il y en a un.
        ///
        /// ⚠️ A appeler EN BOUCLE jusqu'a ce qu'il rende faux. Ne traiter qu'un
        /// paquet par image ferait grossir la file indefiniment des que le debit
        /// depasse le nombre d'images par seconde - et le retard s'aggraverait
        /// tout seul, sans jamais rattraper.
        /// </summary>
        bool Receive(out NetPacket packet);

        /// <summary>Adresse locale, pour l'affichage.</summary>
        NetAddress Local { get; }
    }
}
