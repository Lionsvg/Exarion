// ===========================================================================
//  Exarion - Tests du rayon passant par un pixel
//
//  C'est la brique qui rend un curseur LIBRE utilisable dans l'editeur : sans
//  elle, cliquer quelque part selectionne ce qui est au centre de l'ecran.
//  Elle est purement trigonometrique, donc entierement testable ici.
// ===========================================================================

using System;
using System.Numerics;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core;

namespace Exarion.Tests
{
    [TestFixture]
    public class ScreenRayTests
    {
        // Une camera qui regarde vers les Z negatifs, comme celle du moteur a
        // lacet nul. Reperes pris exactement comme Camera les expose.
        private static readonly Vector3 Forward = new(0f, 0f, -1f);
        private static readonly Vector3 Right = new(1f, 0f, 0f);
        private static readonly Vector3 Up = new(0f, 1f, 0f);

        private const int W = 1280;
        private const int H = 720;
        private const float Fov = 90f;

        private static Vector3 At(float x, float y)
            => ScreenRay.Through(x, y, W, H, Forward, Right, Up, Fov);

        [Test]
        public void Le_Centre_De_Lecran_Regarde_Droit_Devant()
        {
            // LE TEST DE BASE : si le centre ne donne pas la direction de la
            // camera, tout le reste est decale.
            Vector3 ray = At(W / 2f - 0.5f, H / 2f - 0.5f);

            ClassicAssert.AreEqual(Forward.X, ray.X, 1e-5f);
            ClassicAssert.AreEqual(Forward.Y, ray.Y, 1e-5f);
            ClassicAssert.AreEqual(Forward.Z, ray.Z, 1e-5f);
        }

        [Test]
        public void Le_Rayon_Est_Toujours_Unitaire()
        {
            foreach (float x in new[] { 0f, 1f, W / 2f, W - 1f })
                foreach (float y in new[] { 0f, 1f, H / 2f, H - 1f })
                    ClassicAssert.AreEqual(1f, At(x, y).Length(), 1e-5f,
                        $"le rayon en ({x}, {y}) doit etre unitaire");
        }

        [Test]
        public void La_Droite_De_Lecran_Part_A_DROITE()
        {
            Vector3 ray = At(W - 1f, H / 2f - 0.5f);

            ClassicAssert.Greater(ray.X, 0f, "un pixel a droite doit viser a droite");
            ClassicAssert.Less(ray.Z, 0f, "et toujours vers l'avant");
            ClassicAssert.AreEqual(0f, ray.Y, 1e-5f, "sans composante verticale");
        }

        [Test]
        public void Le_HAUT_De_Lecran_Part_EN_HAUT()
        {
            // ⚠️ LE TEST QUI ATTRAPE LE SIGNE. Le pixel 0 est EN HAUT dans un
            // systeme de fenetrage, alors que l'axe vertical de la camera monte.
            // Une inversion oubliee donne une selection qui marche au centre et
            // se decale de plus en plus vers les bords - on cherche le defaut
            // dans le lancer de rayon pendant des heures.
            Vector3 haut = At(W / 2f - 0.5f, 0f);
            Vector3 bas = At(W / 2f - 0.5f, H - 1f);

            ClassicAssert.Greater(haut.Y, 0f, "le pixel du HAUT doit viser vers le haut");
            ClassicAssert.Less(bas.Y, 0f, "celui du BAS vers le bas");
        }

        [Test]
        public void Le_Champ_Horizontal_Est_Bien_Celui_Demande()
        {
            // A 90 degres de champ horizontal, le bord de l'ecran est a 45
            // degres de l'axe. On le verifie sur l'angle, pas sur une composante.
            Vector3 bord = ScreenRay.Through(W - 0.5f, H / 2f - 0.5f, W, H,
                                             Forward, Right, Up, 90f);

            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(bord, Forward), -1f, 1f));

            ClassicAssert.AreEqual(45f, angle * 180f / MathF.PI, 0.05f,
                "un champ de 90 degres met le bord a 45 degres de l'axe");
        }

        [Test]
        public void Le_Champ_VERTICAL_Se_Deduit_Du_Format_Dimage()
        {
            // ⚠️ Convention "Hor+" du moteur : le champ donne est l'HORIZONTAL,
            // le vertical s'en deduit. Si on utilisait le champ tel quel a la
            // verticale, la selection serait juste en carre et fausse en 16:9 -
            // decalee d'autant plus qu'on s'eloigne du centre en hauteur.
            //
            // tan(v/2) = tan(h/2) / aspect, soit v = 2*atan(1 / (1280/720)) pour
            // h = 90 : environ 58,72 degres.
            Vector3 bord = ScreenRay.Through(W / 2f - 0.5f, -0.5f, W, H,
                                             Forward, Right, Up, 90f);

            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(bord, Forward), -1f, 1f));
            float attendu = MathF.Atan(1f / (W / (float)H)) * 180f / MathF.PI;

            ClassicAssert.AreEqual(attendu, angle * 180f / MathF.PI, 0.05f,
                $"le demi-champ vertical doit valoir {attendu:0.00} degres en {W}x{H}");
        }

        [Test]
        public void Un_Format_Carre_Donne_Les_Memes_Angles_Dans_Les_Deux_Sens()
        {
            Vector3 droite = ScreenRay.Through(511.5f, 255.5f, 512, 512, Forward, Right, Up, 90f);
            Vector3 haut = ScreenRay.Through(255.5f, -0.5f, 512, 512, Forward, Right, Up, 90f);

            float aDroite = MathF.Acos(Math.Clamp(Vector3.Dot(droite, Forward), -1f, 1f));
            float enHaut = MathF.Acos(Math.Clamp(Vector3.Dot(haut, Forward), -1f, 1f));

            ClassicAssert.AreEqual(aDroite, enHaut, 1e-4f,
                "en carre, horizontal et vertical doivent etre symetriques");
        }

        [Test]
        public void Le_Rayon_Suit_LORIENTATION_De_La_Camera()
        {
            // Camera tournee de 90 degres : elle regarde vers les X negatifs.
            // Le centre de l'ecran doit suivre, sinon le rayon est calcule dans
            // le repere du monde au lieu de celui de la camera.
            var forward = new Vector3(-1f, 0f, 0f);
            var right = new Vector3(0f, 0f, -1f);
            var up = new Vector3(0f, 1f, 0f);

            Vector3 ray = ScreenRay.Through(W / 2f - 0.5f, H / 2f - 0.5f, W, H,
                                            forward, right, up, Fov);

            ClassicAssert.AreEqual(-1f, ray.X, 1e-5f);
            ClassicAssert.AreEqual(0f, ray.Z, 1e-5f);
        }

        [Test]
        public void Une_Taille_Nulle_Ne_Rend_Pas_NaN()
        {
            // Ca arrive : une fenetre reduite peut avoir un tampon de 0 pixel.
            // Un rayon NaN se propagerait dans toute la selection en silence.
            Vector3 ray = ScreenRay.Through(10f, 10f, 0, 0, Forward, Right, Up, Fov);

            ClassicAssert.IsFalse(float.IsNaN(ray.X) || float.IsNaN(ray.Y) || float.IsNaN(ray.Z));
            ClassicAssert.AreEqual(Forward, ray, "on retombe sur l'axe de la camera");
        }
    }
}
