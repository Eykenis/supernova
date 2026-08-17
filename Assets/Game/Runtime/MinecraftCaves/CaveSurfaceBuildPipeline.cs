using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Supernova.Voxels;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.MinecraftCaves
{
    internal static class CaveSurfaceListPool<T>
    {
        private static readonly ConcurrentBag<List<T>> Pool =
            new ConcurrentBag<List<T>>();

        public static List<T> Rent(int capacity = 0)
        {
            if (!Pool.TryTake(out List<T> list))
            {
                return new List<T>(capacity);
            }

            list.Clear();
            if (list.Capacity < capacity)
            {
                list.Capacity = capacity;
            }
            return list;
        }

        public static void Return(List<T> list)
        {
            if (list == null)
            {
                return;
            }

            list.Clear();
            Pool.Add(list);
        }
    }

    internal sealed class CaveTerrainSurfaceLayerData : IDisposable
    {
        private List<Vector3> vertices;
        private List<Vector3> normals;
        private List<Vector4> tangents;
        private List<Vector2> uvs;
        private List<Color32> colors;
        private List<int> triangles;

        public CaveTerrainSurfaceLayerData(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uvs,
            List<Color32> colors,
            List<int> triangles)
        {
            this.vertices = vertices;
            this.normals = normals;
            this.tangents = tangents;
            this.uvs = uvs;
            this.colors = colors;
            this.triangles = triangles;
        }

        public Mesh CreateMesh(string meshName)
        {
            if (vertices == null || triangles == null || triangles.Count == 0)
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

        public void Dispose()
        {
            CaveSurfaceListPool<Vector3>.Return(vertices);
            CaveSurfaceListPool<Vector3>.Return(normals);
            CaveSurfaceListPool<Vector4>.Return(tangents);
            CaveSurfaceListPool<Vector2>.Return(uvs);
            CaveSurfaceListPool<Color32>.Return(colors);
            CaveSurfaceListPool<int>.Return(triangles);
            vertices = null;
            normals = null;
            tangents = null;
            uvs = null;
            colors = null;
            triangles = null;
        }
    }

    internal sealed class CaveSurfaceBuildResult : IDisposable
    {
        private List<CaveSurfacePlacement> placements;

        private CaveSurfaceBuildResult(
            CaveTerrainSurfaceLayerData terrainLayer,
            List<CaveSurfacePlacement> placements)
        {
            TerrainLayer = terrainLayer;
            this.placements = placements;
        }

        public CaveTerrainSurfaceLayerData TerrainLayer { get; private set; }
        public IReadOnlyList<CaveSurfacePlacement> Placements => placements;

        public static CaveSurfaceBuildResult Build(
            VoxelMeshData meshData,
            Vector3Int meshSection,
            int sectionStartY,
            float voxelSize,
            float isoLevel,
            int worldSeed,
            VoxelGroupMap voxelGroupMap,
            CaveSurfaceGenerationSnapshot generationSnapshot,
            CaveSurfaceSampleSnapshot sampleSnapshot,
            ISet<Vector3Int> carvedVoxels)
        {
            if (meshData == null
                || generationSnapshot == null
                || voxelSize <= 0f
                || meshData.TriangleCount == 0)
            {
                return null;
            }

            meshData.PrepareForUpload();
            CaveTerrainSurfaceLayerData terrainLayer = null;
            List<CaveSurfacePlacement> placements = null;
            try
            {
                terrainLayer = CaveTerrainSurfaceLayerWorker.Build(
                    meshData,
                    meshSection,
                    sectionStartY,
                    voxelSize,
                    worldSeed,
                    voxelGroupMap,
                    generationSnapshot,
                    carvedVoxels);
                if (generationSnapshot.HasBrushes && sampleSnapshot != null)
                {
                    placements = CaveSurfaceBrushWorker.Build(
                        meshData,
                        sampleSnapshot,
                        meshSection,
                        sectionStartY,
                        voxelSize,
                        isoLevel,
                        worldSeed,
                        generationSnapshot,
                        carvedVoxels);
                }
                return new CaveSurfaceBuildResult(terrainLayer, placements);
            }
            catch
            {
                terrainLayer?.Dispose();
                CaveSurfaceListPool<CaveSurfacePlacement>.Return(placements);
                throw;
            }
        }

        public void Dispose()
        {
            TerrainLayer?.Dispose();
            TerrainLayer = null;
            CaveSurfaceListPool<CaveSurfacePlacement>.Return(placements);
            placements = null;
        }
    }

    internal static class CaveTerrainSurfaceLayerWorker
    {
        private static readonly ProfilerMarker BuildMarker =
            new ProfilerMarker("CaveSurface.Worker.TerrainLayer");

        private const float MinimumFaceUpAlignment = 0.15f;
        private const float SlopeFadeStart = 0.22f;
        private const float SlopeFadeEnd = 0.68f;
        private const float MinimumVisibleAlpha = 1f / 255f;

        public static CaveTerrainSurfaceLayerData Build(
            VoxelMeshData meshData,
            Vector3Int meshSection,
            int sectionStartY,
            float voxelSize,
            int worldSeed,
            VoxelGroupMap voxelGroupMap,
            CaveSurfaceGenerationSnapshot generationSnapshot,
            ISet<Vector3Int> carvedVoxels)
        {
            using (BuildMarker.Auto())
            {
                int sourceVertexCount = meshData.Vertices.Count;
                SurfaceVertexSample[] sampledVertices =
                    ArrayPool<SurfaceVertexSample>.Shared.Rent(
                        sourceVertexCount);
                bool[] hasSample = ArrayPool<bool>.Shared.Rent(
                    sourceVertexCount);
                int[] layerIndexBySourceVertex =
                    ArrayPool<int>.Shared.Rent(sourceVertexCount);
                Array.Clear(hasSample, 0, sourceVertexCount);
                Array.Fill(
                    layerIndexBySourceVertex,
                    -1,
                    0,
                    sourceVertexCount);

                List<Vector3> vertices =
                    CaveSurfaceListPool<Vector3>.Rent(sourceVertexCount);
                List<Vector3> normals =
                    CaveSurfaceListPool<Vector3>.Rent(sourceVertexCount);
                List<Vector4> tangents =
                    CaveSurfaceListPool<Vector4>.Rent(sourceVertexCount);
                List<Vector2> uvs =
                    CaveSurfaceListPool<Vector2>.Rent(sourceVertexCount);
                List<Color32> colors =
                    CaveSurfaceListPool<Color32>.Rent(sourceVertexCount);
                List<int> triangles = CaveSurfaceListPool<int>.Rent(
                    meshData.Triangles.Count);
                bool ownsLists = true;

                try
                {
                    var sectionVoxelOrigin = new Vector3(
                        meshSection.x * VoxelColumnChunkData.Width,
                        sectionStartY,
                        meshSection.z * VoxelColumnChunkData.Depth);
                    IReadOnlyList<VoxelTypeId> surfaceTypes =
                        meshData.SubmeshTypes;
                    for (int typeIndex = 0;
                        typeIndex < surfaceTypes.Count;
                        typeIndex++)
                    {
                        VoxelTypeId surfaceType = surfaceTypes[typeIndex];
                        if (!voxelGroupMap.TryGetGroup(
                                surfaceType,
                                out VoxelGroup group)
                            || group != VoxelGroup.Stone)
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
                                meshData.Vertices[second]
                                    - meshData.Vertices[first],
                                meshData.Vertices[third]
                                    - meshData.Vertices[first]);
                            if (faceNormal.sqrMagnitude <= Mathf.Epsilon
                                || faceNormal.normalized.y
                                    < MinimumFaceUpAlignment)
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
                                generationSnapshot,
                                sampledVertices,
                                hasSample);
                            SurfaceVertexSample secondSample = GetSample(
                                second,
                                meshData,
                                sectionVoxelOrigin,
                                voxelSize,
                                worldSeed,
                                generationSnapshot,
                                sampledVertices,
                                hasSample);
                            SurfaceVertexSample thirdSample = GetSample(
                                third,
                                meshData,
                                sectionVoxelOrigin,
                                voxelSize,
                                worldSeed,
                                generationSnapshot,
                                sampledVertices,
                                hasSample);
                            float maximumAlpha = Mathf.Max(
                                firstSample.Color.a,
                                Mathf.Max(
                                    secondSample.Color.a,
                                    thirdSample.Color.a));
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

                    ownsLists = false;
                    return new CaveTerrainSurfaceLayerData(
                        vertices,
                        normals,
                        tangents,
                        uvs,
                        colors,
                        triangles);
                }
                finally
                {
                    ArrayPool<SurfaceVertexSample>.Shared.Return(
                        sampledVertices);
                    Array.Clear(hasSample, 0, sourceVertexCount);
                    ArrayPool<bool>.Shared.Return(hasSample);
                    ArrayPool<int>.Shared.Return(layerIndexBySourceVertex);
                    if (ownsLists)
                    {
                        CaveSurfaceListPool<Vector3>.Return(vertices);
                        CaveSurfaceListPool<Vector3>.Return(normals);
                        CaveSurfaceListPool<Vector4>.Return(tangents);
                        CaveSurfaceListPool<Vector2>.Return(uvs);
                        CaveSurfaceListPool<Color32>.Return(colors);
                        CaveSurfaceListPool<int>.Return(triangles);
                    }
                }
            }
        }

        private static SurfaceVertexSample GetSample(
            int sourceVertex,
            VoxelMeshData meshData,
            Vector3 sectionVoxelOrigin,
            float voxelSize,
            int worldSeed,
            CaveSurfaceGenerationSnapshot generationSnapshot,
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
            CaveBiomeRuntimeSnapshot biome =
                generationSnapshot.EvaluateSurface(
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

    internal static class CaveSurfaceBrushWorker
    {
        private static readonly ProfilerMarker BuildMarker =
            new ProfilerMarker("CaveSurface.Worker.BrushPlacement");
        private static readonly ConcurrentBag<Dictionary<
            Vector3Int,
            CaveBiomeRuntimeSnapshot>> BiomeMaps =
                new ConcurrentBag<Dictionary<
                    Vector3Int,
                    CaveBiomeRuntimeSnapshot>>();

        private const int BiomeSampleCellSize = 4;
        private const double Inverse53BitRange =
            1.0 / 9007199254740992.0;

        public static List<CaveSurfacePlacement> Build(
            VoxelMeshData meshData,
            CaveSurfaceSampleSnapshot samples,
            Vector3Int meshSection,
            int sectionStartY,
            float voxelSize,
            float isoLevel,
            int worldSeed,
            CaveSurfaceGenerationSnapshot generationSnapshot,
            ISet<Vector3Int> carvedVoxels)
        {
            using (BuildMarker.Auto())
            {
                List<CaveSurfacePlacement> placements =
                    CaveSurfaceListPool<CaveSurfacePlacement>.Rent();
                Dictionary<Vector3Int, CaveBiomeRuntimeSnapshot>
                    biomesBySampleCell;
                if (!BiomeMaps.TryTake(out biomesBySampleCell))
                {
                    biomesBySampleCell =
                        new Dictionary<
                            Vector3Int,
                            CaveBiomeRuntimeSnapshot>();
                }
                biomesBySampleCell.Clear();
                bool ownsPlacements = true;
                try
                {
                    var sectionVoxelOrigin = new Vector3(
                        meshSection.x * VoxelColumnChunkData.Width,
                        sectionStartY,
                        meshSection.z * VoxelColumnChunkData.Depth);
                    IReadOnlyList<VoxelTypeId> surfaceTypes =
                        meshData.SubmeshTypes;
                    for (int typeIndex = 0;
                        typeIndex < surfaceTypes.Count;
                        typeIndex++)
                    {
                        VoxelTypeId surfaceType = surfaceTypes[typeIndex];
                        IReadOnlyList<int> triangles =
                            meshData.GetTriangles(surfaceType);
                        for (int triangle = 0;
                            triangle + 2 < triangles.Count;
                            triangle += 3)
                        {
                            Vector3 first =
                                meshData.Vertices[triangles[triangle]];
                            Vector3 second =
                                meshData.Vertices[triangles[triangle + 1]];
                            Vector3 third =
                                meshData.Vertices[triangles[triangle + 2]];
                            Vector3 cross = Vector3.Cross(
                                second - first,
                                third - first);
                            float doubledArea = cross.magnitude;
                            if (doubledArea <= Mathf.Epsilon)
                            {
                                continue;
                            }

                            Vector3 centroid =
                                (first + second + third) / 3f;
                            Vector3 centroidVoxel = sectionVoxelOrigin
                                + centroid / voxelSize;
                            if (CaveSurfaceDisturbance.IsNearCarvedVoxel(
                                centroidVoxel,
                                carvedVoxels))
                            {
                                continue;
                            }

                            Vector3Int biomeSampleCell =
                                GetBiomeSampleCell(centroidVoxel);
                            if (!biomesBySampleCell.TryGetValue(
                                biomeSampleCell,
                                out CaveBiomeRuntimeSnapshot biome))
                            {
                                Vector3 biomeSamplePosition =
                                    (Vector3)biomeSampleCell
                                    * BiomeSampleCellSize
                                    + Vector3.one
                                    * (BiomeSampleCellSize * 0.5f);
                                biome = generationSnapshot.Evaluate(
                                    biomeSamplePosition,
                                    worldSeed);
                                biomesBySampleCell.Add(
                                    biomeSampleCell,
                                    biome);
                            }
                            if (biome == null
                                || biome.Brushes.Length == 0)
                            {
                                continue;
                            }

                            if (!TryResolveAttachment(
                                samples,
                                sectionVoxelOrigin,
                                centroid,
                                cross / doubledArea,
                                surfaceType,
                                voxelSize,
                                isoLevel,
                                out Vector3 outwardNormal,
                                out _))
                            {
                                continue;
                            }

                            float area = doubledArea * 0.5f;
                            for (int brushIndex = 0;
                                brushIndex < biome.Brushes.Length;
                                brushIndex++)
                            {
                                CaveSurfaceBrushRuntimeSnapshot brush =
                                    biome.Brushes[brushIndex];
                                if (!brush.CanAttachTo(surfaceType)
                                    || !brush.MatchesOrientation(
                                        outwardNormal))
                                {
                                    continue;
                                }

                                double expected =
                                    brush.DensityPerSquareUnit * area;
                                int guaranteed =
                                    Mathf.FloorToInt((float)expected);
                                ulong seed = BuildSeed(
                                    worldSeed,
                                    brush.SeedSalt,
                                    meshSection,
                                    surfaceType,
                                    triangle / 3);
                                var random =
                                    new DeterministicRandom(seed);
                                int count = guaranteed;
                                if (random.NextDouble()
                                    < expected - guaranteed)
                                {
                                    count++;
                                }

                                for (int instance = 0;
                                    instance < count;
                                    instance++)
                                {
                                    Vector3 position = SampleTriangle(
                                        first,
                                        second,
                                        third,
                                        ref random,
                                        out Vector3 barycentric);
                                    Vector3 placementVoxel =
                                        sectionVoxelOrigin
                                        + position / voxelSize;
                                    if (CaveSurfaceDisturbance
                                        .IsNearCarvedVoxel(
                                            placementVoxel,
                                            carvedVoxels))
                                    {
                                        continue;
                                    }
                                    if (!TryResolveAttachment(
                                        samples,
                                        sectionVoxelOrigin,
                                        position,
                                        outwardNormal,
                                        surfaceType,
                                        voxelSize,
                                        isoLevel,
                                        out Vector3 resolvedNormal,
                                        out Vector3Int anchorVoxel))
                                    {
                                        continue;
                                    }

                                    Vector2 tangentRange =
                                        brush.TangentScaleRange;
                                    Vector2 normalRange =
                                        brush.NormalScaleRange;
                                    float tangentScale = Mathf.Lerp(
                                        tangentRange.x,
                                        tangentRange.y,
                                        (float)random.NextDouble());
                                    float normalScale = Mathf.Lerp(
                                        normalRange.x,
                                        normalRange.y,
                                        (float)random.NextDouble());
                                    float yaw =
                                        (float)random.NextDouble() * 360f;
                                    Vector3 shadingNormal =
                                        InterpolateNormal(
                                            meshData,
                                            triangles,
                                            triangle,
                                            barycentric,
                                            resolvedNormal);
                                    Vector3 vertical =
                                        shadingNormal.y < 0f
                                            ? Vector3.down
                                            : Vector3.up;
                                    Vector3 stanceNormal = Vector3.Lerp(
                                        shadingNormal,
                                        vertical,
                                        brush.UprightBias);
                                    if (stanceNormal.sqrMagnitude
                                        <= Mathf.Epsilon)
                                    {
                                        stanceNormal = shadingNormal;
                                    }

                                    CaveSurfaceClumpAttributes clump =
                                        CaveSurfaceClumpField.Sample(
                                            placementVoxel,
                                            brush
                                                .ClumpHorizontalCellSize,
                                            brush.ClumpVerticalCellSize,
                                            brush.ClumpHeightRange,
                                            brush.ClumpWidthRange,
                                            brush.ClumpYawBiasDegrees,
                                            worldSeed,
                                            brush.SeedSalt);
                                    placements.Add(
                                        new CaveSurfacePlacement(
                                            brush.Definition,
                                            biome.Definition,
                                            position
                                                + resolvedNormal
                                                * brush.NormalOffset,
                                            resolvedNormal,
                                            stanceNormal,
                                            new Vector3(
                                                tangentScale
                                                    * clump
                                                        .WidthMultiplier,
                                                normalScale
                                                    * clump
                                                        .HeightMultiplier,
                                                tangentScale
                                                    * clump
                                                        .WidthMultiplier),
                                            yaw + clump.YawBiasDegrees,
                                            anchorVoxel));
                                }
                            }
                        }
                    }

                    ownsPlacements = false;
                    return placements;
                }
                finally
                {
                    biomesBySampleCell.Clear();
                    BiomeMaps.Add(biomesBySampleCell);
                    if (ownsPlacements)
                    {
                        CaveSurfaceListPool<
                            CaveSurfacePlacement>.Return(
                                placements);
                    }
                }
            }
        }

        private static Vector3Int GetBiomeSampleCell(
            Vector3 worldVoxelPosition)
        {
            return new Vector3Int(
                Mathf.FloorToInt(
                    worldVoxelPosition.x / BiomeSampleCellSize),
                Mathf.FloorToInt(
                    worldVoxelPosition.y / BiomeSampleCellSize),
                Mathf.FloorToInt(
                    worldVoxelPosition.z / BiomeSampleCellSize));
        }

        private static bool TryResolveAttachment(
            CaveSurfaceSampleSnapshot samples,
            Vector3 sectionVoxelOrigin,
            Vector3 localSurfacePosition,
            Vector3 faceNormal,
            VoxelTypeId surfaceType,
            float voxelSize,
            float isoLevel,
            out Vector3 outwardNormal,
            out Vector3Int anchorVoxel)
        {
            outwardNormal = faceNormal.normalized;
            anchorVoxel = default;
            if (outwardNormal.sqrMagnitude <= Mathf.Epsilon)
            {
                return false;
            }

            Vector3 surfaceVoxelPosition = sectionVoxelOrigin
                + localSurfacePosition / voxelSize;
            Vector3Int centre = new Vector3Int(
                Mathf.RoundToInt(surfaceVoxelPosition.x),
                Mathf.RoundToInt(surfaceVoxelPosition.y),
                Mathf.RoundToInt(surfaceVoxelPosition.z));
            bool foundSolid = false;
            bool foundAir = false;
            float closestSolidDistance = float.PositiveInfinity;
            float closestAirDistance = float.PositiveInfinity;
            Vector3 closestSolid = default;
            Vector3 closestAir = default;

            for (int z = -1; z <= 1; z++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        var coordinate = new Vector3Int(
                            centre.x + x,
                            centre.y + y,
                            centre.z + z);
                        if (!samples.TryGetSample(
                            coordinate.x,
                            coordinate.y,
                            coordinate.z,
                            out VoxelSample sample))
                        {
                            continue;
                        }

                        float distance = (
                            (Vector3)coordinate
                            - surfaceVoxelPosition).sqrMagnitude;
                        if (sample.IsSolid(isoLevel)
                            && sample.Type == surfaceType
                            && distance < closestSolidDistance)
                        {
                            foundSolid = true;
                            closestSolidDistance = distance;
                            closestSolid = coordinate;
                            anchorVoxel = coordinate;
                        }
                        else if (!sample.IsSolid(isoLevel)
                            && distance < closestAirDistance)
                        {
                            foundAir = true;
                            closestAirDistance = distance;
                            closestAir = coordinate;
                        }
                    }
                }
            }

            if (!foundSolid || !foundAir)
            {
                return false;
            }

            Vector3 solidToAir = closestAir - closestSolid;
            if (Vector3.Dot(outwardNormal, solidToAir) < 0f)
            {
                outwardNormal = -outwardNormal;
            }
            return true;
        }

        private static Vector3 SampleTriangle(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            ref DeterministicRandom random,
            out Vector3 barycentric)
        {
            float root = Mathf.Sqrt((float)random.NextDouble());
            float secondWeight = (float)random.NextDouble();
            barycentric = new Vector3(
                1f - root,
                root * (1f - secondWeight),
                root * secondWeight);
            return barycentric.x * first
                + barycentric.y * second
                + barycentric.z * third;
        }

        private static Vector3 InterpolateNormal(
            VoxelMeshData meshData,
            IReadOnlyList<int> triangles,
            int triangle,
            Vector3 barycentric,
            Vector3 fallback)
        {
            List<Vector3> normals = meshData.Normals;
            int firstIndex = triangles[triangle];
            int secondIndex = triangles[triangle + 1];
            int thirdIndex = triangles[triangle + 2];
            if (normals == null
                || thirdIndex >= normals.Count
                || secondIndex >= normals.Count
                || firstIndex >= normals.Count)
            {
                return fallback;
            }

            Vector3 interpolated =
                barycentric.x * normals[firstIndex]
                + barycentric.y * normals[secondIndex]
                + barycentric.z * normals[thirdIndex];
            if (interpolated.sqrMagnitude <= Mathf.Epsilon)
            {
                return fallback;
            }

            interpolated.Normalize();
            return Vector3.Dot(interpolated, fallback) < 0f
                ? -interpolated
                : interpolated;
        }

        private static ulong BuildSeed(
            int worldSeed,
            int seedSalt,
            Vector3Int meshSection,
            VoxelTypeId surfaceType,
            int triangle)
        {
            ulong value = (uint)worldSeed;
            value ^= (ulong)(uint)seedSalt
                * 0x9E3779B185EBCA87UL;
            value ^= (ulong)(uint)meshSection.x
                * 0xC2B2AE3D27D4EB4FUL;
            value ^= (ulong)(uint)meshSection.y
                * 0x165667B19E3779F9UL;
            value ^= (ulong)(uint)meshSection.z
                * 0x85EBCA77C2B2AE63UL;
            value ^= (ulong)surfaceType.Value
                * 0x27D4EB2F165667C5UL;
            value ^= (ulong)(uint)triangle
                * 0x94D049BB133111EBUL;
            return Mix(value);
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        private struct DeterministicRandom
        {
            private ulong state;

            public DeterministicRandom(ulong seed)
            {
                state = seed;
            }

            public double NextDouble()
            {
                return (NextUInt64() >> 11) * Inverse53BitRange;
            }

            private ulong NextUInt64()
            {
                state += 0x9E3779B97F4A7C15UL;
                return Mix(state);
            }
        }
    }
}
