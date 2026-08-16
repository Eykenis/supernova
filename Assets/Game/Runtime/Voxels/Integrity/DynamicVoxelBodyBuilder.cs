using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Supernova.Voxels.Integrity
{
    public sealed class DynamicVoxelComponentBuildData
    {
        public DynamicVoxelComponentBuildData(
            List<Vector3Int> coordinates,
            Dictionary<Vector3Int, VoxelSample> samples,
            VoxelMeshData meshData,
            VoxelMeshMassProperties massProperties,
            Vector3 pivot,
            float mass,
            List<VoxelConvexColliderMeshData> convexColliderMeshes,
            VoxelMeshRaycastBvh raycastBvh)
        {
            Coordinates = coordinates;
            Samples = samples;
            MeshData = meshData;
            MassProperties = massProperties;
            Pivot = pivot;
            Mass = mass;
            ConvexColliderMeshes = convexColliderMeshes;
            RaycastBvh = raycastBvh;
        }

        public List<Vector3Int> Coordinates { get; }
        public Dictionary<Vector3Int, VoxelSample> Samples { get; }
        public VoxelMeshData MeshData { get; }
        public VoxelMeshMassProperties MassProperties { get; }
        public Vector3 Pivot { get; }
        public float Mass { get; }
        public List<VoxelConvexColliderMeshData> ConvexColliderMeshes
        {
            get;
        }
        public VoxelMeshRaycastBvh RaycastBvh { get; }
    }

    public sealed class DynamicVoxelBodyBuildResult
    {
        public DynamicVoxelBodyBuildResult(
            int revision,
            List<DynamicVoxelComponentBuildData> components,
            int fillCount,
            int visitedVoxelCount,
            float buildMilliseconds,
            Exception error)
        {
            Revision = revision;
            Components = components;
            FillCount = fillCount;
            VisitedVoxelCount = visitedVoxelCount;
            BuildMilliseconds = buildMilliseconds;
            Error = error;
        }

        public int Revision { get; }
        public List<DynamicVoxelComponentBuildData> Components { get; }
        public int FillCount { get; }
        public int VisitedVoxelCount { get; }
        public float BuildMilliseconds { get; }
        public Exception Error { get; }
    }

    /// <summary>
    /// Pure detached-body topology and geometry worker. One shared visited set
    /// discovers every six-connected component in a snapshot exactly once.
    /// </summary>
    public static class DynamicVoxelBodyBuilder
    {
        private static readonly Vector3Int[] FaceNeighbours =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1),
        };

        public static DynamicVoxelBodyBuildResult Build(
            int revision,
            Dictionary<Vector3Int, VoxelSample> snapshot,
            float isoLevel,
            float voxelSize,
            MarchingCubesVertexPlacement vertexPlacement,
            VoxelGroupMap groupMap,
            IReadOnlyDictionary<VoxelTypeId, float> massByType,
            float defaultMassPerFullVoxel,
            VoxelConvexDecompositionSettings? convexSettings = null,
            VoxelConvexDecompositionPriority decompositionPriority =
                VoxelConvexDecompositionPriority.Normal,
            VoxelConvexDecompositionQuality decompositionQuality =
                VoxelConvexDecompositionQuality.Production,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var occupied = new HashSet<Vector3Int>();
                foreach (KeyValuePair<Vector3Int, VoxelSample> pair in snapshot)
                {
                    if ((occupied.Count & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    if (pair.Value.IsSolid(isoLevel))
                    {
                        occupied.Add(pair.Key);
                    }
                }

                var components = new List<DynamicVoxelComponentBuildData>();
                var visited = new HashSet<Vector3Int>();
                var pending = new Queue<Vector3Int>();
                var orderedSeeds = new List<Vector3Int>(occupied);
                orderedSeeds.Sort(CompareCoordinates);
                for (int seedIndex = 0;
                    seedIndex < orderedSeeds.Count;
                    seedIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Vector3Int seed = orderedSeeds[seedIndex];
                    if (!visited.Add(seed))
                    {
                        continue;
                    }

                    var coordinates = new List<Vector3Int>();
                    pending.Enqueue(seed);
                    while (pending.Count > 0)
                    {
                        Vector3Int coordinate = pending.Dequeue();
                        coordinates.Add(coordinate);
                        if ((coordinates.Count & 255) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        for (int neighbourIndex = 0;
                            neighbourIndex < FaceNeighbours.Length;
                            neighbourIndex++)
                        {
                            Vector3Int neighbour =
                                coordinate + FaceNeighbours[neighbourIndex];
                            if (occupied.Contains(neighbour)
                                && visited.Add(neighbour))
                            {
                                pending.Enqueue(neighbour);
                            }
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    coordinates.Sort(CompareCoordinates);
                    var componentSet = new HashSet<Vector3Int>(coordinates);
                    var componentSamples =
                        new Dictionary<Vector3Int, VoxelSample>(
                            coordinates.Count);
                    for (int i = 0; i < coordinates.Count; i++)
                    {
                        Vector3Int coordinate = coordinates[i];
                        componentSamples.Add(coordinate, snapshot[coordinate]);
                    }

                    VoxelMeshData meshData =
                        MarchingCubesMesher.BuildCapturedComponent(
                            componentSet,
                            componentSamples,
                            isoLevel,
                            voxelSize,
                            vertexPlacement,
                            groupMap);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (meshData.Vertices.Count == 0
                        || meshData.Triangles.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "A detached voxel component produced no surface.");
                    }

                    VoxelMeshMassProperties properties =
                        VoxelIntegrityRigidbodyFactory
                            .CalculateMassProperties(
                                meshData.Vertices,
                                meshData.Triangles);
                    Vector3 pivot = properties.Volume > 0.000001f
                        ? properties.Centroid
                        : CalculateGridPivot(coordinates) * voxelSize;
                    float averageMassPerFullVoxel = ResolveAverageMass(
                        componentSamples,
                        massByType,
                        defaultMassPerFullVoxel);
                    float representedFullVoxelVolume =
                        VoxelIntegrityRigidbodyFactory
                            .CalculateRepresentedFullVoxelVolume(
                                properties,
                                voxelSize,
                                Vector3.one);
                    float mass = Mathf.Max(
                        0.01f,
                        representedFullVoxelVolume
                            * averageMassPerFullVoxel);
                    List<VoxelConvexColliderMeshData> colliderMeshes =
                        VoxelConvexDecomposer.Decompose(
                            meshData.Vertices,
                            meshData.Triangles,
                            pivot,
                            convexSettings
                                ?? VoxelConvexDecompositionSettings.Default,
                            decompositionPriority,
                            decompositionQuality,
                            cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    var bvh = new VoxelMeshRaycastBvh(
                        meshData.Vertices,
                        meshData.Triangles,
                        pivot);
                    components.Add(new DynamicVoxelComponentBuildData(
                        coordinates,
                        componentSamples,
                        meshData,
                        properties,
                        pivot,
                        mass,
                        colliderMeshes,
                        bvh));
                }

                components.Sort((a, b) =>
                    b.Coordinates.Count.CompareTo(a.Coordinates.Count));
                stopwatch.Stop();
                return new DynamicVoxelBodyBuildResult(
                    revision,
                    components,
                    components.Count,
                    visited.Count,
                    (float)stopwatch.Elapsed.TotalMilliseconds,
                    null);
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                return new DynamicVoxelBodyBuildResult(
                    revision,
                    new List<DynamicVoxelComponentBuildData>(),
                    0,
                    0,
                    (float)stopwatch.Elapsed.TotalMilliseconds,
                    exception);
            }
        }

        private static float ResolveAverageMass(
            Dictionary<Vector3Int, VoxelSample> samples,
            IReadOnlyDictionary<VoxelTypeId, float> massByType,
            float defaultMassPerFullVoxel)
        {
            float total = 0f;
            int count = 0;
            foreach (VoxelSample sample in samples.Values)
            {
                if (massByType != null
                    && massByType.TryGetValue(sample.Type, out float mass))
                {
                    total += mass;
                }
                else
                {
                    total += defaultMassPerFullVoxel;
                }
                count++;
            }
            return count > 0
                ? total / count
                : defaultMassPerFullVoxel;
        }

        private static Vector3 CalculateGridPivot(
            IReadOnlyList<Vector3Int> coordinates)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < coordinates.Count; i++)
            {
                sum += (Vector3)coordinates[i];
            }
            return coordinates.Count > 0
                ? sum / coordinates.Count
                : Vector3.zero;
        }

        private static int CompareCoordinates(Vector3Int a, Vector3Int b)
        {
            int z = a.z.CompareTo(b.z);
            if (z != 0) return z;
            int y = a.y.CompareTo(b.y);
            return y != 0 ? y : a.x.CompareTo(b.x);
        }
    }
}
