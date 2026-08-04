using NUnit.Framework;
using Supernova.MinecraftCaves;
using UnityEngine;

namespace Supernova.Tests
{
    /// <summary>
    /// Guards the vertex-channel contract the blade shader depends on. These are
    /// pure geometry assertions, so a broken contract surfaces here rather than as
    /// grass that floats above the ground or refuses to sway.
    /// </summary>
    public sealed class CaveGrassBladeMeshTests
    {
        [Test]
        public void BladeMesh_PinsRootVerticesToTheGroundPlane()
        {
            Mesh mesh = CaveGrassBladeMeshBuilder.Build(
                CaveGrassBladeMeshSettings.Lod0);
            try
            {
                Vector3[] vertices = mesh.vertices;
                Vector2[] bladeUvs = mesh.uv;
                Assert.That(vertices.Length, Is.EqualTo(bladeUvs.Length));

                int rootVertexCount = 0;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (bladeUvs[i].y > 0f)
                    {
                        continue;
                    }
                    rootVertexCount++;
                    Assert.That(
                        vertices[i].y,
                        Is.EqualTo(0f).Within(1e-6f),
                        "Root vertices must sit at y == 0 so the shader can pin "
                        + "them; otherwise wind detaches blades from the terrain.");
                }
                Assert.That(rootVertexCount, Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BladeMesh_SpansRootToTipUvAndCarriesBladeData()
        {
            CaveGrassBladeMeshSettings settings = CaveGrassBladeMeshSettings.Lod0;
            Mesh mesh = CaveGrassBladeMeshBuilder.Build(settings);
            try
            {
                Vector2[] bladeUvs = mesh.uv;
                Vector2[] bladeData = mesh.uv2;
                Assert.That(bladeData.Length, Is.EqualTo(bladeUvs.Length));

                float maximumHeightRatio = 0f;
                float maximumBladeIndex = 0f;
                for (int i = 0; i < bladeUvs.Length; i++)
                {
                    Assert.That(bladeUvs[i].y, Is.InRange(0f, 1f));
                    Assert.That(bladeUvs[i].x, Is.InRange(0f, 1f));
                    Assert.That(bladeData[i].y, Is.GreaterThan(0f));
                    maximumHeightRatio = Mathf.Max(
                        maximumHeightRatio,
                        bladeUvs[i].y);
                    maximumBladeIndex = Mathf.Max(
                        maximumBladeIndex,
                        bladeData[i].x);
                }

                Assert.That(maximumHeightRatio, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(
                    maximumBladeIndex,
                    Is.EqualTo(settings.bladeCount - 1).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BladeMesh_LeavesInteractionChannelsEmpty()
        {
            Mesh mesh = CaveGrassBladeMeshBuilder.Build(
                CaveGrassBladeMeshSettings.Lod0);
            try
            {
                Assert.That(
                    mesh.colors.Length,
                    Is.Zero,
                    "COLOR is reserved for future trample/cut/burn data.");
                Assert.That(mesh.uv3.Length, Is.Zero);
                Assert.That(mesh.uv4.Length, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BladeMesh_IsSingleSidedWithOneQuadPerSegment()
        {
            CaveGrassBladeMeshSettings settings = CaveGrassBladeMeshSettings.Lod0;
            Mesh mesh = CaveGrassBladeMeshBuilder.Build(settings);
            try
            {
                int expectedVertices = settings.bladeCount
                    * (settings.segmentsPerBlade + 1)
                    * 2;
                int expectedTriangles = settings.bladeCount
                    * settings.segmentsPerBlade
                    * 2;
                Assert.That(mesh.vertexCount, Is.EqualTo(expectedVertices));
                Assert.That(
                    mesh.triangles.Length,
                    Is.EqualTo(expectedTriangles * 3),
                    "Blades are single sided; the shader flips backface normals "
                    + "instead of paying for duplicated geometry.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BladeMesh_IsDeterministicForTheSameSettings()
        {
            Mesh first = CaveGrassBladeMeshBuilder.Build(
                CaveGrassBladeMeshSettings.Lod0);
            Mesh second = CaveGrassBladeMeshBuilder.Build(
                CaveGrassBladeMeshSettings.Lod0);
            try
            {
                Assert.That(second.vertices, Is.EqualTo(first.vertices));
                Assert.That(second.uv, Is.EqualTo(first.uv));
                Assert.That(second.uv2, Is.EqualTo(first.uv2));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void CoarserLodTiers_WidenBladesToPreserveCoverage()
        {
            // Fewer blades at distance must be compensated with wider ones, or the
            // field visibly thins out as tiers switch.
            Assert.That(
                CaveGrassBladeMeshSettings.Lod1.bladeCount,
                Is.LessThan(CaveGrassBladeMeshSettings.Lod0.bladeCount));
            Assert.That(
                CaveGrassBladeMeshSettings.Lod2.bladeCount,
                Is.LessThan(CaveGrassBladeMeshSettings.Lod1.bladeCount));
            Assert.That(
                CaveGrassBladeMeshSettings.Lod1.rootHalfWidth,
                Is.GreaterThan(CaveGrassBladeMeshSettings.Lod0.rootHalfWidth));
            Assert.That(
                CaveGrassBladeMeshSettings.Lod2.rootHalfWidth,
                Is.GreaterThan(CaveGrassBladeMeshSettings.Lod1.rootHalfWidth));
            Assert.That(
                CaveGrassBladeMeshSettings.Lod2.segmentsPerBlade,
                Is.LessThan(CaveGrassBladeMeshSettings.Lod0.segmentsPerBlade));
        }

        [Test]
        public void ZeroedSettings_AreRepairedBeforeUse()
        {
            // Structs deserialise all-zero from assets written before these fields
            // existed; building from one must not produce a degenerate mesh.
            var zeroed = default(CaveGrassBladeMeshSettings);
            Mesh mesh = CaveGrassBladeMeshBuilder.Build(zeroed);
            try
            {
                Assert.That(mesh.vertexCount, Is.GreaterThan(0));
                Assert.That(mesh.bounds.size.y, Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BladeMesh_BoundsCoverWindDisplacedTips()
        {
            // Instanced draws frustum-cull against mesh bounds, so a tip blown
            // outside them would be culled while still visible.
            CaveGrassBladeMeshSettings settings = CaveGrassBladeMeshSettings.Lod0;
            Mesh mesh = CaveGrassBladeMeshBuilder.Build(settings);
            try
            {
                Assert.That(
                    mesh.bounds.extents.x,
                    Is.GreaterThan(settings.rootHalfWidth * 2f));
                Assert.That(
                    mesh.bounds.max.y,
                    Is.GreaterThan(settings.height));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
