using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels.Support.Prototype
{
    /// <summary>
    /// Outcome of a single support-graph analysis triggered by voxel removal.
    /// </summary>
    public readonly struct SupportAnalysisResult
    {
        public SupportAnalysisResult(
            IReadOnlyList<Vector3Int> collapsedVoxels,
            IReadOnlyList<Vector3Int> fragileVoxels,
            IReadOnlyCollection<Vector3Int> affectedVoxels,
            int cascadeIterationsUsed)
        {
            CollapsedVoxels = collapsedVoxels ?? Array.Empty<Vector3Int>();
            FragileVoxels = fragileVoxels ?? Array.Empty<Vector3Int>();
            AffectedVoxels = affectedVoxels ?? Array.Empty<Vector3Int>();
            CascadeIterationsUsed = cascadeIterationsUsed;
        }

        /// <summary>Voxels that carry zero stress and should collapse.</summary>
        public IReadOnlyList<Vector3Int> CollapsedVoxels { get; }

        /// <summary>Voxels on near-shortest stress paths — fragile.</summary>
        public IReadOnlyList<Vector3Int> FragileVoxels { get; }

        /// <summary>All voxels visited during the BFS (the affected sub-graph).</summary>
        public IReadOnlyCollection<Vector3Int> AffectedVoxels { get; }

        /// <summary>Number of cascade iterations that actually ran.</summary>
        public int CascadeIterationsUsed { get; }
    }

    /// <summary>
    /// Voxel stress analysis based on the EBC_LB (Edge Boundary Betweenness
    /// Centrality) algorithm from Reyes-Martinez et al. (arXiv:2412.15344).
    ///
    /// <b>Core adaptation to uniform voxel grids:</b>
    ///
    /// Stress (load) flows from <em>source</em> voxels (those that were directly
    /// supported from below by the removed voxel) to <em>target</em> anchors
    /// (bedrock / explicit structural supports) along <em>shortest paths</em>.
    ///
    /// A voxel that lies on NO shortest path from any source to any anchor
    /// carries zero stress — it is supported only by longer detours that would
    /// require the material to bear tension sideways, which rigid strut lattices
    /// cannot do.  Such voxels are marked as <c>collapsed</c>.
    ///
    /// Voxels that lie on shortest paths but only <em>near</em> the minimum
    /// are marked <c>fragile</c> (single stress path).
    ///
    /// <b>Complexity:</b> O(k) per analysis where k = affected sub-graph size.
    /// Two BFS passes (one from sources, one from anchors), no Brandes iteration.
    /// </summary>
    public sealed class VoxelSupportGraph
    {
        // ── 6-connectivity ────────────────────────────────────────────
        private static readonly Vector3Int[] Neighbours =
        {
            new( 1,  0,  0), new(-1,  0,  0),
            new( 0,  1,  0), new( 0, -1,  0),
            new( 0,  0,  1), new( 0,  0, -1),
        };

        private readonly VoxelSupportConfig config;

        // Pre-allocated collections to avoid GC pressure on hot path.
        private readonly Queue<Vector3Int> bfsQueue = new(512);
        private readonly HashSet<Vector3Int> bfsVisited = new(512);
        private readonly Dictionary<Vector3Int, int> distanceMap = new(512);
        private readonly List<Vector3Int> sourceBuffer = new(32);
        private readonly List<Vector3Int> anchorBuffer = new(32);

        public VoxelSupportGraph(VoxelSupportConfig config)
        {
            this.config = config;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Public API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Run EBC_LB stress analysis after removing voxels at
        /// <paramref name="removedWorldPositions"/>.
        /// </summary>
        public SupportAnalysisResult Analyze(
            IReadOnlyList<Vector3Int> removedWorldPositions,
            Func<Vector3Int, bool> solidity,
            Func<Vector3Int, bool> isAnchor)
        {
            if (removedWorldPositions == null || removedWorldPositions.Count == 0)
                return Empty();

            List<Vector3Int> allCollapsed = new(64);
            List<Vector3Int> allFragile = new(64);
            HashSet<Vector3Int> allAffected = new(64);

            HashSet<Vector3Int> removedSet = new(removedWorldPositions);
            Func<Vector3Int, bool> effectiveSolidity = pos =>
            {
                if (removedSet.Contains(pos)) return false;
                return solidity(pos);
            };

            foreach (Vector3Int seed in removedWorldPositions)
            {
                // ── Step 1: collect affected sub-graph ─────────────────
                var (subGraph, wasTruncated) = CollectAffectedSubGraph(
                    seed, effectiveSolidity);
                if (subGraph.Count == 0) continue;

                allAffected.UnionWith(subGraph);

                if (wasTruncated)
                {
                    // Sub-graph too large — only the seed itself might be
                    // at risk if it has no downward anchor within reach.
                    if (!HasDownwardPathToAnchor(seed, effectiveSolidity, isAnchor))
                        allCollapsed.Add(seed);
                    continue;
                }

                // ── Step 2: identify sources (load boundary) ───────────
                // Source = solid voxels directly above each removed position.
                sourceBuffer.Clear();
                foreach (Vector3Int removed in removedWorldPositions)
                {
                    Vector3Int above = removed + Vector3Int.up;
                    if (effectiveSolidity(above) && subGraph.Contains(above))
                        sourceBuffer.Add(above);
                }
                // If no voxels sit directly above the removal, walk upward
                // up to MaxSearchRadius to find the next solid voxel through
                // air — this is the overhang / ceiling that just lost its support.
                if (sourceBuffer.Count == 0)
                {
                    foreach (Vector3Int removed in removedWorldPositions)
                    {
                        for (int y = 1; y <= config.MaxSearchRadius; y++)
                        {
                            Vector3Int above = removed + Vector3Int.up * y;
                            if (effectiveSolidity(above) && subGraph.Contains(above))
                            {
                                sourceBuffer.Add(above);
                                break;
                            }
                        }
                    }
                }

                // ── Step 3: identify anchors (support boundary) ────────
                anchorBuffer.Clear();
                foreach (Vector3Int pos in subGraph)
                {
                    if (isAnchor(pos))
                        anchorBuffer.Add(pos);
                }

                if (anchorBuffer.Count == 0)
                {
                    // No anchors → entire sub-graph is floating → collapse all.
                    allCollapsed.AddRange(subGraph);
                    continue;
                }

                if (sourceBuffer.Count == 0)
                {
                    // No sources found above the removal → the cave ceiling
                    // above the removal is already air.  Nothing to stress-check.
                    continue;
                }

                // ── Step 4: dual BFS (EBC_LB core) ─────────────────────
                //   distFromSource[v] = shortest-path distance from v to any source.
                //   distToAnchor[v]  = shortest-path distance from v to any anchor.
                Dictionary<Vector3Int, int> distFromSource;
                Dictionary<Vector3Int, int> distToAnchor;
                HashSet<Vector3Int> sourceReachable;
                HashSet<Vector3Int> anchorReachable;

                BfsFromSources(sourceBuffer, effectiveSolidity, subGraph,
                    out sourceReachable, out distFromSource);
                BfsFromSources(anchorBuffer, effectiveSolidity, subGraph,
                    out anchorReachable, out distToAnchor);

                // ── Step 5: collapse zero-stress voxels ────────────────
                // Compute the shortest source→anchor distance (the "stress
                // path" baseline).
                int? minStressPathLen = null;
                foreach (Vector3Int pos in subGraph)
                {
                    if (!sourceReachable.Contains(pos)) continue;
                    if (!anchorReachable.Contains(pos)) continue;

                    int pathLen = distFromSource[pos] + distToAnchor[pos];
                    if (!minStressPathLen.HasValue || pathLen < minStressPathLen.Value)
                        minStressPathLen = pathLen;
                }

                if (!minStressPathLen.HasValue)
                {
                    // No shortest path from any source to any anchor exists
                    // within the sub-graph → the entire load-bearing structure
                    // is disconnected from anchors.
                    allCollapsed.AddRange(subGraph);
                    continue;
                }

                int baseline = minStressPathLen.Value;
                int slack = Mathf.Max(1, baseline / 2); // allow small detours

                foreach (Vector3Int pos in subGraph)
                {
                    bool onSourceSide = sourceReachable.Contains(pos);
                    bool onAnchorSide = anchorReachable.Contains(pos);

                    if (!onSourceSide || !onAnchorSide)
                    {
                        // Voxel is not reachable from BOTH source and anchor
                        // frontiers — it carries no stress.
                        if (!isAnchor(pos))
                            allCollapsed.Add(pos);
                    }
                    else
                    {
                        int pathLen = distFromSource[pos] + distToAnchor[pos];
                        if (pathLen <= baseline + slack)
                        {
                            // On or near a shortest stress path — stable.
                        }
                        else
                        {
                            // Detour is too long — stress would bypass this voxel.
                            allCollapsed.Add(pos);
                        }
                    }
                }

                // ── Step 6: mark fragile voxels ────────────────────────
                foreach (Vector3Int pos in sourceReachable)
                {
                    if (!anchorReachable.Contains(pos)) continue;
                    if (isAnchor(pos)) continue;

                    Vector3Int below = pos + Vector3Int.down;
                    if (!effectiveSolidity(below))
                        allFragile.Add(pos);
                }
            }

            // ── Cascade loop (connectivity fallback) ───────────────────
            int iteration = 0;
            while (allCollapsed.Count > 0 && iteration < config.MaxCascadeIterations)
            {
                iteration++;
                HashSet<Vector3Int> expandedRemoved = new(removedSet);
                expandedRemoved.UnionWith(allCollapsed);
                Func<Vector3Int, bool> cascadeSolidity = pos =>
                {
                    if (expandedRemoved.Contains(pos)) return false;
                    return solidity(pos);
                };

                List<Vector3Int> cascadeCollapsed = new(64);
                foreach (Vector3Int collapsed in allCollapsed)
                {
                    foreach (Vector3Int dir in Neighbours)
                    {
                        Vector3Int neighbour = collapsed + dir;
                        if (!solidity(neighbour)) continue;
                        if (expandedRemoved.Contains(neighbour)) continue;
                        if (isAnchor(neighbour)) continue;

                        if (!HasPathToAnchor(neighbour, cascadeSolidity, isAnchor))
                        {
                            cascadeCollapsed.Add(neighbour);
                            expandedRemoved.Add(neighbour);
                        }
                    }
                }

                if (cascadeCollapsed.Count == 0) break;
                allCollapsed.AddRange(cascadeCollapsed);
                allAffected.UnionWith(cascadeCollapsed);
            }

            return new SupportAnalysisResult(
                allCollapsed, allFragile, allAffected, iteration);
        }

        // ═══════════════════════════════════════════════════════════════
        //  FullScan
        // ═══════════════════════════════════════════════════════════════

        public SupportAnalysisResult FullScan(
            Func<Vector3Int, bool> solidity,
            Func<Vector3Int, bool> isAnchor,
            int volumeSizeX, int volumeSizeY, int volumeSizeZ)
        {
            List<Vector3Int> collapsed = new(64);
            List<Vector3Int> fragile = new(64);
            HashSet<Vector3Int> affected = new(64);
            bfsQueue.Clear();
            bfsVisited.Clear();

            for (int x = 0; x < volumeSizeX; x++)
            for (int y = config.BedrockYThreshold + 1; y < volumeSizeY; y++)
            for (int z = 0; z < volumeSizeZ; z++)
            {
                Vector3Int pos = new(x, y, z);
                if (!solidity(pos)) continue;
                if (bfsVisited.Contains(pos)) continue;
                if (isAnchor(pos)) { bfsVisited.Add(pos); continue; }

                HashSet<Vector3Int> component = CollectAffectedSubGraphFull(
                    pos, solidity, volumeSizeX, volumeSizeY, volumeSizeZ);
                if (component.Count == 0) continue;

                bool hasAnchor = false;
                foreach (Vector3Int v in component)
                    if (isAnchor(v)) { hasAnchor = true; break; }

                if (!hasAnchor)
                {
                    collapsed.AddRange(component);
                    affected.UnionWith(component);
                }
                else
                {
                    foreach (Vector3Int v in component)
                    {
                        bfsVisited.Add(v);
                        if (isAnchor(v)) continue;
                        Vector3Int below = v + Vector3Int.down;
                        if (!solidity(below)) fragile.Add(v);
                    }
                }
                foreach (Vector3Int v in component) bfsVisited.Add(v);
            }

            return new SupportAnalysisResult(collapsed, fragile, affected, 0);
        }

        // ═══════════════════════════════════════════════════════════════
        //  BFS helpers
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Multi-source BFS.  Returns the set of reachable voxels and their
        /// distances from the nearest source.
        /// </summary>
        private void BfsFromSources(
            IReadOnlyList<Vector3Int> sources,
            Func<Vector3Int, bool> solidity,
            HashSet<Vector3Int> restrictTo,
            out HashSet<Vector3Int> reachable,
            out Dictionary<Vector3Int, int> distances)
        {
            reachable = new HashSet<Vector3Int>(64);
            distances = new Dictionary<Vector3Int, int>(64);
            bfsQueue.Clear();

            foreach (Vector3Int src in sources)
            {
                if (!restrictTo.Contains(src)) continue;
                bfsQueue.Enqueue(src);
                reachable.Add(src);
                distances[src] = 0;
            }

            while (bfsQueue.Count > 0)
            {
                Vector3Int current = bfsQueue.Dequeue();
                int nextDist = distances[current] + 1;

                foreach (Vector3Int dir in Neighbours)
                {
                    Vector3Int neighbour = current + dir;

                    if (!restrictTo.Contains(neighbour)) continue;
                    if (!solidity(neighbour)) continue;
                    if (reachable.Contains(neighbour)) continue;

                    distances[neighbour] = nextDist;
                    reachable.Add(neighbour);
                    bfsQueue.Enqueue(neighbour);
                }
            }
        }

        private bool HasPathToAnchor(
            Vector3Int start,
            Func<Vector3Int, bool> solidity,
            Func<Vector3Int, bool> isAnchor)
        {
            bfsQueue.Clear();
            bfsVisited.Clear();
            bfsQueue.Enqueue(start);
            bfsVisited.Add(start);

            while (bfsQueue.Count > 0)
            {
                Vector3Int current = bfsQueue.Dequeue();
                if (ManhattanDistance(start, current) > config.MaxSearchRadius)
                    continue;
                if (isAnchor(current))
                    return true;

                foreach (Vector3Int dir in Neighbours)
                {
                    Vector3Int n = current + dir;
                    if (!solidity(n)) continue;
                    if (!bfsVisited.Add(n)) continue;
                    bfsQueue.Enqueue(n);
                }
            }
            return false;
        }

        private bool HasDownwardPathToAnchor(
            Vector3Int start,
            Func<Vector3Int, bool> solidity,
            Func<Vector3Int, bool> isAnchor)
        {
            bfsQueue.Clear();
            bfsVisited.Clear();
            bfsQueue.Enqueue(start);
            bfsVisited.Add(start);

            int maxSteps = config.MaxSearchRadius * 3;
            int steps = 0;
            while (bfsQueue.Count > 0 && steps < maxSteps)
            {
                Vector3Int current = bfsQueue.Dequeue();
                steps++;
                if (isAnchor(current)) return true;

                foreach (Vector3Int dir in Neighbours)
                {
                    Vector3Int n = current + dir;
                    if (!solidity(n)) continue;
                    if (!bfsVisited.Add(n)) continue;
                    bfsQueue.Enqueue(n);
                }
            }
            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Sub-graph collection
        // ═══════════════════════════════════════════════════════════════

        private (HashSet<Vector3Int> graph, bool wasTruncated)
        CollectAffectedSubGraph(Vector3Int seed, Func<Vector3Int, bool> solidity)
        {
            HashSet<Vector3Int> subGraph = new(64);
            bfsQueue.Clear();
            bfsVisited.Clear();
            bfsQueue.Enqueue(seed);
            bfsVisited.Add(seed);

            bool truncated = false;
            while (bfsQueue.Count > 0 && subGraph.Count < config.MaxSubGraphVoxels)
            {
                Vector3Int current = bfsQueue.Dequeue();
                foreach (Vector3Int dir in Neighbours)
                {
                    Vector3Int n = current + dir;
                    if (ManhattanDistance(seed, n) > config.MaxSearchRadius)
                        continue;
                    if (!solidity(n)) continue;
                    if (!bfsVisited.Add(n)) continue;
                    bfsQueue.Enqueue(n);
                    subGraph.Add(n);
                }
            }

            if (bfsQueue.Count > 0)
            {
                truncated = true;
                while (bfsQueue.Count > 0)
                {
                    subGraph.Add(bfsQueue.Dequeue());
                }
            }

            return (subGraph, truncated);
        }

        private HashSet<Vector3Int> CollectAffectedSubGraphFull(
            Vector3Int seed, Func<Vector3Int, bool> solidity,
            int maxX, int maxY, int maxZ)
        {
            HashSet<Vector3Int> subGraph = new(64);
            bfsQueue.Clear();
            HashSet<Vector3Int> localVisited = new(64);
            bfsQueue.Enqueue(seed);
            localVisited.Add(seed);
            subGraph.Add(seed);

            while (bfsQueue.Count > 0 && subGraph.Count < config.MaxSubGraphVoxels)
            {
                Vector3Int current = bfsQueue.Dequeue();
                foreach (Vector3Int dir in Neighbours)
                {
                    Vector3Int n = current + dir;
                    if (n.x < 0 || n.x >= maxX || n.y < 0 || n.y >= maxY
                        || n.z < 0 || n.z >= maxZ) continue;
                    if (!solidity(n)) continue;
                    if (!localVisited.Add(n)) continue;
                    bfsQueue.Enqueue(n);
                    subGraph.Add(n);
                }
            }
            return subGraph;
        }

        private static int ManhattanDistance(Vector3Int a, Vector3Int b)
        {
            return Math.Abs(a.x - b.x)
                 + Math.Abs(a.y - b.y)
                 + Math.Abs(a.z - b.z);
        }

        private static SupportAnalysisResult Empty()
        {
            return new SupportAnalysisResult(
                Array.Empty<Vector3Int>(),
                Array.Empty<Vector3Int>(),
                Array.Empty<Vector3Int>(),
                0);
        }
    }
}
