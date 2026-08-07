using System;
using System.Collections.Generic;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures
{
    public interface ICreatureVoxelQuery
    {
        bool TryGetSolid(Vector3Int voxel, out bool isSolid);
    }

    public readonly struct CreatureTraversalLink
    {
        public CreatureTraversalLink(
            Vector3Int toSupport,
            int movementCost,
            float horizontalDistanceInVoxels,
            int verticalDeltaInVoxels)
        {
            ToSupport = toSupport;
            MovementCost = Mathf.Max(1, movementCost);
            HorizontalDistanceInVoxels =
                Mathf.Max(0f, horizontalDistanceInVoxels);
            VerticalDeltaInVoxels = verticalDeltaInVoxels;
        }

        public Vector3Int ToSupport { get; }
        public int MovementCost { get; }
        public float HorizontalDistanceInVoxels { get; }
        public int VerticalDeltaInVoxels { get; }
    }

    public interface ICreatureTraversalLinkQuery
    {
        int NavigationRevision { get; }

        void GetTraversalLinks(
            Vector3Int fromSupport,
            List<CreatureTraversalLink> results);

        bool TryGetTraversalLink(
            Vector3Int fromSupport,
            Vector3Int toSupport,
            out CreatureTraversalLink link);
    }

    public sealed class MinecraftCaveVoxelQuery :
        ICreatureVoxelQuery,
        ICreatureTraversalLinkQuery
    {
        private readonly IVoxelTerrain terrain;
        private readonly MinecraftCaveInfiniteWorld caveWorld;
        private readonly float solidDensityThreshold;

        public MinecraftCaveVoxelQuery(
            IVoxelTerrain terrain,
            float solidDensityThreshold = 0f)
        {
            this.terrain = terrain
                ?? throw new ArgumentNullException(nameof(terrain));
            caveWorld = terrain as MinecraftCaveInfiniteWorld;
            this.solidDensityThreshold = solidDensityThreshold;
        }

        public int NavigationRevision => caveWorld != null
            ? DynamicCreatureNavigation.GetRevision(caveWorld)
            : 0;

        public bool TryGetSolid(Vector3Int voxel, out bool isSolid)
        {
            if (caveWorld != null
                && DynamicCreatureNavigation.ContainsSupport(
                    caveWorld,
                    voxel))
            {
                isSolid = true;
                return true;
            }

            InfiniteVoxelWorld world = terrain.World;
            if (world == null
                || !world.TryGetDensity(
                    voxel.x,
                    voxel.y,
                    voxel.z,
                    out float density))
            {
                isSolid = false;
                return false;
            }

            isSolid = density >= solidDensityThreshold;
            return true;
        }

        public void GetTraversalLinks(
            Vector3Int fromSupport,
            List<CreatureTraversalLink> results)
        {
            results.Clear();
            if (caveWorld != null)
            {
                DynamicCreatureNavigation.GetTraversalLinks(
                    caveWorld,
                    fromSupport,
                    results);
            }
        }

        public bool TryGetTraversalLink(
            Vector3Int fromSupport,
            Vector3Int toSupport,
            out CreatureTraversalLink link)
        {
            if (caveWorld == null)
            {
                link = default;
                return false;
            }
            return DynamicCreatureNavigation.TryGetTraversalLink(
                caveWorld,
                fromSupport,
                toSupport,
                out link);
        }
    }

    [Serializable]
    public sealed class CreatureNavigationSettings
    {
        [Min(0)] public int safeFallHeight = 3;
        [Min(0)] public int maximumJumpHeight = 1;
        [Min(1)] public int maximumTraversalJumpHeight = 4;
        [Min(1)] public int maximumTraversalHorizontalDistance = 3;
        [Min(1)] public int maximumSingleMoveCost = 100;
        [Range(1, CreatureVoxelNavigation.MaximumExpandedNodeLimit)]
        public int maximumExpandedNodes =
            CreatureVoxelNavigation.MaximumExpandedNodeLimit;
        [Range(2, 32)] public int maximumSmoothingLookahead = 16;
    }

    public static class CreatureVoxelNavigation
    {
        public const int MaximumExpandedNodeLimit = 4096;
        public const int MaximumPursuitPathNodeCount = 16;

        private static readonly Vector3Int[] HorizontalDirections =
        {
            new Vector3Int(-1, 0, -1),
            new Vector3Int(0, 0, -1),
            new Vector3Int(1, 0, -1),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 1),
            new Vector3Int(0, 0, 1),
            new Vector3Int(1, 0, 1),
        };

        [ThreadStatic] private static PathSearchWorkspace reusableWorkspace;
        [ThreadStatic] private static List<CreatureTraversalLink>
            reusableTraversalLinks;

        public static bool IsStandable(
            ICreatureVoxelQuery query,
            CreatureVoxelShape shape,
            Vector3Int support)
        {
            return IsStandable(
                query,
                shape,
                support,
                true);
        }

        private static bool IsStandable(
            ICreatureVoxelQuery query,
            CreatureVoxelShape shape,
            Vector3Int support,
            bool allowLowestBodyLayerOnFloorSurface)
        {
            if (query == null || shape == null || shape.IsEmpty)
            {
                return false;
            }

            if (!query.TryGetSolid(support, out bool supportSolid) || !supportSolid)
            {
                return false;
            }

            Vector3Int foot = support + Vector3Int.up;
            bool footClear = allowLowestBodyLayerOnFloorSurface
                ? IsKnownAirOrFloorSurface(query, foot)
                : IsKnownAir(query, foot);
            if (!footClear)
            {
                return false;
            }

            IReadOnlyList<Vector3Int> occupied = shape.OccupiedVoxels;
            int lowestBodyLayer = shape.OccupiedBounds.min.y;
            for (int i = 0; i < occupied.Count; i++)
            {
                Vector3Int offset = occupied[i];
                bool clear = allowLowestBodyLayerOnFloorSurface
                    && offset.y == lowestBodyLayer
                    ? IsKnownAirOrFloorSurface(query, foot + offset)
                    : IsKnownAir(query, foot + offset);
                if (!clear)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool TryResolveTransition(
            ICreatureVoxelQuery query,
            CreatureVoxelShape shape,
            CreatureNavigationSettings settings,
            Vector3Int fromSupport,
            Vector3Int horizontalDirection,
            out Vector3Int destinationSupport,
            out int movementCost)
        {
            destinationSupport = default;
            movementCost = settings.maximumSingleMoveCost + 1;
            Vector3Int requestedDestination =
                fromSupport + horizontalDirection;
            if (query is ICreatureTraversalLinkQuery traversalQuery
                && traversalQuery.TryGetTraversalLink(
                    fromSupport,
                    requestedDestination,
                    out CreatureTraversalLink link)
                && IsTraversalLinkAllowed(link, settings)
                && IsStandable(
                    query,
                    shape,
                    requestedDestination,
                    false))
            {
                destinationSupport = requestedDestination;
                movementCost = link.MovementCost;
                return true;
            }

            int horizontalX = Math.Sign(horizontalDirection.x);
            int horizontalZ = Math.Sign(horizontalDirection.z);
            Vector3Int candidate = fromSupport + new Vector3Int(
                horizontalX,
                0,
                horizontalZ);

            if (candidate.x == fromSupport.x && candidate.z == fromSupport.z)
            {
                return false;
            }

            int horizontalMovementCost = Math.Abs(horizontalX)
                + Math.Abs(horizontalZ);

            if (!query.TryGetSolid(candidate, out bool candidateSolid))
            {
                return false;
            }

            if (candidateSolid)
            {
                for (int rise = 0; rise <= settings.maximumJumpHeight; rise++)
                {
                    Vector3Int raised = candidate + Vector3Int.up * rise;
                    if (IsStandable(query, shape, raised, false))
                    {
                        int cost = horizontalMovementCost + rise * 2;
                        if (cost <= settings.maximumSingleMoveCost)
                        {
                            destinationSupport = raised;
                            movementCost = cost;
                            return true;
                        }

                        return false;
                    }

                    if (rise < settings.maximumJumpHeight
                        && !query.TryGetSolid(raised + Vector3Int.up, out _))
                    {
                        return false;
                    }
                }

                return false;
            }

            int costSoFar = horizontalMovementCost;
            for (int drop = 1; costSoFar <= settings.maximumSingleMoveCost; drop++)
            {
                costSoFar += drop <= settings.safeFallHeight ? 1 : 10;
                if (costSoFar > settings.maximumSingleMoveCost)
                {
                    return false;
                }

                Vector3Int lowered = candidate + Vector3Int.down * drop;
                if (!query.TryGetSolid(lowered, out bool loweredSolid))
                {
                    return false;
                }

                if (!loweredSolid)
                {
                    continue;
                }

                if (IsStandable(query, shape, lowered, false))
                {
                    destinationSupport = lowered;
                    movementCost = costSoFar;
                    return true;
                }

                return false;
            }

            return false;
        }

        public static bool TryFindPath(
            ICreatureVoxelQuery query,
            CreatureVoxelShape shape,
            CreatureNavigationSettings settings,
            Vector3Int startSupport,
            Vector3Int targetSupport,
            List<Vector3Int> path,
            out int expandedNodeCount)
        {
            return TryFindPathInternal(
                query,
                shape,
                settings,
                startSupport,
                targetSupport,
                path,
                false,
                1,
                out expandedNodeCount,
                out _);
        }

        public static bool TryFindPursuitPath(
            ICreatureVoxelQuery query,
            CreatureVoxelShape shape,
            CreatureNavigationSettings settings,
            Vector3Int startSupport,
            Vector3Int targetSupport,
            List<Vector3Int> path,
            out int expandedNodeCount,
            out bool reachedTarget)
        {
            bool found = TryFindPathInternal(
                query,
                shape,
                settings,
                startSupport,
                targetSupport,
                path,
                true,
                2,
                out expandedNodeCount,
                out reachedTarget);
            if (!found || path.Count <= MaximumPursuitPathNodeCount)
            {
                return found;
            }

            path.RemoveRange(
                MaximumPursuitPathNodeCount,
                path.Count - MaximumPursuitPathNodeCount);
            reachedTarget = false;
            return true;
        }

        private static bool TryFindPathInternal(
            ICreatureVoxelQuery query,
            CreatureVoxelShape shape,
            CreatureNavigationSettings settings,
            Vector3Int startSupport,
            Vector3Int targetSupport,
            List<Vector3Int> path,
            bool allowPartialPath,
            int heuristicWeight,
            out int expandedNodeCount,
            out bool reachedTarget)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            path.Clear();
            expandedNodeCount = 0;
            reachedTarget = false;
            // The dynamic Rigidbody and the actual Marching Cubes collider own the
            // creature's present placement. Its current support may not satisfy the
            // discrete density approximation even though physics says it is grounded.
            if (!IsStandable(query, shape, targetSupport))
            {
                return false;
            }

            int expansionLimit = Mathf.Clamp(
                settings.maximumExpandedNodes,
                1,
                MaximumExpandedNodeLimit);
            reusableWorkspace ??= new PathSearchWorkspace();
            PathSearchWorkspace workspace = reusableWorkspace;
            workspace.Reset(expansionLimit);
            int startIndex = workspace.GetOrAddRecord(startSupport, out _);
            ref NodeRecord startRecord = ref workspace.GetRecord(startIndex);
            startRecord.G = 0;
            int clampedHeuristicWeight = Mathf.Max(1, heuristicWeight);
            int bestRecordIndex = startIndex;
            int bestHeuristic = Heuristic(startSupport, targetSupport);
            int bestCost = 0;
            workspace.Push(new OpenNode(
                startIndex,
                0,
                bestHeuristic * clampedHeuristicWeight));

            while (workspace.OpenCount > 0 && expandedNodeCount < expansionLimit)
            {
                OpenNode current = workspace.Pop();
                ref NodeRecord currentRecord =
                    ref workspace.GetRecord(current.RecordIndex);
                if (current.G != currentRecord.G)
                {
                    continue;
                }

                int currentHeuristic = Heuristic(
                    currentRecord.Position,
                    targetSupport);
                if (currentHeuristic < bestHeuristic
                    || (currentHeuristic == bestHeuristic
                        && current.G < bestCost))
                {
                    bestRecordIndex = current.RecordIndex;
                    bestHeuristic = currentHeuristic;
                    bestCost = current.G;
                }

                if (currentRecord.Position == targetSupport)
                {
                    reachedTarget = true;
                    ReconstructPath(workspace, current.RecordIndex, path);
                    return true;
                }

                expandedNodeCount++;
                Vector3Int currentPosition = currentRecord.Position;
                for (int i = 0; i < HorizontalDirections.Length; i++)
                {
                    if (!TryResolveTransition(
                            query,
                            shape,
                            settings,
                            currentPosition,
                            HorizontalDirections[i],
                            out Vector3Int neighbour,
                            out int stepCost))
                    {
                        continue;
                    }

                    QueueNeighbour(
                        workspace,
                        current.RecordIndex,
                        current.G,
                        neighbour,
                        stepCost,
                        targetSupport,
                        clampedHeuristicWeight);
                }

                if (query is ICreatureTraversalLinkQuery linkQuery)
                {
                    reusableTraversalLinks ??=
                        new List<CreatureTraversalLink>();
                    linkQuery.GetTraversalLinks(
                        currentPosition,
                        reusableTraversalLinks);
                    for (int i = 0; i < reusableTraversalLinks.Count; i++)
                    {
                        CreatureTraversalLink link =
                            reusableTraversalLinks[i];
                        if (!IsTraversalLinkAllowed(link, settings)
                            || !IsStandable(
                                query,
                                shape,
                                link.ToSupport))
                        {
                            continue;
                        }

                        QueueNeighbour(
                            workspace,
                            current.RecordIndex,
                            current.G,
                            link.ToSupport,
                            link.MovementCost,
                            targetSupport,
                            clampedHeuristicWeight);
                    }
                }
            }

            if (allowPartialPath && bestRecordIndex != startIndex)
            {
                ReconstructPath(workspace, bestRecordIndex, path);
                return path.Count > 1;
            }

            return false;
        }

        /// <summary>
        /// Removes intermediate flat-ground nodes when the complete horizontal
        /// segment is physically clear for the baked creature shape.
        /// </summary>
        public static void SimplifyPath(
            ICreatureVoxelQuery query,
            CreatureVoxelShape shape,
            CreatureNavigationSettings settings,
            List<Vector3Int> path)
        {
            if (query == null
                || shape == null
                || settings == null
                || path == null
                || path.Count < 3)
            {
                return;
            }

            int lookahead = Mathf.Clamp(
                settings.maximumSmoothingLookahead,
                2,
                32);
            int writeIndex = 1;
            int anchorIndex = 0;
            while (anchorIndex < path.Count - 1)
            {
                int furthest = Mathf.Min(
                    path.Count - 1,
                    anchorIndex + lookahead);
                int selected = anchorIndex + 1;
                for (int candidate = furthest;
                    candidate > anchorIndex + 1;
                    candidate--)
                {
                    if (CanTraverseDirectHorizontalSegment(
                        query,
                        shape,
                        settings,
                        path[anchorIndex],
                        path[candidate]))
                    {
                        selected = candidate;
                        break;
                    }
                }

                path[writeIndex++] = path[selected];
                anchorIndex = selected;
            }
            if (writeIndex < path.Count)
            {
                path.RemoveRange(writeIndex, path.Count - writeIndex);
            }
        }

        public static bool CanTraverseDirectHorizontalSegment(
            ICreatureVoxelQuery query,
            CreatureVoxelShape shape,
            CreatureNavigationSettings settings,
            Vector3Int fromSupport,
            Vector3Int toSupport)
        {
            if (query == null
                || shape == null
                || settings == null
                || fromSupport.y != toSupport.y)
            {
                return false;
            }

            int x = fromSupport.x;
            int z = fromSupport.z;
            int deltaX = Mathf.Abs(toSupport.x - x);
            int deltaZ = Mathf.Abs(toSupport.z - z);
            int stepX = Math.Sign(toSupport.x - x);
            int stepZ = Math.Sign(toSupport.z - z);
            int error = deltaX - deltaZ;
            Vector3Int current = fromSupport;
            while (x != toSupport.x || z != toSupport.z)
            {
                int doubledError = error * 2;
                int horizontalX = 0;
                int horizontalZ = 0;
                if (doubledError > -deltaZ)
                {
                    error -= deltaZ;
                    x += stepX;
                    horizontalX = stepX;
                }
                if (doubledError < deltaX)
                {
                    error += deltaX;
                    z += stepZ;
                    horizontalZ = stepZ;
                }

                if (horizontalX != 0 && horizontalZ != 0)
                {
                    if (!TryResolveTransition(
                            query,
                            shape,
                            settings,
                            current,
                            new Vector3Int(horizontalX, 0, 0),
                            out Vector3Int sideX,
                            out _)
                        || sideX != current
                            + new Vector3Int(horizontalX, 0, 0)
                        || !TryResolveTransition(
                            query,
                            shape,
                            settings,
                            current,
                            new Vector3Int(0, 0, horizontalZ),
                            out Vector3Int sideZ,
                            out _)
                        || sideZ != current
                            + new Vector3Int(0, 0, horizontalZ))
                    {
                        return false;
                    }
                }

                Vector3Int direction =
                    new Vector3Int(horizontalX, 0, horizontalZ);
                Vector3Int expected =
                    new Vector3Int(x, fromSupport.y, z);
                if (!TryResolveTransition(
                        query,
                        shape,
                        settings,
                        current,
                        direction,
                        out Vector3Int resolved,
                        out _)
                    || resolved != expected)
                {
                    return false;
                }
                current = resolved;
            }
            return current == toSupport;
        }

        public static bool IsTraversalLinkAllowed(
            CreatureTraversalLink link,
            CreatureNavigationSettings settings)
        {
            if (settings == null
                || link.MovementCost > settings.maximumSingleMoveCost
                || link.HorizontalDistanceInVoxels
                    > settings.maximumTraversalHorizontalDistance)
            {
                return false;
            }

            return link.VerticalDeltaInVoxels >= 0
                ? link.VerticalDeltaInVoxels
                    <= settings.maximumTraversalJumpHeight
                : -link.VerticalDeltaInVoxels <= settings.safeFallHeight;
        }

        private static bool IsKnownAir(ICreatureVoxelQuery query, Vector3Int voxel)
        {
            return query.TryGetSolid(voxel, out bool solid) && !solid;
        }

        private static bool IsKnownAirOrFloorSurface(
            ICreatureVoxelQuery query,
            Vector3Int voxel)
        {
            if (!query.TryGetSolid(voxel, out bool solid))
            {
                return false;
            }

            return !solid || IsKnownAir(query, voxel + Vector3Int.up);
        }


        private static int Heuristic(Vector3Int from, Vector3Int to)
        {
            Vector3Int delta = to - from;
            return Mathf.Abs(delta.x) + Mathf.Abs(delta.y) + Mathf.Abs(delta.z);
        }

        private static void QueueNeighbour(
            PathSearchWorkspace workspace,
            int currentIndex,
            int currentG,
            Vector3Int neighbour,
            int stepCost,
            Vector3Int targetSupport,
            int heuristicWeight)
        {
            int tentativeG = currentG + stepCost;
            int neighbourIndex = workspace.GetOrAddRecord(
                neighbour,
                out bool wasAdded);
            ref NodeRecord neighbourRecord =
                ref workspace.GetRecord(neighbourIndex);
            if (!wasAdded && tentativeG >= neighbourRecord.G)
            {
                return;
            }

            neighbourRecord.G = tentativeG;
            neighbourRecord.ParentIndex = currentIndex;
            int f = tentativeG
                + Heuristic(neighbour, targetSupport) * heuristicWeight;
            workspace.Push(new OpenNode(neighbourIndex, tentativeG, f));
        }

        private static void ReconstructPath(
            PathSearchWorkspace workspace,
            int currentIndex,
            List<Vector3Int> path)
        {
            while (currentIndex >= 0)
            {
                NodeRecord current = workspace.GetRecord(currentIndex);
                path.Add(current.Position);
                currentIndex = current.ParentIndex;
            }

            path.Reverse();
        }

        private struct NodeRecord
        {
            public Vector3Int Position;
            public int G;
            public int ParentIndex;
        }

        private readonly struct OpenNode
        {
            public OpenNode(int recordIndex, int g, int f)
            {
                RecordIndex = recordIndex;
                G = g;
                F = f;
            }

            public int RecordIndex { get; }
            public int G { get; }
            public int F { get; }
        }

        private sealed class PathSearchWorkspace
        {
            private NodeRecord[] records = Array.Empty<NodeRecord>();
            private int[] buckets = Array.Empty<int>();
            private int[] bucketGenerations = Array.Empty<int>();
            private OpenNode[] openHeap = Array.Empty<OpenNode>();
            private int recordCount;
            private int openCount;
            private int currentGeneration;

            public int OpenCount => openCount;

            public void Reset(int maximumExpandedNodes)
            {
                int maximumRecords = maximumExpandedNodes
                    * HorizontalDirections.Length * 2 + 1;
                EnsureCapacity(maximumRecords);
                if (currentGeneration == int.MaxValue)
                {
                    Array.Clear(bucketGenerations, 0, bucketGenerations.Length);
                    currentGeneration = 1;
                }
                else
                {
                    currentGeneration++;
                }

                recordCount = 0;
                openCount = 0;
            }

            public int GetOrAddRecord(Vector3Int position, out bool wasAdded)
            {
                int mask = buckets.Length - 1;
                int bucket = Hash(position) & mask;
                while (bucketGenerations[bucket] == currentGeneration)
                {
                    int existingIndex = buckets[bucket] - 1;
                    if (records[existingIndex].Position == position)
                    {
                        wasAdded = false;
                        return existingIndex;
                    }

                    bucket = (bucket + 1) & mask;
                }

                if (recordCount >= records.Length)
                {
                    throw new InvalidOperationException(
                        "Creature path search record capacity was exhausted.");
                }

                int recordIndex = recordCount++;
                records[recordIndex] = new NodeRecord
                {
                    Position = position,
                    G = int.MaxValue,
                    ParentIndex = -1,
                };
                buckets[bucket] = recordIndex + 1;
                bucketGenerations[bucket] = currentGeneration;
                wasAdded = true;
                return recordIndex;
            }

            public ref NodeRecord GetRecord(int index)
            {
                return ref records[index];
            }

            public void Push(OpenNode item)
            {
                if (openCount >= openHeap.Length)
                {
                    throw new InvalidOperationException(
                        "Creature path search open heap capacity was exhausted.");
                }

                int index = openCount++;
                openHeap[index] = item;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!ComesBefore(openHeap[index], openHeap[parent]))
                    {
                        break;
                    }

                    (openHeap[index], openHeap[parent]) =
                        (openHeap[parent], openHeap[index]);
                    index = parent;
                }
            }

            public OpenNode Pop()
            {
                OpenNode root = openHeap[0];
                int last = --openCount;
                openHeap[0] = openHeap[last];

                int index = 0;
                while (index < openCount)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    if (left >= openCount)
                    {
                        break;
                    }

                    int best = right < openCount
                        && ComesBefore(openHeap[right], openHeap[left])
                        ? right
                        : left;
                    if (!ComesBefore(openHeap[best], openHeap[index]))
                    {
                        break;
                    }

                    (openHeap[index], openHeap[best]) =
                        (openHeap[best], openHeap[index]);
                    index = best;
                }

                return root;
            }

            private void EnsureCapacity(int maximumRecords)
            {
                if (records.Length < maximumRecords)
                {
                    records = new NodeRecord[maximumRecords];
                    openHeap = new OpenNode[maximumRecords];
                }

                int requiredBuckets = 1;
                while (requiredBuckets < maximumRecords * 2)
                {
                    requiredBuckets <<= 1;
                }
                if (buckets.Length < requiredBuckets)
                {
                    buckets = new int[requiredBuckets];
                    bucketGenerations = new int[requiredBuckets];
                    currentGeneration = 0;
                }
            }

            private static int Hash(Vector3Int position)
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + position.x;
                    hash = hash * 31 + position.y;
                    hash = hash * 31 + position.z;
                    return hash ^ (int)((uint)hash >> 16);
                }
            }

            private static bool ComesBefore(OpenNode left, OpenNode right)
            {
                if (left.F != right.F)
                {
                    return left.F < right.F;
                }

                return left.G > right.G;
            }
        }
    }
}
