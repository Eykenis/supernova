using System.Collections.Generic;
using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures.Navigation
{
    /// <summary>
    /// Builds the implicit three dimensional navigation graph on demand, playing
    /// the role Minecraft gives LandPathNodeMaker. A node is a foot position; its
    /// successors are resolved by probing the eight horizontal neighbours and
    /// then following the terrain up or down within the creature's limits.
    /// <para>
    /// The graph only ever describes adjacency between standable positions. It is
    /// deliberately not an absolute height field: a smooth Marching Cubes slope
    /// reads as a same-layer neighbour, and the small real height difference is
    /// left to the physics motor to walk or to jump over.
    /// </para>
    /// </summary>
    public sealed class VoxelPathNodeMaker
    {
        /// <summary>The eight horizontal directions, orthogonals first.</summary>
        private static readonly Vector3Int[] HorizontalDirections =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1),
            new Vector3Int(1, 0, 1),
            new Vector3Int(1, 0, -1),
            new Vector3Int(-1, 0, 1),
            new Vector3Int(-1, 0, -1),
        };

        public const int MaximumSuccessors = 8;

        private readonly IVoxelSolidityQuery solidity;
        private readonly Dictionary<Vector3Int, int> classificationMemo =
            new Dictionary<Vector3Int, int>();

        private CreatureBodyBox body;
        private CreatureNavigationProfile profile;

        public VoxelPathNodeMaker(IVoxelSolidityQuery solidity)
        {
            this.solidity = solidity;
        }

        public CreatureBodyBox Body => body;

        /// <summary>
        /// Prepares the maker for one search. The classification memo is scoped
        /// to a single search because eight parents probe overlapping candidates,
        /// while terrain edits between searches must be picked up immediately.
        /// </summary>
        public void BeginSearch(CreatureBodyBox bodyBox, CreatureNavigationProfile searchProfile)
        {
            body = bodyBox;
            profile = searchProfile;
            classificationMemo.Clear();
        }

        /// <summary>
        /// Classifies a foot position. Fails when the body would intersect solid
        /// voxels or when any probed voxel belongs to an ungenerated chunk.
        /// </summary>
        public bool TryClassify(Vector3Int footNode, out PathNodeType type)
        {
            if (classificationMemo.TryGetValue(footNode, out int cached))
            {
                type = (PathNodeType)Mathf.Max(0, cached);
                return cached >= 0;
            }

            bool classified = Classify(footNode, out type);
            classificationMemo[footNode] = classified ? (int)type : -1;
            return classified;
        }

        private bool Classify(Vector3Int footNode, out PathNodeType type)
        {
            type = PathNodeType.Open;
            if (!IsBodyBoxClear(footNode))
            {
                return false;
            }

            // Support is judged on the centre column alone. Requiring ground under
            // every column the body covers would reject slopes, ledges and cave
            // rims that the creature can physically stand on.
            if (!solidity.TryGetSolid(
                footNode.x,
                footNode.y - 1,
                footNode.z,
                out bool supported))
            {
                return false;
            }

            type = supported ? PathNodeType.Walkable : PathNodeType.Open;
            return true;
        }

        /// <summary>
        /// Checks that the creature's box fits at a foot position. Every probed
        /// voxel must be known and empty.
        /// </summary>
        public bool IsBodyBoxClear(Vector3Int footNode)
        {
            int radius = body.HorizontalRadius;
            for (int y = 0; y < body.HeightInVoxels; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    for (int z = -radius; z <= radius; z++)
                    {
                        if (!solidity.TryGetSolid(
                            footNode.x + x,
                            footNode.y + y,
                            footNode.z + z,
                            out bool isSolid)
                            || isSolid)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Writes the walkable successors of a node into the caller's buffers and
        /// returns how many were produced. Buffers must hold at least
        /// <see cref="MaximumSuccessors"/> entries.
        /// </summary>
        public int GetSuccessors(
            Vector3Int node,
            Vector3Int[] successors,
            float[] stepCosts)
        {
            int count = 0;
            for (int i = 0; i < HorizontalDirections.Length; i++)
            {
                Vector3Int direction = HorizontalDirections[i];
                bool isDiagonal = direction.x != 0 && direction.z != 0;
                if (isDiagonal && !CanCutCorner(node, direction))
                {
                    continue;
                }

                if (!TryResolveStep(node, direction, out Vector3Int resolved))
                {
                    continue;
                }

                successors[count] = resolved;
                stepCosts[count] = StepCost(node, resolved);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Resolves where one horizontal step actually lands. A blocked candidate
        /// is retried upwards within the jump limit, and an unsupported candidate
        /// falls to the first standable surface within the fall limit.
        /// </summary>
        public bool TryResolveStep(
            Vector3Int node,
            Vector3Int direction,
            out Vector3Int resolved)
        {
            Vector3Int candidate = node + direction;
            if (TryClassify(candidate, out PathNodeType type))
            {
                if (type == PathNodeType.Walkable)
                {
                    resolved = candidate;
                    return true;
                }

                return TryFall(candidate, out resolved);
            }

            return TryClimb(candidate, out resolved);
        }

        private bool TryClimb(Vector3Int blockedCandidate, out Vector3Int resolved)
        {
            for (int rise = 1; rise <= profile.MaximumJumpHeight; rise++)
            {
                Vector3Int lifted = blockedCandidate + new Vector3Int(0, rise, 0);
                if (!TryClassify(lifted, out PathNodeType type))
                {
                    continue;
                }

                if (type == PathNodeType.Walkable)
                {
                    resolved = lifted;
                    return true;
                }

                // Clearance opened up without support, so nothing higher in this
                // column can be climbed into either.
                break;
            }

            resolved = default;
            return false;
        }

        private bool TryFall(Vector3Int openCandidate, out Vector3Int resolved)
        {
            for (int drop = 1; drop <= profile.MaximumSafeFall; drop++)
            {
                Vector3Int lowered = openCandidate - new Vector3Int(0, drop, 0);
                if (!TryClassify(lowered, out PathNodeType type))
                {
                    break;
                }

                if (type == PathNodeType.Walkable)
                {
                    resolved = lowered;
                    return true;
                }
            }

            resolved = default;
            return false;
        }

        /// <summary>
        /// Diagonal moves require both orthogonal neighbours at the current layer
        /// to be enterable, so a creature never slices through a wall corner.
        /// </summary>
        private bool CanCutCorner(Vector3Int node, Vector3Int diagonal)
        {
            Vector3Int alongX = node + new Vector3Int(diagonal.x, 0, 0);
            Vector3Int alongZ = node + new Vector3Int(0, 0, diagonal.z);
            return TryClassify(alongX, out _) && TryClassify(alongZ, out _);
        }

        /// <summary>
        /// Straight line distance between the two nodes. Matching the heuristic's
        /// metric keeps it consistent, so A* stays optimal and a diagonal costs
        /// exactly the square root of two.
        /// </summary>
        public static float StepCost(Vector3Int from, Vector3Int to)
        {
            return Vector3.Distance(from, to);
        }
    }
}
