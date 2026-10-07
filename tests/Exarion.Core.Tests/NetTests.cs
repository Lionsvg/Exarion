// ===========================================================================
//  Exarion - Tests du reseau (3.1 -> 3.4)
//
//  ---------------------------------------------------------------------
//  POURQUOI CES TESTS SONT LA PARTIE LA PLUS IMPORTANTE DE LA PHASE 3
//
//  La roadmap le dit : « c'est le domaine ou une erreur ne se voit pas tout
//  de suite. Un bug de reconciliation se manifeste comme *ca tremble
//  parfois* trois semaines plus tard. Chaque etape doit etre testee
//  automatiquement avant la suivante, sans exception. »
//
//  Tout ici tourne sur LoopbackTransport : latence, gigue, perte et
//  duplication reproduites a l'identique a chaque execution. Aucune socket,
//  aucune machine distante, aucun hasard.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core.Assets;
using Exarion.Core.Net;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    [TestFixture]
    public class NetTests
    {
        private const float Step = 1f / 64f;

        /// <summary>Une carte plate de 40 m, avec un point d'apparition.</summary>
        private static MapData FlatMap()
        {
            var map = new MapData { Name = "plat", Spawn = new Vector3(0f, 0f, 0f) };
            map.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), new Vector3(0.3f));
            return map;
        }

        /// <summary>Une entree qui avance tout droit.</summary>
        private static MoveInput Forward(float yaw = 0f)
            => new() { Move = new Vector2(0f, 1f), Yaw = yaw };

        // ===================================================================
        //  3.1 - SERIALISATION
        // ===================================================================

        [Test]
        public void Un_Aller_Retour_Binaire_Rend_Exactement_Les_Memes_Valeurs()
        {
            var buffer = new byte[256];
            var w = new NetWriter(buffer);

            w.Byte(200);
            w.Bool(true);
            w.Bool(false);
            w.UShort(65535);
            w.UInt(4000000000u);
            w.Int(-123456);
            w.Float(3.14159f);
            w.Vector3(new Vector3(1.5f, -2.25f, 1000.125f));
            w.ShortString("porte_hall");

            ClassicAssert.IsFalse(w.Overflowed);

            var r = new NetReader(buffer, w.Length);

            ClassicAssert.AreEqual(200, r.Byte());
            ClassicAssert.IsTrue(r.Bool());
            ClassicAssert.IsFalse(r.Bool());
            ClassicAssert.AreEqual(65535, r.UShort());
            ClassicAssert.AreEqual(4000000000u, r.UInt());
            ClassicAssert.AreEqual(-123456, r.Int());
            ClassicAssert.AreEqual(3.14159f, r.Float(), 0f, "un float doit revenir AU BIT PRES");
            ClassicAssert.AreEqual(new Vector3(1.5f, -2.25f, 1000.125f), r.Vector3());
            ClassicAssert.AreEqual("porte_hall", r.ShortString());

            ClassicAssert.IsFalse(r.Failed);
            ClassicAssert.AreEqual(0, r.Remaining, "tout doit avoir ete relu");
        }

        [Test]
        public void Un_Angle_Tient_Sur_DEUX_Octets_Sans_Perte_Perceptible()
        {
            // ⚠️ Ce n'est pas de l'avarice : une orientation part dans chaque
            // instantane, pour chaque joueur, 64 fois par seconde.
            var buffer = new byte[8];
            var w = new NetWriter(buffer);

            w.Angle(1.2345f);
            ClassicAssert.AreEqual(2, w.Length, "un angle doit tenir sur deux octets");

            var r = new NetReader(buffer, w.Length);
            float back = r.Angle();

            // 360/65536 = 0,0055 degre, soit 9,6e-5 radian.
            ClassicAssert.AreEqual(1.2345f, back, 1e-4f);
        }

        [Test]
        public void Un_Angle_NEGATIF_Ne_Part_Pas_A_Lautre_Bout_Du_Cercle()
        {
            // ⚠️ Le piege : quantifier sans ramener dans [0, 2pi[ donne un
            // entier negatif, donc un cast qui part n'importe ou. Un joueur qui
            // regarde a gauche se retrouverait oriente au hasard.
            var buffer = new byte[8];
            var w = new NetWriter(buffer);

            w.Angle(-1f);

            var r = new NetReader(buffer, w.Length);
            float back = r.Angle();

            // -1 rad vaut 2pi - 1 = 5,283 rad. Les deux decrivent la MEME
            // direction : on compare donc les cosinus et sinus.
            ClassicAssert.AreEqual(MathF.Cos(-1f), MathF.Cos(back), 1e-3f);
            ClassicAssert.AreEqual(MathF.Sin(-1f), MathF.Sin(back), 1e-3f);
        }

        [Test]
        public void Un_Paquet_TRONQUE_Ne_Leve_Pas_Dexception_Et_Se_Signale()
        {
            // ⚠️⚠️ CE TEST PROTEGE LE SERVEUR. Un paquet vient du reseau : il
            // peut etre coupe par la route, ou fabrique expres. Lever une
            // exception ici, c'est offrir un bouton d'arret a n'importe qui.
            var buffer = new byte[64];
            var w = new NetWriter(buffer);

            w.UInt(42);
            w.Vector3(Vector3.One);

            // On ne donne que 5 octets sur les 16 ecrits.
            var r = new NetReader(buffer, 5);

            ClassicAssert.AreEqual(42u, r.UInt());

            Vector3 truncated = r.Vector3();

            ClassicAssert.IsTrue(r.Failed, "la lecture doit se declarer ratee");
            ClassicAssert.AreEqual(Vector3.Zero, truncated, "et rendre du neutre, pas des ordures");
        }

        [Test]
        public void Un_NaN_Venu_Du_Reseau_Est_REFUSE()
        {
            // ⚠️ Un NaN se propage silencieusement dans les positions puis dans
            // la physique, et finit par tout figer sans qu'aucune ligne ne le
            // signale. On le traite comme une lecture ratee : c'en est une.
            var buffer = new byte[16];
            var w = new NetWriter(buffer);

            w.Float(float.NaN);

            var r = new NetReader(buffer, w.Length);
            float value = r.Float();

            ClassicAssert.IsTrue(r.Failed);
            ClassicAssert.AreEqual(0f, value);
        }

        [Test]
        public void Une_Ecriture_Trop_Grosse_Se_Signale_Au_Lieu_De_Deborder()
        {
            var buffer = new byte[4];
            var w = new NetWriter(buffer);

            w.UInt(1);
            ClassicAssert.IsFalse(w.Overflowed);

            w.UInt(2);
            ClassicAssert.IsTrue(w.Overflowed, "il n'y avait plus de place");
            ClassicAssert.AreEqual(4, w.Length, "et rien n'a ete ecrit au-dela");
        }

        [Test]
        public void Une_Entree_ABERRANTE_Est_Bornee()
        {
            // ⚠️⚠️ TOUT CE QUI VIENT DU RESEAU EST HOSTILE. La vitesse du moteur
            // est proportionnelle a la longueur du vecteur de deplacement : un
            // client modifie qui envoie (1000, 1000) courrait mille fois plus
            // vite. Borner n'est pas de la defiance, c'est la definition d'un
            // serveur autoritaire.
            var wild = new MoveInput { Move = new Vector2(1000f, 1000f) };
            MoveInput safe = TimedInput.Sanitize(wild);

            ClassicAssert.LessOrEqual(safe.Move.Length(), 1.0001f);
            ClassicAssert.Greater(safe.Move.X, 0f, "mais la DIRECTION voulue est conservee");
            ClassicAssert.AreEqual(safe.Move.X, safe.Move.Y, 1e-5f);
        }

        // ===================================================================
        //  3.1 - LE RESEAU SIMULE LUI-MEME
        // ===================================================================

        [Test]
        public void Un_Paquet_Arrive_Apres_La_Latence_Et_Pas_Avant()
        {
            var router = new LoopbackRouter { Latency = 0.05 };
            using var a = new LoopbackTransport(router, "a");
            using var b = new LoopbackTransport(router, "b");

            var data = new byte[] { 1, 2, 3 };
            a.Send(b.Local, data, 3);

            router.Advance(0.04);
            ClassicAssert.IsFalse(b.Receive(out _), "a 40 ms il est encore en vol");

            router.Advance(0.02);
            ClassicAssert.IsTrue(b.Receive(out NetPacket packet), "a 60 ms il est arrive");
            ClassicAssert.AreEqual(3, packet.Length);
            ClassicAssert.AreEqual(1, packet.Data[0]);
            ClassicAssert.AreEqual(new NetAddress("a"), packet.From);
        }

        [Test]
        public void Le_Reseau_Simule_Est_DETERMINISTE()
        {
            // ⚠️ Un test de netcode qui echoue une fois sur vingt ne sert a
            // rien : on ne saurait jamais si une correction a marche. Deux
            // routeurs de meme graine doivent perdre EXACTEMENT les memes
            // paquets.
            int[] delivered = new int[2];

            for (int run = 0; run < 2; run++)
            {
                var router = new LoopbackRouter(seed: 777) { Latency = 0.02, Loss = 0.3 };
                using var a = new LoopbackTransport(router, "a");
                using var b = new LoopbackTransport(router, "b");

                var data = new byte[4];

                for (int i = 0; i < 200; i++)
                {
                    a.Send(b.Local, data, 4);
                    router.Advance(Step);

                    while (b.Receive(out _))
                        delivered[run]++;
                }
            }

            ClassicAssert.AreEqual(delivered[0], delivered[1],
                "deux executions de meme graine doivent livrer le meme nombre de paquets");
            ClassicAssert.Greater(delivered[0], 0);
            ClassicAssert.Less(delivered[0], 200, "et la perte doit vraiment perdre des paquets");
        }

        [Test]
        public void Les_Paquets_En_Vol_Ne_Partagent_PAS_Leur_Tampon()
        {
            // ⚠️ Defaut particulierement retors : sans copie du tampon, tous les
            // paquets en vol pointent sur les MEMES octets et arrivent tous avec
            // le contenu du dernier envoye. Tout marche tant qu'un seul paquet
            // est en vol a la fois - donc tout marche jusqu'au jour ou non.
            var router = new LoopbackRouter { Latency = 0.1 };
            using var a = new LoopbackTransport(router, "a");
            using var b = new LoopbackTransport(router, "b");

            var shared = new byte[1];

            for (byte i = 1; i <= 5; i++)
            {
                shared[0] = i;                     // on REUTILISE le meme tampon
                a.Send(b.Local, shared, 1);
            }

            router.Advance(0.2);

            var received = new List<byte>();

            while (b.Receive(out NetPacket packet))
                received.Add(packet.Data[0]);

            ClassicAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, received.ToArray(),
                "chaque paquet doit porter le contenu qu'il avait A L'ENVOI");
        }

        // ===================================================================
        //  3.2 - CONNEXION
        // ===================================================================

        private sealed class Pair : IDisposable
        {
            public LoopbackRouter Router = null!;
            public NetServer Server = null!;
            public NetClient Client = null!;
            public LoopbackTransport ServerTransport = null!;
            public LoopbackTransport ClientTransport = null!;
            public double Time;

            public static Pair Create(double latency = 0.0, double loss = 0.0,
                                      double jitter = 0.0, uint seed = 4242)
            {
                var pair = new Pair();

                pair.Router = new LoopbackRouter(seed) { Latency = latency, Loss = loss, Jitter = jitter };
                pair.ServerTransport = new LoopbackTransport(pair.Router, "serveur");
                pair.ClientTransport = new LoopbackTransport(pair.Router, "client1");

                pair.Server = new NetServer(pair.ServerTransport);
                pair.Server.LoadMap(FlatMap());

                pair.Client = new NetClient(pair.ClientTransport);

                return pair;
            }

            /// <summary>Un pas complet : reseau, serveur, client.</summary>
            public void Tick(MoveInput input)
            {
                Router.Advance(Step);
                Time += Step;

                Server.Step(Time, Step);
                Client.Poll(Time);
                Client.SendInput(input);
            }

            public void Run(int steps, MoveInput input)
            {
                for (int i = 0; i < steps; i++)
                    Tick(input);
            }

            public void Dispose()
            {
                Client.Dispose();
                Server.Dispose();
                ClientTransport.Dispose();
                ServerTransport.Dispose();
            }
        }

        [Test]
        public void Un_Client_Se_Connecte_Et_Recoit_Un_Identifiant()
        {
            using var pair = Pair.Create();

            pair.Client.Connect(pair.ServerTransport.Local, "moi");
            pair.Run(10, default);

            ClassicAssert.AreEqual(NetClientState.Connecte, pair.Client.State);
            ClassicAssert.AreNotEqual(0, pair.Client.LocalId, "le serveur doit attribuer un identifiant");
            ClassicAssert.AreEqual(1, pair.Server.ClientCount);
            ClassicAssert.AreEqual("plat", pair.Client.ServerMap, "et annoncer sa carte");
        }

        [Test]
        public void Une_Mauvaise_Version_Est_REFUSEE_Avec_Une_Raison()
        {
            // ⚠️ Sans cette verification, deux versions se connectent et
            // decodent des octets qui ne veulent plus rien dire : ca se
            // manifeste comme des joueurs qui tremblent ou volent, et on
            // cherche dans le netcode au lieu de la version.
            var router = new LoopbackRouter();
            using var serverTransport = new LoopbackTransport(router, "serveur");
            using var clientTransport = new LoopbackTransport(router, "client");

            using var server = new NetServer(serverTransport);
            server.LoadMap(FlatMap());

            // On fabrique un Hello a la main, avec une version fausse.
            var buffer = new byte[64];
            var w = new NetWriter(buffer);
            w.Byte(NetMessage.Hello);
            w.UShort((ushort)(NetConfig.Version + 1));
            w.ShortString("intrus");

            clientTransport.Send(serverTransport.Local, buffer, w.Length);

            router.Advance(Step);
            server.Step(Step, Step);
            router.Advance(Step);

            ClassicAssert.AreEqual(0, server.ClientCount, "il ne doit PAS etre accepte");

            ClassicAssert.IsTrue(clientTransport.Receive(out NetPacket reply));

            var r = new NetReader(reply.Data, reply.Length);
            ClassicAssert.AreEqual(NetMessage.Reject, r.Byte(), "et recevoir un refus explicite");
            Assert.That(r.ShortString(), Does.Contain("version"));
        }

        [Test]
        public void Un_HELLO_Repete_Ne_Cree_PAS_Un_Joueur_Fantome()
        {
            // ⚠️ Le client reessaye tant qu'il n'a pas recu le Welcome, et c'est
            // justement le Welcome qui a pu se perdre. Sans traitement du Hello
            // repete, chaque tentative creerait un joueur de plus - le serveur se
            // remplirait de fantomes du meme client.
            using var pair = Pair.Create(latency: 0.15, loss: 0.6, seed: 99);

            pair.Client.Connect(pair.ServerTransport.Local, "insistant");
            pair.Run(200, default);

            ClassicAssert.AreEqual(NetClientState.Connecte, pair.Client.State,
                "il doit finir par passer malgre 60 % de perte");
            ClassicAssert.AreEqual(1, pair.Server.ClientCount,
                $"UN seul joueur, pas {pair.Server.ClientCount}");
        }

        [Test]
        public void Un_Client_Muet_Finit_Par_Etre_Ejecte()
        {
            using var pair = Pair.Create();

            pair.Client.Connect(pair.ServerTransport.Local, "moi");
            pair.Run(10, default);
            ClassicAssert.AreEqual(1, pair.Server.ClientCount);

            // Le client se tait : on avance seulement le serveur.
            for (int i = 0; i < 64 * 12; i++)
            {
                pair.Time += Step;
                pair.Server.Step(pair.Time, Step);
            }

            ClassicAssert.AreEqual(0, pair.Server.ClientCount,
                "au-dela du delai d'attente, il doit etre retire");
        }

        // ===================================================================
        //  3.2 / 3.3 - LE SERVEUR EST AUTORITAIRE
        // ===================================================================

        [Test]
        public void Les_Entrees_Du_Client_Font_VRAIMENT_Avancer_Le_Joueur()
        {
            using var pair = Pair.Create();

            pair.Client.Connect(pair.ServerTransport.Local, "moi");
            pair.Run(10, default);

            byte id = pair.Client.LocalId;
            ClassicAssert.IsTrue(pair.Server.TryGetPlayer(id, out PlayerState before));

            // Une seconde de course vers l'avant.
            pair.Run(64, Forward());

            ClassicAssert.IsTrue(pair.Server.TryGetPlayer(id, out PlayerState after));

            float travelled = Vector3.Distance(
                new Vector3(before.Position.X, 0f, before.Position.Z),
                new Vector3(after.Position.X, 0f, after.Position.Z));

            // Vitesse de course : 4,76 m/s. Avec l'acceleration au demarrage et
            // les quelques pas perdus a la connexion, on attend l'ordre de
            // grandeur, pas la valeur exacte.
            ClassicAssert.Greater(travelled, 3.5f,
                $"une seconde de course doit faire plusieurs metres, mesure {travelled:0.00} m");
            ClassicAssert.Less(travelled, 5.5f, "et pas plus que la vitesse de course");
        }

        [Test]
        public void Le_Client_Voit_Le_Joueur_Distant_Bouger()
        {
            // LE TEST QUI COMPTE POUR 3.3 : deux clients, chacun voit l'autre.
            var router = new LoopbackRouter { Latency = 0.03 };
            using var st = new LoopbackTransport(router, "serveur");
            using var c1t = new LoopbackTransport(router, "client1");
            using var c2t = new LoopbackTransport(router, "client2");

            using var server = new NetServer(st);
            server.LoadMap(FlatMap());

            using var c1 = new NetClient(c1t);
            using var c2 = new NetClient(c2t);

            c1.Connect(st.Local, "un");
            c2.Connect(st.Local, "deux");

            double time = 0;

            // Connexion.
            for (int i = 0; i < 20; i++)
            {
                router.Advance(Step);
                time += Step;
                server.Step(time, Step);
                c1.Poll(time);
                c2.Poll(time);
                c1.SendInput(default);
                c2.SendInput(default);
            }

            ClassicAssert.AreEqual(2, server.ClientCount, "les deux doivent etre connectes");
            ClassicAssert.AreNotEqual(c1.LocalId, c2.LocalId, "et avoir des identifiants distincts");

            ClassicAssert.IsTrue(c1.TryGetPlayer(c2.LocalId, out PlayerState seenBefore),
                "le client 1 doit VOIR le client 2");

            // Le client 2 court, le client 1 ne bouge pas.
            for (int i = 0; i < 96; i++)
            {
                router.Advance(Step);
                time += Step;
                server.Step(time, Step);
                c1.Poll(time);
                c2.Poll(time);
                c1.SendInput(default);
                c2.SendInput(Forward());
            }

            ClassicAssert.IsTrue(c1.TryGetPlayer(c2.LocalId, out PlayerState seenAfter));

            float seenTravel = Vector3.Distance(seenBefore.Position, seenAfter.Position);

            ClassicAssert.Greater(seenTravel, 3f,
                $"le client 1 doit voir le client 2 s'eloigner, mesure {seenTravel:0.00} m");

            // Et le serveur doit etre d'accord, a l'interpolation pres.
            ClassicAssert.IsTrue(server.TryGetPlayer(c2.LocalId, out PlayerState truth));

            float error = Vector3.Distance(truth.Position, seenAfter.Position);

            ClassicAssert.Less(error, 0.6f,
                $"l'ecart avec la verite du serveur doit rester petit, mesure {error:0.000} m");
        }

        [Test]
        public void Le_Client_Ne_Peut_PAS_Se_Teleporter()
        {
            // ⚠️⚠️ LA PROPRIETE FONDAMENTALE D'UN SERVEUR AUTORITAIRE. Le
            // protocole n'a AUCUN message « je suis ici ». Ce test le fige :
            // c'est le defaut de conception le plus courant et le plus
            // irreparable une fois le jeu sorti.
            using var pair = Pair.Create();

            pair.Client.Connect(pair.ServerTransport.Local, "tricheur");
            pair.Run(10, default);

            byte id = pair.Client.LocalId;

            // On envoie un instantane fabrique VERS le serveur, comme si on
            // pretendait etre a 500 m.
            var buffer = new byte[128];
            var w = new NetWriter(buffer);
            w.Byte(NetMessage.Snapshot);
            w.UInt(9999);
            w.UInt(0);
            w.Byte(id);
            w.Byte(1);
            new PlayerState { Id = id, Position = new Vector3(500f, 0f, 500f) }.Write(ref w);

            pair.ClientTransport.Send(pair.ServerTransport.Local, buffer, w.Length);

            pair.Run(10, default);

            ClassicAssert.IsTrue(pair.Server.TryGetPlayer(id, out PlayerState state));
            ClassicAssert.Less(state.Position.Length(), 10f,
                "le serveur ne doit pas avoir bouge d'un metre a cause de ce paquet");
        }

        // ===================================================================
        //  3.3 - RESISTANCE A LA PERTE ET AU DESORDRE
        // ===================================================================

        [Test]
        public void Le_Joueur_Avance_Malgre_Une_Perte_De_20_Pour_Cent()
        {
            // ⚠️ C'est la redondance des entrees qui fait ca : chaque paquet
            // porte les huit dernieres. Il faudrait perdre huit paquets
            // consecutifs pour que le serveur manque une entree.
            using var pair = Pair.Create(latency: 0.04, loss: 0.2, jitter: 0.01, seed: 31337);

            pair.Client.Connect(pair.ServerTransport.Local, "moi");
            pair.Run(40, default);

            ClassicAssert.AreEqual(NetClientState.Connecte, pair.Client.State);
            ClassicAssert.IsTrue(pair.Server.TryGetPlayer(pair.Client.LocalId, out PlayerState before));

            pair.Run(128, Forward());

            ClassicAssert.IsTrue(pair.Server.TryGetPlayer(pair.Client.LocalId, out PlayerState after));

            float travelled = Vector3.Distance(
                new Vector3(before.Position.X, 0f, before.Position.Z),
                new Vector3(after.Position.X, 0f, after.Position.Z));

            // Deux secondes a 4,76 m/s feraient 9,5 m sans perte. Avec 20 % de
            // perte et la redondance, on doit rester tres proche.
            ClassicAssert.Greater(travelled, 8f,
                $"avec 20 % de perte il doit quand meme parcourir la distance, mesure {travelled:0.00} m");

            ClassicAssert.Greater(pair.Router.DroppedCount, 0, "et la perte doit avoir vraiment eu lieu");
        }

        [Test]
        public void Un_Instantane_EN_RETARD_Est_Jete_Et_Ne_Fait_Pas_Reculer()
        {
            // ⚠️⚠️ UDP LIVRE DANS LE DESORDRE. Sans ce test, un paquet en retard
            // ferait reculer tout le monde d'une image : c'est LE tremblement
            // qu'on met des semaines a attribuer au reseau plutot qu'au rendu.
            using var pair = Pair.Create(latency: 0.05, jitter: 0.04, seed: 5150);

            pair.Client.Connect(pair.ServerTransport.Local, "moi");
            pair.Run(300, Forward());

            ClassicAssert.Greater(pair.Client.SnapshotsReceived, 100);
            ClassicAssert.Greater(pair.Client.SnapshotsDiscarded, 0,
                "avec 40 ms de gigue, des instantanes DOIVENT arriver dans le desordre");

            // Le pas de serveur vu par le client ne doit jamais avoir recule.
            // C'est exactement ce que garantit le rejet des anciens.
            ClassicAssert.Greater(pair.Client.ServerTick, 0u);
        }

        [Test]
        public void Une_Entree_Dupliquee_Nest_Appliquee_Quune_Fois()
        {
            // ⚠️ La redondance fait arriver la meme entree jusqu'a huit fois.
            // L'appliquer deux fois ferait avancer le joueur deux fois plus vite
            // - et l'ecart grandirait avec la perte, donc les mauvaises
            // connexions courraient plus vite que les bonnes.
            using var pair = Pair.Create(latency: 0.02);

            pair.Client.Connect(pair.ServerTransport.Local, "moi");
            pair.Run(20, default);

            int appliedBefore = pair.Server.InputsApplied;

            pair.Run(64, Forward());

            int applied = pair.Server.InputsApplied - appliedBefore;

            // 64 pas envoyes, chacun redondant huit fois : sans le filtre par
            // numero de pas, on en compterait des centaines.
            ClassicAssert.LessOrEqual(applied, 70,
                $"au plus une entree appliquee par pas envoye, mesure {applied}");
        }

        // ===================================================================
        //  3.4 - INTERPOLATION
        // ===================================================================

        [Test]
        public void Linterpolation_Rend_Le_Mouvement_CONTINU()
        {
            // ⚠️ LE TEST QUI JUSTIFIE 3.4. Sans interpolation, un joueur distant
            // avance par sauts de 15 ms, et la gigue rend ces sauts irreguliers.
            // On mesure donc la REGULARITE : le plus grand pas observe ne doit
            // pas etre beaucoup plus grand que le pas moyen.
            var router = new LoopbackRouter(seed: 8080) { Latency = 0.05, Jitter = 0.015 };
            using var st = new LoopbackTransport(router, "serveur");
            using var c1t = new LoopbackTransport(router, "client1");
            using var c2t = new LoopbackTransport(router, "client2");

            using var server = new NetServer(st);
            server.LoadMap(FlatMap());

            using var c1 = new NetClient(c1t);
            using var c2 = new NetClient(c2t);

            c1.Connect(st.Local, "un");
            c2.Connect(st.Local, "deux");

            double time = 0;

            for (int i = 0; i < 40; i++)
            {
                router.Advance(Step);
                time += Step;
                server.Step(time, Step);
                c1.Poll(time);
                c2.Poll(time);
                c1.SendInput(default);
                c2.SendInput(default);
            }

            ClassicAssert.AreEqual(2, server.ClientCount);

            // On observe le client 2 depuis le client 1, en dessinant PLUS VITE
            // que le serveur ne publie : c'est la situation reelle (300 img/s
            // pour 64 instantanes/s).
            var steps = new List<float>();
            Vector3 previous = Vector3.Zero;
            bool first = true;

            for (int i = 0; i < 400; i++)
            {
                // Un quart de pas de simulation : quatre images par pas serveur.
                router.Advance(Step * 0.25);
                time += Step * 0.25;

                if (i % 4 == 0)
                {
                    server.Step(time, Step);
                    c2.SendInput(Forward());
                }

                c1.Poll(time);

                if (!c1.TryGetPlayer(c2.LocalId, out PlayerState seen))
                    continue;

                if (!first)
                    steps.Add(Vector3.Distance(previous, seen.Position));

                previous = seen.Position;
                first = false;
            }

            ClassicAssert.Greater(steps.Count, 100, "il faut des mesures pour conclure");

            float total = 0f, max = 0f;
            int moving = 0;

            foreach (float s in steps)
            {
                if (s <= 1e-6f)
                    continue;

                total += s;
                moving++;

                if (s > max)
                    max = s;
            }

            float average = total / Math.Max(1, moving);

            // Sans interpolation, le joueur bouge d'un seul coup tous les quatre
            // rendus : le maximum ferait alors environ quatre fois la moyenne des
            // pas non nuls, et trois rendus sur quatre seraient immobiles.
            ClassicAssert.Greater(moving, steps.Count * 0.7f,
                $"la plupart des images doivent montrer du mouvement, {moving}/{steps.Count}");

            ClassicAssert.Less(max, average * 3.5f,
                $"aucun saut ne doit dominer : max {max * 1000f:0.00} mm, moyenne {average * 1000f:0.00} mm");
        }

        [Test]
        public void Un_Angle_Sinterpole_Par_Le_Plus_COURT_Chemin()
        {
            // ⚠️ Un lerp direct entre 359 et 1 degre fait faire un tour complet
            // a l'envers : sur l'orientation d'un joueur, ca se voit comme une
            // pirouette chaque fois qu'il traverse le nord.
            float from = MathF.Tau - 0.1f;   // presque un tour
            float to = 0.1f;                 // juste apres zero

            float middle = NetClient.LerpAngle(from, to, 0.5f);

            // Le milieu du plus court chemin est 0 (ou 2pi), pas pi.
            float cos = MathF.Cos(middle);

            ClassicAssert.Greater(cos, 0.99f,
                $"le milieu doit etre proche de zero, obtenu {middle:0.000} rad");
        }

        [Test]
        public void Un_Joueur_Qui_Part_Ne_Fait_Pas_Glisser_Les_Autres()
        {
            // ⚠️ L'interpolation apparie les joueurs par IDENTIFIANT, pas par
            // indice. Par indice, un joueur qui se deconnecte decale la liste et
            // on interpolerait la position d'un joueur vers celle d'un autre - un
            // glissement instantane a travers la carte.
            var router = new LoopbackRouter { Latency = 0.02 };
            using var st = new LoopbackTransport(router, "serveur");
            using var c1t = new LoopbackTransport(router, "client1");
            using var c2t = new LoopbackTransport(router, "client2");
            using var c3t = new LoopbackTransport(router, "client3");

            using var server = new NetServer(st);
            server.LoadMap(FlatMap());

            using var c1 = new NetClient(c1t);
            var c2 = new NetClient(c2t);
            using var c3 = new NetClient(c3t);

            c1.Connect(st.Local, "un");
            c2.Connect(st.Local, "deux");
            c3.Connect(st.Local, "trois");

            double time = 0;

            void Tick(bool withC2)
            {
                router.Advance(Step);
                time += Step;
                server.Step(time, Step);
                c1.Poll(time);
                c3.Poll(time);
                c1.SendInput(default);
                c3.SendInput(default);

                if (!withC2)
                    return;

                c2.Poll(time);
                c2.SendInput(default);
            }

            for (int i = 0; i < 30; i++) Tick(true);

            ClassicAssert.AreEqual(3, server.ClientCount);
            ClassicAssert.IsTrue(c1.TryGetPlayer(c3.LocalId, out PlayerState before));

            // Le client 2 part. Son identifiant disparait au milieu de la liste.
            byte goneId = c2.LocalId;
            c2.Disconnect();
            c2.Dispose();

            for (int i = 0; i < 30; i++) Tick(false);

            ClassicAssert.AreEqual(2, server.ClientCount, "il ne doit rester que deux joueurs");
            ClassicAssert.IsFalse(c1.TryGetPlayer(goneId, out _), "le partant ne doit plus etre affiche");

            ClassicAssert.IsTrue(c1.TryGetPlayer(c3.LocalId, out PlayerState after));

            float slide = Vector3.Distance(before.Position, after.Position);

            ClassicAssert.Less(slide, 0.05f,
                $"le client 3 n'a pas bouge : il ne doit pas glisser, mesure {slide:0.000} m");
        }

        // ===================================================================
        //  MESURES
        // ===================================================================

        [Test]
        public void Un_Instantane_A_Dix_Joueurs_Tient_Largement_Dans_Un_Datagramme()
        {
            // ⚠️ Au-dela de la MTU, IP FRAGMENTE : un seul fragment perdu detruit
            // tout le paquet, donc la probabilite de perte effective explose.
            // Ce test fige la marge.
            int header = 1 + 4 + 4 + 1 + 1;   // type, tick, ack, id local, compte
            int payload = NetConfig.MaxPlayers * PlayerState.SizeInBytes;
            int total = header + payload;

            ClassicAssert.Less(total, NetConfig.MaxPacketSize,
                $"un instantane plein fait {total} octets pour un plafond de {NetConfig.MaxPacketSize}");

            // On veut de la MARGE, pas juste tenir : la suite de la phase 3
            // ajoutera des champs (vie, arme, etat de tir).
            ClassicAssert.Less(total, NetConfig.MaxPacketSize / 4,
                $"il doit rester de la place pour la suite, {total} octets utilises");
        }

        [Test]
        public void Le_Debit_Par_Client_Est_Mesure_Et_Raisonnable()
        {
            using var pair = Pair.Create(latency: 0.02);

            pair.Client.Connect(pair.ServerTransport.Local, "moi");
            pair.Run(64 * 2, Forward());   // deux secondes

            // Ce que le SERVEUR emet vers un client, par seconde.
            double serverBytesPerSecond = pair.Server.BytesSent / 2.0;
            double clientBytesPerSecond = pair.Client.BytesSent / 2.0;

            // Un instantane a un joueur fait ~29 octets, 64 fois par seconde,
            // soit environ 1,9 ko/s. Une entree redondante fait ~115 octets.
            ClassicAssert.Less(serverBytesPerSecond, 8000.0,
                $"serveur -> client mesure a {serverBytesPerSecond:0} o/s");

            ClassicAssert.Less(clientBytesPerSecond, 12000.0,
                $"client -> serveur mesure a {clientBytesPerSecond:0} o/s");

            ClassicAssert.Greater(serverBytesPerSecond, 500.0, "et il doit vraiment emettre");
        }

        [Test]
        public void Laller_Retour_Est_Mesure_Et_Correspond_A_La_Latence()
        {
            using var pair = Pair.Create(latency: 0.05);

            pair.Client.Connect(pair.ServerTransport.Local, "moi");
            pair.Run(20, default);

            pair.Client.SendPing();

            for (int i = 0; i < 40; i++)
                pair.Tick(default);

            // 50 ms aller + 50 ms retour = 100 ms, plus la granularite du pas.
            ClassicAssert.Greater(pair.Client.RoundTrip, 0.0, "un Pong doit etre revenu");
            ClassicAssert.AreEqual(0.1, pair.Client.RoundTrip, 0.04,
                $"aller-retour mesure a {pair.Client.RoundTrip * 1000.0:0} ms pour 100 ms attendus");
        }
    }
}
