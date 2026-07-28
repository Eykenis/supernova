using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
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
