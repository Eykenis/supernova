using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Generic deterministic piece-pool generator shared by every jigsaw
    /// structure definition. Layouts are replayed per streamed voxel column.
    /// </summary>
    public static class JigsawStructureGenerator
    {
        private const int MaximumCachedLayouts = 512;
        private const int CollisionCellSize = 16;
        private const int HorizontalDirectionCount = 4;
        private const int UpDirection =
            (int)JigsawConnectorDefinition.Face.Up;
        private const int DownDirection =
            (int)JigsawConnectorDefinition.Face.Down;
        private static readonly ConcurrentDictionary<LayoutCacheKey, Lazy<LayoutCacheEntry>>
            LayoutCache = new ConcurrentDictionary<LayoutCacheKey, Lazy<LayoutCacheEntry>>();
        private static readonly ConcurrentQueue<LayoutCacheKey> LayoutCacheOrder =
            new ConcurrentQueue<LayoutCacheKey>();
        private static readonly VoxelPass[] RasterPasses =
        {
            VoxelPass.Shell,
            VoxelPass.Air,
            VoxelPass.Accent,
            VoxelPass.Processor,
        };
        private static long layoutBuildCount;

        public readonly struct Placement
        {
            public Placement(int regionX, int regionZ, Vector3Int centre)
            {
                RegionX = regionX;
                RegionZ = regionZ;
                Centre = centre;
            }

            public int RegionX { get; }
            public int RegionZ { get; }
            public Vector3Int Centre { get; }
        }

        public readonly struct IntBounds
        {
            public IntBounds(
                int minX,
                int minY,
                int minZ,
                int maxX,
                int maxY,
                int maxZ)
            {
                MinX = Math.Min(minX, maxX);
                MinY = Math.Min(minY, maxY);
                MinZ = Math.Min(minZ, maxZ);
                MaxX = Math.Max(minX, maxX);
                MaxY = Math.Max(minY, maxY);
                MaxZ = Math.Max(minZ, maxZ);
            }

            public int MinX { get; }
            public int MinY { get; }
            public int MinZ { get; }
            public int MaxX { get; }
            public int MaxY { get; }
            public int MaxZ { get; }

            public bool Intersects(IntBounds other)
            {
                return MinX <= other.MaxX && MaxX >= other.MinX
                    && MinY <= other.MaxY && MaxY >= other.MinY
                    && MinZ <= other.MaxZ && MaxZ >= other.MinZ;
            }

            public IntBounds Expand(int horizontal, int vertical)
            {
                return new IntBounds(
                    MinX - horizontal,
                    MinY - vertical,
                    MinZ - horizontal,
                    MaxX + horizontal,
                    MaxY + vertical,
                    MaxZ + horizontal);
            }
        }

        public readonly struct Piece
        {
            internal Piece(
                int moduleIndex,
                string moduleId,
                JigsawPieceDefinition.Shape shape,
                IntBounds bounds,
                Vector3Int origin,
                int direction,
                int length,
                int startFloorY,
                int endFloorY,
                int depth,
                int parentIndex,
                int connectorMask,
                Opening[] openings = null)
            {
                ModuleIndex = moduleIndex;
                ModuleId = moduleId;
                Shape = shape;
                Bounds = bounds;
                Origin = origin;
                Direction = direction & 3;
                Length = length;
                StartFloorY = startFloorY;
                EndFloorY = endFloorY;
                Depth = depth;
                ParentIndex = parentIndex;
                ConnectorMask = connectorMask;
                Openings = openings ?? Array.Empty<Opening>();
            }

            public int ModuleIndex { get; }
            public string ModuleId { get; }
            public JigsawPieceDefinition.Shape Shape { get; }
            public IntBounds Bounds { get; }
            public Vector3Int Origin { get; }
            public int Direction { get; }
            public int Length { get; }
            public int StartFloorY { get; }
            public int EndFloorY { get; }
            public int Depth { get; }
            public int ParentIndex { get; }
            public int ConnectorMask { get; }
            public IReadOnlyList<Opening> Openings { get; }

            internal Piece WithConnections(
                int connectorMask,
                Opening[] openings)
            {
                return new Piece(
                    ModuleIndex,
                    ModuleId,
                    Shape,
                    Bounds,
                    Origin,
                    Direction,
                    Length,
                    StartFloorY,
                    EndFloorY,
                    Depth,
                    ParentIndex,
                    connectorMask,
                    openings);
            }
        }

        public readonly struct Opening
        {
            internal Opening(
                Vector3Int boundary,
                int direction,
                int width,
                int height)
            {
                Boundary = boundary;
                Direction = NormalizeConnectionDirection(direction);
                Width = Math.Max(1, width);
                Height = Math.Max(1, height);
            }

            public Vector3Int Boundary { get; }
            public int Direction { get; }
            public int Width { get; }
            public int Height { get; }
        }

        private enum VoxelPass
        {
            Shell,
            Air,
            Accent,

            /// <summary>
            /// Landing pass. Runs after every piece has written its shell, air
            /// and accent voxels so supports and headroom see the finished
            /// structure rather than a half-built one.
            /// </summary>
            Processor,
        }

        private readonly struct Connector
        {
            public Connector(
                Vector3Int position,
                int direction,
                int depth,
                int parentIndex,
                string poolId,
                string fallbackPoolId,
                string socketName,
                string targetName,
                int openingWidth,
                int openingHeight)
            {
                Position = position;
                Direction = NormalizeConnectionDirection(direction);
                Depth = depth;
                ParentIndex = parentIndex;
                PoolId = poolId;
                FallbackPoolId = fallbackPoolId ?? string.Empty;
                SocketName = string.IsNullOrWhiteSpace(socketName)
                    ? "*"
                    : socketName;
                TargetName = string.IsNullOrWhiteSpace(targetName)
                    ? "*"
                    : targetName;
                OpeningWidth = Math.Max(1, openingWidth);
                OpeningHeight = Math.Max(1, openingHeight);
            }

            public Vector3Int Position { get; }
            public int Direction { get; }
            public int Depth { get; }
            public int ParentIndex { get; }
            public string PoolId { get; }
            public string FallbackPoolId { get; }
            public string SocketName { get; }
            public string TargetName { get; }
            public int OpeningWidth { get; }
            public int OpeningHeight { get; }
        }

        public static int CachedLayoutCount => LayoutCache.Count;
        public static int LayoutCacheCapacity => MaximumCachedLayouts;
        public static long LayoutBuildCount => Interlocked.Read(ref layoutBuildCount);

        public static void ClearLayoutCache()
        {
            LayoutCache.Clear();
            while (LayoutCacheOrder.TryDequeue(out _))
            {
            }
            Interlocked.Exchange(ref layoutBuildCount, 0);
            JigsawPlacementService.ClearCaches();
        }

        public static int GenerateColumn(
            Vector3Int columnCoordinate,
            float[] densities,
            VoxelTypeId[] types,
            int worldSeed,
            IReadOnlyList<JigsawStructureFeatureSettings> features,
            float solidDensity,
            float airDensity,
            CancellationToken cancellationToken = default,
            JigsawPlacementSelection placementSelection = null)
        {
            ValidateColumnData(densities, types);
            if (features == null || features.Count == 0)
            {
                return 0;
            }

            int targetMinX = columnCoordinate.x * VoxelColumnChunkData.Width;
            int targetMinZ = columnCoordinate.z * VoxelColumnChunkData.Depth;
            int targetMaxX = targetMinX + VoxelColumnChunkData.Width - 1;
            int targetMaxZ = targetMinZ + VoxelColumnChunkData.Depth - 1;
            int changed = 0;
            var placements = new List<Placement>();

            for (int featureIndex = 0; featureIndex < features.Count; featureIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JigsawStructureFeatureSettings feature = features[featureIndex];
                if (feature.PlacementChance <= 0f)
                {
                    continue;
                }

                JigsawPlacementService.CollectPlacements(
                    feature,
                    worldSeed,
                    targetMinX,
                    targetMinZ,
                    targetMaxX,
                    targetMaxZ,
                    placements);
                for (int i = 0; i < placements.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Placement placement = placements[i];
                    if (!JigsawPlacementService.WinsStructureSet(
                        features,
                        featureIndex,
                        worldSeed,
                        placement.RegionX,
                        placement.RegionZ))
                    {
                        continue;
                    }
                    if (placementSelection != null
                        && !placementSelection.Allows(feature, placement))
                    {
                        continue;
                    }

                    LayoutCacheEntry layout = GetOrCreateLayout(
                        feature,
                        worldSeed,
                        placement);
                    IReadOnlyList<Piece> intersectingPieces =
                        layout.GetPiecesForColumn(
                            columnCoordinate.x,
                            columnCoordinate.z);
                    if (intersectingPieces.Count == 0)
                    {
                        continue;
                    }
                    changed += ApplyLayoutToColumn(
                        feature,
                        intersectingPieces,
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

            return changed;
        }

        /// <summary>
        /// Random-spread candidate for one region. Kept as the direct entry point
        /// used by tests and tooling; general iteration should call
        /// <see cref="JigsawPlacementService.CollectPlacements"/> so that every
        /// placement strategy is honoured.
        /// </summary>
        public static bool TryGetPlacement(
            JigsawStructureFeatureSettings feature,
            int worldSeed,
            int regionX,
            int regionZ,
            out Placement placement)
        {
            if (feature.PlacementStrategy == JigsawPlacementStrategy.FixedOrigin)
            {
                return JigsawPlacementService.TryGetFixedOriginPlacement(
                    feature,
                    out placement);
            }

            return JigsawPlacementService.TryGetRandomSpreadPlacement(
                feature,
                worldSeed,
                regionX,
                regionZ,
                out placement);
        }

        public static IReadOnlyList<Piece> BuildLayout(
            JigsawStructureFeatureSettings feature,
            int worldSeed,
            Placement placement)
        {
            return GetOrCreateLayout(feature, worldSeed, placement).Pieces;
        }

        /// <summary>
        /// Resolves the first authored player spawn in a fixed-origin structure.
        /// The structure asset owns the piece, position, and facing direction.
        /// </summary>
        public static bool TryResolvePlayerSpawn(
            int worldSeed,
            IReadOnlyList<JigsawStructureFeatureSettings> features,
            out PlayerSpawnRequest request)
        {
            request = default;
            if (features == null || features.Count == 0)
            {
                return false;
            }

            for (int featureIndex = 0; featureIndex < features.Count; featureIndex++)
            {
                JigsawStructureFeatureSettings feature = features[featureIndex];
                if (feature.PlacementStrategy != JigsawPlacementStrategy.FixedOrigin
                    || feature.PlacementChance <= 0f
                    || !TryGetPlacement(
                        feature,
                        worldSeed,
                        0,
                        0,
                        out Placement placement)
                    || !JigsawPlacementService.WinsStructureSet(
                        features,
                        featureIndex,
                        worldSeed,
                        placement.RegionX,
                        placement.RegionZ))
                {
                    continue;
                }

                IReadOnlyList<Piece> pieces = GetOrCreateLayout(
                    feature,
                    worldSeed,
                    placement).Pieces;
                for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
                {
                    Piece piece = pieces[pieceIndex];
                    JigsawPieceSettings module = feature.GetPiece(piece.ModuleIndex);
                    for (int markerIndex = 0;
                        markerIndex < module.SpawnMarkers.Count;
                        markerIndex++)
                    {
                        StructureSpawnMarkerSettings marker =
                            module.SpawnMarkers[markerIndex];
                        if (marker.Kind
                                != StructureSpawnMarkerDefinition.Kind.PlayerSpawn
                            || marker.SpawnChance <= 0f
                            || !marker.IsConfigured)
                        {
                            continue;
                        }

                        request = new PlayerSpawnRequest(
                            ResolveMarkerAnchor(piece, marker),
                            Mathf.Repeat(
                                piece.Direction * 90f + marker.Yaw,
                                360f));
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Resolves every authored spawn marker whose position falls inside the
        /// given voxel column. Resolution reuses the cached layout and is keyed on
        /// world coordinates, so the same column always yields the same spawns no
        /// matter when it streams in or how many times it is revisited.
        /// </summary>
        public static void CollectSpawnRequests(
            Vector3Int columnCoordinate,
            int worldSeed,
            IReadOnlyList<JigsawStructureFeatureSettings> features,
            List<StructureSpawnRequest> results,
            CancellationToken cancellationToken = default,
            JigsawPlacementSelection placementSelection = null)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }
            results.Clear();
            if (features == null || features.Count == 0)
            {
                return;
            }

            int targetMinX = columnCoordinate.x * VoxelColumnChunkData.Width;
            int targetMinZ = columnCoordinate.z * VoxelColumnChunkData.Depth;
            int targetMaxX = targetMinX + VoxelColumnChunkData.Width - 1;
            int targetMaxZ = targetMinZ + VoxelColumnChunkData.Depth - 1;
            var placements = new List<Placement>();

            for (int featureIndex = 0; featureIndex < features.Count; featureIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JigsawStructureFeatureSettings feature = features[featureIndex];
                if (feature.PlacementChance <= 0f)
                {
                    continue;
                }

                JigsawPlacementService.CollectPlacements(
                    feature,
                    worldSeed,
                    targetMinX,
                    targetMinZ,
                    targetMaxX,
                    targetMaxZ,
                    placements);
                for (int i = 0; i < placements.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Placement placement = placements[i];
                    if (!JigsawPlacementService.WinsStructureSet(
                        features,
                        featureIndex,
                        worldSeed,
                        placement.RegionX,
                        placement.RegionZ))
                    {
                        continue;
                    }
                    if (placementSelection != null
                        && !placementSelection.Allows(feature, placement))
                    {
                        continue;
                    }

                    IReadOnlyList<Piece> pieces = GetOrCreateLayout(
                        feature,
                        worldSeed,
                        placement).GetPiecesForColumn(
                            columnCoordinate.x,
                            columnCoordinate.z);
                    for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
                    {
                        ResolvePieceSpawnMarkers(
                            feature,
                            pieces[pieceIndex],
                            worldSeed,
                            targetMinX,
                            targetMaxX,
                            targetMinZ,
                            targetMaxZ,
                            results);
                    }
                }
            }
        }

        /// <summary>
        /// Resolves checkpoint markers from every generated jigsaw piece. This
        /// covers both the fixed-origin start layout and random Dense layouts.
        /// </summary>
        public static void CollectCheckpointRequests(
            Vector3Int columnCoordinate,
            int worldSeed,
            IReadOnlyList<JigsawStructureFeatureSettings> features,
            List<CheckpointSpawnRequest> results,
            float chance,
            CancellationToken cancellationToken = default,
            JigsawPlacementSelection placementSelection = null)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }
            results.Clear();
            if (features == null || features.Count == 0 || chance <= 0f)
            {
                return;
            }

            int targetMinX = columnCoordinate.x * VoxelColumnChunkData.Width;
            int targetMinZ = columnCoordinate.z * VoxelColumnChunkData.Depth;
            int targetMaxX = targetMinX + VoxelColumnChunkData.Width - 1;
            int targetMaxZ = targetMinZ + VoxelColumnChunkData.Depth - 1;
            var placements = new List<Placement>();

            for (int featureIndex = 0; featureIndex < features.Count; featureIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JigsawStructureFeatureSettings feature = features[featureIndex];
                if (feature.PlacementChance <= 0f)
                {
                    continue;
                }

                JigsawPlacementService.CollectPlacements(
                    feature,
                    worldSeed,
                    targetMinX,
                    targetMinZ,
                    targetMaxX,
                    targetMaxZ,
                    placements);
                for (int i = 0; i < placements.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Placement placement = placements[i];
                    if (!JigsawPlacementService.WinsStructureSet(
                        features,
                        featureIndex,
                        worldSeed,
                        placement.RegionX,
                        placement.RegionZ))
                    {
                        continue;
                    }
                    if (placementSelection != null
                        && !placementSelection.Allows(feature, placement))
                    {
                        continue;
                    }

                    IReadOnlyList<Piece> pieces = GetOrCreateLayout(
                        feature,
                        worldSeed,
                        placement).GetPiecesForColumn(
                            columnCoordinate.x,
                            columnCoordinate.z);
                    for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
                    {
                        ResolvePieceCheckpoint(
                            feature,
                            pieces[pieceIndex],
                            chance,
                            targetMinX,
                            targetMaxX,
                            targetMinZ,
                            targetMaxZ,
                            results);
                    }
                }
            }
        }

        private static void ResolvePieceCheckpoint(
            JigsawStructureFeatureSettings feature,
            Piece piece,
            float chance,
            int targetMinX,
            int targetMaxX,
            int targetMinZ,
            int targetMaxZ,
            List<CheckpointSpawnRequest> results)
        {
            if (chance <= 0f)
            {
                return;
            }

            JigsawPieceSettings module = feature.GetPiece(piece.ModuleIndex);
            if (!module.HasSpawnMarkers)
            {
                return;
            }

            for (int i = 0; i < module.SpawnMarkers.Count; i++)
            {
                StructureSpawnMarkerSettings marker = module.SpawnMarkers[i];
                if (marker.Kind
                    != StructureSpawnMarkerDefinition.Kind.Checkpoint
                    || marker.SpawnChance <= 0f
                    || !marker.IsConfigured)
                {
                    continue;
                }

                Vector3Int anchor = ResolveMarkerAnchor(piece, marker);
                if (anchor.x < targetMinX || anchor.x > targetMaxX
                    || anchor.z < targetMinZ || anchor.z > targetMaxZ)
                {
                    continue;
                }

                results.Add(new CheckpointSpawnRequest(
                    marker.CheckpointPrefab,
                    anchor,
                    anchor.y,
                    Mathf.Repeat(piece.Direction * 90f + marker.Yaw, 360f),
                    feature.PlacementStrategy
                        == JigsawPlacementStrategy.FixedOrigin));
            }
        }

        private static void ResolvePieceSpawnMarkers(
            JigsawStructureFeatureSettings feature,
            Piece piece,
            int worldSeed,
            int targetMinX,
            int targetMaxX,
            int targetMinZ,
            int targetMaxZ,
            List<StructureSpawnRequest> results)
        {
            JigsawPieceSettings module = feature.GetPiece(piece.ModuleIndex);
            if (!module.HasSpawnMarkers)
            {
                return;
            }

            for (int i = 0; i < module.SpawnMarkers.Count; i++)
            {
                StructureSpawnMarkerSettings marker = module.SpawnMarkers[i];
                if (marker.Kind
                    != StructureSpawnMarkerDefinition.Kind.Treasure)
                {
                    continue;
                }
                if (!marker.IsConfigured)
                {
                    continue;
                }

                // The marker's authored offset is in the piece's own axes, so it
                // rotates with the piece rather than pointing at world north.
                Vector3Int anchor = ResolveMarkerAnchor(piece, marker);
                // Keyed on the anchor rather than an index, so every column that
                // sees this marker agrees on whether it fired.
                var random = new DeterministicRandom(BuildSeed(
                    worldSeed,
                    feature.SeedSalt ^ marker.Salt,
                    anchor.x ^ (anchor.y << 16),
                    anchor.z));
                if (random.NextDouble() >= marker.SpawnChance)
                {
                    continue;
                }

                float pieceYaw = piece.Direction * 90f;
                for (int instance = 0; instance < marker.Count; instance++)
                {
                    Vector3Int position = anchor;
                    if (instance > 0 && marker.ScatterRadiusInVoxels > 0f)
                    {
                        double angle = random.NextDouble() * Math.PI * 2.0;
                        double distance = Math.Sqrt(random.NextDouble())
                            * marker.ScatterRadiusInVoxels;
                        position = new Vector3Int(
                            anchor.x + (int)Math.Round(Math.Cos(angle) * distance),
                            anchor.y,
                            anchor.z + (int)Math.Round(Math.Sin(angle) * distance));
                    }
                    float treasureSelectionRoll =
                        (float)random.NextDouble();
                    // A scattered instance can land in a neighbouring column; that
                    // column will resolve it itself, so drop it here to avoid
                    // spawning the same instance twice.
                    if (position.x < targetMinX
                        || position.x > targetMaxX
                        || position.z < targetMinZ
                        || position.z > targetMaxZ)
                    {
                        continue;
                    }
                    results.Add(new StructureSpawnRequest(
                        marker.Kind,
                        marker.Treasure,
                        marker.TreasureSelection,
                        treasureSelectionRoll,
                        position,
                        Mathf.Repeat(pieceYaw + marker.Yaw, 360f),
                        marker.SnapToFloor,
                        marker.FloorSearchDistance));
                }
            }
        }

        private static Vector3Int ResolveMarkerAnchor(
            Piece piece,
            StructureSpawnMarkerSettings marker)
        {
            Vector3Int forward = DirectionVector(piece.Direction);
            Vector3Int right = DirectionVector((piece.Direction + 1) & 3);
            return new Vector3Int(
                piece.Origin.x
                    + right.x * marker.LocalOffset.x
                    + forward.x * marker.LocalOffset.z,
                piece.Origin.y + marker.LocalOffset.y,
                piece.Origin.z
                    + right.z * marker.LocalOffset.x
                    + forward.z * marker.LocalOffset.z);
        }

        private static LayoutCacheEntry GetOrCreateLayout(
            JigsawStructureFeatureSettings feature,
            int worldSeed,
            Placement placement)
        {
            var key = new LayoutCacheKey(
                feature.ContentHash,
                worldSeed,
                placement.RegionX,
                placement.RegionZ,
                placement.Centre);
            Lazy<LayoutCacheEntry> lazy = LayoutCache.GetOrAdd(
                key,
                cacheKey =>
                {
                    LayoutCacheOrder.Enqueue(cacheKey);
                    TrimLayoutCache();
                    return new Lazy<LayoutCacheEntry>(
                        () => BuildBestLayout(feature, worldSeed, placement),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                });
            return lazy.Value;
        }

        private static LayoutCacheEntry BuildBestLayout(
            JigsawStructureFeatureSettings feature,
            int worldSeed,
            Placement placement)
        {
            Interlocked.Increment(ref layoutBuildCount);
            List<Piece> best = null;
            int bestDeficit = int.MaxValue;
            for (int attempt = 0; attempt < feature.LayoutAttempts; attempt++)
            {
                List<Piece> candidate = BuildLayoutAttempt(
                    feature,
                    worldSeed,
                    placement,
                    attempt);
                int deficit = CountRequiredDeficit(feature, candidate);
                if (best == null
                    || deficit < bestDeficit
                    || (deficit == bestDeficit && candidate.Count > best.Count))
                {
                    best = candidate;
                    bestDeficit = deficit;
                }
                if (deficit == 0)
                {
                    break;
                }
            }
            return new LayoutCacheEntry(best.ToArray());
        }

        private static List<Piece> BuildLayoutAttempt(
            JigsawStructureFeatureSettings feature,
            int worldSeed,
            Placement placement,
            int layoutAttempt)
        {
            var random = new DeterministicRandom(BuildSeed(
                worldSeed,
                feature.SeedSalt
                    ^ unchecked((int)0x6D2B79F5)
                    ^ unchecked(layoutAttempt * (int)0x9E3779B9),
                placement.RegionX,
                placement.RegionZ));
            var pieces = new List<Piece>(feature.MaxPieces);
            var connectors = new Queue<Connector>();
            var spatialIndex = new PieceSpatialIndex();
            var counts = new int[feature.Pieces.Count];

            // Mixed pools list one hub per family. Rolling before the graph grows
            // keeps every family's signature entrance in rotation instead of
            // always opening with whichever family was authored first.
            IReadOnlyList<int> startCandidates =
                feature.StartPieceCandidateIndices;
            int startPieceIndex = startCandidates.Count > 1
                ? startCandidates[random.NextInt(startCandidates.Count)]
                : feature.StartPieceIndex;

            JigsawPieceSettings startModule = feature.GetPiece(
                startPieceIndex);
            int firstDirection = random.NextInt(4);
            Piece start = CreateStartPiece(
                startPieceIndex,
                startModule,
                placement.Centre,
                firstDirection,
                ref random);
            pieces.Add(start);
            counts[startPieceIndex]++;
            spatialIndex.Add(0, start.Bounds);
            var startOpenings = new List<Opening>();
            int startConnectorMask = EnqueueConnectors(
                start,
                startModule,
                0,
                connectors,
                startOpenings,
                ref random,
                true);
            pieces[0] = start.WithConnections(
                startConnectorMask,
                startOpenings.ToArray());

            while (connectors.Count > 0 && pieces.Count < feature.MaxPieces)
            {
                Connector connector = connectors.Dequeue();
                if (connector.Depth > feature.MaxDepth)
                {
                    continue;
                }

                bool added = false;
                var excludedModules = new HashSet<int>();
                string activePool = connector.PoolId;
                int totalAttempts = feature.ConnectorPlacementAttempts;
                for (int attempt = 0; attempt < totalAttempts && !added; attempt++)
                {
                    int moduleIndex = PickPieceIndex(
                        feature,
                        connector,
                        activePool,
                        counts,
                        pieces.Count,
                        connector.ParentIndex >= 0
                            ? pieces[connector.ParentIndex].ModuleIndex
                            : -1,
                        excludedModules,
                        ref random);
                    if (moduleIndex < 0)
                    {
                        if (activePool == connector.PoolId
                            && connector.FallbackPoolId.Length > 0)
                        {
                            activePool = connector.FallbackPoolId;
                            excludedModules.Clear();
                            continue;
                        }
                        break;
                    }
                    excludedModules.Add(moduleIndex);
                    JigsawPieceSettings module = feature.GetPiece(moduleIndex);
                    bool priorityContinuation =
                        RequiresPriorityContinuation(module);
                    if (priorityContinuation
                        && (pieces.Count > feature.MaxPieces - 2
                            || connector.Depth >= feature.MaxDepth))
                    {
                        // A vertical corridor is only useful when its far-side
                        // landing can also be committed. Reject the entrance
                        // before it becomes an opening if the graph has no
                        // remaining piece/depth budget for that continuation.
                        continue;
                    }
                    if (!TryCreateCandidate(
                        moduleIndex,
                        module,
                        connector,
                        pieces[connector.ParentIndex].Direction,
                        ref random,
                        out Piece candidate,
                        out Opening entranceOpening,
                        out int usedInputConnector))
                    {
                        continue;
                    }
                    if (!CanAddPiece(
                        feature,
                        placement,
                        pieces,
                        spatialIndex,
                        candidate,
                        connector.ParentIndex))
                    {
                        continue;
                    }
                    if (priorityContinuation
                        && !CanReservePriorityContinuation(
                            feature,
                            placement,
                            pieces,
                            spatialIndex,
                            counts,
                            candidate,
                            module,
                            usedInputConnector,
                            random))
                    {
                        // Do not open the source-room door unless the far-side
                        // landing has valid graph budget and collision space.
                        continue;
                    }

                    int pieceIndex = pieces.Count;
                    pieces.Add(candidate);
                    counts[moduleIndex]++;
                    spatialIndex.Add(pieceIndex, candidate.Bounds);
                    var openings = new List<Opening> { entranceOpening };
                    Queue<Connector> childConnectors = priorityContinuation
                        ? new Queue<Connector>()
                        : connectors;
                    int connectorMask = EnqueueConnectors(
                        candidate,
                        module,
                        pieceIndex,
                        childConnectors,
                        openings,
                        ref random,
                        false,
                        usedInputConnector);
                    if (priorityContinuation)
                    {
                        PrependConnectors(connectors, childConnectors);
                    }
                    connectorMask |= 1 << entranceOpening.Direction;
                    pieces[pieceIndex] = candidate.WithConnections(
                        connectorMask,
                        openings.ToArray());
                    pieces[connector.ParentIndex] = AddConnectedOpening(
                        pieces[connector.ParentIndex],
                        connector);
                    added = true;
                }
            }

            return pieces;
        }

        private static bool RequiresPriorityContinuation(
            JigsawPieceSettings module)
        {
            if (module.Shape != JigsawPieceDefinition.Shape.VerticalShaft)
            {
                return false;
            }

            int mandatoryOutputs = 0;
            for (int i = 0; i < module.Connectors.Count; i++)
            {
                JigsawConnectorSettings connector = module.Connectors[i];
                if (connector.CanEmitOutput
                    && connector.ActivationChance >= 1f)
                {
                    mandatoryOutputs++;
                }
            }
            return mandatoryOutputs == 1;
        }

        private static bool CanReservePriorityContinuation(
            JigsawStructureFeatureSettings feature,
            Placement placement,
            List<Piece> pieces,
            PieceSpatialIndex spatialIndex,
            int[] counts,
            Piece candidate,
            JigsawPieceSettings module,
            int usedInputConnector,
            DeterministicRandom random)
        {
            for (int connectorIndex = 0;
                connectorIndex < module.Connectors.Count;
                connectorIndex++)
            {
                JigsawConnectorSettings authored =
                    module.Connectors[connectorIndex];
                if (connectorIndex == usedInputConnector
                    || !authored.CanEmitOutput
                    || authored.ActivationChance < 1f)
                {
                    continue;
                }

                int direction = GetWorldDirection(
                    candidate.Direction,
                    authored.Face);
                Vector3Int boundary = GetAuthoredConnectorBoundary(
                    candidate,
                    module,
                    authored);
                var continuationConnector = new Connector(
                    boundary + DirectionVector(direction),
                    direction,
                    candidate.Depth + 1,
                    pieces.Count,
                    authored.TargetPoolId,
                    authored.FallbackPoolId,
                    authored.SocketName,
                    authored.TargetName,
                    authored.OpeningWidth,
                    authored.OpeningHeight);

                for (int targetIndex = 0;
                    targetIndex < feature.Pieces.Count;
                    targetIndex++)
                {
                    JigsawPieceSettings target = feature.GetPiece(targetIndex);
                    if (!target.IsEligible(
                            continuationConnector.PoolId,
                            continuationConnector.Depth,
                            counts[targetIndex],
                            candidate.ModuleIndex,
                            targetIndex)
                        || !HasCompatibleInput(target, continuationConnector))
                    {
                        continue;
                    }

                    DeterministicRandom previewRandom = random;
                    if (TryCreateCandidate(
                            targetIndex,
                            target,
                            continuationConnector,
                            candidate.Direction,
                            ref previewRandom,
                            out Piece continuation,
                            out _,
                            out _)
                        && CanAddPiece(
                            feature,
                            placement,
                            pieces,
                            spatialIndex,
                            continuation,
                            pieces.Count))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void PrependConnectors(
            Queue<Connector> connectors,
            Queue<Connector> priority)
        {
            if (priority.Count == 0)
            {
                return;
            }

            Connector[] existing = connectors.ToArray();
            connectors.Clear();
            while (priority.Count > 0)
            {
                connectors.Enqueue(priority.Dequeue());
            }
            for (int i = 0; i < existing.Length; i++)
            {
                connectors.Enqueue(existing[i]);
            }
        }

        private static Piece AddConnectedOpening(
            Piece piece,
            Connector connector)
        {
            var opening = new Opening(
                connector.Position - DirectionVector(connector.Direction),
                connector.Direction,
                connector.OpeningWidth,
                connector.OpeningHeight);
            var openings = new Opening[piece.Openings.Count + 1];
            for (int i = 0; i < piece.Openings.Count; i++)
            {
                openings[i] = piece.Openings[i];
            }
            openings[openings.Length - 1] = opening;
            return piece.WithConnections(
                piece.ConnectorMask | 1 << opening.Direction,
                openings);
        }

        private static int CountRequiredDeficit(
            JigsawStructureFeatureSettings feature,
            IReadOnlyList<Piece> pieces)
        {
            var counts = new int[feature.Pieces.Count];
            for (int i = 0; i < pieces.Count; i++)
            {
                counts[pieces[i].ModuleIndex]++;
            }
            int deficit = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                deficit += Math.Max(0, feature.GetPiece(i).MinimumCount - counts[i]);
            }
            return deficit;
        }

        private static void TrimLayoutCache()
        {
            while (LayoutCache.Count >= MaximumCachedLayouts
                && LayoutCacheOrder.TryDequeue(out LayoutCacheKey oldest))
            {
                LayoutCache.TryRemove(oldest, out _);
            }
        }

        private static Piece CreateStartPiece(
            int moduleIndex,
            JigsawPieceSettings module,
            Vector3Int centre,
            int direction,
            ref DeterministicRandom random)
        {
            if (module.HasTemplate)
            {
                return CreateTemplatePiece(
                    moduleIndex,
                    module,
                    centre,
                    direction,
                    0,
                    -1);
            }
            int width = NextOddInRange(
                module.MinimumWidth,
                module.MaximumWidth,
                ref random);
            int depth = NextOddInRange(
                module.MinimumDepth,
                module.MaximumDepth,
                ref random);
            int height = NextInRange(
                module.MinimumHeight,
                module.MaximumHeight,
                ref random);
            int halfX = width / 2;
            int halfZ = depth / 2;
            IntBounds bounds = new IntBounds(
                centre.x - halfX,
                centre.y,
                centre.z - halfZ,
                centre.x + halfX,
                centre.y + height,
                centre.z + halfZ);
            return new Piece(
                moduleIndex,
                module.StableId,
                module.Shape,
                bounds,
                centre,
                direction,
                depth,
                centre.y,
                centre.y,
                0,
                -1,
                0);
        }

        private static int PickPieceIndex(
            JigsawStructureFeatureSettings feature,
            Connector connector,
            string poolId,
            int[] counts,
            int placedPieceCount,
            int parentModuleIndex,
            HashSet<int> excludedModules,
            ref DeterministicRandom random)
        {
            if (connector.Depth == 1 && feature.FirstPieceIndex >= 0)
            {
                JigsawPieceSettings first = feature.GetPiece(
                    feature.FirstPieceIndex);
                if (!excludedModules.Contains(feature.FirstPieceIndex)
                    && first.IsEligible(
                        poolId,
                        connector.Depth,
                        counts[feature.FirstPieceIndex],
                        parentModuleIndex,
                        feature.FirstPieceIndex)
                    && HasCompatibleInput(first, connector))
                {
                    return feature.FirstPieceIndex;
                }
            }

            int outstandingRequired = 0;
            for (int i = 0; i < feature.Pieces.Count; i++)
            {
                outstandingRequired += Math.Max(
                    0,
                    feature.GetPiece(i).MinimumCount - counts[i]);
            }
            bool capacityUrgent = feature.MaxPieces - placedPieceCount
                <= outstandingRequired;
            int requiredWeight = 0;
            for (int i = 0; i < feature.Pieces.Count; i++)
            {
                JigsawPieceSettings piece = feature.GetPiece(i);
                int requiredDepth = piece.RequiredByDepth > 0
                    ? piece.RequiredByDepth
                    : feature.MaxDepth;
                bool urgent = counts[i] < piece.MinimumCount
                    && (capacityUrgent || connector.Depth >= requiredDepth);
                if (urgent
                    && !excludedModules.Contains(i)
                    && piece.IsEligible(
                        poolId,
                        connector.Depth,
                        counts[i],
                        parentModuleIndex,
                        i)
                    && HasCompatibleInput(piece, connector))
                {
                    requiredWeight += piece.Weight;
                }
            }
            if (requiredWeight > 0)
            {
                int requiredRoll = random.NextInt(requiredWeight);
                for (int i = 0; i < feature.Pieces.Count; i++)
                {
                    JigsawPieceSettings piece = feature.GetPiece(i);
                    int requiredDepth = piece.RequiredByDepth > 0
                        ? piece.RequiredByDepth
                        : feature.MaxDepth;
                    bool urgent = counts[i] < piece.MinimumCount
                        && (capacityUrgent || connector.Depth >= requiredDepth);
                    if (!urgent
                        || excludedModules.Contains(i)
                        || !piece.IsEligible(
                            poolId,
                            connector.Depth,
                            counts[i],
                            parentModuleIndex,
                            i)
                        || !HasCompatibleInput(piece, connector))
                    {
                        continue;
                    }
                    if (requiredRoll < piece.Weight)
                    {
                        return i;
                    }
                    requiredRoll -= piece.Weight;
                }
            }

            int totalWeight = 0;
            for (int i = 0; i < feature.Pieces.Count; i++)
            {
                JigsawPieceSettings piece = feature.GetPiece(i);
                if (!excludedModules.Contains(i)
                    && piece.IsEligible(
                        poolId,
                        connector.Depth,
                        counts[i],
                        parentModuleIndex,
                        i)
                    && HasCompatibleInput(piece, connector))
                {
                    totalWeight += piece.Weight;
                }
            }
            if (totalWeight <= 0)
            {
                return -1;
            }

            int roll = random.NextInt(totalWeight);
            for (int i = 0; i < feature.Pieces.Count; i++)
            {
                JigsawPieceSettings piece = feature.GetPiece(i);
                if (excludedModules.Contains(i)
                    || !piece.IsEligible(
                        poolId,
                        connector.Depth,
                        counts[i],
                        parentModuleIndex,
                        i)
                    || !HasCompatibleInput(piece, connector))
                {
                    continue;
                }
                if (roll < piece.Weight)
                {
                    return i;
                }
                roll -= piece.Weight;
            }
            return -1;
        }

        private static bool HasCompatibleInput(
            JigsawPieceSettings module,
            Connector connector)
        {
            if (!module.HasExplicitConnectors)
            {
                return !IsVerticalDirection(connector.Direction);
            }
            for (int i = 0; i < module.Connectors.Count; i++)
            {
                JigsawConnectorSettings input = module.Connectors[i];
                if (ConnectorMatches(connector, input))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryCreateCandidate(
            int moduleIndex,
            JigsawPieceSettings module,
            Connector connector,
            int parentDirection,
            ref DeterministicRandom random,
            out Piece candidate,
            out Opening entranceOpening,
            out int usedInputConnector)
        {
            usedInputConnector = -1;
            int direction = connector.Direction;
            Connector placementConnector = new Connector(
                connector.Position - Vector3Int.up,
                connector.Direction,
                connector.Depth,
                connector.ParentIndex,
                connector.PoolId,
                connector.FallbackPoolId,
                connector.SocketName,
                connector.TargetName,
                connector.OpeningWidth,
                connector.OpeningHeight);
            if (module.HasExplicitConnectors)
            {
                int matchingCount = 0;
                for (int i = 0; i < module.Connectors.Count; i++)
                {
                    if (ConnectorMatches(connector, module.Connectors[i]))
                    {
                        matchingCount++;
                        if (random.NextInt(matchingCount) == 0)
                        {
                            usedInputConnector = i;
                        }
                    }
                }
                if (usedInputConnector < 0)
                {
                    candidate = default;
                    entranceOpening = default;
                    return false;
                }
                JigsawConnectorSettings input =
                    module.Connectors[usedInputConnector];
                if (IsVerticalDirection(connector.Direction))
                {
                    // Consume the historical yaw roll so subsequent seeded
                    // choices remain stable, but keep vertically connected
                    // pieces in the same frame. Spiral ramp lanes are authored
                    // relative to that frame; rotating the child independently
                    // makes its lower lane cross and block the parent's upper
                    // lane inside the shared aperture.
                    random.NextInt(HorizontalDirectionCount);
                    direction = parentDirection & 3;
                }
                else
                {
                    direction = (OppositeDirection(connector.Direction)
                        - (int)input.Face) & 3;
                }
                placementConnector = new Connector(
                    Vector3Int.zero,
                    direction,
                    connector.Depth,
                    connector.ParentIndex,
                    connector.PoolId,
                    connector.FallbackPoolId,
                    connector.SocketName,
                    connector.TargetName,
                    connector.OpeningWidth,
                    connector.OpeningHeight);
            }

            if (module.HasTemplate)
            {
                candidate = CreateTemplatePiece(
                    moduleIndex,
                    module,
                    placementConnector.Position,
                    direction,
                    connector.Depth,
                    connector.ParentIndex);
            }
            else switch (module.Shape)
            {
                case JigsawPieceDefinition.Shape.Room:
                case JigsawPieceDefinition.Shape.Crossing:
                case JigsawPieceDefinition.Shape.VerticalShaft:
                    candidate = CreateBox(
                        moduleIndex,
                        module,
                        placementConnector,
                        ref random);
                    break;
                case JigsawPieceDefinition.Shape.Stairs:
                    candidate = CreatePassage(
                        moduleIndex,
                        module,
                        placementConnector,
                        true,
                        ref random);
                    break;
                default:
                    candidate = CreatePassage(
                        moduleIndex,
                        module,
                        placementConnector,
                        false,
                        ref random);
                    break;
            }

            if (usedInputConnector >= 0)
            {
                JigsawConnectorSettings input =
                    module.Connectors[usedInputConnector];
                Vector3Int boundary = GetAuthoredConnectorBoundary(
                    candidate,
                    module,
                    input);
                candidate = TranslatePiece(
                    candidate,
                    connector.Position - boundary);
            }
            else if (module.HasTemplate)
            {
                var syntheticInput = new JigsawConnectorSettings(
                    "synthetic_entrance",
                    JigsawConnectorDefinition.Role.Input,
                    JigsawConnectorDefinition.Face.Back,
                    JigsawConnectorDefinition.Joint.Aligned,
                    "*",
                    "*",
                    module.PoolId,
                    string.Empty,
                    -1,
                    0,
                    1,
                    1f,
                    connector.OpeningWidth,
                    connector.OpeningHeight);
                Vector3Int boundary = GetAuthoredConnectorBoundary(
                    candidate,
                    module,
                    syntheticInput);
                candidate = TranslatePiece(
                    candidate,
                    connector.Position - boundary);
            }
            int entranceDirection = OppositeDirection(connector.Direction);
            entranceOpening = new Opening(
                connector.Position,
                entranceDirection,
                connector.OpeningWidth,
                connector.OpeningHeight);
            return true;
        }

        private static Piece CreateTemplatePiece(
            int moduleIndex,
            JigsawPieceSettings module,
            Vector3Int worldAnchor,
            int direction,
            int depth,
            int parentIndex)
        {
            Vector3Int size = module.TemplateSize;
            Vector3Int anchor = module.TemplateAnchor;
            int minX = int.MaxValue;
            int minZ = int.MaxValue;
            int maxX = int.MinValue;
            int maxZ = int.MinValue;
            int[] localXs = { -anchor.x, size.x - 1 - anchor.x };
            int[] localZs = { -anchor.z, size.z - 1 - anchor.z };
            Vector3Int forward = DirectionVector(direction);
            Vector3Int right = DirectionVector((direction + 1) & 3);
            for (int xIndex = 0; xIndex < localXs.Length; xIndex++)
            {
                for (int zIndex = 0; zIndex < localZs.Length; zIndex++)
                {
                    int worldX = worldAnchor.x
                        + right.x * localXs[xIndex]
                        + forward.x * localZs[zIndex];
                    int worldZ = worldAnchor.z
                        + right.z * localXs[xIndex]
                        + forward.z * localZs[zIndex];
                    minX = Math.Min(minX, worldX);
                    maxX = Math.Max(maxX, worldX);
                    minZ = Math.Min(minZ, worldZ);
                    maxZ = Math.Max(maxZ, worldZ);
                }
            }
            IntBounds bounds = new IntBounds(
                minX,
                worldAnchor.y - anchor.y,
                minZ,
                maxX,
                worldAnchor.y + size.y - 1 - anchor.y,
                maxZ);
            return new Piece(
                moduleIndex,
                module.StableId,
                module.Shape,
                bounds,
                worldAnchor,
                direction,
                size.z,
                bounds.MinY,
                bounds.MinY,
                depth,
                parentIndex,
                0);
        }

        private static Piece CreateBox(
            int moduleIndex,
            JigsawPieceSettings module,
            Connector connector,
            ref DeterministicRandom random)
        {
            int localWidth = NextOddInRange(
                module.MinimumWidth,
                module.MaximumWidth,
                ref random);
            int localDepth = NextOddInRange(
                module.MinimumDepth,
                module.MaximumDepth,
                ref random);
            int height = NextInRange(
                module.MinimumHeight,
                module.MaximumHeight,
                ref random);
            Vector3Int forward = DirectionVector(connector.Direction);
            int halfForward = localDepth / 2;
            Vector3Int centre = connector.Position + forward * halfForward;
            int halfX = (connector.Direction & 1) == 0
                ? localWidth / 2
                : localDepth / 2;
            int halfZ = (connector.Direction & 1) == 0
                ? localDepth / 2
                : localWidth / 2;
            IntBounds bounds = new IntBounds(
                centre.x - halfX,
                connector.Position.y,
                centre.z - halfZ,
                centre.x + halfX,
                connector.Position.y + height,
                centre.z + halfZ);
            return new Piece(
                moduleIndex,
                module.StableId,
                module.Shape,
                bounds,
                centre,
                connector.Direction,
                localDepth,
                connector.Position.y,
                connector.Position.y,
                connector.Depth,
                connector.ParentIndex,
                0);
        }

        private static Piece CreatePassage(
            int moduleIndex,
            JigsawPieceSettings module,
            Connector connector,
            bool stairs,
            ref DeterministicRandom random)
        {
            int length = NextInRange(
                module.MinimumLength,
                module.MaximumLength,
                ref random);
            int verticalDelta = stairs
                ? (random.NextDouble() < module.DescendingChance
                    ? -module.VerticalDelta
                    : module.VerticalDelta)
                : 0;
            Vector3Int forward = DirectionVector(connector.Direction);
            Vector3Int right = DirectionVector((connector.Direction + 1) & 3);
            Vector3Int end = connector.Position + forward * (length - 1);
            IntBounds bounds = BoundsAroundLine(
                connector.Position,
                end,
                right,
                module.Width / 2,
                Math.Min(
                    connector.Position.y,
                    connector.Position.y + verticalDelta),
                Math.Max(
                    connector.Position.y,
                    connector.Position.y + verticalDelta) + module.Height);
            return new Piece(
                moduleIndex,
                module.StableId,
                module.Shape,
                bounds,
                connector.Position,
                connector.Direction,
                length,
                connector.Position.y,
                connector.Position.y + verticalDelta,
                connector.Depth,
                connector.ParentIndex,
                0);
        }

        private static bool ConnectorMatches(
            Connector output,
            JigsawConnectorSettings input)
        {
            return input.CanAcceptInput
                && CanOrientInput(output.Direction, input.Face)
                && CanUseWildcardSocketNames(output, input)
                && NamesMatch(output.TargetName, input.SocketName)
                && NamesMatch(input.TargetName, output.SocketName);
        }

        private static bool CanUseWildcardSocketNames(
            Connector output,
            JigsawConnectorSettings input)
        {
            bool reservedLiftSocket = IsLiftSocketName(output.SocketName)
                || IsLiftSocketName(output.TargetName)
                || IsLiftSocketName(input.SocketName)
                || IsLiftSocketName(input.TargetName);
            if (!reservedLiftSocket)
            {
                return true;
            }

            return output.SocketName != "*"
                && output.TargetName != "*"
                && input.SocketName != "*"
                && input.TargetName != "*";
        }

        private static bool IsLiftSocketName(string value)
        {
            return value != null
                && value.IndexOf(
                    "fort_lift_",
                    StringComparison.Ordinal) >= 0;
        }

        private static bool NamesMatch(string expected, string actual)
        {
            return expected == "*" || actual == "*"
                || string.Equals(expected, actual, StringComparison.Ordinal);
        }

        private static Vector3Int GetAuthoredConnectorBoundary(
            Piece piece,
            JigsawPieceSettings module,
            JigsawConnectorSettings connector)
        {
            // A template marker already knows exactly which voxel it sits on, so
            // rotate that local position instead of inferring one from the
            // piece's generated dimensions.
            if (connector.HasTemplatePosition && module.HasTemplate)
            {
                Vector3Int templateForward = DirectionVector(piece.Direction);
                Vector3Int templateRight = DirectionVector(
                    (piece.Direction + 1) & 3);
                int localX = connector.TemplatePosition.x
                    - module.TemplateAnchor.x;
                int localZ = connector.TemplatePosition.z
                    - module.TemplateAnchor.z;
                return new Vector3Int(
                    piece.Origin.x
                        + templateRight.x * localX
                        + templateForward.x * localZ,
                    piece.Origin.y
                        + connector.TemplatePosition.y
                        - module.TemplateAnchor.y,
                    piece.Origin.z
                        + templateRight.z * localX
                        + templateForward.z * localZ);
            }

            int worldDirection = GetWorldDirection(
                piece.Direction,
                connector.Face);
            bool box = module.HasTemplate || IsBoxShape(piece.Shape);
            if (IsVerticalDirection(worldDirection))
            {
                Vector3Int verticalForward = DirectionVector(piece.Direction);
                Vector3Int verticalRight = DirectionVector(
                    (piece.Direction + 1) & 3);
                int verticalAlong = connector.AlongOffset < 0
                    ? (box ? 0 : piece.Length / 2)
                    : (box
                        ? connector.AlongOffset
                        : Math.Min(piece.Length - 1, connector.AlongOffset));
                int surfaceY;
                if (box)
                {
                    surfaceY = worldDirection == UpDirection
                        ? piece.Bounds.MaxY
                        : piece.Bounds.MinY;
                }
                else
                {
                    int verticalFloorY = GetFloorY(piece, verticalAlong);
                    surfaceY = worldDirection == UpDirection
                        ? verticalFloorY + module.Height
                        : verticalFloorY;
                }
                return piece.Origin
                    + verticalForward * verticalAlong
                    + verticalRight * connector.LateralOffset
                    + Vector3Int.up * (surfaceY - piece.Origin.y);
            }
            if (box)
            {
                Vector3Int side = DirectionVector(worldDirection);
                Vector3Int right = DirectionVector((worldDirection + 1) & 3);
                int halfExtent = IsXAxis(worldDirection)
                    ? (piece.Bounds.MaxX - piece.Bounds.MinX) / 2
                    : (piece.Bounds.MaxZ - piece.Bounds.MinZ) / 2;
                return new Vector3Int(
                    piece.Origin.x
                        + side.x * halfExtent
                        + right.x * connector.LateralOffset,
                    piece.Bounds.MinY + connector.VerticalOffset,
                    piece.Origin.z
                        + side.z * halfExtent
                        + right.z * connector.LateralOffset);
            }

            Vector3Int forward = DirectionVector(piece.Direction);
            Vector3Int rightAxis = DirectionVector((piece.Direction + 1) & 3);
            int along;
            int lateral;
            switch (connector.Face)
            {
                case JigsawConnectorDefinition.Face.Forward:
                    along = piece.Length - 1;
                    lateral = connector.LateralOffset;
                    break;
                case JigsawConnectorDefinition.Face.Back:
                    along = 0;
                    lateral = -connector.LateralOffset;
                    break;
                case JigsawConnectorDefinition.Face.Right:
                    along = connector.AlongOffset < 0
                        ? piece.Length / 2
                        : Math.Min(piece.Length - 1, connector.AlongOffset);
                    lateral = module.Width / 2;
                    break;
                default:
                    along = connector.AlongOffset < 0
                        ? piece.Length / 2
                        : Math.Min(piece.Length - 1, connector.AlongOffset);
                    lateral = -module.Width / 2;
                    break;
            }
            int floorY = GetFloorY(piece, along);
            return piece.Origin
                + forward * along
                + rightAxis * lateral
                + Vector3Int.up * (floorY - piece.Origin.y
                    + connector.VerticalOffset);
        }

        private static Piece TranslatePiece(Piece piece, Vector3Int offset)
        {
            IntBounds bounds = new IntBounds(
                piece.Bounds.MinX + offset.x,
                piece.Bounds.MinY + offset.y,
                piece.Bounds.MinZ + offset.z,
                piece.Bounds.MaxX + offset.x,
                piece.Bounds.MaxY + offset.y,
                piece.Bounds.MaxZ + offset.z);
            return new Piece(
                piece.ModuleIndex,
                piece.ModuleId,
                piece.Shape,
                bounds,
                piece.Origin + offset,
                piece.Direction,
                piece.Length,
                piece.StartFloorY + offset.y,
                piece.EndFloorY + offset.y,
                piece.Depth,
                piece.ParentIndex,
                piece.ConnectorMask);
        }

        private static bool CanAddPiece(
            JigsawStructureFeatureSettings feature,
            Placement placement,
            List<Piece> pieces,
            PieceSpatialIndex spatialIndex,
            Piece candidate,
            int parentIndex)
        {
            if (candidate.Bounds.MinY <= 1
                || candidate.Bounds.MaxY >= feature.WorldHeight - 1)
            {
                return false;
            }
            int distance = feature.MaxHorizontalDistance;
            if (candidate.Bounds.MinX < placement.Centre.x - distance
                || candidate.Bounds.MaxX > placement.Centre.x + distance
                || candidate.Bounds.MinZ < placement.Centre.z - distance
                || candidate.Bounds.MaxZ > placement.Centre.z + distance)
            {
                return false;
            }

            IntBounds clearance = candidate.Bounds.Expand(
                feature.CollisionPadding,
                0);
            foreach (int i in spatialIndex.Query(clearance))
            {
                if (i == parentIndex)
                {
                    continue;
                }
                if (clearance.Intersects(pieces[i].Bounds))
                {
                    return false;
                }
            }
            return true;
        }

        private static int EnqueueConnectors(
            Piece piece,
            JigsawPieceSettings module,
            int pieceIndex,
            Queue<Connector> connectors,
            List<Opening> openings,
            ref DeterministicRandom random,
            bool isStart,
            int usedInputConnector = -1)
        {
            int childDepth = piece.Depth + 1;
            int mask = 0;
            if (module.HasExplicitConnectors)
            {
                for (int i = 0; i < module.Connectors.Count; i++)
                {
                    JigsawConnectorSettings authored = module.Connectors[i];
                    if (i == usedInputConnector
                        || !authored.CanEmitOutput
                        || random.NextDouble() >= authored.ActivationChance)
                    {
                        continue;
                    }
                    int direction = GetWorldDirection(
                        piece.Direction,
                        authored.Face);
                    Vector3Int boundary = GetAuthoredConnectorBoundary(
                        piece,
                        module,
                        authored);
                    Vector3Int position = boundary + DirectionVector(direction);
                    connectors.Enqueue(new Connector(
                        position,
                        direction,
                        childDepth,
                        pieceIndex,
                        authored.TargetPoolId,
                        authored.FallbackPoolId,
                        authored.SocketName,
                        authored.TargetName,
                        authored.OpeningWidth,
                        authored.OpeningHeight));
                    mask |= 1 << direction;
                }
                return mask;
            }
            if (module.ConnectorPattern == JigsawPieceDefinition.ConnectorPattern.None)
            {
                return mask;
            }

            if (isStart)
            {
                int count;
                switch (module.ConnectorPattern)
                {
                    case JigsawPieceDefinition.ConnectorPattern.FourWay:
                        count = 4;
                        break;
                    case JigsawPieceDefinition.ConnectorPattern.ThreeWay:
                        count = 3;
                        break;
                    case JigsawPieceDefinition.ConnectorPattern.ForwardAndSides:
                        count = 1;
                        break;
                    default:
                        count = 1;
                        break;
                }
                for (int offset = 0; offset < count; offset++)
                {
                    int direction = (piece.Direction + offset) & 3;
                    EnqueueFromPieceSide(
                        piece,
                        direction,
                        childDepth,
                        pieceIndex,
                        module.OutputPoolId,
                        connectors,
                        openings);
                    mask |= 1 << direction;
                }
                if (module.ConnectorPattern
                    == JigsawPieceDefinition.ConnectorPattern.ForwardAndSides)
                {
                    mask |= EnqueueOptionalSideConnectors(
                        piece,
                        module,
                        childDepth,
                        pieceIndex,
                        connectors,
                        openings,
                        ref random);
                }
                return mask;
            }

            mask |= EnqueueDirection(
                piece,
                module,
                piece.Direction,
                childDepth,
                pieceIndex,
                connectors,
                openings);
            if (module.ConnectorPattern
                == JigsawPieceDefinition.ConnectorPattern.Forward)
            {
                return mask;
            }
            if (module.ConnectorPattern
                == JigsawPieceDefinition.ConnectorPattern.ForwardAndSides)
            {
                return mask | EnqueueOptionalSideConnectors(
                    piece,
                    module,
                    childDepth,
                    pieceIndex,
                    connectors,
                    openings,
                    ref random);
            }

            mask |= EnqueueDirection(
                piece,
                module,
                (piece.Direction + 1) & 3,
                childDepth,
                pieceIndex,
                connectors,
                openings);
            mask |= EnqueueDirection(
                piece,
                module,
                (piece.Direction + 3) & 3,
                childDepth,
                pieceIndex,
                connectors,
                openings);
            if (module.ConnectorPattern
                == JigsawPieceDefinition.ConnectorPattern.FourWay)
            {
                int back = (piece.Direction + 2) & 3;
                mask |= 1 << back;
            }
            return mask;
        }

        private static int EnqueueOptionalSideConnectors(
            Piece piece,
            JigsawPieceSettings module,
            int depth,
            int parentIndex,
            Queue<Connector> connectors,
            List<Opening> openings,
            ref DeterministicRandom random)
        {
            int mask = 0;
            int leftDirection = (piece.Direction + 3) & 3;
            int rightDirection = (piece.Direction + 1) & 3;
            if (random.NextDouble() < module.SideBranchChance)
            {
                mask |= EnqueueDirection(
                    piece,
                    module,
                    leftDirection,
                    depth,
                    parentIndex,
                    connectors,
                    openings);
            }
            if (random.NextDouble() < module.SideBranchChance)
            {
                mask |= EnqueueDirection(
                    piece,
                    module,
                    rightDirection,
                    depth,
                    parentIndex,
                    connectors,
                    openings);
            }
            return mask;
        }

        private static int EnqueueDirection(
            Piece piece,
            JigsawPieceSettings module,
            int direction,
            int depth,
            int parentIndex,
            Queue<Connector> connectors,
            List<Opening> openings)
        {
            EnqueueFromPieceSide(
                piece,
                direction,
                depth,
                parentIndex,
                module.OutputPoolId,
                connectors,
                openings);
            return 1 << direction;
        }

        private static void EnqueueFromPieceSide(
            Piece piece,
            int direction,
            int depth,
            int parentIndex,
            string poolId,
            Queue<Connector> connectors,
            List<Opening> openings)
        {
            Vector3Int position;
            bool box = IsBoxShape(piece.Shape);
            if (!box && direction == piece.Direction)
            {
                Vector3Int forward = DirectionVector(piece.Direction);
                Vector3Int end = piece.Origin + forward * piece.Length;
                position = new Vector3Int(end.x, piece.EndFloorY, end.z);
            }
            else if (!box)
            {
                Vector3Int forward = DirectionVector(piece.Direction);
                Vector3Int side = DirectionVector(direction);
                int midpoint = Math.Max(2, piece.Length / 2);
                int halfWidth = IsXAxis(piece.Direction)
                    ? (piece.Bounds.MaxZ - piece.Bounds.MinZ) / 2
                    : (piece.Bounds.MaxX - piece.Bounds.MinX) / 2;
                Vector3Int centre = piece.Origin + forward * midpoint;
                position = centre + side * (halfWidth + 1);
                position.y = GetFloorY(piece, midpoint);
            }
            else
            {
                switch (direction & 3)
                {
                    case 1:
                        position = new Vector3Int(
                            piece.Bounds.MaxX + 1,
                            piece.EndFloorY,
                            piece.Origin.z);
                        break;
                    case 2:
                        position = new Vector3Int(
                            piece.Origin.x,
                            piece.EndFloorY,
                            piece.Bounds.MinZ - 1);
                        break;
                    case 3:
                        position = new Vector3Int(
                            piece.Bounds.MinX - 1,
                            piece.EndFloorY,
                            piece.Origin.z);
                        break;
                    default:
                        position = new Vector3Int(
                            piece.Origin.x,
                            piece.EndFloorY,
                            piece.Bounds.MaxZ + 1);
                        break;
                }
            }
            connectors.Enqueue(new Connector(
                position + Vector3Int.up,
                direction,
                depth,
                parentIndex,
                poolId,
                string.Empty,
                "*",
                "*",
                3,
                3));
        }

        private static int ApplyLayoutToColumn(
            JigsawStructureFeatureSettings feature,
            IReadOnlyList<Piece> pieces,
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
            int changed = 0;
            for (int passIndex = 0;
                passIndex < RasterPasses.Length;
                passIndex++)
            {
                for (int i = 0; i < pieces.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    changed += ApplyPiecePass(
                        pieces[i],
                        feature.GetPiece(pieces[i].ModuleIndex),
                        feature,
                        RasterPasses[passIndex],
                        targetMinX,
                        targetMaxX,
                        targetMinZ,
                        targetMaxZ,
                        densities,
                        types,
                        solidDensity,
                        airDensity);
                }
            }
            return changed;
        }

        private static int ApplyPiecePass(
            Piece piece,
            JigsawPieceSettings module,
            JigsawStructureFeatureSettings feature,
            VoxelPass pass,
            int targetMinX,
            int targetMaxX,
            int targetMinZ,
            int targetMaxZ,
            float[] densities,
            VoxelTypeId[] types,
            float solidDensity,
            float airDensity)
        {
            if (pass == VoxelPass.Processor)
            {
                return module.HasProcessors
                    ? ApplyProcessors(
                        piece,
                        module,
                        feature,
                        targetMinX,
                        targetMaxX,
                        targetMinZ,
                        targetMaxZ,
                        densities,
                        types,
                        solidDensity,
                        airDensity)
                    : 0;
            }

            int minX = Math.Max(targetMinX, piece.Bounds.MinX);
            int maxX = Math.Min(targetMaxX, piece.Bounds.MaxX);
            int minZ = Math.Max(targetMinZ, piece.Bounds.MinZ);
            int maxZ = Math.Min(targetMaxZ, piece.Bounds.MaxZ);
            if (minX > maxX || minZ > maxZ)
            {
                return 0;
            }

            int changed = 0;
            float isoDensity = (solidDensity + airDensity) * 0.5f;
            VoxelTypeId primaryType = ResolvePrimaryType(module, feature);
            VoxelTypeId accentType = ResolveAccentType(module, feature);
            for (int worldZ = minZ; worldZ <= maxZ; worldZ++)
            {
                int localZ = worldZ - targetMinZ;
                for (int worldX = minX; worldX <= maxX; worldX++)
                {
                    int localX = worldX - targetMinX;
                    int maximumSampleY = pass == VoxelPass.Accent
                            && module.Decoration
                                == JigsawPieceDefinition.Decoration.SpiralStairs
                        ? Math.Min(
                            VoxelColumnChunkData.Height - 1,
                            piece.Bounds.MaxY + 1)
                        : piece.Bounds.MaxY;
                    for (int worldY = piece.Bounds.MinY;
                        worldY <= maximumSampleY;
                        worldY++)
                    {
                        bool conditionalFloor = false;
                        bool mergeSpiralRamp = false;
                        float targetDensity;
                        VoxelTypeId targetType;
                        if (module.HasTemplate)
                        {
                            if (!TryEvaluateTemplateSample(
                                piece,
                                module,
                                worldX,
                                worldY,
                                worldZ,
                                pass,
                                airDensity,
                                out targetDensity,
                                out targetType))
                            {
                                continue;
                            }
                        }
                        else if (pass == VoxelPass.Accent
                            && module.BuildStyle
                                == JigsawPieceDefinition.BuildStyle.Masonry
                            && IsBoxShape(module.Shape)
                            && module.Decoration
                                == JigsawPieceDefinition.Decoration.SpiralStairs
                            && TryEvaluateSpiralRampSample(
                                piece,
                                worldX,
                                worldY,
                                worldZ,
                                solidDensity,
                                airDensity,
                                out targetDensity,
                                out bool solidRampSample))
                        {
                            targetType = solidRampSample
                                ? accentType
                                : VoxelTypeId.Air;
                            mergeSpiralRamp = true;
                        }
                        else
                        {
                            if (!TryEvaluatePieceSample(
                                piece,
                                module,
                                worldX,
                                worldY,
                                worldZ,
                                pass,
                                out conditionalFloor))
                            {
                                continue;
                            }
                            bool air = pass == VoxelPass.Air;
                            targetDensity = air ? airDensity : solidDensity;
                            targetType = air
                                ? VoxelTypeId.Air
                                : pass == VoxelPass.Accent
                                    ? accentType
                                    : primaryType;
                        }

                        int index = VoxelColumnChunkData.ToIndex(
                            localX,
                            worldY,
                            localZ);
                        if (conditionalFloor && densities[index] >= isoDensity)
                        {
                            continue;
                        }
                        if (mergeSpiralRamp
                            && types[index] == accentType
                            && densities[index] >= targetDensity)
                        {
                            // Adjacent vertical pieces may both contribute a
                            // ramp tongue inside the shared aperture. Treat the
                            // ramp fields as a solid union so a later spiral's
                            // empty slab samples cannot erase the earlier
                            // piece's walkable connection. Primary shell voxels
                            // are still carved when the ramp needs headroom.
                            continue;
                        }
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

        /// <summary>
        /// Runs every authored processor for one piece against the current voxel
        /// column. Processors read the already-rasterized field, so they can find
        /// the terrain surface below a piece and stop there.
        /// </summary>
        private static int ApplyProcessors(
            Piece piece,
            JigsawPieceSettings module,
            JigsawStructureFeatureSettings feature,
            int targetMinX,
            int targetMaxX,
            int targetMinZ,
            int targetMaxZ,
            float[] densities,
            VoxelTypeId[] types,
            float solidDensity,
            float airDensity)
        {
            int changed = 0;
            for (int i = 0; i < module.Processors.Count; i++)
            {
                JigsawProcessorSettings processor = module.Processors[i];
                if (processor.Chance <= 0f)
                {
                    continue;
                }
                int minX = Math.Max(
                    targetMinX,
                    piece.Bounds.MinX + processor.Inset);
                int maxX = Math.Min(
                    targetMaxX,
                    piece.Bounds.MaxX - processor.Inset);
                int minZ = Math.Max(
                    targetMinZ,
                    piece.Bounds.MinZ + processor.Inset);
                int maxZ = Math.Min(
                    targetMaxZ,
                    piece.Bounds.MaxZ - processor.Inset);
                if (minX > maxX || minZ > maxZ)
                {
                    continue;
                }

                for (int worldZ = minZ; worldZ <= maxZ; worldZ++)
                {
                    for (int worldX = minX; worldX <= maxX; worldX++)
                    {
                        changed += ApplyProcessorColumn(
                            piece,
                            module,
                            feature,
                            processor,
                            worldX,
                            worldZ,
                            targetMinX,
                            targetMinZ,
                            densities,
                            types,
                            solidDensity,
                            airDensity);
                    }
                }
            }
            return changed;
        }

        private static int ApplyProcessorColumn(
            Piece piece,
            JigsawPieceSettings module,
            JigsawStructureFeatureSettings feature,
            JigsawProcessorSettings processor,
            int worldX,
            int worldZ,
            int targetMinX,
            int targetMinZ,
            float[] densities,
            VoxelTypeId[] types,
            float solidDensity,
            float airDensity)
        {
            int localX = worldX - targetMinX;
            int localZ = worldZ - targetMinZ;
            VoxelTypeId writeType = processor.ResolveType(
                ResolvePrimaryType(module, feature),
                ResolveAccentType(module, feature));
            bool fillsBelow =
                processor.Kind
                    == JigsawProcessorDefinition.Kind.SupportToGround
                || processor.Kind
                    == JigsawProcessorDefinition.Kind.FoundationFill;
            if (fillsBelow
                && HasOpeningInDirection(piece, DownDirection))
            {
                // A connected Down socket means the complete volume below this
                // floor belongs to a playable vertical passage. Foundation and
                // support processors run after every accent pass, so even a
                // write outside the socket aperture can re-seal the spiral
                // ramp that approaches the upper floor around that aperture.
                return 0;
            }

            switch (processor.Kind)
            {
                case JigsawProcessorDefinition.Kind.SupportToGround:
                    if (processor.PerimeterOnly
                        && !IsPerimeterColumn(piece, processor, worldX, worldZ))
                    {
                        return 0;
                    }
                    if (!ShouldApply(processor, piece, worldX, 0, worldZ))
                    {
                        return 0;
                    }
                    return FillDownwards(
                        piece.Bounds.MinY - 1,
                        processor.MaximumDistance,
                        true,
                        localX,
                        localZ,
                        densities,
                        types,
                        solidDensity,
                        writeType);

                case JigsawProcessorDefinition.Kind.FoundationFill:
                    if (!ShouldApply(processor, piece, worldX, 0, worldZ))
                    {
                        return 0;
                    }
                    return FillDownwards(
                        piece.Bounds.MinY - 1,
                        processor.MaximumDistance,
                        false,
                        localX,
                        localZ,
                        densities,
                        types,
                        solidDensity,
                        writeType);

                case JigsawProcessorDefinition.Kind.ClearAbove:
                    return ClearUpwards(
                        piece.Bounds.MaxY + 1,
                        processor.MaximumDistance,
                        localX,
                        localZ,
                        densities,
                        types,
                        airDensity);

                case JigsawProcessorDefinition.Kind.Weathering:
                    return ApplyWeathering(
                        piece,
                        module,
                        feature,
                        processor,
                        worldX,
                        worldZ,
                        localX,
                        localZ,
                        densities,
                        types,
                        solidDensity,
                        writeType);

                default:
                    return 0;
            }
        }

        /// <summary>
        /// Writes a solid column below a piece. When <paramref name="stopAtSolid"/>
        /// is set the column ends as soon as existing terrain is reached, which is
        /// how bridge and platform pillars gain continuous footings.
        /// </summary>
        private static int FillDownwards(
            int startY,
            int maximumDistance,
            bool stopAtSolid,
            int localX,
            int localZ,
            float[] densities,
            VoxelTypeId[] types,
            float solidDensity,
            VoxelTypeId writeType)
        {
            int changed = 0;
            for (int step = 0; step < maximumDistance; step++)
            {
                int worldY = startY - step;
                if (worldY <= 1)
                {
                    break;
                }
                int index = VoxelColumnChunkData.ToIndex(localX, worldY, localZ);
                bool alreadySolid = !types[index].IsAir && densities[index] >= 0f;
                if (stopAtSolid && alreadySolid)
                {
                    break;
                }
                if (densities[index] == solidDensity
                    && types[index] == writeType)
                {
                    continue;
                }
                densities[index] = solidDensity;
                types[index] = writeType;
                changed++;
            }
            return changed;
        }

        private static int ClearUpwards(
            int startY,
            int maximumDistance,
            int localX,
            int localZ,
            float[] densities,
            VoxelTypeId[] types,
            float airDensity)
        {
            int changed = 0;
            for (int step = 0; step < maximumDistance; step++)
            {
                int worldY = startY + step;
                if (worldY >= VoxelColumnChunkData.Height - 1)
                {
                    break;
                }
                int index = VoxelColumnChunkData.ToIndex(localX, worldY, localZ);
                if (densities[index] == airDensity && types[index].IsAir)
                {
                    continue;
                }
                densities[index] = airDensity;
                types[index] = VoxelTypeId.Air;
                changed++;
            }
            return changed;
        }

        /// <summary>
        /// Substitutes a fraction of the piece's own solid voxels for a second
        /// palette, producing the mixed brick look of stronghold masonry. Only
        /// voxels this structure wrote are eligible, so surrounding terrain and
        /// ore veins are never recoloured.
        /// </summary>
        private static int ApplyWeathering(
            Piece piece,
            JigsawPieceSettings module,
            JigsawStructureFeatureSettings feature,
            JigsawProcessorSettings processor,
            int worldX,
            int worldZ,
            int localX,
            int localZ,
            float[] densities,
            VoxelTypeId[] types,
            float solidDensity,
            VoxelTypeId writeType)
        {
            int changed = 0;
            VoxelTypeId ownedPrimary = ResolvePrimaryType(module, feature);
            VoxelTypeId ownedAccent = ResolveAccentType(module, feature);
            for (int worldY = piece.Bounds.MinY;
                worldY <= piece.Bounds.MaxY;
                worldY++)
            {
                int index = VoxelColumnChunkData.ToIndex(localX, worldY, localZ);
                if (densities[index] < 0f || types[index] == writeType)
                {
                    continue;
                }
                bool ownedByStructure = types[index] == ownedPrimary
                    || types[index] == ownedAccent;
                if (!ownedByStructure)
                {
                    continue;
                }
                if (!ShouldApply(processor, piece, worldX, worldY, worldZ))
                {
                    continue;
                }
                densities[index] = solidDensity;
                types[index] = writeType;
                changed++;
            }
            return changed;
        }

        /// <summary>
        /// A mixed pool carries modules from several families, so the module's own
        /// authored palette wins when it has one. Single-family features leave the
        /// override unset and keep using the feature palette.
        /// </summary>
        private static VoxelTypeId ResolvePrimaryType(
            JigsawPieceSettings module,
            JigsawStructureFeatureSettings feature)
        {
            return module.HasPaletteOverride
                ? module.PrimaryTypeOverride
                : feature.PrimaryType;
        }

        private static VoxelTypeId ResolveAccentType(
            JigsawPieceSettings module,
            JigsawStructureFeatureSettings feature)
        {
            return module.HasPaletteOverride
                ? module.AccentTypeOverride
                : feature.AccentType;
        }

        private static bool IsPerimeterColumn(
            Piece piece,
            JigsawProcessorSettings processor,
            int worldX,
            int worldZ)
        {
            return worldX == piece.Bounds.MinX + processor.Inset
                || worldX == piece.Bounds.MaxX - processor.Inset
                || worldZ == piece.Bounds.MinZ + processor.Inset
                || worldZ == piece.Bounds.MaxZ - processor.Inset;
        }

        /// <summary>
        /// Per-voxel processor roll. Keyed on world coordinates rather than an
        /// iteration counter so the outcome is identical no matter which column
        /// is streamed first.
        /// </summary>
        private static bool ShouldApply(
            JigsawProcessorSettings processor,
            Piece piece,
            int worldX,
            int worldY,
            int worldZ)
        {
            if (processor.Chance >= 1f)
            {
                return true;
            }
            ulong hash = Mix(unchecked(
                (ulong)(uint)worldX * 0x9E3779B185EBCA87UL
                ^ (ulong)(uint)worldY * 0xC2B2AE3D27D4EB4FUL
                ^ (ulong)(uint)worldZ * 0x165667B19E3779F9UL
                ^ (ulong)(uint)processor.Salt
                ^ (ulong)(uint)piece.ModuleIndex));
            double roll = (hash >> 11) * (1.0 / 9007199254740992.0);
            return roll < processor.Chance;
        }

        private static bool TryEvaluateTemplateSample(
            Piece piece,
            JigsawPieceSettings module,
            int worldX,
            int worldY,
            int worldZ,
            VoxelPass pass,
            float airDensity,
            out float targetDensity,
            out VoxelTypeId targetType)
        {
            targetDensity = 0f;
            targetType = VoxelTypeId.Air;
            if (pass == VoxelPass.Accent)
            {
                return false;
            }
            Vector3Int forward = DirectionVector(piece.Direction);
            Vector3Int right = DirectionVector((piece.Direction + 1) & 3);
            int deltaX = worldX - piece.Origin.x;
            int deltaZ = worldZ - piece.Origin.z;
            int localX = deltaX * right.x
                + deltaZ * right.z
                + module.TemplateAnchor.x;
            int localY = worldY - piece.Origin.y + module.TemplateAnchor.y;
            int localZ = deltaX * forward.x
                + deltaZ * forward.z
                + module.TemplateAnchor.z;
            if ((uint)localX >= module.TemplateSize.x
                || (uint)localY >= module.TemplateSize.y
                || (uint)localZ >= module.TemplateSize.z)
            {
                return false;
            }

            if (pass == VoxelPass.Air
                && IsPieceOpening(piece, worldX, worldY, worldZ, 0))
            {
                targetDensity = airDensity;
                targetType = VoxelTypeId.Air;
                return true;
            }

            VoxelSample sample = module.GetTemplateSample(
                localX,
                localY,
                localZ);
            bool solid = sample.Density >= 0f && !sample.Type.IsAir;
            if (pass == VoxelPass.Shell)
            {
                if (!solid)
                {
                    return false;
                }
                targetDensity = sample.Density;
                targetType = sample.Type;
                return true;
            }
            if (solid || !module.TemplateWritesAir)
            {
                return false;
            }
            targetDensity = sample.Density < 0f ? sample.Density : airDensity;
            targetType = VoxelTypeId.Air;
            return true;
        }

        private static bool TryEvaluatePieceSample(
            Piece piece,
            JigsawPieceSettings module,
            int worldX,
            int worldY,
            int worldZ,
            VoxelPass pass,
            out bool conditionalFloor)
        {
            conditionalFloor = false;
            bool box = IsBoxShape(piece.Shape);
            if (module.BuildStyle == JigsawPieceDefinition.BuildStyle.Masonry)
            {
                return box
                    ? EvaluateMasonryBox(
                        piece,
                        module,
                        worldX,
                        worldY,
                        worldZ,
                        pass)
                    : EvaluateMasonryPassage(
                        piece,
                        module,
                        worldX,
                        worldY,
                        worldZ,
                        pass);
            }

            if (pass == VoxelPass.Shell)
            {
                return false;
            }
            if (box)
            {
                if (pass == VoxelPass.Air)
                {
                    return worldY > piece.Bounds.MinY
                        && worldY < piece.Bounds.MaxY;
                }
                return EvaluateExcavatedBoxAccent(
                    piece,
                    module,
                    worldX,
                    worldY,
                    worldZ);
            }

            GetLocalCoordinates(
                piece,
                worldX,
                worldZ,
                out int along,
                out int lateral);
            if ((uint)along >= piece.Length
                || Math.Abs(lateral) > module.Width / 2)
            {
                return false;
            }
            int floorY = GetFloorY(piece, along);
            if (pass == VoxelPass.Air)
            {
                return worldY > floorY && worldY < floorY + module.Height;
            }
            if (module.Decoration
                != JigsawPieceDefinition.Decoration.SupportFrames)
            {
                return false;
            }
            if (worldY == floorY)
            {
                conditionalFloor = piece.Shape
                    == JigsawPieceDefinition.Shape.Corridor;
                return true;
            }
            bool frame = along == 0
                || along == piece.Length - 1
                || (piece.Shape == JigsawPieceDefinition.Shape.Corridor
                    && along % module.DecorationSpacing == 0);
            if (!frame)
            {
                return false;
            }
            bool sidePost = Math.Abs(lateral) == module.Width / 2
                && worldY > floorY
                && worldY < floorY + module.Height;
            bool topBeam = worldY == floorY + module.Height;
            return sidePost || topBeam;
        }

        private static bool EvaluateExcavatedBoxAccent(
            Piece piece,
            JigsawPieceSettings module,
            int worldX,
            int worldY,
            int worldZ)
        {
            if (module.Decoration
                != JigsawPieceDefinition.Decoration.SupportFrames)
            {
                return false;
            }
            bool cornerPost = (worldX == piece.Bounds.MinX
                    || worldX == piece.Bounds.MaxX)
                && (worldZ == piece.Bounds.MinZ
                    || worldZ == piece.Bounds.MaxZ)
                && worldY > piece.StartFloorY
                && worldY < piece.Bounds.MaxY;
            bool ceilingCross = worldY == piece.Bounds.MaxY
                && (worldX == piece.Origin.x || worldZ == piece.Origin.z);
            return cornerPost || ceilingCross;
        }

        private static bool EvaluateMasonryBox(
            Piece piece,
            JigsawPieceSettings module,
            int worldX,
            int worldY,
            int worldZ,
            VoxelPass pass)
        {
            bool boundary = worldY == piece.Bounds.MinY
                || worldY == piece.Bounds.MaxY
                || worldX == piece.Bounds.MinX
                || worldX == piece.Bounds.MaxX
                || worldZ == piece.Bounds.MinZ
                || worldZ == piece.Bounds.MaxZ;
            if (pass == VoxelPass.Shell)
            {
                return boundary;
            }
            if (pass == VoxelPass.Air)
            {
                bool interior = worldX > piece.Bounds.MinX
                    && worldX < piece.Bounds.MaxX
                    && worldZ > piece.Bounds.MinZ
                    && worldZ < piece.Bounds.MaxZ
                    && worldY > piece.Bounds.MinY
                    && worldY < piece.Bounds.MaxY;
                return interior || IsPieceOpening(
                    piece,
                    worldX,
                    worldY,
                    worldZ,
                    0);
            }
            return EvaluateMasonryBoxAccent(
                piece,
                module,
                worldX,
                worldY,
                worldZ);
        }

        private static bool EvaluateMasonryBoxAccent(
            Piece piece,
            JigsawPieceSettings module,
            int worldX,
            int worldY,
            int worldZ)
        {
            switch (module.Decoration)
            {
                case JigsawPieceDefinition.Decoration.LibraryShelves:
                    int shelfTop = Math.Min(
                        piece.Bounds.MinY + 3,
                        piece.Bounds.MaxY - 1);
                    bool shelfBand = worldY > piece.Bounds.MinY
                        && worldY <= shelfTop;
                    bool shelfPosition = worldX == piece.Bounds.MinX + 1
                        || worldX == piece.Bounds.MaxX - 1
                        || worldZ == piece.Bounds.MinZ + 1
                        || worldZ == piece.Bounds.MaxZ - 1;
                    return shelfBand
                        && shelfPosition
                        && !IsPieceOpening(
                            piece,
                            worldX,
                            worldY,
                            worldZ,
                            1);

                case JigsawPieceDefinition.Decoration.Pillars:
                    if (worldY <= piece.Bounds.MinY
                        || worldY >= piece.Bounds.MaxY)
                    {
                        return false;
                    }
                    bool pillarX = worldX == piece.Bounds.MinX + 2
                        || worldX == piece.Bounds.MaxX - 2;
                    bool pillarZ = worldZ == piece.Bounds.MinZ + 2
                        || worldZ == piece.Bounds.MaxZ - 2;
                    return pillarX && pillarZ;

                case JigsawPieceDefinition.Decoration.PrisonCells:
                    if (worldY <= piece.Bounds.MinY
                        || worldY >= piece.Bounds.MaxY)
                    {
                        return false;
                    }
                    GetLocalCoordinates(
                        piece,
                        worldX,
                        worldZ,
                        out int cellAlong,
                        out int cellLateral);
                    int forwardExtent = IsXAxis(piece.Direction)
                        ? (piece.Bounds.MaxX - piece.Bounds.MinX) / 2
                        : (piece.Bounds.MaxZ - piece.Bounds.MinZ) / 2;
                    bool cellFront = cellAlong == Math.Max(2, forwardExtent / 3);
                    bool bar = (cellLateral % 2) == 0;
                    bool centralDoor = Math.Abs(cellLateral) <= 1
                        && worldY <= piece.Bounds.MinY + 3;
                    return cellFront && bar && !centralDoor;

                case JigsawPieceDefinition.Decoration.PortalFrame:
                    GetLocalCoordinates(
                        piece,
                        worldX,
                        worldZ,
                        out int portalAlong,
                        out int portalLateral);
                    int depthExtent = IsXAxis(piece.Direction)
                        ? (piece.Bounds.MaxX - piece.Bounds.MinX) / 2
                        : (piece.Bounds.MaxZ - piece.Bounds.MinZ) / 2;
                    int portalY = worldY - piece.Bounds.MinY;
                    bool portalPlane = portalAlong == Math.Max(2, depthExtent - 2);
                    bool portalSide = Math.Abs(portalLateral) == 3
                        && portalY >= 1
                        && portalY <= 5;
                    bool portalTop = Math.Abs(portalLateral) <= 3
                        && portalY == 5;
                    return portalPlane && (portalSide || portalTop);

                case JigsawPieceDefinition.Decoration.SpiralStairs:
                    // Spiral ramps need fractional densities so marching cubes
                    // produces a walkable slope. ApplyPiecePass handles them
                    // before the boolean accent path reaches this switch.
                    return false;

                default:
                    return false;
            }
        }

        private static bool TryEvaluateSpiralRampSample(
            Piece piece,
            int worldX,
            int worldY,
            int worldZ,
            float solidDensity,
            float airDensity,
            out float targetDensity,
            out bool solidSample)
        {
            targetDensity = 0f;
            solidSample = false;
            if (!TryGetOpeningInDirection(
                    piece,
                    UpDirection,
                    out Opening upperOpening)
                || worldY < piece.Bounds.MinY
                || worldY > piece.Bounds.MaxY + 1)
            {
                return false;
            }

            GetLocalCoordinates(
                piece,
                worldX,
                worldZ,
                out int along,
                out int lateral);
            int halfAlong = IsXAxis(piece.Direction)
                ? (piece.Bounds.MaxX - piece.Bounds.MinX) / 2
                : (piece.Bounds.MaxZ - piece.Bounds.MinZ) / 2;
            int halfLateral = IsXAxis(piece.Direction)
                ? (piece.Bounds.MaxZ - piece.Bounds.MinZ) / 2
                : (piece.Bounds.MaxX - piece.Bounds.MinX) / 2;
            int openingHalfSpan =
                Math.Min(upperOpening.Width, upperOpening.Height) / 2;
            int outerRadius = Math.Min(halfAlong, halfLateral) - 2;
            const int WalkwayWidth = 3;
            int walkwayWidth = WalkwayWidth;
            int innerRadius = outerRadius - walkwayWidth + 1;
            if (openingHalfSpan < 2
                || outerRadius < 4
                || innerRadius <= openingHalfSpan)
            {
                return false;
            }

            int lowerRingY = piece.Bounds.MinY + 1;
            int upperRingY = Math.Max(
                lowerRingY,
                piece.Bounds.MaxY - 7);
            int chebyshevRadius = Math.Max(
                Math.Abs(along),
                Math.Abs(lateral));
            double maximumSignedDistance = double.NegativeInfinity;
            bool onStairBand = chebyshevRadius >= innerRadius
                && chebyshevRadius <= outerRadius;
            if (onStairBand)
            {
                double ringSurfaceY = GetSpiralRingSurfaceY(
                    along,
                    lateral,
                    lowerRingY,
                    upperRingY);
                maximumSignedDistance = Math.Max(
                    maximumSignedDistance,
                    GetRampSlabSignedDistance(ringSurfaceY, worldY));
            }

            // Two parallel three-voxel lanes cross the vertical aperture. The
            // negative lane connects the room floor to the bottom of the helix;
            // the positive lane continues the top of the helix through the roof
            // and onto the connected room's floor. Keeping the lanes separate
            // lets both ramps occupy the same plan area without sealing the hole.
            int bridgeStart = -outerRadius;
            int bridgeEnd = openingHalfSpan;
            if (along >= bridgeStart && along <= bridgeEnd)
            {
                double bridgeProgress = (along - bridgeStart)
                    / (double)(bridgeEnd - bridgeStart);
                if (lateral >= -openingHalfSpan && lateral <= -1)
                {
                    double seamSurfaceY = GetSpiralRingSurfaceY(
                        bridgeStart,
                        lateral,
                        lowerRingY,
                        upperRingY);
                    double lowerBridgeSurfaceY = seamSurfaceY
                        + (piece.Bounds.MinY - seamSurfaceY)
                            * bridgeProgress;
                    maximumSignedDistance = Math.Max(
                        maximumSignedDistance,
                        GetRampSlabSignedDistance(
                            lowerBridgeSurfaceY,
                            worldY));
                }
                else if (lateral >= 1
                    && lateral <= openingHalfSpan)
                {
                    double seamSurfaceY = GetSpiralRingSurfaceY(
                        bridgeStart,
                        lateral,
                        lowerRingY,
                        upperRingY);
                    double upperBridgeSurfaceY = seamSurfaceY
                        + (piece.Bounds.MaxY + 1 - seamSurfaceY)
                            * bridgeProgress;
                    maximumSignedDistance = Math.Max(
                        maximumSignedDistance,
                        GetRampSlabSignedDistance(
                            upperBridgeSurfaceY,
                            worldY));
                }
            }

            if (double.IsNegativeInfinity(maximumSignedDistance))
            {
                return false;
            }

            double normalizedDensity = Math.Max(
                -1.0,
                Math.Min(1.0, maximumSignedDistance));
            float isoDensity = (solidDensity + airDensity) * 0.5f;
            float densityAmplitude =
                Math.Abs(solidDensity - airDensity) * 0.5f;
            targetDensity = isoDensity
                + (float)normalizedDensity * densityAmplitude;
            solidSample = maximumSignedDistance >= 0.0;
            return true;
        }

        private static double GetSpiralRingSurfaceY(
            int along,
            int lateral,
            int lowerY,
            int upperY)
        {
            double angle = Math.Atan2(lateral, along);
            double progress = (angle + Math.PI) / (Math.PI * 2.0);
            return lowerY + (upperY - lowerY) * progress;
        }

        private static double GetRampSlabSignedDistance(
            double surfaceY,
            int worldY)
        {
            const double RampThicknessInVoxels = 1.25;
            double belowTop = surfaceY - worldY;
            double aboveBottom =
                worldY - (surfaceY - RampThicknessInVoxels);
            return Math.Min(belowTop, aboveBottom);
        }

        private static bool TryGetOpeningInDirection(
            Piece piece,
            int direction,
            out Opening result)
        {
            for (int i = 0; i < piece.Openings.Count; i++)
            {
                if (piece.Openings[i].Direction == direction)
                {
                    result = piece.Openings[i];
                    return true;
                }
            }
            result = default;
            return false;
        }

        private static bool HasOpeningInDirection(Piece piece, int direction)
        {
            return TryGetOpeningInDirection(piece, direction, out _);
        }

        private static bool EvaluateMasonryPassage(
            Piece piece,
            JigsawPieceSettings module,
            int worldX,
            int worldY,
            int worldZ,
            VoxelPass pass)
        {
            GetLocalCoordinates(
                piece,
                worldX,
                worldZ,
                out int along,
                out int lateral);
            int halfWidth = module.Width / 2;
            if ((uint)along >= piece.Length || Math.Abs(lateral) > halfWidth)
            {
                return false;
            }
            int floorY = GetFloorY(piece, along);
            if (pass == VoxelPass.Shell)
            {
                return worldY == floorY
                    || worldY == floorY + module.Height
                    || (Math.Abs(lateral) == halfWidth
                        && worldY >= floorY
                        && worldY <= floorY + module.Height);
            }
            if (pass == VoxelPass.Air)
            {
                bool interior = Math.Abs(lateral) < halfWidth
                    && worldY > floorY
                    && worldY < floorY + module.Height;
                return interior || IsPieceOpening(
                    piece,
                    worldX,
                    worldY,
                    worldZ,
                    0);
            }
            return false;
        }

        private static bool IsPieceOpening(
            Piece piece,
            int worldX,
            int worldY,
            int worldZ,
            int inwardOffset)
        {
            for (int i = 0; i < piece.Openings.Count; i++)
            {
                Opening opening = piece.Openings[i];
                if (IsVerticalDirection(opening.Direction))
                {
                    Vector3Int vertical = DirectionVector(opening.Direction);
                    Vector3Int verticalPlane = opening.Boundary
                        - vertical * inwardOffset;
                    if (worldY != verticalPlane.y)
                    {
                        continue;
                    }
                    Vector3Int pieceForward = DirectionVector(piece.Direction);
                    Vector3Int pieceRight = DirectionVector(
                        (piece.Direction + 1) & 3);
                    int verticalDeltaX = worldX - verticalPlane.x;
                    int verticalDeltaZ = worldZ - verticalPlane.z;
                    int verticalAlong = verticalDeltaX * pieceForward.x
                        + verticalDeltaZ * pieceForward.z;
                    int verticalLateral = verticalDeltaX * pieceRight.x
                        + verticalDeltaZ * pieceRight.z;
                    if (IsWithinCenteredSpan(
                            verticalLateral,
                            opening.Width)
                        && IsWithinCenteredSpan(
                            verticalAlong,
                            opening.Height))
                    {
                        return true;
                    }
                    continue;
                }
                if (worldY < opening.Boundary.y
                    || worldY >= opening.Boundary.y + opening.Height)
                {
                    continue;
                }
                Vector3Int forward = DirectionVector(opening.Direction);
                Vector3Int right = DirectionVector((opening.Direction + 1) & 3);
                Vector3Int plane = opening.Boundary - forward * inwardOffset;
                int deltaX = worldX - plane.x;
                int deltaZ = worldZ - plane.z;
                int normal = deltaX * forward.x + deltaZ * forward.z;
                int lateral = deltaX * right.x + deltaZ * right.z;
                if (normal == 0 && Math.Abs(lateral) <= opening.Width / 2)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsWithinCenteredSpan(int offset, int size)
        {
            int minimum = -(size / 2);
            return offset >= minimum && offset < minimum + size;
        }

        private static int GetFloorY(Piece piece, int along)
        {
            return piece.Shape == JigsawPieceDefinition.Shape.Stairs
                ? InterpolateFloor(
                    piece.StartFloorY,
                    piece.EndFloorY,
                    along,
                    piece.Length)
                : piece.StartFloorY;
        }

        private static int InterpolateFloor(
            int startY,
            int endY,
            int along,
            int length)
        {
            if (length <= 1 || startY == endY)
            {
                return startY;
            }
            double t = along / (double)(length - 1);
            return startY + (int)Math.Round(
                (endY - startY) * t,
                MidpointRounding.AwayFromZero);
        }

        private static void GetLocalCoordinates(
            Piece piece,
            int worldX,
            int worldZ,
            out int along,
            out int lateral)
        {
            int deltaX = worldX - piece.Origin.x;
            int deltaZ = worldZ - piece.Origin.z;
            Vector3Int forward = DirectionVector(piece.Direction);
            Vector3Int right = DirectionVector((piece.Direction + 1) & 3);
            along = deltaX * forward.x + deltaZ * forward.z;
            lateral = deltaX * right.x + deltaZ * right.z;
        }

        private static IntBounds BoundsAroundLine(
            Vector3Int start,
            Vector3Int end,
            Vector3Int right,
            int halfWidth,
            int minY,
            int maxY)
        {
            int x1 = start.x + right.x * halfWidth;
            int z1 = start.z + right.z * halfWidth;
            int x2 = start.x - right.x * halfWidth;
            int z2 = start.z - right.z * halfWidth;
            int x3 = end.x + right.x * halfWidth;
            int z3 = end.z + right.z * halfWidth;
            int x4 = end.x - right.x * halfWidth;
            int z4 = end.z - right.z * halfWidth;
            return new IntBounds(
                Math.Min(Math.Min(x1, x2), Math.Min(x3, x4)),
                minY,
                Math.Min(Math.Min(z1, z2), Math.Min(z3, z4)),
                Math.Max(Math.Max(x1, x2), Math.Max(x3, x4)),
                maxY,
                Math.Max(Math.Max(z1, z2), Math.Max(z3, z4)));
        }

        private static int NextOddInRange(
            int minimum,
            int maximum,
            ref DeterministicRandom random)
        {
            int first = (minimum & 1) == 0 ? minimum + 1 : minimum;
            int last = (maximum & 1) == 0 ? maximum - 1 : maximum;
            if (last <= first)
            {
                return first;
            }
            int count = (last - first) / 2 + 1;
            return first + random.NextInt(count) * 2;
        }

        private static int NextInRange(
            int minimum,
            int maximum,
            ref DeterministicRandom random)
        {
            return minimum + random.NextInt(maximum - minimum + 1);
        }

        private static Vector3Int DirectionVector(int direction)
        {
            if (direction == UpDirection)
            {
                return Vector3Int.up;
            }
            if (direction == DownDirection)
            {
                return Vector3Int.down;
            }
            switch (direction & 3)
            {
                case 1: return Vector3Int.right;
                case 2: return new Vector3Int(0, 0, -1);
                case 3: return Vector3Int.left;
                default: return new Vector3Int(0, 0, 1);
            }
        }

        private static int NormalizeConnectionDirection(int direction)
        {
            return IsVerticalDirection(direction) ? direction : direction & 3;
        }

        private static bool IsVerticalDirection(int direction)
        {
            return direction == UpDirection || direction == DownDirection;
        }

        private static int OppositeDirection(int direction)
        {
            if (direction == UpDirection)
            {
                return DownDirection;
            }
            if (direction == DownDirection)
            {
                return UpDirection;
            }
            return (direction + 2) & 3;
        }

        private static int GetWorldDirection(
            int pieceDirection,
            JigsawConnectorDefinition.Face face)
        {
            int direction = (int)face;
            return IsVerticalDirection(direction)
                ? direction
                : (pieceDirection + direction) & 3;
        }

        private static bool CanOrientInput(
            int outputDirection,
            JigsawConnectorDefinition.Face inputFace)
        {
            int inputDirection = (int)inputFace;
            if (IsVerticalDirection(outputDirection)
                || IsVerticalDirection(inputDirection))
            {
                return OppositeDirection(outputDirection) == inputDirection;
            }
            return true;
        }

        private static bool IsXAxis(int direction)
        {
            return (direction & 1) != 0;
        }

        private static bool IsBoxShape(JigsawPieceDefinition.Shape shape)
        {
            return shape == JigsawPieceDefinition.Shape.Room
                || shape == JigsawPieceDefinition.Shape.Crossing
                || shape == JigsawPieceDefinition.Shape.VerticalShaft;
        }

        private readonly struct LayoutCacheKey : IEquatable<LayoutCacheKey>
        {
            public LayoutCacheKey(
                ulong featureHash,
                int worldSeed,
                int regionX,
                int regionZ,
                Vector3Int centre)
            {
                FeatureHash = featureHash;
                WorldSeed = worldSeed;
                RegionX = regionX;
                RegionZ = regionZ;
                Centre = centre;
            }

            private ulong FeatureHash { get; }
            private int WorldSeed { get; }
            private int RegionX { get; }
            private int RegionZ { get; }
            private Vector3Int Centre { get; }

            public bool Equals(LayoutCacheKey other)
            {
                return FeatureHash == other.FeatureHash
                    && WorldSeed == other.WorldSeed
                    && RegionX == other.RegionX
                    && RegionZ == other.RegionZ
                    && Centre == other.Centre;
            }

            public override bool Equals(object obj)
            {
                return obj is LayoutCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)FeatureHash ^ (int)(FeatureHash >> 32);
                    hash = hash * 397 ^ WorldSeed;
                    hash = hash * 397 ^ RegionX;
                    hash = hash * 397 ^ RegionZ;
                    return hash * 397 ^ Centre.GetHashCode();
                }
            }
        }

        private sealed class LayoutCacheEntry
        {
            private readonly Dictionary<long, Piece[]> piecesByColumn;

            public LayoutCacheEntry(Piece[] pieces)
            {
                Pieces = pieces ?? Array.Empty<Piece>();
                var builders = new Dictionary<long, List<Piece>>();
                for (int i = 0; i < Pieces.Length; i++)
                {
                    Piece piece = Pieces[i];
                    int minColumnX = FloorDiv(
                        piece.Bounds.MinX,
                        VoxelColumnChunkData.Width);
                    int maxColumnX = FloorDiv(
                        piece.Bounds.MaxX,
                        VoxelColumnChunkData.Width);
                    int minColumnZ = FloorDiv(
                        piece.Bounds.MinZ,
                        VoxelColumnChunkData.Depth);
                    int maxColumnZ = FloorDiv(
                        piece.Bounds.MaxZ,
                        VoxelColumnChunkData.Depth);
                    for (int columnZ = minColumnZ;
                        columnZ <= maxColumnZ;
                        columnZ++)
                    {
                        for (int columnX = minColumnX;
                            columnX <= maxColumnX;
                            columnX++)
                        {
                            long key = CoordinateKey(columnX, columnZ);
                            if (!builders.TryGetValue(key, out List<Piece> list))
                            {
                                list = new List<Piece>();
                                builders.Add(key, list);
                            }
                            list.Add(piece);
                        }
                    }
                }
                piecesByColumn = new Dictionary<long, Piece[]>(builders.Count);
                foreach (KeyValuePair<long, List<Piece>> pair in builders)
                {
                    piecesByColumn.Add(pair.Key, pair.Value.ToArray());
                }
            }

            public Piece[] Pieces { get; }

            public IReadOnlyList<Piece> GetPiecesForColumn(int x, int z)
            {
                return piecesByColumn.TryGetValue(
                    CoordinateKey(x, z),
                    out Piece[] pieces)
                    ? pieces
                    : Array.Empty<Piece>();
            }
        }

        private sealed class PieceSpatialIndex
        {
            private readonly Dictionary<long, List<int>> cells =
                new Dictionary<long, List<int>>();

            public void Add(int pieceIndex, IntBounds bounds)
            {
                VisitCells(bounds, (x, z) =>
                {
                    long key = CoordinateKey(x, z);
                    if (!cells.TryGetValue(key, out List<int> list))
                    {
                        list = new List<int>();
                        cells.Add(key, list);
                    }
                    list.Add(pieceIndex);
                });
            }

            public IEnumerable<int> Query(IntBounds bounds)
            {
                var result = new HashSet<int>();
                VisitCells(bounds, (x, z) =>
                {
                    if (cells.TryGetValue(
                        CoordinateKey(x, z),
                        out List<int> list))
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            result.Add(list[i]);
                        }
                    }
                });
                return result;
            }

            private static void VisitCells(IntBounds bounds, Action<int, int> visit)
            {
                int minX = FloorDiv(bounds.MinX, CollisionCellSize);
                int maxX = FloorDiv(bounds.MaxX, CollisionCellSize);
                int minZ = FloorDiv(bounds.MinZ, CollisionCellSize);
                int maxZ = FloorDiv(bounds.MaxZ, CollisionCellSize);
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        visit(x, z);
                    }
                }
            }
        }

        private static long CoordinateKey(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }

        private static void ValidateColumnData(
            float[] densities,
            VoxelTypeId[] types)
        {
            if (densities == null) throw new ArgumentNullException(nameof(densities));
            if (types == null) throw new ArgumentNullException(nameof(types));
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
