// ===========================================================================
//  Marqueurs d'entites - une SILHOUETTE par type
//
//  ⚠️⚠️ LE DEFAUT QUE CECI CORRIGE, ET IL EST DE CONCEPTION.
//  Jusqu'au 17/08, les six types partageaient le MEME cube de 60 cm,
//  distingues par la seule couleur. Poser un PNJ et poser un checkpoint
//  donnaient donc deux objets identiques, et le volume reellement couvert par
//  un declencheur - sa propriete « size », jusqu'a 3 m de cote par defaut -
//  n'apparaissait NULLE PART.
//
//  On ne pouvait pas voir ce qu'on editait : le cube ne disait ni la nature,
//  ni la portee, ni l'orientation de ce qu'il representait.
//
//  Tout est bati a partir des primitives de MapShape. Aucun modele a charger,
//  donc un marqueur ne peut pas manquer parce qu'un fichier d'asset est
//  absent - contrairement au personnage glTF, qui lui est facultatif au
//  demarrage.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Assets;
using Exarion.Core.Editor;
using Exarion.Core.Entities;
using Exarion.Core.Physics;
using Exarion.Engine.Graphics;
using Silk.NET.OpenGL;

namespace Exarion.Game.Rendering
{
    internal sealed class MarkerRenderer : IDisposable
    {
        /// <summary>
        /// Une forme unitaire par MapShape, indexee par la valeur de l'enum.
        ///
        /// Construites UNE fois et redessinees avec une matrice, exactement
        /// comme les caisses reutilisent le cube : reconstruire un maillage
        /// par marqueur a chaque image couterait une allocation GPU par entite
        /// et par image. Le tableau est dimensionne sur l'enum : ajouter une
        /// forme sans l'instancier ici donnerait un marqueur invisible, pas un
        /// plantage - on garde donc le test de nullite au dessin.
        /// </summary>
        private readonly Mesh?[] _unitShapes = new Mesh?[MapShapes.All.Length];

        /// <summary>Tampon reutilise pour les piles de caisses : pas d'allocation par image.</summary>
        private readonly List<CrateSpawn> _crates = new();

        public MarkerRenderer(GL gl)
        {
            foreach (MapShape shape in MapShapes.All)
                _unitShapes[(int)shape] = MeshBuilder.UnitShape(gl, shape, Vector3.One);
        }

        public void Dispose()
        {
            for (int i = 0; i < _unitShapes.Length; i++)
                _unitShapes[i]?.Dispose();
        }

        /// <summary>Une forme unitaire mise a l'echelle, tournee autour de Y, puis posee.</summary>
        public void DrawShape(SceneRenderer scene, MapShape shape, Vector3 center,
                              Vector3 size, float yawDegrees, Vector3 tint)
        {
            Mesh? mesh = _unitShapes[(int)shape];

            // Un maillage manquant fait un marqueur invisible, pas un plantage :
            // l'editeur doit survivre a une forme qu'on aurait oublie d'instancier.
            if (mesh is null)
                return;

            scene.Draw(mesh,
                Matrix4x4.CreateScale(size)
                * Matrix4x4.CreateRotationY(yawDegrees * MathF.PI / 180f)
                * Matrix4x4.CreateTranslation(center),
                tint);
        }

        /// <summary>
        /// Variante a matrice libre, pour le seul cas qui a besoin d'autre
        /// chose qu'un lacet : la pointe de fleche, un cone couche.
        /// </summary>
        public void DrawShape(SceneRenderer scene, MapShape shape, Matrix4x4 model, Vector3 tint)
        {
            Mesh? mesh = _unitShapes[(int)shape];

            if (mesh is not null)
                scene.Draw(mesh, model, tint);
        }

        /// <summary>
        /// Une piece d'un assemblage, exprimee dans le repere LOCAL de
        /// l'entite puis tournee avec elle.
        ///
        /// ⚠️ L'ordre est echelle -> translation locale -> rotation -> position.
        /// Tourner APRES avoir translate dans le monde ferait pivoter chaque
        /// membre autour de l'origine de la carte au lieu de son entite : le
        /// personnage se disloquerait des qu'on le sort du centre du monde.
        /// </summary>
        private static Matrix4x4 PartMatrix(Vector3 localOffset, Vector3 size, float yawRadians, Vector3 origin)
            => Matrix4x4.CreateScale(size)
             * Matrix4x4.CreateTranslation(localOffset)
             * Matrix4x4.CreateRotationY(yawRadians)
             * Matrix4x4.CreateTranslation(origin);

        /// <summary>
        /// Direction horizontale d'un lacet, en degres.
        ///
        /// ⚠️ MEME CONVENTION QUE Camera.Forward : a lacet nul on regarde vers
        /// -Z. Recalculer ca a la main ailleurs finirait par diverger, et une
        /// fleche d'apparition qui montre l'inverse du regard reel est pire
        /// qu'une absence de fleche.
        /// </summary>
        public static Vector3 YawDirection(float degrees)
        {
            float r = degrees * MathF.PI / 180f;
            return new Vector3(-MathF.Sin(r), 0f, -MathF.Cos(r));
        }

        /// <summary>Les 12 aretes d'une boite, en fines lattes. Le moteur n'a pas de primitive de ligne.</summary>
        public void DrawWireBox(SceneRenderer scene, Vector3 center, Vector3 size,
                                float thickness, Vector3 tint)
        {
            Vector3 h = size * 0.5f;

            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    DrawShape(scene, MapShape.Block,
                        center + new Vector3(sx * h.X, 0f, sz * h.Z),
                        new Vector3(thickness, size.Y, thickness), 0f, tint);

            for (int sy = -1; sy <= 1; sy += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                    DrawShape(scene, MapShape.Block,
                        center + new Vector3(0f, sy * h.Y, sz * h.Z),
                        new Vector3(size.X, thickness, thickness), 0f, tint);

                for (int sx = -1; sx <= 1; sx += 2)
                    DrawShape(scene, MapShape.Block,
                        center + new Vector3(sx * h.X, sy * h.Y, 0f),
                        new Vector3(thickness, thickness, size.Z), 0f, tint);
            }
        }

        /// <summary>
        /// Une latte de a vers b, dans n'importe quelle direction - y compris
        /// en pente, pour suivre un escalier. Le moteur n'a pas de primitive de
        /// ligne.
        ///
        /// La latte est un bloc allonge selon Z, incline puis tourne. MEME
        /// convention de lacet que YawDirection : a lacet nul, l'axe vise -Z.
        /// </summary>
        public void DrawSegment(SceneRenderer scene, Vector3 a, Vector3 b, float thickness, Vector3 tint)
        {
            Vector3 d = b - a;
            float length = d.Length();

            if (length < 1e-4f)
                return;

            d /= length;

            float yaw = MathF.Atan2(-d.X, -d.Z);
            float pitch = MathF.Asin(Math.Clamp(d.Y, -1f, 1f));

            DrawShape(scene, MapShape.Block,
                Matrix4x4.CreateScale(new Vector3(thickness, thickness, length))
                * Matrix4x4.CreateRotationX(pitch)
                * Matrix4x4.CreateRotationY(yaw)
                * Matrix4x4.CreateTranslation((a + b) * 0.5f),
                tint);
        }

        /// <summary>Fleche horizontale : un fut et une pointe conique couchee.</summary>
        public void DrawArrow(SceneRenderer scene, Vector3 from, float yawDegrees,
                              float length, float thickness, Vector3 tint)
        {
            Vector3 dir = YawDirection(yawDegrees);

            DrawShape(scene, MapShape.Block, from + dir * (length * 0.5f),
                      new Vector3(thickness, thickness, length), yawDegrees, tint);

            // Le cone pointe vers +Y dans son repere : on le couche d'un quart
            // de tour autour de X pour l'envoyer vers -Z, puis le lacet
            // l'oriente comme le reste.
            DrawShape(scene, MapShape.Cone,
                Matrix4x4.CreateScale(new Vector3(thickness * 3.5f, thickness * 5f, thickness * 3.5f))
                * Matrix4x4.CreateRotationX(-MathF.PI / 2f)
                * Matrix4x4.CreateRotationY(yawDegrees * MathF.PI / 180f)
                * Matrix4x4.CreateTranslation(from + dir * (length + thickness * 2.5f)),
                tint);
        }

        /// <summary>
        /// Silhouette humaine, batie de primitives.
        ///
        /// ⚠️ CE N'EST PAS LE MODELE glTF, et c'est voulu. Le personnage est un
        /// asset FACULTATIF - le moteur demarre sans lui - alors qu'un marqueur
        /// d'editeur ne peut pas se permettre de manquer. Il serait en outre
        /// dessine en pose de liaison, bras ecartes, ce qui ressemble a un bug
        /// plutot qu'a un repere.
        ///
        /// Les proportions visent 1,80 m : c'est ce qui permet de juger a l'oeil
        /// si un PNJ passe sous une porte.
        /// </summary>
        public void DrawHumanoid(SceneRenderer scene, Vector3 feet, float yawDegrees, Vector3 tint)
            => ForEachHumanoidPart(feet, yawDegrees * MathF.PI / 180f, (mesh, model) => scene.Draw(mesh, model, tint));

        /// <summary>
        /// Les pieces de la silhouette, avec leur matrice.
        ///
        /// Une seule description pour DEUX usages : le dessin teinte, et la
        /// passe d'ombre et d'occlusion, qui ne connait que des maillages et
        /// des matrices. Deux listes de pieces finiraient par diverger, et une
        /// ombre qui n'a pas la forme du corps ne se remarque pas tout de suite.
        /// </summary>
        /// <param name="yawRadians">Lacet du MOTEUR : 0 regarde vers -Z, comme le nez.</param>
        public void ForEachHumanoidPart(Vector3 feet, float yawRadians, Action<Mesh, Matrix4x4> visit)
        {
            void Part(MapShape shape, Vector3 localOffset, Vector3 size)
            {
                if (_unitShapes[(int)shape] is Mesh mesh)
                    visit(mesh, PartMatrix(localOffset, size, yawRadians, feet));
            }

            for (int side = -1; side <= 1; side += 2)
            {
                Part(MapShape.Cylinder, new Vector3(side * 0.13f, 0.43f, 0f), new Vector3(0.22f, 0.86f, 0.22f));
                Part(MapShape.Cylinder, new Vector3(side * 0.30f, 1.15f, 0f), new Vector3(0.16f, 0.62f, 0.16f));
            }

            Part(MapShape.Block, new Vector3(0f, 1.18f, 0f), new Vector3(0.46f, 0.66f, 0.26f));
            Part(MapShape.Sphere, new Vector3(0f, 1.64f, 0f), new Vector3(0.26f));

            // Le nez : sans lui, la silhouette est symetrique et ne dit pas de
            // quel cote le personnage regarde - or c'est exactement ce qu'on
            // vient regler avec « fov ».
            Part(MapShape.Cone, new Vector3(0f, 1.62f, -0.16f), new Vector3(0.09f, 0.14f, 0.09f));
        }

        /// <summary>
        /// Les deux bords du champ de vision d'un PNJ, a hauteur d'yeux.
        ///
        /// ⚠️ C'est la seule chose qui rende « sight » et « fov » JUGEABLES.
        /// Deux nombres dans un panneau ne disent pas si le garde couvre le
        /// couloir ; deux lattes posees dans le decor, si. Elles partent des
        /// YEUX et pas des pieds, pour la meme raison que la ligne de vue
        /// elle-meme (voir AgentWorld) - depuis les pieds, un muret rendrait
        /// le trace faux sans rien changer au comportement reel.
        /// </summary>
        private void DrawSightCone(SceneRenderer scene, MapEntity entity, Vector3 feet,
                                   float yawDegrees, Vector3 tint)
        {
            float sight = MathF.Min(entity.GetFloat("sight", 20f), 40f);
            float half = MathF.Min(entity.GetFloat("fov", 110f), 360f) * 0.5f;

            if (sight <= 0f)
                return;

            Vector3 eyes = feet + new Vector3(0f, 1.6f, 0f);

            for (int side = -1; side <= 1; side += 2)
            {
                float edge = yawDegrees + side * half;

                DrawShape(scene, MapShape.Block,
                          eyes + YawDirection(edge) * (sight * 0.5f),
                          new Vector3(0.05f, 0.05f, sight), edge, tint);
            }
        }

        /// <summary>
        /// La COURSE d'une porte : d'ou elle part, jusqu'ou elle va.
        ///
        /// La boite de la porte elle-meme est deja dessinee par MoverWorld,
        /// dans l'editeur comme en jeu. Ce qui manquait est invisible par
        /// nature : « axis » et « distance » ne se voient qu'une fois la porte
        /// ouverte, donc jamais pendant qu'on l'edite.
        /// </summary>
        private void DrawDoorTravel(SceneRenderer scene, MapEntity entity, Vector3 position, Vector3 tint)
        {
            float distance = entity.GetFloat("distance", 3f);

            Vector3 axis = entity.Get("axis", "y").Trim().ToLowerInvariant() switch
            {
                "x" => Vector3.UnitX,
                "z" => Vector3.UnitZ,
                _ => Vector3.UnitY,
            };

            Vector3 travel = axis * distance;
            float length = MathF.Abs(distance);

            if (length < 0.01f)
                return;

            Vector3 thickness = Vector3.One * 0.06f + axis * (length - 0.06f);

            DrawShape(scene, MapShape.Block, position + travel * 0.5f, thickness, 0f, tint);
            DrawShape(scene, MapShape.Sphere, position + travel, new Vector3(0.22f), 0f, tint);
        }

        /// <summary>
        /// Le marqueur d'une entite, choisi sur son TYPE.
        ///
        /// ⚠️ Un type inconnu retombe sur le cube historique, magenta. Le
        /// format conserve exprime les types qu'il ne connait pas (voir
        /// MapEntity) : le marqueur doit faire pareil, sinon une carte ecrite
        /// pour une version ulterieure aurait des entites INVISIBLES et
        /// impossibles a designer - donc impossibles a supprimer.
        /// </summary>
        public void DrawEntityMarker(SceneRenderer scene, MapEntity entity, bool active)
        {
            Vector3 tint = active ? new Vector3(2f, 1.6f, 0.3f) : EntityTint(entity.Type);
            Vector3 p = entity.Position;

            switch (entity.Type)
            {
                case EntityTypes.PlayerSpawn:
                    // Un socle plat plutot qu'un cube : une apparition est un
                    // ENDROIT, pas un objet, et le socle montre son emprise au
                    // sol sans masquer le decor qu'on est en train de cadrer.
                    DrawShape(scene, MapShape.Cylinder, p + new Vector3(0f, 0.05f, 0f),
                              new Vector3(1.3f, 0.1f, 1.3f), 0f, tint);
                    DrawShape(scene, MapShape.Cone, p + new Vector3(0f, 0.75f, 0f),
                              new Vector3(0.45f, 1.1f, 0.45f), 0f, tint);
                    DrawArrow(scene, p + new Vector3(0f, 0.14f, 0f), entity.Yaw, 1.5f, 0.07f, tint);
                    return;

                case EntityTypes.Checkpoint:
                case EntityTypes.Trigger:
                {
                    // ⚠️ ON DESSINE LE VOLUME REEL, calcule par la MEME fonction
                    // que celle qui teste le joueur. Un marqueur de taille fixe
                    // mentirait sur la portee du declencheur - et c'est
                    // precisement ce qu'on venait de reprocher au cube.
                    Aabb volume = EntityWorld.VolumeOf(entity);

                    DrawWireBox(scene, (volume.Min + volume.Max) * 0.5f,
                                volume.Max - volume.Min, 0.06f, tint);

                    DrawShape(scene, MapShape.Sphere, p, new Vector3(0.34f), 0f, tint);
                    return;
                }

                case EntityTypes.Npc:
                    DrawHumanoid(scene, p, entity.Yaw, tint);
                    DrawSightCone(scene, entity, p, entity.Yaw, tint);
                    return;

                case EntityTypes.Waypoint:
                    // Un jalon : une bille sur un mat. Assez fin pour qu'une
                    // ronde de six points ne bouche pas la vue du couloir.
                    DrawShape(scene, MapShape.Cylinder, p + new Vector3(0f, 0.4f, 0f),
                              new Vector3(0.07f, 0.8f, 0.07f), 0f, tint);
                    DrawShape(scene, MapShape.Sphere, p + new Vector3(0f, 0.95f, 0f),
                              new Vector3(0.3f), 0f, tint);
                    return;

                case EntityTypes.Door:
                    DrawDoorTravel(scene, entity, p, tint);
                    return;

                case EntityTypes.Crate:
                {
                    // Chaque caisse de la pile, a sa taille, calculee par la
                    // MEME fonction que celle qui les fait naitre : le marqueur
                    // dit exactement ou elles tomberont.
                    _crates.Clear();
                    CrateStacks.AppendStack(entity, _crates);

                    foreach (CrateSpawn crate in _crates)
                        DrawWireBox(scene, crate.Center, new Vector3(crate.Size), 0.04f, tint);

                    return;
                }

                case EntityTypes.Mannequin:
                    // Le lacet de l'entite suit la convention du moteur, comme
                    // la silhouette : la fleche montre ou il regardera.
                    DrawHumanoid(scene, p, entity.Yaw, tint);
                    DrawArrow(scene, p + new Vector3(0f, 0.1f, 0f), entity.Yaw, 1.2f, 0.06f, tint);
                    return;

                // Les entites logiques n'ont AUCUNE apparence en jeu : leur
                // marqueur est tout ce qui permet de les trouver dans
                // l'editeur. Trois silhouettes distinctes plutot qu'un cube de
                // couleur : on doit lire « relais », « minuterie », « compteur »
                // sans selectionner.
                case EntityTypes.LogicRelay:
                    // Un boitier et une antenne : ca relaie.
                    DrawShape(scene, MapShape.Block, p + new Vector3(0f, 0.2f, 0f), new Vector3(0.5f, 0.4f, 0.5f), 0f, tint);
                    DrawShape(scene, MapShape.Cone, p + new Vector3(0f, 0.65f, 0f), new Vector3(0.2f, 0.5f, 0.2f), 0f, tint);
                    return;

                case EntityTypes.LogicTimer:
                    // Un cadran pose sur un pied, et une aiguille.
                    DrawShape(scene, MapShape.Cylinder, p + new Vector3(0f, 0.3f, 0f), new Vector3(0.08f, 0.6f, 0.08f), 0f, tint);
                    DrawShape(scene, MapShape.Cylinder, p + new Vector3(0f, 0.65f, 0f), new Vector3(0.7f, 0.1f, 0.7f), 0f, tint);
                    DrawShape(scene, MapShape.Block, p + new Vector3(0.15f, 0.72f, 0f), new Vector3(0.3f, 0.04f, 0.05f), 0f, tint);
                    return;

                case EntityTypes.LogicCounter:
                    // Trois plaques empilees : un comptage.
                    for (int i = 0; i < 3; i++)
                        DrawShape(scene, MapShape.Block, p + new Vector3(0f, 0.08f + i * 0.2f, 0f),
                                  new Vector3(0.55f - i * 0.1f, 0.12f, 0.55f - i * 0.1f), 0f, tint);
                    return;

                case EntityTypes.EnvLight:
                {
                    // Une ampoule DE LA COULEUR de la lampe, et sa portee en fil
                    // de fer au sol : on regle une lumiere en voyant ce qu'elle
                    // couvre, pas en relisant un nombre.
                    Vector3 color = active ? tint : EnvWorld.ParseColor(entity.Get("color"), EnvLight.DefaultColor);
                    float range = MathF.Max(0.1f, entity.GetFloat("range", EnvLight.DefaultRange));

                    DrawShape(scene, MapShape.Sphere, p, new Vector3(0.35f), 0f, color * 1.6f);
                    DrawShape(scene, MapShape.Cylinder, p + new Vector3(0f, 0.3f, 0f), new Vector3(0.12f, 0.25f, 0.12f), 0f, tint);
                    DrawWireBox(scene, new Vector3(p.X, 0.02f, p.Z), new Vector3(range * 2f, 0.02f, range * 2f), 0.03f, color);
                    return;
                }

                case EntityTypes.EnvSound:
                    // Un haut-parleur : un pavillon couche sur un boitier.
                    DrawShape(scene, MapShape.Block, p + new Vector3(0f, 0.25f, 0f), new Vector3(0.4f, 0.5f, 0.3f), entity.Yaw, tint);
                    DrawShape(scene, MapShape.Cone,
                        Matrix4x4.CreateScale(new Vector3(0.45f, 0.35f, 0.45f))
                        * Matrix4x4.CreateRotationX(MathF.PI / 2f)
                        * Matrix4x4.CreateRotationY(entity.Yaw * MathF.PI / 180f)
                        * Matrix4x4.CreateTranslation(p + new Vector3(0f, 0.3f, 0f) + YawDirection(entity.Yaw) * 0.3f),
                        tint);
                    return;

                default:
                    DrawShape(scene, MapShape.Block, p + new Vector3(0f, 0.3f, 0f),
                              new Vector3(EditorState.EntityMarkerSize), entity.Yaw, tint);
                    return;
            }
        }

        /// <summary>
        /// Couleur du marqueur, par type d'entite.
        ///
        /// Des teintes VIVES et au-dela de 1 : ce sont des reperes d'edition,
        /// pas du decor. Elles doivent se distinguer d'un mur quelle que soit
        /// la lumiere de la scene.
        /// </summary>
        public static Vector3 EntityTint(string type) => type switch
        {
            EntityTypes.PlayerSpawn => new Vector3(0.3f, 1.5f, 0.5f),   // vert
            EntityTypes.Checkpoint => new Vector3(0.35f, 0.9f, 1.6f),   // bleu
            EntityTypes.Trigger => new Vector3(1.5f, 0.9f, 0.3f),       // ambre
            EntityTypes.Npc => new Vector3(1.5f, 0.4f, 0.6f),           // rose
            EntityTypes.Waypoint => new Vector3(0.9f, 0.9f, 1.2f),      // blanc bleute
            EntityTypes.Door => new Vector3(0.4f, 1.4f, 1.3f),          // cyan
            EntityTypes.Crate => new Vector3(1.4f, 1.0f, 0.45f),        // bois
            EntityTypes.Mannequin => new Vector3(1.3f, 1.25f, 0.6f),    // jaune paille
            EntityTypes.LogicRelay => new Vector3(0.6f, 1.5f, 0.9f),    // vert d'eau
            EntityTypes.LogicTimer => new Vector3(1.0f, 0.75f, 1.5f),   // lavande
            EntityTypes.LogicCounter => new Vector3(1.5f, 0.65f, 0.35f),// orange
            EntityTypes.EnvLight => new Vector3(1.6f, 1.5f, 1.1f),      // blanc chaud
            EntityTypes.EnvSound => new Vector3(0.5f, 1.2f, 1.6f),      // bleu ciel
            _ => new Vector3(1.2f, 0.5f, 1.2f),                         // magenta = type inconnu
        };
    }
}
