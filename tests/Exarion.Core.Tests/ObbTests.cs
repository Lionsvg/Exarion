// ===========================================================================
//  Tests des boites orientees et du theoreme des axes separateurs.
//
//  Le cas qui compte vraiment : le contact ARETE CONTRE ARETE. Deux boites
//  tournees peuvent se toucher par leurs aretes sans qu'aucun de leurs 6
//  axes propres ne le detecte. C'est precisement ce que couvrent les 9
//  produits vectoriels - et c'est ce qu'on oublie toujours.
// ===========================================================================

using System;
using System.Numerics;
using NUnit.Framework;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class ObbTests
    {
        private static Obb Unit(Vector3 center, float yawDegrees = 0f)
            => new Obb(center, new Vector3(0.5f),
                       Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f));

        // --- Geometrie de base ------------------------------------------------

        [Test]
        public void Sans_Rotation_Les_Axes_Sont_Ceux_Du_Monde()
        {
            var box = Unit(Vector3.Zero);

            ClassicAssert.AreEqual(1f, box.AxisX.X, 1e-5f);
            ClassicAssert.AreEqual(1f, box.AxisY.Y, 1e-5f);
            ClassicAssert.AreEqual(1f, box.AxisZ.Z, 1e-5f);
        }

        [Test]
        public void Une_Rotation_De_90_Degres_Echange_X_Et_Z()
        {
            var box = Unit(Vector3.Zero, 90f);

            // Rotation autour de Y : X part vers -Z.
            ClassicAssert.AreEqual(0f, box.AxisX.X, 1e-5f);
            ClassicAssert.AreEqual(-1f, box.AxisX.Z, 1e-5f);
        }

        [Test]
        public void La_Boite_Englobante_Grandit_Quand_La_Boite_Tourne()
        {
            var aligned = Unit(Vector3.Zero);
            var turned = Unit(Vector3.Zero, 45f);

            // A 45 deg, l'emprise passe de 1 a sqrt(2) sur X et Z.
            ClassicAssert.AreEqual(1f, aligned.GetBounds().Size.X, 1e-4f);
            ClassicAssert.AreEqual(MathF.Sqrt(2f), turned.GetBounds().Size.X, 1e-3f);
            ClassicAssert.AreEqual(1f, turned.GetBounds().Size.Y, 1e-4f, "la hauteur ne change pas");
        }

        [Test]
        public void Les_Huit_Sommets_Sont_A_La_Bonne_Distance()
        {
            var box = Unit(new Vector3(3f, 4f, 5f), 37f);
            Span<Vector3> corners = stackalloc Vector3[8];
            box.GetCorners(corners);

            // La demi-diagonale d'un cube d'arete 1 vaut sqrt(3)/2.
            float expected = MathF.Sqrt(3f) * 0.5f;

            foreach (var c in corners)
                ClassicAssert.AreEqual(expected, (c - box.Center).Length(), 1e-4f);
        }

        [Test]
        public void Aller_Retour_Local_Monde()
        {
            var box = Unit(new Vector3(2f, -1f, 7f), 63f);
            var point = new Vector3(1.3f, 2.7f, -4.1f);

            Vector3 back = box.ToWorld(box.ToLocal(point));

            ClassicAssert.AreEqual(point.X, back.X, 1e-4f);
            ClassicAssert.AreEqual(point.Y, back.Y, 1e-4f);
            ClassicAssert.AreEqual(point.Z, back.Z, 1e-4f);
        }

        [Test]
        public void Le_Point_Le_Plus_Proche_Est_Sur_La_Boite()
        {
            var box = Unit(Vector3.Zero, 30f);

            Vector3 outside = new Vector3(10f, 0f, 0f);
            Vector3 closest = box.ClosestPoint(outside);

            ClassicAssert.Less((closest - box.Center).Length(), 0.9f, "le point doit etre sur la boite");
            ClassicAssert.IsTrue(box.Contains(box.ClosestPoint(outside) - Vector3.Normalize(outside) * 1e-3f));
        }

        // --- SAT : cas simples --------------------------------------------------

        [Test]
        public void Deux_Boites_Eloignees_Ne_Se_Touchent_Pas()
        {
            var a = Unit(Vector3.Zero);
            var b = Unit(new Vector3(5f, 0f, 0f));

            ClassicAssert.IsFalse(ObbCollision.Intersect(a, b).Touching);
        }

        [Test]
        public void Deux_Boites_Superposees_Se_Touchent()
        {
            var a = Unit(Vector3.Zero);
            var b = Unit(new Vector3(0.3f, 0f, 0f));

            ObbContact c = ObbCollision.Intersect(a, b);

            ClassicAssert.IsTrue(c.Touching);
            ClassicAssert.AreEqual(0.7f, c.Depth, 1e-4f, "1 - 0.3 de recouvrement sur X");
            ClassicAssert.AreEqual(1f, c.Normal.X, 1e-4f, "la normale doit pointer de A vers B");
        }

        [Test]
        public void La_Normale_Pointe_Toujours_De_A_Vers_B()
        {
            var a = Unit(Vector3.Zero);
            var left = Unit(new Vector3(-0.3f, 0f, 0f));

            ObbContact c = ObbCollision.Intersect(a, left);

            ClassicAssert.IsTrue(c.Touching);
            ClassicAssert.AreEqual(-1f, c.Normal.X, 1e-4f);
        }

        [Test]
        public void La_Normale_Suit_Laxe_De_Moindre_Penetration()
        {
            // Fort recouvrement sur X, faible sur Y : la separation doit se
            // faire sur Y, la moins couteuse.
            var a = new Obb(Vector3.Zero, new Vector3(2f, 0.5f, 2f), Quaternion.Identity);
            var b = new Obb(new Vector3(0.2f, 0.9f, 0.1f), new Vector3(2f, 0.5f, 2f), Quaternion.Identity);

            ObbContact c = ObbCollision.Intersect(a, b);

            ClassicAssert.IsTrue(c.Touching);
            ClassicAssert.AreEqual(1f, c.Normal.Y, 1e-3f, "on separe verticalement");
            ClassicAssert.AreEqual(0.1f, c.Depth, 1e-3f);
        }

        // --- SAT : ce qu'une AABB ne sait pas faire --------------------------------

        [Test]
        public void Une_Boite_Tournee_A_45_Degres_Ne_Touche_Plus_Sa_Voisine()
        {
            // A droit a l'origine, B tournee a 45 deg et placee en diagonale.
            //
            // B tournee presente sa diagonale : son englobante fait 1.414 de
            // cote et deborde jusqu'a l'englobante de A. Mais son ARETE la
            // plus proche ne s'avance que de 0.5 vers A, qui lui tend sa
            // diagonale (0.707). Total 1.207, pour une distance de 1.273 :
            // elles ne se touchent PAS.
            //
            // C'est exactement le cas ou une AABB se trompe.
            var a = Unit(Vector3.Zero);
            var b = Unit(new Vector3(0.9f, 0f, 0.9f), 45f);

            ClassicAssert.IsTrue(a.GetBounds().Intersects(b.GetBounds()),
                "les boites englobantes se chevauchent : une AABB dirait 'collision'");

            ClassicAssert.IsFalse(ObbCollision.Intersect(a, b).Touching,
                "le theoreme des axes doit voir qu'elles ne se touchent PAS");
        }

        [Test]
        public void Le_Contact_Arete_Contre_Arete_Est_Detecte()
        {
            // LE cas que les 6 axes propres ne suffisent pas a trancher, et
            // qui exige les 9 produits vectoriels.
            var a = new Obb(Vector3.Zero, new Vector3(0.5f),
                            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f));

            var b = new Obb(new Vector3(0.9f, 0f, 0f), new Vector3(0.5f),
                            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f));

            ObbContact c = ObbCollision.Intersect(a, b);

            ClassicAssert.IsTrue(c.Touching);
            ClassicAssert.Greater(c.Depth, 0f);
            ClassicAssert.AreEqual(1f, c.Normal.Length(), 1e-4f, "la normale doit rester unitaire");
        }

        [Test]
        public void La_Normale_Est_Toujours_Unitaire()
        {
            // Un axe croise non normalise donnerait une profondeur absurde :
            // c'est le piege principal de cette implementation.
            var rng = new Random(12345);

            for (int i = 0; i < 300; i++)
            {
                Quaternion qa = RandomRotation(rng);
                Quaternion qb = RandomRotation(rng);

                var a = new Obb(Vector3.Zero, new Vector3(0.5f), qa);
                var b = new Obb(new Vector3(
                    (float)rng.NextDouble() * 1.4f - 0.7f,
                    (float)rng.NextDouble() * 1.4f - 0.7f,
                    (float)rng.NextDouble() * 1.4f - 0.7f), new Vector3(0.5f), qb);

                ObbContact c = ObbCollision.Intersect(a, b);
                if (!c.Touching)
                    continue;

                ClassicAssert.AreEqual(1f, c.Normal.Length(), 1e-3f, $"normale non unitaire au tirage {i}");
                ClassicAssert.Greater(c.Depth, 0f, $"profondeur nulle au tirage {i}");
                ClassicAssert.Less(c.Depth, 2f, $"profondeur aberrante au tirage {i}");
            }
        }

        [Test]
        public void Separer_Les_Boites_Les_Rend_Disjointes()
        {
            // Verification de bout en bout : si on ecarte B de Normal * Depth,
            // elles ne doivent plus se toucher. C'est ce que fera le solveur.
            var rng = new Random(999);
            int tested = 0;

            for (int i = 0; i < 300; i++)
            {
                var a = new Obb(Vector3.Zero, new Vector3(0.5f), RandomRotation(rng));
                var b = new Obb(new Vector3(
                    (float)rng.NextDouble() * 1.2f - 0.6f,
                    (float)rng.NextDouble() * 1.2f - 0.6f,
                    (float)rng.NextDouble() * 1.2f - 0.6f), new Vector3(0.5f), RandomRotation(rng));

                ObbContact c = ObbCollision.Intersect(a, b);
                if (!c.Touching)
                    continue;

                // On ecarte d'un cheveu de plus que la penetration.
                Obb separated = b.Translated(c.Normal * (c.Depth + 1e-3f));

                ClassicAssert.IsFalse(ObbCollision.Intersect(a, separated).Touching,
                    $"toujours en contact apres separation, tirage {i}");
                tested++;
            }

            ClassicAssert.Greater(tested, 30, "l'echantillon doit contenir de vrais contacts");
        }

        // --- Rayon ---------------------------------------------------------------

        [Test]
        public void Un_Rayon_Touche_Une_Boite_Tournee()
        {
            var box = Unit(new Vector3(0f, 0f, 10f), 45f);

            bool hit = ObbCollision.Raycast(box, Vector3.Zero, new Vector3(0f, 0f, 1f), 50f,
                                            out float d, out Vector3 n);

            ClassicAssert.IsTrue(hit);

            // A 45 deg, la face avant du cube d'arete 1 avance jusqu'a
            // 10 - sqrt(2)/2 environ.
            ClassicAssert.AreEqual(10f - MathF.Sqrt(2f) * 0.5f, d, 0.01f);
            ClassicAssert.AreEqual(-1f, Vector3.Dot(n, new Vector3(0f, 0f, 1f)), 0.71f,
                "la normale s'oppose au rayon");
        }

        [Test]
        public void Un_Rayon_Qui_Passe_A_Cote_Ne_Touche_Pas()
        {
            var box = Unit(new Vector3(0f, 0f, 10f));

            ClassicAssert.IsFalse(ObbCollision.Raycast(box, new Vector3(5f, 0f, 0f),
                new Vector3(0f, 0f, 1f), 50f, out _, out _));
        }

        [Test]
        public void Une_Boite_Sans_Rotation_Se_Comporte_Comme_Une_Aabb()
        {
            var aabb = Aabb.FromCenterSize(new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f));
            var obb = Obb.FromAabb(aabb);

            ClassicAssert.AreEqual(aabb.Center.X, obb.Center.X, 1e-5f);
            ClassicAssert.AreEqual(aabb.Extents.Y, obb.HalfExtents.Y, 1e-5f);

            Aabb bounds = obb.GetBounds();
            ClassicAssert.AreEqual(aabb.Min.X, bounds.Min.X, 1e-4f);
            ClassicAssert.AreEqual(aabb.Max.Z, bounds.Max.Z, 1e-4f);
        }

        private static Quaternion RandomRotation(Random rng)
        {
            var axis = new Vector3(
                (float)rng.NextDouble() * 2f - 1f,
                (float)rng.NextDouble() * 2f - 1f,
                (float)rng.NextDouble() * 2f - 1f);

            if (axis.LengthSquared() < 1e-6f)
                axis = Vector3.UnitY;

            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis),
                                                  (float)rng.NextDouble() * MathF.Tau);
        }
    }
}
