using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Supernova.Effects;
using Supernova.Gameplay;
using Supernova.MinecraftCaves.Creatures;
using Supernova.Missions;

using Supernova.UI;
using Supernova.Voxels;
using Supernova.Voxels.Integrity;
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
        public static event Action<MinecraftCaveInfiniteWorld> InstanceEnabled;
        public static event Action<MinecraftCaveInfiniteWorld> InstanceDisabled;

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
        private const int DenseJigsawSelectionPaddingInChunks = 2;
        private const int MonsterSpawnSeedSalt = unchecked((int)0xA511E9B3);
        private const int MonsterSpawnRoundSeedStep = unchecked((int)0x9E3779B9);
        private const int NaturalMonsterSpawnGroupSize = 1;
        private const float BoundaryBedrockDensity = 1f;
        private const float TerrainProgressWeight = 0.72f;
        private const float MinimumGroundClearance = 0.02f;
        private const float MinimumExitHeadroom = 2.1f;

        private const int MinimumExitClearanceRadiusInSamples = 1;
        private const float InitialLoadPresentationFadeSeconds = 0.5f;
        private const int MinimumChunkObjectsDestroyedPerFrame = 2;
        private const int MaximumChunkObjectsDestroyedPerFrame = 8;
        private const float ChunkDestructionBudgetMilliseconds = 1f;
        private const int MaximumPooledChunkObjects = 32;
        private const int MaximumPooledChunkMeshes = 2;
        private const int MaximumPooledTreasureInstances = 64;
        private const int DefaultMaximumPooledMonsterInstances = 32;
        private const string TerrainSurfaceLayerObjectName =
            "TerrainSurfaceLayer";
        private const string SurfaceContentObjectName = "SurfaceContent";
        private const string SoftFalloffLitShaderName =
            "Supernova/Lighting/Soft Falloff Lit";

        private static readonly int SoftFalloffParametersId =
            Shader.PropertyToID("_SupernovaSoftFalloffParams");
        private static readonly ProfilerMarker UpdateViewerMarker =
            new ProfilerMarker("MinecraftWorld.Update.Viewer");
        private static readonly ProfilerMarker UpdateStreamingMarker =
            new ProfilerMarker("MinecraftWorld.Update.Streaming");
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
        private static readonly ProfilerMarker UpdateMeshPostProcessMarker =
            new ProfilerMarker("MinecraftWorld.Update.MeshPostProcess");
        private static readonly ProfilerMarker UpdateDestructionMarker =
            new ProfilerMarker("MinecraftWorld.Update.Destruction");
        private static readonly ProfilerMarker UpdateReadyMarker =
            new ProfilerMarker("MinecraftWorld.Update.Ready");
        private static readonly ProfilerMarker UpdatePhysicsFinalizeMarker =
            new ProfilerMarker("MinecraftWorld.Update.PhysicsFinalize");
        private static readonly ProfilerMarker DenseJigsawSelectionMarker =
            new ProfilerMarker("MinecraftWorld.Streaming.DenseJigsawSelection");
        private static readonly ProfilerMarker DenseJigsawCommitMarker =
            new ProfilerMarker(
                "MinecraftWorld.Streaming.DenseJigsawSelection.Commit");
        private static readonly ProfilerMarker MeshColliderPostProcessMarker =
            new ProfilerMarker("MinecraftWorld.MeshPostProcess.Collider");
        private static readonly ProfilerMarker MeshSurfaceUploadMarker =
            new ProfilerMarker("MinecraftWorld.MeshPostProcess.SurfaceUpload");
        private static readonly ProfilerMarker MeshSurfaceObjectsMarker =
            new ProfilerMarker("MinecraftWorld.MeshPostProcess.SurfaceObjects");

        private static readonly ReadOnlyCollection<Vector3Int> RequiredOffsets =
            Array.AsReadOnly(BuildRequiredOffsets());
        private static readonly ReadOnlyCollection<Vector3Int> PreviewOffsets =
            Array.AsReadOnly(BuildOffsets(PreviewRadiusInChunks));
        private static readonly ReadOnlyCollection<Vector3Int>
            DenseRegionOffsets =
                Array.AsReadOnly(BuildSquareOffsets(
                    DenseJigsawWorldConfiguration
                        .DefaultRegionColumnsPerSide));
        private static int lastGeneratedWorldSeed;

        [Header("Viewer")]
        [SerializeField] private Transform viewer;

        [Header("Level")]
        [Tooltip(
            "Ordered levels supported by this scene. The active mission or "
            + "persisted level number selects one entry from this list.")]
        [SerializeField]
        private List<LevelConfiguration> levelConfigurations =
            new List<LevelConfiguration>();
        [Tooltip(
            "Optional direct world-generation configuration for isolated preview "
            + "scenes that do not need mission, treasure, or monster settings.")]
        [SerializeField]
        private MinecraftWorldGenerationConfiguration
            worldGenerationConfigurationOverride;
        [Tooltip(
            "Optional dense-jigsaw profile. When assigned, this world "
            + "still runs the complete InfiniteCaves pipeline and only replaces "
            + "the streaming extent and jigsaw snapshot.")]
        [SerializeField]
        private DenseJigsawWorldConfiguration
            denseJigsawRegionConfigurationOverride;
        private ReadOnlyCollection<Vector3Int> configuredDenseRegionOffsets;
        private int configuredDenseRegionColumns;
        private ReadOnlyCollection<Vector3Int> configuredFixedPreviewOffsets;
        private int configuredFixedPreviewColumns;
        [Tooltip(
            "Generate a fixed preview and disable viewer-driven streaming.")]
        [SerializeField] private bool fixedPreviewArea;
        [Tooltip(
            "Optional square preview size fixed around world origin. Zero "
            + "keeps the legacy diameter-16 circular preview.")]
        [SerializeField, Min(0)] private int fixedPreviewColumnsPerSide;
        [SerializeField] private bool overrideWorldSeed;
        [SerializeField] private int worldSeedOverride = 18731;

        [Header("World Generation Debug")]
        [Tooltip(
            "Cumulative generation cut-off used by the pass debug scene. "
            + "Full Pipeline preserves normal gameplay generation.")]
        [SerializeField]
        private MinecraftWorldGenerationDebugPass generationDebugPass =
            MinecraftWorldGenerationDebugPass.FullPipeline;
        [SerializeField]
        [Tooltip(
            "Keeps the configured viewer transform unchanged while this debug "
            + "world initializes or regenerates.")]
        private bool keepViewerTransformDuringGeneration;
        private bool debugPresentationVisible = true;
        private bool debugPresentationUiVisible = true;

        [Header("Structures")]
        [SerializeField] private SpawnPointSceneStructure spawnPointSceneStructure;

        private LevelConfiguration levelConfiguration;
        private MinecraftWorldGenerationConfiguration worldGenerationConfiguration;
        private bool placeViewerInCave = true;
        private int maxConcurrentGenerationJobs = 2;
        private int maxConcurrentMeshJobs = 1;
        private int meshesBuiltPerFrame = 2;
        private int meshSnapshotsCapturedPerFrame = 2;
        private float meshCommitBudgetMilliseconds = 4f;
        private float meshSnapshotBudgetMilliseconds = 2f;
        private int voxelDataRetentionRadiusInChunks = 3;

        private readonly System.Diagnostics.Stopwatch meshCommitStopwatch =
            new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch meshSnapshotStopwatch =
            new System.Diagnostics.Stopwatch();
        private readonly System.Diagnostics.Stopwatch
            chunkDestructionStopwatch =
                new System.Diagnostics.Stopwatch();
        private DepthProbabilityProfile oreDepthProbability =
            new DepthProbabilityProfile();
        private DepthProbabilityProfile treasureDepthProbability =
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
        private CaveSurfaceGenerationSnapshot caveSurfaceGenerationSnapshot;
        private CaveSurfaceBuildResult applyingSurfaceBuildResult;
        private TreasureSpawnTable treasureSpawnTable;
        [SerializeField]
        [Tooltip(
            "Monster spawning configuration. Level configurations replace this "
            + "value; isolated world-preview scenes may assign it directly.")]
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
        private readonly Queue<MeshPostProcessRequest>
            meshPostProcessQueue = new Queue<MeshPostProcessRequest>();
        private readonly Dictionary<Vector3Int, MeshPostProcessRequest>
            pendingMeshPostProcesses =
                new Dictionary<Vector3Int, MeshPostProcessRequest>();
        private readonly Dictionary<Vector3Int, int> meshBuildVersions =
            new Dictionary<Vector3Int, int>();
        private readonly List<Vector3Int> completedMeshCoordinates =
            new List<Vector3Int>();
        private readonly HashSet<Vector3Int> destructionDirtyMeshes =
            new HashSet<Vector3Int>();
        // High-priority rebuilds from player interaction (mining / placing). These
        // coordinates enter the same snapshot/worker/commit pipeline as streaming
        // meshes, but are selected before the streaming backlog.
        private readonly Queue<Vector3Int> priorityMeshQueue = new Queue<Vector3Int>();
        private readonly HashSet<Vector3Int> priorityDirtyMeshes =
            new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> priorityMeshTasks =
            new HashSet<Vector3Int>();

        private readonly Dictionary<Vector3Int, GameObject> chunkObjects =
            new Dictionary<Vector3Int, GameObject>();
        private readonly Stack<GameObject> pooledChunkObjects =
            new Stack<GameObject>();
        private readonly Stack<Mesh> pooledChunkMeshes =
            new Stack<Mesh>();
        private readonly Dictionary<Vector3Int, Mesh> chunkMeshes =
            new Dictionary<Vector3Int, Mesh>();
        private readonly Dictionary<Vector3Int, Mesh> terrainSurfaceMeshes =
            new Dictionary<Vector3Int, Mesh>();
        private readonly HashSet<Vector3Int> gameplayCarvedVoxels =
            new HashSet<Vector3Int>();
        private readonly Dictionary<
            Vector3Int,
            Dictionary<Vector3Int, VoxelSample>> gameplayVoxelOverridesByColumn =
                new Dictionary<Vector3Int, Dictionary<Vector3Int, VoxelSample>>();
        private readonly List<Vector2Int> voxelColumnsToEvict =
            new List<Vector2Int>();
        private int gameplayVoxelOverrideCount;
        private readonly Queue<Vector3Int> chunkDestructionQueue =
            new Queue<Vector3Int>();
        private readonly HashSet<Vector3Int> queuedChunkDestructions =
            new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> departingColumns =
            new HashSet<Vector3Int>();
        private readonly VoxelMiningProgress miningProgress = new VoxelMiningProgress();
        private readonly List<MinedOreDrop> activeOreDrops =
            new List<MinedOreDrop>();
        private readonly List<PendingOreTerrainRelease>
            pendingOreTerrainReleases =
                new List<PendingOreTerrainRelease>();
        private readonly HashSet<MinedOreDrop>
            oreDropsAwaitingPhysicsSync =
                new HashSet<MinedOreDrop>();
        private readonly List<TreasurePickup> activeTreasures =
            new List<TreasurePickup>();
        private readonly Dictionary<GameObject, Stack<TreasurePickup>>
            pooledTreasuresByPrefab =
                new Dictionary<GameObject, Stack<TreasurePickup>>();
        private readonly Dictionary<TreasurePickup, GameObject>
            activeTreasurePrefabs =
                new Dictionary<TreasurePickup, GameObject>();
        private int pooledTreasureInstanceCount;
        private readonly HashSet<Vector3Int> treasureSpawnedColumns =
            new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> pendingTreasureColumns =
            new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> pendingPhysicsColumns =
            new HashSet<Vector3Int>();
        private readonly List<Vector3Int> pendingPhysicsColumnBuffer =
            new List<Vector3Int>();
        private readonly List<CreatureBehaviorAgent> activeMonsters =
            new List<CreatureBehaviorAgent>();
        private readonly Dictionary<GameObject, Stack<CreatureBehaviorAgent>>
            pooledMonstersByPrefab =
                new Dictionary<GameObject, Stack<CreatureBehaviorAgent>>();
        private readonly Dictionary<CreatureBehaviorAgent, GameObject>
            activeMonsterPrefabs =
                new Dictionary<CreatureBehaviorAgent, GameObject>();
        private int pooledMonsterInstanceCount;
        private readonly Queue<PendingMonsterGroupSpawn> pendingMonsterSpawnGroups =
            new Queue<PendingMonsterGroupSpawn>();
        private readonly List<Vector3Int> monsterSpawnCandidateColumns =
            new List<Vector3Int>();
        private PendingMonsterGroupSpawn activePendingMonsterSpawnGroup;
        private int pendingMonsterSpawnCount;
        private int naturalMonsterSpawnAttemptRound;
        private float nextNaturalMonsterSpawnAttemptTime =
            float.PositiveInfinity;
        private bool naturalMonsterSpawningEnabled;
        private bool waitingForExternalDensePortalEntry;
        private readonly HashSet<Vector3Int> markerSpawnedColumns =
            new HashSet<Vector3Int>();
        private readonly List<StructureSpawnRequest> markerSpawnBuffer =
            new List<StructureSpawnRequest>();
        private readonly List<CaveSurfacePlacement>
            instancedSurfacePlacementBuffer =
                new List<CaveSurfacePlacement>();
        private readonly HashSet<Vector3Int> checkpointSpawnedColumns =
            new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> placedCheckpointVoxels =
            new HashSet<Vector3Int>();
        private readonly List<CheckpointSpawnRequest> checkpointSpawnBuffer =
            new List<CheckpointSpawnRequest>();
        private readonly List<GameObject> activeCheckpointObjects =
            new List<GameObject>();
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
        private bool usesExternalWorldRendering;
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
        private DenseJigsawSelectionTaskHandle denseJigsawSelectionTask;
        private bool denseJigsawSelectionPending;
        private bool hasDenseJigsawSelectionWindow;
        private int denseJigsawSelectionMinimumChunkX;
        private int denseJigsawSelectionMaximumChunkX;
        private int denseJigsawSelectionMinimumChunkZ;
        private int denseJigsawSelectionMaximumChunkZ;
        private int denseJigsawSelectionRebuildCount;
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
        private MinecraftCaveGenerationStage publishedInitialLoadStage =
            (MinecraftCaveGenerationStage)(-1);
        private int publishedInitialLoadPercent = -1;
        private bool publishedInitialLoadComplete;

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
        public MinecraftWorldGenerationDebugPass GenerationDebugPass =>
            generationDebugPass;
        public int FixedPreviewColumnsPerSide =>
            Mathf.Max(0, fixedPreviewColumnsPerSide);
        public bool OverridesWorldSeed => overrideWorldSeed;
        public bool KeepsViewerTransformDuringGeneration =>
            keepViewerTransformDuringGeneration;
        public bool DebugPresentationVisible => debugPresentationVisible;
        public int InFlightChunkCount => generationTasks.Count;
        public int QueuedChunkCount => generationQueue.Count;
        public int RenderedChunkCount => chunkObjects.Count;
        public int CachedVoxelColumnCount => world != null ? world.ChunkCount : 0;
        public int GameplayVoxelOverrideCount => gameplayVoxelOverrideCount;
        public int DenseJigsawSelectionRebuildCount =>
            denseJigsawSelectionRebuildCount;
        public bool IsDenseJigsawSelectionPending =>
            denseJigsawSelectionPending;
        public int PendingMeshPostProcessCount =>
            pendingMeshPostProcesses.Count;
        public int PooledChunkObjectCount => pooledChunkObjects.Count;
        public int PooledChunkMeshCount => pooledChunkMeshes.Count;
        public int PooledTreasureInstanceCount =>
            pooledTreasureInstanceCount;
        public int PooledMonsterInstanceCount =>
            pooledMonsterInstanceCount;
        public Vector3Int ViewerChunk => viewerChunk;
        public Vector3Int SpawnVoxel => spawnVoxel;
        public Vector3 SpawnWorldPosition => targetSpawnWorldPosition;
        public Vector3 AuthoredSpawnWorldPosition => authoredSpawnWorldPosition;
        public Quaternion AuthoredSpawnWorldRotation =>
            authoredSpawnWorldRotation;
        public GameObject PrimarySpawnCheckpoint => primarySpawnCheckpoint;
        public event Action<GameObject> PrimarySpawnCheckpointCreated;
        public event Action<MinecraftCaveGenerationStage, float, bool>
            InitialLoadProgressChanged;
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
        public IReadOnlyList<LevelConfiguration> ConfiguredLevels =>
            levelConfigurations;
        public MinecraftWorldGenerationConfiguration WorldGenerationConfiguration =>
            worldGenerationConfiguration;
        public IReadOnlyList<MinedOreDrop> ActiveOreDrops => activeOreDrops;
        public IReadOnlyList<TreasurePickup> ActiveTreasures => activeTreasures;
        public IReadOnlyList<CreatureBehaviorAgent> ActiveMonsters => activeMonsters;
        public MonsterSpawnTable MonsterSpawnTable => monsterSpawnTable;
        public bool NaturalMonsterSpawningEnabled =>
            generationDebugPass == MinecraftWorldGenerationDebugPass.FullPipeline
            && (naturalMonsterSpawningEnabled || !UsesExternalDenseLandingCell);
        public static IReadOnlyList<Vector3Int> StreamingOffsets => RequiredOffsets;
        public static IReadOnlyList<Vector3Int> PreviewStreamingOffsets =>
            PreviewOffsets;
        public static IReadOnlyList<Vector3Int> DenseRegionStreamingOffsets =>
            DenseRegionOffsets;
        public IReadOnlyList<Vector3Int>
            ConfiguredDenseRegionStreamingOffsets =>
                ResolveConfiguredDenseRegionOffsets();
        private bool HasDenseJigsawConfiguration =>
            denseJigsawRegionConfigurationOverride != null;
        public bool IsFiniteDenseRegion =>
            HasDenseJigsawConfiguration
            && !denseJigsawRegionConfigurationOverride.GenerateInfiniteWorld;
        public bool IsInfiniteDenseWorld =>
            HasDenseJigsawConfiguration
            && denseJigsawRegionConfigurationOverride.GenerateInfiniteWorld;
        public int EffectiveWorldHeight => HasDenseJigsawConfiguration
            ? denseJigsawRegionConfigurationOverride.WorldHeight
            : VoxelColumnChunkData.Height;
        public int EffectiveMeshSectionsPerColumn =>
            EffectiveWorldHeight / MeshSectionHeight;
        private bool UsesConfiguredFixedPreviewArea =>
            fixedPreviewArea && fixedPreviewColumnsPerSide > 0;
        private bool UsesFixedGenerationArea =>
            fixedPreviewArea || IsFiniteDenseRegion;
        public bool UsesExternalDenseLandingCell =>
            HasDenseJigsawConfiguration
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

        public bool ConfigureLevels(
            IReadOnlyList<LevelConfiguration> levels)
        {
            if (world != null)
            {
                Debug.LogError(
                    "Level configurations cannot be changed after world "
                    + "initialization has started.",
                    this);
                return false;
            }

            levelConfigurations.Clear();
            if (levels != null)
            {
                for (int i = 0; i < levels.Count; i++)
                {
                    LevelConfiguration level = levels[i];
                    if (level != null && !levelConfigurations.Contains(level))
                        levelConfigurations.Add(level);
                }
            }
            return levelConfigurations.Count > 0;
        }

        public bool BeginNaturalMonsterSpawningAfterPortalEntry()
        {
            if (!UsesExternalDenseLandingCell
                || naturalMonsterSpawningEnabled)
            {
                return false;
            }

            naturalMonsterSpawningEnabled = true;
            waitingForExternalDensePortalEntry = false;
            ScheduleNextNaturalMonsterSpawnAttempt();
            if (IsInfiniteDenseWorld && world != null && viewer != null)
            {
                viewerChunk = WorldPositionToChunk(viewer.position);
                hasViewerChunk = true;
                RefreshRequiredChunks();
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

        public bool ApplyLevelConfigurationForNewRun(LevelConfiguration value)
        {
            if (!ApplyLevelConfiguration(value))
            {
                return false;
            }

            worldSeed = CreateRandomWorldSeed(value.WorldSeed);
            return true;
        }

        public static int CreateRandomWorldSeed()
        {
            return CreateRandomWorldSeed(0);
        }

        private static int CreateRandomWorldSeed(int excludedSeed)
        {
            int seed;
            do
            {
                seed = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0)
                    & int.MaxValue;
            }
            while (seed == 0
                || seed == excludedSeed
                || seed == lastGeneratedWorldSeed);

            lastGeneratedWorldSeed = seed;
            return seed;
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
            maxConcurrentMeshJobs = value.MaxConcurrentMeshJobs;
            meshesBuiltPerFrame = value.MeshesBuiltPerFrame;
            meshSnapshotsCapturedPerFrame =
                value.MeshSnapshotsCapturedPerFrame;
            meshCommitBudgetMilliseconds =
                value.MeshCommitBudgetMilliseconds;
            meshSnapshotBudgetMilliseconds =
                value.MeshSnapshotBudgetMilliseconds;
            voxelDataRetentionRadiusInChunks =
                value.VoxelDataRetentionRadiusInChunks;
            oreDepthProbability =
                value.OreDepthProbability ?? new DepthProbabilityProfile();
            treasureDepthProbability =
                value.TreasureDepthProbability ?? new DepthProbabilityProfile();
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
            caveSurfaceGenerationSnapshot =
                CaveSurfaceGenerationSnapshot.Capture(caveBiomeCatalog);
            structureFeatures =
                new List<VoxelStructureFeatureDefinition>(value.StructureFeatures);
            jigsawStructures = new List<JigsawStructureFeatureDefinition>(
                value.JigsawStructures);
            spawnPointStructureRule =
                value.SpawnPointStructureRule ?? new SpawnPointStructureRule();
            return true;
        }

        public bool SetGenerationDebugPass(
            MinecraftWorldGenerationDebugPass value,
            bool regenerateIfPlaying = true)
        {
            if (value != MinecraftWorldGenerationDebugPass.FullPipeline
                && !value.IsSelectableDebugPass())
            {
                return false;
            }
            if (generationDebugPass == value)
            {
                return true;
            }

            generationDebugPass = value;
            if (regenerateIfPlaying
                && Application.isPlaying
                && isActiveAndEnabled
                && world != null)
            {
                RestartGenerationPreservingViewer();
            }
            return true;
        }

        public bool SetGenerationSeedOverride(
            int value,
            bool regenerateIfPlaying = true)
        {
            if (overrideWorldSeed && worldSeedOverride == value)
            {
                return true;
            }

            overrideWorldSeed = true;
            worldSeedOverride = value;
            worldSeed = value;
            if (regenerateIfPlaying
                && Application.isPlaying
                && isActiveAndEnabled
                && world != null)
            {
                RestartGenerationPreservingViewer();
            }
            return true;
        }

        public void SetDebugPresentationVisible(bool visible, bool showUi)
        {
            debugPresentationVisible = visible;
            debugPresentationUiVisible = showUi;
            foreach (GameObject chunkObject in chunkObjects.Values)
            {
                ApplyDebugPresentationVisibility(chunkObject);
            }
            for (int i = 0; i < activeTreasures.Count; i++)
            {
                TreasurePickup treasure = activeTreasures[i];
                ApplyDebugPresentationVisibility(
                    treasure != null ? treasure.gameObject : null);
            }
            for (int i = 0; i < activeMonsters.Count; i++)
            {
                CreatureBehaviorAgent monster = activeMonsters[i];
                ApplyDebugPresentationVisibility(
                    monster != null ? monster.gameObject : null);
            }
            for (int i = 0; i < activeCheckpointObjects.Count; i++)
            {
                ApplyDebugPresentationVisibility(activeCheckpointObjects[i]);
            }
        }

        private void ApplyDebugPresentationVisibility(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = debugPresentationVisible;
            }
            Light[] lights = target.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                lights[i].enabled = debugPresentationVisible;
            }
            Canvas[] canvases = target.GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                canvases[i].enabled = debugPresentationVisible
                    && debugPresentationUiVisible;
            }
        }

        private void RestartGenerationPreservingViewer()
        {
            Transform preservedViewer = viewer;
            bool preserve = keepViewerTransformDuringGeneration
                && preservedViewer != null;
            Vector3 preservedPosition = preserve
                ? preservedViewer.position
                : default;
            Quaternion preservedRotation = preserve
                ? preservedViewer.rotation
                : default;
            enabled = false;
            enabled = true;
            if (preserve && preservedViewer != null)
            {
                preservedViewer.SetPositionAndRotation(
                    preservedPosition,
                    preservedRotation);
            }
        }

        /// <summary>
        /// Adopts voxel data produced by a custom generator while retaining this
        /// component's normal mining, ore recovery, damage, and gameplay rules.
        /// Mesh presentation remains the responsibility of the generator adapter.
        /// </summary>
        public bool AdoptGeneratedWorld(
            LevelConfiguration configuration,
            InfiniteVoxelWorld generatedWorld)
        {
            if (configuration == null || generatedWorld == null)
            {
                return false;
            }

            ClearRuntimeState();
            if (!ApplyLevelConfiguration(configuration))
            {
                return false;
            }

            world = generatedWorld;
            SnapshotVoxelGenerationSettings();
            usesExternalWorldRendering = true;
            generationStage = MinecraftCaveGenerationStage.Ready;
            initialLoadComplete = true;
            return true;
        }

        /// <summary>
        /// Copies mesh-section coordinates queued by mutations in an adopted
        /// world so its external renderer can rebuild only the affected sections.
        /// </summary>
        public int CollectAdoptedWorldDirtyMeshes(
            ISet<Vector3Int> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (!usesExternalWorldRendering)
            {
                return 0;
            }

            int previousCount = destination.Count;
            destination.UnionWith(dirtyMeshes);
            destination.UnionWith(priorityDirtyMeshes);
            return destination.Count - previousCount;
        }

        /// <summary>
        /// Clears mesh rebuild requests after an external renderer has synchronously
        /// presented mutations made by the shared gameplay runtime.
        /// </summary>
        public void CompleteAdoptedWorldMeshRebuild()
        {
            if (!usesExternalWorldRendering)
            {
                return;
            }

            dirtyMeshes.Clear();
            meshQueue.Clear();
            priorityDirtyMeshes.Clear();
            priorityMeshQueue.Clear();
            destructionDirtyMeshes.Clear();
            for (int i = pendingOreTerrainReleases.Count - 1; i >= 0; i--)
            {
                PendingOreTerrainRelease pending =
                    pendingOreTerrainReleases[i];
                if (pending.Drop != null)
                {
                    oreDropsAwaitingPhysicsSync.Add(pending.Drop);
                }
            }
            pendingOreTerrainReleases.Clear();
            if (oreDropsAwaitingPhysicsSync.Count > 0)
            {
                Physics.SyncTransforms();
                ReleaseOreDropsAfterPhysicsSync();
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                LevelConfiguration selectedLevel =
                    ResolveConfiguredLevelConfiguration();
                bool configurationApplied =
                    worldGenerationConfigurationOverride != null
                        ? ApplyWorldGenerationConfiguration(
                            worldGenerationConfigurationOverride)
                        : ApplyLevelConfigurationForNewRun(selectedLevel);
                if (!configurationApplied)
                {
                    Debug.LogError(
                        "MinecraftCaveInfiniteWorld requires either a direct "
                        + "MinecraftWorldGenerationConfiguration or a valid "
                        + "LevelConfiguration from its configured level list "
                        + "before it can initialize.",
                        this);
                    enabled = false;
                    return;
                }
                if (overrideWorldSeed)
                {
                    worldSeed = worldSeedOverride;
                }
                ApplyPunctualLightFalloffParameters();
                InitializeWorld();
                ResetPublishedInitialLoadProgress();
                InstanceEnabled?.Invoke(this);
                PublishInitialLoadProgress();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeEvents()
        {
            InstanceEnabled = null;
            InstanceDisabled = null;
            lastGeneratedWorldSeed = 0;
        }

        private LevelConfiguration ResolveConfiguredLevelConfiguration()
        {
            LevelConfiguration activeLevel =
                MissionGameLoop.CurrentLevelConfiguration;
            if (levelConfigurations == null || levelConfigurations.Count == 0)
                return activeLevel;

            if (activeLevel != null)
            {
                for (int i = 0; i < levelConfigurations.Count; i++)
                {
                    LevelConfiguration candidate = levelConfigurations[i];
                    if (candidate == activeLevel
                        || candidate != null
                        && candidate.LevelNumber == activeLevel.LevelNumber)
                    {
                        return candidate;
                    }
                }
            }

            if (MissionProgressPersistence.TryLoadLevel(
                    levelConfigurations,
                    out LevelConfiguration savedLevel))
            {
                return savedLevel;
            }

            for (int i = 0; i < levelConfigurations.Count; i++)
            {
                if (levelConfigurations[i] != null)
                    return levelConfigurations[i];
            }
            return activeLevel;
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
                CommitDenseJigsawSelectionTask();
                RefreshStreamingForViewerMovement();
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
            ProcessNaturalMonsterSpawnSchedule();
            ProcessPendingMonsterSpawns();
            using (UpdateDestructionMarker.Auto())
            {
                ProcessChunkDestructions();
            }
            using (UpdateReadyMarker.Auto())
            {
                ReportReadyState();
            }
            using (UpdatePhysicsFinalizeMarker.Auto())
            {
                ProcessPendingColumnPhysics();
            }
            PublishInitialLoadProgress();
        }

        private void RefreshStreamingForViewerMovement()
        {
            if (UsesFixedGenerationArea
                || initialSpawnPlacementPending
                || waitingForExternalDensePortalEntry)
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
            if (!keepViewerTransformDuringGeneration)
            {
                SuspendGlobalGravityForInitialLoad();
            }
            world = new InfiniteVoxelWorld();
            densityField = new MinecraftCaveDensityField(worldSeed, settings);
            SnapshotVoxelGenerationSettings();
            generationCancellation = new CancellationTokenSource();
            ResolveViewer();
            naturalMonsterSpawningEnabled =
                generationDebugPass
                    == MinecraftWorldGenerationDebugPass.FullPipeline
                && !UsesExternalDenseLandingCell;
            waitingForExternalDensePortalEntry =
                IsInfiniteDenseWorld && UsesExternalDenseLandingCell;

            bool isSuperflat =
                generationMode == MinecraftWorldGenerationMode.Superflat;
            PlayerSpawnRequest authoredPlayerSpawn = default;
            bool hasAuthoredPlayerSpawn = !isSuperflat
                && HasDenseJigsawConfiguration
                && JigsawStructureGenerator.TryResolvePlayerSpawn(
                    densityField.Seed,
                    jigsawStructureSettings,
                    out authoredPlayerSpawn);
            spawnVoxel = isSuperflat
                ? new Vector3Int(0, SuperflatStoneHeight, 0)
                : hasAuthoredPlayerSpawn
                    ? authoredPlayerSpawn.VoxelPosition
                    : FindCaveSpawnVoxel();
            Vector3 spawnVoxelPosition =
                isSuperflat || HasDenseJigsawConfiguration
                    || UsesConfiguredFixedPreviewArea
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
                // The Dense landing Cell is authored directly in the scene.
                // Treat its player marker as the spawn pose without relocating
                // the Cell root during world initialization.
                Transform landingSpawn =
                    spawnPointSceneStructure.PlayerSpawnPoint;
                targetSpawnWorldPosition = landingSpawn.position;
                targetSpawnWorldRotation = landingSpawn.rotation;
            }
            if (!isSuperflat && !HasDenseJigsawConfiguration)
            {
                PlaceSpawnPointSceneStructure();
            }
            generationStage = MinecraftCaveGenerationStage.Terrain;
            structurePassApplied = false;
            initialSpawnPlacementPending = placeViewerInCave
                && viewer != null
                && !keepViewerTransformDuringGeneration;

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

                Vector3 streamingPosition = UsesExternalDenseLandingCell
                    ? authoredSpawnWorldPosition
                    : placeViewerInCave
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
                        SetGameplayVoxel(
                            coordinate,
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
                    SetGameplayVoxel(
                        coordinate,
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
            SetGameplayVoxel(
                coordinate,
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

            MinedOreDrop drop = CreateOreVeinBody(component, type, meshData);
            var oreAffectedMeshes = new HashSet<Vector3Int>();
            foreach (Vector3Int coordinate in component)
            {
                SetGameplayVoxel(
                    coordinate,
                    isoLevel - 1f,
                    VoxelTypeId.Air);
                miningProgress.Reset(coordinate);
                CollectMeshesAffectedByVoxel(
                    coordinate,
                    oreAffectedMeshes);
            }
            affectedMeshes.UnionWith(oreAffectedMeshes);
            if (Application.isPlaying)
            {
                RegisterOreTerrainRelease(drop, oreAffectedMeshes);
            }
            return component.Count;
        }

        private void RegisterOreTerrainRelease(
            MinedOreDrop drop,
            HashSet<Vector3Int> affectedMeshes)
        {
            if (drop == null || affectedMeshes == null
                || affectedMeshes.Count == 0)
            {
                return;
            }

            drop.SuspendForTerrainColliderRebuild();
            pendingOreTerrainReleases.Add(
                new PendingOreTerrainRelease(drop, affectedMeshes));
        }

        private void NotifyOreTerrainMeshRebuilt(Vector3Int coordinate)
        {
            for (int i = pendingOreTerrainReleases.Count - 1; i >= 0; i--)
            {
                PendingOreTerrainRelease pending =
                    pendingOreTerrainReleases[i];
                if (pending.Drop == null)
                {
                    pendingOreTerrainReleases.RemoveAt(i);
                    continue;
                }
                if (!pending.AffectedMeshes.Remove(coordinate)
                    || pending.AffectedMeshes.Count > 0)
                {
                    continue;
                }

                // Keep collision damage protection active until every rebuilt
                // terrain collider has also been synchronized into PhysX.
                oreDropsAwaitingPhysicsSync.Add(pending.Drop);
                pendingOreTerrainReleases.RemoveAt(i);
            }
        }

        private void ReleaseOreDropsAfterPhysicsSync()
        {
            foreach (MinedOreDrop drop in oreDropsAwaitingPhysicsSync)
            {
                if (drop != null)
                {
                    drop.ReleaseAfterTerrainColliderRebuild();
                }
            }
            oreDropsAwaitingPhysicsSync.Clear();
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

        internal MinedOreDrop CreateOreVeinBody(
            HashSet<Vector3Int> component,
            VoxelTypeId type,
            VoxelMeshData meshData,
            Transform coordinateFrame = null,
            Vector3 gridPivot = default)
        {
            coordinateFrame = coordinateFrame != null
                ? coordinateFrame
                : transform;
            VoxelTypeDefinition definition = voxelTypeCatalog != null
                ? voxelTypeCatalog.Find(type)
                : null;
            VoxelOreFeatureDefinition oreFeature = FindOreFeature(type);
            string displayName = definition != null
                ? definition.DisplayName
                : type.ToString();
            VoxelMeshMassProperties massProperties =
                VoxelIntegrityRigidbodyFactory.CalculateMassProperties(
                    meshData.Vertices,
                    meshData.Triangles);
            float representedFullVoxelVolume =
                VoxelIntegrityRigidbodyFactory
                    .CalculateRepresentedFullVoxelVolume(
                        massProperties,
                        voxelSize,
                        Vector3.one * MinedOreDrop.RecoveredLinearScale);
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
            // Keep the recovered mesh in the exact cavity frame. Scaling or
            // decorative rotation here would make the collider intersect the
            // freshly rebuilt terrain before its first physics step.
            dropObject.transform.SetPositionAndRotation(
                coordinateFrame.TransformPoint(meshCentre - gridPivot),
                coordinateFrame.rotation);
            dropObject.transform.localScale = Vector3.Scale(
                coordinateFrame.lossyScale,
                Vector3.one * MinedOreDrop.RecoveredLinearScale);


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
            Material recoveredMaterial = oreFeature != null
                && oreFeature.RecoveredMaterial != null
                    ? CloneRecoveredOreMaterial(
                        oreFeature.RecoveredMaterial,
                        sourceMaterial,
                        displayName)
                    : CreateRecoveredOreMaterial(
                        sourceMaterial,
                        displayName);
            DisableRecoveredOreSparkles(recoveredMaterial);
            renderer.sharedMaterial = recoveredMaterial;
            CrystalOreSparkleOverlay.Synchronize(
                renderer as MeshRenderer,
                mesh);
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
            RigidbodyImpactFeedback.Ensure(body);
            body.velocity = coordinateFrame.TransformDirection(
                escapeDirection * (0.45f + Mathf.Min(component.Count, 8) * 0.04f));
            var drop = dropObject.AddComponent<MinedOreDrop>();
            drop.Configure(
                type,
                component.Count,
                representedFullVoxelVolume,
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
            return drop;
        }

        private static void DisableRecoveredOreSparkles(Material material)
        {
            const string sparkleDensityProperty =
                "_DetailAlbedoMapScale";
            if (material != null
                && material.HasProperty(sparkleDensityProperty))
            {
                material.SetFloat(sparkleDensityProperty, 0f);
            }
        }

        private static Material CloneRecoveredOreMaterial(
            Material template,
            Material source,
            string displayName)
        {
            Material material = new Material(template)
            {
                name = $"Recovered {displayName} Material",
                hideFlags = HideFlags.DontSave,
            };
            OverrideRecoveredBaseMap(material, source);
            OverrideRecoveredCrystalProperties(material, source);
            return material;
        }

        private static void OverrideRecoveredCrystalProperties(
            Material recovered,
            Material source)
        {
            CopyRecoveredColor(recovered, source, "_BaseColor");
            CopyRecoveredColor(recovered, source, "_Color");
            CopyRecoveredColor(recovered, source, "_EmissionColor");
            CopyRecoveredFloat(recovered, source, "_ClearCoatMask");
            CopyRecoveredFloat(recovered, source, "_Metallic");
            CopyRecoveredFloat(recovered, source, "_Smoothness");
        }

        private static void CopyRecoveredColor(
            Material recovered,
            Material source,
            string property)
        {
            if (recovered.HasProperty(property) && source.HasProperty(property))
            {
                recovered.SetColor(property, source.GetColor(property));
            }
        }

        private static void CopyRecoveredFloat(
            Material recovered,
            Material source,
            string property)
        {
            if (recovered.HasProperty(property) && source.HasProperty(property))
            {
                recovered.SetFloat(property, source.GetFloat(property));
            }
        }

        private static void OverrideRecoveredBaseMap(
            Material recovered,
            Material source)
        {
            if (recovered == null || source == null)
            {
                return;
            }

            string sourceProperty = source.HasProperty("_BaseMap")
                ? "_BaseMap"
                : source.HasProperty("_MainTex")
                    ? "_MainTex"
                    : null;
            if (sourceProperty == null)
            {
                return;
            }

            Texture texture = source.GetTexture(sourceProperty);
            if (texture == null
                && sourceProperty == "_BaseMap"
                && source.HasProperty("_MainTex"))
            {
                sourceProperty = "_MainTex";
                texture = source.GetTexture(sourceProperty);
            }
            Vector2 scale = source.GetTextureScale(sourceProperty);
            Vector2 offset = source.GetTextureOffset(sourceProperty);
            ApplyBaseMapProperty(
                recovered,
                "_BaseMap",
                texture,
                scale,
                offset);
            ApplyBaseMapProperty(
                recovered,
                "_MainTex",
                texture,
                scale,
                offset);
        }

        private static void ApplyBaseMapProperty(
            Material material,
            string property,
            Texture texture,
            Vector2 scale,
            Vector2 offset)
        {
            if (!material.HasProperty(property))
            {
                return;
            }
            material.SetTexture(property, texture);
            material.SetTextureScale(property, scale);
            material.SetTextureOffset(property, offset);
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

        private Vector3 ResolveOreEscapeDirection(
            HashSet<Vector3Int> component)
        {
            Vector3 direction = Vector3.zero;
            Vector3Int[] neighbours =
            {
                Vector3Int.right, Vector3Int.left, Vector3Int.up,
                Vector3Int.down, Vector3Int.forward, Vector3Int.back,
            };
            var openFaceCounts = new int[neighbours.Length];
            foreach (Vector3Int coordinate in component)
            {
                for (int i = 0; i < neighbours.Length; i++)
                {
                    Vector3Int neighbour = coordinate + neighbours[i];
                    if (component.Contains(neighbour))
                    {
                        continue;
                    }

                    bool isOpen = world == null
                        || !world.TryGetSample(
                            neighbour.x,
                            neighbour.y,
                            neighbour.z,
                            out VoxelSample sample)
                        || !sample.IsSolid(isoLevel);
                    if (!isOpen)
                    {
                        continue;
                    }

                    openFaceCounts[i]++;
                    direction += (Vector3)neighbours[i];
                }
            }
            if (direction.sqrMagnitude >= 0.001f)
            {
                return direction.normalized;
            }

            int mostOpenFaceIndex = -1;
            for (int i = 0; i < openFaceCounts.Length; i++)
            {
                if (mostOpenFaceIndex < 0
                    || openFaceCounts[i] > openFaceCounts[mostOpenFaceIndex])
                {
                    mostOpenFaceIndex = i;
                }
            }
            return mostOpenFaceIndex >= 0
                && openFaceCounts[mostOpenFaceIndex] > 0
                    ? (Vector3)neighbours[mostOpenFaceIndex]
                    : Vector3.up;
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

        internal bool IsRecoverableOreType(VoxelTypeId type)
        {
            return FindOreFeature(type) != null;
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
            SetGameplayVoxel(coordinate, density, normalizedType);
            miningProgress.Reset(coordinate);
            QueueMeshesAffectedByVoxel(coordinate);
            return true;
        }

        private void SetGameplayVoxel(
            Vector3Int coordinate,
            float density,
            VoxelTypeId type)
        {
            world.SetVoxel(
                coordinate.x,
                coordinate.y,
                coordinate.z,
                density,
                type);
            if (!world.TryGetSample(
                    coordinate.x,
                    coordinate.y,
                    coordinate.z,
                    out VoxelSample current))
            {
                return;
            }

            Vector3Int column = InfiniteVoxelWorld.WorldToChunk(
                coordinate.x,
                coordinate.y,
                coordinate.z);
            if (!gameplayVoxelOverridesByColumn.TryGetValue(
                    column,
                    out Dictionary<Vector3Int, VoxelSample> overrides))
            {
                overrides = new Dictionary<Vector3Int, VoxelSample>();
                gameplayVoxelOverridesByColumn.Add(column, overrides);
            }
            if (!overrides.ContainsKey(coordinate))
            {
                gameplayVoxelOverrideCount++;
            }
            overrides[coordinate] = current;

            if (current.IsSolid(isoLevel))
            {
                gameplayCarvedVoxels.Remove(coordinate);
            }
            else
            {
                gameplayCarvedVoxels.Add(coordinate);
            }
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
            if (IsFiniteDenseRegion || UsesConfiguredFixedPreviewArea)
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
            pendingPhysicsColumns.Clear();
            renderingReadyLogged = false;
            generationStage = MinecraftCaveGenerationStage.Terrain;

            IReadOnlyList<Vector3Int> offsets = IsFiniteDenseRegion
                ? ResolveConfiguredDenseRegionOffsets()
                : UsesConfiguredFixedPreviewArea
                    ? ResolveConfiguredFixedPreviewOffsets()
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
                    || UsesConfiguredFixedPreviewArea
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

            if (!structurePassApplied
                && !fixedPreviewArea
                && !HasDenseJigsawConfiguration)
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
            CullVoxelDataOutsideRetentionRadius();
        }

        private void RefreshDenseJigsawPlacementSelection()
        {
            if (!HasDenseJigsawConfiguration
                || !denseJigsawRegionConfigurationOverride
                    .PreventStructureIntersections
                || jigsawStructureSettings == null
                || jigsawStructureSettings.Length == 0
                || requiredChunks.Count == 0)
            {
                CancelDenseJigsawSelectionTask();
                denseJigsawPlacementSelection = null;
                hasDenseJigsawSelectionWindow = false;
                return;
            }

            int minimumChunkX = int.MaxValue;
            int maximumChunkX = int.MinValue;
            int minimumChunkZ = int.MaxValue;
            int maximumChunkZ = int.MinValue;
            IReadOnlyCollection<Vector3Int> selectionColumns =
                IsFiniteDenseRegion
                    ? ResolveConfiguredDenseRegionOffsets()
                    : requiredChunks;
            foreach (Vector3Int coordinate in selectionColumns)
            {
                minimumChunkX = Math.Min(minimumChunkX, coordinate.x);
                maximumChunkX = Math.Max(maximumChunkX, coordinate.x);
                minimumChunkZ = Math.Min(minimumChunkZ, coordinate.z);
                maximumChunkZ = Math.Max(maximumChunkZ, coordinate.z);
            }

            if (hasDenseJigsawSelectionWindow
                && denseJigsawPlacementSelection != null
                && minimumChunkX >= denseJigsawSelectionMinimumChunkX
                && maximumChunkX <= denseJigsawSelectionMaximumChunkX
                && minimumChunkZ >= denseJigsawSelectionMinimumChunkZ
                && maximumChunkZ <= denseJigsawSelectionMaximumChunkZ)
            {
                CancelDenseJigsawSelectionTask();
                return;
            }

            if (denseJigsawSelectionTask != null
                && denseJigsawSelectionTask.Contains(
                    minimumChunkX,
                    maximumChunkX,
                    minimumChunkZ,
                    maximumChunkZ))
            {
                denseJigsawSelectionPending = true;
                return;
            }

            CancelDenseJigsawSelectionTask();
            int padding = IsFiniteDenseRegion
                ? 0
                : DenseJigsawSelectionPaddingInChunks;
            int selectionMinimumChunkX = minimumChunkX - padding;
            int selectionMaximumChunkX = maximumChunkX + padding;
            int selectionMinimumChunkZ = minimumChunkZ - padding;
            int selectionMaximumChunkZ = maximumChunkZ + padding;
            CancellationTokenSource cancellation =
                generationCancellation != null
                    ? CancellationTokenSource.CreateLinkedTokenSource(
                        generationCancellation.Token)
                    : new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            JigsawStructureFeatureSettings[] capturedSettings =
                jigsawStructureSettings;
            int capturedSeed =
                densityField != null ? densityField.Seed : worldSeed;
            Task<JigsawPlacementSelection> task = Task.Run(
                () =>
                {
                    using (DenseJigsawSelectionMarker.Auto())
                    {
                        return JigsawPlacementSelection
                            .CreateNonIntersecting(
                                capturedSettings,
                                capturedSeed,
                                selectionMinimumChunkX
                                    * VoxelColumnChunkData.Width,
                                selectionMinimumChunkZ
                                    * VoxelColumnChunkData.Depth,
                                (selectionMaximumChunkX + 1)
                                    * VoxelColumnChunkData.Width - 1,
                                (selectionMaximumChunkZ + 1)
                                    * VoxelColumnChunkData.Depth - 1,
                                token);
                    }
                },
                token);
            denseJigsawSelectionTask =
                new DenseJigsawSelectionTaskHandle(
                    task,
                    cancellation,
                    selectionMinimumChunkX,
                    selectionMaximumChunkX,
                    selectionMinimumChunkZ,
                    selectionMaximumChunkZ);
            denseJigsawSelectionPending = true;
            CancelGenerationTasksForDenseJigsawSelection();
        }

        private bool CommitDenseJigsawSelectionTask()
        {
            DenseJigsawSelectionTaskHandle handle =
                denseJigsawSelectionTask;
            if (handle == null || !handle.Task.IsCompleted)
            {
                return false;
            }

            denseJigsawSelectionTask = null;
            denseJigsawSelectionPending = false;
            try
            {
                if (handle.Task.IsCanceled)
                {
                    denseJigsawSelectionPending =
                        generationCancellation != null
                        && !generationCancellation.IsCancellationRequested;
                    return false;
                }
                if (handle.Task.IsFaulted)
                {
                    Debug.LogException(
                        handle.Task.Exception?.GetBaseException()
                            ?? new InvalidOperationException(
                                "Dense jigsaw selection task failed."),
                        this);
                    denseJigsawSelectionPending =
                        generationCancellation != null
                        && !generationCancellation.IsCancellationRequested;
                    return false;
                }

                using (DenseJigsawCommitMarker.Auto())
                {
                    denseJigsawPlacementSelection = handle.Task.Result;
                    denseJigsawSelectionMinimumChunkX =
                        handle.MinimumChunkX;
                    denseJigsawSelectionMaximumChunkX =
                        handle.MaximumChunkX;
                    denseJigsawSelectionMinimumChunkZ =
                        handle.MinimumChunkZ;
                    denseJigsawSelectionMaximumChunkZ =
                        handle.MaximumChunkZ;
                    hasDenseJigsawSelectionWindow = true;
                    denseJigsawSelectionRebuildCount++;
                }
                return true;
            }
            finally
            {
                handle.Dispose();
            }
        }


        private void CancelDenseJigsawSelectionTask()
        {
            DenseJigsawSelectionTaskHandle handle =
                denseJigsawSelectionTask;
            denseJigsawSelectionTask = null;
            denseJigsawSelectionPending = false;
            if (handle == null)
            {
                return;
            }

            handle.Cancel();
            handle.Dispose();
        }

        private void CancelGenerationTasksForDenseJigsawSelection()
        {
            foreach (GenerationTaskHandle handle in generationTasks.Values)
            {
                handle.Cancel();
                handle.Dispose();
            }
            generationTasks.Clear();
            completedGenerationCoordinates.Clear();
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
            if (denseJigsawSelectionPending)
            {
                return;
            }

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
                MinecraftOreFeatureSettings[] features = generationDebugPass
                    .Includes(MinecraftWorldGenerationDebugPass.OreGeneration)
                        ? oreFeatureSettings
                        : Array.Empty<MinecraftOreFeatureSettings>();
                MinecraftStructureFeatureSettings[] structures =
                    generationDebugPass.Includes(
                        MinecraftWorldGenerationDebugPass.JigsawStructures)
                            ? structureFeatureSettings
                            : Array.Empty<MinecraftStructureFeatureSettings>();
                JigsawStructureFeatureSettings[] jigsaws =
                    generationDebugPass.Includes(
                        MinecraftWorldGenerationDebugPass.JigsawStructures)
                            ? jigsawStructureSettings
                            : Array.Empty<JigsawStructureFeatureSettings>();
                JigsawPlacementSelection jigsawSelection =
                    generationDebugPass.Includes(
                        MinecraftWorldGenerationDebugPass.JigsawStructures)
                            ? denseJigsawPlacementSelection
                            : null;
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
            ApplyGameplayVoxelOverrides(
                result.Coordinate,
                result.Densities,
                result.Types);
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



        private void ApplyGameplayVoxelOverrides(
            Vector3Int column,
            float[] densities,
            VoxelTypeId[] types)
        {
            if (!gameplayVoxelOverridesByColumn.TryGetValue(
                    column,
                    out Dictionary<Vector3Int, VoxelSample> overrides))
            {
                return;
            }

            foreach (KeyValuePair<Vector3Int, VoxelSample> pair in overrides)
            {
                Vector3Int local = InfiniteVoxelWorld.WorldToLocal(
                    pair.Key.x,
                    pair.Key.y,
                    pair.Key.z,
                    column);
                int index = VoxelColumnChunkData.ToIndex(
                    local.x,
                    local.y,
                    local.z);
                densities[index] = pair.Value.Density;
                types[index] = pair.Value.Type;
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

        // Player edits only mark and enqueue work here. Snapshot capture, mesh
        // generation, and Unity object updates are handled later by ProcessMeshes,
        // keeping those costs out of the interaction call stack.
        private void EnqueuePriorityMesh(Vector3Int coordinate)
        {
            IncrementMeshBuildVersion(coordinate);
            // Drop any pending low-priority entry; the priority pass supersedes it.
            dirtyMeshes.Remove(coordinate);
            if (priorityDirtyMeshes.Add(coordinate))
            {
                priorityMeshQueue.Enqueue(coordinate);
            }
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
        }

        private void ProcessMeshes(int budget)
        {
            bool canProcessStreamingMeshes =
                generationStage == MinecraftCaveGenerationStage.Meshes
                || generationStage == MinecraftCaveGenerationStage.Ready
                || structurePassApplied;

            meshCommitStopwatch.Restart();
            using (UpdateMeshPostProcessMarker.Auto())
            {
                ProcessPendingMeshPostProcesses(
                    budget,
                    meshCommitBudgetMilliseconds);
            }
            using (UpdateMeshCommitMarker.Auto())
            {
                CommitCompletedMeshTasks(
                    budget,
                    canProcessStreamingMeshes,
                    meshCommitBudgetMilliseconds);
            }
            meshCommitStopwatch.Stop();

            meshSnapshotStopwatch.Restart();
            using (UpdateMeshSnapshotMarker.Auto())
            {
                DispatchMeshTasks(
                    meshSnapshotsCapturedPerFrame,
                    canProcessStreamingMeshes,
                    meshSnapshotBudgetMilliseconds);
            }
            meshSnapshotStopwatch.Stop();
        }
        private void CommitCompletedMeshTasks(
            int budget,
            bool includeStreamingMeshes,
            float timeBudgetMilliseconds)
        {
            if (meshTasks.Count == 0
                || budget <= 0
                || meshCommitStopwatch.Elapsed.TotalMilliseconds
                    >= timeBudgetMilliseconds)
            {
                return;
            }

            completedMeshCoordinates.Clear();
            CollectCompletedMeshTasks(budget, true);
            if (includeStreamingMeshes
                && completedMeshCoordinates.Count < budget)
            {
                CollectCompletedMeshTasks(budget, false);
            }

            int processed = 0;
            foreach (Vector3Int coordinate in completedMeshCoordinates)
            {
                if (processed > 0
                    && meshCommitStopwatch.Elapsed.TotalMilliseconds
                        >= timeBudgetMilliseconds)
                {
                    break;
                }
                processed++;

                Task<MeshGenerationResult> task = meshTasks[coordinate];
                meshTasks.Remove(coordinate);
                bool wasPriority = priorityMeshTasks.Remove(coordinate);
                if (task.IsCanceled)
                {
                    continue;
                }
                if (task.IsFaulted)
                {
                    Debug.LogException(
                        task.Exception?.GetBaseException()
                            ?? new InvalidOperationException(
                                "Mesh generation task failed."),
                        this);
                    Vector3Int columnCoordinate = ToColumnCoordinate(coordinate);
                    if (world.TryGetChunk(columnCoordinate, out _))
                    {
                        if (wasPriority)
                        {
                            EnqueuePriorityMesh(coordinate);
                        }
                        else if (requiredChunks.Contains(columnCoordinate))
                        {
                            QueueMesh(coordinate, true);
                        }
                    }
                    continue;
                }

                MeshGenerationResult result = task.Result;
                Vector3Int resultColumn = ToColumnCoordinate(coordinate);
                bool isStillLoaded = world.TryGetChunk(resultColumn, out _);
                if (!isStillLoaded
                    || (!wasPriority && !requiredChunks.Contains(resultColumn))
                    || dirtyMeshes.Contains(coordinate)
                    || priorityDirtyMeshes.Contains(coordinate)
                    || GetMeshBuildVersion(coordinate) != result.Version)
                {
                    result.Dispose();
                    continue;
                }

                applyingSurfaceBuildResult =
                    result.TakeSurfaceBuildResult();
                try
                {
                    ApplyChunkMeshData(coordinate, result.Data);
                }
                finally
                {
                    applyingSurfaceBuildResult?.Dispose();
                    applyingSurfaceBuildResult = null;
                    result.Dispose();
                }
            }
        }

        private void CollectCompletedMeshTasks(int budget, bool priority)
        {
            foreach (KeyValuePair<Vector3Int, Task<MeshGenerationResult>> pair
                in meshTasks)
            {
                if (!pair.Value.IsCompleted
                    || priorityMeshTasks.Contains(pair.Key) != priority)
                {
                    continue;
                }

                completedMeshCoordinates.Add(pair.Key);
                if (completedMeshCoordinates.Count >= budget)
                {
                    break;
                }
            }
        }

        private void DispatchMeshTasks(
            int captureBudget,
            bool includeStreamingMeshes,
            float timeBudgetMilliseconds)
        {
            int priorityCandidates = priorityMeshQueue.Count;
            int streamingCandidates = includeStreamingMeshes
                ? meshQueue.Count
                : 0;
            int attempted = 0;
            int captured = 0;
            while (captured < captureBudget
                && meshTasks.Count < maxConcurrentMeshJobs)
            {
                if (attempted > 0
                    && meshSnapshotStopwatch.Elapsed.TotalMilliseconds
                        >= timeBudgetMilliseconds)
                {
                    break;
                }
                attempted++;

                if (!TryDequeueMeshCandidate(
                        ref priorityCandidates,
                        ref streamingCandidates,
                        out Vector3Int coordinate,
                        out bool isPriority))
                {
                    break;
                }

                Vector3Int columnCoordinate = ToColumnCoordinate(coordinate);
                if ((!isPriority && !requiredChunks.Contains(columnCoordinate))
                    || !world.TryGetChunk(columnCoordinate, out _))
                {
                    RemoveDirtyMesh(coordinate, isPriority);
                    continue;
                }
                if (!IsMeshSnapshotNeighborhoodReady(columnCoordinate))
                {
                    RequeueMeshCandidate(coordinate, isPriority);
                    continue;
                }

                RemoveDirtyMesh(coordinate, isPriority);
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
                CaveSurfaceGenerationSnapshot capturedSurfaceGeneration =
                    caveSurfaceGenerationSnapshot;
                CaveSurfaceSampleSnapshot surfaceSamples = null;
                try
                {
                    if (capturedSurfaceGeneration != null
                        && capturedSurfaceGeneration.HasBrushes)
                    {
                        surfaceSamples = CaveSurfaceSampleSnapshot.Capture(
                            world,
                            columnCoordinate,
                            startY,
                            MeshSectionHeight);
                    }
                }
                catch
                {
                    ArrayPool<VoxelSample>.Shared.Return(samples);
                    throw;
                }

                ISet<Vector3Int> capturedCarvedVoxels =
                    CaptureCarvedVoxelsForMeshSection(
                        columnCoordinate,
                        startY);
                int version = GetMeshBuildVersion(coordinate);
                float capturedIsoLevel = isoLevel;
                float capturedVoxelSize = voxelSize;
                MarchingCubesVertexPlacement capturedVertexPlacement =
                    vertexPlacement;
                VoxelGroupMap capturedGroupMap = voxelGroupMap;
                int capturedWorldSeed = worldSeed;
                if (isPriority)
                {
                    priorityMeshTasks.Add(coordinate);
                }
                meshTasks.Add(
                    coordinate,
                    Task.Run(
                        () =>
                        {
                            try
                            {
                                VoxelMeshData data = MarchingCubesMesher
                                    .BuildCapturedColumnSectionPooled(
                                        samples,
                                        MeshSectionHeight,
                                        capturedIsoLevel,
                                        capturedVoxelSize,
                                        capturedVertexPlacement,
                                        capturedGroupMap);
                                CaveSurfaceBuildResult surfaceBuildResult = null;
                                try
                                {
                                    surfaceBuildResult =
                                        CaveSurfaceBuildResult.Build(
                                            data,
                                            coordinate,
                                            startY,
                                            capturedVoxelSize,
                                            capturedIsoLevel,
                                            capturedWorldSeed,
                                            capturedGroupMap,
                                            capturedSurfaceGeneration,
                                            surfaceSamples,
                                            capturedCarvedVoxels);
                                    return new MeshGenerationResult(
                                        coordinate,
                                        version,
                                        data,
                                        surfaceBuildResult);
                                }
                                catch
                                {
                                    surfaceBuildResult?.Dispose();
                                    data.Dispose();
                                    throw;
                                }
                            }
                            finally
                            {
                                ArrayPool<VoxelSample>.Shared.Return(samples);
                                surfaceSamples?.Dispose();
                            }
                        }));
                captured++;
            }
        }

        private ISet<Vector3Int> CaptureCarvedVoxelsForMeshSection(
            Vector3Int columnCoordinate,
            int startY)
        {
            if (gameplayCarvedVoxels.Count == 0)
            {
                return null;
            }

            int minimumX =
                columnCoordinate.x * VoxelColumnChunkData.Width - 2;
            int maximumX = minimumX
                + VoxelColumnChunkData.Width + 4;
            int minimumY = startY - 2;
            int maximumY = startY + MeshSectionHeight + 2;
            int minimumZ =
                columnCoordinate.z * VoxelColumnChunkData.Depth - 2;
            int maximumZ = minimumZ
                + VoxelColumnChunkData.Depth + 4;
            HashSet<Vector3Int> captured = null;
            foreach (Vector3Int coordinate in gameplayCarvedVoxels)
            {
                if (coordinate.x < minimumX
                    || coordinate.x > maximumX
                    || coordinate.y < minimumY
                    || coordinate.y > maximumY
                    || coordinate.z < minimumZ
                    || coordinate.z > maximumZ)
                {
                    continue;
                }

                if (captured == null)
                {
                    captured = new HashSet<Vector3Int>();
                }
                captured.Add(coordinate);
            }
            return captured;
        }

        private bool TryDequeueMeshCandidate(
            ref int priorityCandidates,
            ref int streamingCandidates,
            out Vector3Int coordinate,
            out bool isPriority)
        {
            while (priorityCandidates-- > 0)
            {
                coordinate = priorityMeshQueue.Dequeue();
                if (!priorityDirtyMeshes.Contains(coordinate))
                {
                    continue;
                }
                if (meshTasks.ContainsKey(coordinate))
                {
                    priorityMeshQueue.Enqueue(coordinate);
                    continue;
                }

                isPriority = true;
                return true;
            }

            while (streamingCandidates-- > 0)
            {
                coordinate = meshQueue.Dequeue();
                if (!dirtyMeshes.Contains(coordinate))
                {
                    continue;
                }
                if (meshTasks.ContainsKey(coordinate))
                {
                    meshQueue.Enqueue(coordinate);
                    continue;
                }

                isPriority = false;
                return true;
            }

            coordinate = default;
            isPriority = false;
            return false;
        }

        private void RequeueMeshCandidate(
            Vector3Int coordinate,
            bool isPriority)
        {
            if (isPriority)
            {
                priorityMeshQueue.Enqueue(coordinate);
            }
            else
            {
                meshQueue.Enqueue(coordinate);
            }
        }

        private void RemoveDirtyMesh(Vector3Int coordinate, bool isPriority)
        {
            if (isPriority)
            {
                priorityDirtyMeshes.Remove(coordinate);
            }
            else
            {
                dirtyMeshes.Remove(coordinate);
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
            try
            {
                ApplyChunkMeshData(coordinate, data);
            }
            finally
            {
                data.Dispose();
            }
        }

        private void ApplyChunkMeshData(
            Vector3Int coordinate,
            VoxelMeshData data)
        {
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
            }
            if (data.Vertices.Count == 0)
            {
                applyingSurfaceBuildResult?.Dispose();
                applyingSurfaceBuildResult = null;
                DestroyChunkObject(coordinate, false);
                FinalizeColumnPhysicsIfReady(columnCoordinate);
                return;
            }

            string meshName =
                $"Minecraft Cave Column {coordinate.x},{coordinate.z} "
                + $"Section {section}";
            bool reuseChunkObject = chunkObjects.TryGetValue(
                    coordinate,
                    out GameObject chunkObject)
                && chunkObject != null;
            if (!reuseChunkObject)
            {
                chunkObject = AcquireChunkObject(
                    $"CaveColumn_{coordinate.x}_{coordinate.z}_Section_{section}");
            }
            chunkObject.transform.localPosition = new Vector3(
                coordinate.x * VoxelColumnChunkData.Width,
                startY,
                coordinate.z * VoxelColumnChunkData.Depth) * voxelSize;

            MeshFilter filter = chunkObject.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = chunkObject.AddComponent<MeshFilter>();
            }
            MeshRenderer renderer = chunkObject.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = chunkObject.AddComponent<MeshRenderer>();
            }
            MeshCollider collider = chunkObject.GetComponent<MeshCollider>();

            if (reuseChunkObject)
            {
                ClearChunkSurfaceContent(coordinate, chunkObject.transform);
            }

            // A queued request may still refer to the current render mesh. Cancel it
            // before that mesh is reused for a newer version.
            CancelPendingMeshPostProcess(coordinate);

            chunkMeshes.TryGetValue(coordinate, out Mesh currentRenderMesh);
            if (currentRenderMesh == null)
            {
                currentRenderMesh = filter.sharedMesh;
            }

            Mesh currentColliderMesh = collider != null
                ? collider.sharedMesh
                : null;
            // A render mesh that has not reached the collider is already the spare
            // side of the double buffer and can be overwritten safely. Once render
            // and collision share a mesh, build into another instance so the old
            // collision remains valid until the deferred PhysX cook is committed.
            Mesh mesh = currentRenderMesh != null
                && currentRenderMesh != currentColliderMesh
                    ? currentRenderMesh
                    : AcquireChunkMesh(meshName);
            try
            {
                data.ApplyToMesh(mesh, meshName);
                mesh.hideFlags = HideFlags.DontSave;
            }
            catch
            {
                if (mesh != currentRenderMesh)
                {
                    ReleaseChunkMesh(mesh);
                }
                throw;
            }
            filter.sharedMesh = mesh;
            IReadOnlyList<VoxelTypeDefinition> voxelDefinitions =
                voxelTypeCatalog != null
                    ? voxelTypeCatalog.Definitions
                    : null;
            renderer.sharedMaterials = VoxelTypeUtility.ResolveMaterials(
                data,
                EnsureMaterial(),
                voxelDefinitions);
            CrystalOreSparkleOverlay.Synchronize(renderer, mesh);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            if (!generateColliders && collider != null)
            {
                Mesh obsoleteColliderMesh = collider.sharedMesh;
                collider.enabled = false;
                collider.sharedMesh = null;
                if (obsoleteColliderMesh != null
                    && obsoleteColliderMesh != mesh)
                {
                    ReleaseChunkMesh(obsoleteColliderMesh);
                }
            }

            chunkObject.SetActive(true);
            ApplyDebugPresentationVisibility(chunkObject);
            chunkObjects[coordinate] = chunkObject;
            chunkMeshes[coordinate] = mesh;
            QueueMeshPostProcess(coordinate, data, mesh);
            applyingSurfaceBuildResult = null;
        }

        private void QueueMeshPostProcess(
            Vector3Int coordinate,
            VoxelMeshData data,
            Mesh preparedMesh)
        {
            CancelPendingMeshPostProcess(coordinate);
            var request = new MeshPostProcessRequest(
                coordinate,
                GetMeshBuildVersion(coordinate),
                generateColliders
                    ? MeshPostProcessStage.Collider
                    : MeshPostProcessStage.Surface,
                data,
                preparedMesh,
                applyingSurfaceBuildResult);
            pendingMeshPostProcesses[coordinate] = request;
            meshPostProcessQueue.Enqueue(request);

            if (!generateColliders)
            {
                NotifyOreTerrainMeshRebuilt(coordinate);
                FinalizeColumnPhysicsIfReady(ToColumnCoordinate(coordinate));
            }
        }

        private void ProcessPendingMeshPostProcesses(
            int budget,
            float timeBudgetMilliseconds)
        {
            if (budget <= 0 || meshPostProcessQueue.Count == 0)
            {
                return;
            }

            int attempts = 0;
            int processed = 0;
            while (meshPostProcessQueue.Count > 0 && processed < budget)
            {
                if (attempts > 0
                    && meshCommitStopwatch.Elapsed.TotalMilliseconds
                        >= timeBudgetMilliseconds)
                {
                    break;
                }
                attempts++;

                MeshPostProcessRequest request =
                    meshPostProcessQueue.Dequeue();
                if (request == null || request.IsCanceled)
                {
                    continue;
                }
                if (!pendingMeshPostProcesses.TryGetValue(
                        request.Coordinate,
                        out MeshPostProcessRequest currentRequest)
                    || !ReferenceEquals(request, currentRequest))
                {
                    request.Cancel();
                    continue;
                }
                if (GetMeshBuildVersion(request.Coordinate) != request.Version
                    || !chunkObjects.TryGetValue(
                        request.Coordinate,
                        out GameObject chunkObject)
                    || chunkObject == null
                    || !chunkMeshes.TryGetValue(
                        request.Coordinate,
                        out Mesh mesh)
                    || mesh == null
                    || request.PreparedMesh == null
                    || request.PreparedMesh != mesh)
                {
                    CompleteMeshPostProcess(request);
                    continue;
                }

                processed++;
                if (request.Stage == MeshPostProcessStage.Collider)
                {
                    ProcessMeshColliderPostProcess(
                        request,
                        chunkObject,
                        mesh);
                }
                else
                {
                    ProcessMeshSurfacePostProcess(
                        request,
                        chunkObject.transform);
                }
            }
        }

        private void ProcessMeshColliderPostProcess(
            MeshPostProcessRequest request,
            GameObject chunkObject,
            Mesh mesh)
        {
            using (MeshColliderPostProcessMarker.Auto())
            {
                MeshCollider collider =
                    chunkObject.GetComponent<MeshCollider>();
                if (collider == null)
                {
                    collider = chunkObject.AddComponent<MeshCollider>();
                }
                collider.sharedMaterial = terrainPhysicsMaterial;
                Mesh previousColliderMesh = collider.sharedMesh;
                collider.sharedMesh = mesh;
                collider.enabled = true;
                if (previousColliderMesh != null
                    && previousColliderMesh != mesh)
                {
                    ReleaseChunkMesh(previousColliderMesh);
                }

                request.Stage = MeshPostProcessStage.Surface;
                meshPostProcessQueue.Enqueue(request);
                NotifyOreTerrainMeshRebuilt(request.Coordinate);
                FinalizeColumnPhysicsIfReady(
                    ToColumnCoordinate(request.Coordinate));
            }
        }

        private void ProcessMeshSurfacePostProcess(
            MeshPostProcessRequest request,
            Transform chunkTransform)
        {
            try
            {
                CaveSurfaceBuildResult surfaceBuildResult =
                    request.SurfaceBuildResult;
                if (surfaceBuildResult != null)
                {
                    using (MeshSurfaceUploadMarker.Auto())
                    {
                        SpawnPreparedTerrainSurfaceLayer(
                            chunkTransform,
                            request.Coordinate,
                            surfaceBuildResult.TerrainLayer);
                    }
                    using (MeshSurfaceObjectsMarker.Auto())
                    {
                        SpawnSurfacePlacements(
                            chunkTransform,
                            request.Coordinate,
                            surfaceBuildResult.Placements);
                    }
                }
                else
                {
                    int section = Mathf.Clamp(
                        request.Coordinate.y,
                        0,
                        EffectiveMeshSectionsPerColumn - 1);
                    int startY = section * MeshSectionHeight;
                    IReadOnlyList<VoxelTypeDefinition> voxelDefinitions =
                        voxelTypeCatalog != null
                            ? voxelTypeCatalog.Definitions
                            : null;
                    using (MeshSurfaceUploadMarker.Auto())
                    {
                        SpawnTerrainSurfaceLayer(
                            chunkTransform,
                            request.Coordinate,
                            startY,
                            request.Data,
                            voxelDefinitions);
                    }
                    using (MeshSurfaceObjectsMarker.Auto())
                    {
                        SpawnSurfaceContent(
                            chunkTransform,
                            request.Coordinate,
                            startY,
                            request.Data);
                    }
                }

            }
            finally
            {
                CompleteMeshPostProcess(request);
            }
        }

        private void CompleteMeshPostProcess(
            MeshPostProcessRequest request)
        {
            if (request == null)
            {
                return;
            }
            if (pendingMeshPostProcesses.TryGetValue(
                    request.Coordinate,
                    out MeshPostProcessRequest currentRequest)
                && ReferenceEquals(request, currentRequest))
            {
                pendingMeshPostProcesses.Remove(request.Coordinate);
            }
            request.Cancel();
        }

        private void CancelPendingMeshPostProcess(Vector3Int coordinate)
        {
            if (!pendingMeshPostProcesses.TryGetValue(
                    coordinate,
                    out MeshPostProcessRequest request))
            {
                return;
            }

            pendingMeshPostProcesses.Remove(coordinate);
            request.Cancel();
        }

        private bool HasPendingColliderPostProcess(Vector3Int column)
        {
            for (int section = 0;
                section < EffectiveMeshSectionsPerColumn;
                section++)
            {
                var coordinate = new Vector3Int(
                    column.x,
                    section,
                    column.z);
                if (pendingMeshPostProcesses.TryGetValue(
                        coordinate,
                        out MeshPostProcessRequest request)
                    && request.Stage == MeshPostProcessStage.Collider)
                {
                    return true;
                }
            }
            return false;
        }

        private bool HasPendingColliderPostProcessesForRequiredChunks()
        {
            foreach (KeyValuePair<Vector3Int, MeshPostProcessRequest> pair
                in pendingMeshPostProcesses)
            {
                if (pair.Value.Stage == MeshPostProcessStage.Collider
                    && requiredChunks.Contains(
                        ToColumnCoordinate(pair.Key)))
                {
                    return true;
                }
            }
            return false;
        }

        private void ClearChunkSurfaceContent(
            Vector3Int coordinate,
            Transform chunkTransform)
        {
            for (int childIndex = chunkTransform.childCount - 1;
                childIndex >= 0;
                childIndex--)
            {
                Transform child = chunkTransform.GetChild(childIndex);
                if (child.name != TerrainSurfaceLayerObjectName
                    && child.name != SurfaceContentObjectName)
                {
                    continue;
                }

                child.gameObject.SetActive(false);
                DestroyGeneratedObject(child.gameObject);
            }

            if (terrainSurfaceMeshes.TryGetValue(
                    coordinate,
                    out Mesh terrainSurfaceMesh))
            {
                DestroyGeneratedObject(terrainSurfaceMesh);
                terrainSurfaceMeshes.Remove(coordinate);
            }
        }

        private void SpawnPreparedTerrainSurfaceLayer(
            Transform sectionTransform,
            Vector3Int coordinate,
            CaveTerrainSurfaceLayerData surfaceLayerData)
        {
            if (sectionTransform == null || surfaceLayerData == null)
            {
                return;
            }

            Mesh surfaceMesh = surfaceLayerData.CreateMesh(
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

            var surfaceObject = new GameObject(
                TerrainSurfaceLayerObjectName);
            surfaceObject.hideFlags = HideFlags.DontSave;
            surfaceObject.transform.SetParent(sectionTransform, false);
            MeshFilter surfaceFilter =
                surfaceObject.AddComponent<MeshFilter>();
            MeshRenderer surfaceRenderer =
                surfaceObject.AddComponent<MeshRenderer>();
            surfaceFilter.sharedMesh = surfaceMesh;
            surfaceRenderer.sharedMaterial = surfaceMaterial;
            surfaceRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            surfaceRenderer.receiveShadows = true;
            terrainSurfaceMeshes[coordinate] = surfaceMesh;
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

            var surfaceObject = new GameObject(
                TerrainSurfaceLayerObjectName);
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
            SpawnSurfacePlacements(
                sectionTransform,
                coordinate,
                placements);
        }

        private void SpawnSurfacePlacements(
            Transform sectionTransform,
            Vector3Int coordinate,
            IReadOnlyList<CaveSurfacePlacement> placements)
        {
            if (sectionTransform == null
                || placements == null
                || placements.Count == 0)
            {
                return;
            }

            var contentRootObject = new GameObject(SurfaceContentObjectName);
            contentRootObject.hideFlags = HideFlags.DontSave;
            Transform contentRoot = contentRootObject.transform;
            contentRoot.SetParent(sectionTransform, false);
            instancedSurfacePlacementBuffer.Clear();
            try
            {
                for (int i = 0; i < placements.Count; i++)
                {
                    CaveSurfacePlacement placement = placements[i];
                    CaveSurfaceBrushDefinition brush = placement.Brush;
                    if (brush == null)
                    {
                        continue;
                    }
                    if (brush.RenderMode ==
                        CaveSurfaceBrushRenderMode.InstancedMesh)
                    {
                        instancedSurfacePlacementBuffer.Add(placement);
                        continue;
                    }

                    GameObject prefab = brush.Prefab;
                    if (prefab == null)
                    {
                        continue;
                    }

                    GameObject instance = Instantiate(
                        prefab,
                        contentRoot,
                        false);
                    instance.hideFlags = HideFlags.DontSave;
                    instance.name = prefab.name;
                    Transform instanceTransform = instance.transform;
                    Vector3 prefabScale = instanceTransform.localScale;
                    instanceTransform.localPosition =
                        placement.LocalPosition;
                    instanceTransform.localRotation =
                        placement.LocalRotation;
                    instanceTransform.localScale = Vector3.Scale(
                        prefabScale,
                        placement.Scale);

                    VoxelSurfaceAttachment attachment =
                        instance.GetComponent<VoxelSurfaceAttachment>();
                    if (attachment == null)
                    {
                        attachment =
                            instance.AddComponent<VoxelSurfaceAttachment>();
                    }
                    attachment.Configure(
                        placement.AnchorVoxel,
                        coordinate,
                        placement.Biome,
                        brush);
                }

                if (instancedSurfacePlacementBuffer.Count > 0)
                {
                    CaveSurfaceInstanceRenderer instanceRenderer =
                        contentRootObject.AddComponent<
                            CaveSurfaceInstanceRenderer>();
                    instanceRenderer.Configure(
                        instancedSurfacePlacementBuffer);
                }
            }
            finally
            {
                instancedSurfacePlacementBuffer.Clear();
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
            if (!HasBuiltAllColumnSections(column)
                || HasPendingColliderPostProcess(column))
            {
                return;
            }

            // Collider cooking can be deferred from the mesh upload. Queue the
            // column only after every non-empty section has finished that stage.
            pendingPhysicsColumns.Add(column);
        }

        private void ProcessPendingColumnPhysics()
        {
            bool canFinalizeColumns =
                generationStage == MinecraftCaveGenerationStage.Ready
                && pendingPhysicsColumns.Count > 0;
            bool hasOreDropsAwaitingSync =
                oreDropsAwaitingPhysicsSync.Count > 0;
            if (!canFinalizeColumns && !hasOreDropsAwaitingSync)
            {
                return;
            }

            pendingPhysicsColumnBuffer.Clear();
            if (canFinalizeColumns)
            {
                foreach (Vector3Int column in pendingPhysicsColumns)
                {
                    if (requiredChunks.Contains(column)
                        && HasBuiltAllColumnSections(column))
                    {
                        pendingPhysicsColumnBuffer.Add(column);
                    }
                }
                pendingPhysicsColumns.Clear();
            }

            if (pendingPhysicsColumnBuffer.Count == 0
                && !hasOreDropsAwaitingSync)
            {
                return;
            }

            Physics.SyncTransforms();
            ReleaseOreDropsAfterPhysicsSync();
            for (int i = 0; i < pendingPhysicsColumnBuffer.Count; i++)
            {
                Vector3Int column = pendingPhysicsColumnBuffer[i];
                SpawnStructureMarkers(column);
                SpawnCheckpoints(column);
                SpawnPendingTreasures(column);
                ResumeBodiesInColumn(column);
            }
            pendingPhysicsColumnBuffer.Clear();
        }



        /// <summary>
        /// Instantiates the authored spawn markers of every structure piece that
        /// reaches into this column. Marker positions are resolved from the cached
        /// layout, so they are deterministic and independent of streaming order.
        /// </summary>
        private void SpawnStructureMarkers(Vector3Int column)
        {
            if (!generationDebugPass.Includes(
                    MinecraftWorldGenerationDebugPass.MarkerObjects))
            {
                return;
            }
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
            if (!generationDebugPass.Includes(
                    MinecraftWorldGenerationDebugPass.MarkerObjects))
            {
                return;
            }
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
            activeCheckpointObjects.Add(checkpoint);

            Rigidbody body = checkpoint.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = checkpoint.AddComponent<Rigidbody>();
            }
            body.isKinematic = true;
            body.useGravity = false;
            body.detectCollisions = true;
            RigidbodyImpactFeedback.Ensure(body);

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
            ApplyDebugPresentationVisibility(checkpoint);
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
            if (request.Kind != StructureSpawnMarkerDefinition.Kind.Treasure)
            {
                return;
            }
            if (!TryResolveMarkerPosition(request, out Vector3 localPosition))
            {
                return;
            }

            TreasureDefinition treasure = request.TreasureSelection
                    == StructureSpawnMarkerDefinition
                        .TreasureSelectionMode.WeightedWorldTable
                ? treasureSpawnTable != null
                    ? treasureSpawnTable.SelectWeighted(
                        request.TreasureSelectionRoll)
                    : null
                : request.Treasure;
            SpawnTreasure(treasure, localPosition, request.Yaw);
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
            TrySpawnNaturalTreasures(column);
        }

        private void SpawnAllPendingTreasures()
        {
            var columns = new List<Vector3Int>(pendingTreasureColumns);
            for (int i = 0; i < columns.Count; i++)
            {
                if (HasBuiltAllColumnSections(columns[i]))
                {
                    SpawnPendingTreasures(columns[i]);
                }
            }
        }

        private void TrySpawnNaturalTreasures(Vector3Int column)
        {
            if (generationDebugPass
                != MinecraftWorldGenerationDebugPass.FullPipeline)
            {
                return;
            }
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

        private void ScheduleNextNaturalMonsterSpawnAttempt()
        {
            if (!NaturalMonsterSpawningEnabled
                || monsterSpawnTable == null)
            {
                nextNaturalMonsterSpawnAttemptTime = float.PositiveInfinity;
                return;
            }

            nextNaturalMonsterSpawnAttemptTime = Time.unscaledTime
                + monsterSpawnTable.SpawnAttemptIntervalSeconds;
        }

        private void ProcessNaturalMonsterSpawnSchedule()
        {
            if (!NaturalMonsterSpawningEnabled
                || monsterSpawnTable == null
                || !initialLoadComplete
                || Time.unscaledTime < nextNaturalMonsterSpawnAttemptTime)
            {
                return;
            }

            ScheduleNextNaturalMonsterSpawnAttempt();
            System.Random random = CreateNaturalMonsterSpawnRandom(
                worldSeed,
                naturalMonsterSpawnAttemptRound++);
            int maximumActive = monsterSpawnTable.MaximumActiveMonsters;
            if (maximumActive == 0
                || CountLivingMonsters() + pendingMonsterSpawnCount
                    >= maximumActive
                || random.NextDouble()
                    >= monsterSpawnTable.SpawnAttemptChance)
            {
                return;
            }

            MonsterSpawnDefinition definition =
                SelectRandomMonsterDefinition(random);
            if (definition == null)
            {
                return;
            }

            Vector3Int playerChunk = ResolveMonsterSpawnReferenceChunk();
            int exclusionRadius =
                monsterSpawnTable.PlayerExclusionRadiusInChunks;
            CollectMonsterSpawnCandidateColumns(
                playerChunk,
                exclusionRadius);
            int candidateChecks = Mathf.Min(
                monsterSpawnTable.CandidateChunksPerSpawnAttempt,
                monsterSpawnCandidateColumns.Count);
            for (int candidateIndex = 0;
                candidateIndex < candidateChecks;
                candidateIndex++)
            {
                int selectedIndex = random.Next(
                    candidateIndex,
                    monsterSpawnCandidateColumns.Count);
                Vector3Int selectedColumn =
                    monsterSpawnCandidateColumns[selectedIndex];
                monsterSpawnCandidateColumns[selectedIndex] =
                    monsterSpawnCandidateColumns[candidateIndex];
                monsterSpawnCandidateColumns[candidateIndex] = selectedColumn;
                if (TryQueueNaturalMonsterSpawn(
                    definition,
                    random,
                    selectedColumn,
                    playerChunk,
                    exclusionRadius,
                    maximumActive))
                {
                    return;
                }
            }
        }

        private MonsterSpawnDefinition SelectRandomMonsterDefinition(
            System.Random random)
        {
            IReadOnlyList<MonsterSpawnDefinition> definitions =
                monsterSpawnTable.Monsters;
            int validCount = 0;
            for (int index = 0; index < definitions.Count; index++)
            {
                MonsterSpawnDefinition definition = definitions[index];
                if (definition != null && definition.Prefab != null)
                {
                    validCount++;
                }
            }
            if (validCount == 0)
            {
                return null;
            }

            int selectedValidIndex = random.Next(validCount);
            for (int index = 0; index < definitions.Count; index++)
            {
                MonsterSpawnDefinition definition = definitions[index];
                if (definition == null || definition.Prefab == null)
                {
                    continue;
                }
                if (selectedValidIndex-- == 0)
                {
                    return definition;
                }
            }
            return null;
        }

        private void CollectMonsterSpawnCandidateColumns(
            Vector3Int playerChunk,
            int exclusionRadius)
        {
            monsterSpawnCandidateColumns.Clear();
            foreach (Vector3Int column in requiredChunks)
            {
                if (!IsMonsterSpawnChunkExcluded(
                        column,
                        playerChunk,
                        exclusionRadius)
                    && HasBuiltAllColumnSections(column))
                {
                    monsterSpawnCandidateColumns.Add(column);
                }
            }
        }

        private bool TryQueueNaturalMonsterSpawn(
            MonsterSpawnDefinition definition,
            System.Random random,
            Vector3Int column,
            Vector3Int playerChunk,
            int exclusionRadius,
            int maximumActive)
        {
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

                int pendingBefore = pendingMonsterSpawnCount;
                QueueMonsterSpawnGroup(
                    definition,
                    random,
                    NaturalMonsterSpawnGroupSize,
                    candidate.X,
                    candidate.Z,
                    column,
                    localPosition,
                    surfaceY,
                    playerChunk,
                    exclusionRadius,
                    maximumActive);
                return pendingMonsterSpawnCount > pendingBefore;
            }
            return false;
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
            Vector3Int playerChunk,
            int exclusionRadius,
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
                            playerChunk,
                            exclusionRadius)
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

        private Vector3Int ResolveMonsterSpawnReferenceChunk()
        {
            Vector3 worldPosition = viewer != null
                ? viewer.position
                : targetSpawnWorldPosition;
            return WorldPositionToChunk(worldPosition);
        }

        private static bool IsMonsterSpawnChunkExcluded(
            Vector3Int candidateChunk,
            Vector3Int playerChunk,
            int exclusionRadiusInChunks)
        {
            int radius = Mathf.Max(0, exclusionRadiusInChunks);
            long deltaX = (long)candidateChunk.x - playerChunk.x;
            long deltaZ = (long)candidateChunk.z - playerChunk.z;
            return deltaX * deltaX + deltaZ * deltaZ
                <= (long)radius * radius;
        }

        private static System.Random CreateNaturalMonsterSpawnRandom(
            int baseSeed,
            int attemptRound)
        {
            int seed = unchecked(baseSeed ^ MonsterSpawnSeedSalt);
            seed = unchecked(
                seed
                + attemptRound * MonsterSpawnRoundSeedStep);
            return new System.Random(seed);
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
            if (definition == null || definition.Prefab == null)
            {
                return;
            }

            GameObject prefab = definition.Prefab;
            TreasurePickup pickup = AcquireTreasureInstance(prefab);
            if (pickup == null)
            {
                return;
            }

            GameObject treasureObject = pickup.gameObject;
            treasureObject.transform.SetParent(null, false);
            treasureObject.transform.SetPositionAndRotation(
                transform.TransformPoint(localPosition),
                transform.rotation * Quaternion.Euler(0f, yaw, 0f));
            treasureObject.transform.localScale =
                prefab.transform.localScale;
            treasureObject.name = "Natural Treasure - " + definition.name;

            MeshCollider[] meshColliders =
                treasureObject.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < meshColliders.Length; i++)
            {
                meshColliders[i].convex = true;
            }
            Rigidbody body = treasureObject.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = treasureObject.AddComponent<Rigidbody>();
            }
            body.mass = definition.Weight;
            RigidbodyImpactFeedback.Ensure(body);
            if (treasureObject.GetComponentInChildren<Collider>() == null)
            {
                treasureObject.AddComponent<BoxCollider>();
            }

            pickup.PrepareForReuse();
            pickup.SetPoolReleaseHandler(ReleaseTreasureToPool);
            treasureObject.SetActive(true);
            pickup.Configure(definition, this);
            activeTreasurePrefabs[pickup] = prefab;
            activeTreasures.Add(pickup);
            ApplyDebugPresentationVisibility(treasureObject);
        }

        private TreasurePickup AcquireTreasureInstance(GameObject prefab)
        {
            if (pooledTreasuresByPrefab.TryGetValue(
                    prefab,
                    out Stack<TreasurePickup> pool))
            {
                while (pool.Count > 0)
                {
                    TreasurePickup pickup = pool.Pop();
                    pooledTreasureInstanceCount = Mathf.Max(
                        0,
                        pooledTreasureInstanceCount - 1);
                    if (pickup != null)
                    {
                        return pickup;
                    }
                }
            }

            GameObject treasureObject = Instantiate(prefab);
            treasureObject.SetActive(false);
            TreasurePickup created =
                treasureObject.GetComponent<TreasurePickup>();
            if (created == null)
            {
                created = treasureObject.AddComponent<TreasurePickup>();
            }
            return created;
        }

        private void ReleaseTreasureToPool(TreasurePickup pickup)
        {
            if (pickup == null)
            {
                return;
            }

            activeTreasures.Remove(pickup);
            if (!activeTreasurePrefabs.TryGetValue(
                    pickup,
                    out GameObject prefab)
                || prefab == null)
            {
                pickup.SetPoolReleaseHandler(null);
                DestroyGeneratedObject(pickup.gameObject);
                return;
            }
            activeTreasurePrefabs.Remove(pickup);

            GameObject treasureObject = pickup.gameObject;
            pickup.PrepareForPool();
            treasureObject.SetActive(false);
            treasureObject.name = "Pooled Treasure - " + prefab.name;
            treasureObject.transform.SetParent(transform, false);
            if (pooledTreasureInstanceCount
                >= MaximumPooledTreasureInstances)
            {
                DestroyGeneratedObject(treasureObject);
                return;
            }

            if (!pooledTreasuresByPrefab.TryGetValue(
                    prefab,
                    out Stack<TreasurePickup> pool))
            {
                pool = new Stack<TreasurePickup>();
                pooledTreasuresByPrefab.Add(prefab, pool);
            }
            pool.Push(pickup);
            pooledTreasureInstanceCount++;
        }

        private CreatureBehaviorAgent SpawnMonster(
            MonsterSpawnDefinition definition,
            Vector3 localPosition,
            float yaw)
        {
            if (definition == null || definition.Prefab == null)
            {
                return null;
            }

            GameObject prefab = definition.Prefab;
            CreatureBehaviorAgent agent =
                AcquireMonsterInstance(prefab);
            if (agent == null)
            {
                return null;
            }

            GameObject monsterObject = agent.gameObject;
            monsterObject.transform.SetParent(null, false);
            monsterObject.transform.SetPositionAndRotation(
                transform.TransformPoint(localPosition),
                transform.rotation * Quaternion.Euler(0f, yaw, 0f));
            monsterObject.transform.localScale =
                prefab.transform.localScale;
            monsterObject.name = "Natural Monster - " + definition.name;
            agent.PrepareForReuse(this, viewer);
            agent.SetPoolReleaseHandler(ReleaseMonsterToPool);
            monsterObject.SetActive(true);
            activeMonsterPrefabs[agent] = prefab;
            activeMonsters.Add(agent);
            ApplyDebugPresentationVisibility(monsterObject);
            return agent;
        }

        private CreatureBehaviorAgent AcquireMonsterInstance(
            GameObject prefab)
        {
            if (pooledMonstersByPrefab.TryGetValue(
                    prefab,
                    out Stack<CreatureBehaviorAgent> pool))
            {
                while (pool.Count > 0)
                {
                    CreatureBehaviorAgent agent = pool.Pop();
                    pooledMonsterInstanceCount = Mathf.Max(
                        0,
                        pooledMonsterInstanceCount - 1);
                    if (agent != null)
                    {
                        return agent;
                    }
                }
            }

            GameObject monsterObject = Instantiate(prefab);
            monsterObject.SetActive(false);
            CreatureBehaviorAgent created =
                monsterObject.GetComponent<CreatureBehaviorAgent>();
            if (created != null)
            {
                return created;
            }

            Debug.LogError(
                "Monster prefab '" + prefab.name + "' has no "
                + nameof(CreatureBehaviorAgent) + " on its root.",
                prefab);
            DestroyGeneratedObject(monsterObject);
            return null;
        }

        private void ReleaseMonsterToPool(
            CreatureBehaviorAgent agent)
        {
            if (agent == null)
            {
                return;
            }

            activeMonsters.Remove(agent);
            if (!activeMonsterPrefabs.TryGetValue(
                    agent,
                    out GameObject prefab)
                || prefab == null)
            {
                agent.SetPoolReleaseHandler(null);
                DestroyGeneratedObject(agent.gameObject);
                return;
            }
            activeMonsterPrefabs.Remove(agent);

            GameObject monsterObject = agent.gameObject;
            agent.PrepareForPool();
            monsterObject.SetActive(false);
            monsterObject.name = "Pooled Monster - " + prefab.name;
            monsterObject.transform.SetParent(transform, false);
            int maximumPoolSize = monsterSpawnTable != null
                ? monsterSpawnTable.MaximumActiveMonsters
                : DefaultMaximumPooledMonsterInstances;
            if (pooledMonsterInstanceCount >= maximumPoolSize)
            {
                DestroyGeneratedObject(monsterObject);
                return;
            }

            if (!pooledMonstersByPrefab.TryGetValue(
                    prefab,
                    out Stack<CreatureBehaviorAgent> pool))
            {
                pool = new Stack<CreatureBehaviorAgent>();
                pooledMonstersByPrefab.Add(prefab, pool);
            }
            pool.Push(agent);
            pooledMonsterInstanceCount++;
        }

        private void DestroyPooledTreasureInstances()
        {
            foreach (Stack<TreasurePickup> pool
                in pooledTreasuresByPrefab.Values)
            {
                while (pool.Count > 0)
                {
                    TreasurePickup pickup = pool.Pop();
                    if (pickup != null)
                    {
                        DestroyGeneratedObject(pickup.gameObject);
                    }
                }
            }
            pooledTreasuresByPrefab.Clear();
            pooledTreasureInstanceCount = 0;
        }

        private void DestroyPooledMonsterInstances()
        {
            foreach (Stack<CreatureBehaviorAgent> pool
                in pooledMonstersByPrefab.Values)
            {
                while (pool.Count > 0)
                {
                    CreatureBehaviorAgent agent = pool.Pop();
                    if (agent != null)
                    {
                        DestroyGeneratedObject(agent.gameObject);
                    }
                }
            }
            pooledMonstersByPrefab.Clear();
            pooledMonsterInstanceCount = 0;
        }

        private void ProcessPendingMonsterSpawns()
        {
            if (!NaturalMonsterSpawningEnabled
                || monsterSpawnTable == null
                || pendingMonsterSpawnCount <= 0)
            {
                return;
            }

            int spawnBudget = monsterSpawnTable.MaximumMonsterSpawnsPerFrame;
            while (spawnBudget > 0)
            {
                if (activePendingMonsterSpawnGroup == null)
                {
                    if (pendingMonsterSpawnGroups.Count == 0)
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
                if (IsMonsterSpawnChunkExcluded(
                    pendingSpawn.Column,
                    ResolveMonsterSpawnReferenceChunk(),
                    monsterSpawnTable.PlayerExclusionRadiusInChunks))
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

        private void CullVoxelDataOutsideRetentionRadius()
        {
            if (world == null || UsesFixedGenerationArea)
            {
                return;
            }

            int retentionRadius = Mathf.Max(
                0,
                voxelDataRetentionRadiusInChunks);
            int retentionRadiusSquared = retentionRadius * retentionRadius;
            voxelColumnsToEvict.Clear();
            foreach (Vector2Int coordinate in world.Chunks.Keys)
            {
                int deltaX = coordinate.x - viewerChunk.x;
                int deltaZ = coordinate.y - viewerChunk.z;
                if (deltaX * deltaX + deltaZ * deltaZ
                    > retentionRadiusSquared)
                {
                    voxelColumnsToEvict.Add(coordinate);
                }
            }

            for (int i = 0; i < voxelColumnsToEvict.Count; i++)
            {
                Vector2Int coordinate = voxelColumnsToEvict[i];
                var column = new Vector3Int(coordinate.x, 0, coordinate.y);
                if (requiredChunks.Contains(column)
                    || !world.RemoveChunk(coordinate, out _))
                {
                    continue;
                }

                pendingTreasureColumns.Remove(column);
                pendingPhysicsColumns.Remove(column);
                for (int section = 0;
                    section < EffectiveMeshSectionsPerColumn;
                    section++)
                {
                    meshBuildVersions.Remove(
                        new Vector3Int(column.x, section, column.z));
                }
            }
            voxelColumnsToEvict.Clear();
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

            Rigidbody[] bodies =
                UnityEngine.Object.FindObjectsByType<Rigidbody>(
                    FindObjectsSortMode.None);
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

        private void ProcessChunkDestructions()
        {
            chunkDestructionStopwatch.Restart();
            int inspected = 0;
            int destroyed = 0;
            int maximumInspections =
                MaximumChunkObjectsDestroyedPerFrame * 4;
            while (chunkDestructionQueue.Count > 0
                && destroyed < MaximumChunkObjectsDestroyedPerFrame
                && inspected < maximumInspections)
            {
                if (destroyed >= MinimumChunkObjectsDestroyedPerFrame
                    && chunkDestructionStopwatch.Elapsed.TotalMilliseconds
                        >= ChunkDestructionBudgetMilliseconds)
                {
                    break;
                }

                inspected++;
                Vector3Int coordinate = chunkDestructionQueue.Dequeue();
                if (!queuedChunkDestructions.Remove(coordinate)
                    || requiredChunks.Contains(ToColumnCoordinate(coordinate)))
                {
                    continue;
                }

                DestroyChunkObject(coordinate, true);
                destroyed++;
            }
            chunkDestructionStopwatch.Stop();
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

        private GameObject AcquireChunkObject(string objectName)
        {
            GameObject chunkObject = null;
            while (pooledChunkObjects.Count > 0 && chunkObject == null)
            {
                chunkObject = pooledChunkObjects.Pop();
            }

            if (chunkObject == null)
            {
                chunkObject = new GameObject();
                chunkObject.hideFlags = HideFlags.DontSave;
            }
            chunkObject.name = objectName;
            chunkObject.transform.SetParent(transform, false);
            chunkObject.transform.localPosition = Vector3.zero;
            chunkObject.transform.localRotation = Quaternion.identity;
            chunkObject.transform.localScale = Vector3.one;
            chunkObject.SetActive(false);
            return chunkObject;
        }

        private Mesh AcquireChunkMesh(string meshName)
        {
            Mesh mesh = null;
            while (pooledChunkMeshes.Count > 0 && mesh == null)
            {
                mesh = pooledChunkMeshes.Pop();
            }

            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.hideFlags = HideFlags.DontSave;
            }
            mesh.name = meshName;
            return mesh;
        }

        private void ReleaseChunkMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            mesh.Clear();
            mesh.name = "Pooled Cave Section Mesh";
            if (pooledChunkMeshes.Count < MaximumPooledChunkMeshes)
            {
                pooledChunkMeshes.Push(mesh);
            }
            else
            {
                DestroyGeneratedObject(mesh);
            }
        }

        private void ReleaseChunkObject(
            Vector3Int coordinate,
            GameObject chunkObject)
        {
            if (chunkObject == null)
            {
                return;
            }

            // Disable collision immediately before the old mesh is destroyed.
            chunkObject.SetActive(false);
            ClearChunkSurfaceContent(coordinate, chunkObject.transform);
            MeshCollider collider = chunkObject.GetComponent<MeshCollider>();
            if (collider != null)
            {
                collider.sharedMesh = null;
                collider.enabled = false;
            }
            MeshFilter filter = chunkObject.GetComponent<MeshFilter>();
            if (filter != null)
            {
                filter.sharedMesh = null;
            }
            MeshRenderer renderer = chunkObject.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                CrystalOreSparkleOverlay.Clear(renderer);
                renderer.sharedMaterials = Array.Empty<Material>();
            }

            chunkObject.name = "PooledCaveSection";
            chunkObject.transform.localPosition = Vector3.zero;
            chunkObject.transform.localRotation = Quaternion.identity;
            chunkObject.transform.localScale = Vector3.one;
            if (pooledChunkObjects.Count < MaximumPooledChunkObjects)
            {
                pooledChunkObjects.Push(chunkObject);
            }
            else
            {
                DestroyGeneratedObject(chunkObject);
            }
        }

        private void DestroyPooledChunkObjects()
        {
            while (pooledChunkObjects.Count > 0)
            {
                GameObject chunkObject = pooledChunkObjects.Pop();
                if (chunkObject != null)
                {
                    DestroyGeneratedObject(chunkObject);
                }
            }
        }

        private void DestroyPooledChunkMeshes()
        {
            while (pooledChunkMeshes.Count > 0)
            {
                Mesh mesh = pooledChunkMeshes.Pop();
                if (mesh != null)
                {
                    DestroyGeneratedObject(mesh);
                }
            }
        }

        private void DestroyChunkObject(
            Vector3Int coordinate,
            bool forgetBuildState)
        {
            CancelPendingMeshPostProcess(coordinate);
            chunkMeshes.TryGetValue(coordinate, out Mesh renderMesh);
            Mesh colliderMesh = null;
            if (chunkObjects.TryGetValue(
                    coordinate,
                    out GameObject chunkObject))
            {
                MeshCollider collider = chunkObject != null
                    ? chunkObject.GetComponent<MeshCollider>()
                    : null;
                colliderMesh = collider != null
                    ? collider.sharedMesh
                    : null;
                chunkObjects.Remove(coordinate);
                ReleaseChunkObject(coordinate, chunkObject);
            }
            if (renderMesh != null)
            {
                ReleaseChunkMesh(renderMesh);
            }
            if (colliderMesh != null && colliderMesh != renderMesh)
            {
                ReleaseChunkMesh(colliderMesh);
            }
            chunkMeshes.Remove(coordinate);
            if (terrainSurfaceMeshes.TryGetValue(
                    coordinate,
                    out Mesh terrainSurfaceMesh))
            {
                DestroyGeneratedObject(terrainSurfaceMesh);
                terrainSurfaceMeshes.Remove(coordinate);
            }
            // An unloaded/empty section is just as safe as a rebuilt collider:
            // its stale terrain collision has already been disabled above.
            NotifyOreTerrainMeshRebuilt(coordinate);
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
                && meshQueue.Count == 0
                && !HasPendingColliderPostProcessesForRequiredChunks())
            {
                bool initialSpawnAreaReady = initialSpawnPlacementPending;
                int readyChunkCount = requiredChunks.Count;
                renderingReadyLogged = true;
                generationStage = MinecraftCaveGenerationStage.Ready;
                FinalizeReadyColumns();
                ProcessPendingColumnPhysics();
                ReleaseViewerAtSpawn();
                initialLoadComplete = true;
                initialLoadCompletedAtUnscaledTime = Time.unscaledTime;
                RestoreGlobalGravityAfterInitialLoad();
                SpawnAllPendingTreasures();
                ScheduleNextNaturalMonsterSpawnAttempt();
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
                    if (!fixedPreviewArea && !HasDenseJigsawConfiguration)
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

            int columns = UsesConfiguredFixedPreviewArea
                ? Mathf.Max(1, fixedPreviewColumnsPerSide)
                : denseJigsawRegionConfigurationOverride.RegionColumnsPerSide;
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
                || spawnPointSceneStructure == null
                || UsesExternalDenseLandingCell)
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
            int lowestSpawnY = HasDenseJigsawConfiguration
                ? Math.Max(4, EffectiveWorldHeight / 4)
                : LowestSpawnY;
            int highestSpawnY = HasDenseJigsawConfiguration
                ? Math.Min(
                    EffectiveWorldHeight - 4,
                    EffectiveWorldHeight * 3 / 4)
                : HighestSpawnY;
            int minimumHorizontal = -72;
            int maximumHorizontal = 72;
            if (UsesConfiguredFixedPreviewArea)
            {
                int columns = Mathf.Max(1, fixedPreviewColumnsPerSide);
                int minimumChunk = -(columns / 2);
                int maximumChunk = minimumChunk + columns - 1;
                minimumHorizontal =
                    minimumChunk * VoxelColumnChunkData.Width + 2;
                maximumHorizontal =
                    (maximumChunk + 1) * VoxelColumnChunkData.Width - 3;
            }

            int middleSpawnY = (lowestSpawnY + highestSpawnY) / 2;
            var best = new Vector3Int(0, middleSpawnY, 0);
            float bestDensity = float.PositiveInfinity;
            for (int attempt = 0; attempt < 2400; attempt++)
            {
                Vector3Int point = attempt == 0
                    ? best
                    : new Vector3Int(
                        random.Next(
                            minimumHorizontal,
                            maximumHorizontal + 1),
                        random.Next(lowestSpawnY, highestSpawnY + 1),
                        random.Next(
                            minimumHorizontal,
                            maximumHorizontal + 1));
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
            if (HasDenseJigsawConfiguration)
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

            if (HasDenseJigsawConfiguration)
            {
                if (!DenseJigsawFeatureMixer.TryBuild(
                        denseJigsawRegionConfigurationOverride,
                        worldGenerationConfiguration,
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

        private IReadOnlyList<Vector3Int> ResolveConfiguredFixedPreviewOffsets()
        {
            int columns = Mathf.Max(1, fixedPreviewColumnsPerSide);
            if (configuredFixedPreviewOffsets == null
                || configuredFixedPreviewColumns != columns)
            {
                configuredFixedPreviewOffsets = Array.AsReadOnly(
                    BuildSquareOffsets(columns));
                configuredFixedPreviewColumns = columns;
            }
            return configuredFixedPreviewOffsets;
        }

        private IReadOnlyList<Vector3Int> ResolveConfiguredDenseRegionOffsets()
        {
            if (!HasDenseJigsawConfiguration)
            {
                return DenseRegionOffsets;
            }

            int columns = UsesConfiguredFixedPreviewArea
                ? Mathf.Max(1, fixedPreviewColumnsPerSide)
                : denseJigsawRegionConfigurationOverride.RegionColumnsPerSide;
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
            InstanceDisabled?.Invoke(this);
            ClearRuntimeState();
        }

        private void ResetPublishedInitialLoadProgress()
        {
            publishedInitialLoadStage =
                (MinecraftCaveGenerationStage)(-1);
            publishedInitialLoadPercent = -1;
            publishedInitialLoadComplete = false;
        }

        private void PublishInitialLoadProgress()
        {
            if (InitialLoadProgressChanged == null)
                return;

            float progress = Mathf.Clamp01(InitialLoadProgress);
            int progressPercent = Mathf.RoundToInt(progress * 100f);
            if (generationStage == publishedInitialLoadStage
                && progressPercent == publishedInitialLoadPercent
                && initialLoadComplete == publishedInitialLoadComplete)
            {
                return;
            }

            publishedInitialLoadStage = generationStage;
            publishedInitialLoadPercent = progressPercent;
            publishedInitialLoadComplete = initialLoadComplete;
            InitialLoadProgressChanged.Invoke(
                generationStage,
                progressPercent * 0.01f,
                initialLoadComplete);
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
            CancelDenseJigsawSelectionTask();
            foreach (GenerationTaskHandle handle in generationTasks.Values)
            {
                handle.Cancel();
                handle.Dispose();
            }
            generationCancellation?.Dispose();
            generationCancellation = null;
            generationTasks.Clear();
            completedGenerationCoordinates.Clear();
            applyingSurfaceBuildResult?.Dispose();
            applyingSurfaceBuildResult = null;
            foreach (Task<MeshGenerationResult> meshTask
                in meshTasks.Values)
            {
                if (meshTask.Status == TaskStatus.RanToCompletion)
                {
                    meshTask.Result.Dispose();
                    continue;
                }

                _ = meshTask.ContinueWith(
                    completed =>
                    {
                        if (completed.Status
                            == TaskStatus.RanToCompletion)
                        {
                            completed.Result.Dispose();
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            meshTasks.Clear();
            priorityMeshTasks.Clear();
            meshBuildVersions.Clear();
            foreach (MeshPostProcessRequest request
                in pendingMeshPostProcesses.Values)
            {
                request.Cancel();
            }
            pendingMeshPostProcesses.Clear();
            meshPostProcessQueue.Clear();
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
            pendingOreTerrainReleases.Clear();
            oreDropsAwaitingPhysicsSync.Clear();
            for (int i = activeTreasures.Count - 1; i >= 0; i--)
            {
                TreasurePickup treasure = activeTreasures[i];
                if (treasure != null)
                {
                    treasure.SetPoolReleaseHandler(null);
                    DestroyGeneratedObject(treasure.gameObject);
                }
            }
            activeTreasures.Clear();
            activeTreasurePrefabs.Clear();
            DestroyPooledTreasureInstances();
            treasureSpawnedColumns.Clear();
            pendingTreasureColumns.Clear();
            pendingPhysicsColumns.Clear();
            pendingPhysicsColumnBuffer.Clear();
            for (int i = activeMonsters.Count - 1; i >= 0; i--)
            {
                CreatureBehaviorAgent monster = activeMonsters[i];
                if (monster != null)
                {
                    monster.SetPoolReleaseHandler(null);
                    DestroyGeneratedObject(monster.gameObject);
                }
            }
            activeMonsters.Clear();
            activeMonsterPrefabs.Clear();
            DestroyPooledMonsterInstances();
            markerSpawnedColumns.Clear();
            markerSpawnBuffer.Clear();
            checkpointSpawnedColumns.Clear();
            placedCheckpointVoxels.Clear();
            checkpointSpawnBuffer.Clear();
            for (int i = activeCheckpointObjects.Count - 1; i >= 0; i--)
            {
                if (activeCheckpointObjects[i] != null)
                {
                    DestroyGeneratedObject(activeCheckpointObjects[i]);
                }
            }
            activeCheckpointObjects.Clear();
            primarySpawnCheckpoint = null;
            pendingMonsterSpawnGroups.Clear();
            monsterSpawnCandidateColumns.Clear();
            activePendingMonsterSpawnGroup = null;
            pendingMonsterSpawnCount = 0;
            naturalMonsterSpawnAttemptRound = 0;
            nextNaturalMonsterSpawnAttemptTime = float.PositiveInfinity;
            naturalMonsterSpawningEnabled = false;
            waitingForExternalDensePortalEntry = false;
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
            DestroyPooledChunkObjects();
            DestroyPooledChunkMeshes();
            foreach (Mesh terrainSurfaceMesh in terrainSurfaceMeshes.Values)
            {
                DestroyGeneratedObject(terrainSurfaceMesh);
            }
            terrainSurfaceMeshes.Clear();
            gameplayCarvedVoxels.Clear();
            gameplayVoxelOverridesByColumn.Clear();
            voxelColumnsToEvict.Clear();
            gameplayVoxelOverrideCount = 0;
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
            if (hasViewerInitialTransform
                && viewer != null
                && !keepViewerTransformDuringGeneration)
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
            hasDenseJigsawSelectionWindow = false;
            denseJigsawSelectionRebuildCount = 0;
            hasViewerChunk = false;

            structurePassApplied = false;
            initialSpawnPlacementPending = false;
            initialLoadComplete = false;
            initialLoadCompletedAtUnscaledTime = 0f;
            generationStage = MinecraftCaveGenerationStage.None;
            hasViewerInitialTransform = false;
            usesExternalWorldRendering = false;
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

        private sealed class PendingOreTerrainRelease
        {
            public PendingOreTerrainRelease(
                MinedOreDrop drop,
                IEnumerable<Vector3Int> affectedMeshes)
            {
                Drop = drop;
                AffectedMeshes = new HashSet<Vector3Int>(affectedMeshes);
            }

            public MinedOreDrop Drop { get; }
            public HashSet<Vector3Int> AffectedMeshes { get; }
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

        private enum MeshPostProcessStage
        {
            Collider,
            Surface
        }

        private sealed class MeshPostProcessRequest
        {
            private bool isCanceled;

            public MeshPostProcessRequest(
                Vector3Int coordinate,
                int version,
                MeshPostProcessStage stage,
                VoxelMeshData data,
                Mesh preparedMesh,
                CaveSurfaceBuildResult surfaceBuildResult)
            {
                Coordinate = coordinate;
                Version = version;
                Stage = stage;
                VoxelMeshData validatedData = data
                    ?? throw new ArgumentNullException(nameof(data));
                PreparedMesh = preparedMesh
                    ?? throw new ArgumentNullException(nameof(preparedMesh));
                SurfaceBuildResult = surfaceBuildResult;
                if (SurfaceBuildResult == null)
                {
                    Data = validatedData;
                    Data.Retain();
                }
            }

            public Vector3Int Coordinate { get; }
            public int Version { get; }
            public MeshPostProcessStage Stage { get; set; }
            public VoxelMeshData Data { get; private set; }
            public Mesh PreparedMesh { get; }
            public CaveSurfaceBuildResult SurfaceBuildResult
            {
                get;
                private set;
            }
            public bool IsCanceled => isCanceled;

            public void Cancel()
            {
                if (isCanceled)
                {
                    return;
                }

                isCanceled = true;
                Data?.Dispose();
                Data = null;
                SurfaceBuildResult?.Dispose();
                SurfaceBuildResult = null;
            }
        }

        private sealed class MeshGenerationResult : IDisposable
        {
            private CaveSurfaceBuildResult surfaceBuildResult;
            private VoxelMeshData data;

            public MeshGenerationResult(
                Vector3Int coordinate,
                int version,
                VoxelMeshData data,
                CaveSurfaceBuildResult surfaceBuildResult)
            {
                Coordinate = coordinate;
                Version = version;
                this.data = data ?? throw new ArgumentNullException(nameof(data));
                this.surfaceBuildResult = surfaceBuildResult;
            }

            public Vector3Int Coordinate { get; }
            public int Version { get; }
            public VoxelMeshData Data => data;

            public CaveSurfaceBuildResult TakeSurfaceBuildResult()
            {
                CaveSurfaceBuildResult result = surfaceBuildResult;
                surfaceBuildResult = null;
                return result;
            }

            public void Dispose()
            {
                data?.Dispose();
                data = null;
                surfaceBuildResult?.Dispose();
                surfaceBuildResult = null;
            }
        }

        private sealed class DenseJigsawSelectionTaskHandle :
            IDisposable
        {
            private readonly CancellationTokenSource cancellation;

            public DenseJigsawSelectionTaskHandle(
                Task<JigsawPlacementSelection> task,
                CancellationTokenSource cancellation,
                int minimumChunkX,
                int maximumChunkX,
                int minimumChunkZ,
                int maximumChunkZ)
            {
                Task = task ?? throw new ArgumentNullException(nameof(task));
                this.cancellation = cancellation
                    ?? throw new ArgumentNullException(nameof(cancellation));
                MinimumChunkX = minimumChunkX;
                MaximumChunkX = maximumChunkX;
                MinimumChunkZ = minimumChunkZ;
                MaximumChunkZ = maximumChunkZ;
            }

            public Task<JigsawPlacementSelection> Task { get; }
            public int MinimumChunkX { get; }
            public int MaximumChunkX { get; }
            public int MinimumChunkZ { get; }
            public int MaximumChunkZ { get; }

            public bool Contains(
                int minimumChunkX,
                int maximumChunkX,
                int minimumChunkZ,
                int maximumChunkZ)
            {
                return minimumChunkX >= MinimumChunkX
                    && maximumChunkX <= MaximumChunkX
                    && minimumChunkZ >= MinimumChunkZ
                    && maximumChunkZ <= MaximumChunkZ;
            }

            public void Cancel()
            {
                cancellation.Cancel();
            }

            public void Dispose()
            {
                cancellation.Dispose();
            }
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
