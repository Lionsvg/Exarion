// ===========================================================================
//  L'interface du bac a sable EN PARTIE
//
//  ⚠️ TOUT CE QUI EST AFFICHE ICI L'EST POUR QU'UNE CAPTURE SE MESURE. Les
//  --exec partent tous a t=0 et il n'y a personne devant l'ecran en headless :
//  sans ces chiffres, une capture montrant une porte, un garde ou deux
//  personnages ne prouverait pas qu'ils bougent, ni qu'ils viennent du reseau.
// ===========================================================================

using System.Numerics;
using Exarion.Core;
using Exarion.Core.Entities;
using Exarion.Core.Net;
using Exarion.Engine.Graphics;
using Exarion.Game.Gameplay;
using Exarion.Game.Networking;
using Exarion.Game.Rendering;
using Exarion.Game.World;

namespace Exarion.Game.Ui
{
    internal sealed class SandboxHud
    {
        private readonly GameWorld _world;
        private readonly PlayerController _player;
        private readonly NetSession _net;
        private readonly EventFeed _events;
        private readonly RenderSettings _settings;

        public SandboxHud(GameWorld world, PlayerController player, NetSession net,
                          EventFeed events, RenderSettings settings)
        {
            _world = world;
            _player = player;
            _net = net;
            _events = events;
            _settings = settings;
        }

        /// <param name="consoleOpen">Console ouverte : pas de viseur par-dessus le texte.</param>
        public void Draw(HudRenderer hud, int w, int h, float simTime, bool consoleOpen)
        {
            var motor = _world.Motor;
            var physics = _world.Physics;

            if (!consoleOpen)
            {
                float spreadDeg = Ballistics.GetSpread(_player.Weapon, motor.Stance);
                hud.Crosshair(4f + spreadDeg * 26f, 7f, 2f, new Vector4(0.25f, 1f, 0.35f, 0.9f));

                if (simTime < _player.HitMarkerUntil)
                    hud.HitMarker(11f, 2f, new Vector4(1f, 0.9f, 0.3f, 0.95f));
            }

            if (!_settings.ShowStats)
                return;

            int awake = 0;
            for (int i = 0; i < physics.Bodies.Count; i++)
                if (!physics.Bodies[i].Sleeping) awake++;

            var dim = new Vector4(0.72f, 0.76f, 0.82f, 0.95f);
            var hot = new Vector4(1f, 0.75f, 0.35f, 0.95f);

            hud.TextBoxed(20f, h - 96f, 2f,
                $"CAISSES {physics.Bodies.Count}   ACTIVES {awake}", awake > 0 ? hot : dim);

            // ⚠️ L'ALTITUDE EST AFFICHEE POUR QU'UNE CAPTURE SE MESURE.
            // C'est le seul moyen de prouver qu'un ascenseur porte vraiment son
            // passager : on ne peut pas relire la position du joueur en console
            // une fois la plateforme arrivee.
            hud.TextBoxed(20f, h - 62f, 2f,
                $"VITESSE {motor.HorizontalSpeed:0.0} M/S   {motor.Stance.ToString().ToUpperInvariant()}" +
                $"   Y {motor.Position.Y:0.00}", dim);

            hud.TextBoxed(20f, h - 28f, 2f, "F1 CONSOLE", new Vector4(0.5f, 0.85f, 0.6f, 0.9f));

            // Checkpoints franchis et dernier declenchement : sans ca, un
            // cablage qui marche est indiscernable d'un cablage muet.
            var checkpoints = _world.Entities.Checkpoints;

            if (checkpoints.Count > 0)
                hud.TextBoxed(20f, 20f, 2f,
                    $"CHECKPOINTS {checkpoints.Count} : " +
                    string.Join(" > ", checkpoints).ToUpperInvariant(),
                    new Vector4(0.45f, 0.85f, 1f, 0.95f));

            if (simTime < _events.LastEventUntil && _events.LastEvent.Length > 0)
                hud.TextBoxed(20f, 54f, 2f, _events.LastEvent.ToUpperInvariant(),
                    new Vector4(1f, 0.85f, 0.4f, 0.95f));

            DrawNetwork(hud, w, h, dim);
            DrawDoors(hud, w, dim, hot);
            DrawAgents(hud);
        }

        private void DrawNetwork(HudRenderer hud, int w, int h, Vector4 dim)
        {
            if (_net.Client is not { } netClient)
                return;

            var netColor = new Vector4(0.5f, 0.8f, 1f, 0.95f);

            hud.TextBoxed(20f, h - 130f, 1.8f,
                $"RESEAU {netClient.State.ToString().ToUpperInvariant()}  ID {netClient.LocalId}  " +
                $"PAS {netClient.ServerTick}  " +
                $"RTT {(netClient.RoundTrip >= 0 ? $"{netClient.RoundTrip * 1000.0:0} MS" : "-")}  " +
                $"VUS {netClient.Players.Count}",
                netColor);

            // ⚠️ L'etat de la PREDICTION sur le HUD : un ecart qui grimpe ou des
            // corrections qui s'emballent se voient ici avant de se sentir a la
            // manette.
            hud.TextBoxed(20f, h - 182f, 1.8f,
                $"PREDICTION {(netClient.PredictionEnabled ? "ON" : "OFF")}  " +
                $"CORR {netClient.Corrections}  " +
                $"ECART {netClient.LastPredictionError * 100f:0.0} CM  " +
                $"PIRE {netClient.WorstPredictionError * 100f:0.0} CM",
                netColor);

            if (_net.Server is { } netServer)
                hud.TextBoxed(20f, h - 156f, 1.8f,
                    $"SERVEUR  {netServer.ClientCount} CLIENT(S)  PAS {netServer.Tick}  " +
                    $"{netServer.BytesSent / 1024} KO EMIS",
                    netColor);

            // Le dernier VERDICT du serveur sur un tir : la seule preuve visible
            // que le tir a ete arbitre, et pas seulement joue en local.
            if (netClient.ShotReports > 0)
                hud.TextBoxed(20f, h - 208f, 1.8f,
                    NetSession.Describe(netClient.LastShot).ToUpperInvariant(),
                    netClient.LastShot.Hit ? new Vector4(1f, 0.6f, 0.45f, 0.95f) : netColor);

            // Position de chaque joueur : c'est ce qui rend la capture
            // verifiable au chiffre.
            int line = 0;

            for (int i = 0; i < netClient.Players.Count && line < 4; i++)
            {
                PlayerState s = netClient.Players[i];

                hud.TextBoxed(w - 340f, 130f + line * 26f, 1.7f,
                    $"{(s.Id == netClient.LocalId ? "MOI" : "JOUEUR " + s.Id)}  " +
                    $"{s.Position.X:0.00} {s.Position.Y:0.00} {s.Position.Z:0.00}",
                    s.Id == netClient.LocalId ? dim : netColor);

                line++;
            }
        }

        /// <summary>
        /// Etat de chaque porte : OU en est le mouvement, pas seulement qu'il a
        /// eu lieu. Le pourcentage est ce qui distingue « ouverte » de
        /// « bloquee a 40 % ».
        /// </summary>
        private void DrawDoors(HudRenderer hud, int w, Vector4 dim, Vector4 hot)
        {
            var doors = _world.Movers.Movers;

            for (int i = 0; i < doors.Count && i < 4; i++)
            {
                Mover m = doors[i];

                hud.TextBoxed(w - 330f, 20f + i * 26f, 1.7f,
                    $"{(m.Name.Length > 0 ? m.Name : "PORTE " + i).ToUpperInvariant()}  " +
                    $"{m.State.ToString().ToUpperInvariant()}  {m.Fraction * 100f:0} %  Y {m.Center.Y:0.00}",
                    m.State is MoverState.Ouverture or MoverState.Fermeture ? hot : dim);
            }
        }

        /// <summary>
        /// Etat de chaque PNJ : sans ces chiffres, on ne peut pas verifier
        /// qu'un garde a bien avance entre deux instants.
        ///
        /// ⚠️ L'ALTITUDE Y EST, et ce n'est pas du remplissage : c'est le seul
        /// chiffre qui prouve sur une capture qu'un garde a MONTE un escalier
        /// (E16). L'ascenseur de E17 n'a ete trouve casse que parce que le HUD
        /// du joueur affichait la sienne.
        /// </summary>
        private void DrawAgents(HudRenderer hud)
        {
            var agents = _world.Agents.Agents;

            for (int i = 0; i < agents.Count && i < 6; i++)
            {
                Agent a = agents[i];

                string nav = a.NavMode switch
                {
                    AgentNavMode.Path => $"  CHEMIN {a.PathCursor}/{a.Path.Count - 1}",
                    AgentNavMode.Approach => "  INACCESSIBLE",
                    AgentNavMode.OffGrid => "  HORS GRILLE",
                    _ => "",
                };

                hud.TextBoxed(20f, 88f + i * 26f, 1.7f,
                    $"{(a.Name.Length > 0 ? a.Name : "PNJ " + i).ToUpperInvariant()}  " +
                    $"{a.State.ToString().ToUpperInvariant()}  " +
                    $"{(a.SeesPlayer ? "VOIT " : "")}" +
                    $"{a.Position.X:0.0} {a.Position.Y:0.0} {a.Position.Z:0.0}  {a.Speed:0.0} M/S  PT {a.Waypoint}{nav}",
                    a.State == AgentState.Chase
                        ? new Vector4(1f, 0.55f, 0.45f, 0.95f)
                        : new Vector4(0.7f, 0.85f, 0.7f, 0.9f));
            }
        }
    }
}
