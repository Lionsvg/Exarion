// ===========================================================================
//  Tests des FORMES de decor (17/08).
//
//  Le format gagne un champ. C'est l'operation la plus risquee qu'on puisse
//  faire sur un format de fichier : elle peut casser toutes les cartes deja
//  ecrites, en silence, et on ne s'en apercoit qu'en rouvrant une carte
//  vieille de trois semaines.
//
//  Ces tests couvrent donc trois choses, dans cet ordre d'importance :
//
//  1. LES CARTES SANS FORME NE CHANGENT PAS D'UN OCTET. C'est la propriete
//     qui protege l'aller-retour de 4.2, et elle est fragile : il suffit
//     d'ecrire « bloc » a la fin de chaque ligne « box » pour la perdre.
//
//  2. UNE CARTE DE VERSION 1 OU 2 SE RELIT, et ses boites sont des blocs.
//     C'est la compatibilite arriere promise par MinimumVersion.
//
//  3. UN NOM DE FORME INCONNU EST FATAL, avec sa ligne. Meme contrat que le
//     reste du fichier : une rampe muee en cube boucherait un passage, et on
//     chercherait le defaut dans la geometrie.
// ===========================================================================

using System.Numerics;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core.Assets;

namespace Exarion.Tests
{
    public class MapShapeTests
    {
        private static MapData WithShapes()
        {
            var map = new MapData { Name = "formes" };

            map.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), new Vector3(0.34f, 0.35f, 0.38f));
            map.AddBox(new Vector3(2f, 1f, 0f), new Vector3(2f), new Vector3(0.8f, 0.3f, 0.3f), MapShape.Sphere);
            map.AddBox(new Vector3(6f, 1.5f, 0f), new Vector3(1.5f, 3f, 1.5f), new Vector3(0.3f, 0.8f, 0.4f), MapShape.Cylinder);
            map.AddBox(new Vector3(10f, 0.9f, 0f), new Vector3(4f, 1.8f, 3f), new Vector3(0.7f, 0.6f, 0.2f), MapShape.Wedge);
            map.AddBox(new Vector3(14f, 1.2f, 0f), new Vector3(2f, 2.4f, 2f), new Vector3(0.4f, 0.5f, 0.9f), MapShape.Cone);

            return map;
        }

        // ===================================================================

        [Test]
        public void L_Aller_Retour_Conserve_Chaque_Forme()
        {
            MapData original = WithShapes();
            MapData relu = MapFile.Parse(MapFile.Save(original));

            ClassicAssert.AreEqual(original.Boxes.Count, relu.Boxes.Count);

            for (int i = 0; i < original.Boxes.Count; i++)
            {
                ClassicAssert.AreEqual(original.Boxes[i].Shape, relu.Boxes[i].Shape,
                    $"la forme de la boite {i} n'a pas survecu a l'aller-retour");

                // La forme ne doit pas non plus avoir mange la couleur : les
                // deux se suivent sur la ligne, un decalage d'un champ
                // donnerait des couleurs plausibles et fausses.
                ClassicAssert.IsTrue(original.Boxes[i].Equals(relu.Boxes[i]),
                    $"la boite {i} a change autrement que par sa forme");
            }
        }

        /// <summary>
        /// ⚠️ LE TEST QUI PROTEGE LA PREUVE DE 4.2.
        ///
        /// Verifie par mutation : en ecrivant systematiquement le nom de la
        /// forme - « bloc » compris - ce test tombe, et lui seul. Les cartes
        /// du depot changeraient alors d'octets a chaque enregistrement sans
        /// qu'aucune autre verification ne bronche.
        /// </summary>
        [Test]
        public void Une_Carte_SANS_Forme_S_Ecrit_Exactement_Comme_Avant()
        {
            var map = new MapData { Name = "que des blocs" };

            map.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), new Vector3(0.34f, 0.35f, 0.38f));
            map.AddBox(new Vector3(3f, 1f, -2f), new Vector3(2f, 2f, 2f), new Vector3(0.5f, 0.5f, 0.5f));

            string text = MapFile.Save(map);

            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();

                // On saute les commentaires : l'en-tete DECRIT le champ
                // optionnel, il a donc le droit de nommer les formes.
                if (!trimmed.StartsWith("box ", System.StringComparison.Ordinal))
                    continue;

                string[] fields = trimmed.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);

                ClassicAssert.AreEqual(10, fields.Length,
                    "une ligne box de blocs doit avoir exactement 10 champs, sans nom de forme : " + trimmed);
            }
        }

        [Test]
        public void Une_Carte_De_Version_1_Se_Relit_Et_Ses_Boites_Sont_Des_Blocs()
        {
            // Ecrite a la main, exactement comme les cartes du depot.
            string ancienne =
                "exarion_map 1\n" +
                "name ancienne\n" +
                "spawn 0 1 12 0 0\n" +
                "box 0 -0.5 0  60 1 60  0.34 0.35 0.38\n";

            MapData map = MapFile.Parse(ancienne);

            ClassicAssert.AreEqual(1, map.Boxes.Count);
            ClassicAssert.AreEqual(MapShape.Block, map.Boxes[0].Shape,
                "l'absence de forme doit valoir « bloc », sinon toutes les cartes anterieures changent d'aspect");
        }

        [Test]
        public void Une_Forme_Se_Lit_Aussi_Sous_Son_Nom_Anglais()
        {
            // Le format est fait pour etre ecrit a la main : quelqu'un venant
            // de Roblox tapera « sphere » et « wedge ».
            string text =
                "exarion_map 3\n" +
                "box 0 1 0  2 2 2  0.5 0.5 0.5  wedge\n";

            MapData map = MapFile.Parse(text);

            ClassicAssert.AreEqual(MapShape.Wedge, map.Boxes[0].Shape);
        }

        [Test]
        public void Une_Forme_INCONNUE_Est_Fatale_Et_Porte_Sa_Ligne()
        {
            string text =
                "exarion_map 3\n" +
                "box 0 -0.5 0  60 1 60  0.34 0.35 0.38\n" +
                "box 0 1 0  2 2 2  0.5 0.5 0.5  pyramide\n";

            var error = Assert.Throws<MapFormatException>(() => MapFile.Parse(text));

            ClassicAssert.AreEqual(3, error!.Line,
                "l'erreur doit designer la ligne fautive, pas le fichier en bloc");

            StringAssert.Contains("pyramide", error.Message);
        }

        /// <summary>
        /// ⚠️ Le champ de forme ne doit pas relacher le controle du NOMBRE de
        /// champs. Passer le maximum de 10 a 11 rend acceptable une ligne a
        /// laquelle il manque une composante de couleur, suivie d'un nombre
        /// pris pour une forme - donc une erreur muee en couleur fausse.
        /// </summary>
        [Test]
        public void Une_Ligne_Box_A_Douze_Champs_Reste_Refusee()
        {
            string text =
                "exarion_map 3\n" +
                "box 0 1 0  2 2 2  0.5 0.5 0.5  sphere  bloc\n";

            Assert.Throws<MapFormatException>(() => MapFile.Parse(text));
        }

        [Test]
        public void Un_Nombre_A_La_Place_D_Une_Forme_Est_Refuse()
        {
            string text =
                "exarion_map 3\n" +
                "box 0 1 0  2 2 2  0.5 0.5  0.5\n" +   // 10 champs : couleur incomplete decalee
                "box 0 1 0  2 2 2  0.5 0.5 0.5  0.9\n";

            var error = Assert.Throws<MapFormatException>(() => MapFile.Parse(text));

            ClassicAssert.AreEqual(3, error!.Line);
        }

        [Test]
        public void Le_Clonage_Conserve_La_Forme()
        {
            // L'editeur construit son historique d'annulation par instantanes :
            // une forme perdue au clonage rendrait l'annulation destructrice.
            MapData copy = WithShapes().Clone();

            ClassicAssert.AreEqual(MapShape.Sphere, copy.Boxes[1].Shape);
            ClassicAssert.AreEqual(MapShape.Wedge, copy.Boxes[3].Shape);
        }

        [Test]
        public void Deux_Boites_Ne_Different_Que_Par_Leur_Forme_Et_Ne_Sont_Pas_Egales()
        {
            // Equals sert a comparer deux cartes dans les tests. S'il ignore la
            // forme, un test de non-regression sur la geometrie passerait alors
            // que tout le decor a change d'aspect.
            var bloc = new MapBox(Vector3.Zero, Vector3.One, Vector3.One, MapShape.Block);
            var boule = new MapBox(Vector3.Zero, Vector3.One, Vector3.One, MapShape.Sphere);

            ClassicAssert.IsFalse(bloc.Equals(boule));
        }

        [Test]
        public void Le_Constructeur_A_Trois_Arguments_Pose_Un_Bloc()
        {
            // Tout le code anterieur passe par cette surcharge : si elle posait
            // autre chose, le bac a sable changerait d'aspect sans qu'une seule
            // ligne de son code ait bouge.
            var box = new MapBox(Vector3.Zero, Vector3.One, Vector3.One);

            ClassicAssert.AreEqual(MapShape.Block, box.Shape);
        }
    }
}
