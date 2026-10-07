// ===========================================================================
//  Tests de l'inertie de rotation et de l'orientation (R3).
//
//  Deux choses se verifient ici, et une seule est evidente.
//
//  L'evidente : les formules d'inertie d'une boite.
//
//  L'autre : le SENS de composition des quaternions. Une rotation exprimee
//  dans le monde et une rotation exprimee dans le repere du corps donnent le
//  meme resultat tant que le corps est droit - et divergent des qu'il est
//  incline. C'est le genre d'erreur qui passe tous les tests simples et qui
//  se manifeste trois semaines plus tard par "les caisses tournent bizarre".
//  Le test Une_Rotation_Du_Monde_Ne_Suit_Pas_Le_Corps est la pour ca.
// ===========================================================================

using System;
using System.Numerics;
using NUnit.Framework;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class InertiaTests
    {
        private const float Deg = MathF.PI / 180f;

        private static RigidBody Crate(Vector3 size, float mass = 20f)
            => new RigidBody { Size = size, Mass = mass };

        // --- Le tenseur d'inertie -----------------------------------------------

        [Test]
        public void Un_Cube_Oppose_La_Meme_Inertie_Sur_Les_Trois_Axes()
        {
            // Cube de 1 m et 20 kg : I = m(a2 + a2)/12 = 20/6 = 3,3333.
            Vector3 inverse = Inertia.BoxInverse(Vector3.One, 20f);

            float expected = 1f / (20f / 6f);

            ClassicAssert.AreEqual(expected, inverse.X, 1e-5f);
            ClassicAssert.AreEqual(expected, inverse.Y, 1e-5f);
            ClassicAssert.AreEqual(expected, inverse.Z, 1e-5f);
        }

        [Test]
        public void Une_Barre_Se_Lance_Bien_Plus_Facilement_Autour_De_Sa_Longueur()
        {
            // Barre de 4 m allongee sur X, section de 20 cm. La faire tourner
            // sur elle-meme ne demande presque rien ; la faire tournoyer en
            // travers demande beaucoup.
            Vector3 inverse = Inertia.BoxInverse(new Vector3(4f, 0.2f, 0.2f), 20f);

            ClassicAssert.Greater(inverse.X, inverse.Y * 100f,
                "tourner autour de la longueur doit etre au moins cent fois plus facile");
            ClassicAssert.AreEqual(inverse.Y, inverse.Z, 1e-5f, "section carree : Y et Z identiques");
        }

        [Test]
        public void Un_Corps_Statique_Ne_Peut_Pas_Tourner()
        {
            var wall = new RigidBody { Size = new Vector3(4f, 3f, 0.5f), Mass = 500f, IsStatic = true };

            ClassicAssert.AreEqual(Vector3.Zero, wall.InverseInertiaLocal);
            ClassicAssert.AreEqual(0f, wall.InverseMass);
        }

        [Test]
        public void Une_Masse_Nulle_Ne_Donne_Pas_Une_Inertie_Infinie()
        {
            // Zero plutot que l'infini : une division par zero contaminerait
            // toute la simulation avec des NaN.
            ClassicAssert.AreEqual(Vector3.Zero, Inertia.BoxInverse(Vector3.One, 0f));
            ClassicAssert.AreEqual(Vector3.Zero, Inertia.BoxInverse(Vector3.One, -5f));
        }

        [Test]
        public void Une_Boite_Aplatie_En_Segment_Ne_Tourne_Pas_Autour_De_Ce_Segment()
        {
            // Boite reduite a un fil le long de X : son inertie autour de X
            // est nulle, donc son inverse serait infini. On rend zero.
            Vector3 inverse = Inertia.BoxInverse(new Vector3(2f, 0f, 0f), 10f);

            ClassicAssert.AreEqual(0f, inverse.X, 1e-9f);
            ClassicAssert.Greater(inverse.Y, 0f);
        }

        // --- Le tenseur suit le corps -------------------------------------------

        [Test]
        public void Le_Tenseur_Tourne_Avec_Le_Corps()
        {
            // Barre couchee sur X : facile a faire tourner autour de X,
            // difficile autour de Z. On la fait pivoter d'un quart de tour
            // autour de Y : sa longueur pointe maintenant selon Z, et les
            // deux difficultes doivent avoir echange leur axe.
            var bar = Crate(new Vector3(4f, 0.2f, 0.2f));

            Vector3 easyBefore = bar.ApplyInverseInertia(Vector3.UnitX);
            Vector3 hardBefore = bar.ApplyInverseInertia(Vector3.UnitZ);

            ClassicAssert.Greater(easyBefore.Length(), hardBefore.Length() * 100f);

            bar.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 90f * Deg);

            Vector3 easyAfter = bar.ApplyInverseInertia(Vector3.UnitZ);
            Vector3 hardAfter = bar.ApplyInverseInertia(Vector3.UnitX);

            ClassicAssert.Greater(easyAfter.Length(), hardAfter.Length() * 100f,
                "apres le quart de tour, c'est l'axe Z du monde qui est devenu facile");

            // Et l'inertie ne se cree pas en tournant : meme valeur, autre axe.
            ClassicAssert.AreEqual(easyBefore.Length(), easyAfter.Length(), 1e-4f);
        }

        [Test]
        public void Sur_Un_Axe_Propre_Le_Resultat_Reste_Sur_Cet_Axe()
        {
            // Appliquer un moment le long d'un axe principal ne doit pas
            // reveiller de rotation sur les deux autres. Si le produit
            // R * I(-1) * Rt etait faux, un terme croise apparaitrait ici.
            var bar = Crate(new Vector3(4f, 0.2f, 0.6f));
            bar.Orientation = Quaternion.CreateFromAxisAngle(
                Vector3.Normalize(new Vector3(1f, 2f, -3f)), 37f * Deg);

            Vector3 axis = Vector3.Transform(Vector3.UnitY, bar.Orientation);
            Vector3 result = bar.ApplyInverseInertia(axis);

            Vector3 parallel = axis * Vector3.Dot(result, axis);

            ClassicAssert.Less((result - parallel).Length(), 1e-5f * result.Length() + 1e-6f,
                "aucune composante hors de l'axe sollicite");
        }

        [Test]
        public void Un_Cube_Reagit_Pareil_Quelle_Que_Soit_Son_Orientation()
        {
            // L'inertie d'un cube est isotrope : la tourner ne change rien.
            // C'est le controle qui detecte une rotation appliquee de travers.
            var cube = Crate(Vector3.One);
            Vector3 reference = cube.ApplyInverseInertia(new Vector3(1f, 2f, 3f));

            var rng = new Random(97);
            for (int i = 0; i < 50; i++)
            {
                cube.Orientation = RandomRotation(rng);
                Vector3 turned = cube.ApplyInverseInertia(new Vector3(1f, 2f, 3f));

                ClassicAssert.Less((turned - reference).Length(), 1e-4f, $"tirage {i}");
            }
        }

        // --- Integration de l'orientation ----------------------------------------

        [Test]
        public void Une_Rotation_Du_Monde_Ne_Suit_Pas_Le_Corps()
        {
            // LE test qui distingue les deux sens de composition.
            //
            // Le corps est deja bascule d'un quart de tour autour de Z. On le
            // fait tourner d'un quart de tour autour de l'axe Y DU MONDE.
            //
            // Son axe Z propre pointe vers le Z du monde ; une rotation
            // autour du Y du monde doit l'emmener vers +X.
            //
            // Si la composition se faisait a droite, le corps tournerait
            // autour de son PROPRE Y - qui pointe vers le -X du monde - et
            // l'axe Z finirait vers +Y. Les deux resultats sont a 90 degres
            // l'un de l'autre : impossible de confondre.
            Quaternion orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 90f * Deg);
            var spin = new Vector3(0f, MathF.PI / 2f, 0f);   // un quart de tour par seconde

            for (int i = 0; i < 1000; i++)
                orientation = Inertia.Integrate(orientation, spin, 1f / 1000f);

            Vector3 localZ = Vector3.Transform(Vector3.UnitZ, orientation);

            ClassicAssert.AreEqual(1f, localZ.X, 1e-3f, "l'axe Z du corps doit finir sur le +X du monde");
            ClassicAssert.AreEqual(0f, localZ.Y, 1e-3f);
        }

        [Test]
        public void Un_Tour_Complet_Revient_Exactement_Au_Depart()
        {
            // La formule linearisee classique echoue ici : elle coupe le
            // virage et n'arrive jamais tout a fait au bout du tour.
            Quaternion start = Quaternion.CreateFromAxisAngle(
                Vector3.Normalize(new Vector3(1f, 1f, 0f)), 20f * Deg);

            var spin = new Vector3(0f, MathF.Tau, 0f);   // un tour par seconde
            Quaternion orientation = start;

            for (int i = 0; i < 64; i++)
                orientation = Inertia.Integrate(orientation, spin, 1f / 64f);

            // Un quaternion et son oppose decrivent la meme orientation :
            // on compare donc ce qu'ils font a un vecteur, pas leurs composantes.
            var probe = new Vector3(0.3f, -0.7f, 0.5f);
            Vector3 before = Vector3.Transform(probe, start);
            Vector3 after = Vector3.Transform(probe, orientation);

            ClassicAssert.Less((after - before).Length(), 1e-4f, "le tour doit boucler exactement");
        }

        [Test]
        public void Le_Quaternion_Ne_Derive_Pas_Sur_Cent_Mille_Pas()
        {
            // LE piege annonce. Un quaternion qui perd sa norme met les
            // vecteurs a l'echelle de son CARRE : la caisse grandit ou
            // retrecit sans que rien ne le demande.
            var rng = new Random(31337);
            Quaternion orientation = RandomRotation(rng);
            var spin = new Vector3(2.7f, -5.1f, 3.3f);

            for (int i = 0; i < 100_000; i++)
                orientation = Inertia.Integrate(orientation, spin, 1f / 64f);

            ClassicAssert.AreEqual(1f, orientation.Length(), 1e-5f, "le quaternion a derive");

            // Consequence concrete : une longueur ne doit pas bouger.
            Vector3 turned = Vector3.Transform(Vector3.UnitX, orientation);
            ClassicAssert.AreEqual(1f, turned.Length(), 1e-5f, "le corps se deforme");
        }

        [Test]
        public void Sans_Vitesse_Angulaire_Rien_Ne_Bouge()
        {
            Quaternion start = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 42f * Deg);
            Quaternion after = Inertia.Integrate(start, Vector3.Zero, 1f / 64f);

            ClassicAssert.AreEqual(start, after);
        }

        [Test]
        public void Sans_Couple_La_Vitesse_Angulaire_Ne_Change_Pas()
        {
            // Choix assume : pas de terme gyroscopique. Un corps libre garde
            // sa vitesse angulaire, il ne culbute pas comme une raquette.
            var bar = Crate(new Vector3(4f, 0.2f, 1f));
            bar.AngularVelocity = new Vector3(1f, 0.05f, 0f);
            Vector3 before = bar.AngularVelocity;

            for (int i = 0; i < 640; i++)
                bar.IntegrateOrientation(1f / 64f);

            ClassicAssert.AreEqual(before, bar.AngularVelocity);
        }

        [Test]
        public void Un_Corps_Statique_Ne_Tourne_Jamais()
        {
            var wall = new RigidBody { Size = Vector3.One, IsStatic = true };
            wall.AngularVelocity = new Vector3(0f, 10f, 0f);

            wall.IntegrateOrientation(1f / 64f);

            ClassicAssert.AreEqual(Quaternion.Identity, wall.Orientation);
        }

        // --- Impulsions et bras de levier -----------------------------------------

        [Test]
        public void Une_Impulsion_En_Plein_Centre_Ne_Fait_Pas_Tourner()
        {
            var crate = Crate(Vector3.One);
            crate.Position = new Vector3(3f, 1f, -2f);

            crate.ApplyImpulseAt(new Vector3(10f, 0f, 0f), crate.Position);

            ClassicAssert.AreEqual(Vector3.Zero, crate.AngularVelocity);
            ClassicAssert.AreEqual(0.5f, crate.Velocity.X, 1e-5f, "10 N.s sur 20 kg");
        }

        [Test]
        public void Une_Impulsion_Sur_Le_Coin_Fait_Tourner_Dans_Le_Bon_Sens()
        {
            // Caisse frappee vers +X sur son coin du DESSUS. Le bras de
            // levier va du centre vers le haut : r x J = (+Y) x (+X) = -Z.
            // Elle doit donc partir en rotation negative autour de Z.
            var crate = Crate(Vector3.One);
            Vector3 top = crate.Position + new Vector3(0f, 0.5f, 0f);

            crate.ApplyImpulseAt(new Vector3(10f, 0f, 0f), top);

            ClassicAssert.Less(crate.AngularVelocity.Z, -0.1f, "le haut part en avant, donc rotation en -Z");
            ClassicAssert.AreEqual(0f, crate.AngularVelocity.X, 1e-5f);
            ClassicAssert.AreEqual(0f, crate.AngularVelocity.Y, 1e-5f);

            // La poussee reste entiere : la rotation ne la vole pas.
            ClassicAssert.AreEqual(0.5f, crate.Velocity.X, 1e-5f);
        }

        [Test]
        public void Deux_Impulsions_Opposees_Font_Un_Couple_Pur()
        {
            // Poussee vers +X en haut, vers -X en bas : la caisse tourne sur
            // place, sans partir dans aucune direction.
            var crate = Crate(Vector3.One);

            crate.ApplyImpulseAt(new Vector3(10f, 0f, 0f), crate.Position + new Vector3(0f, 0.5f, 0f));
            crate.ApplyImpulseAt(new Vector3(-10f, 0f, 0f), crate.Position - new Vector3(0f, 0.5f, 0f));

            ClassicAssert.Less(crate.Velocity.Length(), 1e-5f, "un couple pur ne deplace rien");
            ClassicAssert.Less(crate.AngularVelocity.Z, -0.2f);
        }

        [Test]
        public void Une_Impulsion_Reveille_Le_Corps()
        {
            var crate = Crate(Vector3.One);
            crate.Sleeping = true;
            crate.StillTime = 5f;

            crate.ApplyImpulseAt(new Vector3(0f, 0f, 3f), crate.Position + Vector3.UnitX);

            ClassicAssert.IsFalse(crate.Sleeping);
            ClassicAssert.AreEqual(0f, crate.StillTime);
        }

        [Test]
        public void Un_Corps_Statique_Encaisse_Sans_Bouger()
        {
            var wall = new RigidBody { Size = Vector3.One, Mass = 500f, IsStatic = true };

            wall.ApplyImpulseAt(new Vector3(1000f, 0f, 0f), wall.Position + Vector3.UnitY);
            wall.ApplyAngularImpulse(new Vector3(0f, 500f, 0f));

            ClassicAssert.AreEqual(Vector3.Zero, wall.Velocity);
            ClassicAssert.AreEqual(Vector3.Zero, wall.AngularVelocity);
        }

        [Test]
        public void Une_Barre_Tourne_Plus_Vite_Frappee_Dans_Le_Sens_Facile()
        {
            // Meme impulsion, meme bras de levier, deux axes differents :
            // c'est toute la difference entre la masse et l'inertie.
            var flat = Crate(new Vector3(4f, 0.2f, 0.2f));
            var edge = Crate(new Vector3(4f, 0.2f, 0.2f));

            // Autour de X (la longueur) : facile.
            flat.ApplyImpulseAt(new Vector3(0f, 0f, 5f), flat.Position + new Vector3(0f, 0.1f, 0f));

            // Autour de Z (en travers) : difficile.
            edge.ApplyImpulseAt(new Vector3(0f, 5f, 0f), edge.Position + new Vector3(2f, 0f, 0f));

            ClassicAssert.Greater(MathF.Abs(flat.AngularVelocity.X),
                                  MathF.Abs(edge.AngularVelocity.Z) * 10f);
        }

        // --- Vitesse d'un point ----------------------------------------------------

        [Test]
        public void Le_Centre_Va_A_La_Vitesse_Du_Corps()
        {
            var crate = Crate(Vector3.One);
            crate.Position = new Vector3(1f, 2f, 3f);
            crate.Velocity = new Vector3(4f, 0f, 0f);
            crate.AngularVelocity = new Vector3(0f, 7f, 0f);

            ClassicAssert.AreEqual(crate.Velocity, crate.GetVelocityAt(crate.Position));
        }

        [Test]
        public void Un_Point_Excentre_Va_Plus_Vite_Que_Le_Centre()
        {
            // Caisse immobile qui tourne autour de Y a 2 rad/s. Un point a
            // 1 m sur +X se deplace a 2 m/s, et vers -Z : w x r = (+Y) x (+X).
            var crate = Crate(Vector3.One);
            crate.AngularVelocity = new Vector3(0f, 2f, 0f);

            Vector3 velocity = crate.GetVelocityAt(crate.Position + Vector3.UnitX);

            ClassicAssert.AreEqual(2f, velocity.Length(), 1e-5f);
            ClassicAssert.AreEqual(-2f, velocity.Z, 1e-5f);
        }

        [Test]
        public void Deux_Points_Opposes_Vont_En_Sens_Contraire()
        {
            var crate = Crate(Vector3.One);
            crate.AngularVelocity = new Vector3(0f, 3f, 0f);

            Vector3 right = crate.GetVelocityAt(crate.Position + Vector3.UnitX);
            Vector3 left = crate.GetVelocityAt(crate.Position - Vector3.UnitX);

            ClassicAssert.Less((right + left).Length(), 1e-5f);
        }

        // --- Raccord avec le reste --------------------------------------------------

        [Test]
        public void Le_Corps_Rend_Une_Boite_Orientee_Coherente()
        {
            var crate = Crate(new Vector3(2f, 1f, 3f));
            crate.Position = new Vector3(5f, 6f, 7f);
            crate.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 30f * Deg);

            Obb box = crate.GetObb();

            ClassicAssert.AreEqual(crate.Position, box.Center);
            ClassicAssert.AreEqual(1f, box.HalfExtents.X, 1e-5f, "demi-dimensions, pas dimensions");
            ClassicAssert.AreEqual(1.5f, box.HalfExtents.Z, 1e-5f);

            // Et l'orientation est bien celle du corps.
            Vector3 axis = box.AxisX;
            ClassicAssert.AreEqual(MathF.Cos(30f * Deg), axis.X, 1e-4f);
        }

        [Test]
        public void Un_Corps_Neuf_Est_Droit_Et_Immobile()
        {
            var crate = new RigidBody();

            ClassicAssert.AreEqual(Quaternion.Identity, crate.Orientation);
            ClassicAssert.AreEqual(Vector3.Zero, crate.AngularVelocity);
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
