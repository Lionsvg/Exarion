// ===========================================================================
//  Exarion - Description d'une carte
//
//  Jusqu'ici le decor du bac a sable etait ECRIT EN DUR dans Program.cs :
//  32 boites posees ligne par ligne en C#. Deplacer un mur demandait de
//  recompiler, et personne d'autre que le programme ne pouvait decrire un
//  niveau. C'est le trou le plus visible d'un moteur cense etre reutilisable.
//
//  MapData est la description d'un niveau, et RIEN d'autre : de la donnee
//  pure, sans OpenGL, sans fichier, sans moteur. C'est ce qui permet de la
//  construire, de la comparer et de la tester en ligne de commande - regle
//  n°1 du projet.
//
//  ---------------------------------------------------------------------
//  POURQUOI LES LUMIERES SONT DECRITES ICI, ET PAS DANS LE MOTEUR
//
//  Exarion.Core ne connait pas la classe Light du rendu, et ne doit pas la
//  connaitre. Une carte porte donc sa propre description des lumieres -
//  position, couleur, portee - que la couche de rendu traduit ensuite. Le
//  cout est une poignee de champs recopies ; le gain est qu'une carte se
//  lit, se valide et se teste sans jamais ouvrir de fenetre.
//  ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Exarion.Core.Physics;

namespace Exarion.Core.Assets
{
    public enum MapLightKind
    {
        Directional,
        Point,
        Spot,
    }

    /// <summary>
    /// La FORME d'un element de decor.
    ///
    /// ---------------------------------------------------------------------
    /// POURQUOI UNE FORME PLUTOT QU'UN MAILLAGE
    ///
    /// Un editeur qui ne pose que des cubes produit des niveaux qui se
    /// ressemblent tous, et surtout il rend le decor illisible : une colonne,
    /// une rampe et une caisse ont exactement la meme silhouette. C'est le
    /// reproche remonte le 17/08, et il est juste.
    ///
    /// On garde neanmoins la BOITE comme description : centre, taille,
    /// couleur. La forme n'est qu'une maniere de remplir ce volume. Un vrai
    /// maillage par objet demanderait un format d'asset, un gestionnaire de
    /// ressources et un budget memoire - alors que quatre primitives couvrent
    /// l'essentiel du blocage de niveau, exactement comme les Parts de Roblox
    /// ou les brushes de Hammer.
    /// ---------------------------------------------------------------------
    ///
    /// ⚠️⚠️ LA COLLISION RESTE LA BOITE ENGLOBANTE, POUR TOUTES LES FORMES.
    /// C'est une entorse assumee au principe « ce qu'on voit est ce qui
    /// bloque » affiche en tete de MeshBuilder. Une sphere se cogne donc aux
    /// coins de son cube, et une rampe ne se monte PAS - on bute dessus comme
    /// sur un mur. La collision convexe est un chantier a part entiere
    /// (CharacterMotor et PhysicsWorld ne connaissent que des AABB) ; tant
    /// qu'elle n'est pas faite, ces formes sont DECORATIVES et l'editeur le
    /// dit a l'ecran plutot que de laisser le decouvrir en jouant.
    /// </summary>
    public enum MapShape
    {
        /// <summary>Le cube historique. Valeur par defaut, donc les cartes anterieures ne changent pas.</summary>
        Block,

        /// <summary>Ellipsoide inscrit dans la boite. Une boule si la boite est cubique.</summary>
        Sphere,

        /// <summary>Cylindre d'axe Y, inscrit dans la boite.</summary>
        Cylinder,

        /// <summary>Coin : plan incline montant vers -Z. Le « wedge » de Roblox, la rampe de Hammer.</summary>
        Wedge,

        /// <summary>Cone d'axe Y, pointe en haut.</summary>
        Cone,
    }

    /// <summary>
    /// Une boite de decor : sa position, sa taille, sa couleur, sa forme.
    ///
    /// La couleur est en LINEAIRE, comme partout dans le moteur depuis le
    /// passage au rendu HDR (1.8). Ecrire ici une valeur pensee "couleur
    /// d'ecran" donne un decor delave - le meme piege que la fumee blanche
    /// des particules.
    ///
    /// ⚠️ Le constructeur a TROIS arguments est conserve et pose un Block.
    /// C'est ce qui permet a tout le code anterieur - et aux cartes des
    /// versions 1 et 2 - de continuer a decrire exactement le meme decor.
    /// </summary>
    public readonly struct MapBox : IEquatable<MapBox>
    {
        public readonly Vector3 Center;
        public readonly Vector3 Size;
        public readonly Vector3 Color;
        public readonly MapShape Shape;

        public MapBox(Vector3 center, Vector3 size, Vector3 color)
            : this(center, size, color, MapShape.Block)
        {
        }

        public MapBox(Vector3 center, Vector3 size, Vector3 color, MapShape shape)
        {
            Center = center;
            Size = size;
            Color = color;
            Shape = shape;
        }

        public bool Equals(MapBox other) =>
            Center.Equals(other.Center) && Size.Equals(other.Size) &&
            Color.Equals(other.Color) && Shape == other.Shape;

        public override bool Equals(object? obj) => obj is MapBox b && Equals(b);
        public override int GetHashCode() => HashCode.Combine(Center, Size, Color, Shape);
    }

    /// <summary>
    /// Noms de formes utilises par le format de carte et par la console.
    ///
    /// Une table explicite plutot que Enum.Parse : le nom du fichier ne doit
    /// pas dependre du nom C# de la valeur. Renommer l'enum casserait sinon
    /// toutes les cartes deja enregistrees, en silence.
    /// </summary>
    public static class MapShapes
    {
        public static readonly MapShape[] All =
        {
            MapShape.Block, MapShape.Sphere, MapShape.Cylinder, MapShape.Wedge, MapShape.Cone,
        };

        public static string Name(MapShape shape) => shape switch
        {
            MapShape.Sphere => "sphere",
            MapShape.Cylinder => "cylindre",
            MapShape.Wedge => "rampe",
            MapShape.Cone => "cone",
            _ => "bloc",
        };

        /// <summary>
        /// Lit un nom de forme. Rend faux si le nom est inconnu.
        ///
        /// ⚠️ Les noms anglais sont acceptes en plus des francais : le format
        /// est fait pour etre ecrit a la main, et « sphere »/« cylinder » sont
        /// ce que taperait spontanement quelqu'un venant de Roblox ou de
        /// Hammer. Accepter les deux coute cinq lignes ; refuser produirait
        /// une carte fatale sur une faute qui n'en est pas une.
        /// </summary>
        public static bool TryParse(string? text, out MapShape shape)
        {
            shape = MapShape.Block;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            switch (text.Trim().ToLowerInvariant())
            {
                case "bloc": case "block": case "box": case "cube": shape = MapShape.Block; return true;
                case "sphere": case "ball": case "boule": shape = MapShape.Sphere; return true;
                case "cylindre": case "cylinder": shape = MapShape.Cylinder; return true;
                case "rampe": case "wedge": case "ramp": case "coin": shape = MapShape.Wedge; return true;
                case "cone": shape = MapShape.Cone; return true;
                default: return false;
            }
        }
    }

    /// <summary>
    /// Une lumiere. Les champs inutiles au type sont simplement ignores :
    /// une directionnelle n'a pas de portee, une ponctuelle pas de cone.
    /// </summary>
    public readonly struct MapLight : IEquatable<MapLight>
    {
        public readonly MapLightKind Kind;
        public readonly Vector3 Position;
        public readonly Vector3 Direction;
        public readonly Vector3 Color;
        public readonly float Intensity;
        public readonly float Range;
        public readonly float InnerDegrees;
        public readonly float OuterDegrees;

        public MapLight(MapLightKind kind, Vector3 position, Vector3 direction, Vector3 color,
                        float intensity, float range, float innerDegrees, float outerDegrees)
        {
            Kind = kind;
            Position = position;
            Direction = direction;
            Color = color;
            Intensity = intensity;
            Range = range;
            InnerDegrees = innerDegrees;
            OuterDegrees = outerDegrees;
        }

        public static MapLight Directional(Vector3 direction, Vector3 color, float intensity) =>
            new MapLight(MapLightKind.Directional, Vector3.Zero, direction, color, intensity, 0f, 0f, 0f);

        public static MapLight Point(Vector3 position, Vector3 color, float intensity, float range) =>
            new MapLight(MapLightKind.Point, position, Vector3.Zero, color, intensity, range, 0f, 0f);

        public static MapLight Spot(Vector3 position, Vector3 direction, Vector3 color,
                                    float intensity, float range, float innerDegrees, float outerDegrees) =>
            new MapLight(MapLightKind.Spot, position, direction, color, intensity, range, innerDegrees, outerDegrees);

        public bool Equals(MapLight other) =>
            Kind == other.Kind &&
            Position.Equals(other.Position) &&
            Direction.Equals(other.Direction) &&
            Color.Equals(other.Color) &&
            Intensity.Equals(other.Intensity) &&
            Range.Equals(other.Range) &&
            InnerDegrees.Equals(other.InnerDegrees) &&
            OuterDegrees.Equals(other.OuterDegrees);

        public override bool Equals(object? obj) => obj is MapLight l && Equals(l);

        public override int GetHashCode() =>
            HashCode.Combine(Kind, Position, Direction, Color, Intensity, Range, InnerDegrees);
    }

    /// <summary>
    /// Une ENTITE : un point remarquable de la carte, avec un type et des
    /// proprietes libres.
    ///
    /// ---------------------------------------------------------------------
    /// POURQUOI DES PROPRIETES CLE/VALEUR ET PAS UN CHAMP PAR TYPE
    ///
    /// C'est le modele de Hammer, et il n'est pas choisi par paresse. Un point
    /// d'apparition a besoin d'une equipe, un declencheur d'un volume, un PNJ
    /// d'une vitesse : mettre tous ces champs dans une seule structure la
    /// remplirait de cases vides, et surtout il faudrait TOUCHER AU FORMAT a
    /// chaque nouveau type d'entite.
    ///
    /// Avec des cles libres, ajouter un type n'exige plus rien : ni nouvelle
    /// version de fichier, ni modification du lecteur, ni de l'editeur.
    /// ---------------------------------------------------------------------
    ///
    /// ⚠️ C'est une CLASSE, pas une structure - a la difference de MapBox et
    /// MapLight. Elle porte un dictionnaire, donc elle ne peut pas se recopier
    /// par affectation. Voir MapData.Clone : c'est exactement la ou une copie
    /// superficielle casserait l'annulation de l'editeur.
    /// </summary>
    public sealed class MapEntity
    {
        public MapEntity(string type, Vector3 position, float yaw = 0f, float pitch = 0f)
        {
            Type = string.IsNullOrWhiteSpace(type) ? "info_null" : type;
            Position = position;
            Yaw = yaw;
            Pitch = pitch;
        }

        /// <summary>Type, par exemple « info_player_spawn ». Jamais vide.</summary>
        public string Type { get; set; }

        public Vector3 Position { get; set; }

        public float Yaw { get; set; }
        public float Pitch { get; set; }

        /// <summary>
        /// Proprietes libres. Ordonnees : sans ordre stable, deux
        /// enregistrements de la MEME carte donneraient deux fichiers
        /// differents, et l'aller-retour octet pour octet du format tomberait.
        /// </summary>
        public SortedDictionary<string, string> Properties { get; } =
            new(StringComparer.Ordinal);

        public MapEntity Set(string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(key))
                Properties[key] = value ?? "";

            return this;
        }

        public string Get(string key, string fallback = "")
            => Properties.TryGetValue(key, out string? v) ? v : fallback;

        public float GetFloat(string key, float fallback = 0f)
            => Properties.TryGetValue(key, out string? v)
               && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
                ? f : fallback;

        public int GetInt(string key, int fallback = 0)
            => Properties.TryGetValue(key, out string? v)
               && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                ? n : fallback;

        /// <summary>
        /// Copie INDEPENDANTE, dictionnaire compris.
        ///
        /// ⚠️ C'est la methode qui fait tenir l'annulation de l'editeur. Sans
        /// elle, l'instantane partagerait ses entites avec la carte vivante :
        /// annuler restituerait les boites mais PAS les proprietes d'entites,
        /// et la divergence ne se verrait que bien plus tard.
        /// </summary>
        public MapEntity Clone()
        {
            var copy = new MapEntity(Type, Position, Yaw, Pitch);

            foreach (KeyValuePair<string, string> pair in Properties)
                copy.Properties[pair.Key] = pair.Value;

            return copy;
        }
    }

    public sealed class MapData
    {
        /// <summary>Nom lisible, purement informatif.</summary>
        public string Name { get; set; } = "sans nom";

        public List<MapBox> Boxes { get; } = new();
        public List<MapLight> Lights { get; } = new();

        /// <summary>
        /// Entites : apparitions, declencheurs, evenements, PNJ.
        ///
        /// Le decor dit a quoi ressemble le niveau ; les entites disent ce qui
        /// s'y PASSE. C'est la separation de Hammer, et elle a une raison
        /// pratique : on retouche la geometrie sans toucher au gameplay, et
        /// inversement.
        /// </summary>
        public List<MapEntity> Entities { get; } = new();

        /// <summary>Position des PIEDS du joueur a l'apparition.</summary>
        public Vector3 Spawn { get; set; } = new Vector3(0f, 0f, 0f);

        /// <summary>Orientation de depart, en degres.</summary>
        public float SpawnYaw { get; set; }
        public float SpawnPitch { get; set; }

        public void AddBox(Vector3 center, Vector3 size, Vector3 color) =>
            Boxes.Add(new MapBox(center, size, color));

        public void AddBox(Vector3 center, Vector3 size, Vector3 color, MapShape shape) =>
            Boxes.Add(new MapBox(center, size, color, shape));

        /// <summary>
        /// Copie complete et independante.
        ///
        /// Ajoutee pour l'editeur (4.1), qui construit son historique
        /// d'annulation par INSTANTANES.
        ///
        /// ⚠️⚠️ LES BOITES ET LES LUMIERES SE RECOPIENT, LES ENTITES SE CLONENT.
        /// MapBox et MapLight sont des STRUCTURES : AddRange en copie la valeur,
        /// il n'y a rien de partage. MapEntity est une CLASSE portant un
        /// dictionnaire : AddRange n'en copierait que la reference, et
        /// l'instantane partagerait ses entites avec la carte vivante.
        ///
        /// Le defaut serait particulierement vicieux : annuler restituerait
        /// correctement la geometrie, et pas les proprietes d'entites. On
        /// chercherait le bug dans l'annulation alors qu'il serait ici.
        /// </summary>
        public MapData Clone()
        {
            var copy = new MapData
            {
                Name = Name,
                Spawn = Spawn,
                SpawnYaw = SpawnYaw,
                SpawnPitch = SpawnPitch,
            };

            copy.Boxes.AddRange(Boxes);
            copy.Lights.AddRange(Lights);

            foreach (MapEntity entity in Entities)
                copy.Entities.Add(entity.Clone());

            return copy;
        }

        /// <summary>
        /// Points d'apparition declares par la carte.
        ///
        /// La propriete Spawn historique reste : elle sert de repli quand la
        /// carte n'en declare aucun. Une carte sans apparition ne doit pas
        /// laisser le joueur a l'origine du monde, souvent DANS le sol.
        /// </summary>
        public IEnumerable<MapEntity> SpawnPoints()
        {
            foreach (MapEntity entity in Entities)
                if (string.Equals(entity.Type, EntityTypes.PlayerSpawn, StringComparison.Ordinal))
                    yield return entity;
        }

        /// <summary>
        /// Construit le monde de collision et la liste de couleurs paralleles
        /// attendue par le constructeur de maillage.
        ///
        /// Les deux listes doivent rester du MEME ordre et de la MEME longueur :
        /// le maillage lit la couleur d'indice i pour la boite d'indice i. Les
        /// desynchroniser donne un decor aux couleurs permutees, ce qui se voit
        /// tout de suite - mais autant ne construire les deux qu'ici.
        /// </summary>
        public CollisionWorld ToCollisionWorld(List<Vector3> colors)
        {
            ArgumentNullException.ThrowIfNull(colors);

            var world = new CollisionWorld();
            colors.Clear();

            for (int i = 0; i < Boxes.Count; i++)
            {
                MapBox box = Boxes[i];
                world.AddBox(box.Center, box.Size);
                colors.Add(box.Color);
            }

            return world;
        }
    }

    /// <summary>
    /// Types d'entites que le moteur sait interpreter.
    ///
    /// Des CONSTANTES et non un enum : le format accepte n'importe quel type,
    /// et une carte doit pouvoir porter une entite qu'une version future
    /// comprendra. Un enum forcerait a refuser ce qu'on ne connait pas - donc
    /// a perdre l'entite en la relisant, ce qui est le pire des deux mondes.
    /// </summary>
    public static class EntityTypes
    {
        /// <summary>Point d'apparition du joueur. Propriete utile : team.</summary>
        public const string PlayerSpawn = "info_player_spawn";

        /// <summary>Volume qui enregistre un passage. Proprietes : size, order.</summary>
        public const string Checkpoint = "trigger_checkpoint";

        /// <summary>Volume generique. Proprietes : size, target, output, once.</summary>
        public const string Trigger = "trigger_volume";

        /// <summary>Pion sans intelligence, pour l'instant. Proprietes : speed, path.</summary>
        public const string Npc = "npc_dummy";

        /// <summary>Point de passage nomme, cible d'un PNJ ou d'un evenement.</summary>
        public const string Waypoint = "info_waypoint";

        /// <summary>
        /// Boite solide qui va-et-vient : porte, herse, ascenseur, pont.
        ///
        /// C'est la PREMIERE entite qui agit au lieu de se decrire, et donc la
        /// premiere cible possible d'une sortie. Proprietes : name, axis
        /// (x|y|z), distance (signee), speed, width, height, depth, wait,
        /// start_open. Entrees : Ouvrir, Fermer, Basculer.
        ///
        /// ⚠️ Elle a besoin d'un « name » pour servir a quelque chose : sans
        /// nom, aucune sortie ne peut la viser et elle reste une boite fermee.
        /// </summary>
        public const string Door = "func_door";

        /// <summary>
        /// Une pile de caisses dynamiques, posee au sol a l'emplacement de
        /// l'entite. Proprietes : size, mass, count.
        ///
        /// ⚠️ C'EST LA CARTE QUI DECLARE SES CAISSES. Jusqu'au 07/10, le bac a
        /// sable en lachait quatre en (0, 0, 8) apres TOUTE carte chargee : sur
        /// une carte qui n'en voulait pas, elles tombaient devant le point de
        /// depart, s'endormaient aussitot, et l'assombrissement du sommeil en
        /// faisait un pilier noir plante devant le joueur.
        /// </summary>
        public const string Crate = "prop_crate";

        /// <summary>
        /// Le personnage anime decoratif, s'il y a un modele glTF. Le lacet de
        /// l'entite est la direction ou il regarde. Sans cette entite, la carte
        /// n'a pas de mannequin - il apparaissait sur toutes, au meme endroit.
        /// </summary>
        public const string Mannequin = "prop_mannequin";

        /// <summary>
        /// Relais : recoit « Declencher » et emet OnDeclenche vers sa cible,
        /// apres « delay » secondes. Proprietes : name, target, input, delay,
        /// once, start_disabled. Entrees : Declencher, Activer, Desactiver,
        /// Basculer, Annuler. Voir LogicWorld.
        /// </summary>
        public const string LogicRelay = "logic_relay";

        /// <summary>
        /// Minuterie : emet OnMinuterie toutes les « interval » secondes.
        /// Entrees : Demarrer, Arreter, Basculer, Declencher.
        /// </summary>
        public const string LogicTimer = "logic_timer";

        /// <summary>
        /// Compteur : « Ajouter » jusqu'a « max », ou il emet OnMax.
        /// Entrees : Ajouter, Retirer, Remettre.
        /// </summary>
        public const string LogicCounter = "logic_counter";

        /// <summary>
        /// Lumiere ponctuelle COMMUTABLE. Proprietes : name, color (r,g,b
        /// lineaire, virgules), intensity, range, start_off. Entrees :
        /// Allumer, Eteindre, Basculer. Voir EnvWorld.
        /// </summary>
        public const string EnvLight = "env_light";

        /// <summary>
        /// Bip en boucle, place dans le monde. Proprietes : name, hz, volume,
        /// start_on. Entrees : Jouer, Arreter, Basculer.
        /// </summary>
        public const string EnvSound = "env_sound";

        /// <summary>Tous les types connus, dans l'ordre ou l'editeur les propose.</summary>
        public static readonly string[] All =
        {
            PlayerSpawn, Checkpoint, Trigger, Npc, Waypoint, Door, Crate, Mannequin,
            LogicRelay, LogicTimer, LogicCounter, EnvLight, EnvSound,
        };
    }
}
