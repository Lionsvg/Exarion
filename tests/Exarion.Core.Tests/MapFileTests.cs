// ===========================================================================
//  Tests du format de carte (4.2).
//
//  Trois choses sont verifiees ici, et elles ne se valent pas :
//
//  1. L'ALLER-RETOUR EST EXACT. C'est la propriete qui fait qu'un format
//     est un format. Un arrondi a l'ecriture deplacerait les murs a chaque
//     enregistrement, d'une quantite trop petite pour se voir et assez
//     grande pour s'accumuler.
//
//  2. LA CULTURE. Deja paye dans ce projet sur ConsoleConfig, et reel sur
//     cette machine : sans culture invariante, 9.81 s'ecrit "9,81" et se
//     relit comme 981. Invisible sur un poste anglais.
//
//  3. UNE CARTE FAUSSE EST REFUSEE, AVEC SA LIGNE. Contrat inverse de celui
//     des reglages de console : la ligne qu'on ne sait pas lire est un MUR,
//     et l'ignorer se manifesterait comme un joueur qui traverse le decor.
// ===========================================================================

using System;
using System.Globalization;
using System.Numerics;
using System.Threading;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core.Assets;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class MapFileTests
    {
        private static MapData Sample()
        {
            var map = new MapData { Name = "arene de test", Spawn = new Vector3(0f, 0f, 25f), SpawnYaw = 20f, SpawnPitch = -5f };

            map.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), new Vector3(0.34f, 0.35f, 0.38f));
            map.AddBox(new Vector3(-14f, 2.2500002f, -14.4f), new Vector3(6f, 2.4f, 1.2f), new Vector3(0.55f, 0.48f, 0.38f));

            map.Lights.Add(MapLight.Directional(new Vector3(-0.3f, -1f, -0.2f), new Vector3(0.62f, 0.7f, 0.85f), 1.4f));
            map.Lights.Add(MapLight.Point(new Vector3(18f, 6.5f, -18f), new Vector3(1f, 0.78f, 0.48f), 2f, 26f));
            map.Lights.Add(MapLight.Spot(new Vector3(0f, 7.5f, -11f), new Vector3(0f, -1f, 0f),
                                         new Vector3(1f, 0.42f, 0.35f), 4.4f, 18f, 22f, 34f));

            return map;
        }

        private static void AssertSame(MapData expected, MapData actual)
        {
            ClassicAssert.AreEqual(expected.Name, actual.Name);
            ClassicAssert.AreEqual(expected.Spawn, actual.Spawn);
            ClassicAssert.AreEqual(expected.SpawnYaw, actual.SpawnYaw);
            ClassicAssert.AreEqual(expected.SpawnPitch, actual.SpawnPitch);

            ClassicAssert.AreEqual(expected.Boxes.Count, actual.Boxes.Count);
            for (int i = 0; i < expected.Boxes.Count; i++)
                ClassicAssert.AreEqual(expected.Boxes[i], actual.Boxes[i], $"boite {i}");

            ClassicAssert.AreEqual(expected.Lights.Count, actual.Lights.Count);
            for (int i = 0; i < expected.Lights.Count; i++)
                ClassicAssert.AreEqual(expected.Lights[i], actual.Lights[i], $"lumiere {i}");
        }

        // ------------------------------------------------------------------

        [Test]
        public void L_Aller_Retour_Rend_Exactement_La_Meme_Carte()
        {
            MapData source = Sample();

            AssertSame(source, MapFile.Parse(MapFile.Save(source)));
        }

        /// <summary>
        /// Le vrai garde-fou contre un arrondi a l'ecriture : une valeur qui
        /// ne tombe PAS juste. 2,2500002 vient d'un vrai calcul de marche
        /// (0,15 + i × 0,3) et devient 2,25 des qu'on formate a deux
        /// decimales - un deplacement de 200 nanometres par enregistrement,
        /// invisible et cumulatif.
        /// </summary>
        [Test]
        public void Une_Valeur_Qui_Ne_Tombe_Pas_Juste_Survit_A_L_Aller_Retour()
        {
            var map = new MapData();
            map.AddBox(new Vector3(0f, 2.2500002f, 0f), new Vector3(1f, 1f, 1f), new Vector3(0.1f, 0.2f, 0.3f));

            MapData back = MapFile.Parse(MapFile.Save(map));

            ClassicAssert.AreEqual(2.2500002f, back.Boxes[0].Center.Y);
            Assert.That(back.Boxes[0].Center.Y, Is.Not.EqualTo(2.25f));
        }

        /// <summary>
        /// LE test de culture. On force la machine en francais - separateur
        /// decimal virgule - et on verifie que le fichier reste lisible.
        /// Sans CultureInfo.InvariantCulture dans MapFile, ce test tombe.
        /// </summary>
        [Test]
        public void Le_Fichier_Se_Relit_En_Culture_Francaise()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");

                MapData source = Sample();
                string text = MapFile.Save(source);

                // La preuve directe : aucune virgule decimale dans le fichier.
                Assert.That(text, Does.Not.Contain("0,3"));

                AssertSame(source, MapFile.Parse(text));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        /// <summary>
        /// Un fichier ecrit en culture francaise ne doit pas se relire "un
        /// peu de travers" : il doit etre REFUSE. "9,81" decoupe en deux
        /// champs decale tout le reste de la ligne.
        /// </summary>
        [Test]
        public void Un_Nombre_A_Virgule_Est_Refuse()
        {
            string text = $"{MapFile.Magic} {MapFile.Version}\nbox 0 -0,5 0  60 1 60  0.3 0.3 0.3\n";

            Assert.Throws<MapFormatException>(() => MapFile.Parse(text));
        }

        // ------------------------------------------------------------------
        //  Refus, et numero de ligne
        // ------------------------------------------------------------------

        [Test]
        public void Un_Fichier_Sans_En_Tete_Est_Refuse()
        {
            Assert.Throws<MapFormatException>(() => MapFile.Parse("box 0 0 0  1 1 1  1 1 1\n"));
            Assert.Throws<MapFormatException>(() => MapFile.Parse(""));
            Assert.Throws<MapFormatException>(() => MapFile.Parse("# juste un commentaire\n"));
        }

        [Test]
        public void Une_Version_Inconnue_Est_Refusee()
        {
            var ex = Assert.Throws<MapFormatException>(() => MapFile.Parse($"{MapFile.Magic} 99\n"));
            Assert.That(ex!.Message, Does.Contain("99"));
        }

        [Test]
        public void L_Erreur_Porte_Le_Numero_De_La_Ligne_Fautive()
        {
            string text = $"{MapFile.Magic} {MapFile.Version}\n" +
                          "\n" +
                          "# un commentaire\n" +
                          "box 0 0 0  1 1 1  1 1 1\n" +
                          "box 0 0 0  1 1 1  1 1\n";      // ligne 5 : un champ manque

            var ex = Assert.Throws<MapFormatException>(() => MapFile.Parse(text));

            ClassicAssert.AreEqual(5, ex!.Line);
        }

        [Test]
        public void Un_Mot_Cle_Inconnu_Est_Refuse()
        {
            string text = $"{MapFile.Magic} {MapFile.Version}\nsphere 0 0 0 1\n";

            var ex = Assert.Throws<MapFormatException>(() => MapFile.Parse(text));
            Assert.That(ex!.Message, Does.Contain("sphere"));
        }

        /// <summary>
        /// Une taille nulle donne une AABB retournee (Min au-dessus de Max) :
        /// le balayage la traite comme un obstacle solide d'un cote et
        /// traversable de l'autre. C'est introuvable a l'oeil, donc c'est au
        /// chargement que ca doit tomber.
        /// </summary>
        [Test]
        public void Une_Boite_De_Taille_Nulle_Ou_Negative_Est_Refusee()
        {
            string zero = $"{MapFile.Magic} {MapFile.Version}\nbox 0 0 0  1 0 1  1 1 1\n";
            string negative = $"{MapFile.Magic} {MapFile.Version}\nbox 0 0 0  1 1 -2  1 1 1\n";

            Assert.Throws<MapFormatException>(() => MapFile.Parse(zero));
            Assert.Throws<MapFormatException>(() => MapFile.Parse(negative));
        }

        [Test]
        public void Un_Nombre_Invalide_Est_Refuse()
        {
            foreach (string bad in new[] { "abc", "NaN", "Infinity" })
            {
                string text = $"{MapFile.Magic} {MapFile.Version}\nbox 0 {bad} 0  1 1 1  1 1 1\n";
                Assert.Throws<MapFormatException>(() => MapFile.Parse(text), $"\"{bad}\" aurait du etre refuse");
            }
        }

        // ------------------------------------------------------------------
        //  Tolerance de mise en forme
        // ------------------------------------------------------------------

        [Test]
        public void Commentaires_Lignes_Vides_Et_Espaces_Sont_Tolerés()
        {
            string text = $"  {MapFile.Magic} {MapFile.Version}   \n" +
                          "\n" +
                          "   # un commentaire indente\n" +
                          "name  arene   \n" +
                          "\tbox   0 -0.5 0\t60 1 60   0.3 0.3 0.3   # le sol\n" +
                          "\n";

            MapData map = MapFile.Parse(text);

            ClassicAssert.AreEqual("arene", map.Name);
            ClassicAssert.AreEqual(1, map.Boxes.Count);
            ClassicAssert.AreEqual(new Vector3(60f, 1f, 60f), map.Boxes[0].Size);
        }

        [Test]
        public void Les_Fins_De_Ligne_Windows_Sont_Acceptees()
        {
            string text = $"{MapFile.Magic} {MapFile.Version}\r\nbox 0 0 0  2 2 2  1 1 1\r\n";

            ClassicAssert.AreEqual(1, MapFile.Parse(text).Boxes.Count);
        }

        [Test]
        public void Le_Nom_Peut_Contenir_Des_Espaces()
        {
            MapData map = MapFile.Parse($"{MapFile.Magic} {MapFile.Version}\nname le grand hall\n");

            ClassicAssert.AreEqual("le grand hall", map.Name);
        }

        [Test]
        public void Le_Lacet_Et_Le_Tangage_Du_Spawn_Sont_Facultatifs()
        {
            MapData map = MapFile.Parse($"{MapFile.Magic} {MapFile.Version}\nspawn 1 2 3\n");

            ClassicAssert.AreEqual(new Vector3(1f, 2f, 3f), map.Spawn);
            ClassicAssert.AreEqual(0f, map.SpawnYaw);
        }

        // ------------------------------------------------------------------
        //  Vers le monde de collision
        // ------------------------------------------------------------------

        [Test]
        public void La_Carte_Construit_Un_Monde_De_Collision_Coherent()
        {
            MapData map = Sample();
            var colors = new System.Collections.Generic.List<Vector3>();

            CollisionWorld world = map.ToCollisionWorld(colors);

            // Les deux listes DOIVENT rester paralleles : le maillage lit la
            // couleur d'indice i pour la boite d'indice i.
            ClassicAssert.AreEqual(map.Boxes.Count, world.Boxes.Count);
            ClassicAssert.AreEqual(map.Boxes.Count, colors.Count);

            for (int i = 0; i < map.Boxes.Count; i++)
            {
                ClassicAssert.AreEqual(map.Boxes[i].Color, colors[i]);

                // Aabb.FromCenterSize passe par des demi-dimensions : on
                // compare a la tolerance du flottant, pas au bit pres.
                Aabb box = world.Boxes[i];
                Assert.That((box.Center - map.Boxes[i].Center).Length(), Is.LessThan(1e-4f));
                Assert.That((box.Size - map.Boxes[i].Size).Length(), Is.LessThan(1e-4f));
            }
        }

        /// <summary>
        /// Garde-fou du meme genre que celui de M4 sur le squelette : tous
        /// les tests ci-dessus passeraient sur une carte VIDE. Celui-ci
        /// exige que l'echantillon decrive vraiment quelque chose.
        /// </summary>
        [Test]
        public void L_Echantillon_Decrit_Vraiment_Une_Carte()
        {
            MapData map = Sample();

            Assert.That(map.Boxes.Count, Is.GreaterThan(0));
            Assert.That(map.Lights.Count, Is.GreaterThan(0));
            Assert.That(MapFile.Save(map).Split('\n').Length, Is.GreaterThan(8));
        }
    }
}
