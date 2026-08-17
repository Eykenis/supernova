using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Supernova.Effects;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using Supernova.Missions;
using Supernova.UI;
using Supernova.Voxels;
using TMPro;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class ValuableObjectTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            BreakFragmentEffect[] breakEffects =
                Object.FindObjectsOfType<BreakFragmentEffect>(true);
            for (int i = breakEffects.Length - 1; i >= 0; i--)
            {
                if (breakEffects[i] != null)
                {
                    Object.DestroyImmediate(
                        breakEffects[i].gameObject);
                }
            }

            ValueLossPopup[] popups =
                Object.FindObjectsOfType<ValueLossPopup>(true);
            for (int i = popups.Length - 1; i >= 0; i--)
            {
                if (popups[i] != null)
                {
                    Object.DestroyImmediate(popups[i].gameObject);
                }
            }

            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }
            objects.Clear();
        }

        [Test]
        public void CollisionValueLoss_UsesImpulseThresholdAndFragility()
        {
            ValuableObject valuable = CreateValuable("Treasure", 100, 0.5f);

            int loss = valuable.ApplyCollisionImpulse(3f);

            Assert.That(loss, Is.EqualTo(6));
            Assert.That(valuable.CurrentValue, Is.EqualTo(94));
            Assert.That(
                valuable.CurrentValuePercentage,
                Is.EqualTo(0.94f).Within(0.0001f));
        }

        [Test]
        public void CollisionValueLoss_IsPercentageOfInitialValue()
        {
            ValuableObject small = CreateValuable("Small Treasure", 100, 0.5f);
            ValuableObject large = CreateValuable("Large Treasure", 200, 0.5f);

            int smallLoss = small.ApplyCollisionImpulse(3f);
            int largeLoss = large.ApplyCollisionImpulse(3f);

            Assert.That(smallLoss, Is.EqualTo(6));
            Assert.That(largeLoss, Is.EqualTo(12));
            Assert.That(
                small.CurrentValuePercentage,
                Is.EqualTo(large.CurrentValuePercentage).Within(0.0001f));
        }

        [Test]
        public void CollisionValueLoss_GrowsQuadraticallyWithImpactSpeed()
        {
            ValuableObject weakImpact =
                CreateValuable("Weak Impact", 1000, 0.5f);
            ValuableObject strongImpact =
                CreateValuable("Strong Impact", 1000, 0.5f);

            int weakLoss = weakImpact.ApplyCollisionImpulse(2f);
            int strongLoss = strongImpact.ApplyCollisionImpulse(3f);

            Assert.That(weakLoss, Is.EqualTo(15));
            Assert.That(strongLoss, Is.EqualTo(60));
            Assert.That(strongLoss, Is.EqualTo(weakLoss * 4));
        }

        [Test]
        public void CollisionValueLoss_NormalizesImpulseByOwnRigidbodyMass()
        {
            ValuableObject valuable = CreateValuable("Heavy Ore", 100, 0.5f);
            valuable.GetComponent<Rigidbody>().mass = 10f;

            int lowSpeedLoss = valuable.ApplyCollisionImpulse(8f);
            int damagingLoss = valuable.ApplyCollisionImpulse(20f);

            Assert.That(lowSpeedLoss, Is.Zero);
            Assert.That(damagingLoss, Is.EqualTo(2));
            Assert.That(valuable.CurrentValue, Is.EqualTo(98));
        }

        [Test]
        public void CollisionValueLoss_ClampsAtZeroAndMarksObjectBroken()
        {
            ValuableObject valuable = CreateValuable("Fragile Treasure", 8, 1f);

            int loss = valuable.ApplyCollisionImpulse(100f);

            Assert.That(loss, Is.EqualTo(8));
            Assert.That(valuable.CurrentValue, Is.Zero);
            Assert.That(valuable.IsBroken, Is.True);
        }

        [Test]
        public void TreasurePickup_InitializesRuntimeValueAndConfiguredFragility()
        {
            TreasureDefinition definition =
                ScriptableObject.CreateInstance<TreasureDefinition>();
            try
            {
                definition.Configure(
                    null,
                    140,
                    3f,
                    1f,
                    1,
                    12f,
                    0.9f);
                GameObject target = Create("Treasure");
                target.AddComponent<Rigidbody>().isKinematic = true;
                target.AddComponent<BoxCollider>();
                TreasurePickup pickup = target.AddComponent<TreasurePickup>();

                pickup.Configure(definition);

                Assert.That(pickup.Value, Is.EqualTo(140));
                Assert.That(pickup.Valuable.InitialValue, Is.EqualTo(140));
                Assert.That(pickup.Valuable.Fragility, Is.EqualTo(0.9f));
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void MinedOreDrop_UsesActualVolumeForMassAndValue()
        {
            GameObject target = Create("Recovered Ore");
            target.AddComponent<Rigidbody>().isKinematic = true;
            target.AddComponent<BoxCollider>();
            MinedOreDrop drop = target.AddComponent<MinedOreDrop>();
            var mesh = new Mesh();

            drop.Configure(
                new VoxelTypeId(3),
                4,
                2.4f,
                mesh,
                10f,
                null,
                10,
                0.25f);

            Assert.That(
                MinedOreDrop.RecoveredLinearScale,
                Is.EqualTo(1f));
            Assert.That(drop.VoxelCount, Is.EqualTo(4));
            Assert.That(drop.RepresentedFullVoxelVolume, Is.EqualTo(2.4f));
            Assert.That(drop.Value, Is.EqualTo(24));
            Assert.That(drop.Valuable.InitialValue, Is.EqualTo(24));
            Assert.That(drop.Valuable.Fragility, Is.EqualTo(0.25f));
            Assert.That(drop.Body.mass, Is.EqualTo(24f));
        }

        [Test]
        public void MinedOreDrop_TerrainRebuildWaitBlocksDamageAndRestoresMotion()
        {
            GameObject target = Create("Recovered Ore");
            Rigidbody body = target.AddComponent<Rigidbody>();
            target.AddComponent<BoxCollider>();
            MinedOreDrop drop = target.AddComponent<MinedOreDrop>();
            var mesh = new Mesh();
            var velocity = new Vector3(1f, 2f, 3f);
            var angularVelocity = new Vector3(0.25f, 0.5f, 0.75f);
            body.velocity = velocity;
            body.angularVelocity = angularVelocity;
            drop.Configure(
                new VoxelTypeId(3),
                1,
                1f,
                mesh,
                1f,
                null,
                100,
                0.5f);

            MethodInfo suspend = typeof(MinedOreDrop).GetMethod(
                "SuspendForTerrainColliderRebuild",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo release = typeof(MinedOreDrop).GetMethod(
                "ReleaseAfterTerrainColliderRebuild",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(suspend, Is.Not.Null);
            Assert.That(release, Is.Not.Null);

            suspend.Invoke(drop, null);

            Assert.That(drop.IsWaitingForTerrainColliderRebuild, Is.True);
            Assert.That(body.isKinematic, Is.True);
            Assert.That(body.detectCollisions, Is.False);
            Assert.That(drop.Valuable.IsCollisionValueLossProtected, Is.True);
            Assert.That(drop.Valuable.ApplyCollisionImpulse(100f), Is.Zero);
            Assert.That(drop.Value, Is.EqualTo(100));

            release.Invoke(drop, null);

            Assert.That(drop.IsWaitingForTerrainColliderRebuild, Is.False);
            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.detectCollisions, Is.True);
            Assert.That(body.velocity, Is.EqualTo(velocity));
            Assert.That(body.angularVelocity, Is.EqualTo(angularVelocity));
            Assert.That(drop.Valuable.IsCollisionValueLossProtected, Is.False);
            Assert.That(drop.Valuable.ApplyCollisionImpulse(3f), Is.GreaterThan(0));
        }

        [Test]
        public void MinedOreDrop_TerrainRebuildKeepsProtectionUntilPhysicsSync()
        {
            GameObject terrainObject = Create("Terrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            GameObject target = Create("Recovered Ore");
            target.AddComponent<Rigidbody>();
            target.AddComponent<BoxCollider>();
            MinedOreDrop drop = target.AddComponent<MinedOreDrop>();
            drop.Configure(
                new VoxelTypeId(3),
                1,
                1f,
                new Mesh(),
                1f,
                null,
                100,
                0.5f);
            MethodInfo suspend = typeof(MinedOreDrop).GetMethod(
                "SuspendForTerrainColliderRebuild",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo register = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "RegisterOreTerrainRelease",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo notify = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "NotifyOreTerrainMeshRebuilt",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo process = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "ProcessPendingColumnPhysics",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(suspend, Is.Not.Null);
            Assert.That(register, Is.Not.Null);
            Assert.That(notify, Is.Not.Null);
            Assert.That(process, Is.Not.Null);
            var coordinate = Vector3Int.zero;
            var affectedMeshes = new HashSet<Vector3Int> { coordinate };

            suspend.Invoke(drop, null);
            register.Invoke(
                terrain,
                new object[] { drop, affectedMeshes });
            notify.Invoke(terrain, new object[] { coordinate });

            Assert.That(drop.IsWaitingForTerrainColliderRebuild, Is.True);
            Assert.That(
                drop.Valuable.IsCollisionValueLossProtected,
                Is.True);
            Assert.That(drop.Valuable.ApplyCollisionImpulse(100f), Is.Zero);
            Assert.That(drop.Value, Is.EqualTo(100));

            process.Invoke(terrain, null);

            Assert.That(drop.IsWaitingForTerrainColliderRebuild, Is.False);
            Assert.That(
                drop.Valuable.IsCollisionValueLossProtected,
                Is.False);
            Assert.That(drop.Value, Is.EqualTo(100));
        }

        [Test]
        public void MinedOreDrop_EscapeDirectionUsesOnlyActualAirFaces()
        {
            GameObject terrainObject = Create("Terrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            var world = new InfiniteVoxelWorld();
            world.EnsureChunk(Vector2Int.zero).Data.Fill(
                -1f,
                VoxelTypeId.Air);
            typeof(MinecraftCaveInfiniteWorld).GetField(
                "world",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(
                    terrain,
                    world);
            var coordinate = new Vector3Int(10, 10, 10);
            var component = new HashSet<Vector3Int> { coordinate };
            Vector3Int[] neighbours =
            {
                Vector3Int.right, Vector3Int.left, Vector3Int.up,
                Vector3Int.down, Vector3Int.forward, Vector3Int.back,
            };
            for (int i = 0; i < neighbours.Length; i++)
            {
                Vector3Int neighbour = coordinate + neighbours[i];
                world.SetVoxel(
                    neighbour.x,
                    neighbour.y,
                    neighbour.z,
                    1f,
                    VoxelTypeId.Default);
            }
            Vector3Int openNeighbour = coordinate + Vector3Int.left;
            world.SetVoxel(
                openNeighbour.x,
                openNeighbour.y,
                openNeighbour.z,
                -1f,
                VoxelTypeId.Air);
            MethodInfo resolve = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "ResolveOreEscapeDirection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(resolve, Is.Not.Null);

            Vector3 direction = (Vector3)resolve.Invoke(
                terrain,
                new object[] { component });

            Assert.That(direction, Is.EqualTo(Vector3.left));
        }


        [Test]
        public void ExtractionZone_UsesLiveCurrentValueInsteadOfEntrySnapshot()
        {
            GameObject extractionObject = Create("Extraction");
            OreExtractionZone extraction =
                extractionObject.AddComponent<OreExtractionZone>();
            extraction.Configure(null);
            ValuableObject valuable = CreateValuable("Ore", 100, 0.5f);
            Collider resourceCollider = valuable.GetComponent<Collider>();

            // Damage taken before delivery must still be reflected, so the zone
            // cannot cache the authored value at entry time.
            valuable.ApplyCollisionImpulse(3f);
            Assert.That(valuable.CurrentValue, Is.EqualTo(94));

            InvokeTrigger(extraction, "OnTriggerEnter", resourceCollider);

            Assert.That(extraction.CurrentStoredValue, Is.EqualTo(94));
        }

        [Test]
        public void ExtractionZone_ProtectsBankedValueFromFurtherImpacts()
        {
            GameObject extractionObject = Create("Extraction");
            OreExtractionZone extraction =
                extractionObject.AddComponent<OreExtractionZone>();
            extraction.Configure(null);
            ValuableObject valuable = CreateValuable("Ore", 100, 0.5f);
            Collider resourceCollider = valuable.GetComponent<Collider>();

            InvokeTrigger(extraction, "OnTriggerEnter", resourceCollider);
            Assert.That(valuable.ApplyCollisionImpulse(3f), Is.Zero);

            Assert.That(valuable.CurrentValue, Is.EqualTo(100));
            Assert.That(extraction.CurrentStoredValue, Is.EqualTo(100));
        }

        [Test]
        public void WorldValueUi_ShowsGreenCurrentValueAboveObject()
        {
            ValuableObject valuable = CreateValuable("Treasure", 80, 0.5f);
            valuable.ApplyCollisionImpulse(3f);
            ValuableObjectWorldUi worldUi =
                valuable.GetComponent<ValuableObjectWorldUi>();

            Assert.That(worldUi, Is.Not.Null);
            Assert.That(worldUi.WorldCanvas.renderMode,
                Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(worldUi.ValueLabel.text, Is.EqualTo("$75"));
            Assert.That(worldUi.ValueLabel.color.g,
                Is.GreaterThan(worldUi.ValueLabel.color.r));
            Assert.That(worldUi.ValueLabel.fontSharedMaterial.shader.name,
                Is.Not.EqualTo("TextMeshPro/Distance Field Overlay"));
            Assert.That(worldUi.WorldCanvas.transform.position.y,
                Is.GreaterThan(valuable.transform.position.y));
        }

        [Test]
        public void CollisionLossPopup_StartsAtContactAndRisesWhileFading()
        {
            ValuableObject valuable = CreateValuable("Treasure", 100, 0.5f);
            Vector3 collisionPoint = new Vector3(2f, 3f, 4f);

            int loss = valuable.ApplyCollisionImpulse(3f, collisionPoint);
            ValuableObjectWorldUi worldUi =
                valuable.GetComponent<ValuableObjectWorldUi>();
            ValueLossPopup popup = worldUi.LastLossPopup;

            Assert.That(loss, Is.EqualTo(6));
            Assert.That(popup, Is.Not.Null);
            Assert.That(popup.Label.text, Is.EqualTo("-$6"));
            Assert.That(popup.Label.color.r,
                Is.GreaterThan(popup.Label.color.g));
            Assert.That(popup.Label.fontSharedMaterial.shader.name,
                Is.EqualTo("TextMeshPro/Distance Field Overlay"));
            Assert.That(popup.transform.position, Is.EqualTo(collisionPoint));

            float initialY = popup.transform.position.y;
            float initialAlpha = popup.Label.color.a;
            popup.Tick(0.5f);

            Assert.That(popup.transform.position.y, Is.GreaterThan(initialY));
            Assert.That(popup.Label.color.a, Is.LessThan(initialAlpha));
        }

        [Test]
        public void MeshFragmentBuilder_PreservesAllSourceTriangles()
        {
            var source = new Mesh { name = "Octahedron" };
            source.vertices = new[]
            {
                Vector3.up,
                Vector3.down,
                Vector3.left,
                Vector3.right,
                Vector3.forward,
                Vector3.back
            };
            source.triangles = new[]
            {
                0, 4, 3,
                0, 2, 4,
                0, 5, 2,
                0, 3, 5,
                1, 3, 4,
                1, 4, 2,
                1, 2, 5,
                1, 5, 3
            };
            source.RecalculateNormals();

            IReadOnlyList<MeshFragmentBuilder.Fragment> fragments = null;
            try
            {
                fragments = MeshFragmentBuilder.Build(source, 4, 12345);

                Assert.That(fragments.Count, Is.EqualTo(4));
                int triangleCount = 0;
                for (int i = 0; i < fragments.Count; i++)
                {
                    triangleCount += fragments[i].TriangleCount;
                }
                Assert.That(triangleCount, Is.EqualTo(8));
            }
            finally
            {
                if (fragments != null)
                {
                    for (int i = 0; i < fragments.Count; i++)
                    {
                        Object.DestroyImmediate(fragments[i].Mesh);
                    }
                }
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TreasureDefinition_SelectsOneWholeConfiguredVariant()
        {
            TreasureDefinition definition =
                ScriptableObject.CreateInstance<TreasureDefinition>();
            GameObject first = Create("First Variant");
            GameObject second = Create("Second Variant");
            try
            {
                definition.ConfigureFractureVariants(
                    new[] { first, null, second });

                Assert.That(definition.FractureVariants.Count, Is.EqualTo(3));
                Assert.That(definition.GetFractureVariant(0), Is.SameAs(first));
                Assert.That(
                    definition.GetFractureVariant(1),
                    Is.SameAs(second));
                Assert.That(definition.GetFractureVariant(2), Is.SameAs(first));
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void BreakFragmentEffect_GivesEveryPiecePhysicsForFiveSeconds()
        {
            GameObject variant = Create("Fracture Variant");
            for (int i = 0; i < 3; i++)
            {
                GameObject piece =
                    GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = $"Piece {i + 1}";
                piece.transform.SetParent(variant.transform, false);
                piece.transform.localPosition =
                    Vector3.right * (i * 0.2f);
            }

            var context = new ValuableObject.BreakContext(
                new Vector3(2f, 3f, 4f),
                Quaternion.identity,
                Vector3.one,
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(2f, 2.5f, 4f),
                10f,
                6f,
                0,
                123);

            BreakFragmentEffect effect =
                BreakFragmentEffect.SpawnPrefab(variant, context);

            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.FragmentBodies.Count, Is.EqualTo(3));
            for (int i = 0; i < effect.FragmentBodies.Count; i++)
            {
                Assert.That(
                    effect.FragmentBodies[i]
                        .GetComponent<RigidbodyImpactFeedback>(),
                    Is.Not.Null);
            }
            Assert.That(
                effect.FragmentBodies[0].mass,
                Is.EqualTo(2f).Within(0.0001f));
            Assert.That(
                effect.Lifetime,
                Is.EqualTo(5f).Within(0.0001f));

            effect.Tick(4.5f);

            Assert.That(effect.NormalizedAge, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(
                effect.FragmentBodies[0].transform.localScale.x,
                Is.LessThan(1f));
        }

        [Test]
        public void MinedOreBreak_UsesRuntimeMeshFragments()
        {
            GameObject target = Create("Recovered Ore");
            Mesh sourceMesh = CreateOctahedronMesh();
            MeshFilter filter = target.AddComponent<MeshFilter>();
            filter.sharedMesh = sourceMesh;
            target.AddComponent<MeshRenderer>();
            target.AddComponent<Rigidbody>().isKinematic = true;
            target.AddComponent<BoxCollider>();
            MinedOreDrop drop = target.AddComponent<MinedOreDrop>();
            drop.Configure(
                new VoxelTypeId(3),
                4,
                4f,
                sourceMesh,
                1f,
                null,
                10,
                0.25f);
            var context = new ValuableObject.BreakContext(
                Vector3.zero,
                Quaternion.identity,
                Vector3.one,
                Vector3.zero,
                Vector3.zero,
                Vector3.down,
                12f,
                4f,
                0,
                456);

            bool spawned = drop.TrySpawnBreakEffect(context);
            BreakFragmentEffect effect = drop.LastBreakEffect;

            Assert.That(spawned, Is.True);
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.FragmentBodies.Count, Is.EqualTo(5));
        }

        private ValuableObject CreateValuable(
            string objectName,
            int value,
            float fragility)
        {
            GameObject target = Create(objectName);
            target.AddComponent<Rigidbody>().isKinematic = true;
            target.AddComponent<BoxCollider>();
            ValuableObject valuable = target.AddComponent<ValuableObject>();
            valuable.Configure(value, fragility);
            return valuable;
        }

        private GameObject Create(string objectName)
        {
            var gameObject = new GameObject(objectName);
            objects.Add(gameObject);
            return gameObject;
        }

        private static Mesh CreateOctahedronMesh()
        {
            var mesh = new Mesh { name = "Octahedron" };
            mesh.vertices = new[]
            {
                Vector3.up,
                Vector3.down,
                Vector3.left,
                Vector3.right,
                Vector3.forward,
                Vector3.back
            };
            mesh.triangles = new[]
            {
                0, 4, 3,
                0, 2, 4,
                0, 5, 2,
                0, 3, 5,
                1, 3, 4,
                1, 4, 2,
                1, 2, 5,
                1, 5, 3
            };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static void InvokeTrigger(
            object target,
            string methodName,
            Collider collider)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(target, new object[] { collider });
        }

    }
}
