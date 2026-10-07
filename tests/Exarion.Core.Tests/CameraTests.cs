// ===========================================================================
//  Tests de la camera.
//
//  Ecrits APRES coup, suite a un bug visuel : l'image etait etiree et je
//  n'avais aucun moyen de m'en apercevoir sans regarder l'ecran. Ces tests
//  verifient la geometrie sans rien afficher.
//
//  Convention verifiee : System.Numerics, VECTEUR-LIGNE (v * M).
//  Un point du monde devient : clip = Vector4.Transform(point, vueProj)
//  puis NDC = clip.xyz / clip.w, avec :
//      x, y dans [-1, 1] a l'ecran
//      z    dans [-1, 1] (convention OpenGL, PAS [0,1] comme DirectX)
// ===========================================================================

using System;
using System.Numerics;
using NUnit.Framework;
using Exarion.Engine.Graphics;

namespace Exarion.Tests
{
    public class CameraTests
    {
        private static Camera MakeCamera()
            => new Camera
            {
                Position = Vector3.Zero,
                Yaw = 0f,
                Pitch = 0f,
                AspectRatio = 16f / 9f,
                HorizontalFovDegrees = 90f,
                NearPlane = 0.02f,
                FarPlane = 500f,
            };

        /// <summary>Projette un point du monde en coordonnees normalisees.</summary>
        private static Vector3 ToNdc(Camera cam, Vector3 world)
        {
            Vector4 clip = Vector4.Transform(new Vector4(world, 1f), cam.GetViewProjection());
            return new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        }

        // --- Orientation -----------------------------------------------------

        [Test]
        public void Yaw_Zero_Regarde_Vers_Moins_Z()
        {
            var c = MakeCamera();

            ClassicAssert.AreEqual(0f, c.Forward.X, 1e-5f);
            ClassicAssert.AreEqual(0f, c.Forward.Y, 1e-5f);
            ClassicAssert.AreEqual(-1f, c.Forward.Z, 1e-5f);
        }

        [Test]
        public void Le_Vecteur_Droite_Est_Perpendiculaire_Et_Horizontal()
        {
            var c = MakeCamera();
            c.AddLook(0.7f, 0.3f);

            ClassicAssert.AreEqual(0f, c.Right.Y, 1e-5f, "la droite doit rester horizontale");
            ClassicAssert.AreEqual(0f, Vector3.Dot(c.Right, c.Forward), 1e-4f, "droite et avant sont perpendiculaires");
            ClassicAssert.AreEqual(1f, c.Right.Length(), 1e-4f);
        }

        [Test]
        public void Le_Pitch_Est_Borne()
        {
            var c = MakeCamera();

            c.AddLook(0f, 100f);
            ClassicAssert.LessOrEqual(c.Pitch, Camera.MaxPitch + 1e-4f, "on ne doit pas pouvoir se retourner");

            c.AddLook(0f, -200f);
            ClassicAssert.GreaterOrEqual(c.Pitch, -Camera.MaxPitch - 1e-4f);
        }

        // --- Projection : LE test qui aurait attrape le bug ------------------

        [Test]
        public void Un_Point_Droit_Devant_Tombe_Au_Centre_De_Lecran()
        {
            var c = MakeCamera();

            Vector3 ndc = ToNdc(c, new Vector3(0f, 0f, -10f));

            ClassicAssert.AreEqual(0f, ndc.X, 1e-4f);
            ClassicAssert.AreEqual(0f, ndc.Y, 1e-4f);
        }

        [Test]
        public void Un_Point_A_Droite_Tombe_A_Droite_De_Lecran()
        {
            var c = MakeCamera();

            Vector3 ndc = ToNdc(c, new Vector3(2f, 0f, -10f));

            ClassicAssert.Greater(ndc.X, 0f, "a droite dans le monde = a droite a l'ecran");
            ClassicAssert.AreEqual(0f, ndc.Y, 1e-4f);
        }

        [Test]
        public void Un_Point_En_Hauteur_Tombe_En_Haut_De_Lecran()
        {
            var c = MakeCamera();

            Vector3 ndc = ToNdc(c, new Vector3(0f, 2f, -10f));

            ClassicAssert.Greater(ndc.Y, 0f, "en haut dans le monde = en haut a l'ecran");
            ClassicAssert.AreEqual(0f, ndc.X, 1e-4f);
        }

        [Test]
        public void La_Profondeur_Respecte_La_Convention_OpenGL()
        {
            // C'EST CE TEST QUI PROTEGE DU PIEGE System.Numerics :
            // CreatePerspectiveFieldOfView produit [0,1] (DirectX).
            // OpenGL 3.3 attend [-1,1]. Sans ca, toute la scene s'ecrase
            // dans la moitie proche du tampon de profondeur.
            var c = MakeCamera();

            Vector3 near = ToNdc(c, new Vector3(0f, 0f, -c.NearPlane));
            Vector3 far = ToNdc(c, new Vector3(0f, 0f, -c.FarPlane));

            ClassicAssert.AreEqual(-1f, near.Z, 1e-3f, "le plan proche doit tomber sur -1");
            ClassicAssert.AreEqual(1f, far.Z, 1e-3f, "le plan lointain doit tomber sur +1");
        }

        [Test]
        public void La_Profondeur_Croit_Avec_Leloignement()
        {
            var c = MakeCamera();

            float z5 = ToNdc(c, new Vector3(0f, 0f, -5f)).Z;
            float z50 = ToNdc(c, new Vector3(0f, 0f, -50f)).Z;

            ClassicAssert.Less(z5, z50, "plus c'est loin, plus z est grand");
        }

        [Test]
        public void Un_Point_Derriere_La_Camera_Nest_Pas_Devant()
        {
            var c = MakeCamera();

            Vector4 clip = Vector4.Transform(new Vector4(0f, 0f, 10f, 1f), c.GetViewProjection());

            ClassicAssert.Less(clip.W, 0f, "un point derriere doit avoir un w negatif");
        }

        // --- Champ de vision --------------------------------------------------

        [Test]
        public void Le_Bord_Du_Champ_Horizontal_Tombe_Sur_Le_Bord_De_Lecran()
        {
            var c = MakeCamera();   // 90 degres horizontal

            // A 45 degres sur le cote, on est pile au bord : x = -z.
            Vector3 ndc = ToNdc(c, new Vector3(10f, 0f, -10f));

            ClassicAssert.AreEqual(1f, ndc.X, 1e-3f, "45 deg = bord droit exactement");
        }

        [Test]
        public void Le_Champ_Vertical_Est_Derive_Du_Format_Dimage()
        {
            var wide = MakeCamera();
            wide.AspectRatio = 16f / 9f;

            var square = MakeCamera();
            square.AspectRatio = 1f;

            // A champ horizontal egal, un ecran plus large montre MOINS en
            // hauteur. C'est le comportement "Hor+" attendu d'un FPS.
            ClassicAssert.Less(wide.VerticalFovDegrees, square.VerticalFovDegrees);
            ClassicAssert.AreEqual(90f, square.VerticalFovDegrees, 0.01f,
                "en 1:1, vertical et horizontal se confondent");
            ClassicAssert.AreEqual(58.7f, wide.VerticalFovDegrees, 0.5f,
                "90 deg horizontal en 16:9 donne ~59 deg vertical");
        }

        [Test]
        public void Changer_Le_Format_Ne_Deforme_Pas_Les_Angles_Horizontaux()
        {
            // Le bug percu par le joueur etait "la vision s'etire". Ce test
            // verifie qu'un meme angle horizontal tombe au meme endroit
            // quel que soit le format de la fenetre.
            foreach (float aspect in new[] { 4f / 3f, 16f / 9f, 21f / 9f })
            {
                var c = MakeCamera();
                c.AspectRatio = aspect;

                Vector3 ndc = ToNdc(c, new Vector3(10f, 0f, -10f));
                ClassicAssert.AreEqual(1f, ndc.X, 1e-3f, $"format {aspect:0.00}");
            }
        }

        // --- Deplacement de la camera ------------------------------------------

        [Test]
        public void Deplacer_La_Camera_Deplace_La_Scene_En_Sens_Inverse()
        {
            var c = MakeCamera();

            Vector3 before = ToNdc(c, new Vector3(0f, 0f, -10f));
            ClassicAssert.AreEqual(0f, before.X, 1e-4f);

            // On se decale a droite : l'objet doit partir vers la gauche.
            c.Position = new Vector3(2f, 0f, 0f);
            Vector3 after = ToNdc(c, new Vector3(0f, 0f, -10f));

            ClassicAssert.Less(after.X, -0.05f, "la translation de la vue doit etre prise en compte");
        }

        [Test]
        public void Tourner_La_Camera_Fait_Defiler_La_Scene()
        {
            var c = MakeCamera();

            // On tourne vers la gauche : l'objet devant doit partir a droite.
            c.AddLook(0.3f, 0f);
            Vector3 ndc = ToNdc(c, new Vector3(0f, 0f, -10f));

            ClassicAssert.Greater(ndc.X, 0.05f);
        }
    }
}
