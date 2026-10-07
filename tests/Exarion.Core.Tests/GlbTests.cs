// ===========================================================================
//  Tests du conteneur GLB (M1).
//
//  Deux familles, et les deux comptent.
//
//  Les cas MALFORMES d'abord : un modele mal lu ne plante pas, il produit
//  des triangles absurdes trois etapes plus loin. Mieux vaut refuser net.
//
//  Puis le VRAI fichier du projet. Un format se prouve sur ses fichiers
//  reels, pas sur des exemples fabriques a sa propre image.
// ===========================================================================

using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Exarion.Core.Assets;

namespace Exarion.Tests
{
    public class GlbTests
    {
        // --- Cas malformes ------------------------------------------------

        [Test]
        public void Un_Fichier_Trop_Court_Est_Refuse()
        {
            Assert.Throws<InvalidDataException>(() => Glb.Parse(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void Une_Signature_Absente_Est_Refusee()
        {
            var bytes = new byte[16];
            Encoding.ASCII.GetBytes("PNG ").CopyTo(bytes, 0);

            Assert.Throws<InvalidDataException>(() => Glb.Parse(bytes));
        }

        [Test]
        public void Une_Version_Inconnue_Est_Refusee()
        {
            byte[] bytes = Build(version: 3, json: "{}", binary: null);

            Assert.Throws<InvalidDataException>(() => Glb.Parse(bytes));
        }

        [Test]
        public void Un_Fichier_Tronque_Est_Refuse()
        {
            // La longueur annoncee survit a une coupure : c'est exactement le
            // cas d'un telechargement interrompu.
            byte[] complete = Build(version: 2, json: "{\"asset\":{}}", binary: new byte[64]);
            byte[] truncated = complete[..(complete.Length - 20)];

            Assert.Throws<InvalidDataException>(() => Glb.Parse(truncated));
        }

        [Test]
        public void Un_Glb_Sans_Json_Est_Refuse()
        {
            // Un GLB peut se passer de donnees binaires, jamais de JSON.
            byte[] bytes = BuildRaw(new (uint Type, byte[] Data)[] { (0x004E4942, new byte[8]) });

            Assert.Throws<InvalidDataException>(() => Glb.Parse(bytes));
        }

        [Test]
        public void Un_Morceau_Inconnu_Est_Ignore_Sans_Broncher()
        {
            // La specification autorise les extensions a ajouter leurs
            // propres morceaux. Les rejeter casserait sur des fichiers
            // parfaitement valides.
            byte[] bytes = BuildRaw(new (uint, byte[])[]
            {
                (0x4E4F534A, Encoding.UTF8.GetBytes("{\"asset\":{}}")),
                (0x12345678, new byte[] { 9, 9, 9, 9 }),
                (0x004E4942, new byte[] { 1, 2, 3, 4 }),
            });

            Glb glb = Glb.Parse(bytes);

            ClassicAssert.AreEqual("{\"asset\":{}}", glb.Json);
            ClassicAssert.AreEqual(4, glb.Binary.Length, "le morceau binaire doit survivre a l'inconnu");
        }

        [Test]
        public void Un_Glb_Sans_Binaire_Est_Accepte()
        {
            Glb glb = Glb.Parse(Build(version: 2, json: "{\"asset\":{}}", binary: null));

            ClassicAssert.AreEqual(0, glb.Binary.Length);
        }

        // --- Le vrai fichier du projet --------------------------------------

        [Test]
        public void Le_Personnage_Du_Projet_Se_Lit()
        {
            string? path = FindModel("Merged_Animations");

            if (path is null)
                Assert.Ignore("Modele absent : test ignore plutot qu'en echec.");

            Glb glb = Glb.Load(path!);

            ClassicAssert.AreEqual(2u, glb.Version);
            ClassicAssert.Greater(glb.Json.Length, 1000, "le JSON decrit toute la scene");
            ClassicAssert.Greater(glb.Binary.Length, 1_000_000, "les sommets et images sont dans le binaire");

            // Le JSON doit etre du texte exploitable, pas des octets bruts.
            Assert.That(glb.Json, Does.StartWith("{"));
            Assert.That(glb.Json, Does.EndWith("}"));
            Assert.That(glb.Json, Does.Contain("\"animations\""));
            Assert.That(glb.Json, Does.Contain("\"skins\""));
        }

        /// <summary>
        /// Remonte depuis le repertoire d'execution jusqu'a trouver Model3D.
        /// Les tests tournent depuis bin/Debug/net9.0, pas depuis la racine.
        /// </summary>
        private static string? FindModel(string contains)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir is not null)
            {
                var models = new DirectoryInfo(Path.Combine(dir.FullName, "Model3D"));

                if (models.Exists)
                {
                    foreach (FileInfo file in models.GetFiles("*.glb", SearchOption.AllDirectories))
                        if (file.Name.Contains(contains, StringComparison.OrdinalIgnoreCase))
                            return file.FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }

        // --- Fabrication de GLB de test --------------------------------------

        private static byte[] Build(uint version, string json, byte[]? binary)
        {
            var chunks = new System.Collections.Generic.List<(uint, byte[])>
            {
                (0x4E4F534A, Encoding.UTF8.GetBytes(json)),
            };

            if (binary is not null)
                chunks.Add((0x004E4942, binary));

            return BuildRaw(chunks.ToArray(), version);
        }

        private static byte[] BuildRaw((uint Type, byte[] Data)[] chunks, uint version = 2)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(0x46546C67u);
            writer.Write(version);
            writer.Write(0u);            // longueur, corrigee a la fin

            foreach ((uint type, byte[] data) in chunks)
            {
                writer.Write((uint)data.Length);
                writer.Write(type);
                writer.Write(data);
            }

            byte[] bytes = stream.ToArray();
            BitConverter.GetBytes((uint)bytes.Length).CopyTo(bytes, 8);

            return bytes;
        }
    }
}
