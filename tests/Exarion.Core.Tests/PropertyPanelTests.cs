// ===========================================================================
//  Tests du panneau de proprietes (17/08).
//
//  Un panneau d'edition est une machine a etats : focalise ou non, en saisie
//  ou non, avec un tampon qu'on valide ou qu'on abandonne. C'est exactement
//  le genre de code ou l'on casse quelque chose sans le voir - et ou le
//  degat ne se lit PAS comme un bug de panneau, mais comme une carte qui a
//  bouge toute seule.
//
//  Ces tests existent parce que le panneau vit dans Core (regle n°1) : on
//  peut donc taper, annuler, changer de selection et verifier que la carte
//  est intacte, sans ouvrir de fenetre.
//
//  ⚠️ LE TEST LE PLUS IMPORTANT DU FICHIER EST
//  Ouvrir_Un_Champ_Et_Valider_Sans_Rien_Changer_Ne_DEPLACE_Rien.
//  Il couvre le piege qui a dicte la conception : l'affichage arrondit, la
//  saisie non. Si le tampon partait de la valeur AFFICHEE, ouvrir puis
//  valider un champ deplacerait le mur de quelques 1e-7 - trop peu pour se
//  voir, assez pour s'accumuler a chaque passage.
// ===========================================================================

using System.Globalization;
using System.Numerics;
using System.Threading;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core.Assets;
using Exarion.Core.Editor;

namespace Exarion.Tests
{
    public class PropertyPanelTests
    {
        private static EditorState WithOneBox()
        {
            var map = new MapData { Name = "essai" };
            map.AddBox(new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f), new Vector3(0.5f, 0.5f, 0.5f));

            var editor = new EditorState(map);
            editor.Select(0);

            return editor;
        }

        private static void TypeAll(PropertyPanel panel, string text)
        {
            foreach (char c in text)
                panel.Type(c);
        }

        /// <summary>Ouvre le champ portant cet identifiant. Echoue si absent.</summary>
        private static void Focus(PropertyPanel panel, string id)
        {
            panel.ToggleFocus();

            for (int guard = 0; guard < 200; guard++)
            {
                if (panel.Current is PropertyField f && f.Id == id)
                    return;

                panel.MoveCursor(+1);
            }

            Assert.Fail($"champ \"{id}\" introuvable dans le panneau");
        }

        // ===================================================================
        //  Navigation
        // ===================================================================

        [Test]
        public void Le_Curseur_Ne_S_Arrete_JAMAIS_Sur_Un_Titre()
        {
            var panel = new PropertyPanel(WithOneBox());
            panel.ToggleFocus();

            // Un tour complet et demi : le curseur boucle, il doit rester
            // valide apres le rebouclage aussi.
            for (int i = 0; i < 40; i++)
            {
                panel.MoveCursor(+1);

                ClassicAssert.IsTrue(panel.Current!.Value.Selectable,
                    "le curseur s'est arrete sur un titre de groupe, qui n'est pas editable");
            }
        }

        [Test]
        public void Un_Panneau_Sans_Selection_Ne_Plante_Pas_A_La_Navigation()
        {
            // Liste faite UNIQUEMENT de titres : sans borne, MoveCursor
            // tournerait indefiniment a la recherche d'une ligne selectionnable.
            var editor = new EditorState(new MapData());
            var panel = new PropertyPanel(editor);

            panel.ToggleFocus();
            panel.MoveCursor(+1);
            panel.MoveCursor(-1);

            ClassicAssert.IsFalse(panel.BeginEdit(), "il n'y a rien a editer sans selection");
        }

        // ===================================================================
        //  Saisie
        // ===================================================================

        [Test]
        public void Valider_Une_Valeur_L_Ecrit_Dans_La_Carte()
        {
            EditorState editor = WithOneBox();
            var panel = new PropertyPanel(editor);

            Focus(panel, "cy");

            ClassicAssert.IsTrue(panel.BeginEdit());
            TypeAll(panel, "7.5");
            ClassicAssert.IsTrue(panel.CommitEdit());

            ClassicAssert.AreEqual(7.5f, editor.Map.Boxes[0].Center.Y, 1e-6f);
        }

        [Test]
        public void Le_Tampon_Part_De_La_Valeur_Courante_Et_Pas_Du_Vide()
        {
            EditorState editor = WithOneBox();
            var panel = new PropertyPanel(editor);

            Focus(panel, "cy");
            panel.BeginEdit();

            // Un tampon vide obligerait a retaper la valeur entiere pour en
            // changer un chiffre - et surtout, valider par reflexe ecrirait
            // « nombre illisible » sur une valeur parfaitement valide.
            ClassicAssert.AreEqual("2", panel.Buffer);
        }

        /// <summary>
        /// ⚠️ LE TEST CENTRAL. Verifie par mutation : en faisant partir le
        /// tampon de PropertyField.Display au lieu de Raw, il tombe - et lui
        /// seul. La derive serait de 2e-7 par passage, invisible a l'oeil et
        /// cumulative a chaque enregistrement, exactement ce que le format
        /// .map s'interdit en ecrivant ses flottants sans perte.
        /// </summary>
        [Test]
        public void Ouvrir_Un_Champ_Et_Valider_Sans_Rien_Changer_Ne_DEPLACE_Rien()
        {
            var map = new MapData();
            map.AddBox(new Vector3(0f, 2.2500002f, 0f), Vector3.One, Vector3.One);

            var editor = new EditorState(map);
            editor.Select(0);

            var panel = new PropertyPanel(editor);

            Focus(panel, "cy");
            panel.BeginEdit();
            panel.CommitEdit();

            ClassicAssert.AreEqual(2.2500002f, editor.Map.Boxes[0].Center.Y,
                "ouvrir puis valider un champ sans le modifier a change sa valeur");
        }

        [Test]
        public void Annuler_Une_Saisie_Laisse_La_Carte_Intacte()
        {
            EditorState editor = WithOneBox();
            var panel = new PropertyPanel(editor);

            Focus(panel, "cx");
            panel.BeginEdit();
            TypeAll(panel, "999");
            panel.CancelEdit();

            ClassicAssert.AreEqual(1f, editor.Map.Boxes[0].Center.X, 1e-6f);
            ClassicAssert.IsFalse(panel.Editing);
        }

        [Test]
        public void Une_Valeur_Illisible_Est_Refusee_Et_Le_Tampon_RESTE_Ouvert()
        {
            EditorState editor = WithOneBox();
            var panel = new PropertyPanel(editor);

            Focus(panel, "cx");
            panel.BeginEdit();
            TypeAll(panel, "12a4");

            ClassicAssert.IsFalse(panel.CommitEdit());

            // Vider le champ sur une faute de frappe ferait perdre la saisie
            // entiere pour un caractere.
            ClassicAssert.IsTrue(panel.Editing);
            ClassicAssert.AreEqual("12a4", panel.Buffer);
            ClassicAssert.IsTrue(panel.Message.Length > 0, "un refus doit se dire");

            ClassicAssert.AreEqual(1f, editor.Map.Boxes[0].Center.X, 1e-6f);
        }

        [Test]
        public void L_Infini_Et_Le_Non_Nombre_Sont_Refuses()
        {
            // « Infinity » et « NaN » passent float.TryParse. Ils ne cassent
            // qu'au rendu, sous forme de geometrie disparue sans message.
            ClassicAssert.IsFalse(PropertyPanel.TryReadNumber("NaN", out _));
            ClassicAssert.IsFalse(PropertyPanel.TryReadNumber("Infinity", out _));
            ClassicAssert.IsFalse(PropertyPanel.TryReadNumber("-Infinity", out _));
        }

        [Test]
        public void La_Virgule_Est_Acceptee_A_La_Saisie()
        {
            // Clavier francais : « 2,5 » est ce qu'on tape spontanement.
            ClassicAssert.IsTrue(PropertyPanel.TryReadNumber("2,5", out float value));
            ClassicAssert.AreEqual(2.5f, value, 1e-6f);
        }

        /// <summary>
        /// ⚠️ Le meme piege qu'en 2.4, pris du bon cote. En culture francaise,
        /// une lecture en culture COURANTE rendrait « 2.5 » illisible ou egal
        /// a 25 - et la valeur part ensuite dans un .map, ou elle doit etre
        /// invariante.
        /// </summary>
        [Test]
        public void La_Lecture_Ne_Depend_PAS_De_La_Culture_Du_Poste()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");

                ClassicAssert.IsTrue(PropertyPanel.TryReadNumber("2.5", out float point));
                ClassicAssert.AreEqual(2.5f, point, 1e-6f);

                ClassicAssert.IsTrue(PropertyPanel.TryReadNumber("2,5", out float comma));
                ClassicAssert.AreEqual(2.5f, comma, 1e-6f);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void Un_Espace_Ne_Peut_Pas_Entrer_Dans_Une_Valeur()
        {
            // Le format .map decoupe sur les espaces : une valeur qui en
            // contient produit un fichier qu'on ne sait plus relire.
            EditorState editor = WithOneBox();
            var panel = new PropertyPanel(editor);

            Focus(panel, "cx");
            panel.BeginEdit();
            TypeAll(panel, "1 2");

            ClassicAssert.AreEqual("12", panel.Buffer);
            StringAssert.DoesNotContain(" ", panel.Buffer);
        }

        /// <summary>
        /// ⚠️ LA CONTREPARTIE DU TAMPON EXACT, et elle a ete trouvee par ce
        /// test, pas en relisant le code.
        ///
        /// Le tampon doit partir de la valeur exacte pour ne pas deriver. Mais
        /// si la frappe AJOUTE a cette valeur, taper « 7.5 » sur un champ
        /// valant 2 donne 27,5 - le panneau devient inutilisable. La valeur
        /// s'ouvre donc « toute selectionnee », comme dans Excel.
        /// </summary>
        [Test]
        public void La_Premiere_Frappe_REMPLACE_La_Valeur_Au_Lieu_De_S_Y_Ajouter()
        {
            EditorState editor = WithOneBox();   // centre Y = 2
            var panel = new PropertyPanel(editor);

            Focus(panel, "cy");
            panel.BeginEdit();

            ClassicAssert.AreEqual("2", panel.Buffer, "la valeur doit s'afficher entiere a l'ouverture");

            TypeAll(panel, "7.5");

            ClassicAssert.AreEqual("7.5", panel.Buffer, "la premiere frappe devait remplacer, pas completer");
        }

        [Test]
        public void Corriger_Au_Retour_Arriere_Passe_En_Edition_NORMALE()
        {
            EditorState editor = WithOneBox();
            var panel = new PropertyPanel(editor);

            Focus(panel, "cz");   // vaut 3
            panel.BeginEdit();

            // Le premier retour arriere efface la selection entiere...
            panel.Backspace();
            ClassicAssert.AreEqual("", panel.Buffer);

            // ...et la frappe qui suit s'ajoute normalement.
            TypeAll(panel, "12");
            ClassicAssert.AreEqual("12", panel.Buffer);

            panel.CommitEdit();
            ClassicAssert.AreEqual(12f, editor.Map.Boxes[0].Center.Z, 1e-6f);
        }

        // ===================================================================
        //  Entites
        // ===================================================================

        [Test]
        public void Un_PNJ_Fraichement_Pose_Montre_DEJA_Tous_Ses_Reglages()
        {
            // C'est la raison d'etre du catalogue : sans lui, une entite neuve
            // n'a aucune propriete, donc le panneau serait vide et rien ne
            // dirait qu'un PNJ se regle avec « speed » et « sight ».
            var editor = new EditorState(new MapData());
            editor.AddEntity(EntityTypes.Npc, Vector3.Zero);

            var panel = new PropertyPanel(editor);

            AssertHasField(panel, "p:speed");
            AssertHasField(panel, "p:sight");
            AssertHasField(panel, "p:fov");
            AssertHasField(panel, "p:behaviour");
        }

        [Test]
        public void Une_Propriete_Absente_S_Affiche_Comme_UN_DEFAUT()
        {
            var editor = new EditorState(new MapData());
            editor.AddEntity(EntityTypes.Npc, Vector3.Zero);

            var panel = new PropertyPanel(editor);
            PropertyField speed = Find(panel, "p:speed");

            // ⚠️ La distinction « absente » / « ecrite » n'est pas cosmetique :
            // sans elle, on croit avoir regle une valeur qu'on n'a jamais
            // ecrite, et copier l'entite sur une autre carte ne donne pas le
            // meme resultat.
            ClassicAssert.IsTrue(speed.IsDefault);
            ClassicAssert.AreEqual("2.2", speed.Display);
            ClassicAssert.AreEqual("", speed.Raw, "le tampon doit partir VIDE sur une propriete absente");
        }

        [Test]
        public void Ecrire_Puis_Vider_Une_Propriete_La_RETIRE()
        {
            var editor = new EditorState(new MapData());
            editor.AddEntity(EntityTypes.Npc, Vector3.Zero);

            var panel = new PropertyPanel(editor);

            Focus(panel, "p:speed");
            panel.BeginEdit();
            TypeAll(panel, "5");
            panel.CommitEdit();

            ClassicAssert.AreEqual("5", editor.Map.Entities[0].Get("speed"));

            // Vider rend la propriete au defaut du moteur. Ecrire "" a la place
            // laisserait une propriete PRESENTE et vide, qui n'est pas la meme
            // chose : l'absente prend le defaut, la vide impose "".
            panel.BeginEdit();
            panel.Backspace();
            panel.CommitEdit();

            ClassicAssert.IsFalse(editor.Map.Entities[0].Properties.ContainsKey("speed"));
        }

        [Test]
        public void Une_Propriete_HORS_Catalogue_Reste_Visible()
        {
            // Le format conserve ce qu'il ne connait pas. La cacher ferait
            // disparaitre du panneau une propriete bel et bien dans le fichier,
            // et on la reecrirait en double.
            var editor = new EditorState(new MapData());
            editor.AddEntity(EntityTypes.Npc, Vector3.Zero);
            editor.SetProperty("truc_a_moi", "42");

            AssertHasField(new PropertyPanel(editor), "p:truc_a_moi");
        }

        // ===================================================================
        //  Interaction avec le reste de l'editeur
        // ===================================================================

        [Test]
        public void Une_Modification_Par_Le_Panneau_S_ANNULE()
        {
            EditorState editor = WithOneBox();
            var panel = new PropertyPanel(editor);

            Focus(panel, "cy");
            panel.BeginEdit();
            TypeAll(panel, "50");
            panel.CommitEdit();

            ClassicAssert.AreEqual(50f, editor.Map.Boxes[0].Center.Y, 1e-6f);

            ClassicAssert.IsTrue(editor.Undo());
            ClassicAssert.AreEqual(2f, editor.Map.Boxes[0].Center.Y, 1e-6f);
        }

        [Test]
        public void La_Reconstruction_Ne_Mange_PAS_La_Saisie_En_Cours()
        {
            // Refresh est appele a chaque image. S'il effacait le tampon, on
            // perdrait chaque caractere soixante fois par seconde.
            EditorState editor = WithOneBox();
            var panel = new PropertyPanel(editor);

            Focus(panel, "cx");
            panel.BeginEdit();
            TypeAll(panel, "42");

            panel.Refresh();

            ClassicAssert.IsTrue(panel.Editing);
            ClassicAssert.AreEqual("42", panel.Buffer);
        }

        [Test]
        public void Changer_De_Selection_ABANDONNE_La_Saisie()
        {
            // Garder le tampon ecrirait la valeur tapee dans le champ d'un
            // AUTRE objet - le pire resultat possible, parce qu'il est
            // plausible.
            var map = new MapData();
            map.AddBox(new Vector3(1f, 2f, 3f), Vector3.One, Vector3.One);

            var editor = new EditorState(map);
            editor.Select(0);

            var panel = new PropertyPanel(editor);

            Focus(panel, "cx");
            panel.BeginEdit();
            TypeAll(panel, "999");

            editor.AddEntity(EntityTypes.Waypoint, Vector3.Zero);   // selectionne l'entite
            panel.Refresh();

            ClassicAssert.IsFalse(panel.Editing, "la saisie devait etre abandonnee avec l'ancienne selection");
            ClassicAssert.AreEqual(1f, map.Boxes[0].Center.X, 1e-6f);
        }

        [Test]
        public void Rendre_Le_Clavier_Abandonne_La_Saisie()
        {
            EditorState editor = WithOneBox();
            var panel = new PropertyPanel(editor);

            Focus(panel, "cx");
            panel.BeginEdit();
            TypeAll(panel, "77");

            panel.ToggleFocus();   // on rend le clavier a la camera

            ClassicAssert.IsFalse(panel.Focused);
            ClassicAssert.IsFalse(panel.Editing);
            ClassicAssert.AreEqual(1f, editor.Map.Boxes[0].Center.X, 1e-6f);
        }

        // ===================================================================
        //  Formes
        // ===================================================================

        [Test]
        public void La_Forme_Se_Fait_DEFILER_Et_Ne_Se_Tape_Pas()
        {
            EditorState editor = WithOneBox();
            var panel = new PropertyPanel(editor);

            Focus(panel, "shape");

            // Ouvrir un tampon de texte sur une forme obligerait a ecrire
            // « cylindre » sans faute pour en changer.
            ClassicAssert.IsFalse(panel.BeginEdit());

            ClassicAssert.IsTrue(panel.CycleValue(+1));
            ClassicAssert.AreEqual(MapShape.Sphere, editor.Map.Boxes[0].Shape);

            ClassicAssert.IsTrue(panel.CycleValue(-1));
            ClassicAssert.AreEqual(MapShape.Block, editor.Map.Boxes[0].Shape);
        }

        [Test]
        public void Le_Defilement_Des_Formes_BOUCLE_Dans_Les_Deux_Sens()
        {
            EditorState editor = WithOneBox();
            var panel = new PropertyPanel(editor);

            Focus(panel, "shape");

            // Depuis le bloc, un cran en arriere doit atteindre la derniere
            // forme et non sortir du tableau.
            panel.CycleValue(-1);

            ClassicAssert.AreEqual(MapShapes.All[^1], editor.Map.Boxes[0].Shape);
        }

        [Test]
        public void Changer_La_Forme_Ne_Touche_NI_Au_Centre_NI_A_La_Taille()
        {
            // C'est ce qui permet d'essayer les cinq formes sur un meme
            // emplacement sans avoir a realigner quoi que ce soit.
            EditorState editor = WithOneBox();
            var panel = new PropertyPanel(editor);

            Focus(panel, "shape");
            panel.CycleValue(+1);

            ClassicAssert.AreEqual(new Vector3(1f, 2f, 3f), editor.Map.Boxes[0].Center);
            ClassicAssert.AreEqual(new Vector3(2f, 4f, 6f), editor.Map.Boxes[0].Size);
        }

        // ===================================================================

        private static PropertyField Find(PropertyPanel panel, string id)
        {
            foreach (PropertyField field in panel.Fields)
                if (field.Id == id)
                    return field;

            Assert.Fail($"champ \"{id}\" absent du panneau");
            return default;
        }

        private static void AssertHasField(PropertyPanel panel, string id) => Find(panel, id);
    }
}
