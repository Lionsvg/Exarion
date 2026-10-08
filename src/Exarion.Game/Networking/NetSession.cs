// ===========================================================================
//  Le reseau du bac a sable (phase 3)
//
//  ⚠️ Le serveur et le client peuvent tourner DANS LE MEME PROCESSUS : c'est
//  le mode « heberger », ou l'hote joue sur son propre serveur. Deux sockets
//  distinctes, deux ports - le client passe par le reseau comme n'importe
//  qui. Le raccourci « l'hote lit directement l'etat du serveur » donnerait a
//  l'hote un comportement different de celui des autres joueurs, donc des
//  defauts qu'on ne verrait jamais en local.
// ===========================================================================

using System;
using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Console;
using Exarion.Core.Net;
using Exarion.Core.Physics;
using Exarion.Engine.Net;

namespace Exarion.Game.Networking
{
    internal sealed class NetSession
    {
        public UdpTransport? ServerTransport { get; private set; }
        public UdpTransport? ClientTransport { get; private set; }
        public NetServer? Server { get; private set; }
        public NetClient? Client { get; private set; }

        /// <summary>Nom annonce au serveur (« net_name »).</summary>
        public string PlayerName { get; set; } = "joueur";

        /// <summary>
        /// La carte locale a ete confrontee a celle du serveur (voir MapSync).
        /// Remis a faux a chaque connexion ET a chaque changement de carte
        /// locale : un « map_load » en cours de partie doit etre re-verifie.
        /// </summary>
        public bool MapChecked { get; set; }

        /// <summary>La derniere decision de synchronisation, pour « net ».</summary>
        public string MapStatus { get; set; } = "";

        private float _nextPing;

        /// <summary>
        /// Heberge une partie, puis s'y connecte.
        ///
        /// ⚠️ L'HOTE SE CONNECTE A SON PROPRE SERVEUR PAR LE RESEAU, sur
        /// 127.0.0.1. Il pourrait lire l'etat du serveur directement - et ce
        /// serait un piege : l'hote aurait un comportement different des autres
        /// joueurs, donc des defauts qui n'apparaitraient que chez eux.
        /// </summary>
        /// <param name="mapSource">
        /// D'ou vient la carte : un chemin de fichier, ou SandboxMap.Source. Le
        /// serveur annonce le NOM DE FICHIER, celui par lequel un client la
        /// retrouvera chez lui (voir NetServer.MapName).
        /// </param>
        public void Host(int port, float fixedDelta, MapData map, string mapSource, CollisionWorld predictionWorld, ConsoleSystem c)
        {
            Stop();

            try
            {
                ServerTransport = new UdpTransport(port);
                Server = new NetServer(ServerTransport) { FixedDelta = fixedDelta };

                string announced = mapSource.EndsWith(MapFile.Extension, StringComparison.OrdinalIgnoreCase)
                    ? System.IO.Path.GetFileNameWithoutExtension(mapSource)
                    : map.Name;

                Server.LoadMap(map, announced);

                c.Print($"  serveur ouvert sur le port {ServerTransport.Port}");
                c.Print($"  carte : {Server.MapName}");

                Connect("127.0.0.1", ServerTransport.Port, predictionWorld, c);
            }
            catch (Exception ex)
            {
                Stop();
                c.Print($"  impossible d'ouvrir le port {port} : {ex.Message}");
            }
        }

        /// <summary>
        /// Ferme le client seul. On garde le serveur s'il tourne : on peut
        /// heberger et se connecter ailleurs, c'est utile pour tester.
        /// </summary>
        public void DropClient()
        {
            Client?.Dispose();
            Client = null;
            ClientTransport?.Dispose();
            ClientTransport = null;
        }

        /// <summary>
        /// Ouvre une socket client et lance la poignee de main.
        ///
        /// ⚠️ La resolution DNS se fait ICI, hors de la boucle de jeu :
        /// Dns.GetHostEntry BLOQUE, parfois plusieurs secondes. Appelee depuis
        /// le pas de simulation, elle figerait l'image.
        /// </summary>
        public void Connect(string host, int port, CollisionWorld predictionWorld, ConsoleSystem c)
        {
            NetAddress address = UdpTransport.Resolve(host, port);

            if (address.IsEmpty)
            {
                c.Print($"  hote introuvable : {host}");
                return;
            }

            try
            {
                // Port 0 : le systeme choisit. Reutiliser le port du serveur
                // ferait echouer l'ouverture quand on heberge et joue en meme
                // temps, sur la meme machine.
                ClientTransport = new UdpTransport(0);

                Client = new NetClient(ClientTransport)
                {
                    // ⚠️⚠️ LE CLIENT PREDIT DANS LE MEME DECOR QUE LE SERVEUR.
                    // C'est la condition de la prediction (3.5) : le client
                    // simule lui-meme, avec le MEME CharacterMotor et le MEME
                    // monde de collision. Un decor different ferait diverger la
                    // prediction en permanence, et le joueur serait tire en
                    // arriere plusieurs fois par seconde sans qu'on comprenne
                    // pourquoi.
                    //
                    // C'est le monde deja construit pour le jeu local. Des le
                    // Welcome, SandboxApp.SyncServerMap le confronte a la carte
                    // du serveur (empreinte du decor) : il la garde, charge celle
                    // du serveur, ou se deconnecte en disant pourquoi.
                    PredictionWorld = predictionWorld,
                };

                MapChecked = false;
                MapStatus = "en attente du serveur";

                Client.Connect(address, PlayerName);

                c.Print($"  connexion a {address}, depuis le port {ClientTransport.Port}");
            }
            catch (Exception ex)
            {
                Client = null;
                ClientTransport?.Dispose();
                ClientTransport = null;

                c.Print($"  connexion impossible : {ex.Message}");
            }
        }

        /// <summary>
        /// Tire en ligne : le serveur jugera (rembobinage, 3.7). L'origine est
        /// l'oeil du joueur PREDIT - le plus proche de ce que le serveur sait de
        /// lui, donc le moins susceptible d'etre refuse (MaxShotOriginDrift).
        /// </summary>
        /// <returns>Numero du tir, 0 si pas connecte.</returns>
        public uint Fire(Vector3 direction)
        {
            if (Client is not { State: NetClientState.Connecte } client)
                return 0;

            Vector3 eye = client.Predicted.Position + new Vector3(0f, client.Predicted.Height - 0.18f, 0f);

            return client.SendFire(eye, direction);
        }

        /// <summary>Le verdict d'un tir, en une ligne lisible.</summary>
        public static string Describe(in ShotReport shot)
        {
            if (shot.Rejected)
                return $"tir {shot.ShotId} : REFUSE par le serveur (origine trop loin du tireur)";

            if (!shot.Hit)
                return $"tir {shot.ShotId} : rate";

            return $"tir {shot.ShotId} : TOUCHE joueur {shot.VictimId} a {shot.Distance:0.0} m" +
                   (shot.RewindMattered ? " (le rembobinage a decide)" : "");
        }

        /// <summary>Ferme proprement serveur et client. Idempotent.</summary>
        public void Stop()
        {
            DropClient();

            Server?.Dispose();
            Server = null;
            ServerTransport?.Dispose();
            ServerTransport = null;
        }

        /// <summary>
        /// Un pas de reseau.
        ///
        /// ⚠️ APPELE AU PAS FIXE, pas au rendu. Le serveur consomme une entree
        /// par pas : lui en envoyer 300 par seconde en jetterait 236, et le
        /// debit montant serait cinq fois trop gros pour rien.
        /// </summary>
        /// <param name="time">Horloge du reseau : temps de simulation, jamais DateTime.Now.</param>
        /// <param name="afterPoll">
        /// Appele entre la reception et l'envoi. C'est la qu'on confronte la
        /// carte a celle du serveur : AVANT la premiere prediction, sinon le
        /// premier pas apres le Welcome predit dans l'ancien decor - mesure, une
        /// correction de 39 cm au demarrage de chaque connexion.
        /// </param>
        public void Step(float time, float dt, in MoveInput input, Action? afterPoll = null)
        {
            Server?.Step(time, dt);

            if (Client is null)
                return;

            Client.Poll(time);

            afterPoll?.Invoke();

            // La synchronisation a pu nous deconnecter (carte refusee).
            if (Client is null)
                return;

            // ⚠️ On n'envoie que CONNECTE. Envoyer pendant la poignee de main
            // ferait des paquets que le serveur jette, et masquerait l'echec de
            // connexion derriere du trafic.
            if (Client.State == NetClientState.Connecte)
            {
                Client.SendInput(input);

                // Une mesure d'aller-retour par seconde : assez pour afficher une
                // valeur utile, trop peu pour peser sur le debit.
                if (time >= _nextPing)
                {
                    Client.SendPing();
                    _nextPing = time + 1f;
                }
            }
        }
    }
}
