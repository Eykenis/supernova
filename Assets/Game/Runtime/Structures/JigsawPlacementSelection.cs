using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Supernova.Voxels;


namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Immutable, deterministic allow-list for jigsaw placements. It is built
    /// once before parallel column generation so strict placement never depends
    /// on the order in which worker tasks finish.
    /// </summary>
    public sealed class JigsawPlacementSelection
    {
        private readonly HashSet<PlacementKey> acceptedPlacements;

        private JigsawPlacementSelection(HashSet<PlacementKey> accepted)
        {
            acceptedPlacements = accepted ?? new HashSet<PlacementKey>();
        }

        public int AcceptedPlacementCount => acceptedPlacements.Count;

        public bool Allows(
            JigsawStructureFeatureSettings feature,
            JigsawStructureGenerator.Placement placement)
        {
            return acceptedPlacements.Contains(
                new PlacementKey(feature.ContentHash, placement));
        }

        public static JigsawPlacementSelection CreateNonIntersecting(
            IReadOnlyList<JigsawStructureFeatureSettings> features,
            int worldSeed,
            int minimumWorldX,
            int minimumWorldZ,
            int maximumWorldX,
            int maximumWorldZ,
            CancellationToken cancellationToken = default)
        {
            if (features == null)
            {
                throw new ArgumentNullException(nameof(features));
            }

            cancellationToken.ThrowIfCancellationRequested();
            int minX = Math.Min(minimumWorldX, maximumWorldX);
            int minZ = Math.Min(minimumWorldZ, maximumWorldZ);
            int maxX = Math.Max(minimumWorldX, maximumWorldX);
            int maxZ = Math.Max(minimumWorldZ, maximumWorldZ);
            List<Candidate> candidates = ListPool<Candidate>.Rent();
            List<JigsawStructureGenerator.Placement> placements =
                ListPool<JigsawStructureGenerator.Placement>.Rent();
            try
            {
                for (int featureIndex = 0;
                    featureIndex < features.Count;
                    featureIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    JigsawStructureFeatureSettings feature =
                        features[featureIndex];
                    if (feature.PlacementChance <= 0f)
                    {
                        continue;
                    }

                    JigsawPlacementService.CollectPlacements(
                        feature,
                        worldSeed,
                        minX,
                        minZ,
                        maxX,
                        maxZ,
                        placements);
                    for (int placementIndex = 0;
                        placementIndex < placements.Count;
                        placementIndex++)
                    {
                        if ((placementIndex & 15) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        JigsawStructureGenerator.Placement placement =
                            placements[placementIndex];
                        if (!JigsawPlacementService.WinsStructureSet(
                            features,
                            featureIndex,
                            worldSeed,
                            placement.RegionX,
                            placement.RegionZ))
                        {
                            continue;
                        }

                        IReadOnlyList<JigsawStructureGenerator.Piece> pieces =
                            JigsawStructureGenerator.BuildLayout(
                                feature,
                                worldSeed,
                                placement);
                        if (HasInternalIntersections(pieces)
                            || !IntersectsHorizontalWindow(
                                pieces,
                                minX,
                                minZ,
                                maxX,
                                maxZ))
                        {
                            continue;
                        }
                        candidates.Add(new Candidate(
                            featureIndex,
                            feature,
                            placement,
                            pieces));
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                candidates.Sort(CompareCandidates);
                var accepted = new HashSet<PlacementKey>();
                BoundsSpatialIndex occupiedPieces =
                    BoundsSpatialIndex.Rent();
                try
                {
                    for (int candidateIndex = 0;
                        candidateIndex < candidates.Count;
                        candidateIndex++)
                    {
                        if ((candidateIndex & 15) == 0)
                        {
                            cancellationToken
                                .ThrowIfCancellationRequested();
                        }

                        Candidate candidate = candidates[candidateIndex];
                        if (IntersectsAny(
                            candidate.Pieces,
                            occupiedPieces))
                        {
                            continue;
                        }

                        accepted.Add(new PlacementKey(
                            candidate.Feature.ContentHash,
                            candidate.Placement));
                        for (int pieceIndex = 0;
                            pieceIndex < candidate.Pieces.Count;
                            pieceIndex++)
                        {
                            occupiedPieces.Add(
                                candidate.Pieces[pieceIndex].Bounds);
                        }
                    }
                }
                finally
                {
                    BoundsSpatialIndex.Return(occupiedPieces);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return new JigsawPlacementSelection(accepted);
            }
            finally
            {
                ListPool<Candidate>.Return(candidates);
                ListPool<JigsawStructureGenerator.Placement>.Return(
                    placements);
            }
        }

        private static int CompareCandidates(Candidate left, Candidate right)
        {
            bool leftFixed = left.Feature.PlacementStrategy
                == JigsawPlacementStrategy.FixedOrigin;
            bool rightFixed = right.Feature.PlacementStrategy
                == JigsawPlacementStrategy.FixedOrigin;
            if (leftFixed != rightFixed)
            {
                return leftFixed ? -1 : 1;
            }

            int comparison = left.Placement.RegionZ.CompareTo(
                right.Placement.RegionZ);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = left.Placement.RegionX.CompareTo(
                right.Placement.RegionX);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = left.FeatureIndex.CompareTo(right.FeatureIndex);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = left.Placement.Centre.z.CompareTo(
                right.Placement.Centre.z);
            return comparison != 0
                ? comparison
                : left.Placement.Centre.x.CompareTo(
                    right.Placement.Centre.x);
        }

        private static bool IntersectsHorizontalWindow(
            IReadOnlyList<JigsawStructureGenerator.Piece> pieces,
            int minX,
            int minZ,
            int maxX,
            int maxZ)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                JigsawStructureGenerator.IntBounds bounds = pieces[i].Bounds;
                if (bounds.MinX <= maxX
                    && bounds.MaxX >= minX
                    && bounds.MinZ <= maxZ
                    && bounds.MaxZ >= minZ)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasInternalIntersections(
            IReadOnlyList<JigsawStructureGenerator.Piece> pieces)
        {
            BoundsSpatialIndex occupiedPieces =
                BoundsSpatialIndex.Rent();
            try
            {
                for (int pieceIndex = 0;
                    pieceIndex < pieces.Count;
                    pieceIndex++)
                {
                    JigsawStructureGenerator.IntBounds bounds =
                        pieces[pieceIndex].Bounds;
                    if (occupiedPieces.Intersects(bounds))
                    {
                        return true;
                    }

                    occupiedPieces.Add(bounds);
                }
                return false;
            }
            finally
            {
                BoundsSpatialIndex.Return(occupiedPieces);
            }
        }

        private static bool IntersectsAny(
            IReadOnlyList<JigsawStructureGenerator.Piece> candidatePieces,
            BoundsSpatialIndex occupiedPieces)
        {
            for (int candidateIndex = 0;
                candidateIndex < candidatePieces.Count;
                candidateIndex++)
            {
                if (occupiedPieces.Intersects(
                    candidatePieces[candidateIndex].Bounds))
                {
                    return true;
                }
            }
            return false;
        }

        private static class ListPool<T>
        {
            private static readonly ConcurrentBag<List<T>> Pool =
                new ConcurrentBag<List<T>>();

            public static List<T> Rent()
            {
                if (!Pool.TryTake(out List<T> list))
                {
                    return new List<T>();
                }

                list.Clear();
                return list;
            }

            public static void Return(List<T> list)
            {
                if (list == null)
                {
                    return;
                }

                list.Clear();
                Pool.Add(list);
            }
        }

        private sealed class BoundsSpatialIndex
        {
            private const int BucketSize = VoxelColumnChunkData.Width;
            private static readonly ConcurrentBag<BoundsSpatialIndex> Pool =
                new ConcurrentBag<BoundsSpatialIndex>();
            private static readonly ConcurrentBag<List<
                JigsawStructureGenerator.IntBounds>> EntryPool =
                    new ConcurrentBag<List<
                        JigsawStructureGenerator.IntBounds>>();

            private readonly Dictionary<SpatialBucketKey,
                List<JigsawStructureGenerator.IntBounds>> buckets =
                    new Dictionary<SpatialBucketKey,
                        List<JigsawStructureGenerator.IntBounds>>();

            public static BoundsSpatialIndex Rent()
            {
                return Pool.TryTake(out BoundsSpatialIndex index)
                    ? index
                    : new BoundsSpatialIndex();
            }

            public static void Return(BoundsSpatialIndex index)
            {
                if (index == null)
                {
                    return;
                }

                foreach (List<JigsawStructureGenerator.IntBounds> entries
                    in index.buckets.Values)
                {
                    entries.Clear();
                    EntryPool.Add(entries);
                }
                index.buckets.Clear();
                Pool.Add(index);
            }

            public void Add(JigsawStructureGenerator.IntBounds bounds)
            {
                int minimumBucketX = FloorDivide(bounds.MinX, BucketSize);
                int maximumBucketX = FloorDivide(bounds.MaxX, BucketSize);
                int minimumBucketY = FloorDivide(bounds.MinY, BucketSize);
                int maximumBucketY = FloorDivide(bounds.MaxY, BucketSize);
                int minimumBucketZ = FloorDivide(bounds.MinZ, BucketSize);
                int maximumBucketZ = FloorDivide(bounds.MaxZ, BucketSize);
                for (int bucketX = minimumBucketX;
                    bucketX <= maximumBucketX;
                    bucketX++)
                {
                    for (int bucketY = minimumBucketY;
                        bucketY <= maximumBucketY;
                        bucketY++)
                    {
                        for (int bucketZ = minimumBucketZ;
                            bucketZ <= maximumBucketZ;
                            bucketZ++)
                        {
                            var key = new SpatialBucketKey(
                                bucketX,
                                bucketY,
                                bucketZ);
                            if (!buckets.TryGetValue(
                                key,
                                out List<JigsawStructureGenerator.IntBounds>
                                    entries))
                            {
                                if (!EntryPool.TryTake(out entries))
                                {
                                    entries = new List<
                                        JigsawStructureGenerator.IntBounds>();
                                }
                                buckets.Add(key, entries);
                            }
                            entries.Add(bounds);
                        }
                    }
                }
            }

            public bool Intersects(
                JigsawStructureGenerator.IntBounds bounds)
            {
                int minimumBucketX = FloorDivide(bounds.MinX, BucketSize);
                int maximumBucketX = FloorDivide(bounds.MaxX, BucketSize);
                int minimumBucketY = FloorDivide(bounds.MinY, BucketSize);
                int maximumBucketY = FloorDivide(bounds.MaxY, BucketSize);
                int minimumBucketZ = FloorDivide(bounds.MinZ, BucketSize);
                int maximumBucketZ = FloorDivide(bounds.MaxZ, BucketSize);
                for (int bucketX = minimumBucketX;
                    bucketX <= maximumBucketX;
                    bucketX++)
                {
                    for (int bucketY = minimumBucketY;
                        bucketY <= maximumBucketY;
                        bucketY++)
                    {
                        for (int bucketZ = minimumBucketZ;
                            bucketZ <= maximumBucketZ;
                            bucketZ++)
                        {
                            var key = new SpatialBucketKey(
                                bucketX,
                                bucketY,
                                bucketZ);
                            if (!buckets.TryGetValue(
                                key,
                                out List<JigsawStructureGenerator.IntBounds>
                                    entries))
                            {
                                continue;
                            }

                            for (int entryIndex = 0;
                                entryIndex < entries.Count;
                                entryIndex++)
                            {
                                if (bounds.Intersects(entries[entryIndex]))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
                return false;
            }

            private static int FloorDivide(int value, int divisor)
            {
                int quotient = value / divisor;
                return value < 0 && value % divisor != 0
                    ? quotient - 1
                    : quotient;
            }
        }

        private readonly struct SpatialBucketKey :
            IEquatable<SpatialBucketKey>
        {
            public SpatialBucketKey(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            private int X { get; }
            private int Y { get; }
            private int Z { get; }

            public bool Equals(SpatialBucketKey other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is SpatialBucketKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X;
                    hash = hash * 397 ^ Y;
                    return hash * 397 ^ Z;
                }
            }
        }

        private readonly struct Candidate
        {
            public Candidate(
                int featureIndex,
                JigsawStructureFeatureSettings feature,
                JigsawStructureGenerator.Placement placement,
                IReadOnlyList<JigsawStructureGenerator.Piece> pieces)
            {
                FeatureIndex = featureIndex;
                Feature = feature;
                Placement = placement;
                Pieces = pieces;
            }

            public int FeatureIndex { get; }
            public JigsawStructureFeatureSettings Feature { get; }
            public JigsawStructureGenerator.Placement Placement { get; }
            public IReadOnlyList<JigsawStructureGenerator.Piece> Pieces { get; }
        }

        private readonly struct PlacementKey : IEquatable<PlacementKey>
        {
            public PlacementKey(
                ulong featureHash,
                JigsawStructureGenerator.Placement placement)
            {
                FeatureHash = featureHash;
                RegionX = placement.RegionX;
                RegionZ = placement.RegionZ;
                CentreX = placement.Centre.x;
                CentreY = placement.Centre.y;
                CentreZ = placement.Centre.z;
            }

            private ulong FeatureHash { get; }
            private int RegionX { get; }
            private int RegionZ { get; }
            private int CentreX { get; }
            private int CentreY { get; }
            private int CentreZ { get; }

            public bool Equals(PlacementKey other)
            {
                return FeatureHash == other.FeatureHash
                    && RegionX == other.RegionX
                    && RegionZ == other.RegionZ
                    && CentreX == other.CentreX
                    && CentreY == other.CentreY
                    && CentreZ == other.CentreZ;
            }

            public override bool Equals(object obj)
            {
                return obj is PlacementKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)FeatureHash ^ (int)(FeatureHash >> 32);
                    hash = hash * 397 ^ RegionX;
                    hash = hash * 397 ^ RegionZ;
                    hash = hash * 397 ^ CentreX;
                    hash = hash * 397 ^ CentreY;
                    return hash * 397 ^ CentreZ;
                }
            }
        }
    }
}
