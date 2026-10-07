// ===========================================================================
//  Tests du tir.
//
//  Deux exigences distinctes, verifiees separement :
//    - le RECUL doit etre reproductible (competence du joueur)
//    - la DISPERSION doit etre aleatoire mais DETERMINISTE a graine egale
//      (sinon client et serveur ne tirent pas la meme balle)
// ===========================================================================

using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Exarion.Core;
using Exarion.Core.Combat;
using Exarion.Core.Deterministic;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    public class RngTests
    {
        [Test]
        public void La_Meme_Graine_Donne_La_Meme_Suite()
        {
            var a = new Rng(12345);
            var b = new Rng(12345);

            for (int i = 0; i < 100; i++)
                ClassicAssert.AreEqual(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void Deux_Graines_Differentes_Divergent()
        {
            var a = new Rng(1);
            var b = new Rng(2);

            bool different = false;
            for (int i = 0; i < 10 && !different; i++)
                different = a.NextUInt() != b.NextUInt();

            ClassicAssert.IsTrue(different);
        }

        [Test]
        public void La_Graine_Zero_Ne_Bloque_Pas_Le_Generateur()
        {
            // L'etat 0 est un point fixe du xorshift : il faut le neutraliser.
            var r = new Rng(0);

            ClassicAssert.AreNotEqual(0u, r.NextUInt());
            ClassicAssert.AreNotEqual(0u, r.NextUInt());
        }

        [Test]
        public void La_Graine_De_Tir_Est_Reproductible()
        {
            var a = Rng.FromShot(tick: 1234, playerId: 7, shotIndex: 3);
            var b = Rng.FromShot(tick: 1234, playerId: 7, shotIndex: 3);

            ClassicAssert.AreEqual(a.NextUInt(), b.NextUInt(), "meme tir = meme balle");
        }

        [Test]
        public void Deux_Tirs_Distincts_Ont_Des_Graines_Distinctes()
        {
            var a = Rng.FromShot(1000, 1, 0);
            var b = Rng.FromShot(1000, 1, 1);
            var c = Rng.FromShot(1000, 2, 0);
            var d = Rng.FromShot(1001, 1, 0);

            uint va = a.NextUInt(), vb = b.NextUInt(), vc = c.NextUInt(), vd = d.NextUInt();

            ClassicAssert.AreNotEqual(va, vb, "balles differentes");
            ClassicAssert.AreNotEqual(va, vc, "joueurs differents");
            ClassicAssert.AreNotEqual(va, vd, "ticks differents");
        }

        [Test]
        public void Les_Flottants_Restent_Dans_Lintervalle()
        {
            var r = new Rng(999);

            for (int i = 0; i < 5000; i++)
            {
                float v = r.NextFloat();
                ClassicAssert.GreaterOrEqual(v, 0f);
                ClassicAssert.Less(v, 1f);
            }
        }

        [Test]
        public void Les_Points_Restent_Dans_Le_Disque_Unite()
        {
            var r = new Rng(4242);

            for (int i = 0; i < 5000; i++)
                ClassicAssert.LessOrEqual(r.NextInsideUnitCircle().Length(), 1.0001f);
        }

        [Test]
        public void Les_Points_Ne_Sagglutinent_Pas_Au_Centre()
        {
            // Sans racine carree sur le rayon, la moitie des points tomberait
            // dans le disque de rayon 0.5, au lieu du quart attendu.
            var r = new Rng(777);
            int inner = 0;
            const int total = 20000;

            for (int i = 0; i < total; i++)
            {
                if (r.NextInsideUnitCircle().Length() < 0.5f)
                    inner++;
            }

            float ratio = inner / (float)total;
            ClassicAssert.AreEqual(0.25f, ratio, 0.02f, "la repartition doit etre uniforme en surface");
        }
    }

    public class CombatTargetTests
    {
        private static CombatTarget MakeTarget(Vector3 feet, int id = 1)
            => new CombatTarget { Id = id, Position = feet, Height = 1.83f, Width = 0.8f };

        [Test]
        public void La_Tete_Est_Plus_Etroite_Que_Le_Corps()
        {
            var t = MakeTarget(Vector3.Zero);

            float head = t.GetZoneBox(HitZone.Head).Size.X;
            float chest = t.GetZoneBox(HitZone.Chest).Size.X;

            ClassicAssert.Less(head, chest, "un headshot doit se meriter");
            ClassicAssert.AreEqual(chest * CombatTarget.HeadWidthRatio, head, 1e-4f);
        }

        [Test]
        public void Les_Zones_Sempilent_Sans_Trou()
        {
            var t = MakeTarget(Vector3.Zero);

            var legs = t.GetZoneBox(HitZone.Legs);
            var stomach = t.GetZoneBox(HitZone.Stomach);
            var chest = t.GetZoneBox(HitZone.Chest);
            var head = t.GetZoneBox(HitZone.Head);

            ClassicAssert.AreEqual(0f, legs.Min.Y, 1e-4f, "les jambes partent des pieds");
            ClassicAssert.AreEqual(legs.Max.Y, stomach.Min.Y, 1e-4f);
            ClassicAssert.AreEqual(stomach.Max.Y, chest.Min.Y, 1e-4f);
            ClassicAssert.AreEqual(chest.Max.Y, head.Min.Y, 1e-4f);
            ClassicAssert.AreEqual(t.Height, head.Max.Y, 1e-4f, "la tete finit au sommet");
        }

        [Test]
        public void Viser_La_Tete_Touche_La_Tete()
        {
            var t = MakeTarget(new Vector3(0f, 0f, 10f));

            bool hit = t.Raycast(new Vector3(0f, 1.70f, 0f), new Vector3(0f, 0f, 1f), 50f,
                                 out HitZone zone, out float d);

            ClassicAssert.IsTrue(hit);
            ClassicAssert.AreEqual(HitZone.Head, zone);
            // La cible est a z = 10 ; la tete fait 0.36 m de large, donc sa
            // face avant est a 10 - 0.18 = 9.82.
            ClassicAssert.AreEqual(9.82f, d, 0.02f);
        }

        [Test]
        public void Viser_Le_Torse_Les_Jambes_Ou_Le_Ventre_Touche_La_Bonne_Zone()
        {
            var t = MakeTarget(new Vector3(0f, 0f, 10f));
            var origin = new Vector3(0f, 0f, 0f);
            var dir = new Vector3(0f, 0f, 1f);

            t.Raycast(origin + new Vector3(0f, 1.30f, 0f), dir, 50f, out HitZone chest, out _);
            t.Raycast(origin + new Vector3(0f, 0.95f, 0f), dir, 50f, out HitZone stomach, out _);
            t.Raycast(origin + new Vector3(0f, 0.50f, 0f), dir, 50f, out HitZone legs, out _);

            ClassicAssert.AreEqual(HitZone.Chest, chest);
            ClassicAssert.AreEqual(HitZone.Stomach, stomach);
            ClassicAssert.AreEqual(HitZone.Legs, legs);
        }

        [Test]
        public void Tirer_A_Cote_De_La_Tete_Ne_Touche_Rien()
        {
            var t = MakeTarget(new Vector3(0f, 0f, 10f));

            // x = 0.30 : hors de la tete (demi-largeur 0.18) et au-dessus du torse.
            bool hit = t.Raycast(new Vector3(0.30f, 1.75f, 0f), new Vector3(0f, 0f, 1f), 50f, out _, out _);

            ClassicAssert.IsFalse(hit, "la tete est etroite, on doit pouvoir la rater");
        }

        [Test]
        public void Une_Cible_Morte_Nest_Plus_Touchable()
        {
            var t = MakeTarget(new Vector3(0f, 0f, 10f));
            t.ApplyDamage(500);

            ClassicAssert.IsFalse(t.IsAlive);
            ClassicAssert.IsFalse(t.Raycast(new Vector3(0f, 1.3f, 0f), new Vector3(0f, 0f, 1f), 50f, out _, out _));
        }
    }

    public class HitscanTests
    {
        private static readonly Vector3 Forward = new(0f, 0f, 1f);
        private static readonly Vector3 Right = new(1f, 0f, 0f);
        private static readonly Vector3 Up = new(0f, 1f, 0f);

        private static CollisionWorld EmptyWorld() => new();

        private static List<CombatTarget> OneTargetAt(float z) => new()
        {
            new CombatTarget { Id = 2, Position = new Vector3(0f, 0f, z) },
        };

        [Test]
        public void La_Premiere_Balle_Du_Sniper_Part_Parfaitement_Droit()
        {
            // Le sniper a 0 de dispersion immobile, et son premier point de
            // recul est nul : la balle doit partir exactement dans l'axe.
            var rng = new Rng(1);
            Vector3 dir = Hitscan.ComputeDirection(Forward, Right, Up, Arsenal.Sniper,
                                                   StanceState.Still, 0, ref rng);

            ClassicAssert.AreEqual(1f, Vector3.Dot(dir, Forward), 1e-5f);
        }

        [Test]
        public void Tirer_En_Courant_Disperse_Beaucoup_Plus_Quimmobile()
        {
            var w = Arsenal.Rifle;

            float SpreadOf(StanceState stance)
            {
                var rng = new Rng(99);
                float worst = 0f;

                for (int i = 0; i < 200; i++)
                {
                    Vector3 d = Hitscan.ComputeDirection(Forward, Right, Up, w, stance, 0, ref rng);
                    float angle = System.MathF.Acos(System.Math.Clamp(Vector3.Dot(d, Forward), -1f, 1f));
                    if (angle > worst) worst = angle;
                }

                return worst;
            }

            ClassicAssert.Greater(SpreadOf(StanceState.Running), SpreadOf(StanceState.Still) * 10f,
                "courir doit degrader la precision de facon flagrante");
        }

        [Test]
        public void Le_Recul_Fait_Monter_Les_Balles()
        {
            var rng = new Rng(5);
            var w = Arsenal.Rifle;

            Vector3 first = Hitscan.ComputeDirection(Forward, Right, Up, w, StanceState.Crouching, 0, ref rng);
            Vector3 eighth = Hitscan.ComputeDirection(Forward, Right, Up, w, StanceState.Crouching, 8, ref rng);

            ClassicAssert.Greater(eighth.Y, first.Y + 0.03f, "la 9e balle doit partir nettement plus haut");
        }

        [Test]
        public void Le_Meme_Tir_Donne_Toujours_Le_Meme_Resultat()
        {
            // C'EST LE TEST QUI COMPTE POUR LE RESEAU.
            Vector3 Shoot()
            {
                var rng = Rng.FromShot(tick: 500, playerId: 3, shotIndex: 12);
                return Hitscan.ComputeDirection(Forward, Right, Up, Arsenal.Rifle,
                                                StanceState.Running, 4, ref rng);
            }

            Vector3 a = Shoot();
            Vector3 b = Shoot();

            ClassicAssert.AreEqual(a.X, b.X, 1e-9f);
            ClassicAssert.AreEqual(a.Y, b.Y, 1e-9f);
            ClassicAssert.AreEqual(a.Z, b.Z, 1e-9f);
        }

        [Test]
        public void Un_Headshot_De_Fusil_Tue_Une_Cible_A_Cent_Points()
        {
            var targets = OneTargetAt(8f);
            var rng = new Rng(1);

            // On vise la tete : origine a 1.70 m, cible dont la tete commence a 1.60 m.
            var result = Hitscan.Fire(new Vector3(0f, 1.70f, 0f), Forward, Right, Up,
                                      Arsenal.Rifle, StanceState.Crouching, 0, ref rng,
                                      EmptyWorld(), targets, shooterId: 1);

            ClassicAssert.IsTrue(result.HitTarget);
            ClassicAssert.AreEqual(HitZone.Head, result.Zone);
            ClassicAssert.IsTrue(result.Killed, "un headshot de fusil doit tuer");
            ClassicAssert.AreEqual(0, targets[0].Health);
        }

        [Test]
        public void Un_Tir_Au_Torse_Ne_Tue_Pas_Du_Premier_Coup()
        {
            var targets = OneTargetAt(8f);
            var rng = new Rng(1);

            var result = Hitscan.Fire(new Vector3(0f, 1.30f, 0f), Forward, Right, Up,
                                      Arsenal.Rifle, StanceState.Crouching, 0, ref rng,
                                      EmptyWorld(), targets, shooterId: 1);

            ClassicAssert.AreEqual(HitZone.Chest, result.Zone);
            ClassicAssert.IsFalse(result.Killed);
            ClassicAssert.Greater(targets[0].Health, 0);
            ClassicAssert.Less(targets[0].Health, 100);
        }

        [Test]
        public void Un_Mur_Arrete_La_Balle()
        {
            var world = new CollisionWorld();
            // Mur plein entre le tireur et la cible.
            world.Add(new Aabb(new Vector3(-5f, 0f, 4f), new Vector3(5f, 3f, 4.5f)));

            var targets = OneTargetAt(8f);
            var rng = new Rng(1);

            var result = Hitscan.Fire(new Vector3(0f, 1.30f, 0f), Forward, Right, Up,
                                      Arsenal.Rifle, StanceState.Crouching, 0, ref rng,
                                      world, targets, shooterId: 1);

            ClassicAssert.IsTrue(result.HitAnything);
            ClassicAssert.IsFalse(result.HitTarget, "on ne tire pas a travers un mur");
            ClassicAssert.AreEqual(100, targets[0].Health, "la cible ne doit rien prendre");
            ClassicAssert.AreEqual(4f, result.Distance, 0.2f);
        }

        [Test]
        public void On_Ne_Se_Tire_Pas_Dessus()
        {
            var targets = new List<CombatTarget>
            {
                new CombatTarget { Id = 1, Position = new Vector3(0f, 0f, 8f) },
            };
            var rng = new Rng(1);

            var result = Hitscan.Fire(new Vector3(0f, 1.30f, 0f), Forward, Right, Up,
                                      Arsenal.Rifle, StanceState.Crouching, 0, ref rng,
                                      EmptyWorld(), targets, shooterId: 1);

            ClassicAssert.IsFalse(result.HitTarget);
            ClassicAssert.AreEqual(100, targets[0].Health);
        }

        [Test]
        public void La_Cible_La_Plus_Proche_Encaisse()
        {
            var targets = new List<CombatTarget>
            {
                new CombatTarget { Id = 2, Position = new Vector3(0f, 0f, 20f) },
                new CombatTarget { Id = 3, Position = new Vector3(0f, 0f, 6f) },
            };
            var rng = new Rng(1);

            var result = Hitscan.Fire(new Vector3(0f, 1.30f, 0f), Forward, Right, Up,
                                      Arsenal.Rifle, StanceState.Crouching, 0, ref rng,
                                      EmptyWorld(), targets, shooterId: 1);

            ClassicAssert.AreEqual(3, result.TargetId, "la cible devant doit encaisser");
            ClassicAssert.AreEqual(100, targets[0].Health, "celle derriere est protegee");
        }

        [Test]
        public void Les_Degats_Diminuent_Avec_La_Distance()
        {
            int DamageAt(float z)
            {
                var targets = OneTargetAt(z);
                var rng = new Rng(1);
                var r = Hitscan.Fire(new Vector3(0f, 1.30f, 0f), Forward, Right, Up,
                                     Arsenal.Rifle, StanceState.Crouching, 0, ref rng,
                                     EmptyWorld(), targets, shooterId: 1);
                return r.Damage;
            }

            int close = DamageAt(10f);
            int far = DamageAt(90f);

            ClassicAssert.Greater(close, far, "l'attenuation doit se faire sentir");
        }
    }
}
