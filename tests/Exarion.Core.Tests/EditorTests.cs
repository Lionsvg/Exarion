// ===========================================================================
//  Tests de l'editeur (4.1).
//
//  L'ANNULATION est la partie d'un editeur qui casse le plus facilement, et
//  ses defauts sont sournois : on ne les voit pas au moment ou ils
//  apparaissent, mais deux minutes plus tard, quand la carte ne ressemble
//  plus a rien et qu'on ne sait plus quelle operation l'a abimee.
//
//  D'ou le test central de ce fichier : cinquante operations, cinquante
//  annulations, et on doit retomber EXACTEMENT sur la carte de depart.
//
//  Les autres pieges couverts :
//   - une touche maintenue ne doit produire QU'UNE annulation ;
//   - une operation apres une annulation doit invalider le "retablir" ;
//   - une taille ne doit jamais devenir nulle ou negative ;
//   - une selection doit survivre a une annulation qui retire des boites.
// ===========================================================================

using System;
using System.Numerics;
using NUnit.Framework;
using Exarion.Core.Assets;
using Exarion.Core.Editor;
using Exarion.Core.UI;

namespace Exarion.Tests
{
    public class EditorTests
    {
        private static MapData Room()
        {
            var map = new MapData { Name = "essai" };

            map.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), new Vector3(0.3f));
            map.AddBox(new Vector3(4f, 1f, 0f), new Vector3(2f, 2f, 2f), new Vector3(0.5f));
            map.AddBox(new Vector3(-4f, 1f, 0f), new Vector3(2f, 2f, 2f), new Vector3(0.5f));

            return map;
        }

        private static string Fingerprint(MapData map)
        {
            var sb = new System.Text.StringBuilder();

            sb.Append(map.Name).Append('|').Append(map.Spawn).Append('|')
              .Append(map.SpawnYaw).Append('|').Append(map.SpawnPitch).Append('|');

            foreach (MapBox b in map.Boxes)
                sb.Append(b.Center).Append(b.Size).Append(b.Color).Append(';');

            foreach (MapLight l in map.Lights)
                sb.Append(l.Kind).Append(l.Position).Append(l.Intensity).Append(';');

            return sb.ToString();
        }

        // ===================================================================
        //  Annulation
        // ===================================================================

        [Test]
        public void Cinquante_Operations_Puis_Cinquante_Annulations_Rendent_La_Carte_De_Depart()
        {
            // LE test de ce fichier. Un historique par commandes inverses passe
            // les cas simples et derive sur les cas bornes ; celui-ci enchaine
            // deplacements, redimensionnements, creations, suppressions et
            // peintures, y compris des redimensionnements qui BUTENT sur la
            // taille minimale - donc exactement ce qu'un inverse rate.
            var editor = new EditorState(Room());
            string before = Fingerprint(editor.Map);

            var axes = new[] { EditAxis.X, EditAxis.Y, EditAxis.Z };

            for (int i = 0; i < 50; i++)
            {
                editor.Select(i % Math.Max(1, editor.Map.Boxes.Count));
                editor.Axis = axes[i % 3];

                switch (i % 5)
                {
                    case 0: editor.Move(axes[i % 3], (i % 7) - 3); break;

                    // Des pas tres negatifs : le bornage a la taille minimale
                    // se declenche pour de bon.
                    case 1: editor.Resize(axes[i % 3], -(i % 9)); break;

                    case 2: editor.Create(new Vector3(i, 0f, -i), new Vector3(1f, 3f, 1f)); break;
                    case 3: editor.Duplicate(); break;
                    case 4: editor.Delete(); break;
                }
            }

            Assert.That(Fingerprint(editor.Map), Is.Not.EqualTo(before),
                        "les cinquante operations n'ont rien change : le test ne prouve rien");

            int undone = 0;
            while (editor.Undo()) undone++;

            Assert.That(undone, Is.GreaterThan(10), $"seulement {undone} annulations disponibles");
            ClassicAssert.AreEqual(before, Fingerprint(editor.Map), "la carte n'est pas revenue a son etat initial");
        }

        [Test]
        public void Une_Touche_Maintenue_Ne_Compte_Que_Pour_Une_Annulation()
        {
            // ⚠️ LE bug classique. Une touche maintenue appelle Move soixante
            // fois par seconde. Sans regroupement, annuler un deplacement d'un
            // metre demande soixante Ctrl+Z - et l'editeur passe pour casse.
            var editor = new EditorState(Room());
            editor.Select(1);

            Vector3 start = editor.Map.Boxes[1].Center;

            editor.BeginStroke();
            for (int i = 0; i < 60; i++)
                editor.Move(EditAxis.X, 1);
            editor.EndStroke();

            ClassicAssert.AreEqual(1, editor.UndoDepth, "60 appels ont produit plus d'une annulation");
            ClassicAssert.AreEqual(start.X + 60f * editor.GridSize, editor.Map.Boxes[1].Center.X, 1e-4f);

            editor.Undo();

            ClassicAssert.AreEqual(start.X, editor.Map.Boxes[1].Center.X, 1e-4f, "une seule annulation doit tout defaire");
        }

        [Test]
        public void Deux_Operations_Distinctes_Font_Deux_Annulations()
        {
            // Le pendant du test precedent : sans regroupement explicite, chaque
            // operation doit rester independante.
            var editor = new EditorState(Room());
            editor.Select(1);

            editor.Move(EditAxis.X, 1);
            editor.Move(EditAxis.X, 1);

            ClassicAssert.AreEqual(2, editor.UndoDepth);
        }

        [Test]
        public void Une_Operation_Apres_Une_Annulation_Invalide_Le_Retablir()
        {
            // ⚠️ Sans ca, "retablir" ressuscite une branche abandonnee de
            // l'historique et produit une carte qui n'a jamais existe.
            var editor = new EditorState(Room());
            editor.Select(1);

            editor.Move(EditAxis.X, 4);
            editor.Undo();

            ClassicAssert.AreEqual(1, editor.RedoDepth, "il doit y avoir de quoi retablir");

            editor.Move(EditAxis.Z, 2);   // nouvelle branche

            ClassicAssert.AreEqual(0, editor.RedoDepth, "le retablir devait etre invalide");
            Assert.That(editor.Redo(), Is.False);
        }

        [Test]
        public void Annuler_Puis_Retablir_Redonne_Le_Meme_Etat()
        {
            var editor = new EditorState(Room());
            editor.Select(2);
            editor.Resize(EditAxis.Y, 3);

            string after = Fingerprint(editor.Map);

            editor.Undo();
            Assert.That(Fingerprint(editor.Map), Is.Not.EqualTo(after));

            editor.Redo();
            ClassicAssert.AreEqual(after, Fingerprint(editor.Map));
        }

        [Test]
        public void L_Historique_Est_Borne()
        {
            // Un historique sans borne est une fuite de memoire deguisee : une
            // heure d'edition, c'est des milliers d'operations.
            var editor = new EditorState(Room());
            editor.Select(1);

            for (int i = 0; i < EditorState.MaxHistory * 3; i++)
                editor.Move(EditAxis.X, 1);

            ClassicAssert.AreEqual(EditorState.MaxHistory, editor.UndoDepth);
        }

        [Test]
        public void Une_Selection_Survit_A_Une_Annulation_Qui_Retire_Des_Boites()
        {
            // Apres une annulation la carte peut avoir MOINS de boites : une
            // selection restee sur l'ancienne derniere serait hors bornes, et
            // la premiere operation suivante lancerait une exception.
            var editor = new EditorState(Room());

            editor.Create(Vector3.Zero, Vector3.One);
            editor.Create(new Vector3(5f, 0f, 5f), Vector3.One);

            int last = editor.Selected;
            ClassicAssert.AreEqual(editor.Map.Boxes.Count - 1, last);

            editor.Undo();
            editor.Undo();

            Assert.That(editor.Selected, Is.LessThan(editor.Map.Boxes.Count));
            Assert.DoesNotThrow(() => editor.Move(EditAxis.X, 1));
        }

        // ===================================================================
        //  Geometrie
        // ===================================================================

        [Test]
        public void Une_Boite_Ne_Peut_Jamais_Devenir_Nulle_Ou_Negative()
        {
            // ⚠️ Une taille nulle casse la collision - un intervalle inverse ne
            // croise jamais rien, donc le joueur traverse - et le maillage,
            // dont les faces se retournent. Et c'est trivial a declencher :
            // maintenir la touche de reduction deux secondes.
            var editor = new EditorState(Room());
            editor.Select(1);

            for (int i = 0; i < 200; i++)
                editor.Resize(EditAxis.X, -1);

            Vector3 size = editor.Map.Boxes[1].Size;

            Assert.That(size.X, Is.GreaterThanOrEqualTo(EditorState.MinimumSize));
            Assert.That(size.Y, Is.GreaterThan(0f));
            Assert.That(size.Z, Is.GreaterThan(0f));
        }

        [Test]
        public void Redimensionner_Ne_Deplace_Que_La_Face_Tiree()
        {
            // Semantique de Hammer : on tire une face, le cote oppose ne bouge
            // pas. Grossir symetriquement serait plus simple mais desaligne les
            // deux faces des que la taille n'est pas un multiple du double du pas.
            var editor = new EditorState(Room());
            editor.Select(1);

            MapBox before = editor.Map.Boxes[1];
            float minBefore = before.Center.X - before.Size.X * 0.5f;

            editor.Resize(EditAxis.X, 2, farSide: true);

            MapBox after = editor.Map.Boxes[1];
            float minAfter = after.Center.X - after.Size.X * 0.5f;
            float maxAfter = after.Center.X + after.Size.X * 0.5f;

            ClassicAssert.AreEqual(minBefore, minAfter, 1e-4f, "la face opposee a bouge");
            ClassicAssert.AreEqual(before.Size.X + 2f * editor.GridSize, after.Size.X, 1e-4f);
            ClassicAssert.AreEqual(minBefore + before.Size.X + 2f * editor.GridSize, maxAfter, 1e-4f);
        }

        [Test]
        public void Un_Deplacement_Conserve_L_Alignement()
        {
            // Une boite dont les faces tombent a 0,5 doit RESTER a 0,5. Si le
            // deplacement accrochait le centre a la grille, elle se ferait
            // recentrer au premier appui et perdrait son alignement - defaut
            // insidieux, parce qu'il n'arrive qu'une fois.
            var map = new MapData();
            map.AddBox(new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1f, 1f, 1f), Vector3.One);

            var editor = new EditorState(map);
            editor.Select(0);

            editor.Move(EditAxis.X, 3);

            ClassicAssert.AreEqual(3.5f, editor.Map.Boxes[0].Center.X, 1e-5f);
        }

        [Test]
        public void Une_Creation_Accroche_Le_COIN_Et_Pas_Le_Centre()
        {
            // Accrocher le centre d'une boite de taille impaire poserait ses
            // faces entre deux lignes de grille, et plus rien ne s'emboiterait.
            var editor = new EditorState(new MapData());

            editor.Create(new Vector3(2.3f, 0.1f, -1.8f), new Vector3(3f, 3f, 3f));

            MapBox box = editor.Map.Boxes[0];
            Vector3 min = box.Center - box.Size * 0.5f;

            ClassicAssert.AreEqual(2f, min.X, 1e-4f);
            ClassicAssert.AreEqual(0f, min.Y, 1e-4f);
            ClassicAssert.AreEqual(-2f, min.Z, 1e-4f);
        }

        [Test]
        public void Dupliquer_Pose_La_Copie_A_COTE_Et_Pas_Dedans()
        {
            // Le decalage vaut la TAILLE de la boite, pas un pas de grille :
            // dupliquer un mur de 8 m avec un pas de 1 m le poserait dedans, et
            // on ne verrait rien se passer.
            var editor = new EditorState(Room());
            editor.Select(1);
            editor.Axis = EditAxis.X;

            MapBox source = editor.Map.Boxes[1];
            editor.Duplicate();
            MapBox copy = editor.Map.Boxes[editor.Selected];

            ClassicAssert.AreEqual(source.Center.X + source.Size.X, copy.Center.X, 1e-4f);
            ClassicAssert.AreEqual(source.Size.X, copy.Size.X, 1e-4f);

            // Les deux se touchent sans se chevaucher.
            float sourceMax = source.Center.X + source.Size.X * 0.5f;
            float copyMin = copy.Center.X - copy.Size.X * 0.5f;
            ClassicAssert.AreEqual(sourceMax, copyMin, 1e-4f);
        }

        [Test]
        public void La_Grille_Se_Change_Sans_Sortir_De_La_Liste()
        {
            var editor = new EditorState(Room());

            for (int i = 0; i < 30; i++) editor.CycleGrid(+1);
            ClassicAssert.AreEqual(EditorState.GridSteps[^1], editor.GridSize);

            for (int i = 0; i < 30; i++) editor.CycleGrid(-1);
            ClassicAssert.AreEqual(EditorState.GridSteps[0], editor.GridSize);
        }

        // ===================================================================
        //  Selection au rayon
        // ===================================================================

        [Test]
        public void Le_Rayon_Prend_La_Boite_LA_PLUS_PROCHE()
        {
            // Une selection qui prend la premiere boite touchee dans l'ordre du
            // tableau attrape parfois celle de derriere. Le defaut ne se voit
            // que dans les scenes ou deux boites sont alignees - donc tout le
            // temps, dans un couloir.
            var map = new MapData();
            map.AddBox(new Vector3(0f, 0f, -20f), new Vector3(2f, 2f, 2f), Vector3.One);   // loin, indice 0
            map.AddBox(new Vector3(0f, 0f, -5f), new Vector3(2f, 2f, 2f), Vector3.One);    // pres, indice 1

            var editor = new EditorState(map);

            int hit = editor.Pick(Vector3.Zero, -Vector3.UnitZ);

            ClassicAssert.AreEqual(1, hit, "c'est la boite lointaine qui a ete prise");
        }

        [Test]
        public void Un_Rayon_Dans_Le_Vide_Ne_Selectionne_Rien()
        {
            var editor = new EditorState(Room());

            ClassicAssert.AreEqual(-1, editor.Pick(new Vector3(0f, 50f, 0f), Vector3.UnitY));
        }

        [Test]
        public void Une_Selection_Hors_Bornes_Est_Refusee()
        {
            var editor = new EditorState(Room());

            editor.Select(999);
            Assert.That(editor.HasSelection, Is.False);

            editor.Select(-5);
            Assert.That(editor.HasSelection, Is.False);

            Assert.That(editor.Move(EditAxis.X, 1), Is.False, "sans selection, rien ne doit bouger");
            ClassicAssert.AreEqual(0, editor.UndoDepth, "une operation refusee ne doit pas polluer l'historique");
        }

        [Test]
        public void Le_Drapeau_De_Modification_Suit_Les_Enregistrements()
        {
            var editor = new EditorState(Room());
            Assert.That(editor.Dirty, Is.False);

            editor.Select(1);
            editor.Move(EditAxis.X, 1);
            Assert.That(editor.Dirty, Is.True);

            editor.MarkSaved();
            Assert.That(editor.Dirty, Is.False);
        }

        // ===================================================================
        //  Menu
        // ===================================================================

        [Test]
        public void Le_Menu_Boucle_Dans_Les_Deux_Sens()
        {
            // ⚠️ Le modulo de C# garde le signe du dividende : -1 % 3 vaut -1.
            // Remonter depuis la premiere entree sans rattrapage donne un
            // indice negatif, donc une exception.
            var menu = new MenuModel("Jouer", "Editeur", "Quitter");

            ClassicAssert.AreEqual(0, menu.Selected);

            menu.Move(-1);
            ClassicAssert.AreEqual(2, menu.Selected, "remonter depuis le haut doit aller en bas");

            menu.Move(+1);
            ClassicAssert.AreEqual(0, menu.Selected, "descendre depuis le bas doit revenir en haut");
        }

        [Test]
        public void Un_Menu_Vide_Ne_Fait_Pas_Planter()
        {
            var menu = new MenuModel();

            Assert.DoesNotThrow(() => menu.Move(+1));
            Assert.DoesNotThrow(() => menu.Move(-1));
            ClassicAssert.AreEqual("", menu.Current);
        }

        [Test]
        public void Remplacer_Les_Entrees_Rentre_La_Selection_Dans_Les_Bornes()
        {
            // Le menu de choix de carte se reconstruit a chaque ouverture : si
            // une carte disparait du dossier, la selection doit suivre.
            var menu = new MenuModel("a", "b", "c", "d");
            menu.Select(3);

            menu.SetItems(new[] { "a", "b" });

            ClassicAssert.AreEqual(1, menu.Selected);
            ClassicAssert.AreEqual("b", menu.Current);
        }
    }
}
