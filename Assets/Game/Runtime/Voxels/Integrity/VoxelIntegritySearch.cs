using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels.Integrity
{
    public enum VoxelIntegrityCell
    {
        Unloaded,
        Air,
        Solid,
        StructuralSupport,
    }

    public enum VoxelIntegritySupport
    {
        None,
        StructuralSupport,
        UnloadedBoundary,
        SearchLimit,
    }

    /// <summary>
    /// Read-only voxel view consumed by the isolated integrity experiment.
    /// Implementations must distinguish loaded air from an unloaded coordinate.
    /// </summary>
    public interface IVoxelIntegrityMap
    {
        VoxelIntegrityCell GetCell(Vector3Int coordinate);

        /// <summary>
        /// Admissible estimate to the nearest of the six loaded-volume faces.
        /// The selected face is recalculated for every expanded coordinate.
        /// </summary>
        int EstimateDistanceToUnloadedBoundary(Vector3Int coordinate);
    }

    public sealed class VoxelIntegrityComponent
    {
        internal VoxelIntegrityComponent(
            List<Vector3Int> voxels,
            VoxelIntegritySupport support,
            Vector3Int supportCoordinate)
        {
            Voxels = voxels ?? throw new ArgumentNullException(nameof(voxels));
            Support = support;
            SupportCoordinate = supportCoordinate;
        }

        public IReadOnlyList<Vector3Int> Voxels { get; }
        public VoxelIntegritySupport Support { get; }
        public Vector3Int SupportCoordinate { get; }
        public bool IsSupported => Support != VoxelIntegritySupport.None;
    }

    public sealed class VoxelIntegrityResult
    {
        internal VoxelIntegrityResult(
            List<VoxelIntegrityComponent> components,
            int seedCount,
            int visitedVoxelCount)
        {
            Components = components
                ?? throw new ArgumentNullException(nameof(components));
            SeedCount = seedCount;
            VisitedVoxelCount = visitedVoxelCount;
        }

        public IReadOnlyList<VoxelIntegrityComponent> Components { get; }
        public int SeedCount { get; }
        public int FillCount => Components.Count;
        public int VisitedVoxelCount { get; }
    }

    /// <summary>
    /// Performs one six-connected fill per affected component after a destructive
    /// voxel update. Air is never enqueued. The open set uses A* cost (g + h),
    /// where h dynamically selects the nearest of the six loaded-volume faces.
    /// This keeps the fill complete for isolated components while reaching a
    /// conservative support boundary early for large supported components.
    /// </summary>
    public sealed class VoxelIntegritySearch
    {
        private static readonly Vector3Int[] SixNeighbours =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1),
        };

        private readonly int maxVisitedVoxels;
        private readonly BoundaryOpenSet openSet = new BoundaryOpenSet();
        private readonly HashSet<Vector3Int> visited =
            new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> removedSet =
            new HashSet<Vector3Int>();
        private readonly HashSet<Vector3Int> globallyVisited =
            new HashSet<Vector3Int>();
        private readonly Dictionary<Vector3Int, CachedSupport> cachedSupport =
            new Dictionary<Vector3Int, CachedSupport>();

        public VoxelIntegritySearch(int maxVisitedVoxels = 16384)
        {
            this.maxVisitedVoxels = Mathf.Max(64, maxVisitedVoxels);
        }

        public VoxelIntegrityResult Analyze(
            IReadOnlyCollection<Vector3Int> removedVoxels,
            IVoxelIntegrityMap map)
        {
            if (removedVoxels == null)
                throw new ArgumentNullException(nameof(removedVoxels));
            if (map == null)
                throw new ArgumentNullException(nameof(map));

            removedSet.Clear();
            globallyVisited.Clear();
            cachedSupport.Clear();
            foreach (Vector3Int removed in removedVoxels)
                removedSet.Add(removed);

            var components = new List<VoxelIntegrityComponent>();
            if (removedSet.Count == 0)
                return new VoxelIntegrityResult(components, 0, 0);

            var seeds = new HashSet<Vector3Int>();
            foreach (Vector3Int removed in removedSet)
            {
                for (int i = 0; i < SixNeighbours.Length; i++)
                {
                    Vector3Int candidate = removed + SixNeighbours[i];
                    if (removedSet.Contains(candidate))
                        continue;

                    VoxelIntegrityCell cell = map.GetCell(candidate);
                    if (cell == VoxelIntegrityCell.Solid
                        || cell == VoxelIntegrityCell.StructuralSupport)
                    {
                        seeds.Add(candidate);
                    }
                }
            }

            foreach (Vector3Int seed in seeds)
            {
                if (globallyVisited.Contains(seed))
                    continue;

                VoxelIntegrityComponent component = FillComponent(seed, map);
                components.Add(component);
                CacheVisitedSupport(
                    component.Support,
                    component.SupportCoordinate);
            }

            return new VoxelIntegrityResult(
                components,
                seeds.Count,
                globallyVisited.Count);
        }

        private VoxelIntegrityComponent FillComponent(
            Vector3Int seed,
            IVoxelIntegrityMap map)
        {
            openSet.Clear();
            visited.Clear();
            var component = new List<Vector3Int>();

            Enqueue(seed, 0, map);
            while (openSet.Count > 0)
            {
                SearchNode node = openSet.Pop();
                Vector3Int current = node.Coordinate;
                VoxelIntegrityCell currentCell = map.GetCell(current);
                if (currentCell == VoxelIntegrityCell.StructuralSupport)
                {
                    return new VoxelIntegrityComponent(
                        component,
                        VoxelIntegritySupport.StructuralSupport,
                        current);
                }

                component.Add(current);
                if (component.Count >= maxVisitedVoxels)
                {
                    return new VoxelIntegrityComponent(
                        component,
                        VoxelIntegritySupport.SearchLimit,
                        current);
                }

                int nextCost = node.PathCost + 1;
                for (int i = 0; i < SixNeighbours.Length; i++)
                {
                    Vector3Int neighbour = current + SixNeighbours[i];
                    if (removedSet.Contains(neighbour))
                        continue;

                    if (cachedSupport.TryGetValue(
                            neighbour,
                            out CachedSupport support))
                    {
                        if (support.Support != VoxelIntegritySupport.None)
                        {
                            return new VoxelIntegrityComponent(
                                component,
                                support.Support,
                                support.SupportCoordinate);
                        }

                        continue;
                    }

                    VoxelIntegrityCell cell = map.GetCell(neighbour);
                    if (cell == VoxelIntegrityCell.Unloaded)
                    {
                        return new VoxelIntegrityComponent(
                            component,
                            VoxelIntegritySupport.UnloadedBoundary,
                            neighbour);
                    }
                    if (cell == VoxelIntegrityCell.Air)
                        continue;
                    if (cell == VoxelIntegrityCell.StructuralSupport)
                    {
                        return new VoxelIntegrityComponent(
                            component,
                            VoxelIntegritySupport.StructuralSupport,
                            neighbour);
                    }

                    Enqueue(neighbour, nextCost, map);
                }
            }

            return new VoxelIntegrityComponent(
                component,
                VoxelIntegritySupport.None,
                default);
        }

        private void CacheVisitedSupport(
            VoxelIntegritySupport support,
            Vector3Int supportCoordinate)
        {
            var cached = new CachedSupport(support, supportCoordinate);
            foreach (Vector3Int coordinate in visited)
            {
                globallyVisited.Add(coordinate);
                cachedSupport[coordinate] = cached;
            }
        }


        private void Enqueue(
            Vector3Int coordinate,
            int pathCost,
            IVoxelIntegrityMap map)
        {
            if (!visited.Add(coordinate))
                return;

            int heuristic = Mathf.Max(
                0,
                map.EstimateDistanceToUnloadedBoundary(coordinate));
            openSet.Push(new SearchNode(
                coordinate,
                pathCost,
                pathCost + heuristic,
                heuristic));
        }

        private readonly struct SearchNode
        {
            public SearchNode(
                Vector3Int coordinate,
                int pathCost,
                int estimatedTotalCost,
                int boundaryDistance)
            {
                Coordinate = coordinate;
                PathCost = pathCost;
                EstimatedTotalCost = estimatedTotalCost;
                BoundaryDistance = boundaryDistance;
            }

            public Vector3Int Coordinate { get; }
            public int PathCost { get; }
            public int EstimatedTotalCost { get; }
            public int BoundaryDistance { get; }
        }

        private readonly struct CachedSupport
        {
            public CachedSupport(
                VoxelIntegritySupport support,
                Vector3Int supportCoordinate)
            {
                Support = support;
                SupportCoordinate = supportCoordinate;
            }

            public VoxelIntegritySupport Support { get; }
            public Vector3Int SupportCoordinate { get; }
        }


        private sealed class BoundaryOpenSet
        {
            private readonly List<SearchNode> heap = new List<SearchNode>();

            public int Count => heap.Count;

            public void Clear()
            {
                heap.Clear();
            }

            public void Push(SearchNode node)
            {
                heap.Add(node);
                int index = heap.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!ComesBefore(node, heap[parent]))
                        break;

                    heap[index] = heap[parent];
                    index = parent;
                }
                heap[index] = node;
            }

            public SearchNode Pop()
            {
                SearchNode result = heap[0];
                int lastIndex = heap.Count - 1;
                SearchNode replacement = heap[lastIndex];
                heap.RemoveAt(lastIndex);
                if (heap.Count == 0)
                    return result;

                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    if (left >= heap.Count)
                        break;
                    int right = left + 1;
                    int child = right < heap.Count
                        && ComesBefore(heap[right], heap[left])
                            ? right
                            : left;
                    if (!ComesBefore(heap[child], replacement))
                        break;

                    heap[index] = heap[child];
                    index = child;
                }
                heap[index] = replacement;
                return result;
            }

            private static bool ComesBefore(SearchNode a, SearchNode b)
            {
                if (a.EstimatedTotalCost != b.EstimatedTotalCost)
                    return a.EstimatedTotalCost < b.EstimatedTotalCost;
                if (a.BoundaryDistance != b.BoundaryDistance)
                    return a.BoundaryDistance < b.BoundaryDistance;
                if (a.PathCost != b.PathCost)
                    return a.PathCost < b.PathCost;
                if (a.Coordinate.x != b.Coordinate.x)
                    return a.Coordinate.x < b.Coordinate.x;
                if (a.Coordinate.y != b.Coordinate.y)
                    return a.Coordinate.y < b.Coordinate.y;
                return a.Coordinate.z < b.Coordinate.z;
            }
        }
    }
}
