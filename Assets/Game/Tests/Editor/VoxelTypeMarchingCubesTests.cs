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
}
