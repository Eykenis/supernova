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
        [Header("Density")]
        [SerializeField] private MinecraftWorldGenerationMode generationMode;
        [SerializeField, Min(1)] private int superflatStoneHeight = 10;
        [SerializeField] private int worldSeed = 18731;
        [SerializeField] private MinecraftCaveSettings settings =
            new MinecraftCaveSettings();

        [Header("Generation Runtime")]
        [SerializeField] private bool placeViewerInCave = true;
        [SerializeField, Range(1, 8)]
        private int maxConcurrentGenerationJobs = 4;
        [SerializeField, Range(1, 8)] private int meshesBuiltPerFrame = 1;

        [Header("Depth Probability Scaling")]
        [Tooltip("Depth scaling applied to every configured ore feature.")]
        [SerializeField] private DepthProbabilityProfile oreDepthProbability =
            new DepthProbabilityProfile();
        [Tooltip("Depth scaling applied to every configured treasure.")]
        [SerializeField] private DepthProbabilityProfile treasureDepthProbability =
            new DepthProbabilityProfile();
        [Tooltip("Depth scaling applied to every configured monster.")]
        [SerializeField] private DepthProbabilityProfile monsterDepthProbability =
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

        [Header("Structures")]
        [SerializeField]
        private SpawnPointStructureRule spawnPointStructureRule =
            new SpawnPointStructureRule();

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
        public int MeshesBuiltPerFrame => Mathf.Clamp(meshesBuiltPerFrame, 1, 8);
        public DepthProbabilityProfile OreDepthProbability =>
            oreDepthProbability;
        public DepthProbabilityProfile TreasureDepthProbability =>
            treasureDepthProbability;
        public DepthProbabilityProfile MonsterDepthProbability =>
            monsterDepthProbability;
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
        public SpawnPointStructureRule SpawnPointStructureRule =>
            spawnPointStructureRule;

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
            meshesBuiltPerFrame = Mathf.Clamp(meshesBuiltPerFrame, 1, 8);
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
            if (monsterDepthProbability == null)
            {
                monsterDepthProbability = new DepthProbabilityProfile();
            }
            if (oreFeatures == null)
            {
                oreFeatures = new List<VoxelOreFeatureDefinition>();
            }
            if (spawnPointStructureRule == null)
            {
                spawnPointStructureRule = new SpawnPointStructureRule();
            }
        }
    }
}
