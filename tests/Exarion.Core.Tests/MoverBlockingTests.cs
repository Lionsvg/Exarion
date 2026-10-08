// ===========================================================================
//  Tests : une porte qui se referme sur quelqu'un
//
//  Avant le 08/10, une porte avancait sans regarder : refermee sur le joueur,
//  elle le chevauchait, et c'etait au CharacterMotor de l'en sortir - le
//  README le notait, « elle le repousse, pas de notion de blocage ». Une porte
//  de Source REPART en arriere ; c'est le comportement par defaut ici aussi.
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
    public class MoverBlockingTests
    {
        private const float Dt = 1f / 64f;

        /// <summary>Une porte de 3 m qui monte de 3 m pour s'ouvrir, deja ouverte.</summary>
        private static (MoverWorld World, Mover Door) OpenDoor(string? blocked = null)
        {
            var entity = new MapEntity(EntityTypes.Door, Vector3.Zero)
                .Set("name", "porte").Set("height", "3").Set("width", "3").Set("distance", "3").Set("speed", "3");

            if (blocked != null)
                entity.Set("blocked", blocked);

            var map = new MapData();
            map.Entities.Add(entity);

            var world = new MoverWorld();
            world.Load(map);

            Mover door = world.Movers[0];
            door.Receive(Mover.InputOpen);

            for (int i = 0; i < 128; i++)
                world.Step(Dt);

            ClassicAssert.AreEqual(MoverState.Ouvert, door.State);
            return (world, door);
        }

        /// <summary>Le joueur, debout dans l'embrasure : 1,83 m sous une porte qui redescend.</summary>
        private static readonly Aabb PlayerInDoorway = Aabb.ForCharacter(Vector3.Zero, 0.8f, 1.83f);

        [Test]
        public void Une_Porte_Qui_Se_Referme_Sur_Le_Joueur_Repart_En_Arriere()
        {
            (MoverWorld world, Mover door) = OpenDoor();
            var blockers = new List<Aabb> { PlayerInDoorway };
            var outputs = new List<string>();

            door.Receive(Mover.InputClose);

            for (int i = 0; i < 128; i++)
            {
                world.Step(Dt, blockers);

                if (door.PendingOutput.Length > 0)
                    outputs.Add(door.PendingOutput);

                // ⚠️ L'invariant : la porte ne chevauche JAMAIS le joueur.
                ClassicAssert.IsFalse(door.Box.Intersects(PlayerInDoorway), $"pas {i} : la porte ecrase le joueur");
            }

            Assert.That(outputs, Does.Contain(Mover.OnBlocked), "le blocage doit se DIRE - une sortie, comme les fins de course");
            Assert.That(door.State, Is.EqualTo(MoverState.Ouvert).Or.EqualTo(MoverState.Ouverture),
                        "bloquee, elle repart vers l'ouverture");
        }

        [Test]
        public void Une_Porte_Bloquee_En_Mode_Stop_Attend_Sans_Avancer()
        {
            (MoverWorld world, Mover door) = OpenDoor("stop");
            var blockers = new List<Aabb> { PlayerInDoorway };
            int blockedEvents = 0;

            door.Receive(Mover.InputClose);

            for (int i = 0; i < 128; i++)
            {
                world.Step(Dt, blockers);
                ClassicAssert.IsFalse(door.Box.Intersects(PlayerInDoorway), $"pas {i}");

                if (door.PendingOutput == Mover.OnBlocked)
                    blockedEvents++;
            }

            ClassicAssert.AreEqual(MoverState.Fermeture, door.State, "elle attend, toujours en fermeture");

            // ⚠️ UNE sortie pour tout le blocage, pas une par pas d'attente :
            // sinon 64 evenements par seconde, la classe de defaut des volumes
            // sans memoire.
            ClassicAssert.AreEqual(1, blockedEvents);

            // L'obstacle s'en va : elle finit sa course.
            blockers.Clear();

            for (int i = 0; i < 128; i++)
                world.Step(Dt, blockers);

            ClassicAssert.AreEqual(MoverState.Ferme, door.State);
        }

        /// <summary>
        /// L'ancien comportement reste disponible, et c'est la contre-epreuve :
        /// « push » chevauche le joueur, comme avant le 08/10.
        /// </summary>
        [Test]
        public void En_Mode_Push_La_Porte_Passe_Comme_Avant()
        {
            (MoverWorld world, Mover door) = OpenDoor("push");
            var blockers = new List<Aabb> { PlayerInDoorway };

            door.Receive(Mover.InputClose);

            for (int i = 0; i < 128; i++)
                world.Step(Dt, blockers);

            ClassicAssert.AreEqual(MoverState.Ferme, door.State);
            ClassicAssert.IsTrue(door.Box.Intersects(PlayerInDoorway));
        }

        /// <summary>
        /// ⚠️ Un PASSAGER n'est pas un obstacle. Une plateforme qui monte porte
        /// quelqu'un dont les pieds touchent son dessus : le compter comme un
        /// blocage figerait tout ascenseur des qu'on monte dessus.
        /// </summary>
        [Test]
        public void Un_Passager_Ne_Bloque_Pas_Son_Ascenseur()
        {
            var entity = new MapEntity(EntityTypes.Door, Vector3.Zero)
                .Set("name", "ascenseur").Set("height", "0.4").Set("width", "4").Set("depth", "4")
                .Set("distance", "3").Set("speed", "2");

            var map = new MapData();
            map.Entities.Add(entity);

            var world = new MoverWorld();
            world.Load(map);

            Mover lift = world.Movers[0];
            Vector3 feet = new(0f, lift.Box.Max.Y, 0f);

            lift.Receive(Mover.InputOpen);

            for (int i = 0; i < 128; i++)
            {
                var blockers = new List<Aabb> { Aabb.ForCharacter(feet, 0.8f, 1.83f) };
                world.Step(Dt, blockers);
                world.CarryRider(ref feet, Aabb.ForCharacter(feet, 0.8f, 1.83f));
            }

            ClassicAssert.AreEqual(MoverState.Ouvert, lift.State, "l'ascenseur doit arriver en haut");
            ClassicAssert.AreEqual(lift.Box.Max.Y, feet.Y, 0.05f, "et son passager avec lui");
        }

        /// <summary>
        /// ⚠️ Trouve EN JEU : un joueur teleporte dans une porte fermee la
        /// bloquait a l'ouverture - elle oscillait sur place sans jamais
        /// s'ouvrir. Une porte doit toujours pouvoir S'ELOIGNER de ce qu'elle
        /// chevauche deja.
        /// </summary>
        [Test]
        public void Une_Porte_Qui_Chevauche_Deja_Le_Joueur_Peut_S_Ouvrir()
        {
            var entity = new MapEntity(EntityTypes.Door, Vector3.Zero)
                .Set("name", "porte").Set("height", "3").Set("width", "3").Set("distance", "3").Set("speed", "3");

            var map = new MapData();
            map.Entities.Add(entity);

            var world = new MoverWorld();
            world.Load(map);

            Mover door = world.Movers[0];
            ClassicAssert.IsTrue(door.Box.Intersects(PlayerInDoorway), "le scenario : le joueur est DANS la porte fermee");

            var blockers = new List<Aabb> { PlayerInDoorway };
            door.Receive(Mover.InputOpen);

            for (int i = 0; i < 128; i++)
                world.Step(Dt, blockers);

            ClassicAssert.AreEqual(MoverState.Ouvert, door.State);
        }

        [Test]
        public void Un_Mode_De_Blocage_Inconnu_Retombe_Sur_Le_Retour()
        {
            (MoverWorld world, Mover door) = OpenDoor("ecraser");
            var blockers = new List<Aabb> { PlayerInDoorway };

            door.Receive(Mover.InputClose);

            for (int i = 0; i < 128; i++)
            {
                world.Step(Dt, blockers);
                ClassicAssert.IsFalse(door.Box.Intersects(PlayerInDoorway), $"pas {i}");
            }
        }
    }
}
