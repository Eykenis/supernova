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

    public sealed class MinecraftCaveVoxelQuery : ICreatureVoxelQuery
    {
        private readonly MinecraftCaveInfiniteWorld caveWorld;
        private readonly float solidDensityThreshold;

        public MinecraftCaveVoxelQuery(
            MinecraftCaveInfiniteWorld caveWorld,
            float solidDensityThreshold = 0f)
        {
            this.caveWorld = caveWorld != null
                ? caveWorld
                : throw new ArgumentNullException(nameof(caveWorld));
            this.solidDensityThreshold = solidDensityThreshold;
        }

        public bool TryGetSolid(Vector3Int voxel, out bool isSolid)
        {
            InfiniteVoxelWorld world = caveWorld.World;
            if (world == null
                || !world.TryGetDensity(voxel.x, voxel.y, voxel.z, out float density))
            {
                isSolid = false;
                return false;
            }

            isSolid = density >= solidDensityThreshold;
            return true;
        }
    }

    [Serializable]
    public sealed class CreatureNavigationSettings
    {
        [Min(0)] public int safeFallHeight = 3;
        [Min(0)] public int maximumJumpHeight = 1;
        [Min(1)] public int maximumSingleMoveCost = 100;
        [Min(1)] public int maximumExpandedNodes = 8192;
    }

    public static class CreatureVoxelNavigation
    {
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

        public static bool IsStandable(
            ICreatureVoxelQuery query,
            CreatureVoxelShape shape,
            Vector3Int support)
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
            if (!IsKnownAirOrFloorSurface(query, foot))
            {
                return false;
            }

            IReadOnlyList<Vector3Int> occupied = shape.OccupiedVoxels;
            int lowestBodyLayer = shape.OccupiedBounds.min.y;
            for (int i = 0; i < occupied.Count; i++)
            {
                Vector3Int offset = occupied[i];
                bool clear = offset.y == lowestBodyLayer
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
            Vector3Int candidate = fromSupport + new Vector3Int(
                Math.Sign(horizontalDirection.x),
                0,
                Math.Sign(horizontalDirection.z));

            if (candidate.x == fromSupport.x && candidate.z == fromSupport.z)
            {
                return false;
            }

            if (!query.TryGetSolid(candidate, out bool candidateSolid))
            {
                return false;
            }

            if (candidateSolid)
            {
                for (int rise = 0; rise <= settings.maximumJumpHeight; rise++)
                {
                    Vector3Int raised = candidate + Vector3Int.up * rise;
                    if (IsStandable(query, shape, raised))
                    {
                        int cost = 1 + rise * 2;
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

            int costSoFar = 1;
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

                if (IsStandable(query, shape, lowered))
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
            // The dynamic Rigidbody and the actual Marching Cubes collider own the
            // creature's present placement. Its current support may not satisfy the
            // discrete density approximation even though physics says it is grounded.
            if (!IsStandable(query, shape, targetSupport))
            {
                return false;
            }

            var open = new OpenMinHeap();
            var bestG = new Dictionary<Vector3Int, int>();
            var cameFrom = new Dictionary<Vector3Int, Vector3Int>();
            bestG[startSupport] = 0;
            open.Push(new OpenNode(startSupport, 0, Heuristic(startSupport, targetSupport)));

            while (open.Count > 0 && expandedNodeCount < settings.maximumExpandedNodes)
            {
                OpenNode current = open.Pop();
                if (!bestG.TryGetValue(current.Position, out int knownG)
                    || current.G != knownG)
                {
                    continue;
                }

                if (current.Position == targetSupport)
                {
                    ReconstructPath(cameFrom, current.Position, path);
                    return true;
                }

                expandedNodeCount++;
                for (int i = 0; i < HorizontalDirections.Length; i++)
                {
                    if (!TryResolveTransition(
                            query,
                            shape,
                            settings,
                            current.Position,
                            HorizontalDirections[i],
                            out Vector3Int neighbour,
                            out int stepCost))
                    {
                        continue;
                    }

                    int tentativeG = current.G + stepCost;
                    if (bestG.TryGetValue(neighbour, out int previousG)
                        && tentativeG >= previousG)
                    {
                        continue;
                    }

                    bestG[neighbour] = tentativeG;
                    cameFrom[neighbour] = current.Position;
                    float f = tentativeG + Heuristic(neighbour, targetSupport);
                    open.Push(new OpenNode(neighbour, tentativeG, f));
                }
            }

            return false;
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


        private static float Heuristic(Vector3Int from, Vector3Int to)
        {
            return Vector3.Distance(from, to);
        }

        private static void ReconstructPath(
            IReadOnlyDictionary<Vector3Int, Vector3Int> cameFrom,
            Vector3Int current,
            List<Vector3Int> path)
        {
            path.Add(current);
            while (cameFrom.TryGetValue(current, out Vector3Int previous))
            {
                current = previous;
                path.Add(current);
            }

            path.Reverse();
        }

        private readonly struct OpenNode
        {
            public OpenNode(Vector3Int position, int g, float f)
            {
                Position = position;
                G = g;
                F = f;
            }

            public Vector3Int Position { get; }
            public int G { get; }
            public float F { get; }
        }

        private sealed class OpenMinHeap
        {
            private readonly List<OpenNode> items = new List<OpenNode>();

            public int Count => items.Count;

            public void Push(OpenNode item)
            {
                items.Add(item);
                int index = items.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!ComesBefore(items[index], items[parent]))
                    {
                        break;
                    }

                    (items[index], items[parent]) = (items[parent], items[index]);
                    index = parent;
                }
            }

            public OpenNode Pop()
            {
                OpenNode root = items[0];
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);

                int index = 0;
                while (index < items.Count)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    if (left >= items.Count)
                    {
                        break;
                    }

                    int best = right < items.Count && ComesBefore(items[right], items[left])
                        ? right
                        : left;
                    if (!ComesBefore(items[best], items[index]))
                    {
                        break;
                    }

                    (items[index], items[best]) = (items[best], items[index]);
                    index = best;
                }

                return root;
            }

            private static bool ComesBefore(OpenNode left, OpenNode right)
            {
                if (!Mathf.Approximately(left.F, right.F))
                {
                    return left.F < right.F;
                }

                return left.G > right.G;
            }
        }
    }
}
