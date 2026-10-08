// ===========================================================================
//  Tests des piles de caisses declarees par une carte (prop_crate)
//
//  Avant le 07/10, le bac a sable lachait quatre caisses en (0, 0, 8) apres
//  N'IMPORTE QUELLE carte. Desormais c'est la carte qui les declare : ces
//  tests verifient ce qu'elle obtient, sans ouvrir de fenetre.
// ===========================================================================

using System.Numerics;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core.Assets;
using Exarion.Core.Editor;
using Exarion.Core.Entities;

namespace Exarion.Tests
{
    public class CrateStackTests
    {
        private static MapData MapWith(params MapEntity[] entities)
        {
            var map = new MapData { Name = "essai" };
            foreach (MapEntity e in entities)
                map.Entities.Add(e);
            return map;
        }

        private static MapEntity Crate(Vector3 at, params (string Key, string Value)[] props)
        {
            var e = new MapEntity(EntityTypes.Crate, at);
            foreach (var (key, value) in props)
                e.Set(key, value);
            return e;
        }

        [Test]
        public void Une_Carte_Sans_Caisse_N_En_Fait_Naitre_Aucune()
        {
            var map = MapWith(new MapEntity(EntityTypes.PlayerSpawn, new Vector3(0f, 0f, 8f)));

            Assert.That(CrateStacks.From(map), Is.Empty);
        }

        [Test]
        public void Les_Defauts_Sont_Ceux_Du_Schema()
        {
            CrateSpawn c = CrateStacks.From(MapWith(Crate(Vector3.Zero)))[0];

            ClassicAssert.AreEqual(CrateStacks.DefaultSize, c.Size);
            ClassicAssert.AreEqual(CrateStacks.DefaultMass, c.Mass);

            // Le schema affiche ces defauts dans le panneau : s'ils divergeaient
            // du code, l'auteur du niveau lirait un etat qui n'existe pas.
            foreach (PropertySpec spec in EntitySchema.For(EntityTypes.Crate))
            {
                if (spec.Key == "size") ClassicAssert.AreEqual(CrateStacks.DefaultSize.ToString(System.Globalization.CultureInfo.InvariantCulture), spec.Default);
                if (spec.Key == "mass") ClassicAssert.AreEqual(CrateStacks.DefaultMass.ToString(System.Globalization.CultureInfo.InvariantCulture), spec.Default);
                if (spec.Key == "count") ClassicAssert.AreEqual("1", spec.Default);
            }
        }

        /// <summary>
        /// La pile se pose SUR le point de l'entite et monte, avec un jeu de
        /// 2 cm : la formule du bac a sable historique, a l'identique, pour
        /// que le decor de reference retombe exactement de la meme facon.
        /// </summary>
        [Test]
        public void Une_Pile_Monte_Depuis_Le_Sol_Avec_Un_Jeu()
        {
            var spawns = CrateStacks.From(MapWith(Crate(new Vector3(0f, 0f, 8f), ("count", "4"))));

            ClassicAssert.AreEqual(4, spawns.Count);

            for (int i = 0; i < 4; i++)
            {
                float expected = 0.9f * 0.5f + i * (0.9f + 0.02f);
                ClassicAssert.AreEqual(expected, spawns[i].Center.Y, 1e-5f, $"caisse {i}");
                ClassicAssert.AreEqual(8f, spawns[i].Center.Z);
            }
        }

        [Test]
        public void Les_Valeurs_Absurdes_Sont_Ramenees_Pas_Refusees()
        {
            var spawns = CrateStacks.From(MapWith(
                Crate(Vector3.Zero, ("size", "0"), ("mass", "-3"), ("count", "1000"))));

            ClassicAssert.AreEqual(CrateStacks.MaxCount, spawns.Count);
            ClassicAssert.AreEqual(CrateStacks.MinSize, spawns[0].Size);
            ClassicAssert.AreEqual(CrateStacks.MinMass, spawns[0].Mass);
        }

        [Test]
        public void Un_Compte_Nul_Donne_Quand_Meme_Une_Caisse()
        {
            ClassicAssert.AreEqual(1, CrateStacks.From(MapWith(Crate(Vector3.Zero, ("count", "0")))).Count);
        }

        /// <summary>
        /// L'ordre est celui de l'insertion dans le monde physique : fichier
        /// d'abord, puis de bas en haut. Le changer changerait l'effondrement.
        /// </summary>
        [Test]
        public void L_Ordre_Est_Celui_Du_Fichier_Puis_Du_Bas_Vers_Le_Haut()
        {
            var spawns = CrateStacks.From(MapWith(
                Crate(new Vector3(5f, 0f, 0f), ("count", "2")),
                Crate(new Vector3(-5f, 0f, 0f))));

            ClassicAssert.AreEqual(3, spawns.Count);
            ClassicAssert.AreEqual(5f, spawns[0].Center.X);
            ClassicAssert.AreEqual(5f, spawns[1].Center.X);
            ClassicAssert.Less(spawns[0].Center.Y, spawns[1].Center.Y);
            ClassicAssert.AreEqual(-5f, spawns[2].Center.X);
        }
    }
}
