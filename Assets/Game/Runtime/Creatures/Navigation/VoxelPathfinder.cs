using System.Collections.Generic;
using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures.Navigation
{
    /// <summary>
    /// A* over the implicit voxel graph, mirroring Minecraft's PathNodeNavigator.
    /// <para>
    /// The search keeps the node whose straight line distance to the target is
    /// smallest, updated in constant time wherever that distance is computed. When
    /// the visit limit is reached the route to that node is returned as a partial
    /// path instead of failing outright, so a creature always makes progress
    /// toward an unreachable or distant goal.
    /// </para>
    /// All buffers are fields reused between searches so many creatures replanning
    /// each second do not churn the garbage collector.
    /// </summary>
    public sealed class VoxelPathfinder
    {
        private readonly VoxelPathNodeMaker nodeMaker;
        private readonly PathNodeMinHeap openSet = new PathNodeMinHeap();
        private readonly Dictionary<Vector3Int, int> nodeIndices =
            new Dictionary<Vector3Int, int>();
        private readonly List<Vector3Int> positions = new List<Vector3Int>();
        private readonly List<int> parents = new List<int>();
        private readonly List<float> costFromStart = new List<float>();
        private readonly List<float> heuristics = new List<float>();
        private readonly List<float> totalCost = new List<float>();
        private readonly List<bool> closed = new List<bool>();
        private readonly VoxelPath path = new VoxelPath();
        private readonly Vector3Int[] successors =
            new Vector3Int[VoxelPathNodeMaker.MaximumSuccessors];
        private readonly float[] stepCosts =
            new float[VoxelPathNodeMaker.MaximumSuccessors];

        private int nearestIndex;
        private float nearestHeuristic;
        private float nearestCostFromStart;

        public VoxelPathfinder(VoxelPathNodeMaker nodeMaker)
        {
            this.nodeMaker = nodeMaker;
        }

        /// <summary>Node expansions the last search consumed.</summary>
        public int LastVisitedNodeCount { get; private set; }

        /// <summary>
        /// Searches from a foot node to a target foot node. Returns null only when
        /// the start itself is unusable; otherwise a full or partial path.
        /// </summary>
        public VoxelPath Search(
            Vector3Int start,
            Vector3Int target,
            CreatureBodyBox body,
            CreatureNavigationProfile profile)
        {
            nodeMaker.BeginSearch(body, profile);
            ClearWorkspace();

            if (!nodeMaker.TryClassify(start, out _))
            {
                LastVisitedNodeCount = 0;
                return null;
            }

            float startHeuristic = Heuristic(start, target);
            int startIndex = AddNode(start, -1, 0f, startHeuristic);
            nearestIndex = startIndex;
            nearestHeuristic = startHeuristic;
            nearestCostFromStart = 0f;
            openSet.Push(startIndex);

            int visited = 0;
            int visitLimit = profile.VisitLimit;
            while (!openSet.IsEmpty)
            {
                int currentIndex = openSet.Pop();
                closed[currentIndex] = true;
                Vector3Int current = positions[currentIndex];
                if (current == target)
                {
                    LastVisitedNodeCount = visited;
                    return BuildPath(currentIndex, true);
                }

                visited++;
                if (visited >= visitLimit)
                {
                    LastVisitedNodeCount = visited;
                    return BuildPath(nearestIndex, false);
                }

                Expand(currentIndex, current, target);
            }

            LastVisitedNodeCount = visited;
            return BuildPath(nearestIndex, false);
        }

        private void Expand(int currentIndex, Vector3Int current, Vector3Int target)
        {
            int successorCount = nodeMaker.GetSuccessors(current, successors, stepCosts);
            float currentCost = costFromStart[currentIndex];
            for (int i = 0; i < successorCount; i++)
            {
                Vector3Int next = successors[i];
                float tentativeCost = currentCost + stepCosts[i];
                if (!nodeIndices.TryGetValue(next, out int nextIndex))
                {
                    float heuristic = Heuristic(next, target);
                    nextIndex = AddNode(
                        next,
                        currentIndex,
                        tentativeCost,
                        heuristic);
                    UpdateNearest(nextIndex, heuristic, tentativeCost);
                    openSet.Push(nextIndex);
                    continue;
                }

                if (closed[nextIndex] || tentativeCost >= costFromStart[nextIndex])
                {
                    continue;
                }

                float known = heuristics[nextIndex];
                parents[nextIndex] = currentIndex;
                costFromStart[nextIndex] = tentativeCost;
                totalCost[nextIndex] = tentativeCost + known;
                UpdateNearest(nextIndex, known, tentativeCost);
                if (openSet.Contains(nextIndex))
                {
                    openSet.Sift(nextIndex);
                }
                else
                {
                    openSet.Push(nextIndex);
                }
            }
        }

        /// <summary>
        /// Tracks the node closest to the target. Constant time because it only
        /// compares against the running best, so recovering the fallback route
        /// after the visit limit needs no scan.
        /// </summary>
        private void UpdateNearest(int index, float heuristic, float cost)
        {
            if (heuristic > nearestHeuristic)
            {
                return;
            }

            // Ties resolve toward the cheaper route so the fallback path is the
            // shortest way to reach that same distance from the target.
            if (heuristic < nearestHeuristic || cost < nearestCostFromStart)
            {
                nearestIndex = index;
                nearestHeuristic = heuristic;
                nearestCostFromStart = cost;
            }
        }

        private VoxelPath BuildPath(int endIndex, bool reachesTarget)
        {
            path.Reset(reachesTarget);
            for (int index = endIndex; index >= 0; index = parents[index])
            {
                path.Append(positions[index]);
            }
            path.FinishReversedAppend();
            path.SkipStartNode();
            return path;
        }

        private int AddNode(
            Vector3Int position,
            int parent,
            float cost,
            float heuristic)
        {
            int index = positions.Count;
            positions.Add(position);
            parents.Add(parent);
            costFromStart.Add(cost);
            heuristics.Add(heuristic);
            totalCost.Add(cost + heuristic);
            closed.Add(false);
            nodeIndices[position] = index;
            return index;
        }

        private void ClearWorkspace()
        {
            nodeIndices.Clear();
            positions.Clear();
            parents.Clear();
            costFromStart.Clear();
            heuristics.Clear();
            totalCost.Clear();
            closed.Clear();
            openSet.Begin(totalCost, 256);
        }

        /// <summary>Straight line distance to the target, in voxels.</summary>
        public static float Heuristic(Vector3Int node, Vector3Int target)
        {
            return Vector3.Distance(node, target);
        }
    }
}
