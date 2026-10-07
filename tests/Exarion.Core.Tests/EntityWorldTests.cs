// ===========================================================================
//  Tests des entites vivantes (E13).
//
//  Le piege dominant est la REPETITION. Un volume est teste soixante-quatre
//  fois par seconde ; sans memoire de « j'y etais deja », un checkpoint
//  franchi une fois s'enregistrerait soixante-quatre fois par seconde, et un
//  evenement deviendrait une avalanche. Ce genre de defaut ne casse rien
//  immediatement : il noie simplement tout le reste.
//
//  Les autres pieges couverts :
//   - un volume de taille nulle qui ne declenche JAMAIS, en silence ;
//   - un usage unique qui se rearme en ressortant ;
//   - un indice de volume qui survit a un changement de carte.
// ===========================================================================

using System;
using System.Numerics;
using NUnit.Framework;
using Exarion.Core.Assets;
using Exarion.Core.Entities;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class EntityWorldTests
    {
        /// <summary>Boite du joueur, 60 cm de cote sur 1,80 m de haut.</summary>
        private static Aabb Player(Vector3 feet)
            => new(feet + new Vector3(-0.3f, 0f, -0.3f), feet + new Vector3(0.3f, 1.8f, 0.3f));

        private static MapData OneTrigger(string type, params (string key, string value)[] props)
        {
            var map = new MapData();
            var entity = new MapEntity(type, new Vector3(0f, 1f, 0f));

            foreach ((string key, string value) in props)
                entity.Set(key, value);

            map.Entities.Add(entity);
            return map;
        }

        // ===================================================================
        //  Repetition
        // ===================================================================

        [Test]
        public void Rester_Dans_Un_Volume_N_Emet_Qu_UN_Evenement()
        {
            // ⚠️ LE test de ce fichier. Un volume est teste a chaque pas. Sans
            // memoire de l'etat precedent, ce test verrait 100 evenements au
            // lieu d'un - et en jeu, 64 par seconde.
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.Trigger, ("size", "4")));

            for (int i = 0; i < 100; i++)
                world.Update(Player(Vector3.Zero));

            ClassicAssert.AreEqual(1, world.TotalEvents, "le volume a emis a chaque pas");
        }

        [Test]
        public void Sortir_Puis_Rentrer_Emet_A_Nouveau()
        {
            // Le pendant du test precedent : la memoire ne doit pas non plus
            // condamner le declencheur apres un seul passage.
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.Trigger, ("size", "4")));

            world.Update(Player(Vector3.Zero));                    // entree
            world.Update(Player(new Vector3(50f, 0f, 0f)));         // sortie
            world.Update(Player(Vector3.Zero));                    // entree

            // 2 entrees + 1 sortie
            ClassicAssert.AreEqual(3, world.TotalEvents);
        }

        [Test]
        public void La_Sortie_Emet_OnExit()
        {
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.Trigger, ("size", "4")));

            world.Update(Player(Vector3.Zero));
            ClassicAssert.AreEqual(EntityWorld.OnEnter, world.Events[0].Output);

            world.Update(Player(new Vector3(50f, 0f, 0f)));
            ClassicAssert.AreEqual(1, world.Events.Count);
            ClassicAssert.AreEqual(EntityWorld.OnExit, world.Events[0].Output);
        }

        [Test]
        public void Les_Evenements_Ne_S_Accumulent_Pas_D_Un_Pas_A_L_Autre()
        {
            // Events decrit la DERNIERE mise a jour. Ne pas la vider ferait
            // grossir la liste indefiniment, et l'affichage rejouerait
            // eternellement le premier declenchement.
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.Trigger, ("size", "4")));

            world.Update(Player(Vector3.Zero));
            ClassicAssert.AreEqual(1, world.Events.Count);

            world.Update(Player(Vector3.Zero));
            ClassicAssert.AreEqual(0, world.Events.Count, "rien de neuf, donc rien a rapporter");
        }

        // ===================================================================
        //  Usage unique
        // ===================================================================

        [Test]
        public void Un_Declencheur_A_Usage_Unique_Ne_Se_Rearme_Pas()
        {
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.Trigger, ("size", "4"), ("once", "1")));

            world.Update(Player(Vector3.Zero));
            world.Update(Player(new Vector3(50f, 0f, 0f)));
            world.Update(Player(Vector3.Zero));
            world.Update(Player(new Vector3(50f, 0f, 0f)));
            world.Update(Player(Vector3.Zero));

            int enters = 0;
            // On recompte en rejouant : TotalEvents inclut les OnExit.
            var again = new EntityWorld();
            again.Load(OneTrigger(EntityTypes.Trigger, ("size", "4"), ("once", "1")));

            for (int i = 0; i < 5; i++)
            {
                again.Update(Player(Vector3.Zero));
                foreach (EntityEvent e in again.Events)
                    if (e.Output == EntityWorld.OnEnter) enters++;

                again.Update(Player(new Vector3(50f, 0f, 0f)));
            }

            ClassicAssert.AreEqual(1, enters, "l'usage unique s'est rearme");
        }

        [Test]
        public void Sans_once_Un_Declencheur_Se_Rearme()
        {
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.Trigger, ("size", "4")));

            int enters = 0;

            for (int i = 0; i < 3; i++)
            {
                world.Update(Player(Vector3.Zero));
                foreach (EntityEvent e in world.Events)
                    if (e.Output == EntityWorld.OnEnter) enters++;

                world.Update(Player(new Vector3(50f, 0f, 0f)));
            }

            ClassicAssert.AreEqual(3, enters);
        }

        // ===================================================================
        //  Checkpoints
        // ===================================================================

        [Test]
        public void Un_Checkpoint_Ne_S_Enregistre_Qu_Une_Fois_Meme_En_Repassant()
        {
            // ⚠️ C'est ce qui distingue un checkpoint d'un declencheur : meme
            // sans « once », repasser dessus ne doit pas allonger la liste.
            // Sinon un aller-retour ferait croire a deux checkpoints franchis.
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.Checkpoint, ("size", "4"), ("name", "hall")));

            for (int i = 0; i < 4; i++)
            {
                world.Update(Player(Vector3.Zero));
                world.Update(Player(new Vector3(50f, 0f, 0f)));
            }

            ClassicAssert.AreEqual(1, world.Checkpoints.Count);
            ClassicAssert.AreEqual("hall", world.Checkpoints[0]);
        }

        [Test]
        public void Les_Checkpoints_Sont_Dans_L_Ordre_DE_PASSAGE()
        {
            // Et pas dans l'ordre du fichier : c'est le parcours du joueur qu'on
            // enregistre, pas la maniere dont la carte a ete ecrite.
            var map = new MapData();

            map.Entities.Add(new MapEntity(EntityTypes.Checkpoint, new Vector3(0f, 1f, 0f))
                .Set("size", "3").Set("name", "un"));
            map.Entities.Add(new MapEntity(EntityTypes.Checkpoint, new Vector3(20f, 1f, 0f))
                .Set("size", "3").Set("name", "deux"));

            var world = new EntityWorld();
            world.Load(map);

            world.Update(Player(new Vector3(20f, 0f, 0f)));   // le second d'abord
            world.Update(Player(new Vector3(60f, 0f, 0f)));
            world.Update(Player(Vector3.Zero));

            ClassicAssert.AreEqual(2, world.Checkpoints.Count);
            ClassicAssert.AreEqual("deux", world.Checkpoints[0]);
            ClassicAssert.AreEqual("un", world.Checkpoints[1]);
        }

        // ===================================================================
        //  Volumes
        // ===================================================================

        [Test]
        public void Une_Taille_Nulle_Retombe_Sur_La_Valeur_Par_Defaut()
        {
            // ⚠️ Une taille nulle ou negative donne un intervalle INVERSE, que
            // Intersects ne croise jamais : le declencheur serait muet, et rien
            // ne le signalerait. Meme piege que les boites de decor.
            foreach (string bad in new[] { "0", "-4" })
            {
                var world = new EntityWorld();
                world.Load(OneTrigger(EntityTypes.Trigger, ("size", bad)));

                world.Update(Player(Vector3.Zero));

                ClassicAssert.AreEqual(1, world.TotalEvents, $"size={bad} n'a rien declenche");
            }
        }

        [Test]
        public void Le_Volume_Attrape_Un_Joueur_Qui_Saute_Par_Dessus()
        {
            // On teste contre la BOITE du joueur, pas ses pieds. Un declencheur
            // pose au sol raterait sinon quelqu'un qui passe en sautant, alors
            // que son corps traverse bien le volume.
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.Trigger, ("size", "2"), ("height", "1")));

            // Pieds a 0,9 m : au-dessus du volume, mais le corps le traverse.
            world.Update(Player(new Vector3(0f, 0.9f, 0f)));

            ClassicAssert.AreEqual(1, world.TotalEvents);
        }

        [Test]
        public void Un_Volume_Loin_Ne_Declenche_Pas()
        {
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.Trigger, ("size", "2")));

            world.Update(Player(new Vector3(30f, 0f, 30f)));

            ClassicAssert.AreEqual(0, world.TotalEvents);
        }

        [Test]
        public void Une_Entite_Qui_N_Est_Pas_Un_Volume_Ne_Declenche_Jamais()
        {
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.PlayerSpawn));

            for (int i = 0; i < 10; i++)
                world.Update(Player(Vector3.Zero));

            ClassicAssert.AreEqual(0, world.TotalEvents);
        }

        // ===================================================================
        //  Cablage
        // ===================================================================

        [Test]
        public void Une_Sortie_Cablee_Porte_Sa_Cible_Et_Son_Entree()
        {
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.Trigger,
                ("size", "4"), ("target", "porte_hall"), ("input", "Ouvrir")));

            world.Update(Player(Vector3.Zero));

            EntityEvent e = world.Events[0];

            ClassicAssert.AreEqual("porte_hall", e.Target);
            ClassicAssert.AreEqual("Ouvrir", e.Input);
            Assert.That(e.ToString(), Does.Contain("porte_hall.Ouvrir"));
        }

        [Test]
        public void Une_Sortie_NON_Cablee_Emet_Quand_Meme()
        {
            // Un declenchement non cable doit se VOIR. Le taire ferait croire
            // que le volume ne fonctionne pas, alors qu'il fonctionne mais ne
            // vise personne - deux problemes tres differents.
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.Trigger, ("size", "4")));

            world.Update(Player(Vector3.Zero));

            ClassicAssert.AreEqual(1, world.Events.Count);
            Assert.That(world.Events[0].Target, Is.Empty);
        }

        [Test]
        public void Une_Cible_Se_Resout_Par_Son_Nom()
        {
            var map = new MapData();
            map.Entities.Add(new MapEntity(EntityTypes.Trigger, Vector3.Zero).Set("target", "cible"));
            map.Entities.Add(new MapEntity(EntityTypes.Npc, new Vector3(9f, 0f, 9f)).Set("name", "cible"));

            var world = new EntityWorld();
            world.Load(map);

            MapEntity? found = world.Find("cible");

            Assert.That(found, Is.Not.Null);
            ClassicAssert.AreEqual(EntityTypes.Npc, found!.Type);
            Assert.That(world.Find("personne"), Is.Null);
            Assert.That(world.Find(""), Is.Null);
        }

        // ===================================================================
        //  Changement de carte
        // ===================================================================

        [Test]
        public void Charger_Une_Autre_Carte_Remet_Tout_A_Zero()
        {
            // ⚠️ Un indice de volume renvoie a une AUTRE entite d'une carte a
            // l'autre. Garder l'etat ferait qu'un declencheur consomme bloque
            // un declencheur sans rapport dans le niveau suivant.
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.Trigger, ("size", "4"), ("once", "1")));

            world.Update(Player(Vector3.Zero));
            ClassicAssert.AreEqual(1, world.TotalEvents);

            world.Load(OneTrigger(EntityTypes.Trigger, ("size", "4"), ("once", "1")));
            world.Update(Player(Vector3.Zero));

            // Le compteur total ne se remet pas a zero, mais le declencheur
            // doit avoir emis a nouveau.
            ClassicAssert.AreEqual(2, world.TotalEvents, "l'etat de l'ancienne carte a survecu");
            ClassicAssert.AreEqual(0, world.Checkpoints.Count);
        }

        [Test]
        public void Une_Entite_Ajoutee_En_Cours_De_Route_Est_Prise_En_Compte()
        {
            // L'editeur ajoute des entites pendant que le monde tourne. Sortir
            // en erreur sur un indice hors bornes serait le pire comportement.
            var map = OneTrigger(EntityTypes.Trigger, ("size", "4"));

            var world = new EntityWorld();
            world.Load(map);
            world.Update(Player(new Vector3(50f, 0f, 0f)));

            map.Entities.Add(new MapEntity(EntityTypes.Trigger, new Vector3(50f, 1f, 0f))
                .Set("size", "4"));

            Assert.DoesNotThrow(() => world.Update(Player(new Vector3(50f, 0f, 0f))));
            ClassicAssert.AreEqual(1, world.TotalEvents);
        }

        [Test]
        public void Rearmer_Efface_Les_Checkpoints_Et_Les_Usages_Uniques()
        {
            var world = new EntityWorld();
            world.Load(OneTrigger(EntityTypes.Checkpoint, ("size", "4"), ("name", "un"), ("once", "1")));

            world.Update(Player(Vector3.Zero));
            ClassicAssert.AreEqual(1, world.Checkpoints.Count);

            world.Rearm();
            ClassicAssert.AreEqual(0, world.Checkpoints.Count);

            world.Update(Player(Vector3.Zero));
            ClassicAssert.AreEqual(1, world.Checkpoints.Count, "le rearmement n'a pas rendu le volume actif");
        }
    }
}
