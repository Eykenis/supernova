using System.Threading.Tasks;
using NUnit.Framework;
using Supernova.Voxels;
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
