using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    public enum MinecraftCaveGenerationStage
    {
        None,
        Terrain,
        Structures,
        Meshes,
        Ready,
    }

    [DisallowMultipleComponent]
    public sealed class MinecraftCaveInfiniteWorld : MonoBehaviour, IVoxelTerrain
    {
        public const int GenerationRadiusInChunks = 4;
        public const int RequiredChunkCountAtRadius = 257;
        private const int InitialSpawnRadiusInChunks = 1;
        private const float TerrainProgressWeight = 0.72f;
        private const float MinimumGroundClearance = 0.02f;
        private const float MinimumExitHeadroom = 2.1f;
        private const int MinimumExitClearanceRadiusInSamples = 1;
        private const float InitialLoadPresentationFadeSeconds = 0.5f;

        private static readonly ReadOnlyCollection<Vector3Int> RequiredOffsets =
            Array.AsReadOnly(BuildRequiredOffsets());

        [Header("Viewer")]
        [SerializeField] private Transform viewer;
        [SerializeField] private bool placeViewerInCave = true;

        [Header("Density")]
        [SerializeField] private int worldSeed = 18731;
        [SerializeField] private MinecraftCaveSettings settings = new MinecraftCaveSettings();

        [Header("Streaming")]
        [SerializeField, Range(1, 8)] private int maxConcurrentGenerationJobs = 4;
        [SerializeField, Range(1, 8)] private int meshesBuiltPerFrame = 2;

        [Header("Rendering")]
        [SerializeField, Min(0.01f)] private float voxelSize = 0.42f;
        [SerializeField] private float isoLevel;
        [SerializeField] private bool generateColliders;
        [SerializeField] private VoxelTypeCatalog voxelTypeCatalog;

        [Header("Voxel Generation")]
        [SerializeField] private VoxelTypeDefinition baseSolidVoxelType;
        [SerializeField] private List<VoxelOreFeatureDefinition> oreFeatures =
            new List<VoxelOreFeatureDefinition>();

        [Header("Structures")]
        [SerializeField]
        private SpawnPointStructureRule spawnPointStructureRule =
            new SpawnPointStructureRule();
        [SerializeField] private SpawnPointSceneStructure spawnPointSceneStructure;

        private readonly HashSet<Vector3Int> requiredChunks = new HashSet<Vector3Int>();
        private readonly Queue<Vector3Int> generationQueue = new Queue<Vector3Int>();
        private readonly HashSet<Vector3Int> queuedChunks = new HashSet<Vector3Int>();
        private readonly Dictionary<Vector3Int, Task<ChunkGenerationResult>> generationTasks =
            new Dictionary<Vector3Int, Task<ChunkGenerationResult>>();
        private readonly Queue<Vector3Int> meshQueue = new Queue<Vector3Int>();
        private readonly HashSet<Vector3Int> dirtyMeshes = new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> builtMeshes = new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> destructionDirtyMeshes =
            new HashSet<Vector3Int>();
        // High-priority rebuilds from player interaction (mining / placing). Drained
        // in full every frame, before and independent of the streaming mesh budget
        // and generation-stage gate, so edits are visible the same frame even while
        // the background chunk stream is still catching up.
        private readonly Queue<Vector3Int> priorityMeshQueue = new Queue<Vector3Int>();
        private readonly HashSet<Vector3Int> priorityDirtyMeshes =
            new HashSet<Vector3Int>();

        private readonly Dictionary<Vector3Int, GameObject> chunkObjects =
            new Dictionary<Vector3Int, GameObject>();
        private readonly Dictionary<Vector3Int, Mesh> chunkMeshes =
            new Dictionary<Vector3Int, Mesh>();
        private readonly VoxelMiningProgress miningProgress = new VoxelMiningProgress();

        private InfiniteVoxelWorld world;
        private MinecraftCaveDensityField densityField;
        private VoxelTypeId baseSolidType = VoxelTypeId.Default;
        private MinecraftOreFeatureSettings[] oreFeatureSettings =
            Array.Empty<MinecraftOreFeatureSettings>();
        private CancellationTokenSource generationCancellation;
        private Material runtimeMaterial;
        private Vector3Int viewerChunk;
        private bool hasViewerChunk;
        private bool initialSpawnPlacementPending;
        private bool initialLoadComplete;
        private bool globalGravitySuspended;
        private Vector3 gravityBeforeInitialLoad;
        private float initialLoadCompletedAtUnscaledTime;
        private bool structurePassApplied;
        private bool renderingReadyLogged;
        private bool hasViewerInitialTransform;
        private Vector3 viewerInitialPosition;
        private Quaternion viewerInitialRotation;
        private Vector3Int spawnVoxel;
        private Vector3 targetSpawnWorldPosition;
        private Quaternion targetSpawnWorldRotation;
        private CharacterController frozenCharacterController;
        private bool frozenControllerWasEnabled;
        private MinecraftCaveGenerationStage generationStage;
        private GUIStyle headingStyle;
        private GUIStyle statusStyle;

        public InfiniteVoxelWorld World => world;
        public int WorldSeed => worldSeed;
        public float VoxelSize => voxelSize;
        public float IsoLevel => isoLevel;
        public Transform TerrainTransform => transform;
        public int RequiredChunkCount => requiredChunks.Count;
        public int GeneratedChunkCount => world != null ? world.ChunkCount : 0;
        public int InFlightChunkCount => generationTasks.Count;
        public int QueuedChunkCount => generationQueue.Count;
        public int RenderedChunkCount => chunkObjects.Count;
        public Vector3Int ViewerChunk => viewerChunk;
        public Vector3Int SpawnVoxel => spawnVoxel;
        public Vector3 SpawnWorldPosition => targetSpawnWorldPosition;
        public MinecraftCaveGenerationStage GenerationStage => generationStage;
        public bool IsGlobalGravitySuspendedForInitialLoad => globalGravitySuspended;
        public bool IsInitialLoadComplete => initialLoadComplete;
        public float InitialLoadProgress
        {
            get
            {
                if (initialLoadComplete) return 1f;

                int requiredCount = Mathf.Max(1, requiredChunks.Count);
                switch (generationStage)
                {
                    case MinecraftCaveGenerationStage.Terrain:
                        return TerrainProgressWeight * Mathf.Clamp01(
                            (float)CountGeneratedRequiredChunks() / requiredCount);
                    case MinecraftCaveGenerationStage.Structures:
                        return TerrainProgressWeight;
                    case MinecraftCaveGenerationStage.Meshes:
                        return TerrainProgressWeight
                            + (1f - TerrainProgressWeight) * Mathf.Clamp01(
                                (float)CountBuiltRequiredMeshes() / requiredCount);
                    case MinecraftCaveGenerationStage.Ready:
                        return 1f;
                    default:
                        return 0f;
                }
            }
        }
        public VoxelTypeCatalog VoxelTypeCatalog => voxelTypeCatalog;
        public VoxelTypeDefinition BaseSolidVoxelType => baseSolidVoxelType;
        public IReadOnlyList<VoxelOreFeatureDefinition> OreFeatures => oreFeatures;
        public static IReadOnlyList<Vector3Int> StreamingOffsets => RequiredOffsets;

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                InitializeWorld();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || world == null)
            {
                return;
            }

            ResolveViewer();
            if (viewer == null)
            {
                return;
            }

            if (initialSpawnPlacementPending)
            {
                HoldViewerAtSpawn();
            }

            RefreshStreamingForViewerMovement();

            // Player edits jump the queue: fully drained, no stage gate, no budget.
            ProcessPriorityMeshes();

            CommitCompletedGenerationTasks();
            DispatchGenerationTasks();
            AdvanceGenerationPipeline();
            ProcessMeshes(meshesBuiltPerFrame);
            ReportReadyState();
        }

        private void RefreshStreamingForViewerMovement()
        {
            if (initialSpawnPlacementPending)
            {
                return;
            }

            Vector3Int currentViewerChunk = WorldPositionToChunk(viewer.position);
            if (hasViewerChunk && currentViewerChunk == viewerChunk)
            {
                return;
            }

            viewerChunk = currentViewerChunk;
            hasViewerChunk = true;
            RefreshRequiredChunks();
        }

        public void InitializeWorld()
        {
            ClearRuntimeState();
            SuspendGlobalGravityForInitialLoad();
            world = new InfiniteVoxelWorld();
            densityField = new MinecraftCaveDensityField(worldSeed, settings);
            SnapshotVoxelGenerationSettings();
            generationCancellation = new CancellationTokenSource();
            ResolveViewer();

            spawnVoxel = FindCaveSpawnVoxel();
            Vector3 spawnVoxelPosition =
                spawnPointStructureRule.GetPlayerSpawnVoxel(spawnVoxel);
            targetSpawnWorldPosition =
                transform.TransformPoint(spawnVoxelPosition * voxelSize);
            targetSpawnWorldRotation = viewer != null
                ? viewer.rotation
                : transform.rotation;
            if (spawnPointSceneStructure != null)
            {
                spawnPointSceneStructure.ClearExitTarget();
            }
            PlaceSpawnPointSceneStructure();
            generationStage = MinecraftCaveGenerationStage.Terrain;
            structurePassApplied = false;
            initialSpawnPlacementPending = placeViewerInCave && viewer != null;

            if (viewer != null)
            {
                viewerInitialPosition = viewer.position;
                viewerInitialRotation = viewer.rotation;
                hasViewerInitialTransform = true;
                if (initialSpawnPlacementPending)
                {
                    FreezeViewerForInitialGeneration();
                    HoldViewerAtSpawn();
                }

                Vector3 streamingPosition = placeViewerInCave
                    ? targetSpawnWorldPosition
                    : viewer.position;
                viewerChunk = WorldPositionToChunk(streamingPosition);
                hasViewerChunk = true;
                RefreshRequiredChunks(initialSpawnPlacementPending);
            }
        }

        /// <summary>
        /// Removes solid samples inside a noisy sphere and queues each affected mesh once.
        /// Voxel edits are batched; mesh and collider rebuilds remain frame-budgeted by
        /// meshesBuiltPerFrame, avoiding the per-voxel rebuild path.
        /// </summary>
        public int CarveSphere(
            Vector3 worldPosition,
            float worldRadius,
            float randomness,
            int seed)
        {
            if (world == null || worldRadius <= 0f)
            {
                return 0;
            }

            Vector3 centre = transform.InverseTransformPoint(worldPosition) / voxelSize;
            float radius = worldRadius / voxelSize;
            float clampedRandomness = Mathf.Clamp01(randomness);
            float minimumRadius = radius * (1f - clampedRandomness * 0.35f);
            float maximumRadius = radius * (1f + clampedRandomness * 0.35f);
            float minimumRadiusSquared = minimumRadius * minimumRadius;
            float maximumRadiusSquared = maximumRadius * maximumRadius;
            int minX = Mathf.FloorToInt(centre.x - maximumRadius);
            int minY = Mathf.FloorToInt(centre.y - maximumRadius);
            int minZ = Mathf.FloorToInt(centre.z - maximumRadius);
            int maxX = Mathf.CeilToInt(centre.x + maximumRadius);
            int maxY = Mathf.CeilToInt(centre.y + maximumRadius);
            int maxZ = Mathf.CeilToInt(centre.z + maximumRadius);
            destructionDirtyMeshes.Clear();
            int removed = 0;

            for (int z = minZ; z <= maxZ; z++)
            {
                float dz = z - centre.z;
                for (int y = minY; y <= maxY; y++)
                {
                    float dy = y - centre.y;
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dx = x - centre.x;
                        float distanceSquared = dx * dx + dy * dy + dz * dz;
                        if (distanceSquared > maximumRadiusSquared)
                        {
                            continue;
                        }

                        if (distanceSquared > minimumRadiusSquared)
                        {
                            float unitNoise = HashToUnitFloat(x, y, z, seed);
                            float noisyRadius = Mathf.Lerp(
                                minimumRadius,
                                maximumRadius,
                                unitNoise);
                            if (distanceSquared > noisyRadius * noisyRadius)
                            {
                                continue;
                            }
                        }

                        if (!world.TryGetDensity(x, y, z, out float density)
                            || density < isoLevel)
                        {
                            continue;
                        }

                        world.SetDensity(x, y, z, isoLevel - 1f);
                        miningProgress.Reset(new Vector3Int(x, y, z));
                        removed++;
                        Vector3Int chunk = InfiniteVoxelWorld.WorldToChunk(x, y, z);
                        Vector3Int local = InfiniteVoxelWorld.WorldToLocal(x, y, z, chunk);

                        // BuildChunk owns cells from local 0..31 and samples the +1 border.
                        // A sample at local zero is therefore also consumed by the negative neighbour.
                        int minOffsetX = local.x == 0 ? -1 : 0;
                        int minOffsetY = local.y == 0 ? -1 : 0;
                        int minOffsetZ = local.z == 0 ? -1 : 0;
                        for (int oz = minOffsetZ; oz <= 0; oz++)
                        {
                            for (int oy = minOffsetY; oy <= 0; oy++)
                            {
                                for (int ox = minOffsetX; ox <= 0; ox++)
                                {
                                    Vector3Int affected = chunk + new Vector3Int(ox, oy, oz);
                                    if (world.TryGetChunk(affected, out _))
                                    {
                                        destructionDirtyMeshes.Add(affected);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            foreach (Vector3Int coordinate in destructionDirtyMeshes)
            {
                QueueMesh(coordinate);
            }

            return removed;
        }

        public Vector3Int WorldPositionToVoxel(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition) / voxelSize;
            return new Vector3Int(
                Mathf.RoundToInt(local.x),
                Mathf.RoundToInt(local.y),
                Mathf.RoundToInt(local.z));
        }

        public bool TryMineVoxel(Vector3Int coordinate, out VoxelMiningResult result)
        {
            result = default;
            if (world == null
                || !world.TryGetSample(coordinate.x, coordinate.y, coordinate.z, out VoxelSample sample)
                || !sample.IsSolid(isoLevel))
            {
                return false;
            }

            int durability = VoxelTypeUtility.ResolveDurability(
                sample.Type,
                voxelTypeCatalog != null ? voxelTypeCatalog.Definitions : null);
            if (!miningProgress.TryApplyHit(coordinate, sample, durability, out result))
            {
                return false;
            }

            if (result.Destroyed)
            {
                TrySetVoxelAndRebuild(
                    coordinate.x,
                    coordinate.y,
                    coordinate.z,
                    isoLevel - 1f,
                    VoxelTypeId.Air);
            }
            return true;
        }

        public bool TryMineBrush(
            Vector3Int primaryCoordinate,
            Vector3 worldDirection,
            VoxelMiningBrushSettings settings,
            out VoxelMiningBrushResult result)
        {
            result = default;
            if (settings.IsSingleVoxel)
            {
                if (!TryMineVoxel(primaryCoordinate, out VoxelMiningResult singleResult))
                {
                    return false;
                }

                result = new VoxelMiningBrushResult(
                    primaryCoordinate,
                    singleResult.Type,
                    1,
                    1,
                    singleResult.Destroyed ? 1 : 0,
                    singleResult);
                return true;
            }

            if (world == null
                || !world.TryGetSample(
                    primaryCoordinate.x,
                    primaryCoordinate.y,
                    primaryCoordinate.z,
                    out VoxelSample primarySample)
                || !primarySample.IsSolid(isoLevel))
            {
                return false;
            }

            Vector3 localDirection = transform.InverseTransformDirection(
                worldDirection);
            if (localDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }
            localDirection.Normalize();

            float radiusInVoxels = settings.Radius / voxelSize;
            float depthInVoxels = settings.Depth / voxelSize;
            int searchRadius = Mathf.CeilToInt(
                Mathf.Max(radiusInVoxels, depthInVoxels) + 0.25f);
            var candidates = new List<MiningBrushCandidate>();

            for (int z = -searchRadius; z <= searchRadius; z++)
            {
                for (int y = -searchRadius; y <= searchRadius; y++)
                {
                    for (int x = -searchRadius; x <= searchRadius; x++)
                    {
                        Vector3Int coordinate =
                            primaryCoordinate + new Vector3Int(x, y, z);
                        if (!world.TryGetSample(
                                coordinate.x,
                                coordinate.y,
                                coordinate.z,
                                out VoxelSample sample)
                            || !sample.IsSolid(isoLevel)
                            || sample.Type != primarySample.Type)
                        {
                            continue;
                        }

                        Vector3 offset = new Vector3(x, y, z);
                        float axialDistance = Vector3.Dot(
                            offset,
                            localDirection);
                        if (axialDistance < -0.25f
                            || axialDistance > depthInVoxels)
                        {
                            continue;
                        }

                        float forwardDistance = Mathf.Max(0f, axialDistance);
                        Vector3 radialOffset =
                            offset - localDirection * axialDistance;
                        float normalizedSquared =
                            radialOffset.sqrMagnitude
                                / (radiusInVoxels * radiusInVoxels)
                            + forwardDistance * forwardDistance
                                / (depthInVoxels * depthInVoxels);
                        if (normalizedSquared >= 1f
                            && coordinate != primaryCoordinate)
                        {
                            continue;
                        }

                        float normalizedDistance = coordinate == primaryCoordinate
                            ? 0f
                            : Mathf.Sqrt(Mathf.Max(0f, normalizedSquared));
                        float coreWeight = Mathf.Pow(
                            1f - Mathf.Clamp01(normalizedDistance),
                            settings.FalloffExponent);
                        float powerFraction = Mathf.Lerp(
                            settings.MinimumPowerFraction,
                            1f,
                            coreWeight);
                        candidates.Add(
                            new MiningBrushCandidate(
                                coordinate,
                                sample,
                                settings.Power * powerFraction,
                                offset.sqrMagnitude));
                    }
                }
            }

            candidates.Sort((left, right) =>
            {
                int powerOrder = right.Damage.CompareTo(left.Damage);
                return powerOrder != 0
                    ? powerOrder
                    : left.DistanceSquared.CompareTo(right.DistanceSquared);
            });
            int candidateCount = Mathf.Min(
                candidates.Count,
                settings.MaxAffectedSamples);
            if (candidateCount == 0)
            {
                return false;
            }

            int durability = VoxelTypeUtility.ResolveDurability(
                primarySample.Type,
                voxelTypeCatalog != null ? voxelTypeCatalog.Definitions : null);
            destructionDirtyMeshes.Clear();
            int damagedCount = 0;
            int destroyedCount = 0;
            VoxelMiningResult primaryResult = default;
            bool hasPrimaryResult = false;

            for (int i = 0; i < candidateCount; i++)
            {
                MiningBrushCandidate candidate = candidates[i];
                if (!miningProgress.TryApplyDamage(
                        candidate.Coordinate,
                        candidate.Sample,
                        durability,
                        candidate.Damage,
                        false,
                        out VoxelMiningResult damageResult))
                {
                    continue;
                }

                damagedCount++;
                if (candidate.Coordinate == primaryCoordinate)
                {
                    primaryResult = damageResult;
                    hasPrimaryResult = true;
                }
                if (!damageResult.Destroyed)
                {
                    continue;
                }

                world.SetVoxel(
                    candidate.Coordinate.x,
                    candidate.Coordinate.y,
                    candidate.Coordinate.z,
                    isoLevel - 1f,
                    VoxelTypeId.Air);
                miningProgress.Reset(candidate.Coordinate);
                CollectMeshesAffectedByVoxel(
                    candidate.Coordinate,
                    destructionDirtyMeshes);
                destroyedCount++;
            }

            if (destructionDirtyMeshes.Count > 0)
            {
                EnqueuePriorityMeshes(destructionDirtyMeshes);
            }
            result = new VoxelMiningBrushResult(
                primaryCoordinate,
                primarySample.Type,
                candidateCount,
                damagedCount,
                destroyedCount,
                hasPrimaryResult ? primaryResult : default);
            return damagedCount > 0;
        }

        public bool TrySetVoxelAndRebuild(
            int worldX,
            int worldY,
            int worldZ,
            float density,
            VoxelTypeId type)
        {
            if (world == null
                || !world.TryGetSample(worldX, worldY, worldZ, out VoxelSample previous))
            {
                return false;
            }

            VoxelTypeId normalizedType = density >= 0f
                ? (type.IsAir ? VoxelTypeId.Default : type)
                : VoxelTypeId.Air;
            bool occupancyChanged =
                (previous.Density >= isoLevel) != (density >= isoLevel);
            bool typeChanged = previous.Type != normalizedType;
            if (!occupancyChanged && !typeChanged) return false;

            world.SetVoxel(worldX, worldY, worldZ, density, normalizedType);
            var coordinate = new Vector3Int(worldX, worldY, worldZ);
            miningProgress.Reset(coordinate);
            QueueMeshesAffectedByVoxel(coordinate);
            return true;
        }

        private void QueueMeshesAffectedByVoxel(Vector3Int coordinate)
        {
            destructionDirtyMeshes.Clear();
            CollectMeshesAffectedByVoxel(coordinate, destructionDirtyMeshes);
            EnqueuePriorityMeshes(destructionDirtyMeshes);
        }

        private void CollectMeshesAffectedByVoxel(
            Vector3Int coordinate,
            ISet<Vector3Int> affectedMeshes)
        {
            Vector3Int chunk = InfiniteVoxelWorld.WorldToChunk(
                coordinate.x,
                coordinate.y,
                coordinate.z);
            Vector3Int local = InfiniteVoxelWorld.WorldToLocal(
                coordinate.x,
                coordinate.y,
                coordinate.z,
                chunk);
            int minOffsetX = local.x == 0 ? -1 : 0;
            int minOffsetY = local.y == 0 ? -1 : 0;
            int minOffsetZ = local.z == 0 ? -1 : 0;
            for (int z = minOffsetZ; z <= 0; z++)
            {
                for (int y = minOffsetY; y <= 0; y++)
                {
                    for (int x = minOffsetX; x <= 0; x++)
                    {
                        Vector3Int affected = chunk + new Vector3Int(x, y, z);
                        if (world.TryGetChunk(affected, out _))
                        {
                            affectedMeshes.Add(affected);
                        }
                    }
                }
            }
        }

        private static float HashToUnitFloat(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint hash = (uint)seed;
                hash ^= (uint)x * 0x9E3779B9u;
                hash = (hash << 13) | (hash >> 19);
                hash ^= (uint)y * 0x85EBCA6Bu;
                hash = (hash << 11) | (hash >> 21);
                hash ^= (uint)z * 0xC2B2AE35u;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                return (hash & 0x00FFFFFFu) / 16777215f;
            }
        }

        public bool IsWithinGenerationRadius(Vector3Int coordinate)
        {
            return (coordinate - viewerChunk).sqrMagnitude
                <= GenerationRadiusInChunks * GenerationRadiusInChunks;
        }

        private void ResolveViewer()
        {
            if (viewer != null)
            {
                return;
            }

            VoxelPlayerController player = FindObjectOfType<VoxelPlayerController>();
            if (player != null)
            {
                viewer = player.transform;
                return;
            }

            GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
            {
                viewer = taggedPlayer.transform;
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                viewer = mainCamera.transform;
            }
        }

        private void RefreshRequiredChunks(bool initialSpawnAreaOnly = false)
        {
            requiredChunks.Clear();
            generationQueue.Clear();
            queuedChunks.Clear();
            meshQueue.Clear();
            dirtyMeshes.Clear();
            priorityMeshQueue.Clear();
            priorityDirtyMeshes.Clear();
            renderingReadyLogged = false;
            generationStage = MinecraftCaveGenerationStage.Terrain;

            foreach (Vector3Int offset in RequiredOffsets)
            {
                if (initialSpawnAreaOnly
                    && (Mathf.Abs(offset.x) > InitialSpawnRadiusInChunks
                        || Mathf.Abs(offset.y) > InitialSpawnRadiusInChunks
                        || Mathf.Abs(offset.z) > InitialSpawnRadiusInChunks))
                {
                    continue;
                }
                requiredChunks.Add(viewerChunk + offset);
            }

            if (!structurePassApplied)
            {
                spawnPointStructureRule.CollectRequiredChunks(spawnVoxel, requiredChunks);
            }

            foreach (Vector3Int coordinate in requiredChunks)
            {
                if (!world.TryGetChunk(coordinate, out _)
                    && !generationTasks.ContainsKey(coordinate)
                    && queuedChunks.Add(coordinate))
                {
                    generationQueue.Enqueue(coordinate);
                }
            }

            CullMeshesOutsideRequiredSet();
        }

        private void DispatchGenerationTasks()
        {
            while (generationTasks.Count < maxConcurrentGenerationJobs
                && generationQueue.Count > 0)
            {
                Vector3Int coordinate = generationQueue.Dequeue();
                queuedChunks.Remove(coordinate);
                if (!requiredChunks.Contains(coordinate)
                    || world.TryGetChunk(coordinate, out _)
                    || generationTasks.ContainsKey(coordinate))
                {
                    continue;
                }

                CancellationToken token = generationCancellation.Token;
                MinecraftCaveDensityField field = densityField;
                VoxelTypeId solidType = baseSolidType;
                MinecraftOreFeatureSettings[] features = oreFeatureSettings;
                generationTasks.Add(
                    coordinate,
                    Task.Run(
                        () => GenerateChunkData(
                            coordinate,
                            field,
                            solidType,
                            features,
                            token),
                        token));
            }
        }

        private void CommitCompletedGenerationTasks()
        {
            if (generationTasks.Count == 0)
            {
                return;
            }

            var completedCoordinates = new List<Vector3Int>();
            foreach (KeyValuePair<Vector3Int, Task<ChunkGenerationResult>> pair
                in generationTasks)
            {
                if (!pair.Value.IsCompleted)
                {
                    continue;
                }

                completedCoordinates.Add(pair.Key);
                if (pair.Value.IsCanceled)
                {
                    continue;
                }

                if (pair.Value.IsFaulted)
                {
                    Debug.LogException(
                        pair.Value.Exception?.GetBaseException()
                            ?? new InvalidOperationException("Chunk generation task failed."),
                        this);
                    continue;
                }

                ChunkGenerationResult result = pair.Value.Result;
                if (!requiredChunks.Contains(result.Coordinate)
                    || world.TryGetChunk(result.Coordinate, out _))
                {
                    continue;
                }

                CommitChunk(result);
            }

            foreach (Vector3Int coordinate in completedCoordinates)
            {
                generationTasks.Remove(coordinate);
            }
        }

        private void CommitChunk(ChunkGenerationResult result)
        {
            InfiniteVoxelChunk chunk = world.EnsureChunk(result.Coordinate);
            int index = 0;
            for (int z = 0; z < VoxelVolume.Size; z++)
            {
                for (int y = 0; y < VoxelVolume.Size; y++)
                {
                    for (int x = 0; x < VoxelVolume.Size; x++)
                    {
                        chunk.Data.SetSample(
                            x,
                            y,
                            z,
                            result.Densities[index],
                            result.Types[index]);
                        index++;
                    }
                }
            }
        }



        private void QueueMesh(Vector3Int coordinate)
        {
            if (dirtyMeshes.Add(coordinate))
            {
                meshQueue.Enqueue(coordinate);
            }
        }

        // Queues a player-interaction rebuild and drains it right away so the edit is
        // visible this frame regardless of when this runs relative to world.Update.
        // The priority queue is also drained at the top of Update, keeping edits
        // ahead of the streaming backlog and its generation-stage gate.
        private void EnqueuePriorityMesh(Vector3Int coordinate)
        {
            // Drop any pending low-priority entry; the priority pass supersedes it.
            dirtyMeshes.Remove(coordinate);
            if (priorityDirtyMeshes.Add(coordinate))
            {
                priorityMeshQueue.Enqueue(coordinate);
            }

            ProcessPriorityMeshes();
        }

        private void EnqueuePriorityMeshes(IEnumerable<Vector3Int> coordinates)
        {
            foreach (Vector3Int coordinate in coordinates)
            {
                dirtyMeshes.Remove(coordinate);
                if (priorityDirtyMeshes.Add(coordinate))
                {
                    priorityMeshQueue.Enqueue(coordinate);
                }
            }
            ProcessPriorityMeshes();
        }

        private void ProcessPriorityMeshes()
        {
            if (world == null)
            {
                return;
            }

            while (priorityMeshQueue.Count > 0)
            {
                Vector3Int coordinate = priorityMeshQueue.Dequeue();
                priorityDirtyMeshes.Remove(coordinate);
                // No stage gate and no requiredChunks check: the player is editing a
                // chunk that is loaded right here, so rebuild it unconditionally.
                if (world.TryGetChunk(coordinate, out _))
                {
                    RebuildChunk(coordinate);
                }
            }
        }

        private void ProcessMeshes(int budget)
        {
            if (generationStage != MinecraftCaveGenerationStage.Meshes
                && generationStage != MinecraftCaveGenerationStage.Ready)
            {
                return;
            }

            for (int i = 0; i < budget && meshQueue.Count > 0; i++)
            {
                Vector3Int coordinate = meshQueue.Dequeue();
                dirtyMeshes.Remove(coordinate);
                if (!requiredChunks.Contains(coordinate)
                    || !world.TryGetChunk(coordinate, out _))
                {
                    continue;
                }

                RebuildChunk(coordinate);
            }
        }

        private void RebuildChunk(Vector3Int coordinate)
        {
            DestroyChunkObject(coordinate, false);
            VoxelMeshData data = MarchingCubesMesher.BuildChunk(
                world,
                coordinate,
                isoLevel,
                voxelSize);
            builtMeshes.Add(coordinate);
            if (data.Vertices.Count == 0)
            {
                return;
            }

            Mesh mesh = data.CreateMesh(
                $"Minecraft Cave Chunk {coordinate.x},{coordinate.y},{coordinate.z}");
            mesh.hideFlags = HideFlags.DontSave;

            var chunkObject = new GameObject(
                $"CaveChunk_{coordinate.x}_{coordinate.y}_{coordinate.z}");
            chunkObject.hideFlags = HideFlags.DontSave;
            chunkObject.transform.SetParent(transform, false);
            chunkObject.transform.localPosition =
                (Vector3)(coordinate * VoxelVolume.Size) * voxelSize;

            MeshFilter filter = chunkObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = chunkObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = VoxelTypeUtility.ResolveMaterials(
                data,
                EnsureMaterial(),
                voxelTypeCatalog != null ? voxelTypeCatalog.Definitions : null);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (generateColliders)
            {
                MeshCollider collider = chunkObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
            }

            chunkObjects[coordinate] = chunkObject;
            chunkMeshes[coordinate] = mesh;
        }

        private Material EnsureMaterial()
        {
            if (runtimeMaterial != null)
            {
                return runtimeMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                throw new InvalidOperationException("No compatible lit shader is available.");
            }

            runtimeMaterial = new Material(shader)
            {
                name = "Infinite Cave Material",
                hideFlags = HideFlags.DontSave,
            };
            Color stone = new Color(0.46f, 0.49f, 0.50f, 1f);
            if (runtimeMaterial.HasProperty("_BaseColor"))
            {
                runtimeMaterial.SetColor("_BaseColor", stone);
            }
            if (runtimeMaterial.HasProperty("_Color"))
            {
                runtimeMaterial.SetColor("_Color", stone);
            }
            if (runtimeMaterial.HasProperty("_Smoothness"))
            {
                runtimeMaterial.SetFloat("_Smoothness", 0.12f);
            }
            return runtimeMaterial;
        }

        private void CullMeshesOutsideRequiredSet()
        {
            var coordinates = new List<Vector3Int>(chunkObjects.Keys);
            foreach (Vector3Int coordinate in coordinates)
            {
                if (!requiredChunks.Contains(coordinate))
                {
                    DestroyChunkObject(coordinate, true);
                }
            }

            builtMeshes.RemoveWhere(coordinate => !requiredChunks.Contains(coordinate));
        }

        private void DestroyChunkObject(Vector3Int coordinate, bool forgetBuildState)
        {
            if (chunkObjects.TryGetValue(coordinate, out GameObject chunkObject))
            {
                // Destroy is deferred in Play Mode. Disable the old collider immediately
                // so a mine-and-rebuild followed by another raycast in the same frame
                // cannot hit stale geometry in front of the replacement mesh.
                chunkObject.SetActive(false);
                DestroyGeneratedObject(chunkObject);
                chunkObjects.Remove(coordinate);
            }
            if (chunkMeshes.TryGetValue(coordinate, out Mesh mesh))
            {
                DestroyGeneratedObject(mesh);
                chunkMeshes.Remove(coordinate);
            }
            if (forgetBuildState)
            {
                builtMeshes.Remove(coordinate);
            }
        }

        private void ReportReadyState()
        {
            if (!renderingReadyLogged
                && generationStage == MinecraftCaveGenerationStage.Meshes
                && CountBuiltRequiredMeshes() == requiredChunks.Count
                && meshQueue.Count == 0)
            {
                bool initialSpawnAreaReady = initialSpawnPlacementPending;
                int readyChunkCount = requiredChunks.Count;
                renderingReadyLogged = true;
                generationStage = MinecraftCaveGenerationStage.Ready;
                ReleaseViewerAtSpawn();
                initialLoadComplete = true;
                initialLoadCompletedAtUnscaledTime = Time.unscaledTime;
                RestoreGlobalGravityAfterInitialLoad();
                Debug.Log(
                    $"Minecraft infinite cave rendering ready: {readyChunkCount} "
                    + $"chunk meshes evaluated, {chunkObjects.Count} non-empty meshes.",
                    this);

                if (initialSpawnAreaReady)
                {
                    RefreshRequiredChunks();
                }
            }
        }

        private void SuspendGlobalGravityForInitialLoad()
        {
            if (!Application.isPlaying || globalGravitySuspended)
            {
                return;
            }

            gravityBeforeInitialLoad = Physics.gravity;
            Physics.gravity = Vector3.zero;
            globalGravitySuspended = true;
        }

        private void RestoreGlobalGravityAfterInitialLoad()
        {
            if (!globalGravitySuspended)
            {
                return;
            }

            Physics.gravity = gravityBeforeInitialLoad;
            globalGravitySuspended = false;
        }


        private void AdvanceGenerationPipeline()
        {
            if (generationStage != MinecraftCaveGenerationStage.Terrain
                || generationQueue.Count != 0
                || generationTasks.Count != 0
                || CountGeneratedRequiredChunks() != requiredChunks.Count)
            {
                return;
            }

            generationStage = MinecraftCaveGenerationStage.Structures;
            int writtenSamples = 0;
            int clearedSamples = 0;
            if (!structurePassApplied)
            {
                PrepareSpawnPointSceneStructure();
                writtenSamples = spawnPointStructureRule.Apply(world, spawnVoxel);
                if (spawnPointSceneStructure != null)
                {
                    clearedSamples = spawnPointSceneStructure.CarveTerrainClearance(
                        world,
                        transform,
                        voxelSize,
                        isoLevel - 1f);
                }
                structurePassApplied = true;
            }

            generationStage = MinecraftCaveGenerationStage.Meshes;
            builtMeshes.RemoveWhere(coordinate => requiredChunks.Contains(coordinate));
            foreach (Vector3Int coordinate in requiredChunks)
            {
                QueueMesh(coordinate);
            }

            Debug.Log(
                $"Minecraft infinite cave data passes ready: {requiredChunks.Count} terrain chunks, "
                + $"{writtenSamples} structure samples, {clearedSamples} Cell clearance samples. "
                + "Marching Cubes may now begin.",
                this);
        }

        private void PrepareSpawnPointSceneStructure()
        {
            if (spawnPointSceneStructure == null || !initialSpawnPlacementPending)
            {
                return;
            }

            if (TryFindGroundedSpawnPosition(out Vector3 groundedSpawnPosition))
            {
                targetSpawnWorldPosition = groundedSpawnPosition;
            }

            spawnPointSceneStructure.ClearExitTarget();
            if (TryFindCardinalCaveTarget(
                    out CardinalCaveTarget caveTarget,
                    out Vector3 targetWorldPosition))
            {
                Vector3 exitDirection = Vector3.ProjectOnPlane(
                    targetWorldPosition - targetSpawnWorldPosition,
                    transform.up);
                if (exitDirection.sqrMagnitude > 0.0001f)
                {
                    targetSpawnWorldRotation = Quaternion.LookRotation(
                        exitDirection.normalized,
                        transform.up);
                    spawnPointSceneStructure.SetExitTarget(targetWorldPosition);
                    Debug.Log(
                        $"Cell exit connected from chunk "
                        + $"{WorldPositionToChunk(targetSpawnWorldPosition)} to cardinal "
                        + $"chunk {caveTarget.Chunk} at voxel {caveTarget.AirVoxel}; "
                        + $"passage length {Mathf.Sqrt(caveTarget.SquaredDistance) * voxelSize:F1}m.",
                        this);
                }
            }

            PlaceSpawnPointSceneStructure();
            HoldViewerAtSpawn();
        }

        private void FreezeViewerForInitialGeneration()
        {
            frozenCharacterController = viewer.GetComponent<CharacterController>();
            if (frozenCharacterController == null)
            {
                return;
            }

            frozenControllerWasEnabled = frozenCharacterController.enabled;
            frozenCharacterController.enabled = false;
        }

        private void HoldViewerAtSpawn()
        {
            if (viewer == null)
            {
                return;
            }

            if (frozenCharacterController != null)
            {
                frozenCharacterController.enabled = false;
            }
            viewer.position = targetSpawnWorldPosition;
            viewer.rotation = targetSpawnWorldRotation;
        }

        private void ReleaseViewerAtSpawn()
        {
            if (!initialSpawnPlacementPending || viewer == null)
            {
                return;
            }

            if (spawnPointSceneStructure == null
                && TryFindGroundedSpawnPosition(out Vector3 groundedSpawnPosition))
            {
                targetSpawnWorldPosition = groundedSpawnPosition;
            }

            PlaceSpawnPointSceneStructure();
            HoldViewerAtSpawn();
            initialSpawnPlacementPending = false;
            if (frozenCharacterController != null)
            {
                frozenCharacterController.enabled = frozenControllerWasEnabled;
                frozenCharacterController = null;
            }
        }

        private void PlaceSpawnPointSceneStructure()
        {
            if (spawnPointSceneStructure == null)
            {
                return;
            }

            spawnPointSceneStructure.PlaceAt(
                targetSpawnWorldPosition,
                targetSpawnWorldRotation);
        }

        private bool TryFindCardinalCaveTarget(
            out CardinalCaveTarget target,
            out Vector3 targetWorldPosition)
        {
            target = default;
            targetWorldPosition = default;
            Vector3 localSpawn = transform.InverseTransformPoint(
                targetSpawnWorldPosition) / voxelSize;
            var spawnAirVoxel = new Vector3Int(
                Mathf.RoundToInt(localSpawn.x),
                Mathf.RoundToInt(localSpawn.y),
                Mathf.RoundToInt(localSpawn.z));
            int headroomSamples = Mathf.Max(
                2,
                Mathf.CeilToInt(MinimumExitHeadroom / voxelSize));
            int minimumTargetDistanceSamples = Mathf.CeilToInt(
                spawnPointSceneStructure.GetMinimumExitTargetDistance()
                / voxelSize);
            if (!CardinalCaveConnectionSearch.TryFindNearest(
                    world,
                    spawnAirVoxel,
                    isoLevel,
                    headroomSamples,
                    MinimumExitClearanceRadiusInSamples,
                    minimumTargetDistanceSamples,
                    out target)
                || !TryGetCaveFloorWorldPosition(
                    target.AirVoxel,
                    out targetWorldPosition))
            {
                return false;
            }

            return true;
        }

        private bool TryGetCaveFloorWorldPosition(
            Vector3Int airVoxel,
            out Vector3 floorWorldPosition)
        {
            floorWorldPosition = default;
            if (!world.TryGetDensity(
                    airVoxel.x,
                    airVoxel.y,
                    airVoxel.z,
                    out float airDensity)
                || !world.TryGetDensity(
                    airVoxel.x,
                    airVoxel.y - 1,
                    airVoxel.z,
                    out float groundDensity)
                || airDensity >= isoLevel
                || groundDensity < isoLevel)
            {
                return false;
            }

            float denominator = airDensity - groundDensity;
            float surfaceBlend = Mathf.Abs(denominator) > Mathf.Epsilon
                ? Mathf.Clamp01((isoLevel - groundDensity) / denominator)
                : 0.5f;
            float surfaceY = airVoxel.y - 1f + surfaceBlend;
            Vector3 localFloor = new Vector3(
                airVoxel.x,
                surfaceY,
                airVoxel.z) * voxelSize;
            floorWorldPosition = transform.TransformPoint(localFloor)
                + transform.up * GetGroundClearance();
            return true;
        }

        private float GetGroundClearance()
        {
            return frozenCharacterController != null
                ? Mathf.Max(
                    MinimumGroundClearance,
                    frozenCharacterController.skinWidth * 2f)
                : MinimumGroundClearance;
        }

        private bool TryFindGroundedSpawnPosition(out Vector3 groundedPosition)
        {
            groundedPosition = targetSpawnWorldPosition;
            Vector3 up = transform.up;
            float clearance = GetGroundClearance();
            float rayStartOffset = Mathf.Max(voxelSize * 0.25f, clearance);
            float rayDistance = voxelSize * VoxelVolume.Size
                * (InitialSpawnRadiusInChunks * 2 + 1);

            Physics.SyncTransforms();
            RaycastHit[] hits = Physics.RaycastAll(
                targetSpawnWorldPosition + up * rayStartOffset,
                -up,
                rayDistance,
                ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null
                    || !hit.collider.transform.IsChildOf(transform)
                    || Vector3.Dot(hit.normal, up) <= 0.1f)
                {
                    continue;
                }

                groundedPosition = hit.point + up * clearance;
                return true;
            }

            if (world == null)
            {
                return false;
            }

            Vector3 localSpawn = transform.InverseTransformPoint(targetSpawnWorldPosition)
                / voxelSize;
            int sampleX = Mathf.RoundToInt(localSpawn.x);
            int sampleY = Mathf.FloorToInt(localSpawn.y);
            int sampleZ = Mathf.RoundToInt(localSpawn.z);
            int maximumSamples = VoxelVolume.Size
                * (InitialSpawnRadiusInChunks * 2 + 1);
            for (int offset = 0; offset < maximumSamples; offset++)
            {
                int airY = sampleY - offset;
                if (!world.TryGetDensity(sampleX, airY, sampleZ, out float airDensity)
                    || airDensity >= isoLevel
                    || !world.TryGetDensity(sampleX, airY - 1, sampleZ, out float groundDensity)
                    || groundDensity < isoLevel)
                {
                    continue;
                }

                float denominator = airDensity - groundDensity;
                float surfaceBlend = Mathf.Abs(denominator) > Mathf.Epsilon
                    ? Mathf.Clamp01((isoLevel - groundDensity) / denominator)
                    : 0.5f;
                float surfaceY = airY - 1f + surfaceBlend;
                Vector3 localGround = new Vector3(localSpawn.x, surfaceY, localSpawn.z)
                    * voxelSize;
                groundedPosition = transform.TransformPoint(localGround) + up * clearance;
                return true;
            }

            return false;
        }

        private int CountGeneratedRequiredChunks()
        {
            if (world == null)
            {
                return 0;
            }

            int count = 0;
            foreach (Vector3Int coordinate in requiredChunks)
            {
                if (world.TryGetChunk(coordinate, out _))
                {
                    count++;
                }
            }
            return count;
        }

        private int CountBuiltRequiredMeshes()
        {
            int count = 0;
            foreach (Vector3Int coordinate in requiredChunks)
            {
                if (builtMeshes.Contains(coordinate))
                {
                    count++;
                }
            }
            return count;
        }

        private Vector3Int FindCaveSpawnVoxel()
        {
            var random = new System.Random(worldSeed ^ 0x51F15EED);
            Vector3Int best = Vector3Int.zero;
            float bestDensity = float.PositiveInfinity;
            for (int attempt = 0; attempt < 2400; attempt++)
            {
                Vector3Int point = attempt == 0
                    ? Vector3Int.zero
                    : new Vector3Int(
                        random.Next(-72, 73),
                        random.Next(-48, 49),
                        random.Next(-72, 73));
                float density = densityField.SampleFeatureDensity(
                    point,
                    MinecraftCaveType.Combined);
                if (density < bestDensity)
                {
                    bestDensity = density;
                    best = point;
                }
                if (density < -0.035f && HasCaveClearance(point, 2))
                {
                    return point;
                }
            }
            return best;
        }

        private bool HasCaveClearance(Vector3Int centre, int radius)
        {
            Vector3Int[] directions =
            {
                Vector3Int.zero,
                Vector3Int.right,
                Vector3Int.left,
                Vector3Int.up,
                Vector3Int.down,
                new Vector3Int(0, 0, 1),
                new Vector3Int(0, 0, -1),
            };
            foreach (Vector3Int direction in directions)
            {
                float density = densityField.SampleFeatureDensity(
                    centre + direction * radius,
                    MinecraftCaveType.Combined);
                if (density >= 0f)
                {
                    return false;
                }
            }
            return true;
        }

        private Vector3Int WorldPositionToChunk(Vector3 position)
        {
            Vector3 localVoxel = transform.InverseTransformPoint(position) / voxelSize;
            return InfiniteVoxelWorld.WorldToChunk(
                Mathf.FloorToInt(localVoxel.x),
                Mathf.FloorToInt(localVoxel.y),
                Mathf.FloorToInt(localVoxel.z));
        }

        private static ChunkGenerationResult GenerateChunkData(
            Vector3Int coordinate,
            MinecraftCaveDensityField field,
            VoxelTypeId solidType,
            MinecraftOreFeatureSettings[] features,
            CancellationToken token)
        {
            var densities = new float[VoxelVolume.VoxelCount];
            var types = new VoxelTypeId[VoxelVolume.VoxelCount];
            Vector3Int origin = coordinate * VoxelVolume.Size;
            int index = 0;
            for (int z = 0; z < VoxelVolume.Size; z++)
            {
                token.ThrowIfCancellationRequested();
                for (int y = 0; y < VoxelVolume.Size; y++)
                {
                    for (int x = 0; x < VoxelVolume.Size; x++)
                    {
                        Vector3 worldPosition = (Vector3)(
                            origin + new Vector3Int(x, y, z));
                        float density = field.SampleFeatureDensity(
                            worldPosition,
                            MinecraftCaveType.Combined);
                        densities[index] = density;
                        types[index] = density >= 0f
                            ? solidType
                            : VoxelTypeId.Air;
                        index++;
                    }
                }
            }

            MinecraftOreFeatureGenerator.GenerateChunk(
                coordinate,
                densities,
                types,
                field.Seed,
                features,
                (x, y, z) => field.SampleFeatureDensity(
                    new Vector3(x, y, z),
                    MinecraftCaveType.Combined),
                token);
            return new ChunkGenerationResult(coordinate, densities, types);
        }

        private void SnapshotVoxelGenerationSettings()
        {
            baseSolidType = baseSolidVoxelType != null
                ? baseSolidVoxelType.TypeId
                : VoxelTypeId.Default;

            var snapshots = new List<MinecraftOreFeatureSettings>();
            if (oreFeatures != null)
            {
                for (int i = 0; i < oreFeatures.Count; i++)
                {
                    VoxelOreFeatureDefinition feature = oreFeatures[i];
                    if (feature == null)
                    {
                        Debug.LogWarning(
                            $"Ore feature entry {i} is null and will be skipped.",
                            this);
                        continue;
                    }
                    if (feature.TryCreateSettings(
                        out MinecraftOreFeatureSettings snapshot,
                        out string error))
                    {
                        snapshots.Add(snapshot);
                    }
                    else
                    {
                        Debug.LogWarning(error, feature);
                    }
                }
            }
            oreFeatureSettings = snapshots.ToArray();
        }

        private static Vector3Int[] BuildRequiredOffsets()
        {
            int radiusSquared = GenerationRadiusInChunks * GenerationRadiusInChunks;
            var offsets = new List<Vector3Int>();
            for (int z = -GenerationRadiusInChunks; z <= GenerationRadiusInChunks; z++)
            {
                for (int y = -GenerationRadiusInChunks; y <= GenerationRadiusInChunks; y++)
                {
                    for (int x = -GenerationRadiusInChunks; x <= GenerationRadiusInChunks; x++)
                    {
                        var offset = new Vector3Int(x, y, z);
                        if (offset.sqrMagnitude <= radiusSquared)
                        {
                            offsets.Add(offset);
                        }
                    }
                }
            }
            offsets.Sort((left, right) => left.sqrMagnitude.CompareTo(right.sqrMagnitude));
            return offsets.ToArray();
        }

        private void OnGUI()
        {
            if (!Application.isPlaying
                || !initialLoadComplete
                || Time.unscaledTime - initialLoadCompletedAtUnscaledTime
                    < InitialLoadPresentationFadeSeconds)
            {
                return;
            }

            EnsureGuiStyles();
            GUI.Label(new Rect(22f, 17f, 520f, 32f), "INFINITE CAVES", headingStyle);
            GUI.Label(
                new Rect(24f, 48f, 620f, 24f),
                $"CHUNK  {VoxelVolume.Size}^3    RADIUS  {GenerationRadiusInChunks}    "
                + $"POSITION  {viewerChunk.x}, {viewerChunk.y}, {viewerChunk.z}",
                statusStyle);
            GUI.Label(
                new Rect(24f, 70f, 700f, 24f),
                $"GENERATED  {CountGeneratedRequiredChunks()}/{requiredChunks.Count}    "
                + $"QUEUED  {generationQueue.Count}    JOBS  {generationTasks.Count}    "
                + $"MESHES  {chunkObjects.Count}    PASS  {generationStage}",
                statusStyle);
        }

        private void EnsureGuiStyles()
        {
            if (headingStyle != null)
            {
                return;
            }

            headingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.93f, 0.94f, 0.92f) },
            };
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.68f, 0.74f, 0.76f) },
            };
        }

        private void OnDisable()
        {
            ClearRuntimeState();
        }

        private void OnApplicationQuit()
        {
            ClearRuntimeState();
        }

        private void OnDestroy()
        {
            ClearRuntimeState();
        }

        private void ClearRuntimeState()
        {
            RestoreGlobalGravityAfterInitialLoad();
            generationCancellation?.Cancel();
            generationCancellation?.Dispose();
            generationCancellation = null;
            generationTasks.Clear();
            generationQueue.Clear();
            queuedChunks.Clear();
            meshQueue.Clear();
            dirtyMeshes.Clear();
            priorityMeshQueue.Clear();
            priorityDirtyMeshes.Clear();
            builtMeshes.Clear();
            requiredChunks.Clear();
            miningProgress.Clear();

            var coordinates = new List<Vector3Int>(chunkObjects.Keys);
            foreach (Vector3Int coordinate in coordinates)
            {
                DestroyChunkObject(coordinate, true);
            }
            if (runtimeMaterial != null)
            {
                DestroyGeneratedObject(runtimeMaterial);
                runtimeMaterial = null;
            }


            if (frozenCharacterController != null)
            {
                frozenCharacterController.enabled = frozenControllerWasEnabled;
                frozenCharacterController = null;
            }
            if (hasViewerInitialTransform && viewer != null)
            {
                viewer.position = viewerInitialPosition;
                viewer.rotation = viewerInitialRotation;
            }

            world = null;
            densityField = null;
            baseSolidType = VoxelTypeId.Default;
            oreFeatureSettings = Array.Empty<MinecraftOreFeatureSettings>();
            hasViewerChunk = false;

            structurePassApplied = false;
            initialSpawnPlacementPending = false;
            initialLoadComplete = false;
            initialLoadCompletedAtUnscaledTime = 0f;
            generationStage = MinecraftCaveGenerationStage.None;
            hasViewerInitialTransform = false;
        }

        private static void DestroyGeneratedObject(UnityEngine.Object target)
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

        private sealed class ChunkGenerationResult
        {
            public ChunkGenerationResult(
                Vector3Int coordinate,
                float[] densities,
                VoxelTypeId[] types)
            {
                Coordinate = coordinate;
                Densities = densities;
                Types = types;
            }

            public Vector3Int Coordinate { get; }
            public float[] Densities { get; }
            public VoxelTypeId[] Types { get; }
        }

        private readonly struct MiningBrushCandidate
        {
            public MiningBrushCandidate(
                Vector3Int coordinate,
                VoxelSample sample,
                float damage,
                float distanceSquared)
            {
                Coordinate = coordinate;
                Sample = sample;
                Damage = damage;
                DistanceSquared = distanceSquared;
            }

            public Vector3Int Coordinate { get; }
            public VoxelSample Sample { get; }
            public float Damage { get; }
            public float DistanceSquared { get; }
        }
    }
}
