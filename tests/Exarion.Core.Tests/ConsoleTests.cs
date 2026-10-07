// ===========================================================================
//  Tests de la console.
//
//  Une console de debug qui se trompe est pire que pas de console : on
//  regle une valeur, on croit l'avoir changee, et on cherche le probleme
//  ailleurs pendant une heure.
// ===========================================================================

using System.Collections.Generic;
using NUnit.Framework;
using Exarion.Core.Console;

namespace Exarion.Tests
{
    public class ConVarTests
    {
        [Test]
        public void La_Valeur_Est_Bornee_Des_La_Construction()
        {
            var v = new ConVar("test", defaultValue: 100f, min: 0f, max: 10f);

            ClassicAssert.AreEqual(10f, v.Value, 1e-5f);
        }

        [Test]
        public void La_Valeur_Reste_Dans_Les_Bornes()
        {
            var v = new ConVar("test", 5f, 0f, 10f);

            v.Value = 999f;
            ClassicAssert.AreEqual(10f, v.Value, 1e-5f);

            v.Value = -999f;
            ClassicAssert.AreEqual(0f, v.Value, 1e-5f);
        }

        [Test]
        public void Le_Changement_Est_Notifie()
        {
            var v = new ConVar("test", 5f, 0f, 10f);
            int calls = 0;
            v.Changed += _ => calls++;

            v.Value = 7f;
            ClassicAssert.AreEqual(1, calls);

            // Rassigner la meme valeur ne doit rien declencher.
            v.Value = 7f;
            ClassicAssert.AreEqual(1, calls, "pas de notification inutile");
        }

        [Test]
        public void Reset_Revient_Au_Defaut()
        {
            var v = new ConVar("test", 5f, 0f, 10f);
            v.Value = 9f;

            v.Reset();

            ClassicAssert.AreEqual(5f, v.Value, 1e-5f);
        }

        [Test]
        public void Les_Accesseurs_Bool_Et_Int_Sont_Coherents()
        {
            var v = new ConVar("test", 0f, 0f, 10f);

            ClassicAssert.IsFalse(v.BoolValue);

            v.Value = 1f;
            ClassicAssert.IsTrue(v.BoolValue);
            ClassicAssert.AreEqual(1, v.IntValue);

            v.Value = 2.6f;
            ClassicAssert.AreEqual(3, v.IntValue, "l'entier est arrondi");
        }
    }

    public class ConsoleSystemTests
    {
        private ConsoleSystem _console = null!;

        [SetUp]
        public void SetUp()
        {
            _console = new ConsoleSystem();
            _console.Register("gravity", 15.24f, 0f, 100f, "Acceleration de la pesanteur");
            _console.Register("sensitivity", 0.0022f, 0.0001f, 0.05f, "Sensibilite souris");
            _console.Register("showfps", 0f, 0f, 1f, "Afficher le compteur d'images");
        }

        private string LastOutput()
        {
            var o = _console.Output;
            return o.Count > 0 ? o[^1] : "";
        }

        // --- Lecture et ecriture ---------------------------------------------

        [Test]
        public void Taper_Un_Nom_Affiche_Sa_Valeur()
        {
            _console.Execute("gravity");

            StringAssert.Contains("gravity", LastOutput());
            StringAssert.Contains("15.24", LastOutput());
        }

        [Test]
        public void Taper_Un_Nom_Et_Une_Valeur_La_Change()
        {
            _console.Execute("gravity 20");

            ClassicAssert.AreEqual(20f, _console.Find("gravity")!.Value, 1e-4f);
        }

        [Test]
        public void Une_Valeur_Hors_Bornes_Est_Ramenee_Et_Signalee()
        {
            _console.Execute("gravity 5000");

            ClassicAssert.AreEqual(100f, _console.Find("gravity")!.Value, 1e-4f);
            StringAssert.Contains("borne", LastOutput());
        }

        [Test]
        public void Une_Valeur_Qui_Nest_Pas_Un_Nombre_Est_Refusee()
        {
            _console.Execute("gravity beaucoup");

            ClassicAssert.AreEqual(15.24f, _console.Find("gravity")!.Value, 1e-4f, "la valeur ne doit pas changer");
            StringAssert.Contains("nombre", LastOutput());
        }

        [Test]
        public void Les_Decimales_Marchent_Avec_Un_Point()
        {
            _console.Execute("sensitivity 0.005");

            ClassicAssert.AreEqual(0.005f, _console.Find("sensitivity")!.Value, 1e-6f);
        }

        [Test]
        public void Un_Nom_Inconnu_Est_Signale_Sans_Planter()
        {
            _console.Execute("nawak 5");

            StringAssert.Contains("inconnu", LastOutput());
        }

        [Test]
        public void La_Casse_Est_Ignoree()
        {
            _console.Execute("GRAVITY 30");

            ClassicAssert.AreEqual(30f, _console.Find("Gravity")!.Value, 1e-4f);
        }

        // --- Commandes -----------------------------------------------------------

        [Test]
        public void Une_Commande_Est_Declenchee_Avec_Ses_Arguments()
        {
            string[]? received = null;
            _console.Register("spawn", args => received = args, "Fait apparaitre des caisses");

            _console.Execute("spawn 5 grosses");

            ClassicAssert.IsNotNull(received);
            ClassicAssert.AreEqual(2, received!.Length);
            ClassicAssert.AreEqual("5", received[0]);
            ClassicAssert.AreEqual("grosses", received[1]);
        }

        [Test]
        public void Une_Commande_Sans_Argument_Recoit_Un_Tableau_Vide()
        {
            string[]? received = null;
            _console.Register("clearcrates", args => received = args);

            _console.Execute("clearcrates");

            ClassicAssert.IsNotNull(received);
            ClassicAssert.AreEqual(0, received!.Length);
        }

        [Test]
        public void Une_Variable_Dont_Le_Gestionnaire_Plante_Ne_Fait_Pas_Tomber_Le_Jeu()
        {
            // Cas reel : 'shadow_res 8192' demande une texture de 256 Mo. Si
            // l'allocation echoue, le gestionnaire leve. Sans protection,
            // l'exception remonte jusqu'au gestionnaire clavier et tue le jeu.
            var v = _console.Register("shadow_res", 2048f, 256f, 8192f, "Resolution");
            v.Changed += cv =>
            {
                if (cv.Value > 4096f)
                    throw new OutOfMemoryException("texture trop grande");
            };

            _console.Execute("shadow_res 8192");

            StringAssert.Contains("refuse", LastOutput());
            ClassicAssert.AreEqual(2048f, _console.Find("shadow_res")!.Value, 1f,
                "la valeur doit revenir a l'etat precedent");
        }

        [Test]
        public void Un_Reset_Global_Continue_Malgre_Une_Variable_Recalcitrante()
        {
            var bad = _console.Register("bad", 1f, 0f, 10f);
            bad.Changed += _ => throw new InvalidOperationException("non");

            _console.Execute("gravity 50");
            _console.Execute("reset");

            ClassicAssert.AreEqual(15.24f, _console.Find("gravity")!.Value, 1e-3f,
                "les autres variables doivent quand meme etre reinitialisees");
        }

        [Test]
        public void Une_Commande_Qui_Plante_Ne_Fait_Pas_Tomber_La_Console()
        {
            _console.Register("boom", _ => throw new System.InvalidOperationException("casse"));

            _console.Execute("boom");

            StringAssert.Contains("erreur", LastOutput());
            StringAssert.Contains("casse", LastOutput());
        }

        // --- Commandes integrees ---------------------------------------------------

        [Test]
        public void Reset_Remet_Une_Variable_Par_Defaut()
        {
            _console.Execute("gravity 50");
            _console.Execute("reset gravity");

            ClassicAssert.AreEqual(15.24f, _console.Find("gravity")!.Value, 1e-4f);
        }

        [Test]
        public void Reset_Sans_Argument_Remet_Tout()
        {
            _console.Execute("gravity 50");
            _console.Execute("showfps 1");

            _console.Execute("reset");

            ClassicAssert.AreEqual(15.24f, _console.Find("gravity")!.Value, 1e-4f);
            ClassicAssert.AreEqual(0f, _console.Find("showfps")!.Value, 1e-4f);
        }

        [Test]
        public void List_Affiche_Les_Variables()
        {
            _console.Execute("list");

            var joined = string.Join("\n", _console.Output);
            StringAssert.Contains("gravity", joined);
            StringAssert.Contains("sensitivity", joined);
            StringAssert.Contains("showfps", joined);
        }

        [Test]
        public void List_Accepte_Un_Filtre()
        {
            _console.Execute("list sens");

            var joined = string.Join("\n", _console.Output);
            StringAssert.Contains("sensitivity", joined);
            StringAssert.DoesNotContain("gravity =", joined);
        }

        [Test]
        public void Help_Donne_La_Description()
        {
            _console.Execute("help gravity");

            var joined = string.Join("\n", _console.Output);
            StringAssert.Contains("pesanteur", joined);
        }

        [Test]
        public void Clear_Vide_Laffichage()
        {
            _console.Execute("gravity");
            ClassicAssert.Greater(_console.Output.Count, 0);

            _console.Execute("clear");

            ClassicAssert.AreEqual(0, _console.Output.Count);
        }

        // --- Confort ---------------------------------------------------------------

        [Test]
        public void Lhistorique_Retient_Les_Commandes()
        {
            _console.Execute("gravity 20");
            _console.Execute("list");

            ClassicAssert.AreEqual(2, _console.History.Count);
            ClassicAssert.AreEqual("gravity 20", _console.History[0]);
        }

        [Test]
        public void Une_Ligne_Vide_Est_Ignoree()
        {
            _console.Execute("   ");

            ClassicAssert.AreEqual(0, _console.History.Count);
            ClassicAssert.AreEqual(0, _console.Output.Count);
        }

        [Test]
        public void La_Completion_Trouve_Le_Nom_Unique()
        {
            ClassicAssert.AreEqual("gravity", _console.Complete("grav"));
        }

        [Test]
        public void La_Completion_Sarrete_Au_Prefixe_Commun()
        {
            _console.Register("sv_tickrate", 64f, 1f, 256f);
            _console.Register("sv_maxplayers", 10f, 1f, 64f);

            ClassicAssert.AreEqual("sv_", _console.Complete("sv"));
        }

        [Test]
        public void La_Completion_Laisse_Le_Texte_Si_Rien_Ne_Correspond()
        {
            ClassicAssert.AreEqual("zzz", _console.Complete("zzz"));
        }

        [Test]
        public void Laffichage_Ne_Grossit_Pas_Indefiniment()
        {
            _console.MaxOutputLines = 10;

            for (int i = 0; i < 100; i++)
                _console.Print("ligne " + i);

            ClassicAssert.AreEqual(10, _console.Output.Count);
            StringAssert.Contains("99", _console.Output[^1], "on garde les plus recentes");
        }

        // --- Integration : le but reel de la console ---------------------------------

        [Test]
        public void Changer_Une_Variable_Applique_Vraiment_Le_Reglage()
        {
            // C'est TOUT l'interet : regler sans recompiler.
            float appliedGravity = 15.24f;

            var g = _console.Find("gravity")!;
            g.Changed += v => appliedGravity = v.Value;

            _console.Execute("gravity 3.7");

            ClassicAssert.AreEqual(3.7f, appliedGravity, 1e-4f,
                "le moteur doit recevoir la nouvelle valeur");
        }
    }
}
