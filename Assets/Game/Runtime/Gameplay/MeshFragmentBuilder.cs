using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Partitions a mesh's surface triangles into spatially coherent pieces.
    /// Runtime ore uses the result directly; the editor fracture builder saves
    /// the same result as pre-cut treasure assets.
    /// </summary>
    public static class MeshFragmentBuilder
    {
        public readonly struct Fragment
        {
            public Fragment(
                Mesh mesh,
                Vector3 localPosition,
                int triangleCount)
            {
                Mesh = mesh;
                LocalPosition = localPosition;
                TriangleCount = triangleCount;
            }

            public Mesh Mesh { get; }
            public Vector3 LocalPosition { get; }
            public int TriangleCount { get; }
        }

        private readonly struct Triangle
        {
            public Triangle(int a, int b, int c, int submesh, Vector3 centre)
            {
                A = a;
                B = b;
                C = c;
                Submesh = submesh;
                Centre = centre;
            }

            public int A { get; }
            public int B { get; }
            public int C { get; }
            public int Submesh { get; }
            public Vector3 Centre { get; }
        }

        private sealed class MeshSnapshot
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector4[] Tangents;
            public Vector2[] Uv;
            public Color[] Colors;
            public int[][] Indices;
        }

        private sealed class FragmentData
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector4> Tangents = new List<Vector4>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<int>[] TrianglesBySubmesh;

            public FragmentData(int submeshCount)
            {
                TrianglesBySubmesh = new List<int>[submeshCount];
                for (int i = 0; i < submeshCount; i++)
                {
                    TrianglesBySubmesh[i] = new List<int>();
                }
            }
        }

        public static IReadOnlyList<Fragment> Build(
            Mesh source,
            int requestedFragmentCount,
            int seed)
        {
            if (source == null || source.vertexCount < 3)
            {
                return Array.Empty<Fragment>();
            }

            MeshSnapshot snapshot = ReadSnapshot(source);
            List<Triangle> triangles = CollectTriangles(snapshot);
            if (triangles.Count == 0)
            {
                return Array.Empty<Fragment>();
            }

            int fragmentCount = Mathf.Clamp(
                requestedFragmentCount,
                1,
                triangles.Count);
            Vector3[] clusterCentres = ChooseClusterCentres(
                triangles,
                fragmentCount,
                seed);
            var groups = new FragmentData[fragmentCount];
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = new FragmentData(snapshot.Indices.Length);
            }

            for (int i = 0; i < triangles.Count; i++)
            {
                Triangle triangle = triangles[i];
                int groupIndex = FindNearestCluster(
                    triangle.Centre,
                    clusterCentres);
                AddTriangle(groups[groupIndex], snapshot, triangle);
            }

            var fragments = new List<Fragment>(fragmentCount);
            for (int i = 0; i < groups.Length; i++)
            {
                FragmentData group = groups[i];
                if (group.Vertices.Count == 0)
                {
                    continue;
                }

                fragments.Add(CreateFragment(
                    source.name,
                    group,
                    snapshot,
                    i));
            }

            return fragments;
        }

        private static MeshSnapshot ReadSnapshot(Mesh source)
        {
            var snapshot = new MeshSnapshot();
            using (Mesh.MeshDataArray dataArray =
                   Mesh.AcquireReadOnlyMeshData(source))
            {
                Mesh.MeshData data = dataArray[0];
                snapshot.Vertices = ReadVertices(data);
                snapshot.Normals = ReadNormals(data);
                snapshot.Tangents = ReadTangents(data);
                snapshot.Uv = ReadUv(data);
                snapshot.Colors = ReadColors(data);
                snapshot.Indices = new int[data.subMeshCount][];
                for (int i = 0; i < data.subMeshCount; i++)
                {
                    SubMeshDescriptor descriptor = data.GetSubMesh(i);
                    var indices = new NativeArray<int>(
                        descriptor.indexCount,
                        Allocator.Temp);
                    data.GetIndices(indices, i, true);
                    snapshot.Indices[i] = indices.ToArray();
                    indices.Dispose();
                }
            }

            return snapshot;
        }

        private static Vector3[] ReadVertices(Mesh.MeshData data)
        {
            var values = new NativeArray<Vector3>(
                data.vertexCount,
                Allocator.Temp);
            data.GetVertices(values);
            Vector3[] result = values.ToArray();
            values.Dispose();
            return result;
        }

        private static Vector3[] ReadNormals(Mesh.MeshData data)
        {
            if (!data.HasVertexAttribute(VertexAttribute.Normal))
            {
                return Array.Empty<Vector3>();
            }

            var values = new NativeArray<Vector3>(
                data.vertexCount,
                Allocator.Temp);
            data.GetNormals(values);
            Vector3[] result = values.ToArray();
            values.Dispose();
            return result;
        }

        private static Vector4[] ReadTangents(Mesh.MeshData data)
        {
            if (!data.HasVertexAttribute(VertexAttribute.Tangent))
            {
                return Array.Empty<Vector4>();
            }

            var values = new NativeArray<Vector4>(
                data.vertexCount,
                Allocator.Temp);
            data.GetTangents(values);
            Vector4[] result = values.ToArray();
            values.Dispose();
            return result;
        }

        private static Vector2[] ReadUv(Mesh.MeshData data)
        {
            if (!data.HasVertexAttribute(VertexAttribute.TexCoord0))
            {
                return Array.Empty<Vector2>();
            }

            var values = new NativeArray<Vector2>(
                data.vertexCount,
                Allocator.Temp);
            data.GetUVs(0, values);
            Vector2[] result = values.ToArray();
            values.Dispose();
            return result;
        }

        private static Color[] ReadColors(Mesh.MeshData data)
        {
            if (!data.HasVertexAttribute(VertexAttribute.Color))
            {
                return Array.Empty<Color>();
            }

            var values = new NativeArray<Color>(
                data.vertexCount,
                Allocator.Temp);
            data.GetColors(values);
            Color[] result = values.ToArray();
            values.Dispose();
            return result;
        }

        private static List<Triangle> CollectTriangles(
            MeshSnapshot snapshot)
        {
            var triangles = new List<Triangle>();
            for (int submesh = 0; submesh < snapshot.Indices.Length; submesh++)
            {
                int[] indices = snapshot.Indices[submesh];
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = indices[i];
                    int b = indices[i + 1];
                    int c = indices[i + 2];
                    Vector3 centre = (
                        snapshot.Vertices[a]
                        + snapshot.Vertices[b]
                        + snapshot.Vertices[c]) / 3f;
                    triangles.Add(new Triangle(a, b, c, submesh, centre));
                }
            }

            return triangles;
        }

        private static Vector3[] ChooseClusterCentres(
            IReadOnlyList<Triangle> triangles,
            int count,
            int seed)
        {
            var centres = new Vector3[count];
            int firstIndex = PositiveModulo(seed, triangles.Count);
            centres[0] = triangles[firstIndex].Centre;

            for (int centreIndex = 1; centreIndex < count; centreIndex++)
            {
                float greatestDistance = -1f;
                int selectedIndex = 0;
                for (int triangleIndex = 0;
                     triangleIndex < triangles.Count;
                     triangleIndex++)
                {
                    Vector3 candidate = triangles[triangleIndex].Centre;
                    float nearestDistance = float.MaxValue;
                    for (int existing = 0;
                         existing < centreIndex;
                         existing++)
                    {
                        nearestDistance = Mathf.Min(
                            nearestDistance,
                            (candidate - centres[existing]).sqrMagnitude);
                    }

                    float tieBreaker = PositiveModulo(
                        seed + triangleIndex * 486187739,
                        1000) * 0.0000001f;
                    nearestDistance += tieBreaker;
                    if (nearestDistance > greatestDistance)
                    {
                        greatestDistance = nearestDistance;
                        selectedIndex = triangleIndex;
                    }
                }

                centres[centreIndex] = triangles[selectedIndex].Centre;
            }

            return centres;
        }

        private static int FindNearestCluster(
            Vector3 centre,
            IReadOnlyList<Vector3> clusterCentres)
        {
            int nearestIndex = 0;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < clusterCentres.Count; i++)
            {
                float distance =
                    (centre - clusterCentres[i]).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = i;
                }
            }

            return nearestIndex;
        }

        private static void AddTriangle(
            FragmentData target,
            MeshSnapshot snapshot,
            Triangle triangle)
        {
            int[] sourceIndices = { triangle.A, triangle.B, triangle.C };
            int firstIndex = target.Vertices.Count;
            for (int i = 0; i < sourceIndices.Length; i++)
            {
                int sourceIndex = sourceIndices[i];
                target.Vertices.Add(snapshot.Vertices[sourceIndex]);
                if (snapshot.Normals.Length > 0)
                    target.Normals.Add(snapshot.Normals[sourceIndex]);
                if (snapshot.Tangents.Length > 0)
                    target.Tangents.Add(snapshot.Tangents[sourceIndex]);
                if (snapshot.Uv.Length > 0)
                    target.Uv.Add(snapshot.Uv[sourceIndex]);
                if (snapshot.Colors.Length > 0)
                    target.Colors.Add(snapshot.Colors[sourceIndex]);
            }

            List<int> indices =
                target.TrianglesBySubmesh[triangle.Submesh];
            indices.Add(firstIndex);
            indices.Add(firstIndex + 1);
            indices.Add(firstIndex + 2);
        }

        private static Fragment CreateFragment(
            string sourceName,
            FragmentData data,
            MeshSnapshot snapshot,
            int index)
        {
            Bounds bounds = new Bounds(data.Vertices[0], Vector3.zero);
            for (int i = 1; i < data.Vertices.Count; i++)
            {
                bounds.Encapsulate(data.Vertices[i]);
            }

            Vector3 centre = bounds.center;
            for (int i = 0; i < data.Vertices.Count; i++)
            {
                data.Vertices[i] -= centre;
            }

            var mesh = new Mesh
            {
                name = $"{sourceName} Fragment {index + 1}",
                indexFormat = data.Vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(data.Vertices);
            if (snapshot.Normals.Length > 0)
                mesh.SetNormals(data.Normals);
            if (snapshot.Tangents.Length > 0)
                mesh.SetTangents(data.Tangents);
            if (snapshot.Uv.Length > 0)
                mesh.SetUVs(0, data.Uv);
            if (snapshot.Colors.Length > 0)
                mesh.SetColors(data.Colors);

            mesh.subMeshCount = data.TrianglesBySubmesh.Length;
            int triangleCount = 0;
            for (int i = 0; i < data.TrianglesBySubmesh.Length; i++)
            {
                List<int> indices = data.TrianglesBySubmesh[i];
                mesh.SetTriangles(indices, i, false);
                triangleCount += indices.Count / 3;
            }

            if (snapshot.Normals.Length == 0)
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return new Fragment(mesh, centre, triangleCount);
        }

        private static int PositiveModulo(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }
    }
}
