using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Supernova.MinecraftCaves.Editor
{
    public static class WorldGenerationPreviewSceneBuilder
    {
        public const string ScenePath =
            ProjectAssetPaths.Scenes.WorldGenerationPreview;

        [MenuItem("Tools/Minecraft Caves/Rebuild World Generation Preview Scene")]
        public static void RebuildWorldGenerationPreviewScene()
        {
            MinecraftWorldGenerationConfiguration configuration =
                AssetDatabase.LoadAssetAtPath<
                    MinecraftWorldGenerationConfiguration>(
                    ProjectAssetPaths.Config.WorldGeneration);
            if (configuration == null)
            {
                throw new InvalidOperationException(
                    "Missing world generation configuration at "
                    + ProjectAssetPaths.Config.WorldGeneration + ".");
            }

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
            scene.name = "WorldGenerationPreview";
            SceneManager.SetActiveScene(scene);

            var worldObject = new GameObject("World Generation Preview");
            MinecraftCaveInfiniteWorld world =
                worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
            SerializedObject serializedWorld = new SerializedObject(world);
            serializedWorld.FindProperty("worldGenerationConfigurationOverride")
                .objectReferenceValue = configuration;
            serializedWorld.FindProperty("fixedPreviewArea").boolValue = true;
            serializedWorld.ApplyModifiedPropertiesWithoutUndo();

            CreateViewer();
            CreateDirectionalLight();
            ConfigureEnvironment();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException(
                    $"Failed to save {ScenePath}.");
            }

            Selection.activeGameObject = worldObject;
            Debug.Log(
                $"Created diameter-16-chunk world generation preview at {ScenePath}. "
                + "Select 'World Generation Preview' and assign another "
                + "MinecraftWorldGenerationConfiguration to preview it.");
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
            var viewerObject = new GameObject("Preview Camera");
            viewerObject.tag = "MainCamera";
            viewerObject.transform.position = new Vector3(0f, 0f, 0f);
            viewerObject.transform.rotation = Quaternion.Euler(15f, 35f, 0f);

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
        }

        private static void CreateDirectionalLight()
        {
            var lightObject = new GameObject("Preview Directional Light");
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
    }
}
