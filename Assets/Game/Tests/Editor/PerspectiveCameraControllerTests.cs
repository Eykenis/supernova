using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class PerspectiveCameraControllerTests
    {
        private GameObject player;
        private GameObject obstruction;

        [TearDown]
        public void TearDown()
        {
            if (player != null) Object.DestroyImmediate(player);
            if (obstruction != null) Object.DestroyImmediate(obstruction);
        }

        [Test]
        public void F5Cycle_TogglesFirstAndThirdPerson()
        {
            PerspectiveCameraController controller = CreateController();
            Assert.That(controller.CurrentMode, Is.EqualTo(PlayerViewMode.FirstPerson));
            controller.CycleMode();
            Assert.That(controller.CurrentMode, Is.EqualTo(PlayerViewMode.ThirdPerson));
            controller.CycleMode();
            Assert.That(controller.CurrentMode, Is.EqualTo(PlayerViewMode.FirstPerson));
        }

        [Test]
        public void ThirdPersonYaw_DoesNotFollowPlayerRotation()
        {
            PerspectiveCameraController controller = CreateController();
            controller.SetMode(PlayerViewMode.ThirdPerson, true);
            InvokeLateUpdate(controller);
            Quaternion cameraRotation = controller.ControlledCamera.transform.rotation;

            player.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            InvokeLateUpdate(controller);

            Assert.That(Quaternion.Angle(
                cameraRotation, controller.ControlledCamera.transform.rotation), Is.LessThan(0.001f));
        }

        [Test]
        public void ExternalCamera_IsPushedForwardByTriggerCollider_AndIgnoresPlayerCollider()
        {
            PerspectiveCameraController controller = CreateController();

            GameObject ownedCollider = new GameObject("OwnedCollider");
            ownedCollider.transform.SetParent(player.transform);
            ownedCollider.transform.localPosition = new Vector3(0f, 0f, -0.5f);
            ownedCollider.AddComponent<BoxCollider>();

            obstruction = new GameObject("TriggerObstruction");
            obstruction.transform.position = new Vector3(0f, 0f, -2f);
            BoxCollider wall = obstruction.AddComponent<BoxCollider>();
            wall.size = new Vector3(4f, 4f, 0.4f);
            wall.isTrigger = true;
            Physics.SyncTransforms();

            MethodInfo method = typeof(PerspectiveCameraController).GetMethod(
                "FindAllowedDistance", BindingFlags.Instance | BindingFlags.NonPublic);
            float distance = (float)method.Invoke(
                controller,
                new object[] { Vector3.zero, Vector3.back, 4f });

            Assert.That(distance, Is.GreaterThan(0.5f),
                "The player's own collider must be ignored.");
            Assert.That(distance, Is.LessThan(4f),
                "Even a trigger Collider must push the camera forward.");
        }

        private static void InvokeLateUpdate(PerspectiveCameraController controller)
        {
            typeof(PerspectiveCameraController).GetMethod(
                "LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
        }

        private PerspectiveCameraController CreateController()
        {
            player = new GameObject("Player");
            GameObject head = new GameObject("Head");
            head.transform.SetParent(player.transform);
            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(player.transform);
            Camera camera = cameraObject.AddComponent<Camera>();
            PerspectiveCameraController controller =
                player.AddComponent<PerspectiveCameraController>();
            controller.Bind(player.transform, head.transform, camera, new Renderer[0]);
            controller.SetMode(PlayerViewMode.FirstPerson, true);
            return controller;
        }
    }
}
