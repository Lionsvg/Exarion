// ===========================================================================
//  Tests de MapLibrary : le dossier des cartes et les noms de fichiers.
//
//  Ces fonctions vivaient dans Program.cs, hors de portee de tout test. La
//  plus importante decide de ce qui a le droit de devenir un nom de fichier
//  sur le disque : elle se verifie ici, pas en ecrasant un fichier par erreur.
// ===========================================================================

using System;
using System.IO;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core.Assets;

namespace Exarion.Tests
{
    public class MapLibraryTests
    {
        private string _root = "";

        [SetUp]
        public void CreerDossier()
        {
            _root = Path.Combine(Path.GetTempPath(), "exarion-maplib-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void EffacerDossier()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        // --- Noms de fichiers -------------------------------------------------

        [Test]
        public void Un_Nom_Avec_Des_Espaces_Devient_Un_Nom_De_Fichier()
        {
            ClassicAssert.AreEqual("bac_a_sable", MapLibrary.SafeFileName("bac a sable"));
        }

        /// <summary>
        /// ⚠️ LE CAS QUI JUSTIFIE LA FONCTION. Un nom de carte vient d'un fichier
        /// qu'on a pu recevoir de n'importe qui : il ne doit jamais pouvoir faire
        /// ecrire en dehors du dossier des cartes.
        /// </summary>
        [Test]
        public void Un_Nom_Ne_Peut_Pas_Remonter_Les_Dossiers()
        {
            string safe = MapLibrary.SafeFileName("../../etc");

            ClassicAssert.AreEqual("etc", safe);
            ClassicAssert.IsFalse(safe.Contains('/') || safe.Contains('\\') || safe.Contains(".."));
        }

        [Test]
        public void Un_Nom_Vide_Apres_Nettoyage_Devient_Sans_Nom()
        {
            ClassicAssert.AreEqual("sans_nom", MapLibrary.SafeFileName(""));
            ClassicAssert.AreEqual("sans_nom", MapLibrary.SafeFileName("___"));
            ClassicAssert.AreEqual("sans_nom", MapLibrary.SafeFileName("?*:"));
        }

        [Test]
        public void Tirets_Soulignes_Et_Lettres_Accentuees_Sont_Gardes()
        {
            ClassicAssert.AreEqual("Carte-1_b", MapLibrary.SafeFileName("Carte-1_b"));
            ClassicAssert.AreEqual("ete", MapLibrary.SafeFileName("ete"));
            ClassicAssert.AreEqual("été", MapLibrary.SafeFileName("été"));
        }

        // --- Resolution d'un nom en chemin -------------------------------------

        [Test]
        public void Un_Nom_Nu_Va_Dans_Le_Dossier_Et_Prend_L_Extension()
        {
            ClassicAssert.AreEqual(Path.Combine(_root, "arene" + MapFile.Extension),
                                   MapLibrary.Resolve("arene", _root));
        }

        [Test]
        public void Une_Extension_Deja_Presente_N_Est_Pas_Doublee()
        {
            ClassicAssert.AreEqual(Path.Combine(_root, "arene.map"), MapLibrary.Resolve("arene.map", _root));
        }

        /// <summary>
        /// Un chemin est pris TEL QUEL : c'est ce qui permet d'ouvrir une carte
        /// hors du dossier des cartes. Le recoller au dossier donnerait un chemin
        /// qui n'existe pas.
        /// </summary>
        [Test]
        public void Un_Chemin_Est_Pris_Tel_Quel()
        {
            ClassicAssert.AreEqual("autre/arene.map", MapLibrary.Resolve("autre/arene", _root));
            ClassicAssert.AreEqual("autre/arene.map", MapLibrary.Resolve("autre/arene.map", _root));
        }

        // --- Recherche du dossier ----------------------------------------------

        [Test]
        public void Le_Dossier_Est_Trouve_En_Remontant()
        {
            string maps = Path.Combine(_root, MapLibrary.FolderName);
            string deep = Path.Combine(_root, "bin", "Release", "net9.0");

            Directory.CreateDirectory(maps);
            Directory.CreateDirectory(deep);

            ClassicAssert.AreEqual(Path.GetFullPath(maps), Path.GetFullPath(MapLibrary.FindFolder(deep)));
        }

        /// <summary>
        /// Sans dossier « maps » nulle part au-dessus, on rend celui qu'on creerait
        /// a cote du point de depart - jamais null. Le test pose sa racine dans le
        /// dossier temporaire : si un « maps » existait par hasard au-dessus, le
        /// resultat ne serait pas sous la racine, et on le saurait.
        /// </summary>
        [Test]
        public void Sans_Dossier_On_Rend_Celui_Qu_On_Creerait()
        {
            string found = MapLibrary.FindFolder(_root);

            if (!found.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
                Assert.Ignore("Un dossier « maps » existe au-dessus du dossier temporaire : test sans objet ici.");

            ClassicAssert.AreEqual(Path.Combine(_root, MapLibrary.FolderName), found);
        }

        // --- Liste des cartes --------------------------------------------------

        /// <summary>
        /// Tri sans casse, extension retiree, autres fichiers ignores. Le tri
        /// donne le meme menu sur toutes les machines.
        /// </summary>
        [Test]
        public void La_Liste_Est_Triee_Et_Ne_Garde_Que_Les_Cartes()
        {
            File.WriteAllText(Path.Combine(_root, "b" + MapFile.Extension), "");
            File.WriteAllText(Path.Combine(_root, "A" + MapFile.Extension), "");
            File.WriteAllText(Path.Combine(_root, "c.txt"), "");

            Assert.That(MapLibrary.ListNames(_root), Is.EqualTo(new[] { "A", "b" }));
        }

        [Test]
        public void Un_Dossier_Absent_Donne_Une_Liste_Vide()
        {
            Assert.That(MapLibrary.ListNames(Path.Combine(_root, "nulle-part")), Is.Empty);
        }
    }
}
