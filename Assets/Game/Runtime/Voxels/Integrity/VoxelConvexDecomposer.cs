using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.Voxels.Integrity
{
    public enum VoxelConvexDecompositionPriority
    {
        Normal,
        Interactive,
    }

    public enum VoxelConvexDecompositionQuality
    {
        Production,
        Interactive,
    }

    public readonly struct VoxelConvexDecompositionSettings
    {
        public static readonly VoxelConvexDecompositionSettings Default =
            new VoxelConvexDecompositionSettings(0.1f, 8);

        public VoxelConvexDecompositionSettings(
            float concavityThreshold,
            int maxConvexHulls)
        {
            ConcavityThreshold = Mathf.Clamp(
                concavityThreshold,
                0.01f,
                1f);
            MaxConvexHulls = Mathf.Clamp(maxConvexHulls, 1, 64);
        }

        public float ConcavityThreshold { get; }
        public int MaxConvexHulls { get; }
    }

    /// <summary>Pure managed data for one convex PhysX collider mesh.</summary>
    public sealed class VoxelConvexColliderMeshData
    {
        public VoxelConvexColliderMeshData(
            Vector3[] vertices,
            int[] triangles)
        {
            Vertices = vertices
                ?? throw new ArgumentNullException(nameof(vertices));
            Triangles = triangles
                ?? throw new ArgumentNullException(nameof(triangles));
        }

        public Vector3[] Vertices { get; }
        public int[] Triangles { get; }
        public int TriangleCount => Triangles.Length / 3;

        public Mesh CreateMesh(string meshName)
        {
            var mesh = new Mesh
            {
                name = meshName,
                hideFlags = HideFlags.DontSave,
                indexFormat = Vertices.Length > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16,
            };
            mesh.SetVertices(Vertices);
            mesh.SetTriangles(Triangles, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// Background-safe collider decomposition. Production builds use the pinned
    /// CoACD package, while interactive edits create a bounded compound directly
    /// from spatial clusters of the rendered surface. Workers never touch Unity
    /// objects.
    /// </summary>
    public static class VoxelConvexDecomposer
    {
        private const int UnityConvexTriangleLimit = 255;
        private const int MaximumHullInputVertices = 64;
        private const int PreprocessOff = 2;
        private const int PreprocessResolution = 50;
        private const int SampleResolution = 1000;
        private const int MctsNodes = 10;
        private const int MctsIterations = 60;
        private const int MctsMaximumDepth = 2;
        private static readonly object NativeRunGate = new object();
        private static bool nativeRunInUse;
        private static int waitingInteractiveRuns;
        private static bool logLevelConfigured;

        public static List<VoxelConvexColliderMeshData> Decompose(
            IReadOnlyList<Vector3> sourceVertices,
            IReadOnlyList<int> sourceTriangles,
            Vector3 pivot,
            VoxelConvexDecompositionSettings settings,
            VoxelConvexDecompositionPriority priority =
                VoxelConvexDecompositionPriority.Normal,
            VoxelConvexDecompositionQuality quality =
                VoxelConvexDecompositionQuality.Production,
            CancellationToken cancellationToken = default)
        {
            if (sourceVertices == null)
            {
                throw new ArgumentNullException(nameof(sourceVertices));
            }
            if (sourceTriangles == null)
            {
                throw new ArgumentNullException(nameof(sourceTriangles));
            }
            if (sourceVertices.Count < 4
                || sourceTriangles.Count < 12
                || sourceTriangles.Count % 3 != 0)
            {
                throw new ArgumentException(
                    "Convex decomposition requires a closed triangle mesh.");
            }
            if (quality == VoxelConvexDecompositionQuality.Interactive)
            {
                return DecomposeInteractiveSurface(
                    sourceVertices,
                    sourceTriangles,
                    pivot,
                    settings,
                    cancellationToken);
            }

            var vertices = new double[sourceVertices.Count * 3];
            for (int i = 0; i < sourceVertices.Count; i++)
            {
                Vector3 vertex = sourceVertices[i] - pivot;
                int target = i * 3;
                vertices[target] = vertex.x;
                vertices[target + 1] = vertex.y;
                vertices[target + 2] = vertex.z;
            }
            var triangles = new int[sourceTriangles.Count];
            for (int i = 0; i < triangles.Length; i++)
            {
                triangles[i] = sourceTriangles[i];
            }

            GCHandle vertexHandle = default;
            GCHandle triangleHandle = default;
            try
            {
                vertexHandle = GCHandle.Alloc(
                    vertices,
                    GCHandleType.Pinned);
                triangleHandle = GCHandle.Alloc(
                    triangles,
                    GCHandleType.Pinned);
                var input = new NativeMesh
                {
                    Vertices = vertexHandle.AddrOfPinnedObject(),
                    VertexCount = (ulong)sourceVertices.Count,
                    Triangles = triangleHandle.AddrOfPinnedObject(),
                    TriangleCount = (ulong)(sourceTriangles.Count / 3),
                };

                cancellationToken.ThrowIfCancellationRequested();
                EnterNativeRun(priority, cancellationToken);
                try
                {
                    if (!logLevelConfigured)
                    {
                        SetLogLevel("off");
                        logLevelConfigured = true;
                    }
                    NativeMeshArray output = Run(
                        ref input,
                        settings.ConcavityThreshold,
                        settings.MaxConvexHulls,
                        PreprocessOff,
                        PreprocessResolution,
                        SampleResolution,
                        MctsNodes,
                        MctsIterations,
                        MctsMaximumDepth,
                        false,
                        true,
                        0u);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return CopyOutput(output);
                    }
                    finally
                    {
                        if (output.Meshes != IntPtr.Zero)
                        {
                            Free(output);
                        }
                    }
                }
                finally
                {
                    ExitNativeRun();
                }
            }
            finally
            {
                if (triangleHandle.IsAllocated)
                {
                    triangleHandle.Free();
                }
                if (vertexHandle.IsAllocated)
                {
                    vertexHandle.Free();
                }
            }
        }

        private static List<VoxelConvexColliderMeshData>
            DecomposeInteractiveSurface(
                IReadOnlyList<Vector3> sourceVertices,
                IReadOnlyList<int> sourceTriangles,
                Vector3 pivot,
                VoxelConvexDecompositionSettings settings,
                CancellationToken cancellationToken)
        {
            int triangleCount = sourceTriangles.Count / 3;
            int targetHullCount = Mathf.Clamp(
                (triangleCount + 15) / 16,
                1,
                settings.MaxConvexHulls);
            var root = new List<int>(triangleCount);
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                root.Add(triangle);
            }
            var clusters = new List<List<int>> { root };
            while (clusters.Count < targetHullCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool splitCompleted = false;
                var attempted = new HashSet<int>();
                while (attempted.Count < clusters.Count)
                {
                    int bestIndex = -1;
                    int bestTriangleCount = -1;
                    for (int i = 0; i < clusters.Count; i++)
                    {
                        if (!attempted.Contains(i)
                            && clusters[i].Count > bestTriangleCount)
                        {
                            bestIndex = i;
                            bestTriangleCount = clusters[i].Count;
                        }
                    }
                    if (bestIndex < 0)
                    {
                        break;
                    }
                    attempted.Add(bestIndex);
                    if (!TrySplitTriangleCluster(
                        clusters[bestIndex],
                        sourceVertices,
                        sourceTriangles,
                        out List<int> first,
                        out List<int> second))
                    {
                        continue;
                    }

                    clusters[bestIndex] = first;
                    clusters.Add(second);
                    splitCompleted = true;
                    break;
                }
                if (!splitCompleted)
                {
                    break;
                }
            }

            var result = new List<VoxelConvexColliderMeshData>(
                clusters.Count);
            for (int i = 0; i < clusters.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Add(BuildTriangleClusterMesh(
                    clusters[i],
                    sourceVertices,
                    sourceTriangles,
                    pivot));
            }
            return result;
        }

        private static bool TrySplitTriangleCluster(
            List<int> cluster,
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<int> triangles,
            out List<int> first,
            out List<int> second)
        {
            first = null;
            second = null;
            if (cluster.Count < 8)
            {
                return false;
            }

            Vector3 minimum = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            Vector3 maximum = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            for (int i = 0; i < cluster.Count; i++)
            {
                Vector3 centroid = GetTriangleCentroid(
                    cluster[i],
                    vertices,
                    triangles);
                minimum = Vector3.Min(minimum, centroid);
                maximum = Vector3.Max(maximum, centroid);
            }
            Vector3 size = maximum - minimum;
            int[] axes = { 0, 1, 2 };
            for (int i = 0; i < axes.Length - 1; i++)
            {
                for (int j = i + 1; j < axes.Length; j++)
                {
                    if (size[axes[j]] > size[axes[i]])
                    {
                        int swap = axes[i];
                        axes[i] = axes[j];
                        axes[j] = swap;
                    }
                }
            }

            for (int axisIndex = 0;
                axisIndex < axes.Length;
                axisIndex++)
            {
                int axis = axes[axisIndex];
                var ordered = new List<int>(cluster);
                ordered.Sort((a, b) => GetTriangleCentroid(
                        a,
                        vertices,
                        triangles)[axis]
                    .CompareTo(GetTriangleCentroid(
                        b,
                        vertices,
                        triangles)[axis]));
                int splitIndex = ordered.Count / 2;
                var candidateFirst = ordered.GetRange(0, splitIndex);
                var candidateSecond = ordered.GetRange(
                    splitIndex,
                    ordered.Count - splitIndex);
                if (!HasVolumeSupport(
                        candidateFirst,
                        vertices,
                        triangles)
                    || !HasVolumeSupport(
                        candidateSecond,
                        vertices,
                        triangles))
                {
                    continue;
                }

                first = candidateFirst;
                second = candidateSecond;
                return true;
            }
            return false;
        }

        private static bool HasVolumeSupport(
            List<int> cluster,
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<int> triangles)
        {
            Vector3 minimum = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            Vector3 maximum = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            var unique = new HashSet<int>();
            for (int i = 0; i < cluster.Count; i++)
            {
                int triangleStart = cluster[i] * 3;
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertexIndex = triangles[triangleStart + corner];
                    if (!unique.Add(vertexIndex))
                    {
                        continue;
                    }
                    Vector3 vertex = vertices[vertexIndex];
                    minimum = Vector3.Min(minimum, vertex);
                    maximum = Vector3.Max(maximum, vertex);
                }
            }
            Vector3 size = maximum - minimum;
            return unique.Count >= 4
                && size.x > 0.0001f
                && size.y > 0.0001f
                && size.z > 0.0001f;
        }

        private static VoxelConvexColliderMeshData BuildTriangleClusterMesh(
            List<int> cluster,
            IReadOnlyList<Vector3> sourceVertices,
            IReadOnlyList<int> sourceTriangles,
            Vector3 pivot)
        {
            var indexMap = new Dictionary<int, int>();
            var vertices = new List<Vector3>();
            var triangles = new List<int>(cluster.Count * 3);
            for (int i = 0; i < cluster.Count; i++)
            {
                int triangleStart = cluster[i] * 3;
                for (int corner = 0; corner < 3; corner++)
                {
                    int sourceIndex = sourceTriangles[triangleStart + corner];
                    if (!indexMap.TryGetValue(sourceIndex, out int localIndex))
                    {
                        localIndex = vertices.Count;
                        indexMap.Add(sourceIndex, localIndex);
                        vertices.Add(sourceVertices[sourceIndex] - pivot);
                    }
                    triangles.Add(localIndex);
                }
            }

            Vector3[] colliderVertices = vertices.ToArray();
            int[] colliderTriangles = triangles.ToArray();
            if (colliderVertices.Length > MaximumHullInputVertices
                || colliderTriangles.Length / 3
                    > UnityConvexTriangleLimit)
            {
                colliderVertices = SelectHullSupportPoints(
                    colliderVertices,
                    MaximumHullInputVertices);
                colliderTriangles = BuildConvexPointCloudTriangles(
                    colliderVertices.Length);
            }
            return new VoxelConvexColliderMeshData(
                colliderVertices,
                colliderTriangles);
        }

        private static Vector3 GetTriangleCentroid(
            int triangleIndex,
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<int> triangles)
        {
            int start = triangleIndex * 3;
            return (vertices[triangles[start]]
                + vertices[triangles[start + 1]]
                + vertices[triangles[start + 2]]) / 3f;
        }

        private static void EnterNativeRun(
            VoxelConvexDecompositionPriority priority,
            CancellationToken cancellationToken)
        {
            bool interactive =
                priority == VoxelConvexDecompositionPriority.Interactive;
            lock (NativeRunGate)
            {
                if (interactive)
                {
                    waitingInteractiveRuns++;
                }
                try
                {
                    while (nativeRunInUse
                        || (!interactive && waitingInteractiveRuns > 0))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Monitor.Wait(NativeRunGate, 50);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    nativeRunInUse = true;
                }
                finally
                {
                    if (interactive)
                    {
                        waitingInteractiveRuns--;
                    }
                }
            }
        }

        private static void ExitNativeRun()
        {
            lock (NativeRunGate)
            {
                nativeRunInUse = false;
                Monitor.PulseAll(NativeRunGate);
            }
        }

        private static List<VoxelConvexColliderMeshData> CopyOutput(
            NativeMeshArray output)
        {
            if (output.Meshes == IntPtr.Zero || output.MeshCount == 0)
            {
                throw new InvalidOperationException(
                    "CoACD returned no convex collider meshes.");
            }
            if (output.MeshCount > 64)
            {
                throw new InvalidOperationException(
                    $"CoACD returned an unsafe collider count: "
                    + $"{output.MeshCount}.");
            }

            int nativeMeshSize = Marshal.SizeOf<NativeMesh>();
            var meshes = new List<VoxelConvexColliderMeshData>(
                (int)output.MeshCount);
            for (ulong meshIndex = 0;
                meshIndex < output.MeshCount;
                meshIndex++)
            {
                IntPtr meshPointer = IntPtr.Add(
                    output.Meshes,
                    checked((int)meshIndex * nativeMeshSize));
                NativeMesh native =
                    Marshal.PtrToStructure<NativeMesh>(meshPointer);
                int vertexCount = checked((int)native.VertexCount);
                int triangleCount = checked((int)native.TriangleCount);
                if (vertexCount < 4 || triangleCount < 4)
                {
                    throw new InvalidOperationException(
                        "CoACD returned a zero-volume convex hull.");
                }
                var nativeVertices = new double[vertexCount * 3];
                var hullTriangles = new int[triangleCount * 3];
                Marshal.Copy(
                    native.Vertices,
                    nativeVertices,
                    0,
                    nativeVertices.Length);
                Marshal.Copy(
                    native.Triangles,
                    hullTriangles,
                    0,
                    hullTriangles.Length);
                var hullVertices = new Vector3[vertexCount];
                for (int vertexIndex = 0;
                    vertexIndex < vertexCount;
                    vertexIndex++)
                {
                    int source = vertexIndex * 3;
                    hullVertices[vertexIndex] = new Vector3(
                        (float)nativeVertices[source],
                        (float)nativeVertices[source + 1],
                        (float)nativeVertices[source + 2]);
                }
                if (triangleCount > UnityConvexTriangleLimit
                    || vertexCount > MaximumHullInputVertices)
                {
                    hullVertices = SelectHullSupportPoints(
                        hullVertices,
                        MaximumHullInputVertices);
                    hullTriangles = BuildConvexPointCloudTriangles(
                        hullVertices.Length);
                }
                meshes.Add(new VoxelConvexColliderMeshData(
                    hullVertices,
                    hullTriangles));
            }
            return meshes;
        }

        private static Vector3[] SelectHullSupportPoints(
            IReadOnlyList<Vector3> source,
            int maximumCount)
        {
            var unique = new List<Vector3>(source.Count);
            var seen = new HashSet<Vector3>();
            for (int i = 0; i < source.Count; i++)
            {
                if (seen.Add(source[i]))
                {
                    unique.Add(source[i]);
                }
            }
            if (unique.Count <= maximumCount)
            {
                return unique.ToArray();
            }

            var selected = new List<int>(maximumCount);
            var selectedSet = new HashSet<int>();
            for (int axis = 0; axis < 3; axis++)
            {
                int minimum = 0;
                int maximum = 0;
                for (int i = 1; i < unique.Count; i++)
                {
                    if (unique[i][axis] < unique[minimum][axis])
                    {
                        minimum = i;
                    }
                    if (unique[i][axis] > unique[maximum][axis])
                    {
                        maximum = i;
                    }
                }
                AddSelected(minimum, selected, selectedSet);
                AddSelected(maximum, selected, selectedSet);
            }

            while (selected.Count < maximumCount)
            {
                int bestIndex = -1;
                float bestMinimumDistance = -1f;
                for (int candidate = 0;
                    candidate < unique.Count;
                    candidate++)
                {
                    if (selectedSet.Contains(candidate))
                    {
                        continue;
                    }
                    float minimumDistance = float.PositiveInfinity;
                    for (int selectedIndex = 0;
                        selectedIndex < selected.Count;
                        selectedIndex++)
                    {
                        float distance = (
                            unique[candidate]
                            - unique[selected[selectedIndex]]).sqrMagnitude;
                        if (distance < minimumDistance)
                        {
                            minimumDistance = distance;
                        }
                    }
                    if (minimumDistance > bestMinimumDistance)
                    {
                        bestMinimumDistance = minimumDistance;
                        bestIndex = candidate;
                    }
                }
                if (bestIndex < 0)
                {
                    break;
                }
                AddSelected(bestIndex, selected, selectedSet);
            }

            var result = new Vector3[selected.Count];
            for (int i = 0; i < selected.Count; i++)
            {
                result[i] = unique[selected[i]];
            }
            return result;
        }

        private static void AddSelected(
            int index,
            List<int> selected,
            HashSet<int> selectedSet)
        {
            if (selectedSet.Add(index))
            {
                selected.Add(index);
            }
        }

        private static int[] BuildConvexPointCloudTriangles(int vertexCount)
        {
            if (vertexCount < 4)
            {
                throw new InvalidOperationException(
                    "A convex collider requires at least four points.");
            }
            var triangles = new int[(vertexCount - 2) * 3];
            for (int i = 0; i < vertexCount - 2; i++)
            {
                int target = i * 3;
                triangles[target] = 0;
                triangles[target + 1] = i + 1;
                triangles[target + 2] = i + 2;
            }
            return triangles;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMesh
        {
            public IntPtr Vertices;
            public ulong VertexCount;
            public IntPtr Triangles;
            public ulong TriangleCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMeshArray
        {
            public IntPtr Meshes;
            public ulong MeshCount;
        }

        [DllImport(
            "lib_coacd",
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "CoACD_setLogLevel")]
        private static extern void SetLogLevel(
            [MarshalAs(UnmanagedType.LPStr)] string level);

        [DllImport(
            "lib_coacd",
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "CoACD_freeMeshArray")]
        private static extern void Free(NativeMeshArray array);

        [DllImport(
            "lib_coacd",
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "CoACD_run")]
        private static extern NativeMeshArray Run(
            ref NativeMesh mesh,
            double threshold,
            int maxConvexHull,
            int preprocessMode,
            int preprocessResolution,
            int sampleResolution,
            int mctsNodes,
            int mctsIterations,
            int mctsMaximumDepth,
            bool pca,
            bool merge,
            uint seed);
    }
}
