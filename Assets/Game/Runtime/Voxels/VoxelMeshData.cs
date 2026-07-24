using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.Voxels
{
    public sealed class VoxelMeshData
    {
        private static readonly ProfilerMarker SetVerticesMarker =
            new ProfilerMarker("Voxel.Mesh.UploadVertices");
        private static readonly ProfilerMarker SetTrianglesMarker =
            new ProfilerMarker("Voxel.Mesh.UploadTriangles");
        private static readonly ProfilerMarker RecalculateNormalsMarker =
            new ProfilerMarker("Voxel.Mesh.RecalculateNormals");
        private static readonly ProfilerMarker RecalculateBoundsMarker =
            new ProfilerMarker("Voxel.Mesh.RecalculateBounds");

        private readonly SortedDictionary<VoxelTypeId, List<int>> trianglesByType =
            new SortedDictionary<VoxelTypeId, List<int>>();
        private readonly List<VoxelTypeId> submeshTypes = new List<VoxelTypeId>();

        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<int> Triangles = new List<int>();

        public int TriangleCount => Triangles.Count / 3;
        public int SubmeshCount => trianglesByType.Count;
        public IReadOnlyList<VoxelTypeId> SubmeshTypes
        {
            get
            {
                RefreshSubmeshTypes();
                return submeshTypes;
            }
        }

        public IReadOnlyList<int> GetTriangles(VoxelTypeId type)
        {
            return trianglesByType.TryGetValue(type, out List<int> triangles)
                ? triangles
                : Array.Empty<int>();
        }

        internal void AddTriangleIndex(VoxelTypeId type, int vertexIndex)
        {
            if (type.IsAir)
            {
                throw new ArgumentException("Air cannot own mesh triangles.", nameof(type));
            }

            if (!trianglesByType.TryGetValue(type, out List<int> typeTriangles))
            {
                typeTriangles = new List<int>();
                trianglesByType.Add(type, typeTriangles);
            }

            typeTriangles.Add(vertexIndex);
            Triangles.Add(vertexIndex);
        }

        public Mesh CreateMesh(string meshName = "Marching Cubes Mesh")
        {
            var mesh = new Mesh
            {
                name = meshName,
                indexFormat = Vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16,
            };

            using (SetVerticesMarker.Auto())
            {
                mesh.SetVertices(Vertices);
            }

            using (SetTrianglesMarker.Auto())
            {
                if (trianglesByType.Count == 0)
                {
                    mesh.SetTriangles(Triangles, 0, true);
                }
                else
                {
                    mesh.subMeshCount = trianglesByType.Count;
                    int submesh = 0;
                    foreach (KeyValuePair<VoxelTypeId, List<int>> pair in trianglesByType)
                    {
                        mesh.SetTriangles(pair.Value, submesh++, true);
                    }
                }
            }

            using (RecalculateNormalsMarker.Auto())
            {
                mesh.RecalculateNormals();
            }

            using (RecalculateBoundsMarker.Auto())
            {
                mesh.RecalculateBounds();
            }
            return mesh;
        }

        private void RefreshSubmeshTypes()
        {
            submeshTypes.Clear();
            foreach (VoxelTypeId type in trianglesByType.Keys)
            {
                submeshTypes.Add(type);
            }
        }
    }
}
