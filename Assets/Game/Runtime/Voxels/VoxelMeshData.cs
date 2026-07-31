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
        private static readonly ProfilerMarker SetUvsMarker =
            new ProfilerMarker("Voxel.Mesh.UploadUvs");
        private static readonly ProfilerMarker SetNormalsMarker =
            new ProfilerMarker("Voxel.Mesh.UploadNormals");
        private static readonly ProfilerMarker SetTangentsMarker =
            new ProfilerMarker("Voxel.Mesh.UploadTangents");
        private static readonly ProfilerMarker SetTrianglesMarker =
            new ProfilerMarker("Voxel.Mesh.UploadTriangles");
        private static readonly ProfilerMarker FinalizeNormalsMarker =
            new ProfilerMarker("Voxel.Mesh.FinalizeNormals");
        private static readonly ProfilerMarker RecalculateBoundsMarker =
            new ProfilerMarker("Voxel.Mesh.RecalculateBounds");

        private const float WorldUvScale = 0.25f;
        private const int MarchingCubesEdgeCount = 12;
        private const int ProjectionAxisCount = 3;

        internal const int ProjectedEdgeCacheSize =
            MarchingCubesEdgeCount * ProjectionAxisCount;

        private readonly SortedDictionary<VoxelTypeId, List<int>> trianglesByType =
            new SortedDictionary<VoxelTypeId, List<int>>();
        private readonly List<VoxelTypeId> submeshTypes = new List<VoxelTypeId>();
        private readonly List<int> smoothingGroupByVertex = new List<int>();
        private readonly List<ProjectionAxis> projectionAxisByVertex =
            new List<ProjectionAxis>();
        private readonly List<Vector3> accumulatedNormalsBySmoothingGroup =
            new List<Vector3>();
        private bool normalsFinalized;

        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector4> Tangents = new List<Vector4>();
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

        internal int CreateSmoothingGroup()
        {
            accumulatedNormalsBySmoothingGroup.Add(Vector3.zero);
            return accumulatedNormalsBySmoothingGroup.Count - 1;
        }

        internal void AddFaceProjectedTriangle(
            VoxelTypeId type,
            int firstEdge,
            int secondEdge,
            int thirdEdge,
            Vector3[] edgePositions,
            int[] edgeSmoothingGroups,
            int[] projectedEdgeVertexIndices)
        {
            if (type.IsAir)
                throw new ArgumentException("Air cannot own mesh triangles.", nameof(type));

            Vector3 firstPosition = edgePositions[firstEdge];
            Vector3 secondPosition = edgePositions[secondEdge];
            Vector3 thirdPosition = edgePositions[thirdEdge];
            Vector3 faceNormal = Vector3.Cross(
                secondPosition - firstPosition,
                thirdPosition - firstPosition);
            ProjectionAxis projectionAxis = ResolveProjectionAxis(faceNormal);

            int first = GetOrCreateProjectedVertex(
                firstEdge,
                projectionAxis,
                edgePositions,
                edgeSmoothingGroups,
                projectedEdgeVertexIndices);
            int second = GetOrCreateProjectedVertex(
                secondEdge,
                projectionAxis,
                edgePositions,
                edgeSmoothingGroups,
                projectedEdgeVertexIndices);
            int third = GetOrCreateProjectedVertex(
                thirdEdge,
                projectionAxis,
                edgePositions,
                edgeSmoothingGroups,
                projectedEdgeVertexIndices);

            if (!trianglesByType.TryGetValue(type, out List<int> typeTriangles))
            {
                typeTriangles = new List<int>();
                trianglesByType.Add(type, typeTriangles);
            }

            typeTriangles.Add(first);
            typeTriangles.Add(second);
            typeTriangles.Add(third);
            Triangles.Add(first);
            Triangles.Add(second);
            Triangles.Add(third);

            AccumulateNormal(edgeSmoothingGroups[firstEdge], faceNormal);
            AccumulateNormal(edgeSmoothingGroups[secondEdge], faceNormal);
            AccumulateNormal(edgeSmoothingGroups[thirdEdge], faceNormal);
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

            using (FinalizeNormalsMarker.Auto())
            {
                PrepareForUpload();
            }

            using (SetUvsMarker.Auto())
            {
                mesh.SetUVs(0, Uvs);
            }

            using (SetNormalsMarker.Auto())
            {
                mesh.SetNormals(Normals);
            }

            using (SetTrianglesMarker.Auto())
            {
                if (trianglesByType.Count == 0)
                {
                    mesh.SetTriangles(Triangles, 0, false);
                }
                else
                {
                    mesh.subMeshCount = trianglesByType.Count;
                    int submesh = 0;
                    foreach (KeyValuePair<VoxelTypeId, List<int>> pair in trianglesByType)
                    {
                        mesh.SetTriangles(pair.Value, submesh++, false);
                    }
                }
            }

            using (SetTangentsMarker.Auto())
            {
                mesh.SetTangents(Tangents);
            }

            using (RecalculateBoundsMarker.Auto())
            {
                mesh.RecalculateBounds();
            }
            return mesh;
        }

        private int GetOrCreateProjectedVertex(
            int edge,
            ProjectionAxis projectionAxis,
            Vector3[] edgePositions,
            int[] edgeSmoothingGroups,
            int[] projectedEdgeVertexIndices)
        {
            int cacheIndex = edge
                + (int)projectionAxis * MarchingCubesEdgeCount;
            int vertexIndex = projectedEdgeVertexIndices[cacheIndex];
            if (vertexIndex >= 0)
            {
                return vertexIndex;
            }

            Vector3 vertex = edgePositions[edge];
            vertexIndex = Vertices.Count;
            projectedEdgeVertexIndices[cacheIndex] = vertexIndex;
            Vertices.Add(vertex);
            Uvs.Add(ProjectVertex(vertex, projectionAxis));
            Normals.Add(Vector3.zero);
            Tangents.Add(Vector4.zero);
            smoothingGroupByVertex.Add(edgeSmoothingGroups[edge]);
            projectionAxisByVertex.Add(projectionAxis);
            normalsFinalized = false;
            return vertexIndex;
        }

        private static ProjectionAxis ResolveProjectionAxis(Vector3 faceNormal)
        {
            float absoluteX = Mathf.Abs(faceNormal.x);
            float absoluteY = Mathf.Abs(faceNormal.y);
            float absoluteZ = Mathf.Abs(faceNormal.z);
            if (absoluteX >= absoluteY && absoluteX >= absoluteZ)
            {
                return ProjectionAxis.X;
            }

            return absoluteY >= absoluteZ
                ? ProjectionAxis.Y
                : ProjectionAxis.Z;
        }

        private static Vector2 ProjectVertex(
            Vector3 vertex,
            ProjectionAxis projectionAxis)
        {
            switch (projectionAxis)
            {
                case ProjectionAxis.X:
                    return new Vector2(vertex.z, vertex.y) * WorldUvScale;
                case ProjectionAxis.Y:
                    return new Vector2(vertex.x, vertex.z) * WorldUvScale;
                default:
                    return new Vector2(vertex.x, vertex.y) * WorldUvScale;
            }
        }

        private void AccumulateNormal(int smoothingGroup, Vector3 faceNormal)
        {
            accumulatedNormalsBySmoothingGroup[smoothingGroup] += faceNormal;
        }

        internal void PrepareForUpload()
        {
            if (normalsFinalized)
            {
                return;
            }

            for (int vertex = 0; vertex < Normals.Count; vertex++)
            {
                Vector3 normal = accumulatedNormalsBySmoothingGroup[
                    smoothingGroupByVertex[vertex]].normalized;
                Normals[vertex] = normal;
                Tangents[vertex] = CalculateTangent(
                    normal,
                    projectionAxisByVertex[vertex]);
            }
            normalsFinalized = true;
        }

        private static Vector4 CalculateTangent(
            Vector3 normal,
            ProjectionAxis projectionAxis)
        {
            Vector3 uDirection;
            Vector3 vDirection;
            switch (projectionAxis)
            {
                case ProjectionAxis.X:
                    uDirection = Vector3.forward;
                    vDirection = Vector3.up;
                    break;
                case ProjectionAxis.Y:
                    uDirection = Vector3.right;
                    vDirection = Vector3.forward;
                    break;
                default:
                    uDirection = Vector3.right;
                    vDirection = Vector3.up;
                    break;
            }

            Vector3 tangent = uDirection
                - normal * Vector3.Dot(normal, uDirection);
            tangent.Normalize();
            float handedness = Vector3.Dot(
                    Vector3.Cross(normal, tangent),
                    vDirection) < 0f
                ? -1f
                : 1f;
            return new Vector4(tangent.x, tangent.y, tangent.z, handedness);
        }

        private enum ProjectionAxis
        {
            X,
            Y,
            Z,
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
