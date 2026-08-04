using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Supernova.MinecraftCaves.Editor
{
    public static class JigsawSuperflatSceneBuilder
    {
        public const string ScenePath = ProjectAssetPaths.Scenes.JigsawSuperflat;
        public const string ConfigurationPath =
            ProjectAssetPaths.Config.JigsawSuperflatWorldGeneration;
        private const int TerrainHeight = 96;

        private static readonly string[] JigsawStructurePaths =
        {
            ProjectAssetPaths.Config.AbandonedMineshaftJigsaw,
            ProjectAssetPaths.Config.StrongholdJigsaw,
            ProjectAssetPaths.Config.NetherFortressJigsaw,
            ProjectAssetPaths.Config.AncientCityJigsaw,
            ProjectAssetPaths.Config.CaveVillageJigsaw,
            ProjectAssetPaths.Config.AncientPrisonJigsaw,
            ProjectAssetPaths.Config.CactusGrottoJigsaw,
        };

        [MenuItem("Tools/Minecraft Caves/Rebuild Jigsaw Superflat Scene")]
        public static void RebuildJigsawSuperflatScene()
        {
            MinecraftWorldGenerationConfiguration configuration =
                CreateOrUpdateConfiguration();

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
            scene.name = "JigsawSuperflat";
            SceneManager.SetActiveScene(scene);

            var worldObject = new GameObject("Jigsaw Superflat World");
            MinecraftCaveInfiniteWorld world =
                worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
            var serializedWorld = new SerializedObject(world);
            serializedWorld.FindProperty("worldGenerationConfigurationOverride")
                .objectReferenceValue = configuration;
            serializedWorld.FindProperty("fixedPreviewArea").boolValue = false;
            serializedWorld.ApplyModifiedPropertiesWithoutUndo();

            CreateViewer();
            CreateDirectionalLight();
            ConfigureEnvironment();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"Failed to save {ScenePath}.");
            }

            AssetDatabase.SaveAssets();
            Selection.activeGameObject = worldObject;
            Debug.Log(
                $"Created jigsaw superflat scene at {ScenePath} with its "
                + $"world configuration at {ConfigurationPath}.");
        }

        private static MinecraftWorldGenerationConfiguration
            CreateOrUpdateConfiguration()
        {
            MinecraftWorldGenerationConfiguration configuration =
                AssetDatabase.LoadAssetAtPath<
                    MinecraftWorldGenerationConfiguration>(ConfigurationPath);
            if (configuration == null)
            {
                MinecraftWorldGenerationConfiguration source =
                    AssetDatabase.LoadAssetAtPath<
                        MinecraftWorldGenerationConfiguration>(
                        ProjectAssetPaths.Config.WorldGeneration);
                if (source == null)
                {
                    throw new InvalidOperationException(
                        "Missing source world configuration at "
                        + ProjectAssetPaths.Config.WorldGeneration + ".");
                }

                configuration = UnityEngine.Object.Instantiate(source);
                configuration.name = "JigsawSuperflatWorldGeneration";
                AssetDatabase.CreateAsset(configuration, ConfigurationPath);
            }

            var serializedConfiguration = new SerializedObject(configuration);
            serializedConfiguration.FindProperty("generationMode").enumValueIndex =
                (int)MinecraftWorldGenerationMode.Superflat;
            serializedConfiguration.FindProperty("superflatStoneHeight").intValue =
                TerrainHeight;
            serializedConfiguration.FindProperty("placeViewerInCave").boolValue =
                false;
            serializedConfiguration.FindProperty("oreFeatures").ClearArray();
            serializedConfiguration.FindProperty("caveBiomeCatalog")
                .objectReferenceValue = null;
            serializedConfiguration.FindProperty("structureFeatures").ClearArray();

            SerializedProperty spawnRule =
                serializedConfiguration.FindProperty("spawnPointStructureRule");
            spawnRule.FindPropertyRelative("enabled").boolValue = false;
            spawnRule.FindPropertyRelative("structure").objectReferenceValue = null;

            SerializedProperty structures =
                serializedConfiguration.FindProperty("jigsawStructures");
            structures.arraySize = JigsawStructurePaths.Length;
            for (int index = 0; index < JigsawStructurePaths.Length; index++)
            {
                JigsawStructureFeatureDefinition structure =
                    AssetDatabase.LoadAssetAtPath<
                        JigsawStructureFeatureDefinition>(
                        JigsawStructurePaths[index]);
                if (structure == null)
                {
                    throw new InvalidOperationException(
                        $"Missing jigsaw structure at "
                        + $"{JigsawStructurePaths[index]}.");
                }
                structures.GetArrayElementAtIndex(index).objectReferenceValue =
                    structure;
            }

            serializedConfiguration.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(configuration);
            return configuration;
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

        private static void CreateViewer()
        {
            var viewerObject = new GameObject("Superflat Explorer");
            viewerObject.tag = "MainCamera";
            viewerObject.transform.position = Vector3.zero;
            viewerObject.transform.rotation = Quaternion.Euler(12f, 35f, 0f);

            Camera camera = viewerObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 68f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 260f;
            camera.allowHDR = true;
            viewerObject.AddComponent<AudioListener>();
            viewerObject.AddComponent<MinecraftCaveFlyController>();
        }

        private static void CreateDirectionalLight()
        {
            var lightObject = new GameObject("Superflat Sun");
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.92f, 0.95f, 1f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
        }

        private static void ConfigureEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.35f, 0.43f, 0.56f);
            RenderSettings.ambientEquatorColor = new Color(0.2f, 0.23f, 0.27f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.075f, 0.07f);
            RenderSettings.reflectionIntensity = 0.5f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.42f, 0.48f, 0.56f);
            RenderSettings.fogStartDistance = 100f;
            RenderSettings.fogEndDistance = 240f;
        }
    }
}
