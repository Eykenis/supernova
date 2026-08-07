using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;

public sealed class VoxelTypeMarchingCubesTests
{
    [Test]
    public void VoxelVolume_StoresTypeAndNormalizesAir()
    {
        var volume = new VoxelVolume(-1f);
        var crystal = new VoxelTypeId(7);

        volume.SetSample(3, 4, 5, 1f, crystal);
        Assert.AreEqual(crystal, volume.GetType(3, 4, 5));
        Assert.IsTrue(volume.GetSample(3, 4, 5).IsSolid());

        volume[3, 4, 5] = -1f;
        Assert.AreEqual(VoxelTypeId.Air, volume.GetType(3, 4, 5));

        volume[3, 4, 5] = 1f;
        Assert.AreEqual(VoxelTypeId.Default, volume.GetType(3, 4, 5));
    }

    [Test]
    public void MarchingCubes_DifferentTypesProduceSeparateInsetSubmeshes()
    {
        var volume = new VoxelVolume(-1f);
        var stone = new VoxelTypeId(2);
        var ore = new VoxelTypeId(3);
        volume.SetSample(10, 10, 10, 1f, stone);
        volume.SetSample(11, 10, 10, 1f, ore);

        VoxelMeshData data = MarchingCubesMesher.Build(volume);

        Assert.AreEqual(2, data.SubmeshCount);
        Assert.Greater(data.GetTriangles(stone).Count, 0);
        Assert.Greater(data.GetTriangles(ore).Count, 0);
        Assert.IsTrue(ContainsReferencedVertexAtX(data, stone, 10.45f));
        Assert.IsTrue(ContainsReferencedVertexAtX(data, ore, 10.55f));

        Mesh mesh = data.CreateMesh("Typed voxel test");
        Assert.AreEqual(2, mesh.subMeshCount);
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void MarchingCubes_SameTypeAdjacentSamplesShareOneTypeCluster()
    {
        var volume = new VoxelVolume(-1f);
        var stone = new VoxelTypeId(2);
        volume.SetSample(10, 10, 10, 1f, stone);
        volume.SetSample(11, 10, 10, 1f, stone);

        VoxelMeshData data = MarchingCubesMesher.Build(volume);

        Assert.AreEqual(1, data.SubmeshCount);
        Assert.AreEqual(stone, data.SubmeshTypes[0]);
        Assert.Greater(data.GetTriangles(stone).Count, 0);
    }

    [Test]
    public void CreatedMesh_GeneratesUvForEveryVertex()
    {
        var volume = new VoxelVolume(-1f);
        volume.SetSample(10, 10, 10, 1f, VoxelTypeId.Default);

        VoxelMeshData data = MarchingCubesMesher.Build(volume);
        int generatedVertexCount = data.Vertices.Count;

        Assert.That(data.Uvs, Has.Count.EqualTo(generatedVertexCount));
        Assert.That(data.Normals, Has.Count.EqualTo(generatedVertexCount));
        Mesh mesh = data.CreateMesh("UV generation test");

        Assert.That(data.Vertices, Has.Count.EqualTo(generatedVertexCount));
        Assert.That(mesh.uv, Has.Length.EqualTo(mesh.vertexCount));
        Assert.That(mesh.uv, Has.Some.Not.EqualTo(Vector2.zero));
        Assert.That(mesh.uv, Has.Some.Not.EqualTo(mesh.uv[0]));
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void CreatedMesh_UsesOneProjectionAxisAcrossEachTriangle()
    {
        var volume = new VoxelVolume(-1f);
        volume.SetSample(10, 10, 10, 1f, VoxelTypeId.Default);
        volume.SetSample(11, 10, 10, 1f, VoxelTypeId.Default);
        volume.SetSample(10, 11, 10, 1f, VoxelTypeId.Default);

        VoxelMeshData data = MarchingCubesMesher.Build(volume);
        Mesh mesh = data.CreateMesh("Face UV projection test");
        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        int[] triangles = mesh.triangles;

        for (int triangle = 0; triangle < triangles.Length; triangle += 3)
        {
            int first = triangles[triangle];
            int second = triangles[triangle + 1];
            int third = triangles[triangle + 2];
            Vector3 faceNormal = Vector3.Cross(
                vertices[second] - vertices[first],
                vertices[third] - vertices[first]);

            AssertProjectedUv(vertices[first], uvs[first], faceNormal);
            AssertProjectedUv(vertices[second], uvs[second], faceNormal);
            AssertProjectedUv(vertices[third], uvs[third], faceNormal);
        }

        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void CreatedMesh_GeneratesTangentForEveryVertex()
    {
        var volume = new VoxelVolume(-1f);
        volume.SetSample(10, 10, 10, 1f, VoxelTypeId.Default);

        VoxelMeshData data = MarchingCubesMesher.Build(volume);
        Assert.That(data.Tangents, Has.Count.EqualTo(data.Vertices.Count));
        foreach (Vector4 tangent in data.Tangents)
        {
            Assert.That(tangent.sqrMagnitude, Is.GreaterThan(0.5f));
        }
        Mesh mesh = data.CreateMesh("Tangent generation test");

        Assert.That(mesh.tangents, Has.Length.EqualTo(mesh.vertexCount));
        foreach (Vector4 tangent in mesh.tangents)
        {
            Assert.That(tangent.sqrMagnitude, Is.GreaterThan(0.5f));
        }
        Object.DestroyImmediate(mesh);
    }

    [Test]
    public void MarchingCubes_ParallelBuildsKeepScratchStateIsolated()
    {
        var firstVolume = new VoxelVolume(-1f);
        firstVolume.SetSample(10, 10, 10, 1f, VoxelTypeId.Default);
        var secondVolume = new VoxelVolume(-1f);
        secondVolume.SetSample(20, 20, 20, 1f, new VoxelTypeId(7));

        Task<VoxelMeshData> first = Task.Run(
            () => MarchingCubesMesher.Build(firstVolume));
        Task<VoxelMeshData> second = Task.Run(
            () => MarchingCubesMesher.Build(secondVolume));
        Task.WaitAll(first, second);

        Assert.That(first.Result.Vertices, Is.Not.Empty);
        Assert.That(second.Result.Vertices, Is.Not.Empty);
        Assert.That(first.Result.SubmeshTypes, Has.Count.EqualTo(1));
        Assert.That(second.Result.SubmeshTypes, Has.Count.EqualTo(1));
        Assert.That(first.Result.SubmeshTypes[0], Is.EqualTo(VoxelTypeId.Default));
        Assert.That(second.Result.SubmeshTypes[0], Is.EqualTo(new VoxelTypeId(7)));
    }

    [Test]
    public void MarchingCubes_DensityInterpolationUsesIsoSurfaceCrossing()
    {
        var volume = new VoxelVolume(-0.75f);
        volume.SetSample(10, 10, 10, 0.25f, VoxelTypeId.Default);

        VoxelMeshData midpoint = MarchingCubesMesher.Build(
            volume,
            0f,
            1f,
            MarchingCubesVertexPlacement.EdgeMidpoint);
        VoxelMeshData interpolated = MarchingCubesMesher.Build(
            volume,
            0f,
            1f,
            MarchingCubesVertexPlacement.DensityInterpolated);

        Assert.IsTrue(ContainsReferencedVertexAtX(
            midpoint,
            VoxelTypeId.Default,
            10.5f));
        Assert.IsTrue(ContainsReferencedVertexAtX(
            interpolated,
            VoxelTypeId.Default,
            10.25f));
        Assert.IsFalse(ContainsReferencedVertexAtX(
            interpolated,
            VoxelTypeId.Default,
            10.5f));
    }

    [Test]
    public void MarchingCubes_DensityInterpolationPreservesMaterialInset()
    {
        var volume = new VoxelVolume(-1f);
        var stone = new VoxelTypeId(2);
        var ore = new VoxelTypeId(3);
        volume.SetSample(10, 10, 10, 0.1f, stone);
        volume.SetSample(11, 10, 10, 0.9f, ore);

        VoxelMeshData data = MarchingCubesMesher.Build(
            volume,
            0f,
            1f,
            MarchingCubesVertexPlacement.DensityInterpolated);

        Assert.IsTrue(ContainsReferencedVertexAtX(data, stone, 10.45f));
        Assert.IsTrue(ContainsReferencedVertexAtX(data, ore, 10.55f));
    }

    [Test]
    public void MarchingCubes_ExposingOreDoesNotReshapeItsSurface()
    {
        var volume = new VoxelVolume(-1f);
        var stone = new VoxelTypeId(2);
        var ore = new VoxelTypeId(3);
        volume.SetSample(10, 10, 10, 0.1f, stone);
        volume.SetSample(11, 10, 10, 0.1f, ore);
        VoxelGroupMap groupMap = BuildGroupMap(
            new KeyValuePair<VoxelTypeId, VoxelGroup>(stone, VoxelGroup.Stone),
            new KeyValuePair<VoxelTypeId, VoxelGroup>(ore, VoxelGroup.Ore));

        VoxelMeshData embedded = MarchingCubesMesher.Build(
            volume,
            0f,
            1f,
            MarchingCubesVertexPlacement.DensityInterpolated,
            groupMap);
        List<Vector3> embeddedOreVertices = GetReferencedVertices(embedded, ore);

        volume.SetSample(10, 10, 10, -1f, VoxelTypeId.Air);
        VoxelMeshData exposed = MarchingCubesMesher.Build(
            volume,
            0f,
            1f,
            MarchingCubesVertexPlacement.DensityInterpolated,
            groupMap);
        List<Vector3> exposedOreVertices = GetReferencedVertices(exposed, ore);

        Assert.That(exposedOreVertices, Has.Count.EqualTo(embeddedOreVertices.Count));
        for (int i = 0; i < embeddedOreVertices.Count; i++)
        {
            Assert.That(
                Vector3.Distance(embeddedOreVertices[i], exposedOreVertices[i]),
                Is.LessThan(0.0001f),
                $"Ore vertex {i} moved when adjacent stone became air.");
        }
        Assert.IsTrue(ContainsReferencedVertexAtX(exposed, ore, 10.55f));
    }

    [Test]
    public void MarchingCubes_SameGroupTypesFormOneContinuousSurface()
    {
        var volume = new VoxelVolume(-1f);
        var brick = new VoxelTypeId(5);
        var weathered = new VoxelTypeId(6);
        volume.SetSample(10, 10, 10, 1f, brick);
        volume.SetSample(11, 10, 10, 1f, weathered);
        VoxelGroupMap groupMap = BuildGroupMap(
            new KeyValuePair<VoxelTypeId, VoxelGroup>(brick, VoxelGroup.Structure),
            new KeyValuePair<VoxelTypeId, VoxelGroup>(
                weathered,
                VoxelGroup.Structure));

        VoxelMeshData data = MarchingCubesMesher.Build(
            volume,
            0f,
            1f,
            MarchingCubesVertexPlacement.EdgeMidpoint,
            groupMap);

        // The shared face is interior to the group, so neither the inset seam nor
        // any surface is generated between the two bricks.
        Assert.IsFalse(ContainsAnyVertexAtX(data, 10.45f));
        Assert.IsFalse(ContainsAnyVertexAtX(data, 10.55f));
        // The group's outer hull still exists, spanning both samples.
        Assert.That(data.Vertices, Is.Not.Empty);
        Assert.IsTrue(ContainsAnyVertexAtX(data, 9.5f));
        Assert.IsTrue(ContainsAnyVertexAtX(data, 11.5f));
    }

    [Test]
    public void MarchingCubes_SameGroupSurfaceKeepsAMaterialPerVoxelType()
    {
        var volume = new VoxelVolume(-1f);
        var brick = new VoxelTypeId(5);
        var weathered = new VoxelTypeId(6);
        for (int x = 8; x <= 11; x++)
        {
            volume.SetSample(x, 10, 10, 1f, brick);
        }
        for (int x = 12; x <= 15; x++)
        {
            volume.SetSample(x, 10, 10, 1f, weathered);
        }
        VoxelGroupMap groupMap = BuildGroupMap(
            new KeyValuePair<VoxelTypeId, VoxelGroup>(brick, VoxelGroup.Structure),
            new KeyValuePair<VoxelTypeId, VoxelGroup>(
                weathered,
                VoxelGroup.Structure));

        VoxelMeshData data = MarchingCubesMesher.Build(
            volume,
            0f,
            1f,
            MarchingCubesVertexPlacement.EdgeMidpoint,
            groupMap);

        // Both palettes keep their own submesh so each still renders with its own
        // material, even though the geometry is one continuous surface.
        Assert.AreEqual(2, data.SubmeshCount);
        Assert.Greater(data.GetTriangles(brick).Count, 0);
        Assert.Greater(data.GetTriangles(weathered).Count, 0);
    }

    [Test]
    public void MarchingCubes_DifferentGroupsStillProduceAnInsetSeam()
    {
        var volume = new VoxelVolume(-1f);
        var brick = new VoxelTypeId(5);
        var stone = new VoxelTypeId(2);
        volume.SetSample(10, 10, 10, 1f, brick);
        volume.SetSample(11, 10, 10, 1f, stone);
        VoxelGroupMap groupMap = BuildGroupMap(
            new KeyValuePair<VoxelTypeId, VoxelGroup>(brick, VoxelGroup.Structure),
            new KeyValuePair<VoxelTypeId, VoxelGroup>(stone, VoxelGroup.Stone));

        VoxelMeshData data = MarchingCubesMesher.Build(
            volume,
            0f,
            1f,
            MarchingCubesVertexPlacement.EdgeMidpoint,
            groupMap);

        Assert.AreEqual(2, data.SubmeshCount);
        Assert.IsTrue(ContainsReferencedVertexAtX(data, brick, 10.45f));
        Assert.IsTrue(ContainsReferencedVertexAtX(data, stone, 10.55f));
    }

    [Test]
    public void MarchingCubes_TypesMissingFromTheCatalogKeepTheirOwnSurface()
    {
        var volume = new VoxelVolume(-1f);
        var known = new VoxelTypeId(5);
        var unknown = new VoxelTypeId(9);
        volume.SetSample(10, 10, 10, 1f, known);
        volume.SetSample(11, 10, 10, 1f, unknown);
        VoxelGroupMap groupMap = BuildGroupMap(
            new KeyValuePair<VoxelTypeId, VoxelGroup>(known, VoxelGroup.Structure));

        VoxelMeshData data = MarchingCubesMesher.Build(
            volume,
            0f,
            1f,
            MarchingCubesVertexPlacement.EdgeMidpoint,
            groupMap);

        // An unmapped type must not silently merge into a group it was never
        // assigned to, so the boundary is preserved.
        Assert.AreEqual(2, data.SubmeshCount);
        Assert.IsTrue(ContainsReferencedVertexAtX(data, known, 10.45f));
        Assert.IsTrue(ContainsReferencedVertexAtX(data, unknown, 10.55f));
    }

    [Test]
    public void GroupMap_ReadsGroupsFromVoxelTypeDefinitions()
    {
        VoxelTypeDefinition brick = CreateDefinition(5, VoxelGroup.Structure);
        VoxelTypeDefinition stone = CreateDefinition(2, VoxelGroup.Stone);
        VoxelTypeDefinition ore = CreateDefinition(3, VoxelGroup.Ore);
        try
        {
            VoxelGroupMap map = VoxelGroupMap.FromDefinitions(
                new[] { brick, stone, ore });

            Assert.IsTrue(map.IsConfigured);
            Assert.IsTrue(map.TryGetGroup(brick.TypeId, out VoxelGroup brickGroup));
            Assert.AreEqual(VoxelGroup.Structure, brickGroup);
            Assert.IsTrue(map.TryGetGroup(stone.TypeId, out VoxelGroup stoneGroup));
            Assert.AreEqual(VoxelGroup.Stone, stoneGroup);
            Assert.IsTrue(map.TryGetGroup(ore.TypeId, out VoxelGroup oreGroup));
            Assert.AreEqual(VoxelGroup.Ore, oreGroup);
            Assert.IsFalse(map.TryGetGroup(new VoxelTypeId(99), out _));
            Assert.AreNotEqual(
                map.GetGroupKey(brick.TypeId),
                map.GetGroupKey(stone.TypeId));
        }
        finally
        {
            Object.DestroyImmediate(brick);
            Object.DestroyImmediate(stone);
            Object.DestroyImmediate(ore);
        }
    }

    [Test]
    public void ProjectVoxelTypes_KeepVoxelTypeIdsUnique()
    {
        VoxelTypeCatalog catalog =
            AssetDatabase.LoadAssetAtPath<VoxelTypeCatalog>(
                ProjectAssetPaths.Config.VoxelCatalog);
        Assert.That(catalog, Is.Not.Null);

        // Two palettes sharing an id would collapse into one submesh, and a group
        // mismatch between them would go unnoticed.
        var seen = new HashSet<ushort>();
        foreach (VoxelTypeDefinition definition in catalog.Definitions)
        {
            Assert.That(definition, Is.Not.Null);
            Assert.IsTrue(
                seen.Add(definition.TypeId.Value),
                $"Duplicate voxel type id {definition.TypeId.Value} on {definition.name}.");
        }
    }

    private static VoxelTypeDefinition CreateDefinition(
        ushort type,
        VoxelGroup group)
    {
        VoxelTypeDefinition definition =
            ScriptableObject.CreateInstance<VoxelTypeDefinition>();
        definition.Configure(type, "Type " + type, 1);
        definition.ConfigureGroup(group);
        return definition;
    }

    private static VoxelGroupMap BuildGroupMap(
        params KeyValuePair<VoxelTypeId, VoxelGroup>[] pairs)
    {
        return VoxelGroupMap.FromPairs(pairs);
    }

    private static bool ContainsAnyVertexAtX(VoxelMeshData data, float expectedX)
    {
        foreach (Vector3 vertex in data.Vertices)
        {
            if (Mathf.Abs(vertex.x - expectedX) < 0.0001f)
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsReferencedVertexAtX(
        VoxelMeshData data,
        VoxelTypeId type,
        float expectedX)
    {
        foreach (int index in data.GetTriangles(type))
        {
            if (Mathf.Abs(data.Vertices[index].x - expectedX) < 0.0001f)
            {
                return true;
            }
        }
        return false;
    }

    private static List<Vector3> GetReferencedVertices(
        VoxelMeshData data,
        VoxelTypeId type)
    {
        var vertices = new List<Vector3>();
        foreach (int index in data.GetTriangles(type))
        {
            vertices.Add(data.Vertices[index]);
        }
        return vertices;
    }

    private static void AssertProjectedUv(
        Vector3 vertex,
        Vector2 actualUv,
        Vector3 faceNormal)
    {
        const float uvScale = 0.25f;
        float absoluteX = Mathf.Abs(faceNormal.x);
        float absoluteY = Mathf.Abs(faceNormal.y);
        float absoluteZ = Mathf.Abs(faceNormal.z);
        Vector2 expectedUv;
        if (absoluteX >= absoluteY && absoluteX >= absoluteZ)
        {
            expectedUv = new Vector2(vertex.z, vertex.y) * uvScale;
        }
        else if (absoluteY >= absoluteZ)
        {
            expectedUv = new Vector2(vertex.x, vertex.z) * uvScale;
        }
        else
        {
            expectedUv = new Vector2(vertex.x, vertex.y) * uvScale;
        }

        Assert.That(actualUv.x, Is.EqualTo(expectedUv.x).Within(0.0001f));
        Assert.That(actualUv.y, Is.EqualTo(expectedUv.y).Within(0.0001f));
    }
}
