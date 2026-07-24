using System.Collections.Generic;
using System;
using UnityEngine;

namespace Supernova.Voxels
{
    [ExecuteAlways]
    public sealed class RandomChunkRegionDemo : MonoBehaviour
    {
        [Header("Perlin SDF carving")]
        [SerializeField] private int seed = 20260721;
        [SerializeField, Min(0.001f)] private float sdfNoiseScale = 0.105f;
        [SerializeField, Range(0f, 2f)] private float sdfNoiseStrength = 0.9f;
        [SerializeField, Range(0.1f, 1.5f)] private float sdfBodyRadius = 0.82f;
        [SerializeField] private bool generateOnEnable = true;

        [Header("Scene view")]
        [SerializeField] private bool drawChunkBounds = true;
        [SerializeField, Min(0.01f)] private float gizmoVoxelSize = 0.25f;

        [Header("Generated statistics")]
        [SerializeField, HideInInspector] private int chunkCount;
        [SerializeField, HideInInspector] private int voxelCount;
        [SerializeField, HideInInspector] private int emptyVoxelCount;
        [SerializeField, HideInInspector] private int solidVoxelCount;
        [SerializeField, HideInInspector] private float densityMemoryMiB;

        [Header("Chunk mesh generation")]
        [SerializeField] private bool generateMeshes = true;
        [SerializeField] private bool generateColliders;
        [SerializeField, Min(0.01f)] private float meshVoxelSize = 0.25f;
        [SerializeField] private float isoLevel;
        [SerializeField] private Material chunkMaterial;

        [Header("Generated mesh statistics")]
        [SerializeField, HideInInspector] private long generatedVertexCount;
        [SerializeField, HideInInspector] private long generatedTriangleCount;

        private readonly List<Mesh> generatedMeshes = new List<Mesh>();
        private Material runtimeMaterial;

        private VoxelChunkRegion region;

        public VoxelChunkRegion Region => region;
        public int ChunkCount => chunkCount;
        public int VoxelCount => voxelCount;
        public float MeshVoxelSize => meshVoxelSize;
        public int EmptyVoxelCount => emptyVoxelCount;
        public int SolidVoxelCount => solidVoxelCount;

        private void OnEnable()
        {
            if (generateOnEnable)
            {
                GenerateRandomRegion();
            }
        }

public void GenerateRandomRegion()
        {
            region = new VoxelChunkRegion(1f);
            region.CarveWithPerlinSdf(
                seed,
                sdfNoiseScale,
                sdfNoiseStrength,
                sdfBodyRadius);
            chunkCount = region.Count;
            voxelCount = VoxelChunkRegion.TotalVoxelCount;
            solidVoxelCount = region.SolidVoxelCount;
            emptyVoxelCount = region.EmptyVoxelCount;
            densityMemoryMiB = VoxelChunkRegion.DensityMemoryBytes / (1024f * 1024f);
            RebuildChunkMeshes();
        }

public void RebuildChunkMeshes()
        {
            ClearGeneratedMeshes();
            generatedVertexCount = 0;
            generatedTriangleCount = 0;

            if (!generateMeshes || region == null)
            {
                return;
            }

            for (int chunkZ = 0; chunkZ < VoxelChunkRegion.ChunkCountZ; chunkZ++)
            {
                for (int chunkX = 0; chunkX < VoxelChunkRegion.ChunkCountX; chunkX++)
                {
                    CreateChunkObject(chunkX, chunkZ);
                }
            }
        }

        public void RebuildChunk(int chunkX, int chunkZ)
        {
            if (!generateMeshes || region == null || !region.IsChunkInBounds(chunkX, chunkZ))
            {
                return;
            }

            Transform existing = transform.Find($"MC_Chunk_{chunkX}_{chunkZ}");
            if (existing != null)
            {
                MeshFilter filter = existing.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    generatedVertexCount -= filter.sharedMesh.vertexCount;
                    generatedTriangleCount -= (long)filter.sharedMesh.GetIndexCount(0) / 3L;
                }

                DestroyGeneratedChunk(existing.gameObject);
            }

            CreateChunkObject(chunkX, chunkZ);
        }

        public void RebuildChunksAroundVoxel(int worldX, int worldZ)
        {
            if (region == null || !region.IsWorldVoxelInBounds(worldX, 0, worldZ))
            {
                return;
            }

            int chunkX = worldX / VoxelVolume.Size;
            int chunkZ = worldZ / VoxelVolume.Size;
            int localX = worldX % VoxelVolume.Size;
            int localZ = worldZ % VoxelVolume.Size;

            for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
            {
                bool includeZ = offsetZ == 0
                    || (offsetZ == -1 && localZ == 0)
                    || (offsetZ == 1 && localZ == VoxelVolume.Size - 1);
                if (!includeZ)
                {
                    continue;
                }

                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    bool includeX = offsetX == 0
                        || (offsetX == -1 && localX == 0)
                        || (offsetX == 1 && localX == VoxelVolume.Size - 1);
                    if (!includeX)
                    {
                        continue;
                    }

                    RebuildChunk(chunkX + offsetX, chunkZ + offsetZ);
                }
            }
        }

public bool TrySetVoxelAndRebuild(int worldX, int worldY, int worldZ, float density)
        {
            if (region == null || !region.IsWorldVoxelInBounds(worldX, worldY, worldZ))
            {
                return false;
            }

            float previous = region.GetWorldVoxel(worldX, worldY, worldZ);
            if ((previous >= 0f) == (density >= 0f))
            {
                return false;
            }

            region.SetWorldVoxel(worldX, worldY, worldZ, density);
            solidVoxelCount = region.SolidVoxelCount;
            emptyVoxelCount = region.EmptyVoxelCount;
            RebuildChunksAroundVoxel(worldX, worldZ);
            return true;
        }


        private void CreateChunkObject(int chunkX, int chunkZ)
        {
            VoxelMeshData data = MarchingCubesMesher.BuildChunk(
                region,
                chunkX,
                chunkZ,
                isoLevel,
                meshVoxelSize);
            Mesh mesh = data.CreateMesh($"Perlin SDF Chunk Mesh {chunkX},{chunkZ}");
            mesh.hideFlags = HideFlags.DontSave;
            generatedMeshes.Add(mesh);

            GameObject chunkObject = new GameObject($"MC_Chunk_{chunkX}_{chunkZ}");
            chunkObject.hideFlags = HideFlags.DontSave;
            chunkObject.transform.SetParent(transform, false);
            chunkObject.transform.localPosition = new Vector3(
                chunkX * VoxelVolume.Size * meshVoxelSize,
                0f,
                chunkZ * VoxelVolume.Size * meshVoxelSize);

            MeshFilter filter = chunkObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = chunkObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = EnsureMaterial();

            if (generateColliders)
            {
                MeshCollider collider = chunkObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
            }

            generatedVertexCount += mesh.vertexCount;
            generatedTriangleCount += data.TriangleCount;
        }

        private void DestroyGeneratedChunk(GameObject chunkObject)
        {
            MeshFilter filter = chunkObject.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null)
            {
                generatedMeshes.Remove(mesh);
            }

            if (Application.isPlaying)
            {
                Destroy(chunkObject);
                if (mesh != null)
                {
                    Destroy(mesh);
                }
            }
            else
            {
                DestroyImmediate(chunkObject);
                if (mesh != null)
                {
                    DestroyImmediate(mesh);
                }
            }
        }

        private Material EnsureMaterial()
        {
            if (chunkMaterial != null)
            {
                return chunkMaterial;
            }

            if (runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                if (shader != null)
                {
                    runtimeMaterial = new Material(shader)
                    {
                        name = "Random Chunk Region Material",
                        hideFlags = HideFlags.DontSave,
                    };
                    if (runtimeMaterial.HasProperty("_BaseColor"))
                    {
                        runtimeMaterial.SetColor("_BaseColor", new Color(0.35f, 0.55f, 0.85f, 1f));
                    }
                }
            }

            return runtimeMaterial;
        }

        private void OnDisable()
        {
            ClearGeneratedMeshes();
        }

        private void OnDestroy()
        {
            ClearGeneratedMeshes();
            if (runtimeMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(runtimeMaterial);
                }
                else
                {
                    DestroyImmediate(runtimeMaterial);
                }

                runtimeMaterial = null;
            }
        }

        private void ClearGeneratedMeshes()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name.StartsWith("MC_Chunk_", StringComparison.Ordinal))
                {
                    if (Application.isPlaying)
                    {
                        Destroy(child.gameObject);
                    }
                    else
                    {
                        DestroyImmediate(child.gameObject);
                    }
                }
            }

            for (int i = 0; i < generatedMeshes.Count; i++)
            {
                Mesh mesh = generatedMeshes[i];
                if (mesh == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(mesh);
                }
                else
                {
                    DestroyImmediate(mesh);
                }
            }

            generatedMeshes.Clear();
        }

        [ContextMenu("Use New Random Seed")]
        public void UseNewRandomSeed()
        {
            seed = Environment.TickCount;
            GenerateRandomRegion();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawChunkBounds)
            {
                return;
            }

            float chunkSize = VoxelVolume.Size * gizmoVoxelSize;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;

            for (int chunkZ = 0; chunkZ < VoxelChunkRegion.ChunkCountZ; chunkZ++)
            {
                for (int chunkX = 0; chunkX < VoxelChunkRegion.ChunkCountX; chunkX++)
                {
                    Gizmos.color = (chunkX + chunkZ) % 2 == 0
                        ? new Color(0.1f, 0.75f, 0.95f, 0.9f)
                        : new Color(0.8f, 0.85f, 0.9f, 0.75f);
                    Vector3 centre = new Vector3(
                        (chunkX + 0.5f) * chunkSize,
                        chunkSize * 0.5f,
                        (chunkZ + 0.5f) * chunkSize);
                    Gizmos.DrawWireCube(centre, Vector3.one * chunkSize);
                }
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
