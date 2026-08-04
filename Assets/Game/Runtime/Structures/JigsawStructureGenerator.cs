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
                Direction = direction & 3;
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
                Direction = direction & 3;
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
        }

        public static int GenerateColumn(
            Vector3Int columnCoordinate,
            float[] densities,
            VoxelTypeId[] types,
            int worldSeed,
            IReadOnlyList<JigsawStructureFeatureSettings> features,
            float solidDensity,
            float airDensity,
            CancellationToken cancellationToken = default)
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

            for (int featureIndex = 0; featureIndex < features.Count; featureIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JigsawStructureFeatureSettings feature = features[featureIndex];
                if (feature.PlacementChance <= 0f)
                {
                    continue;
                }

                int regionSize = feature.RegionSizeInChunks
                    * VoxelColumnChunkData.Width;
                int influence = feature.MaxHorizontalDistance;
                int minRegionX = FloorDiv(targetMinX - influence, regionSize);
                int maxRegionX = FloorDiv(targetMaxX + influence, regionSize);
                int minRegionZ = FloorDiv(targetMinZ - influence, regionSize);
                int maxRegionZ = FloorDiv(targetMaxZ + influence, regionSize);

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
            }

            return changed;
        }

        public static bool TryGetPlacement(
            JigsawStructureFeatureSettings feature,
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
            int margin = feature.MaxHorizontalDistance;
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
                + random.NextInt(
                    feature.MaxFloorHeight - feature.MinFloorHeight + 1);
            placement = new Placement(
                regionX,
                regionZ,
                new Vector3Int(centreX, floorY, centreZ));
            return true;
        }

        public static IReadOnlyList<Piece> BuildLayout(
            JigsawStructureFeatureSettings feature,
            int worldSeed,
            Placement placement)
        {
            return GetOrCreateLayout(feature, worldSeed, placement).Pieces;
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

            JigsawPieceSettings startModule = feature.GetPiece(
                feature.StartPieceIndex);
            int firstDirection = random.NextInt(4);
            Piece start = CreateStartPiece(
                feature.StartPieceIndex,
                startModule,
                placement.Centre,
                firstDirection,
                ref random);
            pieces.Add(start);
            counts[feature.StartPieceIndex]++;
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
                    if (!TryCreateCandidate(
                        moduleIndex,
                        module,
                        connector,
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

                    int pieceIndex = pieces.Count;
                    pieces.Add(candidate);
                    counts[moduleIndex]++;
                    spatialIndex.Add(pieceIndex, candidate.Bounds);
                    var openings = new List<Opening> { entranceOpening };
                    int connectorMask = EnqueueConnectors(
                        candidate,
                        module,
                        pieceIndex,
                        connectors,
                        openings,
                        ref random,
                        false,
                        usedInputConnector);
                    connectorMask |= 1 << entranceOpening.Direction;
                    pieces[pieceIndex] = candidate.WithConnections(
                        connectorMask,
                        openings.ToArray());
                    added = true;
                }
            }

            return pieces;
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
                return true;
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
                direction = ((connector.Direction + 2) - (int)input.Face) & 3;
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
            int entranceDirection = (connector.Direction + 2) & 3;
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
                && NamesMatch(output.TargetName, input.SocketName)
                && NamesMatch(input.TargetName, output.SocketName);
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
            int worldDirection = (piece.Direction + (int)connector.Face) & 3;
            bool box = module.HasTemplate
                || piece.Shape == JigsawPieceDefinition.Shape.Room
                || piece.Shape == JigsawPieceDefinition.Shape.Crossing;
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
                || candidate.Bounds.MaxY >= VoxelColumnChunkData.Height - 1)
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
                    int direction = (piece.Direction + (int)authored.Face) & 3;
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
                    openings.Add(new Opening(
                        boundary,
                        direction,
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
            bool box = piece.Shape == JigsawPieceDefinition.Shape.Room
                || piece.Shape == JigsawPieceDefinition.Shape.Crossing;
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
            openings.Add(new Opening(
                position - DirectionVector(direction) + Vector3Int.up,
                direction,
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
            for (int worldZ = minZ; worldZ <= maxZ; worldZ++)
            {
                int localZ = worldZ - targetMinZ;
                for (int worldX = minX; worldX <= maxX; worldX++)
                {
                    int localX = worldX - targetMinX;
                    for (int worldY = piece.Bounds.MinY;
                        worldY <= piece.Bounds.MaxY;
                        worldY++)
                    {
                        bool conditionalFloor = false;
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
                                    ? feature.AccentType
                                    : feature.PrimaryType;
                        }

                        int index = VoxelColumnChunkData.ToIndex(
                            localX,
                            worldY,
                            localZ);
                        if (conditionalFloor && densities[index] >= isoDensity)
                        {
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
                feature.PrimaryType,
                feature.AccentType);

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
            for (int worldY = piece.Bounds.MinY;
                worldY <= piece.Bounds.MaxY;
                worldY++)
            {
                int index = VoxelColumnChunkData.ToIndex(localX, worldY, localZ);
                if (densities[index] < 0f || types[index] == writeType)
                {
                    continue;
                }
                bool ownedByStructure = types[index] == feature.PrimaryType
                    || types[index] == feature.AccentType;
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
            bool box = piece.Shape == JigsawPieceDefinition.Shape.Room
                || piece.Shape == JigsawPieceDefinition.Shape.Crossing;
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

                default:
                    return false;
            }
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
            switch (direction & 3)
            {
                case 1: return Vector3Int.right;
                case 2: return new Vector3Int(0, 0, -1);
                case 3: return Vector3Int.left;
                default: return new Vector3Int(0, 0, 1);
            }
        }

        private static bool IsXAxis(int direction)
        {
            return (direction & 1) != 0;
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
