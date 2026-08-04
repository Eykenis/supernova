using System;
using System.Collections.Generic;
using System.Threading;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Replays deterministic structure regions for each target column and writes
    /// only the intersecting samples. Results do not depend on streaming order.
    /// </summary>
    public static class MinecraftStructureFeatureGenerator
    {
        public readonly struct Placement
        {
            public Placement(
                int regionX,
                int regionZ,
                Vector3Int centre,
                int quarterTurns)
            {
                RegionX = regionX;
                RegionZ = regionZ;
                Centre = centre;
                QuarterTurns = quarterTurns;
            }

            public int RegionX { get; }
            public int RegionZ { get; }
            public Vector3Int Centre { get; }
            public int QuarterTurns { get; }
        }

        public static int GenerateColumn(
            Vector3Int columnCoordinate,
            float[] densities,
            VoxelTypeId[] types,
            int worldSeed,
            IReadOnlyList<MinecraftStructureFeatureSettings> features,
            float solidDensity,
            float airDensity,
            CancellationToken cancellationToken = default)
        {
            if (densities == null)
            {
                throw new ArgumentNullException(nameof(densities));
            }
            if (types == null)
            {
                throw new ArgumentNullException(nameof(types));
            }
            if (densities.Length != VoxelColumnChunkData.VoxelCount)
            {
                throw new ArgumentException(
                    $"Density array must contain {VoxelColumnChunkData.VoxelCount} samples.",
                    nameof(densities));
            }
            if (types.Length != VoxelColumnChunkData.VoxelCount)
            {
                throw new ArgumentException(
                    $"Type array must contain {VoxelColumnChunkData.VoxelCount} samples.",
                    nameof(types));
            }
            if (features == null || features.Count == 0)
            {
                return 0;
            }

            int targetMinX = columnCoordinate.x * VoxelColumnChunkData.Width;
            int targetMinZ = columnCoordinate.z * VoxelColumnChunkData.Depth;
            int targetMaxX = targetMinX + VoxelColumnChunkData.Width - 1;
            int targetMaxZ = targetMinZ + VoxelColumnChunkData.Depth - 1;
            int changed = 0;

            for (int featureIndex = 0; featureIndex < features.Count; featureIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MinecraftStructureFeatureSettings feature = features[featureIndex];
                if (feature.PlacementChance <= 0f)
                {
                    continue;
                }

                int regionSize = feature.RegionSizeInChunks
                    * VoxelColumnChunkData.Width;
                int radius = feature.MaximumHorizontalInfluence;
                int minRegionX = FloorDiv(targetMinX - radius, regionSize);
                int maxRegionX = FloorDiv(targetMaxX + radius, regionSize);
                int minRegionZ = FloorDiv(targetMinZ - radius, regionSize);
                int maxRegionZ = FloorDiv(targetMaxZ + radius, regionSize);

                for (int regionZ = minRegionZ; regionZ <= maxRegionZ; regionZ++)
                {
                    for (int regionX = minRegionX; regionX <= maxRegionX; regionX++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!TryGetPlacement(
                            feature,
                            worldSeed,
                            regionX,
                            regionZ,
                            out Placement placement))
                        {
                            continue;
                        }

                        changed += ApplyPlacementToColumn(
                            feature,
                            placement,
                            targetMinX,
                            targetMaxX,
                            targetMinZ,
                            targetMaxZ,
                            densities,
                            types,
                            solidDensity,
                            airDensity,
                            cancellationToken);
                    }
                }
            }

            return changed;
        }

        /// <summary>
        /// Returns the single deterministic candidate owned by a placement region.
        /// Useful for tests, debugging, and future structure-locator UI.
        /// </summary>
        public static bool TryGetPlacement(
            MinecraftStructureFeatureSettings feature,
            int worldSeed,
            int regionX,
            int regionZ,
            out Placement placement)
        {
            var random = new DeterministicRandom(BuildSeed(
                worldSeed,
                feature.SeedSalt,
                regionX,
                regionZ));
            if (random.NextDouble() >= feature.PlacementChance)
            {
                placement = default;
                return false;
            }

            int regionSize = feature.RegionSizeInChunks
                * VoxelColumnChunkData.Width;
            int margin = feature.MaximumHorizontalInfluence;
            int offsetRange = regionSize - margin * 2;
            if (offsetRange <= 0)
            {
                placement = default;
                return false;
            }

            int centreX = regionX * regionSize
                + margin
                + random.NextInt(offsetRange);
            int centreZ = regionZ * regionSize
                + margin
                + random.NextInt(offsetRange);
            int floorY = feature.MinFloorHeight
                + random.NextInt(feature.MaxFloorHeight - feature.MinFloorHeight + 1);
            int quarterTurns = random.NextInt(4);
            placement = new Placement(
                regionX,
                regionZ,
                new Vector3Int(centreX, floorY, centreZ),
                quarterTurns);
            return true;
        }

        private static int ApplyPlacementToColumn(
            MinecraftStructureFeatureSettings feature,
            Placement placement,
            int targetMinX,
            int targetMaxX,
            int targetMinZ,
            int targetMaxZ,
            float[] densities,
            VoxelTypeId[] types,
            float solidDensity,
            float airDensity,
            CancellationToken cancellationToken)
        {
            int radius = feature.MaximumHorizontalInfluence;
            int minX = Math.Max(targetMinX, placement.Centre.x - radius);
            int maxX = Math.Min(targetMaxX, placement.Centre.x + radius);
            int minZ = Math.Max(targetMinZ, placement.Centre.z - radius);
            int maxZ = Math.Min(targetMaxZ, placement.Centre.z + radius);
            int minimumLocalY = feature.HasTemplate
                ? -feature.TemplateAnchor.y
                : -feature.FoundationDepth;
            int maximumLocalY = feature.HasTemplate
                ? feature.TemplateSize.y - 1 - feature.TemplateAnchor.y
                : feature.RoomSize.y - 1;
            int minY = Math.Max(1, placement.Centre.y + minimumLocalY);
            int maxY = Math.Min(
                VoxelColumnChunkData.Height - 2,
                placement.Centre.y + maximumLocalY);
            if (minX > maxX || minZ > maxZ || minY > maxY)
            {
                return 0;
            }

            int changed = 0;
            for (int worldZ = minZ; worldZ <= maxZ; worldZ++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int localColumnZ = worldZ - targetMinZ;
                for (int worldX = minX; worldX <= maxX; worldX++)
                {
                    int localColumnX = worldX - targetMinX;
                    InverseRotate(
                        worldX - placement.Centre.x,
                        worldZ - placement.Centre.z,
                        placement.QuarterTurns,
                        out int structureX,
                        out int structureZ);

                    for (int worldY = minY; worldY <= maxY; worldY++)
                    {
                        int structureY = worldY - placement.Centre.y;
                        if (!TryEvaluateSample(
                            feature,
                            structureX,
                            structureY,
                            structureZ,
                            solidDensity,
                            airDensity,
                            out float targetDensity,
                            out VoxelTypeId targetType))
                        {
                            continue;
                        }

                        int index = VoxelColumnChunkData.ToIndex(
                            localColumnX,
                            worldY,
                            localColumnZ);
                        if (densities[index] == targetDensity
                            && types[index] == targetType)
                        {
                            continue;
                        }

                        densities[index] = targetDensity;
                        types[index] = targetType;
                        changed++;
                    }
                }
            }
            return changed;
        }

        private static bool TryEvaluateSample(
            MinecraftStructureFeatureSettings feature,
            int x,
            int y,
            int z,
            float solidDensity,
            float airDensity,
            out float density,
            out VoxelTypeId type)
        {
            if (feature.HasTemplate)
            {
                int positiveZ = feature.TemplateSize.z - 1
                    - feature.TemplateAnchor.z;
                int templateDoorwayStartZ = positiveZ - feature.WallThickness + 1;
                bool insideTemplateEntrance = Math.Abs(x) <= feature.EntranceWidth / 2
                    && z >= templateDoorwayStartZ
                    && z <= positiveZ + feature.EntranceLength
                    && y >= 0
                    && y <= feature.EntranceHeight;
                if (insideTemplateEntrance)
                {
                    bool entranceFloor = y == 0;
                    density = entranceFloor ? solidDensity : airDensity;
                    type = entranceFloor
                        ? feature.StructureType
                        : VoxelTypeId.Air;
                    return true;
                }

                int templateX = x + feature.TemplateAnchor.x;
                int templateY = y + feature.TemplateAnchor.y;
                int templateZ = z + feature.TemplateAnchor.z;
                if ((uint)templateX >= feature.TemplateSize.x
                    || (uint)templateY >= feature.TemplateSize.y
                    || (uint)templateZ >= feature.TemplateSize.z)
                {
                    density = default;
                    type = default;
                    return false;
                }

                VoxelSample sample = feature.GetTemplateSample(
                    templateX,
                    templateY,
                    templateZ);
                density = sample.Density;
                type = sample.Density >= 0f ? sample.Type : VoxelTypeId.Air;
                return true;
            }

            int halfX = feature.RoomSize.x / 2;
            int halfZ = feature.RoomSize.z / 2;
            bool insideRoomXz = Math.Abs(x) <= halfX && Math.Abs(z) <= halfZ;
            bool insideRoomY = y >= -feature.FoundationDepth
                && y < feature.RoomSize.y;

            int doorwayStartZ = halfZ - feature.WallThickness + 1;
            bool insideEntrance = Math.Abs(x) <= feature.EntranceWidth / 2
                && z >= doorwayStartZ
                && z <= halfZ + feature.EntranceLength
                && y >= 0
                && y <= feature.EntranceHeight;
            if (insideEntrance)
            {
                bool entranceFloor = y == 0;
                density = entranceFloor ? solidDensity : airDensity;
                type = entranceFloor
                    ? feature.StructureType
                    : VoxelTypeId.Air;
                return true;
            }

            if (!insideRoomXz || !insideRoomY)
            {
                density = default;
                type = default;
                return false;
            }
            if (y < 0)
            {
                density = solidDensity;
                type = feature.StructureType;
                return true;
            }

            bool shell = y == 0
                || y >= feature.RoomSize.y - feature.WallThickness
                || Math.Abs(x) > halfX - feature.WallThickness
                || Math.Abs(z) > halfZ - feature.WallThickness;
            if (shell)
            {
                density = solidDensity;
                type = feature.StructureType;
                return true;
            }

            int pillarX = Math.Max(2, halfX - 3);
            int pillarZ = Math.Max(2, halfZ - 3);
            bool pillar = Math.Abs(x) == pillarX
                && Math.Abs(z) == pillarZ
                && y > 0
                && y < feature.RoomSize.y - feature.WallThickness;
            bool centralDais = Math.Abs(x) <= 2
                && Math.Abs(z) <= 2
                && y == 1;
            bool solid = pillar || centralDais;
            density = solid ? solidDensity : airDensity;
            type = solid ? feature.StructureType : VoxelTypeId.Air;
            return true;
        }

        private static void InverseRotate(
            int x,
            int z,
            int quarterTurns,
            out int localX,
            out int localZ)
        {
            switch (quarterTurns & 3)
            {
                case 1:
                    localX = z;
                    localZ = -x;
                    break;
                case 2:
                    localX = -x;
                    localZ = -z;
                    break;
                case 3:
                    localX = -z;
                    localZ = x;
                    break;
                default:
                    localX = x;
                    localZ = z;
                    break;
            }
        }

        private static ulong BuildSeed(
            int worldSeed,
            int seedSalt,
            int regionX,
            int regionZ)
        {
            ulong value = (uint)worldSeed;
            value ^= (ulong)(uint)seedSalt * 0x9E3779B185EBCA87UL;
            value ^= (ulong)(uint)regionX * 0xC2B2AE3D27D4EB4FUL;
            value ^= (ulong)(uint)regionZ * 0x165667B19E3779F9UL;
            return Mix(value);
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private struct DeterministicRandom
        {
            private ulong state;

            public DeterministicRandom(ulong seed)
            {
                state = seed;
            }

            public int NextInt(int maximumExclusive)
            {
                if (maximumExclusive <= 0)
                {
                    return 0;
                }
                return (int)(NextUInt64() % (ulong)maximumExclusive);
            }

            public double NextDouble()
            {
                return (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);
            }

            private ulong NextUInt64()
            {
                state += 0x9E3779B97F4A7C15UL;
                return Mix(state);
            }
        }
    }
}
