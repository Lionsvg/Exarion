// ===========================================================================
//  Tests des entites LOGIQUES : relais, minuterie, compteur
//
//  Les pieges, dans l'ordre ou ils coutent :
//
//   - LA BOUCLE. Deux relais qui se visent s'appelleraient a l'infini dans
//     le meme pas. La file les fait battre a un evenement par pas.
//   - LA DERIVE. Une minuterie qui recale sur « maintenant » a chaque
//     sortie prend un pas de retard a chaque fois : 96 sorties en 30 s au
//     lieu de 100. Seul un comptage long la voit.
//   - LE DEBORDEMENT. Une file sans borne finit par manger la memoire ; une
//     file bornee qui refuse EN SILENCE fait « arreter de marcher » un niveau
//     sans raison visible. On borne ET on compte.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core.Assets;
using Exarion.Core.Entities;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class LogicWorldTests
    {
        private const float Dt = 1f / 64f;

        private static MapEntity Entity(string type, string name, params (string Key, string Value)[] props)
        {
            var e = new MapEntity(type, Vector3.Zero);

            if (name.Length > 0)
                e.Set("name", name);

            foreach ((string k, string v) in props)
                e.Set(k, v);

            return e;
        }

        /// <summary>Les mondes d'entites cables comme dans le jeu, et dans le meme ordre.</summary>
        private sealed class Rig
        {
            public readonly EntityWorld Entities = new();
            public readonly MoverWorld Movers = new();
            public readonly LogicWorld Logic = new();

            /// <summary>Une ligne par sortie logique livree : « pas source.sortie -> cible.entree etat ».</summary>
            public readonly List<string> Log = new();

            public int StepIndex;

            public Rig(params MapEntity[] entities)
            {
                var map = new MapData();
                map.Entities.AddRange(entities);

                // ⚠️ Load efface les cibles inscrites : les portes et la logique
                // s'inscrivent APRES, exactement comme GameWorld.
                Entities.Load(map);
                Movers.Load(map);
                Movers.RegisterInto(Entities);
                Logic.Load(map);
                Logic.RegisterInto(Entities);
            }

            public void Step(int count = 1)
            {
                for (int i = 0; i < count; i++)
                {
                    StepIndex++;
                    Movers.Step(Dt);
                    Movers.DeliverOutputs(Entities, _ => { });
                    Logic.Step(Dt, Entities);

                    foreach (EntityEvent e in Logic.Events)
                        Log.Add($"{StepIndex} {e.Source.Get("name")}.{e.Output} -> {e.Target}.{e.Input} {e.Delivery}");
                }
            }

            public EntityDelivery Send(string target, string input) => Entities.Send(target, input);
        }

        // ===================================================================
        //  Relais
        // ===================================================================

        [Test]
        public void Un_Relais_Ouvre_Une_Porte_Au_Pas_Suivant()
        {
            var rig = new Rig(
                Entity(EntityTypes.LogicRelay, "relais", ("target", "porte"), ("input", "Ouvrir")),
                Entity(EntityTypes.Door, "porte"));

            ClassicAssert.AreEqual(EntityDelivery.Delivree, rig.Send("relais", LogicNode.InputTrigger));
            ClassicAssert.AreEqual(MoverState.Ferme, rig.Movers.Find("porte")!.State, "rien ne part avant le pas");

            rig.Step();

            ClassicAssert.AreEqual(1, rig.Log.Count, string.Join(" | ", rig.Log));
            Assert.That(rig.Log[0], Does.Contain("relais.OnDeclenche -> porte.Ouvrir Delivree"));
            ClassicAssert.AreEqual(MoverState.Ouverture, rig.Movers.Find("porte")!.State);
        }

        [Test]
        public void Le_Delai_Tombe_Au_Pas_Pres()
        {
            // 2 s a 64 pas/s : exactement 128 pas, ni 127, ni 129.
            var rig = new Rig(Entity(EntityTypes.LogicRelay, "relais", ("delay", "2")));

            rig.Send("relais", LogicNode.InputTrigger);

            rig.Step(127);
            Assert.That(rig.Log, Is.Empty, "parti trop tot");

            rig.Step();
            ClassicAssert.AreEqual(1, rig.Log.Count);
            Assert.That(rig.Log[0], Does.StartWith("128 "));
        }

        [Test]
        public void Desactive_Le_Relais_Entend_Mais_Ne_Relaie_Pas()
        {
            var rig = new Rig(Entity(EntityTypes.LogicRelay, "relais", ("start_disabled", "1")));

            // L'entree est CONNUE : la livraison reussit. Repondre « entree
            // inconnue » ferait chercher une faute de frappe inexistante.
            ClassicAssert.AreEqual(EntityDelivery.Delivree, rig.Send("relais", LogicNode.InputTrigger));
            rig.Step(10);
            Assert.That(rig.Log, Is.Empty);

            rig.Send("relais", LogicNode.InputEnable);
            rig.Send("relais", LogicNode.InputTrigger);
            rig.Step();

            ClassicAssert.AreEqual(1, rig.Log.Count);
        }

        [Test]
        public void Un_Relais_A_Usage_Unique_Ne_Relaie_Qu_Une_Fois()
        {
            var rig = new Rig(Entity(EntityTypes.LogicRelay, "relais", ("once", "1")));

            for (int i = 0; i < 3; i++)
            {
                rig.Send("relais", LogicNode.InputTrigger);
                rig.Step();
            }

            ClassicAssert.AreEqual(1, rig.Log.Count);

            // Reactive, il relaie de nouveau - une fois.
            rig.Send("relais", LogicNode.InputEnable);
            rig.Send("relais", LogicNode.InputTrigger);
            rig.Send("relais", LogicNode.InputTrigger);
            rig.Step();

            ClassicAssert.AreEqual(2, rig.Log.Count);
        }

        [Test]
        public void Annuler_Retire_Les_Sorties_En_Attente()
        {
            var rig = new Rig(Entity(EntityTypes.LogicRelay, "relais", ("delay", "1")));

            rig.Send("relais", LogicNode.InputTrigger);
            rig.Step(10);
            rig.Send("relais", LogicNode.InputCancel);
            rig.Step(64 * 2);

            Assert.That(rig.Log, Is.Empty);
            ClassicAssert.AreEqual(0, rig.Logic.PendingCount);
        }

        /// <summary>
        /// ⚠️ LE test de ce fichier. Deux relais qui se visent l'un l'autre
        /// sans delai : livres dans le meme pas, ils s'appelleraient a
        /// l'infini et le jeu se figerait sans un message. Ils doivent BATTRE,
        /// un evenement par pas, avec une seule sortie en attente.
        /// </summary>
        [Test]
        public void Deux_Relais_Qui_Se_Visent_Battent_Au_Lieu_De_Geler()
        {
            var rig = new Rig(
                Entity(EntityTypes.LogicRelay, "a", ("target", "b"), ("input", LogicNode.InputTrigger)),
                Entity(EntityTypes.LogicRelay, "b", ("target", "a"), ("input", LogicNode.InputTrigger)));

            rig.Send("a", LogicNode.InputTrigger);

            for (int i = 0; i < 100; i++)
            {
                rig.Step();

                ClassicAssert.AreEqual(1, rig.Logic.Events.Count, $"pas {i + 1}");
                ClassicAssert.AreEqual(1, rig.Logic.PendingCount, $"pas {i + 1}");
            }

            ClassicAssert.AreEqual(100, rig.Logic.TotalEvents);
            Assert.That(rig.Log[0], Does.Contain("a.OnDeclenche -> b.Declencher"));
            Assert.That(rig.Log[1], Does.Contain("b.OnDeclenche -> a.Declencher"));
        }

        // ===================================================================
        //  Minuterie
        // ===================================================================

        [Test]
        public void Une_Minuterie_Tombe_Tous_Les_N_Pas()
        {
            // 0,25 s = 16 pas exactement : chaque sortie tombe sur un multiple de 16.
            var rig = new Rig(Entity(EntityTypes.LogicTimer, "horloge", ("interval", "0.25")));

            rig.Step(64 * 10);

            ClassicAssert.AreEqual(40, rig.Log.Count);

            foreach (string line in rig.Log)
                ClassicAssert.AreEqual(0, int.Parse(line.Split(' ')[0]) % 16, line);
        }

        /// <summary>
        /// ⚠️ LA DERIVE. 0,3 s ne tombe pas sur un pas (19,2 pas) : chaque
        /// sortie arrive au premier pas APRES l'echeance. Recaler l'echeance
        /// suivante sur « maintenant » ajouterait ce retard a chaque fois -
        /// 96 sorties en 30 s au lieu de 100. Calculee depuis la precedente,
        /// l'echeance ne derive pas.
        /// </summary>
        [Test]
        public void Une_Minuterie_Ne_Derive_Pas()
        {
            var rig = new Rig(Entity(EntityTypes.LogicTimer, "horloge", ("interval", "0.3")));

            // Un pas de plus que 30 s : en float, 0,3 vaut 0,30000001, et la
            // centieme echeance tombe a 30,0000012 s - juste APRES le pas
            // 1920. Ecrit d'abord a 30 s pile, ce test comptait 99 : c'etait
            // la representation de 0,3, pas une derive (une derive en donne
            // environ 96).
            rig.Step(64 * 30 + 1);

            ClassicAssert.AreEqual(100, rig.Log.Count);
        }

        [Test]
        public void Arreter_Puis_Demarrer_Repart_D_Un_Intervalle_Entier()
        {
            var rig = new Rig(Entity(EntityTypes.LogicTimer, "horloge", ("interval", "1"), ("start_disabled", "1")));

            rig.Step(64 * 3);
            Assert.That(rig.Log, Is.Empty, "arretee au depart");

            rig.Send("horloge", LogicNode.InputStart);
            rig.Step(63);
            Assert.That(rig.Log, Is.Empty, "un intervalle ENTIER apres le demarrage");
            rig.Step();
            ClassicAssert.AreEqual(1, rig.Log.Count);

            // ⚠️ Redemarrer une minuterie qui tourne ne repousse PAS l'echeance :
            // sinon un declencheur qui renvoie « Demarrer » a chaque passage la
            // ferait taire indefiniment.
            rig.Step(32);
            rig.Send("horloge", LogicNode.InputStart);
            rig.Step(32);
            ClassicAssert.AreEqual(2, rig.Log.Count);

            rig.Send("horloge", LogicNode.InputStop);
            rig.Step(64 * 3);
            ClassicAssert.AreEqual(2, rig.Log.Count, "arretee, elle se tait");
        }

        // ===================================================================
        //  Compteur
        // ===================================================================

        [Test]
        public void Un_Compteur_Emet_En_ATTEIGNANT_Son_Maximum()
        {
            var rig = new Rig(Entity(EntityTypes.LogicCounter, "compteur", ("max", "3")));

            for (int i = 0; i < 5; i++)
            {
                rig.Send("compteur", LogicNode.InputAdd);
                rig.Step();
            }

            // Une seule sortie : les ajouts au-dela du maximum ne rouvrent pas
            // trois fois une porte deja ouverte.
            ClassicAssert.AreEqual(1, rig.Log.Count, string.Join(" | ", rig.Log));
            ClassicAssert.AreEqual(3, rig.Logic.Find("compteur")!.Value);

            // Redescendre puis remonter, c'est l'atteindre DE NOUVEAU.
            rig.Send("compteur", LogicNode.InputSubtract);
            rig.Send("compteur", LogicNode.InputAdd);
            rig.Step();
            ClassicAssert.AreEqual(2, rig.Log.Count);

            rig.Send("compteur", LogicNode.InputReset);
            ClassicAssert.AreEqual(0, rig.Logic.Find("compteur")!.Value);
        }

        // ===================================================================
        //  La file
        // ===================================================================

        /// <summary>
        /// Une minuterie qui tombe a chaque pas, vers un relais qui attend
        /// 100 s : la file grossit d'une sortie par pas. A 256 elle REFUSE, et
        /// chaque refus est COMPTE.
        /// </summary>
        [Test]
        public void La_File_Est_Bornee_Et_Le_Debordement_Compte()
        {
            var rig = new Rig(
                Entity(EntityTypes.LogicTimer, "rafale", ("interval", "0.01"), ("target", "lent"), ("input", LogicNode.InputTrigger)),
                Entity(EntityTypes.LogicRelay, "lent", ("delay", "100")));

            rig.Step(300);

            ClassicAssert.AreEqual(LogicWorld.MaxQueued, rig.Logic.PendingCount);
            ClassicAssert.AreEqual(300 - LogicWorld.MaxQueued, rig.Logic.Dropped);
        }

        [Test]
        public void Recharger_Vide_La_File()
        {
            // Une sortie promise par l'ancienne carte ne doit pas tomber sur une
            // entite de la nouvelle qui porterait le meme nom.
            var rig = new Rig(Entity(EntityTypes.LogicRelay, "relais", ("delay", "1")));

            rig.Send("relais", LogicNode.InputTrigger);
            ClassicAssert.AreEqual(1, rig.Logic.PendingCount);

            var map = new MapData();
            map.Entities.Add(Entity(EntityTypes.LogicRelay, "relais"));
            rig.Logic.Load(map);

            ClassicAssert.AreEqual(0, rig.Logic.PendingCount);
        }

        // ===================================================================
        //  Bout en bout
        // ===================================================================

        /// <summary>
        /// La chaine complete, avec un VRAI volume : traverser deux fois le
        /// couloir fait monter le compteur a 2, qui declenche le relais, qui
        /// ouvre la porte une seconde plus tard. Rien n'est appele a la main.
        /// </summary>
        [Test]
        public void Volume_Compteur_Relais_Porte()
        {
            var volume = new MapEntity(EntityTypes.Trigger, new Vector3(0f, 1f, 0f))
                .Set("size", "3").Set("target", "compteur").Set("input", LogicNode.InputAdd);

            var rig = new Rig(
                volume,
                Entity(EntityTypes.LogicCounter, "compteur", ("max", "2"), ("target", "relais"), ("input", LogicNode.InputTrigger)),
                Entity(EntityTypes.LogicRelay, "relais", ("delay", "1"), ("target", "porte"), ("input", "Ouvrir")),
                Entity(EntityTypes.Door, "porte"));

            Aabb inside = Aabb.ForCharacter(new Vector3(0f, 0f, 0f), 0.8f, 1.8f);
            Aabb outside = Aabb.ForCharacter(new Vector3(30f, 0f, 0f), 0.8f, 1.8f);

            void Walk(Aabb at)
            {
                rig.Entities.Update(at);
                rig.Step();
            }

            Walk(inside);
            Walk(outside);
            ClassicAssert.AreEqual(1, rig.Logic.Find("compteur")!.Value);
            ClassicAssert.AreEqual(MoverState.Ferme, rig.Movers.Find("porte")!.State, "un seul passage");

            Walk(inside);
            Walk(outside);
            rig.Step(64);

            ClassicAssert.AreEqual(MoverState.Ouverture, rig.Movers.Find("porte")!.State,
                                   "deux passages : la porte s'ouvre. Journal : " + string.Join(" | ", rig.Log));
        }
    }
}
