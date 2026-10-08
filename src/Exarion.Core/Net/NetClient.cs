// ===========================================================================
//  Exarion - Client reseau (3.2 + 3.4)
//
//  ---------------------------------------------------------------------
//  POURQUOI LES AUTRES JOUEURS SONT AFFICHES DANS LE PASSE
//
//  Le serveur publie 64 instantanes par seconde. Le client, lui, dessine
//  souvent 300 images par seconde. Afficher le dernier instantane recu ferait
//  donc bouger les autres joueurs par SAUTS de 15 ms, et surtout ces sauts
//  seraient irreguliers : la gigue du reseau fait arriver les paquets a 12,
//  18, 14, 21 ms d'intervalle.
//
//  La parade est de dessiner l'etat d'il y a un peu plus d'un intervalle, et
//  d'INTERPOLER entre les deux instantanes qui encadrent cet instant. Le
//  mouvement devient continu, au prix d'un retard volontaire.
//
//  ⚠️⚠️ CE RETARD N'EST PAS UN DEFAUT, C'EST LE PRIX A PAYER, et il faut le
//  dire parce qu'on est tente de le reduire a zero. A zero, il n'y a plus
//  d'instantane futur pour interpoler : on EXTRAPOLE, donc on invente, et le
//  joueur distant part dans les murs des qu'il change de direction. Tous les
//  FPS competitifs paient ce retard - il vaut mieux voir la verite en retard
//  qu'un mensonge a l'heure.
//  ---------------------------------------------------------------------
//
//  ⚠️ LE JOUEUR LOCAL N'EST PAS INTERPOLE. Sa position vient telle quelle du
//  serveur ; l'interpoler ajouterait le retard a SES propres mouvements, ce
//  qui est exactement ce qu'un joueur ne supporte pas. C'est la prediction
//  (3.5) qui reglera son cas - elle n'est PAS faite.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Physics;

namespace Exarion.Core.Net
{
    public enum NetClientState
    {
        Deconnecte,
        EnConnexion,
        Connecte,
        Refuse,
    }

    public sealed class NetClient : IDisposable
    {
        private readonly struct Snapshot
        {
            public readonly uint Tick;
            public readonly double ReceivedAt;
            public readonly PlayerState[] Players;
            public readonly int Count;

            public Snapshot(uint tick, double receivedAt, PlayerState[] players, int count)
            {
                Tick = tick;
                ReceivedAt = receivedAt;
                Players = players;
                Count = count;
            }
        }

        private readonly INetTransport _transport;
        private readonly byte[] _out = new byte[NetConfig.MaxPacketSize];

        /// <summary>
        /// Les derniers instantanes recus, du plus ancien au plus recent.
        ///
        /// ⚠️ Une file BORNEE. Sans borne, un client qui ne consomme pas ferait
        /// grossir la liste indefiniment - la fuite se verrait apres une heure
        /// de jeu, donc jamais pendant un test.
        /// </summary>
        private readonly List<Snapshot> _snapshots = new();

        /// <summary>Entrees en attente d'accuse, renvoyees en redondance.</summary>
        private readonly List<TimedInput> _pending = new();

        // -------------------------------------------------------------------
        //  PREDICTION ET RECONCILIATION (3.5 / 3.6)
        //
        //  ---------------------------------------------------------------
        //  LE PROBLEME QUE CA RESOUT, ET POURQUOI IL EST INCONTOURNABLE
        //
        //  Sans prediction, appuyer sur « avancer » envoie une entree au
        //  serveur, qui simule, qui renvoie une position. Le joueur ne bouge
        //  donc qu'au bout d'un ALLER-RETOUR complet. A 60 ms de ping, chaque
        //  pas se voit 60 ms apres l'appui - et le jeu passe de « nerveux » a
        //  « pateux » sans qu'aucun chiffre du profileur ne bouge.
        //
        //  La prediction consiste a simuler SOI-MEME, tout de suite, avec sa
        //  propre entree. Le serveur reste autoritaire : quand sa reponse
        //  arrive, elle concerne un pas DEJA PASSE cote client.
        //
        //  D'ou la reconciliation : on remet le joueur a l'etat que le serveur
        //  dicte pour ce pas-la, puis on REJOUE toutes les entrees emises
        //  depuis. Si la prediction etait juste, on retombe exactement au meme
        //  endroit et rien ne bouge a l'ecran. Sinon, on est corrige.
        //  ---------------------------------------------------------------
        //
        //  ⚠️⚠️ LA PREDICTION EXIGE LA MEME PHYSIQUE DES DEUX COTES. Le client
        //  utilise le MEME CharacterMotor et le MEME CollisionWorld que le
        //  serveur - pas une approximation. Deux implementations divergeraient,
        //  et la correction se declencherait en permanence : le joueur serait
        //  tire en arriere plusieurs fois par seconde. C'est exactement ce que
        //  la regle n°1 du projet rend possible : la physique est du C# pur,
        //  donc le client et le serveur en partagent litteralement le code.
        // -------------------------------------------------------------------

        private readonly struct Recorded
        {
            public readonly uint Tick;
            public readonly MoveInput Input;
            public readonly Vector3 PredictedPosition;

            public Recorded(uint tick, MoveInput input, Vector3 predicted)
            {
                Tick = tick;
                Input = input;
                PredictedPosition = predicted;
            }
        }

        /// <summary>Entrees emises et leur resultat predit, pour le rejeu.</summary>
        private readonly List<Recorded> _history = new();

        /// <summary>Le motor local. Simule en avance sur le serveur.</summary>
        public CharacterMotor Predicted { get; } = new();

        /// <summary>
        /// Le decor dans lequel le client predit.
        ///
        /// ⚠️ DOIT etre le meme que celui du serveur. Le serveur annonce le nom
        /// de sa carte dans le Welcome ; c'est au jeu de la charger et de la
        /// poser ici. Un decor different ferait diverger la prediction en
        /// permanence, et le joueur serait corrige sans arret sans qu'on
        /// comprenne pourquoi.
        /// </summary>
        public CollisionWorld PredictionWorld { get; set; } = new();

        /// <summary>Active la prediction. Sans elle, on subit la latence complete.</summary>
        public bool PredictionEnabled { get; set; } = true;

        /// <summary>Corrections appliquees depuis la connexion.</summary>
        public int Corrections { get; private set; }

        /// <summary>Rejeux d'entrees effectues. Une correction en declenche plusieurs.</summary>
        public int Replays { get; private set; }

        /// <summary>Ecart entre la prediction et la verite du serveur, au dernier instantane.</summary>
        public float LastPredictionError { get; private set; }

        /// <summary>Le pire ecart observe. Sert a juger la qualite de la prediction.</summary>
        public float WorstPredictionError { get; private set; }

        /// <summary>
        /// En dessous de cet ecart, on ne corrige PAS la position affichee.
        ///
        /// ⚠️⚠️ CE SEUIL N'EST PAS DU CONFORT, IL EST NECESSAIRE. Le rejeu ne
        /// redonne jamais le bit pres : les flottants ne sont pas associatifs, et
        /// l'ordre des operations differe forcement un peu. Corriger sur un ecart
        /// d'un micrometre ferait donc sauter le joueur a CHAQUE instantane,
        /// soixante-quatre fois par seconde - un tremblement permanent qu'on
        /// attribuerait au rendu.
        /// </summary>
        public float CorrectionThreshold { get; set; } = 0.02f;

        /// <summary>
        /// Au-dela de cet ecart, on se replace d'un coup au lieu de lisser.
        ///
        /// Un gros ecart veut dire qu'on a ete pousse, teleporte, ou qu'on a
        /// perdu beaucoup de paquets. Le lisser ferait glisser le joueur a
        /// travers le decor pendant une seconde.
        /// </summary>
        public float SnapThreshold { get; set; } = 2f;

        /// <summary>
        /// Reste de correction a resorber, en metres.
        ///
        /// ⚠️ ON NE CORRIGE PAS LA POSITION SIMULEE, ON CORRIGE L'AFFICHAGE. La
        /// simulation, elle, part immediatement de la verite du serveur - sinon
        /// on predirait a partir d'un mensonge et l'erreur s'accumulerait. Ce
        /// decalage est purement visuel et fond en quelques dixiemes de seconde.
        /// </summary>
        private Vector3 _visualOffset;

        /// <summary>Position a AFFICHER pour le joueur local, correction lissee comprise.</summary>
        public Vector3 SmoothedPosition => Predicted.Position + _visualOffset;

        private NetAddress _server;
        private double _time;
        private double _nextHello;
        private uint _inputTick;
        private uint _pingStamp;
        private readonly Dictionary<uint, double> _pingSent = new();

        public NetClient(INetTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public NetClientState State { get; private set; } = NetClientState.Deconnecte;

        /// <summary>Identifiant attribue par le serveur. 0 tant qu'on n'est pas connecte.</summary>
        public byte LocalId { get; private set; }

        public string ServerMap { get; private set; } = "";

        /// <summary>Empreinte du CONTENU de la carte du serveur. Voir MapSync.</summary>
        public uint ServerMapFingerprint { get; private set; }

        public string RejectReason { get; private set; } = "";

        /// <summary>Dernier pas de serveur recu.</summary>
        public uint ServerTick { get; private set; }

        /// <summary>Dernier pas d'entree que le serveur declare avoir applique.</summary>
        public uint AckedInputTick { get; private set; }

        /// <summary>Aller-retour mesure, en secondes. -1 tant qu'aucun Pong n'est revenu.</summary>
        public double RoundTrip { get; private set; } = -1.0;

        /// <summary>
        /// Retard d'interpolation, en secondes.
        ///
        /// ⚠️ DEUX intervalles de serveur, pas un. Avec un seul, la moindre
        /// gigue fait manquer l'instantane suivant : on se retrouve sans borne
        /// haute, l'interpolation se fige, et ca se voit comme un micro-arret.
        /// Deux intervalles laissent le temps a un paquet en retard d'arriver.
        /// </summary>
        public double InterpolationDelay { get; set; } = 2.0 / 64.0;

        public int SnapshotsReceived { get; private set; }
        public int SnapshotsDiscarded { get; private set; }
        public long BytesSent { get; private set; }

        /// <summary>Nombre d'instantanes en tampon. Sert au diagnostic.</summary>
        public int BufferedSnapshots => _snapshots.Count;

        /// <summary>
        /// Etat interpole des joueurs, pret a etre affiche.
        ///
        /// Reconstruit a chaque Poll : c'est de l'affichage, pas de l'etat.
        /// </summary>
        public List<PlayerState> Players { get; } = new();

        public void Connect(NetAddress server, string name)
        {
            _server = server;
            State = NetClientState.EnConnexion;
            RejectReason = "";
            LocalId = 0;
            _nextHello = 0.0;
            _pendingName = name;

            _snapshots.Clear();
            _pending.Clear();
            Players.Clear();

            // ⚠️ L'historique de prediction aussi. Le garder ferait rejouer les
            // entrees de la partie precedente sur la position d'apparition de la
            // nouvelle - le joueur partirait tout seul des la connexion.
            _history.Clear();
            _visualOffset = Vector3.Zero;
            _playbackStarted = false;
            _lastPoll = -1.0;
        }

        private string _pendingName = "joueur";

        public void Disconnect()
        {
            if (State == NetClientState.Connecte)
            {
                var writer = new NetWriter(_out);
                writer.Byte(NetMessage.Bye);
                _transport.Send(_server, _out, writer.Length);
            }

            State = NetClientState.Deconnecte;
            LocalId = 0;
            _snapshots.Clear();
            _pending.Clear();
            Players.Clear();
        }

        /// <summary>
        /// Lit le reseau, entretient la connexion, et met Players a jour.
        /// </summary>
        /// <param name="time">Horloge, en secondes. Fournie, jamais DateTime.Now.</param>
        public void Poll(double time)
        {
            _time = time;

            while (_transport.Receive(out NetPacket packet))
                Handle(packet);

            // La poignee de main est le SEUL echange qu'on reessaye. Un
            // instantane perdu est remplace par le suivant ; un Welcome perdu,
            // non - sans lui on attendrait indefiniment.
            if (State == NetClientState.EnConnexion && time >= _nextHello)
            {
                SendHello();
                _nextHello = time + NetConfig.HelloRetrySeconds;
            }

            Interpolate();
        }

        /// <summary>
        /// Envoie une entree au serveur, avec les precedentes en redondance.
        ///
        /// A appeler UNE FOIS PAR PAS DE SIMULATION, pas par image : le serveur
        /// consomme une entree par pas, et lui en envoyer 300 par seconde ne
        /// ferait qu'en jeter 236.
        /// </summary>
        public void SendInput(in MoveInput input)
        {
            if (State != NetClientState.Connecte)
                return;

            _inputTick++;

            // ⚠️⚠️ ON SIMULE TOUT DE SUITE, AVANT MEME D'ENVOYER. C'est toute la
            // prediction : le joueur bouge a l'image ou il appuie, pas au bout
            // d'un aller-retour. Le serveur reste autoritaire - sa reponse
            // arrivera plus tard et concernera ce pas-ci.
            if (PredictionEnabled)
            {
                Predicted.Step(input, ServerStep, PredictionWorld);

                _history.Add(new Recorded(_inputTick, input, Predicted.Position));

                // ⚠️ L'historique est BORNE. Il ne sert qu'a rejouer depuis le
                // dernier pas accuse par le serveur ; au-dela c'est du passe que
                // personne ne redemandera jamais. Sans borne, une deconnexion
                // silencieuse le ferait grossir indefiniment.
                while (_history.Count > 256)
                    _history.RemoveAt(0);
            }

            _pending.Add(new TimedInput { Tick = _inputTick, Input = input });

            // ⚠️ On ne garde que les N dernieres. La redondance sert a combler un
            // trou recent ; renvoyer tout l'historique ferait grossir le paquet
            // jusqu'a la fragmentation IP, donc AUGMENTERAIT la perte.
            while (_pending.Count > NetConfig.InputRedundancy)
                _pending.RemoveAt(0);

            var writer = new NetWriter(_out);

            writer.Byte(NetMessage.Input);
            writer.Byte((byte)_pending.Count);

            for (int i = 0; i < _pending.Count; i++)
            {
                TimedInput timed = _pending[i];
                timed.Write(ref writer);
            }

            if (writer.Overflowed)
                return;

            _transport.Send(_server, _out, writer.Length);
            BytesSent += writer.Length;
        }

        // -------------------------------------------------------------------
        //  TIR ARBITRE (3.7, branche le 08/10)
        // -------------------------------------------------------------------

        private uint _shotId;

        /// <summary>
        /// Le pas SERVEUR - fractionnaire - que le joueur voit en ce moment :
        /// celui ou l'interpolation (3.4) place les autres joueurs. C'est
        /// l'instant que le serveur rembobinera pour juger un tir.
        ///
        /// ⚠️ Pas le dernier pas recu : le joueur voit les autres AVEC le
        /// retard d'interpolation. Juger sur le dernier pas, c'est juger sur une
        /// position qu'il n'a jamais vue.
        /// </summary>
        public double ViewTick => _playbackStarted && ServerStep > 0f ? _playback / ServerStep : ServerTick;

        public int ShotsSent { get; private set; }

        /// <summary>Verdicts recus du serveur. Le dernier est dans LastShot.</summary>
        public int ShotReports { get; private set; }

        public int HitsConfirmed { get; private set; }

        public ShotReport LastShot { get; private set; }

        /// <summary>
        /// Tire : le serveur jugera, et repondra par un HitReport.
        ///
        /// Le tir local du jeu (impacts, son) reste immediat - attendre
        /// l'aller-retour pour voir l'etincelle serait la latence complete que
        /// la prediction a justement supprimee. Seul le VERDICT vient du serveur.
        /// </summary>
        /// <returns>Le numero du tir, 0 si pas connecte.</returns>
        public uint SendFire(Vector3 origin, Vector3 direction)
        {
            if (State != NetClientState.Connecte)
                return 0;

            _shotId++;

            double view = Math.Max(0.0, ViewTick);
            uint whole = (uint)Math.Floor(view);

            var writer = new NetWriter(_out);

            writer.Byte(NetMessage.Fire);
            writer.UInt(_shotId);
            writer.UInt(whole);
            writer.Float((float)(view - whole));
            writer.Vector3(origin);
            writer.Vector3(direction);

            if (writer.Overflowed)
                return 0;

            _transport.Send(_server, _out, writer.Length);
            BytesSent += writer.Length;
            ShotsSent++;

            return _shotId;
        }

        private void OnHitReport(ref NetReader reader)
        {
            uint shot = reader.UInt();
            byte victim = reader.Byte();
            Vector3 point = reader.Vector3();
            float distance = reader.Float();
            byte flags = reader.Byte();

            if (reader.Failed)
                return;

            LastShot = new ShotReport(shot, victim, point, distance, flags);
            ShotReports++;

            if (victim != 0)
                HitsConfirmed++;
        }

        /// <summary>Envoie une mesure d'aller-retour.</summary>
        public void SendPing()
        {
            if (State != NetClientState.Connecte)
                return;

            _pingStamp++;

            // ⚠️ On borne la table : sans reponse, elle grossirait a l'infini.
            if (_pingSent.Count > 64)
                _pingSent.Clear();

            _pingSent[_pingStamp] = _time;

            var writer = new NetWriter(_out);
            writer.Byte(NetMessage.Ping);
            writer.UInt(_pingStamp);

            _transport.Send(_server, _out, writer.Length);
            BytesSent += writer.Length;
        }

        // -------------------------------------------------------------------

        private void SendHello()
        {
            var writer = new NetWriter(_out);

            writer.Byte(NetMessage.Hello);
            writer.UShort(NetConfig.Version);
            writer.ShortString(_pendingName);

            _transport.Send(_server, _out, writer.Length);
            BytesSent += writer.Length;
        }

        private void Handle(in NetPacket packet)
        {
            // ⚠️ ON IGNORE TOUT CE QUI NE VIENT PAS DU SERVEUR. Sans ce filtre,
            // n'importe qui connaissant le port peut envoyer un instantane et
            // teleporter le joueur. Ce n'est pas de la securite complete - il
            // faudrait un jeton - mais c'est le minimum, et son absence serait
            // une porte ouverte.
            if (packet.From != _server)
                return;

            var reader = new NetReader(packet.Data, packet.Length);
            byte type = reader.Byte();

            switch (type)
            {
                case NetMessage.Welcome: OnWelcome(ref reader); break;
                case NetMessage.Reject: OnReject(ref reader); break;
                case NetMessage.Snapshot: OnSnapshot(ref reader); break;
                case NetMessage.Pong: OnPong(ref reader); break;
                case NetMessage.HitReport: OnHitReport(ref reader); break;

                case NetMessage.Bye:
                    State = NetClientState.Deconnecte;
                    RejectReason = "le serveur a ferme";
                    break;

                default: break;
            }
        }

        private void OnWelcome(ref NetReader reader)
        {
            ushort version = reader.UShort();
            byte id = reader.Byte();
            float step = reader.Float();
            string map = reader.ShortString();
            uint fingerprint = reader.UInt();
            Vector3 spawn = reader.Vector3();

            if (reader.Failed || version != NetConfig.Version)
                return;

            // Un Welcome recu deux fois ne doit RIEN reinitialiser : le premier
            // a pu etre suivi de plusieurs pas de jeu, et repartir de zero
            // effacerait les instantanes en tampon pour rien.
            if (State == NetClientState.Connecte && LocalId == id)
                return;

            LocalId = id;
            ServerMap = map;
            ServerMapFingerprint = fingerprint;
            ServerStep = step;
            Spawn = spawn;
            State = NetClientState.Connecte;

            _inputTick = 0;
            _pending.Clear();

            // ⚠️ Le motor predit part du point d'apparition ANNONCE PAR LE
            // SERVEUR, pas de l'origine du monde. Sans ca, la premiere
            // prediction se fait depuis (0,0,0) - souvent dans le sol - et la
            // premiere reconciliation corrige de plusieurs dizaines de metres.
            Predicted.Restore(new CharacterMotor.MotorState(
                spawn, Vector3.Zero, Predicted.Settings.StandHeight));

            _history.Clear();
            _visualOffset = Vector3.Zero;
        }

        /// <summary>Pas de simulation annonce par le serveur.</summary>
        public float ServerStep { get; private set; } = 1f / 64f;

        /// <summary>Position d'apparition annoncee par le serveur.</summary>
        public Vector3 Spawn { get; private set; }

        private void OnReject(ref NetReader reader)
        {
            RejectReason = reader.ShortString();
            State = NetClientState.Refuse;
        }

        private void OnSnapshot(ref NetReader reader)
        {
            uint tick = reader.UInt();
            uint acked = reader.UInt();
            byte localId = reader.Byte();
            byte count = reader.Byte();

            if (reader.Failed)
                return;

            // ⚠️⚠️ ON JETTE TOUT INSTANTANE PLUS ANCIEN QUE LE DERNIER RECU.
            // UDP livre dans le desordre : sans ce test, un paquet en retard
            // ferait RECULER tout le monde d'une image, ce qui se voit comme un
            // tremblement - le defaut typique qu'on met des semaines a
            // attribuer au reseau plutot qu'au rendu.
            if (SnapshotsReceived > 0 && tick <= ServerTick)
            {
                SnapshotsDiscarded++;
                return;
            }

            int limit = Math.Min((int)count, NetConfig.MaxPlayers);
            var players = new PlayerState[limit];

            for (int i = 0; i < limit; i++)
            {
                players[i] = PlayerState.Read(ref reader);

                if (reader.Failed)
                    return;   // paquet tronque : on ne garde rien de partiel
            }

            // L'etat complet du destinataire ferme le paquet.
            LocalState local = LocalState.Read(ref reader);

            if (reader.Failed)
                return;

            ServerTick = tick;
            AckedInputTick = acked;

            if (localId != 0)
                LocalId = localId;

            SnapshotsReceived++;

            _snapshots.Add(new Snapshot(tick, _time, players, limit));

            // Deux secondes de tampon suffisent largement a interpoler ; au-dela
            // c'est de la memoire perdue.
            while (_snapshots.Count > 128)
                _snapshots.RemoveAt(0);

            // Les entrees que le serveur a deja appliquees n'ont plus besoin
            // d'etre renvoyees : c'est ce qui garde le paquet d'entree petit.
            _pending.RemoveAll(p => p.Tick <= acked);

            Reconcile(players, limit, local, acked);
        }

        // -------------------------------------------------------------------
        //  RECONCILIATION (3.6)
        // -------------------------------------------------------------------

        private void Reconcile(PlayerState[] players, int count, in LocalState local, uint acked)
        {
            if (!PredictionEnabled || LocalId == 0)
                return;

            // La verite du serveur sur NOTRE joueur.
            Vector3 authoritative = Vector3.Zero;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                if (players[i].Id != LocalId)
                    continue;

                authoritative = players[i].Position;
                found = true;
                break;
            }

            if (!found)
                return;

            // Ce qu'on avait PREDIT pour ce pas-la. ⚠️ Sans cette recherche, on
            // comparerait la verite d'un pas passe a notre position ACTUELLE,
            // qui est en avance de tout un aller-retour : la correction serait
            // enorme et permanente.
            int index = -1;

            for (int i = 0; i < _history.Count; i++)
            {
                if (_history[i].Tick != acked)
                    continue;

                index = i;
                break;
            }

            if (index < 0)
            {
                // ⚠️⚠️ TROIS CAS TRES DIFFERENTS SE CACHENT ICI, et les confondre
                // casse toute la reconciliation. Premiere version livree : je
                // vidais l'historique dans tous les cas. Consequence mesuree, et
                // c'est instructif parce que le symptome ne ressemble pas a la
                // cause - le joueur revenait bien sur la verite du serveur (le
                // test de convergence passait !), mais le compteur de
                // corrections restait a ZERO et le client se retrouvait EN
                // RETARD sur le serveur au lieu d'etre en avance. Autrement dit
                // la prediction ne servait plus a rien, en silence.

                // 1. Le serveur n'a encore rien applique de notre part. C'est le
                //    cas normal des premieres images : il n'y a rien a
                //    reconcilier, et surtout rien a jeter.
                if (acked == 0)
                    return;

                // 2. Il accuse un pas que nous avons DEJA elague, donc deja
                //    reconcilie. L'information est perimee : on l'ignore.
                if (_history.Count > 0 && acked < _history[0].Tick)
                    return;

                // 3. Il accuse un pas dont nous n'avons plus trace du tout : on
                //    a vraiment decroche. La, et seulement la, on se remet a la
                //    verite sans rejeu possible.
                Predicted.Restore(new CharacterMotor.MotorState(authoritative, local.Velocity, local.Height));
                _history.Clear();
                _visualOffset = Vector3.Zero;
                return;
            }

            LastPredictionError = Vector3.Distance(_history[index].PredictedPosition, authoritative);

            if (LastPredictionError > WorstPredictionError)
                WorstPredictionError = LastPredictionError;

            // ⚠️ Tout ce qui precede le pas accuse est du passe DEFINITIF : le
            // serveur l'a deja traite, il ne le remettra jamais en cause.
            _history.RemoveRange(0, index + 1);

            if (LastPredictionError <= CorrectionThreshold)
                return;   // la prediction etait juste : on ne touche a rien

            Corrections++;

            // Position affichee AVANT correction : c'est elle qu'on veut voir
            // conservee a l'ecran, pour que la correction ne se voie pas.
            Vector3 before = Predicted.Position;

            // On repart de la VERITE, puis on rejoue tout ce que le serveur
            // n'avait pas encore vu au moment ou il a produit cet instantane.
            Predicted.Restore(new CharacterMotor.MotorState(authoritative, local.Velocity, local.Height));

            for (int i = 0; i < _history.Count; i++)
            {
                Predicted.Step(_history[i].Input, ServerStep, PredictionWorld);
                Replays++;

                // On reecrit la prediction : le prochain instantane comparera a
                // celle-ci, pas a l'ancienne. Sans ca, une correction unique
                // ferait diverger toutes les suivantes.
                _history[i] = new Recorded(_history[i].Tick, _history[i].Input, Predicted.Position);
            }

            // ⚠️⚠️ LA CORRECTION EST ABSORBEE PAR L'AFFICHAGE, PAS PAR LA
            // SIMULATION. La simulation part de la verite - c'est le principe.
            // Mais si on affichait ce saut tel quel, le joueur se verrait tire
            // en arriere a chaque paquet. On garde donc l'ecart comme un
            // decalage VISUEL, qu'on resorbe en quelques dixiemes de seconde.
            Vector3 jump = before - Predicted.Position;

            _visualOffset = jump.Length() > SnapThreshold ? Vector3.Zero : _visualOffset + jump;
        }

        /// <summary>
        /// Resorbe le decalage visuel laisse par la derniere correction.
        ///
        /// A appeler au rendu, avec le temps ecoule : la vitesse de resorption
        /// ne doit pas dependre du nombre d'images par seconde.
        /// </summary>
        public void SmoothCorrection(float dt)
        {
            if (_visualOffset == Vector3.Zero)
                return;

            // Resorption exponentielle : rapide au debut, imperceptible a la
            // fin. Un retour lineaire s'arreterait net et se verrait.
            float keep = MathF.Exp(-dt * 12f);

            _visualOffset *= keep;

            // En dessous du millimetre, on solde : sinon on traine un residu
            // infinitesimal pour toujours, et l'egalite a zero n'arrive jamais.
            if (_visualOffset.LengthSquared() < 1e-6f)
                _visualOffset = Vector3.Zero;
        }

        private void OnPong(ref NetReader reader)
        {
            uint stamp = reader.UInt();

            if (reader.Failed || !_pingSent.TryGetValue(stamp, out double sent))
                return;

            RoundTrip = _time - sent;
            _pingSent.Remove(stamp);
        }

        // -------------------------------------------------------------------
        //  INTERPOLATION (3.4)
        // -------------------------------------------------------------------

        /// <summary>
        /// Horloge de LECTURE, en temps SERVEUR (secondes).
        ///
        /// ⚠️⚠️ C'EST LA CORRECTION D'UNE CONCEPTION FAIBLE, et elle merite
        /// d'etre expliquee parce que la premiere version paraissait juste.
        ///
        /// J'interpolais d'abord sur l'HEURE D'ARRIVEE des paquets. Ca marche
        /// sur un reseau parfait et ca se degrade tres mal : avec 15 ms de
        /// gigue, l'ecart entre deux arrivees varie du simple au triple, donc la
        /// VITESSE APPARENTE du joueur distant varie d'autant. Mesure : le plus
        /// grand pas observe faisait 6,3 fois le pas moyen (111 mm contre 18).
        /// A l'oeil, c'est un joueur qui accelere et ralentit sans raison.
        ///
        /// Ici l'horloge avance avec le TEMPS LOCAL et se recale doucement sur
        /// le dernier instantane recu. Les instantanes sont ranges par leur pas
        /// SERVEUR, qui est regulier par construction. La gigue n'influence donc
        /// plus que la qualite du recalage, jamais la vitesse affichee.
        /// </summary>
        private double _playback;
        private double _lastPoll = -1.0;
        private bool _playbackStarted;

        /// <summary>Ecart courant entre l'horloge de lecture et le dernier instantane.</summary>
        public double PlaybackError { get; private set; }

        private void Interpolate()
        {
            Players.Clear();

            if (_snapshots.Count == 0)
                return;

            double dt = _lastPoll < 0.0 ? 0.0 : Math.Max(0.0, _time - _lastPoll);
            _lastPoll = _time;

            // Temps SERVEUR du dernier instantane recu. Le pas serveur est
            // regulier, donc cette echelle est propre - contrairement aux
            // heures d'arrivee.
            double newest = _snapshots[^1].Tick * (double)ServerStep;
            double desired = newest - InterpolationDelay;

            if (!_playbackStarted)
            {
                _playback = desired;
                _playbackStarted = true;
            }
            else
            {
                _playback += dt;

                PlaybackError = desired - _playback;

                // ⚠️ Un ecart enorme veut dire qu'on a decroche (pause,
                // changement de carte, longue coupure) : on se recale d'un coup.
                // Le rattraper doucement ferait defiler le passe au ralenti
                // pendant plusieurs secondes.
                if (Math.Abs(PlaybackError) > 0.5)
                {
                    _playback = desired;
                }
                else
                {
                    // Sinon on corrige une FRACTION de l'ecart, proportionnelle
                    // au temps ecoule : ainsi le rattrapage ne depend pas du
                    // nombre d'images par seconde. Sans ce lissage, l'horloge
                    // sauterait a chaque arrivee de paquet - exactement le
                    // defaut qu'on vient de corriger.
                    _playback += PlaybackError * Math.Clamp(dt * 4.0, 0.0, 1.0);
                }
            }

            double target = _playback;

            // On cherche les deux instantanes qui ENCADRENT l'instant vise, sur
            // l'echelle du TEMPS SERVEUR.
            int after = -1;

            for (int i = 0; i < _snapshots.Count; i++)
            {
                if (_snapshots[i].Tick * (double)ServerStep < target)
                    continue;

                after = i;
                break;
            }

            if (after <= 0)
            {
                // Pas encore d'encadrement : soit on vient de se connecter, soit
                // le reseau est en retard. On montre le plus proche disponible
                // plutot que rien - un joueur invisible est pire qu'un joueur en
                // retard, et ca ne dure qu'un instant.
                Snapshot only = _snapshots[after == 0 ? 0 : ^1];

                for (int i = 0; i < only.Count; i++)
                    Players.Add(only.Players[i]);

                return;
            }

            Snapshot a = _snapshots[after - 1];
            Snapshot b = _snapshots[after];

            double span = (b.Tick - a.Tick) * (double)ServerStep;
            float t = span > 1e-9 ? (float)((target - a.Tick * (double)ServerStep) / span) : 0f;
            t = Math.Clamp(t, 0f, 1f);

            for (int i = 0; i < b.Count; i++)
            {
                PlayerState state = b.Players[i];

                // On cherche le MEME joueur dans l'instantane precedent, par
                // identifiant. ⚠️ Pas par indice : un joueur qui se deconnecte
                // decale toute la liste, et on interpolerait alors la position
                // d'un joueur vers celle d'un autre - un glissement instantane a
                // travers la carte.
                if (TryFind(a, state.Id, out PlayerState previous))
                {
                    state.Position = Vector3.Lerp(previous.Position, state.Position, t);
                    state.Yaw = LerpAngle(previous.Yaw, state.Yaw, t);
                    state.Pitch = LerpAngle(previous.Pitch, state.Pitch, t);
                }

                Players.Add(state);
            }

            // On jette ce qui est trop vieux pour servir encore. ⚠️ On garde
            // toujours l'instantane qui PRECEDE l'instant vise : c'est la borne
            // basse de l'interpolation, la jeter figerait l'affichage.
            while (_snapshots.Count > 2 && _snapshots[1].Tick * (double)ServerStep < target)
                _snapshots.RemoveAt(0);
        }

        private static bool TryFind(in Snapshot snapshot, byte id, out PlayerState state)
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot.Players[i].Id != id)
                    continue;

                state = snapshot.Players[i];
                return true;
            }

            state = default;
            return false;
        }

        /// <summary>
        /// Interpole deux angles par le plus COURT chemin.
        ///
        /// ⚠️ Un lerp direct entre 359 et 1 degre fait faire un tour complet a
        /// l'envers. Sur une orientation de joueur, ca se voit comme une
        /// pirouette instantanee chaque fois qu'il traverse le nord.
        /// </summary>
        public static float LerpAngle(float from, float to, float t)
        {
            float delta = to - from;

            while (delta > MathF.PI) delta -= MathF.Tau;
            while (delta < -MathF.PI) delta += MathF.Tau;

            return from + delta * t;
        }

        /// <summary>Etat d'un joueur par identifiant, apres interpolation.</summary>
        public bool TryGetPlayer(byte id, out PlayerState state)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].Id != id)
                    continue;

                state = Players[i];
                return true;
            }

            state = default;
            return false;
        }

        public void Dispose() => Disconnect();
    }
}
