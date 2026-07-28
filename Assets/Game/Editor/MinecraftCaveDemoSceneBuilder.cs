using System;
using System.IO;
using Supernova.Voxels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Supernova.MinecraftCaves.Editor
{
    public static class MinecraftCaveDemoSceneBuilder
    {
        public const string ScenePath =
            ProjectAssetPaths.Scenes.CaveGallery;

        [MenuItem("Tools/Minecraft Caves/Rebuild Demo Scene")]
        public static void RebuildDemoScene()
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    ProjectAssetPaths.ToAbsoluteFileSystemPath(ScenePath))
                ?? string.Empty);
            NewSceneMode mode = HasDirtyLoadedScene()
                ? NewSceneMode.Additive
                : NewSceneMode.Single;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
            scene.name = "MinecraftCaveGallery";
            SceneManager.SetActiveScene(scene);

            var galleryObject = new GameObject("Minecraft Cave Gallery");
            galleryObject.AddComponent<MinecraftCaveGallery>();

            var focusObject = new GameObject("Camera Focus");
            focusObject.transform.SetParent(galleryObject.transform, false);
            focusObject.transform.localPosition = Vector3.zero;

            CreateCamera(focusObject.transform);
            CreateDirectionalLight(
                "Key Light",
                new Vector3(46f, -34f, 0f),
                new Color(1f, 0.88f, 0.73f),
                1.35f,
                true);
            CreateDirectionalLight(
                "Fill Light",
                new Vector3(24f, 145f, 18f),
                new Color(0.45f, 0.68f, 0.90f),
                0.55f,
                false);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.18f, 0.21f, 0.24f);
            RenderSettings.ambientEquatorColor = new Color(0.10f, 0.11f, 0.12f);
            RenderSettings.ambientGroundColor = new Color(0.035f, 0.038f, 0.042f);
            RenderSettings.reflectionIntensity = 0.55f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.025f, 0.029f, 0.034f);
            RenderSettings.fogStartDistance = 48f;
            RenderSettings.fogEndDistance = 92f;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"Failed to save {ScenePath}.");
            }

            Selection.activeGameObject = galleryObject;
            Debug.Log(
                $"Created isolated Minecraft cave demo scene at {ScenePath} "
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

        [MenuItem("Tools/Minecraft Caves/Validate Generation")]
        public static void ValidateGeneration()
        {
            const int seed = 18731;
            var settings = new MinecraftCaveSettings();
            var field = new MinecraftCaveDensityField(seed, settings);
            int totalTriangles = 0;

            foreach (MinecraftCaveType type in Enum.GetValues(typeof(MinecraftCaveType)))
            {
                var volume = new VoxelVolume();
                float centre = (VoxelVolume.Size - 1) * 0.5f;
                float inverseCentre = 1f / centre;
                int positive = 0;
                int negative = 0;
                int featurePositive = 0;
                int featureNegative = 0;

                for (int z = 0; z < VoxelVolume.Size; z++)
                {
                    for (int y = 0; y < VoxelVolume.Size; y++)
                    {
                        for (int x = 0; x < VoxelVolume.Size; x++)
                        {
                            Vector3 point = new Vector3(x - centre, y - centre, z - centre);
                            Vector3 normalized = point * inverseCentre;
                            float density = field.SampleSolidDensity(point, normalized, type, true);
                            volume[x, y, z] = density;
                            if (density >= 0f)
                            {
                                positive++;
                            }
                            else
                            {
                                negative++;
                            }

                            float featureDensity = type == MinecraftCaveType.Pillar
                                ? field.SamplePillar(point)
                                : field.SampleFeatureDensity(point, type);
                            if (featureDensity >= 0f)
                            {
                                featurePositive++;
                            }
                            else
                            {
                                featureNegative++;
                            }
                        }
                    }
                }

                if (positive == 0 || negative == 0)
                {
                    throw new InvalidOperationException(
                        $"{type} does not contain both solid and empty samples.");
                }

                if (featurePositive == 0 || featureNegative == 0)
                {
                    throw new InvalidOperationException(
                        $"{type} feature density does not cross the zero isosurface.");
                }

                VoxelMeshData meshData = MarchingCubesMesher.Build(volume, 0f, 0.38f);
                if (meshData.TriangleCount < 32)
                {
                    throw new InvalidOperationException(
                        $"{type} generated only {meshData.TriangleCount} triangles.");
                }

                totalTriangles += meshData.TriangleCount;
                Debug.Log(
                    $"{type}: {featurePositive:N0} feature-positive, "
                    + $"{featureNegative:N0} feature-negative, "
                    + $"{meshData.TriangleCount:N0} triangles.");
            }

            Vector3 seamPoint = new Vector3(32f, -7f, 19f);
            float first = field.SampleFeatureDensity(seamPoint, MinecraftCaveType.Combined);
            float second = new MinecraftCaveDensityField(seed, settings)
                .SampleFeatureDensity(seamPoint, MinecraftCaveType.Combined);
            if (BitConverter.SingleToInt32Bits(first) != BitConverter.SingleToInt32Bits(second))
            {
                throw new InvalidOperationException(
                    "World-coordinate density sampling is not deterministic at a chunk boundary.");
            }

            var leftChunk = new VoxelChunkData(0, 0, 0);
            var rightChunk = new VoxelChunkData(1, 0, 0);
            MinecraftCaveVolumeGenerator.FillChunk(leftChunk, field);
            MinecraftCaveVolumeGenerator.FillChunk(rightChunk, field);
            float expectedLeft = field.SampleFeatureDensity(
                new Vector3(VoxelVolume.Size - 1, 12f, 9f),
                MinecraftCaveType.Combined);
            float expectedRight = field.SampleFeatureDensity(
                new Vector3(VoxelVolume.Size, 12f, 9f),
                MinecraftCaveType.Combined);
            if (BitConverter.SingleToInt32Bits(leftChunk[VoxelVolume.Size - 1, 12, 9])
                    != BitConverter.SingleToInt32Bits(expectedLeft)
                || BitConverter.SingleToInt32Bits(rightChunk[0, 12, 9])
                    != BitConverter.SingleToInt32Bits(expectedRight))
            {
                throw new InvalidOperationException(
                    "Adjacent chunk generation does not preserve absolute world coordinates.");
            }

            TopologyStats topology = AnalyzeCombinedTopology(field, 64, 2);
            if (topology.EmptyFraction > 0.25f)
            {
                throw new InvalidOperationException(
                    $"Combined cave volume is too open: {topology.EmptyFraction:P1}.");
            }
            if (topology.LargestComponentFraction > 0.70f)
            {
                throw new InvalidOperationException(
                    "Combined caves have regressed into one dominant connected network: "
                    + $"{topology.LargestComponentFraction:P1}.");
            }
            Debug.Log(
                $"Combined topology: {topology.EmptyFraction:P1} empty, "
                + $"{topology.ComponentCount} disconnected spaces, "
                + $"largest space {topology.LargestComponentFraction:P1} of empty volume.");

            Debug.Log(
                $"Minecraft cave validation passed: five fields, {totalTriangles:N0} total triangles, "
                + "deterministic world-coordinate and adjacent-chunk sampling.");
        }

        private static TopologyStats AnalyzeCombinedTopology(
            MinecraftCaveDensityField field,
            int size,
            int sampleSpacing)
        {
            int sampleCount = size * size * size;
            var empty = new bool[sampleCount];
            var visited = new bool[sampleCount];
            var queue = new int[sampleCount];
            int emptyCount = 0;
            int halfExtent = size * sampleSpacing / 2;

            for (int z = 0; z < size; z++)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        Vector3 point = new Vector3(
                            x * sampleSpacing - halfExtent,
                            y * sampleSpacing - halfExtent,
                            z * sampleSpacing - halfExtent);
                        int index = x + size * (y + size * z);
                        empty[index] = field.SampleFeatureDensity(
                            point,
                            MinecraftCaveType.Combined) < 0f;
                        if (empty[index])
                        {
                            emptyCount++;
                        }
                    }
                }
            }

            int componentCount = 0;
            int largestComponent = 0;
            for (int start = 0; start < sampleCount; start++)
            {
                if (!empty[start] || visited[start])
                {
                    continue;
                }

                componentCount++;
                int head = 0;
                int tail = 0;
                int componentSize = 0;
                visited[start] = true;
                queue[tail++] = start;
                while (head < tail)
                {
                    int index = queue[head++];
                    componentSize++;
                    int x = index % size;
                    int yz = index / size;
                    int y = yz % size;
                    int z = yz / size;

                    TryVisit(x - 1, y, z);
                    TryVisit(x + 1, y, z);
                    TryVisit(x, y - 1, z);
                    TryVisit(x, y + 1, z);
                    TryVisit(x, y, z - 1);
                    TryVisit(x, y, z + 1);
                }

                largestComponent = Mathf.Max(largestComponent, componentSize);

                void TryVisit(int neighbourX, int neighbourY, int neighbourZ)
                {
                    if ((uint)neighbourX >= (uint)size
                        || (uint)neighbourY >= (uint)size
                        || (uint)neighbourZ >= (uint)size)
                    {
                        return;
                    }

                    int neighbour = neighbourX
                        + size * (neighbourY + size * neighbourZ);
                    if (!empty[neighbour] || visited[neighbour])
                    {
                        return;
                    }

                    visited[neighbour] = true;
                    queue[tail++] = neighbour;
                }
            }

            return new TopologyStats(
                emptyCount / (float)sampleCount,
                componentCount,
                emptyCount > 0 ? largestComponent / (float)emptyCount : 0f);
        }

        private readonly struct TopologyStats
        {
            public TopologyStats(
                float emptyFraction,
                int componentCount,
                float largestComponentFraction)
            {
                EmptyFraction = emptyFraction;
                ComponentCount = componentCount;
                LargestComponentFraction = largestComponentFraction;
            }

            public float EmptyFraction { get; }
            public int ComponentCount { get; }
            public float LargestComponentFraction { get; }
        }

        private static void CreateCamera(Transform focus)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.018f, 0.021f, 0.025f);
            camera.fieldOfView = 52f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 160f;
            camera.allowHDR = true;
            cameraObject.AddComponent<AudioListener>();
            MinecraftCaveOrbitCamera orbit = cameraObject.AddComponent<MinecraftCaveOrbitCamera>();
            orbit.Configure(focus, 52f, 4f);
        }

        private static void CreateDirectionalLight(
            string name,
            Vector3 rotation,
            Color color,
            float intensity,
            bool shadows)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.rotation = Quaternion.Euler(rotation);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        }
    }
}
