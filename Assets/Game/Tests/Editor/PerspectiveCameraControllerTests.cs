using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

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
        public void CursorLock_SuppressesLookInputForStartupWindow()
        {
            PerspectiveCameraController controller = CreateController();
            CursorLockMode previousLockState = Cursor.lockState;
            bool previousVisibility = Cursor.visible;
            MethodInfo setCursorLocked =
                typeof(PerspectiveCameraController).GetMethod(
                    "SetCursorLocked",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo shouldSuppress =
                typeof(PerspectiveCameraController).GetMethod(
                    "ShouldSuppressLookInput",
                    BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(setCursorLocked, Is.Not.Null);
            Assert.That(shouldSuppress, Is.Not.Null);

            try
            {
                setCursorLocked.Invoke(controller, new object[] { true });

                Assert.That(
                    GetPrivateField<int>(
                        controller,
                        "suppressLookInputThroughFrame"),
                    Is.GreaterThanOrEqualTo(Time.frameCount));
                Assert.That(
                    (bool)shouldSuppress.Invoke(controller, null),
                    Is.True);

                SetPrivateField(
                    controller,
                    "suppressLookInputThroughFrame",
                    Time.frameCount - 1);
                Assert.That(
                    (bool)shouldSuppress.Invoke(controller, null),
                    Is.False);
            }
            finally
            {
                Cursor.lockState = previousLockState;
                Cursor.visible = previousVisibility;
            }
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
        public void FirstPerson_CollapsesVisibleHead_AndKeepsHiddenRendererInShadowPass()
        {
            player = new GameObject("Player");
            GameObject head = new GameObject("Head");
            head.transform.SetParent(player.transform);
            head.transform.localScale = new Vector3(1.1f, 0.9f, 1.2f);
            Vector3 expectedHeadScale = head.transform.localScale;

            GameObject hiddenPart = new GameObject("FirstPersonHiddenPart");
            hiddenPart.transform.SetParent(player.transform);
            MeshRenderer hiddenRenderer = hiddenPart.AddComponent<MeshRenderer>();

            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(player.transform);
            Camera camera = cameraObject.AddComponent<Camera>();
            PerspectiveCameraController controller =
                player.AddComponent<PerspectiveCameraController>();
            controller.Bind(
                player.transform,
                head.transform,
                camera,
                new Renderer[] { hiddenRenderer });

            controller.SetMode(PlayerViewMode.FirstPerson, true);

            Assert.That(head.transform.localScale, Is.EqualTo(expectedHeadScale * 0.001f));
            Assert.That(hiddenRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.ShadowsOnly));
            Assert.That(hiddenRenderer.receiveShadows, Is.False);

            controller.SetMode(PlayerViewMode.ThirdPerson, true);

            Assert.That(head.transform.localScale, Is.EqualTo(expectedHeadScale));
            Assert.That(hiddenRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
            Assert.That(hiddenRenderer.receiveShadows, Is.True);
        }

        [Test]
        public void BoundPlayerSkinnedMeshes_KeepUpdatingOutsideImportedBounds()
        {
            player = new GameObject("Player");
            GameObject clothing = new GameObject("Dynamic Clothing");
            clothing.transform.SetParent(player.transform);
            SkinnedMeshRenderer clothingRenderer =
                clothing.AddComponent<SkinnedMeshRenderer>();

            GameObject head = new GameObject("Head");
            head.transform.SetParent(player.transform);
            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(player.transform);
            Camera camera = cameraObject.AddComponent<Camera>();
            PerspectiveCameraController controller =
                player.AddComponent<PerspectiveCameraController>();

            // Awake can already configure renderers on the controller root.
            // Reset the flag so this assertion specifically covers Bind.
            clothingRenderer.updateWhenOffscreen = false;
            controller.Bind(
                player.transform,
                head.transform,
                camera,
                new Renderer[0]);

            Assert.That(clothingRenderer.updateWhenOffscreen, Is.True,
                "A player garment must not be culled from its imported bounds "
                + "while cloth or first-person posing moves its vertices.");
        }

        [Test]
        public void MenuTransition_CanEnterFirstPersonVisibilityBeforeMenuEnds()
        {
            player = new GameObject("Player");
            GameObject head = new GameObject("Head");
            head.transform.SetParent(player.transform);
            Vector3 expectedHeadScale = head.transform.localScale;

            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(player.transform);
            Camera camera = cameraObject.AddComponent<Camera>();
            PerspectiveCameraController controller =
                player.AddComponent<PerspectiveCameraController>();
            controller.Bind(
                player.transform,
                head.transform,
                camera,
                new Renderer[0]);
            controller.SetMode(PlayerViewMode.FirstPerson, true);

            controller.SetMenuPresentationActive(true);
            Assert.That(controller.IsMenuPresentationActive, Is.True);
            Assert.That(head.transform.localScale, Is.EqualTo(expectedHeadScale));

            controller.SetMenuFirstPersonVisibilityActive(true);
            Assert.That(controller.IsMenuPresentationActive, Is.True);
            Assert.That(
                head.transform.localScale,
                Is.EqualTo(expectedHeadScale * 0.001f));
        }

        [Test]
        public void PlayerDeath_SwitchesCameraToThirdPerson()
        {
            PerspectiveCameraController controller = CreateController();
            VoxelPlayerController playerController =
                player.AddComponent<VoxelPlayerController>();

            var lethalDamage = new DamageInfo(
                1000f,
                null,
                Vector3.zero,
                Vector3.forward);
            Assert.That(playerController.ReceiveDamage(lethalDamage), Is.True);

            Assert.That(
                controller.CurrentMode,
                Is.EqualTo(PlayerViewMode.ThirdPerson));
        }

        [Test]
        public void ExternalCamera_IsBlockedOnlyByStaticSceneMesh()
        {
            PerspectiveCameraController controller = CreateController();

            GameObject ownedCollider = new GameObject("OwnedCollider");
            ownedCollider.transform.SetParent(player.transform);
            ownedCollider.transform.localPosition = new Vector3(0f, 0f, -0.5f);
            ownedCollider.AddComponent<BoxCollider>();

            obstruction = new GameObject("Obstructions");
            CreateMeshObstruction("DynamicMesh", -1.2f, true, false);
            CreateMeshObstruction("TriggerMesh", -1.8f, false, true);

            GameObject rigidCollider = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rigidCollider.name = "OtherCollider";
            rigidCollider.transform.SetParent(obstruction.transform);
            rigidCollider.transform.position = new Vector3(0f, 0f, -2.4f);
            rigidCollider.transform.localScale = new Vector3(4f, 4f, 0.4f);

            CreateMeshObstruction("SceneMesh", -3.2f, false, false);
            Physics.SyncTransforms();

            MethodInfo method = typeof(PerspectiveCameraController).GetMethod(
                "FindAllowedDistance", BindingFlags.Instance | BindingFlags.NonPublic);
            float distance = (float)method.Invoke(
                controller,
                new object[] { Vector3.zero, Vector3.back, 4f });

            Assert.That(distance, Is.GreaterThan(2.5f),
                "Player, trigger, non-mesh, and Rigidbody colliders must be ignored.");
            Assert.That(distance, Is.LessThan(4f),
                "A static scene MeshCollider must push the camera forward.");
        }

        [Test]
        public void CrouchArmPitch_RotatesArmTowardPlayerForward()
        {
            player = new GameObject("Player");
            GameObject upperArm = new GameObject("UpperArm");
            upperArm.transform.SetParent(player.transform);
            GameObject hand = new GameObject("Hand");
            hand.transform.SetParent(upperArm.transform);
            hand.transform.localPosition = Vector3.down;

            MethodInfo method = typeof(PerspectiveCameraController).GetMethod(
                "ApplyArmForwardRotation",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(
                null,
                new object[] { upperArm.transform, player.transform.right, 12f });

            Assert.That(hand.transform.position.z, Is.GreaterThan(0f),
                "The crouch correction must move a hanging arm toward player forward.");
        }

        [Test]
        public void FirstPersonPitch_RotatesArmChainByExactCameraPitch()
        {
            player = new GameObject("Player");
            GameObject spine = new GameObject("Spine");
            spine.transform.SetParent(player.transform);
            GameObject chest = new GameObject("Chest");
            chest.transform.SetParent(spine.transform);
            GameObject upperChest = new GameObject("UpperChest");
            upperChest.transform.SetParent(chest.transform);
            GameObject upperArm = new GameObject("UpperArm");
            upperArm.transform.SetParent(upperChest.transform);
            GameObject leftHand = new GameObject("LeftHand");
            leftHand.transform.SetParent(upperArm.transform);
            GameObject twoHandedMount = new GameObject("Two Handed Model Mount");
            twoHandedMount.transform.SetParent(leftHand.transform);

            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(player.transform);
            Camera camera = cameraObject.AddComponent<Camera>();
            PerspectiveCameraController controller =
                player.AddComponent<PerspectiveCameraController>();
            controller.Bind(player.transform, null, camera, new Renderer[0]);
            controller.SetMode(PlayerViewMode.FirstPerson, true);

            SetPrivateField(controller, "animatedSpine", spine.transform);
            SetPrivateField(controller, "animatedChest", chest.transform);
            SetPrivateField(controller, "animatedUpperChest", upperChest.transform);
            SetPrivateField(controller, "animatedLeftUpperArm", upperArm.transform);

            const float pitch = 84f;
            controller.SetLookPitch(pitch);
            typeof(PerspectiveCameraController).GetMethod(
                    "UpdateUpperBodyPose",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);

            Vector3 expectedForward = Quaternion.AngleAxis(
                pitch,
                player.transform.right) * player.transform.forward;
            Assert.That(
                Vector3.Angle(upperArm.transform.forward, expectedForward),
                Is.LessThan(0.01f),
                "First-person arms must receive the full camera pitch, including the "
                + "rotation not inherited through the torso chain.");
            Assert.That(
                GetPrivateField<float>(controller, "smoothedUpperBodyPitch"),
                Is.EqualTo(pitch).Within(0.001f),
                "First-person bone pitch must not lag behind the camera.");
            Assert.That(
                Vector3.Angle(twoHandedMount.transform.forward, expectedForward),
                Is.LessThan(0.01f),
                "A left-hand two-handed mount must inherit the arm's exact camera pitch.");
        }

        private static void InvokeLateUpdate(PerspectiveCameraController controller)
        {
            typeof(PerspectiveCameraController).GetMethod(
                "LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
        }

        private static void SetPrivateField<T>(object target, string name, T value)
        {
            typeof(PerspectiveCameraController).GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            return (T)typeof(PerspectiveCameraController).GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(target);
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

        private void CreateMeshObstruction(
            string name,
            float z,
            bool addRigidbody,
            bool isTrigger)
        {
            GameObject meshObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            meshObject.name = name;
            meshObject.transform.SetParent(obstruction.transform);
            meshObject.transform.position = new Vector3(0f, 0f, z);
            meshObject.transform.localScale = new Vector3(4f, 4f, 0.4f);

            Mesh mesh = meshObject.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(meshObject.GetComponent<BoxCollider>());
            MeshCollider meshCollider = meshObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            meshCollider.convex = addRigidbody || isTrigger;
            meshCollider.isTrigger = isTrigger;
            if (addRigidbody)
            {
                Rigidbody body = meshObject.AddComponent<Rigidbody>();
                body.isKinematic = true;
            }
        }
    }
}
