using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.MinecraftCaves.Creatures;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class FirstPersonCartAttractorTests
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
        public void BeginHandleTow_RequiresCartToolToBeEnabled()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            Rigidbody cart = CreateBody(
                "Cart",
                new Vector3(0f, 0f, 1.5f));
            cart.gameObject.AddComponent<CartHandle>().Configure(cart);
            Physics.SyncTransforms();

            Assert.That(attractor.CartTowEnabled, Is.False);
            Assert.That(attractor.BeginHandleTow(), Is.False);
            Assert.That(attractor.IsTowingCart, Is.False);
        }

        [Test]
        public void BeginHandleTow_AcquiresOnlyCartHandleWithinShortRange()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            camera.transform.localPosition = Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;

            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);

            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            attractor.SetCartTowEnabled(true);
            Rigidbody nearbyBody = CreateBody(
                "Nearby Body",
                new Vector3(0.3f, 0f, 1.25f));
            Rigidbody focusedBody = CreateBody(
                "Focused Body",
                new Vector3(0f, 0f, 1.5f));
            focusedBody.gameObject.AddComponent<CartHandle>().Configure(focusedBody);
            focusedBody.mass = 1000f;
            Physics.SyncTransforms();

            Assert.That(attractor.CartHandleAcquisitionDistance, Is.EqualTo(2f));
            Assert.That(attractor.BeginHandleTow(), Is.True);
            Assert.That(attractor.HeldBody, Is.SameAs(focusedBody));
            Assert.That(attractor.HeldBody, Is.Not.SameAs(nearbyBody));
        }

        [Test]
        public void BeginHandleTow_AllowsOwnCartColliderBeforeHandle()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            attractor.SetCartTowEnabled(true);

            GameObject cart = Create("Compound Cart");
            Rigidbody body = cart.AddComponent<Rigidbody>();
            body.useGravity = false;
            BoxCollider cartCollider = cart.AddComponent<BoxCollider>();
            cartCollider.center = Vector3.forward;
            cartCollider.size = Vector3.one * 0.4f;
            GameObject handleObject = Create("Tow Handle");
            handleObject.transform.SetParent(cart.transform);
            handleObject.transform.localPosition = Vector3.forward * 1.5f;
            handleObject.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;
            handleObject.AddComponent<CartHandle>().Configure(body);
            Physics.SyncTransforms();

            Assert.That(attractor.BeginHandleTow(), Is.True);
            Assert.That(attractor.IsTowingCart, Is.True);
            Assert.That(attractor.HeldBody, Is.SameAs(body));
        }

        [Test]
        public void BeginHandleTow_IsBlockedByUnrelatedColliderBeforeHandle()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            attractor.SetCartTowEnabled(true);

            GameObject cart = Create("Cart");
            Rigidbody body = cart.AddComponent<Rigidbody>();
            body.useGravity = false;
            GameObject handleObject = Create("Tow Handle");
            handleObject.transform.SetParent(cart.transform);
            handleObject.transform.localPosition = Vector3.forward * 1.5f;
            handleObject.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;
            handleObject.AddComponent<CartHandle>().Configure(body);
            GameObject blocker = Create("Unrelated Blocker");
            blocker.transform.position = Vector3.forward * 0.75f;
            blocker.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;
            Physics.SyncTransforms();

            Assert.That(attractor.BeginHandleTow(), Is.False);
            Assert.That(attractor.IsTowingCart, Is.False);
        }

        [Test]
        public void BeginHandleTow_RejectsCartHandleBeyondShortRange()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            attractor.SetCartTowEnabled(true);
            Rigidbody cart = CreateBody("Distant Cart", new Vector3(0f, 0f, 2.5f));
            cart.gameObject.AddComponent<CartHandle>().Configure(cart);
            Physics.SyncTransforms();

            Assert.That(attractor.BeginHandleTow(), Is.False);
            Assert.That(attractor.HeldBody, Is.Null);
        }

        [Test]
        public void CartTowTarget_PreservesCapturedWorldOffsetWhenPlayerTurns()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            attractor.SetCartTowEnabled(true);
            Rigidbody cart = CreateBody("Cart", new Vector3(0f, 0f, 1.5f));
            cart.gameObject.AddComponent<CartHandle>().Configure(cart);
            Physics.SyncTransforms();

            Vector3 originalTarget = cart.position;
            Assert.That(attractor.BeginHandleTow(), Is.True);
            Vector3 translation = new Vector3(3f, 1f, -2f);
            player.transform.position += translation;
            player.transform.rotation = Quaternion.Euler(0f, 135f, 0f);

            Vector3 desired = InvokePrivate<Vector3>(
                attractor,
                "CalculateDesiredHoldPosition");

            Assert.That(desired, Is.EqualTo(originalTarget + translation));
        }

        [Test]
        public void CartTowRotation_PointsActualHandleDirectionTowardPlayer()
        {
            GameObject player = Create("Player");
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();

            Quaternion targetRotation = InvokePrivate<Quaternion>(
                attractor,
                "CalculateCartTowTargetRotation",
                Quaternion.identity,
                Vector3.back,
                Vector3.right);
            Vector3 handleDirection = targetRotation * Vector3.back;

            Assert.That(
                Vector3.Angle(handleDirection, Vector3.right),
                Is.LessThan(0.001f));
        }



        [Test]
        public void AttractionForce_IsCappedToConfiguredNewtonStrength()
        {
            GameObject player = Create("Player");
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            SetPrivateField(attractor, "attractionForce", 125f);
            SetPrivateField(attractor, "forceDamping", 0f);

            Vector3 force = InvokePrivate<Vector3>(
                attractor,
                "CalculateAttractionForce",
                Vector3.up * 20f,
                Vector3.zero);

            Assert.That(force.magnitude, Is.EqualTo(125f).Within(0.001f));
            Assert.That(force.normalized, Is.EqualTo(Vector3.up));
        }

        [Test]
        public void AttractionModule_AddsFourHundredNewtons()
        {
            GameObject player = Create("Player");
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            SetPrivateField(attractor, "attractionForce", 800f);

            attractor.SetAttractionForceUpgrade(
                FirstPersonCartAttractor.AttractionModuleUpgradeForce);

            Assert.That(attractor.BaseAttractionForce, Is.EqualTo(800f));
            Assert.That(attractor.AttractionForce, Is.EqualTo(1200f));
        }

        [Test]
        public void OrientationHold_DampsUnrequestedRotationOnEveryAxis()
        {
            GameObject player = Create("Player");
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            SetPrivateField(attractor, "heldTargetRotation", Quaternion.identity);
            SetPrivateField(attractor, "hasHeldTargetRotation", true);
            SetPrivateField(attractor, "orientationSpring", 55f);
            SetPrivateField(attractor, "orientationDamping", 10f);
            SetPrivateField(attractor, "maximumOrientationTorque", 1000f);

            Vector3 angularVelocity = new Vector3(1f, -2f, 3f);
            Vector3 torque = InvokePrivate<Vector3>(
                attractor,
                "CalculateOrientationTorque",
                Quaternion.identity,
                angularVelocity);

            Assert.That(torque, Is.EqualTo(-angularVelocity * 10f));
        }

        [Test]
        public void MagnetAttraction_AcquiresOrdinaryRigidbody()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            Rigidbody body = CreateBody("Ordinary Body", new Vector3(0f, 0f, 2f));
            Physics.SyncTransforms();

            Assert.That(attractor.BeginAttraction(), Is.True);
            Assert.That(attractor.HeldBody, Is.SameAs(body));
        }

        [Test]
        public void MagnetAttraction_CatchesMonsterAndStopsItsNavigation()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();

            Rigidbody body = CreateBody("Monster", new Vector3(0f, 0f, 2f));
            CreatureBehaviorAgent monster =
                body.gameObject.AddComponent<CreatureBehaviorAgent>();
            CreaturePhysicsMotor motor =
                body.gameObject.GetComponent<CreaturePhysicsMotor>();
            motor.Submit(new CreatureMovementCommand(
                1,
                Vector3.forward,
                Vector3.up,
                0));
            FieldInfo pathField = typeof(CreatureBehaviorAgent).GetField(
                "path",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(pathField, Is.Not.Null);
            var path = (List<Vector3Int>)pathField.GetValue(monster);
            path.Add(Vector3Int.zero);
            path.Add(Vector3Int.forward);
            Physics.SyncTransforms();

            Assert.That(attractor.BeginAttraction(), Is.True);
            Assert.That(attractor.HeldBody, Is.SameAs(body));
            Assert.That(monster.IsCaught, Is.True);
            Assert.That(
                monster.CurrentState,
                Is.EqualTo(CreatureBehaviorState.Caught));
            Assert.That(monster.CurrentPath, Is.Empty);
            Assert.That(motor.HasCommand, Is.False);

            attractor.TickAttraction();

            Assert.That(
                monster.CurrentState,
                Is.EqualTo(CreatureBehaviorState.Caught));

            attractor.EndAttraction();

            Assert.That(monster.IsCaught, Is.False);
            Assert.That(
                monster.CurrentState,
                Is.EqualTo(CreatureBehaviorState.Idle));
        }

        [Test]
        public void MagnetAttraction_AimAssistAcquiresSparseOffAxisBody()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            Rigidbody body = CreateBody(
                "Sparse Magnet Target",
                new Vector3(0f, 0f, 2f));
            body.GetComponent<BoxCollider>().center =
                new Vector3(0.25f, 0f, 0f);
            Physics.SyncTransforms();

            Assert.That(attractor.BeginAttraction(), Is.True);
            Assert.That(attractor.HeldBody, Is.SameAs(body));
        }

        [Test]
        public void MagnetAttraction_AimAssistDoesNotAcquireThroughWall()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            Rigidbody body = CreateBody(
                "Occluded Magnet Target",
                new Vector3(0f, 0f, 2f));
            body.GetComponent<BoxCollider>().center =
                new Vector3(0.25f, 0f, 0f);
            GameObject wall = Create("Wall");
            wall.transform.position = new Vector3(0.125f, 0f, 1f);
            BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
            wallCollider.size = new Vector3(1f, 1f, 0.1f);
            Physics.SyncTransforms();

            Assert.That(attractor.BeginAttraction(), Is.True);
            Assert.That(attractor.HeldBody, Is.Null);
        }



        [Test]
        public void MagnetAttraction_RejectsCartBody()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();

            Rigidbody cart = CreateBody("Cart", new Vector3(0f, 0f, 2f));
            cart.gameObject.AddComponent<CartHandle>().Configure(cart);
            Physics.SyncTransforms();

            Assert.That(attractor.BeginAttraction(), Is.True);
            Assert.That(attractor.HeldBody, Is.Null);
        }

        [Test]
        public void FailedCartTow_DoesNotInterruptMagnetAction()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            attractor.SetCartTowEnabled(true);
            Rigidbody body = CreateBody(
                "Magnet Target",
                new Vector3(0f, 0f, 1.5f));
            Physics.SyncTransforms();

            Assert.That(attractor.BeginAttraction(), Is.True);
            Assert.That(attractor.HeldBody, Is.SameAs(body));
            Assert.That(attractor.BeginHandleTow(), Is.False);
            Assert.That(attractor.IsActionActive, Is.True);
            Assert.That(attractor.HeldBody, Is.SameAs(body));
        }

        [Test]
        public void EndingMagnetAction_DoesNotReleaseCartTow()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            attractor.SetCartTowEnabled(true);
            Rigidbody cart = CreateBody("Cart", new Vector3(0f, 0f, 1.5f));
            cart.gameObject.AddComponent<CartHandle>().Configure(cart);
            Physics.SyncTransforms();

            Assert.That(attractor.BeginHandleTow(), Is.True);
            attractor.EndAttraction();

            Assert.That(attractor.IsTowingCart, Is.True);
            Assert.That(attractor.HeldBody, Is.SameAs(cart));
        }

        [Test]
        public void MagnetHeightDrag_MovesPhysicalHoldPointWithoutMovingBodyDirectly()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            Rigidbody body = CreateBody(
                "Magnet Target",
                new Vector3(0f, 0f, 2f));
            Physics.SyncTransforms();

            Assert.That(attractor.BeginAttraction(), Is.True);
            Vector3 bodyPosition = body.position;
            Vector3 before = InvokePrivate<Vector3>(
                attractor,
                "CalculateDesiredHoldPosition");
            attractor.AdjustMagnetHeight(2f);
            Vector3 after = InvokePrivate<Vector3>(
                attractor,
                "CalculateDesiredHoldPosition");

            Assert.That(after.y - before.y, Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(body.position, Is.EqualTo(bodyPosition));
        }

        [Test]
        public void MagnetLiftForce_DecreasesWithActualLiftedHeight()
        {
            GameObject player = Create("Player");
            Camera camera = Create("View Camera").AddComponent<Camera>();
            camera.transform.SetParent(player.transform);
            PerspectiveCameraController perspective =
                player.AddComponent<PerspectiveCameraController>();
            perspective.Bind(player.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            CreateBody("Magnet Target", new Vector3(0f, 0f, 2f));
            Physics.SyncTransforms();

            Assert.That(attractor.BeginAttraction(), Is.True);
            float forceAtPickup = InvokePrivate<float>(
                attractor,
                "CalculateMaximumLiftForce",
                0f);
            float forceAtThreeMetres = InvokePrivate<float>(
                attractor,
                "CalculateMaximumLiftForce",
                3f);

            Assert.That(forceAtPickup, Is.EqualTo(300f).Within(0.001f));
            Assert.That(
                forceAtThreeMetres,
                Is.EqualTo(300f / 2.8f).Within(0.001f));
            Assert.That(forceAtThreeMetres, Is.LessThan(forceAtPickup));
        }

        [Test]
        public void MagnetLiftLimit_ClampsOnlyUpwardForce()
        {
            GameObject player = Create("Player");
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            SetPrivateField(attractor, "baseMaximumLiftForce", 100f);
            SetPrivateField(attractor, "liftForceFalloffPerMeter", 1f);
            SetPrivateField(attractor, "magnetPickupHeight", 0f);

            Vector3 upward = InvokePrivate<Vector3>(
                attractor,
                "LimitMagnetLiftForce",
                new Vector3(20f, 200f, 30f),
                1f);
            Vector3 downward = InvokePrivate<Vector3>(
                attractor,
                "LimitMagnetLiftForce",
                new Vector3(20f, -200f, 30f),
                1f);

            Assert.That(upward, Is.EqualTo(new Vector3(20f, 50f, 30f)));
            Assert.That(downward, Is.EqualTo(new Vector3(20f, -200f, 30f)));
        }

        [Test]
        public void SaturatedMagnetLift_DampingRemovesUpwardMotionEnergy()
        {
            GameObject player = Create("Player");
            FirstPersonCartAttractor attractor =
                player.AddComponent<FirstPersonCartAttractor>();
            SetPrivateField(attractor, "baseMaximumLiftForce", 100f);
            SetPrivateField(attractor, "liftForceFalloffPerMeter", 0f);
            SetPrivateField(attractor, "positionSpring", 300f);
            SetPrivateField(attractor, "forceDamping", 90f);
            SetPrivateField(attractor, "attractionForce", 1000f);
            SetPrivateField(attractor, "maximumAttractionAcceleration", 1000f);
            SetPrivateField(attractor, "magnetPickupHeight", 0f);

            Vector3 rising = InvokePrivate<Vector3>(
                attractor,
                "CalculateMagnetAttractionForce",
                Vector3.up * 2f,
                Vector3.down,
                0f,
                1f);
            Vector3 falling = InvokePrivate<Vector3>(
                attractor,
                "CalculateMagnetAttractionForce",
                Vector3.up * 2f,
                Vector3.up,
                0f,
                1f);

            Assert.That(rising.y, Is.EqualTo(10f).Within(0.001f));
            Assert.That(falling.y, Is.EqualTo(100f).Within(0.001f));
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

        private static T InvokePrivate<T>(
            object target,
            string name,
            params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (T)method.Invoke(target, arguments);
        }

        private Rigidbody CreateBody(string objectName, Vector3 position)
        {
            GameObject cart = Create(objectName);
            cart.transform.position = position;
            BoxCollider collider = cart.AddComponent<BoxCollider>();
            collider.size = Vector3.one * 0.2f;
            Rigidbody body = cart.AddComponent<Rigidbody>();
            body.useGravity = false;
            return body;
        }

        private GameObject Create(string objectName)
        {
            var gameObject = new GameObject(objectName);
            objects.Add(gameObject);
            return gameObject;
        }
    }
}
