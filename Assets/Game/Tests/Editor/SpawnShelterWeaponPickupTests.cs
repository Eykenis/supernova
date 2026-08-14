using System;
using System.Linq;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using Supernova.Shop;
using Supernova.WorldGeneration;
using Supernova.Voxels;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Supernova.Tests.Editor
{
    public sealed class SpawnShelterWeaponPickupTests
    {
        [Test]
        public void IsolatedSession_IgnoresPersistentOwnershipAndLoadout()
        {
            string ownershipKey = PlayerEconomy.GetItemOwnershipPreferenceKey(
                PlayerInventoryItem.SMG);
            string slotKey = PlayerEconomy.GetQuickSlotPreferenceKey(0);
            PreferenceSnapshot ownership = new PreferenceSnapshot(ownershipKey);
            PreferenceSnapshot slot = new PreferenceSnapshot(slotKey);
            GameObject player = null;

            try
            {
                PlayerPrefs.SetInt(ownershipKey, 1);
                PlayerPrefs.SetInt(slotKey, (int)PlayerInventoryItem.SMG);

                player = new GameObject("Isolated Inventory Player");
                player.AddComponent<PlayerInventorySessionSettings>()
                    .ConfigurePickaxeOnly();
                PlayerToolController controller =
                    player.AddComponent<PlayerToolController>();

                Assert.That(controller.UsesPersistentPlayerData, Is.False);
                Assert.That(
                    controller.OwnedItems.InventoryItems,
                    Is.EqualTo(new[] { PlayerInventoryItem.Pickaxe }));
                Assert.That(
                    controller.GetItemAtSlot(0),
                    Is.EqualTo(PlayerInventoryItem.Pickaxe));
                for (int i = 1; i < PlayerInventory.SlotCount; i++)
                {
                    Assert.That(
                        controller.GetItemAtSlot(i),
                        Is.EqualTo(PlayerInventoryItem.Empty));
                }

                Assert.That(PlayerPrefs.GetInt(ownershipKey), Is.EqualTo(1));
                Assert.That(
                    PlayerPrefs.GetInt(slotKey),
                    Is.EqualTo((int)PlayerInventoryItem.SMG));
            }
            finally
            {
                if (player != null)
                    UnityEngine.Object.DestroyImmediate(player);
                ownership.Restore();
                slot.Restore();
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void WeaponPickup_AddsWeaponToFirstEmptySlotWithoutPlayerPrefs()
        {
            string ownershipKey = PlayerEconomy.GetItemOwnershipPreferenceKey(
                PlayerInventoryItem.Gun);
            PreferenceSnapshot ownership = new PreferenceSnapshot(ownershipKey);
            GameObject player = null;
            GameObject pickupObject = null;

            try
            {
                PlayerPrefs.DeleteKey(ownershipKey);
                player = new GameObject("Pickup Test Player");
                player.AddComponent<PlayerInventorySessionSettings>()
                    .ConfigurePickaxeOnly();
                PlayerToolController controller =
                    player.AddComponent<PlayerToolController>();
                PlayerToolDefinition rifle =
                    AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                        ProjectAssetPaths.Config.RifleTool);
                Assert.That(rifle, Is.Not.Null);

                pickupObject = new GameObject("Rifle Pickup");
                WeaponPickup pickup = pickupObject.AddComponent<WeaponPickup>();
                pickup.Configure(rifle, null, null);

                Assert.That(pickup.TryCollect(controller), Is.True);
                Assert.That(controller.OwnsItem(PlayerInventoryItem.Gun), Is.True);
                Assert.That(
                    controller.GetItemAtSlot(1),
                    Is.EqualTo(PlayerInventoryItem.Gun));
                Assert.That(PlayerPrefs.HasKey(ownershipKey), Is.False);
                Assert.That(pickupObject.activeSelf, Is.False);
            }
            finally
            {
                if (pickupObject != null)
                    UnityEngine.Object.DestroyImmediate(pickupObject);
                if (player != null)
                    UnityEngine.Object.DestroyImmediate(player);
                ownership.Restore();
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void WeaponPickup_AddsBombToFirstEmptySlot()
        {
            GameObject player = null;
            GameObject pickupObject = null;

            try
            {
                player = new GameObject("Bomb Pickup Test Player");
                player.AddComponent<PlayerInventorySessionSettings>()
                    .ConfigurePickaxeOnly();
                PlayerToolController controller =
                    player.AddComponent<PlayerToolController>();
                PlayerToolDefinition bomb =
                    AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                        ProjectAssetPaths.Config.BombTool);
                Assert.That(bomb, Is.Not.Null);

                pickupObject = new GameObject("Bomb Pickup");
                WeaponPickup pickup = pickupObject.AddComponent<WeaponPickup>();
                pickup.Configure(bomb, null, null);

                Assert.That(pickup.TryCollect(controller), Is.True);
                Assert.That(
                    controller.OwnsItem(PlayerInventoryItem.Bomb),
                    Is.True);
                Assert.That(
                    controller.GetItemAtSlot(1),
                    Is.EqualTo(PlayerInventoryItem.Bomb));
            }
            finally
            {
                if (pickupObject != null)
                    UnityEngine.Object.DestroyImmediate(pickupObject);
                if (player != null)
                    UnityEngine.Object.DestroyImmediate(player);
            }
        }


        [Test]
        public void SpawnShelterScene_HasIsolatedPlayerAndFourWeaponPickups()
        {
            Scene scene = SceneManager.GetSceneByPath(
                ProjectAssetPaths.Scenes.SpawnShelterStoneTest);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    ProjectAssetPaths.Scenes.SpawnShelterStoneTest,
                    OpenSceneMode.Additive);
            }

            try
            {
                PlayerInventorySessionSettings session = scene
                    .GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        PlayerInventorySessionSettings>(true))
                    .Single();
                WeaponPickup[] pickups = scene
                    .GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        WeaponPickup>(true))
                    .ToArray();
                VoxelStructureAsset structure =
                    AssetDatabase.LoadAssetAtPath<VoxelStructureAsset>(
                        ProjectAssetPaths.Structures.SpawnShelter);
                VoxelStructureStoneTestWorld testWorld = scene
                    .GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        VoxelStructureStoneTestWorld>(true))
                    .Single();
                TMP_Text roomMarker = scene
                    .GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<TMP_Text>(true))
                    .Single(label => label.text.Trim() == "Try more props!");

                Assert.That(session.IsolatedFromPersistentData, Is.True);
                Assert.That(structure, Is.Not.Null);
                Assert.That(testWorld, Is.Not.Null);
                Assert.That(roomMarker, Is.Not.Null);
                Assert.That(
                    session.InitialOwnedItems,
                    Is.EqualTo(new[] { PlayerInventoryItem.Pickaxe }));
                Assert.That(pickups, Has.Length.EqualTo(4));
                Assert.That(
                    pickups.Select(pickup => pickup.Item),
                    Is.EquivalentTo(new[]
                    {
                        PlayerInventoryItem.Bomb,
                        PlayerInventoryItem.SMG,
                        PlayerInventoryItem.SolidGun,
                        PlayerInventoryItem.PortalGun,
                    }));
                Vector3Int markerVoxel = testWorld.WorldPositionToVoxel(
                    roomMarker.transform.position);
                Assert.That(
                    pickups.All(pickup =>
                    {
                        Vector3Int pickupVoxel =
                            testWorld.WorldPositionToVoxel(
                                pickup.transform.position);
                        return Mathf.Abs(pickupVoxel.z - markerVoxel.z) <= 6
                            && Mathf.Abs(pickupVoxel.y - markerVoxel.y) <= 6
                            && pickupVoxel.x > 0
                            && pickupVoxel.x < structure.Size.x - 1;
                    }),
                    Is.True,
                    "Pickups must use the test world's voxel scale and stay "
                    + "on the floor beside the Try more props sign.");
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void SpawnShelterScene_UsesDenseRuntimeAndRecoversOreRigidbody()
        {
            Scene scene = SceneManager.GetSceneByPath(
                ProjectAssetPaths.Scenes.SpawnShelterStoneTest);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    ProjectAssetPaths.Scenes.SpawnShelterStoneTest,
                    OpenSceneMode.Additive);
            }

            VoxelStructureStoneTestWorld testWorld = null;
            try
            {
                testWorld = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        VoxelStructureStoneTestWorld>(true))
                    .Single();
                DenseJigsawWorldConfiguration dense =
                    AssetDatabase.LoadAssetAtPath<
                        DenseJigsawWorldConfiguration>(
                        ProjectAssetPaths.Config
                            .DenseJigsawRegionWorldGeneration);

                Assert.That(dense, Is.Not.Null);
                Assert.That(
                    testWorld.RuntimeLevelConfiguration,
                    Is.SameAs(dense.InfiniteCavesLevelSource));
                Assert.That(testWorld.SharedRuntime, Is.Not.Null);
                Assert.That(
                    testWorld.SharedRuntime.World,
                    Is.SameAs(testWorld.World));
                Assert.That(testWorld.IntegrityRuntime, Is.Not.Null);
                Assert.That(
                    testWorld.IntegrityRuntime.SourceTerrain,
                    Is.SameAs(testWorld.SharedRuntime));

                VoxelTypeDefinition bedrock =
                    testWorld.SharedRuntime.BedrockVoxelType;
                VoxelTypeDefinition solidStone =
                    AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                        ProjectAssetPaths.Config.SolidStoneVoxel);
                Assert.That(bedrock, Is.Not.Null);
                Assert.That(solidStone, Is.Not.Null);
                Assert.That(bedrock.IsStructuralSupport, Is.True);
                Assert.That(solidStone.IsStructuralSupport, Is.True);

                Vector3Int worldSize = testWorld.WorldVoxelSize;
                VoxelTypeId[] recoverableOreTypes = testWorld.SharedRuntime
                    .OreFeatures
                    .Where(feature => feature != null
                        && feature.ResultVoxelType != null)
                    .Select(feature => feature.ResultVoxelType.TypeId)
                    .ToArray();
                Assert.That(recoverableOreTypes, Is.Not.Empty);
                VoxelTypeId oreType = VoxelTypeId.Air;

                Vector3Int oreCoordinate = default;
                bool foundOre = false;
                for (int z = 0; z < worldSize.z && !foundOre; z++)
                {
                    for (int y = 0; y < worldSize.y && !foundOre; y++)
                    {
                        for (int x = 0; x < worldSize.x; x++)
                        {
                            VoxelSample sample = testWorld.World
                                .GetSampleOrDefault(x, y, z);
                            if (sample.IsSolid(testWorld.IsoLevel)
                                && recoverableOreTypes.Contains(sample.Type))
                            {
                                oreCoordinate = new Vector3Int(x, y, z);
                                oreType = sample.Type;
                                foundOre = true;
                                break;
                            }
                        }
                    }
                }
                Assert.That(foundOre, Is.True,
                    "SpawnShelter must contain a recoverable ore sample.");

                for (int hit = 0;
                    hit < 128 && testWorld.ActiveOreDrops.Count == 0;
                    hit++)
                {
                    Assert.That(
                        testWorld.TryMineVoxel(oreCoordinate, out _),
                        Is.True);
                }

                Assert.That(testWorld.ActiveOreDrops, Has.Count.EqualTo(1));
                MinedOreDrop drop = testWorld.ActiveOreDrops[0];
                Assert.That(drop.VoxelType, Is.EqualTo(oreType));
                Assert.That(drop.Body, Is.Not.Null);
                Assert.That(drop.Body.isKinematic, Is.False);
                Assert.That(drop.GetComponent<Collider>(), Is.Not.Null);
            }
            finally
            {
                if (testWorld != null)
                {
                    testWorld.Rebuild();
                }
                if (openedForTest)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }


        private readonly struct PreferenceSnapshot
        {
            private readonly string key;
            private readonly bool existed;
            private readonly int value;

            public PreferenceSnapshot(string preferenceKey)
            {
                key = preferenceKey;
                existed = PlayerPrefs.HasKey(key);
                value = PlayerPrefs.GetInt(key, 0);
            }

            public void Restore()
            {
                if (existed)
                    PlayerPrefs.SetInt(key, value);
                else
                    PlayerPrefs.DeleteKey(key);
            }
        }
    }
}
