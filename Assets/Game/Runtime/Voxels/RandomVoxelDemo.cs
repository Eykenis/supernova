using System;
using UnityEngine;

namespace Supernova.Voxels
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class RandomVoxelDemo : MonoBehaviour
    {
        [Header("Random field")]
        [SerializeField] private int seed = 20260721;
        [SerializeField, Min(0.01f)] private float noiseScale = 0.105f;
        [SerializeField, Range(0f, 2f)] private float noiseStrength = 0.9f;
        [SerializeField, Range(0.2f, 1.2f)] private float bodyRadius = 0.82f;

        [Header("Surface")]
        [SerializeField] private float isoLevel;
        [SerializeField, Min(0.01f)] private float voxelSize = 0.65f;
        [SerializeField] private Color surfaceColor = new Color(0.16f, 0.62f, 0.48f, 1f);
        [SerializeField] private bool generateCollider = true;

        [Header("Generated statistics")]
        [SerializeField, HideInInspector] private int vertexCount;
        [SerializeField, HideInInspector] private int triangleCount;

        private VoxelVolume volume;
        private Mesh generatedMesh;
        private Material generatedMaterial;

        public VoxelVolume Volume => volume;
        public int VertexCount => vertexCount;
        public int TriangleCount => triangleCount;

        private void OnEnable()
        {
            GenerateRandomVoxels();
        }

        [ContextMenu("Generate Random Voxels")]
        public void GenerateRandomVoxels()
        {
            volume = new VoxelVolume();
            var random = new System.Random(seed);
            float offsetX = (float)random.NextDouble() * 1000f;
            float offsetY = (float)random.NextDouble() * 1000f;
            float offsetZ = (float)random.NextDouble() * 1000f;
            float half = (VoxelVolume.Size - 1) * 0.5f;

            for (int z = 0; z < VoxelVolume.Size; z++)
            {
                for (int y = 0; y < VoxelVolume.Size; y++)
                {
                    for (int x = 0; x < VoxelVolume.Size; x++)
                    {
                        bool boundary = x == 0 || y == 0 || z == 0
                            || x == VoxelVolume.Size - 1
                            || y == VoxelVolume.Size - 1
                            || z == VoxelVolume.Size - 1;
                        if (boundary)
                        {
                            volume[x, y, z] = -1f;
                            continue;
                        }

                        float nx = (x - half) / half;
                        float ny = (y - half) / half;
                        float nz = (z - half) / half;
                        float distance = Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                        float noise = SampleThreeDimensionalNoise(x, y, z, offsetX, offsetY, offsetZ);
                        volume[x, y, z] = bodyRadius - distance + (noise - 0.5f) * noiseStrength;
                    }
                }
            }

            RebuildMesh();
        }

        [ContextMenu("Use New Random Seed")]
        public void UseNewRandomSeed()
        {
            seed = Environment.TickCount;
            GenerateRandomVoxels();
        }

        [ContextMenu("Rebuild Mesh")]
        public void RebuildMesh()
        {
            if (volume == null)
            {
                GenerateRandomVoxels();
                return;
            }

            VoxelMeshData meshData = MarchingCubesMesher.Build(volume, isoLevel, voxelSize);
            ReleaseObject(generatedMesh);
            generatedMesh = meshData.CreateMesh($"Random Voxel Mesh ({seed})");
            generatedMesh.hideFlags = HideFlags.DontSave;

            var meshFilter = GetComponent<MeshFilter>();
            var meshRenderer = GetComponent<MeshRenderer>();
            var meshCollider = GetComponent<MeshCollider>();
            if (meshFilter == null || meshRenderer == null || meshCollider == null)
            {
                Debug.LogError("RandomVoxelDemo requires MeshFilter, MeshRenderer and MeshCollider components.", this);
                return;
            }
            meshFilter.sharedMesh = generatedMesh;
            meshCollider.sharedMesh = null;
            meshCollider.enabled = generateCollider;
            if (generateCollider)
            {
                meshCollider.sharedMesh = generatedMesh;
            }

            EnsureMaterial(meshRenderer);
            vertexCount = generatedMesh.vertexCount;
            triangleCount = meshData.TriangleCount;
        }

        private float SampleThreeDimensionalNoise(
            float x,
            float y,
            float z,
            float offsetX,
            float offsetY,
            float offsetZ)
        {
            x *= noiseScale;
            y *= noiseScale;
            z *= noiseScale;
            float xy = Mathf.PerlinNoise(x + offsetX, y + offsetY);
            float yz = Mathf.PerlinNoise(y + offsetY, z + offsetZ);
            float xz = Mathf.PerlinNoise(x + offsetX, z + offsetZ);
            float yx = Mathf.PerlinNoise(y + offsetY, x + offsetX);
            float zy = Mathf.PerlinNoise(z + offsetZ, y + offsetY);
            float zx = Mathf.PerlinNoise(z + offsetZ, x + offsetX);
            return (xy + yz + xz + yx + zy + zx) / 6f;
        }

        private void EnsureMaterial(MeshRenderer meshRenderer)
        {
            if (generatedMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                if (shader == null)
                {
                    return;
                }

                generatedMaterial = new Material(shader)
                {
                    name = "Random Voxel Demo Material",
                    hideFlags = HideFlags.DontSave,
                };
            }

            if (generatedMaterial.HasProperty("_BaseColor"))
            {
                generatedMaterial.SetColor("_BaseColor", surfaceColor);
            }
            else
            {
                generatedMaterial.color = surfaceColor;
            }

            meshRenderer.sharedMaterial = generatedMaterial;
        }

        private void OnDestroy()
        {
            ReleaseObject(generatedMesh);
            ReleaseObject(generatedMaterial);
        }

        private static void ReleaseObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
