using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class VoxelMiningTests
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
        public void VoxelTypeDefinition_ProvidesDurabilityAndMaterialTogether()
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            try
            {
                var definition = new VoxelTypeDefinition(7, 4, material);

                Assert.That(definition.TypeId, Is.EqualTo(new VoxelTypeId(7)));
                Assert.That(definition.Durability, Is.EqualTo(4));
                Assert.That(definition.Material, Is.SameAs(material));
                Assert.That(
                    VoxelTypeUtility.ResolveDurability(
                        new VoxelTypeId(7),
                        new[] { definition }),
                    Is.EqualTo(4));

                var volume = new VoxelVolume(-1f);
                volume.SetSample(10, 10, 10, 1f, definition.TypeId);
                VoxelMeshData meshData = MarchingCubesMesher.Build(volume);
                Material[] resolved = VoxelTypeUtility.ResolveMaterials(
                    meshData,
                    null,
                    new[] { definition });
                Assert.That(resolved, Has.Length.EqualTo(1));
                Assert.That(resolved[0], Is.SameAs(material));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void MiningProgress_RequiresConfiguredHitCountAndResetsForAnotherType()
        {
            var progress = new VoxelMiningProgress();
            var coordinate = new Vector3Int(3, 4, 5);
            var stone = new VoxelSample(1f, new VoxelTypeId(2));

            Assert.That(progress.TryApplyHit(coordinate, stone, 3, out VoxelMiningResult first), Is.True);
            Assert.That(first.Destroyed, Is.False);
            Assert.That(first.RemainingHits, Is.EqualTo(2));

            progress.TryApplyHit(coordinate, stone, 3, out VoxelMiningResult second);
            Assert.That(second.Destroyed, Is.False);
            Assert.That(second.RemainingHits, Is.EqualTo(1));

            progress.TryApplyHit(coordinate, stone, 3, out VoxelMiningResult third);
            Assert.That(third.Destroyed, Is.True);
            Assert.That(third.RemainingHits, Is.Zero);
            Assert.That(progress.DamagedVoxelCount, Is.Zero);

            var ore = new VoxelSample(1f, new VoxelTypeId(3));
            progress.TryApplyHit(coordinate, stone, 3, out _);
            progress.TryApplyHit(coordinate, ore, 2, out VoxelMiningResult changedType);
            Assert.That(changedType.AccumulatedHits, Is.EqualTo(1));
            Assert.That(changedType.Destroyed, Is.False);
        }

        [Test]
        public void CrosshairMining_UsesDurabilityAndHonoursProfileReach()
        {
            VoxelTypeCatalog catalog = ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            try
            {
                var type = new VoxelTypeId(2);
                catalog.SetDefinitions(
                    new[] { new VoxelTypeDefinition(type.Value, 2) });

                GameObject terrainObject = Create("Terrain");
                terrainObject.transform.position = Vector3.one * 1000f;
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                SetPrivateField(terrain, "voxelTypeCatalog", catalog);
                terrain.InitializeWorld();
                terrain.World.EnsureChunk(Vector3Int.zero).Data.SetSample(
                    0,
                    0,
                    0,
                    1f,
                    type);

                GameObject target = Create("VoxelCollider");
                target.transform.SetParent(terrainObject.transform, false);
                target.transform.localPosition = Vector3.zero;
                target.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;

                GameObject player = Create("Player");
                GameObject cameraObject = Create("ViewCamera");
                cameraObject.transform.SetParent(player.transform, false);
                cameraObject.transform.position = terrainObject.transform.position + Vector3.back;
                Camera camera = cameraObject.AddComponent<Camera>();
                VoxelPlayerInteractor interactor = player.AddComponent<VoxelPlayerInteractor>();
                Assert.That(player.GetComponent<PlayerProfile>(), Is.Not.Null);
                SetPrivateField(interactor, "viewCamera", camera);
                SetPrivateField(interactor, "terrain", terrain);
                SetPrivateField(interactor, "raycastMask", Physics.DefaultRaycastLayers);
                Physics.SyncTransforms();

                Assert.That(interactor.TryMineAtCrosshair(out VoxelMiningResult first), Is.True);
                Assert.That(first.Destroyed, Is.False);
                Assert.That(terrain.World.GetSampleOrDefault(0, 0, 0).IsSolid(), Is.True);

                Assert.That(interactor.TryMineAtCrosshair(out VoxelMiningResult second), Is.True);
                Assert.That(second.Destroyed, Is.True);
                Assert.That(terrain.World.GetSampleOrDefault(0, 0, 0).IsSolid(), Is.False);

                terrain.World.SetVoxel(0, 0, 0, 1f, type);
                cameraObject.transform.position = terrainObject.transform.position + Vector3.back * 5f;
                Physics.SyncTransforms();
                Assert.That(interactor.TryMineAtCrosshair(out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void MinecraftWorld_MinesTypedVoxelUsingTheSameDurabilityDefinition()
        {
            VoxelTypeCatalog catalog = ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            catalog.SetDefinitions(
                new[] { new VoxelTypeDefinition(6, 2) });
            GameObject terrainObject = Create("MinecraftTerrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            SetPrivateField(terrain, "voxelTypeCatalog", catalog);
            terrain.InitializeWorld();
            var coordinate = new Vector3Int(2, 3, 4);
            var type = new VoxelTypeId(6);
            terrain.World.EnsureChunk(Vector3Int.zero).Data.SetSample(
                coordinate.x,
                coordinate.y,
                coordinate.z,
                1f,
                type);
            Assert.That(terrain.TryMineVoxel(coordinate, out VoxelMiningResult first), Is.True);
            Assert.That(first.Destroyed, Is.False);
            Assert.That(terrain.TryMineVoxel(coordinate, out VoxelMiningResult second), Is.True);
            Assert.That(second.Destroyed, Is.True);
            Assert.That(terrain.World.GetSampleOrDefault(2, 3, 4).IsSolid(), Is.False);
            Object.DestroyImmediate(catalog);
        }

        private GameObject Create(string name)
        {
            var gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject;
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {target.GetType().Name}.{name}");
            field.SetValue(target, value);
        }
    }
}
