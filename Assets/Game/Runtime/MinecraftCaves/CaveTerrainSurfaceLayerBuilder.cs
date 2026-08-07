using System.Collections.Generic;
using Supernova.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Extracts upward-facing natural-terrain triangles into an independent,
    /// slightly lifted turf mesh. Vertex alpha contains both biome-boundary and
    /// slope feathering, leaving the source voxel mesh and material untouched.
    /// </summary>
    public static class CaveTerrainSurfaceLayerBuilder
    {
        private const float MinimumFaceUpAlignment = 0.15f;
        private const float SlopeFadeStart = 0.22f;
        private const float SlopeFadeEnd = 0.68f;
        private const float MinimumVisibleAlpha = 1f / 255f;

        public static Mesh Build(
            VoxelMeshData meshData,
            Vector3Int meshSection,
            int sectionStartY,
            float voxelSize,
            int worldSeed,
            CaveBiomeCatalog biomeCatalog,
            IReadOnlyList<VoxelTypeDefinition> voxelDefinitions,
            ISet<Vector3Int> carvedVoxels = null,
            string meshName = "Cave Terrain Surface Layer")
        {
            if (meshData == null
                || biomeCatalog == null
                || voxelSize <= 0f
                || meshData.TriangleCount == 0)
            {
                return null;
            }

            meshData.PrepareForUpload();
            int sourceVertexCount = meshData.Vertices.Count;
            var sampledVertices = new SurfaceVertexSample[sourceVertexCount];
            var hasSample = new bool[sourceVertexCount];
            var layerIndexBySourceVertex = new int[sourceVertexCount];
            for (int i = 0; i < layerIndexBySourceVertex.Length; i++)
            {
                layerIndexBySourceVertex[i] = -1;
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uvs = new List<Vector2>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            var sectionVoxelOrigin = new Vector3(
                meshSection.x * VoxelColumnChunkData.Width,
                sectionStartY,
                meshSection.z * VoxelColumnChunkData.Depth);

            IReadOnlyList<VoxelTypeId> surfaceTypes = meshData.SubmeshTypes;
            for (int typeIndex = 0; typeIndex < surfaceTypes.Count; typeIndex++)
            {
                VoxelTypeId surfaceType = surfaceTypes[typeIndex];
                VoxelTypeDefinition definition = VoxelTypeUtility.Find(
                    surfaceType,
                    voxelDefinitions);
                if (definition == null || definition.Group != VoxelGroup.Stone)
                {
                    continue;
                }

                IReadOnlyList<int> sourceTriangles =
                    meshData.GetTriangles(surfaceType);
                for (int triangle = 0;
                    triangle + 2 < sourceTriangles.Count;
                    triangle += 3)
                {
                    int first = sourceTriangles[triangle];
                    int second = sourceTriangles[triangle + 1];
                    int third = sourceTriangles[triangle + 2];
                    Vector3 faceNormal = Vector3.Cross(
                        meshData.Vertices[second] - meshData.Vertices[first],
                        meshData.Vertices[third] - meshData.Vertices[first]);
                    if (faceNormal.sqrMagnitude <= Mathf.Epsilon
                        || faceNormal.normalized.y < MinimumFaceUpAlignment)
                    {
                        continue;
                    }

                    Vector3 centroid = (
                        meshData.Vertices[first]
                        + meshData.Vertices[second]
                        + meshData.Vertices[third]) / 3f;
                    Vector3 centroidVoxel = sectionVoxelOrigin
                        + centroid / voxelSize;
                    if (CaveSurfaceDisturbance.IsNearCarvedVoxel(
                        centroidVoxel,
                        carvedVoxels))
                    {
                        continue;
                    }

                    SurfaceVertexSample firstSample = GetSample(
                        first,
                        meshData,
                        sectionVoxelOrigin,
                        voxelSize,
                        worldSeed,
                        biomeCatalog,
                        sampledVertices,
                        hasSample);
                    SurfaceVertexSample secondSample = GetSample(
                        second,
                        meshData,
                        sectionVoxelOrigin,
                        voxelSize,
                        worldSeed,
                        biomeCatalog,
                        sampledVertices,
                        hasSample);
                    SurfaceVertexSample thirdSample = GetSample(
                        third,
                        meshData,
                        sectionVoxelOrigin,
                        voxelSize,
                        worldSeed,
                        biomeCatalog,
                        sampledVertices,
                        hasSample);
                    float maximumAlpha = Mathf.Max(
                        firstSample.Color.a,
                        Mathf.Max(secondSample.Color.a, thirdSample.Color.a));
                    if (maximumAlpha <= MinimumVisibleAlpha)
                    {
                        continue;
                    }

                    triangles.Add(GetOrCreateLayerVertex(
                        first,
                        firstSample,
                        meshData,
                        layerIndexBySourceVertex,
                        vertices,
                        normals,
                        tangents,
                        uvs,
                        colors));
                    triangles.Add(GetOrCreateLayerVertex(
                        second,
                        secondSample,
                        meshData,
                        layerIndexBySourceVertex,
                        vertices,
                        normals,
                        tangents,
                        uvs,
                        colors));
                    triangles.Add(GetOrCreateLayerVertex(
                        third,
                        thirdSample,
                        meshData,
                        layerIndexBySourceVertex,
                        vertices,
                        normals,
                        tangents,
                        uvs,
                        colors));
                }
            }

            if (triangles.Count == 0)
            {
                return null;
            }

            var mesh = new Mesh
            {
                name = meshName,
                hideFlags = HideFlags.DontSave,
                indexFormat = vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16,
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static SurfaceVertexSample GetSample(
            int sourceVertex,
            VoxelMeshData meshData,
            Vector3 sectionVoxelOrigin,
            float voxelSize,
            int worldSeed,
            CaveBiomeCatalog biomeCatalog,
            SurfaceVertexSample[] sampledVertices,
            bool[] hasSample)
        {
            if (hasSample[sourceVertex])
            {
                return sampledVertices[sourceVertex];
            }

            Vector3 normal = meshData.Normals[sourceVertex].normalized;
            Vector3 worldVoxelPosition = sectionVoxelOrigin
                + meshData.Vertices[sourceVertex] / voxelSize;
            CaveBiomeDefinition biome = biomeCatalog.EvaluateSurface(
                worldVoxelPosition,
                worldSeed,
                out float interiorCoverage);
            Color color = biome != null
                ? biome.TerrainSurfaceColor
                : Color.clear;
            float slopeCoverage = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    SlopeFadeStart,
                    SlopeFadeEnd,
                    Mathf.Clamp01(normal.y)));
            color.a = Mathf.Clamp01(
                color.a * interiorCoverage * slopeCoverage);
            var sample = new SurfaceVertexSample(
                color,
                biome != null ? biome.TerrainSurfaceOffset : 0f);
            sampledVertices[sourceVertex] = sample;
            hasSample[sourceVertex] = true;
            return sample;
        }

        private static int GetOrCreateLayerVertex(
            int sourceVertex,
            SurfaceVertexSample sample,
            VoxelMeshData meshData,
            int[] layerIndexBySourceVertex,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uvs,
            List<Color32> colors)
        {
            int layerVertex = layerIndexBySourceVertex[sourceVertex];
            if (layerVertex >= 0)
            {
                return layerVertex;
            }

            Vector3 normal = meshData.Normals[sourceVertex].normalized;
            layerVertex = vertices.Count;
            layerIndexBySourceVertex[sourceVertex] = layerVertex;
            vertices.Add(
                meshData.Vertices[sourceVertex] + normal * sample.Offset);
            normals.Add(normal);
            tangents.Add(meshData.Tangents[sourceVertex]);
            uvs.Add(meshData.Uvs[sourceVertex]);
            colors.Add((Color32)sample.Color);
            return layerVertex;
        }

        private readonly struct SurfaceVertexSample
        {
            public SurfaceVertexSample(Color color, float offset)
            {
                Color = color;
                Offset = offset;
            }

            public Color Color { get; }
            public float Offset { get; }
        }
    }
}
