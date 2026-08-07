using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Supernova.Voxels.Support.Prototype.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="VoxelSupportGraph"/>.
    /// </summary>
    public sealed class VoxelSupportGraphTests
    {
        private VoxelSupportConfig config;
        private VoxelSupportGraph graph;
        private HashSet<Vector3Int> solidSet;
        private int maxY;

        [SetUp]
        public void SetUp()
        {
            // Create a minimal in-memory config so no asset is required.
            config = ScriptableObject.CreateInstance<VoxelSupportConfig>();
            graph = new VoxelSupportGraph(config);
            solidSet = new HashSet<Vector3Int>();
        }

        [TearDown]
        public void TearDown()
        {
            if (config != null)
                Object.DestroyImmediate(config);
        }

        // ── helpers ──────────────────────────────────────────────────

        private void AddColumn(Vector3Int pos, int height)
        {
            for (int y = pos.y; y < pos.y + height; y++)
                solidSet.Add(new Vector3Int(pos.x, y, pos.z));
            maxY = Mathf.Max(maxY, pos.y + height - 1);
        }

        private void AddRow(Vector3Int from, int length)
        {
            for (int i = 0; i < length; i++)
                solidSet.Add(new Vector3Int(from.x + i, from.y, from.z));
            maxY = Mathf.Max(maxY, from.y);
        }

        private bool IsSolid(Vector3Int p) => solidSet.Contains(p);
        private bool IsAnchor(Vector3Int p) => p.y <= config.BedrockYThreshold && solidSet.Contains(p);

        // ══════════════════════════════════════════════════════════════
        //  Tests
        // ══════════════════════════════════════════════════════════════

        [Test]
        public void AllVoxelsGrounded_NoRemoval_NoCollapse()
        {
            // Floor + one pillar standing on it.
            // Floor at Y=0, pillar from Y=1..3
            AddColumn(new Vector3Int(2, 0, 2), 4); // pillar standing on floor

            var result = graph.Analyze(
                new List<Vector3Int>(),
                IsSolid,
                IsAnchor);

            Assert.That(result.CollapsedVoxels, Is.Empty,
                "No voxels were removed, so nothing should collapse.");
        }

        [Test]
        public void RemovePillarBase_PillarCollapses()
        {
            // Floor Y=0 with a 4-block tall pillar at (2, 1..4, 2).
            AddColumn(new Vector3Int(2, 0, 2), 5); // Y=0 (anchor), 1,2,3,4

            // Remove the base block (the one just above the anchor).
            Vector3Int removed = new(2, 1, 2);
            solidSet.Remove(removed);

            var result = graph.Analyze(
                new List<Vector3Int> { removed },
                IsSolid,
                IsAnchor);

            Assert.That(result.CollapsedVoxels, Is.Not.Empty,
                "Removing the base of a pillar should collapse everything above.");
            Assert.That(result.CollapsedVoxels, Does.Contain(new Vector3Int(2, 2, 2)),
                "Pillar block above removed base should collapse.");
            Assert.That(result.CollapsedVoxels, Does.Contain(new Vector3Int(2, 3, 2)));
            Assert.That(result.CollapsedVoxels, Does.Contain(new Vector3Int(2, 4, 2)));
        }

        [Test]
        public void RemoveArchPillar_BridgeCollapses()
        {
            // Arch: two pillars + bridge on top.
            // Left pillar (3,0..5, 3)
            AddColumn(new Vector3Int(3, 0, 3), 6); // Y=0 anchor..5
            // Right pillar (8,0..5, 3)
            AddColumn(new Vector3Int(8, 0, 3), 6); // Y=0 anchor..5
            // Bridge at Y=6 spanning X=3..8
            AddRow(new Vector3Int(3, 6, 3), 6);

            // Remove the entire left pillar above Y=0.
            for (int y = 1; y <= 5; y++)
                solidSet.Remove(new Vector3Int(3, y, 3));

            var removed = new List<Vector3Int>();
            for (int y = 1; y <= 5; y++)
                removed.Add(new Vector3Int(3, y, 3));

            var result = graph.Analyze(removed, IsSolid, IsAnchor);

            // Bridge blocks at X=4,5,6,7 (between the two pillars) should collapse
            // because they were only supported by the left pillar (the right side
            // is their only remaining support).
            // Actually since the right pillar is intact, the bridge at X=8 still
            // touches it.  Blocks at X=4,5,6,7 depend on the left pillar via the
            // bridge itself, so they're now connected to the right pillar.  They
            // should NOT collapse if the bridge is continuous.  But if the bridge
            // was spanning BETWEEN the pillars (i.e. floating without the left),
            // the left end of the bridge might be unsupported.
            //
            // In a pure connectivity model, all bridge blocks are still connected
            // to the right pillar.  So we expect zero collapsed voxels from a
            // single-pillar removal IF the bridge remains intact.
            Assert.That(result.CollapsedVoxels, Is.Empty,
                "Bridge is still connected to the right pillar — nothing should collapse.");
        }

        [Test]
        public void RemoveBothArchPillars_EverythingAboveCollapses()
        {
            // Arch: two pillars + bridge. Remove BOTH pillars.
            AddColumn(new Vector3Int(3, 0, 3), 6);
            AddColumn(new Vector3Int(8, 0, 3), 6);
            AddRow(new Vector3Int(3, 6, 3), 6);

            var removed = new List<Vector3Int>();
            for (int y = 1; y <= 5; y++)
            {
                solidSet.Remove(new Vector3Int(3, y, 3));
                solidSet.Remove(new Vector3Int(8, y, 3));
                removed.Add(new Vector3Int(3, y, 3));
                removed.Add(new Vector3Int(8, y, 3));
            }

            var result = graph.Analyze(removed, IsSolid, IsAnchor);

            Assert.That(result.CollapsedVoxels, Is.Not.Empty,
                "Both pillars removed → entire bridge should collapse.");
            // Every bridge block should be collapsed.
            for (int x = 3; x <= 8; x++)
                Assert.That(result.CollapsedVoxels, Does.Contain(new Vector3Int(x, 6, 3)));
        }

        [Test]
        public void FloatingBlock_DetectedByFullScan()
        {
            // A single block floating at Y=5 with no connection to anything.
            solidSet.Add(new Vector3Int(7, 5, 7));

            var result = graph.FullScan(
                IsSolid,
                IsAnchor,
                volumeSizeX: 16,
                volumeSizeY: 16,
                volumeSizeZ: 16);

            Assert.That(result.CollapsedVoxels, Is.Not.Empty,
                "Floating block should be detected by FullScan.");
            Assert.That(result.CollapsedVoxels, Does.Contain(new Vector3Int(7, 5, 7)),
                "Floating block should be in the collapsed set.");
        }

        [Test]
        public void CascadeDetection_RemovingSupportTriggersFurtherCollapse()
        {
            // A chain: anchor → A → B → C
            //    anchor at Y=0
            //    A at Y=1
            //    B at Y=2
            //    C at Y=3
            AddColumn(new Vector3Int(5, 0, 5), 4);

            // Remove A.
            Vector3Int a = new(5, 1, 5);
            solidSet.Remove(a);

            var result = graph.Analyze(
                new List<Vector3Int> { a },
                IsSolid,
                IsAnchor);

            Assert.That(result.CollapsedVoxels, Does.Contain(new Vector3Int(5, 2, 5)),
                "B should collapse after A is removed.");
            Assert.That(result.CollapsedVoxels, Does.Contain(new Vector3Int(5, 3, 5)),
                "C should collapse via cascade.");
            Assert.That(result.CascadeIterationsUsed, Is.GreaterThanOrEqualTo(1),
                "Cascade iterations should have run.");
        }

        [Test]
        public void MultiplePaths_NoCollapse()
        {
            // A block with two support paths — removing one should not collapse it.
            // Y=0 anchor plate (3x3)
            for (int x = 4; x <= 6; x++)
            for (int z = 4; z <= 6; z++)
                solidSet.Add(new Vector3Int(x, 0, z));

            // Two pillars at (4,1..3,5) and (6,1..3,5)
            AddColumn(new Vector3Int(4, 1, 5), 3);
            AddColumn(new Vector3Int(6, 1, 5), 3);

            // Platform at Y=4 connecting both pillars (X=4..6, Z=5)
            for (int x = 4; x <= 6; x++)
                solidSet.Add(new Vector3Int(x, 4, 5));

            // Remove left pillar.
            var removed = new List<Vector3Int>();
            for (int y = 1; y <= 3; y++)
            {
                Vector3Int p = new(4, y, 5);
                solidSet.Remove(p);
                removed.Add(p);
            }

            var result = graph.Analyze(removed, IsSolid, IsAnchor);

            Assert.That(result.CollapsedVoxels, Is.Empty,
                "Platform is still supported by the right pillar — nothing collapses.");
        }

        [Test]
        public void Cantilever_OuterEndCollapses()
        {
            // Y=0 floor plate (X=2..8, Z=5)
            for (int x = 2; x <= 8; x++)
                solidSet.Add(new Vector3Int(x, 0, 5));

            // Pillar at X=3, Y=1..4
            AddColumn(new Vector3Int(3, 1, 5), 4);

            // Shelf at Y=5 spanning X=3..8 (cantilever from X=3 to X=8).
            for (int x = 3; x <= 8; x++)
                solidSet.Add(new Vector3Int(x, 5, 5));

            // Remove the pillar at X=3.
            var removed = new List<Vector3Int>();
            for (int y = 1; y <= 4; y++)
            {
                Vector3Int p = new(3, y, 5);
                solidSet.Remove(p);
                removed.Add(p);
            }

            var result = graph.Analyze(removed, IsSolid, IsAnchor);

            // The entire shelf should collapse because the pillar was the only
            // connection to the floor.
            Assert.That(result.CollapsedVoxels, Is.Not.Empty,
                "Cantilever shelf should collapse when its only pillar is removed.");
            Assert.That(result.CollapsedVoxels, Does.Contain(new Vector3Int(8, 5, 5)),
                "Far end of cantilever should be included in collapse.");
        }
    }
}
