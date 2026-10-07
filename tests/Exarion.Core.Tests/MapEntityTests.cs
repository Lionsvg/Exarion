// ===========================================================================
//  Tests des entites dans le format de carte.
//
//  Deux pieges dominent, et tous deux sont silencieux :
//
//   - L'ORDRE DES PROPRIETES. Un Dictionary ne garantit pas son ordre de
//     parcours. Deux enregistrements de la meme carte donneraient deux
//     fichiers differents, et l'aller-retour octet pour octet - la preuve
//     centrale de 4.2 - tomberait sans qu'on comprenne pourquoi.
//
//   - LA COPIE. MapEntity est une CLASSE portant un dictionnaire, alors que
//     MapBox et MapLight sont des structures. Une copie superficielle rendrait
//     l'annulation de l'editeur fausse pour les entites SEULEMENT, ce qui est
//     le genre de defaut qu'on cherche pendant des heures au mauvais endroit.
// ===========================================================================

using System;
using System.Numerics;
using NUnit.Framework;
using Exarion.Core.Assets;
using Exarion.Core.Editor;

namespace Exarion.Tests
{
    public class MapEntityTests
    {
        private static MapData WithEntities()
        {
            var map = new MapData { Name = "essai entites", Spawn = new Vector3(0f, 1f, 5f) };

            map.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), new Vector3(0.3f));

            map.Entities.Add(new MapEntity(EntityTypes.PlayerSpawn, new Vector3(4f, 0f, 8f), 90f)
                .Set("team", "attaque"));

            map.Entities.Add(new MapEntity(EntityTypes.PlayerSpawn, new Vector3(-4f, 0f, 8f), -90f)
                .Set("team", "defense"));

            map.Entities.Add(new MapEntity(EntityTypes.Checkpoint, new Vector3(0f, 1f, -6f))
                .Set("order", "1").Set("size", "4"));

            map.Entities.Add(new MapEntity(EntityTypes.Npc, new Vector3(10f, 0f, -10f), 45f, -3f)
                .Set("speed", "2.5").Set("path", "boucle_nord"));

            return map;
        }

        // ===================================================================
        //  Aller-retour
        // ===================================================================

        [Test]
        public void Un_Aller_Retour_Restitue_Les_Entites()
        {
            MapData source = WithEntities();
            MapData back = MapFile.Parse(MapFile.Save(source));

            ClassicAssert.AreEqual(source.Entities.Count, back.Entities.Count);

            for (int i = 0; i < source.Entities.Count; i++)
            {
                MapEntity a = source.Entities[i];
                MapEntity b = back.Entities[i];

                ClassicAssert.AreEqual(a.Type, b.Type);
                ClassicAssert.AreEqual(a.Position.X, b.Position.X, 1e-6f);
                ClassicAssert.AreEqual(a.Position.Y, b.Position.Y, 1e-6f);
                ClassicAssert.AreEqual(a.Position.Z, b.Position.Z, 1e-6f);
                ClassicAssert.AreEqual(a.Yaw, b.Yaw, 1e-6f);
                ClassicAssert.AreEqual(a.Pitch, b.Pitch, 1e-6f);

                ClassicAssert.AreEqual(a.Properties.Count, b.Properties.Count, $"entite {i}");

                foreach (var pair in a.Properties)
                    ClassicAssert.AreEqual(pair.Value, b.Get(pair.Key), $"entite {i}, cle {pair.Key}");
            }
        }

        [Test]
        public void Deux_Enregistrements_Donnent_LE_MEME_TEXTE()
        {
            // ⚠️ LE test de ce fichier. L'ordre de parcours d'un Dictionary
            // n'est PAS garanti, meme sans modification entre deux parcours.
            // Si les proprietes sortaient dans un ordre variable, deux
            // enregistrements donneraient deux fichiers differents, et
            // l'aller-retour octet pour octet de 4.2 tomberait de facon
            // intermittente - le pire cas de figure.
            MapData map = WithEntities();

            string first = MapFile.Save(map);
            string second = MapFile.Save(MapFile.Parse(first));
            string third = MapFile.Save(MapFile.Parse(second));

            ClassicAssert.AreEqual(first, second, "le deuxieme enregistrement differe");
            ClassicAssert.AreEqual(second, third, "le troisieme enregistrement differe");
        }

        [Test]
        public void Les_Proprietes_Sortent_Triees()
        {
            // Le tri n'est pas cosmetique : c'est lui qui rend l'ordre stable.
            var map = new MapData();
            map.Entities.Add(new MapEntity("test", Vector3.Zero)
                .Set("zebre", "3").Set("alpha", "1").Set("mimosa", "2"));

            string text = MapFile.Save(map);

            int a = text.IndexOf("alpha=", StringComparison.Ordinal);
            int m = text.IndexOf("mimosa=", StringComparison.Ordinal);
            int z = text.IndexOf("zebre=", StringComparison.Ordinal);

            Assert.That(a, Is.GreaterThan(0));
            Assert.That(a, Is.LessThan(m));
            Assert.That(m, Is.LessThan(z));
        }

        // ===================================================================
        //  Compatibilite du format
        // ===================================================================

        [Test]
        public void Une_Carte_De_Version_1_Se_Lit_ENCORE()
        {
            // ⚠️ Le passage a la version 2 aurait rendu illisibles les deux
            // cartes du depot. Un moteur doit lire ses anciens fichiers ; c'est
            // l'inverse qui n'a jamais ete promis.
            string old = "exarion_map 1\nname ancienne\nbox 0 0 0  2 2 2  1 1 1\n";

            MapData map = MapFile.Parse(old);

            ClassicAssert.AreEqual("ancienne", map.Name);
            ClassicAssert.AreEqual(1, map.Boxes.Count);
            ClassicAssert.AreEqual(0, map.Entities.Count);
        }

        [Test]
        public void Une_Version_Trop_Recente_Est_Refusee()
        {
            var ex = Assert.Throws<MapFormatException>(
                () => MapFile.Parse($"{MapFile.Magic} {MapFile.Version + 1}\n"));

            Assert.That(ex!.Message, Does.Contain((MapFile.Version + 1).ToString()));
        }

        [Test]
        public void Ce_Moteur_Ecrit_La_Version_Courante()
        {
            string text = MapFile.Save(new MapData());

            Assert.That(text, Does.StartWith($"{MapFile.Magic} {MapFile.Version}"));
            Assert.That(MapFile.Version, Is.GreaterThanOrEqualTo(MapFile.MinimumVersion));
        }

        // ===================================================================
        //  Ecriture a la main
        // ===================================================================

        [Test]
        public void Une_Entite_Ecrite_A_La_Main_Sans_Angles_Se_Lit()
        {
            // Le lacet et le tangage sont optionnels : une carte ecrite a la
            // main doit pouvoir s'arreter apres la position. Ils se distinguent
            // d'une propriete par l'absence de « = ».
            MapData map = MapFile.Parse(
                $"{MapFile.Magic} {MapFile.Version}\n" +
                "entity info_player_spawn 1 2 3\n" +
                "entity npc_dummy 4 5 6  speed=3\n");

            ClassicAssert.AreEqual(2, map.Entities.Count);

            ClassicAssert.AreEqual(0f, map.Entities[0].Yaw, 1e-6f);
            ClassicAssert.AreEqual(3f, map.Entities[0].Position.Z, 1e-6f);

            ClassicAssert.AreEqual(0f, map.Entities[1].Yaw, 1e-6f, "speed= ne doit pas etre lu comme un lacet");
            ClassicAssert.AreEqual(3f, map.Entities[1].GetFloat("speed"), 1e-6f);
        }

        [Test]
        public void Une_Propriete_Sans_Egal_Est_Refusee()
        {
            // Une faute de frappe dans une propriete ne doit PAS etre avalee :
            // un declencheur dont une propriete manque ne se declenche pas, et
            // rien ne le dirait.
            var ex = Assert.Throws<MapFormatException>(() => MapFile.Parse(
                $"{MapFile.Magic} {MapFile.Version}\nentity trigger_volume 0 0 0 0 0 sizeQuatre\n"));

            Assert.That(ex!.Message, Does.Contain("sizeQuatre"));
        }

        [Test]
        public void Une_Valeur_A_Espaces_Ne_Casse_Pas_Le_Fichier()
        {
            // ⚠️ Le format decoupe sur les espaces. Une valeur qui en contient
            // produirait un fichier qu'on ne saurait PAS relire - pas seulement
            // cette propriete, toute la ligne. On remplace donc a l'ecriture.
            var map = new MapData();
            map.Entities.Add(new MapEntity("test", Vector3.Zero)
                .Set("nom", "porte du hall").Set("vide", ""));

            MapData back = MapFile.Parse(MapFile.Save(map));

            ClassicAssert.AreEqual(1, back.Entities.Count);
            ClassicAssert.AreEqual("porte_du_hall", back.Entities[0].Get("nom"));
            Assert.That(back.Entities[0].Get("vide"), Is.Not.Empty, "une valeur vide casserait le decoupage");
        }

        [Test]
        public void Un_Type_Inconnu_Est_CONSERVE()
        {
            // Le format accepte n'importe quel type. Refuser l'inconnu ferait
            // PERDRE l'entite en relisant une carte faite pour une version
            // ulterieure du moteur - le pire des deux mondes.
            MapData map = MapFile.Parse(
                $"{MapFile.Magic} {MapFile.Version}\nentity truc_du_futur 1 1 1 0 0 machin=3\n");

            ClassicAssert.AreEqual(1, map.Entities.Count);
            ClassicAssert.AreEqual("truc_du_futur", map.Entities[0].Type);
            ClassicAssert.AreEqual("3", map.Entities[0].Get("machin"));
        }

        // ===================================================================
        //  Copie et annulation
        // ===================================================================

        [Test]
        public void Une_Copie_De_Carte_NE_PARTAGE_PAS_Ses_Entites()
        {
            // ⚠️ MapEntity est une CLASSE portant un dictionnaire, alors que
            // MapBox et MapLight sont des structures. Un AddRange n'en copierait
            // que les references : l'instantane de l'editeur partagerait ses
            // entites avec la carte vivante, et l'annulation serait juste pour
            // la geometrie et fausse pour les entites.
            MapData source = WithEntities();
            MapData copy = source.Clone();

            copy.Entities[0].Position = new Vector3(999f, 999f, 999f);
            copy.Entities[0].Set("team", "modifiee");

            ClassicAssert.AreEqual(4f, source.Entities[0].Position.X, 1e-6f, "la copie a modifie l'original");
            ClassicAssert.AreEqual("attaque", source.Entities[0].Get("team"));
        }

        [Test]
        public void L_Annulation_Restitue_Aussi_Les_Entites()
        {
            // Le pendant du test precedent, mais par le chemin reel : l'editeur.
            var editor = new EditorState(WithEntities());

            string before = Dump(editor.Map);

            editor.AddEntity(EntityTypes.Waypoint, new Vector3(20f, 0f, 20f));
            editor.SelectEntity(0);
            editor.Move(EditAxis.X, 5);

            Assert.That(Dump(editor.Map), Is.Not.EqualTo(before));

            while (editor.Undo()) { }

            ClassicAssert.AreEqual(before, Dump(editor.Map));
        }

        private static string Dump(MapData map)
        {
            var sb = new System.Text.StringBuilder();

            foreach (MapEntity e in map.Entities)
            {
                sb.Append(e.Type).Append(e.Position).Append(e.Yaw).Append(e.Pitch);

                foreach (var pair in e.Properties)
                    sb.Append(pair.Key).Append('=').Append(pair.Value).Append(';');

                sb.Append('|');
            }

            foreach (MapBox b in map.Boxes)
                sb.Append(b.Center).Append(b.Size).Append(';');

            return sb.ToString();
        }

        // ===================================================================
        //  Points d'apparition
        // ===================================================================

        [Test]
        public void La_Carte_Rend_Ses_Points_D_Apparition()
        {
            MapData map = WithEntities();

            var spawns = new System.Collections.Generic.List<MapEntity>(map.SpawnPoints());

            ClassicAssert.AreEqual(2, spawns.Count, "deux apparitions declarees");
            Assert.That(spawns[0].Get("team"), Is.EqualTo("attaque"));
            Assert.That(spawns[1].Get("team"), Is.EqualTo("defense"));
        }

        [Test]
        public void Une_Carte_Sans_Apparition_N_En_Rend_Aucune()
        {
            var map = new MapData();
            map.Entities.Add(new MapEntity(EntityTypes.Npc, Vector3.Zero));

            ClassicAssert.AreEqual(0, new System.Collections.Generic.List<MapEntity>(map.SpawnPoints()).Count);
        }
    }
}
