using System;
using System.Collections.Generic;
using Supernova.MinecraftCaves;
using Supernova.Voxels.Integrity;
using Supernova.Missions;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.Voxels
{
    /// <summary>
    /// Builds the smallest finite chunk/section envelope for one voxel structure.
    /// The envelope starts as Stone, then the structure's complete dense field is
    /// applied so authored air carves the surrounding fill.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VoxelStructureStoneTestWorld : MonoBehaviour, IVoxelTerrain
    {
        private const string GeneratedRootName = "Generated Minimal Voxel World";

        [SerializeField] private VoxelStructureAsset structure;
        [SerializeField] private LevelConfiguration runtimeLevelConfiguration;
        [SerializeField] private MinecraftCaveInfiniteWorld sharedRuntime;
        [SerializeField] private VoxelIntegrityWorldBridge integrityRuntime;
        [SerializeField] private bool generateColliders = true;

        private InfiniteVoxelWorld world;
        private Transform generatedRoot;
        private readonly HashSet<Vector3Int> dirtySections =
            new HashSet<Vector3Int>();

        public VoxelStructureAsset Structure => structure;
        public LevelConfiguration RuntimeLevelConfiguration =>
            runtimeLevelConfiguration;
        public MinecraftCaveInfiniteWorld SharedRuntime => sharedRuntime;
        public VoxelIntegrityWorldBridge IntegrityRuntime => integrityRuntime;
        public VoxelTypeDefinition StoneVoxelType => sharedRuntime != null
            ? sharedRuntime.BaseSolidVoxelType
            : runtimeLevelConfiguration != null
                && runtimeLevelConfiguration.WorldGeneration != null
                    ? runtimeLevelConfiguration.WorldGeneration.BaseSolidVoxelType
                    : null;
        public InfiniteVoxelWorld World => world;
        public Transform TerrainTransform => transform;
        public VoxelTypeCatalog VoxelTypeCatalog => sharedRuntime != null
            ? sharedRuntime.VoxelTypeCatalog
            : runtimeLevelConfiguration != null
                && runtimeLevelConfiguration.WorldGeneration != null
                    ? runtimeLevelConfiguration.WorldGeneration.VoxelTypeCatalog
                    : null;
        public IReadOnlyList<MinedOreDrop> ActiveOreDrops =>
            sharedRuntime != null
                ? sharedRuntime.ActiveOreDrops
                : Array.Empty<MinedOreDrop>();
        public float VoxelSize => sharedRuntime != null
            ? sharedRuntime.VoxelSize
            : runtimeLevelConfiguration != null
                && runtimeLevelConfiguration.WorldGeneration != null
                    ? runtimeLevelConfiguration.WorldGeneration.VoxelSize
                    : 1f;
        public float IsoLevel => sharedRuntime != null
            ? sharedRuntime.IsoLevel
            : runtimeLevelConfiguration != null
                && runtimeLevelConfiguration.WorldGeneration != null
                    ? runtimeLevelConfiguration.WorldGeneration.IsoLevel
                    : 0f;
        public Vector3Int MinimumChunkGrid => structure != null
            ? CalculateMinimumChunkGrid(structure.Size)
            : Vector3Int.zero;
        public Vector3Int WorldVoxelSize => GetWorldVoxelSize(MinimumChunkGrid);

        public void Configure(
            VoxelStructureAsset value,
            LevelConfiguration levelConfiguration)
        {
            structure = value;
            runtimeLevelConfiguration = levelConfiguration;
        }

        public static Vector3Int CalculateMinimumChunkGrid(Vector3Int structureSize)
        {
            if (structureSize.x <= 0
                || structureSize.y <= 0
                || structureSize.z <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(structureSize),
                    "Structure dimensions must all be positive.");
            }

            return new Vector3Int(
                DivideRoundUp(structureSize.x, VoxelColumnChunkData.Width),
                DivideRoundUp(
                    structureSize.y,
                    MinecraftCaveInfiniteWorld.MeshSectionHeight),
                DivideRoundUp(structureSize.z, VoxelColumnChunkData.Depth));
        }

        public static Vector3Int GetWorldVoxelSize(Vector3Int chunkGrid)
        {
            return new Vector3Int(
                chunkGrid.x * VoxelColumnChunkData.Width,
                chunkGrid.y * MinecraftCaveInfiniteWorld.MeshSectionHeight,
                chunkGrid.z * VoxelColumnChunkData.Depth);
        }

        public static InfiniteVoxelWorld BuildWorld(
            VoxelStructureAsset value,
            VoxelTypeId stoneType,
            out Vector3Int chunkGrid)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (stoneType.IsAir)
            {
                throw new ArgumentException(
                    "The finite-world fill type must be solid.",
                    nameof(stoneType));
            }

            chunkGrid = CalculateMinimumChunkGrid(value.Size);
            int generatedHeight = chunkGrid.y
                * MinecraftCaveInfiniteWorld.MeshSectionHeight;
            var result = new InfiniteVoxelWorld();

            for (int chunkZ = 0; chunkZ < chunkGrid.z; chunkZ++)
            {
                for (int chunkX = 0; chunkX < chunkGrid.x; chunkX++)
                {
                    float[] densities = new float[VoxelColumnChunkData.VoxelCount];
                    VoxelTypeId[] types =
                        new VoxelTypeId[VoxelColumnChunkData.VoxelCount];
                    Array.Fill(densities, -1f);
                    Array.Fill(types, VoxelTypeId.Air);

                    for (int localZ = 0;
                        localZ < VoxelColumnChunkData.Depth;
                        localZ++)
                    {
                        for (int localY = 0; localY < generatedHeight; localY++)
                        {
                            int rowStart = VoxelColumnChunkData.ToIndex(
                                0,
                                localY,
                                localZ);
                            for (int localX = 0;
                                localX < VoxelColumnChunkData.Width;
                                localX++)
                            {
                                int index = rowStart + localX;
                                densities[index] = 1f;
                                types[index] = stoneType;
                            }
                        }
                    }

                    result.AddChunkTakingOwnership(
                        new Vector3Int(chunkX, 0, chunkZ),
                        densities,
                        types);
                }
            }

            // Align the structure's local origin with world zero. Its dense air
            // samples deliberately replace Stone inside the authored bounds.
            value.Apply(result, value.Anchor, Vector3Int.zero);
            return result;
        }






        [ContextMenu("Rebuild Minimal Stone World")]
        public void Rebuild()
        {
            ClearGeneratedContent();
            world = null;

            MinecraftWorldGenerationConfiguration generation =
                runtimeLevelConfiguration != null
                    ? runtimeLevelConfiguration.WorldGeneration
                    : null;
            if (structure == null
                || generation == null
                || generation.BaseSolidVoxelType == null)
            {
                return;
            }

            world = BuildWorld(
                structure,
                generation.BaseSolidVoxelType.TypeId,
                out Vector3Int chunkGrid);
            if (!EnsureSharedRuntime())
            {
                world = null;
                return;
            }

            generatedRoot = new GameObject(GeneratedRootName).transform;
            generatedRoot.SetParent(transform, false);
            generatedRoot.gameObject.hideFlags = HideFlags.DontSave;

            dirtySections.Clear();
            IReadOnlyList<VoxelTypeDefinition> definitions =
                ResolveMeshDefinitions();
            for (int section = 0; section < chunkGrid.y; section++)
            {
                int startY = section
                    * MinecraftCaveInfiniteWorld.MeshSectionHeight;
                for (int chunkZ = 0; chunkZ < chunkGrid.z; chunkZ++)
                {
                    for (int chunkX = 0; chunkX < chunkGrid.x; chunkX++)
                    {
                        CreateSectionMesh(
                            new Vector3Int(chunkX, section, chunkZ),
                            startY,
                            definitions);
                    }
                }
            }
            sharedRuntime.CompleteAdoptedWorldMeshRebuild();
        }



        public Vector3Int WorldPositionToVoxel(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition)
                / VoxelSize;
            return new Vector3Int(
                Mathf.RoundToInt(local.x),
                Mathf.RoundToInt(local.y),
                Mathf.RoundToInt(local.z));
        }

        public bool TryMineVoxel(
            Vector3Int coordinate,
            out VoxelMiningResult result)
        {
            result = default;
            if (!EnsureSharedRuntime()
                || !integrityRuntime.TryMineVoxel(coordinate, out result))
            {
                return false;
            }

            if (result.Destroyed)
            {
                RebuildAfterSharedMutation();
            }
            return true;
        }

        /// <summary>
        /// Uses the same integrity-aware mining implementation as
        /// DenseJigsawRegion. This adapter only presents the mutated finite data.
        /// </summary>
        public bool TryMineBrush(
            Vector3Int primaryCoordinate,
            Vector3 worldDirection,
            VoxelMiningBrushSettings settings,
            out VoxelMiningBrushResult result)
        {
            result = default;
            if (!EnsureSharedRuntime()
                || !integrityRuntime.TryMineBrush(
                    primaryCoordinate,
                    worldDirection,
                    settings,
                    out result))
            {
                return false;
            }

            if (result.DestroyedCount > 0)
            {
                RebuildAfterSharedMutation();
            }
            return true;
        }


        public bool TryMineExplosion(
            Vector3 worldCenter,
            VoxelExplosionSettings settings,
            out VoxelExplosionResult result)
        {
            result = default;
            if (!EnsureSharedRuntime()
                || !integrityRuntime.TryMineExplosion(
                    worldCenter,
                    settings,
                    out result))
            {
                return false;
            }

            if (result.DestroyedCount > 0)
            {
                RebuildAfterSharedMutation();
            }
            return true;
        }


        public bool TrySetVoxelAndRebuild(
            int worldX,
            int worldY,
            int worldZ,
            float density,
            VoxelTypeId type)
        {
            if (!EnsureSharedRuntime()
                || !integrityRuntime.TrySetVoxelAndRebuild(
                    worldX,
                    worldY,
                    worldZ,
                    density,
                    type))
            {
                return false;
            }

            RebuildAfterSharedMutation();
            return true;
        }


        private bool EnsureSharedRuntime()
        {
            if (sharedRuntime == null)
            {
                sharedRuntime = GetComponent<MinecraftCaveInfiniteWorld>();
                if (sharedRuntime == null)
                {
                    sharedRuntime = gameObject.AddComponent<
                        MinecraftCaveInfiniteWorld>();
                }
            }
            sharedRuntime.enabled = false;

            if (world == null)
            {
                return false;
            }
            if (sharedRuntime.World != world
                && (runtimeLevelConfiguration == null
                    || !sharedRuntime.AdoptGeneratedWorld(
                        runtimeLevelConfiguration,
                        world)))
            {
                return false;
            }

            if (integrityRuntime == null)
            {
                integrityRuntime = GetComponent<VoxelIntegrityWorldBridge>();
                if (integrityRuntime == null)
                {
                    integrityRuntime = gameObject.AddComponent<
                        VoxelIntegrityWorldBridge>();
                }
            }
            integrityRuntime.Configure(sharedRuntime);
            return true;
        }


        private void RebuildAfterSharedMutation()
        {
            dirtySections.Clear();
            sharedRuntime.CollectAdoptedWorldDirtyMeshes(dirtySections);
            RebuildDirtySections();
            sharedRuntime.CompleteAdoptedWorldMeshRebuild();
        }

        private void RebuildDirtySections()
        {
            if (dirtySections.Count == 0 || generatedRoot == null) return;

            IReadOnlyList<VoxelTypeDefinition> definitions =
                ResolveMeshDefinitions();
            foreach (Vector3Int section in dirtySections)
            {
                string name =
                    $"Column_{section.x}_{section.z}_Section_{section.y}";
                Transform existing = generatedRoot.Find(name);
                if (existing != null)
                {
                    MeshFilter filter = existing.GetComponent<MeshFilter>();
                    if (filter != null)
                    {
                        Mesh stale = filter.sharedMesh;
                        filter.sharedMesh = null;
                        DestroyGeneratedObject(stale);
                    }
                    DestroyGeneratedObject(existing.gameObject);
                }

                CreateSectionMesh(
                    section,
                    section.y * MinecraftCaveInfiniteWorld.MeshSectionHeight,
                    definitions);
            }
            dirtySections.Clear();
        }

        private IReadOnlyList<VoxelTypeDefinition> ResolveMeshDefinitions()
        {
            VoxelTypeCatalog catalog = VoxelTypeCatalog;
            return catalog != null && catalog.Definitions != null
                ? catalog.Definitions
                : StoneVoxelType != null
                    ? new[] { StoneVoxelType }
                    : Array.Empty<VoxelTypeDefinition>();
        }

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnDisable()
        {
            ClearGeneratedContent();
            world = null;
        }

        private void CreateSectionMesh(
            Vector3Int coordinate,
            int startY,
            IReadOnlyList<VoxelTypeDefinition> definitions)
        {
            VoxelTypeDefinition stone = StoneVoxelType;
            if (world == null || stone == null)
            {
                return;
            }

            VoxelMeshData data = MarchingCubesMesher.BuildColumnSection(
                world,
                new Vector3Int(coordinate.x, 0, coordinate.z),
                startY,
                MinecraftCaveInfiniteWorld.MeshSectionHeight,
                IsoLevel,
                VoxelSize,
                sharedRuntime.VertexPlacement,
                stone.TypeId,
                stone.TypeId);
            if (data.Vertices.Count == 0)
            {
                return;
            }

            var sectionObject = new GameObject(
                $"Column_{coordinate.x}_{coordinate.z}_Section_{coordinate.y}");
            sectionObject.transform.SetParent(generatedRoot, false);
            sectionObject.transform.localPosition = new Vector3(
                coordinate.x * VoxelColumnChunkData.Width,
                startY,
                coordinate.z * VoxelColumnChunkData.Depth) * VoxelSize;

            Mesh mesh = data.CreateMesh(
                $"SpawnShelter Test {coordinate.x},{coordinate.z} "
                + $"Section {coordinate.y}");
            mesh.hideFlags = HideFlags.DontSave;

            MeshFilter filter = sectionObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = sectionObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = VoxelTypeUtility.ResolveMaterials(
                data,
                stone.Material,
                definitions);
            renderer.shadowCastingMode = ShadowCastingMode.On;

            if (generateColliders)
            {
                MeshCollider collider = sectionObject.AddComponent<MeshCollider>();
                collider.sharedMaterial = sharedRuntime.TerrainPhysicsMaterial;
                collider.sharedMesh = mesh;
            }
        }

        private void ClearGeneratedContent()
        {
            Transform root = generatedRoot;
            if (root == null)
            {
                root = transform.Find(GeneratedRootName);
            }
            generatedRoot = null;
            if (root == null)
            {
                return;
            }

            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                filters[i].sharedMesh = null;
                DestroyGeneratedObject(mesh);
            }
            DestroyGeneratedObject(root.gameObject);
        }

        private static void DestroyGeneratedObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }

        private static int DivideRoundUp(int value, int divisor)
        {
            return (value + divisor - 1) / divisor;
        }

    }
}
