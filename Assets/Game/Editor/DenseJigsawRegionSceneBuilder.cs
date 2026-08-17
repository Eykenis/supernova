using Supernova.MinecraftCaves;
using Supernova.Missions;
using Supernova.PortalExample;
using Supernova.PortalExample.Editor;

using Supernova.Voxels.Integrity;
using Supernova.Voxels;
using Supernova.WorldGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DenseJigsawRegionSceneBuilder
{
    private const float LandingCellScale = 0.7f;

    [MenuItem("Supernova/World Generation/Build Configured Dense Jigsaw Scene")]
    public static void BuildScene()
    {
        DenseJigsawWorldConfiguration configuration =
            LoadOrCreateConfiguration();
        if (configuration == null)
        {
            return;
        }

        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        scene.name = "DenseJigsawRegion";
        configuration = Load<DenseJigsawWorldConfiguration>(
            ProjectAssetPaths.Config.DenseJigsawRegionWorldGeneration);
        if (configuration == null)
        {
            Debug.LogError(
                "Dense jigsaw configuration could not be reloaded after "
                + "creating the scene.");
            return;
        }

        GameObject playerPrefab = Load<GameObject>(
            ProjectAssetPaths.Prefabs.Player);
        GameObject player = playerPrefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab)
            : new GameObject("Player");
        player.name = "Player";
        if (player.GetComponent<VoxelPlayerController>() == null)
        {
            Debug.LogError(
                "Dense jigsaw scene requires the configured Player prefab.");
            Object.DestroyImmediate(player);
            return;
        }
        EnsurePortalTraveller(player);

        SpawnPointSceneStructure landingCell = CreateLandingCell();
        if (landingCell == null)
        {
            Object.DestroyImmediate(player);
            return;
        }

        var worldObject = new GameObject("Dense Jigsaw Region World");
        MinecraftCaveInfiniteWorld world =
            worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
        if (!world.ConfigureLevels(LoadCampaignLevels()))
        {
            Debug.LogError(
                "Dense jigsaw world requires at least one configured level.",
                world);
            return;
        }
        if (!world.ConfigureDenseRegion(configuration, player.transform))
        {
            Debug.LogError(
                "Dense jigsaw world rejected its configuration before scene "
                + "serialization.",
                world);
            return;
        }
        world.ConfigureSpawnCheckpointJigsaw(
            Load<JigsawStructureFeatureDefinition>(
                ProjectAssetPaths.Config.SpawnCheckpointHallJigsaw));
        world.ConfigureSpawnPointSceneStructure(landingCell);
        ConfigurePortalBridge(world, landingCell, player.transform);
        ConfigureVoxelIntegrity(world, player);
        EditorUtility.SetDirty(world);

        CreateLighting();
        EditorSceneManager.SaveScene(
            scene,
            ProjectAssetPaths.Scenes.DenseJigsawRegion);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = worldObject;
        Debug.Log(
            "Built DenseJigsawRegion with the complete InfiniteCaves runtime "
            + $"pipeline and a {configuration.RegionColumnsPerSide}x"
            + $"{configuration.RegionColumnsPerSide}, "
            + $"{configuration.WorldSectionCount}-section mixed-jigsaw profile.");
    }

    [MenuItem("Supernova/World Generation/Upgrade Dense Jigsaw Scene To Shared Runtime")]
    public static void UpgradeExistingScene()
    {
        DenseJigsawWorldConfiguration configuration =
            LoadOrCreateConfiguration();
        if (configuration == null)
        {
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(
            ProjectAssetPaths.Scenes.DenseJigsawRegion,
            OpenSceneMode.Single);
        configuration = Load<DenseJigsawWorldConfiguration>(
            ProjectAssetPaths.Config.DenseJigsawRegionWorldGeneration);
        if (configuration == null)
        {
            Debug.LogError(
                "Dense jigsaw configuration could not be reloaded after "
                + "opening the scene.");
            return;
        }
        VoxelPlayerController player =
            Object.FindObjectOfType<VoxelPlayerController>();
        if (player == null)
        {
            Debug.LogError("DenseJigsawRegion scene has no Player.");
            return;
        }
        EnsurePortalTraveller(player.gameObject);

        SpawnPointSceneStructure landingCell =
            Object.FindObjectOfType<SpawnPointSceneStructure>();
        if (landingCell == null)
        {
            landingCell = CreateLandingCell();
        }
        if (landingCell == null)
        {
            return;
        }

        MinecraftCaveInfiniteWorld world =
            Object.FindObjectOfType<MinecraftCaveInfiniteWorld>();
        if (world == null)
        {
            var worldObject = new GameObject("Dense Jigsaw Region World");
            world = worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
        }
        if (!world.ConfigureLevels(LoadCampaignLevels()))
        {
            Debug.LogError(
                "Dense jigsaw world requires at least one configured level.",
                world);
            return;
        }
        world.ConfigureDenseRegion(configuration, player.transform);
        world.ConfigureSpawnCheckpointJigsaw(
            Load<JigsawStructureFeatureDefinition>(
                ProjectAssetPaths.Config.SpawnCheckpointHallJigsaw));
        world.ConfigureSpawnPointSceneStructure(landingCell);
        ConfigurePortalBridge(world, landingCell, player.transform);
        ConfigureVoxelIntegrity(world, player.gameObject);
        EditorUtility.SetDirty(world);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    private static LevelConfiguration[] LoadCampaignLevels()
    {
        string[] paths =
        {
            ProjectAssetPaths.Config.FirstLevel,
            ProjectAssetPaths.Config.SecondLevel,
            ProjectAssetPaths.Config.ThirdLevel,
        };
        var levels = new LevelConfiguration[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            levels[i] = Load<LevelConfiguration>(paths[i]);
            if (levels[i] == null)
            {
                throw new UnityException(
                    "Dense jigsaw scene requires the configured level at "
                    + paths[i] + ".");
            }
        }
        return levels;
    }

    private static DenseJigsawWorldConfiguration LoadOrCreateConfiguration()
    {
        EnsureFolder(ProjectAssetPaths.Folders.Worlds);
        DenseJigsawWorldConfiguration configuration =
            Load<DenseJigsawWorldConfiguration>(
                ProjectAssetPaths.Config.DenseJigsawRegionWorldGeneration);
        if (configuration == null)
        {
            configuration =
                ScriptableObject.CreateInstance<DenseJigsawWorldConfiguration>();
            AssetDatabase.CreateAsset(
                configuration,
                ProjectAssetPaths.Config.DenseJigsawRegionWorldGeneration);
        }

        LevelConfiguration sourceLevel = Load<LevelConfiguration>(
            ProjectAssetPaths.Config.FirstLevel);
        if (sourceLevel == null || sourceLevel.WorldGeneration == null)
        {
            Debug.LogError(
                "Dense jigsaw scene requires the InfiniteCaves FirstLevel "
                + "configuration source.");
            return null;
        }
        configuration.Configure(sourceLevel);
        EditorUtility.SetDirty(configuration);
        AssetDatabase.SaveAssets();
        return configuration;
    }

    private static void CreateLighting()
    {
        var lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

        RenderSettings.ambientMode =
            UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.18f, 0.2f, 0.24f);
        RenderSettings.ambientEquatorColor = new Color(0.09f, 0.1f, 0.12f);
        RenderSettings.ambientGroundColor =
            new Color(0.025f, 0.025f, 0.03f);
    }

    private static SpawnPointSceneStructure CreateLandingCell()
    {
        GameObject prefab = Load<GameObject>(
            ProjectAssetPaths.Prefabs.LandingCell);
        if (prefab == null)
        {
            Debug.LogError(
                "Dense jigsaw scene requires the landing Cell prefab at the "
                + "registered project path: "
                + ProjectAssetPaths.Prefabs.LandingCell);
            return null;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = ProjectAssetPaths.LookupNames.MissionCell;
        instance.transform.localScale = Vector3.one * LandingCellScale;
        SpawnPointSceneStructure structure =
            instance.GetComponent<SpawnPointSceneStructure>();
        if (structure == null)
        {
            Debug.LogError(
                "The registered landing Cell prefab has no "
                + nameof(SpawnPointSceneStructure) + " component.",
                instance);
            Object.DestroyImmediate(instance);
        }
        return structure;
    }

    private static void ConfigurePortalBridge(
        MinecraftCaveInfiniteWorld world,
        SpawnPointSceneStructure landingCell,
        Transform player)
    {
        DenseJigsawPortalBridge bridge =
            Object.FindObjectOfType<DenseJigsawPortalBridge>();
        PortalExampleGate landingGate = bridge != null
            ? bridge.LandingCellGate
            : null;
        PortalExampleGate checkpointGate = bridge != null
            ? bridge.CheckpointGate
            : null;
        if (bridge == null || landingGate == null || checkpointGate == null)
        {
            PortalExampleSceneBuilder.EnsurePortalAssets(
                out Material bluePortal,
                out Material orangePortal,
                out Mesh ringMesh);
            GameObject bridgeObject = bridge != null
                ? bridge.gameObject
                : new GameObject("Dense Checkpoint Portal Bridge");
            bridge = bridge != null
                ? bridge
                : bridgeObject.AddComponent<DenseJigsawPortalBridge>();
            landingGate = PortalExampleSceneBuilder.CreatePortal(
                bridgeObject.transform,
                "Landing Cell Portal / 登陆舱传送门",
                Vector3.zero,
                Quaternion.identity,
                bluePortal,
                ringMesh);
            checkpointGate = PortalExampleSceneBuilder.CreatePortal(
                bridgeObject.transform,
                "Spawn Checkpoint Portal / 出生检查点传送门",
                Vector3.zero,
                Quaternion.identity,
                orangePortal,
                ringMesh);
            PortalExampleSceneBuilder.LinkPortals(
                landingGate,
                checkpointGate);
            PortalExampleSceneBuilder.LinkPortals(
                checkpointGate,
                landingGate);
            landingGate.gameObject.SetActive(false);
            checkpointGate.gameObject.SetActive(false);
        }

        if (bridge.transform.parent != landingCell.transform)
        {
            bridge.transform.SetParent(landingCell.transform, true);
        }

        bridge.Configure(
            world,
            landingCell,
            player,
            landingGate,
            checkpointGate);
        EditorUtility.SetDirty(bridge);
    }

    private static void EnsurePortalTraveller(GameObject player)
    {
        if (player.GetComponent<PortalExampleTraveller>() == null)
        {
            player.AddComponent<PortalExampleTraveller>();
        }
    }

    private static T Load<T>(string path) where T : Object
    {
        return AssetDatabase.LoadAssetAtPath<T>(path);
    }

    private static void EnsureFolder(string path)
    {
        string[] segments = path.Split('/');
        string current = segments[0];
        for (int i = 1; i < segments.Length; i++)
        {
            string next = current + "/" + segments[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[i]);
            }
            current = next;
        }
    }


    private static void ConfigureVoxelIntegrity(
                MinecraftCaveInfiniteWorld world,
                GameObject player)
    {
        if (world == null || player == null)
        {
            return;
        }

        VoxelIntegrityWorldBridge bridge =
            world.GetComponent<VoxelIntegrityWorldBridge>();
        if (bridge == null)
        {
            bridge =
                world.gameObject.AddComponent<VoxelIntegrityWorldBridge>();
        }
        bridge.Configure(world);

        var serializedBridge = new SerializedObject(bridge);
        serializedBridge.FindProperty("showDebugOverlay").boolValue = false;
        serializedBridge.ApplyModifiedPropertiesWithoutUndo();

        VoxelPlayerInteractor[] interactors =
            player.GetComponentsInChildren<VoxelPlayerInteractor>(true);
        if (interactors.Length == 0)
        {
            Debug.LogError(
                "DenseJigsawRegion Player requires a "
                + nameof(VoxelPlayerInteractor)
                + " for voxel-integrity routing.",
                player);
            return;
        }

        for (int i = 0; i < interactors.Length; i++)
        {
            var serializedInteractor =
                new SerializedObject(interactors[i]);
            serializedInteractor.FindProperty("terrain")
                .objectReferenceValue = bridge;
            serializedInteractor.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(interactors[i]);
        }
        EditorUtility.SetDirty(bridge);
    }
}
