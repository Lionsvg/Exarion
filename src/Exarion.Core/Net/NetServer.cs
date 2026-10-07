// ===========================================================================
//  Exarion - Serveur autoritaire (3.2 + 3.3)
//
//  ---------------------------------------------------------------------
//  CE QUE « AUTORITAIRE » VEUT DIRE CONCRETEMENT
//
//  Le serveur possede UN CharacterMotor par joueur, et c'est le seul endroit
//  du systeme ou ces motors avancent. Un client n'envoie que des ENTREES ;
//  il ne dit jamais ou il est.
//
//  Consequence directe : le serveur reutilise exactement le meme
//  CharacterMotor, la meme CollisionWorld et le meme pas fixe que le mode
//  solo. Il n'y a pas « la physique du solo » et « la physique du reseau » -
//  il n'y en a qu'une, testee depuis des semaines. Deux simulations auraient
//  divergé, et c'est le genre d'ecart qui se manifeste comme « en ligne ca ne
//  saute pas pareil ».
//  ---------------------------------------------------------------------
//
//  ⚠️ Ecrit dans Core : aucune socket. Le serveur tourne au-dessus d'un
//  INetTransport, donc un test peut le faire jouer contre trois clients avec
//  200 ms de latence et 5 % de perte, en quelques millisecondes et de facon
//  reproductible.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Entities;
using Exarion.Core.Physics;

namespace Exarion.Core.Net
{
    public sealed class NetServer : IDisposable
    {
        private sealed class Client
        {
            public NetAddress Address;
            public byte Id;
            public string Name = "";
            public CharacterMotor Motor = new();

            /// <summary>Derniere entree recue, rejouee tant qu'il n'en arrive pas de neuve.</summary>
            public MoveInput Input;

            /// <summary>Pas du client de la derniere entree appliquee.</summary>
            public uint LastInputTick;

            public double LastHeard;
            public bool WantsSnapshot;
        }

        private readonly INetTransport _transport;
        private readonly List<Client> _clients = new();
        private readonly byte[] _out = new byte[NetConfig.MaxPacketSize];

        private CollisionWorld _world = new();
        private MapData _map = new();
        private byte _nextId = 1;

        public NetServer(INetTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        /// <summary>Pas de simulation du serveur, en secondes.</summary>
        public float FixedDelta { get; set; } = 1f / 64f;

        /// <summary>Numero du pas courant. Sert d'horodatage aux instantanes.</summary>
        public uint Tick { get; private set; }

        public int ClientCount => _clients.Count;

        /// <summary>Octets emis depuis le demarrage. Pour MESURER le debit.</summary>
        public long BytesSent { get; private set; }
        public int SnapshotsSent { get; private set; }
        public int InputsApplied { get; private set; }

        /// <summary>Entrees manquantes rejouees a l'identique, faute de neuve.</summary>
        public int InputsRepeated { get; private set; }

        /// <summary>Le monde de collision. Les joueurs marchent dedans.</summary>
        public CollisionWorld World => _world;

        public MapData Map => _map;

        /// <summary>
        /// Charge la carte que le serveur fait tourner.
        ///
        /// ⚠️ C'est le serveur qui decide de la carte, pas le client. Sinon deux
        /// joueurs pourraient simuler des decors differents, et le serveur
        /// trancherait en faveur du sien sans que le client comprenne pourquoi
        /// il traverse un mur.
        /// </summary>
        public void LoadMap(MapData map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));

            // La liste de couleurs ne sert qu'au rendu ; le serveur n'affiche
            // rien. On la jette, mais on doit la fournir - c'est ce qui garantit
            // que le monde de collision et les couleurs restent alignes pour
            // ceux qui en ont besoin.
            _world = map.ToCollisionWorld(new List<Vector3>());

            // Les joueurs deja connectes repartent du point d'apparition : leur
            // position venait de l'ancien decor.
            foreach (Client client in _clients)
                client.Motor.Position = SpawnPoint();
        }

        /// <summary>
        /// Point d'apparition : la PREMIERE entite, sinon celui de la carte.
        ///
        /// ⚠️ Le premier, pas un tirage. Un tirage rendrait deux parties
        /// incomparables, et surtout il ferait apparaitre les joueurs a des
        /// endroits differents d'un test a l'autre.
        /// </summary>
        private Vector3 SpawnPoint()
        {
            foreach (MapEntity spawn in _map.SpawnPoints())
                return spawn.Position;

            return _map.Spawn;
        }

        /// <summary>
        /// Un pas de serveur : on lit le reseau, on simule, on publie.
        ///
        /// ⚠️ L'ORDRE EST OBLIGATOIRE. Simuler avant de lire les entrees ferait
        /// perdre un pas a chaque paquet - le joueur repondrait avec 15 ms de
        /// retard en plus, gratuitement.
        /// </summary>
        /// <param name="time">Horloge, en secondes. Fournie, jamais DateTime.Now.</param>
        public void Step(double time, float dt)
        {
            Receive(time);
            Simulate(dt);
            Broadcast(time);

            Tick++;
        }

        // -------------------------------------------------------------------
        //  RECEPTION
        // -------------------------------------------------------------------

        private void Receive(double time)
        {
            // ⚠️ EN BOUCLE jusqu'a vider la file. Traiter un paquet par pas
            // ferait grossir la file des qu'un client envoie plus vite que le
            // serveur ne lit, et le retard s'aggraverait tout seul.
            while (_transport.Receive(out NetPacket packet))
                Handle(packet, time);

            Timeout(time);
        }

        private void Handle(in NetPacket packet, double time)
        {
            var reader = new NetReader(packet.Data, packet.Length);
            byte type = reader.Byte();

            switch (type)
            {
                case NetMessage.Hello: OnHello(packet.From, ref reader, time); break;
                case NetMessage.Input: OnInput(packet.From, ref reader, time); break;
                case NetMessage.Bye: Remove(packet.From); break;
                case NetMessage.Ping: OnPing(packet.From, ref reader); break;

                // ⚠️ Un type inconnu est IGNORE, pas fatal. Le paquet peut venir
                // d'une autre version, d'un scanner de ports, ou de n'importe
                // qui : un serveur qui tombe sur un octet inattendu se fait
                // eteindre par le premier curieux.
                default: break;
            }
        }

        private void OnHello(NetAddress from, ref NetReader reader, double time)
        {
            ushort version = reader.UShort();
            string name = reader.ShortString();

            if (reader.Failed)
                return;

            Client? existing = Find(from);

            if (existing != null)
            {
                // ⚠️ UN HELLO REPETE N'EST PAS UNE ERREUR : le client reessaye
                // tant qu'il n'a pas recu le Welcome, et c'est justement le
                // Welcome qui a pu se perdre. On le renvoie sans rien recreer -
                // sinon un accuse perdu creerait un joueur fantome par tentative.
                existing.LastHeard = time;
                SendWelcome(existing);
                return;
            }

            if (version != NetConfig.Version)
            {
                SendReject(from, $"version {version} refusee, serveur en {NetConfig.Version}");
                return;
            }

            if (_clients.Count >= NetConfig.MaxPlayers)
            {
                SendReject(from, "serveur plein");
                return;
            }

            var client = new Client
            {
                Address = from,
                Id = _nextId++,
                Name = name.Length > 0 ? name : "joueur",
                LastHeard = time,
            };

            client.Motor.Position = SpawnPoint();

            _clients.Add(client);
            SendWelcome(client);
        }

        private void OnInput(NetAddress from, ref NetReader reader, double time)
        {
            Client? client = Find(from);

            if (client is null)
                return;   // pas connecte : on ignore, on ne cree pas

            client.LastHeard = time;

            byte count = reader.Byte();

            // ⚠️ On BORNE le nombre annonce avant de boucler. Un paquet
            // fabrique peut declarer 255 entrees : sans borne on lit 255 fois
            // hors du tampon. Failed nous protegerait, mais autant ne pas
            // dependre d'un filet de securite pour une borne evidente.
            int limit = Math.Min((int)count, NetConfig.InputRedundancy);

            for (int i = 0; i < limit; i++)
            {
                TimedInput timed = TimedInput.Read(ref reader);

                if (reader.Failed)
                    return;

                // ⚠️ ON NE GARDE QUE CE QUI EST PLUS RECENT. La redondance fait
                // arriver la meme entree jusqu'a huit fois, et UDP les livre
                // dans le desordre : appliquer une entree deja vue ferait
                // avancer le joueur deux fois, appliquer une ancienne le ferait
                // reculer. Le numero de pas tranche.
                if (timed.Tick <= client.LastInputTick)
                    continue;

                client.Input = TimedInput.Sanitize(timed.Input);
                client.LastInputTick = timed.Tick;
                client.WantsSnapshot = true;
                InputsApplied++;
            }
        }

        private void OnPing(NetAddress from, ref NetReader reader)
        {
            uint stamp = reader.UInt();

            if (reader.Failed)
                return;

            var writer = new NetWriter(_out);
            writer.Byte(NetMessage.Pong);

            // On renvoie l'horodatage TEL QUEL : c'est le client qui mesure, et
            // ainsi le serveur n'a aucune horloge a partager avec lui.
            writer.UInt(stamp);

            SendTo(from, ref writer);
        }

        private void Timeout(double time)
        {
            for (int i = _clients.Count - 1; i >= 0; i--)
            {
                if (time - _clients[i].LastHeard <= NetConfig.TimeoutSeconds)
                    continue;

                _clients.RemoveAt(i);
            }
        }

        // -------------------------------------------------------------------
        //  SIMULATION
        // -------------------------------------------------------------------

        private void Simulate(float dt)
        {
            foreach (Client client in _clients)
            {
                // ⚠️⚠️ ON REJOUE LA DERNIERE ENTREE QUAND IL N'EN ARRIVE PAS.
                // C'est contre-intuitif mais c'est la bonne reponse : un paquet
                // perdu ne doit pas figer le joueur d'un pas, ce qui se voit
                // comme un a-coup. Continuer sur sa lancee est ce qui ressemble
                // le plus a ce qu'il faisait. On COMPTE ces repetitions pour
                // pouvoir mesurer la qualite de la liaison.
                if (!client.WantsSnapshot)
                    InputsRepeated++;

                client.WantsSnapshot = false;

                client.Motor.Step(client.Input, dt, _world);
            }
        }

        // -------------------------------------------------------------------
        //  PUBLICATION
        // -------------------------------------------------------------------

        private void Broadcast(double time)
        {
            if (_clients.Count == 0)
                return;

            // ⚠️ L'INSTANTANE N'EST PAS IDENTIQUE POUR TOUS, et c'est ce qui
            // empeche de le serialiser une seule fois : chacun recoit son propre
            // identifiant et le dernier pas d'entree que le serveur a applique
            // DE SA PART. Sans ce numero, la reconciliation de 3.6 n'a aucun
            // moyen de savoir jusqu'ou le serveur l'a suivi.
            //
            // Le corps de la liste, lui, est bien commun.
            foreach (Client client in _clients)
            {
                var per = new NetWriter(_out);
                per.Byte(NetMessage.Snapshot);
                per.UInt(Tick);
                per.UInt(client.LastInputTick);
                per.Byte(client.Id);
                per.Byte((byte)_clients.Count);

                foreach (Client other in _clients)
                {
                    new PlayerState
                    {
                        Id = other.Id,
                        Position = other.Motor.Position,
                        Yaw = other.Input.Yaw,
                        Pitch = 0f,
                        Crouched = other.Motor.Stance == StanceState.Crouching,
                    }.Write(ref per);
                }

                // ⚠️ L'ETAT COMPLET DU DESTINATAIRE, en fin de paquet. C'est ce
                // dont il a besoin pour REJOUER ses entrees (3.6) : sa position
                // est deja dans la liste, mais pas sa vitesse ni sa hauteur.
                // Repartir d'une vitesse nulle ferait diverger le rejeu a chaque
                // correction, surtout en l'air.
                CharacterMotor.MotorState saved = client.Motor.Save();

                new LocalState
                {
                    Velocity = saved.Velocity,
                    Height = saved.Height,
                }.Write(ref per);

                if (per.Overflowed)
                    continue;

                SendTo(client.Address, ref per);
                SnapshotsSent++;
            }
        }

        // -------------------------------------------------------------------

        private void SendWelcome(Client client)
        {
            var writer = new NetWriter(_out);

            writer.Byte(NetMessage.Welcome);
            writer.UShort(NetConfig.Version);
            writer.Byte(client.Id);
            writer.Float(FixedDelta);
            writer.ShortString(_map.Name);
            writer.Vector3(client.Motor.Position);

            SendTo(client.Address, ref writer);
        }

        private void SendReject(NetAddress to, string reason)
        {
            var writer = new NetWriter(_out);

            writer.Byte(NetMessage.Reject);
            writer.ShortString(reason);

            SendTo(to, ref writer);
        }

        private void SendTo(NetAddress to, ref NetWriter writer)
        {
            if (writer.Overflowed)
                return;

            _transport.Send(to, _out, writer.Length);
            BytesSent += writer.Length;
        }

        private Client? Find(NetAddress address)
        {
            foreach (Client client in _clients)
                if (client.Address == address)
                    return client;

            return null;
        }

        private void Remove(NetAddress address)
        {
            for (int i = 0; i < _clients.Count; i++)
            {
                if (_clients[i].Address != address)
                    continue;

                _clients.RemoveAt(i);
                return;
            }
        }

        /// <summary>Etat courant d'un joueur, pour l'affichage et les tests.</summary>
        public bool TryGetPlayer(byte id, out PlayerState state)
        {
            foreach (Client client in _clients)
            {
                if (client.Id != id)
                    continue;

                state = new PlayerState
                {
                    Id = client.Id,
                    Position = client.Motor.Position,
                    Yaw = client.Input.Yaw,
                    Crouched = client.Motor.Stance == StanceState.Crouching,
                    Active = true,
                };

                return true;
            }

            state = default;
            return false;
        }

        /// <summary>Noms des joueurs connectes, dans l'ordre d'arrivee.</summary>
        public IReadOnlyList<string> PlayerNames()
        {
            var names = new List<string>(_clients.Count);

            foreach (Client client in _clients)
                names.Add(client.Name);

            return names;
        }

        public void Dispose()
        {
            // On previent les clients : sans ca ils attendent le timeout, dix
            // secondes a regarder un monde fige.
            var writer = new NetWriter(_out);
            writer.Byte(NetMessage.Bye);

            foreach (Client client in _clients)
                _transport.Send(client.Address, _out, writer.Length);

            _clients.Clear();
        }
    }
}
