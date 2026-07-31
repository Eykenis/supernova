using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Supernova.Gameplay;
using Supernova.MinecraftCaves.Creatures;
using Supernova.Missions;
using Supernova.UI;
using Supernova.Voxels;
using Unity.Profiling;
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

    public enum MinecraftWorldGenerationMode
    {
        InfiniteCaves,
        Superflat,
    }

    [DisallowMultipleComponent]
    public sealed class MinecraftCaveInfiniteWorld : MonoBehaviour, IVoxelTerrain
    {
        public const int GenerationRadiusInChunks = 4;
        public const int RequiredChunkCountAtRadius = 49;
        public const int MeshSectionHeight = 32;
        public const int MeshSectionsPerColumn =
            VoxelColumnChunkData.Height / MeshSectionHeight;
        public const int MinimumSpawnDepthBelowTop = 32;
        public const int MaximumSpawnDepthBelowTop = 160;
        public const int HighestSpawnY =
            VoxelColumnChunkData.Height - 1 - MinimumSpawnDepthBelowTop;
        public const int LowestSpawnY =
            VoxelColumnChunkData.Height - 1 - MaximumSpawnDepthBelowTop;
        private const int InitialSpawnRadiusInChunks = 1;
        private const int MonsterSpawnSeedSalt = unchecked((int)0xA511E9B3);
        private const int MonsterSpawnRoundSeedStep = unchecked((int)0x9E3779B9);
        private const float BoundaryBedrockDensity = 1f;
        private const float TerrainProgressWeight = 0.72f;
        private const float MinimumGroundClearance = 0.02f;
        private const float MinimumExitHeadroom = 2.1f;
        private const int MinimumExitClearanceRadiusInSamples = 1;
        private const float InitialLoadPresentationFadeSeconds = 0.5f;
        private const int ChunkObjectsDestroyedPerFrame = 2;
        private const int MeshSnapshotsCapturedPerFrame = 1;
        private const string SoftFalloffLitShaderName =
            "Supernova/Lighting/Soft Falloff Lit";

        private static readonly int SoftFalloffParametersId =
            Shader.PropertyToID("_SupernovaSoftFalloffParams");
        private static readonly ProfilerMarker UpdateViewerMarker =
            new ProfilerMarker("MinecraftWorld.Update.Viewer");
        private static readonly ProfilerMarker UpdateStreamingMarker =
            new ProfilerMarker("MinecraftWorld.Update.Streaming");
        private static readonly ProfilerMarker UpdatePriorityMeshesMarker =
            new ProfilerMarker("MinecraftWorld.Update.PriorityMeshes");
        private static readonly ProfilerMarker UpdateGenerationCommitMarker =
            new ProfilerMarker("MinecraftWorld.Update.GenerationCommit");
        private static readonly ProfilerMarker UpdateGenerationDispatchMarker =
            new ProfilerMarker("MinecraftWorld.Update.GenerationDispatch");
        private static readonly ProfilerMarker UpdatePipelineMarker =
            new ProfilerMarker("MinecraftWorld.Update.Pipeline");
        private static readonly ProfilerMarker UpdateMeshCommitMarker =
            new ProfilerMarker("MinecraftWorld.Update.MeshCommit");
        private static readonly ProfilerMarker UpdateMeshSnapshotMarker =
            new ProfilerMarker("MinecraftWorld.Update.MeshSnapshot");
        private static readonly ProfilerMarker UpdateDestructionMarker =
            new ProfilerMarker("MinecraftWorld.Update.Destruction");
        private static readonly ProfilerMarker UpdateReadyMarker =
            new ProfilerMarker("MinecraftWorld.Update.Ready");

        private static readonly ReadOnlyCollection<Vector3Int> RequiredOffsets =
            Array.AsReadOnly(BuildRequiredOffsets());

        [Header("Viewer")]
        [SerializeField] private Transform viewer;

        [Header("Level")]
        [Tooltip(
            "Optional level used by isolated test/demo scenes. Product scenes "
            + "use the level selected by MissionGameLoop.")]
        [SerializeField] private LevelConfiguration levelConfigurationOverride;

        [Header("Structures")]
        [SerializeField] private SpawnPointSceneStructure spawnPointSceneStructure;

        private LevelConfiguration levelConfiguration;
        private MinecraftWorldGenerationConfiguration worldGenerationConfiguration;
        private bool placeViewerInCave = true;
        private int maxConcurrentGenerationJobs = 4;
        private int meshesBuiltPerFrame = 1;
        private DepthProbabilityProfile oreDepthProbability =
            new DepthProbabilityProfile();
        private DepthProbabilityProfile treasureDepthProbability =
            new DepthProbabilityProfile();
        private DepthProbabilityProfile monsterDepthProbability =
            new DepthProbabilityProfile();
        private MinecraftWorldGenerationMode generationMode;
        private int superflatStoneHeight = 10;
        private int worldSeed = 18731;
        private MinecraftCaveSettings settings = new MinecraftCaveSettings();
        private float voxelSize = 0.42f;
        private float isoLevel;
        private MarchingCubesVertexPlacement vertexPlacement =
            MarchingCubesVertexPlacement.DensityInterpolated;
        private bool generateColliders;
        private PhysicMaterial terrainPhysicsMaterial;
        private VoxelTypeCatalog voxelTypeCatalog;
        private float punctualLightFalloffPower = 0.55f;
        private float punctualLightAttenuationLimit = 1.5f;
        private float punctualLightMultiplier = 1f;
        private VoxelTypeDefinition baseSolidVoxelType;
        private VoxelTypeDefinition bedrockVoxelType;
        private List<VoxelOreFeatureDefinition> oreFeatures =
            new List<VoxelOreFeatureDefinition>();
        private TreasureSpawnTable treasureSpawnTable;
        private MonsterSpawnTable monsterSpawnTable;
        private SpawnPointStructureRule spawnPointStructureRule =
            new SpawnPointStructureRule();

        private readonly HashSet<Vector3Int> requiredChunks = new HashSet<Vector3Int>();
        private readonly Queue<Vector3Int> generationQueue = new Queue<Vector3Int>();
        private readonly HashSet<Vector3Int> queuedChunks = new HashSet<Vector3Int>();
        private readonly Dictionary<Vector3Int, GenerationTaskHandle> generationTasks =
            new Dictionary<Vector3Int, GenerationTaskHandle>();
        private readonly List<Vector3Int> completedGenerationCoordinates =
            new List<Vector3Int>();
        private readonly Queue<Vector3Int> meshQueue = new Queue<Vector3Int>();
        private readonly HashSet<Vector3Int> dirtyMeshes = new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> builtMeshes = new HashSet<Vector3Int>();
        private readonly Dictionary<Vector3Int, Task<MeshGenerationResult>> meshTasks =
            new Dictionary<Vector3Int, Task<MeshGenerationResult>>();
        private readonly Dictionary<Vector3Int, int> meshBuildVersions =
            new Dictionary<Vector3Int, int>();
        private readonly List<Vector3Int> completedMeshCoordinates =
            new List<Vector3Int>();
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
        private readonly Queue<Vector3Int> chunkDestructionQueue =
            new Queue<Vector3Int>();
        private readonly HashSet<Vector3Int> queuedChunkDestructions =
            new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> departingColumns =
            new HashSet<Vector3Int>();
        private readonly VoxelMiningProgress miningProgress = new VoxelMiningProgress();
        private readonly List<MinedOreDrop> activeOreDrops =
            new List<MinedOreDrop>();
        private readonly List<TreasurePickup> activeTreasures =
            new List<TreasurePickup>();
        private readonly HashSet<Vector3Int> treasureSpawnedColumns =
            new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> pendingTreasureColumns =
            new HashSet<Vector3Int>();
        private readonly List<CreatureBehaviorAgent> activeMonsters =
            new List<CreatureBehaviorAgent>();
        private readonly Dictionary<Vector3Int, int> monsterSpawnAttemptRounds =
            new Dictionary<Vector3Int, int>();
        private readonly Queue<PendingMonsterGroupSpawn> pendingMonsterSpawnGroups =
            new Queue<PendingMonsterGroupSpawn>();
        private PendingMonsterGroupSpawn activePendingMonsterSpawnGroup;
        private int pendingMonsterSpawnCount;
        private float nextMonsterGroupSpawnTime;
        private readonly HashSet<Vector3Int> pendingMonsterColumns =
            new HashSet<Vector3Int>();
        private readonly Dictionary<Vector3Int, List<SuspendedBodyState>>
            suspendedBodiesByColumn =
                new Dictionary<Vector3Int, List<SuspendedBodyState>>();

        private InfiniteVoxelWorld world;
        private MinecraftCaveDensityField densityField;
        private VoxelTypeId baseSolidType = VoxelTypeId.Default;
        private VoxelTypeId bedrockType = VoxelTypeId.Default;
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

        public InfiniteVoxelWorld World => world;
        public int WorldSeed => worldSeed;
        public MinecraftWorldGenerationMode GenerationMode => generationMode;
        public int SuperflatStoneHeight => Mathf.Clamp(
            superflatStoneHeight,
            1,
            VoxelColumnChunkData.Height - 1);
        public float VoxelSize => voxelSize;
        public float IsoLevel => isoLevel;
        public MarchingCubesVertexPlacement VertexPlacement => vertexPlacement;
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
        public VoxelTypeDefinition BedrockVoxelType => bedrockVoxelType;
        public PhysicMaterial TerrainPhysicsMaterial => terrainPhysicsMaterial;
        public IReadOnlyList<VoxelOreFeatureDefinition> OreFeatures => oreFeatures;
        public LevelConfiguration LevelConfiguration => levelConfiguration;
        public MinecraftWorldGenerationConfiguration WorldGenerationConfiguration =>
            worldGenerationConfiguration;
        public IReadOnlyList<MinedOreDrop> ActiveOreDrops => activeOreDrops;
        public IReadOnlyList<TreasurePickup> ActiveTreasures => activeTreasures;
        public IReadOnlyList<CreatureBehaviorAgent> ActiveMonsters => activeMonsters;
        public static IReadOnlyList<Vector3Int> StreamingOffsets => RequiredOffsets;

        public bool ApplyLevelConfiguration(LevelConfiguration value)
        {
            if (value == null || value.WorldGeneration == null)
            {
                return false;
            }
            if (world != null)
            {
                Debug.LogError(
                    "A level configuration cannot be changed after world "
                    + "initialization has started.",
                    this);
                return false;
            }

            levelConfiguration = value;
            worldGenerationConfiguration = value.WorldGeneration;
            treasureSpawnTable = value.TreasureGeneration;
            monsterSpawnTable = value.MonsterGeneration;

            generationMode = worldGenerationConfiguration.GenerationMode;
            superflatStoneHeight =
                worldGenerationConfiguration.SuperflatStoneHeight;
            worldSeed = worldGenerationConfiguration.WorldSeed;
            settings = worldGenerationConfiguration.Settings;
            placeViewerInCave =
                worldGenerationConfiguration.PlaceViewerInCave;
            maxConcurrentGenerationJobs =
                worldGenerationConfiguration.MaxConcurrentGenerationJobs;
            meshesBuiltPerFrame =
                worldGenerationConfiguration.MeshesBuiltPerFrame;
            oreDepthProbability =
                worldGenerationConfiguration.OreDepthProbability
                ?? new DepthProbabilityProfile();
            treasureDepthProbability =
                worldGenerationConfiguration.TreasureDepthProbability
                ?? new DepthProbabilityProfile();
            monsterDepthProbability =
                worldGenerationConfiguration.MonsterDepthProbability
                ?? new DepthProbabilityProfile();
            voxelSize = worldGenerationConfiguration.VoxelSize;
            isoLevel = worldGenerationConfiguration.IsoLevel;
            vertexPlacement = worldGenerationConfiguration.VertexPlacement;
            generateColliders =
                worldGenerationConfiguration.GenerateColliders;
            terrainPhysicsMaterial =
                worldGenerationConfiguration.TerrainPhysicsMaterial;
            voxelTypeCatalog =
                worldGenerationConfiguration.VoxelTypeCatalog;
            punctualLightFalloffPower =
                worldGenerationConfiguration.PunctualLightFalloffPower;
            punctualLightAttenuationLimit =
                worldGenerationConfiguration.PunctualLightAttenuationLimit;
            punctualLightMultiplier =
                worldGenerationConfiguration.PunctualLightMultiplier;
            baseSolidVoxelType =
                worldGenerationConfiguration.BaseSolidVoxelType;
            bedrockVoxelType =
                worldGenerationConfiguration.BedrockVoxelType;
            oreFeatures = new List<VoxelOreFeatureDefinition>(
                worldGenerationConfiguration.OreFeatures);
            spawnPointStructureRule =
                worldGenerationConfiguration.SpawnPointStructureRule
                ?? new SpawnPointStructureRule();
            return true;
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                LevelConfiguration activeLevel =
                    levelConfigurationOverride != null
                        ? levelConfigurationOverride
                        : MissionGameLoop.CurrentLevelConfiguration;
                if (!ApplyLevelConfiguration(
                    activeLevel))
                {
                    Debug.LogError(
                        "MinecraftCaveInfiniteWorld requires the active "
                        + "LevelConfiguration before it can initialize.",
                        this);
                    enabled = false;
                    return;
                }
                ApplyPunctualLightFalloffParameters();
                InitializeWorld();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || world == null)
            {
                return;
            }

            using (UpdateViewerMarker.Auto())
            {
                ResolveViewer();
                if (viewer == null)
                {
                    return;
                }

                if (initialSpawnPlacementPending)
                {
                    HoldViewerAtSpawn();
                }
            }

            using (UpdateStreamingMarker.Auto())
            {
                RefreshStreamingForViewerMovement();
            }

            // Player edits jump the queue: fully drained, no stage gate, no budget.
            using (UpdatePriorityMeshesMarker.Auto())
            {
                ProcessPriorityMeshes();
            }

            using (UpdateGenerationCommitMarker.Auto())
            {
                CommitCompletedGenerationTasks();
            }
            using (UpdateGenerationDispatchMarker.Auto())
            {
                DispatchGenerationTasks();
            }
            using (UpdatePipelineMarker.Auto())
            {
                AdvanceGenerationPipeline();
            }
            ProcessMeshes(meshesBuiltPerFrame);
            ProcessPendingMonsterSpawns();
            using (UpdateDestructionMarker.Auto())
            {
                ProcessChunkDestructions(ChunkObjectsDestroyedPerFrame);
            }
            using (UpdateReadyMarker.Auto())
            {
                ReportReadyState();
            }
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

            bool isSuperflat =
                generationMode == MinecraftWorldGenerationMode.Superflat;
            spawnVoxel = isSuperflat
                ? new Vector3Int(0, SuperflatStoneHeight, 0)
                : FindCaveSpawnVoxel();
            Vector3 spawnVoxelPosition = isSuperflat
                ? (Vector3)spawnVoxel
                : spawnPointStructureRule.GetPlayerSpawnVoxel(spawnVoxel);
            targetSpawnWorldPosition =
                transform.TransformPoint(spawnVoxelPosition * voxelSize);
            targetSpawnWorldRotation = viewer != null
                ? viewer.rotation
                : transform.rotation;
            if (spawnPointSceneStructure != null)
            {
                spawnPointSceneStructure.ClearExitTarget();
            }
            if (!isSuperflat)
            {
                PlaceSpawnPointSceneStructure();
            }
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
                if (IsOreType(result.Type))
                {
                    destructionDirtyMeshes.Clear();
                    if (HarvestConnectedOreVein(
                            coordinate,
                            result.Type,
                            destructionDirtyMeshes) > 0)
                    {
                        EnqueuePriorityMeshes(destructionDirtyMeshes);
                    }
                    else
                    {
                        TrySetVoxelAndRebuild(
                            coordinate.x,
                            coordinate.y,
                            coordinate.z,
                            isoLevel - 1f,
                            VoxelTypeId.Air);
                    }
                }
                else
                {
                    TrySetVoxelAndRebuild(
                        coordinate.x,
                        coordinate.y,
                        coordinate.z,
                        isoLevel - 1f,
                        VoxelTypeId.Air);
                }
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
            _ = worldDirection;
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

            var pending = new Queue<MiningPropagationNode>();
            var visited = new HashSet<Vector3Int>();
            pending.Enqueue(
                new MiningPropagationNode(primaryCoordinate, settings.Power));
            visited.Add(primaryCoordinate);

            destructionDirtyMeshes.Clear();
            int candidateCount = 0;
            int damagedCount = 0;
            int destroyedCount = 0;
            VoxelMiningResult primaryResult = default;
            bool hasPrimaryResult = false;

            while (pending.Count > 0
                && candidateCount < settings.MaxAffectedSamples)
            {
                MiningPropagationNode node = pending.Dequeue();
                Vector3Int coordinate = node.Coordinate;
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

                candidateCount++;
                int durability = VoxelTypeUtility.ResolveDurability(
                    sample.Type,
                    voxelTypeCatalog != null
                        ? voxelTypeCatalog.Definitions
                        : null);
                if (!miningProgress.TryApplyDamage(
                        coordinate,
                        sample,
                        durability,
                        node.Damage,
                        false,
                        out VoxelMiningResult damageResult))
                {
                    continue;
                }

                damagedCount++;
                if (coordinate == primaryCoordinate)
                {
                    primaryResult = damageResult;
                    hasPrimaryResult = true;
                }
                if (!damageResult.Destroyed)
                {
                    continue;
                }

                if (IsOreType(sample.Type))
                {
                    int harvestedCount = HarvestConnectedOreVein(
                        coordinate,
                        sample.Type,
                        destructionDirtyMeshes);
                    if (harvestedCount > 0)
                    {
                        destroyedCount += harvestedCount;
                    }
                    else
                    {
                        world.SetVoxel(
                            coordinate.x,
                            coordinate.y,
                            coordinate.z,
                            isoLevel - 1f,
                            VoxelTypeId.Air);
                        miningProgress.Reset(coordinate);
                        CollectMeshesAffectedByVoxel(
                            coordinate,
                            destructionDirtyMeshes);
                        destroyedCount++;
                    }
                }
                else
                {
                    world.SetVoxel(
                        coordinate.x,
                        coordinate.y,
                        coordinate.z,
                        isoLevel - 1f,
                        VoxelTypeId.Air);
                    miningProgress.Reset(coordinate);
                    CollectMeshesAffectedByVoxel(
                        coordinate,
                        destructionDirtyMeshes);
                    destroyedCount++;
                }

                float propagatedDamage =
                    damageResult.ExcessDamage / settings.PropagationDivisor;
                if (propagatedDamage <= 0f)
                {
                    continue;
                }

                for (int z = -1; z <= 1; z++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            if (x == 0 && y == 0 && z == 0)
                            {
                                continue;
                            }

                            Vector3Int neighbour = coordinate
                                + new Vector3Int(x, y, z);
                            if (visited.Add(neighbour))
                            {
                                pending.Enqueue(
                                    new MiningPropagationNode(
                                        neighbour,
                                        propagatedDamage));
                            }
                        }
                    }
                }
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

        private int HarvestConnectedOreVein(
            Vector3Int start,
            VoxelTypeId type,
            HashSet<Vector3Int> affectedMeshes)
        {
            HashSet<Vector3Int> component = FindConnectedOreVein(start, type);
            if (component.Count == 0)
            {
                return 0;
            }

            VoxelMeshData meshData = MarchingCubesMesher.BuildTypeComponent(
                world,
                component,
                type,
                isoLevel,
                voxelSize,
                vertexPlacement);
            if (meshData.Vertices.Count == 0)
            {
                return 0;
            }

            CreateOreVeinBody(component, type, meshData);
            foreach (Vector3Int coordinate in component)
            {
                world.SetVoxel(
                    coordinate.x,
                    coordinate.y,
                    coordinate.z,
                    isoLevel - 1f,
                    VoxelTypeId.Air);
                miningProgress.Reset(coordinate);
                CollectMeshesAffectedByVoxel(coordinate, affectedMeshes);
            }
            return component.Count;
        }

        private HashSet<Vector3Int> FindConnectedOreVein(
            Vector3Int start,
            VoxelTypeId type)
        {
            var component = new HashSet<Vector3Int>();
            var pending = new Queue<Vector3Int>();
            pending.Enqueue(start);
            component.Add(start);

            while (pending.Count > 0)
            {
                Vector3Int coordinate = pending.Dequeue();
                for (int z = -1; z <= 1; z++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            if (x == 0 && y == 0 && z == 0)
                            {
                                continue;
                            }

                            Vector3Int neighbour = coordinate
                                + new Vector3Int(x, y, z);
                            if (component.Contains(neighbour)
                                || !world.TryGetSample(
                                    neighbour.x,
                                    neighbour.y,
                                    neighbour.z,
                                    out VoxelSample sample)
                                || !sample.IsSolid(isoLevel)
                                || sample.Type != type)
                            {
                                continue;
                            }

                            component.Add(neighbour);
                            pending.Enqueue(neighbour);
                        }
                    }
                }
            }
            return component;
        }

        private void CreateOreVeinBody(
            HashSet<Vector3Int> component,
            VoxelTypeId type,
            VoxelMeshData meshData)
        {
            VoxelTypeDefinition definition = voxelTypeCatalog != null
                ? voxelTypeCatalog.Find(type)
                : null;
            string displayName = definition != null
                ? definition.DisplayName
                : type.ToString();
            Vector3 minimum = meshData.Vertices[0];
            Vector3 maximum = minimum;
            for (int i = 1; i < meshData.Vertices.Count; i++)
            {
                minimum = Vector3.Min(minimum, meshData.Vertices[i]);
                maximum = Vector3.Max(maximum, meshData.Vertices[i]);
            }
            Vector3 meshCentre = (minimum + maximum) * 0.5f;
            for (int i = 0; i < meshData.Vertices.Count; i++)
            {
                meshData.Vertices[i] -= meshCentre;
            }

            Mesh mesh = meshData.CreateMesh($"Mined {displayName} Vein");
            mesh.hideFlags = HideFlags.DontSave;
            var dropObject = new GameObject($"Recovered {displayName} Chunk");
            dropObject.hideFlags = HideFlags.DontSave;
            Vector3 escapeDirection = ResolveOreEscapeDirection(component);
            Vector3 recoveryOffset = (escapeDirection + Vector3.up * 0.35f)
                .normalized * (voxelSize * 1.15f);
            dropObject.transform.SetPositionAndRotation(
                transform.TransformPoint(meshCentre) + transform.TransformVector(recoveryOffset),
                transform.rotation * Quaternion.Euler(12f, 24f, 8f));
            // A recovered ore is a compact, readable loot chunk rather than an
            // exact duplicate of the in-rock vein. The smaller collision envelope
            // plus the cavity-facing offset prevents it being born wedged in stone.
            dropObject.transform.localScale = Vector3.Scale(
                transform.lossyScale,
                Vector3.one * 0.68f);

            MeshFilter filter = dropObject.AddComponent<MeshFilter>();
            Renderer renderer = dropObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                renderer = dropObject.AddComponent<MeshRenderer>();
            }
            filter.sharedMesh = mesh;
            Material sourceMaterial = definition != null
                && definition.Material != null
                    ? definition.Material
                    : EnsureMaterial();
            Material recoveredMaterial = CreateRecoveredOreMaterial(
                sourceMaterial,
                type,
                displayName);
            renderer.sharedMaterial = recoveredMaterial;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;

            if (meshData.TriangleCount <= 255)
            {
                MeshCollider meshCollider =
                    dropObject.AddComponent<MeshCollider>();
                meshCollider.convex = true;
                meshCollider.sharedMesh = mesh;
            }
            else
            {
                foreach (Vector3Int coordinate in component)
                {
                    BoxCollider box = dropObject.AddComponent<BoxCollider>();
                    box.center = (Vector3)coordinate * voxelSize - meshCentre;
                    box.size = Vector3.one * (voxelSize * 0.9f);
                }
            }

            Rigidbody body = dropObject.AddComponent<Rigidbody>();
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.velocity = transform.TransformDirection(
                escapeDirection * (0.45f + Mathf.Min(component.Count, 8) * 0.04f));
            var drop = dropObject.AddComponent<MinedOreDrop>();
            VoxelOreFeatureDefinition oreFeature = FindOreFeature(type);
            drop.Configure(
                type,
                component.Count,
                mesh,
                oreFeature != null
                    ? oreFeature.MassDensity
                    : MinedOreDrop.DefaultMassDensity,
                recoveredMaterial,
                oreFeature != null
                    ? oreFeature.OreUnitValue
                    : 1,
                oreFeature != null
                    ? oreFeature.Fragility
                    : 0.25f);
            activeOreDrops.Add(drop);
        }

        private static Material CreateRecoveredOreMaterial(
            Material source,
            VoxelTypeId type,
            string displayName)
        {
            Material material = new Material(source)
            {
                name = $"Recovered {displayName} Material",
                hideFlags = HideFlags.DontSave,
            };
            Color baseColor = source != null && source.HasProperty("_BaseColor")
                ? source.GetColor("_BaseColor")
                : new Color(0.82f, 0.47f, 0.12f, 1f);
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
            {
                name = $"Recovered {displayName} Strata",
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            var pixels = new Color32[32 * 32];
            int seed = type.Value * 1103515245 + 12345;
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    float seam = Mathf.Abs(Mathf.Sin(
                        x * 0.55f + y * 0.21f + seed * 0.0001f));
                    float chip = Mathf.PerlinNoise(
                        (x + seed % 17) * 0.19f,
                        (y + seed % 29) * 0.19f);
                    float brightness = seam > 0.82f
                        ? 1.35f
                        : Mathf.Lerp(0.42f, 0.82f, chip);
                    pixels[y * 32 + x] = baseColor * brightness;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.72f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.62f);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", baseColor * 0.28f);
            }
            return material;
        }

        private static Vector3 ResolveOreEscapeDirection(
            HashSet<Vector3Int> component)
        {
            Vector3 direction = Vector3.zero;
            Vector3Int[] neighbours =
            {
                Vector3Int.right, Vector3Int.left, Vector3Int.up,
                Vector3Int.down, Vector3Int.forward, Vector3Int.back,
            };
            foreach (Vector3Int coordinate in component)
            {
                for (int i = 0; i < neighbours.Length; i++)
                {
                    if (!component.Contains(coordinate + neighbours[i]))
                    {
                        direction += (Vector3)neighbours[i];
                    }
                }
            }
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.up;
            return direction.normalized;
        }

        private VoxelOreFeatureDefinition FindOreFeature(VoxelTypeId type)
        {
            if (oreFeatures != null)
            {
                for (int i = 0; i < oreFeatures.Count; i++)
                {
                    VoxelOreFeatureDefinition feature = oreFeatures[i];
                    if (feature != null
                        && feature.ResultVoxelType != null
                        && feature.ResultVoxelType.TypeId == type)
                    {
                        return feature;
                    }
                }
            }

            return null;
        }

        private bool IsOreType(VoxelTypeId type)
        {
            for (int i = 0; i < oreFeatureSettings.Length; i++)
            {
                if (oreFeatureSettings[i].ResultType == type)
                {
                    return true;
                }
            }
            return false;
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
            int minOffsetZ = local.z == 0 ? -1 : 0;
            int section = Mathf.Clamp(
                coordinate.y / MeshSectionHeight,
                0,
                MeshSectionsPerColumn - 1);
            int minimumSection = coordinate.y > 0
                && coordinate.y % MeshSectionHeight == 0
                    ? section - 1
                    : section;
            for (int meshSection = minimumSection;
                meshSection <= section;
                meshSection++)
            {
                for (int z = minOffsetZ; z <= 0; z++)
                {
                    for (int x = minOffsetX; x <= 0; x++)
                    {
                        Vector3Int affectedColumn =
                            chunk + new Vector3Int(x, 0, z);
                        if (world.TryGetChunk(affectedColumn, out _))
                        {
                            affectedMeshes.Add(new Vector3Int(
                                affectedColumn.x,
                                meshSection,
                                affectedColumn.z));
                        }
                    }
                }
            }
        }



        public bool IsWithinGenerationRadius(Vector3Int coordinate)
        {
            int deltaX = coordinate.x - viewerChunk.x;
            int deltaZ = coordinate.z - viewerChunk.z;
            return deltaX * deltaX + deltaZ * deltaZ
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
                if (world.TryGetChunk(coordinate, out _))
                {
                    if (structurePassApplied)
                    {
                        QueueColumnMeshes(coordinate);
                    }
                }
                else if (!generationTasks.ContainsKey(coordinate)
                    && queuedChunks.Add(coordinate))
                {
                    generationQueue.Enqueue(coordinate);
                }
            }

            CancelGenerationTasksOutsideRequiredSet();
            CullMeshesOutsideRequiredSet();
        }

        private void CancelGenerationTasksOutsideRequiredSet()
        {
            foreach (KeyValuePair<Vector3Int, GenerationTaskHandle> pair
                in generationTasks)
            {
                if (!requiredChunks.Contains(pair.Key))
                {
                    pair.Value.Cancel();
                }
            }
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

                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    generationCancellation.Token);
                CancellationToken token = cancellation.Token;
                MinecraftCaveDensityField field = densityField;
                VoxelTypeId solidType = baseSolidType;
                VoxelTypeId boundaryType = bedrockType;
                MinecraftOreFeatureSettings[] features = oreFeatureSettings;
                DepthProbabilityProfile oreDepth = oreDepthProbability;
                MinecraftWorldGenerationMode mode = generationMode;
                int flatHeight = SuperflatStoneHeight;
                generationTasks.Add(
                    coordinate,
                    new GenerationTaskHandle(
                        Task.Run(
                            () => GenerateChunkData(
                                coordinate,
                                field,
                                solidType,
                                boundaryType,
                                features,
                                oreDepth,
                                mode,
                                flatHeight,
                                token),
                            token),
                        cancellation));
            }
        }

        private void CommitCompletedGenerationTasks()
        {
            if (generationTasks.Count == 0)
            {
                return;
            }

            completedGenerationCoordinates.Clear();
            foreach (KeyValuePair<Vector3Int, GenerationTaskHandle> pair
                in generationTasks)
            {
                Task<ChunkGenerationResult> task = pair.Value.Task;
                if (!task.IsCompleted)
                {
                    continue;
                }

                completedGenerationCoordinates.Add(pair.Key);
                if (task.IsCanceled)
                {
                    continue;
                }

                if (task.IsFaulted)
                {
                    Debug.LogException(
                        task.Exception?.GetBaseException()
                            ?? new InvalidOperationException("Chunk generation task failed."),
                        this);
                    continue;
                }

                ChunkGenerationResult result = task.Result;
                if (!requiredChunks.Contains(result.Coordinate)
                    || world.TryGetChunk(result.Coordinate, out _))
                {
                    continue;
                }

                CommitChunk(result);
                if (structurePassApplied)
                {
                    QueueMeshesAffectedByGeneratedColumn(result.Coordinate);
                }
            }

            foreach (Vector3Int coordinate in completedGenerationCoordinates)
            {
                generationTasks[coordinate].Dispose();
                generationTasks.Remove(coordinate);
                if (requiredChunks.Contains(coordinate)
                    && !world.TryGetChunk(coordinate, out _)
                    && queuedChunks.Add(coordinate))
                {
                    generationQueue.Enqueue(coordinate);
                }
            }
        }

        private void CommitChunk(ChunkGenerationResult result)
        {
            world.AddChunkTakingOwnership(
                result.Coordinate,
                result.Densities,
                result.Types);
        }



        private void QueueMesh(Vector3Int coordinate, bool forceRebuild = false)
        {
            if (!forceRebuild && builtMeshes.Contains(coordinate))
            {
                return;
            }
            if (!forceRebuild
                && (dirtyMeshes.Contains(coordinate)
                    || meshTasks.ContainsKey(coordinate)))
            {
                return;
            }
            if (dirtyMeshes.Add(coordinate))
            {
                IncrementMeshBuildVersion(coordinate);
                meshQueue.Enqueue(coordinate);
            }
        }

        private void QueueColumnMeshes(
            Vector3Int columnCoordinate,
            bool forceRebuild = false)
        {
            int preferredSection = GetPreferredMeshSection();
            QueueMesh(
                new Vector3Int(
                    columnCoordinate.x,
                    preferredSection,
                    columnCoordinate.z),
                forceRebuild);
            for (int distance = 1;
                distance < MeshSectionsPerColumn;
                distance++)
            {
                int lower = preferredSection - distance;
                if (lower >= 0)
                {
                    QueueMesh(
                        new Vector3Int(
                            columnCoordinate.x,
                            lower,
                            columnCoordinate.z),
                        forceRebuild);
                }

                int upper = preferredSection + distance;
                if (upper < MeshSectionsPerColumn)
                {
                    QueueMesh(
                        new Vector3Int(
                            columnCoordinate.x,
                            upper,
                            columnCoordinate.z),
                        forceRebuild);
                }
            }
        }

        private int GetPreferredMeshSection()
        {
            int voxelY = spawnVoxel.y;
            if (viewer != null)
            {
                Vector3 localVoxel =
                    transform.InverseTransformPoint(viewer.position) / voxelSize;
                voxelY = Mathf.FloorToInt(localVoxel.y);
            }

            return Mathf.Clamp(
                voxelY / MeshSectionHeight,
                0,
                MeshSectionsPerColumn - 1);
        }

        private void QueueMeshesAffectedByGeneratedColumn(
            Vector3Int generatedColumn)
        {
            for (int zOffset = -1; zOffset <= 0; zOffset++)
            {
                for (int xOffset = -1; xOffset <= 0; xOffset++)
                {
                    Vector3Int affectedColumn = generatedColumn
                        + new Vector3Int(xOffset, 0, zOffset);
                    if (requiredChunks.Contains(affectedColumn)
                        && world.TryGetChunk(affectedColumn, out _))
                    {
                        QueueColumnMeshes(affectedColumn, true);
                    }
                }
            }
        }

        // Queues a player-interaction rebuild and drains it right away so the edit is
        // visible this frame regardless of when this runs relative to world.Update.
        // The priority queue is also drained at the top of Update, keeping edits
        // ahead of the streaming backlog and its generation-stage gate.
        private void EnqueuePriorityMesh(Vector3Int coordinate)
        {
            IncrementMeshBuildVersion(coordinate);
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
                IncrementMeshBuildVersion(coordinate);
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
                && generationStage != MinecraftCaveGenerationStage.Ready
                && !structurePassApplied)
            {
                return;
            }

            using (UpdateMeshCommitMarker.Auto())
            {
                CommitCompletedMeshTasks(budget);
            }
            using (UpdateMeshSnapshotMarker.Auto())
            {
                DispatchMeshTasks(MeshSnapshotsCapturedPerFrame);
            }
        }

        private void CommitCompletedMeshTasks(int budget)
        {
            if (meshTasks.Count == 0 || budget <= 0)
            {
                return;
            }

            completedMeshCoordinates.Clear();
            foreach (KeyValuePair<Vector3Int, Task<MeshGenerationResult>> pair
                in meshTasks)
            {
                if (!pair.Value.IsCompleted)
                {
                    continue;
                }

                completedMeshCoordinates.Add(pair.Key);
                if (completedMeshCoordinates.Count >= budget)
                {
                    break;
                }
            }

            foreach (Vector3Int coordinate in completedMeshCoordinates)
            {
                Task<MeshGenerationResult> task = meshTasks[coordinate];
                meshTasks.Remove(coordinate);
                if (task.IsCanceled)
                {
                    continue;
                }
                if (task.IsFaulted)
                {
                    Debug.LogException(
                        task.Exception?.GetBaseException()
                            ?? new InvalidOperationException("Mesh generation task failed."),
                        this);
                    if (requiredChunks.Contains(ToColumnCoordinate(coordinate)))
                    {
                        QueueMesh(coordinate, true);
                    }
                    continue;
                }

                MeshGenerationResult result = task.Result;
                if (!requiredChunks.Contains(ToColumnCoordinate(coordinate))
                    || dirtyMeshes.Contains(coordinate)
                    || GetMeshBuildVersion(coordinate) != result.Version)
                {
                    continue;
                }

                ApplyChunkMeshData(coordinate, result.Data);
            }
        }

        private void DispatchMeshTasks(int captureBudget)
        {
            int maximumConcurrentMeshTasks = Mathf.Max(
                1,
                maxConcurrentGenerationJobs / 2);
            int candidates = meshQueue.Count;
            int captured = 0;
            while (captured < captureBudget
                && meshTasks.Count < maximumConcurrentMeshTasks
                && candidates-- > 0)
            {
                Vector3Int coordinate = meshQueue.Dequeue();
                if (!dirtyMeshes.Contains(coordinate))
                {
                    continue;
                }
                if (meshTasks.ContainsKey(coordinate))
                {
                    meshQueue.Enqueue(coordinate);
                    continue;
                }

                Vector3Int columnCoordinate = ToColumnCoordinate(coordinate);
                if (!requiredChunks.Contains(columnCoordinate)
                    || !world.TryGetChunk(columnCoordinate, out _))
                {
                    dirtyMeshes.Remove(coordinate);
                    continue;
                }
                if (!IsMeshSnapshotNeighborhoodReady(columnCoordinate))
                {
                    meshQueue.Enqueue(coordinate);
                    continue;
                }

                dirtyMeshes.Remove(coordinate);
                int section = Mathf.Clamp(
                    coordinate.y,
                    0,
                    MeshSectionsPerColumn - 1);
                int startY = section * MeshSectionHeight;
                int sampleCount =
                    MarchingCubesMesher.GetCapturedColumnSectionSampleCount(
                        MeshSectionHeight);
                VoxelSample[] samples =
                    ArrayPool<VoxelSample>.Shared.Rent(sampleCount);
                try
                {
                    MarchingCubesMesher.CaptureColumnSectionSamples(
                        world,
                        columnCoordinate,
                        startY,
                        MeshSectionHeight,
                        isoLevel,
                        samples,
                        baseSolidType,
                        bedrockType);
                }
                catch
                {
                    ArrayPool<VoxelSample>.Shared.Return(samples);
                    throw;
                }
                int version = GetMeshBuildVersion(coordinate);
                float capturedIsoLevel = isoLevel;
                float capturedVoxelSize = voxelSize;
                MarchingCubesVertexPlacement capturedVertexPlacement =
                    vertexPlacement;
                meshTasks.Add(
                    coordinate,
                    Task.Run(
                        () =>
                        {
                            try
                            {
                                return new MeshGenerationResult(
                                    coordinate,
                                    version,
                                    MarchingCubesMesher.BuildCapturedColumnSection(
                                        samples,
                                        MeshSectionHeight,
                                        capturedIsoLevel,
                                        capturedVoxelSize,
                                        capturedVertexPlacement));
                            }
                            finally
                            {
                                ArrayPool<VoxelSample>.Shared.Return(samples);
                            }
                        }));
                captured++;
            }
        }

        private bool IsMeshSnapshotNeighborhoodReady(
            Vector3Int columnCoordinate)
        {
            for (int zOffset = 0; zOffset <= 1; zOffset++)
            {
                for (int xOffset = 0; xOffset <= 1; xOffset++)
                {
                    Vector3Int sampledColumn = columnCoordinate
                        + new Vector3Int(xOffset, 0, zOffset);
                    if (requiredChunks.Contains(sampledColumn)
                        && !world.TryGetChunk(sampledColumn, out _))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void RebuildChunk(Vector3Int coordinate)
        {
            Vector3Int columnCoordinate = ToColumnCoordinate(coordinate);
            int section = Mathf.Clamp(
                coordinate.y,
                0,
                MeshSectionsPerColumn - 1);
            int startY = section * MeshSectionHeight;
            VoxelMeshData data = MarchingCubesMesher.BuildColumnSection(
                world,
                columnCoordinate,
                startY,
                MeshSectionHeight,
                isoLevel,
                voxelSize,
                vertexPlacement,
                baseSolidType,
                bedrockType);
            ApplyChunkMeshData(coordinate, data);
        }

        private void ApplyChunkMeshData(
            Vector3Int coordinate,
            VoxelMeshData data)
        {
            DestroyChunkObject(coordinate, false);
            Vector3Int columnCoordinate = ToColumnCoordinate(coordinate);
            int section = Mathf.Clamp(
                coordinate.y,
                0,
                MeshSectionsPerColumn - 1);
            int startY = section * MeshSectionHeight;
            builtMeshes.Add(coordinate);
            if (section == 0)
            {
                pendingTreasureColumns.Add(columnCoordinate);
                pendingMonsterColumns.Add(columnCoordinate);
            }
            if (data.Vertices.Count == 0)
            {
                FinalizeColumnPhysicsIfReady(columnCoordinate);
                return;
            }

            Mesh mesh = data.CreateMesh(
                $"Minecraft Cave Column {coordinate.x},{coordinate.z} "
                + $"Section {section}");
            mesh.hideFlags = HideFlags.DontSave;

            var chunkObject = new GameObject(
                $"CaveColumn_{coordinate.x}_{coordinate.z}_Section_{section}");
            chunkObject.hideFlags = HideFlags.DontSave;
            chunkObject.transform.SetParent(transform, false);
            chunkObject.transform.localPosition = new Vector3(
                coordinate.x * VoxelColumnChunkData.Width,
                startY,
                coordinate.z * VoxelColumnChunkData.Depth) * voxelSize;

            MeshFilter filter = chunkObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = chunkObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = VoxelTypeUtility.ResolveMaterials(
                data,
                EnsureMaterial(),
                voxelTypeCatalog != null ? voxelTypeCatalog.Definitions : null);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            if (generateColliders)
            {
                MeshCollider collider = chunkObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.sharedMaterial = terrainPhysicsMaterial;
            }

            chunkObjects[coordinate] = chunkObject;
            chunkMeshes[coordinate] = mesh;
            FinalizeColumnPhysicsIfReady(columnCoordinate);
        }

        private int IncrementMeshBuildVersion(Vector3Int coordinate)
        {
            int version = GetMeshBuildVersion(coordinate) + 1;
            meshBuildVersions[coordinate] = version;
            return version;
        }

        private int GetMeshBuildVersion(Vector3Int coordinate)
        {
            return meshBuildVersions.TryGetValue(coordinate, out int version)
                ? version
                : 0;
        }

        private void FinalizeColumnPhysicsIfReady(Vector3Int column)
        {
            if (generationStage != MinecraftCaveGenerationStage.Ready
                || !HasBuiltAllColumnSections(column))
            {
                return;
            }

            // Called after the final section's collider is assigned (or after
            // confirming that section has no surface).
            Physics.SyncTransforms();
            SpawnPendingTreasures(column);
            SpawnPendingMonsters(column);
            ResumeBodiesInColumn(column);
        }

        private bool HasBuiltAllColumnSections(Vector3Int column)
        {
            for (int section = 0; section < MeshSectionsPerColumn; section++)
            {
                if (!builtMeshes.Contains(
                    new Vector3Int(column.x, section, column.z)))
                {
                    return false;
                }
            }
            return true;
        }

        private void SpawnPendingTreasures(Vector3Int column)
        {
            if (!pendingTreasureColumns.Remove(column)) return;
            Physics.SyncTransforms();
            TrySpawnNaturalTreasures(column);
        }

        private void SpawnAllPendingTreasures()
        {
            Physics.SyncTransforms();
            var columns = new List<Vector3Int>(pendingTreasureColumns);
            for (int i = 0; i < columns.Count; i++)
            {
                if (HasBuiltAllColumnSections(columns[i]))
                {
                    SpawnPendingTreasures(columns[i]);
                }
            }
        }

        private void SpawnPendingMonsters(Vector3Int column)
        {
            if (!pendingMonsterColumns.Remove(column)) return;
            Physics.SyncTransforms();
            TrySpawnNaturalMonsters(column);
        }

        private void SpawnAllPendingMonsters()
        {
            Physics.SyncTransforms();
            var columns = new List<Vector3Int>(pendingMonsterColumns);
            for (int i = 0; i < columns.Count; i++)
            {
                if (HasBuiltAllColumnSections(columns[i]))
                {
                    SpawnPendingMonsters(columns[i]);
                }
            }
        }

        private void TrySpawnNaturalTreasures(Vector3Int column)
        {
            if (!treasureSpawnedColumns.Add(column)) return;
            if (treasureSpawnTable == null)
            {
                return;
            }

            IReadOnlyList<TreasureDefinition> definitions =
                treasureSpawnTable.Treasures;
            for (int definitionIndex = 0;
                definitionIndex < definitions.Count;
                definitionIndex++)
            {
                TreasureDefinition definition = definitions[definitionIndex];
                if (definition == null || definition.Prefab == null) continue;

                System.Random random = CreateNaturalSpawnRandom(
                    worldSeed,
                    column,
                    definitionIndex,
                    0);
                for (int attempt = 0;
                    attempt < definition.AttemptsPerChunk;
                    attempt++)
                {
                    NaturalSpawnAttempt candidate =
                        SampleNaturalSpawnAttempt(random, column);
                    Vector3 candidateWorldPosition = transform.TransformPoint(
                        new Vector3(
                            candidate.X,
                            spawnVoxel.y,
                            candidate.Z) * voxelSize);
                    if (IsInsideTreasureSpawnExclusion(candidateWorldPosition))
                    {
                        continue;
                    }
                    if (!TryFindFlatTreasureSurface(
                        candidate.X,
                        candidate.StartY,
                        candidate.Z,
                        definition,
                        out Vector3 localPosition,
                        out int surfaceY))
                    {
                        continue;
                    }
                    float effectiveChance =
                        EvaluateTreasureSpawnProbability(
                            definition,
                            surfaceY);
                    if (candidate.SpawnRoll > effectiveChance)
                    {
                        continue;
                    }

                    SpawnTreasure(definition, localPosition,
                        (float)random.NextDouble() * 360f);
                    break;
                }
            }
        }

        private void TrySpawnNaturalMonsters(Vector3Int column)
        {
            if (monsterSpawnTable == null)
            {
                return;
            }

            int maximumActive = monsterSpawnTable.MaximumActiveMonsters;
            if (maximumActive == 0)
            {
                return;
            }

            IReadOnlyList<MonsterSpawnDefinition> definitions =
                monsterSpawnTable.Monsters;
            Vector3Int playerSpawnChunk =
                WorldPositionToChunk(targetSpawnWorldPosition);
            if (IsMonsterSpawnChunkExcluded(column, playerSpawnChunk))
            {
                return;
            }
            if (CountLivingMonsters() + pendingMonsterSpawnCount
                >= maximumActive)
            {
                return;
            }

            int attemptRound = 0;
            monsterSpawnAttemptRounds.TryGetValue(column, out attemptRound);
            monsterSpawnAttemptRounds[column] = attemptRound + 1;
            int roundSeedSalt = unchecked(
                MonsterSpawnSeedSalt
                + attemptRound * MonsterSpawnRoundSeedStep);

            for (int definitionIndex = 0;
                definitionIndex < definitions.Count;
                definitionIndex++)
            {
                if (CountLivingMonsters() + pendingMonsterSpawnCount
                    >= maximumActive)
                {
                    return;
                }

                MonsterSpawnDefinition definition = definitions[definitionIndex];
                if (definition == null || definition.Prefab == null) continue;

                System.Random random = CreateNaturalSpawnRandom(
                    worldSeed,
                    column,
                    definitionIndex,
                    roundSeedSalt);
                for (int attempt = 0;
                    attempt < definition.AttemptsPerChunk;
                    attempt++)
                {
                    NaturalSpawnAttempt candidate =
                        SampleNaturalSpawnAttempt(random, column);
                    if (!TryFindFlatMonsterSurface(
                        candidate.X,
                        candidate.StartY,
                        candidate.Z,
                        definition,
                        out Vector3 localPosition,
                        out int surfaceY))
                    {
                        continue;
                    }
                    float effectiveChance = EvaluateMonsterSpawnProbability(
                        definition,
                        surfaceY);
                    if (candidate.SpawnRoll > effectiveChance)
                    {
                        continue;
                    }

                    int desiredGroupSize = random.Next(
                        definition.MinimumGroupSize,
                        definition.MaximumGroupSize + 1);
                    QueueMonsterSpawnGroup(
                        definition,
                        random,
                        desiredGroupSize,
                        candidate.X,
                        candidate.Z,
                        column,
                        localPosition,
                        surfaceY,
                        playerSpawnChunk,
                        maximumActive);
                    break;
                }
            }
        }

        private void QueueMonsterSpawnGroup(
            MonsterSpawnDefinition definition,
            System.Random random,
            int desiredGroupSize,
            int centerX,
            int centerZ,
            Vector3Int centerColumn,
            Vector3 centerLocalPosition,
            int groupSurfaceY,
            Vector3Int playerSpawnChunk,
            int maximumActive)
        {
            int availableSlots = maximumActive
                - CountLivingMonsters()
                - pendingMonsterSpawnCount;
            int groupSize = Mathf.Min(desiredGroupSize, availableSlots);
            if (groupSize <= 0)
            {
                return;
            }

            var members = new List<PendingMonsterSpawn>(groupSize)
            {
                new PendingMonsterSpawn(
                    centerLocalPosition,
                    (float)random.NextDouble() * 360f,
                    centerColumn),
            };
            int verticalSearchRadius = Mathf.Max(
                1,
                Mathf.CeilToInt(definition.GroupRadiusInVoxels * 0.5f));
            for (int memberIndex = 1; memberIndex < groupSize; memberIndex++)
            {
                for (int attempt = 0;
                    attempt < definition.AttemptsPerChunk;
                    attempt++)
                {
                    double angle = random.NextDouble() * Math.PI * 2.0;
                    double distance = Math.Sqrt(random.NextDouble())
                        * definition.GroupRadiusInVoxels;
                    int x = centerX + Mathf.RoundToInt(
                        (float)(Math.Cos(angle) * distance));
                    int z = centerZ + Mathf.RoundToInt(
                        (float)(Math.Sin(angle) * distance));
                    Vector3Int candidateColumn =
                        InfiniteVoxelWorld.WorldToChunk(x, 0, z);
                    if (IsMonsterSpawnChunkExcluded(
                            candidateColumn,
                            playerSpawnChunk)
                        || !requiredChunks.Contains(candidateColumn)
                        || !HasBuiltAllColumnSections(candidateColumn)
                        || !HasMonsterCandidateSampleNeighborhood(x, z))
                    {
                        continue;
                    }
                    if (!TryFindFlatMonsterSurfaceNearHeight(
                        x,
                        groupSurfaceY,
                        z,
                        verticalSearchRadius,
                        definition,
                        out Vector3 localPosition,
                        out _))
                    {
                        continue;
                    }

                    members.Add(new PendingMonsterSpawn(
                        localPosition,
                        (float)random.NextDouble() * 360f,
                        candidateColumn));
                    break;
                }
            }

            pendingMonsterSpawnGroups.Enqueue(
                new PendingMonsterGroupSpawn(definition, members));
            pendingMonsterSpawnCount += members.Count;
        }

        private bool HasMonsterCandidateSampleNeighborhood(int x, int z)
        {
            return HasGeneratedMonsterCandidateColumn(x, z)
                && HasGeneratedMonsterCandidateColumn(x - 1, z)
                && HasGeneratedMonsterCandidateColumn(x + 1, z)
                && HasGeneratedMonsterCandidateColumn(x, z - 1)
                && HasGeneratedMonsterCandidateColumn(x, z + 1);
        }

        private bool HasGeneratedMonsterCandidateColumn(int x, int z)
        {
            return world != null
                && world.TryGetChunk(
                    InfiniteVoxelWorld.WorldToChunk(x, 0, z),
                    out _);
        }

        private static bool IsMonsterSpawnChunkExcluded(
            Vector3Int candidateChunk,
            Vector3Int playerSpawnChunk)
        {
            return Mathf.Abs(candidateChunk.x - playerSpawnChunk.x) <= 1
                && Mathf.Abs(candidateChunk.z - playerSpawnChunk.z) <= 1;
        }

        private static System.Random CreateNaturalSpawnRandom(
            int baseSeed,
            Vector3Int column,
            int definitionIndex,
            int seedSalt)
        {
            int seed = unchecked(baseSeed ^ seedSalt);
            seed = unchecked(seed * 397) ^ column.x;
            seed = unchecked(seed * 397) ^ column.z;
            seed = unchecked(seed * 397) ^ definitionIndex;
            return new System.Random(seed);
        }

        private static NaturalSpawnAttempt SampleNaturalSpawnAttempt(
            System.Random random,
            Vector3Int column)
        {
            double spawnRoll = random.NextDouble();
            int x = column.x * VoxelColumnChunkData.Width
                + random.Next(1, VoxelColumnChunkData.Width - 1);
            int z = column.z * VoxelColumnChunkData.Depth
                + random.Next(1, VoxelColumnChunkData.Depth - 1);
            int startY = random.Next(
                2,
                VoxelColumnChunkData.Height - 3);
            return new NaturalSpawnAttempt(x, z, startY, spawnRoll);
        }

        private float EvaluateTreasureSpawnProbability(
            TreasureDefinition definition,
            int surfaceY)
        {
            return treasureDepthProbability.EvaluateProbability(
                definition != null ? definition.SpawnChance : 0f,
                surfaceY,
                VoxelColumnChunkData.Height);
        }

        private float EvaluateMonsterSpawnProbability(
            MonsterSpawnDefinition definition,
            int surfaceY)
        {
            return monsterDepthProbability.EvaluateProbability(
                definition != null ? definition.SpawnChance : 0f,
                surfaceY,
                VoxelColumnChunkData.Height);
        }

        private bool IsInsideTreasureSpawnExclusion(Vector3 worldPosition)
        {
            if (treasureSpawnTable == null)
            {
                return true;
            }

            Vector3 delta = worldPosition - targetSpawnWorldPosition;
            delta.y = 0f;
            float radius = treasureSpawnTable.SpawnExclusionRadius;
            return delta.sqrMagnitude < radius * radius;
        }

        private bool TryFindFlatTreasureSurface(
            int x,
            int startY,
            int z,
            TreasureDefinition definition,
            out Vector3 localPosition,
            out int surfaceY)
        {
            for (int offset = 0; offset < VoxelColumnChunkData.Height; offset++)
            {
                int y = (startY + offset) % (VoxelColumnChunkData.Height - 2);
                if (!IsFlatSpawnSurfaceAtY(x, y, z)
                    || !HasSpawnHeadroom(
                        x,
                        y,
                        z,
                        definition.RequiredHeadroom))
                {
                    continue;
                }

                localPosition = new Vector3(x, y + 0.6f, z) * voxelSize;
                surfaceY = y;
                return true;
            }

            localPosition = default;
            surfaceY = -1;
            return false;
        }

        private bool TryFindFlatMonsterSurface(
            int x,
            int startY,
            int z,
            MonsterSpawnDefinition definition,
            out Vector3 localPosition,
            out int surfaceY)
        {
            for (int offset = 0; offset < VoxelColumnChunkData.Height; offset++)
            {
                int y = (startY + offset) % (VoxelColumnChunkData.Height - 2);
                if (TryGetFlatMonsterSurfaceAtY(
                    x,
                    y,
                    z,
                    definition,
                    out localPosition))
                {
                    surfaceY = y;
                    return true;
                }
            }

            localPosition = default;
            surfaceY = -1;
            return false;
        }

        private bool TryFindFlatMonsterSurfaceNearHeight(
            int x,
            int centerY,
            int z,
            int verticalRadius,
            MonsterSpawnDefinition definition,
            out Vector3 localPosition,
            out int surfaceY)
        {
            for (int offset = 0; offset <= verticalRadius; offset++)
            {
                int lowerY = centerY - offset;
                if (TryGetFlatMonsterSurfaceAtY(
                    x,
                    lowerY,
                    z,
                    definition,
                    out localPosition))
                {
                    surfaceY = lowerY;
                    return true;
                }

                int upperY = centerY + offset;
                if (offset > 0 && TryGetFlatMonsterSurfaceAtY(
                    x,
                    upperY,
                    z,
                    definition,
                    out localPosition))
                {
                    surfaceY = upperY;
                    return true;
                }
            }

            localPosition = default;
            surfaceY = -1;
            return false;
        }

        private bool TryGetFlatMonsterSurfaceAtY(
            int x,
            int y,
            int z,
            MonsterSpawnDefinition definition,
            out Vector3 localPosition)
        {
            if (!IsFlatSpawnSurfaceAtY(x, y, z)
                || !HasSpawnHeadroom(
                    x,
                    y,
                    z,
                    definition.RequiredHeadroom))
            {
                localPosition = default;
                return false;
            }

            localPosition = new Vector3(x, y + 1f, z) * voxelSize
                + Vector3.up * definition.SpawnHeightOffset;
            return true;
        }

        private bool IsFlatSpawnSurfaceAtY(int x, int y, int z)
        {
            return y >= 1
                && y < VoxelColumnChunkData.Height - 1
                && IsSolid(x, y, z)
                && !IsSolid(x, y + 1, z)
                && IsSolid(x - 1, y, z)
                && IsSolid(x + 1, y, z)
                && IsSolid(x, y, z - 1)
                && IsSolid(x, y, z + 1)
                && !IsSolid(x - 1, y + 1, z)
                && !IsSolid(x + 1, y + 1, z)
                && !IsSolid(x, y + 1, z - 1)
                && !IsSolid(x, y + 1, z + 1);
        }

        private bool HasSpawnHeadroom(
            int x,
            int surfaceY,
            int z,
            float requiredHeadroom)
        {
            int headroomSamples = Mathf.CeilToInt(
                requiredHeadroom / voxelSize);
            for (int h = 1; h <= headroomSamples; h++)
            {
                if (IsSolid(x, surfaceY + h, z))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsSolid(int x, int y, int z)
        {
            return world != null
                && world.TryGetSample(x, y, z, out VoxelSample sample)
                && sample.Density >= isoLevel;
        }

        private void SpawnTreasure(
            TreasureDefinition definition,
            Vector3 localPosition,
            float yaw)
        {
            GameObject treasureObject = Instantiate(
                definition.Prefab,
                transform.TransformPoint(localPosition),
                transform.rotation * Quaternion.Euler(0f, yaw, 0f));
            treasureObject.name = "Natural Treasure - " + definition.name;
            MeshCollider[] meshColliders =
                treasureObject.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < meshColliders.Length; i++)
            {
                // Unity ignores non-convex MeshColliders attached to dynamic
                // rigidbodies. Enforce the valid pairing here as a safety net for
                // newly-authored treasure prefabs.
                meshColliders[i].convex = true;
            }
            Rigidbody body = treasureObject.GetComponent<Rigidbody>();
            if (body == null) body = treasureObject.AddComponent<Rigidbody>();
            body.mass = definition.Weight;
            if (treasureObject.GetComponentInChildren<Collider>() == null)
            {
                treasureObject.AddComponent<BoxCollider>();
            }
            TreasurePickup pickup = treasureObject.GetComponent<TreasurePickup>();
            if (pickup == null) pickup = treasureObject.AddComponent<TreasurePickup>();
            pickup.Configure(definition);
            activeTreasures.Add(pickup);
        }

        private void SpawnMonster(
            MonsterSpawnDefinition definition,
            Vector3 localPosition,
            float yaw)
        {
            GameObject monsterObject = Instantiate(
                definition.Prefab,
                transform.TransformPoint(localPosition),
                transform.rotation * Quaternion.Euler(0f, yaw, 0f));
            monsterObject.name = "Natural Monster - " + definition.name;
            CreatureBehaviorAgent agent =
                monsterObject.GetComponent<CreatureBehaviorAgent>();
            if (agent == null)
            {
                Debug.LogError(
                    $"Monster prefab '{definition.Prefab.name}' has no "
                    + $"{nameof(CreatureBehaviorAgent)} on its root.",
                    definition.Prefab);
                DestroyGeneratedObject(monsterObject);
                return;
            }

            agent.BindWorldContext(this, viewer);
            activeMonsters.Add(agent);
        }

        private void ProcessPendingMonsterSpawns()
        {
            if (monsterSpawnTable == null || pendingMonsterSpawnCount <= 0)
            {
                return;
            }

            int spawnBudget = monsterSpawnTable.MaximumMonsterSpawnsPerFrame;
            while (spawnBudget > 0)
            {
                if (activePendingMonsterSpawnGroup == null)
                {
                    if (pendingMonsterSpawnGroups.Count == 0
                        || Time.unscaledTime < nextMonsterGroupSpawnTime)
                    {
                        return;
                    }
                    activePendingMonsterSpawnGroup =
                        pendingMonsterSpawnGroups.Dequeue();
                }

                if (CountLivingMonsters()
                    >= monsterSpawnTable.MaximumActiveMonsters)
                {
                    return;
                }

                if (!activePendingMonsterSpawnGroup.TryTakeNext(
                    out PendingMonsterSpawn pendingSpawn))
                {
                    FinishActiveMonsterSpawnGroup();
                    continue;
                }
                pendingMonsterSpawnCount = Mathf.Max(
                    0,
                    pendingMonsterSpawnCount - 1);

                if (!requiredChunks.Contains(pendingSpawn.Column)
                    || !HasBuiltAllColumnSections(pendingSpawn.Column))
                {
                    if (activePendingMonsterSpawnGroup.IsComplete)
                    {
                        FinishActiveMonsterSpawnGroup();
                    }
                    continue;
                }

                SpawnMonster(
                    activePendingMonsterSpawnGroup.Definition,
                    pendingSpawn.LocalPosition,
                    pendingSpawn.Yaw);
                spawnBudget--;
                if (activePendingMonsterSpawnGroup.IsComplete)
                {
                    FinishActiveMonsterSpawnGroup();
                }
            }
        }

        private void FinishActiveMonsterSpawnGroup()
        {
            activePendingMonsterSpawnGroup = null;
            nextMonsterGroupSpawnTime = Time.unscaledTime
                + monsterSpawnTable.SecondsBetweenMonsterGroups;
        }

        private int CountLivingMonsters()
        {
            int count = 0;
            for (int i = activeMonsters.Count - 1; i >= 0; i--)
            {
                CreatureBehaviorAgent monster = activeMonsters[i];
                if (monster == null)
                {
                    activeMonsters.RemoveAt(i);
                }
                else if (monster.isActiveAndEnabled && monster.IsAlive)
                {
                    count++;
                }
            }
            return count;
        }

        private Material EnsureMaterial()
        {
            if (runtimeMaterial != null)
            {
                return runtimeMaterial;
            }

            ApplyPunctualLightFalloffParameters();

            Shader shader = Shader.Find(SoftFalloffLitShaderName);
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }
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

        private void ApplyPunctualLightFalloffParameters()
        {
            Shader.SetGlobalVector(
                SoftFalloffParametersId,
                new Vector4(
                    punctualLightFalloffPower,
                    punctualLightAttenuationLimit,
                    punctualLightMultiplier,
                    0f));
        }

        private void CullMeshesOutsideRequiredSet()
        {
            departingColumns.Clear();
            foreach (Vector3Int coordinate in chunkObjects.Keys)
            {
                Vector3Int column = ToColumnCoordinate(coordinate);
                if (!requiredChunks.Contains(column))
                {
                    departingColumns.Add(column);
                    if (queuedChunkDestructions.Add(coordinate))
                    {
                        chunkDestructionQueue.Enqueue(coordinate);
                    }
                }
            }
            SuspendBodiesInColumns(departingColumns);

            builtMeshes.RemoveWhere(
                coordinate => !requiredChunks.Contains(
                    ToColumnCoordinate(coordinate)));
        }

        private void SuspendBodiesInColumns(ISet<Vector3Int> columns)
        {
            if (!initialLoadComplete || columns.Count == 0)
            {
                return;
            }

            Rigidbody[] bodies = FindObjectsOfType<Rigidbody>();
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null
                    || body.isKinematic
                    || !body.gameObject.activeInHierarchy
                    || IsViewerOwnedBody(body))
                {
                    continue;
                }

                Vector3 local = transform.InverseTransformPoint(
                    body.worldCenterOfMass) / voxelSize;
                Vector3Int bodyColumn = InfiniteVoxelWorld.WorldToChunk(
                    Mathf.FloorToInt(local.x),
                    Mathf.FloorToInt(local.y),
                    Mathf.FloorToInt(local.z));
                if (!columns.Contains(bodyColumn))
                {
                    continue;
                }

                if (!suspendedBodiesByColumn.TryGetValue(
                        bodyColumn,
                        out List<SuspendedBodyState> suspended))
                {
                    suspended = new List<SuspendedBodyState>();
                    suspendedBodiesByColumn.Add(bodyColumn, suspended);
                }
                suspended.Add(new SuspendedBodyState(
                    body,
                    body.velocity,
                    body.angularVelocity));
                body.gameObject.SetActive(false);
            }
        }

        private void ProcessChunkDestructions(int budget)
        {
            for (int i = 0; i < budget && chunkDestructionQueue.Count > 0; i++)
            {
                Vector3Int coordinate = chunkDestructionQueue.Dequeue();
                if (!queuedChunkDestructions.Remove(coordinate)
                    || requiredChunks.Contains(ToColumnCoordinate(coordinate)))
                {
                    continue;
                }

                DestroyChunkObject(coordinate, true);
            }
        }

        private bool IsViewerOwnedBody(Rigidbody body)
        {
            if (viewer == null) return false;
            Transform candidate = body.transform;
            return candidate == viewer
                || candidate.IsChildOf(viewer)
                || viewer.IsChildOf(candidate);
        }

        private void ResumeBodiesInColumn(Vector3Int column)
        {
            if (!suspendedBodiesByColumn.TryGetValue(
                column,
                out List<SuspendedBodyState> suspended))
            {
                return;
            }

            suspendedBodiesByColumn.Remove(column);
            Physics.SyncTransforms();
            for (int i = 0; i < suspended.Count; i++)
            {
                SuspendedBodyState state = suspended[i];
                if (state.Body == null) continue;
                state.Body.gameObject.SetActive(true);
                state.Body.velocity = state.Velocity;
                state.Body.angularVelocity = state.AngularVelocity;
                state.Body.WakeUp();
            }
        }

        private static Vector3Int ToColumnCoordinate(Vector3Int coordinate)
        {
            return new Vector3Int(coordinate.x, 0, coordinate.z);
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
                SpawnAllPendingTreasures();
                SpawnAllPendingMonsters();
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
            if (generationStage != MinecraftCaveGenerationStage.Terrain)
            {
                return;
            }

            bool allRequiredTerrainReady = generationQueue.Count == 0
                && generationTasks.Count == 0
                && CountGeneratedRequiredChunks() == requiredChunks.Count;
            if (structurePassApplied)
            {
                if (allRequiredTerrainReady)
                {
                    generationStage = MinecraftCaveGenerationStage.Meshes;
                }
                return;
            }

            if (!allRequiredTerrainReady)
            {
                return;
            }

            generationStage = MinecraftCaveGenerationStage.Structures;
            int writtenSamples = 0;
            int clearedSamples = 0;
            int supportedGroundSamples = 0;
            int clearedGroundHeadroomSamples = 0;
            int restoredBedrockSamples = 0;
            if (!structurePassApplied)
            {
                bool isSuperflat =
                    generationMode == MinecraftWorldGenerationMode.Superflat;
                if (!isSuperflat)
                {
                    PrepareSpawnPointSceneStructure();
                    writtenSamples = spawnPointStructureRule.Apply(world, spawnVoxel);
                    restoredBedrockSamples = RestoreBoundaryBedrock();
                    if (spawnPointSceneStructure != null)
                    {
                        clearedSamples = spawnPointSceneStructure.CarveTerrainClearance(
                            world,
                            transform,
                            voxelSize,
                            isoLevel - 1f);
                        supportedGroundSamples =
                            spawnPointSceneStructure.StabilizeLandingGround(
                                world,
                                transform,
                                voxelSize,
                                isoLevel + 1f,
                                baseSolidType,
                                isoLevel - 1f,
                                out clearedGroundHeadroomSamples);
                    }
                }
                structurePassApplied = true;
            }

            generationStage = MinecraftCaveGenerationStage.Meshes;
            builtMeshes.RemoveWhere(
                coordinate => requiredChunks.Contains(
                    ToColumnCoordinate(coordinate)));
            foreach (Vector3Int coordinate in requiredChunks)
            {
                QueueColumnMeshes(coordinate, true);
            }

            Debug.Log(
                $"Minecraft infinite cave data passes ready: {requiredChunks.Count} terrain chunks, "
                + $"{writtenSamples} structure samples, "
                + $"{restoredBedrockSamples} boundary bedrock samples restored, "
                + $"{supportedGroundSamples} landing-ground samples supported, "
                + $"{clearedGroundHeadroomSamples} landing headroom samples cleared, "
                + $"{clearedSamples} Cell and landing-shaft clearance samples. "
                + "Marching Cubes may now begin.",
                this);
        }

        private int RestoreBoundaryBedrock()
        {
            if (world == null)
            {
                return 0;
            }

            int restored = 0;
            foreach (Vector3Int coordinate in requiredChunks)
            {
                if (!world.TryGetChunk(coordinate, out InfiniteVoxelChunk chunk))
                {
                    continue;
                }

                for (int z = 0; z < VoxelColumnChunkData.Depth; z++)
                {
                    for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                    {
                        chunk.Data.SetSample(
                            x,
                            0,
                            z,
                            BoundaryBedrockDensity,
                            bedrockType);
                        chunk.Data.SetSample(
                            x,
                            VoxelColumnChunkData.Height - 1,
                            z,
                            BoundaryBedrockDensity,
                            bedrockType);
                        restored += 2;
                    }
                }
            }
            return restored;
        }

        private void PrepareSpawnPointSceneStructure()
        {
            if (spawnPointSceneStructure == null || !initialSpawnPlacementPending)
            {
                return;
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
            if (generationMode == MinecraftWorldGenerationMode.Superflat
                || spawnPointSceneStructure == null)
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
            float rayDistance = voxelSize * VoxelColumnChunkData.Height;

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
            int maximumSamples = VoxelColumnChunkData.Height;
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
                bool complete = true;
                for (int section = 0;
                    section < MeshSectionsPerColumn;
                    section++)
                {
                    if (!builtMeshes.Contains(new Vector3Int(
                            coordinate.x,
                            section,
                            coordinate.z)))
                    {
                        complete = false;
                        break;
                    }
                }
                if (complete)
                {
                    count++;
                }
            }
            return count;
        }

        private Vector3Int FindCaveSpawnVoxel()
        {
            var random = new System.Random(worldSeed ^ 0x51F15EED);
            int middleSpawnY = (LowestSpawnY + HighestSpawnY) / 2;
            var best = new Vector3Int(0, middleSpawnY, 0);
            float bestDensity = float.PositiveInfinity;
            for (int attempt = 0; attempt < 2400; attempt++)
            {
                Vector3Int point = attempt == 0
                    ? best
                    : new Vector3Int(
                        random.Next(-72, 73),
                        random.Next(LowestSpawnY, HighestSpawnY + 1),
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
            VoxelTypeId boundaryType,
            MinecraftOreFeatureSettings[] features,
            DepthProbabilityProfile oreDepthProbability,
            MinecraftWorldGenerationMode mode,
            int superflatHeight,
            CancellationToken token)
        {
            if (mode == MinecraftWorldGenerationMode.Superflat)
            {
                return GenerateSuperflatChunkData(
                    coordinate,
                    solidType,
                    superflatHeight,
                    token);
            }

            float[] densities = MinecraftCaveDensityInterpolator.SampleColumn(
                coordinate,
                field,
                token);
            var types = new VoxelTypeId[VoxelColumnChunkData.VoxelCount];
            for (int index = 0; index < densities.Length; index++)
            {
                types[index] = densities[index] >= 0f
                    ? solidType
                    : VoxelTypeId.Air;
            }

            ApplyBoundaryBedrock(densities, types, boundaryType);
            MinecraftOreFeatureGenerator.GenerateColumn(
                coordinate,
                densities,
                types,
                field.Seed,
                features,
                (x, y, z) => IsBoundaryBedrockY(y)
                    || !InfiniteVoxelWorld.IsWorldYInBounds(y)
                        ? BoundaryBedrockDensity
                        : field.SampleFeatureDensity(
                            new Vector3(x, y, z),
                            MinecraftCaveType.Combined),
                token,
                oreDepthProbability);
            return new ChunkGenerationResult(coordinate, densities, types);
        }

        private static ChunkGenerationResult GenerateSuperflatChunkData(
            Vector3Int coordinate,
            VoxelTypeId solidType,
            int height,
            CancellationToken token)
        {
            var densities = new float[VoxelColumnChunkData.VoxelCount];
            var types = new VoxelTypeId[VoxelColumnChunkData.VoxelCount];
            int clampedHeight = Mathf.Clamp(
                height,
                1,
                VoxelColumnChunkData.Height - 1);
            for (int y = 0; y < VoxelColumnChunkData.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                bool isStone = y < clampedHeight;
                float density = isStone ? 1f : -1f;
                VoxelTypeId type = isStone ? solidType : VoxelTypeId.Air;
                for (int z = 0; z < VoxelColumnChunkData.Depth; z++)
                {
                    for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                    {
                        int index = VoxelColumnChunkData.ToIndex(x, y, z);
                        densities[index] = density;
                        types[index] = type;
                    }
                }
            }
            return new ChunkGenerationResult(coordinate, densities, types);
        }

        private static int ApplyBoundaryBedrock(
            float[] densities,
            VoxelTypeId[] types,
            VoxelTypeId boundaryType)
        {
            int written = 0;
            for (int z = 0; z < VoxelColumnChunkData.Depth; z++)
            {
                for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                {
                    int bottom = VoxelColumnChunkData.ToIndex(x, 0, z);
                    int top = VoxelColumnChunkData.ToIndex(
                        x,
                        VoxelColumnChunkData.Height - 1,
                        z);
                    densities[bottom] = BoundaryBedrockDensity;
                    types[bottom] = boundaryType;
                    densities[top] = BoundaryBedrockDensity;
                    types[top] = boundaryType;
                    written += 2;
                }
            }
            return written;
        }

        private static bool IsBoundaryBedrockY(int y)
        {
            return y == 0 || y == VoxelColumnChunkData.Height - 1;
        }

        private void SnapshotVoxelGenerationSettings()
        {
            baseSolidType = baseSolidVoxelType != null
                ? baseSolidVoxelType.TypeId
                : VoxelTypeId.Default;
            bedrockType = bedrockVoxelType != null
                ? bedrockVoxelType.TypeId
                : baseSolidType;

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
                for (int x = -GenerationRadiusInChunks; x <= GenerationRadiusInChunks; x++)
                {
                    var offset = new Vector3Int(x, 0, z);
                    if (x * x + z * z <= radiusSquared)
                    {
                        offsets.Add(offset);
                    }
                }
            }
            offsets.Sort((left, right) => left.sqrMagnitude.CompareTo(right.sqrMagnitude));
            return offsets.ToArray();
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
            foreach (GenerationTaskHandle handle in generationTasks.Values)
            {
                handle.Cancel();
                handle.Dispose();
            }
            generationCancellation?.Dispose();
            generationCancellation = null;
            generationTasks.Clear();
            completedGenerationCoordinates.Clear();
            meshTasks.Clear();
            meshBuildVersions.Clear();
            completedMeshCoordinates.Clear();
            generationQueue.Clear();
            queuedChunks.Clear();
            meshQueue.Clear();
            dirtyMeshes.Clear();
            priorityMeshQueue.Clear();
            priorityDirtyMeshes.Clear();
            builtMeshes.Clear();
            requiredChunks.Clear();
            chunkDestructionQueue.Clear();
            queuedChunkDestructions.Clear();
            departingColumns.Clear();
            miningProgress.Clear();
            for (int i = activeOreDrops.Count - 1; i >= 0; i--)
            {
                MinedOreDrop drop = activeOreDrops[i];
                if (drop != null)
                {
                    DestroyGeneratedObject(drop.gameObject);
                }
            }
            activeOreDrops.Clear();
            for (int i = activeTreasures.Count - 1; i >= 0; i--)
            {
                TreasurePickup treasure = activeTreasures[i];
                if (treasure != null) DestroyGeneratedObject(treasure.gameObject);
            }
            activeTreasures.Clear();
            treasureSpawnedColumns.Clear();
            pendingTreasureColumns.Clear();
            for (int i = activeMonsters.Count - 1; i >= 0; i--)
            {
                CreatureBehaviorAgent monster = activeMonsters[i];
                if (monster != null) DestroyGeneratedObject(monster.gameObject);
            }
            activeMonsters.Clear();
            monsterSpawnAttemptRounds.Clear();
            pendingMonsterSpawnGroups.Clear();
            activePendingMonsterSpawnGroup = null;
            pendingMonsterSpawnCount = 0;
            nextMonsterGroupSpawnTime = 0f;
            pendingMonsterColumns.Clear();
            foreach (List<SuspendedBodyState> suspended
                in suspendedBodiesByColumn.Values)
            {
                for (int i = 0; i < suspended.Count; i++)
                {
                    if (suspended[i].Body != null)
                    {
                        suspended[i].Body.gameObject.SetActive(true);
                    }
                }
            }
            suspendedBodiesByColumn.Clear();

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
            bedrockType = VoxelTypeId.Default;
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

        private readonly struct NaturalSpawnAttempt
        {
            public NaturalSpawnAttempt(
                int x,
                int z,
                int startY,
                double spawnRoll)
            {
                X = x;
                Z = z;
                StartY = startY;
                SpawnRoll = spawnRoll;
            }

            public int X { get; }
            public int Z { get; }
            public int StartY { get; }
            public double SpawnRoll { get; }
        }

        private readonly struct PendingMonsterSpawn
        {
            public PendingMonsterSpawn(
                Vector3 localPosition,
                float yaw,
                Vector3Int column)
            {
                LocalPosition = localPosition;
                Yaw = yaw;
                Column = column;
            }

            public Vector3 LocalPosition { get; }
            public float Yaw { get; }
            public Vector3Int Column { get; }
        }

        private sealed class PendingMonsterGroupSpawn
        {
            private readonly List<PendingMonsterSpawn> members;
            private int nextMemberIndex;

            public PendingMonsterGroupSpawn(
                MonsterSpawnDefinition definition,
                List<PendingMonsterSpawn> members)
            {
                Definition = definition;
                this.members = members
                    ?? throw new ArgumentNullException(nameof(members));
            }

            public MonsterSpawnDefinition Definition { get; }
            public bool IsComplete => nextMemberIndex >= members.Count;

            public bool TryTakeNext(out PendingMonsterSpawn spawn)
            {
                if (IsComplete)
                {
                    spawn = default;
                    return false;
                }

                spawn = members[nextMemberIndex];
                nextMemberIndex++;
                return true;
            }
        }

        private readonly struct SuspendedBodyState
        {
            public SuspendedBodyState(
                Rigidbody body,
                Vector3 velocity,
                Vector3 angularVelocity)
            {
                Body = body;
                Velocity = velocity;
                AngularVelocity = angularVelocity;
            }

            public Rigidbody Body { get; }
            public Vector3 Velocity { get; }
            public Vector3 AngularVelocity { get; }
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

        private sealed class MeshGenerationResult
        {
            public MeshGenerationResult(
                Vector3Int coordinate,
                int version,
                VoxelMeshData data)
            {
                Coordinate = coordinate;
                Version = version;
                Data = data ?? throw new ArgumentNullException(nameof(data));
            }

            public Vector3Int Coordinate { get; }
            public int Version { get; }
            public VoxelMeshData Data { get; }
        }

        private sealed class GenerationTaskHandle : IDisposable
        {
            private readonly CancellationTokenSource cancellation;

            public GenerationTaskHandle(
                Task<ChunkGenerationResult> task,
                CancellationTokenSource cancellation)
            {
                Task = task ?? throw new ArgumentNullException(nameof(task));
                this.cancellation = cancellation
                    ?? throw new ArgumentNullException(nameof(cancellation));
            }

            public Task<ChunkGenerationResult> Task { get; }

            public void Cancel()
            {
                cancellation.Cancel();
            }

            public void Dispose()
            {
                cancellation.Dispose();
            }
        }

        private readonly struct MiningPropagationNode
        {
            public MiningPropagationNode(
                Vector3Int coordinate,
                float damage)
            {
                Coordinate = coordinate;
                Damage = damage;
            }

            public Vector3Int Coordinate { get; }
            public float Damage { get; }
        }
    }
}
