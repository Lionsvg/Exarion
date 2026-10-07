// ===========================================================================
//  Exarion - Tests de la geometrie mobile et du cablage sorties -> entrees
//
//  Ce fichier verifie la brique qui manquait a E13 : une sortie qui arrive
//  vraiment quelque part, et une porte qui bouge vraiment.
//
//  Tout se teste sans GPU et sans fenetre. Une porte est une boite, une
//  vitesse et un etat - il n'y a rien a afficher pour savoir si elle marche.
// ===========================================================================

using System.Numerics;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core.Assets;
using Exarion.Core.Entities;
using Exarion.Core.Physics;

namespace Exarion.Tests
{
    [TestFixture]
    public class MoverTests
    {
        private const float Step = 1f / 64f;

        private static MapEntity Door(string name = "porte", string axis = "y",
                                      float distance = 3f, float speed = 2f)
        {
            return new MapEntity(EntityTypes.Door, new Vector3(0f, 0f, 0f))
                .Set("name", name)
                .Set("axis", axis)
                .Set("distance", distance.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Set("speed", speed.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Set("width", "2")
                .Set("height", "3")
                .Set("depth", "0.3");
        }

        // -------------------------------------------------------------------
        //  Le mouvement lui-meme
        // -------------------------------------------------------------------

        [Test]
        public void Une_Porte_Neuve_Est_Fermee_Et_Ne_Bouge_Pas()
        {
            var mover = new Mover(Door());

            for (int i = 0; i < 64; i++)
                mover.Step(Step);

            ClassicAssert.AreEqual(MoverState.Ferme, mover.State);
            ClassicAssert.AreEqual(0f, mover.Fraction);
            ClassicAssert.AreEqual(Vector3.Zero, mover.Delta,
                "une porte fermee ne doit produire AUCUN deplacement");
        }

        [Test]
        public void Ouvrir_Fait_Parcourir_La_Distance_Au_Temps_Prevu()
        {
            // LE TEST QUI COMPTE sur la vitesse : 3 m a 2 m/s = 1,5 s.
            // Si la vitesse est interpretee comme une fraction par seconde au
            // lieu de metres par seconde, ce test tombe.
            var mover = new Mover(Door(distance: 3f, speed: 2f));

            mover.Receive(Mover.InputOpen);

            int steps = 0;
            while (mover.State != MoverState.Ouvert && steps < 1000)
            {
                mover.Step(Step);
                steps++;
            }

            ClassicAssert.AreEqual(MoverState.Ouvert, mover.State, "elle doit finir par s'ouvrir");

            float seconds = steps * Step;
            ClassicAssert.AreEqual(1.5f, seconds, 0.02f,
                $"3 m a 2 m/s font 1,5 s, mesure {seconds:0.000} s");

            ClassicAssert.AreEqual(1f, mover.Fraction, 1e-5f);
            ClassicAssert.AreEqual(3f, mover.Center.Y - mover.ClosedCenter.Y, 1e-4f,
                "et elle doit etre montee de 3 m exactement");
        }

        [Test]
        public void La_Porte_Sarrete_Pile_Et_Ne_Depasse_Jamais()
        {
            // Le cas piegeux : on continue a la faire avancer bien apres
            // l'arrivee. Sans ecretage, elle part a l'infini.
            var mover = new Mover(Door(distance: 3f, speed: 50f));

            mover.Receive(Mover.InputOpen);

            for (int i = 0; i < 300; i++)
                mover.Step(Step);

            ClassicAssert.AreEqual(1f, mover.Fraction, 0f, "l'avancement est borne a 1");
            ClassicAssert.AreEqual(mover.OpenCenter, mover.Center);
        }

        [Test]
        public void Le_Deplacement_Rapporte_Est_Le_Deplacement_REEL()
        {
            // ⚠️ LE PIEGE CENTRAL DE CE FICHIER.
            // Au dernier pas, la course est ecretee par la butee. Si Delta
            // valait direction x vitesse x dt, il annoncerait plus que le
            // mouvement effectif - et un joueur porte serait pousse au-dela de
            // la plateforme, un peu plus a chaque aller-retour.
            //
            // On verifie donc que la SOMME des Delta vaut exactement la course.
            var mover = new Mover(Door(distance: 3f, speed: 7f));

            mover.Receive(Mover.InputOpen);

            Vector3 cumule = Vector3.Zero;

            for (int i = 0; i < 200; i++)
            {
                mover.Step(Step);
                cumule += mover.Delta;
            }

            ClassicAssert.AreEqual(3f, cumule.Y, 1e-4f,
                $"la somme des deplacements doit valoir la course, mesure {cumule.Y:0.00000}");
            ClassicAssert.AreEqual(0f, cumule.X, 1e-6f);
            ClassicAssert.AreEqual(0f, cumule.Z, 1e-6f);
        }

        [Test]
        public void Fermer_Ramene_Exactement_Au_Point_De_Depart()
        {
            var mover = new Mover(Door(distance: 3f, speed: 6f));
            Vector3 depart = mover.Center;

            mover.Receive(Mover.InputOpen);
            for (int i = 0; i < 100; i++) mover.Step(Step);

            mover.Receive(Mover.InputClose);
            for (int i = 0; i < 100; i++) mover.Step(Step);

            ClassicAssert.AreEqual(MoverState.Ferme, mover.State);
            ClassicAssert.AreEqual(depart, mover.Center,
                "un aller-retour ne doit laisser AUCUNE derive : sinon une porte "
                + "actionnee cent fois finit decalee de son cadre");
        }

        [Test]
        public void Basculer_Inverse_Meme_En_Pleine_Course()
        {
            var mover = new Mover(Door(distance: 3f, speed: 1f));

            mover.Receive(Mover.InputToggle);
            for (int i = 0; i < 32; i++) mover.Step(Step);   // ~0,5 s : a mi-course

            ClassicAssert.AreEqual(MoverState.Ouverture, mover.State);
            float milieu = mover.Fraction;
            ClassicAssert.Greater(milieu, 0.1f);
            ClassicAssert.Less(milieu, 0.9f);

            mover.Receive(Mover.InputToggle);
            ClassicAssert.AreEqual(MoverState.Fermeture, mover.State,
                "un second appui doit la faire repartir en arriere, pas attendre l'arrivee");

            mover.Step(Step);
            ClassicAssert.Less(mover.Fraction, milieu);
        }

        [Test]
        public void Une_Entree_Inconnue_Est_Refusee_Et_Ne_Fait_Rien()
        {
            var mover = new Mover(Door());

            ClassicAssert.IsFalse(mover.Receive("Ouvre"),
                "« Ouvre » n'est pas « Ouvrir » : il faut le DIRE, pas l'ignorer");

            for (int i = 0; i < 64; i++) mover.Step(Step);
            ClassicAssert.AreEqual(MoverState.Ferme, mover.State);
        }

        [Test]
        public void Lattente_Referme_Seule_Mais_Seulement_Une_Fois_Ouverte()
        {
            // 3 m a 1 m/s = 3 s de course, pour 1 s d'attente. Ce test verifie
            // qu'une porte lente ne se referme PAS pendant sa montee.
            //
            // ⚠️ Il ne prouve pas ce que je croyais. Mutation faite : lancer le
            // compte a rebours des l'ouverture ne le fait PAS tomber, parce que
            // le compteur ne s'ecoule que dans l'etat Ouvert. Voir le « when »
            // dans Mover.Step - c'est lui la protection.
            var mover = new Mover(Door(distance: 3f, speed: 1f));
            mover.Entity.Set("wait", "1");
            mover = new Mover(mover.Entity);

            mover.Receive(Mover.InputOpen);

            // Apres 2 s, elle est encore en train de monter.
            for (int i = 0; i < 128; i++) mover.Step(Step);
            ClassicAssert.AreEqual(MoverState.Ouverture, mover.State,
                "a 2 s sur 3 s de course, elle monte encore");

            // Elle atteint l'ouverture vers 3 s, puis attend 1 s.
            for (int i = 0; i < 96; i++) mover.Step(Step);
            ClassicAssert.AreEqual(MoverState.Ouvert, mover.State, "a 3,5 s elle est ouverte et attend");

            for (int i = 0; i < 64; i++) mover.Step(Step);
            ClassicAssert.AreEqual(MoverState.Fermeture, mover.State,
                "et apres la seconde d'attente, elle se referme seule");
        }

        [Test]
        public void Une_Porte_Deja_Ouverte_Recharge_Son_Attente()
        {
            // Un joueur qui reste dans le volume ne doit pas voir la porte se
            // refermer sur lui : chaque OnEnter relance le compte a rebours.
            var mover = new Mover(Door(distance: 1f, speed: 10f));
            mover.Entity.Set("wait", "1");
            mover = new Mover(mover.Entity);

            mover.Receive(Mover.InputOpen);
            for (int i = 0; i < 20; i++) mover.Step(Step);
            ClassicAssert.AreEqual(MoverState.Ouvert, mover.State);

            // On la re-ouvre juste avant l'echeance, cinq fois de suite.
            for (int cycle = 0; cycle < 5; cycle++)
            {
                for (int i = 0; i < 50; i++) mover.Step(Step);   // ~0,78 s
                mover.Receive(Mover.InputOpen);
            }

            ClassicAssert.AreEqual(MoverState.Ouvert, mover.State,
                "re-declenchee, elle doit rester ouverte indefiniment");
        }

        [Test]
        public void Une_Distance_Nulle_Ne_Divise_Pas_Par_Zero()
        {
            var mover = new Mover(Door(distance: 0f));

            ClassicAssert.IsTrue(mover.Degenerate);

            mover.Receive(Mover.InputOpen);
            for (int i = 0; i < 64; i++) mover.Step(Step);

            ClassicAssert.IsFalse(float.IsNaN(mover.Center.Y), "aucun NaN ne doit sortir de la");
            ClassicAssert.AreEqual(mover.ClosedCenter, mover.Center);
        }

        [Test]
        public void Une_Taille_Nulle_Retombe_Sur_Un_Minimum_Solide()
        {
            // Meme piege que les boites de decor et les volumes : un intervalle
            // inverse n'est JAMAIS croise, donc la porte s'afficherait sans
            // jamais bloquer personne.
            var entity = new MapEntity(EntityTypes.Door, Vector3.Zero)
                .Set("width", "0").Set("height", "0").Set("depth", "-2");

            var mover = new Mover(entity);

            ClassicAssert.GreaterOrEqual(mover.Size.X, Mover.MinimumSize);
            ClassicAssert.GreaterOrEqual(mover.Size.Y, Mover.MinimumSize);
            ClassicAssert.GreaterOrEqual(mover.Size.Z, Mover.MinimumSize);

            Aabb box = mover.Box;
            ClassicAssert.Less(box.Min.X, box.Max.X, "la boite doit rester valide");
            ClassicAssert.Less(box.Min.Y, box.Max.Y);
            ClassicAssert.Less(box.Min.Z, box.Max.Z);
        }

        [Test]
        public void Une_Porte_Pose_Sa_Base_Sur_Le_Sol_Declare()
        {
            // Une entite est posee par son PIED dans l'editeur (c'est la
            // convention des marqueurs). Une porte centree sur ce point serait
            // enfoncee a moitie dans le sol.
            var mover = new Mover(Door());

            ClassicAssert.AreEqual(0f, mover.Box.Min.Y, 1e-5f,
                "le bas de la porte doit affleurer la position declaree");
        }

        [Test]
        public void Laxe_Choisi_Est_Respecte()
        {
            var surX = new Mover(Door(axis: "x", distance: 4f));
            var surZ = new Mover(Door(axis: "z", distance: -4f));

            ClassicAssert.AreEqual(4f, surX.OpenCenter.X - surX.ClosedCenter.X, 1e-5f);
            ClassicAssert.AreEqual(0f, surX.OpenCenter.Y - surX.ClosedCenter.Y, 1e-5f);

            ClassicAssert.AreEqual(-4f, surZ.OpenCenter.Z - surZ.ClosedCenter.Z, 1e-5f,
                "une distance negative doit aller dans l'autre sens");
        }

        [Test]
        public void Start_Open_Demarre_Ouverte()
        {
            var entity = Door().Set("start_open", "1");
            var mover = new Mover(entity);

            ClassicAssert.AreEqual(MoverState.Ouvert, mover.State);
            ClassicAssert.AreEqual(mover.OpenCenter, mover.Center);
        }

        // -------------------------------------------------------------------
        //  PORTER UN PASSAGER : ce qui fait qu'un ascenseur a un sens
        // -------------------------------------------------------------------

        [Test]
        public void Une_Plateforme_Qui_Monte_Porte_Ce_Qui_Est_Dessus()
        {
            // ⚠️ SANS CECI, UN ASCENSEUR EST INUTILISABLE : la plateforme monte,
            // le joueur reste en l'air, retombe, remonte - il vibre au lieu de
            // monter.
            var world = new MoverWorld();
            var map = new MapData();

            map.Entities.Add(Door(name: "ascenseur", axis: "y", distance: 4f, speed: 2f));
            world.Load(map);

            Mover lift = world.Movers[0];
            lift.Receive(Mover.InputOpen);

            // Un joueur pose pile sur le dessus de la plateforme. On mesure la
            // MONTEE, pas l'altitude : le dessus d'une porte de 3 m est deja a
            // 3 m, et confondre les deux fait attendre 4 la ou 7 est juste.
            float depart = lift.Box.Max.Y;
            Vector3 position = new(0f, depart, 0f);

            for (int i = 0; i < 200; i++)
            {
                world.Step(Step);

                var rider = Aabb.FromCenterSize(position + new Vector3(0f, 0.9f, 0f),
                                                new Vector3(0.6f, 1.8f, 0.6f));

                position += world.CarryDelta(rider);
            }

            float montee = position.Y - depart;

            ClassicAssert.AreEqual(4f, montee, 0.01f,
                $"le passager doit avoir monte de 4 m, mesure {montee:0.000} m");
            ClassicAssert.AreEqual(lift.Box.Max.Y, position.Y, 0.01f,
                "et rester pose sur la plateforme, pas flotter au-dessus");
        }

        [Test]
        public void CarryRider_Repose_Le_Passager_Sur_Le_Dessus_Avec_Un_Jeu()
        {
            // ⚠️⚠️ LE TEST QUI MANQUAIT, et son absence a coute une capture.
            // CarryDelta seul est juste geometriquement, et pourtant l'ascenseur
            // ne portait personne en jeu : « pieds += deplacement » laisse la
            // derive flottante decider, le joueur passe un cheveu SOUS la
            // surface, le balayage de sol du CharacterMotor ignore ce qui
            // chevauche deja, et le joueur tombe en chute libre.
            //
            // CarryRider doit donc REPOSER les pieds sur le dessus a chaque pas.
            var world = new MoverWorld();
            var map = new MapData();

            map.Entities.Add(Door(name: "ascenseur", axis: "y", distance: 4f, speed: 2f));
            world.Load(map);
            world.Movers[0].Receive(Mover.InputOpen);

            Mover lift = world.Movers[0];
            Vector3 feet = new(0f, lift.Box.Max.Y, 0f);

            for (int i = 0; i < 200; i++)
            {
                world.Step(Step);

                var rider = Aabb.FromCenterSize(feet + new Vector3(0f, 0.9f, 0f),
                                                new Vector3(0.6f, 1.8f, 0.6f));

                if (!world.CarryRider(ref feet, rider))
                    continue;

                // A CHAQUE pas, et pas seulement a l'arrivee : le jeu doit etre
                // maintenu en permanence, sinon la derive revient.
                ClassicAssert.AreEqual(lift.Box.Max.Y + MoverWorld.ContactGap, feet.Y, 1e-5f,
                    $"au pas {i}, les pieds doivent etre a {MoverWorld.ContactGap * 1000f:0} mm du dessus");
            }

            ClassicAssert.AreEqual(lift.Box.Max.Y + MoverWorld.ContactGap, feet.Y, 1e-5f);

            // Et le jeu doit rester TRES en dessous des 5 cm du balayage de sol,
            // sinon le passager n'est plus « au sol » du tout.
            ClassicAssert.Less(MoverWorld.ContactGap, 0.02f,
                "un jeu trop grand ferait flotter le passager hors de portee du balayage");
        }

        [Test]
        public void Un_CharacterMotor_Monte_VRAIMENT_Sur_Une_Plateforme()
        {
            // ⚠️⚠️ LE TEST QUI AURAIT DU EXISTER D'ABORD.
            //
            // Tous les autres verifient MoverWorld contre lui-meme. Celui-ci
            // fait monter un VRAI CharacterMotor sur une VRAIE plateforme, avec
            // le vrai monde de collision - et c'est le seul qui pouvait attraper
            // le defaut trouve en capture : le joueur reste au sol pendant que la
            // plateforme part sans lui.
            //
            // Meme lecon que « le joueur contre les caisses » du 16/08 : la
            // suite peut etre verte et le jeu injouable, parce que personne ne
            // teste le joueur CONTRE les corps.
            var world = new MoverWorld();
            var map = new MapData();

            map.Entities.Add(Door(name: "ascenseur", axis: "y", distance: 4f, speed: 2f));
            world.Load(map);
            world.Movers[0].Receive(Mover.InputOpen);

            Mover lift = world.Movers[0];

            var motor = new CharacterMotor();
            motor.Position = new Vector3(0f, lift.Box.Max.Y, 0f);

            // On mesure la MONTEE, pas l'altitude : le dessus d'une plateforme
            // de 3 m de haut est deja a 3 m. J'ai fait l'erreur deux fois.
            float depart = motor.Position.Y;

            // Un sol sous la plateforme, sinon le joueur tomberait a l'infini et
            // le test passerait pour de mauvaises raisons.
            var collision = new CollisionWorld();

            for (int i = 0; i < 200; i++)
            {
                world.Step(Step);

                collision.Clear();
                collision.AddBox(new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
                world.AppendTo(collision);

                world.CarryRider(ref motor.Position, motor.GetBox());
                motor.Step(default, Step, collision);
            }

            ClassicAssert.AreEqual(MoverState.Ouvert, lift.State, "la plateforme est arrivee en haut");

            float montee = motor.Position.Y - depart;

            ClassicAssert.AreEqual(4f, montee, 0.02f,
                $"le joueur doit avoir monte de 4 m avec elle, mesure {montee:0.000} m");

            ClassicAssert.IsTrue(motor.Grounded,
                "et il doit etre AU SOL sur la plateforme : sinon il n'a ni frottement "
                + "ni acceleration au sol, et on ne peut pas marcher dessus");

            ClassicAssert.AreEqual(0f, motor.Velocity.Y, 1e-3f,
                "sa vitesse verticale doit etre nulle, pas une chute libre compensee");
        }

        [Test]
        public void CarryRider_Ne_Touche_Pas_Quelqu_Un_Qui_Nest_Pas_Porte()
        {
            var world = new MoverWorld();
            var map = new MapData();

            map.Entities.Add(Door(name: "ascenseur", distance: 4f, speed: 2f));
            world.Load(map);
            world.Movers[0].Receive(Mover.InputOpen);
            world.Step(Step);

            Vector3 feet = new(10f, 0f, 0f);
            var loin = Aabb.FromCenterSize(feet + new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 1.8f, 0.6f));

            ClassicAssert.IsFalse(world.CarryRider(ref feet, loin));
            ClassicAssert.AreEqual(new Vector3(10f, 0f, 0f), feet,
                "personne ne doit etre deplace, ni recale sur une hauteur");
        }

        [Test]
        public void Une_Plateforme_RAPIDE_Ne_Largue_Pas_Son_Passager()
        {
            // ⚠️ LE PIEGE DE LA TOLERANCE. Si le portage se decide contre la
            // boite d'APRES le pas, la tolerance devient une limite de vitesse
            // deguisee : a 12 cm de tolerance et 1/64 s de pas, tout ce qui
            // depasse 7,7 m/s largue son passager. Et le seuil dependrait du
            // tickrate, donc le defaut n'apparaitrait que sur certaines
            // machines.
            //
            // Ici : 12 m/s, soit 19 cm par pas - largement au-dela.
            var world = new MoverWorld();
            var map = new MapData();

            map.Entities.Add(Door(name: "fusee", axis: "y", distance: 10f, speed: 12f));
            world.Load(map);
            world.Movers[0].Receive(Mover.InputOpen);

            float depart = world.Movers[0].Box.Max.Y;
            Vector3 position = new(0f, depart, 0f);

            for (int i = 0; i < 200; i++)
            {
                world.Step(Step);

                var rider = Aabb.FromCenterSize(position + new Vector3(0f, 0.9f, 0f),
                                                new Vector3(0.6f, 1.8f, 0.6f));

                position += world.CarryDelta(rider);
            }

            ClassicAssert.AreEqual(10f, position.Y - depart, 0.01f,
                $"le passager doit suivre les 10 m, mesure {position.Y - depart:0.000} m");
        }

        [Test]
        public void On_Ne_Porte_Pas_Quelqu_Un_Qui_Passe_A_Cote()
        {
            var world = new MoverWorld();
            var map = new MapData();

            map.Entities.Add(Door(name: "ascenseur", distance: 4f, speed: 2f));
            world.Load(map);
            world.Movers[0].Receive(Mover.InputOpen);
            world.Step(Step);

            // La porte fait 2 m de large, centree en 0 : a 10 m, personne.
            var loin = Aabb.FromCenterSize(new Vector3(10f, 0.9f, 0f), new Vector3(0.6f, 1.8f, 0.6f));

            ClassicAssert.AreEqual(Vector3.Zero, world.CarryDelta(loin));
        }

        [Test]
        public void On_Ne_Porte_Pas_Quelqu_Un_Qui_Est_Sous_La_Plateforme()
        {
            var world = new MoverWorld();
            var map = new MapData();

            map.Entities.Add(Door(name: "plafond", distance: 4f, speed: 2f));
            world.Load(map);
            world.Movers[0].Receive(Mover.InputOpen);
            world.Step(Step);

            // Pieds au sol, sous une plateforme dont le dessus est a 3 m.
            var dessous = Aabb.FromCenterSize(new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 1.8f, 0.6f));

            ClassicAssert.AreEqual(Vector3.Zero, world.CarryDelta(dessous),
                "etre sous une plateforme n'est pas etre porte par elle");
        }

        [Test]
        public void Deux_Plateformes_Jointives_Ne_Portent_Pas_Deux_Fois_Plus_Vite()
        {
            var world = new MoverWorld();
            var map = new MapData();

            // Deux plateformes cote a cote, un joueur a cheval sur les deux.
            var a = Door(name: "a", distance: 4f, speed: 2f);
            var b = Door(name: "b", distance: 4f, speed: 2f);
            b.Position = new Vector3(2f, 0f, 0f);

            map.Entities.Add(a);
            map.Entities.Add(b);
            world.Load(map);

            foreach (Mover m in world.Movers)
                m.Receive(Mover.InputOpen);

            // A cheval : la jointure est en x = 1.
            float depart = world.Movers[0].Box.Max.Y;
            Vector3 position = new(1f, depart, 0f);

            for (int i = 0; i < 200; i++)
            {
                world.Step(Step);

                var rider = Aabb.FromCenterSize(position + new Vector3(0f, 0.9f, 0f),
                                                new Vector3(0.6f, 1.8f, 0.6f));

                position += world.CarryDelta(rider);
            }

            float montee = position.Y - depart;

            ClassicAssert.AreEqual(4f, montee, 0.01f,
                $"a cheval sur deux plateformes, on monte de 4 m et pas de 8, mesure {montee:0.000}");
        }

        // -------------------------------------------------------------------
        //  LE CABLAGE COMPLET : un volume ouvre vraiment une porte
        // -------------------------------------------------------------------

        [Test]
        public void Un_Declencheur_Ouvre_Vraiment_La_Porte()
        {
            // ⚠️ LE TEST QUI JUSTIFIE TOUT E17. Avant lui, cet enchainement
            // s'affichait dans la console et ne faisait rien.
            var map = new MapData();

            map.Entities.Add(new MapEntity(EntityTypes.Trigger, new Vector3(0f, 0f, 0f))
                .Set("size", "4")
                .Set("target", "porte_hall")
                .Set("input", Mover.InputOpen));

            var door = Door(name: "porte_hall", distance: 3f, speed: 4f);
            door.Position = new Vector3(20f, 0f, 0f);
            map.Entities.Add(door);

            var entities = new EntityWorld();
            var movers = new MoverWorld();

            entities.Load(map);
            movers.Load(map);
            movers.RegisterInto(entities);

            Mover hall = movers.Find("porte_hall")!;
            ClassicAssert.IsNotNull(hall);
            ClassicAssert.AreEqual(MoverState.Ferme, hall.State, "fermee au depart");

            // Le joueur entre dans le volume.
            var player = Aabb.FromCenterSize(new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 1.8f, 0.6f));
            entities.Update(player);

            ClassicAssert.AreEqual(1, entities.Events.Count);
            ClassicAssert.AreEqual(EntityDelivery.Delivree, entities.Events[0].Delivery,
                "la sortie doit etre DELIVREE, pas seulement emise");

            ClassicAssert.AreEqual(MoverState.Ouverture, hall.State,
                "et la porte doit s'etre mise en mouvement");

            for (int i = 0; i < 200; i++)
                movers.Step(Step);

            ClassicAssert.AreEqual(MoverState.Ouvert, hall.State);
            ClassicAssert.AreEqual(3f, hall.Center.Y - hall.ClosedCenter.Y, 1e-3f);
        }

        [Test]
        public void Une_Cible_Qui_Nexiste_Pas_Est_SIGNALEE()
        {
            // Une faute de frappe sur un nom de cible est l'erreur la plus
            // banale d'un auteur de niveau. Elle doit se voir.
            var map = new MapData();

            map.Entities.Add(new MapEntity(EntityTypes.Trigger, Vector3.Zero)
                .Set("size", "4")
                .Set("target", "porte_hal")           // il manque un L
                .Set("input", Mover.InputOpen));

            map.Entities.Add(Door(name: "porte_hall"));

            var entities = new EntityWorld();
            var movers = new MoverWorld();

            entities.Load(map);
            movers.Load(map);
            movers.RegisterInto(entities);

            entities.Update(Aabb.FromCenterSize(new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 1.8f, 0.6f)));

            ClassicAssert.AreEqual(1, entities.Events.Count);
            ClassicAssert.AreEqual(EntityDelivery.CibleAbsente, entities.Events[0].Delivery);
            Assert.That(entities.Events[0].ToString(), Does.Contain("CIBLE INTROUVABLE"),
                "et le texte affiche doit le dire, sinon l'auteur cherche ailleurs");
        }

        [Test]
        public void Une_Entree_Mal_Orthographiee_Est_SIGNALEE()
        {
            var map = new MapData();

            map.Entities.Add(new MapEntity(EntityTypes.Trigger, Vector3.Zero)
                .Set("size", "4")
                .Set("target", "porte")
                .Set("input", "Ouvre"));              // ce n'est pas « Ouvrir »

            map.Entities.Add(Door(name: "porte"));

            var entities = new EntityWorld();
            var movers = new MoverWorld();

            entities.Load(map);
            movers.Load(map);
            movers.RegisterInto(entities);

            entities.Update(Aabb.FromCenterSize(new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 1.8f, 0.6f)));

            ClassicAssert.AreEqual(EntityDelivery.EntreeInconnue, entities.Events[0].Delivery,
                "la cible existe et refuse : ce n'est PAS le meme defaut qu'une cible absente");
            Assert.That(entities.Events[0].ToString(), Does.Contain("ENTREE INCONNUE"));
        }

        [Test]
        public void Une_Sortie_Sans_Cible_Reste_Non_Cablee()
        {
            var map = new MapData();
            map.Entities.Add(new MapEntity(EntityTypes.Trigger, Vector3.Zero).Set("size", "4"));

            var entities = new EntityWorld();
            entities.Load(map);

            entities.Update(Aabb.FromCenterSize(new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 1.8f, 0.6f)));

            ClassicAssert.AreEqual(1, entities.Events.Count, "elle emet quand meme");
            ClassicAssert.AreEqual(EntityDelivery.NonCablee, entities.Events[0].Delivery);
        }

        [Test]
        public void Changer_De_Carte_Oublie_Les_Anciennes_Cibles()
        {
            // ⚠️ Sans cet oubli, un declencheur du niveau 2 qui vise
            // « porte_hall » actionnerait la porte du niveau 1 - invisible,
            // puisqu'elle n'est plus rendue.
            var entities = new EntityWorld();
            var movers = new MoverWorld();

            var premiere = new MapData();
            premiere.Entities.Add(Door(name: "porte_hall"));

            entities.Load(premiere);
            movers.Load(premiere);
            movers.RegisterInto(entities);

            ClassicAssert.AreEqual(1, entities.Receivers.Count);

            entities.Load(new MapData());

            ClassicAssert.AreEqual(0, entities.Receivers.Count,
                "la cible de l'ancienne carte ne doit plus etre joignable");
            ClassicAssert.AreEqual(EntityDelivery.CibleAbsente,
                entities.Send("porte_hall", Mover.InputOpen));
        }

        [Test]
        public void La_Console_Peut_Actionner_Une_Porte_Directement()
        {
            // Sans ca, tester une porte demanderait de courir jusqu'a son
            // declencheur - donc serait inverifiable en headless.
            var map = new MapData();
            map.Entities.Add(Door(name: "porte", distance: 2f, speed: 8f));

            var entities = new EntityWorld();
            var movers = new MoverWorld();

            entities.Load(map);
            movers.Load(map);
            movers.RegisterInto(entities);

            ClassicAssert.AreEqual(EntityDelivery.Delivree, entities.Send("porte", Mover.InputToggle));

            for (int i = 0; i < 100; i++) movers.Step(Step);

            ClassicAssert.AreEqual(MoverState.Ouvert, movers.Find("porte")!.State);
        }

        [Test]
        public void Une_Porte_Sans_Nom_Nest_Pas_Inscrite()
        {
            var map = new MapData();
            map.Entities.Add(new MapEntity(EntityTypes.Door, Vector3.Zero));   // pas de name

            var entities = new EntityWorld();
            var movers = new MoverWorld();

            entities.Load(map);
            movers.Load(map);
            movers.RegisterInto(entities);

            ClassicAssert.AreEqual(1, movers.Movers.Count, "elle existe et elle est solide");
            ClassicAssert.AreEqual(0, entities.Receivers.Count, "mais rien ne peut la viser");
        }

        // -------------------------------------------------------------------
        //  Collision
        // -------------------------------------------------------------------

        [Test]
        public void La_Porte_Est_Solide_Et_Sa_Boite_Suit_Le_Mouvement()
        {
            var world = new MoverWorld();
            var map = new MapData();

            map.Entities.Add(Door(name: "porte", axis: "y", distance: 5f, speed: 5f));
            world.Load(map);

            var collision = new CollisionWorld();
            world.AppendTo(collision);

            ClassicAssert.AreEqual(1, collision.Boxes.Count);

            // Un point a 1 m de haut, dans l'embrasure : la porte fermee le bloque.
            var corps = Aabb.FromCenterSize(new Vector3(0f, 1f, 0f), new Vector3(0.5f, 1.8f, 0.5f));
            ClassicAssert.IsTrue(collision.Overlaps(corps), "porte fermee : ca bloque");

            world.Movers[0].Receive(Mover.InputOpen);
            for (int i = 0; i < 200; i++) world.Step(Step);

            collision.Clear();
            world.AppendTo(collision);

            ClassicAssert.IsFalse(collision.Overlaps(corps),
                "porte montee de 5 m : le passage doit etre libre");
        }
    }
}
