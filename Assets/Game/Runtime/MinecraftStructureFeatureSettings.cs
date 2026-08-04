using System;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Thread-safe snapshot of one random voxel structure feature.
    /// </summary>
    public readonly struct MinecraftStructureFeatureSettings
    {
        public MinecraftStructureFeatureSettings(
            string stableId,
            VoxelTypeId structureType,
            int seedSalt,
            int regionSizeInChunks,
            float placementChance,
            int minFloorHeight,
            int maxFloorHeight,
            Vector3Int roomSize,
            int wallThickness,
            int foundationDepth,
            int entranceWidth,
            int entranceHeight,
            int entranceLength,
            Vector3Int templateSize = default(Vector3Int),
            Vector3Int templateAnchor = default(Vector3Int),
            float[] templateDensities = null,
            VoxelTypeId[] templateTypes = null)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new ArgumentException(
                    "A structure feature requires a stable ID.",
                    nameof(stableId));
            }
            if (structureType.IsAir)
            {
                throw new ArgumentException(
                    "A structure feature requires a solid voxel type.",
                    nameof(structureType));
            }
            if (roomSize.x < 7 || roomSize.y < 5 || roomSize.z < 7
                || (roomSize.x & 1) == 0
                || (roomSize.z & 1) == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roomSize),
                    "Room X/Z dimensions must be odd and at least 7; height must be at least 5.");
            }

            StableId = stableId.Trim();
            StructureType = structureType;
            SeedSalt = seedSalt;
            RegionSizeInChunks = Math.Max(2, regionSizeInChunks);
            PlacementChance = Clamp01(placementChance);
            MinFloorHeight = Math.Min(minFloorHeight, maxFloorHeight);
            MaxFloorHeight = Math.Max(minFloorHeight, maxFloorHeight);
            bool hasTemplate = templateDensities != null || templateTypes != null;
            if (hasTemplate)
            {
                int templateCount = templateSize.x * templateSize.y * templateSize.z;
                if (templateSize.x < 1 || templateSize.y < 1 || templateSize.z < 1
                    || templateDensities == null
                    || templateTypes == null
                    || templateDensities.Length != templateCount
                    || templateTypes.Length != templateCount)
                {
                    throw new ArgumentException(
                        "Template dimensions and sample arrays must describe one complete field.",
                        nameof(templateDensities));
                }
                if ((uint)templateAnchor.x >= templateSize.x
                    || (uint)templateAnchor.y >= templateSize.y
                    || (uint)templateAnchor.z >= templateSize.z)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(templateAnchor),
                        "The template anchor must be inside its dimensions.");
                }
            }

            RoomSize = hasTemplate ? templateSize : roomSize;
            WallThickness = Math.Max(
                1,
                Math.Min(
                    wallThickness,
                    Math.Min(roomSize.x, roomSize.z) / 2 - 2));
            FoundationDepth = Math.Max(0, foundationDepth);
            EntranceWidth = MakeOdd(Math.Max(1, entranceWidth));
            EntranceHeight = Math.Max(2, Math.Min(entranceHeight, roomSize.y - 2));
            EntranceLength = Math.Max(0, entranceLength);
            HasTemplate = hasTemplate;
            TemplateSize = hasTemplate ? templateSize : default(Vector3Int);
            TemplateAnchor = hasTemplate ? templateAnchor : default(Vector3Int);
            this.templateDensities = hasTemplate
                ? (float[])templateDensities.Clone()
                : null;
            this.templateTypes = hasTemplate
                ? (VoxelTypeId[])templateTypes.Clone()
                : null;

            if (hasTemplate)
            {
                int negativeX = templateAnchor.x;
                int positiveX = templateSize.x - 1 - templateAnchor.x;
                int negativeZ = templateAnchor.z;
                int positiveZ = templateSize.z - 1 - templateAnchor.z
                    + EntranceLength;
                MaximumHorizontalInfluence = Math.Max(
                    Math.Max(negativeX, positiveX),
                    Math.Max(negativeZ, positiveZ));
            }
            else
            {
                int maximumHalfSize = Math.Max(roomSize.x / 2, roomSize.z / 2);
                MaximumHorizontalInfluence = maximumHalfSize + EntranceLength;
            }
            int regionVoxelSize = RegionSizeInChunks * VoxelColumnChunkData.Width;
            if (regionVoxelSize <= MaximumHorizontalInfluence * 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(regionSizeInChunks),
                    "The placement region must be wider than the structure and its entrance influence.");
            }
        }

        public string StableId { get; }
        public VoxelTypeId StructureType { get; }
        public int SeedSalt { get; }
        public int RegionSizeInChunks { get; }
        public float PlacementChance { get; }
        public int MinFloorHeight { get; }
        public int MaxFloorHeight { get; }
        public Vector3Int RoomSize { get; }
        public int WallThickness { get; }
        public int FoundationDepth { get; }
        public int EntranceWidth { get; }
        public int EntranceHeight { get; }
        public int EntranceLength { get; }
        public int MaximumHorizontalInfluence { get; }
        public bool HasTemplate { get; }
        public Vector3Int TemplateSize { get; }
        public Vector3Int TemplateAnchor { get; }

        private readonly float[] templateDensities;
        private readonly VoxelTypeId[] templateTypes;

        internal VoxelSample GetTemplateSample(int x, int y, int z)
        {
            if (!HasTemplate
                || (uint)x >= TemplateSize.x
                || (uint)y >= TemplateSize.y
                || (uint)z >= TemplateSize.z)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    "Template coordinate is outside the structure field.");
            }
            int index = x + TemplateSize.x * (y + TemplateSize.y * z);
            return new VoxelSample(templateDensities[index], templateTypes[index]);
        }

        private static int MakeOdd(int value)
        {
            return (value & 1) == 0 ? value + 1 : value;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
