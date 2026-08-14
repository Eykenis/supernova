using System.Collections.Generic;
using Supernova.Voxels.Integrity;
using UnityEngine;

namespace Supernova.Voxels
{
    /// <summary>
    /// Stable target for either the generated terrain or one voxel lineage in a
    /// moving body. Dynamic addresses survive a background split.
    /// </summary>
    public readonly struct VoxelTargetReference
    {
        private readonly IVoxelTerrain terrain;
        private readonly DynamicVoxelBodyRegistry registry;
        private readonly DynamicVoxelBody directBody;
        private readonly DynamicVoxelAddress dynamicAddress;

        private VoxelTargetReference(
            IVoxelTerrain terrain,
            DynamicVoxelBodyRegistry registry,
            DynamicVoxelBody directBody,
            DynamicVoxelAddress dynamicAddress,
            Vector3Int coordinate,
            bool isDynamic)
        {
            this.terrain = terrain;
            this.registry = registry;
            this.directBody = directBody;
            this.dynamicAddress = dynamicAddress;
            Coordinate = coordinate;
            IsDynamic = isDynamic;
        }

        public Vector3Int Coordinate { get; }
        public bool IsDynamic { get; }

        internal static VoxelTargetReference Static(
            IVoxelTerrain terrain,
            Vector3Int coordinate)
        {
            return new VoxelTargetReference(
                terrain,
                null,
                null,
                default,
                coordinate,
                false);
        }

        internal static VoxelTargetReference Dynamic(
            DynamicVoxelBody body,
            DynamicVoxelAddress address)
        {
            return new VoxelTargetReference(
                null,
                body != null ? body.Registry : null,
                body,
                address,
                address.Coordinate,
                true);
        }

        public bool TryMineVoxel(out VoxelMiningResult result)
        {
            if (TryResolveDynamicBody(out DynamicVoxelBody body))
            {
                return body.TryMineVoxel(Coordinate, out result);
            }
            if (!IsDynamic && terrain != null)
            {
                return terrain.TryMineVoxel(Coordinate, out result);
            }
            result = default;
            return false;
        }

        public bool TryMineBrush(
            Vector3 worldDirection,
            VoxelMiningBrushSettings settings,
            out VoxelMiningBrushResult result)
        {
            if (TryResolveDynamicBody(out DynamicVoxelBody body))
            {
                return body.TryMineBrush(
                    Coordinate,
                    worldDirection,
                    settings,
                    out result);
            }
            if (!IsDynamic && terrain != null)
            {
                return terrain.TryMineBrush(
                    Coordinate,
                    worldDirection,
                    settings,
                    out result);
            }
            result = default;
            return false;
        }

        private bool TryResolveDynamicBody(out DynamicVoxelBody body)
        {
            body = null;
            if (!IsDynamic)
            {
                return false;
            }
            if (registry != null
                && registry.TryResolve(dynamicAddress, out body))
            {
                return true;
            }
            if (directBody != null
                && directBody.TryGetSample(Coordinate, out _))
            {
                body = directBody;
                return true;
            }
            return false;
        }
    }

    public readonly struct VoxelTargetHit
    {
        public VoxelTargetHit(
            VoxelTargetReference target,
            VoxelSample sample,
            Vector3 point,
            Vector3 normal,
            float distance)
        {
            Target = target;
            Sample = sample;
            Point = point;
            Normal = normal;
            Distance = distance;
        }

        public VoxelTargetReference Target { get; }
        public Vector3Int Coordinate => Target.Coordinate;
        public VoxelSample Sample { get; }
        public Vector3 Point { get; }
        public Vector3 Normal { get; }
        public float Distance { get; }
        public bool IsDynamic => Target.IsDynamic;
    }

    /// <summary>
    /// Shared crosshair query for static terrain and detached voxel worlds.
    /// Physics supplies broadphase candidates; moving bodies then use their
    /// immutable MC triangle BVH for the exact visible hit.
    /// </summary>
    public static class VoxelTargetResolver
    {
        private static readonly Vector3Int[] CellCornerOffsets =
        {
            new Vector3Int(0, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(1, 1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(1, 0, 1),
            new Vector3Int(0, 1, 1),
            new Vector3Int(1, 1, 1),
        };

        public static bool TryRaycast(
            Ray ray,
            float maxDistance,
            int layerMask,
            IVoxelTerrain staticTerrain,
            out VoxelTargetHit targetHit)
        {
            targetHit = default;
            float bestDistance = float.PositiveInfinity;
            bool found = false;
            var checkedBodies = new HashSet<DynamicVoxelBody>();
            DynamicVoxelBodyRegistry bodyRegistry =
                staticTerrain is VoxelIntegrityWorldBridge bridge
                    ? bridge.DynamicBodyRegistry
                    : null;
            if (bodyRegistry != null
                && bodyRegistry.TryRaycastExact(
                    ray,
                    maxDistance,
                    out DynamicVoxelBody registryBody,
                    out DynamicVoxelRaycastHit registryHit))
            {
                checkedBodies.Add(registryBody);
                bestDistance = registryHit.Distance;
                targetHit = new VoxelTargetHit(
                    VoxelTargetReference.Dynamic(
                        registryBody,
                        registryHit.Address),
                    registryHit.Sample,
                    registryHit.Point,
                    registryHit.Normal,
                    registryHit.Distance);
                found = true;
            }

            RaycastHit[] physicsHits = Physics.RaycastAll(
                ray,
                maxDistance,
                layerMask,
                QueryTriggerInteraction.Ignore);
            if (physicsHits.Length == 0)
            {
                return found;
            }

            Transform terrainTransform = staticTerrain != null
                ? staticTerrain.TerrainTransform
                : null;
            for (int i = 0; i < physicsHits.Length; i++)
            {
                RaycastHit physicsHit = physicsHits[i];
                DynamicVoxelBody dynamicBody = physicsHit.collider
                    .GetComponentInParent<DynamicVoxelBody>();
                if (dynamicBody != null && checkedBodies.Add(dynamicBody))
                {
                    if (dynamicBody.TryRaycastExact(
                        ray,
                        maxDistance,
                        out DynamicVoxelRaycastHit dynamicHit)
                        && dynamicHit.Distance < bestDistance)
                    {
                        bestDistance = dynamicHit.Distance;
                        targetHit = new VoxelTargetHit(
                            VoxelTargetReference.Dynamic(
                                dynamicBody,
                                dynamicHit.Address),
                            dynamicHit.Sample,
                            dynamicHit.Point,
                            dynamicHit.Normal,
                            dynamicHit.Distance);
                        found = true;
                    }
                }

                if (staticTerrain == null
                    || staticTerrain.World == null
                    || terrainTransform == null
                    || (physicsHit.transform != terrainTransform
                        && !physicsHit.transform.IsChildOf(terrainTransform))
                    || physicsHit.distance >= bestDistance)
                {
                    continue;
                }

                if (TryResolveStaticSurfaceSample(
                    physicsHit.point + ray.direction.normalized
                        * staticTerrain.VoxelSize * 0.05f,
                    physicsHit.point,
                    ray.origin,
                    ray.direction.normalized,
                    staticTerrain,
                    out Vector3Int coordinate,
                    out VoxelSample sample))
                {
                    bestDistance = physicsHit.distance;
                    targetHit = new VoxelTargetHit(
                        VoxelTargetReference.Static(
                            staticTerrain,
                            coordinate),
                        sample,
                        physicsHit.point,
                        physicsHit.normal,
                        physicsHit.distance);
                    found = true;
                }
            }

            return found;
        }

        private static bool TryResolveStaticSurfaceSample(
            Vector3 pointOnSolidSide,
            Vector3 surfacePoint,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            IVoxelTerrain terrain,
            out Vector3Int coordinate,
            out VoxelSample resolvedSample)
        {
            coordinate = default;
            resolvedSample = default;
            Transform terrainTransform = terrain.TerrainTransform;
            float voxelSize = terrain.VoxelSize;
            Vector3 samplePosition = terrainTransform.InverseTransformPoint(
                pointOnSolidSide) / voxelSize;
            var cellOrigin = new Vector3Int(
                Mathf.FloorToInt(samplePosition.x),
                Mathf.FloorToInt(samplePosition.y),
                Mathf.FloorToInt(samplePosition.z));
            bool found = false;
            float bestSurfaceDistance = float.PositiveInfinity;
            float bestRayDepth = float.PositiveInfinity;
            float tieTolerance = voxelSize * voxelSize * 0.0001f;
            InfiniteVoxelWorld world = terrain.World;
            for (int i = 0; i < CellCornerOffsets.Length; i++)
            {
                Vector3Int candidate = cellOrigin + CellCornerOffsets[i];
                if (!world.TryGetSample(
                    candidate.x,
                    candidate.y,
                    candidate.z,
                    out VoxelSample sample)
                    || !sample.IsSolid(terrain.IsoLevel))
                {
                    continue;
                }

                Vector3 candidateWorld = terrainTransform.TransformPoint(
                    (Vector3)candidate * voxelSize);
                float surfaceDistance =
                    (candidateWorld - surfacePoint).sqrMagnitude;
                float rayDepth = Vector3.Dot(
                    candidateWorld - rayOrigin,
                    rayDirection);
                if (surfaceDistance < bestSurfaceDistance - tieTolerance
                    || (Mathf.Abs(surfaceDistance - bestSurfaceDistance)
                        <= tieTolerance
                        && rayDepth < bestRayDepth))
                {
                    coordinate = candidate;
                    resolvedSample = sample;
                    bestSurfaceDistance = surfaceDistance;
                    bestRayDepth = rayDepth;
                    found = true;
                }
            }
            return found;
        }
    }
}
