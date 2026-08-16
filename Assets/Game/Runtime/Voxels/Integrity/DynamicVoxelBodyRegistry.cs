using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels.Integrity
{
    public readonly struct DynamicVoxelAddress : IEquatable<DynamicVoxelAddress>
    {
        public DynamicVoxelAddress(Guid lineageId, Vector3Int coordinate)
        {
            LineageId = lineageId;
            Coordinate = coordinate;
        }

        public Guid LineageId { get; }
        public Vector3Int Coordinate { get; }

        public bool Equals(DynamicVoxelAddress other)
        {
            return LineageId.Equals(other.LineageId)
                && Coordinate == other.Coordinate;
        }

        public override bool Equals(object obj)
        {
            return obj is DynamicVoxelAddress other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (LineageId.GetHashCode() * 397)
                    ^ Coordinate.GetHashCode();
            }
        }
    }

    /// <summary>
    /// Scene-local detached-world coordinator. It supplies stable voxel
    /// addresses across splits and caps worker/commit pressure for all bodies
    /// created by one integrity bridge.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DynamicVoxelBodyRegistry : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxConcurrentBuilds = 2;
        [SerializeField, Min(1)] private int maxCommitsPerFrame = 1;

        private readonly Dictionary<DynamicVoxelAddress, DynamicVoxelBody>
            bodyByVoxel =
                new Dictionary<DynamicVoxelAddress, DynamicVoxelBody>();
        private readonly Dictionary<DynamicVoxelBody, HashSet<DynamicVoxelAddress>>
            addressesByBody =
                new Dictionary<DynamicVoxelBody, HashSet<DynamicVoxelAddress>>();
        private readonly Queue<DynamicVoxelBody> queuedBodies =
            new Queue<DynamicVoxelBody>();
        private readonly HashSet<DynamicVoxelBody> queuedSet =
            new HashSet<DynamicVoxelBody>();
        private readonly List<DynamicVoxelBody> activeBodies =
            new List<DynamicVoxelBody>();

        public int RegisteredBodyCount => addressesByBody.Count;
        public int ActiveBuildCount => activeBodies.Count;
        public int QueuedBuildCount => queuedSet.Count;

        public void Configure(int concurrentBuilds, int commitsPerFrame)
        {
            maxConcurrentBuilds = Mathf.Max(1, concurrentBuilds);
            maxCommitsPerFrame = Mathf.Max(1, commitsPerFrame);
        }

        public bool TryResolve(
            DynamicVoxelAddress address,
            out DynamicVoxelBody body)
        {
            return bodyByVoxel.TryGetValue(address, out body)
                && body != null;
        }

        public bool TryRaycastExact(
            Ray ray,
            float maxDistance,
            out DynamicVoxelBody hitBody,
            out DynamicVoxelRaycastHit hit)
        {
            hitBody = null;
            hit = default;
            float bestDistance = maxDistance;
            foreach (DynamicVoxelBody body in addressesByBody.Keys)
            {
                if (body == null)
                {
                    continue;
                }
                MeshRenderer renderer = body.GetComponent<MeshRenderer>();
                float boundsDistance = 0f;
                if (renderer != null
                    && !renderer.bounds.IntersectRay(
                        ray,
                        out boundsDistance))
                {
                    continue;
                }
                if (renderer != null && boundsDistance > bestDistance)
                {
                    continue;
                }
                if (!body.TryRaycastExact(
                    ray,
                    bestDistance,
                    out DynamicVoxelRaycastHit candidate))
                {
                    continue;
                }
                hitBody = body;
                hit = candidate;
                bestDistance = candidate.Distance;
            }
            return hitBody != null;
        }

        public bool TryMineExplosion(
            Vector3 worldCenter,
            VoxelExplosionSettings settings,
            out VoxelExplosionResult result)
        {
            var bodies = new List<DynamicVoxelBody>(addressesByBody.Keys);
            int candidateCount = 0;
            int damagedCount = 0;
            int destroyedCount = 0;
            for (int i = 0; i < bodies.Count; i++)
            {
                DynamicVoxelBody body = bodies[i];
                if (body == null)
                {
                    continue;
                }
                body.TryMineExplosion(
                    worldCenter,
                    settings,
                    out VoxelExplosionResult bodyResult);

                candidateCount += bodyResult.CandidateCount;
                damagedCount += bodyResult.DamagedCount;
                destroyedCount += bodyResult.DestroyedCount;
            }

            result = new VoxelExplosionResult(
                worldCenter,
                candidateCount,
                damagedCount,
                destroyedCount);
            return damagedCount > 0;
        }

        internal void RegisterBody(DynamicVoxelBody body)
        {
            if (body == null)
            {
                return;
            }
            RemoveMappings(body);
            AddMappings(body);
        }

        internal void RefreshMappings(DynamicVoxelBody body)
        {
            RemoveMappings(body);
            AddMappings(body);
        }

        internal void RemoveCoordinate(
            DynamicVoxelBody body,
            DynamicVoxelAddress address)
        {
            if (bodyByVoxel.TryGetValue(address, out DynamicVoxelBody owner)
                && owner == body)
            {
                bodyByVoxel.Remove(address);
            }
            if (addressesByBody.TryGetValue(
                body,
                out HashSet<DynamicVoxelAddress> addresses))
            {
                addresses.Remove(address);
            }
        }

        internal void UnregisterBody(DynamicVoxelBody body)
        {
            if (body == null)
            {
                return;
            }
            RemoveMappings(body);
            queuedSet.Remove(body);
        }

        internal void QueueRebuild(DynamicVoxelBody body)
        {
            if (body == null || !body.isActiveAndEnabled)
            {
                return;
            }
            if (queuedSet.Add(body))
            {
                queuedBodies.Enqueue(body);
            }
        }

        private void Update()
        {
            int commitsRemaining = Mathf.Max(1, maxCommitsPerFrame);
            for (int i = activeBodies.Count - 1;
                i >= 0 && commitsRemaining > 0;
                i--)
            {
                DynamicVoxelBody body = activeBodies[i];
                if (body == null)
                {
                    activeBodies.RemoveAt(i);
                    continue;
                }
                if (!body.TryCommitCompletedRebuild())
                {
                    continue;
                }

                activeBodies.RemoveAt(i);
                commitsRemaining--;
            }

            int concurrency = Mathf.Max(1, maxConcurrentBuilds);
            int startAttempts = queuedBodies.Count;
            while (activeBodies.Count < concurrency
                && queuedBodies.Count > 0
                && startAttempts-- > 0)
            {
                DynamicVoxelBody body = queuedBodies.Dequeue();
                if (body == null || !queuedSet.Contains(body)
                    || !body.isActiveAndEnabled)
                {
                    queuedSet.Remove(body);
                    continue;
                }
                if (activeBodies.Contains(body))
                {
                    queuedBodies.Enqueue(body);
                    continue;
                }

                queuedSet.Remove(body);
                if (!body.StartPendingRebuild())
                {
                    continue;
                }
                activeBodies.Add(body);
            }
        }

        private void AddMappings(DynamicVoxelBody body)
        {
            var addresses = new HashSet<DynamicVoxelAddress>();
            foreach (Vector3Int coordinate in body.Coordinates)
            {
                var address = new DynamicVoxelAddress(
                    body.LineageId,
                    coordinate);
                bodyByVoxel[address] = body;
                addresses.Add(address);
            }
            addressesByBody[body] = addresses;
        }

        private void RemoveMappings(DynamicVoxelBody body)
        {
            if (!addressesByBody.TryGetValue(
                body,
                out HashSet<DynamicVoxelAddress> addresses))
            {
                return;
            }

            foreach (DynamicVoxelAddress address in addresses)
            {
                if (bodyByVoxel.TryGetValue(
                    address,
                    out DynamicVoxelBody owner)
                    && owner == body)
                {
                    bodyByVoxel.Remove(address);
                }
            }
            addressesByBody.Remove(body);
        }
    }
}
