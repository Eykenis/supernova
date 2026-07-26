using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Supernova.Effects;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class VoxelMiningTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<VoxelTypeDefinition> definitions =
            new List<VoxelTypeDefinition>();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
            for (int i = definitions.Count - 1; i >= 0; i--)
            {
                if (definitions[i] != null) Object.DestroyImmediate(definitions[i]);
            }
            definitions.Clear();
        }

        [Test]
        public void VoxelTypeDefinition_ProvidesNameDurabilityAndMaterialTogether()
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            try
            {
                VoxelTypeDefinition definition =
                    CreateDefinition(7, 4, "Crystal", material);

                Assert.That(definition.TypeId, Is.EqualTo(new VoxelTypeId(7)));
                Assert.That(definition.DisplayName, Is.EqualTo("Crystal"));
                Assert.That(definition.Durability, Is.EqualTo(4));
                Assert.That(definition.Material, Is.SameAs(material));
                Assert.That(
                    VoxelTypeUtility.ResolveMaterialColor(
                        new VoxelTypeId(7),
                        new[] { definition },
                        Color.black),
                    Is.EqualTo(material.GetColor(
                        material.HasProperty("_BaseColor")
                            ? "_BaseColor"
                            : "_Color")));
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
                    new[] { CreateDefinition(type.Value, 2, "Stone") });

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
        public void ScheduledCrosshairMining_SettlesExpiredHitBeforeSchedulingNextSwing()
        {
            VoxelTypeCatalog catalog = ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            try
            {
                var type = new VoxelTypeId(2);
                catalog.SetDefinitions(
                    new[] { CreateDefinition(type.Value, 1, "Stone") });

                GameObject terrainObject = Create("Terrain");
                terrainObject.transform.position = Vector3.one * 1000f;
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                SetPrivateField(terrain, "voxelTypeCatalog", catalog);
                SetPrivateField(terrain, "generateColliders", true);
                terrain.InitializeWorld();
                InfiniteVoxelChunk chunk =
                    terrain.World.EnsureChunk(Vector3Int.zero);
                chunk.Data.Fill(-1f, VoxelTypeId.Air);
                var firstVoxel = new Vector3Int(8, 8, 8);
                var secondVoxel = new Vector3Int(8, 8, 9);
                chunk.Data.SetSample(
                    firstVoxel.x,
                    firstVoxel.y,
                    firstVoxel.z,
                    1f,
                    type);
                chunk.Data.SetSample(
                    secondVoxel.x,
                    secondVoxel.y,
                    secondVoxel.z,
                    1f,
                    type);
                InvokePrivate(terrain, "RebuildChunk", Vector3Int.zero);

                GameObject player = Create("Player");
                GameObject cameraObject = Create("ViewCamera");
                cameraObject.transform.SetParent(player.transform, false);
                cameraObject.transform.position =
                    terrainObject.transform.TransformPoint(
                        (Vector3)firstVoxel * terrain.VoxelSize
                        + Vector3.back * terrain.VoxelSize * 3f);
                Camera camera = cameraObject.AddComponent<Camera>();
                VoxelPlayerInteractor interactor =
                    player.AddComponent<VoxelPlayerInteractor>();
                VoxelMiningImpactEffect impactEffect =
                    player.AddComponent<VoxelMiningImpactEffect>();
                SetPrivateField(interactor, "viewCamera", camera);
                SetPrivateField(interactor, "terrain", terrain);
                SetPrivateField(
                    interactor,
                    "miningImpactEffect",
                    impactEffect);
                SetPrivateField(
                    interactor,
                    "raycastMask",
                    Physics.DefaultRaycastLayers);
                Physics.SyncTransforms();

                Assert.That(interactor.TryScheduleMineAtCrosshair(1f), Is.True);
                SetPrivateField(interactor, "pendingMineTime", Time.time - 1f);

                Assert.That(interactor.TryScheduleMineAtCrosshair(1f), Is.True);
                Assert.That(
                    impactEffect.ActiveParticleCount,
                    Is.GreaterThan(0),
                    "The settled brush hit should emit mining particles.");
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        firstVoxel.x,
                        firstVoxel.y,
                        firstVoxel.z).IsSolid(),
                    Is.False);
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        secondVoxel.x,
                        secondVoxel.y,
                        secondVoxel.z).IsSolid(),
                    Is.True);
                Assert.That(
                    GetPrivateField<Vector3Int>(interactor, "pendingMineVoxel"),
                    Is.EqualTo(secondVoxel));

                SetPrivateField(interactor, "pendingMineTime", Time.time - 1f);
                Assert.That(interactor.TryScheduleMineAtCrosshair(1f), Is.False);
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        secondVoxel.x,
                        secondVoxel.y,
                        secondVoxel.z).IsSolid(),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void CrosshairMining_DoesNotSearchPastTheHitSurfaceCell()
        {
            GameObject terrainObject = Create("Terrain");
            terrainObject.transform.position = Vector3.one * 1000f;
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            terrain.InitializeWorld();
            InfiniteVoxelChunk chunk =
                terrain.World.EnsureChunk(Vector3Int.zero);
            chunk.Data.Fill(-1f, VoxelTypeId.Air);
            var rearVoxel = new Vector3Int(0, 0, 1);
            chunk.Data.SetSample(
                rearVoxel.x,
                rearVoxel.y,
                rearVoxel.z,
                1f,
                VoxelTypeId.Default);

            // This collider represents a stale/incorrect visible surface around z=0.
            // The only solid sample is one full voxel behind its surface cell.
            GameObject target = Create("StaleVoxelCollider");
            target.transform.SetParent(terrainObject.transform, false);
            target.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;

            GameObject player = Create("Player");
            GameObject cameraObject = Create("ViewCamera");
            cameraObject.transform.SetParent(player.transform, false);
            cameraObject.transform.position =
                terrainObject.transform.position + Vector3.back;
            Camera camera = cameraObject.AddComponent<Camera>();
            VoxelPlayerInteractor interactor =
                player.AddComponent<VoxelPlayerInteractor>();
            SetPrivateField(interactor, "viewCamera", camera);
            SetPrivateField(interactor, "terrain", terrain);
            SetPrivateField(
                interactor,
                "raycastMask",
                Physics.DefaultRaycastLayers);
            Physics.SyncTransforms();

            Assert.That(interactor.TryMineAtCrosshair(out _), Is.False);
            Assert.That(
                terrain.World.GetSampleOrDefault(
                    rearVoxel.x,
                    rearVoxel.y,
                    rearVoxel.z).IsSolid(),
                Is.True);
        }

        [Test]
        public void MinecraftWorld_MinesTypedVoxelUsingTheSameDurabilityDefinition()
        {
            VoxelTypeCatalog catalog = ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            catalog.SetDefinitions(
                new[] { CreateDefinition(6, 2, "Test Ore") });
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

        [Test]
        public void MiningBrush_DestroysSeveralSamplesButLocksToPrimaryVoxelType()
        {
            VoxelTypeCatalog catalog = ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            try
            {
                var stone = new VoxelTypeId(2);
                var ore = new VoxelTypeId(3);
                catalog.SetDefinitions(
                    new[]
                    {
                        CreateDefinition(stone.Value, 2, "Stone"),
                        CreateDefinition(ore.Value, 1, "Ore"),
                    });

                GameObject terrainObject = Create("Mining Brush Terrain");
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                SetPrivateField(terrain, "voxelTypeCatalog", catalog);
                terrain.InitializeWorld();
                InfiniteVoxelChunk chunk =
                    terrain.World.EnsureChunk(Vector3Int.zero);
                chunk.Data.Fill(-1f, VoxelTypeId.Air);

                var primary = new Vector3Int(8, 8, 8);
                var stoneAtSide = new Vector3Int(9, 8, 8);
                var stoneAtDepth = new Vector3Int(8, 8, 9);
                var oreInsideBrush = new Vector3Int(8, 9, 8);
                chunk.Data.SetSample(primary.x, primary.y, primary.z, 1f, stone);
                chunk.Data.SetSample(
                    stoneAtSide.x,
                    stoneAtSide.y,
                    stoneAtSide.z,
                    1f,
                    stone);
                chunk.Data.SetSample(
                    stoneAtDepth.x,
                    stoneAtDepth.y,
                    stoneAtDepth.z,
                    1f,
                    stone);
                chunk.Data.SetSample(
                    oreInsideBrush.x,
                    oreInsideBrush.y,
                    oreInsideBrush.z,
                    1f,
                    ore);
                var brush = new VoxelMiningBrushSettings(
                    2f,
                    terrain.VoxelSize * 1.5f,
                    terrain.VoxelSize * 2f,
                    1f,
                    1f,
                    24);

                Assert.That(
                    terrain.TryMineBrush(
                        primary,
                        Vector3.forward,
                        brush,
                        out VoxelMiningBrushResult result),
                    Is.True);

                Assert.That(result.TargetType, Is.EqualTo(stone));
                Assert.That(result.DestroyedCount, Is.EqualTo(3));
                Assert.That(result.PrimaryDestroyed, Is.True);
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        primary.x,
                        primary.y,
                        primary.z).IsSolid(),
                    Is.False);
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        stoneAtSide.x,
                        stoneAtSide.y,
                        stoneAtSide.z).IsSolid(),
                    Is.False);
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        stoneAtDepth.x,
                        stoneAtDepth.y,
                        stoneAtDepth.z).IsSolid(),
                    Is.False);
                VoxelSample untouchedOre =
                    terrain.World.GetSampleOrDefault(
                        oreInsideBrush.x,
                        oreInsideBrush.y,
                        oreInsideBrush.z);
                Assert.That(untouchedOre.IsSolid(), Is.True);
                Assert.That(untouchedOre.Type, Is.EqualTo(ore));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void PickaxeAsset_ConfiguresBatchBrushWithoutChangingAnimationMode()
        {
            PlayerToolDefinition pickaxe =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    "Assets/Game/Config/Tools/PickaxeTool.asset");

            Assert.That(pickaxe, Is.Not.Null);
            Assert.That(
                pickaxe.AnimationTriggerMode,
                Is.EqualTo(PlayerToolAnimationTriggerMode.Periodic));
            Assert.That(pickaxe.MiningBrush.Power, Is.EqualTo(2f));
            Assert.That(pickaxe.MiningBrush.Radius, Is.EqualTo(0.55f));
            Assert.That(pickaxe.MiningBrush.Depth, Is.EqualTo(0.75f));
            Assert.That(pickaxe.MiningBrush.FalloffExponent, Is.EqualTo(1.5f));
            Assert.That(
                pickaxe.MiningBrush.MinimumPowerFraction,
                Is.EqualTo(0.25f));
            Assert.That(pickaxe.MiningBrush.MaxAffectedSamples, Is.EqualTo(24));
        }

        [Test]
        public void MiningImpact_EmitsVoxelColoredDustAndChips()
        {
            GameObject host = Create("Mining Impact");
            VoxelMiningImpactEffect effect =
                host.AddComponent<VoxelMiningImpactEffect>();
            var primaryResult = new VoxelMiningResult(
                new Vector3Int(2, 3, 4),
                new VoxelTypeId(2),
                2,
                2,
                true);
            var brushResult = new VoxelMiningBrushResult(
                primaryResult.Coordinate,
                primaryResult.Type,
                3,
                3,
                2,
                primaryResult);

            effect.Play(
                new Vector3(1f, 2f, 3f),
                Vector3.back,
                new Color(0.9f, 0.1f, 0.05f, 1f),
                brushResult);

            ParticleSystem[] systems =
                host.GetComponentsInChildren<ParticleSystem>();
            Assert.That(systems, Has.Length.EqualTo(2));
            Assert.That(effect.ActiveParticleCount, Is.GreaterThan(0));

            var particles = new ParticleSystem.Particle[32];
            int count = systems[0].GetParticles(particles);
            Assert.That(count, Is.GreaterThan(0));
            Assert.That(particles[0].startColor.r, Is.GreaterThan(
                particles[0].startColor.g));
        }

        [Test]
        public void PlayerPrefab_WiresMiningImpactParticleMaterial()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Player.prefab");
            Assert.That(player, Is.Not.Null);

            VoxelMiningImpactEffect effect =
                player.GetComponent<VoxelMiningImpactEffect>();
            Assert.That(effect, Is.Not.Null);
            Material material =
                GetPrivateField<Material>(effect, "particleMaterial");
            Assert.That(material, Is.Not.Null);
            Assert.That(
                material.shader.name,
                Is.EqualTo("Universal Render Pipeline/Particles/Unlit"));
        }

        private VoxelTypeDefinition CreateDefinition(
            ushort type,
            int durability,
            string displayName,
            Material material = null)
        {
            VoxelTypeDefinition definition =
                ScriptableObject.CreateInstance<VoxelTypeDefinition>();
            definition.Configure(type, displayName, durability, material);
            definitions.Add(definition);
            return definition;
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

        private static T GetPrivateField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {target.GetType().Name}.{name}");
            return (T)field.GetValue(target);
        }

        private static void InvokePrivate(
            object target,
            string name,
            params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing method {target.GetType().Name}.{name}");
            method.Invoke(target, arguments);
        }
    }
}
