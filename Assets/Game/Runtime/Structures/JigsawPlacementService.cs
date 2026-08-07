using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// How candidate start points for a structure family are chosen. Placement is
    /// deliberately separate from layout so previews, locator queries and actual
    /// generation all share one deterministic candidate computation.
    /// </summary>
    public enum JigsawPlacementStrategy
    {
        /// <summary>
        /// The world is divided into square regions and each region offers at
        /// most one candidate. This is the default and suits mineshafts, caves
        /// and any structure that should appear at a steady density.
        /// </summary>
        RandomSpread,

        /// <summary>
        /// A fixed number of candidates are distributed over rings centred on the
        /// world origin, spaced at roughly even angles with seeded jitter. This
        /// gives an explorable, predictable discovery density outwards from
        /// spawn instead of uniform scatter.
        /// </summary>
        ConcentricRings,

        /// <summary>
        /// A single deterministic placement centred on the world origin. This is
        /// used for spawn-owned structures that must exist exactly once.
        /// </summary>
        FixedOrigin,
    }

    /// <summary>
    /// Deterministic candidate selection for jigsaw structures. Every query is a
    /// pure function of the world seed and the feature's placement settings, so
    /// results never depend on chunk streaming order.
    /// </summary>
    public static class JigsawPlacementService
    {
        private const int MaximumCachedRingSets = 32;

        private static readonly
            ConcurrentDictionary<RingCacheKey, Lazy<RingPlacementSet>> RingCache =
                new ConcurrentDictionary<RingCacheKey, Lazy<RingPlacementSet>>();
        private static readonly ConcurrentQueue<RingCacheKey> RingCacheOrder =
            new ConcurrentQueue<RingCacheKey>();

        public static int CachedRingSetCount => RingCache.Count;

        public static void ClearCaches()
        {
            RingCache.Clear();
            while (RingCacheOrder.TryDequeue(out _))
            {
            }
        }

        /// <summary>
        /// Yields every candidate of one feature that could reach the given
        /// horizontal window, regardless of which placement strategy it uses.
        /// </summary>
        public static void CollectPlacements(
            JigsawStructureFeatureSettings feature,
            int worldSeed,
            int minWorldX,
            int minWorldZ,
            int maxWorldX,
            int maxWorldZ,
            List<JigsawStructureGenerator.Placement> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }
            results.Clear();
            if (feature.PlacementChance <= 0f)
            {
                return;
            }

            if (feature.PlacementStrategy
                == JigsawPlacementStrategy.ConcentricRings)
            {
                CollectRingPlacements(
                    feature,
                    worldSeed,
                    minWorldX,
                    minWorldZ,
                    maxWorldX,
                    maxWorldZ,
                    results);
                return;
            }

            if (feature.PlacementStrategy
                == JigsawPlacementStrategy.FixedOrigin)
            {
                CollectFixedOriginPlacement(
                    feature,
                    minWorldX,
                    minWorldZ,
                    maxWorldX,
                    maxWorldZ,
                    results);
                return;
            }

            int regionSize = feature.RegionSizeInChunks
                * VoxelColumnChunkData.Width;
            int influence = feature.MaxHorizontalDistance;
            int minRegionX = FloorDiv(minWorldX - influence, regionSize);
            int maxRegionX = FloorDiv(maxWorldX + influence, regionSize);
            int minRegionZ = FloorDiv(minWorldZ - influence, regionSize);
            int maxRegionZ = FloorDiv(maxWorldZ + influence, regionSize);
            for (int regionZ = minRegionZ; regionZ <= maxRegionZ; regionZ++)
            {
                for (int regionX = minRegionX; regionX <= maxRegionX; regionX++)
                {
                    if (TryGetRandomSpreadPlacement(
                        feature,
                        worldSeed,
                        regionX,
                        regionZ,
                        out JigsawStructureGenerator.Placement placement))
                    {
                        results.Add(placement);
                    }
                }
            }
        }

        /// <summary>
        /// Random-spread candidate for one region. The centre keeps a margin of
        /// <c>MaxHorizontalDistance</c> from the region edge so a layout cannot
        /// escape the region that owns it.
        /// </summary>
        public static bool TryGetRandomSpreadPlacement(
            JigsawStructureFeatureSettings feature,
            int worldSeed,
            int regionX,
            int regionZ,
            out JigsawStructureGenerator.Placement placement)
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
            int margin = feature.AllowLayoutOutsidePlacementRegion
                ? 0
                : feature.MaxHorizontalDistance;
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
            placement = new JigsawStructureGenerator.Placement(
                regionX,
                regionZ,
                new Vector3Int(
                    centreX,
                    NextFloorHeight(feature, ref random),
                    centreZ));
            return true;
        }

        public static bool TryGetFixedOriginPlacement(
            JigsawStructureFeatureSettings feature,
            out JigsawStructureGenerator.Placement placement)
        {
            if (feature.PlacementChance <= 0f)
            {
                placement = default;
                return false;
            }

            placement = new JigsawStructureGenerator.Placement(
                0,
                0,
                new Vector3Int(0, feature.MinFloorHeight, 0));
            return true;
        }

        private static void CollectFixedOriginPlacement(
            JigsawStructureFeatureSettings feature,
            int minWorldX,
            int minWorldZ,
            int maxWorldX,
            int maxWorldZ,
            List<JigsawStructureGenerator.Placement> results)
        {
            if (!TryGetFixedOriginPlacement(feature, out var placement))
            {
                return;
            }

            int influence = feature.MaxHorizontalDistance;
            if (placement.Centre.x < minWorldX - influence
                || placement.Centre.x > maxWorldX + influence
                || placement.Centre.z < minWorldZ - influence
                || placement.Centre.z > maxWorldZ + influence)
            {
                return;
            }

            results.Add(placement);
        }

        private static void CollectRingPlacements(
            JigsawStructureFeatureSettings feature,
            int worldSeed,
            int minWorldX,
            int minWorldZ,
            int maxWorldX,
            int maxWorldZ,
            List<JigsawStructureGenerator.Placement> results)
        {
            RingPlacementSet set = GetOrCreateRingSet(feature, worldSeed);
            int influence = feature.MaxHorizontalDistance;
            set.Collect(
                minWorldX - influence,
                minWorldZ - influence,
                maxWorldX + influence,
                maxWorldZ + influence,
                results);
        }

        private static RingPlacementSet GetOrCreateRingSet(
            JigsawStructureFeatureSettings feature,
            int worldSeed)
        {
            var key = new RingCacheKey(feature.ContentHash, worldSeed);
            Lazy<RingPlacementSet> lazy = RingCache.GetOrAdd(
                key,
                cacheKey =>
                {
                    RingCacheOrder.Enqueue(cacheKey);
                    TrimRingCache();
                    return new Lazy<RingPlacementSet>(
                        () => BuildRingSet(feature, worldSeed),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                });
            return lazy.Value;
        }

        /// <summary>
        /// Distributes <c>RingStructureCount</c> candidates over
        /// <c>RingCount</c> rings. Each ring holds an increasing share of the
        /// total and spaces its members at near-even angles with seeded jitter,
        /// so coverage widens steadily with distance from the origin.
        /// </summary>
        private static RingPlacementSet BuildRingSet(
            JigsawStructureFeatureSettings feature,
            int worldSeed)
        {
            var random = new DeterministicRandom(BuildSeed(
                worldSeed,
                feature.SeedSalt ^ unchecked((int)0xA5A5C3C3),
                0,
                0));
            int ringCount = feature.RingCount;
            int remaining = feature.RingStructureCount;
            int ringDistance = feature.RingDistanceInChunks
                * VoxelColumnChunkData.Width;
            int ringSpread = feature.RingSpreadInChunks
                * VoxelColumnChunkData.Width;
            var placements = new List<JigsawStructureGenerator.Placement>(
                feature.RingStructureCount);

            // Vanilla-style share growth: ring n receives a slice proportional
            // to its index, which keeps the outer rings from becoming sparse.
            int totalShare = ringCount * (ringCount + 1) / 2;
            for (int ring = 0; ring < ringCount && remaining > 0; ring++)
            {
                int share = ring == ringCount - 1
                    ? remaining
                    : Math.Max(
                        1,
                        feature.RingStructureCount * (ring + 1) / totalShare);
                share = Math.Min(share, remaining);
                double angleOffset = random.NextDouble() * Math.PI * 2.0;
                double radius = (ring + 1) * ringDistance;
                for (int slot = 0; slot < share; slot++)
                {
                    double angle = angleOffset
                        + slot * (Math.PI * 2.0 / share)
                        + (random.NextDouble() - 0.5)
                            * (Math.PI * 2.0 / share)
                            * 0.5;
                    double slotRadius = radius
                        + (random.NextDouble() - 0.5) * 2.0 * ringSpread;
                    int centreX = (int)Math.Round(Math.Cos(angle) * slotRadius);
                    int centreZ = (int)Math.Round(Math.Sin(angle) * slotRadius);
                    placements.Add(new JigsawStructureGenerator.Placement(
                        ring,
                        slot,
                        new Vector3Int(
                            centreX,
                            NextFloorHeight(feature, ref random),
                            centreZ)));
                }
                remaining -= share;
            }
            return new RingPlacementSet(placements);
        }

        /// <summary>
        /// Picks at most one winner among features that share a structure set,
        /// so competing families never stack on the same candidate cell.
        /// Features outside any set always win their own cell.
        /// </summary>
        public static bool WinsStructureSet(
            IReadOnlyList<JigsawStructureFeatureSettings> features,
            int candidateIndex,
            int worldSeed,
            int regionX,
            int regionZ)
        {
            if (features == null)
            {
                throw new ArgumentNullException(nameof(features));
            }
            JigsawStructureFeatureSettings candidate = features[candidateIndex];
            if (!candidate.HasStructureSet)
            {
                return true;
            }

            int totalWeight = 0;
            for (int i = 0; i < features.Count; i++)
            {
                if (SharesSet(features[i], candidate))
                {
                    totalWeight += features[i].StructureSetWeight;
                }
            }
            if (totalWeight <= 0)
            {
                return true;
            }

            // Keyed on the set name rather than any single member, so the winner
            // is stable no matter which order the features were authored in.
            var random = new DeterministicRandom(BuildSeed(
                worldSeed,
                HashSetId(candidate.StructureSetId),
                regionX,
                regionZ));
            int roll = random.NextInt(totalWeight);
            for (int i = 0; i < features.Count; i++)
            {
                if (!SharesSet(features[i], candidate))
                {
                    continue;
                }
                int weight = features[i].StructureSetWeight;
                if (roll < weight)
                {
                    return i == candidateIndex;
                }
                roll -= weight;
            }
            return false;
        }

        private static bool SharesSet(
            JigsawStructureFeatureSettings feature,
            JigsawStructureFeatureSettings candidate)
        {
            return feature.HasStructureSet
                && string.Equals(
                    feature.StructureSetId,
                    candidate.StructureSetId,
                    StringComparison.Ordinal);
        }

        private static int NextFloorHeight(
            JigsawStructureFeatureSettings feature,
            ref DeterministicRandom random)
        {
            return feature.MinFloorHeight
                + random.NextInt(
                    feature.MaxFloorHeight - feature.MinFloorHeight + 1);
        }

        private static void TrimRingCache()
        {
            while (RingCache.Count >= MaximumCachedRingSets
                && RingCacheOrder.TryDequeue(out RingCacheKey oldest))
            {
                RingCache.TryRemove(oldest, out _);
            }
        }

        private static int HashSetId(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }
                return (int)hash;
            }
        }

        internal static ulong BuildSeed(
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

        internal static ulong Mix(ulong value)
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

        /// <summary>
        /// All ring candidates for one world, bucketed into a coarse grid so a
        /// column query does not scan the whole set.
        /// </summary>
        private sealed class RingPlacementSet
        {
            private const int CellSize = 512;

            private readonly Dictionary<long, List<JigsawStructureGenerator.Placement>>
                buckets = new Dictionary<long, List<JigsawStructureGenerator.Placement>>();

            public RingPlacementSet(
                IReadOnlyList<JigsawStructureGenerator.Placement> placements)
            {
                Placements = placements;
                for (int i = 0; i < placements.Count; i++)
                {
                    JigsawStructureGenerator.Placement placement = placements[i];
                    long key = CellKey(
                        FloorDiv(placement.Centre.x, CellSize),
                        FloorDiv(placement.Centre.z, CellSize));
                    if (!buckets.TryGetValue(
                        key,
                        out List<JigsawStructureGenerator.Placement> list))
                    {
                        list = new List<JigsawStructureGenerator.Placement>();
                        buckets.Add(key, list);
                    }
                    list.Add(placement);
                }
            }

            public IReadOnlyList<JigsawStructureGenerator.Placement> Placements
            {
                get;
            }

            public void Collect(
                int minX,
                int minZ,
                int maxX,
                int maxZ,
                List<JigsawStructureGenerator.Placement> results)
            {
                int minCellX = FloorDiv(minX, CellSize);
                int maxCellX = FloorDiv(maxX, CellSize);
                int minCellZ = FloorDiv(minZ, CellSize);
                int maxCellZ = FloorDiv(maxZ, CellSize);
                for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
                {
                    for (int cellX = minCellX; cellX <= maxCellX; cellX++)
                    {
                        if (!buckets.TryGetValue(
                            CellKey(cellX, cellZ),
                            out List<JigsawStructureGenerator.Placement> list))
                        {
                            continue;
                        }
                        for (int i = 0; i < list.Count; i++)
                        {
                            JigsawStructureGenerator.Placement placement = list[i];
                            if (placement.Centre.x < minX
                                || placement.Centre.x > maxX
                                || placement.Centre.z < minZ
                                || placement.Centre.z > maxZ)
                            {
                                continue;
                            }
                            results.Add(placement);
                        }
                    }
                }
            }

            private static long CellKey(int x, int z)
            {
                return ((long)x << 32) ^ (uint)z;
            }
        }

        private readonly struct RingCacheKey : IEquatable<RingCacheKey>
        {
            public RingCacheKey(ulong featureHash, int worldSeed)
            {
                FeatureHash = featureHash;
                WorldSeed = worldSeed;
            }

            private ulong FeatureHash { get; }
            private int WorldSeed { get; }

            public bool Equals(RingCacheKey other)
            {
                return FeatureHash == other.FeatureHash
                    && WorldSeed == other.WorldSeed;
            }

            public override bool Equals(object obj)
            {
                return obj is RingCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)FeatureHash ^ (int)(FeatureHash >> 32);
                    return hash * 397 ^ WorldSeed;
                }
            }
        }

        internal struct DeterministicRandom
        {
            private ulong state;

            public DeterministicRandom(ulong seed)
            {
                state = seed;
            }

            public int NextInt(int maximumExclusive)
            {
                if (maximumExclusive <= 0) return 0;
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
