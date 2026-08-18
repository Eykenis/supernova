using System;
using System.IO;
using Supernova.Missions;
using Supernova.WorldGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Supernova.MinecraftCaves.Editor
{
    public static class WorldGenerationPassDebugSceneBuilder
    {
        private const int PreviewColumnsPerSide = 4;
        private const int InitialSeed = 18731;
        private const int SelectablePassCount = 4;

        public const string ScenePath =
            ProjectAssetPaths.Scenes.WorldGenerationPassDebug;

        [MenuItem("Tools/Minecraft Caves/Rebuild World Generation Pass Debug Scene")]
        public static void RebuildWorldGenerationPassDebugScene()
        {
            CloseFailedDebugScenes();
            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    ProjectAssetPaths.ToAbsoluteFileSystemPath(ScenePath))
                ?? string.Empty);
            NewSceneMode mode = HasDirtyLoadedScene()
                ? NewSceneMode.Additive
                : NewSceneMode.Single;
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                mode);
            scene.name = "WorldGenerationPassDebug";
            SceneManager.SetActiveScene(scene);

            DenseJigsawWorldConfiguration denseConfiguration =
                LoadRequired<DenseJigsawWorldConfiguration>(
                    ProjectAssetPaths.Config.DenseJigsawRegionWorldGeneration);
            LevelConfiguration[] levels = LoadCampaignLevels();
            JigsawStructureFeatureDefinition spawnCheckpoint =
                LoadRequired<JigsawStructureFeatureDefinition>(
                    ProjectAssetPaths.Config.SpawnCheckpointHallJigsaw);

            Transform viewer = CreateViewer();
            var controllerObject = new GameObject(
                "DenseJigsaw Pass Debug Controller");
            WorldGenerationPassDebugController controller =
                controllerObject.AddComponent<WorldGenerationPassDebugController>();

            var worldsRoot = new GameObject("Cached Pass Worlds");
            MinecraftCaveInfiniteWorld[] passWorlds =
                new MinecraftCaveInfiniteWorld[SelectablePassCount];
            for (int i = 0; i < passWorlds.Length; i++)
            {
                MinecraftWorldGenerationDebugPass pass =
                    (MinecraftWorldGenerationDebugPass)(i + 1);
                passWorlds[i] = CreatePassWorld(
                    pass,
                    levels,
                    denseConfiguration,
                    spawnCheckpoint,
                    viewer,
                    worldsRoot.transform);
            }

            SerializedObject serializedController =
                new SerializedObject(controller);
            SerializedProperty serializedWorlds =
                serializedController.FindProperty("passWorlds");
            serializedWorlds.arraySize = passWorlds.Length;
            for (int i = 0; i < passWorlds.Length; i++)
            {
                serializedWorlds.GetArrayElementAtIndex(i)
                    .objectReferenceValue = passWorlds[i];
            }
            serializedController.FindProperty("initialPass").enumValueIndex =
                (int)MinecraftWorldGenerationDebugPass.NaturalTerrain;
            serializedController.FindProperty("initialSeed").intValue =
                InitialSeed;
            serializedController.FindProperty("showOverlay").boolValue = true;
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            CreateDirectionalLight();
            ConfigureEnvironment();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException(
                    $"Failed to save {ScenePath}.");
            }

            Selection.activeGameObject = controllerObject;
            Debug.Log(
                $"Created DenseJigsaw pass debug scene at {ScenePath}. "
                + "It pre-generates four cached 4x4 origin regions. Use F1-F4 "
                + "to switch instantly, F5 for screenshot mode, and the seed "
                + "controls to regenerate all caches.");
        }

        private static MinecraftCaveInfiniteWorld CreatePassWorld(
            MinecraftWorldGenerationDebugPass pass,
            LevelConfiguration[] levels,
            DenseJigsawWorldConfiguration denseConfiguration,
            JigsawStructureFeatureDefinition spawnCheckpoint,
            Transform viewer,
            Transform parent)
        {
            var worldObject = new GameObject(GetPassWorldName(pass));
            worldObject.transform.SetParent(parent, false);
            MinecraftCaveInfiniteWorld world =
                worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
            if (!world.ConfigureLevels(levels))
            {
                throw new InvalidOperationException(
                    $"Failed to configure levels for {pass} debug world.");
            }
            if (!world.ConfigureDenseRegion(denseConfiguration, viewer))
            {
                throw new InvalidOperationException(
                    $"Failed to configure DenseJigsaw for {pass} debug world.");
            }
            world.ConfigureSpawnCheckpointJigsaw(spawnCheckpoint);

            SerializedObject serializedWorld = new SerializedObject(world);
            serializedWorld.FindProperty("fixedPreviewArea").boolValue = true;
            serializedWorld.FindProperty("fixedPreviewColumnsPerSide").intValue =
                PreviewColumnsPerSide;
            serializedWorld.FindProperty("overrideWorldSeed").boolValue = true;
            serializedWorld.FindProperty("worldSeedOverride").intValue =
                InitialSeed;
            serializedWorld.FindProperty("generationDebugPass").enumValueIndex =
                (int)pass;
            serializedWorld
                .FindProperty("keepViewerTransformDuringGeneration")
                .boolValue = true;
            serializedWorld.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(world);
            return world;
        }

        private static string GetPassWorldName(
            MinecraftWorldGenerationDebugPass pass)
        {
            switch (pass)
            {
                case MinecraftWorldGenerationDebugPass.NaturalTerrain:
                    return "01 Natural Terrain Cache";
                case MinecraftWorldGenerationDebugPass.OreGeneration:
                    return "02 Ore Generation Cache";
                case MinecraftWorldGenerationDebugPass.JigsawStructures:
                    return "03 Jigsaw Structures Cache";
                case MinecraftWorldGenerationDebugPass.MarkerObjects:
                    return "04 Marker Objects Cache";
                default:
                    throw new ArgumentOutOfRangeException(nameof(pass), pass, null);
            }
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
                levels[i] = LoadRequired<LevelConfiguration>(paths[i]);
            }
            return levels;
        }

        private static T LoadRequired<T>(string assetPath)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Missing {typeof(T).Name} at {assetPath}.");
            }
            return asset;
        }

        private static bool HasDirtyLoadedScene()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                if (SceneManager.GetSceneAt(index).isDirty)
                {
                    return true;
                }
            }
            return false;
        }

        private static Transform CreateViewer()
        {
            var viewerObject = new GameObject("Pass Debug Camera");
            viewerObject.tag = "MainCamera";
            viewerObject.transform.position = new Vector3(0f, 13f, -8f);
            viewerObject.transform.rotation = Quaternion.Euler(8f, 0f, 0f);

            Camera camera = viewerObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.012f, 0.015f, 0.017f);
            camera.fieldOfView = 68f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 180f;
            camera.allowHDR = true;
            viewerObject.AddComponent<AudioListener>();
            viewerObject.AddComponent<MinecraftCaveFlyController>();

            Light headLight = viewerObject.AddComponent<Light>();
            headLight.type = LightType.Point;
            headLight.range = 34f;
            headLight.intensity = 2.3f;
            headLight.color = new Color(0.94f, 0.84f, 0.69f);
            headLight.shadows = LightShadows.None;
            return viewerObject.transform;
        }

        private static void CreateDirectionalLight()
        {
            var lightObject = new GameObject("Pass Debug Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(42f, -28f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.78f, 0.86f, 1f);
            light.intensity = 0.75f;
            light.shadows = LightShadows.None;
        }

        private static void ConfigureEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.13f, 0.15f, 0.18f);
            RenderSettings.ambientEquatorColor = new Color(0.07f, 0.08f, 0.1f);
            RenderSettings.ambientGroundColor = new Color(0.02f, 0.025f, 0.03f);
            RenderSettings.reflectionIntensity = 0.3f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.018f, 0.022f, 0.028f);
            RenderSettings.fogDensity = 0.01f;
        }
    

private static void CloseFailedDebugScenes()
        {
            for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
            {
                Scene loadedScene = SceneManager.GetSceneAt(index);
                if (string.IsNullOrEmpty(loadedScene.path)
                    && loadedScene.name == "WorldGenerationPassDebug")
                {
                    if (SceneManager.sceneCount == 1)
                    {
                        EditorSceneManager.NewScene(
                            NewSceneSetup.EmptyScene,
                            NewSceneMode.Additive);
                    }
                    EditorSceneManager.CloseScene(loadedScene, true);
                }
            }
        }
}
}
