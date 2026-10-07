// ===========================================================================
//  Exarion - Tests de la prediction, de la reconciliation et du rembobinage
//  (phases 3.5, 3.6, 3.7)
//
//  Ce sont les trois etapes marquees « risque tres eleve » dans la roadmap, et
//  celles ou une erreur ne se voit PAS : un defaut de reconciliation se
//  manifeste comme « ca tremble parfois », des semaines plus tard, chez
//  quelqu'un qui a du ping.
//
//  Tout tourne sur le reseau simule deterministe : latence, gigue et perte
//  reproduites a la graine pres.
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
    public class NetPredictionTests
    {
        private const float Step = 1f / 64f;

        private static MapData FlatMap()
        {
            var map = new MapData { Name = "plat", Spawn = new Vector3(0f, 0f, 0f) };
            map.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), new Vector3(0.3f));
            return map;
        }

        private static MoveInput Forward(float yaw = 0f)
            => new() { Move = new Vector2(0f, 1f), Yaw = yaw };

        /// <summary>Un serveur, un client, un reseau reglable.</summary>
        private sealed class Link : IDisposable
        {
            public LoopbackRouter Router = null!;
            public NetServer Server = null!;
            public NetClient Client = null!;
            public LoopbackTransport ServerTransport = null!;
            public LoopbackTransport ClientTransport = null!;
            public double Time;

            public static Link Create(double latency, bool predict = true,
                                      double loss = 0.0, uint seed = 2024)
            {
                var link = new Link();
                var map = FlatMap();

                link.Router = new LoopbackRouter(seed) { Latency = latency, Loss = loss };
                link.ServerTransport = new LoopbackTransport(link.Router, "serveur");
                link.ClientTransport = new LoopbackTransport(link.Router, "client");

                link.Server = new NetServer(link.ServerTransport);
                link.Server.LoadMap(map);

                link.Client = new NetClient(link.ClientTransport)
                {
                    PredictionEnabled = predict,

                    // ⚠️ LE CLIENT PREDIT DANS LE MEME DECOR QUE LE SERVEUR.
                    // Un decor different ferait diverger la prediction en
                    // permanence - le joueur serait corrige sans arret.
                    PredictionWorld = map.ToCollisionWorld(new List<Vector3>()),
                };

                return link;
            }

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

        // ===================================================================
        //  3.5 - PREDICTION
        // ===================================================================

        [Test]
        public void Sans_Prediction_On_Subit_La_Latence_COMPLETE()
        {
            // ⚠️ Ce test etablit le PROBLEME que 3.5 resout. Sans lui, on ne
            // peut pas montrer que la prediction sert a quelque chose : il faut
            // mesurer le mal avant de mesurer le remede.
            //
            // 150 ms d'aller simple, donc 300 ms d'aller-retour.
            using var link = Link.Create(latency: 0.15, predict: false);

            link.Client.Connect(link.ServerTransport.Local, "moi");
            link.Run(40, default);

            ClassicAssert.AreEqual(NetClientState.Connecte, link.Client.State);

            // On appuie sur « avancer » et on regarde tout de suite.
            link.Client.SendInput(Forward());
            link.Client.Poll(link.Time);

            ClassicAssert.IsTrue(link.Client.TryGetPlayer(link.Client.LocalId, out PlayerState seen));

            // Sans prediction, rien n'a bouge : la reponse est encore en vol.
            ClassicAssert.AreEqual(0f, seen.Position.Z, 0.01f,
                "sans prediction, le joueur ne peut PAS avoir bouge a l'image de l'appui");
        }

        [Test]
        public void Avec_Prediction_Le_Joueur_Bouge_A_LIMAGE_De_LAppui()
        {
            // ⚠️⚠️ LE TEST QUI JUSTIFIE 3.5. A 150 ms de latence, sans
            // prediction le joueur repond au bout de 300 ms - le jeu passe de
            // « nerveux » a « pateux » sans qu'aucun chiffre du profileur ne
            // bouge.
            using var link = Link.Create(latency: 0.15, predict: true);

            link.Client.Connect(link.ServerTransport.Local, "moi");
            link.Run(40, default);

            Vector3 before = link.Client.Predicted.Position;

            // UN SEUL pas d'entree, et on regarde immediatement.
            link.Client.SendInput(Forward());

            float moved = Vector3.Distance(before, link.Client.Predicted.Position);

            ClassicAssert.Greater(moved, 0.001f,
                $"le joueur doit avoir bouge des le premier pas, mesure {moved * 1000f:0.0} mm");
        }

        [Test]
        public void La_Prediction_Suit_Le_Serveur_A_Quelques_Centimetres()
        {
            // ⚠️ LA PROPRIETE QUI COMPTE : predire vite ne sert a rien si on
            // predit faux. Le client et le serveur partagent litteralement le
            // meme CharacterMotor - c'est ce que la regle n°1 du projet rend
            // possible - donc l'ecart doit rester minuscule.
            using var link = Link.Create(latency: 0.06, predict: true);

            link.Client.Connect(link.ServerTransport.Local, "moi");
            link.Run(40, default);

            link.Run(192, Forward());   // trois secondes de course

            ClassicAssert.IsTrue(link.Server.TryGetPlayer(link.Client.LocalId, out PlayerState truth));

            // Le client est EN AVANCE sur le serveur d'environ un aller simple :
            // c'est normal et voulu. On compare donc a ce que le serveur avait
            // au moment ou le client predisait, via l'erreur mesuree.
            ClassicAssert.Less(link.Client.WorstPredictionError, 0.15f,
                $"la pire erreur de prediction doit rester petite, mesure "
                + $"{link.Client.WorstPredictionError * 100f:0.0} cm");

            // Et le client doit etre DEVANT le serveur, pas derriere : il simule
            // en avance.
            float ahead = truth.Position.Z - link.Client.Predicted.Position.Z;

            ClassicAssert.Greater(ahead, 0f,
                "le client predit en AVANCE sur le serveur, c'est tout l'interet");
        }

        [Test]
        public void Une_Prediction_JUSTE_Ne_Declenche_AUCUNE_Correction()
        {
            // ⚠️⚠️ LE PIEGE QUI FAIT TREMBLER UN JEU. Le rejeu ne redonne jamais
            // le bit pres : les flottants ne sont pas associatifs. Corriger sur
            // un ecart d'un micrometre ferait sauter le joueur a CHAQUE
            // instantane - soixante-quatre fois par seconde. C'est le seuil qui
            // l'empeche.
            using var link = Link.Create(latency: 0.05, predict: true);

            link.Client.Connect(link.ServerTransport.Local, "moi");
            link.Run(40, default);

            int before = link.Client.Corrections;

            link.Run(192, Forward());

            int corrections = link.Client.Corrections - before;

            ClassicAssert.LessOrEqual(corrections, 3,
                $"sur trois secondes de course sans perturbation, presque aucune "
                + $"correction ne doit se declencher, mesure {corrections}");
        }

        // ===================================================================
        //  3.6 - RECONCILIATION
        // ===================================================================

        [Test]
        public void Une_Prediction_FAUSSE_Est_CORRIGEE_Et_Converge()
        {
            // ⚠️⚠️ LE TEST QUI JUSTIFIE 3.6. On fausse volontairement la
            // prediction en TELEPORTANT le motor local : le serveur, lui, n'en
            // sait rien. Sans reconciliation, le client resterait a jamais a
            // cote de la verite - et comme il predit a partir de sa propre
            // position, l'ecart ne se resorberait jamais tout seul.
            using var link = Link.Create(latency: 0.04, predict: true);

            link.Client.Connect(link.ServerTransport.Local, "moi");
            link.Run(40, default);

            // Dix metres a cote. Le serveur ne le saura jamais : rien dans le
            // protocole ne transmet une position depuis le client.
            var wrong = new Vector3(10f, 0f, 10f);
            link.Client.Predicted.Restore(new CharacterMotor.MotorState(wrong, Vector3.Zero, 1.83f));

            link.Run(64, default);

            ClassicAssert.IsTrue(link.Server.TryGetPlayer(link.Client.LocalId, out PlayerState truth));

            float error = Vector3.Distance(truth.Position, link.Client.Predicted.Position);

            ClassicAssert.Less(error, 0.1f,
                $"la reconciliation doit ramener le client sur la verite, ecart {error:0.000} m");

            ClassicAssert.Greater(link.Client.Corrections, 0, "et une correction doit avoir eu lieu");
        }

        [Test]
        public void La_Correction_Est_ABSORBEE_Par_Laffichage_Pas_Par_La_Simulation()
        {
            // ⚠️⚠️ LA SUBTILITE QUI SEPARE UN NETCODE PROPRE D'UN NETCODE QUI
            // TREMBLE. La simulation doit repartir de la VERITE tout de suite -
            // sinon on predirait a partir d'un mensonge et l'erreur
            // s'accumulerait. Mais afficher ce saut tel quel tirerait le joueur
            // en arriere a chaque paquet.
            //
            // La position AFFICHEE doit donc rester proche de l'ancienne juste
            // apres la correction, puis rejoindre la simulation.
            using var link = Link.Create(latency: 0.04, predict: true);

            link.Client.Connect(link.ServerTransport.Local, "moi");
            link.Run(40, default);

            // Une erreur MODEREE : au-dela du seuil de correction, en dessous du
            // seuil de replacement brutal.
            Vector3 truthBefore = link.Client.Predicted.Position;
            var wrong = truthBefore + new Vector3(0.5f, 0f, 0f);

            link.Client.Predicted.Restore(new CharacterMotor.MotorState(wrong, Vector3.Zero, 1.83f));

            // Un instantane arrive et corrige.
            for (int i = 0; i < 16; i++)
                link.Tick(default);

            float visualJump = Vector3.Distance(link.Client.SmoothedPosition, link.Client.Predicted.Position);

            ClassicAssert.Greater(link.Client.Corrections, 0, "une correction doit avoir eu lieu");

            ClassicAssert.Greater(visualJump, 0.001f,
                "l'affichage doit RETENIR une partie de la correction au lieu de sauter");

            // Puis le decalage se resorbe.
            for (int i = 0; i < 60; i++)
                link.Client.SmoothCorrection(Step);

            float residual = Vector3.Distance(link.Client.SmoothedPosition, link.Client.Predicted.Position);

            ClassicAssert.Less(residual, 0.005f,
                $"et il doit fondre en moins d'une seconde, reste {residual * 1000f:0.0} mm");
        }

        [Test]
        public void Une_Correction_En_Plein_SAUT_Preserve_La_TRAJECTOIRE()
        {
            // ⚠️⚠️ LE TEST QUI PROUVE QUE LA VITESSE DOIT VOYAGER. Mes premiers
            // tests de reconciliation se passaient tous au SOL, sur du plat :
            // la vitesse verticale y est nulle et l'horizontale se re-derive en
            // deux pas. Verifie par mutation - en restaurant une vitesse NULLE,
            // aucun de ces tests ne tombait.
            //
            // En l'air, tout change : la vitesse verticale decide de toute la
            // suite de la trajectoire. La perdre a chaque correction ferait
            // retomber le joueur comme une pierre, plusieurs fois par saut.
            // ⚠️ Latence VOLONTAIREMENT courte (un pas) : le client predit en
            // avance sur le serveur, donc leurs trajectoires sont decalees dans
            // le temps. Avec 60 ms de decalage, comparer deux paraboles n'aurait
            // aucun sens. A un pas pres, l'ecart vertical devient une mesure
            // propre de la fidelite du rejeu.
            using var link = Link.Create(latency: 0.015, predict: true);

            link.Client.Connect(link.ServerTransport.Local, "moi");
            link.Run(40, default);

            // On saute, et on monte pendant quelques pas.
            link.Run(5, new MoveInput { Jump = true });

            ClassicAssert.Greater(link.Client.Predicted.Velocity.Y, 1f,
                "le joueur doit monter au moment ou on le perturbe");

            // On fausse la position LATERALEMENT : assez pour declencher une
            // correction, sans toucher a la verticale. Ce qu'on observe ensuite
            // est donc uniquement l'effet du rejeu sur la trajectoire.
            CharacterMotor.MotorState airborne = link.Client.Predicted.Save();

            link.Client.Predicted.Restore(new CharacterMotor.MotorState(
                airborne.Position + new Vector3(0.5f, 0f, 0f),
                airborne.Velocity, airborne.Height));

            // ⚠️⚠️ CE QU'ON MESURE : LA CONTINUITE DE LA VITESSE VERTICALE.
            //
            // Comparer la hauteur du client a celle du serveur ne discrimine
            // RIEN, et j'ai mis trois essais a comprendre pourquoi : le serveur
            // publie un instantane a CHAQUE pas, donc le client est re-ancre sur
            // la bonne position toutes les 15 ms. Une vitesse fausse n'a pas le
            // temps de se voir sur la position.
            //
            // Mais elle se voit sur la VITESSE, et c'est une propriete physique
            // exacte : en chute libre, la vitesse verticale ne peut varier que
            // de g x dt = 15,24/64 = 0,238 m/s par pas. Une correction qui
            // remettrait la vitesse a zero la ferait sauter de plusieurs metres
            // par seconde en un seul pas - physiquement impossible.
            float worstJerk = 0f;
            float previousVy = link.Client.Predicted.Velocity.Y;
            float apex = link.Client.Predicted.Position.Y;

            for (int i = 0; i < 30; i++)
            {
                link.Tick(default);

                apex = MathF.Max(apex, link.Client.Predicted.Position.Y);

                // On ne mesure QUE tant qu'on est en l'air : atterrir annule
                // legitimement la vitesse verticale.
                if (link.Client.Predicted.Grounded || link.Client.Predicted.Position.Y < 0.05f)
                    break;

                float vy = link.Client.Predicted.Velocity.Y;

                worstJerk = MathF.Max(worstJerk, MathF.Abs(vy - previousVy));
                previousVy = vy;
            }

            ClassicAssert.Greater(link.Client.Corrections, 0, "une correction doit avoir eu lieu");

            // g x dt = 0,238. On laisse une marge pour l'arrondi, pas pour un
            // saut de vitesse.
            ClassicAssert.Less(worstJerk, 0.30f,
                $"la vitesse verticale ne peut varier que de la gravite d'un pas "
                + $"(0,238 m/s) ; mesure {worstJerk:0.000} m/s");

            // Et le joueur doit avoir CONTINUE de monter apres la correction.
            ClassicAssert.Greater(apex, airborne.Position.Y + 0.05f,
                $"le saut doit se poursuivre : apex {apex:0.000} m contre "
                + $"{airborne.Position.Y:0.000} m au moment de la perturbation");
        }

        [Test]
        public void Une_Correction_Ne_Se_REPETE_Pas_Indefiniment()
        {
            // ⚠️ Apres avoir rejoue, il faut REECRIRE la prediction dans
            // l'historique : le prochain instantane comparera a celle-ci. Sans
            // ca, chaque instantane suivant retrouve l'ancienne erreur - deja
            // corrigee - et corrige encore. Le joueur serait tire en arriere
            // soixante-quatre fois par seconde.
            //
            // Verifie par mutation : sans la reecriture, ce test tombe.
            using var link = Link.Create(latency: 0.05, predict: true);

            link.Client.Connect(link.ServerTransport.Local, "moi");
            link.Run(40, default);

            int before = link.Client.Corrections;

            // UNE seule perturbation.
            CharacterMotor.MotorState state = link.Client.Predicted.Save();

            link.Client.Predicted.Restore(new CharacterMotor.MotorState(
                state.Position + new Vector3(0.6f, 0f, 0f), state.Velocity, state.Height));

            // ⚠️ ON COMPTE A PARTIR DE LA PERTURBATION, pas apres un delai de
            // decantation - c'est justement dans les quelques pas qui SUIVENT la
            // correction que le defaut se manifeste, le temps que les entrees
            // perimees soient accusees puis elaguees. Mesurer plus tard laisse
            // passer exactement ce qu'on veut attraper : verifie par mutation,
            // ma premiere version ne discriminait rien.
            link.Run(148, Forward());

            int corrections = link.Client.Corrections - before;

            ClassicAssert.GreaterOrEqual(corrections, 1,
                "la perturbation doit declencher une correction");

            // Une perturbation = UNE correction. Sans la reecriture de
            // l'historique, chaque entree encore en vol au moment de la
            // correction en redeclenche une, et le joueur est tire en arriere
            // plusieurs fois de suite.
            ClassicAssert.LessOrEqual(corrections, 2,
                $"une seule perturbation ne doit pas produire une cascade de "
                + $"corrections, mesure {corrections}");
        }

        [Test]
        public void Une_Erreur_ENORME_Replace_Dun_Coup_Au_Lieu_De_Glisser()
        {
            // Un gros ecart veut dire qu'on a ete pousse, teleporte, ou qu'on a
            // perdu beaucoup de paquets. Le lisser ferait GLISSER le joueur a
            // travers le decor pendant une seconde.
            using var link = Link.Create(latency: 0.04, predict: true);

            link.Client.Connect(link.ServerTransport.Local, "moi");
            link.Run(40, default);

            link.Client.Predicted.Restore(
                new CharacterMotor.MotorState(new Vector3(0f, 0f, 40f), Vector3.Zero, 1.83f));

            for (int i = 0; i < 16; i++)
                link.Tick(default);

            float visualJump = Vector3.Distance(link.Client.SmoothedPosition, link.Client.Predicted.Position);

            ClassicAssert.Less(visualJump, 0.01f,
                $"au-dela du seuil, on se replace SANS lisser, reste {visualJump:0.000} m");
        }

        [Test]
        public void La_Reconciliation_Tient_Malgre_La_PERTE()
        {
            // ⚠️ Avec de la perte, le serveur accuse des pas plus anciens : le
            // client doit rejouer PLUS d'entrees, et l'historique doit remonter
            // assez loin. S'il etait borne trop court, la correction repartirait
            // d'un pas introuvable et le joueur serait replace brutalement a
            // chaque trou.
            using var link = Link.Create(latency: 0.08, predict: true, loss: 0.25, seed: 31337);

            link.Client.Connect(link.ServerTransport.Local, "moi");
            link.Run(60, default);

            ClassicAssert.AreEqual(NetClientState.Connecte, link.Client.State);

            link.Run(256, Forward());

            ClassicAssert.IsTrue(link.Server.TryGetPlayer(link.Client.LocalId, out PlayerState truth));

            ClassicAssert.Greater(link.Router.DroppedCount, 0, "la perte doit avoir vraiment eu lieu");

            ClassicAssert.Less(link.Client.WorstPredictionError, 0.5f,
                $"meme a 25 % de perte, la prediction doit rester proche, pire ecart "
                + $"{link.Client.WorstPredictionError * 100f:0.0} cm");

            // Et les deux doivent avoir parcouru une distance comparable.
            float clientZ = link.Client.Predicted.Position.Z;

            ClassicAssert.Less(MathF.Abs(clientZ - truth.Position.Z), 2f,
                $"client a Z {clientZ:0.00}, serveur a Z {truth.Position.Z:0.00}");
        }

        [Test]
        public void Le_Rejeu_Est_Deterministe()
        {
            // ⚠️ Le rejeu doit redonner exactement le meme resultat que la
            // simulation d'origine, sinon chaque correction introduit une
            // derive. On le verifie directement : deux motors, memes entrees,
            // meme monde.
            var map = FlatMap();
            CollisionWorld world = map.ToCollisionWorld(new List<Vector3>());

            var a = new CharacterMotor();
            var b = new CharacterMotor();

            var start = new CharacterMotor.MotorState(new Vector3(1f, 0f, 2f),
                                                      new Vector3(0.5f, -1f, 0.25f), 1.7f);
            a.Restore(start);
            b.Restore(start);

            var inputs = new List<MoveInput>();
            var rng = new Exarion.Core.Deterministic.Rng(4242);

            for (int i = 0; i < 128; i++)
            {
                inputs.Add(new MoveInput
                {
                    Move = new Vector2(rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f)),
                    Yaw = rng.NextFloat(-3f, 3f),
                    Jump = rng.NextFloat() < 0.1f,
                    Crouch = rng.NextFloat() < 0.2f,
                });
            }

            foreach (MoveInput input in inputs)
                a.Step(input, Step, world);

            foreach (MoveInput input in inputs)
                b.Step(input, Step, world);

            ClassicAssert.AreEqual(a.Position, b.Position,
                "rejouer les memes entrees doit redonner EXACTEMENT la meme position");
            ClassicAssert.AreEqual(a.Velocity, b.Velocity);
            ClassicAssert.AreEqual(a.Height, b.Height);
        }

        [Test]
        public void Restaurer_Un_Motor_Restitue_Son_Etat_COMPLET()
        {
            // ⚠️ Sans la VITESSE, le rejeu part d'un etat faux : un joueur en
            // l'air a une vitesse verticale qui decide de toute la suite de sa
            // trajectoire. Sans la HAUTEUR, sa taille sauterait a chaque
            // correction.
            var map = FlatMap();
            CollisionWorld world = map.ToCollisionWorld(new List<Vector3>());

            var motor = new CharacterMotor();
            motor.Position = new Vector3(0f, 3f, 0f);

            // On le met en l'air avec de la vitesse et accroupi.
            for (int i = 0; i < 10; i++)
                motor.Step(new MoveInput { Crouch = true, Move = new Vector2(1f, 0f) }, Step, world);

            CharacterMotor.MotorState saved = motor.Save();

            var reference = new List<Vector3>();

            for (int i = 0; i < 30; i++)
            {
                motor.Step(default, Step, world);
                reference.Add(motor.Position);
            }

            // On restaure et on rejoue : la trajectoire doit etre identique.
            motor.Restore(saved);

            for (int i = 0; i < 30; i++)
            {
                motor.Step(default, Step, world);

                ClassicAssert.AreEqual(reference[i], motor.Position,
                    $"le pas {i} apres restauration doit etre identique");
            }
        }

        // ===================================================================
        //  3.7 - COMPENSATION DE LATENCE
        // ===================================================================

        [Test]
        public void Un_Historique_Rembobine_Entre_Deux_Echantillons()
        {
            // ⚠️ On INTERPOLE plutot que de prendre le plus proche : le tireur
            // voyait une position interpolee, et une position echantillonnee
            // introduirait une erreur allant jusqu'a 7 cm a la course - l'ordre
            // de grandeur d'une tete.
            var history = new PlayerHistory();

            history.Record(10, new Vector3(0f, 0f, 0f), 1.83f);
            history.Record(11, new Vector3(1f, 0f, 0f), 1.83f);

            ClassicAssert.IsTrue(history.TryRewind(10.5, out Vector3 middle, out _));

            ClassicAssert.AreEqual(0.5f, middle.X, 1e-5f,
                "a mi-chemin entre deux pas, on doit etre a mi-chemin entre deux positions");
        }

        [Test]
        public void Un_Rembobinage_TROP_ANCIEN_Est_REFUSE()
        {
            // ⚠️ Un client qui annonce un temps de rendu vieux d'une seconde
            // ferait juger son tir sur un monde disparu : il tuerait des gens la
            // ou ils ne sont plus. On refuse au lieu d'extrapoler - extrapoler
            // reviendrait a valider un tir sur une cible imaginaire.
            var history = new PlayerHistory();

            for (uint t = 100; t < 200; t++)
                history.Record(t, new Vector3(t, 0f, 0f), 1.83f);

            ClassicAssert.LessOrEqual(history.Count, PlayerHistory.MaxSamples,
                "l'historique doit rester borne");

            ClassicAssert.IsFalse(history.TryRewind(50, out _, out _),
                "un pas anterieur a tout l'historique doit etre refuse");

            ClassicAssert.IsTrue(history.TryRewind(history.OldestTick, out _, out _),
                "mais le plus ancien disponible doit passer");
        }

        [Test]
        public void Le_Rembobinage_Fait_TOUCHER_Une_Cible_Qui_A_Bouge()
        {
            // ⚠️⚠️ LE TEST QUI JUSTIFIE 3.7, ET IL EST CONSTRUIT POUR ETRE
            // DISCRIMINANT : la cible s'est deplacee lateralement depuis
            // l'instant que voyait le tireur. Sans rembobinage le tir rate ;
            // avec, il touche. Les deux verdicts sont verifies.
            var world = new CollisionWorld();

            var ids = new List<byte> { 1, 2 };
            var histories = new List<PlayerHistory> { new(), new() };

            // Le tireur ne bouge pas.
            // La cible traverse la ligne de tir : elle etait en X = 0 au pas 100,
            // elle est en X = 3 au pas 130.
            for (uint t = 100; t <= 130; t++)
            {
                histories[0].Record(t, new Vector3(0f, 0f, 10f), 1.83f);
                histories[1].Record(t, new Vector3((t - 100) * 0.1f, 0f, 0f), 1.83f);
            }

            var origin = new Vector3(0f, 1.2f, 10f);
            var direction = new Vector3(0f, 0f, -1f);   // vers la cible d'AVANT

            // AVEC rembobinage au pas 100 : la cible etait pile devant.
            ShotResult rewound = LagCompensation.Resolve(
                shooterId: 1, origin, direction, 100f, ids, histories, rewindTick: 100, world);

            ClassicAssert.IsTrue(rewound.Hit, "en rembobinant, le tir bien vise doit TOUCHER");
            ClassicAssert.AreEqual(2, rewound.VictimId);

            ClassicAssert.IsTrue(rewound.RewindMattered,
                "et le rembobinage doit avoir CHANGE le verdict : c'est la mesure de 3.7");

            // SANS rembobinage (on juge sur le present) : la cible est a 3 m de
            // cote, le tir rate.
            ShotResult present = LagCompensation.Resolve(
                shooterId: 1, origin, direction, 100f, ids, histories,
                rewindTick: double.MaxValue, world);

            ClassicAssert.IsFalse(present.Hit,
                "sans rembobinage, le meme tir doit RATER - c'est le probleme qu'on corrige");
        }

        [Test]
        public void On_Ne_Tire_PAS_A_Travers_Un_Mur()
        {
            // ⚠️ Le decor n'est PAS rembobine - il ne bouge pas. Sans cette
            // borne, on toucherait quelqu'un a travers la geometrie des qu'il
            // est aligne, ce qui serait pire que l'absence de compensation.
            var world = new CollisionWorld();
            world.AddBox(new Vector3(0f, 1.5f, 5f), new Vector3(10f, 3f, 0.5f));

            var ids = new List<byte> { 1, 2 };
            var histories = new List<PlayerHistory> { new(), new() };

            for (uint t = 100; t <= 110; t++)
            {
                histories[0].Record(t, new Vector3(0f, 0f, 10f), 1.83f);
                histories[1].Record(t, new Vector3(0f, 0f, 0f), 1.83f);
            }

            ShotResult result = LagCompensation.Resolve(
                shooterId: 1, new Vector3(0f, 1.2f, 10f), new Vector3(0f, 0f, -1f),
                100f, ids, histories, 105, world);

            ClassicAssert.IsFalse(result.Hit, "le mur doit arreter la balle");
        }

        [Test]
        public void On_Ne_Se_Tire_Pas_Dessus_Soi_Meme()
        {
            var world = new CollisionWorld();
            var ids = new List<byte> { 1 };
            var histories = new List<PlayerHistory> { new() };

            for (uint t = 100; t <= 110; t++)
                histories[0].Record(t, new Vector3(0f, 0f, 10f), 1.83f);

            ShotResult result = LagCompensation.Resolve(
                shooterId: 1, new Vector3(0f, 1.2f, 10f), new Vector3(0f, 0f, -1f),
                100f, ids, histories, 105, world);

            ClassicAssert.IsFalse(result.Hit, "le tireur ne doit jamais etre sa propre cible");
        }

        [Test]
        public void Une_Direction_Nulle_Ou_NaN_Ne_Touche_Rien()
        {
            // Ca vient du RESEAU : un client modifie peut envoyer n'importe quoi.
            var world = new CollisionWorld();
            var ids = new List<byte> { 1, 2 };
            var histories = new List<PlayerHistory> { new(), new() };

            for (uint t = 100; t <= 110; t++)
            {
                histories[0].Record(t, new Vector3(0f, 0f, 10f), 1.83f);
                histories[1].Record(t, new Vector3(0f, 0f, 0f), 1.83f);
            }

            ClassicAssert.IsFalse(LagCompensation.Resolve(
                1, new Vector3(0f, 1.2f, 10f), Vector3.Zero, 100f, ids, histories, 105, world).Hit);

            ClassicAssert.IsFalse(LagCompensation.Resolve(
                1, new Vector3(0f, 1.2f, 10f), new Vector3(float.NaN, 0f, -1f),
                100f, ids, histories, 105, world).Hit);
        }

        [Test]
        public void Le_Plus_PROCHE_Est_Touche_Quand_Deux_Cibles_Sont_Alignees()
        {
            var world = new CollisionWorld();
            var ids = new List<byte> { 1, 2, 3 };
            var histories = new List<PlayerHistory> { new(), new(), new() };

            for (uint t = 100; t <= 110; t++)
            {
                histories[0].Record(t, new Vector3(0f, 0f, 10f), 1.83f);
                histories[1].Record(t, new Vector3(0f, 0f, 5f), 1.83f);    // proche
                histories[2].Record(t, new Vector3(0f, 0f, 0f), 1.83f);    // loin
            }

            ShotResult result = LagCompensation.Resolve(
                shooterId: 1, new Vector3(0f, 1.2f, 10f), new Vector3(0f, 0f, -1f),
                100f, ids, histories, 105, world);

            ClassicAssert.IsTrue(result.Hit);
            ClassicAssert.AreEqual(2, result.VictimId, "c'est le plus proche qui prend la balle");
        }
    }
}
