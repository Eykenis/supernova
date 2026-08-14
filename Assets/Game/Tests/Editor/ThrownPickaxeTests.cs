using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class ThrownPickaxeTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<Object> assets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
            for (int i = assets.Count - 1; i >= 0; i--)
            {
                if (assets[i] != null) Object.DestroyImmediate(assets[i]);
            }
            assets.Clear();
        }

        [Test]
        public void PinRotation_AlignsTheHeadTipWithTheTravelDirection()
        {
            Vector3 tipDirection = new Vector3(-0.86f, 0.51f, 0.03f).normalized;
            Vector3[] inwardDirections =
            {
                Vector3.forward,
                Vector3.down,
                new Vector3(1f, -1f, 0f).normalized,
                new Vector3(-0.3f, 0.2f, 0.9f).normalized,
            };

            for (int i = 0; i < inwardDirections.Length; i++)
            {
                Quaternion rotation = ThrownPickaxe.CalculatePinRotation(
                    tipDirection,
                    inwardDirections[i]);
                Assert.That(
                    Vector3.Angle(rotation * tipDirection, inwardDirections[i]),
                    Is.LessThan(0.01f),
                    inwardDirections[i].ToString());
            }
        }

        [Test]
        public void PinRotation_RollsTheShaftAsUprightAsTheAlignmentAllows()
        {
            Vector3 tipDirection = Vector3.left;
            // Perpendicular to the spike, as the real pickaxe's shaft is.
            Vector3 shaftDirection = Vector3.down;

            // Spike driven straight into the ground: the shaft is perpendicular to
            // it, so the handle can only lie in the horizontal plane.
            Quaternion downward = ThrownPickaxe.CalculatePinRotation(
                tipDirection,
                Vector3.down,
                shaftDirection);
            Assert.That(
                Vector3.Angle(downward * tipDirection, Vector3.down),
                Is.LessThan(0.01f));
            Assert.That(
                (downward * shaftDirection).y,
                Is.EqualTo(0f).Within(0.001f));

            // Spike driven horizontally into a wall: the shaft is now free to point
            // straight up, lifting the handle clear of the surface.
            Quaternion horizontal = ThrownPickaxe.CalculatePinRotation(
                tipDirection,
                Vector3.forward,
                shaftDirection);
            Assert.That(
                Vector3.Angle(horizontal * tipDirection, Vector3.forward),
                Is.LessThan(0.01f));
            Assert.That(
                (horizontal * shaftDirection).y,
                Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void Pin_BuriesTheHeadTipAtTheContactPointAndFreezesTheBody()
        {
            ThrownPickaxe pickaxe = CreatePickaxe(
                out Rigidbody body,
                out Transform pivot);
            Vector3 contact = new Vector3(4f, 2f, 6f);
            // Steep enough that the bite-angle correction leaves it alone, so the
            // buried tip lands exactly along the travel direction.
            Vector3 travel = new Vector3(1f, -1f, 0f).normalized;

            pickaxe.Pin(contact, travel, -travel, null);

            Vector3 tipPosition = pickaxe.transform.TransformPoint(
                pickaxe.HeadTipLocalPosition);
            Assert.That(
                Vector3.Distance(
                    tipPosition,
                    contact + travel * pickaxe.PinDepth),
                Is.LessThan(0.001f));
            Assert.That(pickaxe.IsPinned, Is.True);
            Assert.That(body.isKinematic, Is.True);
            Assert.That(body.useGravity, Is.False);
            Assert.That(body.velocity, Is.EqualTo(Vector3.zero));
            Assert.That(pivot, Is.Not.Null);
        }

        [Test]
        public void Pin_LandsOnTheBuriedPoseImmediatelyWithoutSlidingIntoPlace()
        {
            Vector3 travel = new Vector3(1f, -0.5f, 0f).normalized;
            Vector3 contact = new Vector3(2f, 2f, 0f);

            // Whatever the spin phase was at impact, the pickaxe has to be in its
            // final buried pose on the very first frame. Easing towards it instead
            // drags the whole pickaxe across the surface, which reads as the handle
            // burying itself first.
            foreach (float spinDegrees in new[] { 0f, 90f, 180f, 270f })
            {
                ThrownPickaxe pickaxe = CreatePickaxe(out _, out Transform pivot);
                pickaxe.transform.position = new Vector3(-1f, 3f, 0f);
                pickaxe.transform.rotation =
                    ThrownPickaxe.CalculateFlightRotation(travel);
                pivot.localRotation = Quaternion.Euler(0f, 0f, spinDegrees);

                pickaxe.Pin(contact, travel, -travel, null);

                Vector3 tip = pickaxe.transform.TransformPoint(
                    pickaxe.HeadTipLocalPosition);
                Assert.That(
                    Vector3.Distance(tip, contact + travel * pickaxe.PinDepth),
                    Is.LessThan(0.001f),
                    "spin phase " + spinDegrees);
                // The spin must stop dead, or the model keeps spinning about the
                // buried head.
                Assert.That(
                    Quaternion.Angle(pivot.localRotation, Quaternion.identity),
                    Is.LessThan(0.01f),
                    "spin phase " + spinDegrees);
            }
        }

        [Test]
        public void Pin_BuriesTheSpikeAndNotTheHandle()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.ThrownPickaxe);
            GameObject instance = Object.Instantiate(prefab);
            objects.Add(instance);
            ThrownPickaxe pickaxe = instance.GetComponent<ThrownPickaxe>();
            MeshFilter meshFilter =
                instance.GetComponentInChildren<MeshFilter>(true);
            Assert.That(meshFilter, Is.Not.Null);

            Vector3[] vertices = meshFilter.sharedMesh.vertices;
            Vector3 spikeTip = vertices[0];
            Vector3 handleButt = vertices[0];
            for (int i = 1; i < vertices.Length; i++)
            {
                if (vertices[i].x < spikeTip.x) spikeTip = vertices[i];
                if (vertices[i].y < handleButt.y) handleButt = vertices[i];
            }

            // Ground strike: the spike has to end up below the handle butt.
            Vector3 travel = new Vector3(1f, -0.5f, 0f).normalized;
            pickaxe.Pin(Vector3.zero, travel, Vector3.up, null);
            float spikeHeight =
                meshFilter.transform.TransformPoint(spikeTip).y;
            float buttHeight =
                meshFilter.transform.TransformPoint(handleButt).y;
            Assert.That(spikeHeight, Is.LessThan(buttHeight));
            Assert.That(spikeHeight, Is.LessThanOrEqualTo(0f));

            // Wall strike: the spike has to be the part inside the wall.
            Vector3 intoWall = new Vector3(0.2f, -0.1f, 1f).normalized;
            pickaxe.Pin(Vector3.zero, intoWall, Vector3.back, null);
            float spikeDepth =
                meshFilter.transform.TransformPoint(spikeTip).z;
            float buttDepth =
                meshFilter.transform.TransformPoint(handleButt).z;
            Assert.That(spikeDepth, Is.GreaterThan(buttDepth));
            Assert.That(spikeDepth, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void BeginRecall_UnpinsAndFliesHomeInsteadOfVanishing()
        {
            GameObject playerObject = Create("Player");
            PlayerToolController controller =
                playerObject.AddComponent<PlayerToolController>();
            playerObject.transform.position = Vector3.zero;

            ThrownPickaxe pickaxe = CreatePickaxe(out Rigidbody body, out _);
            pickaxe.Launch(
                Vector3.forward * 20f,
                controller,
                PlayerInventoryItem.Pickaxe,
                2f);
            GameObject carrier = Create("Carrier");
            pickaxe.Pin(
                new Vector3(0f, 0f, 5f),
                Vector3.forward,
                Vector3.back,
                carrier.transform);
            Assert.That(pickaxe.transform.parent, Is.SameAs(carrier.transform));

            Assert.That(pickaxe.BeginRecall(), Is.True);
            Assert.That(pickaxe.IsReturning, Is.True);
            // It must survive the recall so the flight is actually visible.
            Assert.That(pickaxe, Is.Not.Null);
            Assert.That(pickaxe.transform.parent, Is.Null);
            Assert.That(body.isKinematic, Is.True);
            // Colliders are off so the flight home cannot re-pin on the way.
            Collider[] colliders =
                pickaxe.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                Assert.That(colliders[i].enabled, Is.False);
            // Recalling twice must not restart the flight.
            Assert.That(pickaxe.BeginRecall(), Is.False);
        }

        [Test]
        public void RecallIsPlayerDriven_NotTriggeredByProximity()
        {
            // Recall used to fire automatically when the player came within
            // PickupDistance, which snatched the pickaxe away while the rope was
            // still in use. The range check now only reports a fact; nothing acts
            // on it, so ThrownPickaxe.Update must contain no recall trigger.
            string source = System.IO.File.ReadAllText(
                ProjectAssetPaths.ToAbsoluteFileSystemPath(
                    "Assets/Game/Runtime/Gameplay/ThrownPickaxe.cs"));
            int updateStart = source.IndexOf("private void Update()");
            Assert.That(updateStart, Is.GreaterThan(0));
            int updateEnd = source.IndexOf("public bool BeginRecall()", updateStart);
            Assert.That(updateEnd, Is.GreaterThan(updateStart));

            string updateBody = source.Substring(
                updateStart,
                updateEnd - updateStart);
            Assert.That(
                updateBody,
                Does.Not.Contain("IsWithinPickupRange"),
                "Update must not recall the pickaxe on proximity.");
            Assert.That(
                updateBody,
                Does.Not.Contain("BeginRecall()"),
                "Update must not start a recall at all.");
        }

        [Test]
        public void RecallThrow_CallsAThrownPickaxeHome()
        {
            GameObject playerObject = Create("Player");
            PlayerToolController controller =
                playerObject.AddComponent<PlayerToolController>();
            PickaxeThrowController throwController =
                playerObject.AddComponent<PickaxeThrowController>();

            // Nothing thrown yet, so there is nothing to recall.
            Assert.That(throwController.RecallThrow(), Is.False);

            ThrownPickaxe pickaxe = CreatePickaxe(out _, out _);
            pickaxe.Launch(
                Vector3.forward * 20f,
                controller,
                PlayerInventoryItem.Pickaxe,
                2f);
            pickaxe.Pin(
                new Vector3(0f, 0f, 12f),
                Vector3.forward,
                Vector3.back,
                null);
            SetPrivateField(throwController, "activeThrow", pickaxe);
            Assert.That(throwController.HasThrowInFlight, Is.True);

            // Distance is irrelevant: the key recalls it from wherever it is.
            Assert.That(throwController.RecallThrow(), Is.True);
            Assert.That(pickaxe.IsReturning, Is.True);
        }

        [Test]
        public void EmbedDirection_NeverLeavesTheSpikeLyingAlongTheSurface()
        {
            const float minimumBite = 35f;

            // A nearly horizontal throw into the ground is the case that used to
            // leave the pickaxe flat on the surface instead of dug in.
            foreach (Vector3 travel in new[]
            {
                new Vector3(1f, -0.05f, 0f).normalized,
                new Vector3(1f, -0.15f, 0f).normalized,
                new Vector3(1f, -0.5f, 0f).normalized,
                new Vector3(0.4f, -0.02f, 0.9f).normalized,
            })
            {
                Vector3 embed = ThrownPickaxe.CalculateEmbedDirection(
                    travel,
                    Vector3.up,
                    minimumBite);
                float biteAngle = 90f - Vector3.Angle(embed, Vector3.down);
                Assert.That(
                    biteAngle,
                    Is.GreaterThanOrEqualTo(minimumBite - 0.01f),
                    travel.ToString());
                // The lean of the throw is preserved, not flattened to the normal.
                Assert.That(
                    Vector3.Dot(embed, travel),
                    Is.GreaterThan(0f),
                    travel.ToString());
            }
        }

        [Test]
        public void EmbedDirection_LeavesSteepImpactsUntouched()
        {
            Vector3 steep = new Vector3(0.1f, -1f, 0f).normalized;
            Vector3 embed = ThrownPickaxe.CalculateEmbedDirection(
                steep,
                Vector3.up,
                35f);

            // Already biting in well past the minimum, so it must not be rotated.
            Assert.That(Vector3.Angle(embed, steep), Is.LessThan(0.01f));
        }

        [Test]
        public void PinRotation_DrivesTheSpikeInAndLeavesTheHandleClearOfTheSurface()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.ThrownPickaxe);
            ThrownPickaxe authored = prefab.GetComponent<ThrownPickaxe>();
            Vector3 spike = authored.HeadTipLocalDirection;
            Vector3 shaft = authored.ShaftLocalDirection;

            // Ground: the spike must point down into it and the handle must lift up.
            Vector3 grazing = new Vector3(1f, -0.15f, 0f).normalized;
            Vector3 groundEmbed = ThrownPickaxe.CalculateEmbedDirection(
                grazing,
                Vector3.up,
                authored.MinimumBiteAngle);
            Quaternion groundPose = ThrownPickaxe.CalculatePinRotation(
                spike,
                groundEmbed,
                shaft);
            Assert.That((groundPose * spike).y, Is.LessThan(-0.3f));
            Assert.That((groundPose * shaft).y, Is.GreaterThan(0.3f));

            // Wall: the spike must go into it, not run along its face.
            Vector3 wallEmbed = ThrownPickaxe.CalculateEmbedDirection(
                new Vector3(0.4f, -0.15f, 1f).normalized,
                Vector3.back,
                authored.MinimumBiteAngle);
            Quaternion wallPose = ThrownPickaxe.CalculatePinRotation(
                spike,
                wallEmbed,
                shaft);
            Assert.That((wallPose * spike).z, Is.GreaterThan(0.3f));
            Assert.That((wallPose * shaft).y, Is.GreaterThan(0.3f));
        }

        [Test]
        public void AuthoredPickaxe_UsesTheHeadSpikeAxisNotTheCentreOfMassAxis()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.ThirdParty.StylizedPickaxePrefab);
            Mesh mesh = model.GetComponentInChildren<MeshFilter>(true).sharedMesh;

            Vector3 spike = Supernova.Editor.Gameplay.ThrownPickaxeAssetBuilder
                .CalculateHeadSpikeDirection(mesh);
            Vector3 centreOfMass = Supernova.Editor.Gameplay
                .ThrownPickaxeAssetBuilder.CalculateCentreOfMass(mesh);
            Vector3 tip = Supernova.Editor.Gameplay.ThrownPickaxeAssetBuilder
                .CalculateHeadTip(mesh);
            Vector3 centreOfMassAxis = (tip - centreOfMass).normalized;

            // The centre of mass sits down the shaft, so using it as the spike axis
            // tilts the buried pose well off the actual pick direction.
            Assert.That(
                Vector3.Angle(spike, centreOfMassAxis),
                Is.GreaterThan(15f));
            // The real spike runs along the head bar, essentially straight out in X.
            Assert.That(Mathf.Abs(spike.x), Is.GreaterThan(0.98f));
            Assert.That(Mathf.Abs(spike.y), Is.LessThan(0.1f));

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.ThrownPickaxe);
            Assert.That(
                Vector3.Angle(
                    prefab.GetComponent<ThrownPickaxe>().HeadTipLocalDirection,
                    spike),
                Is.LessThan(1f));
        }

        [Test]
        public void HidingTheHeldModel_KeepsTheInstanceButTurnsOffItsRenderers()
        {
            GameObject playerObject = Create("Player");
            PlayerToolController controller =
                playerObject.AddComponent<PlayerToolController>();
            GameObject mount = Create("Tool Model Mount");
            mount.transform.SetParent(playerObject.transform);

            GameObject modelPrefab = Create("Pickaxe Model Prefab");
            modelPrefab.AddComponent<MeshRenderer>();
            GameObject childVisual = new GameObject("Child Visual");
            childVisual.transform.SetParent(modelPrefab.transform);
            childVisual.AddComponent<MeshRenderer>();

            PlayerToolDefinition definition =
                ScriptableObject.CreateInstance<PlayerToolDefinition>();
            assets.Add(definition);
            SetPrivateField(definition, "item", PlayerInventoryItem.Pickaxe);
            SetPrivateField(definition, "heldModelPrefab", modelPrefab);
            SetPrivateField(
                controller,
                "toolDefinitions",
                new[] { definition });
            SetPrivateField(controller, "toolModelMount", mount.transform);
            Assert.That(
                controller.ConfigureSlot(0, PlayerInventoryItem.Pickaxe),
                Is.True);
            controller.SelectSlot(0);
            Assert.That(controller.EquippedToolModel, Is.Not.Null);

            controller.SetEquippedToolModelHidden(true);
            Assert.That(controller.IsEquippedToolModelHidden, Is.True);
            // The model must survive so its mount and pose are not rebuilt.
            Assert.That(controller.EquippedToolModel, Is.Not.Null);
            AssertRenderersEnabled(controller.EquippedToolModel, false);

            controller.SetEquippedToolModelHidden(false);
            AssertRenderersEnabled(controller.EquippedToolModel, true);
        }

        [Test]
        public void FlightRotation_LeadsWithTheHeadNotTheTail()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.ThrownPickaxe);
            Vector3 spike =
                prefab.GetComponent<ThrownPickaxe>().HeadTipLocalDirection;

            // The authored mesh points its spike down local -X, so assuming +X flies
            // the pickaxe backwards and buries the handle on impact.
            foreach (Vector3 travel in new[]
            {
                new Vector3(0f, 1f, 0.3f).normalized,
                new Vector3(0f, -1f, 0.3f).normalized,
                Vector3.right,
                new Vector3(0.3f, 0.8f, 0f).normalized,
            })
            {
                Quaternion rotation =
                    ThrownPickaxe.CalculateFlightRotation(travel, spike);
                Assert.That(
                    Vector3.Dot(rotation * spike, travel),
                    Is.GreaterThan(0.99f),
                    travel.ToString());
            }
        }

        [Test]
        public void Pin_LeavesTheHandleExposedOnEverySurfaceOrientation()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.ThrownPickaxe);
            GameObject instance = Object.Instantiate(prefab);
            objects.Add(instance);
            ThrownPickaxe pickaxe = instance.GetComponent<ThrownPickaxe>();
            MeshFilter meshFilter =
                instance.GetComponentInChildren<MeshFilter>(true);
            Vector3[] vertices = meshFilter.sharedMesh.vertices;

            // A ceiling hit is the case that used to bury almost the whole pickaxe,
            // leaving nothing for the magnet to latch onto.
            (Vector3 travel, Vector3 normal, string label)[] cases =
            {
                (new Vector3(0f, 1f, 0.3f).normalized, Vector3.down, "ceiling"),
                (new Vector3(0f, -1f, 0.3f).normalized, Vector3.up, "floor"),
                (new Vector3(0.3f, 0f, 1f).normalized, Vector3.back, "wall"),
                (new Vector3(1f, -0.5f, 0f).normalized, Vector3.up, "grazing"),
            };

            foreach ((Vector3 travel, Vector3 normal, string label) hit in cases)
            {
                Vector3 contact = Vector3.zero;
                pickaxe.transform.rotation = ThrownPickaxe.CalculateFlightRotation(
                    hit.travel,
                    pickaxe.HeadTipLocalDirection);
                pickaxe.Pin(contact, hit.travel, hit.normal, null);

                // The point the magnet aims its sightline at must be outside.
                float visibleDepth = Vector3.Dot(
                    pickaxe.VisiblePosition - contact,
                    -hit.normal);
                Assert.That(visibleDepth, Is.LessThan(0f), hit.label);

                int embedded = 0;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 world =
                        meshFilter.transform.TransformPoint(vertices[i]);
                    if (Vector3.Dot(world - contact, -hit.normal) > 0f)
                        embedded++;
                }

                Assert.That(
                    embedded / (float)vertices.Length,
                    Is.LessThan(0.2f),
                    hit.label + ": most of the pickaxe must stay outside");
            }
        }

        private static void AssertRenderersEnabled(
            GameObject model,
            bool expected)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            for (int i = 0; i < renderers.Length; i++)
                Assert.That(renderers[i].enabled, Is.EqualTo(expected));
        }

        [Test]
        public void Pin_RidesAlongWithWhateverItStruck()
        {
            ThrownPickaxe pickaxe = CreatePickaxe(out _, out _);
            GameObject carrier = Create("Carrier");

            pickaxe.Pin(
                Vector3.zero,
                Vector3.forward,
                Vector3.back,
                carrier.transform);
            Assert.That(
                pickaxe.transform.parent,
                Is.SameAs(carrier.transform));

            Vector3 before = pickaxe.transform.position;
            carrier.transform.position += new Vector3(3f, 1f, 0f);
            Assert.That(
                pickaxe.transform.position - before,
                Is.EqualTo(new Vector3(3f, 1f, 0f)));
        }

        [Test]
        public void PickupRange_UsesTheConfiguredDistance()
        {
            ThrownPickaxe pickaxe = CreatePickaxe(out _, out _);
            pickaxe.transform.position = Vector3.zero;

            Assert.That(
                pickaxe.IsWithinPickupRange(
                    Vector3.forward * (pickaxe.PickupDistance - 0.1f)),
                Is.True);
            Assert.That(
                pickaxe.IsWithinPickupRange(
                    Vector3.forward * (pickaxe.PickupDistance + 0.1f)),
                Is.False);
        }

        [Test]
        public void PullDirection_PointsFromThePlayerTowardsThePickaxe()
        {
            ThrownPickaxe pickaxe = CreatePickaxe(out _, out _);
            pickaxe.transform.position = new Vector3(0f, 0f, 9f);

            Vector3 direction = pickaxe.GetPullDirection(Vector3.zero);
            Assert.That(direction, Is.EqualTo(Vector3.forward));
            Assert.That(
                pickaxe.GetPullDirection(pickaxe.transform.position),
                Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Recover_ReturnsTheSuspendedItemToItsOwner()
        {
            GameObject playerObject = Create("Player");
            PlayerToolController controller =
                playerObject.AddComponent<PlayerToolController>();
            PlayerToolDefinition definition =
                ScriptableObject.CreateInstance<PlayerToolDefinition>();
            assets.Add(definition);
            SetPrivateField(definition, "item", PlayerInventoryItem.Pickaxe);
            SetPrivateField(
                controller,
                "toolDefinitions",
                new[] { definition });
            Assert.That(
                controller.ConfigureSlot(0, PlayerInventoryItem.Pickaxe),
                Is.True);

            ThrownPickaxe pickaxe = CreatePickaxe(out _, out _);
            pickaxe.Launch(
                Vector3.forward * 20f,
                controller,
                PlayerInventoryItem.Pickaxe,
                2f);
            Assert.That(pickaxe.IsFlying, Is.True);
            Assert.That(
                controller.SuspendItem(PlayerInventoryItem.Pickaxe),
                Is.True);
            Assert.That(
                controller.GetItemAtSlot(0),
                Is.EqualTo(PlayerInventoryItem.Empty));

            Assert.That(pickaxe.Recover(), Is.True);
            Assert.That(
                controller.GetItemAtSlot(0),
                Is.EqualTo(PlayerInventoryItem.Pickaxe));
            Assert.That(
                controller.IsItemSuspended(PlayerInventoryItem.Pickaxe),
                Is.False);
        }

        [Test]
        public void ThrownPickaxeAsset_IsBuiltAndWiredIntoThePickaxeTool()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.ThrownPickaxe);
            Assert.That(prefab, Is.Not.Null);

            ThrownPickaxe pickaxe = prefab.GetComponent<ThrownPickaxe>();
            Assert.That(pickaxe, Is.Not.Null);
            Assert.That(prefab.GetComponent<Rigidbody>(), Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<Collider>(true), Is.Not.Null);

            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.runtimeAnimatorController, Is.Not.Null);
            // The pivot has to be a child so spinning it never moves the body.
            Assert.That(
                animator.transform,
                Is.Not.SameAs(prefab.transform));

            PlayerToolDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.PickaxeTool);
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.ThrownPickaxePrefab, Is.SameAs(pickaxe));
        }

        [Test]
        public void SpinClip_LoopsExactlyOneTurnAroundThePivotZAxis()
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                ProjectAssetPaths.Animations.PickaxeSpin);
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.isLooping, Is.True);
            Assert.That(clip.length, Is.EqualTo(1f).Within(0.001f));

            GameObject sample = Create("Spin Sample");
            clip.SampleAnimation(sample, 0f);
            Assert.That(
                Quaternion.Angle(sample.transform.localRotation, Quaternion.identity),
                Is.LessThan(0.01f));

            // A quarter through the loop the pivot is a quarter turn around Z.
            clip.SampleAnimation(sample, 0.25f);
            Assert.That(
                Quaternion.Angle(
                    sample.transform.localRotation,
                    Quaternion.Euler(0f, 0f, 90f)),
                Is.LessThan(0.5f));

            clip.SampleAnimation(sample, 0.5f);
            Assert.That(
                Quaternion.Angle(
                    sample.transform.localRotation,
                    Quaternion.Euler(0f, 0f, 180f)),
                Is.LessThan(0.5f));
        }

        [Test]
        public void SpinPivot_SitsOnTheMeshCentreOfMassRatherThanItsBoundsCentre()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.ThirdParty.StylizedPickaxePrefab);
            Assert.That(model, Is.Not.Null);
            Mesh mesh = model.GetComponentInChildren<MeshFilter>(true).sharedMesh;

            Vector3 centreOfMass =
                Supernova.Editor.Gameplay.ThrownPickaxeAssetBuilder
                    .CalculateCentreOfMass(mesh);
            // The head is the heavy end, so the balance point sits above the
            // bounding-box centre; a bounds-centred spin would look off-balance.
            Assert.That(
                centreOfMass.y,
                Is.GreaterThan(mesh.bounds.center.y + 0.05f));

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.ThrownPickaxe);
            Transform pivot =
                prefab.GetComponentInChildren<Animator>(true).transform;
            Assert.That(pivot.childCount, Is.EqualTo(1));
            Assert.That(
                Vector3.Distance(
                    pivot.GetChild(0).localPosition,
                    -centreOfMass),
                Is.LessThan(0.001f));
        }

        [Test]
        public void PickaxeTool_KeepsTheLoopingMagnetHoldPoseForTheHandAnimation()
        {
            PlayerToolDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.PickaxeTool);
            Assert.That(definition, Is.Not.Null);

            AnimationClip pose = definition.MagnetHoldAnimation;
            Assert.That(pose, Is.Not.Null);
            // The pose is held for as long as right click is down, so it must loop.
            Assert.That(pose.isLooping, Is.True);
            Assert.That(
                AssetDatabase.GetAssetPath(pose),
                Is.EqualTo(ProjectAssetPaths.ThirdParty.SuriyunMagnetHold));
        }

        private ThrownPickaxe CreatePickaxe(
            out Rigidbody body,
            out Transform pivot)
        {
            GameObject root = Create("Thrown Pickaxe");
            body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            GameObject pivotObject = new GameObject("Spin Pivot");
            pivotObject.transform.SetParent(root.transform, false);
            pivot = pivotObject.transform;

            ThrownPickaxe pickaxe = root.AddComponent<ThrownPickaxe>();
            SetPrivateField(pickaxe, "body", body);
            SetPrivateField(pickaxe, "spinPivot", pivot);
            SetPrivateField(
                pickaxe,
                "headTipLocalPosition",
                new Vector3(-0.48f, 0.28f, 0.02f));
            SetPrivateField(
                pickaxe,
                "headTipLocalDirection",
                new Vector3(-0.86f, 0.51f, 0.03f));
            SetPrivateField(pickaxe, "pinDepth", 0.12f);
            SetPrivateField(pickaxe, "pickupDistance", 1.6f);
            return pickaxe;
        }

        private GameObject Create(string name)
        {
            GameObject created = new GameObject(name);
            objects.Add(created);
            return created;
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }
    }
}
