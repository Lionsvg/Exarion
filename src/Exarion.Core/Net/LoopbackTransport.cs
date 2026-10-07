// ===========================================================================
//  Exarion - Reseau simule en memoire (3.1)
//
//  ---------------------------------------------------------------------
//  C'EST L'INSTRUMENT DE MESURE DU NETCODE
//
//  Ce que la capture d'ecran est au rendu et le WAV au son, ce transport
//  l'est au reseau : le moyen de VERIFIER sans avoir besoin d'un humain, de
//  deux machines, ni de chance.
//
//  Il rejoue a volonte les conditions qui font echouer le netcode :
//     - latence (aller simple), et GIGUE autour de cette latence ;
//     - PERTE de paquets ;
//     - DUPLICATION ;
//     - DESORDRE d'arrivee.
//
//  ⚠️⚠️ ET IL EST DETERMINISTE. Le tirage vient d'un generateur a graine, et
//  l'horloge est fournie par l'appelant - pas de DateTime.Now. Deux
//  executions du meme test donnent donc EXACTEMENT le meme scenario de perte.
//  Un test de netcode qui echoue une fois sur vingt ne sert a rien : on ne
//  saurait jamais si une correction a marche.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Collections.Generic;
using Exarion.Core.Deterministic;

namespace Exarion.Core.Net
{
    /// <summary>
    /// Le reseau simule. Plusieurs LoopbackTransport partagent un Router, qui
    /// tient la file des paquets en vol et les livre a l'heure.
    /// </summary>
    public sealed class LoopbackRouter
    {
        private struct InFlight
        {
            public NetAddress From;
            public NetAddress To;
            public byte[] Data;
            public int Length;
            public double DeliverAt;

            /// <summary>Ordre d'emission, pour trier a temps d'arrivee egal.</summary>
            public long Order;
        }

        private readonly List<InFlight> _flying = new();
        private readonly Dictionary<string, LoopbackTransport> _endpoints = new(StringComparer.Ordinal);
        private Rng _rng;
        private long _sent;

        /// <summary>Latence ALLER SIMPLE, en secondes. Le trajet retour la paie aussi.</summary>
        public double Latency;

        /// <summary>Variation aleatoire autour de la latence, en secondes.</summary>
        public double Jitter;

        /// <summary>Probabilite de perte, dans [0,1].</summary>
        public double Loss;

        /// <summary>Probabilite qu'un paquet arrive en double.</summary>
        public double Duplication;

        /// <summary>Temps courant, avance par l'appelant. JAMAIS DateTime.Now.</summary>
        public double Time { get; private set; }

        /// <summary>Compteurs, pour que les tests puissent MESURER au lieu de croire.</summary>
        public int SentCount { get; private set; }
        public int DeliveredCount { get; private set; }
        public int DroppedCount { get; private set; }
        public long SentBytes { get; private set; }

        public LoopbackRouter(uint seed = 12345) => _rng = new Rng(seed);

        public void Register(LoopbackTransport transport)
            => _endpoints[transport.Local.Value] = transport;

        public void Unregister(NetAddress address) => _endpoints.Remove(address.Value);

        internal void Enqueue(NetAddress from, NetAddress to, byte[] data, int length)
        {
            SentCount++;
            SentBytes += length;

            if (!_endpoints.ContainsKey(to.Value))
                return;   // personne a cette adresse : silencieux, comme UDP

            int copies = 1;

            if (Duplication > 0.0 && _rng.NextFloat() < Duplication)
                copies = 2;

            for (int i = 0; i < copies; i++)
            {
                if (Loss > 0.0 && _rng.NextFloat() < Loss)
                {
                    DroppedCount++;
                    continue;
                }

                // ⚠️ ON COPIE LE TAMPON. L'appelant reutilise le sien au paquet
                // suivant : sans copie, tous les paquets en vol pointeraient sur
                // les MEMES octets, et arriveraient tous avec le contenu du
                // dernier envoye. Defaut particulierement retors, parce que tout
                // marche tant qu'un seul paquet est en vol a la fois.
                var copy = new byte[length];
                Array.Copy(data, copy, length);

                double delay = Latency;

                if (Jitter > 0.0)
                    delay += (_rng.NextFloat() * 2.0 - 1.0) * Jitter;

                _flying.Add(new InFlight
                {
                    From = from,
                    To = to,
                    Data = copy,
                    Length = length,

                    // Une latence negative n'a pas de sens et ferait arriver un
                    // paquet avant son envoi.
                    DeliverAt = Time + Math.Max(0.0, delay),
                    Order = _sent++,
                });
            }
        }

        /// <summary>
        /// Avance l'horloge et livre ce qui est arrive.
        ///
        /// ⚠️ La livraison est triee par HEURE D'ARRIVEE, puis par ordre
        /// d'emission. C'est ce qui produit le DESORDRE quand il y a de la gigue :
        /// un paquet parti apres peut arriver avant, exactement comme sur un
        /// vrai reseau. Livrer dans l'ordre d'emission masquerait la moitie des
        /// defauts qu'on cherche.
        /// </summary>
        public void Advance(double dt)
        {
            Time += dt;

            _flying.Sort(static (a, b) =>
            {
                int byTime = a.DeliverAt.CompareTo(b.DeliverAt);
                return byTime != 0 ? byTime : a.Order.CompareTo(b.Order);
            });

            int i = 0;

            while (i < _flying.Count)
            {
                InFlight packet = _flying[i];

                if (packet.DeliverAt > Time)
                    break;

                if (_endpoints.TryGetValue(packet.To.Value, out LoopbackTransport? destination))
                {
                    destination.Deliver(packet.From, packet.Data, packet.Length);
                    DeliveredCount++;
                }

                _flying.RemoveAt(0);
            }
        }

        /// <summary>Paquets encore en vol. Un test peut ainsi attendre le calme.</summary>
        public int InFlightCount => _flying.Count;
    }

    public sealed class LoopbackTransport : INetTransport
    {
        private readonly LoopbackRouter _router;
        private readonly Queue<NetPacket> _received = new();

        public LoopbackTransport(LoopbackRouter router, string name)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
            Local = new NetAddress(name);
            _router.Register(this);
        }

        public NetAddress Local { get; }

        public void Send(NetAddress to, byte[] data, int length)
            => _router.Enqueue(Local, to, data, length);

        public bool Receive(out NetPacket packet)
        {
            if (_received.Count == 0)
            {
                packet = default;
                return false;
            }

            packet = _received.Dequeue();
            return true;
        }

        internal void Deliver(NetAddress from, byte[] data, int length)
            => _received.Enqueue(new NetPacket(from, data, length));

        public void Dispose() => _router.Unregister(Local);
    }
}
