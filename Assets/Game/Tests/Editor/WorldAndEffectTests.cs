using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Supernova.Effects;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class WorldAndEffectTests
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
        public void MagnetBeam_RemainsVisibleAtThePalmSource()
        {
            GameObject host = Create("Magnet Beam");
            MagnetAttractionBeam beam = host.AddComponent<MagnetAttractionBeam>();

            InvokePrivate(beam, "EnsureLine");
            InvokePrivate(beam, "UpdateBeamColor");

            LineRenderer line = host.GetComponent<LineRenderer>();
            Gradient colors = line.colorGradient;
            float sourceAlpha = colors.Evaluate(0f).a;
            float nearSourceAlpha = colors.Evaluate(0.05f).a;

            Assert.That(sourceAlpha, Is.GreaterThan(0f));
            Assert.That(nearSourceAlpha, Is.GreaterThanOrEqualTo(sourceAlpha));
        }

        [Test]
        public void MagnetBeam_StartsAtTheRightPalmCenter()
        {
            GameObject host = Create("Magnet Beam");
            MagnetAttractionBeam beam = host.AddComponent<MagnetAttractionBeam>();
            Transform rightHand = Create("Right Hand").transform;
            Transform rightMiddleProximal = Create("Right Middle Proximal").transform;
            rightHand.position = new Vector3(0.5f, 4f, 8f);
            rightMiddleProximal.position = new Vector3(2.5f, 4f, 8f);
            SetPrivateField(beam, "rightHand", rightHand);
            SetPrivateField(beam, "rightMiddleProximal", rightMiddleProximal);

            Vector3 start = InvokePrivate<Vector3>(beam, "ResolveBeamStart");

            Assert.That(start, Is.EqualTo(new Vector3(1.5f, 4f, 8f)));
        }

        [Test]
        public void MagnetBeam_ExplicitPalmAnchorOverridesAutomaticBones()
        {
            GameObject host = Create("Magnet Beam");
            MagnetAttractionBeam beam = host.AddComponent<MagnetAttractionBeam>();
            Transform rightPalmAnchor = Create("Right Palm Anchor").transform;
            Transform rightHand = Create("Right Hand").transform;
            Transform rightMiddleProximal = Create("Right Middle Proximal").transform;
            rightPalmAnchor.position = new Vector3(3f, 5f, 7f);
            rightHand.position = Vector3.zero;
            rightMiddleProximal.position = Vector3.one;
            SetPrivateField(beam, "rightPalmAnchor", rightPalmAnchor);
            SetPrivateField(beam, "rightHand", rightHand);
            SetPrivateField(beam, "rightMiddleProximal", rightMiddleProximal);

            Vector3 start = InvokePrivate<Vector3>(beam, "ResolveBeamStart");

            Assert.That(start, Is.EqualTo(rightPalmAnchor.position));
        }


        [Test]
        public void MagnetBeam_UsesOneStableQuadraticArc()
        {
            GameObject host = Create("Magnet Beam");
            MagnetAttractionBeam beam = host.AddComponent<MagnetAttractionBeam>();
            SetPrivateField(beam, "arcHeight", 0.6f);

            Vector3 start = new Vector3(0f, 1f, 0f);
            Vector3 end = new Vector3(0f, 1f, 4f);
            Vector3 quarter = InvokePrivate<Vector3>(
                beam, "CalculateCurvePoint", start, end, 0.25f);
            Vector3 midpoint = InvokePrivate<Vector3>(
                beam, "CalculateCurvePoint", start, end, 0.5f);
            Vector3 threeQuarters = InvokePrivate<Vector3>(
                beam, "CalculateCurvePoint", start, end, 0.75f);

            Assert.That(midpoint, Is.EqualTo(new Vector3(0f, 1.6f, 2f)));
            Assert.That(quarter.y, Is.EqualTo(threeQuarters.y).Within(0.0001f));
            Assert.That(quarter.x, Is.Zero.Within(0.0001f));
            Assert.That(midpoint.y, Is.GreaterThan(quarter.y));
        }

        [Test]
        public void MagnetBeam_HelixStrandsOrbitOnOppositeSidesOfTheArc()
        {
            GameObject host = Create("Magnet Beam");
            MagnetAttractionBeam beam = host.AddComponent<MagnetAttractionBeam>();
            SetPrivateField(beam, "helixRadius", 0.2f);
            SetPrivateField(beam, "helixTurns", 2f);

            Vector3 start = Vector3.zero;
            Vector3 end = Vector3.forward * 4f;
            Vector3 center = InvokePrivate<Vector3>(
                beam, "CalculateCurvePoint", start, end, 0.5f);
            Vector3 first = InvokePrivate<Vector3>(
                beam, "CalculateHelixPoint", start, end, 0.5f, 0f);
            Vector3 second = InvokePrivate<Vector3>(
                beam, "CalculateHelixPoint", start, end, 0.5f, Mathf.PI);

            Assert.That(Vector3.Distance(first, center), Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(Vector3.Distance(second, center), Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(
                ((first - center) + (second - center)).magnitude,
                Is.LessThan(0.001f));
        }

        [Test]
        public void MagnetBeam_ChangesFromGreenToRedAsLoadExceedsCurrentForce()
        {
            GameObject host = Create("Magnet Beam");
            FirstPersonMagnetInteractor attractor =
                host.AddComponent<FirstPersonMagnetInteractor>();
            MagnetAttractionBeam beam = host.AddComponent<MagnetAttractionBeam>();
            SetPrivateField(beam, "attractor", attractor);
            SetPrivateField(attractor, "attractionForce", 100f);
            SetPrivateField(attractor, "baseMaximumLiftForce", 100000f);

            GameObject target = Create("Magnet Target");
            Rigidbody body = target.AddComponent<Rigidbody>();
            body.mass = attractor.AttractionForce
                * 0.05f
                / Physics.gravity.magnitude;
            SetPrivateField(attractor, "heldBody", body);
            InvokePrivate(beam, "UpdateLoadPalette", 0.016f);
            Color lightColor = GetPrivateField<Color>(
                beam,
                "currentEnergyColor");

            body.mass = attractor.AttractionForce
                * 2f
                / Physics.gravity.magnitude;
            SetPrivateField(beam, "hasCurrentPalette", false);
            InvokePrivate(beam, "UpdateLoadPalette", 0.016f);
            Color heavyColor = GetPrivateField<Color>(
                beam,
                "currentEnergyColor");

            Assert.That(lightColor.g, Is.GreaterThan(lightColor.r));
            Assert.That(heavyColor.r, Is.GreaterThan(heavyColor.g));
            Assert.That(heavyColor.r, Is.GreaterThan(lightColor.r));
        }

        [Test]
        public void MagnetLoadRatio_UsesCurrentAttractionForce()
        {
            GameObject host = Create("Magnet");
            FirstPersonMagnetInteractor attractor =
                host.AddComponent<FirstPersonMagnetInteractor>();
            SetPrivateField(attractor, "attractionForce", 100f);
            SetPrivateField(attractor, "baseMaximumLiftForce", 300f);

            Rigidbody body = Create("Magnet Target").AddComponent<Rigidbody>();
            body.mass = 10f;
            SetPrivateField(
                attractor,
                "magnetPickupHeight",
                body.worldCenterOfMass.y);

            Assert.That(
                attractor.GetAttractionLoadRatio(body),
                Is.EqualTo(
                    10f
                    * Physics.gravity.magnitude
                    / attractor.AttractionForce)
                    .Within(0.001f));
        }

        [Test]
        public void MagnetLoadRatio_IncreasesAsLiftForceFallsWithHeight()
        {
            GameObject host = Create("Magnet");
            FirstPersonMagnetInteractor attractor =
                host.AddComponent<FirstPersonMagnetInteractor>();
            SetPrivateField(attractor, "attractionForce", 1000f);
            SetPrivateField(attractor, "baseMaximumLiftForce", 300f);
            SetPrivateField(attractor, "liftForceFalloffPerMeter", 0.6f);
            SetPrivateField(attractor, "magnetPickupHeight", 0f);

            Rigidbody body = Create("Magnet Target").AddComponent<Rigidbody>();
            body.mass = 20f;
            body.position = Vector3.up * 2f;
            float availableLiftForce = 300f / (1f + 2f * 0.6f);

            Assert.That(
                attractor.GetAttractionLoadRatio(body),
                Is.EqualTo(20f * Physics.gravity.magnitude / availableLiftForce)
                    .Within(0.001f));
        }




        [Test]
        public void ViewerMovement_RefreshesStreamingWhileMeshesAreStillQueued()
        {
            GameObject terrainObject = Create("Terrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            terrain.InitializeWorld();
            GameObject viewer = Create("Viewer");
            SetPrivateField(terrain, "viewer", viewer.transform);
            SetPrivateField(terrain, "hasViewerChunk", true);
            SetPrivateField(terrain, "viewerChunk", Vector3Int.zero);
            SetPrivateField(terrain, "initialSpawnPlacementPending", false);
            SetPrivateField(
                terrain,
                "generationStage",
                MinecraftCaveGenerationStage.Meshes);
            Queue<Vector3Int> queue = GetMeshQueue(terrain);
            queue.Enqueue(Vector3Int.zero);
            viewer.transform.position = Vector3.right * VoxelVolume.Size * terrain.VoxelSize;

            InvokePrivate(terrain, "RefreshStreamingForViewerMovement");

            Assert.That(terrain.ViewerChunk, Is.EqualTo(Vector3Int.right));
            Assert.That(terrain.GenerationStage, Is.EqualTo(MinecraftCaveGenerationStage.Terrain));
            Assert.That(queue.Count, Is.Zero,
                "A visibility refresh should replace stale queued mesh work immediately.");
        }

        [Test]
        public void InitialSpawn_LoadsLocalMeshesBeforeExpandingToTheFullRadius()
        {
            GameObject terrainObject = Create("Terrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            GameObject viewer = Create("Viewer");
            SetPrivateField(terrain, "viewer", viewer.transform);

            terrain.InitializeWorld();

            Assert.That(
                terrain.SpawnVoxel.y,
                Is.InRange(
                    MinecraftCaveInfiniteWorld.LowestSpawnY,
                    MinecraftCaveInfiniteWorld.HighestSpawnY));
            Assert.That(terrain.RequiredChunkCount, Is.EqualTo(9),
                "Initial loading should be limited to the 3x3 spawn columns.");
            HashSet<Vector3Int> requiredChunks = GetPrivateField<HashSet<Vector3Int>>(
                terrain, "requiredChunks");
            HashSet<Vector3Int> builtMeshes = GetPrivateField<HashSet<Vector3Int>>(
                terrain, "builtMeshes");
            foreach (Vector3Int column in requiredChunks)
            {
                for (int section = 0;
                    section < MinecraftCaveInfiniteWorld.MeshSectionsPerColumn;
                    section++)
                {
                    builtMeshes.Add(new Vector3Int(
                        column.x,
                        section,
                        column.z));
                }
            }
            SetPrivateField(
                terrain,
                "generationStage",
                MinecraftCaveGenerationStage.Meshes);

            InvokePrivate(terrain, "ReportReadyState");

            Assert.That(terrain.IsInitialLoadComplete, Is.True);
            Assert.That(terrain.InitialLoadProgress, Is.EqualTo(1f).Within(0.001f));

            Assert.That(
                terrain.RequiredChunkCount,
                Is.EqualTo(MinecraftCaveInfiniteWorld.RequiredChunkCountAtRadius),
                "The full streaming radius should continue loading after the player is released.");
            Assert.That(terrain.GenerationStage, Is.EqualTo(MinecraftCaveGenerationStage.Terrain));
        }

        [Test]
        public void InitialLoadGravity_RestoresPreviousValueWhenWorldIsCleared()
        {
            GameObject terrainObject = Create("Terrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            Vector3 originalGravity = Physics.gravity;
            Vector3 gravityBeforeLoad = new Vector3(1.5f, -4.25f, 0.75f);

            try
            {
                Physics.gravity = Vector3.zero;
                SetPrivateField(terrain, "gravityBeforeInitialLoad", gravityBeforeLoad);
                SetPrivateField(terrain, "globalGravitySuspended", true);

                InvokePrivate(terrain, "ClearRuntimeState");

                Assert.That(Physics.gravity, Is.EqualTo(gravityBeforeLoad));
                Assert.That(terrain.IsGlobalGravitySuspendedForInitialLoad, Is.False);
            }
            finally
            {
                Physics.gravity = originalGravity;
            }
        }

        [Test]
        public void GroundedSpawn_UsesTerrainSurfaceBeforeEnablingThePlayer()
        {
            GameObject terrainObject = Create("Terrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            GameObject viewer = Create("Viewer");
            viewer.transform.position = new Vector3(0f, 5f, 0f);
            CharacterController controller = viewer.AddComponent<CharacterController>();
            controller.enabled = false;
            SetPrivateField(terrain, "viewer", viewer.transform);
            SetPrivateField(terrain, "frozenCharacterController", controller);
            SetPrivateField(terrain, "targetSpawnWorldPosition", viewer.transform.position);

            GameObject ground = Create("Generated Ground");
            ground.transform.SetParent(terrain.transform, false);
            ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            BoxCollider groundCollider = ground.AddComponent<BoxCollider>();
            groundCollider.size = new Vector3(10f, 1f, 10f);

            MethodInfo method = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "TryFindGroundedSpawnPosition",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { Vector3.zero };

            bool found = (bool)method.Invoke(terrain, arguments);
            Vector3 groundedPosition = (Vector3)arguments[0];

            Assert.That(found, Is.True);
            Assert.That(groundedPosition.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(groundedPosition.z, Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                groundedPosition.y,
                Is.EqualTo(Mathf.Max(0.02f, controller.skinWidth * 2f)).Within(0.01f));
        }

        private static Queue<Vector3Int> GetMeshQueue(MinecraftCaveInfiniteWorld terrain)
        {
            FieldInfo field = typeof(MinecraftCaveInfiniteWorld).GetField(
                "meshQueue", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (Queue<Vector3Int>)field.GetValue(terrain);
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {target.GetType().Name}.{name}");
            return (T)field.GetValue(target);
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {target.GetType().Name}.{name}");
            field.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing method {target.GetType().Name}.{name}");
            method.Invoke(target, null);
        }

        private static void InvokePrivate(
            object target,
            string name,
            params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing method {target.GetType().Name}.{name}");
            method.Invoke(target, arguments);
        }

        private static T InvokePrivate<T>(object target, string name, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing method {target.GetType().Name}.{name}");
            return (T)method.Invoke(target, arguments);
        }

        private GameObject Create(string name)
        {
            var gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject;
        }
    }
}
