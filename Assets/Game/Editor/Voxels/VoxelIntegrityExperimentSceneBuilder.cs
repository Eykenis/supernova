using System;
using Supernova.MinecraftCaves;
using Supernova.Missions;
using Supernova.Voxels;
using Supernova.Voxels.Integrity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Additive path registry for the isolated experiment. It is rooted in the
/// project-wide path table so the builder contains no independent asset root.
/// </summary>
public static class VoxelIntegrityExperimentAssetPaths
{
    public const string SceneFolder =
        ProjectAssetPaths.Folders.VoxelIntegrityExperimentScenes;
    public const string Scene =
        ProjectAssetPaths.Scenes.VoxelIntegrityExperiment;
}

public static class VoxelIntegrityExperimentSceneBuilder
{
    [MenuItem("Supernova/Experiments/Build Voxel Integrity Scene")]
    public static void BuildScene()
    {
        EnsureAssetFolder(VoxelIntegrityExperimentAssetPaths.SceneFolder);
        LevelConfiguration level = Load<LevelConfiguration>(
            ProjectAssetPaths.Config.FirstLevel);
        GameObject playerPrefab = Load<GameObject>(
            ProjectAssetPaths.Prefabs.Player);
        if (level == null || level.WorldGeneration == null)
        {
            Debug.LogError(
                "The isolated integrity scene requires FirstLevel from the "
                + "registered project path: "
                + ProjectAssetPaths.Config.FirstLevel);
            return;
        }
        if (playerPrefab == null)
        {
            Debug.LogError(
                "The isolated integrity scene requires the Player prefab from "
                + "the registered project path: "
                + ProjectAssetPaths.Prefabs.Player);
            return;
        }

        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        scene.name = "VoxelIntegrityExperiment";

        GameObject player =
            (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
        player.name = "Player";
        VoxelPlayerInteractor[] interactors =
            player.GetComponentsInChildren<VoxelPlayerInteractor>(true);
        Camera playerCamera = player.GetComponentInChildren<Camera>(true);
        if (interactors.Length == 0 || playerCamera == null)
        {
            Debug.LogError(
                "The registered Player prefab must contain a "
                + nameof(VoxelPlayerInteractor)
                + " and a Camera.",
                player);
            return;
        }

        var worldObject = new GameObject("Real Infinite Cave World");
        MinecraftCaveInfiniteWorld world =
            worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
        if (!world.ApplyLevelConfiguration(level))
        {
            Debug.LogError(
                "The real world rejected FirstLevel before scene serialization.",
                world);
            return;
        }
        if (!world.ConfigureLevels(new[] { level }))
        {
            Debug.LogError(
                "The real world rejected the configured level list.",
                world);
            return;
        }

        var serializedWorld = new SerializedObject(world);
        serializedWorld.FindProperty("viewer")
            .objectReferenceValue = player.transform;
        serializedWorld.ApplyModifiedPropertiesWithoutUndo();

        var bridgeObject = new GameObject("Integrity Experiment Bridge");
        VoxelIntegrityWorldBridge bridge =
            bridgeObject.AddComponent<VoxelIntegrityWorldBridge>();
        bridge.Configure(world);
        for (int i = 0; i < interactors.Length; i++)
        {
            var serializedInteractor = new SerializedObject(interactors[i]);
            serializedInteractor.FindProperty("terrain")
                .objectReferenceValue = bridge;
            serializedInteractor.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorUtility.SetDirty(world);
        EditorUtility.SetDirty(bridge);
        CreateLighting();
        EditorSceneManager.SaveScene(
            scene,
            VoxelIntegrityExperimentAssetPaths.Scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = bridgeObject;
        Debug.Log(
            "Built the isolated voxel-integrity regression scene with "
            + "FirstLevel's real world, Player prefab, and production "
            + "integrity bridge. It remains outside Build Settings.");
    }

    private static void CreateLighting()
    {
        var lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        light.color = new Color(1f, 0.94f, 0.84f);
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        RenderSettings.ambientLight = new Color(0.27f, 0.3f, 0.36f);
    }

    private static void EnsureAssetFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder))
            return;

        string[] segments = assetFolder.Split('/');
        if (segments.Length == 0 || segments[0] != "Assets")
        {
            throw new ArgumentException(
                "Asset folders must be rooted below Assets.",
                nameof(assetFolder));
        }

        string current = segments[0];
        for (int i = 1; i < segments.Length; i++)
        {
            string next = current + "/" + segments[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[i]);
            current = next;
        }
    }

    private static T Load<T>(string path) where T : UnityEngine.Object
    {
        return AssetDatabase.LoadAssetAtPath<T>(path);
    }
}
