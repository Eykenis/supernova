using System;
using System.Collections.Generic;
using Supernova.Voxels;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Immutable snapshot of one complete jigsaw structure family.
    /// </summary>
    public readonly struct JigsawStructureFeatureSettings
    {
        private readonly JigsawPieceSettings[] pieces;
        private readonly int[] startPieceCandidateIndices;

        public JigsawStructureFeatureSettings(
            string stableId,
            VoxelTypeId primaryType,
            VoxelTypeId accentType,
            int seedSalt,
            int regionSizeInChunks,
            float placementChance,
            int minFloorHeight,
            int maxFloorHeight,
            int maxPieces,
            int maxDepth,
            int maxHorizontalDistance,
            string firstPieceId,
            int layoutAttempts,
            int connectorPlacementAttempts,
            int collisionPadding,
            JigsawPieceSettings[] pieceSettings,
            JigsawPlacementStrategy placementStrategy =
                JigsawPlacementStrategy.RandomSpread,
            int ringStructureCount = 128,
            int ringCount = 8,
            int ringDistanceInChunks = 32,
            int ringSpreadInChunks = 3,
            string structureSetId = null,
            int structureSetWeight = 1,
            int worldHeight = VoxelColumnChunkData.Height,
            bool allowLayoutOutsidePlacementRegion = false,
            int[] startPieceCandidates = null)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new ArgumentException(
                    "A jigsaw structure requires a stable ID.",
                    nameof(stableId));
            }
            if (primaryType.IsAir)
            {
                throw new ArgumentException(
                    "A jigsaw structure requires a solid primary voxel type.",
                    nameof(primaryType));
            }
            if (pieceSettings == null || pieceSettings.Length == 0)
            {
                throw new ArgumentException(
                    "A jigsaw structure requires at least one piece module.",
                    nameof(pieceSettings));
            }

            StableId = stableId.Trim();
            PrimaryType = primaryType;
            AccentType = accentType.IsAir ? primaryType : accentType;
            SeedSalt = seedSalt;
            RegionSizeInChunks = Math.Max(1, regionSizeInChunks);
            PlacementChance = Clamp01(placementChance);
            MinFloorHeight = Math.Min(minFloorHeight, maxFloorHeight);
            MaxFloorHeight = Math.Max(minFloorHeight, maxFloorHeight);
            MaxPieces = Math.Max(2, maxPieces);
            MaxDepth = Math.Max(1, maxDepth);
            MaxHorizontalDistance = Math.Max(16, maxHorizontalDistance);
            FirstPieceId = string.IsNullOrWhiteSpace(firstPieceId)
                ? string.Empty
                : firstPieceId.Trim();
            LayoutAttempts = Math.Max(1, layoutAttempts);
            ConnectorPlacementAttempts = Math.Max(
                1,
                connectorPlacementAttempts);
            CollisionPadding = Math.Max(0, collisionPadding);
            PlacementStrategy = placementStrategy;
            RingStructureCount = Math.Max(1, ringStructureCount);
            RingCount = Math.Max(1, ringCount);
            RingDistanceInChunks = Math.Max(4, ringDistanceInChunks);
            RingSpreadInChunks = Math.Max(0, ringSpreadInChunks);
            StructureSetId = string.IsNullOrWhiteSpace(structureSetId)
                ? string.Empty
                : structureSetId.Trim();
            StructureSetWeight = Math.Max(1, structureSetWeight);
            WorldHeight = Math.Max(
                MinecraftCaveInfiniteWorld.MeshSectionHeight,
                Math.Min(VoxelColumnChunkData.Height, worldHeight));
            AllowLayoutOutsidePlacementRegion =
                allowLayoutOutsidePlacementRegion;
            pieces = (JigsawPieceSettings[])pieceSettings.Clone();

            int startIndex = -1;
            int forcedFirstIndex = -1;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int maximumPieceHeight = 4;
            int maximumBelowAnchor = 0;
            int totalRequiredPieces = 0;
            for (int i = 0; i < pieces.Length; i++)
            {
                JigsawPieceSettings piece = pieces[i];
                if (!ids.Add(piece.StableId))
                {
                    throw new ArgumentException(
                        $"Duplicate jigsaw piece ID '{piece.StableId}'.",
                        nameof(pieceSettings));
                }
                if (piece.IsStartPiece)
                {
                    if (startIndex >= 0)
                    {
                        throw new ArgumentException(
                            "A jigsaw structure must have exactly one start piece.",
                            nameof(pieceSettings));
                    }
                    startIndex = i;
                }
                if (string.Equals(
                    piece.StableId,
                    FirstPieceId,
                    StringComparison.Ordinal))
                {
                    forcedFirstIndex = i;
                }
                maximumPieceHeight = Math.Max(
                    maximumPieceHeight,
                    piece.MaximumVoxelHeight);
                maximumBelowAnchor = Math.Max(
                    maximumBelowAnchor,
                    piece.HasTemplate
                        ? piece.TemplateAnchor.y
                        : piece.Shape == JigsawPieceDefinition.Shape.Stairs
                            ? piece.VerticalDelta
                            : 0);
                if (!piece.IsStartPiece)
                {
                    totalRequiredPieces += piece.MinimumCount;
                }
                var connectorIds = new HashSet<string>(StringComparer.Ordinal);
                for (int connectorIndex = 0;
                    connectorIndex < piece.Connectors.Count;
                    connectorIndex++)
                {
                    JigsawConnectorSettings connector =
                        piece.Connectors[connectorIndex];
                    if (!connectorIds.Add(connector.StableId))
                    {
                        throw new ArgumentException(
                            $"Piece '{piece.StableId}' has duplicate connector ID '{connector.StableId}'.",
                            nameof(pieceSettings));
                    }
                }
            }
            if (startIndex < 0)
            {
                throw new ArgumentException(
                    "A jigsaw structure must have exactly one start piece.",
                    nameof(pieceSettings));
            }
            if (FirstPieceId.Length > 0 && forcedFirstIndex < 0)
            {
                throw new ArgumentException(
                    $"First piece '{FirstPieceId}' does not exist.",
                    nameof(firstPieceId));
            }
            if (forcedFirstIndex >= 0
                && !pieces[forcedFirstIndex].IsEligible(
                    pieces[forcedFirstIndex].PoolId,
                    1))
            {
                throw new ArgumentException(
                    $"First piece '{FirstPieceId}' is not eligible at graph depth 1.",
                    nameof(firstPieceId));
            }
            if (totalRequiredPieces + 1 > MaxPieces)
            {
                throw new ArgumentException(
                    "The sum of minimum piece counts exceeds maxPieces.",
                    nameof(pieceSettings));
            }
            StartPieceIndex = startIndex;
            FirstPieceIndex = forcedFirstIndex;
            // A mixed pool wants a different family's hub each layout. Candidates
            // are validated here so the generator can pick one without re-checking.
            startPieceCandidateIndices = BuildStartPieceCandidates(
                startPieceCandidates,
                startIndex,
                pieces.Length);

            int regionVoxelSize = RegionSizeInChunks
                * VoxelColumnChunkData.Width;
            // Only random spread owns a region, so only it needs the region to be
            // wider than the layout influence. Ring candidates are absolute world
            // positions and carry no region box.
            if (PlacementStrategy == JigsawPlacementStrategy.RandomSpread
                && !AllowLayoutOutsidePlacementRegion
                && regionVoxelSize <= MaxHorizontalDistance * 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(regionSizeInChunks),
                    "The placement region must be wider than twice the jigsaw influence distance.");
            }
            if (MinFloorHeight < maximumBelowAnchor + 2
                || MaxFloorHeight
                    > WorldHeight - maximumPieceHeight - 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minFloorHeight),
                    "Jigsaw floor heights must leave room for the tallest configured piece.");
            }
            ContentHash = 0UL;
            ContentHash = ComputeContentHash();
        }

        public string StableId { get; }
        public VoxelTypeId PrimaryType { get; }
        public VoxelTypeId AccentType { get; }
        public int SeedSalt { get; }
        public int RegionSizeInChunks { get; }
        public float PlacementChance { get; }
        public int MinFloorHeight { get; }
        public int MaxFloorHeight { get; }
        public int MaxPieces { get; }
        public int MaxDepth { get; }
        public int MaxHorizontalDistance { get; }
        public string FirstPieceId { get; }
        public int StartPieceIndex { get; }
        public int FirstPieceIndex { get; }
        public int LayoutAttempts { get; }
        public int ConnectorPlacementAttempts { get; }
        public int CollisionPadding { get; }
        public JigsawPlacementStrategy PlacementStrategy { get; }
        public int RingStructureCount { get; }
        public int RingCount { get; }
        public int RingDistanceInChunks { get; }
        public int RingSpreadInChunks { get; }
        public string StructureSetId { get; }
        public int StructureSetWeight { get; }
        public int WorldHeight { get; }
        public bool AllowLayoutOutsidePlacementRegion { get; }
        public bool HasStructureSet => StructureSetId.Length > 0;
        public ulong ContentHash { get; }
        public IReadOnlyList<JigsawPieceSettings> Pieces => pieces;

        /// <summary>
        /// Every module that may open a layout. This always contains
        /// <see cref="StartPieceIndex"/> and holds more entries only for mixed
        /// pools that rotate the opening hub between families.
        /// </summary>
        public IReadOnlyList<int> StartPieceCandidateIndices =>
            startPieceCandidateIndices;

        public JigsawPieceSettings GetPiece(int index)
        {
            return pieces[index];
        }

        private static int[] BuildStartPieceCandidates(
            int[] requested,
            int startIndex,
            int pieceCount)
        {
            if (requested == null || requested.Length == 0)
            {
                return new[] { startIndex };
            }

            var accepted = new List<int> { startIndex };
            for (int i = 0; i < requested.Length; i++)
            {
                int candidate = requested[i];
                if (candidate >= 0
                    && candidate < pieceCount
                    && !accepted.Contains(candidate))
                {
                    accepted.Add(candidate);
                }
            }
            return accepted.ToArray();
        }

        private ulong ComputeContentHash()
        {
            ulong hash = 1469598103934665603UL;
            AddHash(ref hash, StableId);
            AddHash(ref hash, PrimaryType.Value);
            AddHash(ref hash, AccentType.Value);
            AddHash(ref hash, SeedSalt);
            AddHash(ref hash, RegionSizeInChunks);
            AddHash(ref hash, PlacementChance.GetHashCode());
            AddHash(ref hash, MinFloorHeight);
            AddHash(ref hash, MaxFloorHeight);
            AddHash(ref hash, MaxPieces);
            AddHash(ref hash, MaxDepth);
            AddHash(ref hash, MaxHorizontalDistance);
            AddHash(ref hash, FirstPieceId);
            AddHash(ref hash, LayoutAttempts);
            AddHash(ref hash, ConnectorPlacementAttempts);
            AddHash(ref hash, CollisionPadding);
            AddHash(ref hash, (int)PlacementStrategy);
            AddHash(ref hash, RingStructureCount);
            AddHash(ref hash, RingCount);
            AddHash(ref hash, RingDistanceInChunks);
            AddHash(ref hash, RingSpreadInChunks);
            AddHash(ref hash, StructureSetId);
            AddHash(ref hash, StructureSetWeight);
            AddHash(ref hash, WorldHeight);
            AddHash(ref hash, AllowLayoutOutsidePlacementRegion ? 1 : 0);
            for (int i = 0; i < startPieceCandidateIndices.Length; i++)
            {
                AddHash(ref hash, startPieceCandidateIndices[i]);
            }
            for (int i = 0; i < pieces.Length; i++)
            {
                JigsawPieceSettings piece = pieces[i];
                AddHash(ref hash, piece.StableId);
                AddHash(ref hash, piece.PoolId);
                AddHash(ref hash, piece.OutputPoolId);
                AddHash(ref hash, piece.IsStartPiece ? 1 : 0);
                AddHash(ref hash, piece.Weight);
                AddHash(ref hash, piece.MinimumGraphDepth);
                AddHash(ref hash, piece.MaximumGraphDepth);
                AddHash(ref hash, piece.MinimumCount);
                AddHash(ref hash, piece.MaximumCount);
                AddHash(ref hash, piece.AllowConsecutive ? 1 : 0);
                AddHash(ref hash, piece.RequiredByDepth);
                AddHash(ref hash, (int)piece.Shape);
                AddHash(ref hash, (int)piece.BuildStyle);
                AddHash(ref hash, (int)piece.ConnectorPattern);
                AddHash(ref hash, (int)piece.Decoration);
                AddHash(ref hash, piece.MinimumWidth);
                AddHash(ref hash, piece.MaximumWidth);
                AddHash(ref hash, piece.MinimumDepth);
                AddHash(ref hash, piece.MaximumDepth);
                AddHash(ref hash, piece.MinimumHeight);
                AddHash(ref hash, piece.MaximumHeight);
                AddHash(ref hash, piece.MinimumLength);
                AddHash(ref hash, piece.MaximumLength);
                AddHash(ref hash, piece.Width);
                AddHash(ref hash, piece.Height);
                AddHash(ref hash, piece.VerticalDelta);
                AddHash(ref hash, piece.SideBranchChance.GetHashCode());
                AddHash(ref hash, piece.DescendingChance.GetHashCode());
                AddHash(ref hash, piece.DecorationSpacing);
                AddHash(ref hash, piece.HasTemplate ? 1 : 0);
                AddHash(ref hash, (int)piece.TemplateContentHash);
                AddHash(ref hash, (int)(piece.TemplateContentHash >> 32));
                AddHash(ref hash, piece.PrimaryTypeOverride.Value);
                AddHash(ref hash, piece.AccentTypeOverride.Value);
                for (int connectorIndex = 0;
                    connectorIndex < piece.Connectors.Count;
                    connectorIndex++)
                {
                    JigsawConnectorSettings connector =
                        piece.Connectors[connectorIndex];
                    AddHash(ref hash, connector.StableId);
                    AddHash(ref hash, (int)connector.Role);
                    AddHash(ref hash, (int)connector.Face);
                    AddHash(ref hash, (int)connector.Joint);
                    AddHash(ref hash, connector.SocketName);
                    AddHash(ref hash, connector.TargetName);
                    AddHash(ref hash, connector.TargetPoolId);
                    AddHash(ref hash, connector.FallbackPoolId);
                    AddHash(ref hash, connector.AlongOffset);
                    AddHash(ref hash, connector.LateralOffset);
                    AddHash(ref hash, connector.VerticalOffset);
                    AddHash(ref hash, connector.ActivationChance.GetHashCode());
                    AddHash(ref hash, connector.OpeningWidth);
                    AddHash(ref hash, connector.OpeningHeight);
                    AddHash(ref hash, connector.HasTemplatePosition ? 1 : 0);
                    AddHash(ref hash, connector.TemplatePosition.x);
                    AddHash(ref hash, connector.TemplatePosition.y);
                    AddHash(ref hash, connector.TemplatePosition.z);
                }
                for (int processorIndex = 0;
                    processorIndex < piece.Processors.Count;
                    processorIndex++)
                {
                    JigsawProcessorSettings processor =
                        piece.Processors[processorIndex];
                    AddHash(ref hash, processor.StableId);
                    AddHash(ref hash, (int)processor.Kind);
                    AddHash(ref hash, (int)processor.Palette);
                    AddHash(ref hash, processor.MaximumDistance);
                    AddHash(ref hash, processor.Inset);
                    AddHash(ref hash, processor.Chance.GetHashCode());
                    AddHash(ref hash, processor.PerimeterOnly ? 1 : 0);
                }
                for (int markerIndex = 0;
                    markerIndex < piece.SpawnMarkers.Count;
                    markerIndex++)
                {
                    StructureSpawnMarkerSettings marker =
                        piece.SpawnMarkers[markerIndex];
                    AddHash(ref hash, marker.StableId);
                    AddHash(ref hash, (int)marker.Kind);
                    AddHash(ref hash, (int)marker.TreasureSelection);
                    AddHash(ref hash, marker.LocalOffset.x);
                    AddHash(ref hash, marker.LocalOffset.y);
                    AddHash(ref hash, marker.LocalOffset.z);
                    AddHash(ref hash, marker.Yaw.GetHashCode());
                    AddHash(ref hash, marker.SpawnChance.GetHashCode());
                    AddHash(ref hash, marker.Count);
                    AddHash(
                        ref hash,
                        marker.ScatterRadiusInVoxels.GetHashCode());
                    AddHash(ref hash, marker.SnapToFloor ? 1 : 0);
                    AddHash(ref hash, marker.FloorSearchDistance);
                }
            }
            return hash;
        }

        private static void AddHash(ref ulong hash, string value)
        {
            if (value == null)
            {
                AddHash(ref hash, -1);
                return;
            }
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 1099511628211UL;
            }
        }

        private static void AddHash(ref ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= 1099511628211UL;
            }
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
