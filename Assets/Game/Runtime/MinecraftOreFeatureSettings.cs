using System;
using System.Collections.Generic;
using Supernova.Voxels;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Thread-safe snapshot of one configured normal-ore pass.
    /// </summary>
    public readonly struct MinecraftOreFeatureSettings
    {
        public enum HeightDistribution
        {
            Uniform,
            Trapezoid,
        }

        private readonly VoxelTypeId[] replaceableTypes;

        public MinecraftOreFeatureSettings(
            VoxelTypeId resultType,
            IEnumerable<VoxelTypeId> replaceableTypes,
            int seedSalt,
            int attemptsPerRegion,
            float placementChance,
            HeightDistribution heightDistribution,
            int minHeight,
            int maxHeight,
            int plateau,
            int size,
            float discardChanceOnAirExposure)
        {
            if (resultType.IsAir)
            {
                throw new ArgumentException(
                    "An ore feature cannot generate air.",
                    nameof(resultType));
            }

            var replacements = new List<VoxelTypeId>();
            if (replaceableTypes != null)
            {
                foreach (VoxelTypeId type in replaceableTypes)
                {
                    if (!type.IsAir && !replacements.Contains(type))
                    {
                        replacements.Add(type);
                    }
                }
            }
            if (replacements.Count == 0)
            {
                throw new ArgumentException(
                    "At least one solid replaceable voxel type is required.",
                    nameof(replaceableTypes));
            }

            ResultType = resultType;
            this.replaceableTypes = replacements.ToArray();
            SeedSalt = seedSalt;
            AttemptsPerRegion = Math.Max(0, attemptsPerRegion);
            PlacementChance = Clamp01(placementChance);
            Distribution = heightDistribution;
            MinHeight = Math.Min(minHeight, maxHeight);
            MaxHeight = Math.Max(minHeight, maxHeight);
            Plateau = Math.Max(0, plateau);
            Size = Math.Max(1, Math.Min(64, size));
            DiscardChanceOnAirExposure = Clamp01(
                discardChanceOnAirExposure);
        }

        public VoxelTypeId ResultType { get; }
        public IReadOnlyList<VoxelTypeId> ReplaceableTypes => replaceableTypes;
        public int SeedSalt { get; }
        public int AttemptsPerRegion { get; }
        public float PlacementChance { get; }
        public HeightDistribution Distribution { get; }
        public int MinHeight { get; }
        public int MaxHeight { get; }
        public int Plateau { get; }
        public int Size { get; }
        public float DiscardChanceOnAirExposure { get; }

        internal bool CanReplace(VoxelTypeId type)
        {
            for (int i = 0; i < replaceableTypes.Length; i++)
            {
                if (replaceableTypes[i] == type)
                {
                    return true;
                }
            }
            return false;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
