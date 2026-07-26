using System.Collections.Generic;
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
        public void BeginAttraction_AcquiresCartUnderCrosshairInsteadOfNearbyCart()
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
            Rigidbody nearbyBody = CreateCart(
                "Nearby Cart",
                new Vector3(0.3f, 0f, 1.5f));
            Rigidbody focusedBody = CreateCart(
                "Focused Cart",
                new Vector3(0f, 0f, 2.5f));
            Physics.SyncTransforms();

            Assert.That(attractor.BeginAttraction(), Is.True);
            Assert.That(attractor.HeldBody, Is.SameAs(focusedBody));
            Assert.That(attractor.HeldBody, Is.Not.SameAs(nearbyBody));
        }

        private Rigidbody CreateCart(string objectName, Vector3 position)
        {
            GameObject cart = Create(objectName);
            cart.transform.position = position;
            BoxCollider collider = cart.AddComponent<BoxCollider>();
            collider.size = Vector3.one * 0.2f;
            Rigidbody body = cart.AddComponent<Rigidbody>();
            body.useGravity = false;
            cart.AddComponent<PhysicsAttractable>();
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
