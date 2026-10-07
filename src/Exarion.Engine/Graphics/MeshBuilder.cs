// ===========================================================================
//  Exarion - Construction de maillages
//
//  Genere la geometrie visible A PARTIR du volume de collision. C'etait un
//  choix delibere : impossible que ce que le joueur voit differe de ce qui
//  le bloque. Le "mur invisible" et le "mur qu'on traverse" sont deux des
//  bugs les plus penibles d'un FPS.
//
//  ⚠️⚠️ CETTE GARANTIE NE VAUT PLUS QUE POUR LE BLOC (17/08). Les formes
//  arrivees avec MapShape - sphere, cylindre, rampe, cone - se dessinent
//  DANS leur boite englobante, et c'est cette boite qui bloque. Une rampe
//  ne se monte donc pas : on bute dessus comme sur un mur de sa hauteur.
//
//  C'est une dette assumee et bornee : la forme ne DEBORDE jamais de sa
//  boite, donc la collision est toujours plus GENEREUSE que le visuel,
//  jamais plus permissive. Le pire defaut possible est « je suis bloque par
//  du vide », qui se voit tout de suite - pas « je traverse un mur », qui se
//  decouvre trois heures plus tard.
//
//  Chaque boite produit 24 sommets (4 par face) : une face a besoin de sa
//  propre normale, on ne peut donc pas partager les sommets des coins.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Exarion.Core.Assets;
using Exarion.Core.Physics;

namespace Exarion.Engine.Graphics
{
    public static class MeshBuilder
    {
        /// <summary>
        /// Convertit un modele glTF lu par Core en maillage GPU.
        ///
        /// Tous les morceaux sont FUSIONNES en un seul maillage. C'est
        /// suffisant tant qu'il n'y a qu'un materiau - le cas de notre
        /// personnage - et ca evite un appel de dessin par morceau. Il faudra
        /// les separer a l'etape M3, quand chaque materiau aura sa texture.
        /// </summary>
        public static Mesh FromGltf(GL gl, GltfModel model, Vector3 color)
        {
            bool skinned = model.Primitives.Count > 0 && model.Primitives[0].IsSkinned;

            var indices = new List<uint>(model.TriangleCount * 3);
            var rigid = skinned ? null : new List<Vertex>(model.VertexCount);
            var animated = skinned ? new List<SkinnedVertex>(model.VertexCount) : null;

            foreach (GltfPrimitive primitive in model.Primitives)
            {
                // Les indices d'un morceau comptent depuis SON debut. En les
                // concatenant, il faut les decaler du nombre de sommets deja
                // places - sinon chaque morceau redessine le premier.
                uint offset = (uint)(rigid?.Count ?? animated!.Count);

                for (int i = 0; i < primitive.Positions.Length; i++)
                {
                    if (rigid is not null)
                    {
                        rigid.Add(new Vertex(primitive.Positions[i], primitive.Normals[i],
                                             color, primitive.TexCoords[i]));
                        continue;
                    }

                    SkinInfluence influence = primitive.Influences![i];

                    animated!.Add(new SkinnedVertex(
                        primitive.Positions[i],
                        primitive.Normals[i],
                        color,
                        primitive.TexCoords[i],
                        new Vector4(influence.Joint0, influence.Joint1, influence.Joint2, influence.Joint3),
                        influence.Weights));
                }

                foreach (uint index in primitive.Indices)
                    indices.Add(index + offset);
            }

            return rigid is not null
                ? new Mesh(gl, CollectionsMarshal.AsSpan(rigid), CollectionsMarshal.AsSpan(indices))
                : new Mesh(gl, CollectionsMarshal.AsSpan(animated!), CollectionsMarshal.AsSpan(indices));
        }

        /// <summary>Les 6 normales de faces, dans l'ordre utilise ci-dessous.</summary>
        private static readonly Vector3[] FaceNormals =
        {
            new Vector3( 0f,  1f,  0f),   // haut
            new Vector3( 0f, -1f,  0f),   // bas
            new Vector3( 0f,  0f,  1f),   // avant (+Z)
            new Vector3( 0f,  0f, -1f),   // arriere (-Z)
            new Vector3( 1f,  0f,  0f),   // droite (+X)
            new Vector3(-1f,  0f,  0f),   // gauche (-X)
        };

        /// <summary>
        /// Repetitions de texture par metre. 1/8 avec une grille de 8 cases
        /// donne une case par metre : l'echelle du decor devient lisible.
        /// </summary>
        public const float DefaultUvPerMeter = 1f / 8f;

        /// <summary>
        /// Coordonnees de texture d'un point, deduites de sa POSITION MONDE
        /// et de l'orientation de la face.
        ///
        /// C'est une projection planaire par axe. Consequence recherchee : la
        /// densite de texels est la meme partout, quelle que soit la taille de
        /// la boite. Un mur de 20 m et une caisse de 1 m ont des carreaux
        /// identiques - sans quoi la grille ne dirait plus rien de l'echelle.
        /// </summary>
        private static Vector2 WorldUv(Vector3 position, Vector3 normal, float uvPerMeter)
        {
            float ax = MathF.Abs(normal.X);
            float ay = MathF.Abs(normal.Y);
            float az = MathF.Abs(normal.Z);

            Vector2 uv;
            if (ay >= ax && ay >= az)
                uv = new Vector2(position.X, position.Z);   // sol et plafond
            else if (ax >= az)
                uv = new Vector2(position.Z, position.Y);   // murs orientes X
            else
                uv = new Vector2(position.X, position.Y);   // murs orientes Z

            return uv * uvPerMeter;
        }

        /// <summary>Ajoute une boite au tampon, en sens antihoraire vu de l'exterieur.</summary>
        public static void AddBox(List<Vertex> vertices, List<uint> indices, in Aabb box, Vector3 color)
            => AddBox(vertices, indices, box, color, DefaultUvPerMeter);

        public static void AddBox(List<Vertex> vertices, List<uint> indices, in Aabb box,
                                  Vector3 color, float uvPerMeter)
        {
            Vector3 lo = box.Min;
            Vector3 hi = box.Max;

            // Les 8 coins.
            var c = new Vector3[8]
            {
                new Vector3(lo.X, lo.Y, lo.Z),   // 0
                new Vector3(hi.X, lo.Y, lo.Z),   // 1
                new Vector3(hi.X, lo.Y, hi.Z),   // 2
                new Vector3(lo.X, lo.Y, hi.Z),   // 3
                new Vector3(lo.X, hi.Y, lo.Z),   // 4
                new Vector3(hi.X, hi.Y, lo.Z),   // 5
                new Vector3(hi.X, hi.Y, hi.Z),   // 6
                new Vector3(lo.X, hi.Y, hi.Z),   // 7
            };

            // Chaque face, dans l'ordre de FaceNormals.
            int[][] faces =
            {
                new[] { 4, 7, 6, 5 },   // haut     (+Y)
                new[] { 3, 0, 1, 2 },   // bas      (-Y)
                new[] { 7, 3, 2, 6 },   // avant    (+Z)
                new[] { 0, 4, 5, 1 },   // arriere  (-Z)
                new[] { 5, 6, 2, 1 },   // droite   (+X)
                new[] { 4, 0, 3, 7 },   // gauche   (-X)
            };

            for (int f = 0; f < 6; f++)
            {
                uint baseIndex = (uint)vertices.Count;
                Vector3 n = FaceNormals[f];

                // Legere variation de teinte par orientation : sans ombres,
                // c'est ce qui rend le relief lisible en greybox.
                float shade = 0.72f + 0.28f * System.MathF.Abs(Vector3.Dot(n, new Vector3(0.35f, 0.85f, 0.4f)));
                Vector3 shaded = color * shade;

                for (int v = 0; v < 4; v++)
                {
                    Vector3 p = c[faces[f][v]];
                    vertices.Add(new Vertex(p, n, shaded, WorldUv(p, n, uvPerMeter)));
                }

                indices.Add(baseIndex + 0);
                indices.Add(baseIndex + 1);
                indices.Add(baseIndex + 2);
                indices.Add(baseIndex + 0);
                indices.Add(baseIndex + 2);
                indices.Add(baseIndex + 3);
            }
        }

        // ===================================================================
        //  Formes autres que la boite
        //
        //  ⚠️⚠️ CES FORMES SONT DECORATIVES. La collision reste la boite
        //  englobante, comme l'explique MapShape : une sphere se cogne a son
        //  cube, une rampe ne se monte pas. L'en-tete de ce fichier affirme
        //  qu'on ne peut pas voir autre chose que ce qui bloque - c'est
        //  desormais FAUX pour tout ce qui n'est pas un Block, et c'est un
        //  choix, pas un oubli. La collision convexe est un chantier a part.
        //
        //  ⚠️ Toutes s'inscrivent dans l'AABB et n'en sortent JAMAIS. C'est ce
        //  qui permet de garder le meme centre et la meme taille en changeant
        //  de forme, et ce qui rend la boite englobante utilisable comme
        //  collision approchee plutot que fausse.
        // ===================================================================

        /// <summary>Nombre de secteurs autour de l'axe. 20 : la silhouette ne montre plus ses aretes.</summary>
        private const int RadialSegments = 20;

        /// <summary>Empilements d'une sphere, du pole sud au pole nord.</summary>
        private const int SphereStacks = 12;

        /// <summary>Teinte de face, identique a celle de AddBox : le relief reste lisible en greybox.</summary>
        private static Vector3 Shade(Vector3 color, Vector3 normal)
            => color * (0.72f + 0.28f * MathF.Abs(Vector3.Dot(normal, new Vector3(0.35f, 0.85f, 0.4f))));

        /// <summary>
        /// Ajoute un polygone convexe, triangule en eventail.
        ///
        /// Factorise parce que les cinq formes en posent : sans ca, chacune
        /// recopierait la meme boucle d'indices et le meme calcul de teinte -
        /// donc cinq endroits pour se tromper de sens de parcours, et des
        /// faces retournees introuvables a l'oeil (on ne voit qu'un trou).
        /// </summary>
        private static void AddPolygon(List<Vertex> vertices, List<uint> indices,
                                       ReadOnlySpan<Vector3> points, Vector3 normal,
                                       Vector3 color, float uvPerMeter)
        {
            if (points.Length < 3)
                return;

            uint baseIndex = (uint)vertices.Count;
            Vector3 shaded = Shade(color, normal);

            foreach (Vector3 p in points)
                vertices.Add(new Vertex(p, normal, shaded, WorldUv(p, normal, uvPerMeter)));

            for (int i = 1; i + 1 < points.Length; i++)
            {
                indices.Add(baseIndex);
                indices.Add(baseIndex + (uint)i);
                indices.Add(baseIndex + (uint)i + 1);
            }
        }

        /// <summary>Un sommet a normale LISSE : les formes rondes ne doivent pas montrer leurs facettes.</summary>
        private static void AddSmooth(List<Vertex> vertices, Vector3 position, Vector3 normal,
                                      Vector3 color, float uvPerMeter)
            => vertices.Add(new Vertex(position, normal, Shade(color, normal),
                                       WorldUv(position, normal, uvPerMeter)));

        private static void Quad(List<uint> indices, uint a, uint b, uint c, uint d)
        {
            indices.Add(a); indices.Add(b); indices.Add(c);
            indices.Add(a); indices.Add(c); indices.Add(d);
        }

        /// <summary>
        /// Ellipsoide inscrit dans la boite.
        ///
        /// ⚠️ LA NORMALE D'UN ELLIPSOIDE N'EST PAS LA DIRECTION DU CENTRE VERS
        /// LE POINT. Pour un rayon (a,b,c) elle vaut (x/a², y/b², z/c²)
        /// normalise. Sur une boule (a=b=c) les deux coincident, donc l'erreur
        /// ne se verrait JAMAIS sur une sphere de test - seulement sur une
        /// boite aplatie, sous forme d'un eclairage un peu faux. Meme classe de
        /// piege que la matrice des normales de R6.
        /// </summary>
        public static void AddSphere(List<Vertex> vertices, List<uint> indices, in Aabb box,
                                     Vector3 color, float uvPerMeter)
        {
            Vector3 center = (box.Min + box.Max) * 0.5f;
            Vector3 radius = (box.Max - box.Min) * 0.5f;

            // Un rayon nul rendrait la normale indefinie ; on borne plutot que
            // de produire des NaN qui feraient disparaitre la forme en silence.
            radius = Vector3.Max(radius, new Vector3(1e-4f));

            uint baseIndex = (uint)vertices.Count;

            for (int stack = 0; stack <= SphereStacks; stack++)
            {
                float phi = MathF.PI * stack / SphereStacks;      // 0 = pole bas
                float sinPhi = MathF.Sin(phi), cosPhi = MathF.Cos(phi);

                for (int seg = 0; seg <= RadialSegments; seg++)
                {
                    float theta = MathF.Tau * seg / RadialSegments;
                    var unit = new Vector3(sinPhi * MathF.Cos(theta), -cosPhi, sinPhi * MathF.Sin(theta));

                    Vector3 position = center + unit * radius;
                    var normal = Vector3.Normalize(new Vector3(unit.X / radius.X,
                                                               unit.Y / radius.Y,
                                                               unit.Z / radius.Z));

                    AddSmooth(vertices, position, normal, color, uvPerMeter);
                }
            }

            uint stride = RadialSegments + 1;

            for (uint stack = 0; stack < SphereStacks; stack++)
            {
                for (uint seg = 0; seg < RadialSegments; seg++)
                {
                    uint a = baseIndex + stack * stride + seg;
                    Quad(indices, a, a + stride, a + stride + 1, a + 1);
                }
            }
        }

        /// <summary>Cylindre d'axe Y, inscrit dans la boite. Section elliptique si la boite ne l'est pas.</summary>
        public static void AddCylinder(List<Vertex> vertices, List<uint> indices, in Aabb box,
                                       Vector3 color, float uvPerMeter)
        {
            Vector3 center = (box.Min + box.Max) * 0.5f;
            float rx = MathF.Max((box.Max.X - box.Min.X) * 0.5f, 1e-4f);
            float rz = MathF.Max((box.Max.Z - box.Min.Z) * 0.5f, 1e-4f);

            uint side = (uint)vertices.Count;

            // --- Paroi : deux sommets par secteur, normale horizontale lisse.
            for (int seg = 0; seg <= RadialSegments; seg++)
            {
                float theta = MathF.Tau * seg / RadialSegments;
                float cx = MathF.Cos(theta), sz = MathF.Sin(theta);

                // Normale d'une ellipse : (x/rx², z/rz²), pas (x, z).
                var normal = Vector3.Normalize(new Vector3(cx / rx, 0f, sz / rz));

                AddSmooth(vertices, new Vector3(center.X + cx * rx, box.Min.Y, center.Z + sz * rz),
                          normal, color, uvPerMeter);
                AddSmooth(vertices, new Vector3(center.X + cx * rx, box.Max.Y, center.Z + sz * rz),
                          normal, color, uvPerMeter);
            }

            for (uint seg = 0; seg < RadialSegments; seg++)
            {
                uint a = side + seg * 2;
                Quad(indices, a, a + 2, a + 3, a + 1);
            }

            // --- Couvercles. Un disque par eventail : le centre puis le bord.
            AddDisc(vertices, indices, center, rx, rz, box.Max.Y, up: true, color, uvPerMeter);
            AddDisc(vertices, indices, center, rx, rz, box.Min.Y, up: false, color, uvPerMeter);
        }

        private static void AddDisc(List<Vertex> vertices, List<uint> indices, Vector3 center,
                                    float rx, float rz, float y, bool up, Vector3 color, float uvPerMeter)
        {
            Vector3 normal = up ? Vector3.UnitY : -Vector3.UnitY;
            uint hub = (uint)vertices.Count;

            AddSmooth(vertices, new Vector3(center.X, y, center.Z), normal, color, uvPerMeter);

            for (int seg = 0; seg <= RadialSegments; seg++)
            {
                float theta = MathF.Tau * seg / RadialSegments;
                AddSmooth(vertices,
                          new Vector3(center.X + MathF.Cos(theta) * rx, y, center.Z + MathF.Sin(theta) * rz),
                          normal, color, uvPerMeter);
            }

            for (uint seg = 0; seg < RadialSegments; seg++)
            {
                // ⚠️ Le sens de parcours s'INVERSE entre le dessus et le
                // dessous. Le meme pour les deux donnerait un couvercle
                // invisible de l'exterieur - et parfaitement visible depuis
                // l'interieur, ce qui se lit comme « le cylindre est troue ».
                if (up)
                {
                    indices.Add(hub); indices.Add(hub + seg + 1); indices.Add(hub + seg + 2);
                }
                else
                {
                    indices.Add(hub); indices.Add(hub + seg + 2); indices.Add(hub + seg + 1);
                }
            }
        }

        /// <summary>Cone d'axe Y, pointe en haut, inscrit dans la boite.</summary>
        public static void AddCone(List<Vertex> vertices, List<uint> indices, in Aabb box,
                                   Vector3 color, float uvPerMeter)
        {
            Vector3 center = (box.Min + box.Max) * 0.5f;
            float rx = MathF.Max((box.Max.X - box.Min.X) * 0.5f, 1e-4f);
            float rz = MathF.Max((box.Max.Z - box.Min.Z) * 0.5f, 1e-4f);
            float height = MathF.Max(box.Max.Y - box.Min.Y, 1e-4f);

            var apex = new Vector3(center.X, box.Max.Y, center.Z);
            uint start = (uint)vertices.Count;

            for (int seg = 0; seg <= RadialSegments; seg++)
            {
                float theta = MathF.Tau * seg / RadialSegments;
                float cx = MathF.Cos(theta), sz = MathF.Sin(theta);

                // La normale du flanc penche : sa composante verticale vaut le
                // rayon divise par la hauteur. La prendre horizontale ferait un
                // cone eclaire comme un cylindre - un aplat, sans pointe.
                var normal = Vector3.Normalize(new Vector3(cx * height / rx, MathF.Max(rx, rz) / height,
                                                           sz * height / rz));

                AddSmooth(vertices, new Vector3(center.X + cx * rx, box.Min.Y, center.Z + sz * rz),
                          normal, color, uvPerMeter);
                AddSmooth(vertices, apex, normal, color, uvPerMeter);
            }

            for (uint seg = 0; seg < RadialSegments; seg++)
            {
                uint a = start + seg * 2;
                indices.Add(a); indices.Add(a + 2); indices.Add(a + 1);
            }

            AddDisc(vertices, indices, center, rx, rz, box.Min.Y, up: false, color, uvPerMeter);
        }

        /// <summary>
        /// Coin : le dessus descend du bord -Z (haut) vers le bord +Z (bas).
        ///
        /// L'orientation est fixe faute de rotation dans le format - meme
        /// limite que func_door, qui ne pivote pas non plus. On tourne une
        /// rampe en echangeant sa taille en X et en Z... ce qui ne la retourne
        /// PAS. C'est la limite la plus genante de ce lot, et elle est ecrite
        /// ici plutot que decouverte en posant un escalier.
        /// </summary>
        public static void AddWedge(List<Vertex> vertices, List<uint> indices, in Aabb box,
                                    Vector3 color, float uvPerMeter)
        {
            Vector3 lo = box.Min, hi = box.Max;

            var a = new Vector3(lo.X, lo.Y, lo.Z);   // bas arriere gauche
            var b = new Vector3(hi.X, lo.Y, lo.Z);   // bas arriere droit
            var c = new Vector3(hi.X, lo.Y, hi.Z);   // bas avant droit
            var d = new Vector3(lo.X, lo.Y, hi.Z);   // bas avant gauche
            var e = new Vector3(lo.X, hi.Y, lo.Z);   // haut arriere gauche
            var f = new Vector3(hi.X, hi.Y, lo.Z);   // haut arriere droit

            Span<Vector3> quad = stackalloc Vector3[4];
            Span<Vector3> tri = stackalloc Vector3[3];

            // Dessous.
            quad[0] = d; quad[1] = a; quad[2] = b; quad[3] = c;
            AddPolygon(vertices, indices, quad, -Vector3.UnitY, color, uvPerMeter);

            // Dos vertical, en -Z.
            quad[0] = a; quad[1] = e; quad[2] = f; quad[3] = b;
            AddPolygon(vertices, indices, quad, -Vector3.UnitZ, color, uvPerMeter);

            // La pente. Sa normale se CALCULE : l'ecrire a la main donnerait un
            // eclairage faux des que la boite n'est pas cubique, et l'erreur
            // serait invisible sur le cube de test.
            float depth = MathF.Max(hi.Z - lo.Z, 1e-4f);
            float height = MathF.Max(hi.Y - lo.Y, 1e-4f);
            var slope = Vector3.Normalize(new Vector3(0f, depth, height));

            quad[0] = e; quad[1] = d; quad[2] = c; quad[3] = f;
            AddPolygon(vertices, indices, quad, slope, color, uvPerMeter);

            // Les deux flancs triangulaires.
            tri[0] = a; tri[1] = d; tri[2] = e;
            AddPolygon(vertices, indices, tri, -Vector3.UnitX, color, uvPerMeter);

            tri[0] = b; tri[1] = f; tri[2] = c;
            AddPolygon(vertices, indices, tri, Vector3.UnitX, color, uvPerMeter);
        }

        /// <summary>
        /// Ajoute la forme demandee dans les bornes donnees.
        ///
        /// Un SEUL point d'entree : le jour ou une forme s'ajoute, l'editeur,
        /// le decor et les marqueurs la connaissent tous les trois sans
        /// modification. Trois aiguillages separes divergeraient.
        /// </summary>
        public static void AddShape(List<Vertex> vertices, List<uint> indices, in Aabb box,
                                    Vector3 color, MapShape shape, float uvPerMeter = DefaultUvPerMeter)
        {
            switch (shape)
            {
                case MapShape.Sphere: AddSphere(vertices, indices, box, color, uvPerMeter); break;
                case MapShape.Cylinder: AddCylinder(vertices, indices, box, color, uvPerMeter); break;
                case MapShape.Wedge: AddWedge(vertices, indices, box, color, uvPerMeter); break;
                case MapShape.Cone: AddCone(vertices, indices, box, color, uvPerMeter); break;
                default: AddBox(vertices, indices, box, color, uvPerMeter); break;
            }
        }

        /// <summary>
        /// Construit un maillage unique pour tout le monde de collision.
        /// Un seul appel de dessin pour la map entiere : suffisant tant qu'on
        /// est en greybox, on decoupera quand il y aura du culling.
        ///
        /// ⚠️ Cette surcharge ne connait que des AABB, donc que des blocs.
        /// C'est ce qu'il faut pour un jeu qui construit son decor a la main -
        /// le jeu d'exemple - et ca evite de lui imposer MapData. Le decor
        /// d'une CARTE passe par FromMap, qui seul voit les formes.
        /// </summary>
        public static Mesh FromCollisionWorld(GL gl, CollisionWorld world, IReadOnlyList<Vector3>? colors = null)
        {
            var vertices = new List<Vertex>();
            var indices = new List<uint>();

            for (int i = 0; i < world.Boxes.Count; i++)
            {
                Vector3 color = colors != null && i < colors.Count
                    ? colors[i]
                    : new Vector3(0.52f, 0.54f, 0.58f);

                AddBox(vertices, indices, world.Boxes[i], color);
            }

            return new Mesh(gl, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vertices),
                                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(indices));
        }

        /// <summary>
        /// Construit le decor visible d'une CARTE, formes comprises.
        ///
        /// ⚠️ On lit MapData et non le CollisionWorld qui en derive : ce
        /// dernier ne porte que des AABB, il a donc DEJA perdu la forme. C'est
        /// exactement la raison pour laquelle le decor et la collision se
        /// separent ici - et la raison pour laquelle il faut le dire fort.
        /// </summary>
        public static Mesh FromMap(GL gl, MapData map)
        {
            System.ArgumentNullException.ThrowIfNull(map);

            var vertices = new List<Vertex>();
            var indices = new List<uint>();

            foreach (MapBox box in map.Boxes)
                AddShape(vertices, indices, Aabb.FromCenterSize(box.Center, box.Size), box.Color, box.Shape);

            return new Mesh(gl, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vertices),
                                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(indices));
        }

        /// <summary>
        /// Un maillage isole pour UNE forme, centre sur l'origine et de taille 1.
        ///
        /// Sert aux marqueurs de l'editeur et a l'apercu : on le redessine avec
        /// une matrice, comme le cube unitaire des caisses, plutot que de
        /// reconstruire un maillage par objet a chaque image.
        /// </summary>
        public static Mesh UnitShape(GL gl, MapShape shape, Vector3 color)
        {
            var vertices = new List<Vertex>();
            var indices = new List<uint>();

            AddShape(vertices, indices,
                     Aabb.FromCenterSize(Vector3.Zero, Vector3.One), color, shape, uvPerMeter: 1f);

            return new Mesh(gl, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vertices),
                                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(indices));
        }
    }
}
