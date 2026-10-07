// ===========================================================================
//  Exarion 1 - Generateur de la map de_exarion
//
//  Reconstruit en primitives Unity le layout valide sur Source, converti en
//  metres (1 unite Source = 0.01905 m).
//
//         NORD  (defenseurs)
//   +--------------------------------------+
//   |  SITE A     [SPAWN DEF]     SITE B   |
//   |  +--------+            +--------+    |
//   |  |        |            |        |    |
//   |  | bloc   |    MID     | bloc   |    |
//   |  |        |            |        |    |
//   |  +--------+            +--------+    |
//   |            [SPAWN ATK]               |
//   +--------------------------------------+
//         SUD  (attaquants)
//
//  Les 3 voies naissent de 2 blocs pleins : aucune ouverture a gerer, donc
//  aucun risque de trou dans la geometrie.
//
//  Appelable en ligne de commande :
//    Unity.exe -batchmode -quit -projectPath <proj> \
//      -executeMethod Exarion.EditorTools.MapBuilder.BuildFromCommandLine
// ===========================================================================

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Exarion.Core;
using Exarion.Game;

namespace Exarion.EditorTools
{
    public static class MapBuilder
    {
        // --- Dimensions, en metres ------------------------------------------
        private const float Half = 39f;          // demi-cote de la map (78 x 78 m)
        private const float Ceiling = 7.3f;
        private const float WallThickness = 0.6f;

        private const float MidHalfWidth = 7.6f; // demi-largeur du couloir central
        private const float BlockWidth = 11.4f;  // largeur des blocs separateurs
        private const float ConnectorZ = 26.7f;  // ou s'arretent les blocs

        private const string ScenePath = "Assets/Scenes/de_exarion.unity";
        private const string MaterialDir = "Assets/Materials";

        private static Material _matFloor, _matWall, _matSiteA, _matSiteB, _matCover;

        [MenuItem("Exarion/Construire la map de_exarion")]
        public static void BuildMenu()
        {
            Build();
            EditorUtility.DisplayDialog("Exarion", "Map generee :\n" + ScenePath, "OK");
        }

        /// <summary>Point d'entree ligne de commande.</summary>
        public static void BuildFromCommandLine()
        {
            Build();
            Debug.Log("[Exarion] Map generee : " + ScenePath);
        }

        private static void Build()
        {
            EnsureMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("de_exarion");

            BuildShell(root.transform);
            BuildLanes(root.transform);
            BuildSites(root.transform);
            BuildCover(root.transform);
            BuildSpawns(root.transform);
            BuildLighting(root.transform);
            BuildDirector();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            // La scene devient la scene de demarrage : ouvrir le projet suffit
            // pour pouvoir appuyer sur Play.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            AssetDatabase.SaveAssets();
        }

        // -------------------------------------------------------------------
        // Coque : sol, plafond, murs de perimetre
        // -------------------------------------------------------------------
        private static void BuildShell(Transform parent)
        {
            var g = new GameObject("Coque").transform;
            g.SetParent(parent, false);

            float outer = Half + WallThickness;

            Box(g, "Sol", new Vector3(0f, -0.5f, 0f),
                new Vector3(outer * 2f, 1f, outer * 2f), _matFloor);

            Box(g, "Plafond", new Vector3(0f, Ceiling + 0.5f, 0f),
                new Vector3(outer * 2f, 1f, outer * 2f), _matWall);

            float mid = Ceiling * 0.5f;
            Box(g, "Mur_Ouest", new Vector3(-Half - WallThickness * 0.5f, mid, 0f),
                new Vector3(WallThickness, Ceiling, outer * 2f), _matWall);
            Box(g, "Mur_Est", new Vector3(Half + WallThickness * 0.5f, mid, 0f),
                new Vector3(WallThickness, Ceiling, outer * 2f), _matWall);
            Box(g, "Mur_Nord", new Vector3(0f, mid, Half + WallThickness * 0.5f),
                new Vector3(Half * 2f, Ceiling, WallThickness), _matWall);
            Box(g, "Mur_Sud", new Vector3(0f, mid, -Half - WallThickness * 0.5f),
                new Vector3(Half * 2f, Ceiling, WallThickness), _matWall);
        }

        // -------------------------------------------------------------------
        // Les 2 blocs qui creent les 3 voies
        // -------------------------------------------------------------------
        private static void BuildLanes(Transform parent)
        {
            var g = new GameObject("Voies").transform;
            g.SetParent(parent, false);

            float centerX = MidHalfWidth + BlockWidth * 0.5f;
            float depth = ConnectorZ * 2f;
            float mid = Ceiling * 0.5f;

            Box(g, "Bloc_Ouest", new Vector3(-centerX, mid, 0f),
                new Vector3(BlockWidth, Ceiling, depth), _matWall);

            Box(g, "Bloc_Est", new Vector3(centerX, mid, 0f),
                new Vector3(BlockWidth, Ceiling, depth), _matWall);
        }

        // -------------------------------------------------------------------
        // Sites de bombe, dans les coins nord
        // -------------------------------------------------------------------
        private static void BuildSites(Transform parent)
        {
            var g = new GameObject("Sites").transform;
            g.SetParent(parent, false);

            CreateSite(g, "A", new Vector3(-28.5f, 0f, 32f), _matSiteA);
            CreateSite(g, "B", new Vector3(28.5f, 0f, 32f), _matSiteB);
        }

        private static void CreateSite(Transform parent, string name, Vector3 center, Material mat)
        {
            Box(parent, "Site_" + name, center + Vector3.up * 0.15f,
                new Vector3(15f, 0.3f, 7.5f), mat);

            // Volume de detection : c'est lui qui dira si la bombe est posable.
            var zone = new GameObject("Zone_" + name);
            zone.transform.SetParent(parent, false);
            zone.transform.position = center + Vector3.up * 1.5f;

            var col = zone.AddComponent<BoxCollider>();
            col.size = new Vector3(15f, 3f, 7.5f);
            col.isTrigger = true;

            var site = zone.AddComponent<BombSite>();
            site.SiteName = name;
        }

        // -------------------------------------------------------------------
        // Couverts
        // -------------------------------------------------------------------
        private static void BuildCover(Transform parent)
        {
            var g = new GameObject("Couverts").transform;
            g.SetParent(parent, false);

            // (x, z) en metres. Hauteur 1.1 m : on se cache accroupi, on tire debout.
            var spots = new[]
            {
                // voie ouest
                new Vector2(-30f, -11f), new Vector2(-23f, 4f), new Vector2(-28f, 15f),
                // couloir central
                new Vector2(-2f, -8f), new Vector2(3f, 6f), new Vector2(0f, 17f),
                // voie est
                new Vector2(30f, -11f), new Vector2(23f, 4f), new Vector2(28f, 15f),
                // sites
                new Vector2(-24f, 32f), new Vector2(-33f, 30f),
                new Vector2(24f, 32f), new Vector2(33f, 30f),
                // connecteur sud
                new Vector2(-15f, -32f), new Vector2(15f, -32f),
            };

            for (int i = 0; i < spots.Length; i++)
            {
                Box(g, $"Couvert_{i:00}",
                    new Vector3(spots[i].x, 0.55f, spots[i].y),
                    new Vector3(2.4f, 1.1f, 2.4f), _matCover);
            }
        }

        // -------------------------------------------------------------------
        // Apparitions
        // -------------------------------------------------------------------
        private static void BuildSpawns(Transform parent)
        {
            var g = new GameObject("Spawns").transform;
            g.SetParent(parent, false);

            // Defenseurs au nord, tournes vers le sud.
            for (int i = 0; i < 8; i++)
            {
                float x = -6.75f + (i % 4) * 4.5f;
                float z = (i < 4) ? 34.5f : 31.5f;
                CreateSpawn(g, Team.Defenders, new Vector3(x, 0.1f, z), 180f, i);
            }

            // Attaquants au sud, tournes vers le nord.
            for (int i = 0; i < 8; i++)
            {
                float x = -6.75f + (i % 4) * 4.5f;
                float z = (i < 4) ? -34.5f : -31.5f;
                CreateSpawn(g, Team.Attackers, new Vector3(x, 0.1f, z), 0f, i);
            }
        }

        private static void CreateSpawn(Transform parent, Team team, Vector3 pos, float yaw, int index)
        {
            var go = new GameObject($"Spawn_{team}_{index:00}");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<SpawnPoint>().Team = team;
        }

        // -------------------------------------------------------------------
        // Eclairage
        // -------------------------------------------------------------------
        private static void BuildLighting(Transform parent)
        {
            var g = new GameObject("Lumieres").transform;
            g.SetParent(parent, false);

            // Grille de lampes ponctuelles : la map est fermee, pas de soleil.
            for (float x = -32f; x <= 32f; x += 16f)
            {
                for (float z = -32f; z <= 32f; z += 16f)
                {
                    var go = new GameObject($"Lampe_{x:0}_{z:0}");
                    go.transform.SetParent(g, false);
                    go.transform.position = new Vector3(x, Ceiling - 1.2f, z);

                    var l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.range = 22f;
                    l.intensity = 1.15f;
                    l.color = new Color(1f, 0.96f, 0.89f);
                    l.shadows = LightShadows.None;   // greybox : on privilegie le framerate
                }
            }

            // Lumiere ambiante douce pour eviter les zones totalement noires.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.26f, 0.27f, 0.30f);
        }

        // -------------------------------------------------------------------
        // Chef d'orchestre : fait tourner le round et cree le joueur au Play
        // -------------------------------------------------------------------
        private static void BuildDirector()
        {
            var go = new GameObject("_RoundDirector");
            var d = go.AddComponent<RoundDirector>();

            // Reglages de test solo : on veut pouvoir se promener seul sans
            // que le round s'arrete faute d'adversaire.
            d.MinPlayers = 1;
            d.RequireBothTeams = false;
            d.FreezeTime = 8f;
            d.RoundTime = 90f;
            d.EndTime = 4f;
            d.WinLimit = 0;
            d.LocalTeam = Team.Attackers;
        }

        // -------------------------------------------------------------------
        // Utilitaires
        // -------------------------------------------------------------------
        private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.isStatic = true;

            if (mat != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            return go;
        }

        private static void EnsureMaterials()
        {
            Directory.CreateDirectory(MaterialDir);

            _matFloor = GetOrCreate("Greybox_Sol", new Color(0.38f, 0.39f, 0.42f));
            _matWall = GetOrCreate("Greybox_Mur", new Color(0.52f, 0.53f, 0.55f));
            _matSiteA = GetOrCreate("Greybox_SiteA", new Color(0.72f, 0.30f, 0.28f));
            _matSiteB = GetOrCreate("Greybox_SiteB", new Color(0.28f, 0.45f, 0.72f));
            _matCover = GetOrCreate("Greybox_Couvert", new Color(0.62f, 0.56f, 0.42f));

            AssetDatabase.SaveAssets();
        }

        private static Material GetOrCreate(string name, Color color)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.color = color;
                return existing;
            }

            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
