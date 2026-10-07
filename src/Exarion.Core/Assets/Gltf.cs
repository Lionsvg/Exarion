// ===========================================================================
//  Exarion - Lecture de la geometrie glTF (M2)
//
//  Le JSON d'un glTF ne contient aucune donnee lourde. Il decrit des VUES
//  sur le bloc binaire, en trois etages :
//
//     buffer      le bloc d'octets entier
//     bufferView  une tranche : decalage, longueur, pas eventuel
//     accessor    comment lire cette tranche : type, nombre, format
//
//  Lire un maillage revient donc a suivre ces trois etages jusqu'aux
//  octets. Rien de plus. Toute la difficulte du format est ailleurs -
//  animations, peaux, extensions - et viendra plus tard.
//
//  ---------------------------------------------------------------------
//  DEUX CONVENTIONS A NE PAS CONFONDRE
//
//  1. glTF est en Y vers le haut, main droite : la meme convention
//     qu'OpenGL. Rien a convertir, contrairement a ce qu'on lit souvent.
//
//  2. Les matrices de glTF sont en COLONNES, celles de System.Numerics en
//     LIGNES. Les deux conventions sont transposees l'une de l'autre - et
//     transposer un tableau lu dans l'ordre revient a... le lire dans
//     l'ordre. C'est le seul endroit du fichier ou l'evidence est trompeuse.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Exarion.Core.Physics;

namespace Exarion.Core.Assets
{
    /// <summary>
    /// Les quatre os qui tirent sur un sommet, et avec quelle force.
    ///
    /// Quatre, c'est la limite du format et celle de toutes les cartes
    /// graphiques. C'est assez : au-dela, la contribution du cinquieme os
    /// est indiscernable.
    /// </summary>
    public readonly struct SkinInfluence
    {
        public readonly ushort Joint0, Joint1, Joint2, Joint3;
        public readonly Vector4 Weights;

        public SkinInfluence(ushort j0, ushort j1, ushort j2, ushort j3, Vector4 weights)
        {
            Joint0 = j0; Joint1 = j1; Joint2 = j2; Joint3 = j3;
            Weights = weights;
        }

        public ushort this[int i] => i switch { 0 => Joint0, 1 => Joint1, 2 => Joint2, _ => Joint3 };

        public float Weight(int i) => i switch
        {
            0 => Weights.X, 1 => Weights.Y, 2 => Weights.Z, _ => Weights.W,
        };
    }

    /// <summary>Un morceau de maillage : un materiau, un tas de triangles.</summary>
    public sealed class GltfPrimitive
    {
        public required Vector3[] Positions { get; init; }
        public required Vector3[] Normals { get; init; }
        public required Vector2[] TexCoords { get; init; }
        public required uint[] Indices { get; init; }

        /// <summary>Un par sommet, ou null si le morceau n'est pas skinne.</summary>
        public SkinInfluence[]? Influences { get; init; }

        /// <summary>Indice du materiau dans le fichier, ou -1.</summary>
        public int Material { get; init; } = -1;

        public int TriangleCount => Indices.Length / 3;
        public bool IsSkinned => Influences is not null;
    }

    /// <summary>
    /// Un noeud de la scene. Pour un squelette, c'est un os.
    ///
    /// On garde la decomposition translation / rotation / echelle SEPAREE,
    /// et pas seulement la matrice composee : une animation interpole ces
    /// trois canaux independamment. Recomposer puis redecomposer une matrice
    /// a chaque image serait couteux et imprecis.
    /// </summary>
    public sealed class GltfNode
    {
        public string Name { get; init; } = "";
        public int[] Children { get; init; } = Array.Empty<int>();

        public Vector3 Translation { get; init; } = Vector3.Zero;
        public Quaternion Rotation { get; init; } = Quaternion.Identity;
        public Vector3 Scale { get; init; } = Vector3.One;

        /// <summary>
        /// Vrai si le fichier donnait une matrice deja composee. La
        /// specification interdit alors d'animer ce noeud.
        /// </summary>
        public bool HasMatrix { get; init; }
        public Matrix4x4 Matrix { get; init; } = Matrix4x4.Identity;

        public int Mesh { get; init; } = -1;
        public int Skin { get; init; } = -1;

        public Matrix4x4 LocalTransform => HasMatrix
            ? Matrix
            : Matrix4x4.CreateScale(Scale)
            * Matrix4x4.CreateFromQuaternion(Rotation)
            * Matrix4x4.CreateTranslation(Translation);
    }

    /// <summary>
    /// Une peau : la liste des os, et pour chacun la matrice qui ramene le
    /// maillage dans SON repere.
    ///
    /// La matrice inverse de liaison est la piece qu'on comprend mal au
    /// debut. Le maillage est modelise une fois, dans une pose de repos. Pour
    /// qu'un os le deforme, il faut d'abord exprimer les sommets dans le
    /// repere de cet os TEL QU'IL ETAIT au repos - c'est exactement ce que
    /// fait cette matrice. Ensuite seulement on applique la pose courante.
    /// </summary>
    public sealed class GltfSkin
    {
        public required int[] Joints { get; init; }
        public required Matrix4x4[] InverseBindMatrices { get; init; }

        public int BoneCount => Joints.Length;
    }

    /// <summary>Une image embarquee, telle quelle. Le decodage vient apres.</summary>
    public sealed class GltfImage
    {
        public required string MimeType { get; init; }
        public required byte[] Data { get; init; }

        public bool IsPng => MimeType == "image/png";
    }

    /// <summary>
    /// Materiau glTF, modele "metallic-roughness" - le meme que celui du
    /// moteur, ce qui evite toute conversion.
    /// </summary>
    public sealed class GltfMaterial
    {
        public Vector4 BaseColorFactor { get; init; } = Vector4.One;
        public float MetallicFactor { get; init; } = 1f;
        public float RoughnessFactor { get; init; } = 1f;

        /// <summary>Indice dans <see cref="GltfModel.Images"/>, ou -1.</summary>
        public int BaseColorImage { get; init; } = -1;
    }

    /// <summary>Ce qu'un canal d'animation pilote sur un noeud.</summary>
    public enum GltfPath { Translation, Rotation, Scale }

    /// <summary>Comment relier deux images cles.</summary>
    public enum GltfInterpolation
    {
        /// <summary>Droit entre les deux. Spherique pour les rotations.</summary>
        Linear,

        /// <summary>Aucune transition : on garde la cle precedente.</summary>
        Step,

        /// <summary>Courbe de Hermite. Chaque cle porte ses deux tangentes.</summary>
        CubicSpline,
    }

    /// <summary>Pose d'un noeud : les trois canaux, non composes.</summary>
    public struct NodePose
    {
        public Vector3 Translation;
        public Quaternion Rotation;
        public Vector3 Scale;

        public Matrix4x4 ToMatrix()
            => Matrix4x4.CreateScale(Scale)
             * Matrix4x4.CreateFromQuaternion(Rotation)
             * Matrix4x4.CreateTranslation(Translation);
    }

    /// <summary>Un canal : un noeud, un attribut, une suite d'images cles.</summary>
    public sealed class GltfChannel
    {
        public int Node { get; init; } = -1;
        public GltfPath Path { get; init; }
        public GltfInterpolation Interpolation { get; init; }

        /// <summary>Instants des images cles, croissants, en secondes.</summary>
        public required float[] Times { get; init; }

        /// <summary>
        /// Valeurs. Un Vector3 occupe XYZ, un quaternion XYZW.
        /// En CubicSpline il y en a TROIS par image cle : tangente
        /// entrante, valeur, tangente sortante.
        /// </summary>
        public required Vector4[] Values { get; init; }
    }

    public sealed class GltfAnimation
    {
        public string Name { get; init; } = "";
        public required IReadOnlyList<GltfChannel> Channels { get; init; }

        /// <summary>Duree, en secondes : la derniere image cle de tous les canaux.</summary>
        public float Duration { get; init; }

        /// <summary>
        /// Applique l'animation a une pose. Les noeuds qu'aucun canal ne
        /// pilote gardent leur valeur : on part donc de la pose de repos.
        /// </summary>
        public void Sample(float time, NodePose[] pose)
        {
            foreach (GltfChannel channel in Channels)
            {
                if (channel.Node < 0 || channel.Node >= pose.Length || channel.Times.Length == 0)
                    continue;

                Vector4 value = SampleChannel(channel, time);

                switch (channel.Path)
                {
                    case GltfPath.Translation:
                        pose[channel.Node].Translation = new Vector3(value.X, value.Y, value.Z);
                        break;

                    case GltfPath.Rotation:
                        // Les erreurs d'interpolation s'accumulent : on
                        // renormalise, comme pour l'integration de R3.
                        pose[channel.Node].Rotation = Quaternion.Normalize(
                            new Quaternion(value.X, value.Y, value.Z, value.W));
                        break;

                    case GltfPath.Scale:
                        pose[channel.Node].Scale = new Vector3(value.X, value.Y, value.Z);
                        break;
                }
            }
        }

        private static Vector4 SampleChannel(GltfChannel channel, float time)
        {
            float[] times = channel.Times;
            int last = times.Length - 1;

            // Hors bornes : on tient la premiere ou la derniere valeur. Le
            // bouclage est l'affaire de l'appelant, qui seul sait si
            // l'animation tourne en rond.
            if (time <= times[0])
                return ValueAt(channel, 0);

            if (time >= times[last])
                return ValueAt(channel, last);

            // Recherche dichotomique : une animation peut avoir des
            // centaines d'images cles, et on echantillonne a chaque image.
            int low = 0, high = last;

            while (high - low > 1)
            {
                int middle = (low + high) / 2;

                if (times[middle] <= time) low = middle;
                else high = middle;
            }

            float span = times[high] - times[low];
            float t = span > 1e-9f ? (time - times[low]) / span : 0f;

            if (channel.Interpolation == GltfInterpolation.Step)
                return ValueAt(channel, low);

            if (channel.Interpolation == GltfInterpolation.CubicSpline)
                return Hermite(channel, low, high, t, span);

            Vector4 a = ValueAt(channel, low);
            Vector4 b = ValueAt(channel, high);

            if (channel.Path != GltfPath.Rotation)
                return Vector4.Lerp(a, b, t);

            // ------------------------------------------------------------
            // UNE ROTATION NE S'INTERPOLE PAS COMME UN POINT.
            //
            // Melanger deux quaternions lineairement fait passer le
            // resultat PAR L'INTERIEUR de la sphere : la rotation accelere
            // au milieu du trajet, et un membre semble se detendre d'un
            // coup. Slerp suit l'arc.
            //
            // Le CHEMIN LE PLUS COURT est l'autre piege : q et -q decrivent
            // la meme rotation, mais interpoler vers le mauvais des deux
            // fait faire au bras un tour complet dans le mauvais sens.
            //
            // On avait ajoute une negation manuelle pour ca. VERIFIE PAR
            // MUTATION : la retirer ne change rien, parce que le Slerp de
            // System.Numerics inverse deja le second quaternion quand leur
            // produit scalaire est negatif. Code mort, donc supprime - mais
            // le test Une_Rotation_Prend_Le_Chemin_Le_Plus_Court reste, il
            // verifie le comportement d'ou qu'il vienne.
            // ------------------------------------------------------------
            var qa = new Quaternion(a.X, a.Y, a.Z, a.W);
            var qb = new Quaternion(b.X, b.Y, b.Z, b.W);

            Quaternion result = Quaternion.Normalize(Quaternion.Slerp(qa, qb, t));

            return new Vector4(result.X, result.Y, result.Z, result.W);
        }

        private static Vector4 ValueAt(GltfChannel channel, int key)
            => channel.Interpolation == GltfInterpolation.CubicSpline
                ? channel.Values[key * 3 + 1]   // 0 = tangente entrante, 1 = valeur, 2 = sortante
                : channel.Values[key];

        /// <summary>
        /// Courbe de Hermite entre deux cles. Les tangentes sont exprimees
        /// par seconde : il faut donc les multiplier par la duree du
        /// segment, sinon la courbe s'emballe sur les segments courts.
        /// </summary>
        private static Vector4 Hermite(GltfChannel channel, int low, int high, float t, float span)
        {
            Vector4 p0 = channel.Values[low * 3 + 1];
            Vector4 m0 = channel.Values[low * 3 + 2] * span;
            Vector4 p1 = channel.Values[high * 3 + 1];
            Vector4 m1 = channel.Values[high * 3] * span;

            float t2 = t * t;
            float t3 = t2 * t;

            return p0 * (2f * t3 - 3f * t2 + 1f)
                 + m0 * (t3 - 2f * t2 + t)
                 + p1 * (-2f * t3 + 3f * t2)
                 + m1 * (t3 - t2);
        }
    }

    public sealed class GltfModel
    {
        public IReadOnlyList<GltfPrimitive> Primitives { get; }
        public IReadOnlyList<GltfMaterial> Materials { get; }
        public IReadOnlyList<GltfImage> Images { get; }
        public IReadOnlyList<GltfNode> Nodes { get; }
        public IReadOnlyList<GltfSkin> Skins { get; }
        public IReadOnlyList<GltfAnimation> Animations { get; }

        /// <summary>Racines de la scene : par ou commencer le parcours.</summary>
        public IReadOnlyList<int> Roots { get; }

        /// <summary>Emprise du modele entier, transformations appliquees.</summary>
        public Aabb Bounds { get; }

        private GltfModel(List<GltfPrimitive> primitives, List<GltfMaterial> materials,
                          List<GltfImage> images, List<GltfNode> nodes, List<GltfSkin> skins,
                          List<GltfAnimation> animations, List<int> roots, Aabb bounds)
        {
            Primitives = primitives;
            Materials = materials;
            Images = images;
            Nodes = nodes;
            Skins = skins;
            Animations = animations;
            Roots = roots;
            Bounds = bounds;
        }

        /// <summary>Pose de repos : celle decrite par les noeuds du fichier.</summary>
        public NodePose[] CreateRestPose()
        {
            var pose = new NodePose[Nodes.Count];

            for (int i = 0; i < pose.Length; i++)
            {
                pose[i] = new NodePose
                {
                    Translation = Nodes[i].Translation,
                    Rotation = Nodes[i].Rotation,
                    Scale = Nodes[i].Scale,
                };
            }

            return pose;
        }

        /// <summary>Animation par son nom, insensible a la casse.</summary>
        public GltfAnimation? FindAnimation(string name)
        {
            foreach (GltfAnimation animation in Animations)
                if (string.Equals(animation.Name, name, StringComparison.OrdinalIgnoreCase))
                    return animation;

            return null;
        }

        /// <summary>Transformations monde a partir d'une pose donnee.</summary>
        public Matrix4x4[] ComputeWorldTransforms(NodePose[] pose)
        {
            var world = new Matrix4x4[Nodes.Count];
            for (int i = 0; i < world.Length; i++)
                world[i] = Matrix4x4.Identity;

            foreach (int root in Roots)
                Accumulate(root, Matrix4x4.Identity, world, pose);

            return world;
        }

        private void Accumulate(int index, Matrix4x4 parent, Matrix4x4[] world, NodePose[]? pose)
        {
            if (index < 0 || index >= Nodes.Count)
                return;

            GltfNode node = Nodes[index];

            // Un noeud donne par une matrice composee n'est pas animable :
            // la specification l'interdit. On garde donc sa matrice telle
            // quelle, meme si une pose est fournie.
            Matrix4x4 local = pose is not null && !node.HasMatrix
                ? pose[index].ToMatrix()
                : node.LocalTransform;

            world[index] = local * parent;

            foreach (int child in node.Children)
                Accumulate(child, world[index], world, pose);
        }

        /// <summary>
        /// Matrices a envoyer au GPU : une par os.
        ///
        /// C'est le produit qui valait l'identite en pose de liaison (M4).
        /// Des que la pose change, il devient la deformation a appliquer.
        /// </summary>
        public Matrix4x4[] ComputeSkinMatrices(int skinIndex, Matrix4x4[] world)
        {
            GltfSkin skin = Skins[skinIndex];
            var matrices = new Matrix4x4[skin.BoneCount];

            for (int b = 0; b < skin.BoneCount; b++)
                matrices[b] = skin.InverseBindMatrices[b] * world[skin.Joints[b]];

            return matrices;
        }

        /// <summary>
        /// Transformations MONDE de tous les noeuds, dans la pose decrite par
        /// le fichier. C'est la base de tout le reste : une animation ne fera
        /// que modifier les noeuds avant ce calcul.
        /// </summary>
        public Matrix4x4[] ComputeWorldTransforms()
        {
            var world = new Matrix4x4[Nodes.Count];
            for (int i = 0; i < world.Length; i++)
                world[i] = Matrix4x4.Identity;

            foreach (int root in Roots)
                Accumulate(root, Matrix4x4.Identity, world);

            return world;
        }

        private void Accumulate(int index, Matrix4x4 parent, Matrix4x4[] world)
            => Accumulate(index, parent, world, null);

        /// <summary>Image d'albedo d'un morceau, ou null s'il n'en a pas.</summary>
        public GltfImage? BaseColorImageOf(GltfPrimitive primitive)
        {
            if (primitive.Material < 0 || primitive.Material >= Materials.Count)
                return null;

            int image = Materials[primitive.Material].BaseColorImage;

            return image >= 0 && image < Images.Count ? Images[image] : null;
        }

        public int VertexCount
        {
            get
            {
                int n = 0;
                foreach (GltfPrimitive p in Primitives) n += p.Positions.Length;
                return n;
            }
        }

        public int TriangleCount
        {
            get
            {
                int n = 0;
                foreach (GltfPrimitive p in Primitives) n += p.TriangleCount;
                return n;
            }
        }

        public static GltfModel Load(string path) => FromGlb(Glb.Load(path));

        public static GltfModel FromGlb(Glb glb)
        {
            using JsonDocument doc = JsonDocument.Parse(glb.Json);
            JsonElement root = doc.RootElement;

            ReadOnlySpan<byte> bin = glb.Binary.Span;

            var primitives = new List<GltfPrimitive>();
            var roots = new List<int>(SceneRoots(root));

            Vector3 min = new(float.MaxValue), max = new(float.MinValue);

            // On parcourt la scene depuis ses racines : un noeud porte une
            // transformation, et ses enfants la subissent. Prendre les
            // maillages sans passer par la hierarchie donnerait un modele
            // eparpille, chaque morceau a l'origine.
            foreach (int rootNode in roots)
                Visit(root, bin, rootNode, Matrix4x4.Identity, primitives, ref min, ref max);

            Aabb bounds = primitives.Count > 0 ? new Aabb(min, max) : default;

            return new GltfModel(primitives, ReadMaterials(root), ReadImages(root, bin),
                                 ReadNodes(root), ReadSkins(root, bin), ReadAnimations(root, bin),
                                 roots, bounds);
        }

        // -------------------------------------------------------------------
        //  Animations (M5)
        // -------------------------------------------------------------------

        private static List<GltfAnimation> ReadAnimations(JsonElement root, ReadOnlySpan<byte> bin)
        {
            var animations = new List<GltfAnimation>();

            if (!root.TryGetProperty("animations", out JsonElement list))
                return animations;

            int index = 0;

            foreach (JsonElement animation in list.EnumerateArray())
            {
                var channels = new List<GltfChannel>();
                float duration = 0f;

                JsonElement samplers = animation.GetProperty("samplers");

                foreach (JsonElement channel in animation.GetProperty("channels").EnumerateArray())
                {
                    if (!channel.TryGetProperty("target", out JsonElement target)
                        || !target.TryGetProperty("path", out JsonElement pathName))
                        continue;

                    GltfPath path;

                    switch (pathName.GetString())
                    {
                        case "translation": path = GltfPath.Translation; break;
                        case "rotation": path = GltfPath.Rotation; break;
                        case "scale": path = GltfPath.Scale; break;

                        // "weights" pilote les formes de fusion (morph
                        // targets). Notre personnage n'en a pas ; les
                        // ignorer vaut mieux que de lever.
                        default: continue;
                    }

                    JsonElement sampler = samplers[channel.GetProperty("sampler").GetInt32()];

                    GltfInterpolation interpolation = GltfInterpolation.Linear;

                    if (sampler.TryGetProperty("interpolation", out JsonElement mode))
                    {
                        interpolation = mode.GetString() switch
                        {
                            "STEP" => GltfInterpolation.Step,
                            "CUBICSPLINE" => GltfInterpolation.CubicSpline,
                            _ => GltfInterpolation.Linear,
                        };
                    }

                    float[] times = ReadScalarArray(root, bin, sampler.GetProperty("input").GetInt32());

                    // Une translation a 3 composantes, une rotation 4. On
                    // range tout en Vector4 : un seul type de tableau, et le
                    // W ignore ne coute rien.
                    int components = path == GltfPath.Rotation ? 4 : 3;

                    Vector4[] values = ReadVector4Array(root, bin,
                                                        sampler.GetProperty("output").GetInt32(),
                                                        components);

                    if (times.Length > 0)
                        duration = MathF.Max(duration, times[^1]);

                    channels.Add(new GltfChannel
                    {
                        Node = target.TryGetProperty("node", out JsonElement n) ? n.GetInt32() : -1,
                        Path = path,
                        Interpolation = interpolation,
                        Times = times,
                        Values = values,
                    });
                }

                animations.Add(new GltfAnimation
                {
                    Name = animation.TryGetProperty("name", out JsonElement name)
                        ? name.GetString() ?? $"animation{index}"
                        : $"animation{index}",
                    Channels = channels,
                    Duration = duration,
                });

                index++;
            }

            return animations;
        }

        // -------------------------------------------------------------------
        //  Squelette et peaux (M4)
        // -------------------------------------------------------------------

        private static List<GltfNode> ReadNodes(JsonElement root)
        {
            var nodes = new List<GltfNode>();

            if (!root.TryGetProperty("nodes", out JsonElement list))
                return nodes;

            foreach (JsonElement node in list.EnumerateArray())
            {
                int[] children = Array.Empty<int>();

                if (node.TryGetProperty("children", out JsonElement kids))
                {
                    children = new int[kids.GetArrayLength()];
                    int k = 0;
                    foreach (JsonElement child in kids.EnumerateArray())
                        children[k++] = child.GetInt32();
                }

                bool hasMatrix = node.TryGetProperty("matrix", out JsonElement m) && m.GetArrayLength() == 16;

                Quaternion rotation = Quaternion.Identity;
                if (node.TryGetProperty("rotation", out JsonElement r) && r.GetArrayLength() == 4)
                    rotation = new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle());

                nodes.Add(new GltfNode
                {
                    Name = node.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "",
                    Children = children,
                    Translation = ReadVector3(node, "translation", Vector3.Zero),
                    Rotation = rotation,
                    Scale = ReadVector3(node, "scale", Vector3.One),
                    HasMatrix = hasMatrix,
                    Matrix = hasMatrix ? LocalTransform(node) : Matrix4x4.Identity,
                    Mesh = node.TryGetProperty("mesh", out JsonElement me) ? me.GetInt32() : -1,
                    Skin = node.TryGetProperty("skin", out JsonElement sk) ? sk.GetInt32() : -1,
                });
            }

            return nodes;
        }

        private static List<GltfSkin> ReadSkins(JsonElement root, ReadOnlySpan<byte> bin)
        {
            var skins = new List<GltfSkin>();

            if (!root.TryGetProperty("skins", out JsonElement list))
                return skins;

            foreach (JsonElement skin in list.EnumerateArray())
            {
                JsonElement jointList = skin.GetProperty("joints");

                var joints = new int[jointList.GetArrayLength()];
                int j = 0;
                foreach (JsonElement joint in jointList.EnumerateArray())
                    joints[j++] = joint.GetInt32();

                // Sans matrices inverses de liaison, la specification dit de
                // prendre l'identite : le maillage est alors deja exprime
                // dans le repere de chaque os.
                Matrix4x4[] inverseBind;

                if (skin.TryGetProperty("inverseBindMatrices", out JsonElement accessor))
                {
                    inverseBind = ReadMatrixArray(root, bin, accessor.GetInt32());

                    if (inverseBind.Length != joints.Length)
                        throw new InvalidDataException(
                            $"glTF : {joints.Length} os mais {inverseBind.Length} matrices de liaison.");
                }
                else
                {
                    inverseBind = new Matrix4x4[joints.Length];
                    Array.Fill(inverseBind, Matrix4x4.Identity);
                }

                skins.Add(new GltfSkin { Joints = joints, InverseBindMatrices = inverseBind });
            }

            return skins;
        }

        // -------------------------------------------------------------------
        //  Materiaux et images
        // -------------------------------------------------------------------

        private static List<GltfMaterial> ReadMaterials(JsonElement root)
        {
            var materials = new List<GltfMaterial>();

            if (!root.TryGetProperty("materials", out JsonElement list))
                return materials;

            foreach (JsonElement material in list.EnumerateArray())
            {
                Vector4 baseColor = Vector4.One;
                float metallic = 1f, roughness = 1f;
                int image = -1;

                if (material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr))
                {
                    if (pbr.TryGetProperty("baseColorFactor", out JsonElement f) && f.GetArrayLength() == 4)
                        baseColor = new Vector4(f[0].GetSingle(), f[1].GetSingle(), f[2].GetSingle(), f[3].GetSingle());

                    if (pbr.TryGetProperty("metallicFactor", out JsonElement m))
                        metallic = m.GetSingle();

                    if (pbr.TryGetProperty("roughnessFactor", out JsonElement r))
                        roughness = r.GetSingle();

                    // Le chemin est indirect a dessein : plusieurs materiaux
                    // peuvent partager une texture, et plusieurs textures une
                    // meme image avec des reglages d'echantillonnage differents.
                    //     materiau -> texture -> image
                    if (pbr.TryGetProperty("baseColorTexture", out JsonElement bct)
                        && bct.TryGetProperty("index", out JsonElement textureIndex)
                        && root.TryGetProperty("textures", out JsonElement textures))
                    {
                        JsonElement texture = textures[textureIndex.GetInt32()];

                        if (texture.TryGetProperty("source", out JsonElement source))
                            image = source.GetInt32();
                    }
                }

                materials.Add(new GltfMaterial
                {
                    BaseColorFactor = baseColor,
                    MetallicFactor = metallic,
                    RoughnessFactor = roughness,
                    BaseColorImage = image,
                });
            }

            return materials;
        }

        private static List<GltfImage> ReadImages(JsonElement root, ReadOnlySpan<byte> bin)
        {
            var images = new List<GltfImage>();

            if (!root.TryGetProperty("images", out JsonElement list))
                return images;

            foreach (JsonElement image in list.EnumerateArray())
            {
                string mime = image.TryGetProperty("mimeType", out JsonElement m)
                    ? m.GetString() ?? "image/png"
                    : "image/png";

                byte[] data;

                if (image.TryGetProperty("bufferView", out JsonElement viewIndex))
                {
                    // Cas d'un GLB : l'image est un simple morceau du bloc.
                    JsonElement view = root.GetProperty("bufferViews")[viewIndex.GetInt32()];

                    int offset = view.TryGetProperty("byteOffset", out JsonElement o) ? o.GetInt32() : 0;
                    int length = view.GetProperty("byteLength").GetInt32();

                    if (offset < 0 || offset + length > bin.Length)
                        throw new InvalidDataException("glTF : image hors du bloc binaire.");

                    data = bin.Slice(offset, length).ToArray();
                }
                else if (image.TryGetProperty("uri", out JsonElement uri)
                         && uri.GetString() is string text
                         && text.StartsWith("data:", StringComparison.Ordinal))
                {
                    // Image en base64 dans l'URI. Frequent dans les .gltf
                    // texte, jamais dans un .glb - mais ca ne coute rien.
                    int comma = text.IndexOf(',');
                    if (comma < 0)
                        throw new InvalidDataException("glTF : URI de donnees malformee.");

                    data = Convert.FromBase64String(text[(comma + 1)..]);
                }
                else
                {
                    // Fichier externe : on ne va pas le chercher. Le format
                    // du projet est le GLB, tout doit y etre embarque.
                    throw new NotSupportedException(
                        "glTF : image dans un fichier externe. Exporter en .glb avec textures embarquees.");
                }

                images.Add(new GltfImage { MimeType = mime, Data = data });
            }

            return images;
        }

        // -------------------------------------------------------------------
        //  Hierarchie
        // -------------------------------------------------------------------

        private static IEnumerable<int> SceneRoots(JsonElement root)
        {
            int index = root.TryGetProperty("scene", out JsonElement s) ? s.GetInt32() : 0;

            if (!root.TryGetProperty("scenes", out JsonElement scenes) || scenes.GetArrayLength() == 0)
                yield break;

            if (index < 0 || index >= scenes.GetArrayLength())
                index = 0;

            if (!scenes[index].TryGetProperty("nodes", out JsonElement nodes))
                yield break;

            foreach (JsonElement node in nodes.EnumerateArray())
                yield return node.GetInt32();
        }

        private static void Visit(JsonElement root, ReadOnlySpan<byte> bin, int nodeIndex,
                                  Matrix4x4 parent, List<GltfPrimitive> output,
                                  ref Vector3 min, ref Vector3 max)
        {
            JsonElement node = root.GetProperty("nodes")[nodeIndex];

            Matrix4x4 world = LocalTransform(node) * parent;

            if (node.TryGetProperty("mesh", out JsonElement meshIndex))
            {
                // ------------------------------------------------------------
                // UN MAILLAGE SKINNE IGNORE LA TRANSFORMATION DE SON NOEUD.
                //
                // La specification glTF l'impose : ce sont les OS qui portent
                // la transformation, pas le noeud du maillage. Appliquer les
                // deux revient a la compter deux fois.
                //
                // Le symptome est spectaculaire et trompeur. Ce personnage a
                // un squelette exporte en CENTIMETRES - ses os se translatent
                // de 14, 36, 35 - et son noeud racine porte donc une echelle
                // de 0,01 pour rattraper. Le maillage, lui, est deja en
                // metres. En lui appliquant quand meme cette echelle, on
                // obtenait un personnage de 1,7 CENTIMETRE au lieu de 1,70 m.
                //
                // Aucune assertion technique ne l'aurait vu : les indices
                // etaient bons, les normales unitaires, rien d'invalide. Seul
                // le test "a taille humaine" l'a attrape.
                // ------------------------------------------------------------
                bool skinned = node.TryGetProperty("skin", out _);

                ReadMesh(root, bin, meshIndex.GetInt32(),
                         skinned ? Matrix4x4.Identity : world,
                         output, ref min, ref max);
            }

            if (node.TryGetProperty("children", out JsonElement children))
            {
                foreach (JsonElement child in children.EnumerateArray())
                    Visit(root, bin, child.GetInt32(), world, output, ref min, ref max);
            }
        }

        private static Matrix4x4 LocalTransform(JsonElement node)
        {
            // Forme compacte : une matrice deja composee, en colonnes. La
            // transposer revient a lire le tableau dans l'ordre - voir la
            // note en tete de fichier.
            if (node.TryGetProperty("matrix", out JsonElement m) && m.GetArrayLength() == 16)
            {
                Span<float> v = stackalloc float[16];
                for (int i = 0; i < 16; i++)
                    v[i] = m[i].GetSingle();

                return new Matrix4x4(v[0], v[1], v[2], v[3],
                                     v[4], v[5], v[6], v[7],
                                     v[8], v[9], v[10], v[11],
                                     v[12], v[13], v[14], v[15]);
            }

            Vector3 translation = ReadVector3(node, "translation", Vector3.Zero);
            Vector3 scale = ReadVector3(node, "scale", Vector3.One);

            Quaternion rotation = Quaternion.Identity;
            if (node.TryGetProperty("rotation", out JsonElement r) && r.GetArrayLength() == 4)
                rotation = new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle());

            // Ordre impose par la specification : echelle, puis rotation,
            // puis translation.
            return Matrix4x4.CreateScale(scale)
                 * Matrix4x4.CreateFromQuaternion(rotation)
                 * Matrix4x4.CreateTranslation(translation);
        }

        private static Vector3 ReadVector3(JsonElement node, string name, Vector3 fallback)
        {
            if (!node.TryGetProperty(name, out JsonElement v) || v.GetArrayLength() != 3)
                return fallback;

            return new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
        }

        // -------------------------------------------------------------------
        //  Maillages
        // -------------------------------------------------------------------

        private static void ReadMesh(JsonElement root, ReadOnlySpan<byte> bin, int meshIndex,
                                     Matrix4x4 world, List<GltfPrimitive> output,
                                     ref Vector3 min, ref Vector3 max)
        {
            JsonElement mesh = root.GetProperty("meshes")[meshIndex];

            if (!mesh.TryGetProperty("primitives", out JsonElement primitives))
                return;

            // Les normales ne se transforment pas comme les positions : une
            // echelle inegale les ferait pencher du mauvais cote. Il leur
            // faut la transposee de l'inverse.
            Matrix4x4 normalMatrix = Matrix4x4.Invert(world, out Matrix4x4 inverse)
                ? Matrix4x4.Transpose(inverse)
                : world;

            foreach (JsonElement primitive in primitives.EnumerateArray())
            {
                // Mode 4 = triangles. Les autres (lignes, points, bandes) ne
                // sont pas geres : mieux vaut ignorer que dessiner faux.
                if (primitive.TryGetProperty("mode", out JsonElement mode) && mode.GetInt32() != 4)
                    continue;

                if (!primitive.TryGetProperty("attributes", out JsonElement attributes))
                    continue;

                if (!attributes.TryGetProperty("POSITION", out JsonElement positionAccessor))
                    continue;

                Vector3[] positions = ReadVector3Array(root, bin, positionAccessor.GetInt32());

                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = Vector3.Transform(positions[i], world);
                    min = Vector3.Min(min, positions[i]);
                    max = Vector3.Max(max, positions[i]);
                }

                Vector3[] normals = attributes.TryGetProperty("NORMAL", out JsonElement normalAccessor)
                    ? ReadVector3Array(root, bin, normalAccessor.GetInt32())
                    : new Vector3[positions.Length];

                for (int i = 0; i < normals.Length; i++)
                {
                    Vector3 n = Vector3.TransformNormal(normals[i], normalMatrix);
                    normals[i] = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
                }

                Vector2[] uv = attributes.TryGetProperty("TEXCOORD_0", out JsonElement uvAccessor)
                    ? ReadVector2Array(root, bin, uvAccessor.GetInt32())
                    : new Vector2[positions.Length];

                uint[] indices = primitive.TryGetProperty("indices", out JsonElement indexAccessor)
                    ? ReadIndices(root, bin, indexAccessor.GetInt32())
                    : Sequence(positions.Length);

                SkinInfluence[]? influences = null;

                if (attributes.TryGetProperty("JOINTS_0", out JsonElement jointsAccessor)
                    && attributes.TryGetProperty("WEIGHTS_0", out JsonElement weightsAccessor))
                {
                    influences = ReadInfluences(root, bin,
                                                jointsAccessor.GetInt32(),
                                                weightsAccessor.GetInt32());
                }

                output.Add(new GltfPrimitive
                {
                    Positions = positions,
                    Normals = normals,
                    TexCoords = uv,
                    Indices = indices,
                    Influences = influences,
                    Material = primitive.TryGetProperty("material", out JsonElement mat) ? mat.GetInt32() : -1,
                });
            }
        }

        private static uint[] Sequence(int count)
        {
            var indices = new uint[count];
            for (int i = 0; i < count; i++) indices[i] = (uint)i;
            return indices;
        }

        // -------------------------------------------------------------------
        //  Accesseurs : les trois etages jusqu'aux octets
        // -------------------------------------------------------------------

        private const int ComponentUnsignedByte = 5121;
        private const int ComponentUnsignedShort = 5123;
        private const int ComponentUnsignedInt = 5125;
        private const int ComponentFloat = 5126;

        /// <summary>
        /// Resout un accesseur jusqu'a sa tranche d'octets, et rend le PAS
        /// entre deux elements.
        ///
        /// Le pas n'est pas toujours la taille de l'element : un fichier peut
        /// entrelacer position, normale et UV dans le meme tampon. L'ignorer
        /// donnerait un maillage explose, ce qui est le symptome classique
        /// d'un lecteur glTF ecrit trop vite.
        /// </summary>
        private static ReadOnlySpan<byte> Resolve(
            JsonElement root, ReadOnlySpan<byte> bin, int accessorIndex, int componentsPerElement,
            out int stride, out int count, out int componentType)
        {
            JsonElement accessor = root.GetProperty("accessors")[accessorIndex];

            if (accessor.TryGetProperty("sparse", out _))
                throw new NotSupportedException("Accesseur creux (sparse) : non gere.");

            count = accessor.GetProperty("count").GetInt32();
            componentType = accessor.GetProperty("componentType").GetInt32();
            int accessorOffset = accessor.TryGetProperty("byteOffset", out JsonElement ao) ? ao.GetInt32() : 0;

            if (!accessor.TryGetProperty("bufferView", out JsonElement viewIndex))
                throw new NotSupportedException("Accesseur sans bufferView : non gere.");

            JsonElement view = root.GetProperty("bufferViews")[viewIndex.GetInt32()];

            int viewOffset = view.TryGetProperty("byteOffset", out JsonElement vo) ? vo.GetInt32() : 0;
            int viewLength = view.GetProperty("byteLength").GetInt32();
            stride = view.TryGetProperty("byteStride", out JsonElement bs) ? bs.GetInt32() : 0;

            int elementSize = ComponentSize(componentType) * componentsPerElement;
            if (stride == 0)
                stride = elementSize;

            int start = viewOffset + accessorOffset;

            if (start < 0 || viewOffset + viewLength > bin.Length)
                throw new InvalidDataException("glTF : bufferView hors du bloc binaire.");

            int needed = (count - 1) * stride + elementSize;
            if (start + needed > bin.Length)
                throw new InvalidDataException("glTF : accesseur hors du bloc binaire.");

            return bin[start..];
        }

        private static int ComponentSize(int componentType) => componentType switch
        {
            5120 or ComponentUnsignedByte => 1,
            5122 or ComponentUnsignedShort => 2,
            ComponentUnsignedInt or ComponentFloat => 4,
            _ => throw new NotSupportedException($"Type de composante glTF inconnu : {componentType}."),
        };

        private static Vector3[] ReadVector3Array(JsonElement root, ReadOnlySpan<byte> bin, int accessor)
        {
            ReadOnlySpan<byte> data = Resolve(root, bin, accessor, 3, out int stride, out int count, out int componentType);

            if (componentType != ComponentFloat)
                throw new NotSupportedException("POSITION et NORMAL doivent etre en flottants.");

            var result = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> e = data[(i * stride)..];
                result[i] = new Vector3(
                    BitConverter.ToSingle(e),
                    BitConverter.ToSingle(e[4..]),
                    BitConverter.ToSingle(e[8..]));
            }

            return result;
        }

        private static Vector2[] ReadVector2Array(JsonElement root, ReadOnlySpan<byte> bin, int accessor)
        {
            ReadOnlySpan<byte> data = Resolve(root, bin, accessor, 2, out int stride, out int count, out int componentType);

            var result = new Vector2[count];

            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> e = data[(i * stride)..];

                // Les coordonnees de texture peuvent etre compressees en
                // entiers normalises : c'est frequent et parfaitement legal.
                result[i] = componentType switch
                {
                    ComponentFloat => new Vector2(BitConverter.ToSingle(e), BitConverter.ToSingle(e[4..])),
                    ComponentUnsignedByte => new Vector2(e[0] / 255f, e[1] / 255f),
                    ComponentUnsignedShort => new Vector2(
                        BitConverter.ToUInt16(e) / 65535f,
                        BitConverter.ToUInt16(e[2..]) / 65535f),
                    _ => throw new NotSupportedException($"TEXCOORD en {componentType} : non gere."),
                };
            }

            return result;
        }

        /// <summary>
        /// Matrices 4x4, en flottants. Le tableau glTF est en COLONNES et
        /// System.Numerics en LIGNES : les deux conventions sont transposees
        /// l'une de l'autre, donc lire dans l'ordre suffit. Voir la note en
        /// tete de fichier - c'est le seul endroit ou l'evidence est
        /// trompeuse.
        /// </summary>
        private static Matrix4x4[] ReadMatrixArray(JsonElement root, ReadOnlySpan<byte> bin, int accessor)
        {
            ReadOnlySpan<byte> data = Resolve(root, bin, accessor, 16, out int stride, out int count, out int componentType);

            if (componentType != ComponentFloat)
                throw new NotSupportedException("Les matrices doivent etre en flottants.");

            var result = new Matrix4x4[count];

            // Hors de la boucle : un stackalloc par tour s'accumulerait
            // jusqu'au retour de la methode, et deborderait la pile.
            Span<float> v = stackalloc float[16];

            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> e = data[(i * stride)..];

                for (int k = 0; k < 16; k++)
                    v[k] = BitConverter.ToSingle(e[(k * 4)..]);

                result[i] = new Matrix4x4(v[0], v[1], v[2], v[3],
                                          v[4], v[5], v[6], v[7],
                                          v[8], v[9], v[10], v[11],
                                          v[12], v[13], v[14], v[15]);
            }

            return result;
        }

        /// <summary>
        /// Os et poids d'influence, un quadruplet par sommet.
        ///
        /// Les indices d'os sont presque toujours compresses en octets ou en
        /// entiers courts - un squelette depasse rarement 255 os. Les poids
        /// peuvent l'etre aussi, et sont alors normalises.
        /// </summary>
        private static SkinInfluence[] ReadInfluences(JsonElement root, ReadOnlySpan<byte> bin,
                                                      int jointsAccessor, int weightsAccessor)
        {
            ReadOnlySpan<byte> jointData = Resolve(root, bin, jointsAccessor, 4,
                                                   out int jointStride, out int jointCount, out int jointType);

            ReadOnlySpan<byte> weightData = Resolve(root, bin, weightsAccessor, 4,
                                                    out int weightStride, out int weightCount, out int weightType);

            if (jointCount != weightCount)
                throw new InvalidDataException(
                    $"glTF : {jointCount} groupes d'os mais {weightCount} groupes de poids.");

            var result = new SkinInfluence[jointCount];

            // Hors de la boucle : voir ReadMatrixArray. Avec 8000 sommets,
            // un stackalloc par tour ferait deborder la pile.
            Span<ushort> bones = stackalloc ushort[4];
            Span<float> weights = stackalloc float[4];

            for (int i = 0; i < jointCount; i++)
            {
                ReadOnlySpan<byte> j = jointData[(i * jointStride)..];
                ReadOnlySpan<byte> w = weightData[(i * weightStride)..];

                for (int k = 0; k < 4; k++)
                {
                    bones[k] = jointType switch
                    {
                        ComponentUnsignedByte => j[k],
                        ComponentUnsignedShort => BitConverter.ToUInt16(j[(k * 2)..]),
                        _ => throw new NotSupportedException($"JOINTS_0 en {jointType} : non gere."),
                    };

                    weights[k] = weightType switch
                    {
                        ComponentFloat => BitConverter.ToSingle(w[(k * 4)..]),
                        ComponentUnsignedByte => w[k] / 255f,
                        ComponentUnsignedShort => BitConverter.ToUInt16(w[(k * 2)..]) / 65535f,
                        _ => throw new NotSupportedException($"WEIGHTS_0 en {weightType} : non gere."),
                    };
                }

                result[i] = new SkinInfluence(bones[0], bones[1], bones[2], bones[3],
                                              new Vector4(weights[0], weights[1], weights[2], weights[3]));
            }

            return result;
        }

        private static float[] ReadScalarArray(JsonElement root, ReadOnlySpan<byte> bin, int accessor)
        {
            ReadOnlySpan<byte> data = Resolve(root, bin, accessor, 1, out int stride, out int count, out int componentType);

            if (componentType != ComponentFloat)
                throw new NotSupportedException("Les instants d'images cles doivent etre en flottants.");

            var result = new float[count];

            for (int i = 0; i < count; i++)
                result[i] = BitConverter.ToSingle(data[(i * stride)..]);

            return result;
        }

        /// <summary>
        /// Valeurs d'animation, rangees en Vector4 quel que soit leur nombre
        /// de composantes. En CubicSpline l'accesseur en contient trois fois
        /// plus - tangente, valeur, tangente - et le pas les couvre toutes.
        /// </summary>
        private static Vector4[] ReadVector4Array(JsonElement root, ReadOnlySpan<byte> bin,
                                                  int accessor, int components)
        {
            ReadOnlySpan<byte> data = Resolve(root, bin, accessor, components,
                                              out int stride, out int count, out int componentType);

            if (componentType != ComponentFloat)
                throw new NotSupportedException("Les valeurs d'animation doivent etre en flottants.");

            var result = new Vector4[count];

            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> e = data[(i * stride)..];

                result[i] = new Vector4(
                    BitConverter.ToSingle(e),
                    BitConverter.ToSingle(e[4..]),
                    BitConverter.ToSingle(e[8..]),
                    components >= 4 ? BitConverter.ToSingle(e[12..]) : 0f);
            }

            return result;
        }

        private static uint[] ReadIndices(JsonElement root, ReadOnlySpan<byte> bin, int accessor)
        {
            ReadOnlySpan<byte> data = Resolve(root, bin, accessor, 1, out int stride, out int count, out int componentType);

            var result = new uint[count];

            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> e = data[(i * stride)..];

                result[i] = componentType switch
                {
                    ComponentUnsignedByte => e[0],
                    ComponentUnsignedShort => BitConverter.ToUInt16(e),
                    ComponentUnsignedInt => BitConverter.ToUInt32(e),
                    _ => throw new NotSupportedException($"Indices en {componentType} : non gere."),
                };
            }

            return result;
        }
    }
}
