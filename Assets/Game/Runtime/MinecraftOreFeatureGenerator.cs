using System;
using System.Collections.Generic;
using System.Buffers;
using System.Threading;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Deterministic adaptation of Minecraft's normal OreFeature. Each target
    /// chunk replays nearby 16 x 16 placement regions and writes only its own
    /// type array, making cross-chunk veins independent of generation order.
    /// </summary>
    public static class MinecraftOreFeatureGenerator
    {
        public const int PlacementRegionSize = 16;

        public delegate float DensitySampler(int worldX, int worldY, int worldZ);

        private const double Inverse53BitRange =
            1.0 / 9007199254740992.0;

        public static int GenerateChunk(
            Vector3Int chunkCoordinate,
            float[] densities,
            VoxelTypeId[] types,
            int worldSeed,
            IReadOnlyList<MinecraftOreFeatureSettings> features,
            DensitySampler densitySampler = null,
            CancellationToken cancellationToken = default,
            DepthProbabilityProfile depthProbability = null)
        {
            Vector3Int targetOrigin = chunkCoordinate * VoxelVolume.Size;
            return GenerateRegion(
                targetOrigin,
                VoxelVolume.Size,
                VoxelVolume.Size,
                VoxelVolume.Size,
                densities,
                types,
                worldSeed,
                features,
                densitySampler,
                cancellationToken,
                depthProbability);
        }

        public static int GenerateColumn(
            Vector3Int columnCoordinate,
            float[] densities,
            VoxelTypeId[] types,
            int worldSeed,
            IReadOnlyList<MinecraftOreFeatureSettings> features,
            DensitySampler densitySampler = null,
            CancellationToken cancellationToken = default,
            DepthProbabilityProfile depthProbability = null)
        {
            var targetOrigin = new Vector3Int(
                columnCoordinate.x * VoxelColumnChunkData.Width,
                0,
                columnCoordinate.z * VoxelColumnChunkData.Depth);
            return GenerateRegion(
                targetOrigin,
                VoxelColumnChunkData.Width,
                VoxelColumnChunkData.Height,
                VoxelColumnChunkData.Depth,
                densities,
                types,
                worldSeed,
                features,
                densitySampler,
                cancellationToken,
                depthProbability);
        }

        private static int GenerateRegion(
            Vector3Int targetOrigin,
            int sizeX,
            int sizeY,
            int sizeZ,
            float[] densities,
            VoxelTypeId[] types,
            int worldSeed,
            IReadOnlyList<MinecraftOreFeatureSettings> features,
            DensitySampler densitySampler,
            CancellationToken cancellationToken,
            DepthProbabilityProfile depthProbability)
        {
            if (densities == null)
            {
                throw new ArgumentNullException(nameof(densities));
            }
            if (types == null)
            {
                throw new ArgumentNullException(nameof(types));
            }
            int sampleCount = sizeX * sizeY * sizeZ;
            if (densities.Length != sampleCount)
            {
                throw new ArgumentException(
                    $"Density array must contain {sampleCount} samples.",
                    nameof(densities));
            }
            if (types.Length != sampleCount)
            {
                throw new ArgumentException(
                    $"Type array must contain {sampleCount} samples.",
                    nameof(types));
            }
            if (features == null || features.Count == 0)
            {
                return 0;
            }

            int targetMaxX = targetOrigin.x + sizeX - 1;
            int targetMaxY = targetOrigin.y + sizeY - 1;
            int targetMaxZ = targetOrigin.z + sizeZ - 1;
            int[] visited = ArrayPool<int>.Shared.Rent(sampleCount);
            Array.Clear(visited, 0, sampleCount);
            try
            {
                int visitStamp = 0;
                int changed = 0;

                for (int featureIndex = 0; featureIndex < features.Count; featureIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MinecraftOreFeatureSettings feature = features[featureIndex];
                    if (feature.AttemptsPerRegion <= 0
                        || feature.PlacementChance <= 0f)
                    {
                        continue;
                    }

                    int influenceRadius = GetInfluenceRadius(feature.Size);
                    int minRegionX = CeilDiv(
                        targetOrigin.x - influenceRadius - (PlacementRegionSize - 1),
                        PlacementRegionSize);
                    int maxRegionX = FloorDiv(
                        targetMaxX + influenceRadius,
                        PlacementRegionSize);
                    int minRegionZ = CeilDiv(
                        targetOrigin.z - influenceRadius - (PlacementRegionSize - 1),
                        PlacementRegionSize);
                    int maxRegionZ = FloorDiv(
                        targetMaxZ + influenceRadius,
                        PlacementRegionSize);

                    for (int regionZ = minRegionZ; regionZ <= maxRegionZ; regionZ++)
                    {
                        for (int regionX = minRegionX; regionX <= maxRegionX; regionX++)
                        {
                            for (int attempt = 0;
                                attempt < feature.AttemptsPerRegion;
                                attempt++)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                ulong attemptSeed = BuildAttemptSeed(
                                    worldSeed,
                                    feature.SeedSalt,
                                    regionX,
                                    regionZ,
                                    attempt);
                                var random = new DeterministicRandom(attemptSeed);
                                double placementRoll = random.NextDouble();
                                if (depthProbability == null
                                    && placementRoll >= feature.PlacementChance)
                                {
                                    continue;
                                }

                                int originX = regionX * PlacementRegionSize
                                    + random.NextInt(PlacementRegionSize);
                                int originZ = regionZ * PlacementRegionSize
                                    + random.NextInt(PlacementRegionSize);
                                int originY = SampleHeight(feature, ref random);
                                if (depthProbability != null
                                    && placementRoll
                                        >= depthProbability.EvaluateProbability(
                                            feature.PlacementChance,
                                            originY,
                                            VoxelColumnChunkData.Height))
                                {
                                    continue;
                                }
                                Sphere[] spheres = BuildSpheres(
                                    originX,
                                    originY,
                                    originZ,
                                    feature.Size,
                                    ref random);

                                if (!IntersectsTargetY(
                                    spheres,
                                    targetOrigin.y,
                                    targetMaxY))
                                {
                                    continue;
                                }

                                if (visitStamp == int.MaxValue)
                                {
                                    Array.Clear(visited, 0, visited.Length);
                                    visitStamp = 0;
                                }
                                visitStamp++;
                                changed += ApplySpheres(
                                    spheres,
                                    attemptSeed,
                                    feature,
                                    targetOrigin,
                                    targetMaxX,
                                    targetMaxY,
                                    targetMaxZ,
                                    densities,
                                    types,
                                    visited,
                                    visitStamp,
                                    densitySampler,
                                    sizeX,
                                    sizeY,
                                    sizeZ);
                            }
                        }
                    }
                }

                return changed;
            }
            finally
            {
                ArrayPool<int>.Shared.Return(visited);
            }
        }

        private static Sphere[] BuildSpheres(
            int originX,
            int originY,
            int originZ,
            int size,
            ref DeterministicRandom random)
        {
            double angle = random.NextDouble() * Math.PI;
            double axisRadius = size / 8.0;
            double sin = Math.Sin(angle) * axisRadius;
            double cos = Math.Cos(angle) * axisRadius;
            double startX = originX + sin;
            double endX = originX - sin;
            double startZ = originZ + cos;
            double endZ = originZ - cos;
            double startY = originY + random.NextInt(3) - 2;
            double endY = originY + random.NextInt(3) - 2;
            var spheres = new Sphere[size];

            for (int i = 0; i < size; i++)
            {
                double progress = (double)i / size;
                double randomScale = random.NextDouble() * size / 16.0;
                double radius = (
                    (Math.Sin(Math.PI * progress) + 1.0) * randomScale
                    + 1.0) / 2.0;
                spheres[i] = new Sphere(
                    Lerp(startX, endX, progress),
                    Lerp(startY, endY, progress),
                    Lerp(startZ, endZ, progress),
                    radius);
            }

            for (int i = 0; i < spheres.Length - 1; i++)
            {
                if (!spheres[i].IsActive)
                {
                    continue;
                }
                for (int j = i + 1; j < spheres.Length; j++)
                {
                    if (!spheres[j].IsActive)
                    {
                        continue;
                    }

                    Sphere first = spheres[i];
                    Sphere second = spheres[j];
                    double radiusDelta = first.Radius - second.Radius;
                    double dx = first.X - second.X;
                    double dy = first.Y - second.Y;
                    double dz = first.Z - second.Z;
                    if (radiusDelta * radiusDelta
                        < dx * dx + dy * dy + dz * dz)
                    {
                        continue;
                    }

                    if (radiusDelta > 0.0)
                    {
                        spheres[j] = second.Disable();
                    }
                    else
                    {
                        spheres[i] = first.Disable();
                        break;
                    }
                }
            }

            return spheres;
        }

        private static int ApplySpheres(
            IReadOnlyList<Sphere> spheres,
            ulong attemptSeed,
            MinecraftOreFeatureSettings feature,
            Vector3Int targetOrigin,
            int targetMaxX,
            int targetMaxY,
            int targetMaxZ,
            float[] densities,
            VoxelTypeId[] types,
            int[] visited,
            int visitStamp,
            DensitySampler densitySampler,
            int sizeX,
            int sizeY,
            int sizeZ)
        {
            int changed = 0;
            for (int sphereIndex = 0; sphereIndex < spheres.Count; sphereIndex++)
            {
                Sphere sphere = spheres[sphereIndex];
                if (!sphere.IsActive)
                {
                    continue;
                }

                int minX = Math.Max(
                    targetOrigin.x,
                    (int)Math.Floor(sphere.X - sphere.Radius));
                int maxX = Math.Min(
                    targetMaxX,
                    (int)Math.Floor(sphere.X + sphere.Radius));
                int minY = Math.Max(
                    targetOrigin.y,
                    (int)Math.Floor(sphere.Y - sphere.Radius));
                int maxY = Math.Min(
                    targetMaxY,
                    (int)Math.Floor(sphere.Y + sphere.Radius));
                int minZ = Math.Max(
                    targetOrigin.z,
                    (int)Math.Floor(sphere.Z - sphere.Radius));
                int maxZ = Math.Min(
                    targetMaxZ,
                    (int)Math.Floor(sphere.Z + sphere.Radius));

                for (int worldZ = minZ; worldZ <= maxZ; worldZ++)
                {
                    double dz = (worldZ + 0.5 - sphere.Z) / sphere.Radius;
                    double dzSquared = dz * dz;
                    if (dzSquared >= 1.0)
                    {
                        continue;
                    }
                    for (int worldY = minY; worldY <= maxY; worldY++)
                    {
                        double dy = (worldY + 0.5 - sphere.Y) / sphere.Radius;
                        double dyAndDz = dy * dy + dzSquared;
                        if (dyAndDz >= 1.0)
                        {
                            continue;
                        }
                        for (int worldX = minX; worldX <= maxX; worldX++)
                        {
                            double dx =
                                (worldX + 0.5 - sphere.X) / sphere.Radius;
                            if (dx * dx + dyAndDz >= 1.0)
                            {
                                continue;
                            }

                            int localX = worldX - targetOrigin.x;
                            int localY = worldY - targetOrigin.y;
                            int localZ = worldZ - targetOrigin.z;
                            int index = ToIndex(
                                localX,
                                localY,
                                localZ,
                                sizeX,
                                sizeY);
                            if (visited[index] == visitStamp)
                            {
                                continue;
                            }
                            visited[index] = visitStamp;

                            if (densities[index] < 0f
                                || !feature.CanReplace(types[index]))
                            {
                                continue;
                            }

                            float discardChance =
                                feature.DiscardChanceOnAirExposure;
                            if (discardChance > 0f
                                && IsExposedToAir(
                                    worldX,
                                    worldY,
                                    worldZ,
                                    targetOrigin,
                                    densities,
                                    densitySampler,
                                    sizeX,
                                    sizeY,
                                    sizeZ)
                                && CoordinateRandom(
                                    attemptSeed,
                                    worldX,
                                    worldY,
                                    worldZ) < discardChance)
                            {
                                continue;
                            }

                            types[index] = feature.ResultType;
                            changed++;
                        }
                    }
                }
            }
            return changed;
        }

        private static bool IsExposedToAir(
            int worldX,
            int worldY,
            int worldZ,
            Vector3Int targetOrigin,
            float[] densities,
            DensitySampler densitySampler,
            int sizeX,
            int sizeY,
            int sizeZ)
        {
            return IsAir(worldX - 1, worldY, worldZ, targetOrigin, densities, densitySampler, sizeX, sizeY, sizeZ)
                || IsAir(worldX + 1, worldY, worldZ, targetOrigin, densities, densitySampler, sizeX, sizeY, sizeZ)
                || IsAir(worldX, worldY - 1, worldZ, targetOrigin, densities, densitySampler, sizeX, sizeY, sizeZ)
                || IsAir(worldX, worldY + 1, worldZ, targetOrigin, densities, densitySampler, sizeX, sizeY, sizeZ)
                || IsAir(worldX, worldY, worldZ - 1, targetOrigin, densities, densitySampler, sizeX, sizeY, sizeZ)
                || IsAir(worldX, worldY, worldZ + 1, targetOrigin, densities, densitySampler, sizeX, sizeY, sizeZ);
        }

        private static bool IsAir(
            int worldX,
            int worldY,
            int worldZ,
            Vector3Int targetOrigin,
            float[] densities,
            DensitySampler densitySampler,
            int sizeX,
            int sizeY,
            int sizeZ)
        {
            int localX = worldX - targetOrigin.x;
            int localY = worldY - targetOrigin.y;
            int localZ = worldZ - targetOrigin.z;
            if ((uint)localX < sizeX
                && (uint)localY < sizeY
                && (uint)localZ < sizeZ)
            {
                return densities[ToIndex(
                    localX,
                    localY,
                    localZ,
                    sizeX,
                    sizeY)] < 0f;
            }
            if (densitySampler == null)
            {
                throw new InvalidOperationException(
                    "A density sampler is required to evaluate air exposure "
                    + "outside the target chunk.");
            }
            return densitySampler(worldX, worldY, worldZ) < 0f;
        }

        private static int SampleHeight(
            MinecraftOreFeatureSettings feature,
            ref DeterministicRandom random)
        {
            int range = feature.MaxHeight - feature.MinHeight;
            if (range <= 0)
            {
                return feature.MinHeight;
            }
            if (feature.Distribution
                    == MinecraftOreFeatureSettings.HeightDistribution.Uniform
                || feature.Plateau >= range)
            {
                return feature.MinHeight + random.NextInt(range + 1);
            }

            int lowerSpan = (range - feature.Plateau) / 2;
            int upperSpan = range - lowerSpan;
            return feature.MinHeight
                + random.NextInt(upperSpan + 1)
                + random.NextInt(lowerSpan + 1);
        }

        private static bool IntersectsTargetY(
            IReadOnlyList<Sphere> spheres,
            int targetMinY,
            int targetMaxY)
        {
            for (int i = 0; i < spheres.Count; i++)
            {
                Sphere sphere = spheres[i];
                if (sphere.IsActive
                    && sphere.Y + sphere.Radius >= targetMinY
                    && sphere.Y - sphere.Radius <= targetMaxY + 1.0)
                {
                    return true;
                }
            }
            return false;
        }

        private static int GetInfluenceRadius(int size)
        {
            return (int)Math.Ceiling(size * 3.0 / 16.0 + 1.5);
        }

        private static int ToIndex(
            int x,
            int y,
            int z,
            int sizeX,
            int sizeY)
        {
            return x + sizeX * (y + sizeY * z);
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private static int CeilDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder > 0 ? quotient + 1 : quotient;
        }

        private static double Lerp(double start, double end, double progress)
        {
            return start + (end - start) * progress;
        }

        private static ulong BuildAttemptSeed(
            int worldSeed,
            int featureSalt,
            int regionX,
            int regionZ,
            int attempt)
        {
            unchecked
            {
                ulong seed = 0xD1B54A32D192ED03UL ^ (uint)worldSeed;
                seed = Mix64(seed + (uint)featureSalt);
                seed = Mix64(seed + (uint)regionX);
                seed = Mix64(seed + (uint)regionZ);
                return Mix64(seed + (uint)attempt);
            }
        }

        private static float CoordinateRandom(
            ulong attemptSeed,
            int x,
            int y,
            int z)
        {
            unchecked
            {
                ulong hash = Mix64(attemptSeed ^ (uint)x);
                hash = Mix64(hash ^ ((ulong)(uint)y << 1));
                hash = Mix64(hash ^ ((ulong)(uint)z << 2));
                return (float)((hash >> 11) * Inverse53BitRange);
            }
        }

        private static ulong Mix64(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return value;
        }

        private readonly struct Sphere
        {
            public Sphere(double x, double y, double z, double radius)
            {
                X = x;
                Y = y;
                Z = z;
                Radius = radius;
            }

            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public double Radius { get; }
            public bool IsActive => Radius >= 0.0;

            public Sphere Disable()
            {
                return new Sphere(X, Y, Z, -1.0);
            }
        }

        private struct DeterministicRandom
        {
            private ulong state;

            public DeterministicRandom(ulong seed)
            {
                state = seed;
            }

            public ulong NextUInt64()
            {
                state += 0x9E3779B97F4A7C15UL;
                return Mix64(state);
            }

            public double NextDouble()
            {
                return (NextUInt64() >> 11) * Inverse53BitRange;
            }

            public int NextInt(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(exclusiveMaximum));
                }
                return (int)(NextDouble() * exclusiveMaximum);
            }
        }
    }
}
