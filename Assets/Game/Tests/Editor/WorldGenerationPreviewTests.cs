using System.Collections.Generic;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using UnityEngine;

public sealed class WorldGenerationPreviewTests
{
    [Test]
    public void PreviewStreamingOffsets_UseRadiusEightDisk()
    {
        IReadOnlyList<Vector3Int> offsets =
            MinecraftCaveInfiniteWorld.PreviewStreamingOffsets;
        var unique = new HashSet<Vector3Int>(offsets);

        Assert.That(offsets.Count, Is.EqualTo(197));
        Assert.That(unique.Count, Is.EqualTo(offsets.Count));
        Assert.That(offsets, Does.Contain(new Vector3Int(8, 0, 0)));
        Assert.That(offsets, Does.Contain(new Vector3Int(-8, 0, 0)));
        Assert.That(offsets, Does.Contain(new Vector3Int(0, 0, 8)));
        Assert.That(offsets, Does.Contain(new Vector3Int(0, 0, -8)));
        Assert.That(unique.Contains(new Vector3Int(8, 0, 8)), Is.False);

        foreach (Vector3Int offset in offsets)
        {
            Assert.That(offset.y, Is.Zero);
            Assert.That(offset.x * offset.x + offset.z * offset.z,
                Is.LessThanOrEqualTo(64));
        }
    }
}

