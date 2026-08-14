using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures.Navigation
{
    /// <summary>
    /// Owns a creature's current path and decides when to rebuild it, the role
    /// Minecraft gives MobNavigation. Kept free of MonoBehaviour and physics so
    /// replanning policy and wander sampling stay testable: time and randomness
    /// are supplied by the caller.
    /// </summary>
    public sealed class CreatureNavigator
    {
        private readonly VoxelPathNodeMaker nodeMaker;
        private readonly VoxelPathfinder pathfinder;
        private readonly CreatureNavigationProfile profile;
        private readonly System.Random random;

        private VoxelPath path;
        private Vector3Int plannedTarget;
        private bool hasPlannedTarget;
        private float nextReplanTime;
        private float failedPlanRetryTime;
        private float wanderBlockedUntil;

        public CreatureNavigator(
            VoxelPathNodeMaker nodeMaker,
            VoxelPathfinder pathfinder,
            CreatureNavigationProfile profile,
            System.Random random)
        {
            this.nodeMaker = nodeMaker;
            this.pathfinder = pathfinder;
            this.profile = profile;
            this.random = random ?? new System.Random();
        }

        public VoxelPath CurrentPath => path;
        public bool HasActivePath => path != null && !path.IsFinished;
        public Vector3Int PlannedTarget => plannedTarget;
        public int LastVisitedNodeCount => pathfinder.LastVisitedNodeCount;

        public void Clear()
        {
            path?.Invalidate();
            path = null;
            hasPlannedTarget = false;
        }

        /// <summary>
        /// Decides whether a fresh search is worthwhile. A route that ran out is
        /// rebuilt at once, because waiting out the cooldown with no path is simply
        /// standing still. Otherwise pursuit waits for the target to drift far
        /// enough and for the randomized cooldown, which keeps a crowd of creatures
        /// from all searching on the same frame.
        /// </summary>
        public bool ShouldReplan(Vector3Int target, float time)
        {
            if (!HasActivePath)
            {
                // A failed search backs off, otherwise an unreachable target would
                // be searched every single frame.
                return time >= failedPlanRetryTime;
            }

            if (time < nextReplanTime)
            {
                return false;
            }

            if (!hasPlannedTarget)
            {
                return true;
            }

            float drift = Vector3.Distance(plannedTarget, target);
            return drift >= profile.TargetDriftThreshold;
        }

        /// <summary>
        /// Plans a route and adopts it. A partial path toward the closest reachable
        /// node is accepted, so an unreachable target still produces movement.
        /// </summary>
        public VoxelPath MoveTo(
            Vector3Int start,
            Vector3Int target,
            CreatureBodyBox body,
            float time)
        {
            nextReplanTime = time + NextReplanDelay();
            VoxelPath planned = pathfinder.Search(start, target, body, profile);
            if (planned == null || planned.IsFinished)
            {
                path = null;
                hasPlannedTarget = false;
                failedPlanRetryTime = time + NextReplanDelay();
                return null;
            }

            path = planned;
            plannedTarget = target;
            hasPlannedTarget = true;
            failedPlanRetryTime = 0f;
            return path;
        }

        /// <summary>
        /// Picks a random standable node near the creature by rejection sampling,
        /// the approach Minecraft's wander goals use. No global list of walkable
        /// positions is maintained.
        /// <para>
        /// Only the horizontal offset is random; the height is found by scanning the
        /// candidate column for ground. Guessing a height as well would almost
        /// always miss, because a column has many air cells and usually only one
        /// standable one.
        /// </para>
        /// </summary>
        public bool TrySampleWanderTarget(
            Vector3Int origin,
            CreatureBodyBox body,
            float time,
            out Vector3Int target)
        {
            target = default;
            if (time < wanderBlockedUntil)
            {
                return false;
            }

            nodeMaker.BeginSearch(body, profile);
            int radius = Mathf.Max(1, Mathf.RoundToInt(profile.WanderRadius));
            // Require a useful distance. A neighbouring cell is already inside the
            // arrival tolerance, so it would complete the moment the creature starts
            // and leave it stuttering between samples.
            int minimumRadius = Mathf.Max(2, radius / 3);
            int minimumSquared = minimumRadius * minimumRadius;
            for (int attempt = 0; attempt < profile.WanderAttempts; attempt++)
            {
                int offsetX = random.Next(-radius, radius + 1);
                int offsetZ = random.Next(-radius, radius + 1);
                if (offsetX * offsetX + offsetZ * offsetZ < minimumSquared)
                {
                    continue;
                }

                if (TryFindColumnGround(
                    origin.x + offsetX,
                    origin.y,
                    origin.z + offsetZ,
                    out target))
                {
                    return true;
                }
            }

            // Nothing suitable nearby, so pause before burning more samples.
            wanderBlockedUntil = time + profile.WanderRetryInterval;
            return false;
        }

        /// <summary>
        /// Scans a column outwards from the creature's own height for a standable
        /// node, nearest height first.
        /// </summary>
        private bool TryFindColumnGround(
            int voxelX,
            int originY,
            int voxelZ,
            out Vector3Int found)
        {
            int range = profile.WanderVerticalRange;
            for (int offset = 0; offset <= range; offset++)
            {
                for (int sign = 1; sign >= -1; sign -= 2)
                {
                    var candidate = new Vector3Int(
                        voxelX,
                        originY + offset * sign,
                        voxelZ);
                    if (nodeMaker.TryClassify(candidate, out PathNodeType type)
                        && type == PathNodeType.Walkable)
                    {
                        found = candidate;
                        return true;
                    }

                    if (offset == 0)
                    {
                        break;
                    }
                }
            }

            found = default;
            return false;
        }

        /// <summary>
        /// Advances the path against the creature's measured foot position and
        /// reports where to steer next. <paramref name="riseInLayers"/> is the
        /// layer difference of the graph edge being traversed, taken from the path
        /// itself rather than from the sampled foot height: the interpolated
        /// terrain surface sits inside the supporting voxel, so sampling would
        /// straddle a rounding boundary and report phantom climbs on flat ground.
        /// </summary>
        public bool TryGetSteering(
            Vector3 footVoxelPosition,
            float arrivalTolerance,
            out Vector3Int nextNode,
            out int riseInLayers)
        {
            nextNode = default;
            riseInLayers = 0;
            if (!HasActivePath)
            {
                return false;
            }

            AdvanceReachedNodes(footVoxelPosition, arrivalTolerance);
            if (!path.TryGetNextNode(out nextNode))
            {
                return false;
            }

            int previousIndex = path.CurrentIndex - 1;
            int fromLayer = previousIndex >= 0
                ? path.Nodes[previousIndex].y
                : nextNode.y;
            riseInLayers = Mathf.Max(0, nextNode.y - fromLayer);
            return true;
        }

        /// <summary>
        /// Consumes nodes the creature has arrived at. Only horizontal distance
        /// counts, because vertical placement belongs to gravity and the collider,
        /// and the tolerance stays below one cell so a corner is never skipped.
        /// </summary>
        private void AdvanceReachedNodes(Vector3 footVoxelPosition, float tolerance)
        {
            while (path.TryGetNextNode(out Vector3Int node))
            {
                float deltaX = footVoxelPosition.x - node.x;
                float deltaZ = footVoxelPosition.z - node.z;
                if (deltaX * deltaX + deltaZ * deltaZ > tolerance * tolerance)
                {
                    return;
                }

                path.Advance();
            }
        }

        private float NextReplanDelay()
        {
            float minimum = profile.MinimumReplanInterval;
            float maximum = profile.MaximumReplanInterval;
            return minimum + (float)random.NextDouble() * (maximum - minimum);
        }
    }
}
