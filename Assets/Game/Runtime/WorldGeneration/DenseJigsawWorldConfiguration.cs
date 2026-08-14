using System;
using System.Collections.Generic;
using Supernova.MinecraftCaves;
using Supernova.Missions;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.WorldGeneration
{
    /// <summary>
    /// Describes only the intentional differences from InfiniteCaves:
    /// a configurable horizontal extent and a high-frequency mixed jigsaw pool.
    /// Terrain, ores, meshing, mining, drops, markers, mobs, and all other
    /// runtime behaviour come from the referenced level unchanged.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DenseJigsawRegionWorld",
        menuName = "Supernova/World Generation/Dense Jigsaw Region World")]
    public sealed class DenseJigsawWorldConfiguration : ScriptableObject
    {
        public const int DefaultRegionColumnsPerSide = 6;
        public const int DefaultWorldSectionCount = 2;
        public const float DefaultStructureDensity = 1f;
        public const int DefaultExternalLandingCellDistanceInColumns = 2;
        public const int MinimumWorldSectionCount = 2;
        public const int MaximumWorldSectionCount =
            VoxelColumnChunkData.Height
            / MinecraftCaveInfiniteWorld.MeshSectionHeight;
        public const int MaximumRegionColumnsPerSide = 16;
        public const float MaximumStructureDensity = 16f;

        [Header("Inherited InfiniteCaves level")]
        [SerializeField] private LevelConfiguration infiniteCavesLevelSource;

        [Header("World volume")]
        [Tooltip(
            "Streams the Dense jigsaw world without a horizontal boundary. "
            + "Height, terrain, structures, density, and all other generation "
            + "settings remain identical to the finite world.")]
        [SerializeField] private bool generateInfiniteWorld;
        [Tooltip("Vertical section count. Every section is 32 voxels high.")]
        [SerializeField, Range(
            MinimumWorldSectionCount,
            MaximumWorldSectionCount)]
        private int worldSectionCount = DefaultWorldSectionCount;
        [Tooltip(
            "Horizontal size in columns per side. Six creates the original "
            + "6 x 6 region (36 columns total).")]
        [SerializeField, Range(1, MaximumRegionColumnsPerSide)]
        private int regionColumnsPerSide = DefaultRegionColumnsPerSide;

        [Header("External landing Cell")]
        [Tooltip(
            "Places the authored landing Cell in empty space beyond the finite "
            + "voxel region and uses its marker as the player spawn.")]
        [SerializeField] private bool useExternalLandingCell = true;
        [Tooltip(
            "Empty column-widths between the positive-X map edge and the Cell. "
            + "Keep this small for convenient inspection outside the bedrock wall.")]
        [SerializeField, Range(1, 256)]
        private int externalLandingCellDistanceInColumns =
            DefaultExternalLandingCellDistanceInColumns;

        [Header("High-frequency mixed jigsaw override")]
        [Tooltip(
            "When enabled, complete jigsaw layouts are selected before column "
            + "generation and any layout intersecting an earlier winner is "
            + "discarded. Leave disabled for the original interwoven result.")]
        [SerializeField] private bool preventStructureIntersections;
        [Tooltip(
            "Expected structure count relative to the original Dense profile. "
            + "Values below 1 reduce placement chance; values above 1 reduce "
            + "the placement-grid spacing and may overlap more often.")]
        [SerializeField, Range(0f, MaximumStructureDensity)]
        private float structureDensity = DefaultStructureDensity;
        [Tooltip(
            "Base placement-grid spacing at density 1. This is an advanced "
            + "control; Structure Density is normally the value to tune.")]
        [SerializeField, Range(1, 16)]
        private int structureRegionSizeInColumns = 4;
        [Tooltip(
            "Preferred structure floor in voxels. It is clamped to the selected "
            + "world height and to the dimensions of the configured pieces.")]
        [SerializeField, Range(16, VoxelColumnChunkData.Height - 16)]
        private int floorHeight =
            DefaultWorldSectionCount
            * MinecraftCaveInfiniteWorld.MeshSectionHeight
            / 2;
        [SerializeField, Range(16, 256)] private int maxPiecesPerLayout = 128;
        [SerializeField, Range(4, 64)] private int maxDepth = 32;
        [SerializeField, Range(16, 96)] private int layoutRadius = 48;
        [SerializeField, Range(1, 16)] private int layoutAttempts = 8;
        [SerializeField, Range(1, 32)]
        private int connectorPlacementAttempts = 16;

        public LevelConfiguration InfiniteCavesLevelSource =>
            infiniteCavesLevelSource;
        public MinecraftWorldGenerationConfiguration
            InfiniteCavesGenerationSource =>
                infiniteCavesLevelSource != null
                    ? infiniteCavesLevelSource.WorldGeneration
                    : null;
        public IReadOnlyList<JigsawStructureFeatureDefinition>
            StructureFamilies =>
                InfiniteCavesGenerationSource != null
                    ? InfiniteCavesGenerationSource.JigsawStructures
                    : Array.Empty<JigsawStructureFeatureDefinition>();
        public VoxelTypeDefinition StoneType =>
            InfiniteCavesGenerationSource != null
                ? InfiniteCavesGenerationSource.BaseSolidVoxelType
                : null;
        public bool PreventStructureIntersections =>
            preventStructureIntersections;
        public bool GenerateInfiniteWorld => generateInfiniteWorld;
        public int WorldSectionCount => Mathf.Clamp(
            worldSectionCount,
            MinimumWorldSectionCount,
            MaximumWorldSectionCount);
        public int WorldHeight =>
            WorldSectionCount * MinecraftCaveInfiniteWorld.MeshSectionHeight;
        public int RegionColumnsPerSide => Mathf.Clamp(
            regionColumnsPerSide,
            1,
            MaximumRegionColumnsPerSide);
        public int RegionColumnCount =>
            RegionColumnsPerSide * RegionColumnsPerSide;
        public bool UseExternalLandingCell => useExternalLandingCell;
        public int ExternalLandingCellDistanceInColumns => Mathf.Clamp(
            externalLandingCellDistanceInColumns,
            1,
            256);
        public Vector3 ExternalLandingCellPlayerVoxelPosition
        {
            get
            {
                int columns = RegionColumnsPerSide;
                int minimumChunk = -(columns / 2);
                int maximumChunk = minimumChunk + columns - 1;
                float minimumVoxelZ =
                    minimumChunk * VoxelColumnChunkData.Depth;
                float maximumVoxelZ =
                    (maximumChunk + 1) * VoxelColumnChunkData.Depth;
                float outerEdgeVoxelX =
                    (maximumChunk + 1) * VoxelColumnChunkData.Width;
                return new Vector3(
                    outerEdgeVoxelX
                        + ExternalLandingCellDistanceInColumns
                        * VoxelColumnChunkData.Width,
                    FloorHeight,
                    (minimumVoxelZ + maximumVoxelZ) * 0.5f);
            }
        }
        public float StructureDensity => Mathf.Clamp(
            structureDensity,
            0f,
            MaximumStructureDensity);
        public int StructureRegionSizeInColumns
        {
            get
            {
                int baseSpacing = Math.Max(1, structureRegionSizeInColumns);
                if (StructureDensity <= 1f)
                {
                    return baseSpacing;
                }

                return Math.Max(
                    1,
                    Mathf.FloorToInt(
                        baseSpacing / Mathf.Sqrt(StructureDensity)));
            }
        }
        public float StructurePlacementChance
        {
            get
            {
                if (StructureDensity <= 0f)
                {
                    return 0f;
                }

                int baseSpacing = Math.Max(1, structureRegionSizeInColumns);
                int effectiveSpacing = StructureRegionSizeInColumns;
                float spacingDensity =
                    baseSpacing * baseSpacing
                    / (float)(effectiveSpacing * effectiveSpacing);
                return Mathf.Clamp01(StructureDensity / spacingDensity);
            }
        }
        public int FloorHeight => Mathf.Clamp(
            floorHeight,
            16,
            WorldHeight - 16);
        public int MaxPiecesPerLayout => maxPiecesPerLayout;
        public int MaxDepth => maxDepth;
        public int LayoutRadius => layoutRadius;
        public int LayoutAttempts => layoutAttempts;
        public int ConnectorPlacementAttempts =>
            connectorPlacementAttempts;

        public void Configure(LevelConfiguration sourceLevel)
        {
            infiniteCavesLevelSource = sourceLevel;
        }

        public void ConfigureStructureIntersections(bool preventIntersections)
        {
            preventStructureIntersections = preventIntersections;
        }

        public void ConfigureInfiniteWorld(bool generateInfinite)
        {
            generateInfiniteWorld = generateInfinite;
        }

        public void ConfigureGenerationVolume(
            int sectionCount,
            int columnsPerSide,
            float density)
        {
            worldSectionCount = Mathf.Clamp(
                sectionCount,
                MinimumWorldSectionCount,
                MaximumWorldSectionCount);
            regionColumnsPerSide = Mathf.Clamp(
                columnsPerSide,
                1,
                MaximumRegionColumnsPerSide);
            structureDensity = Mathf.Clamp(
                density,
                0f,
                MaximumStructureDensity);
            floorHeight = Mathf.Clamp(
                floorHeight,
                16,
                WorldHeight - 16);
        }

        private void OnValidate()
        {
            worldSectionCount = Mathf.Clamp(
                worldSectionCount,
                MinimumWorldSectionCount,
                MaximumWorldSectionCount);
            regionColumnsPerSide = Mathf.Clamp(
                regionColumnsPerSide,
                1,
                MaximumRegionColumnsPerSide);
            externalLandingCellDistanceInColumns = Mathf.Clamp(
                externalLandingCellDistanceInColumns,
                1,
                256);
            structureDensity = Mathf.Clamp(
                structureDensity,
                0f,
                MaximumStructureDensity);
            structureRegionSizeInColumns = Mathf.Max(
                1,
                structureRegionSizeInColumns);
            floorHeight = Mathf.Clamp(
                floorHeight,
                16,
                WorldHeight - 16);
            maxPiecesPerLayout = Mathf.Clamp(maxPiecesPerLayout, 16, 256);
            maxDepth = Mathf.Clamp(maxDepth, 4, 64);
            layoutRadius = Mathf.Clamp(layoutRadius, 16, 96);
            layoutAttempts = Mathf.Clamp(layoutAttempts, 1, 16);
            connectorPlacementAttempts = Mathf.Clamp(
                connectorPlacementAttempts,
                1,
                32);
        }
    }
}
