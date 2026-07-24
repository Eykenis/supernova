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

            Assert.That(terrain.RequiredChunkCount, Is.EqualTo(27),
                "Initial loading should be limited to the 3x3x3 spawn area.");
            HashSet<Vector3Int> requiredChunks = GetPrivateField<HashSet<Vector3Int>>(
                terrain, "requiredChunks");
            HashSet<Vector3Int> builtMeshes = GetPrivateField<HashSet<Vector3Int>>(
                terrain, "builtMeshes");
            builtMeshes.UnionWith(requiredChunks);
            SetPrivateField(
                terrain,
                "generationStage",
                MinecraftCaveGenerationStage.Meshes);

            InvokePrivate(terrain, "ReportReadyState");

            Assert.That(
                terrain.RequiredChunkCount,
                Is.EqualTo(MinecraftCaveInfiniteWorld.RequiredChunkCountAtRadius),
                "The full streaming radius should continue loading after the player is released.");
            Assert.That(terrain.GenerationStage, Is.EqualTo(MinecraftCaveGenerationStage.Terrain));
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

        private GameObject Create(string name)
        {
            var gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject;
        }
    }
}

