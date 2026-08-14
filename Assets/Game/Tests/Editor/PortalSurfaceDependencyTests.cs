using System.Reflection;
using NUnit.Framework;
using Supernova.PortalExample;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class PortalSurfaceDependencyTests
    {
        [Test]
        public void SpawnedPortal_DisappearsWithDestroyedTerrainMesh()
        {
            GameObject bridgeObject = new GameObject("Portal Bridge");
            GameObject landingObject = new GameObject("Landing Cell Portal");
            GameObject templateObject = new GameObject("Checkpoint Template");
            GameObject terrainObject = new GameObject("Terrain Mesh Section");
            Mesh supportMesh = new Mesh();
            landingObject.transform.SetParent(bridgeObject.transform, false);
            templateObject.transform.SetParent(bridgeObject.transform, false);

            try
            {
                supportMesh.vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.forward
                };
                supportMesh.triangles = new[] { 0, 1, 2 };
                MeshCollider supportCollider =
                    terrainObject.AddComponent<MeshCollider>();
                supportCollider.sharedMesh = supportMesh;

                PortalExampleGate landingGate =
                    landingObject.AddComponent<PortalExampleGate>();
                PortalExampleGate templateGate =
                    templateObject.AddComponent<PortalExampleGate>();
                DenseJigsawPortalBridge bridge =
                    bridgeObject.AddComponent<DenseJigsawPortalBridge>();
                bridge.Configure(
                    null,
                    null,
                    null,
                    landingGate,
                    templateGate);

                Assert.That(
                    bridge.TryCreateSpawnCheckpointPortal(
                        supportCollider,
                        Vector3.zero,
                        Vector3.up,
                        Vector3.forward,
                        out PortalExampleGate portal),
                    Is.True);
                PortalSurfaceDependency dependency =
                    portal.GetComponent<PortalSurfaceDependency>();
                Assert.That(dependency, Is.Not.Null);
                Assert.That(
                    dependency.SupportCollider,
                    Is.SameAs(supportCollider));
                Assert.That(dependency.SupportMesh, Is.SameAs(supportMesh));

                Object.DestroyImmediate(supportMesh);
                supportMesh = null;
                MethodInfo lateUpdate = typeof(PortalSurfaceDependency)
                    .GetMethod(
                        "LateUpdate",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(lateUpdate, Is.Not.Null);
                lateUpdate.Invoke(dependency, null);

                Assert.That(portal == null, Is.True);
                Assert.That(bridge.SpawnedCheckpointGates, Is.Empty);
            }
            finally
            {
                if (supportMesh != null)
                {
                    Object.DestroyImmediate(supportMesh);
                }
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(bridgeObject);
            }
        }
        [Test]
        // Voxel chunk faces can be wound either way, and a MeshCollider only
        // reports front-face hits, so the surface probe must survive both.
        public void SpawnedPortal_DisappearsWhenAnchoredSurfaceIsMinedAway(
            [Values(false, true)] bool flipWinding)
        {
            GameObject bridgeObject = new GameObject("Portal Bridge");
            GameObject landingObject = new GameObject("Landing Cell Portal");
            GameObject templateObject = new GameObject("Checkpoint Template");
            GameObject terrainObject = new GameObject("Terrain Mesh Section");
            Mesh supportMesh = new Mesh();
            landingObject.transform.SetParent(bridgeObject.transform, false);
            templateObject.transform.SetParent(bridgeObject.transform, false);

            try
            {
                // A wide slab whose top face sits at y = 0, matching the surface a
                // PortalGun shot would anchor to.
                supportMesh.vertices = new[]
                {
                    new Vector3(-4f, 0f, -4f),
                    new Vector3(4f, 0f, -4f),
                    new Vector3(4f, 0f, 4f),
                    new Vector3(-4f, 0f, 4f)
                };
                supportMesh.triangles = flipWinding
                    ? new[] { 0, 1, 2, 0, 2, 3 }
                    : new[] { 0, 2, 1, 0, 3, 2 };
                MeshCollider supportCollider =
                    terrainObject.AddComponent<MeshCollider>();
                supportCollider.sharedMesh = supportMesh;

                PortalExampleGate landingGate =
                    landingObject.AddComponent<PortalExampleGate>();
                PortalExampleGate templateGate =
                    templateObject.AddComponent<PortalExampleGate>();
                DenseJigsawPortalBridge bridge =
                    bridgeObject.AddComponent<DenseJigsawPortalBridge>();
                bridge.Configure(null, null, null, landingGate, templateGate);

                Assert.That(
                    bridge.TryCreateSpawnCheckpointPortal(
                        supportCollider,
                        Vector3.zero,
                        Vector3.up,
                        Vector3.forward,
                        out PortalExampleGate portal),
                    Is.True);
                PortalSurfaceDependency dependency =
                    portal.GetComponent<PortalSurfaceDependency>();
                Assert.That(dependency, Is.Not.Null);
                Assert.That(dependency.HasSurfaceAnchor, Is.True,
                    "A surface-anchored portal must record where it was placed.");

                MethodInfo lateUpdate = typeof(PortalSurfaceDependency)
                    .GetMethod(
                        "LateUpdate",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(lateUpdate, Is.Not.Null);

                // The intact surface must keep the portal alive.
                lateUpdate.Invoke(dependency, null);
                Assert.That(portal == null, Is.False,
                    "An intact surface must not remove the portal.");

                // Mining rewrites the chunk mesh in place: same Mesh instance and
                // same GameObject, but the surface under the portal is carved
                // away, leaving geometry only off to the side.
                supportMesh.triangles = new int[0];
                supportMesh.vertices = new[]
                {
                    new Vector3(20f, 0f, 20f),
                    new Vector3(24f, 0f, 20f),
                    new Vector3(24f, 0f, 24f),
                    new Vector3(20f, 0f, 24f)
                };
                supportMesh.triangles = flipWinding
                    ? new[] { 0, 1, 2, 0, 2, 3 }
                    : new[] { 0, 2, 1, 0, 3, 2 };
                supportCollider.sharedMesh = supportMesh;

                lateUpdate.Invoke(dependency, null);

                Assert.That(portal == null, Is.True,
                    "Mining the anchored surface must remove the portal.");
                Assert.That(bridge.SpawnedCheckpointGates, Is.Empty);
            }
            finally
            {
                if (supportMesh != null)
                {
                    Object.DestroyImmediate(supportMesh);
                }
                Object.DestroyImmediate(terrainObject);
                Object.DestroyImmediate(bridgeObject);
            }
        }
    }
}
