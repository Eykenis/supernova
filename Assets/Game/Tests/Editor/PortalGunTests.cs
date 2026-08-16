using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using Supernova.PortalExample;
using Supernova.Shop;
using Supernova.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace Supernova.Tests
{
    public sealed class PortalGunTests
    {
        [Test]
        public void Configuration_UsesPortalGunPresentationAndFiresPortalProjectile()
        {
            PlayerToolDefinition portalGun =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.PortalGunTool);
            PlayerToolDefinition solidGun =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.SolidGunTool);
            GameObject projectilePrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    ProjectAssetPaths.Prefabs.PortalGunProjectile);
            GameObject portalGunModel =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    ProjectAssetPaths.Prefabs.PortalGun);
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.Player);
            EquipmentIconCatalog icons =
                AssetDatabase.LoadAssetAtPath<EquipmentIconCatalog>(
                    ProjectAssetPaths.Config.EquipmentIconCatalog);
            ShopProductProfile product =
                AssetDatabase.LoadAssetAtPath<ShopProductProfile>(
                    ProjectAssetPaths.Config.PortalGunProduct);

            Assert.That(portalGun, Is.Not.Null);
            Assert.That(solidGun, Is.Not.Null);
            Assert.That(projectilePrefab, Is.Not.Null);
            Assert.That(portalGun.Item, Is.EqualTo(PlayerInventoryItem.PortalGun));
            Assert.That(
                portalGun.PrimaryAction,
                Is.EqualTo(PlayerToolPrimaryAction.FireProjectile));
            Assert.That(portalGun.PrimaryActionSound, Is.Not.Null);
            Assert.That(
                portalGun.PrimaryActionSound,
                Is.Not.SameAs(solidGun.PrimaryActionSound));
            Assert.That(
                AssetDatabase.GetAssetPath(portalGun.PrimaryActionSound),
                Is.EqualTo(ProjectAssetPaths.Config.PortalGunShotSound));
            SerializedProperty shotClips =
                new SerializedObject(portalGun.PrimaryActionSound)
                    .FindProperty("clips");
            Assert.That(shotClips.arraySize, Is.EqualTo(1));
            Assert.That(
                AssetDatabase.GetAssetPath(
                    shotClips.GetArrayElementAtIndex(0)
                        .objectReferenceValue),
                Is.EqualTo(ProjectAssetPaths.Audio.PortalShot));
            Assert.That(
                portalGun.HeldModelPrefab,
                Is.SameAs(portalGunModel));
            Assert.That(
                portalGun.PrimaryActionAnimation,
                Is.SameAs(solidGun.PrimaryActionAnimation));
            Assert.That(
                portalGun.MuzzleFlashPrefab,
                Is.SameAs(solidGun.MuzzleFlashPrefab));
            Assert.That(
                portalGun.FirearmProjectilePrefab.gameObject,
                Is.SameAs(projectilePrefab));
            Assert.That(
                projectilePrefab.GetComponent<PortalGunProjectile>(),
                Is.Not.Null);
            Assert.That(
                player.GetComponent<PlayerToolController>()
                    .GetDefinition(PlayerInventoryItem.PortalGun),
                Is.SameAs(portalGun));
            Assert.That(
                icons.GetIcon(PlayerInventoryItem.PortalGun),
                Is.SameAs(icons.GetIcon(PlayerInventoryItem.SolidGun)));
            Assert.That(
                HotbarPresenter.GetItemLabel(PlayerInventoryItem.PortalGun),
                Is.EqualTo("传送门发生器"));
            Assert.That(product, Is.Not.Null);
            Assert.That(
                product.GrantedItem,
                Is.EqualTo(PlayerInventoryItem.PortalGun));
            Assert.That(
                product.DisplayPrefab,
                Is.SameAs(portalGunModel));
        }

        [Test]
        public void SpawnedCheckpointPortals_AllLinkToSameLandingCellGate()
        {
            GameObject bridgeObject = new GameObject("Portal Bridge");
            GameObject landingObject = new GameObject("Landing Cell Portal");
            GameObject templateObject = new GameObject("Checkpoint Template");
            landingObject.transform.SetParent(bridgeObject.transform, false);
            templateObject.transform.SetParent(bridgeObject.transform, false);
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

            try
            {
                Assert.That(
                    bridge.TryCreateSpawnCheckpointPortal(
                        new Vector3(2f, 3f, 4f),
                        Vector3.up,
                        Vector3.forward,
                        out PortalExampleGate first),
                    Is.True);
                Assert.That(
                    bridge.TryCreateSpawnCheckpointPortal(
                        new Vector3(-3f, 1f, 7f),
                        Vector3.right,
                        Vector3.up,
                        out PortalExampleGate second),
                    Is.True);

                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(first.LinkedGate, Is.SameAs(landingGate));
                Assert.That(second.LinkedGate, Is.SameAs(landingGate));
                Assert.That(
                    first.name,
                    Is.EqualTo(DenseJigsawPortalBridge
                        .SpawnCheckpointPortalName));
                Assert.That(
                    second.name,
                    Is.EqualTo(DenseJigsawPortalBridge
                        .SpawnCheckpointPortalName));
                Assert.That(
                    first.transform.forward,
                    Is.EqualTo(Vector3.up)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    first.transform.position.y,
                    Is.EqualTo(
                         3f + bridge.SpawnedPortalSurfaceOffset)
                        .Within(0.0001f));
                Assert.That(
                    bridge.SpawnedPortalSurfaceOffset,
                    Is.EqualTo(0.06f));
                Assert.That(
                    first.transform.localScale,
                    Is.EqualTo(
                            Vector3.one
                            * 0.6f
                            * bridge.SpawnedPortalScaleMultiplier)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    second.transform.forward,
                    Is.EqualTo(Vector3.right)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    bridge.SpawnedCheckpointGates,
                    Has.Count.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(bridgeObject);
            }
        }

        [Test]
        public void PortalBridge_BindsLandingGateAndKeepsCheckpointWorldPlaced()
        {
            GameObject bridgeObject = new GameObject("Portal Bridge");
            GameObject landingCellObject = new GameObject("Landing Cell");
            GameObject spawnMarkerObject = new GameObject("Player Spawn");
            GameObject checkpointSupport = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            spawnMarkerObject.transform.SetParent(
                landingCellObject.transform,
                false);
            landingCellObject.transform.SetPositionAndRotation(
                new Vector3(3f, 1f, -2f),
                Quaternion.Euler(0f, 25f, 0f));
            landingCellObject.transform.localScale = Vector3.one * 0.7f;
            bridgeObject.transform.SetParent(landingCellObject.transform, true);

            try
            {
                SpawnPointSceneStructure landingCell =
                    landingCellObject.AddComponent<SpawnPointSceneStructure>();
                landingCell.Configure(spawnMarkerObject.transform);

                GameObject landingObject = new GameObject("Landing Cell Portal");
                GameObject checkpointObject = new GameObject("Checkpoint Portal");
                landingObject.transform.SetParent(bridgeObject.transform, false);
                checkpointObject.transform.SetParent(bridgeObject.transform, false);
                PortalExampleGate landingGate =
                    landingObject.AddComponent<PortalExampleGate>();
                PortalExampleGate checkpointGate =
                    checkpointObject.AddComponent<PortalExampleGate>();

                Vector3 authoredPosition = new Vector3(12f, 4f, -7f);
                Quaternion authoredRotation = Quaternion.Euler(15f, 80f, 5f);
                Vector3 authoredScale = new Vector3(0.72f, 0.68f, 0.7f);
                landingGate.transform.SetPositionAndRotation(
                    authoredPosition,
                    authoredRotation);
                landingGate.transform.localScale = authoredScale;
                Vector3 authoredLocalPosition =
                    landingGate.transform.localPosition;
                Quaternion authoredLocalRotation =
                    landingGate.transform.localRotation;
                landingGate.gameObject.SetActive(false);
                checkpointGate.gameObject.SetActive(false);

                checkpointSupport.transform.SetPositionAndRotation(
                    new Vector3(-6f, 3f, 9f),
                    Quaternion.identity);
                checkpointSupport.transform.localScale = new Vector3(4f, 2f, 4f);
                Renderer supportRenderer =
                    checkpointSupport.GetComponent<Renderer>();
                Physics.SyncTransforms();

                DenseJigsawPortalBridge bridge =
                    bridgeObject.AddComponent<DenseJigsawPortalBridge>();
                bridge.Configure(
                    null,
                    landingCell,
                    null,
                    landingGate,
                    checkpointGate);
                FieldInfo checkpointField = typeof(DenseJigsawPortalBridge)
                    .GetField(
                        "primaryCheckpoint",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(checkpointField, Is.Not.Null);
                checkpointField.SetValue(bridge, checkpointSupport);

                Assert.That(bridge.TryPlacePortals(), Is.True);
                Assert.That(
                    landingGate.transform.localPosition,
                    Is.EqualTo(authoredLocalPosition)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    Quaternion.Angle(
                        landingGate.transform.localRotation,
                        authoredLocalRotation),
                    Is.LessThan(0.001f));
                Assert.That(
                    landingGate.transform.localScale,
                    Is.EqualTo(authoredScale)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(landingGate.gameObject.activeSelf, Is.True);

                Assert.That(
                    checkpointGate.transform.forward,
                    Is.EqualTo(Vector3.up)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    checkpointGate.transform.position.y,
                    Is.EqualTo(supportRenderer.bounds.max.y + 0.005f)
                        .Within(0.0001f));
                Assert.That(
                    checkpointGate.transform.lossyScale,
                    Is.EqualTo(Vector3.one * 0.6f)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));

                landingCell.PlaceAt(
                    new Vector3(30f, 6f, -15f),
                    Quaternion.Euler(0f, 110f, 0f));
                Physics.SyncTransforms();

                Assert.That(bridge.transform.parent,
                    Is.SameAs(landingCell.transform));
                Assert.That(
                    landingGate.transform.localPosition,
                    Is.EqualTo(authoredLocalPosition)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    Quaternion.Angle(
                        landingGate.transform.localRotation,
                        authoredLocalRotation),
                    Is.LessThan(0.001f));
                Assert.That(
                    checkpointGate.transform.position,
                    Is.EqualTo(new Vector3(
                            supportRenderer.bounds.center.x,
                            supportRenderer.bounds.max.y + 0.005f,
                            supportRenderer.bounds.center.z))
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(
                    checkpointGate.transform.lossyScale,
                    Is.EqualTo(Vector3.one * 0.6f)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
            }
            finally
            {
                Object.DestroyImmediate(bridgeObject);
                Object.DestroyImmediate(landingCellObject);
                Object.DestroyImmediate(checkpointSupport);
            }
        }
    }
}
