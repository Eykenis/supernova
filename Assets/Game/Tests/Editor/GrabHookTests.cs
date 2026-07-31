using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests.Editor
{
    public sealed class GrabHookTests
    {
        private GameObject terrainRoot;
        private GameObject ordinaryRoot;
        private GameObject blocker;
        private GameObject endpoint;

        [TearDown]
        public void TearDown()
        {
            Destroy(terrainRoot);
            Destroy(ordinaryRoot);
            Destroy(blocker);
            Destroy(endpoint);
        }

        [Test]
        public void GrabHookDefinition_ConfiguresEighthSlotAndPhysicalLimits()
        {
            PlayerToolDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.GrabHookTool);

            Assert.That(definition, Is.Not.Null);
            Assert.That(
                definition.Item,
                Is.EqualTo(PlayerInventoryItem.GrabHook));
            Assert.That(
                definition.PrimaryAction,
                Is.EqualTo(PlayerToolPrimaryAction.FireGrabHook));
            Assert.That(definition.HeldModelPrefab, Is.Not.Null);
            Assert.That(definition.GrabHookProjectileModelPrefab, Is.Not.Null);
            Assert.That(definition.GrabHookMaximumLength, Is.GreaterThan(1f));
            Assert.That(
                definition.GrabHookAimPredictionDuration,
                Is.GreaterThan(0f));
            Assert.That(definition.GrabHookArrivalDistance, Is.GreaterThan(0f));
            Assert.That(definition.GrabHookPullAcceleration, Is.GreaterThan(0f));
            Assert.That(definition.AllowMovementWhileUsing, Is.True);
        }

        [Test]
        public void BallisticPrediction_AppliesInitialVelocityAndGravity()
        {
            Vector3 position =
                GrabHookController.CalculateBallisticPosition(
                    Vector3.zero,
                    new Vector3(4f, 10f, 0f),
                    new Vector3(0f, -10f, 0f),
                    1f);

            Assert.That(position.x, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(position.y, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(position.z, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void HookRotation_PointsForwardAlongFlightVelocity()
        {
            Vector3 velocity = new Vector3(3f, -4f, 5f);
            Quaternion rotation =
                GrabHookController.CalculateHookRotation(velocity);

            Assert.That(
                Vector3.Angle(rotation * Vector3.forward, velocity),
                Is.LessThan(0.01f));
        }

        [Test]
        public void PlayerPrefab_WiresGrabHookRuntimeAndDefinition()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.Player);

            Assert.That(player, Is.Not.Null);
            Assert.That(
                player.GetComponent<GrabHookController>(),
                Is.Not.Null);
            PlayerToolController tools =
                player.GetComponent<PlayerToolController>();
            Assert.That(tools, Is.Not.Null);
            Assert.That(
                tools.GetDefinition(PlayerInventoryItem.GrabHook),
                Is.Not.Null);
        }

        [Test]
        public void TerrainFilter_AcceptsOnlyTerrainOwnedMeshCollider()
        {
            terrainRoot = new GameObject("Voxel Terrain");
            terrainRoot.AddComponent<MinecraftCaveInfiniteWorld>();
            GameObject terrainSection = CreateMeshColliderChild(
                terrainRoot.transform,
                "Terrain Section",
                Vector3.zero);
            MeshCollider terrainCollider =
                terrainSection.GetComponent<MeshCollider>();

            ordinaryRoot = CreateMeshColliderChild(
                null,
                "Ordinary Mesh",
                Vector3.zero);
            MeshCollider ordinaryCollider =
                ordinaryRoot.GetComponent<MeshCollider>();

            Assert.That(
                GrabHookController.IsTerrainMeshCollider(terrainCollider),
                Is.True);
            Assert.That(
                GrabHookController.IsTerrainMeshCollider(ordinaryCollider),
                Is.False);
        }

        [Test]
        public void RopeVisibility_DetectsMeshBetweenPlayerAndEndpoint()
        {
            blocker = CreateMeshColliderChild(
                null,
                "Blocking Mesh",
                new Vector3(0f, 0f, 2f));
            endpoint = CreateMeshColliderChild(
                null,
                "Endpoint Mesh",
                new Vector3(0f, 0f, 4f));
            Physics.SyncTransforms();

            Assert.That(
                GrabHookController.HasBlockingMesh(
                    Vector3.zero,
                    new Vector3(0f, 0f, 3.5f),
                    null,
                    endpoint.GetComponent<Collider>()),
                Is.True);

            Object.DestroyImmediate(blocker);
            blocker = null;
            Physics.SyncTransforms();
            Assert.That(
                GrabHookController.HasBlockingMesh(
                    Vector3.zero,
                    new Vector3(0f, 0f, 3.5f),
                    null,
                    endpoint.GetComponent<Collider>()),
                Is.False);
        }

        private static GameObject CreateMeshColliderChild(
            Transform parent,
            string objectName,
            Vector3 position)
        {
            GameObject gameObject =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = objectName;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = position;
            Mesh mesh = gameObject.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(gameObject.GetComponent<BoxCollider>());
            MeshCollider meshCollider =
                gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            return gameObject;
        }

        private static void Destroy(GameObject value)
        {
            if (value != null)
                Object.DestroyImmediate(value);
        }
    }
}
