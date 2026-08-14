using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels.Integrity
{
    /// <summary>
    /// Immutable triangle BVH built from a detached body's visible MC surface.
    /// It filters the deliberately blocky compound physics broadphase so aiming
    /// follows the rendered shape rather than invisible cube faces.
    /// </summary>
    public sealed class VoxelMeshRaycastBvh
    {
        private const int LeafTriangleCount = 8;
        private const float DirectionEpsilon = 0.0000001f;

        private readonly Vector3[] vertices;
        private readonly int[] triangles;
        private readonly int[] triangleOrder;
        private readonly List<Node> nodes = new List<Node>();

        public VoxelMeshRaycastBvh(
            IReadOnlyList<Vector3> sourceVertices,
            IReadOnlyList<int> sourceTriangles,
            Vector3 pivot)
        {
            if (sourceVertices == null)
            {
                throw new ArgumentNullException(nameof(sourceVertices));
            }
            if (sourceTriangles == null)
            {
                throw new ArgumentNullException(nameof(sourceTriangles));
            }
            if (sourceTriangles.Count % 3 != 0)
            {
                throw new ArgumentException(
                    "Triangle indices must be a multiple of three.",
                    nameof(sourceTriangles));
            }

            vertices = new Vector3[sourceVertices.Count];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = sourceVertices[i] - pivot;
            }

            triangles = new int[sourceTriangles.Count];
            for (int i = 0; i < triangles.Length; i++)
            {
                triangles[i] = sourceTriangles[i];
            }

            triangleOrder = new int[triangles.Length / 3];
            for (int i = 0; i < triangleOrder.Length; i++)
            {
                triangleOrder[i] = i;
            }

            if (triangleOrder.Length > 0)
            {
                BuildNode(0, triangleOrder.Length);
            }
        }

        public int TriangleCount => triangleOrder.Length;

        public bool TryRaycast(
            Ray ray,
            float maxDistance,
            out float distance,
            out Vector3 normal)
        {
            distance = maxDistance;
            normal = default;
            if (nodes.Count == 0 || maxDistance < 0f)
            {
                return false;
            }

            bool hit = false;
            var pending = new Stack<int>();
            pending.Push(0);
            while (pending.Count > 0)
            {
                int nodeIndex = pending.Pop();
                Node node = nodes[nodeIndex];
                if (!IntersectsBounds(
                    ray,
                    node.Minimum,
                    node.Maximum,
                    distance,
                    out _))
                {
                    continue;
                }

                if (node.IsLeaf)
                {
                    for (int i = 0; i < node.Count; i++)
                    {
                        int triangle = triangleOrder[node.Start + i];
                        if (!TryIntersectTriangle(
                            ray,
                            triangle,
                            distance,
                            out float triangleDistance,
                            out Vector3 triangleNormal))
                        {
                            continue;
                        }

                        hit = true;
                        distance = triangleDistance;
                        normal = triangleNormal;
                    }
                    continue;
                }

                Node left = nodes[node.Left];
                Node right = nodes[node.Right];
                bool hitLeft = IntersectsBounds(
                    ray,
                    left.Minimum,
                    left.Maximum,
                    distance,
                    out float leftDistance);
                bool hitRight = IntersectsBounds(
                    ray,
                    right.Minimum,
                    right.Maximum,
                    distance,
                    out float rightDistance);
                if (hitLeft && hitRight)
                {
                    if (leftDistance <= rightDistance)
                    {
                        pending.Push(node.Right);
                        pending.Push(node.Left);
                    }
                    else
                    {
                        pending.Push(node.Left);
                        pending.Push(node.Right);
                    }
                }
                else if (hitLeft)
                {
                    pending.Push(node.Left);
                }
                else if (hitRight)
                {
                    pending.Push(node.Right);
                }
            }

            return hit;
        }

        private int BuildNode(int start, int count)
        {
            CalculateBounds(
                start,
                count,
                out Vector3 minimum,
                out Vector3 maximum,
                out Vector3 centroidMinimum,
                out Vector3 centroidMaximum);
            int nodeIndex = nodes.Count;
            nodes.Add(new Node(minimum, maximum, start, count, -1, -1));
            if (count <= LeafTriangleCount)
            {
                return nodeIndex;
            }

            Vector3 centroidSize = centroidMaximum - centroidMinimum;
            int axis = centroidSize.x >= centroidSize.y
                && centroidSize.x >= centroidSize.z
                    ? 0
                    : centroidSize.y >= centroidSize.z ? 1 : 2;
            Array.Sort(
                triangleOrder,
                start,
                count,
                Comparer<int>.Create((a, b) =>
                    GetTriangleCentroid(a)[axis].CompareTo(
                        GetTriangleCentroid(b)[axis])));

            int leftCount = count / 2;
            int left = BuildNode(start, leftCount);
            int right = BuildNode(start + leftCount, count - leftCount);
            nodes[nodeIndex] = new Node(
                minimum,
                maximum,
                start,
                0,
                left,
                right);
            return nodeIndex;
        }

        private void CalculateBounds(
            int start,
            int count,
            out Vector3 minimum,
            out Vector3 maximum,
            out Vector3 centroidMinimum,
            out Vector3 centroidMaximum)
        {
            minimum = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            maximum = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            centroidMinimum = minimum;
            centroidMaximum = maximum;
            for (int i = 0; i < count; i++)
            {
                int triangle = triangleOrder[start + i];
                int firstIndex = triangle * 3;
                Vector3 a = vertices[triangles[firstIndex]];
                Vector3 b = vertices[triangles[firstIndex + 1]];
                Vector3 c = vertices[triangles[firstIndex + 2]];
                minimum = Vector3.Min(minimum, Vector3.Min(a, Vector3.Min(b, c)));
                maximum = Vector3.Max(maximum, Vector3.Max(a, Vector3.Max(b, c)));
                Vector3 centroid = (a + b + c) / 3f;
                centroidMinimum = Vector3.Min(centroidMinimum, centroid);
                centroidMaximum = Vector3.Max(centroidMaximum, centroid);
            }
        }

        private Vector3 GetTriangleCentroid(int triangle)
        {
            int firstIndex = triangle * 3;
            return (
                vertices[triangles[firstIndex]]
                + vertices[triangles[firstIndex + 1]]
                + vertices[triangles[firstIndex + 2]]) / 3f;
        }

        private bool TryIntersectTriangle(
            Ray ray,
            int triangle,
            float maxDistance,
            out float distance,
            out Vector3 normal)
        {
            int firstIndex = triangle * 3;
            Vector3 a = vertices[triangles[firstIndex]];
            Vector3 b = vertices[triangles[firstIndex + 1]];
            Vector3 c = vertices[triangles[firstIndex + 2]];
            Vector3 edgeOne = b - a;
            Vector3 edgeTwo = c - a;
            Vector3 cross = Vector3.Cross(ray.direction, edgeTwo);
            float determinant = Vector3.Dot(edgeOne, cross);
            if (Mathf.Abs(determinant) <= DirectionEpsilon)
            {
                distance = 0f;
                normal = default;
                return false;
            }

            float inverseDeterminant = 1f / determinant;
            Vector3 fromA = ray.origin - a;
            float u = Vector3.Dot(fromA, cross) * inverseDeterminant;
            if (u < 0f || u > 1f)
            {
                distance = 0f;
                normal = default;
                return false;
            }

            Vector3 q = Vector3.Cross(fromA, edgeOne);
            float v = Vector3.Dot(ray.direction, q) * inverseDeterminant;
            if (v < 0f || u + v > 1f)
            {
                distance = 0f;
                normal = default;
                return false;
            }

            distance = Vector3.Dot(edgeTwo, q) * inverseDeterminant;
            if (distance < 0f || distance > maxDistance)
            {
                normal = default;
                return false;
            }

            normal = Vector3.Cross(edgeOne, edgeTwo).normalized;
            if (Vector3.Dot(normal, ray.direction) > 0f)
            {
                normal = -normal;
            }
            return true;
        }

        private static bool IntersectsBounds(
            Ray ray,
            Vector3 minimum,
            Vector3 maximum,
            float maxDistance,
            out float entryDistance)
        {
            float minimumDistance = 0f;
            float maximumDistance = maxDistance;
            for (int axis = 0; axis < 3; axis++)
            {
                float direction = ray.direction[axis];
                float origin = ray.origin[axis];
                if (Mathf.Abs(direction) <= DirectionEpsilon)
                {
                    if (origin < minimum[axis] || origin > maximum[axis])
                    {
                        entryDistance = 0f;
                        return false;
                    }
                    continue;
                }

                float inverseDirection = 1f / direction;
                float first = (minimum[axis] - origin) * inverseDirection;
                float second = (maximum[axis] - origin) * inverseDirection;
                if (first > second)
                {
                    float swap = first;
                    first = second;
                    second = swap;
                }
                minimumDistance = Mathf.Max(minimumDistance, first);
                maximumDistance = Mathf.Min(maximumDistance, second);
                if (minimumDistance > maximumDistance)
                {
                    entryDistance = 0f;
                    return false;
                }
            }

            entryDistance = minimumDistance;
            return maximumDistance >= 0f;
        }

        private readonly struct Node
        {
            public Node(
                Vector3 minimum,
                Vector3 maximum,
                int start,
                int count,
                int left,
                int right)
            {
                Minimum = minimum;
                Maximum = maximum;
                Start = start;
                Count = count;
                Left = left;
                Right = right;
            }

            public Vector3 Minimum { get; }
            public Vector3 Maximum { get; }
            public int Start { get; }
            public int Count { get; }
            public int Left { get; }
            public int Right { get; }
            public bool IsLeaf => Left < 0;
        }
    }
}
