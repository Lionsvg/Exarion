// ===========================================================================
//  Tests de la lecture de geometrie glTF (M2).
//
//  Un lecteur de format se trompe rarement bruyamment. Il rend des sommets
//  a peu pres plausibles, et c'est trois etapes plus loin - a l'ecran - que
//  le modele parait "bizarre" sans qu'on sache pourquoi.
//
//  On verifie donc des proprietes que seul un maillage CORRECT peut avoir :
//  des indices dans les bornes, des normales unitaires, une emprise a
//  taille humaine, et aucune valeur invalide.
// ===========================================================================

using System;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using Exarion.Core.Assets;

namespace Exarion.Tests
{
    public class GltfTests
    {
        private static GltfModel? _cached;

        /// <summary>Le personnage du projet, charge une seule fois.</summary>
        private static GltfModel Model()
        {
            if (_cached is not null)
                return _cached;

            string? path = FindModel();
            if (path is null)
                Assert.Ignore("Modele absent : test ignore plutot qu'en echec.");

            return _cached = GltfModel.Load(path!);
        }

        [Test]
        public void Le_Personnage_A_Une_Geometrie()
        {
            GltfModel model = Model();

            ClassicAssert.GreaterOrEqual(model.Primitives.Count, 1);
            ClassicAssert.Greater(model.VertexCount, 100, "un personnage n'a pas dix sommets");
            ClassicAssert.Greater(model.TriangleCount, 100);
        }

        [Test]
        public void Chaque_Attribut_A_Le_Meme_Nombre_De_Sommets()
        {
            // Positions et normales lues avec des nombres differents veut dire
            // qu'un accesseur a ete mal resolu. Le maillage serait alors
            // eclaire n'importe comment.
            foreach (GltfPrimitive p in Model().Primitives)
            {
                ClassicAssert.AreEqual(p.Positions.Length, p.Normals.Length, "normales");
                ClassicAssert.AreEqual(p.Positions.Length, p.TexCoords.Length, "coordonnees de texture");
            }
        }

        [Test]
        public void Les_Indices_Restent_Dans_Les_Bornes()
        {
            // LE test qui attrape un pas d'accesseur mal lu. Un indice hors
            // bornes fait planter le pilote graphique, ou pire, dessine de la
            // memoire au hasard.
            foreach (GltfPrimitive p in Model().Primitives)
            {
                ClassicAssert.AreEqual(0, p.Indices.Length % 3, "les indices vont par trois");

                foreach (uint index in p.Indices)
                    ClassicAssert.Less(index, (uint)p.Positions.Length, "indice hors du maillage");
            }
        }

        [Test]
        public void Les_Normales_Sont_Unitaires()
        {
            // Un entrelacement mal geré donne des normales de longueur
            // absurde : c'est le symptome le plus fiable d'un lecteur qui
            // ignore le pas entre deux elements.
            foreach (GltfPrimitive p in Model().Primitives)
            {
                foreach (Vector3 n in p.Normals)
                    ClassicAssert.AreEqual(1f, n.Length(), 1e-3f);
            }
        }

        [Test]
        public void Aucune_Valeur_Invalide()
        {
            foreach (GltfPrimitive p in Model().Primitives)
            {
                foreach (Vector3 v in p.Positions)
                {
                    ClassicAssert.IsFalse(float.IsNaN(v.X) || float.IsInfinity(v.X), "position invalide");
                    ClassicAssert.IsFalse(float.IsNaN(v.Y) || float.IsInfinity(v.Y), "position invalide");
                    ClassicAssert.IsFalse(float.IsNaN(v.Z) || float.IsInfinity(v.Z), "position invalide");
                }
            }
        }

        [Test]
        public void Le_Personnage_Est_A_Taille_Humaine_Et_Debout()
        {
            // LE test le plus parlant du lot, et celui qui a servi.
            //
            // Il a attrape un personnage de 1,7 CENTIMETRE : la transformation
            // du noeud etait appliquee a un maillage skinne, qui doit
            // l'ignorer. Indices bons, normales unitaires, rien d'invalide -
            // aucune assertion technique n'aurait bronche.
            Vector3 size = Model().Bounds.Size;

            ClassicAssert.Greater(size.Y, 1.2f, "trop petit pour un humain");
            ClassicAssert.Less(size.Y, 2.6f, "trop grand pour un humain");

            // Debout, donc bien plus haut que profond. On ne compare PAS a la
            // largeur : en pose en T, les bras ecartes sont aussi larges que
            // le personnage est haut.
            ClassicAssert.Greater(size.Y, size.Z * 2f, "il doit etre debout, pas couche");
        }

        [Test]
        public void Les_Coordonnees_De_Texture_Sont_Exploitables()
        {
            // Elles peuvent depasser [0,1] - une texture qui se repete est
            // legitime - mais pas de plusieurs ordres de grandeur.
            foreach (GltfPrimitive p in Model().Primitives)
            {
                foreach (Vector2 uv in p.TexCoords)
                {
                    ClassicAssert.IsFalse(float.IsNaN(uv.X) || float.IsNaN(uv.Y));
                    ClassicAssert.Less(MathF.Abs(uv.X), 100f);
                    ClassicAssert.Less(MathF.Abs(uv.Y), 100f);
                }
            }
        }

        // --- Materiaux et textures (M3) ---------------------------------------

        [Test]
        public void Le_Personnage_A_Un_Materiau_Avec_Sa_Texture()
        {
            GltfModel model = Model();

            ClassicAssert.GreaterOrEqual(model.Materials.Count, 1);
            ClassicAssert.GreaterOrEqual(model.Images.Count, 1);

            // Le chemin materiau -> texture -> image doit aboutir.
            GltfImage? image = model.BaseColorImageOf(model.Primitives[0]);

            ClassicAssert.IsNotNull(image, "le morceau doit trouver son image d'albedo");
            ClassicAssert.IsTrue(image!.IsPng, $"attendu du PNG, recu {image.MimeType}");
            ClassicAssert.Greater(image.Data.Length, 1000, "une texture n'a pas dix octets");
        }

        [Test]
        public void Notre_Decodeur_Png_Lit_La_Texture_Du_Personnage()
        {
            // LE test qui vaut le detour : notre decodeur PNG est ecrit a la
            // main. Il passe ses propres aller-retours, ce qui ne prouve rien
            // sur un fichier venu d'ailleurs - un encodeur tiers utilise
            // d'autres filtres de ligne et d'autres tailles de bloc.
            GltfImage? image = Model().BaseColorImageOf(Model().Primitives[0]);

            if (image is null)
                Assert.Ignore("Pas d'image : test ignore.");

            Image32 decoded = Png.Decode(image!.Data);

            ClassicAssert.Greater(decoded.Width, 16);
            ClassicAssert.Greater(decoded.Height, 16);
            ClassicAssert.AreEqual(decoded.Width * decoded.Height * 4, decoded.Pixels.Length);

            // Une texture entierement noire ou transparente signalerait un
            // decodage qui "reussit" sans rien produire.
            long sum = 0;
            for (int i = 0; i < decoded.Pixels.Length; i += 4)
                sum += decoded.Pixels[i] + decoded.Pixels[i + 1] + decoded.Pixels[i + 2];

            ClassicAssert.Greater(sum, 0, "image entierement noire : decodage suspect");
        }

        // --- Squelette et peau (M4) --------------------------------------------

        [Test]
        public void Le_Personnage_A_Un_Squelette()
        {
            GltfModel model = Model();

            ClassicAssert.AreEqual(1, model.Skins.Count);
            ClassicAssert.AreEqual(24, model.Skins[0].BoneCount, "24 os, comme annonce par l'inspection");
            ClassicAssert.AreEqual(24, model.Skins[0].InverseBindMatrices.Length,
                "une matrice de liaison par os, sinon on ne sait plus laquelle va avec lequel");

            // Sans ceci, La_Pose_De_Liaison_Se_Referme_Sur_Elle_Meme
            // passerait A VIDE : identite x identite fait identite. On exige
            // donc que ces matrices contiennent vraiment quelque chose.
            int moved = 0;
            foreach (Matrix4x4 m in model.Skins[0].InverseBindMatrices)
                if (m.Translation.Length() > 0.01f) moved++;

            ClassicAssert.Greater(moved, 15, "des matrices de liaison quasi identiques sont suspectes");
        }

        [Test]
        public void Chaque_Sommet_Est_Rattache_Au_Squelette()
        {
            GltfSkin skin = Model().Skins[0];

            foreach (GltfPrimitive p in Model().Primitives)
            {
                ClassicAssert.IsTrue(p.IsSkinned, "le maillage doit porter ses influences");
                ClassicAssert.AreEqual(p.Positions.Length, p.Influences!.Length);

                foreach (SkinInfluence influence in p.Influences)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        // Un indice hors bornes ferait lire une matrice au
                        // hasard : le sommet partirait a l'autre bout du monde.
                        ClassicAssert.Less(influence[k], skin.BoneCount, "os inexistant");
                    }
                }
            }
        }

        [Test]
        public void Les_Poids_De_Chaque_Sommet_Font_Un()
        {
            // La somme doit valoir 1, sinon le sommet retrecit ou s'envole.
            // C'est aussi le test qui attrape des poids lus en entiers
            // normalises et pris pour des flottants : la somme serait alors
            // de l'ordre du milliard.
            foreach (GltfPrimitive p in Model().Primitives)
            {
                foreach (SkinInfluence influence in p.Influences!)
                {
                    float sum = influence.Weights.X + influence.Weights.Y
                              + influence.Weights.Z + influence.Weights.W;

                    ClassicAssert.AreEqual(1f, sum, 1e-3f);
                }
            }
        }

        [Test]
        public void La_Pose_De_Liaison_Se_Referme_Sur_Elle_Meme()
        {
            // ============================================================
            //  LE test de M4, et le seul qui prouve vraiment quelque chose.
            //
            //  La matrice inverse de liaison ramene le maillage dans le
            //  repere d'un os TEL QU'IL ETAIT au repos. La transformation
            //  monde de cet os fait exactement le trajet inverse.
            //
            //  Les composer dans la pose de repos doit donc rendre
            //  l'IDENTITE. Si l'une des deux est mal lue - mauvaise
            //  convention de matrice, hierarchie parcourue de travers,
            //  matrices dans le desordre - le produit part n'importe ou.
            //
            //  Aucune verification de structure ne peut dire ca.
            // ============================================================
            GltfModel model = Model();
            GltfSkin skin = model.Skins[0];

            Matrix4x4[] world = model.ComputeWorldTransforms();

            for (int b = 0; b < skin.BoneCount; b++)
            {
                Matrix4x4 product = skin.InverseBindMatrices[b] * world[skin.Joints[b]];

                ClassicAssert.AreEqual(1f, product.M11, 2e-3f, $"os {b}");
                ClassicAssert.AreEqual(1f, product.M22, 2e-3f, $"os {b}");
                ClassicAssert.AreEqual(1f, product.M33, 2e-3f, $"os {b}");

                ClassicAssert.AreEqual(0f, product.M12, 2e-3f, $"os {b}");
                ClassicAssert.AreEqual(0f, product.M21, 2e-3f, $"os {b}");

                // La translation est la plus parlante : un os mal place se
                // trahit ici de plusieurs centimetres.
                ClassicAssert.AreEqual(0f, product.M41, 2e-3f, $"os {b} decale en X");
                ClassicAssert.AreEqual(0f, product.M42, 2e-3f, $"os {b} decale en Y");
                ClassicAssert.AreEqual(0f, product.M43, 2e-3f, $"os {b} decale en Z");
            }
        }

        [Test]
        public void Le_Squelette_Est_Un_Arbre_Sans_Cycle()
        {
            // Un cycle ferait tourner ComputeWorldTransforms indefiniment.
            GltfModel model = Model();
            var seen = new bool[model.Nodes.Count];

            void Walk(int index, int depth)
            {
                ClassicAssert.Less(depth, 64, "hierarchie anormalement profonde : cycle probable");
                ClassicAssert.IsFalse(seen[index], $"le noeud {index} a deux parents");

                seen[index] = true;

                foreach (int child in model.Nodes[index].Children)
                    Walk(child, depth + 1);
            }

            foreach (int r in model.Roots)
                Walk(r, 0);

            ClassicAssert.Greater(model.Nodes.Count, 20);
        }

        // --- Animations (M5) ----------------------------------------------------

        [Test]
        public void Le_Personnage_A_Ses_Deux_Animations()
        {
            GltfModel model = Model();

            ClassicAssert.AreEqual(2, model.Animations.Count);
            ClassicAssert.IsNotNull(model.FindAnimation("Running"));
            ClassicAssert.IsNotNull(model.FindAnimation("Walking"));

            foreach (GltfAnimation animation in model.Animations)
            {
                ClassicAssert.Greater(animation.Duration, 0.1f, $"{animation.Name} : duree nulle");
                ClassicAssert.Less(animation.Duration, 30f, $"{animation.Name} : duree aberrante");
                ClassicAssert.Greater(animation.Channels.Count, 10, $"{animation.Name} : trop peu de canaux");
            }
        }

        [Test]
        public void La_Pose_Change_Vraiment_Au_Cours_De_L_Animation()
        {
            // Sans ce test, tous les suivants passeraient A VIDE : une
            // animation qui ne fait rien satisfait toutes les proprietes de
            // bon comportement qu'on peut imaginer.
            GltfModel model = Model();
            GltfAnimation walking = model.FindAnimation("Walking")!;

            NodePose[] start = model.CreateRestPose();
            NodePose[] middle = model.CreateRestPose();

            walking.Sample(0f, start);
            walking.Sample(walking.Duration * 0.5f, middle);

            Matrix4x4[] a = model.ComputeWorldTransforms(start);
            Matrix4x4[] b = model.ComputeWorldTransforms(middle);

            float moved = 0f;
            for (int i = 0; i < a.Length; i++)
                moved = MathF.Max(moved, (a[i].Translation - b[i].Translation).Length());

            ClassicAssert.Greater(moved, 0.05f, "aucun os n'a bouge : l'animation ne fait rien");
        }

        [Test]
        public void Les_Rotations_Restent_Unitaires_A_Tout_Instant()
        {
            // Une interpolation lineaire naive entre deux quaternions rend un
            // resultat NON unitaire, qui met les os a l'echelle. Slerp plus
            // renormalisation ferment la porte.
            GltfModel model = Model();

            foreach (GltfAnimation animation in model.Animations)
            {
                NodePose[] pose = model.CreateRestPose();

                for (int step = 0; step <= 40; step++)
                {
                    animation.Sample(animation.Duration * step / 40f, pose);

                    foreach (NodePose node in pose)
                        ClassicAssert.AreEqual(1f, node.Rotation.Length(), 1e-3f,
                            $"{animation.Name} : quaternion non unitaire");
                }
            }
        }

        [Test]
        public void Le_Personnage_Reste_A_Taille_Humaine_Pendant_L_Animation()
        {
            // LE test de bon sens de cette etape, dans l'esprit de celui de
            // M2. Un chemin de quaternion pris a l'envers, une tangente mal
            // mise a l'echelle, un canal applique au mauvais os : le membre
            // part a l'autre bout de la scene. Aucune assertion technique ne
            // le dirait - les quaternions resteraient unitaires.
            GltfModel model = Model();
            GltfSkin skin = model.Skins[0];

            foreach (GltfAnimation animation in model.Animations)
            {
                NodePose[] pose = model.CreateRestPose();

                for (int step = 0; step <= 30; step++)
                {
                    animation.Sample(animation.Duration * step / 30f, pose);
                    Matrix4x4[] world = model.ComputeWorldTransforms(pose);

                    foreach (int joint in skin.Joints)
                    {
                        Vector3 p = world[joint].Translation;

                        ClassicAssert.Less(p.Length(), 4f,
                            $"{animation.Name} a t={step}/30 : un os a {p.Length():0.0} m de l'origine");
                    }
                }
            }
        }

        [Test]
        public void Une_Rotation_Prend_Le_Chemin_Le_Plus_Court()
        {
            // ============================================================
            //  Test ECRIT A LA MAIN, et pour une bonne raison.
            //
            //  Le garde-fou du chemin le plus court a d'abord ete verifie
            //  par mutation sur le vrai fichier : le desactiver ne cassait
            //  AUCUN test. Blender exporte ses quaternions dans un
            //  hemisphere coherent, donc le cas ne s'y presente jamais.
            //
            //  Le defaut existe pourtant, et un autre exportateur le
            //  declenchera. On fabrique donc le cas : q et -q decrivent la
            //  MEME rotation, mais interpoler vers le mauvais des deux fait
            //  parcourir 340 degres au lieu de 20.
            // ============================================================
            var start = Quaternion.Identity;

            // Meme rotation que 20 degres autour de Y, ecrite a l'envers.
            Quaternion target = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 20f * MathF.PI / 180f);
            Quaternion flipped = new(-target.X, -target.Y, -target.Z, -target.W);

            var channel = new GltfChannel
            {
                Node = 0,
                Path = GltfPath.Rotation,
                Interpolation = GltfInterpolation.Linear,
                Times = new[] { 0f, 1f },
                Values = new[]
                {
                    new Vector4(start.X, start.Y, start.Z, start.W),
                    new Vector4(flipped.X, flipped.Y, flipped.Z, flipped.W),
                },
            };

            var animation = new GltfAnimation
            {
                Name = "test",
                Channels = new[] { channel },
                Duration = 1f,
            };

            var pose = new NodePose[1];
            pose[0] = new NodePose { Rotation = Quaternion.Identity, Scale = Vector3.One };

            animation.Sample(0.5f, pose);

            // Angle parcouru : 2 * acos(|w|). Chemin court -> 10 degres.
            // Chemin long -> environ 170.
            float angle = 2f * MathF.Acos(Math.Clamp(MathF.Abs(pose[0].Rotation.W), -1f, 1f))
                        * 180f / MathF.PI;

            ClassicAssert.AreEqual(10f, angle, 1f, "l'interpolation doit passer par le petit arc");
        }

        [Test]
        public void Hors_Bornes_On_Tient_La_Premiere_Et_La_Derniere_Image()
        {
            GltfModel model = Model();
            GltfAnimation running = model.FindAnimation("Running")!;

            NodePose[] before = model.CreateRestPose();
            NodePose[] atZero = model.CreateRestPose();
            NodePose[] after = model.CreateRestPose();
            NodePose[] atEnd = model.CreateRestPose();

            running.Sample(-5f, before);
            running.Sample(0f, atZero);
            running.Sample(running.Duration + 5f, after);
            running.Sample(running.Duration, atEnd);

            for (int i = 0; i < before.Length; i++)
            {
                ClassicAssert.AreEqual(atZero[i].Translation.Y, before[i].Translation.Y, 1e-5f);
                ClassicAssert.AreEqual(atEnd[i].Translation.Y, after[i].Translation.Y, 1e-5f);
            }
        }

        [Test]
        public void En_Pose_De_Liaison_Les_Matrices_De_Peau_Sont_L_Identite()
        {
            // Le pendant de M4, mais en passant par le nouveau chemin : pose
            // -> transformations monde -> matrices de peau. Si CreateRestPose
            // ou ComputeSkinMatrices se trompe, ca se voit ici.
            GltfModel model = Model();

            Matrix4x4[] world = model.ComputeWorldTransforms(model.CreateRestPose());
            Matrix4x4[] bones = model.ComputeSkinMatrices(0, world);

            foreach (Matrix4x4 m in bones)
            {
                ClassicAssert.AreEqual(1f, m.M11, 2e-3f);
                ClassicAssert.AreEqual(1f, m.M22, 2e-3f);
                ClassicAssert.AreEqual(0f, m.M41, 2e-3f);
                ClassicAssert.AreEqual(0f, m.M42, 2e-3f);
                ClassicAssert.AreEqual(0f, m.M43, 2e-3f);
            }
        }

        [Test]
        public void Un_Glb_Vide_Ne_Fait_Pas_Planter()
        {
            // Un fichier valide mais sans scene doit rendre un modele vide,
            // pas lever.
            Glb glb = Glb.Parse(BuildMinimal("{\"asset\":{\"version\":\"2.0\"}}"));
            GltfModel model = GltfModel.FromGlb(glb);

            ClassicAssert.AreEqual(0, model.Primitives.Count);
            ClassicAssert.AreEqual(0, model.VertexCount);
        }

        private static byte[] BuildMinimal(string json)
        {
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(json);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(0x46546C67u);
            writer.Write(2u);
            writer.Write(0u);
            writer.Write((uint)payload.Length);
            writer.Write(0x4E4F534Au);
            writer.Write(payload);

            byte[] bytes = stream.ToArray();
            BitConverter.GetBytes((uint)bytes.Length).CopyTo(bytes, 8);
            return bytes;
        }

        private static string? FindModel()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir is not null)
            {
                var models = new DirectoryInfo(Path.Combine(dir.FullName, "Model3D"));

                if (models.Exists)
                {
                    foreach (FileInfo file in models.GetFiles("*.glb", SearchOption.AllDirectories))
                        if (file.Name.Contains("Merged_Animations", StringComparison.OrdinalIgnoreCase))
                            return file.FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }
    }
}
