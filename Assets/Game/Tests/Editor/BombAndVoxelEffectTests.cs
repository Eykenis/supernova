using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Supernova.Effects;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class BombAndVoxelEffectTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        [Test]
        public void AreaEffect_CanDamageANonVoxelReceiver()
        {
            GameObject actor = Create("Actor");
            DestructibleHealth health = actor.AddComponent<DestructibleHealth>();
            typeof(AreaEffectReceiverBehaviour).GetMethod(
                "OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(health, null);
            var context = new AreaEffectContext(
                actor.transform.position, 4f, 40f, 0f, 0f, 123, null);

            AreaEffectDispatcher.Dispatch(context);

            Assert.That(health.Health, Is.EqualTo(60f).Within(0.001f));
        }

        [Test]
        public void MagnetBeam_FadesSmoothlyFromTransparentAtTheSource()
        {
            GameObject host = Create("Magnet Beam");
            MagnetAttractionBeam beam = host.AddComponent<MagnetAttractionBeam>();

            InvokePrivate(beam, "EnsureLine");
            InvokePrivate(beam, "UpdateFlowColor");

            LineRenderer line = host.GetComponent<LineRenderer>();
            Assert.That(line, Is.Not.Null);
            Gradient colors = line.colorGradient;
            float sourceAlpha = colors.Evaluate(0f).a;
            float nearSourceAlpha = colors.Evaluate(0.05f).a;
            float fadedInAlpha = colors.Evaluate(0.25f).a;

            Assert.That(sourceAlpha, Is.Zero.Within(0.0001f));
            Assert.That(nearSourceAlpha, Is.GreaterThan(sourceAlpha));
            Assert.That(fadedInAlpha, Is.GreaterThan(nearSourceAlpha));
        }

        [Test]
        public void MagnetBeam_StartsHalfwayBetweenBothHands()
        {
            GameObject host = Create("Magnet Beam");
            MagnetAttractionBeam beam = host.AddComponent<MagnetAttractionBeam>();
            Transform leftHand = Create("Left Hand").transform;
            Transform rightHand = Create("Right Hand").transform;
            leftHand.position = new Vector3(-1.5f, 2f, 4f);
            rightHand.position = new Vector3(0.5f, 4f, 8f);
            SetPrivateField(beam, "leftHand", leftHand);
            SetPrivateField(beam, "rightHand", rightHand);

            Vector3 start = InvokePrivate<Vector3>(beam, "ResolveBeamStart");

            Assert.That(start, Is.EqualTo(new Vector3(-0.5f, 3f, 6f)));
        }

        [Test]
        public void MagnetBeam_ArcBendsUpward()
        {
            GameObject host = Create("Magnet Beam");
            MagnetAttractionBeam beam = host.AddComponent<MagnetAttractionBeam>();

            float midpointHeight = InvokePrivate<float>(beam, "CalculateArcHeight", 0.5f);

            Assert.That(midpointHeight, Is.GreaterThan(0f));
        }

        [Test]
        public void CarveSphere_BatchesVoxelChangesIntoOneDirtyChunk()
        {
            GameObject terrainObject = Create("Terrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            terrain.InitializeWorld();
            InfiniteVoxelChunk chunk = terrain.World.EnsureChunk(Vector3Int.zero);
            Vector3Int centre = new Vector3Int(16, 16, 16);
            Vector3 worldCentre = terrain.transform.TransformPoint(
                (Vector3)centre * terrain.VoxelSize);

            int removed = terrain.CarveSphere(worldCentre, terrain.VoxelSize * 4f, 0.3f, 99);

            Assert.That(removed, Is.GreaterThan(1));
            Assert.That(chunk.Data[centre.x, centre.y, centre.z], Is.LessThan(0f));
            FieldInfo queueField = typeof(MinecraftCaveInfiniteWorld).GetField(
                "meshQueue", BindingFlags.Instance | BindingFlags.NonPublic);
            var queue = (Queue<Vector3Int>)queueField.GetValue(terrain);
            Assert.That(queue.Count, Is.EqualTo(1),
                "All voxel changes inside one chunk should schedule only one rebuild.");
        }

        [Test]
        public void ViewerMovement_RefreshesStreamingWhileMeshesAreStillQueued()
        {
            GameObject terrainObject = Create("Terrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            terrain.InitializeWorld();
            GameObject viewer = Create("Viewer");
            SetPrivateField(terrain, "viewer", viewer.transform);
            SetPrivateField(terrain, "hasViewerChunk", true);
            SetPrivateField(terrain, "viewerChunk", Vector3Int.zero);
            SetPrivateField(terrain, "initialSpawnPlacementPending", false);
            SetPrivateField(
                terrain,
                "generationStage",
                MinecraftCaveGenerationStage.Meshes);
            Queue<Vector3Int> queue = GetMeshQueue(terrain);
            queue.Enqueue(Vector3Int.zero);
            viewer.transform.position = Vector3.right * VoxelVolume.Size * terrain.VoxelSize;

            InvokePrivate(terrain, "RefreshStreamingForViewerMovement");

            Assert.That(terrain.ViewerChunk, Is.EqualTo(Vector3Int.right));
            Assert.That(terrain.GenerationStage, Is.EqualTo(MinecraftCaveGenerationStage.Terrain));
            Assert.That(queue.Count, Is.Zero,
                "A visibility refresh should replace stale queued mesh work immediately.");
        }

        [Test]
        public void InitialSpawn_LoadsLocalMeshesBeforeExpandingToTheFullRadius()
        {
            GameObject terrainObject = Create("Terrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            GameObject viewer = Create("Viewer");
            SetPrivateField(terrain, "viewer", viewer.transform);

            terrain.InitializeWorld();

            Assert.That(
                terrain.SpawnVoxel.y,
                Is.InRange(
                    MinecraftCaveInfiniteWorld.LowestSpawnY,
                    MinecraftCaveInfiniteWorld.HighestSpawnY));
            Assert.That(terrain.RequiredChunkCount, Is.EqualTo(9),
                "Initial loading should be limited to the 3x3 spawn columns.");
            HashSet<Vector3Int> requiredChunks = GetPrivateField<HashSet<Vector3Int>>(
                terrain, "requiredChunks");
            HashSet<Vector3Int> builtMeshes = GetPrivateField<HashSet<Vector3Int>>(
                terrain, "builtMeshes");
            foreach (Vector3Int column in requiredChunks)
            {
                for (int section = 0;
                    section < MinecraftCaveInfiniteWorld.MeshSectionsPerColumn;
                    section++)
                {
                    builtMeshes.Add(new Vector3Int(
                        column.x,
                        section,
                        column.z));
                }
            }
            SetPrivateField(
                terrain,
                "generationStage",
                MinecraftCaveGenerationStage.Meshes);

            InvokePrivate(terrain, "ReportReadyState");

            Assert.That(terrain.IsInitialLoadComplete, Is.True);
            Assert.That(terrain.InitialLoadProgress, Is.EqualTo(1f).Within(0.001f));

            Assert.That(
                terrain.RequiredChunkCount,
                Is.EqualTo(MinecraftCaveInfiniteWorld.RequiredChunkCountAtRadius),
                "The full streaming radius should continue loading after the player is released.");
            Assert.That(terrain.GenerationStage, Is.EqualTo(MinecraftCaveGenerationStage.Terrain));
        }

        [Test]
        public void InitialLoadGravity_RestoresPreviousValueWhenWorldIsCleared()
        {
            GameObject terrainObject = Create("Terrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            Vector3 originalGravity = Physics.gravity;
            Vector3 gravityBeforeLoad = new Vector3(1.5f, -4.25f, 0.75f);

            try
            {
                Physics.gravity = Vector3.zero;
                SetPrivateField(terrain, "gravityBeforeInitialLoad", gravityBeforeLoad);
                SetPrivateField(terrain, "globalGravitySuspended", true);

                InvokePrivate(terrain, "ClearRuntimeState");

                Assert.That(Physics.gravity, Is.EqualTo(gravityBeforeLoad));
                Assert.That(terrain.IsGlobalGravitySuspendedForInitialLoad, Is.False);
            }
            finally
            {
                Physics.gravity = originalGravity;
            }
        }

        [Test]
        public void GroundedSpawn_UsesTerrainSurfaceBeforeEnablingThePlayer()
        {
            GameObject terrainObject = Create("Terrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            GameObject viewer = Create("Viewer");
            viewer.transform.position = new Vector3(0f, 5f, 0f);
            CharacterController controller = viewer.AddComponent<CharacterController>();
            controller.enabled = false;
            SetPrivateField(terrain, "viewer", viewer.transform);
            SetPrivateField(terrain, "frozenCharacterController", controller);
            SetPrivateField(terrain, "targetSpawnWorldPosition", viewer.transform.position);

            GameObject ground = Create("Generated Ground");
            ground.transform.SetParent(terrain.transform, false);
            ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            BoxCollider groundCollider = ground.AddComponent<BoxCollider>();
            groundCollider.size = new Vector3(10f, 1f, 10f);

            MethodInfo method = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "TryFindGroundedSpawnPosition",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { Vector3.zero };

            bool found = (bool)method.Invoke(terrain, arguments);
            Vector3 groundedPosition = (Vector3)arguments[0];

            Assert.That(found, Is.True);
            Assert.That(groundedPosition.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(groundedPosition.z, Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                groundedPosition.y,
                Is.EqualTo(Mathf.Max(0.02f, controller.skinWidth * 2f)).Within(0.01f));
        }

        private static Queue<Vector3Int> GetMeshQueue(MinecraftCaveInfiniteWorld terrain)
        {
            FieldInfo field = typeof(MinecraftCaveInfiniteWorld).GetField(
                "meshQueue", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (Queue<Vector3Int>)field.GetValue(terrain);
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {target.GetType().Name}.{name}");
            return (T)field.GetValue(target);
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {target.GetType().Name}.{name}");
            field.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing method {target.GetType().Name}.{name}");
            method.Invoke(target, null);
        }

        private static T InvokePrivate<T>(object target, string name, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing method {target.GetType().Name}.{name}");
            return (T)method.Invoke(target, arguments);
        }

        private GameObject Create(string name)
        {
            var gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject;
        }
    }
}
