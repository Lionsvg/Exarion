// ===========================================================================
//  Tests des points de contact (R2).
//
//  Ce qui compte ici n'est pas "y a-t-il collision" - R1 s'en charge - mais
//  la FORME de l'appui. Une caisse posee a plat doit rendre quatre points
//  aux quatre coins, et leur centre doit tomber sous son centre de masse :
//  c'est la condition pour qu'elle reste posee au lieu de basculer en R4.
//
//  Un seul point de contact suffirait a separer les corps, et la pile
//  s'ecroulerait quand meme. C'est tout l'objet de cette etape.
// ===========================================================================

using System;
using System.Numerics;
using NUnit.Framework;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class ObbManifoldTests
    {
        private static Obb Box(Vector3 center, Vector3 size, float yawDegrees = 0f)
            => Obb.FromSize(center, size,
                            Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f));

        /// <summary>Sol de 8 m de cote, dont le DESSUS est exactement a y = 0.</summary>
        private static Obb Floor(float halfWidth = 4f)
            => Box(new Vector3(0f, -0.5f, 0f), new Vector3(halfWidth * 2f, 1f, halfWidth * 2f));

        /// <summary>Caisse de 1 m enfoncee de 2 cm dans le sol.</summary>
        private static Obb Crate(float x = 0f, float yaw = 0f)
            => Box(new Vector3(x, 0.48f, 0f), Vector3.One, yaw);

        // --- Cas nominal : la caisse posee a plat -----------------------------

        [Test]
        public void Une_Caisse_Posee_A_Plat_Donne_Quatre_Points()
        {
            Obb floor = Floor(), crate = Crate();
            ObbContact contact = ObbCollision.Intersect(floor, crate);

            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];
            int count = ObbManifold.Build(floor, crate, contact, points);

            ClassicAssert.AreEqual(ContactFeature.FaceA, contact.Feature, "le sol porte la face de reference");
            ClassicAssert.AreEqual(4, count, "un appui plat, c'est quatre coins - pas un point");
        }

        [Test]
        public void Les_Quatre_Points_Sont_Aux_Coins_De_La_Caisse()
        {
            Obb floor = Floor(), crate = Crate();
            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];
            int count = ObbManifold.Build(floor, crate, ObbCollision.Intersect(floor, crate), points);

            for (int i = 0; i < count; i++)
            {
                ClassicAssert.AreEqual(0.5f, MathF.Abs(points[i].Position.X), 1e-4f);
                ClassicAssert.AreEqual(0.5f, MathF.Abs(points[i].Position.Z), 1e-4f);

                // La face du dessous de la caisse est a 2 cm sous le sol.
                ClassicAssert.AreEqual(-0.02f, points[i].Position.Y, 1e-4f);
                ClassicAssert.AreEqual(0.02f, points[i].Depth, 1e-4f, "meme enfoncement partout");
            }
        }

        [Test]
        public void Le_Centre_Des_Appuis_Tombe_Sous_Le_Centre_De_La_Caisse()
        {
            // LA propriete qui fait tenir une pile. Si le polygone d'appui
            // n'est pas centre sous la caisse, R4 lui appliquera un couple
            // parasite et elle basculera toute seule.
            Obb floor = Floor(), crate = Crate();
            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];
            int count = ObbManifold.Build(floor, crate, ObbCollision.Intersect(floor, crate), points);

            Vector3 centroid = Vector3.Zero;
            for (int i = 0; i < count; i++)
                centroid += points[i].Position;
            centroid /= count;

            ClassicAssert.AreEqual(crate.Center.X, centroid.X, 1e-4f);
            ClassicAssert.AreEqual(crate.Center.Z, centroid.Z, 1e-4f);
        }

        // --- Le decoupage proprement dit ---------------------------------------

        [Test]
        public void Une_Caisse_A_Cheval_Sur_Le_Bord_Est_Rognee_Au_Bord()
        {
            // Sol jusqu'a x = 2, caisse de x = 1,3 a x = 2,3 : la partie qui
            // depasse ne touche rien et ne doit pas produire d'appui.
            Obb floor = Floor(2f);
            Obb crate = Crate(1.8f);

            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];
            int count = ObbManifold.Build(floor, crate, ObbCollision.Intersect(floor, crate), points);

            ClassicAssert.AreEqual(4, count);

            float minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                minX = MathF.Min(minX, points[i].Position.X);
                maxX = MathF.Max(maxX, points[i].Position.X);
            }

            ClassicAssert.AreEqual(1.3f, minX, 1e-4f, "le bord interieur de la caisse");
            ClassicAssert.AreEqual(2.0f, maxX, 1e-4f, "rogne net au bord du sol");
        }

        [Test]
        public void Une_Caisse_Tournee_A_45_Degres_Donne_Un_Octogone()
        {
            // Un carre de 1 m tourne de 45 degres pose sur un carre de 1 m :
            // la zone commune est un octogone. C'est le cas qui justifie
            // huit emplacements et pas quatre.
            Obb pillar = Floor(0.5f);
            Obb crate = Crate(0f, 45f);

            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];
            int count = ObbManifold.Build(pillar, crate, ObbCollision.Intersect(pillar, crate), points);

            ClassicAssert.AreEqual(8, count, "quatre coins coupes, donc huit sommets");

            for (int i = 0; i < count; i++)
            {
                ClassicAssert.LessOrEqual(MathF.Abs(points[i].Position.X), 0.5f + 1e-4f,
                    "aucun appui ne doit deborder du pilier");
                ClassicAssert.LessOrEqual(MathF.Abs(points[i].Position.Z), 0.5f + 1e-4f);
            }
        }

        // --- Contact arete contre arete -----------------------------------------

        [Test]
        public void Un_Contact_Arete_Contre_Arete_Donne_Un_Seul_Point()
        {
            // A tend une arete parallele a Z, B une arete parallele a Y.
            // Deux droites qui se croisent : un seul point de contact, et
            // decouper une face n'aurait aucun sens.
            var a = new Obb(Vector3.Zero, new Vector3(0.5f),
                            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f));

            var b = new Obb(new Vector3(0.9f, 0f, 0f), new Vector3(0.5f),
                            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f));

            ObbContact contact = ObbCollision.Intersect(a, b);
            ClassicAssert.AreEqual(ContactFeature.Edge, contact.Feature);

            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];
            int count = ObbManifold.Build(a, b, contact, points);

            ClassicAssert.AreEqual(1, count);

            // L'arete de A avance jusqu'a x = 0,707 ; celle de B descend
            // jusqu'a x = 0,9 - 0,707 = 0,193. Le contact est a mi-chemin.
            ClassicAssert.AreEqual(0.45f, points[0].Position.X, 1e-3f);
            ClassicAssert.AreEqual(0f, points[0].Position.Y, 1e-3f);
            ClassicAssert.AreEqual(0f, points[0].Position.Z, 1e-3f);
            ClassicAssert.AreEqual(contact.Depth, points[0].Depth, 1e-4f);
        }

        // --- Stabilite du choix de la face --------------------------------------

        [Test]
        public void Sur_Egalite_Parfaite_La_Reference_Est_Toujours_Celle_De_A()
        {
            // Deux caisses identiques a plat : l'axe vertical de A et celui
            // de B donnent EXACTEMENT la meme profondeur. Sans regle de
            // priorite, le bruit de calcul choisirait au hasard, et le
            // polygone de contact sauterait d'un corps a l'autre.
            var lower = Box(new Vector3(0f, 0f, 0f), Vector3.One);
            var upper = Box(new Vector3(0f, 0.98f, 0f), Vector3.One);

            ObbContact contact = ObbCollision.Intersect(lower, upper);

            ClassicAssert.AreEqual(ContactFeature.FaceA, contact.Feature);
            ClassicAssert.AreEqual(1f, contact.Normal.Y, 1e-5f);
        }

        [Test]
        public void Un_Bruit_Infime_Ne_Fait_Pas_Basculer_Le_Manifold()
        {
            // Meme scene rejouee avec des orientations imperceptiblement
            // differentes : le contact doit rester le meme. Un manifold qui
            // change de forme d'un pas a l'autre, c'est une pile qui vibre
            // et ne s'endort jamais.
            var rng = new Random(2026);
            Obb floor = Floor();
            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];

            for (int i = 0; i < 200; i++)
            {
                // +/- 0,05 degre : l'ordre de grandeur du bruit de calcul.
                float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.05f * MathF.PI / 180f;

                var crate = new Obb(new Vector3(0f, 0.48f, 0f), new Vector3(0.5f),
                                    Quaternion.CreateFromAxisAngle(Vector3.UnitX, noise));

                ObbContact contact = ObbCollision.Intersect(floor, crate);
                int count = ObbManifold.Build(floor, crate, contact, points);

                ClassicAssert.AreEqual(ContactFeature.FaceA, contact.Feature, $"bascule au tirage {i}");
                ClassicAssert.AreEqual(4, count, $"le nombre d'appuis a change au tirage {i}");
                ClassicAssert.AreEqual(1f, contact.Normal.Y, 1e-3f, $"normale deviee au tirage {i}");
            }
        }

        [Test]
        public void Echanger_Les_Deux_Corps_Donne_Le_Meme_Appui()
        {
            // Le contact est une propriete de la paire, pas de l'ordre des
            // arguments. Seule la normale doit s'inverser.
            Obb floor = Floor(), crate = Crate();

            Span<ContactPoint> direct = stackalloc ContactPoint[ObbManifold.MaxPoints];
            Span<ContactPoint> reverse = stackalloc ContactPoint[ObbManifold.MaxPoints];

            ObbContact contactDirect = ObbCollision.Intersect(floor, crate);
            ObbContact contactReverse = ObbCollision.Intersect(crate, floor);

            int countDirect = ObbManifold.Build(floor, crate, contactDirect, direct);
            int countReverse = ObbManifold.Build(crate, floor, contactReverse, reverse);

            ClassicAssert.AreEqual(countDirect, countReverse);
            ClassicAssert.AreEqual(-contactDirect.Normal.Y, contactReverse.Normal.Y, 1e-5f);

            // Les points ne sont pas a la meme hauteur - ils sont pris sur la
            // face du corps INCIDENT, qui change - mais l'aplomb est le meme.
            ClassicAssert.AreEqual(HorizontalCentroid(direct, countDirect).X,
                                   HorizontalCentroid(reverse, countReverse).X, 1e-4f);
            ClassicAssert.AreEqual(HorizontalCentroid(direct, countDirect).Z,
                                   HorizontalCentroid(reverse, countReverse).Z, 1e-4f);
        }

        // --- Cas limites ---------------------------------------------------------

        [Test]
        public void Sans_Contact_Aucun_Point()
        {
            Obb floor = Floor();
            var far = Box(new Vector3(0f, 5f, 0f), Vector3.One);

            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];
            ObbContact contact = ObbCollision.Intersect(floor, far);

            ClassicAssert.IsFalse(contact.Touching);
            ClassicAssert.AreEqual(0, ObbManifold.Build(floor, far, contact, points));
        }

        [Test]
        public void Un_Tampon_Trop_Petit_Est_Refuse()
        {
            Obb floor = Floor(), crate = Crate();
            ObbContact contact = ObbCollision.Intersect(floor, crate);

            // Mieux vaut un refus net qu'un polygone tronque en silence.
            Assert.Throws<ArgumentException>(() =>
            {
                Span<ContactPoint> tooSmall = stackalloc ContactPoint[4];
                ObbManifold.Build(floor, crate, contact, tooSmall);
            });
        }

        // --- Tirages aleatoires ---------------------------------------------------

        [Test]
        public void Tout_Contact_Produit_Entre_Un_Et_Huit_Points()
        {
            var rng = new Random(4242);
            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];
            int tested = 0;

            for (int i = 0; i < 500; i++)
            {
                var a = new Obb(Vector3.Zero, new Vector3(0.5f), RandomRotation(rng));
                var b = new Obb(RandomOffset(rng, 1.2f), new Vector3(0.5f), RandomRotation(rng));

                ObbContact contact = ObbCollision.Intersect(a, b);
                if (!contact.Touching)
                    continue;

                int count = ObbManifold.Build(a, b, contact, points);

                // Zero point sur un vrai contact serait le pire des defauts :
                // la collision serait ignoree en silence.
                ClassicAssert.GreaterOrEqual(count, 1, $"contact sans appui, tirage {i}");
                ClassicAssert.LessOrEqual(count, ObbManifold.MaxPoints, $"depassement, tirage {i}");
                tested++;
            }

            ClassicAssert.Greater(tested, 50, "l'echantillon doit contenir de vrais contacts");
        }

        [Test]
        public void Aucune_Profondeur_Ne_Depasse_La_Penetration_Mesuree()
        {
            var rng = new Random(777);
            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];

            for (int i = 0; i < 500; i++)
            {
                var a = new Obb(Vector3.Zero, new Vector3(0.5f), RandomRotation(rng));
                var b = new Obb(RandomOffset(rng, 1.2f), new Vector3(0.5f), RandomRotation(rng));

                ObbContact contact = ObbCollision.Intersect(a, b);
                if (!contact.Touching)
                    continue;

                int count = ObbManifold.Build(a, b, contact, points);

                for (int p = 0; p < count; p++)
                {
                    ClassicAssert.GreaterOrEqual(points[p].Depth, 0f, $"profondeur negative, tirage {i}");
                    ClassicAssert.LessOrEqual(points[p].Depth, contact.Depth + 1e-4f,
                        $"un appui plus profond que la penetration mesuree, tirage {i}");
                }
            }
        }

        [Test]
        public void Deux_Appuis_Ne_Sont_Jamais_Confondus()
        {
            // Deux points identiques donneraient deux fois la meme contrainte
            // au solveur, qui corrigerait en double : ca tremble.
            var rng = new Random(31415);
            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];

            for (int i = 0; i < 500; i++)
            {
                var a = new Obb(Vector3.Zero, new Vector3(0.5f), RandomRotation(rng));
                var b = new Obb(RandomOffset(rng, 1.2f), new Vector3(0.5f), RandomRotation(rng));

                ObbContact contact = ObbCollision.Intersect(a, b);
                if (!contact.Touching)
                    continue;

                int count = ObbManifold.Build(a, b, contact, points);

                for (int p = 0; p < count; p++)
                {
                    for (int q = p + 1; q < count; q++)
                    {
                        float distance = (points[p].Position - points[q].Position).Length();
                        ClassicAssert.Greater(distance, 1e-4f, $"appuis confondus, tirage {i}");
                    }
                }
            }
        }

        [Test]
        public void Les_Appuis_Sont_Sur_La_Surface_Des_Deux_Corps()
        {
            // Un point de contact qui flotte dans le vide donnerait un bras
            // de levier faux en R4, donc une rotation inventee.
            //
            // Restreint aux contacts peu profonds : au-dela, la face
            // incidente peut ressortir de l'autre cote du corps de
            // reference, et la notion de "sur la surface" perd son sens.
            var rng = new Random(8080);
            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];
            int tested = 0;

            for (int i = 0; i < 800; i++)
            {
                // Tirage en COQUILLE, pas dans un cube : deux cubes de 1 m se
                // frolent entre 1,0 et 1,73 m d'ecart selon leur orientation.
                // Un tirage uniforme les enfoncerait presque toujours l'un
                // dans l'autre, et ne testerait pas le cas qui nous occupe.
                var a = new Obb(Vector3.Zero, new Vector3(0.5f), RandomRotation(rng));
                var b = new Obb(RandomDirection(rng) * (1f + (float)rng.NextDouble() * 0.5f),
                                new Vector3(0.5f), RandomRotation(rng));

                ObbContact contact = ObbCollision.Intersect(a, b);
                if (!contact.Touching || contact.Depth > 0.25f)
                    continue;

                int count = ObbManifold.Build(a, b, contact, points);

                for (int p = 0; p < count; p++)
                {
                    Vector3 position = points[p].Position;

                    // ClosestPoint rend le point lui-meme s'il est dedans.
                    ClassicAssert.Less((position - a.ClosestPoint(position)).Length(), 2e-3f,
                        $"appui hors du corps A, tirage {i}");
                    ClassicAssert.Less((position - b.ClosestPoint(position)).Length(), 2e-3f,
                        $"appui hors du corps B, tirage {i}");
                }

                tested++;
            }

            ClassicAssert.Greater(tested, 50, "l'echantillon doit contenir de vrais contacts");
        }

        [Test]
        public void Une_Caisse_Legerement_De_Travers_Garde_Un_Vrai_Polygone()
        {
            // Le regime qui compte pour une pile : jamais parfaitement a
            // plat, mais presque. Tant que l'inclinaison reste sous le
            // degre, l'appui doit rester un POLYGONE. S'il tombait a un ou
            // deux points, la caisse pivoterait autour d'eux en R4.
            var rng = new Random(606);
            Obb floor = Floor();
            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];

            for (int i = 0; i < 200; i++)
            {
                float tilt = (float)rng.NextDouble() * 1f * MathF.PI / 180f;
                Quaternion orientation =
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)rng.NextDouble() * MathF.Tau)
                    * Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(
                        (float)rng.NextDouble() * 2f - 1f, 0f,
                        (float)rng.NextDouble() * 2f - 1f)), tilt);

                var crate = new Obb(new Vector3(0f, 0.48f, 0f), new Vector3(0.5f), orientation);

                ObbContact contact = ObbCollision.Intersect(floor, crate);
                int count = ObbManifold.Build(floor, crate, contact, points);

                ClassicAssert.GreaterOrEqual(count, 3,
                    $"appui reduit a {count} point(s) pour une inclinaison de {tilt * 180f / MathF.PI:0.###} deg, tirage {i}");
            }
        }

        [Test]
        public void Aucun_Appui_Ne_Sort_Du_Corps_De_Reference()
        {
            // La garantie la plus forte du module, et le detecteur du filet
            // de securite : un point d'appui hors du corps de reference
            // signifie que le decoupage n'a rien trouve sous la face et
            // qu'on a rendu un point de secours. Ca ne doit pas arriver.
            var rng = new Random(1234);
            Span<ContactPoint> points = stackalloc ContactPoint[ObbManifold.MaxPoints];
            int tested = 0;

            for (int i = 0; i < 2000; i++)
            {
                var a = new Obb(Vector3.Zero, new Vector3(0.5f), RandomRotation(rng));
                var b = new Obb(RandomDirection(rng) * (1f + (float)rng.NextDouble() * 0.5f),
                                new Vector3(0.5f), RandomRotation(rng));

                ObbContact contact = ObbCollision.Intersect(a, b);
                if (!contact.Touching)
                    continue;

                int count = ObbManifold.Build(a, b, contact, points);

                // Face de A : la reference est A. Face de B : c'est B. Arete
                // contre arete : le point est entre les deux, donc dans les
                // deux corps.
                Obb reference = contact.Feature == ContactFeature.FaceB ? b : a;

                for (int p = 0; p < count; p++)
                {
                    Vector3 position = points[p].Position;

                    ClassicAssert.Less((position - reference.ClosestPoint(position)).Length(), 2e-3f,
                        $"appui hors du corps de reference ({contact.Feature}), tirage {i}");
                }

                tested++;
            }

            ClassicAssert.Greater(tested, 500, "l'echantillon doit contenir beaucoup de contacts");
        }

        // --- Outils ----------------------------------------------------------------

        private static Vector3 HorizontalCentroid(ReadOnlySpan<ContactPoint> points, int count)
        {
            Vector3 sum = Vector3.Zero;
            for (int i = 0; i < count; i++)
                sum += points[i].Position;

            return count > 0 ? sum / count : Vector3.Zero;
        }

        private static Vector3 RandomDirection(Random rng)
        {
            Vector3 v;
            do
            {
                v = new Vector3(
                    (float)rng.NextDouble() * 2f - 1f,
                    (float)rng.NextDouble() * 2f - 1f,
                    (float)rng.NextDouble() * 2f - 1f);
            }
            while (v.LengthSquared() < 1e-4f);

            return Vector3.Normalize(v);
        }

        private static Vector3 RandomOffset(Random rng, float span)
            => new Vector3(
                (float)rng.NextDouble() * span - span * 0.5f,
                (float)rng.NextDouble() * span - span * 0.5f,
                (float)rng.NextDouble() * span - span * 0.5f);

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
