using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class SolidGunTests
    {
        [Test]
        public void SolidGunConfiguration_UsesFifthSlotAndNonOffensiveProjectile()
        {
            PlayerToolDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.SolidGunTool);
            GameObject solidGun = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.SolidGun);
            GameObject projectilePrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    ProjectAssetPaths.Prefabs.SolidVoxelProjectile);
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.Player);

            Assert.That(definition, Is.Not.Null);
            Assert.That(
                definition.Item,
                Is.EqualTo(PlayerInventoryItem.SolidGun));
            Assert.That(
                definition.PrimaryAction,
                Is.EqualTo(PlayerToolPrimaryAction.FireRifle));
            Assert.That(definition.HeldModelPrefab, Is.SameAs(solidGun));
            Assert.That(definition.FirearmProjectilePrefab, Is.Not.Null);
            Assert.That(
                definition.FirearmProjectilePrefab.gameObject,
                Is.SameAs(projectilePrefab));

            SolidVoxelProjectile projectile =
                projectilePrefab.GetComponent<SolidVoxelProjectile>();
            Assert.That(projectile, Is.Not.Null);
            Assert.That(projectile.PlatformMaterial, Is.Not.Null);
            Assert.That(projectile.PlatformDiameter, Is.EqualTo(5));
            Assert.That(
                player.GetComponent<PlayerToolController>()
                    .GetDefinition(PlayerInventoryItem.SolidGun),
                Is.SameAs(definition));
        }

        [Test]
        public void PlatformMesh_IsSixteenSidedAndMineableAsOneObject()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                ProjectAssetPaths.Materials.SolidPlatform);
            SolidVoxelPrototype platform = SolidVoxelPrototype.Create(
                Vector3.zero,
                5,
                0.42f,
                0.2f,
                0.6f,
                material);
            Mesh mesh = platform.GeneratedMesh;

            Assert.That(
                mesh.triangles.Length / 3,
                Is.EqualTo(SolidVoxelPrototype.PlatformSides * 4));
            Assert.That(platform.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(
                platform.GetComponent<MeshCollider>().sharedMesh,
                Is.SameAs(mesh));
            Assert.That(platform.DestroyByMining(), Is.True);
            Assert.That(platform == null, Is.True);
        }

        [Test]
        public void PickaxeCrosshairHit_DestroysWholePlatform()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                ProjectAssetPaths.Materials.SolidPlatform);
            var player = new GameObject("Player");
            var cameraObject = new GameObject("View Camera");
            cameraObject.transform.SetParent(player.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            SolidVoxelPrototype platform = SolidVoxelPrototype.Create(
                Vector3.forward * 2f,
                5,
                0.42f,
                0.2f,
                0.6f,
                material);
            try
            {
                VoxelPlayerInteractor interactor =
                    player.AddComponent<VoxelPlayerInteractor>();
                SetPrivateField(interactor, "viewCamera", camera);
                SetPrivateField(
                    interactor,
                    "raycastMask",
                    Physics.DefaultRaycastLayers);
                Physics.SyncTransforms();

                Assert.That(
                    interactor.TryScheduleMineAtCrosshair(0f),
                    Is.True);
                Assert.That(platform == null, Is.True);
            }
            finally
            {
                if (platform != null)
                    Object.DestroyImmediate(platform.gameObject);
                Object.DestroyImmediate(player);
            }
        }

        private static void SetPrivateField(
            object target,
            string name,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
