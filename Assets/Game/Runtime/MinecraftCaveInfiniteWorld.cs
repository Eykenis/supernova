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
using Supernova.WorldGeneration;
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
        private const int PreviewRadiusInChunks = 8;
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
        private static readonly ReadOnlyCollection<Vector3Int> PreviewOffsets =
            Array.AsReadOnly(BuildOffsets(PreviewRadiusInChunks));
        private static readonly ReadOnlyCollection<Vector3Int>
            DenseRegionOffsets =
                Array.AsReadOnly(BuildSquareOffsets(
                    DenseJigsawWorldConfiguration
                        .DefaultRegionColumnsPerSide));

        [Header("Viewer")]
        [SerializeField] private Transform viewer;

        [Header("Level")]
        [Tooltip(
            "Optional level used by isolated test/demo scenes. Product scenes "
            + "use the level selected by MissionGameLoop.")]
        [SerializeField] private LevelConfiguration levelConfigurationOverride;
        [Tooltip(
            "Optional direct world-generation configuration for isolated preview "
            + "scenes that do not need mission, treasure, or monster settings.")]
        [SerializeField]
        private MinecraftWorldGenerationConfiguration
            worldGenerationConfigurationOverride;
        [Tooltip(
            "Optional finite dense-jigsaw profile. When assigned, this world "
            + "still runs the complete InfiniteCaves pipeline and only replaces "
            + "the streaming extent and jigsaw snapshot.")]
        [SerializeField]
        private DenseJigsawWorldConfiguration
            denseJigsawRegionConfigurationOverride;
        private ReadOnlyCollection<Vector3Int> configuredDenseRegionOffsets;
        private int configuredDenseRegionColumns;
        [Tooltip(
            "Generate only a diameter-16-chunk circular preview around spawn "
            + "and disable viewer-driven streaming.")]
        [SerializeField] private bool fixedPreviewArea;

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
        /// <summary>
        /// Type-to-group snapshot handed to worker threads. Mesh building runs off
        /// the main thread, so the catalog cannot be queried live.
        /// </summary>
        private VoxelGroupMap voxelGroupMap;
        private float punctualLightFalloffPower = 0.55f;
        private float punctualLightAttenuationLimit = 1.5f;
        private float punctualLightMultiplier = 1f;
        private VoxelTypeDefinition baseSolidVoxelType;
        private VoxelTypeDefinition bedrockVoxelType;
        private List<VoxelOreFeatureDefinition> oreFeatures =
            new List<VoxelOreFeatureDefinition>();
        private List<VoxelStructureFeatureDefinition> structureFeatures =
            new List<VoxelStructureFeatureDefinition>();
        private List<JigsawStructureFeatureDefinition> jigsawStructures =
            new List<JigsawStructureFeatureDefinition>();
        private CaveBiomeCatalog caveBiomeCatalog;
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
        private readonly Dictionary<Vector3Int, Mesh> terrainSurfaceMeshes =
            new Dictionary<Vector3Int, Mesh>();
        private readonly HashSet<Vector3Int> gameplayCarvedVoxels =
            new HashSet<Vector3Int>();
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
        // Structures carry authored spawn markers. They are tracked separately from
        // natural scatter so a designer's boss room is not silently skipped just
        // because the world's ambient monster budget happens to be full.
        private readonly HashSet<Vector3Int> markerSpawnedColumns =
            new HashSet<Vector3Int>();
        private readonly List<CreatureBehaviorAgent> activeMarkerMonsters =
            new List<CreatureBehaviorAgent>();
        private readonly List<StructureSpawnRequest> markerSpawnBuffer =
            new List<StructureSpawnRequest>();
        private readonly HashSet<Vector3Int> checkpointSpawnedColumns =
            new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> placedCheckpointVoxels =
            new HashSet<Vector3Int>();
        private readonly List<CheckpointSpawnRequest> checkpointSpawnBuffer =
            new List<CheckpointSpawnRequest>();
        private GameObject primarySpawnCheckpoint;
        [SerializeField, Range(0f, 1f)]
        [Tooltip(
            "Non-zero enables checkpoints authored by jigsaw piece markers.")]
        private float checkpointSpawnChance = 0.35f;
        [SerializeField] private JigsawStructureFeatureDefinition
            spawnCheckpointJigsawFeature;
        private const int CheckpointFloorSearchDistance = 6;
        private readonly Dictionary<Vector3Int, List<SuspendedBodyState>>
            suspendedBodiesByColumn =
                new Dictionary<Vector3Int, List<SuspendedBodyState>>();

        private InfiniteVoxelWorld world;
        private MinecraftCaveDensityField densityField;
        private VoxelTypeId baseSolidType = VoxelTypeId.Default;
        private VoxelTypeId bedrockType = VoxelTypeId.Default;
        private MinecraftOreFeatureSettings[] oreFeatureSettings =
            Array.Empty<MinecraftOreFeatureSettings>();
        private MinecraftStructureFeatureSettings[] structureFeatureSettings =
            Array.Empty<MinecraftStructureFeatureSettings>();
        private JigsawStructureFeatureSettings[] jigsawStructureSettings =
            Array.Empty<JigsawStructureFeatureSettings>();
        private DenseJigsawFeature denseJigsawFeature;
        private JigsawPlacementSelection denseJigsawPlacementSelection;
        private CancellationTokenSource generationCancellation;
        private Material runtimeMaterial;
        private Material runtimeTerrainSurfaceMaterial;
        private bool terrainSurfaceShaderLookupFailed;
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
        private Vector3 authoredSpawnWorldPosition;
        private Quaternion authoredSpawnWorldRotation;
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
            EffectiveWorldHeight - 1);
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
        public Vector3 AuthoredSpawnWorldPosition => authoredSpawnWorldPosition;
        public Quaternion AuthoredSpawnWorldRotation =>
            authoredSpawnWorldRotation;
        public GameObject PrimarySpawnCheckpoint => primarySpawnCheckpoint;
        public event Action<GameObject> PrimarySpawnCheckpointCreated;
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
        public IReadOnlyList<VoxelStructureFeatureDefinition> StructureFeatures =>
            structureFeatures;
        public CaveBiomeCatalog CaveBiomeCatalog => caveBiomeCatalog;
        public LevelConfiguration LevelConfiguration => levelConfiguration;
        public MinecraftWorldGenerationConfiguration WorldGenerationConfiguration =>
            worldGenerationConfiguration;
        public IReadOnlyList<MinedOreDrop> ActiveOreDrops => activeOreDrops;
        public IReadOnlyList<TreasurePickup> ActiveTreasures => activeTreasures;
        public IReadOnlyList<CreatureBehaviorAgent> ActiveMonsters => activeMonsters;
        public static IReadOnlyList<Vector3Int> StreamingOffsets => RequiredOffsets;
        public static IReadOnlyList<Vector3Int> PreviewStreamingOffsets =>
            PreviewOffsets;
        public static IReadOnlyList<Vector3Int> DenseRegionStreamingOffsets =>
            DenseRegionOffsets;
        public IReadOnlyList<Vector3Int>
            ConfiguredDenseRegionStreamingOffsets =>
                ResolveConfiguredDenseRegionOffsets();
        public bool IsFiniteDenseRegion =>
            denseJigsawRegionConfigurationOverride != null;
        public int EffectiveWorldHeight => IsFiniteDenseRegion
            ? denseJigsawRegionConfigurationOverride.WorldHeight
            : VoxelColumnChunkData.Height;
        public int EffectiveMeshSectionsPerColumn =>
            EffectiveWorldHeight / MeshSectionHeight;
        private bool UsesFixedGenerationArea =>
            fixedPreviewArea || IsFiniteDenseRegion;
        public bool UsesExternalDenseLandingCell =>
            IsFiniteDenseRegion
            && denseJigsawRegionConfigurationOverride.UseExternalLandingCell
            && spawnPointSceneStructure != null;
        public DenseJigsawWorldConfiguration DenseRegionConfiguration =>
            denseJigsawRegionConfigurationOverride;
        public int DenseAcceptedJigsawPlacementCount =>
            denseJigsawPlacementSelection != null
                ? denseJigsawPlacementSelection.AcceptedPlacementCount
                : 0;

        public bool ConfigureDenseRegion(
            DenseJigsawWorldConfiguration configuration,
            Transform worldViewer = null)
        {
            if (configuration == null)
            {
                Debug.LogError(
                    "Cannot configure a Dense region without a configuration.",
                    this);
                return false;
            }
            if (world != null)
            {
                Debug.LogError(
                    "Cannot configure a Dense region after world "
                    + "initialization has started.",
                    this);
                return false;
            }

            denseJigsawRegionConfigurationOverride = configuration;
            if (worldViewer != null)
            {
                viewer = worldViewer;
            }
            return true;
        }

        public void ConfigureSpawnCheckpointJigsaw(
            JigsawStructureFeatureDefinition feature)
        {
            spawnCheckpointJigsawFeature = feature;
        }

        public bool ConfigureSpawnPointSceneStructure(
            SpawnPointSceneStructure structure)
        {
            if (world != null)
            {
                return false;
            }

            spawnPointSceneStructure = structure;
            return true;
        }

        public bool ApplyLevelConfiguration(LevelConfiguration value)
        {
            if (value == null || value.WorldGeneration == null)
            {
                return false;
            }
            if (!ApplyWorldGenerationConfiguration(value.WorldGeneration))
            {
                return false;
            }

            levelConfiguration = value;
            worldSeed = value.WorldSeed;
            treasureSpawnTable = value.TreasureGeneration;
            monsterSpawnTable = value.MonsterGeneration;
            return true;
        }

        public bool ApplyWorldGenerationConfiguration(
            MinecraftWorldGenerationConfiguration value)
        {
            if (value == null)
            {
                return false;
            }
            if (world != null)
            {
                Debug.LogError(
                    "A world generation configuration cannot be changed after "
                    + "world initialization has started.",
                    this);
                return false;
            }

            worldGenerationConfiguration = value;
            generationMode = value.GenerationMode;
            superflatStoneHeight = value.SuperflatStoneHeight;
            worldSeed = value.WorldSeed;
            settings = value.Settings;
            placeViewerInCave = value.PlaceViewerInCave;
            maxConcurrentGenerationJobs = value.MaxConcurrentGenerationJobs;
            meshesBuiltPerFrame = value.MeshesBuiltPerFrame;
            oreDepthProbability =
                value.OreDepthProbability ?? new DepthProbabilityProfile();
            treasureDepthProbability =
                value.TreasureDepthProbability ?? new DepthProbabilityProfile();
            monsterDepthProbability =
                value.MonsterDepthProbability ?? new DepthProbabilityProfile();
            voxelSize = value.VoxelSize;
            isoLevel = value.IsoLevel;
            vertexPlacement = value.VertexPlacement;
            generateColliders = value.GenerateColliders;
            terrainPhysicsMaterial = value.TerrainPhysicsMaterial;
            voxelTypeCatalog = value.VoxelTypeCatalog;
            voxelGroupMap = VoxelGroupMap.FromDefinitions(
                voxelTypeCatalog != null ? voxelTypeCatalog.Definitions : null);
            punctualLightFalloffPower = value.PunctualLightFalloffPower;
            punctualLightAttenuationLimit = value.PunctualLightAttenuationLimit;
            punctualLightMultiplier = value.PunctualLightMultiplier;
            baseSolidVoxelType = value.BaseSolidVoxelType;
            bedrockVoxelType = value.BedrockVoxelType;
            oreFeatures = new List<VoxelOreFeatureDefinition>(value.OreFeatures);
            caveBiomeCatalog = value.CaveBiomeCatalog;
            structureFeatures =
                new List<VoxelStructureFeatureDefinition>(value.StructureFeatures);
            jigsawStructures = new List<JigsawStructureFeatureDefinition>(
                value.JigsawStructures);
            spawnPointStructureRule =
                value.SpawnPointStructureRule ?? new SpawnPointStructureRule();
            return true;
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                bool configurationApplied =
                    denseJigsawRegionConfigurationOverride != null
                        ? ApplyLevelConfiguration(
                            denseJigsawRegionConfigurationOverride
                                .InfiniteCavesLevelSource)
                        : worldGenerationConfigurationOverride != null
                            ? ApplyWorldGenerationConfiguration(
                                worldGenerationConfigurationOverride)
                            : ApplyLevelConfiguration(
                                levelConfigurationOverride != null
                                    ? levelConfigurationOverride
                                    : MissionGameLoop.CurrentLevelConfiguration);
                if (!configurationApplied)
                {
                    Debug.LogError(
                        "MinecraftCaveInfiniteWorld requires either a direct "
                        + "MinecraftWorldGenerationConfiguration or an active "
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
            if (UsesFixedGenerationArea || initialSpawnPlacementPending)
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
            PlayerSpawnRequest authoredPlayerSpawn = default;
            bool hasAuthoredPlayerSpawn = !isSuperflat
                && IsFiniteDenseRegion
                && JigsawStructureGenerator.TryResolvePlayerSpawn(
                    densityField.Seed,
                    jigsawStructureSettings,
                    out authoredPlayerSpawn);
            spawnVoxel = isSuperflat
                ? new Vector3Int(0, SuperflatStoneHeight, 0)
                : hasAuthoredPlayerSpawn
                    ? authoredPlayerSpawn.VoxelPosition
                    : FindCaveSpawnVoxel();
            Vector3 spawnVoxelPosition = isSuperflat || UsesFixedGenerationArea
                ? (Vector3)spawnVoxel
                : spawnPointStructureRule.GetPlayerSpawnVoxel(spawnVoxel);
            authoredSpawnWorldPosition =
                transform.TransformPoint(spawnVoxelPosition * voxelSize);
            authoredSpawnWorldRotation = hasAuthoredPlayerSpawn
                ? transform.rotation * Quaternion.Euler(
                    0f,
                    authoredPlayerSpawn.Yaw,
                    0f)
                : viewer != null
                    ? viewer.rotation
                    : transform.rotation;
            targetSpawnWorldPosition = authoredSpawnWorldPosition;
            targetSpawnWorldRotation = authoredSpawnWorldRotation;
            if (spawnPointSceneStructure != null)
            {
                spawnPointSceneStructure.ClearExitTarget();
            }
            if (UsesExternalDenseLandingCell)
            {
                Vector3 externalSpawnVoxel =
                    denseJigsawRegionConfigurationOverride
                        .ExternalLandingCellPlayerVoxelPosition;
                targetSpawnWorldPosition = transform.TransformPoint(
                    externalSpawnVoxel * voxelSize);
                targetSpawnWorldRotation = Quaternion.LookRotation(
                    -transform.right,
                    transform.up);
            }
            if (!isSuperflat
                && (!UsesFixedGenerationArea
                    || UsesExternalDenseLandingCell))
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
                        gameplayCarvedVoxels.Add(coordinate);
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
                    gameplayCarvedVoxels.Add(coordinate);
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

        public bool TryMineExplosion(
            Vector3 worldCenter,
            VoxelExplosionSettings settings,
            out VoxelExplosionResult result)
        {
            result = default;
            if (world == null)
                return false;

            Vector3Int centerCoordinate = WorldPositionToVoxel(worldCenter);
            Vector3 scale = transform.lossyScale;
            float minimumScale = Mathf.Max(
                0.0001f,
                Mathf.Min(
                    Mathf.Abs(scale.x),
                    Mathf.Abs(scale.y),
                    Mathf.Abs(scale.z)));
            int coordinateRadius = Mathf.CeilToInt(
                settings.Radius / (voxelSize * minimumScale)) + 1;
            float radiusSquared = settings.Radius * settings.Radius;
            var pending = new List<ExplosionPropagationNode>();
            var strongestScheduledDamage =
                new Dictionary<Vector3Int, float>();
            var processed = new HashSet<Vector3Int>();
            int candidateCount = 0;

            for (int z = -coordinateRadius; z <= coordinateRadius; z++)
            {
                for (int y = -coordinateRadius; y <= coordinateRadius; y++)
                {
                    for (int x = -coordinateRadius; x <= coordinateRadius; x++)
                    {
                        Vector3Int coordinate = centerCoordinate
                            + new Vector3Int(x, y, z);
                        Vector3 sampleWorldPosition = transform.TransformPoint(
                            (Vector3)coordinate * voxelSize);
                        float distanceSquared =
                            (sampleWorldPosition - worldCenter).sqrMagnitude;
                        if (distanceSquared > radiusSquared
                            || !world.TryGetSample(
                                coordinate.x,
                                coordinate.y,
                                coordinate.z,
                                out VoxelSample sample)
                            || !sample.IsSolid(isoLevel))
                        {
                            continue;
                        }

                        float damage = settings.GetPower(
                            Mathf.Sqrt(distanceSquared));
                        if (damage <= 0f)
                            continue;

                        candidateCount++;
                        pending.Add(
                            new ExplosionPropagationNode(coordinate, damage));
                        strongestScheduledDamage[coordinate] = damage;
                    }
                }
            }

            destructionDirtyMeshes.Clear();
            int damagedCount = 0;
            int destroyedCount = 0;
            while (pending.Count > 0)
            {
                ExplosionPropagationNode node =
                    RemoveStrongestExplosionNode(pending);
                if (processed.Contains(node.Coordinate)
                    || !strongestScheduledDamage.TryGetValue(
                        node.Coordinate,
                        out float strongestDamage)
                    || node.Damage + 0.0001f < strongestDamage
                    || !world.TryGetSample(
                        node.Coordinate.x,
                        node.Coordinate.y,
                        node.Coordinate.z,
                        out VoxelSample sample)
                    || !sample.IsSolid(isoLevel))
                {
                    continue;
                }

                processed.Add(node.Coordinate);
                int durability = VoxelTypeUtility.ResolveDurability(
                    sample.Type,
                    voxelTypeCatalog != null
                        ? voxelTypeCatalog.Definitions
                        : null);
                if (!miningProgress.TryApplyDamage(
                        node.Coordinate,
                        sample,
                        durability,
                        node.Damage,
                        false,
                        out VoxelMiningResult damageResult))
                {
                    continue;
                }

                damagedCount++;
                if (!damageResult.Destroyed)
                    continue;

                if (IsOreType(sample.Type))
                {
                    int harvestedCount = HarvestConnectedOreVein(
                        node.Coordinate,
                        sample.Type,
                        destructionDirtyMeshes);
                    if (harvestedCount > 0)
                    {
                        destroyedCount += harvestedCount;
                    }
                    else
                    {
                        RemoveExplosionVoxel(node.Coordinate);
                        destroyedCount++;
                    }
                }
                else
                {
                    RemoveExplosionVoxel(node.Coordinate);
                    destroyedCount++;
                }

                float propagatedDamage = damageResult.ExcessDamage
                    / settings.PropagationDivisor;
                if (propagatedDamage <= 0f)
                    continue;

                for (int neighbourZ = -1; neighbourZ <= 1; neighbourZ++)
                {
                    for (int neighbourY = -1; neighbourY <= 1; neighbourY++)
                    {
                        for (int neighbourX = -1;
                            neighbourX <= 1;
                            neighbourX++)
                        {
                            if (neighbourX == 0
                                && neighbourY == 0
                                && neighbourZ == 0)
                            {
                                continue;
                            }

                            Vector3Int neighbour = node.Coordinate
                                + new Vector3Int(
                                    neighbourX,
                                    neighbourY,
                                    neighbourZ);
                            Vector3 neighbourWorldPosition =
                                transform.TransformPoint(
                                    (Vector3)neighbour * voxelSize);
                            if ((neighbourWorldPosition - worldCenter)
                                    .sqrMagnitude > radiusSquared
                                || processed.Contains(neighbour)
                                || !world.TryGetSample(
                                    neighbour.x,
                                    neighbour.y,
                                    neighbour.z,
                                    out VoxelSample neighbourSample)
                                || !neighbourSample.IsSolid(isoLevel)
                                || neighbourSample.Type != sample.Type
                                || (strongestScheduledDamage.TryGetValue(
                                        neighbour,
                                        out float scheduledDamage)
                                    && scheduledDamage >= propagatedDamage))
                            {
                                continue;
                            }

                            strongestScheduledDamage[neighbour] =
                                propagatedDamage;
                            pending.Add(
                                new ExplosionPropagationNode(
                                    neighbour,
                                    propagatedDamage));
                        }
                    }
                }
            }

            if (destructionDirtyMeshes.Count > 0)
                EnqueuePriorityMeshes(destructionDirtyMeshes);

            result = new VoxelExplosionResult(
                worldCenter,
                candidateCount,
                damagedCount,
                destroyedCount);
            return damagedCount > 0;
        }

        private void RemoveExplosionVoxel(Vector3Int coordinate)
        {
            gameplayCarvedVoxels.Add(coordinate);
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
        }

        private static ExplosionPropagationNode RemoveStrongestExplosionNode(
            List<ExplosionPropagationNode> pending)
        {
            int strongestIndex = 0;
            for (int i = 1; i < pending.Count; i++)
            {
                if (pending[i].Damage > pending[strongestIndex].Damage)
                    strongestIndex = i;
            }

            ExplosionPropagationNode node = pending[strongestIndex];
            int lastIndex = pending.Count - 1;
            pending[strongestIndex] = pending[lastIndex];
            pending.RemoveAt(lastIndex);
            return node;
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
                gameplayCarvedVoxels.Add(coordinate);
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
                displayName);
            renderer.sharedMaterial = recoveredMaterial;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.On;

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
            string displayName)
        {
            Material material = new Material(source)
            {
                name = $"Recovered {displayName} Material",
                hideFlags = HideFlags.DontSave,
            };
            Color baseColor = source != null && source.HasProperty("_BaseColor")
                ? source.GetColor("_BaseColor")
                : source != null && source.HasProperty("_Color")
                    ? source.GetColor("_Color")
                    : new Color(0.82f, 0.47f, 0.12f, 1f);
            Color recoveredColor = new Color(
                baseColor.r * 0.82f,
                baseColor.g * 0.82f,
                baseColor.b * 0.82f,
                baseColor.a);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", recoveredColor);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", recoveredColor);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat(
                    "_Metallic",
                    Mathf.Min(material.GetFloat("_Metallic"), 0.35f));
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat(
                    "_Smoothness",
                    Mathf.Min(material.GetFloat("_Smoothness"), 0.4f));
            }
            if (material.HasProperty("_EmissionColor"))
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
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

            var coordinate = new Vector3Int(worldX, worldY, worldZ);
            if (previous.IsSolid(isoLevel) && density < isoLevel)
            {
                gameplayCarvedVoxels.Add(coordinate);
            }
            else if (density >= isoLevel)
            {
                gameplayCarvedVoxels.Remove(coordinate);
            }
            world.SetVoxel(worldX, worldY, worldZ, density, normalizedType);
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
                EffectiveMeshSectionsPerColumn - 1);
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
            if (IsFiniteDenseRegion)
            {
                return requiredChunks.Contains(coordinate);
            }

            int deltaX = coordinate.x - viewerChunk.x;
            int deltaZ = coordinate.z - viewerChunk.z;
            if (fixedPreviewArea)
            {
                return deltaX * deltaX + deltaZ * deltaZ
                    <= PreviewRadiusInChunks * PreviewRadiusInChunks;
            }
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

            IReadOnlyList<Vector3Int> offsets = IsFiniteDenseRegion
                ? ResolveConfiguredDenseRegionOffsets()
                : fixedPreviewArea
                    ? PreviewOffsets
                    : RequiredOffsets;
            // Dense can start the viewer in an external landing Cell. Its authored
            // spawn is the portal destination that must be playable first.
            Vector3Int initialLoadCenter = IsFiniteDenseRegion
                ? WorldPositionToChunk(authoredSpawnWorldPosition)
                : viewerChunk;
            foreach (Vector3Int offset in offsets)
            {
                Vector3Int coordinate = IsFiniteDenseRegion
                    ? offset
                    : viewerChunk + offset;
                if (initialSpawnAreaOnly
                    && !fixedPreviewArea
                    && (Mathf.Abs(coordinate.x - initialLoadCenter.x)
                            > InitialSpawnRadiusInChunks
                        || Mathf.Abs(coordinate.z - initialLoadCenter.z)
                            > InitialSpawnRadiusInChunks))
                {
                    continue;
                }
                requiredChunks.Add(coordinate);
            }

            if (!structurePassApplied && !UsesFixedGenerationArea)
            {
                spawnPointStructureRule.CollectRequiredChunks(spawnVoxel, requiredChunks);
            }

            RefreshDenseJigsawPlacementSelection();

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

        private void RefreshDenseJigsawPlacementSelection()
        {
            denseJigsawPlacementSelection = null;
            if (!IsFiniteDenseRegion
                || !denseJigsawRegionConfigurationOverride
                    .PreventStructureIntersections
                || jigsawStructureSettings == null
                || jigsawStructureSettings.Length == 0
                || requiredChunks.Count == 0)
            {
                return;
            }

            int minimumChunkX = int.MaxValue;
            int maximumChunkX = int.MinValue;
            int minimumChunkZ = int.MaxValue;
            int maximumChunkZ = int.MinValue;
            IReadOnlyCollection<Vector3Int> selectionColumns =
                ResolveConfiguredDenseRegionOffsets();
            foreach (Vector3Int coordinate in selectionColumns)
            {
                minimumChunkX = Math.Min(minimumChunkX, coordinate.x);
                maximumChunkX = Math.Max(maximumChunkX, coordinate.x);
                minimumChunkZ = Math.Min(minimumChunkZ, coordinate.z);
                maximumChunkZ = Math.Max(maximumChunkZ, coordinate.z);
            }

            denseJigsawPlacementSelection =
                JigsawPlacementSelection.CreateNonIntersecting(
                    jigsawStructureSettings,
                    densityField != null ? densityField.Seed : worldSeed,
                    minimumChunkX * VoxelColumnChunkData.Width,
                    minimumChunkZ * VoxelColumnChunkData.Depth,
                    (maximumChunkX + 1) * VoxelColumnChunkData.Width - 1,
                    (maximumChunkZ + 1) * VoxelColumnChunkData.Depth - 1);
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
                MinecraftStructureFeatureSettings[] structures =
                    structureFeatureSettings;
                JigsawStructureFeatureSettings[] jigsaws =
                    jigsawStructureSettings;
                JigsawPlacementSelection jigsawSelection =
                    denseJigsawPlacementSelection;
                DepthProbabilityProfile oreDepth = oreDepthProbability;
                MinecraftWorldGenerationMode mode = generationMode;
                int flatHeight = SuperflatStoneHeight;
                int worldHeight = EffectiveWorldHeight;
                float sampleIsoLevel = isoLevel;
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
                                jigsaws,
                                jigsawSelection,
                                structures,
                                oreDepth,
                                mode,
                                flatHeight,
                                worldHeight,
                                sampleIsoLevel,
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
            if (TryGetFiniteRegionChunkBounds(
                    out int minimumChunkX,
                    out int maximumChunkX,
                    out int minimumChunkZ,
                    out int maximumChunkZ)
                && world.TryGetChunk(
                    result.Coordinate,
                    out InfiniteVoxelChunk chunk))
            {
                RestoreFiniteRegionSideBedrock(
                    chunk.Data,
                    result.Coordinate,
                    minimumChunkX,
                    maximumChunkX,
                    minimumChunkZ,
                    maximumChunkZ);
            }
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
                distance < EffectiveMeshSectionsPerColumn;
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
                if (upper < EffectiveMeshSectionsPerColumn)
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
                EffectiveMeshSectionsPerColumn - 1);
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
                    EffectiveMeshSectionsPerColumn - 1);
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
                VoxelGroupMap capturedGroupMap = voxelGroupMap;
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
                                        capturedVertexPlacement,
                                        capturedGroupMap));
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
                EffectiveMeshSectionsPerColumn - 1);
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
                bedrockType,
                voxelGroupMap);
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
                EffectiveMeshSectionsPerColumn - 1);
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
            IReadOnlyList<VoxelTypeDefinition> voxelDefinitions =
                voxelTypeCatalog != null
                    ? voxelTypeCatalog.Definitions
                    : null;
            renderer.sharedMaterials = VoxelTypeUtility.ResolveMaterials(
                data,
                EnsureMaterial(),
                voxelDefinitions);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            if (generateColliders)
            {
                MeshCollider collider = chunkObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.sharedMaterial = terrainPhysicsMaterial;
            }

            SpawnTerrainSurfaceLayer(
                chunkObject.transform,
                coordinate,
                startY,
                data,
                voxelDefinitions);
            SpawnSurfaceContent(chunkObject.transform, coordinate, startY, data);

            chunkObjects[coordinate] = chunkObject;
            chunkMeshes[coordinate] = mesh;
            FinalizeColumnPhysicsIfReady(columnCoordinate);
        }

        private void SpawnTerrainSurfaceLayer(
            Transform sectionTransform,
            Vector3Int coordinate,
            int startY,
            VoxelMeshData meshData,
            IReadOnlyList<VoxelTypeDefinition> voxelDefinitions)
        {
            if (sectionTransform == null || caveBiomeCatalog == null)
            {
                return;
            }

            Mesh surfaceMesh = CaveTerrainSurfaceLayerBuilder.Build(
                meshData,
                coordinate,
                startY,
                voxelSize,
                worldSeed,
                caveBiomeCatalog,
                voxelDefinitions,
                gameplayCarvedVoxels,
                $"Cave Turf {coordinate.x},{coordinate.z},{coordinate.y}");
            if (surfaceMesh == null)
            {
                return;
            }

            Material surfaceMaterial = EnsureTerrainSurfaceMaterial();
            if (surfaceMaterial == null)
            {
                DestroyGeneratedObject(surfaceMesh);
                return;
            }

            var surfaceObject = new GameObject("TerrainSurfaceLayer");
            surfaceObject.hideFlags = HideFlags.DontSave;
            surfaceObject.transform.SetParent(sectionTransform, false);
            MeshFilter surfaceFilter = surfaceObject.AddComponent<MeshFilter>();
            MeshRenderer surfaceRenderer =
                surfaceObject.AddComponent<MeshRenderer>();
            surfaceFilter.sharedMesh = surfaceMesh;
            surfaceRenderer.sharedMaterial = surfaceMaterial;
            surfaceRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            surfaceRenderer.receiveShadows = true;
            terrainSurfaceMeshes[coordinate] = surfaceMesh;
        }

        private void SpawnSurfaceContent(
            Transform sectionTransform,
            Vector3Int coordinate,
            int startY,
            VoxelMeshData meshData)
        {
            if (caveBiomeCatalog == null || sectionTransform == null)
            {
                return;
            }

            List<CaveSurfacePlacement> placements =
                CaveSurfaceBrushGenerator.Generate(
                    meshData,
                    world,
                    coordinate,
                    startY,
                    voxelSize,
                    isoLevel,
                    worldSeed,
                    caveBiomeCatalog,
                    gameplayCarvedVoxels);
            if (placements.Count == 0)
            {
                return;
            }

            var contentRootObject = new GameObject("SurfaceContent");
            contentRootObject.hideFlags = HideFlags.DontSave;
            Transform contentRoot = contentRootObject.transform;
            contentRoot.SetParent(sectionTransform, false);

            var instancedPlacements = new List<CaveSurfacePlacement>();

            for (int i = 0; i < placements.Count; i++)
            {
                CaveSurfacePlacement placement = placements[i];
                if (placement.Brush.RenderMode ==
                    CaveSurfaceBrushRenderMode.InstancedMesh)
                {
                    instancedPlacements.Add(placement);
                    continue;
                }

                GameObject prefab = placement.Brush.Prefab;
                if (prefab == null)
                {
                    continue;
                }

                GameObject instance = Instantiate(prefab, contentRoot, false);
                instance.hideFlags = HideFlags.DontSave;
                instance.name = prefab.name;
                Transform instanceTransform = instance.transform;
                Vector3 prefabScale = instanceTransform.localScale;
                instanceTransform.localPosition = placement.LocalPosition;
                instanceTransform.localRotation = placement.LocalRotation;
                instanceTransform.localScale = Vector3.Scale(
                    prefabScale,
                    placement.Scale);

                VoxelSurfaceAttachment attachment =
                    instance.GetComponent<VoxelSurfaceAttachment>();
                if (attachment == null)
                {
                    attachment = instance.AddComponent<VoxelSurfaceAttachment>();
                }
                attachment.Configure(
                    placement.AnchorVoxel,
                    coordinate,
                    placement.Biome,
                    placement.Brush);
            }

            if (instancedPlacements.Count > 0)
            {
                CaveSurfaceInstanceRenderer instanceRenderer =
                    contentRootObject.AddComponent<
                        CaveSurfaceInstanceRenderer>();
                instanceRenderer.Configure(instancedPlacements);
            }
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
            SpawnStructureMarkers(column);
            SpawnCheckpoints(column);
            SpawnPendingTreasures(column);
            SpawnPendingMonsters(column);
            ResumeBodiesInColumn(column);
        }

        /// <summary>
        /// Instantiates the authored spawn markers of every structure piece that
        /// reaches into this column. Marker positions are resolved from the cached
        /// layout, so they are deterministic and independent of streaming order.
        /// </summary>
        private void SpawnStructureMarkers(Vector3Int column)
        {
            if (!markerSpawnedColumns.Add(column))
            {
                return;
            }
            if (jigsawStructureSettings == null
                || jigsawStructureSettings.Length == 0)
            {
                return;
            }

            JigsawStructureGenerator.CollectSpawnRequests(
                column,
                densityField != null ? densityField.Seed : worldSeed,
                jigsawStructureSettings,
                markerSpawnBuffer,
                placementSelection: denseJigsawPlacementSelection);
            for (int i = 0; i < markerSpawnBuffer.Count; i++)
            {
                SpawnStructureMarker(markerSpawnBuffer[i]);
            }
            markerSpawnBuffer.Clear();
        }

        /// <summary>
        /// Instantiates the fixed checkpoint model at the center of the
        /// dedicated spawn checkpoint hall. Mirrors
        /// <see cref="SpawnStructureMarkers"/>: positions are resolved from the
        /// cached layout, so they are deterministic and independent of streaming
        /// order.
        /// </summary>
        private void SpawnCheckpoints(Vector3Int column)
        {
            if (!checkpointSpawnedColumns.Add(column))
            {
                return;
            }
            if (jigsawStructureSettings == null
                || jigsawStructureSettings.Length == 0
                || checkpointSpawnChance <= 0f)
            {
                return;
            }

            JigsawStructureGenerator.CollectCheckpointRequests(
                column,
                densityField != null ? densityField.Seed : worldSeed,
                jigsawStructureSettings,
                checkpointSpawnBuffer,
                checkpointSpawnChance,
                placementSelection: denseJigsawPlacementSelection);
            for (int i = 0; i < checkpointSpawnBuffer.Count; i++)
            {
                SpawnCheckpoint(checkpointSpawnBuffer[i]);
            }
            checkpointSpawnBuffer.Clear();
        }

        private void SpawnCheckpoint(CheckpointSpawnRequest request)
        {
            if (request.Prefab == null)
            {
                return;
            }
            if (!placedCheckpointVoxels.Add(request.VoxelPosition))
            {
                return;
            }
            if (!TryResolveCheckpointPosition(request, out Vector3 localPosition))
            {
                return;
            }

            GameObject checkpoint = Instantiate(request.Prefab);
            checkpoint.name = "Checkpoint";
            Quaternion prefabRotation = checkpoint.transform.rotation;
            checkpoint.transform.SetPositionAndRotation(
                transform.TransformPoint(localPosition),
                transform.rotation
                    * Quaternion.Euler(0f, request.Yaw, 0f)
                    * prefabRotation);
            checkpoint.transform.SetParent(transform, true);

            Rigidbody body = checkpoint.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = checkpoint.AddComponent<Rigidbody>();
            }
            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = true;

            Collider[] checkpointColliders =
                checkpoint.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < checkpointColliders.Length; i++)
            {
                if (!checkpointColliders[i].isTrigger)
                {
                    checkpointColliders[i].enabled = true;
                }
            }
            if (request.IsSpawnCheckpoint)
            {
                primarySpawnCheckpoint = checkpoint;
                PrimarySpawnCheckpointCreated?.Invoke(checkpoint);
            }
        }

        private bool TryResolveCheckpointPosition(
            CheckpointSpawnRequest request,
            out Vector3 localPosition)
        {
            Vector3Int voxel = request.VoxelPosition;
            if (!InfiniteVoxelWorld.IsWorldYInBounds(voxel.y))
            {
                localPosition = default;
                return false;
            }

            // Drop the disc onto the first solid surface below the authored
            // marker so it rests on the generated hall floor.
            int spawnY = request.FloorY;
            for (int step = 0; step <= CheckpointFloorSearchDistance; step++)
            {
                int candidateY = voxel.y - step;
                if (candidateY <= 1)
                {
                    break;
                }
                if (IsSolidSampleAt(voxel.x, candidateY - 1, voxel.z)
                    && !IsSolidSampleAt(voxel.x, candidateY, voxel.z))
                {
                    spawnY = candidateY;
                    break;
                }
            }

            localPosition = new Vector3(
                voxel.x + 0.5f,
                spawnY,
                voxel.z + 0.5f) * voxelSize;
            return true;
        }

        private void SpawnStructureMarker(StructureSpawnRequest request)
        {
            if (!TryResolveMarkerPosition(request, out Vector3 localPosition))
            {
                return;
            }

            if (request.Kind == StructureSpawnMarkerDefinition.Kind.Treasure)
            {
                SpawnTreasure(request.Treasure, localPosition, request.Yaw);
                return;
            }

            // Marker monsters draw from their own budget rather than the ambient
            // one, so a designed encounter still appears in a busy world.
            int markerLimit = monsterSpawnTable != null
                ? monsterSpawnTable.MaximumMarkerMonsters
                : 0;
            if (CountLivingMarkerMonsters() >= markerLimit)
            {
                return;
            }
            CreatureBehaviorAgent agent = SpawnMonster(
                request.Monster,
                localPosition,
                request.Yaw);
            if (agent != null)
            {
                activeMarkerMonsters.Add(agent);
            }
        }

        /// <summary>
        /// Converts a marker's authored voxel position into a terrain-local spawn
        /// position, optionally dropping it onto the first solid surface below so a
        /// marker authored at ceiling height still lands on the floor.
        /// </summary>
        private bool TryResolveMarkerPosition(
            StructureSpawnRequest request,
            out Vector3 localPosition)
        {
            Vector3Int voxel = request.VoxelPosition;
            if (!InfiniteVoxelWorld.IsWorldYInBounds(voxel.y))
            {
                localPosition = default;
                return false;
            }

            int spawnY = voxel.y;
            if (request.SnapToFloor)
            {
                bool found = false;
                for (int step = 0; step <= request.FloorSearchDistance; step++)
                {
                    int candidateY = voxel.y - step;
                    if (candidateY <= 1)
                    {
                        break;
                    }
                    if (IsSolidSampleAt(voxel.x, candidateY - 1, voxel.z)
                        && !IsSolidSampleAt(voxel.x, candidateY, voxel.z))
                    {
                        spawnY = candidateY;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    localPosition = default;
                    return false;
                }
            }

            localPosition = new Vector3(
                voxel.x + 0.5f,
                spawnY,
                voxel.z + 0.5f) * voxelSize;
            return true;
        }

        private bool IsSolidSampleAt(int x, int y, int z)
        {
            return world != null
                && world.TryGetSample(x, y, z, out VoxelSample sample)
                && sample.IsSolid(isoLevel);
        }

        private int CountLivingMarkerMonsters()
        {
            for (int i = activeMarkerMonsters.Count - 1; i >= 0; i--)
            {
                if (activeMarkerMonsters[i] == null)
                {
                    activeMarkerMonsters.RemoveAt(i);
                }
            }
            return activeMarkerMonsters.Count;
        }

        private bool HasBuiltAllColumnSections(Vector3Int column)
        {
            for (int section = 0;
                section < EffectiveMeshSectionsPerColumn;
                section++)
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
                        SampleNaturalSpawnAttemptForWorld(random, column);
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
                        SampleNaturalSpawnAttemptForWorld(random, column);
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

        private NaturalSpawnAttempt SampleNaturalSpawnAttemptForWorld(
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
                EffectiveWorldHeight - 3);
            return new NaturalSpawnAttempt(x, z, startY, spawnRoll);
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
                EffectiveWorldHeight);
        }

        private float EvaluateMonsterSpawnProbability(
            MonsterSpawnDefinition definition,
            int surfaceY)
        {
            return monsterDepthProbability.EvaluateProbability(
                definition != null ? definition.SpawnChance : 0f,
                surfaceY,
                EffectiveWorldHeight);
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
            for (int offset = 0; offset < EffectiveWorldHeight; offset++)
            {
                int y = (startY + offset) % (EffectiveWorldHeight - 2);
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
            for (int offset = 0; offset < EffectiveWorldHeight; offset++)
            {
                int y = (startY + offset) % (EffectiveWorldHeight - 2);
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
                && y < EffectiveWorldHeight - 1
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

        private CreatureBehaviorAgent SpawnMonster(
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
                return null;
            }

            agent.BindWorldContext(this, viewer);
            activeMonsters.Add(agent);
            return agent;
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

        private Material EnsureTerrainSurfaceMaterial()
        {
            if (caveBiomeCatalog != null
                && caveBiomeCatalog.TerrainSurfaceMaterial != null)
            {
                return caveBiomeCatalog.TerrainSurfaceMaterial;
            }
            if (runtimeTerrainSurfaceMaterial != null)
            {
                return runtimeTerrainSurfaceMaterial;
            }

            Shader shader = Shader.Find(CaveTerrainShaderNames.GrassTurfLayer);
            if (shader == null)
            {
                if (!terrainSurfaceShaderLookupFailed)
                {
                    Debug.LogWarning(
                        "Grass turf layer shader is unavailable; terrain will "
                        + "render without the biome surface layer.",
                        this);
                    terrainSurfaceShaderLookupFailed = true;
                }
                return null;
            }

            runtimeTerrainSurfaceMaterial = new Material(shader)
            {
                name = "Cave Grass Turf Layer",
                hideFlags = HideFlags.DontSave,
                enableInstancing = true,
            };
            return runtimeTerrainSurfaceMaterial;
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
            if (terrainSurfaceMeshes.TryGetValue(
                coordinate,
                out Mesh terrainSurfaceMesh))
            {
                DestroyGeneratedObject(terrainSurfaceMesh);
                terrainSurfaceMeshes.Remove(coordinate);
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
                FinalizeReadyColumns();
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

                if (initialSpawnAreaReady && !fixedPreviewArea)
                {
                    RefreshRequiredChunks();
                }
            }
        }

        private void FinalizeReadyColumns()
        {
            foreach (Vector3Int column in requiredChunks)
            {
                FinalizeColumnPhysicsIfReady(column);
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
                    if (!UsesFixedGenerationArea)
                    {
                        PrepareSpawnPointSceneStructure();
                        writtenSamples = spawnPointStructureRule.Apply(world, spawnVoxel);
                    }
                    restoredBedrockSamples = RestoreBoundaryBedrock();
                    if (spawnPointSceneStructure != null
                        && !UsesExternalDenseLandingCell)
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
            int minimumChunkX = int.MaxValue;
            int maximumChunkX = int.MinValue;
            int minimumChunkZ = int.MaxValue;
            int maximumChunkZ = int.MinValue;
            TryGetFiniteRegionChunkBounds(
                out minimumChunkX,
                out maximumChunkX,
                out minimumChunkZ,
                out maximumChunkZ);

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
                            EffectiveWorldHeight - 1,
                            z,
                            BoundaryBedrockDensity,
                            bedrockType);
                        restored += 2;
                    }
                }

                if (IsFiniteDenseRegion)
                {
                    restored += RestoreFiniteRegionSideBedrock(
                        chunk.Data,
                        coordinate,
                        minimumChunkX,
                        maximumChunkX,
                        minimumChunkZ,
                        maximumChunkZ);
                }
            }
            return restored;
        }

        private bool TryGetFiniteRegionChunkBounds(
            out int minimumChunkX,
            out int maximumChunkX,
            out int minimumChunkZ,
            out int maximumChunkZ)
        {
            minimumChunkX = 0;
            maximumChunkX = 0;
            minimumChunkZ = 0;
            maximumChunkZ = 0;
            if (!IsFiniteDenseRegion)
            {
                return false;
            }

            int columns = denseJigsawRegionConfigurationOverride
                .RegionColumnsPerSide;
            minimumChunkX = -(columns / 2);
            maximumChunkX = minimumChunkX + columns - 1;
            minimumChunkZ = minimumChunkX;
            maximumChunkZ = maximumChunkX;
            return true;
        }

        private int RestoreFiniteRegionSideBedrock(
            VoxelColumnChunkData data,
            Vector3Int coordinate,
            int minimumChunkX,
            int maximumChunkX,
            int minimumChunkZ,
            int maximumChunkZ)
        {
            int restored = 0;
            if (coordinate.x == minimumChunkX
                || coordinate.x == maximumChunkX)
            {
                int x = coordinate.x == minimumChunkX
                    ? 0
                    : VoxelColumnChunkData.Width - 1;
                for (int y = 0; y < EffectiveWorldHeight; y++)
                {
                    for (int z = 0; z < VoxelColumnChunkData.Depth; z++)
                    {
                        data.SetSample(
                            x,
                            y,
                            z,
                            BoundaryBedrockDensity,
                            bedrockType);
                        restored++;
                    }
                }
            }

            if (coordinate.z == minimumChunkZ
                || coordinate.z == maximumChunkZ)
            {
                int z = coordinate.z == minimumChunkZ
                    ? 0
                    : VoxelColumnChunkData.Depth - 1;
                for (int y = 0; y < EffectiveWorldHeight; y++)
                {
                    for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                    {
                        data.SetSample(
                            x,
                            y,
                            z,
                            BoundaryBedrockDensity,
                            bedrockType);
                        restored++;
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
            float rayDistance = voxelSize * EffectiveWorldHeight;

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
            int maximumSamples = EffectiveWorldHeight;
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
                    section < EffectiveMeshSectionsPerColumn;
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
            int lowestSpawnY = IsFiniteDenseRegion
                ? Math.Max(4, EffectiveWorldHeight / 4)
                : LowestSpawnY;
            int highestSpawnY = IsFiniteDenseRegion
                ? Math.Min(
                    EffectiveWorldHeight - 4,
                    EffectiveWorldHeight * 3 / 4)
                : HighestSpawnY;
            int middleSpawnY = (lowestSpawnY + highestSpawnY) / 2;
            var best = new Vector3Int(0, middleSpawnY, 0);
            float bestDensity = float.PositiveInfinity;
            for (int attempt = 0; attempt < 2400; attempt++)
            {
                Vector3Int point = attempt == 0
                    ? best
                    : new Vector3Int(
                        random.Next(-72, 73),
                        random.Next(lowestSpawnY, highestSpawnY + 1),
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
            JigsawStructureFeatureSettings[] jigsaws,
            JigsawPlacementSelection jigsawPlacementSelection,
            MinecraftStructureFeatureSettings[] structures,
            DepthProbabilityProfile oreDepthProbability,
            MinecraftWorldGenerationMode mode,
            int superflatHeight,
            int effectiveWorldHeight,
            float sampleIsoLevel,
            CancellationToken token)
        {
            float[] densities;
            VoxelTypeId[] types;
            if (mode == MinecraftWorldGenerationMode.Superflat)
            {
                ChunkGenerationResult superflat = GenerateSuperflatChunkData(
                    coordinate,
                    solidType,
                    superflatHeight,
                    effectiveWorldHeight,
                    token);
                densities = superflat.Densities;
                types = superflat.Types;
            }
            else
            {
                densities = MinecraftCaveDensityInterpolator.SampleColumn(
                    coordinate,
                    field,
                    effectiveWorldHeight,
                    token);
                types = new VoxelTypeId[VoxelColumnChunkData.VoxelCount];
                for (int index = 0; index < densities.Length; index++)
                {
                    types[index] = densities[index] >= 0f
                        ? solidType
                        : VoxelTypeId.Air;
                }

                ApplyBoundaryBedrockWithinHeight(
                    densities,
                    types,
                    boundaryType,
                    effectiveWorldHeight);
                MinecraftOreFeatureGenerator.GenerateColumn(
                    coordinate,
                    densities,
                    types,
                    field.Seed,
                    features,
                    (x, y, z) => IsBoundaryBedrockY(
                            y,
                            effectiveWorldHeight)
                        || y < 0
                        || y >= effectiveWorldHeight
                            ? BoundaryBedrockDensity
                            : field.SampleFeatureDensity(
                                new Vector3(x, y, z),
                                MinecraftCaveType.Combined),
                    token,
                    oreDepthProbability);
            }
            JigsawStructureGenerator.GenerateColumn(
                coordinate,
                densities,
                types,
                field.Seed,
                jigsaws,
                sampleIsoLevel + 1f,
                sampleIsoLevel - 1f,
                token,
                jigsawPlacementSelection);
            MinecraftStructureFeatureGenerator.GenerateColumn(
                coordinate,
                densities,
                types,
                field.Seed,
                structures,
                sampleIsoLevel + 1f,
                sampleIsoLevel - 1f,
                token);
            ClearSamplesAboveHeight(
                densities,
                types,
                effectiveWorldHeight);
            if (mode != MinecraftWorldGenerationMode.Superflat)
            {
                ApplyBoundaryBedrockWithinHeight(
                    densities,
                    types,
                    boundaryType,
                    effectiveWorldHeight);
            }
            return new ChunkGenerationResult(coordinate, densities, types);
        }

        private static ChunkGenerationResult GenerateSuperflatChunkData(
            Vector3Int coordinate,
            VoxelTypeId solidType,
            int height,
            int effectiveWorldHeight,
            CancellationToken token)
        {
            var densities = new float[VoxelColumnChunkData.VoxelCount];
            var types = new VoxelTypeId[VoxelColumnChunkData.VoxelCount];
            for (int index = 0; index < densities.Length; index++)
            {
                densities[index] = -1f;
                types[index] = VoxelTypeId.Air;
            }
            int clampedHeight = Mathf.Clamp(
                height,
                1,
                effectiveWorldHeight - 1);
            for (int y = 0; y < effectiveWorldHeight; y++)
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
            return ApplyBoundaryBedrockWithinHeight(
                densities,
                types,
                boundaryType,
                VoxelColumnChunkData.Height);
        }

        private static int ApplyBoundaryBedrockWithinHeight(
            float[] densities,
            VoxelTypeId[] types,
            VoxelTypeId boundaryType,
            int effectiveWorldHeight)
        {
            int written = 0;
            for (int z = 0; z < VoxelColumnChunkData.Depth; z++)
            {
                for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                {
                    int bottom = VoxelColumnChunkData.ToIndex(x, 0, z);
                    int top = VoxelColumnChunkData.ToIndex(
                        x,
                        effectiveWorldHeight - 1,
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

        private static bool IsBoundaryBedrockY(
            int y,
            int effectiveWorldHeight)
        {
            return y == 0 || y == effectiveWorldHeight - 1;
        }

        private static void ClearSamplesAboveHeight(
            float[] densities,
            VoxelTypeId[] types,
            int effectiveWorldHeight)
        {
            for (int z = 0; z < VoxelColumnChunkData.Depth; z++)
            {
                for (int y = effectiveWorldHeight;
                    y < VoxelColumnChunkData.Height;
                    y++)
                {
                    for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                    {
                        int index = VoxelColumnChunkData.ToIndex(x, y, z);
                        densities[index] = -1f;
                        types[index] = VoxelTypeId.Air;
                    }
                }
            }
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
            if (IsFiniteDenseRegion)
            {
                for (int i = 0; i < snapshots.Count; i++)
                {
                    snapshots[i] = ClampOreFeatureHeight(
                        snapshots[i],
                        EffectiveWorldHeight);
                }
            }
            oreFeatureSettings = snapshots.ToArray();

            var structureSnapshots =
                new List<MinecraftStructureFeatureSettings>();
            if (structureFeatures != null)
            {
                for (int i = 0; i < structureFeatures.Count; i++)
                {
                    VoxelStructureFeatureDefinition feature = structureFeatures[i];
                    if (feature == null)
                    {
                        Debug.LogWarning(
                            $"Structure feature entry {i} is null and will be skipped.",
                            this);
                        continue;
                    }
                    if (feature.TryCreateSettings(
                        out MinecraftStructureFeatureSettings snapshot,
                        out string error))
                    {
                        structureSnapshots.Add(snapshot);
                    }
                    else
                    {
                        Debug.LogWarning(error, feature);
                    }
                }
            }
            structureFeatureSettings = structureSnapshots.ToArray();

            if (IsFiniteDenseRegion)
            {
                if (!DenseJigsawFeatureMixer.TryBuild(
                        denseJigsawRegionConfigurationOverride,
                        spawnCheckpointJigsawFeature,
                        out denseJigsawFeature,
                        out string denseError))
                {
                    throw new InvalidOperationException(denseError);
                }

                var denseSettings =
                    new List<JigsawStructureFeatureSettings>
                    {
                        denseJigsawFeature.Settings,
                    };
                if (spawnCheckpointJigsawFeature != null)
                {
                    if (spawnCheckpointJigsawFeature.TryCreateSettings(
                        out JigsawStructureFeatureSettings checkpointFamily,
                        out string checkpointError))
                    {
                        if (DenseJigsawFeatureMixer.TryBuildFixedOriginFeature(
                            denseJigsawFeature,
                            checkpointFamily,
                            out JigsawStructureFeatureSettings fixedCheckpoint,
                            out string fixedCheckpointError))
                        {
                            denseSettings.Add(fixedCheckpoint);
                        }
                        else if (!string.IsNullOrEmpty(fixedCheckpointError))
                        {
                            Debug.LogWarning(
                                fixedCheckpointError,
                                spawnCheckpointJigsawFeature);
                        }
                    }
                    else if (!string.IsNullOrEmpty(checkpointError))
                    {
                        Debug.LogWarning(checkpointError, spawnCheckpointJigsawFeature);
                    }
                }
                jigsawStructureSettings = denseSettings.ToArray();
                return;
            }

            var jigsawSnapshots =
                new List<JigsawStructureFeatureSettings>();
            if (jigsawStructures != null)
            {
                for (int i = 0; i < jigsawStructures.Count; i++)
                {
                    JigsawStructureFeatureDefinition structure =
                        jigsawStructures[i];
                    if (structure == null)
                    {
                        continue;
                    }
                    if (structure.TryCreateSettings(
                        out JigsawStructureFeatureSettings snapshot,
                        out string error))
                    {
                        jigsawSnapshots.Add(snapshot);
                    }
                    else if (!string.IsNullOrEmpty(error))
                    {
                        Debug.LogWarning(error, structure);
                    }
                }
            }
            jigsawStructureSettings = jigsawSnapshots.ToArray();
        }

        private static MinecraftOreFeatureSettings ClampOreFeatureHeight(
            MinecraftOreFeatureSettings source,
            int effectiveWorldHeight)
        {
            int maximumOreY = Math.Max(1, effectiveWorldHeight - 2);
            int minimum = Mathf.Clamp(source.MinHeight, 1, maximumOreY);
            int maximum = Mathf.Clamp(source.MaxHeight, minimum, maximumOreY);
            return new MinecraftOreFeatureSettings(
                source.ResultType,
                source.ReplaceableTypes,
                source.SeedSalt,
                source.AttemptsPerRegion,
                source.PlacementChance,
                source.Distribution,
                minimum,
                maximum,
                Math.Min(source.Plateau, maximum - minimum),
                source.Size,
                source.DiscardChanceOnAirExposure);
        }

        private static Vector3Int[] BuildRequiredOffsets()
        {
            return BuildOffsets(GenerationRadiusInChunks);
        }

        private static Vector3Int[] BuildOffsets(int radiusInChunks)
        {
            int radiusSquared = radiusInChunks * radiusInChunks;
            var offsets = new List<Vector3Int>();
            for (int z = -radiusInChunks; z <= radiusInChunks; z++)
            {
                for (int x = -radiusInChunks; x <= radiusInChunks; x++)
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

        private static Vector3Int[] BuildSquareOffsets(int columnsPerSide)
        {
            int clampedColumns = Math.Max(1, columnsPerSide);
            int minimum = -(clampedColumns / 2);
            int maximum = minimum + clampedColumns - 1;
            var offsets = new List<Vector3Int>(
                clampedColumns * clampedColumns);
            for (int z = minimum; z <= maximum; z++)
            {
                for (int x = minimum; x <= maximum; x++)
                {
                    offsets.Add(new Vector3Int(x, 0, z));
                }
            }
            offsets.Sort(
                (left, right) => left.sqrMagnitude.CompareTo(
                    right.sqrMagnitude));
            return offsets.ToArray();
        }

        private IReadOnlyList<Vector3Int> ResolveConfiguredDenseRegionOffsets()
        {
            if (!IsFiniteDenseRegion)
            {
                return DenseRegionOffsets;
            }

            int columns = denseJigsawRegionConfigurationOverride
                .RegionColumnsPerSide;
            if (configuredDenseRegionOffsets == null
                || configuredDenseRegionColumns != columns)
            {
                configuredDenseRegionOffsets = Array.AsReadOnly(
                    BuildSquareOffsets(columns));
                configuredDenseRegionColumns = columns;
            }
            return configuredDenseRegionOffsets;
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
            // Marker monsters were destroyed above as part of activeMonsters; only
            // the marker-budget bookkeeping needs clearing here.
            activeMarkerMonsters.Clear();
            markerSpawnedColumns.Clear();
            markerSpawnBuffer.Clear();
            checkpointSpawnedColumns.Clear();
            placedCheckpointVoxels.Clear();
            checkpointSpawnBuffer.Clear();
            primarySpawnCheckpoint = null;
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
            foreach (Mesh terrainSurfaceMesh in terrainSurfaceMeshes.Values)
            {
                DestroyGeneratedObject(terrainSurfaceMesh);
            }
            terrainSurfaceMeshes.Clear();
            gameplayCarvedVoxels.Clear();
            if (runtimeTerrainSurfaceMaterial != null)
            {
                DestroyGeneratedObject(runtimeTerrainSurfaceMaterial);
                runtimeTerrainSurfaceMaterial = null;
            }
            terrainSurfaceShaderLookupFailed = false;
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
            structureFeatureSettings =
                Array.Empty<MinecraftStructureFeatureSettings>();
            jigsawStructureSettings =
                Array.Empty<JigsawStructureFeatureSettings>();
            denseJigsawFeature = default;
            denseJigsawPlacementSelection = null;
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

        private readonly struct ExplosionPropagationNode
        {
            public ExplosionPropagationNode(
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
