// ===========================================================================
//  Tests de la sauvegarde des reglages (2.4).
//
//  Le piege central de ce fichier n'a rien a voir avec la logique : c'est
//  la CULTURE. Cette machine est en francais, ou le separateur decimal est
//  la virgule. Ecrire 15.24 sans preciser la culture invariante produit
//  "15,24", qui se relit comme 1524 - ou pas du tout.
//
//  Le defaut ne se verrait pas sur un poste anglais, et casserait chez
//  quelqu'un d'autre. C'est exactement le genre de bug qu'un test doit
//  attraper, parce qu'aucune relecture ne le voit.
// ===========================================================================

using System.Globalization;
using System.Threading;
using NUnit.Framework;
using Exarion.Core.Console;

namespace Exarion.Tests
{
    public class ConsoleConfigTests
    {
        private static ConsoleSystem Build()
        {
            var console = new ConsoleSystem();

            console.Register(new ConVar("gravity", 15.24f, 0f, 100f, "pesanteur"));
            console.Register(new ConVar("fov", 90f, 60f, 120f, "champ de vision"));
            console.Register(new ConVar("ssao_radius", 0.8f, 0.02f, 4f, "rayon"));

            return console;
        }

        [Test]
        public void Sans_Changement_Rien_N_Est_Ecrit()
        {
            string text = ConsoleConfig.Save(Build());

            // Seulement des commentaires : aucune ligne de reglage.
            foreach (string line in text.Split('\n'))
                Assert.That(line.Trim(), Is.Empty.Or.StartWith("#"));
        }

        [Test]
        public void Seules_Les_Valeurs_Modifiees_Sont_Ecrites()
        {
            // Ecrire les soixante variables figerait les valeurs par defaut :
            // une amelioration du moteur n'atteindrait jamais quelqu'un qui a
            // deja un fichier.
            ConsoleSystem console = Build();
            console.Find("fov")!.Value = 103f;

            string text = ConsoleConfig.Save(console);

            Assert.That(text, Does.Contain("fov 103"));
            Assert.That(text, Does.Not.Contain("gravity"));
            Assert.That(text, Does.Not.Contain("ssao_radius"));
        }

        [Test]
        public void Un_Aller_Retour_Restitue_Les_Valeurs()
        {
            ConsoleSystem source = Build();
            source.Find("gravity")!.Value = 9.81f;
            source.Find("ssao_radius")!.Value = 1.25f;

            string text = ConsoleConfig.Save(source);

            ConsoleSystem target = Build();
            int applied = ConsoleConfig.Apply(target, text);

            ClassicAssert.AreEqual(2, applied);
            ClassicAssert.AreEqual(9.81f, target.Find("gravity")!.Value, 1e-4f);
            ClassicAssert.AreEqual(1.25f, target.Find("ssao_radius")!.Value, 1e-4f);
            ClassicAssert.AreEqual(90f, target.Find("fov")!.Value, 1e-4f, "non touchee, donc au defaut");
        }

        [Test]
        public void Une_Culture_A_Virgule_Ne_Casse_Rien()
        {
            // LE test de ce fichier. Sans culture invariante, la sauvegarde
            // ecrirait "9,81" et la relecture rendrait 981 - ou echouerait.
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");

                ConsoleSystem source = Build();
                source.Find("gravity")!.Value = 9.81f;

                string text = ConsoleConfig.Save(source);

                Assert.That(text, Does.Contain("9.81"), "le point decimal doit survivre a la locale");
                Assert.That(text, Does.Not.Contain("9,81"));

                ConsoleSystem target = Build();
                ConsoleConfig.Apply(target, text);

                ClassicAssert.AreEqual(9.81f, target.Find("gravity")!.Value, 1e-4f);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void Une_Variable_Inconnue_Est_Ignoree_Sans_Broncher()
        {
            // Un fichier ecrit par une version anterieure doit continuer a
            // marcher, meme si une variable a disparu depuis.
            ConsoleSystem console = Build();

            int applied = ConsoleConfig.Apply(console, "fov 100\nvariable_disparue 42\ngravity 12");

            ClassicAssert.AreEqual(2, applied);
            ClassicAssert.AreEqual(100f, console.Find("fov")!.Value, 1e-4f);
        }

        [Test]
        public void Les_Lignes_Mal_Formees_Sont_Ignorees()
        {
            ConsoleSystem console = Build();

            int applied = ConsoleConfig.Apply(console,
                "# un commentaire\n\n   \nfov\nfov pas_un_nombre\nfov 95\n");

            ClassicAssert.AreEqual(1, applied);
            ClassicAssert.AreEqual(95f, console.Find("fov")!.Value, 1e-4f);
        }

        [Test]
        public void Un_Fichier_Bricole_Ne_Sort_Pas_Des_Bornes()
        {
            // Quelqu'un editera ce fichier a la main, et y mettra n'importe
            // quoi. ConVar borne, mais autant le verifier ici.
            ConsoleSystem console = Build();

            ConsoleConfig.Apply(console, "fov 9999\nssao_radius -50");

            ClassicAssert.AreEqual(120f, console.Find("fov")!.Value, 1e-4f);
            ClassicAssert.AreEqual(0.02f, console.Find("ssao_radius")!.Value, 1e-4f);
        }
    }
}
