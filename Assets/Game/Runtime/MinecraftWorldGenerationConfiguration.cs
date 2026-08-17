using System.Collections.Generic;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    [CreateAssetMenu(
        fileName = "WorldGeneration",
        menuName = "Supernova/Levels/World Generation Configuration")]
    public sealed class MinecraftWorldGenerationConfiguration : ScriptableObject
    {
        private const int MinimumVoxelDataRetentionRadiusInChunks = 0;
        private const int MaximumVoxelDataRetentionRadiusInChunks = 16;
        private const float MinimumMeshPhaseBudgetMilliseconds = 0.25f;
        private const float MaximumMeshPhaseBudgetMilliseconds = 16f;

        [Header("Density")]
        [SerializeField] private MinecraftWorldGenerationMode generationMode;
        [SerializeField, Min(1)] private int superflatStoneHeight = 10;
        [SerializeField] private int worldSeed = 18731;
        [SerializeField] private MinecraftCaveSettings settings =
            new MinecraftCaveSettings();

        [Header("Generation Runtime")]
        [SerializeField] private bool placeViewerInCave = true;
        [SerializeField, Range(1, 8)]
        private int maxConcurrentGenerationJobs = 2;
        [SerializeField, Range(1, 8)]
        private int maxConcurrentMeshJobs = 1;
        [SerializeField, Range(1, 8)] private int meshesBuiltPerFrame = 2;
        [SerializeField, Range(1, 8)]
        private int meshSnapshotsCapturedPerFrame = 2;
        [SerializeField, Range(
            MinimumMeshPhaseBudgetMilliseconds,
            MaximumMeshPhaseBudgetMilliseconds)]
        private float meshCommitBudgetMilliseconds = 4f;
        [SerializeField, Range(
            MinimumMeshPhaseBudgetMilliseconds,
            MaximumMeshPhaseBudgetMilliseconds)]
        private float meshSnapshotBudgetMilliseconds = 2f;
        [SerializeField, Range(
            MinimumVoxelDataRetentionRadiusInChunks,
            MaximumVoxelDataRetentionRadiusInChunks)]
        private int voxelDataRetentionRadiusInChunks = 3;

        [Header("Depth Probability Scaling")]
        [Tooltip("Depth scaling applied to every configured ore feature.")]
        [SerializeField] private DepthProbabilityProfile oreDepthProbability =
            new DepthProbabilityProfile();
        [Tooltip("Depth scaling applied to every configured treasure.")]
        [SerializeField] private DepthProbabilityProfile treasureDepthProbability =
            new DepthProbabilityProfile();

        [Header("Rendering")]
        [SerializeField, Min(0.01f)] private float voxelSize = 0.42f;
        [SerializeField] private float isoLevel;
        [SerializeField]
        private MarchingCubesVertexPlacement vertexPlacement =
            MarchingCubesVertexPlacement.DensityInterpolated;
        [SerializeField] private bool generateColliders = true;
        [SerializeField] private PhysicMaterial terrainPhysicsMaterial;
        [SerializeField] private VoxelTypeCatalog voxelTypeCatalog;

        [Header("Punctual Lighting")]
        [SerializeField, Range(0.25f, 1f)]
        private float punctualLightFalloffPower = 0.55f;
        [SerializeField, Min(0.01f)]
        private float punctualLightAttenuationLimit = 1.5f;
        [SerializeField, Min(0.01f)]
        private float punctualLightMultiplier = 1f;

        [Header("Voxel Generation")]
        [SerializeField] private VoxelTypeDefinition baseSolidVoxelType;
        [SerializeField] private VoxelTypeDefinition bedrockVoxelType;
        [SerializeField] private List<VoxelOreFeatureDefinition> oreFeatures =
            new List<VoxelOreFeatureDefinition>();

        [Header("Surface Biomes")]
        [SerializeField] private CaveBiomeCatalog caveBiomeCatalog;

        [Header("Structures")]
        [SerializeField]
        private SpawnPointStructureRule spawnPointStructureRule =
            new SpawnPointStructureRule();
        [SerializeField]
        private List<VoxelStructureFeatureDefinition> structureFeatures =
            new List<VoxelStructureFeatureDefinition>();
        [SerializeField]
        private List<JigsawStructureFeatureDefinition> jigsawStructures =
            new List<JigsawStructureFeatureDefinition>();

        public MinecraftWorldGenerationMode GenerationMode => generationMode;
        public int SuperflatStoneHeight => Mathf.Clamp(
            superflatStoneHeight,
            1,
            VoxelColumnChunkData.Height - 1);
        public int WorldSeed => worldSeed;
        public MinecraftCaveSettings Settings => settings;
        public bool PlaceViewerInCave => placeViewerInCave;
        public int MaxConcurrentGenerationJobs => Mathf.Clamp(
            maxConcurrentGenerationJobs,
            1,
            8);
        public int MaxConcurrentMeshJobs => Mathf.Clamp(
            maxConcurrentMeshJobs,
            1,
            8);
        public int MeshesBuiltPerFrame => Mathf.Clamp(meshesBuiltPerFrame, 1, 8);
        public int MeshSnapshotsCapturedPerFrame => Mathf.Clamp(
            meshSnapshotsCapturedPerFrame,
            1,
            8);
        public float MeshCommitBudgetMilliseconds => Mathf.Clamp(
            meshCommitBudgetMilliseconds,
            MinimumMeshPhaseBudgetMilliseconds,
            MaximumMeshPhaseBudgetMilliseconds);
        public float MeshSnapshotBudgetMilliseconds => Mathf.Clamp(
            meshSnapshotBudgetMilliseconds,
            MinimumMeshPhaseBudgetMilliseconds,
            MaximumMeshPhaseBudgetMilliseconds);
        public int VoxelDataRetentionRadiusInChunks => Mathf.Clamp(
            voxelDataRetentionRadiusInChunks,
            MinimumVoxelDataRetentionRadiusInChunks,
            MaximumVoxelDataRetentionRadiusInChunks);
        public DepthProbabilityProfile OreDepthProbability =>
            oreDepthProbability;
        public DepthProbabilityProfile TreasureDepthProbability =>
            treasureDepthProbability;
        public float VoxelSize => Mathf.Max(0.01f, voxelSize);
        public float IsoLevel => isoLevel;
        public MarchingCubesVertexPlacement VertexPlacement => vertexPlacement;
        public bool GenerateColliders => generateColliders;
        public PhysicMaterial TerrainPhysicsMaterial => terrainPhysicsMaterial;
        public VoxelTypeCatalog VoxelTypeCatalog => voxelTypeCatalog;
        public float PunctualLightFalloffPower => Mathf.Clamp(
            punctualLightFalloffPower,
            0.25f,
            1f);
        public float PunctualLightAttenuationLimit =>
            Mathf.Max(0.01f, punctualLightAttenuationLimit);
        public float PunctualLightMultiplier =>
            Mathf.Max(0.01f, punctualLightMultiplier);
        public VoxelTypeDefinition BaseSolidVoxelType => baseSolidVoxelType;
        public VoxelTypeDefinition BedrockVoxelType => bedrockVoxelType;
        public IReadOnlyList<VoxelOreFeatureDefinition> OreFeatures => oreFeatures;
        public CaveBiomeCatalog CaveBiomeCatalog => caveBiomeCatalog;
        public SpawnPointStructureRule SpawnPointStructureRule =>
            spawnPointStructureRule;
        public IReadOnlyList<VoxelStructureFeatureDefinition> StructureFeatures =>
            structureFeatures;
        public IReadOnlyList<JigsawStructureFeatureDefinition> JigsawStructures =>
            jigsawStructures;

        public void SetStructureFeatures(
            IEnumerable<VoxelStructureFeatureDefinition> features)
        {
            structureFeatures = features != null
                ? new List<VoxelStructureFeatureDefinition>(features)
                : new List<VoxelStructureFeatureDefinition>();
        }

        public void SetJigsawStructures(
            IEnumerable<JigsawStructureFeatureDefinition> structures)
        {
            jigsawStructures = structures != null
                ? new List<JigsawStructureFeatureDefinition>(structures)
                : new List<JigsawStructureFeatureDefinition>();
        }

        private void OnValidate()
        {
            superflatStoneHeight = Mathf.Clamp(
                superflatStoneHeight,
                1,
                VoxelColumnChunkData.Height - 1);
            maxConcurrentGenerationJobs = Mathf.Clamp(
                maxConcurrentGenerationJobs,
                1,
                8);
            maxConcurrentMeshJobs = Mathf.Clamp(
                maxConcurrentMeshJobs,
                1,
                8);
            meshesBuiltPerFrame = Mathf.Clamp(meshesBuiltPerFrame, 1, 8);
            meshSnapshotsCapturedPerFrame = Mathf.Clamp(
                meshSnapshotsCapturedPerFrame,
                1,
                8);
            meshCommitBudgetMilliseconds = Mathf.Clamp(
                meshCommitBudgetMilliseconds,
                MinimumMeshPhaseBudgetMilliseconds,
                MaximumMeshPhaseBudgetMilliseconds);
            meshSnapshotBudgetMilliseconds = Mathf.Clamp(
                meshSnapshotBudgetMilliseconds,
                MinimumMeshPhaseBudgetMilliseconds,
                MaximumMeshPhaseBudgetMilliseconds);
            voxelDataRetentionRadiusInChunks = Mathf.Clamp(
                voxelDataRetentionRadiusInChunks,
                MinimumVoxelDataRetentionRadiusInChunks,
                MaximumVoxelDataRetentionRadiusInChunks);
            voxelSize = Mathf.Max(0.01f, voxelSize);
            punctualLightFalloffPower =
                Mathf.Clamp(punctualLightFalloffPower, 0.25f, 1f);
            punctualLightAttenuationLimit =
                Mathf.Max(0.01f, punctualLightAttenuationLimit);
            punctualLightMultiplier =
                Mathf.Max(0.01f, punctualLightMultiplier);
            if (settings == null)
            {
                settings = new MinecraftCaveSettings();
            }
            if (oreDepthProbability == null)
            {
                oreDepthProbability = new DepthProbabilityProfile();
            }
            if (treasureDepthProbability == null)
            {
                treasureDepthProbability = new DepthProbabilityProfile();
            }
            if (oreFeatures == null)
            {
                oreFeatures = new List<VoxelOreFeatureDefinition>();
            }
            if (spawnPointStructureRule == null)
            {
                spawnPointStructureRule = new SpawnPointStructureRule();
            }
            if (structureFeatures == null)
            {
                structureFeatures = new List<VoxelStructureFeatureDefinition>();
            }
            if (jigsawStructures == null)
            {
                jigsawStructures = new List<JigsawStructureFeatureDefinition>();
            }
        }
    }
}
