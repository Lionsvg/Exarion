// ===========================================================================
//  Console : le reseau (phase 3)
// ===========================================================================

using System;
using System.Globalization;
using Exarion.Core.Assets;
using Exarion.Core.Console;
using Exarion.Core.Net;
using Exarion.Core.Physics;
using Exarion.Game.Networking;

namespace Exarion.Game.Commands
{
    internal static class NetCommands
    {
        /// <param name="map">Carte que le serveur fera tourner : la carte courante, ou le decor integre.</param>
        /// <param name="level">Monde de collision du jeu local, dans lequel le client predit.</param>
        /// <param name="fixedDelta">Pas fixe de la simulation locale, que le serveur doit partager.</param>
        /// <param name="mapSource">D'ou vient la carte courante : le serveur annonce son NOM DE FICHIER.</param>
        /// <param name="aim">Direction de la vue, pour « net_fire ».</param>
        public static void Register(ConsoleSystem c, NetSession net, Func<MapData> map,
                                    Func<CollisionWorld> level, Func<float> fixedDelta, Func<string> mapSource,
                                    Func<System.Numerics.Vector3> aim)
        {
            c.Register("host", args =>
            {
                int port = args.Length >= 1 && int.TryParse(args[0], out int p) ? p : 27015;

                net.Host(port, fixedDelta(), map(), mapSource(), level(), c);
            }, "host [port] - heberge une partie (27015 par defaut) et s'y connecte");

            c.Register("connect", args =>
            {
                if (args.Length < 1)
                {
                    c.Print("  connect <hote> [port]");
                    return;
                }

                int port = args.Length >= 2 && int.TryParse(args[1], out int p) ? p : 27015;

                net.DropClient();
                net.Connect(args[0], port, level(), c);
            }, "connect <hote> [port] - rejoint un serveur");

            c.Register("disconnect", _ =>
            {
                net.Stop();
                c.Print("  reseau ferme");
            }, "Ferme la connexion et le serveur local");

            c.Register("net", _ =>
            {
                if (net.Server is null && net.Client is null)
                {
                    c.Print("  hors ligne. « host » pour heberger, « connect <ip> » pour rejoindre");
                    return;
                }

                if (net.Server is { } server)
                {
                    c.Print($"  SERVEUR  port {net.ServerTransport?.Port}  pas {server.Tick}  " +
                            $"{server.ClientCount} client(s)");
                    c.Print($"           emis {server.BytesSent / 1024.0:0.0} ko  " +
                            $"instantanes {server.SnapshotsSent}  " +
                            $"entrees {server.InputsApplied}  repetees {server.InputsRepeated}");

                    var names = server.PlayerNames();

                    for (int i = 0; i < names.Count; i++)
                        c.Print($"           joueur {i + 1} : {names[i]}");

                    // Les tirs ARBITRES : c'est ici qu'on mesure 3.7 en vrai -
                    // combien de touches le rembobinage a decidees.
                    c.Print($"           tirs {server.ShotsReceived}  touches {server.ShotsHit}  " +
                            $"rembobinage decisif {server.ShotsRewindDecisive}  refuses {server.ShotsRejected}");
                }

                if (net.Client is { } client)
                {
                    c.Print($"  CLIENT   {client.State}  id {client.LocalId}  carte « {client.ServerMap} »" +
                            $"  empreinte {client.ServerMapFingerprint:x8}");

                    // La decision de synchronisation de carte, telle quelle :
                    // gardee, chargee, ou la raison du refus.
                    if (net.MapStatus.Length > 0)
                        c.Print($"           {net.MapStatus}");

                    if (client.State == NetClientState.Refuse)
                        c.Print($"           refuse : {client.RejectReason}");

                    c.Print($"           pas serveur {client.ServerTick}  accuse {client.AckedInputTick}  " +
                            $"aller-retour {(client.RoundTrip >= 0 ? $"{client.RoundTrip * 1000.0:0} ms" : "-")}");

                    c.Print($"           prediction {(client.PredictionEnabled ? "ON" : "OFF")}  " +
                            $"corrections {client.Corrections}  rejeux {client.Replays}  " +
                            $"ecart {client.LastPredictionError * 100f:0.0} cm  " +
                            $"pire {client.WorstPredictionError * 100f:0.0} cm");

                    c.Print($"           instantanes recus {client.SnapshotsReceived}  " +
                            $"jetes {client.SnapshotsDiscarded}  " +
                            $"en tampon {client.BufferedSnapshots}  " +
                            $"emis {client.BytesSent / 1024.0:0.0} ko");

                    for (int i = 0; i < client.Players.Count; i++)
                    {
                        PlayerState s = client.Players[i];

                        c.Print($"           vu id {s.Id}{(s.Id == client.LocalId ? " (moi)" : "")}  " +
                                $"{s.Position.X:0.00} {s.Position.Y:0.00} {s.Position.Z:0.00}");
                    }

                    c.Print($"           tirs {client.ShotsSent}  verdicts {client.ShotReports}  touches {client.HitsConfirmed}" +
                            (client.ShotReports > 0 ? $"  dernier : {NetSession.Describe(client.LastShot)}" : ""));
                }
            }, "Etat du reseau : serveur, client, debit, aller-retour");

            c.Register("net_fire", _ =>
            {
                // ⚠️ Existe pour la VERIFICATION : le tir a la souris ne se
                // commande pas en headless. Avec « delay », on peut tirer APRES
                // la connexion : « delay 4 net_fire ».
                uint shot = net.Fire(aim());

                c.Print(shot == 0 ? "  pas connecte" : $"  tir {shot} envoye au serveur");
            }, "net_fire - tire une fois dans l'axe de la vue ; le serveur juge");

            c.Register("net_predict", args =>
            {
                if (net.Client is null) { c.Print("  hors ligne"); return; }

                if (args.Length >= 1
                    && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    net.Client.PredictionEnabled = v != 0f;

                // ⚠️ Existe pour COMPARER : couper la prediction en direct est
                // le seul moyen de sentir ce qu'elle apporte. Sans elle, chaque
                // pas se voit un aller-retour apres l'appui.
                c.Print($"  net_predict {(net.Client.PredictionEnabled ? 1 : 0)}");
            }, "net_predict [0|1] - coupe ou remet la prediction, pour comparer");

            c.Register("net_name", args =>
            {
                if (args.Length >= 1)
                    net.PlayerName = args[0];

                c.Print($"  net_name {net.PlayerName}");
            }, "net_name <nom> - nom annonce au serveur");
        }
    }
}
