using Supernova.MinecraftCaves;
using Supernova.Missions;
using Supernova.Voxels;
using Supernova.Voxels.Support;
using Supernova.Voxels.Support.Prototype;
using Supernova.WorldGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates a standalone integration-test scene that combines the full
/// InfiniteCaves runtime pipeline with the voxel-support injection system.
///
/// The built scene contains:
/// - Player (instantiated from <c>Player.prefab</c>, READ-ONLY reference)
/// - MinecraftCaveInfiniteWorld wired to the standard world-generation config
/// - VoxelSupportInjector attached to Player, intercepts all mining
/// - Directional light + fog matching the InfiniteCaves look
///
/// No existing assets or code are modified.  The scene lives under
/// <c>Assets/Scenes/Prototypes/</c> and all support code is in
/// <c>Assets/Game/Runtime/Voxels/Support/</c>.
/// </summary>
public static class VoxelSupportSceneBuilder
{
    private const string OutputScenePath =
        "Assets/Scenes/Prototypes/VoxelSupportIntegration.unity";

    [MenuItem("Supernova/Support/Build Voxel Support Integration Scene")]
    public static void BuildScene()
    {
        MinecraftWorldGenerationConfiguration worldConfig =
            LoadDefaultWorldConfig();
        if (worldConfig == null)
        {
            Debug.LogError(
                "[VoxelSupportSceneBuilder] Could not load world-generation "
                + "configuration.  Ensure "
                + $"{ProjectAssetPaths.Config.WorldGeneration} exists.");
            return;
        }

        VoxelSupportConfig supportConfig =
            LoadSupportConfig();
        if (supportConfig == null)
        {
            Debug.LogError(
                "[VoxelSupportSceneBuilder] Could not load VoxelSupportConfig.  "
                + "Ensure Assets/Game/Config/Support/VoxelSupportConfig.asset "
                + "exists.");
            return;
        }

        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        scene.name = "VoxelSupportIntegration";

        // ── Player (from prefab, read-only) ──────────────────────────
        GameObject playerPrefab = Load<GameObject>(
            ProjectAssetPaths.Prefabs.Player);
        GameObject player;
        if (playerPrefab != null)
        {
            player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.name = "Player";
        }
        else
        {
            Debug.LogError(
                "[VoxelSupportSceneBuilder] Player prefab not found at "
                + $"{ProjectAssetPaths.Prefabs.Player}.");
            return;
        }

        if (player.GetComponent<VoxelPlayerController>() == null)
        {
            Debug.LogError(
                "[VoxelSupportSceneBuilder] Player prefab is missing "
                + "VoxelPlayerController — the scene will not work.");
            Object.DestroyImmediate(player);
            return;
        }

        // ── World ────────────────────────────────────────────────────
        var worldObject = new GameObject("Infinite Caves World");
        MinecraftCaveInfiniteWorld world =
            worldObject.AddComponent<MinecraftCaveInfiniteWorld>();

        // Use the SAME ConfigureDenseRegion path as the DenseJigsawRegion
        // scene builder, clamped to a modest size so the support system
        // has a manageable branch factor.
        DenseJigsawWorldConfiguration denseConfig =
            ScriptableObject.CreateInstance<DenseJigsawWorldConfiguration>();

        // Load the InfiniteCaves LevelConfiguration asset.
        LevelConfiguration levelSource = Load<LevelConfiguration>(
            "Assets/Game/Config/Levels/FirstLevel.asset");
        if (levelSource == null)
        {
            Debug.LogError(
                "[VoxelSupportSceneBuilder] FirstLevel.asset not found.");
            Object.DestroyImmediate(worldObject);
            return;
        }

        denseConfig.Configure(levelSource);
        denseConfig.ConfigureGenerationVolume(
            sectionCount: 4,
            columnsPerSide: 3,
            density: 1f);
        denseConfig.ConfigureStructureIntersections(false);

        world.ConfigureDenseRegion(denseConfig, player.transform);

        // ── Injector ─────────────────────────────────────────────────
        VoxelSupportInjector injector =
            player.AddComponent<VoxelSupportInjector>();
        injector.enabled = true;

        // Assign the support config via serialized field reflection
        // (the field is private [SerializeField]).
        var configField = typeof(VoxelSupportInjector).GetField(
            "config",
            System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance);
        if (configField != null)
        {
            configField.SetValue(injector, supportConfig);
        }

        // ── Lighting ─────────────────────────────────────────────────
        CreateLighting();

        // ── Save ─────────────────────────────────────────────────────
        EditorSceneManager.SaveScene(scene, OutputScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = player;

        Debug.Log(
            "[VoxelSupportSceneBuilder] Built VoxelSupportIntegration scene: "
            + $"{denseConfig.RegionColumnsPerSide * 2 + 1}×"
            + $"{denseConfig.RegionColumnsPerSide * 2 + 1} columns, "
            + $"{denseConfig.WorldSectionCount} sections, "
            + "support injection enabled on Player.");
    }

    // ── helpers ──────────────────────────────────────────────────────

    private static T Load<T>(string path) where T : Object
    {
        return AssetDatabase.LoadAssetAtPath<T>(path);
    }

    private static MinecraftWorldGenerationConfiguration LoadDefaultWorldConfig()
    {
        return Load<MinecraftWorldGenerationConfiguration>(
            ProjectAssetPaths.Config.WorldGeneration);
    }

    private static VoxelSupportConfig LoadSupportConfig()
    {
        return Load<VoxelSupportConfig>(
            "Assets/Game/Config/Support/VoxelSupportConfig.asset");
    }

    private static void CreateLighting()
    {
        var sun = new GameObject("Directional Light");
        Light light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.92f, 0.75f);
        light.intensity = 1.5f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.6f;
        sun.transform.rotation = Quaternion.Euler(50f, 330f, 0f);

        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.26f, 0.19f, 0.09f, 1f);
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 16f;
        RenderSettings.fogEndDistance = 64f;
    }
}
