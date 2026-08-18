using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.PortalExample;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools.Utils;

namespace Supernova.Tests.Editor.PortalExample
{
    public sealed class PortalExampleTests
    {
        [Test]
        public void Mapping_MapsSourceCenterToDestinationCenter()
        {
            GameObject source = new GameObject("Source");
            GameObject destination = new GameObject("Destination");
            source.transform.SetPositionAndRotation(
                new Vector3(2f, 1f, -4f),
                Quaternion.Euler(0f, 35f, 0f));
            destination.transform.SetPositionAndRotation(
                new Vector3(-7f, 3f, 6f),
                Quaternion.Euler(0f, -80f, 0f));

            Vector3 mapped = PortalExampleSpace.BuildMapping(
                    source.transform,
                    destination.transform)
                .MultiplyPoint3x4(source.transform.position);

            Assert.That(mapped, Is.EqualTo(destination.transform.position)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(destination);
        }

        [Test]
        public void Mapping_PreservesVelocityMagnitude()
        {
            GameObject source = new GameObject("Source");
            GameObject destination = new GameObject("Destination");
            source.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
            destination.transform.rotation = Quaternion.Euler(0f, 110f, 0f);
            Vector3 velocity = new Vector3(3f, -5f, 8f);

            Vector3 mapped = PortalExampleSpace.BuildMapping(
                    source.transform,
                    destination.transform)
                .MultiplyVector(velocity);

            Assert.That(
                mapped.magnitude,
                Is.EqualTo(velocity.magnitude).Within(0.0001f));
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(destination);
        }

        [Test]
        public void Mapping_IgnoresPortalScaleAtLargeWorldCoordinates()
        {
            GameObject source = new GameObject("Scaled Cell Portal");
            GameObject destination = new GameObject("Checkpoint Portal");
            source.transform.SetPositionAndRotation(
                new Vector3(1329.66f, 21.55f, 0f),
                Quaternion.Euler(0f, 90f, 0f));
            source.transform.localScale = Vector3.one * 0.65f;
            destination.transform.SetPositionAndRotation(
                new Vector3(0.2f, 22.88f, 0.22f),
                Quaternion.Euler(0f, 95.4f, 0f));
            Vector3 sourceOffset = source.transform.forward * 0.9f
                - source.transform.up * 1.35f;
            Vector3 point = source.transform.position + sourceOffset;

            Vector3 mapped = PortalExampleSpace.BuildMapping(
                    source.transform,
                    destination.transform)
                .MultiplyPoint3x4(point);

            Assert.That(
                Vector3.Distance(mapped, destination.transform.position),
                Is.EqualTo(sourceOffset.magnitude).Within(0.001f));
            Assert.That(
                Vector3.Distance(mapped, destination.transform.position),
                Is.LessThan(2f));
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(destination);
        }

        [Test]
        public void Traveller_TeleportLeavesDestinationFrontAndPreservesSpeed()
        {
            GameObject sourceObject = new GameObject("Source");
            GameObject destinationObject = new GameObject("Destination");
            GameObject travellerObject = new GameObject("Traveller");
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            destinationObject.transform.SetPositionAndRotation(
                new Vector3(9f, 2f, 7f),
                Quaternion.Euler(0f, -90f, 0f));
            Rigidbody body = travellerObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.velocity = new Vector3(0f, 0f, 6f);
            PortalExampleTraveller traveller =
                travellerObject.AddComponent<PortalExampleTraveller>();
            int teleportEventCount = 0;
            PortalExampleGate eventSource = null;
            PortalExampleGate eventDestination = null;
            traveller.Teleported += (observedSource, observedDestination) =>
            {
                teleportEventCount++;
                eventSource = observedSource;
                eventDestination = observedDestination;
            };
            travellerObject.transform.position =
                sourceObject.transform.position - sourceObject.transform.forward * 0.2f;

            bool teleported = traveller.Teleport(source, destination);
            float destinationSide = Vector3.Dot(
                travellerObject.transform.position - destinationObject.transform.position,
                destinationObject.transform.forward);

            Assert.That(teleported, Is.True);
            Assert.That(teleportEventCount, Is.EqualTo(1));
            Assert.That(eventSource, Is.SameAs(source));
            Assert.That(eventDestination, Is.SameAs(destination));
            Assert.That(destinationSide, Is.GreaterThan(0f));
            Assert.That(body.velocity.magnitude, Is.EqualTo(6f).Within(0.0001f));
            Assert.That(traveller.Teleport(destination, source), Is.False);
            Assert.That(traveller.CanTeleport, Is.False);
            Assert.That(traveller.TeleportCooldown, Is.EqualTo(0.75f));
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(travellerObject);
        }

        [Test]
        public void Traveller_WallToFloorTeleportStartsFromMappedCameraPose()
        {
            GameObject sourceObject = new GameObject("Wall Portal");
            GameObject destinationObject = new GameObject("Floor Portal");
            GameObject travellerObject = new GameObject("Traveller");
            GameObject cameraObject = new GameObject("Camera");
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            destinationObject.transform.SetPositionAndRotation(
                new Vector3(4f, 2f, -3f),
                Quaternion.LookRotation(Vector3.up, Vector3.forward));

            travellerObject.AddComponent<CharacterController>();
            cameraObject.transform.SetParent(travellerObject.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.5f, 0.1f);
            Camera camera = cameraObject.AddComponent<Camera>();
            PerspectiveCameraController perspective =
                cameraObject.AddComponent<PerspectiveCameraController>();
            perspective.Bind(
                travellerObject.transform,
                null,
                camera,
                new Renderer[0]);
            PortalExampleTraveller traveller =
                travellerObject.AddComponent<PortalExampleTraveller>();
            travellerObject.transform.position = Vector3.back * 0.2f;

            Matrix4x4 mapping = PortalExampleSpace.BuildMapping(
                sourceObject.transform,
                destinationObject.transform);
            Vector3 expectedCameraPosition = mapping.MultiplyPoint3x4(
                    cameraObject.transform.position)
                + destinationObject.transform.forward * 0.09f;
            Quaternion expectedCameraRotation = PortalExampleSpace.MapRotation(
                mapping,
                cameraObject.transform.rotation);

            Assert.That(traveller.Teleport(source, destination), Is.True);

            Assert.That(
                Vector3.Dot(travellerObject.transform.up, Vector3.up),
                Is.GreaterThan(0.999f));
            Assert.That(
                cameraObject.transform.position,
                Is.EqualTo(expectedCameraPosition)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(
                Quaternion.Angle(
                    cameraObject.transform.rotation,
                    expectedCameraRotation),
                Is.LessThan(0.001f));

            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(travellerObject);
        }

        [Test]
        public void RigidbodyTraveller_DoesNotMoveUnrelatedMainCamera()
        {
            GameObject sourceObject = new GameObject("Source");
            GameObject destinationObject = new GameObject("Destination");
            GameObject travellerObject = new GameObject("Magnet Object");
            GameObject cameraObject = new GameObject("Unrelated Player Camera");
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            destinationObject.transform.position = new Vector3(100f, 5f, -20f);

            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(4f, 3f, 2f),
                Quaternion.Euler(10f, 20f, 0f));
            Camera camera = cameraObject.AddComponent<Camera>();
            PerspectiveCameraController perspective =
                cameraObject.AddComponent<PerspectiveCameraController>();
            perspective.Bind(
                cameraObject.transform,
                null,
                camera,
                new Renderer[0]);

            Rigidbody body = travellerObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            PortalExampleTraveller traveller =
                travellerObject.AddComponent<PortalExampleTraveller>();
            Vector3 cameraPosition = cameraObject.transform.position;
            Quaternion cameraRotation = cameraObject.transform.rotation;

            Assert.That(traveller.Teleport(source, destination), Is.True);

            Assert.That(
                cameraObject.transform.position,
                Is.EqualTo(cameraPosition)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(
                Quaternion.Angle(cameraObject.transform.rotation, cameraRotation),
                Is.LessThan(0.001f));

            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(travellerObject);
            Object.DestroyImmediate(cameraObject);
        }

        [Test]
        public void Traveller_TeleportKeepsBuiltInWorldGravity()
        {
            GameObject sourceObject = new GameObject("Horizontal Source");
            GameObject destinationObject = new GameObject("Vertical Destination");
            GameObject travellerObject = new GameObject("World Gravity Traveller");
            sourceObject.transform.rotation = Quaternion.LookRotation(
                Vector3.up,
                Vector3.forward);
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            Rigidbody body = travellerObject.AddComponent<Rigidbody>();
            body.useGravity = true;
            PortalExampleTraveller traveller =
                travellerObject.AddComponent<PortalExampleTraveller>();

            Assert.That(traveller.Teleport(source, destination), Is.True);

            Assert.That(body.useGravity, Is.True);
            Assert.That(
                travellerObject.GetComponents<MonoBehaviour>().Length,
                Is.EqualTo(1));
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(travellerObject);
        }

        [Test]
        public void ChildTrigger_RelaysAndAutoRegistersRigidbodyTraveller()
        {
            GameObject sourceObject = new GameObject("Source");
            GameObject triggerObject = new GameObject("Traversal Trigger");
            triggerObject.transform.SetParent(sourceObject.transform, false);
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            InvokePrivate(source, "EnsureTriggerRelays");

            GameObject destinationObject = new GameObject("Destination");
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            destinationObject.transform.position = new Vector3(8f, 0f, 0f);
            Link(source, destination);

            GameObject bombObject = new GameObject("Unregistered Bomb");
            Rigidbody body = bombObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.velocity = Vector3.back * 5f;
            SphereCollider bombCollider =
                bombObject.AddComponent<SphereCollider>();
            bombObject.transform.position = Vector3.forward * 0.2f;
            Physics.SyncTransforms();

            PortalExampleTriggerRelay relay =
                triggerObject.GetComponent<PortalExampleTriggerRelay>();
            Assert.That(relay, Is.Not.Null);
            Assert.That(relay.Gate, Is.SameAs(source));

            InvokePrivate(relay, "OnTriggerEnter", bombCollider);
            Assert.That(
                bombObject.GetComponent<PortalExampleTraveller>(),
                Is.Not.Null);

            bombObject.transform.position = Vector3.back * 0.1f;
            Physics.SyncTransforms();
            InvokePrivate(relay, "OnTriggerStay", bombCollider);
            float destinationSide = Vector3.Dot(
                bombObject.transform.position - destinationObject.transform.position,
                destinationObject.transform.forward);

            Assert.That(destinationSide, Is.GreaterThan(0f));
            Assert.That(body.velocity.magnitude, Is.EqualTo(5f).Within(0.0001f));
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(bombObject);
        }

        [Test]
        public void RestrictedReturnGate_OnlyTeleportsConfiguredPlayer()
        {
            GameObject returnGateObject = new GameObject("Landing Cell Gate");
            GameObject triggerObject = new GameObject("Traversal Trigger");
            triggerObject.transform.SetParent(returnGateObject.transform, false);
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            PortalExampleGate returnGate =
                returnGateObject.AddComponent<PortalExampleGate>();
            InvokePrivate(returnGate, "EnsureTriggerRelays");

            GameObject destinationObject = new GameObject("Cave Gate");
            destinationObject.transform.position = new Vector3(8f, 0f, 0f);
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            Link(returnGate, destination);

            GameObject playerObject = new GameObject("Player");
            Rigidbody playerBody = playerObject.AddComponent<Rigidbody>();
            playerBody.useGravity = false;
            playerBody.velocity = Vector3.back;
            SphereCollider playerCollider =
                playerObject.AddComponent<SphereCollider>();
            PortalExampleTraveller player =
                playerObject.AddComponent<PortalExampleTraveller>();

            GameObject oreObject = new GameObject("Ore Feature");
            Rigidbody oreBody = oreObject.AddComponent<Rigidbody>();
            oreBody.useGravity = false;
            oreBody.velocity = Vector3.back;
            SphereCollider oreCollider = oreObject.AddComponent<SphereCollider>();

            returnGate.RestrictTraversalTo(player);
            PortalExampleTriggerRelay relay =
                triggerObject.GetComponent<PortalExampleTriggerRelay>();

            oreObject.transform.position = Vector3.forward * 0.2f;
            Physics.SyncTransforms();
            InvokePrivate(relay, "OnTriggerEnter", oreCollider);
            oreObject.transform.position = Vector3.back * 0.1f;
            Physics.SyncTransforms();
            InvokePrivate(relay, "OnTriggerStay", oreCollider);

            Assert.That(
                Vector3.Distance(
                    oreObject.transform.position,
                    destinationObject.transform.position),
                Is.GreaterThan(1f),
                "A non-player Rigidbody must not return through the landing gate.");

            playerObject.transform.position = Vector3.forward * 0.2f;
            Physics.SyncTransforms();
            InvokePrivate(relay, "OnTriggerEnter", playerCollider);
            playerObject.transform.position = Vector3.back * 0.1f;
            Physics.SyncTransforms();
            InvokePrivate(relay, "OnTriggerStay", playerCollider);

            Assert.That(
                Vector3.Distance(
                    playerObject.transform.position,
                    destinationObject.transform.position),
                Is.LessThan(1f),
                "The configured player must retain bidirectional traversal.");

            Object.DestroyImmediate(returnGateObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(playerObject);
            Object.DestroyImmediate(oreObject);
        }

        [Test]
        public void ThinRigidbody_RendersBothSidesBeforePhysicalPlaneCrossing()
        {
            GameObject sourceObject = new GameObject("Source");
            GameObject triggerObject = new GameObject("Traversal Trigger");
            triggerObject.transform.SetParent(sourceObject.transform, false);
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            Shader clipShader = AssetDatabase.LoadAssetAtPath<Shader>(
                ProjectAssetPaths.Shaders.PortalExampleClippedLit);
            Assert.That(clipShader, Is.Not.Null);
            Assert.That(clipShader.isSupported, Is.True);
            SetObjectReference(source, "seamlessClipShader", clipShader);
            InvokePrivate(source, "EnsureTriggerRelays");

            GameObject destinationObject = new GameObject("Destination");
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            destinationObject.transform.position = new Vector3(8f, 0f, 0f);
            Link(source, destination);

            GameObject supportObject = new GameObject("Destination Support");
            supportObject.transform.position = destinationObject.transform.position;
            BoxCollider supportCollider =
                supportObject.AddComponent<BoxCollider>();
            supportCollider.size = new Vector3(3f, 3f, 0.2f);

            GameObject bookObject =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            bookObject.name = "Thin Book";
            bookObject.transform.localScale =
                new Vector3(0.52f, 0.1f, 0.2f);
            Rigidbody body = bookObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.velocity = Vector3.back;
            BoxCollider bookCollider = bookObject.GetComponent<BoxCollider>();
            MeshRenderer bookRenderer = bookObject.GetComponent<MeshRenderer>();
            bookObject.transform.position = Vector3.forward * 0.08f;
            Physics.SyncTransforms();

            PortalExampleTriggerRelay relay =
                triggerObject.GetComponent<PortalExampleTriggerRelay>();
            InvokePrivate(relay, "OnTriggerEnter", bookCollider);

            PortalExampleTraveller traveller =
                bookObject.GetComponent<PortalExampleTraveller>();
            Assert.That(traveller, Is.Not.Null);
            Assert.That(traveller.IsTraversingPortal, Is.True);
            Assert.That(bookObject.transform.position.z, Is.GreaterThan(0f));
            Assert.That(bookRenderer.sharedMaterial.shader, Is.SameAs(clipShader));
            Assert.That(
                Physics.GetIgnoreCollision(bookCollider, supportCollider),
                Is.True);

            bookObject.transform.position = Vector3.back * 0.02f;
            Physics.SyncTransforms();
            InvokePrivate(relay, "OnTriggerStay", bookCollider);
            float destinationSide = Vector3.Dot(
                bookCollider.bounds.center - destination.transform.position,
                destination.transform.forward);
            Assert.That(destinationSide, Is.GreaterThan(0f));
            Assert.That(traveller.IsTraversingPortal, Is.True);

            InvokePrivate(destination, "HandleTriggerExit", bookCollider);
            Assert.That(traveller.IsTraversingPortal, Is.False);
            Assert.That(
                Physics.GetIgnoreCollision(bookCollider, supportCollider),
                Is.False);
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(supportObject);
            Object.DestroyImmediate(bookObject);
        }

        [Test]
        public void KinematicPortalSupport_DoesNotBecomeVisualTraveller()
        {
            GameObject sourceObject = new GameObject("Source");
            GameObject triggerObject = new GameObject("Traversal Trigger");
            triggerObject.transform.SetParent(sourceObject.transform, false);
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            InvokePrivate(source, "EnsureTriggerRelays");

            GameObject destinationObject = new GameObject("Destination");
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            Link(source, destination);

            GameObject diskObject =
                GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            diskObject.name = "Checkpoint Disk";
            Rigidbody diskBody = diskObject.AddComponent<Rigidbody>();
            diskBody.isKinematic = true;
            diskBody.useGravity = false;
            Collider diskCollider = diskObject.GetComponent<Collider>();
            Material originalMaterial =
                diskObject.GetComponent<Renderer>().sharedMaterial;

            PortalExampleTriggerRelay relay =
                triggerObject.GetComponent<PortalExampleTriggerRelay>();
            InvokePrivate(relay, "OnTriggerEnter", diskCollider);

            Assert.That(
                diskObject.GetComponent<PortalExampleTraveller>(),
                Is.Null);
            Assert.That(
                diskObject.GetComponent<Renderer>().sharedMaterial,
                Is.SameAs(originalMaterial));

            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(diskObject);
        }

        [Test]
        public void DeepTerrainSupport_IsIgnoredAcrossRigidbodyPortalTunnel()
        {
            GameObject sourceObject = new GameObject("Horizontal Source");
            sourceObject.transform.localScale = Vector3.one * 0.6f;
            GameObject triggerObject = new GameObject("Traversal Trigger");
            triggerObject.transform.SetParent(sourceObject.transform, false);
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.05f, 2.05f, 1.2f);
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            InvokePrivate(source, "EnsureTriggerRelays");

            GameObject destinationObject = new GameObject("Destination");
            destinationObject.transform.position = new Vector3(8f, 0f, 0f);
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            Link(source, destination);

            GameObject terrainObject = new GameObject("Terrain Mesh");
            terrainObject.transform.position = Vector3.back * 0.35f;
            BoxCollider terrainCollider =
                terrainObject.AddComponent<BoxCollider>();
            terrainCollider.size = new Vector3(4f, 4f, 0.2f);

            GameObject cauldronObject = new GameObject("Cauldron Sized Body");
            Rigidbody body = cauldronObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            BoxCollider cauldronCollider =
                cauldronObject.AddComponent<BoxCollider>();
            cauldronCollider.size = new Vector3(1.064f, 0.907f, 0.831f);
            cauldronObject.transform.position = Vector3.forward * 0.35f;
            Physics.SyncTransforms();

            PortalExampleTriggerRelay relay =
                triggerObject.GetComponent<PortalExampleTriggerRelay>();
            InvokePrivate(relay, "OnTriggerEnter", cauldronCollider);

            Assert.That(
                Physics.GetIgnoreCollision(
                    cauldronCollider,
                    terrainCollider),
                Is.True);

            cauldronObject.transform.position = Vector3.back * 0.05f;
            Physics.SyncTransforms();
            InvokePrivate(relay, "OnTriggerStay", cauldronCollider);
            Assert.That(
                Vector3.Dot(
                    cauldronObject.transform.position
                        - destinationObject.transform.position,
                    destinationObject.transform.forward),
                Is.GreaterThan(0f));

            InvokePrivate(destination, "HandleTriggerExit", cauldronCollider);
            Assert.That(
                Physics.GetIgnoreCollision(
                    cauldronCollider,
                    terrainCollider),
                Is.False);

            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(terrainObject);
            Object.DestroyImmediate(cauldronObject);
        }

        [Test]
        public void CompoundRigidbody_RetainsCrossingUntilLastColliderExits()
        {
            GameObject sourceObject = new GameObject("Source");
            GameObject triggerObject = new GameObject("Traversal Trigger");
            triggerObject.transform.SetParent(sourceObject.transform, false);
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            InvokePrivate(source, "EnsureTriggerRelays");

            GameObject destinationObject = new GameObject("Destination");
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            destinationObject.transform.position = new Vector3(8f, 0f, 0f);
            Link(source, destination);

            GameObject oreObject = new GameObject("Compound Ore");
            Rigidbody body = oreObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            GameObject leadingObject = new GameObject("Leading Collider");
            leadingObject.transform.SetParent(oreObject.transform, false);
            leadingObject.transform.localPosition = Vector3.back * 0.2f;
            BoxCollider leadingCollider =
                leadingObject.AddComponent<BoxCollider>();
            leadingCollider.size = Vector3.one * 0.4f;
            GameObject trailingObject = new GameObject("Trailing Collider");
            trailingObject.transform.SetParent(oreObject.transform, false);
            trailingObject.transform.localPosition = Vector3.forward * 0.2f;
            BoxCollider trailingCollider =
                trailingObject.AddComponent<BoxCollider>();
            trailingCollider.size = Vector3.one * 0.4f;
            oreObject.transform.position = Vector3.forward;
            Physics.SyncTransforms();

            PortalExampleTriggerRelay relay =
                triggerObject.GetComponent<PortalExampleTriggerRelay>();
            InvokePrivate(relay, "OnTriggerEnter", leadingCollider);
            InvokePrivate(relay, "OnTriggerEnter", trailingCollider);
            InvokePrivate(relay, "OnTriggerExit", leadingCollider);
            oreObject.transform.position = Vector3.back * 0.1f;
            Physics.SyncTransforms();
            InvokePrivate(relay, "OnTriggerStay", trailingCollider);

            float destinationSide = Vector3.Dot(
                oreObject.transform.position - destination.transform.position,
                destination.transform.forward);
            Assert.That(destinationSide, Is.GreaterThan(0f));

            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(oreObject);
        }

        [Test]
        public void RigidbodyTeleport_ClearsWholeColliderPastExitPlane()
        {
            GameObject sourceObject = new GameObject("Source");
            GameObject destinationObject = new GameObject("Destination");
            GameObject oreObject = new GameObject("Large Ore");
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            destinationObject.transform.position = new Vector3(8f, 0f, 0f);
            Rigidbody body = oreObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            BoxCollider oreCollider = oreObject.AddComponent<BoxCollider>();
            oreCollider.size = new Vector3(1f, 1f, 2f);
            PortalExampleTraveller traveller =
                oreObject.AddComponent<PortalExampleTraveller>();

            Assert.That(traveller.Teleport(source, destination), Is.True);

            Vector3 normal = destination.transform.forward;
            Bounds bounds = oreCollider.bounds;
            float projectedExtent = Mathf.Abs(normal.x) * bounds.extents.x
                + Mathf.Abs(normal.y) * bounds.extents.y
                + Mathf.Abs(normal.z) * bounds.extents.z;
            float minimumExitSide = Vector3.Dot(
                    bounds.center - destination.transform.position,
                    normal)
                - projectedExtent;
            Assert.That(minimumExitSide, Is.GreaterThanOrEqualTo(0.089f));

            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(oreObject);
        }

        [Test]
        public void ChildTrigger_AutoRegistersCharacterControllerTraveller()
        {
            GameObject sourceObject = new GameObject("Source");
            GameObject triggerObject = new GameObject("Traversal Trigger");
            triggerObject.transform.SetParent(sourceObject.transform, false);
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            InvokePrivate(source, "EnsureTriggerRelays");

            GameObject destinationObject = new GameObject("Destination");
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            destinationObject.transform.position = new Vector3(8f, 0f, 0f);
            Link(source, destination);

            GameObject playerObject = new GameObject("Unregistered Player");
            CharacterController playerCollider =
                playerObject.AddComponent<CharacterController>();
            playerCollider.height = 1.6f;
            playerCollider.radius = 0.3f;
            playerCollider.center = Vector3.up * 0.8f;
            playerObject.transform.position = Vector3.forward * 0.6f;
            Physics.SyncTransforms();

            PortalExampleTriggerRelay relay =
                triggerObject.GetComponent<PortalExampleTriggerRelay>();
            InvokePrivate(relay, "OnTriggerEnter", playerCollider);
            Assert.That(
                playerObject.GetComponent<PortalExampleTraveller>(),
                Is.Not.Null);

            playerObject.transform.position = Vector3.back * 0.1f;
            InvokePrivate(relay, "OnTriggerStay", playerCollider);
            float destinationSide = Vector3.Dot(
                playerObject.transform.position - destinationObject.transform.position,
                destinationObject.transform.forward);

            Assert.That(destinationSide, Is.GreaterThan(0f));
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(playerObject);
        }

        [Test]
        public void WallPortal_TeleportsWhenControllerLeadingEdgeReachesPlane()
        {
            GameObject sourceObject = new GameObject("Wall Portal");
            GameObject triggerObject = new GameObject("Traversal Trigger");
            triggerObject.transform.SetParent(sourceObject.transform, false);
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.05f, 2.05f, 1.2f);
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            InvokePrivate(source, "EnsureTriggerRelays");

            GameObject destinationObject = new GameObject("Destination");
            destinationObject.transform.position = new Vector3(8f, 0f, 0f);
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            Link(source, destination);

            GameObject playerObject = new GameObject("Approaching Player");
            CharacterController playerCollider =
                playerObject.AddComponent<CharacterController>();
            playerCollider.height = 1.6f;
            playerCollider.radius = 0.3f;
            playerCollider.center = Vector3.up * 0.8f;
            playerObject.transform.position = Vector3.forward * 0.42f;
            Physics.SyncTransforms();

            playerCollider.Move(Vector3.back * 0.1f);
            Assert.That(playerObject.transform.position.z, Is.GreaterThan(0f));

            PortalExampleTriggerRelay relay =
                triggerObject.GetComponent<PortalExampleTriggerRelay>();
            InvokePrivate(relay, "OnTriggerEnter", playerCollider);

            float destinationSide = Vector3.Dot(
                playerObject.transform.position
                    - destinationObject.transform.position,
                destinationObject.transform.forward);
            Assert.That(destinationSide, Is.GreaterThan(0f));

            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(playerObject);
        }

        [Test]
        public void UpwardHorizontalPortal_TeleportsGroundedCharacterController()
        {
            GameObject sourceObject = new GameObject("Floor Portal");
            sourceObject.transform.SetPositionAndRotation(
                Vector3.up * 0.06f,
                Quaternion.LookRotation(Vector3.up, Vector3.forward));
            GameObject triggerObject = new GameObject("Traversal Trigger");
            triggerObject.transform.SetParent(sourceObject.transform, false);
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.05f, 2.05f, 1.2f);
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            InvokePrivate(source, "EnsureTriggerRelays");

            GameObject destinationObject = new GameObject("Destination");
            destinationObject.transform.position = new Vector3(8f, 2f, 0f);
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            Link(source, destination);

            GameObject playerObject = new GameObject("Grounded Player");
            CharacterController playerCollider =
                playerObject.AddComponent<CharacterController>();
            playerCollider.height = 1.6f;
            playerCollider.radius = 0.3f;
            playerCollider.center = Vector3.up * 0.8f;
            // The feet remain 9 cm in front of the visual plane, as can happen
            // on voxel steps and from CharacterController skin separation.
            playerObject.transform.position = Vector3.up * 0.15f;
            Physics.SyncTransforms();

            PortalExampleTriggerRelay relay =
                triggerObject.GetComponent<PortalExampleTriggerRelay>();
            InvokePrivate(relay, "OnTriggerEnter", playerCollider);

            float destinationSide = Vector3.Dot(
                playerObject.transform.position
                    - destinationObject.transform.position,
                destinationObject.transform.forward);
            Assert.That(destinationSide, Is.GreaterThan(0f));

            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(playerObject);
        }

        [Test]
        public void UpwardHorizontalPortal_DoesNotPullControllerOutsideEntryBand()
        {
            GameObject sourceObject = new GameObject("Floor Portal");
            sourceObject.transform.SetPositionAndRotation(
                Vector3.up * 0.06f,
                Quaternion.LookRotation(Vector3.up, Vector3.forward));
            GameObject triggerObject = new GameObject("Traversal Trigger");
            triggerObject.transform.SetParent(sourceObject.transform, false);
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.05f, 2.05f, 1.2f);
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            InvokePrivate(source, "EnsureTriggerRelays");

            GameObject destinationObject = new GameObject("Destination");
            destinationObject.transform.position = new Vector3(8f, 2f, 0f);
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            Link(source, destination);

            GameObject playerObject = new GameObject("Elevated Player");
            CharacterController playerCollider =
                playerObject.AddComponent<CharacterController>();
            playerCollider.height = 1.6f;
            playerCollider.radius = 0.3f;
            playerCollider.center = Vector3.up * 0.8f;
            playerObject.transform.position = Vector3.up * 0.25f;
            Physics.SyncTransforms();

            PortalExampleTriggerRelay relay =
                triggerObject.GetComponent<PortalExampleTriggerRelay>();
            InvokePrivate(relay, "OnTriggerEnter", playerCollider);

            Assert.That(playerObject.transform.position.y, Is.EqualTo(0.25f));
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(playerObject);
        }

        [Test]
        public void CircularAperture_DoesNotTeleportThroughSquareCorner()
        {
            GameObject sourceObject = new GameObject("Source");
            GameObject triggerObject = new GameObject("Traversal Trigger");
            triggerObject.transform.SetParent(sourceObject.transform, false);
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            PortalExampleGate source =
                sourceObject.AddComponent<PortalExampleGate>();
            InvokePrivate(source, "EnsureTriggerRelays");

            GameObject destinationObject = new GameObject("Destination");
            PortalExampleGate destination =
                destinationObject.AddComponent<PortalExampleGate>();
            destinationObject.transform.position = new Vector3(8f, 0f, 0f);
            Link(source, destination);

            GameObject bodyObject = new GameObject("Corner Body");
            Rigidbody body = bodyObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            SphereCollider bodyCollider =
                bodyObject.AddComponent<SphereCollider>();
            bodyObject.transform.position = new Vector3(0.9f, 0.9f, 0.2f);

            PortalExampleTriggerRelay relay =
                triggerObject.GetComponent<PortalExampleTriggerRelay>();
            InvokePrivate(relay, "OnTriggerEnter", bodyCollider);
            bodyObject.transform.position = new Vector3(0.9f, 0.9f, -0.1f);
            InvokePrivate(relay, "OnTriggerStay", bodyCollider);

            Assert.That(
                Vector3.Distance(
                    bodyObject.transform.position,
                    destinationObject.transform.position),
                Is.GreaterThan(1f));
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(destinationObject);
            Object.DestroyImmediate(bodyObject);
        }

        [Test]
        public void Scene_UsesRequestedLocationAndContainsLinkedPair()
        {
            StringAssert.StartsWith(
                "Assets/Scene/",
                ProjectAssetPaths.Scenes.PortalExample);

            Scene scene = EditorSceneManager.OpenScene(
                ProjectAssetPaths.Scenes.PortalExample,
                OpenSceneMode.Additive);
            PortalExampleGate[] gates = Object.FindObjectsOfType<PortalExampleGate>();
            PortalExampleFirstPersonController[] players =
                Object.FindObjectsOfType<PortalExampleFirstPersonController>();

            Assert.That(gates, Has.Length.EqualTo(2));
            Assert.That(gates[0].LinkedGate, Is.Not.Null);
            Assert.That(gates[1].LinkedGate, Is.Not.Null);
            Assert.That(gates[0].LinkedGate, Is.Not.SameAs(gates[0]));
            Assert.That(players, Has.Length.EqualTo(1));
            for (int index = 0; index < gates.Length; index++)
            {
                Transform surface = gates[index].transform.Find("Live Portal View");
                BoxCollider trigger = gates[index]
                    .GetComponentInChildren<BoxCollider>(true);
                Assert.That(surface, Is.Not.Null);
                Assert.That(
                    surface.localScale.x,
                    Is.EqualTo(surface.localScale.y).Within(0.0001f));
                Assert.That(trigger, Is.Not.Null);
                SerializedObject serializedGate =
                    new SerializedObject(gates[index]);
                Assert.That(
                    serializedGate.FindProperty("seamlessClipShader")
                        .objectReferenceValue,
                    Is.Not.Null);
                Assert.That(
                    trigger.size.x,
                    Is.EqualTo(trigger.size.y).Within(0.0001f));
            }
            EditorSceneManager.CloseScene(scene, true);
        }

        private static void Link(
            PortalExampleGate source,
            PortalExampleGate destination)
        {
            SerializedObject serializedSource = new SerializedObject(source);
            serializedSource.FindProperty("linkedGate").objectReferenceValue =
                destination;
            serializedSource.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectReference(
            Object target,
            string propertyName,
            Object value)
        {
            SerializedObject serializedTarget = new SerializedObject(target);
            serializedTarget.FindProperty(propertyName).objectReferenceValue =
                value;
            serializedTarget.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void InvokePrivate(
            object target,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(target, arguments);
        }
    }
}
