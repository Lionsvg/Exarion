// ===========================================================================
//  Tests : le client joue sur la carte du serveur
//
//  Le piege que l'empreinte attrape : deux cartes du MEME NOM qui different
//  d'un mur. Le nom seul les confondrait, et le client predirait dans un
//  decor faux - corrige en permanence, sans que rien ne dise pourquoi.
// ===========================================================================

using System;
using System.Globalization;
using System.Numerics;
using System.Threading;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Exarion.Core.Assets;
using Exarion.Core.Net;

namespace Exarion.Tests
{
    public class MapSyncTests
    {
        private static MapData Arena(float wallX = 5f)
        {
            var map = new MapData { Name = "arene" };
            map.Boxes.Add(new MapBox(new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), new Vector3(0.3f)));
            map.Boxes.Add(new MapBox(new Vector3(wallX, 1.5f, 0f), new Vector3(1f, 3f, 10f), new Vector3(0.5f)));
            return map;
        }

        [Test]
        public void La_Meme_Carte_A_La_Meme_Empreinte()
        {
            ClassicAssert.AreEqual(MapFile.Fingerprint(Arena()), MapFile.Fingerprint(Arena()));
        }

        [Test]
        public void Un_Mur_Deplace_Change_L_Empreinte()
        {
            // Un dixieme de millimetre : l'enregistrement est sans perte, donc
            // l'empreinte le voit.
            Assert.That(MapFile.Fingerprint(Arena(5.0001f)), Is.Not.EqualTo(MapFile.Fingerprint(Arena(5f))));
        }

        /// <summary>
        /// ⚠️ La CULTURE, encore - le piege de 2.4 et de 4.2. L'empreinte d'une
        /// meme carte doit etre la meme sur un poste francais et anglais : sinon
        /// un serveur anglais refuserait tout client francais.
        /// </summary>
        [Test]
        public void L_Empreinte_Ne_Depend_Pas_De_La_Culture()
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                uint fr = MapFile.Fingerprint(Arena(5.25f));

                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                uint en = MapFile.Fingerprint(Arena(5.25f));

                ClassicAssert.AreEqual(en, fr);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        /// <summary>
        /// ⚠️ L'empreinte ne voit que ce que le serveur SIMULE : les boites.
        /// Lumieres, couleurs, formes et entites n'y entrent pas - sinon deux
        /// joueurs du bac a sable integre, dont les intensites viennent de leur
        /// exarion.cfg, se refuseraient l'un l'autre.
        /// </summary>
        [Test]
        public void L_Empreinte_Ignore_Ce_Qui_Ne_Bloque_Pas()
        {
            MapData decorated = Arena();
            decorated.Lights.Add(MapLight.Point(new Vector3(0f, 4f, 0f), Vector3.One, 140f, 18f));
            decorated.Entities.Add(new MapEntity(EntityTypes.Npc, Vector3.Zero));
            decorated.Boxes[1] = new MapBox(decorated.Boxes[1].Center, decorated.Boxes[1].Size, new Vector3(1f, 0f, 0f), MapShape.Wedge);

            ClassicAssert.AreEqual(MapFile.Fingerprint(Arena()), MapFile.Fingerprint(decorated));
        }

        [Test]
        public void Une_Boite_Agrandie_Change_L_Empreinte()
        {
            MapData bigger = Arena();
            bigger.Boxes[1] = new MapBox(bigger.Boxes[1].Center, bigger.Boxes[1].Size + new Vector3(0f, 0.5f, 0f), Vector3.One);

            Assert.That(MapFile.Fingerprint(bigger), Is.Not.EqualTo(MapFile.Fingerprint(Arena())));
        }

        [Test]
        public void Une_Boite_De_Plus_Change_L_Empreinte()
        {
            MapData more = Arena();
            more.Boxes.Add(new MapBox(new Vector3(-5f, 1f, 0f), new Vector3(1f, 2f, 1f), Vector3.One));

            Assert.That(MapFile.Fingerprint(more), Is.Not.EqualTo(MapFile.Fingerprint(Arena())));
        }

        [Test]
        public void Deja_Sur_La_Carte_Du_Serveur_On_Garde()
        {
            MapSyncAction action = MapSync.Decide("arene", MapFile.Fingerprint(Arena()), Arena(),
                _ => throw new InvalidOperationException("aucune lecture de disque necessaire"),
                out MapData? toLoad, out _);

            ClassicAssert.AreEqual(MapSyncAction.Keep, action);
            Assert.That(toLoad, Is.Null);
        }

        [Test]
        public void Sur_Une_Autre_Carte_On_Charge_Celle_Du_Serveur()
        {
            var other = new MapData { Name = "formes" };

            MapSyncAction action = MapSync.Decide("arene", MapFile.Fingerprint(Arena()), other,
                name => name == "arene" ? new[] { Arena() } : Array.Empty<MapData>(), out MapData? toLoad, out string reason);

            ClassicAssert.AreEqual(MapSyncAction.Load, action, reason);
            ClassicAssert.AreEqual("arene", toLoad!.Name);
        }

        /// <summary>
        /// Un nom, deux cartes : le bac a sable INTEGRE et un fichier homonyme.
        /// C'est celle dont le decor concorde qui gagne, pas la premiere venue.
        /// </summary>
        [Test]
        public void Le_Candidat_Dont_Le_Decor_Concorde_Gagne()
        {
            MapSyncAction action = MapSync.Decide("arene", MapFile.Fingerprint(Arena(5f)), new MapData(),
                _ => new[] { Arena(7f), Arena(5f) }, out MapData? toLoad, out string reason);

            ClassicAssert.AreEqual(MapSyncAction.Load, action, reason);
            ClassicAssert.AreEqual(5f, toLoad!.Boxes[1].Center.X);
        }

        [Test]
        public void Une_Carte_Introuvable_Est_Refusee()
        {
            MapSyncAction action = MapSync.Decide("arene", MapFile.Fingerprint(Arena()), new MapData(),
                _ => Array.Empty<MapData>(), out MapData? toLoad, out string reason);

            ClassicAssert.AreEqual(MapSyncAction.Refuse, action);
            Assert.That(toLoad, Is.Null);
            Assert.That(reason, Does.Contain("INTROUVABLE"));
        }

        /// <summary>
        /// ⚠️ LE cas qui justifie l'empreinte : meme nom, un mur deplace. Le nom
        /// seul aurait charge la carte locale et predit dans un decor faux.
        /// </summary>
        [Test]
        public void Meme_Nom_Autre_Contenu_Est_Refuse()
        {
            MapSyncAction action = MapSync.Decide("arene", MapFile.Fingerprint(Arena(5f)), new MapData(),
                _ => new[] { Arena(6f) }, out MapData? toLoad, out string reason);

            ClassicAssert.AreEqual(MapSyncAction.Refuse, action);
            Assert.That(toLoad, Is.Null);
            Assert.That(reason, Does.Contain("DIFFERE"));
        }
    }
}
