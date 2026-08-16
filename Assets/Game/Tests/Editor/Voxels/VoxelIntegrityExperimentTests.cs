using System.Collections.Generic;
using NUnit.Framework;

using Supernova.MinecraftCaves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Supernova.Voxels.Integrity;
using Supernova.Voxels.IntegrityExperiment;
using UnityEngine;

namespace Supernova.Voxels.Tests
{
    public sealed class VoxelIntegrityExperimentTests
    {
        [Test]
        public void Analyze_IsolatedLShape_ReturnsOneCompleteComponent()
        {
            var map = new TestMap(
                new Vector3Int(0, 0, 0),
                new Vector3Int(12, 12, 12));
            var removed = new Vector3Int(5, 5, 5);
            map.SetSolid(new Vector3Int(6, 5, 5));
            map.SetSolid(new Vector3Int(7, 5, 5));
            map.SetSolid(new Vector3Int(6, 6, 5));

            VoxelIntegrityResult result = new VoxelIntegritySearch().Analyze(
                new[] { removed },
                map);

            Assert.That(result.Components, Has.Count.EqualTo(1));
            VoxelIntegrityComponent component = result.Components[0];
            Assert.That(component.IsSupported, Is.False);
            Assert.That(component.Voxels, Has.Count.EqualTo(3));
            Assert.That(component.Voxels, Does.Contain(new Vector3Int(6, 5, 5)));
            Assert.That(component.Voxels, Does.Contain(new Vector3Int(7, 5, 5)));
            Assert.That(component.Voxels, Does.Contain(new Vector3Int(6, 6, 5)));
        }

        [Test]
        public void Analyze_ComponentReachesBedrock_IsSupported()
        {
            var map = new TestMap(
                Vector3Int.zero,
                new Vector3Int(12, 12, 12));
            map.SetSolid(new Vector3Int(6, 5, 5));
            map.SetSolid(new Vector3Int(7, 5, 5));
            map.SetBedrock(new Vector3Int(8, 5, 5));

            VoxelIntegrityComponent component = new VoxelIntegritySearch()
                .Analyze(new[] { new Vector3Int(5, 5, 5) }, map)
                .Components[0];

            Assert.That(
                component.Support,
                Is.EqualTo(VoxelIntegritySupport.StructuralSupport));
            Assert.That(
                component.SupportCoordinate,
                Is.EqualTo(new Vector3Int(8, 5, 5)));
        }

        [Test]
        public void Analyze_SupportedSeedsShareVisited_RunOneFill()
        {
            var map = new TestMap(
                Vector3Int.zero,
                new Vector3Int(16, 16, 16),
                false);
            var removed = new Vector3Int(5, 5, 5);
            for (int z = -2; z <= 2; z++)
            {
                for (int y = -2; y <= 2; y++)
                {
                    for (int x = -2; x <= 2; x++)
                    {
                        int distance = Mathf.Abs(x)
                            + Mathf.Abs(y)
                            + Mathf.Abs(z);
                        if (distance == 0 || distance > 2)
                            continue;

                        map.SetSolid(removed + new Vector3Int(x, y, z));
                    }
                }
            }

            for (int x = 7; x <= 12; x++)
                map.SetSolid(new Vector3Int(x, 5, 5));
            map.SetBedrock(new Vector3Int(13, 5, 5));

            VoxelIntegrityResult result = new VoxelIntegritySearch().Analyze(
                new[] { removed },
                map);

            Assert.That(result.SeedCount, Is.EqualTo(6));
            Assert.That(result.FillCount, Is.EqualTo(1));
            Assert.That(result.Components, Has.Count.EqualTo(1));
            Assert.That(
                result.Components[0].Support,
                Is.EqualTo(VoxelIntegritySupport.StructuralSupport));
            Assert.That(result.VisitedVoxelCount, Is.GreaterThan(6));
        }


        [Test]
        public void Analyze_ComponentTouchesMissingLoadedRegion_IsSupported()
        {
            var map = new TestMap(
                Vector3Int.zero,
                new Vector3Int(3, 8, 8));
            map.SetSolid(new Vector3Int(2, 4, 4));

            VoxelIntegrityComponent component = new VoxelIntegritySearch()
                .Analyze(new[] { new Vector3Int(1, 4, 4) }, map)
                .Components[0];

            Assert.That(
                component.Support,
                Is.EqualTo(VoxelIntegritySupport.UnloadedBoundary));
            Assert.That(
                component.SupportCoordinate,
                Is.EqualTo(new Vector3Int(3, 4, 4)));
        }

        [Test]
        public void Analyze_TwoDisconnectedAffectedSets_ReturnsTwoComponents()
        {
            var map = new TestMap(
                new Vector3Int(-8, -8, -8),
                new Vector3Int(9, 9, 9));
            map.SetSolid(Vector3Int.left);
            map.SetSolid(Vector3Int.right);

            VoxelIntegrityResult result = new VoxelIntegritySearch().Analyze(
                new[] { Vector3Int.zero },
                map);

            Assert.That(result.Components, Has.Count.EqualTo(2));
            Assert.That(result.Components[0].IsSupported, Is.False);
            Assert.That(result.Components[1].IsSupported, Is.False);
            Assert.That(result.Components[0].Voxels, Has.Count.EqualTo(1));
            Assert.That(result.Components[1].Voxels, Has.Count.EqualTo(1));
        }

        [Test]
        public void Analyze_DynamicSixFaceHeuristic_ReachesNearestBoundaryFirst()
        {
            var map = new TestMap(
                Vector3Int.zero,
                new Vector3Int(21, 21, 21));
            for (int x = 0; x <= 10; x++)
                map.SetSolid(new Vector3Int(x, 10, 10));

            VoxelIntegrityComponent component = new VoxelIntegritySearch()
                .Analyze(new[] { new Vector3Int(1, 9, 10) }, map)
                .Components[0];

            Assert.That(
                component.Support,
                Is.EqualTo(VoxelIntegritySupport.UnloadedBoundary));
            Assert.That(
                component.SupportCoordinate,
                Is.EqualTo(new Vector3Int(-1, 10, 10)));
            Assert.That(
                component.Voxels,
                Has.Count.EqualTo(2),
                "A* should expand from X=1 toward the nearer -X face before the long +X branch.");
        }

        [Test]
        public void Analyze_SearchLimit_IsConservativelySupported()
        {
            var map = new TestMap(
                new Vector3Int(-200, -2, -2),
                new Vector3Int(201, 3, 3));
            for (int x = 1; x <= 100; x++)
                map.SetSolid(new Vector3Int(x, 0, 0));

            VoxelIntegrityComponent component = new VoxelIntegritySearch(64)
                .Analyze(new[] { Vector3Int.zero }, map)
                .Components[0];

            Assert.That(
                component.Support,
                Is.EqualTo(VoxelIntegritySupport.SearchLimit));
            Assert.That(component.IsSupported, Is.True);
            Assert.That(component.Voxels, Has.Count.EqualTo(64));
        }

        [Test]
        public void InfiniteMap_DistinguishesAirBedrockAndUnloadedChunk()
        {
            var world = new InfiniteVoxelWorld();
            InfiniteVoxelChunk chunk = world.EnsureChunk(Vector2Int.zero);
            chunk.Data.Fill(-1f, VoxelTypeId.Air);
            var bedrock = new VoxelTypeId(7);
            world.SetVoxel(31, 4, 3, 1f, VoxelTypeId.Default);
            world.SetVoxel(2, 0, 3, 1f, bedrock);
            var map = new InfiniteVoxelIntegrityMap(world, 0f, bedrock);

            Assert.That(
                map.GetCell(new Vector3Int(30, 4, 3)),
                Is.EqualTo(VoxelIntegrityCell.Air));
            Assert.That(
                map.GetCell(new Vector3Int(31, 4, 3)),
                Is.EqualTo(VoxelIntegrityCell.Solid));
            Assert.That(
                map.GetCell(new Vector3Int(2, 0, 3)),
                Is.EqualTo(VoxelIntegrityCell.StructuralSupport));
            Assert.That(
                map.GetCell(new Vector3Int(32, 4, 3)),
                Is.EqualTo(VoxelIntegrityCell.Unloaded));
        }

        [Test]
        public void InfiniteMap_ConfiguredSolidStoneStopsSearchImmediately()
        {
            var world = new InfiniteVoxelWorld();
            InfiniteVoxelChunk chunk = world.EnsureChunk(Vector2Int.zero);
            chunk.Data.Fill(-1f, VoxelTypeId.Air);
            var ordinaryStone = new VoxelTypeId(2);
            var solidStone = new VoxelTypeId(16);
            var removed = new Vector3Int(5, 5, 5);
            world.SetVoxel(6, 5, 5, 1f, ordinaryStone);
            world.SetVoxel(7, 5, 5, 1f, solidStone);

            var map = new InfiniteVoxelIntegrityMap(
                world,
                0f,
                new[] { solidStone });
            VoxelIntegrityComponent component = new VoxelIntegritySearch()
                .Analyze(new[] { removed }, map)
                .Components[0];

            Assert.That(
                component.Support,
                Is.EqualTo(VoxelIntegritySupport.StructuralSupport));
            Assert.That(
                component.SupportCoordinate,
                Is.EqualTo(new Vector3Int(7, 5, 5)));
            Assert.That(
                component.Voxels,
                Has.Count.EqualTo(1),
                "Solid Stone must terminate traversal instead of joining the fill.");
        }


        [Test]
        public void InfiniteWorld_SampleChangedReportsExactMutation()
        {
            var world = new InfiniteVoxelWorld();
            InfiniteVoxelChunk chunk = world.EnsureChunk(Vector2Int.zero);
            chunk.Data.Fill(-1f, VoxelTypeId.Air);
            var coordinate = new Vector3Int(2, 3, 4);
            var solidType = new VoxelTypeId(9);
            int changeCount = 0;
            Vector3Int reportedCoordinate = default;
            VoxelSample previous = default;
            VoxelSample current = default;
            world.SampleChanged += (changed, before, after) =>
            {
                changeCount++;
                reportedCoordinate = changed;
                previous = before;
                current = after;
            };

            world.SetVoxel(
                coordinate.x,
                coordinate.y,
                coordinate.z,
                1f,
                solidType);

            Assert.That(changeCount, Is.EqualTo(1));
            Assert.That(reportedCoordinate, Is.EqualTo(coordinate));
            Assert.That(previous.IsSolid(), Is.False);
            Assert.That(current.IsSolid(), Is.True);
            Assert.That(current.Type, Is.EqualTo(solidType));

            world.SetVoxel(
                coordinate.x,
                coordinate.y,
                coordinate.z,
                1f,
                solidType);
            Assert.That(
                changeCount,
                Is.EqualTo(1),
                "Writing an identical sample must not report a mutation.");

            world.SetVoxel(
                coordinate.x,
                coordinate.y,
                coordinate.z,
                -1f,
                VoxelTypeId.Air);
            Assert.That(changeCount, Is.EqualTo(2));
            Assert.That(previous.IsSolid(), Is.True);
            Assert.That(current.IsSolid(), Is.False);
        }


        [Test]
        public void RigidbodyFactory_LShapeUsesConvexMeshCompound()
        {
            var component = new List<Vector3Int>
            {
                Vector3Int.zero,
                Vector3Int.right,
                Vector3Int.up,
            };
            GameObject root = null;
            Mesh mesh = null;
            try
            {
                root = VoxelIntegrityRigidbodyFactory.Create(
                    component,
                    1f,
                    null,
                    null);
                mesh = root.GetComponent<MeshFilter>().sharedMesh;

                Assert.That(root.GetComponents<Rigidbody>(), Has.Length.EqualTo(1));
                Assert.That(root.GetComponents<BoxCollider>(), Is.Empty);
                MeshCollider[] colliders =
                    root.GetComponents<MeshCollider>();
                Assert.That(colliders, Is.Not.Empty);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Assert.That(colliders[i].convex, Is.True);
                    Assert.That(colliders[i].sharedMesh, Is.Not.Null);
                    Assert.That(
                        colliders[i].sharedMesh.triangles.Length / 3,
                        Is.LessThanOrEqualTo(255));
                }
                Assert.That(
                    mesh.triangles,
                    Has.Length.EqualTo(84),
                    "Three cubes with two shared faces expose 14 quads (28 triangles).");
            }
            finally
            {
                if (root != null)
                    Object.DestroyImmediate(root);
                if (mesh != null)
                    Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void MarchingRigidbody_PreservesDensitySurfaceAndVolumeMass()
        {
            var world = new InfiniteVoxelWorld();
            InfiniteVoxelChunk chunk = world.EnsureChunk(Vector2Int.zero);
            chunk.Data.Fill(-1f, VoxelTypeId.Air);
            var coordinate = new Vector3Int(5, 5, 5);
            world.SetVoxel(
                coordinate.x,
                coordinate.y,
                coordinate.z,
                3f,
                VoxelTypeId.Default);
            var componentSet = new HashSet<Vector3Int>
            {
                coordinate,
            };
            VoxelMeshData meshData = MarchingCubesMesher.BuildComponent(
                world,
                componentSet,
                0f,
                1f,
                MarchingCubesVertexPlacement.DensityInterpolated);
            VoxelMeshMassProperties properties =
                VoxelIntegrityRigidbodyFactory.CalculateMassProperties(
                    meshData.Vertices,
                    meshData.Triangles);
            float expectedMass = properties.Volume * 10f;
            GameObject root = null;
            Mesh mesh = null;
            try
            {
                root =
                    VoxelIntegrityRigidbodyFactory.CreateFromMarchingCubes(
                        new List<Vector3Int>(componentSet),
                        meshData,
                        1f,
                        null,
                        null,
                        expectedMass);
                mesh = root.GetComponent<MeshFilter>().sharedMesh;
                Rigidbody body = root.GetComponent<Rigidbody>();

                Assert.That(
                    meshData.TriangleCount,
                    Is.EqualTo(8),
                    "One density sample is an interpolated octahedron, not a cube.");
                Assert.That(
                    properties.Volume,
                    Is.EqualTo(0.5625f).Within(0.0001f));
                Assert.That(
                    body.mass,
                    Is.EqualTo(expectedMass).Within(0.0001f));
                Assert.That(
                    mesh.triangles.Length,
                    Is.EqualTo(meshData.Triangles.Count));
                Assert.That(
                    mesh.triangles.Length,
                    Is.Not.EqualTo(36),
                    "The rigidbody render mesh must not fall back to cube faces.");
                Assert.That(
                    root.transform.position,
                    Is.EqualTo((Vector3)coordinate));
                Assert.That(body.centerOfMass, Is.EqualTo(Vector3.zero));
                Assert.That(root.GetComponents<BoxCollider>(), Is.Empty);
                Assert.That(root.GetComponents<MeshCollider>(), Is.Not.Empty);
                Assert.That(root.GetComponent<MeshCollider>().convex, Is.True);
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
                if (mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
        }


        [Test]
        public void IsolatedScene_UsesRealWorldPlayerAndIntegrityBridgeOnly()
        {
            Scene scene = SceneManager.GetSceneByPath(
                ProjectAssetPaths.Scenes.VoxelIntegrityExperiment);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    ProjectAssetPaths.Scenes.VoxelIntegrityExperiment,
                    OpenSceneMode.Additive);
            }

            try
            {
                MinecraftCaveInfiniteWorld world =
                    FindComponentInScene<MinecraftCaveInfiniteWorld>(scene);
                VoxelIntegrityWorldBridge bridge =
                    FindComponentInScene<VoxelIntegrityWorldBridge>(scene);
                VoxelPlayerInteractor interactor =
                    FindComponentInScene<VoxelPlayerInteractor>(scene);
                VoxelIntegrityExperimentController toyController =
                    FindComponentInScene<VoxelIntegrityExperimentController>(scene);

                Assert.That(world, Is.Not.Null);
                Assert.That(bridge, Is.Not.Null);
                Assert.That(interactor, Is.Not.Null);
                Assert.That(toyController, Is.Null);
                Assert.That(bridge.SourceTerrain, Is.SameAs(world));

                var serializedWorld = new SerializedObject(world);
                Assert.That(
                    serializedWorld.FindProperty("levelConfigurationOverride")
                        .objectReferenceValue,
                    Is.SameAs(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                        ProjectAssetPaths.Config.FirstLevel)));

                var serializedInteractor = new SerializedObject(interactor);
                Assert.That(
                    serializedInteractor.FindProperty("terrain")
                        .objectReferenceValue,
                    Is.SameAs(bridge));

                GameObject playerRoot = interactor.transform.root.gameObject;
                UnityEngine.Object prefabSource =
                    PrefabUtility.GetCorrespondingObjectFromSource(playerRoot);
                Assert.That(
                    AssetDatabase.GetAssetPath(prefabSource),
                    Is.EqualTo(ProjectAssetPaths.Prefabs.Player));

                for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
                {
                    Assert.That(
                        EditorBuildSettings.scenes[i].path,
                        Is.Not.EqualTo(
                            ProjectAssetPaths.Scenes.VoxelIntegrityExperiment),
                        "The isolated experiment must not be added to Build Settings.");
                }
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }


        [Test]
        public void DynamicBodyBuilder_DiagonalIslandsShareOneVisitedSet()
        {
            var samples = new Dictionary<Vector3Int, VoxelSample>
            {
                [Vector3Int.zero] =
                    new VoxelSample(3f, VoxelTypeId.Default),
                [Vector3Int.right] =
                    new VoxelSample(3f, VoxelTypeId.Default),
                [new Vector3Int(4, 4, 0)] =
                    new VoxelSample(3f, VoxelTypeId.Default),
            };
            var massByType = new Dictionary<VoxelTypeId, float>
            {
                [VoxelTypeId.Default] = 12f,
            };

            DynamicVoxelBodyBuildResult result =
                DynamicVoxelBodyBuilder.Build(
                    7,
                    samples,
                    0f,
                    1f,
                    MarchingCubesVertexPlacement.DensityInterpolated,
                    default,
                    massByType,
                    1f);

            Assert.That(result.Error, Is.Null);
            Assert.That(result.Revision, Is.EqualTo(7));
            Assert.That(result.FillCount, Is.EqualTo(2));
            Assert.That(result.VisitedVoxelCount, Is.EqualTo(3));
            Assert.That(result.Components, Has.Count.EqualTo(2));
            Assert.That(result.Components[0].Coordinates, Has.Count.EqualTo(2));
            for (int i = 0; i < result.Components.Count; i++)
            {
                DynamicVoxelComponentBuildData component =
                    result.Components[i];
                Assert.That(
                    component.Mass,
                    Is.EqualTo(component.MassProperties.Volume * 12f)
                        .Within(0.0001f));
            }
        }

        [Test]
        public void DynamicBodyBuilder_PreCanceledInteractiveBuildStopsEarly()
        {
            var samples = new Dictionary<Vector3Int, VoxelSample>
            {
                [Vector3Int.zero] =
                    new VoxelSample(3f, VoxelTypeId.Default),
            };
            var cancellation =
                new System.Threading.CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                DynamicVoxelBodyBuildResult result =
                    DynamicVoxelBodyBuilder.Build(
                        3,
                        samples,
                        0f,
                        1f,
                        MarchingCubesVertexPlacement.DensityInterpolated,
                        default,
                        null,
                        1f,
                        null,
                        VoxelConvexDecompositionPriority.Interactive,
                        VoxelConvexDecompositionQuality.Interactive,
                        cancellation.Token);

                Assert.That(
                    result.Error,
                    Is.TypeOf<System.OperationCanceledException>());
                Assert.That(result.Components, Is.Empty);
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        [Test]
        public void DynamicBodyBuilder_InteractiveQualityBuildsConvexMeshes()
        {
            GameObject body = null;
            var samples = new Dictionary<Vector3Int, VoxelSample>
            {
                [Vector3Int.zero] =
                    new VoxelSample(3f, VoxelTypeId.Default),
                [Vector3Int.right] =
                    new VoxelSample(3f, VoxelTypeId.Default),
                [Vector3Int.up] =
                    new VoxelSample(3f, VoxelTypeId.Default),
                [Vector3Int.forward] =
                    new VoxelSample(3f, VoxelTypeId.Default),
            };

            try
            {
                DynamicVoxelBodyBuildResult result =
                    DynamicVoxelBodyBuilder.Build(
                        4,
                        samples,
                        0f,
                        1f,
                        MarchingCubesVertexPlacement.DensityInterpolated,
                        default,
                        null,
                        1f,
                        VoxelConvexDecompositionSettings.Default,
                        VoxelConvexDecompositionPriority.Interactive,
                        VoxelConvexDecompositionQuality.Interactive);

                Assert.That(result.Error, Is.Null);
                Assert.That(result.Components, Has.Count.EqualTo(1));
                DynamicVoxelComponentBuildData component =
                    result.Components[0];
                Assert.That(
                    component.ConvexColliderMeshes,
                    Has.Count.GreaterThan(1));
                Assert.That(
                    component.ConvexColliderMeshes.Count,
                    Is.LessThanOrEqualTo(8));
                body = VoxelIntegrityRigidbodyFactory
                    .CreateFromMarchingCubes(
                        component.Coordinates,
                        component.MeshData,
                        1f,
                        null,
                        null,
                        component.Mass,
                        component.MassProperties,
                        component.ConvexColliderMeshes);
                MeshCollider[] colliders = body.GetComponents<MeshCollider>();
                Assert.That(
                    colliders,
                    Has.Length.EqualTo(
                        component.ConvexColliderMeshes.Count));
                Assert.That(body.GetComponents<BoxCollider>(), Is.Empty);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Assert.That(colliders[i].convex, Is.True);
                    Assert.That(colliders[i].sharedMesh, Is.Not.Null);
                }
            }
            finally
            {
                if (body != null)
                {
                    Object.DestroyImmediate(body);
                }
            }
        }

        [Test]
        public void IntegrityBridge_BombDamagesMovingSparseVoxelBody()
        {
            GameObject bridgeObject = null;
            GameObject bodyObject = null;
            try
            {
                bridgeObject = new GameObject("Integrity bridge");
                DynamicVoxelBodyRegistry registry =
                    bridgeObject.AddComponent<DynamicVoxelBodyRegistry>();
                VoxelIntegrityWorldBridge bridge =
                    bridgeObject.AddComponent<VoxelIntegrityWorldBridge>();

                var first = Vector3Int.zero;
                var second = Vector3Int.right;
                var outside = new Vector3Int(5, 0, 0);
                var samples = new Dictionary<Vector3Int, VoxelSample>
                {
                    [first] = new VoxelSample(3f, VoxelTypeId.Default),
                    [second] = new VoxelSample(3f, VoxelTypeId.Default),
                    [outside] = new VoxelSample(3f, VoxelTypeId.Default),
                };
                var coordinates = new List<Vector3Int>(samples.Keys);
                var component = new HashSet<Vector3Int>(coordinates);
                VoxelMeshData meshData =
                    MarchingCubesMesher.BuildCapturedComponent(
                        component,
                        samples,
                        0f,
                        1f,
                        MarchingCubesVertexPlacement.DensityInterpolated,
                        default);
                Vector3 pivot = Vector3.zero;
                var buildData = new DynamicVoxelComponentBuildData(
                    coordinates,
                    samples,
                    meshData,
                    default,
                    pivot,
                    3f,
                    new List<VoxelConvexColliderMeshData>(),
                    new VoxelMeshRaycastBvh(
                        meshData.Vertices,
                        meshData.Triangles,
                        pivot));

                bodyObject = new GameObject("Bomb target sparse body");
                bodyObject.transform.SetPositionAndRotation(
                    new Vector3(13f, 4f, -7f),
                    Quaternion.Euler(11f, 67f, 5f));
                DynamicVoxelBody body =
                    bodyObject.AddComponent<DynamicVoxelBody>();
                var initialize = typeof(DynamicVoxelBody).GetMethod(
                    "Initialize",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic);
                Assert.That(initialize, Is.Not.Null);
                initialize.Invoke(
                    body,
                    new object[]
                    {
                        registry,
                        System.Guid.NewGuid(),
                        buildData,
                        0f,
                        1f,
                        MarchingCubesVertexPlacement.DensityInterpolated,
                        default(VoxelGroupMap),
                        null,
                        1f,
                        VoxelConvexDecompositionSettings.Default,
                        null,
                        null,
                        null,
                    });

                Vector3 blastCenter = bodyObject.transform.TransformPoint(
                    (Vector3)first - pivot);
                var settings = new VoxelExplosionSettings(
                    1.1f,
                    1.1f,
                    10000f,
                    10000f,
                    2f);
                Assert.That(
                    bridge.TryMineExplosion(
                        blastCenter,
                        settings,
                        out VoxelExplosionResult result),
                    Is.True);
                Assert.That(result.CandidateCount, Is.EqualTo(2));
                Assert.That(result.DamagedCount, Is.EqualTo(2));
                Assert.That(result.DestroyedCount, Is.EqualTo(2));
                Assert.That(body.TryGetSample(first, out _), Is.False);
                Assert.That(body.TryGetSample(second, out _), Is.False);
                Assert.That(body.TryGetSample(outside, out _), Is.True);
            }
            finally
            {
                if (bodyObject != null)
                {
                    Object.DestroyImmediate(bodyObject);
                }
                if (bridgeObject != null)
                {
                    Object.DestroyImmediate(bridgeObject);
                }
            }
        }

        [Test]
        public void DynamicBodySplit_PreservesWorldVoxelPositions()
        {
            GameObject registryObject = null;
            DynamicVoxelBody firstBody = null;
            DynamicVoxelBody secondBody = null;
            try
            {
                registryObject = new GameObject("Split registry");
                DynamicVoxelBodyRegistry registry =
                    registryObject.AddComponent<DynamicVoxelBodyRegistry>();
                System.Guid lineage = System.Guid.NewGuid();
                var firstCoordinate = Vector3Int.zero;
                var secondCoordinate = new Vector3Int(4, 0, 0);
                DynamicVoxelComponentBuildData firstComponent =
                    CreateSingleVoxelBuildData(firstCoordinate);
                DynamicVoxelComponentBuildData secondComponent =
                    CreateSingleVoxelBuildData(secondCoordinate);
                var allSamples = new Dictionary<Vector3Int, VoxelSample>
                {
                    [firstCoordinate] =
                        new VoxelSample(3f, VoxelTypeId.Default),
                    [secondCoordinate] =
                        new VoxelSample(3f, VoxelTypeId.Default),
                };
                var allCoordinates = new List<Vector3Int>(allSamples.Keys);
                VoxelMeshData allMesh =
                    MarchingCubesMesher.BuildCapturedComponent(
                        new HashSet<Vector3Int>(allCoordinates),
                        allSamples,
                        0f,
                        1f,
                        MarchingCubesVertexPlacement.DensityInterpolated,
                        default);
                Vector3 oldPivot = new Vector3(2f, 0.25f, -0.5f);
                var initialData = new DynamicVoxelComponentBuildData(
                    allCoordinates,
                    allSamples,
                    allMesh,
                    default,
                    oldPivot,
                    2f,
                    new List<VoxelConvexColliderMeshData>(),
                    new VoxelMeshRaycastBvh(
                        allMesh.Vertices,
                        allMesh.Triangles,
                        oldPivot));

                GameObject bodyObject = new GameObject("Split source body");
                bodyObject.transform.localScale = Vector3.one * 1.35f;
                firstBody = bodyObject.AddComponent<DynamicVoxelBody>();
                Rigidbody sourceRigidbody = bodyObject.GetComponent<Rigidbody>();
                sourceRigidbody.interpolation =
                    RigidbodyInterpolation.Interpolate;
                sourceRigidbody.position = new Vector3(8f, 6f, -9f);
                sourceRigidbody.rotation = Quaternion.Euler(19f, 53f, 7f);
                var initialize = typeof(DynamicVoxelBody).GetMethod(
                    "Initialize",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic);
                Assert.That(initialize, Is.Not.Null);
                initialize.Invoke(
                    firstBody,
                    new object[]
                    {
                        registry,
                        lineage,
                        initialData,
                        0f,
                        1f,
                        MarchingCubesVertexPlacement.DensityInterpolated,
                        default(VoxelGroupMap),
                        null,
                        1f,
                        VoxelConvexDecompositionSettings.Default,
                        null,
                        null,
                        null,
                    });
                Matrix4x4 oldPose = Matrix4x4.TRS(
                    sourceRigidbody.position,
                    sourceRigidbody.rotation,
                    bodyObject.transform.lossyScale);
                Vector3 expectedFirst = oldPose.MultiplyPoint3x4(
                    (Vector3)firstCoordinate - oldPivot);
                Vector3 expectedSecond = oldPose.MultiplyPoint3x4(
                    (Vector3)secondCoordinate - oldPivot);

                var commit = typeof(DynamicVoxelBody).GetMethod(
                    "CommitComponents",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic);
                Assert.That(commit, Is.Not.Null);
                commit.Invoke(
                    firstBody,
                    new object[]
                    {
                        new List<DynamicVoxelComponentBuildData>
                        {
                            firstComponent,
                            secondComponent,
                        },
                    });

                Assert.That(
                    registry.TryResolve(
                        new DynamicVoxelAddress(lineage, firstCoordinate),
                        out firstBody),
                    Is.True);
                Assert.That(
                    registry.TryResolve(
                        new DynamicVoxelAddress(lineage, secondCoordinate),
                        out secondBody),
                    Is.True);
                Vector3 actualFirst = firstBody.transform.TransformPoint(
                    (Vector3)firstCoordinate - firstBody.Pivot);
                Vector3 actualSecond = secondBody.transform.TransformPoint(
                    (Vector3)secondCoordinate - secondBody.Pivot);
                Assert.That(
                    Vector3.Distance(actualFirst, expectedFirst),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Vector3.Distance(actualSecond, expectedSecond),
                    Is.LessThan(0.0001f));
                Assert.That(
                    firstBody.GetComponent<Rigidbody>().interpolation,
                    Is.EqualTo(RigidbodyInterpolation.Interpolate));
                Assert.That(
                    secondBody.GetComponent<Rigidbody>().interpolation,
                    Is.EqualTo(RigidbodyInterpolation.Interpolate));
            }
            finally
            {
                if (firstBody != null)
                {
                    Object.DestroyImmediate(firstBody.gameObject);
                }
                if (secondBody != null && secondBody != firstBody)
                {
                    Object.DestroyImmediate(secondBody.gameObject);
                }
                if (registryObject != null)
                {
                    Object.DestroyImmediate(registryObject);
                }
            }
        }

        [Test]
        public void DynamicBodyBuilder_ConcaveShapeUsesBoundedConvexMeshes()
        {
            var samples = new Dictionary<Vector3Int, VoxelSample>
            {
                [Vector3Int.zero] =
                    new VoxelSample(3f, VoxelTypeId.Default),
                [Vector3Int.right] =
                    new VoxelSample(3f, VoxelTypeId.Default),
                [Vector3Int.up] =
                    new VoxelSample(3f, VoxelTypeId.Default),
                [Vector3Int.forward] =
                    new VoxelSample(3f, VoxelTypeId.Default),
            };

            DynamicVoxelBodyBuildResult result =
                DynamicVoxelBodyBuilder.Build(
                    0,
                    samples,
                    0f,
                    1f,
                    MarchingCubesVertexPlacement.DensityInterpolated,
                    default,
                    null,
                    1f,
                    new VoxelConvexDecompositionSettings(0.1f, 8));

            Assert.That(result.Error, Is.Null);
            List<VoxelConvexColliderMeshData> colliders =
                result.Components[0].ConvexColliderMeshes;
            Assert.That(colliders, Is.Not.Empty);
            Assert.That(
                colliders.Count,
                Is.GreaterThan(1),
                "The concave branch must not be collapsed into one convex hull.");
            Assert.That(colliders.Count, Is.LessThanOrEqualTo(8));
            for (int i = 0; i < colliders.Count; i++)
            {
                Assert.That(
                    colliders[i].TriangleCount,
                    Is.LessThanOrEqualTo(255));
                Assert.That(colliders[i].Vertices.Length, Is.LessThanOrEqualTo(64));
            }
        }

        [Test]
        public void DynamicBodyBvh_RayHitsRenderedDensitySurface()
        {
            var samples = new Dictionary<Vector3Int, VoxelSample>
            {
                [Vector3Int.zero] =
                    new VoxelSample(3f, VoxelTypeId.Default),
            };
            DynamicVoxelBodyBuildResult result =
                DynamicVoxelBodyBuilder.Build(
                    0,
                    samples,
                    0f,
                    1f,
                    MarchingCubesVertexPlacement.DensityInterpolated,
                    default,
                    null,
                    1f);

            Assert.That(result.Error, Is.Null);
            DynamicVoxelComponentBuildData component = result.Components[0];
            bool hit = component.RaycastBvh.TryRaycast(
                new Ray(new Vector3(0f, 0f, -2f), Vector3.forward),
                4f,
                out float distance,
                out Vector3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(normal.z, Is.LessThan(-0.5f));
        }

        [Test]
        public void DynamicBody_MinedOreCreatesRegisteredValuableDrop()
        {
            GameObject terrainObject = null;
            GameObject bodyObject = null;
            try
            {
                var level = AssetDatabase.LoadAssetAtPath<
                    Supernova.Missions.LevelConfiguration>(
                        ProjectAssetPaths.Config.FirstLevel);
                Assert.That(level, Is.Not.Null);

                terrainObject = new GameObject("Dynamic ore drop terrain");
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                Assert.That(terrain.ApplyLevelConfiguration(level), Is.True);
                terrain.InitializeWorld();
                Assert.That(terrain.OreFeatures, Is.Not.Empty);

                VoxelOreFeatureDefinition oreFeature = terrain.OreFeatures[0];
                VoxelTypeId oreType = oreFeature.ResultVoxelType.TypeId;
                VoxelTypeId stoneType = terrain.BaseSolidVoxelType.TypeId;
                var first = Vector3Int.zero;
                var diagonal = new Vector3Int(1, 1, 0);
                var remainingStone = new Vector3Int(2, 1, 0);
                var samples = new Dictionary<Vector3Int, VoxelSample>
                {
                    [first] = new VoxelSample(3f, oreType),
                    [diagonal] = new VoxelSample(3f, oreType),
                    [remainingStone] = new VoxelSample(3f, stoneType),
                };
                var coordinates = new List<Vector3Int>(samples.Keys);
                var component = new HashSet<Vector3Int>(coordinates);
                VoxelGroupMap groupMap = VoxelGroupMap.FromDefinitions(
                    terrain.VoxelTypeCatalog.Definitions);
                VoxelMeshData meshData =
                    MarchingCubesMesher.BuildCapturedComponent(
                        component,
                        samples,
                        terrain.IsoLevel,
                        terrain.VoxelSize,
                        terrain.VertexPlacement,
                        groupMap);
                VoxelMeshMassProperties massProperties =
                    VoxelIntegrityRigidbodyFactory.CalculateMassProperties(
                        meshData.Vertices,
                        meshData.Triangles);
                Vector3 pivot = massProperties.Centroid;
                var buildData = new DynamicVoxelComponentBuildData(
                    coordinates,
                    samples,
                    meshData,
                    massProperties,
                    pivot,
                    1f,
                    new List<VoxelConvexColliderMeshData>(),
                    new VoxelMeshRaycastBvh(
                        meshData.Vertices,
                        meshData.Triangles,
                        pivot));

                bodyObject = new GameObject("Moving sparse ore body");
                bodyObject.transform.SetPositionAndRotation(
                    new Vector3(18f, 7f, -12f),
                    Quaternion.Euler(17f, 41f, 9f));
                DynamicVoxelBody dynamicBody =
                    bodyObject.AddComponent<DynamicVoxelBody>();
                var initialize = typeof(DynamicVoxelBody).GetMethod(
                    "Initialize",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic);
                Assert.That(initialize, Is.Not.Null);
                initialize.Invoke(
                    dynamicBody,
                    new object[]
                    {
                        null,
                        System.Guid.NewGuid(),
                        buildData,
                        terrain.IsoLevel,
                        terrain.VoxelSize,
                        terrain.VertexPlacement,
                        groupMap,
                        new Dictionary<VoxelTypeId, float>
                        {
                            [oreType] = oreFeature.MassDensity,
                        },
                        1f,
                        VoxelConvexDecompositionSettings.Default,
                        terrain,
                        terrain.VoxelTypeCatalog,
                        terrain.BaseSolidVoxelType.Material,
                    });

                for (int hit = 0;
                    hit < 128 && terrain.ActiveOreDrops.Count == 0;
                    hit++)
                {
                    Assert.That(
                        dynamicBody.TryMineVoxel(first, out _),
                        Is.True);
                }

                Assert.That(terrain.ActiveOreDrops, Has.Count.EqualTo(1));
                MinedOreDrop drop = terrain.ActiveOreDrops[0];
                Assert.That(drop.VoxelType, Is.EqualTo(oreType));
                Assert.That(drop.VoxelCount, Is.EqualTo(2));
                VoxelMeshMassProperties recoveredMassProperties =
                    VoxelIntegrityRigidbodyFactory.CalculateMassProperties(
                        drop.Mesh.vertices,
                        drop.Mesh.triangles);
                float expectedRecoveredVolume =
                    VoxelIntegrityRigidbodyFactory
                        .CalculateRepresentedFullVoxelVolume(
                            recoveredMassProperties,
                            terrain.VoxelSize,
                            Vector3.one
                                * MinedOreDrop.RecoveredLinearScale);
                Assert.That(
                    drop.RepresentedFullVoxelVolume,
                    Is.EqualTo(expectedRecoveredVolume).Within(0.0001f));
                Assert.That(
                    drop.Value,
                    Is.EqualTo(
                        MinedOreDrop.CalculateInitialValue(
                            expectedRecoveredVolume,
                            oreFeature.OreUnitValue)));
                Assert.That(
                    drop.Body.mass,
                    Is.EqualTo(
                            oreFeature.MassDensity
                                * expectedRecoveredVolume)
                        .Within(0.0001f));
                Assert.That(dynamicBody.VoxelCount, Is.EqualTo(1));
                Assert.That(dynamicBody.TryGetSample(remainingStone, out _), Is.True);
                Assert.That(drop.Mesh, Is.Not.Null);
                Assert.That(drop.GetComponent<Collider>(), Is.Not.Null);
                Assert.That(
                    Vector3.Distance(
                        drop.transform.position,
                        bodyObject.transform.position),
                    Is.LessThan(4f),
                    "The recovered value body must spawn at the moved body's "
                    + "current transform, not at its former terrain position.");

                Material runtimeMaterial =
                    drop.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.That(oreFeature.RecoveredMaterial, Is.Not.Null);
                Assert.That(runtimeMaterial, Is.Not.Null);
                Assert.That(runtimeMaterial, Is.Not.SameAs(
                    oreFeature.RecoveredMaterial));
                Assert.That(
                    runtimeMaterial.shader,
                    Is.SameAs(oreFeature.RecoveredMaterial.shader));
                Assert.That(
                    runtimeMaterial.GetTexture("_BaseMap"),
                    Is.SameAs(
                        oreFeature.ResultVoxelType.Material.GetTexture(
                            "_BaseMap")));
                Assert.That(
                    runtimeMaterial.GetTexture("_BaseMap"),
                    Is.Not.SameAs(
                        oreFeature.RecoveredMaterial.GetTexture(
                            "_BaseMap")));
                Assert.That(
                    runtimeMaterial.GetColor("_BaseColor"),
                    Is.EqualTo(
                        oreFeature.RecoveredMaterial.GetColor("_BaseColor")));
            }
            finally
            {
                if (bodyObject != null)
                {
                    Object.DestroyImmediate(bodyObject);
                }
                if (terrainObject != null)
                {
                    Object.DestroyImmediate(terrainObject);
                }
            }
        }

        [Test]
        public void RecoveredOreMaterial_IsAssignedToEveryOreFeature()
        {
            Material expected = AssetDatabase.LoadAssetAtPath<Material>(
                ProjectAssetPaths.Materials.RecoveredOre);
            Assert.That(expected, Is.Not.Null);

            string[] guids = AssetDatabase.FindAssets(
                "t:VoxelOreFeatureDefinition",
                new[] { ProjectAssetPaths.Folders.OreFeatures });
            Assert.That(guids, Is.Not.Empty);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                VoxelOreFeatureDefinition feature =
                    AssetDatabase.LoadAssetAtPath<
                        VoxelOreFeatureDefinition>(path);
                Assert.That(feature, Is.Not.Null, path);
                Assert.That(feature.RecoveredMaterial, Is.Not.Null, path);
                Assert.That(
                    AssetDatabase.GetAssetPath(feature.RecoveredMaterial),
                    Is.EqualTo(ProjectAssetPaths.Materials.RecoveredOre),
                    path);
            }
        }

        private static DynamicVoxelComponentBuildData
            CreateSingleVoxelBuildData(Vector3Int coordinate)
        {
            var samples = new Dictionary<Vector3Int, VoxelSample>
            {
                [coordinate] =
                    new VoxelSample(3f, VoxelTypeId.Default),
            };
            var coordinates = new List<Vector3Int> { coordinate };
            VoxelMeshData meshData =
                MarchingCubesMesher.BuildCapturedComponent(
                    new HashSet<Vector3Int>(coordinates),
                    samples,
                    0f,
                    1f,
                    MarchingCubesVertexPlacement.DensityInterpolated,
                    default);
            VoxelMeshMassProperties properties =
                VoxelIntegrityRigidbodyFactory.CalculateMassProperties(
                    meshData.Vertices,
                    meshData.Triangles);
            Vector3 pivot = properties.Centroid;
            var centeredVertices = new Vector3[meshData.Vertices.Count];
            for (int i = 0; i < centeredVertices.Length; i++)
            {
                centeredVertices[i] = meshData.Vertices[i] - pivot;
            }
            var colliderMeshes = new List<VoxelConvexColliderMeshData>
            {
                new VoxelConvexColliderMeshData(
                    centeredVertices,
                    meshData.Triangles.ToArray()),
            };
            return new DynamicVoxelComponentBuildData(
                coordinates,
                samples,
                meshData,
                properties,
                pivot,
                1f,
                colliderMeshes,
                new VoxelMeshRaycastBvh(
                    meshData.Vertices,
                    meshData.Triangles,
                    pivot));
        }

        private sealed class TestMap : IVoxelIntegrityMap
        {
            private readonly Vector3Int minimum;
            private readonly Vector3Int maximumExclusive;
            private readonly bool useBoundaryHeuristic;

            private readonly Dictionary<Vector3Int, VoxelIntegrityCell> cells =
                new Dictionary<Vector3Int, VoxelIntegrityCell>();

            public TestMap(
                Vector3Int minimum,
                Vector3Int maximumExclusive,
                bool useBoundaryHeuristic = true)
            {
                this.minimum = minimum;
                this.maximumExclusive = maximumExclusive;
                this.useBoundaryHeuristic = useBoundaryHeuristic;
            }

            public void SetSolid(Vector3Int coordinate)
            {
                cells[coordinate] = VoxelIntegrityCell.Solid;
            }

            public void SetBedrock(Vector3Int coordinate)
            {
                cells[coordinate] = VoxelIntegrityCell.StructuralSupport;
            }

            public VoxelIntegrityCell GetCell(Vector3Int coordinate)
            {
                if (coordinate.x < minimum.x
                    || coordinate.y < minimum.y
                    || coordinate.z < minimum.z
                    || coordinate.x >= maximumExclusive.x
                    || coordinate.y >= maximumExclusive.y
                    || coordinate.z >= maximumExclusive.z)
                {
                    return VoxelIntegrityCell.Unloaded;
                }

                return cells.TryGetValue(
                    coordinate,
                    out VoxelIntegrityCell cell)
                        ? cell
                        : VoxelIntegrityCell.Air;
            }

            public int EstimateDistanceToUnloadedBoundary(Vector3Int coordinate)
            {
                if (!useBoundaryHeuristic)
                    return 0;

                return Mathf.Min(
                    Mathf.Min(
                        coordinate.x - minimum.x + 1,
                        maximumExclusive.x - coordinate.x),
                    Mathf.Min(
                        Mathf.Min(
                            coordinate.y - minimum.y + 1,
                            maximumExclusive.y - coordinate.y),
                        Mathf.Min(
                            coordinate.z - minimum.z + 1,
                            maximumExclusive.z - coordinate.z)));
            }
        }

        private static T FindComponentInScene<T>(Scene scene)
            where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T component = roots[i].GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }


        [Test]
        public void CapturedComponent_BackgroundBuildMatchesLiveWorldMeshAndMass()
        {
            var world = new InfiniteVoxelWorld();
            InfiniteVoxelChunk chunk = world.EnsureChunk(Vector2Int.zero);
            chunk.Data.Fill(-1f, VoxelTypeId.Air);
            var coordinates = new[]
            {
                new Vector3Int(5, 5, 5),
                new Vector3Int(6, 5, 5),
                new Vector3Int(6, 6, 5),
            };
            world.SetVoxel(5, 5, 5, 3f, VoxelTypeId.Default);
            world.SetVoxel(6, 5, 5, 2f, VoxelTypeId.Default);
            world.SetVoxel(6, 6, 5, 1f, VoxelTypeId.Default);

            var component = new HashSet<Vector3Int>(coordinates);
            var samples =
                new Dictionary<Vector3Int, VoxelSample>(coordinates.Length);
            for (int i = 0; i < coordinates.Length; i++)
            {
                Vector3Int coordinate = coordinates[i];
                Assert.That(
                    world.TryGetSample(
                        coordinate.x,
                        coordinate.y,
                        coordinate.z,
                        out VoxelSample sample),
                    Is.True);
                samples.Add(coordinate, sample);
            }

            VoxelMeshData live = MarchingCubesMesher.BuildComponent(
                world,
                component,
                0f,
                1f,
                MarchingCubesVertexPlacement.DensityInterpolated);
            VoxelMeshData captured =
                System.Threading.Tasks.Task.Run(
                    () => MarchingCubesMesher.BuildCapturedComponent(
                        component,
                        samples,
                        0f,
                        1f,
                        MarchingCubesVertexPlacement.DensityInterpolated,
                        default))
                .GetAwaiter()
                .GetResult();

            Assert.That(captured.TriangleCount, Is.EqualTo(live.TriangleCount));
            Assert.That(captured.Vertices.Count, Is.EqualTo(live.Vertices.Count));
            Assert.That(captured.Triangles, Is.EqualTo(live.Triangles));
            for (int i = 0; i < live.Vertices.Count; i++)
            {
                Assert.That(
                    captured.Vertices[i],
                    Is.EqualTo(live.Vertices[i]));
            }

            VoxelMeshMassProperties liveMass =
                VoxelIntegrityRigidbodyFactory.CalculateMassProperties(
                    live.Vertices,
                    live.Triangles);
            VoxelMeshMassProperties capturedMass =
                VoxelIntegrityRigidbodyFactory.CalculateMassProperties(
                    captured.Vertices,
                    captured.Triangles);
            Assert.That(
                capturedMass.Volume,
                Is.EqualTo(liveMass.Volume).Within(0.0001f));
            Assert.That(
                capturedMass.Centroid,
                Is.EqualTo(liveMass.Centroid));
        }


        [Test]
        public void DenseJigsawRegionScene_RoutesDestructionThroughIntegrityBridge()
        {
            Scene scene = SceneManager.GetSceneByPath(
                ProjectAssetPaths.Scenes.DenseJigsawRegion);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    ProjectAssetPaths.Scenes.DenseJigsawRegion,
                    OpenSceneMode.Additive);
            }

            try
            {
                MinecraftCaveInfiniteWorld world =
                    FindComponentInScene<MinecraftCaveInfiniteWorld>(scene);
                VoxelIntegrityWorldBridge bridge =
                    FindComponentInScene<VoxelIntegrityWorldBridge>(scene);
                VoxelPlayerInteractor interactor =
                    FindComponentInScene<VoxelPlayerInteractor>(scene);

                Assert.That(world, Is.Not.Null);
                Assert.That(bridge, Is.Not.Null);
                Assert.That(interactor, Is.Not.Null);
                Assert.That(bridge.gameObject, Is.SameAs(world.gameObject));
                Assert.That(bridge.SourceTerrain, Is.SameAs(world));
                Assert.That(interactor.VoxelTerrain, Is.SameAs(bridge));

                var serializedBridge = new SerializedObject(bridge);
                Assert.That(
                    serializedBridge.FindProperty("terrain")
                        .objectReferenceValue,
                    Is.SameAs(world));
                Assert.That(
                    serializedBridge.FindProperty("showDebugOverlay")
                        .boolValue,
                    Is.False);

                bool enabledInBuild = false;
                for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
                {
                    if (EditorBuildSettings.scenes[i].enabled
                        && EditorBuildSettings.scenes[i].path
                            == ProjectAssetPaths.Scenes.DenseJigsawRegion)
                    {
                        enabledInBuild = true;
                        break;
                    }
                }
                Assert.That(enabledInBuild, Is.True);
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
