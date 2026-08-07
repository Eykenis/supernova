using System;
using System.Collections.Generic;

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
            int maximumWorldZ)
        {
            if (features == null)
            {
                throw new ArgumentNullException(nameof(features));
            }

            int minX = Math.Min(minimumWorldX, maximumWorldX);
            int minZ = Math.Min(minimumWorldZ, maximumWorldZ);
            int maxX = Math.Max(minimumWorldX, maximumWorldX);
            int maxZ = Math.Max(minimumWorldZ, maximumWorldZ);
            var candidates = new List<Candidate>();
            var placements = new List<JigsawStructureGenerator.Placement>();

            for (int featureIndex = 0;
                featureIndex < features.Count;
                featureIndex++)
            {
                JigsawStructureFeatureSettings feature = features[featureIndex];
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

            candidates.Sort(CompareCandidates);
            var accepted = new HashSet<PlacementKey>();
            var occupiedPieces = new List<JigsawStructureGenerator.Piece>();
            for (int candidateIndex = 0;
                candidateIndex < candidates.Count;
                candidateIndex++)
            {
                Candidate candidate = candidates[candidateIndex];
                if (IntersectsAny(candidate.Pieces, occupiedPieces))
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
                    occupiedPieces.Add(candidate.Pieces[pieceIndex]);
                }
            }

            return new JigsawPlacementSelection(accepted);
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
            for (int leftIndex = 0; leftIndex < pieces.Count; leftIndex++)
            {
                for (int rightIndex = 0;
                    rightIndex < leftIndex;
                    rightIndex++)
                {
                    if (pieces[leftIndex].Bounds.Intersects(
                        pieces[rightIndex].Bounds))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IntersectsAny(
            IReadOnlyList<JigsawStructureGenerator.Piece> candidatePieces,
            IReadOnlyList<JigsawStructureGenerator.Piece> occupiedPieces)
        {
            for (int candidateIndex = 0;
                candidateIndex < candidatePieces.Count;
                candidateIndex++)
            {
                JigsawStructureGenerator.IntBounds candidateBounds =
                    candidatePieces[candidateIndex].Bounds;
                for (int occupiedIndex = 0;
                    occupiedIndex < occupiedPieces.Count;
                    occupiedIndex++)
                {
                    if (candidateBounds.Intersects(
                        occupiedPieces[occupiedIndex].Bounds))
                    {
                        return true;
                    }
                }
            }
            return false;
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
