using System;
using System.Collections.Generic;
using System.IO;
using Supernova.Voxels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Supernova.MinecraftCaves.Editor
{
    public static class MinecraftCaveInfiniteSceneBuilder
    {
        public const string ScenePath =
            ProjectAssetPaths.Scenes.InfiniteWorldDemo;

        [MenuItem("Tools/Minecraft Caves/Rebuild Infinite World Scene")]
        public static void RebuildInfiniteWorldScene()
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    ProjectAssetPaths.ToAbsoluteFileSystemPath(ScenePath))
                ?? string.Empty);
            NewSceneMode mode = HasDirtyLoadedScene()
                ? NewSceneMode.Additive
                : NewSceneMode.Single;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
            scene.name = "MinecraftCaveInfiniteWorld";
            SceneManager.SetActiveScene(scene);

            var worldObject = new GameObject("Minecraft Infinite Cave World");
            worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
            CreateViewer();
            CreateDirectionalLight(
                "Warm Directional Light",
                new Vector3(38f, -32f, 4f),
                new Color(1f, 0.79f, 0.61f),
                0.8f);
            CreateDirectionalLight(
                "Cool Directional Light",
                new Vector3(-28f, 142f, -12f),
                new Color(0.42f, 0.63f, 0.84f),
                0.65f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.16f, 0.18f, 0.19f);
            RenderSettings.ambientEquatorColor = new Color(0.075f, 0.083f, 0.086f);
            RenderSettings.ambientGroundColor = new Color(0.025f, 0.028f, 0.03f);
            RenderSettings.reflectionIntensity = 0.35f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.018f, 0.022f, 0.024f);
            RenderSettings.fogDensity = 0.012f;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"Failed to save {ScenePath}.");
            }

            Selection.activeGameObject = worldObject;
            Debug.Log(
                $"Created isolated Minecraft infinite cave scene at {ScenePath} "
                + $"using {mode} mode.");
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

        [MenuItem("Tools/Minecraft Caves/Validate Infinite World")]
        public static void ValidateInfiniteWorld()
        {
            if (VoxelColumnChunkData.Width != 32
                || VoxelColumnChunkData.Depth != 32
                || VoxelColumnChunkData.Height != 256
                || VoxelColumnChunkData.VoxelCount != 32 * 32 * 256)
            {
                throw new InvalidOperationException(
                    "World columns are not exactly 32x32x256 samples.");
            }
            if (MinecraftCaveInfiniteWorld.GenerationRadiusInChunks != 4)
            {
                throw new InvalidOperationException("Infinite cave generation radius is not 4 chunks.");
            }

            IReadOnlyList<Vector3Int> offsets = MinecraftCaveInfiniteWorld.StreamingOffsets;
            if (offsets.Count != MinecraftCaveInfiniteWorld.RequiredChunkCountAtRadius)
            {
                throw new InvalidOperationException(
                    $"Radius-4 XZ disk contains {offsets.Count} columns instead of "
                    + $"{MinecraftCaveInfiniteWorld.RequiredChunkCountAtRadius}.");
            }

            var unique = new HashSet<Vector3Int>();
            int radiusSquared = MinecraftCaveInfiniteWorld.GenerationRadiusInChunks
                * MinecraftCaveInfiniteWorld.GenerationRadiusInChunks;
            int previousDistance = -1;
            foreach (Vector3Int offset in offsets)
            {
                if (!unique.Add(offset))
                {
                    throw new InvalidOperationException($"Duplicate streaming offset: {offset}.");
                }
                if (offset.y != 0)
                {
                    throw new InvalidOperationException(
                        $"Streaming offset contains a vertical layer: {offset}.");
                }
                if (offset.sqrMagnitude > radiusSquared)
                {
                    throw new InvalidOperationException($"Offset outside radius 4: {offset}.");
                }
                if (offset.sqrMagnitude < previousDistance)
                {
                    throw new InvalidOperationException("Streaming offsets are not near-to-far sorted.");
                }
                previousDistance = offset.sqrMagnitude;
            }

            var settings = new MinecraftCaveSettings();
            var field = new MinecraftCaveDensityField(18731, settings);
            var negativeChunk = new VoxelColumnChunkData(-3, -1, 1f);
            MinecraftCaveVolumeGenerator.FillColumn(negativeChunk, field);
            Vector3Int sampleWorld = new Vector3Int(
                negativeChunk.OriginX + 31,
                217,
                negativeChunk.OriginZ + 5);
            float expected = field.SampleFeatureDensity(
                sampleWorld,
                MinecraftCaveType.Combined);
            float actual = negativeChunk[31, 217, 5];
            if (BitConverter.SingleToInt32Bits(expected) != BitConverter.SingleToInt32Bits(actual))
            {
                throw new InvalidOperationException(
                    "Negative-coordinate chunk sampling does not use its absolute world origin.");
            }

            Debug.Log(
                "Minecraft infinite world validation passed: column size 32x32x256, "
                + "XZ radius 4, 49 unique near-to-far offsets, "
                + "negative-coordinate absolute sampling.");
        }

        private static void CreateViewer()
        {
            var viewerObject = new GameObject("Cave Viewer");
            viewerObject.tag = "MainCamera";
            Camera camera = viewerObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.012f, 0.015f, 0.017f);
            camera.fieldOfView = 68f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 150f;
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

        private static void CreateDirectionalLight(
            string name,
            Vector3 rotation,
            Color color,
            float intensity)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.rotation = Quaternion.Euler(rotation);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }
    }
}
