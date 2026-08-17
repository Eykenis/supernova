using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Supernova.Audio;
using Supernova.Gameplay;
using Supernova.Infrastructure;
using Supernova.MinecraftCaves;
using Supernova.MinecraftCaves.Creatures;
using Supernova.Shop;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class BombToolTests
    {
        private readonly List<Object> objects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                    Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        [Test]
        public void BombExplosionSettings_UseRequestedDistanceBands()
        {
            VoxelExplosionSettings settings = VoxelExplosionSettings.Bomb;

            Assert.That(settings.Radius, Is.EqualTo(2f));
            Assert.That(settings.InnerRadius, Is.EqualTo(1f));
            Assert.That(settings.GetPower(0f), Is.EqualTo(30f));
            Assert.That(settings.GetPower(1f), Is.EqualTo(30f));
            Assert.That(settings.GetPower(1.01f), Is.EqualTo(10f));
            Assert.That(settings.GetPower(2f), Is.EqualTo(10f));
            Assert.That(settings.GetPower(2.01f), Is.Zero);
            Assert.That(settings.PropagationDivisor, Is.EqualTo(2f));
        }

        [Test]
        public void MinecraftWorld_BombAppliesThirtyTenAndStopsAtRadiusTwo()
        {
            VoxelTypeCatalog catalog =
                ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            objects.Add(catalog);
            VoxelTypeDefinition innerDefinition = CreateDefinition(2, 30);
            VoxelTypeDefinition outerDefinition = CreateDefinition(3, 11);
            VoxelTypeDefinition beyondDefinition = CreateDefinition(4, 1);
            catalog.SetDefinitions(
                new[]
                {
                    innerDefinition,
                    outerDefinition,
                    beyondDefinition,
                });

            GameObject terrainObject = new GameObject("Bomb Terrain");
            objects.Add(terrainObject);
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            SetPrivateField(terrain, "voxelTypeCatalog", catalog);
            SetPrivateField(terrain, "voxelSize", 1f);
            terrain.InitializeWorld();
            InfiniteVoxelChunk chunk = terrain.World.EnsureChunk(Vector3Int.zero);
            chunk.Data.Fill(-1f, VoxelTypeId.Air);

            var center = new Vector3Int(8, 8, 8);
            Vector3Int inner = center + Vector3Int.right;
            Vector3Int outer = center + Vector3Int.right * 2;
            Vector3Int beyond = center + Vector3Int.right * 3;
            SetSolid(chunk, inner, innerDefinition.TypeId);
            SetSolid(chunk, outer, outerDefinition.TypeId);
            SetSolid(chunk, beyond, beyondDefinition.TypeId);

            Assert.That(
                terrain.TryMineExplosion(
                    center,
                    VoxelExplosionSettings.Bomb,
                    out VoxelExplosionResult result),
                Is.True);

            Assert.That(result.CandidateCount, Is.EqualTo(2));
            Assert.That(result.DamagedCount, Is.EqualTo(2));
            Assert.That(result.DestroyedCount, Is.EqualTo(1));
            Assert.That(Sample(terrain, inner).IsSolid(), Is.False);
            Assert.That(Sample(terrain, outer).IsSolid(), Is.True);
            Assert.That(Sample(terrain, beyond).IsSolid(), Is.True);

            Assert.That(
                terrain.TryMineVoxel(outer, out VoxelMiningResult outerResult),
                Is.True,
                "The outer voxel should retain exactly ten damage.");
            Assert.That(outerResult.Destroyed, Is.True);
        }

        [Test]
        public void BombAssets_RegisterAFreeTimedThrowingTool()
        {
            PlayerToolDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.BombTool);
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.Player);

            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Item, Is.EqualTo(PlayerInventoryItem.Bomb));
            Assert.That(
                definition.PrimaryAction,
                Is.EqualTo(PlayerToolPrimaryAction.ThrowBomb));
            Assert.That(definition.HeldModelPrefab, Is.Not.Null);
            Assert.That(definition.BombProjectilePrefab, Is.Not.Null);
            Assert.That(definition.BombEntityExplosionImpulse, Is.EqualTo(600f));
            Assert.That(definition.BombExplosionEffectPrefab, Is.Not.Null);
            Assert.That(definition.BombExplosionEffectLifetime, Is.EqualTo(3f));
            Assert.That(definition.ThrowSpeed, Is.GreaterThan(0f));
            Assert.That(definition.ActionIsPeriodic, Is.False);
            SoundEffectCue fuseCue =
                AssetDatabase.LoadAssetAtPath<SoundEffectCue>(
                    ProjectAssetPaths.Config.BombFuseSound);
            Assert.That(fuseCue, Is.Not.Null);
            Assert.That(definition.PrimaryActionSound, Is.SameAs(fuseCue));
            Assert.That(definition.ThrowSound, Is.Null);
            Assert.That(PlayerEconomy.IsItemOwned(PlayerInventoryItem.Bomb), Is.True);
            Assert.That(player, Is.Not.Null);
            Assert.That(
                player.GetComponent<PlayerToolController>()
                    .GetDefinition(PlayerInventoryItem.Bomb),
                Is.SameAs(definition));

            BombProjectile projectile = definition.BombProjectilePrefab;
            Assert.That(projectile.FuseSeconds, Is.EqualTo(2f));
            Assert.That(projectile.ExplosionRadius, Is.EqualTo(2f));
            Assert.That(projectile.InnerRadius, Is.EqualTo(1f));
            Assert.That(projectile.InnerMiningPower, Is.EqualTo(30f));
            Assert.That(projectile.OuterMiningPower, Is.EqualTo(10f));
            Assert.That(projectile.PropagationDivisor, Is.EqualTo(2f));
            Assert.That(projectile.EntityExplosionImpulse, Is.EqualTo(600f));
            Assert.That(projectile.EntityUpwardModifier, Is.EqualTo(0.6f));
            Assert.That(
                projectile.ExplosionEffectPrefab,
                Is.SameAs(definition.BombExplosionEffectPrefab));
            Assert.That(projectile.ExplosionEffectLifetime, Is.EqualTo(3f));
            Assert.That(projectile.ConfigurationVersion, Is.EqualTo(5));

            string effectPath = AssetDatabase.GetAssetPath(
                definition.BombExplosionEffectPrefab).Replace('\\', '/');
            StringAssert.StartsWith("Assets/Game/", effectPath);
            string[] dependencies = AssetDatabase.GetDependencies(
                effectPath,
                true);
            for (int i = 0; i < dependencies.Length; i++)
            {
                StringAssert.DoesNotStartWith(
                    "Assets/3rd/",
                    dependencies[i].Replace('\\', '/'));
            }
        }

        [Test]
        public void BombProjectile_DetonatesOnlyOnce()
        {
            var bombObject = new GameObject("Bomb");
            objects.Add(bombObject);
            bombObject.AddComponent<Rigidbody>();
            bombObject.AddComponent<SphereCollider>();
            BombProjectile bomb = bombObject.AddComponent<BombProjectile>();

            bomb.Launch(Vector3.forward, Vector3.up, null, 123f);

            Assert.That(bomb.IsArmed, Is.True);
            Assert.That(bomb.ActiveEntityExplosionImpulse, Is.EqualTo(123f));
            Assert.That(bomb.Detonate(), Is.True);
            Assert.That(bomb.HasExploded, Is.True);
            Assert.That(bomb.IsArmed, Is.False);
            Assert.That(bomb.Detonate(), Is.False);
        }

        [Test]
        public void BombProjectile_DetonationBroadcastsExplosionOnlyOnce()
        {
            GameAssetCatalog catalog =
                AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(
                    ProjectAssetPaths.Config.GameAssetCatalog);
            SoundEffectCue explosionCue =
                AssetDatabase.LoadAssetAtPath<SoundEffectCue>(
                    ProjectAssetPaths.Config.BombExplosionSound);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(explosionCue, Is.Not.Null);
            Assert.That(catalog.Audio.BombExplosion, Is.SameAs(explosionCue));

            Vector3 explosionPosition = new Vector3(20f, 30f, 40f);
            var bombObject = new GameObject("Sound Bomb");
            objects.Add(bombObject);
            bombObject.transform.position = explosionPosition;
            Rigidbody body = bombObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            bombObject.AddComponent<SphereCollider>();
            BombProjectile bomb = bombObject.AddComponent<BombProjectile>();

            int requestCount = 0;
            SoundEffectPlaybackRequest received = default;
            System.Action<SoundEffectPlaybackRequest> observer = request =>
            {
                requestCount++;
                received = request;
            };
            SoundEffectEvents.PlaybackRequested += observer;
            try
            {
                bomb.Launch(Vector3.zero, Vector3.zero, null, 0f);
                Assert.That(bomb.Detonate(), Is.True);
                Assert.That(bomb.Detonate(), Is.False);
                Assert.That(requestCount, Is.EqualTo(1));
                Assert.That(received.Cue, Is.SameAs(explosionCue));
                Assert.That(received.Position, Is.EqualTo(explosionPosition));
            }
            finally
            {
                SoundEffectEvents.PlaybackRequested -= observer;
            }
        }


        [Test]
        public void BombProjectile_AppliesOneLargeImpulsePerNearbyBody()
        {
            Vector3 explosionCenter = new Vector3(1000f, 1000f, 1000f);
            var bombObject = new GameObject("Bomb");
            objects.Add(bombObject);
            bombObject.transform.position = explosionCenter;
            Rigidbody bombBody = bombObject.AddComponent<Rigidbody>();
            bombBody.useGravity = false;
            bombObject.AddComponent<SphereCollider>();
            BombProjectile bomb = bombObject.AddComponent<BombProjectile>();

            var nearbyObject = new GameObject("Nearby Entity");
            objects.Add(nearbyObject);
            nearbyObject.transform.position = explosionCenter + Vector3.right;
            Rigidbody nearbyBody = nearbyObject.AddComponent<Rigidbody>();
            nearbyBody.useGravity = false;
            nearbyObject.AddComponent<SphereCollider>();
            var extraColliderObject = new GameObject("Extra Collider");
            extraColliderObject.transform.SetParent(
                nearbyObject.transform,
                false);
            extraColliderObject.AddComponent<BoxCollider>();

            var distantObject = new GameObject("Distant Entity");
            objects.Add(distantObject);
            distantObject.transform.position =
                explosionCenter + Vector3.right * 3f;
            Rigidbody distantBody = distantObject.AddComponent<Rigidbody>();
            distantBody.useGravity = false;
            distantObject.AddComponent<SphereCollider>();
            Physics.SyncTransforms();

            bomb.Launch(Vector3.zero, Vector3.zero, null);
            Assert.That(bomb.Detonate(), Is.True);

            Assert.That(bomb.LastImpulsedBodyCount, Is.EqualTo(1));
            Vector3 expectedImpulse = BombProjectile.CalculateEntityImpulse(
                explosionCenter,
                nearbyBody.worldCenterOfMass,
                bomb.ActiveEntityExplosionImpulse,
                bomb.ExplosionRadius,
                bomb.EntityUpwardModifier);
            Assert.That(expectedImpulse.x, Is.GreaterThan(100f));
            Assert.That(expectedImpulse.y, Is.GreaterThan(0f));
            Assert.That(distantBody.velocity, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void BombProjectile_ImmediatelyDamagesNearbyTreasureOnce()
        {
            Vector3 explosionCenter = new Vector3(1100f, 1000f, 1000f);
            BombProjectile bomb = CreateBomb(explosionCenter);

            var treasureObject = new GameObject("Nearby Treasure");
            objects.Add(treasureObject);
            treasureObject.transform.position = explosionCenter + Vector3.right;
            Rigidbody treasureBody = treasureObject.AddComponent<Rigidbody>();
            treasureBody.useGravity = false;
            treasureObject.AddComponent<SphereCollider>();
            ValuableObject valuable = treasureObject.AddComponent<ValuableObject>();
            valuable.Configure(100, 1f, 1f, 0.01f);
            var extraColliderObject = new GameObject("Extra Treasure Collider");
            extraColliderObject.transform.SetParent(treasureObject.transform, false);
            extraColliderObject.AddComponent<BoxCollider>();
            Physics.SyncTransforms();

            bomb.Launch(Vector3.zero, Vector3.zero, null, 10f);
            Assert.That(bomb.Detonate(), Is.True);

            Assert.That(valuable.CurrentValue, Is.LessThan(100));
            Assert.That(bomb.LastDamagedEntityCount, Is.EqualTo(1));
            Assert.That(bomb.LastImpulsedBodyCount, Is.EqualTo(1));
        }

        [Test]
        public void BombProjectile_ImmediatelyDamagesNearbyMonster()
        {
            Vector3 explosionCenter = new Vector3(1200f, 1000f, 1000f);
            BombProjectile bomb = CreateBomb(explosionCenter);

            var monsterObject = new GameObject("Nearby Monster");
            objects.Add(monsterObject);
            monsterObject.transform.position = explosionCenter + Vector3.right;
            Rigidbody monsterBody = monsterObject.AddComponent<Rigidbody>();
            monsterBody.mass = 20f;
            monsterBody.useGravity = false;
            monsterObject.AddComponent<CapsuleCollider>();
            CreatureBehaviorAgent monster =
                monsterObject.AddComponent<CreatureBehaviorAgent>();
            float initialHealth = monster.CurrentHealth;
            Physics.SyncTransforms();

            bomb.Launch(Vector3.zero, Vector3.zero, null, 600f);
            Assert.That(bomb.Detonate(), Is.True);

            Assert.That(monster.CurrentHealth, Is.LessThan(initialHealth));
            Assert.That(bomb.LastDamagedEntityCount, Is.EqualTo(1));
        }

        [Test]
        public void BombProjectile_DamagesAndLaunchesNearbyPlayer()
        {
            Vector3 explosionCenter =
                new Vector3(1250f, 1000f, 1000f);
            BombProjectile bomb = CreateBomb(explosionCenter);
            VoxelPlayerController player =
                CreatePlayer(explosionCenter);
            float initialHealth = player.CurrentHealth;
            Physics.SyncTransforms();

            bomb.Launch(Vector3.zero, Vector3.zero, null, 600f);
            Assert.That(bomb.Detonate(), Is.True);

            Assert.That(player.CurrentHealth, Is.LessThan(initialHealth));
            Assert.That(player.CombinedVelocity.magnitude, Is.GreaterThan(0f));
            Assert.That(player.CombinedVelocity.magnitude, Is.LessThanOrEqualTo(
                VoxelPlayerController.DefaultMaximumExplosionVelocity));
            Assert.That(bomb.LastDamagedEntityCount, Is.EqualTo(1));
        }

        [Test]
        public void MysticCore_ReachingZeroValueEmitsBombExplosion()
        {
            TreasureDefinition definition =
                AssetDatabase.LoadAssetAtPath<TreasureDefinition>(
                    ProjectAssetPaths.Config.MysticCoreTreasure);
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Prefab, Is.Not.Null);

            GameObject core = Object.Instantiate(definition.Prefab);
            objects.Add(core);
            core.transform.position =
                new Vector3(1275f, 1000f, 1000f);
            MeshCollider[] meshColliders =
                core.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < meshColliders.Length; i++)
            {
                meshColliders[i].convex = true;
            }
            if (core.GetComponent<Rigidbody>() == null)
            {
                core.AddComponent<Rigidbody>();
            }
            if (core.GetComponentInChildren<Collider>() == null)
            {
                core.AddComponent<BoxCollider>();
            }
            TreasurePickup pickup = core.GetComponent<TreasurePickup>();
            if (pickup == null)
            {
                pickup = core.AddComponent<TreasurePickup>();
            }
            pickup.Configure(definition);
            TreasureDestructionExplosion explosion =
                core.GetComponent<TreasureDestructionExplosion>();
            Assert.That(explosion, Is.Not.Null);

            VoxelPlayerController player =
                CreatePlayer(core.transform.position);
            float initialHealth = player.CurrentHealth;
            Physics.SyncTransforms();

            pickup.Valuable.ApplyCollisionImpulse(
                definition.Weight * 100f,
                core.transform.position);

            Assert.That(pickup.Valuable.CurrentValue, Is.Zero);
            Assert.That(explosion.HasExploded, Is.True);
            Assert.That(player.CurrentHealth, Is.LessThan(initialHealth));
            Assert.That(player.CombinedVelocity.magnitude, Is.GreaterThan(0f));
            if (explosion.LastExplosionResult.Effect != null)
            {
                objects.Add(explosion.LastExplosionResult.Effect);
            }
        }

        [Test]
        public void BombProjectile_SpawnsConfiguredExplosionEffect()
        {
            Vector3 explosionCenter = new Vector3(1300f, 1000f, 1000f);
            BombProjectile bomb = CreateBomb(explosionCenter);
            var effectPrefab = new GameObject("Configured Explosion Effect");
            objects.Add(effectPrefab);

            bomb.Launch(
                Vector3.zero,
                Vector3.zero,
                null,
                0f,
                effectPrefab,
                1.5f);
            Assert.That(bomb.Detonate(), Is.True);

            Assert.That(bomb.LastExplosionEffect, Is.Not.Null);
            Assert.That(bomb.LastExplosionEffect, Is.Not.SameAs(effectPrefab));
            Assert.That(bomb.ActiveExplosionEffectLifetime, Is.EqualTo(1.5f));
            objects.Add(bomb.LastExplosionEffect);
        }

        private BombProjectile CreateBomb(Vector3 position)
        {
            var bombObject = new GameObject("Bomb");
            objects.Add(bombObject);
            bombObject.transform.position = position;
            Rigidbody body = bombObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            bombObject.AddComponent<SphereCollider>();
            return bombObject.AddComponent<BombProjectile>();
        }

        private VoxelPlayerController CreatePlayer(Vector3 position)
        {
            var playerObject = new GameObject("Explosion Player");
            objects.Add(playerObject);
            playerObject.transform.position = position;
            playerObject.AddComponent<CharacterController>();
            return playerObject.AddComponent<VoxelPlayerController>();
        }

        private VoxelTypeDefinition CreateDefinition(
            ushort type,
            int durability)
        {
            VoxelTypeDefinition definition =
                ScriptableObject.CreateInstance<VoxelTypeDefinition>();
            definition.Configure(
                type,
                "Bomb Test " + type,
                durability,
                null);
            objects.Add(definition);
            return definition;
        }

        private static void SetSolid(
            InfiniteVoxelChunk chunk,
            Vector3Int coordinate,
            VoxelTypeId type)
        {
            chunk.Data.SetSample(
                coordinate.x,
                coordinate.y,
                coordinate.z,
                1f,
                type);
        }

        private static VoxelSample Sample(
            MinecraftCaveInfiniteWorld terrain,
            Vector3Int coordinate)
        {
            return terrain.World.GetSampleOrDefault(
                coordinate.x,
                coordinate.y,
                coordinate.z);
        }

        private static void SetPrivateField(
            object target,
            string name,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }
    }
}
