// ===========================================================================
//  Exarion - Transport UDP reel (3.1)
//
//  ---------------------------------------------------------------------
//  LA SEULE CLASSE DU PROJET QUI TOUCHE UNE SOCKET
//
//  Exactement le role qu'AudioDevice joue pour miniaudio : isoler la
//  plateforme derriere une facade maison. Tout le netcode - protocole,
//  instantanes, interpolation, et plus tard prediction et reconciliation -
//  vit dans Exarion.Core et ne connait que INetTransport.
//
//  Consequence pratique : ce fichier peut etre remplace (par un tunnel, un
//  relais, un transport de rejeu enregistre) sans qu'une ligne de netcode
//  bouge. Et surtout, le netcode se teste sans lui.
//  ---------------------------------------------------------------------
//
//  ⚠️ System.Net.Sockets fait partie du runtime .NET : ce n'est PAS une
//  dependance au sens de DEPENDENCIES.md, pas plus que System.Text.Json ne
//  l'etait pour l'import glTF. Aucun paquet ajoute, aucune licence a verifier.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Exarion.Core.Net;

namespace Exarion.Engine.Net
{
    public sealed class UdpTransport : INetTransport
    {
        private readonly Socket _socket;
        private readonly byte[] _receive = new byte[NetConfig.MaxPacketSize];

        /// <summary>
        /// Correspondance adresse texte -> point de terminaison.
        ///
        /// ⚠️ On MEMORISE au lieu de reconstruire : analyser « 192.168.1.10:27015 »
        /// a chaque envoi allouerait un IPEndPoint 64 fois par seconde et par
        /// client. Le noyau reseau ne manipule que des chaines (il ne doit pas
        /// connaitre System.Net), donc la traduction se fait ici, une fois.
        /// </summary>
        private readonly Dictionary<string, IPEndPoint> _known = new(StringComparer.Ordinal);

        private EndPoint _from = new IPEndPoint(IPAddress.Any, 0);

        /// <summary>
        /// Ouvre une socket UDP.
        /// </summary>
        /// <param name="port">Port d'ecoute. 0 = le systeme en choisit un libre.</param>
        public UdpTransport(int port)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                // ⚠️⚠️ NON BLOQUANT, ET C'EST VITAL. Une lecture bloquante ferait
                // attendre la boucle de jeu jusqu'a l'arrivee d'un paquet : le
                // rendu s'arreterait, et le jeu tournerait a la cadence du
                // reseau. Ici Receive rend simplement faux quand il n'y a rien.
                Blocking = false,
            };

            // Un tampon genereux : a 64 paquets/seconde et 10 joueurs, une image
            // de retard suffit a en accumuler plusieurs dizaines. Trop petit, le
            // systeme les JETTE en silence - et on chercherait la perte dans le
            // reseau alors qu'elle serait locale.
            _socket.ReceiveBufferSize = 1 << 20;
            _socket.SendBufferSize = 1 << 20;

            try
            {
                // ⚠️ Sur Windows, un ICMP « port unreachable » recu apres un envoi
                // fait echouer la LECTURE suivante avec ConnectionReset, sur une
                // socket UDP sans connexion. Ce comportement est propre a Windows
                // et casse le serveur des qu'un client se ferme brutalement.
                // SIO_UDP_CONNRESET le desactive.
                if (OperatingSystem.IsWindows())
                    _socket.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (Exception)
            {
                // Pas disponible sur cette plateforme : on continue. C'est un
                // confort, pas une condition de fonctionnement.
            }

            _socket.Bind(new IPEndPoint(IPAddress.Any, port));

            var local = (IPEndPoint)_socket.LocalEndPoint!;
            Local = new NetAddress($"0.0.0.0:{local.Port}");
            Port = local.Port;
        }

        public NetAddress Local { get; }

        public int Port { get; }

        /// <summary>Paquets perdus a l'emission, faute de place. Doit rester a 0.</summary>
        public int SendFailures { get; private set; }

        public void Send(NetAddress to, byte[] data, int length)
        {
            if (!TryResolve(to, out IPEndPoint? endpoint) || endpoint is null)
                return;

            try
            {
                _socket.SendTo(data, 0, length, SocketFlags.None, endpoint);
            }
            catch (SocketException)
            {
                // ⚠️ On NE LEVE PAS. Un envoi UDP peut echouer pour des raisons
                // qui ne concernent pas le jeu : tampon plein, route
                // momentanement absente, interface qui change. Le protocole est
                // deja concu pour la perte - un paquet en moins est exactement le
                // cas normal. Tuer la partie serait la seule vraie erreur.
                SendFailures++;
            }
        }

        public bool Receive(out NetPacket packet)
        {
            packet = default;

            try
            {
                if (_socket.Available <= 0)
                    return false;

                int read = _socket.ReceiveFrom(_receive, 0, _receive.Length, SocketFlags.None, ref _from);

                if (read <= 0)
                    return false;

                var sender = (IPEndPoint)_from;
                string key = $"{sender.Address}:{sender.Port}";

                // On memorise l'expediteur : c'est ainsi qu'un serveur peut
                // repondre a un client dont il n'avait jamais entendu parler.
                if (!_known.ContainsKey(key))
                    _known[key] = new IPEndPoint(sender.Address, sender.Port);

                packet = new NetPacket(new NetAddress(key), _receive, read);
                return true;
            }
            catch (SocketException)
            {
                // Meme raison qu'a l'emission : une erreur de lecture UDP ne doit
                // pas arreter la boucle de jeu.
                return false;
            }
        }

        /// <summary>
        /// Traduit « hote:port » en point de terminaison.
        ///
        /// ⚠️ Aucune resolution DNS ici : Dns.GetHostEntry BLOQUE, parfois
        /// plusieurs secondes. Appelee depuis la boucle de jeu, elle ferait
        /// figer l'image. Un nom d'hote se resout donc AVANT (voir Resolve).
        /// </summary>
        private bool TryResolve(NetAddress address, out IPEndPoint? endpoint)
        {
            endpoint = null;

            if (address.IsEmpty)
                return false;

            if (_known.TryGetValue(address.Value, out endpoint))
                return true;

            int colon = address.Value.LastIndexOf(':');

            if (colon <= 0
                || !int.TryParse(address.Value[(colon + 1)..], out int port)
                || !IPAddress.TryParse(address.Value[..colon], out IPAddress? ip))
                return false;

            endpoint = new IPEndPoint(ip, port);
            _known[address.Value] = endpoint;
            return true;
        }

        /// <summary>
        /// Resout un nom d'hote en adresse utilisable, une fois pour toutes.
        ///
        /// A appeler HORS de la boucle de jeu : la resolution DNS bloque.
        /// </summary>
        public static NetAddress Resolve(string host, int port)
        {
            if (IPAddress.TryParse(host, out IPAddress? direct))
                return new NetAddress($"{direct}:{port}");

            try
            {
                IPAddress[] found = Dns.GetHostAddresses(host);

                foreach (IPAddress candidate in found)
                    if (candidate.AddressFamily == AddressFamily.InterNetwork)
                        return new NetAddress($"{candidate}:{port}");
            }
            catch (Exception)
            {
                // Nom introuvable : on rend une adresse vide, l'appelant le dira.
            }

            return new NetAddress("");
        }

        public void Dispose()
        {
            try
            {
                _socket.Close();
            }
            catch (Exception)
            {
                // Fermer une socket deja fermee n'est pas une erreur utile.
            }

            _socket.Dispose();
        }
    }
}
