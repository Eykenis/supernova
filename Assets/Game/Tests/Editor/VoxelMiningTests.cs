using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Supernova.Audio;
using Supernova.Effects;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using Supernova.Voxels.Integrity;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class VoxelMiningTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<VoxelTypeDefinition> definitions =
            new List<VoxelTypeDefinition>();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
            for (int i = definitions.Count - 1; i >= 0; i--)
            {
                if (definitions[i] != null) Object.DestroyImmediate(definitions[i]);
            }
            definitions.Clear();
        }

        [Test]
        public void VoxelTypeDefinition_ProvidesNameDurabilityAndMaterialTogether()
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            try
            {
                VoxelTypeDefinition definition =
                    CreateDefinition(7, 4, "Crystal", material);

                Assert.That(definition.TypeId, Is.EqualTo(new VoxelTypeId(7)));
                Assert.That(definition.DisplayName, Is.EqualTo("Crystal"));
                Assert.That(definition.Durability, Is.EqualTo(4));
                Assert.That(definition.Material, Is.SameAs(material));
                Assert.That(
                    VoxelTypeUtility.ResolveMaterialColor(
                        new VoxelTypeId(7),
                        new[] { definition },
                        Color.black),
                    Is.EqualTo(material.GetColor(
                        material.HasProperty("_BaseColor")
                            ? "_BaseColor"
                            : "_Color")));
                Assert.That(
                    VoxelTypeUtility.ResolveDurability(
                        new VoxelTypeId(7),
                        new[] { definition }),
                    Is.EqualTo(4));

                var volume = new VoxelVolume(-1f);
                volume.SetSample(10, 10, 10, 1f, definition.TypeId);
                VoxelMeshData meshData = MarchingCubesMesher.Build(volume);
                Material[] resolved = VoxelTypeUtility.ResolveMaterials(
                    meshData,
                    null,
                    new[] { definition });
                Assert.That(resolved, Has.Length.EqualTo(1));
                Assert.That(resolved[0], Is.SameAs(material));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void MiningProgress_RequiresConfiguredHitCountAndResetsForAnotherType()
        {
            var progress = new VoxelMiningProgress();
            var coordinate = new Vector3Int(3, 4, 5);
            var stone = new VoxelSample(1f, new VoxelTypeId(2));

            Assert.That(progress.TryApplyHit(coordinate, stone, 3, out VoxelMiningResult first), Is.True);
            Assert.That(first.Destroyed, Is.False);
            Assert.That(first.RemainingHits, Is.EqualTo(2));

            progress.TryApplyHit(coordinate, stone, 3, out VoxelMiningResult second);
            Assert.That(second.Destroyed, Is.False);
            Assert.That(second.RemainingHits, Is.EqualTo(1));

            progress.TryApplyHit(coordinate, stone, 3, out VoxelMiningResult third);
            Assert.That(third.Destroyed, Is.True);
            Assert.That(third.RemainingHits, Is.Zero);
            Assert.That(progress.DamagedVoxelCount, Is.Zero);

            var ore = new VoxelSample(1f, new VoxelTypeId(3));
            progress.TryApplyHit(coordinate, stone, 3, out _);
            progress.TryApplyHit(coordinate, ore, 2, out VoxelMiningResult changedType);
            Assert.That(changedType.AccumulatedHits, Is.EqualTo(1));
            Assert.That(changedType.Destroyed, Is.False);
        }

        [Test]
        public void MiningProgress_ReportsDamageBeyondRemainingDurability()
        {
            var progress = new VoxelMiningProgress();
            var coordinate = new Vector3Int(3, 4, 5);
            var stone = new VoxelSample(1f, new VoxelTypeId(2));

            Assert.That(
                progress.TryApplyDamage(
                    coordinate,
                    stone,
                    2,
                    1f,
                    false,
                    out VoxelMiningResult first),
                Is.True);
            Assert.That(first.Destroyed, Is.False);
            Assert.That(first.ExcessDamage, Is.Zero);

            progress.TryApplyDamage(
                coordinate,
                stone,
                2,
                4f,
                false,
                out VoxelMiningResult second);
            Assert.That(second.Destroyed, Is.True);
            Assert.That(second.ExcessDamage, Is.EqualTo(3f));
        }


        [Test]
        public void CrosshairMining_UsesDurabilityAndHonoursProfileReach()
        {
            VoxelTypeCatalog catalog = ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            try
            {
                var type = new VoxelTypeId(2);
                catalog.SetDefinitions(
                    new[] { CreateDefinition(type.Value, 2, "Stone") });

                GameObject terrainObject = Create("Terrain");
                terrainObject.transform.position = Vector3.one * 1000f;
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                SetPrivateField(terrain, "voxelTypeCatalog", catalog);
                terrain.InitializeWorld();
                terrain.World.EnsureChunk(Vector3Int.zero).Data.SetSample(
                    0,
                    0,
                    0,
                    1f,
                    type);

                GameObject target = Create("VoxelCollider");
                target.transform.SetParent(terrainObject.transform, false);
                target.transform.localPosition = Vector3.zero;
                target.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;

                GameObject player = Create("Player");
                GameObject cameraObject = Create("ViewCamera");
                cameraObject.transform.SetParent(player.transform, false);
                cameraObject.transform.position = terrainObject.transform.position + Vector3.back;
                Camera camera = cameraObject.AddComponent<Camera>();
                VoxelPlayerInteractor interactor = player.AddComponent<VoxelPlayerInteractor>();
                Assert.That(player.GetComponent<PlayerProfile>(), Is.Not.Null);
                SetPrivateField(interactor, "viewCamera", camera);
                SetPrivateField(interactor, "terrain", terrain);
                SetPrivateField(interactor, "raycastMask", Physics.DefaultRaycastLayers);
                Physics.SyncTransforms();

                Assert.That(interactor.TryMineAtCrosshair(out VoxelMiningResult first), Is.True);
                Assert.That(first.Destroyed, Is.False);
                Assert.That(terrain.World.GetSampleOrDefault(0, 0, 0).IsSolid(), Is.True);

                Assert.That(interactor.TryMineAtCrosshair(out VoxelMiningResult second), Is.True);
                Assert.That(second.Destroyed, Is.True);
                Assert.That(terrain.World.GetSampleOrDefault(0, 0, 0).IsSolid(), Is.False);

                terrain.World.SetVoxel(0, 0, 0, 1f, type);
                cameraObject.transform.position = terrainObject.transform.position + Vector3.back * 5f;
                Physics.SyncTransforms();
                Assert.That(interactor.TryMineAtCrosshair(out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void ScheduledCrosshairMining_SettlesExpiredHitBeforeSchedulingNextSwing()
        {
            VoxelTypeCatalog catalog = ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            try
            {
                var type = new VoxelTypeId(2);
                catalog.SetDefinitions(
                    new[] { CreateDefinition(type.Value, 1, "Stone") });

                GameObject terrainObject = Create("Terrain");
                terrainObject.transform.position = Vector3.one * 1000f;
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                SetPrivateField(terrain, "voxelTypeCatalog", catalog);
                SetPrivateField(terrain, "generateColliders", true);
                terrain.InitializeWorld();
                InfiniteVoxelChunk chunk =
                    terrain.World.EnsureChunk(Vector3Int.zero);
                chunk.Data.Fill(-1f, VoxelTypeId.Air);
                var firstVoxel = new Vector3Int(8, 8, 8);
                var secondVoxel = new Vector3Int(8, 8, 9);
                chunk.Data.SetSample(
                    firstVoxel.x,
                    firstVoxel.y,
                    firstVoxel.z,
                    1f,
                    type);
                chunk.Data.SetSample(
                    secondVoxel.x,
                    secondVoxel.y,
                    secondVoxel.z,
                    1f,
                    type);
                InvokePrivate(terrain, "RebuildChunk", Vector3Int.zero);

                GameObject player = Create("Player");
                GameObject cameraObject = Create("ViewCamera");
                cameraObject.transform.SetParent(player.transform, false);
                cameraObject.transform.position =
                    terrainObject.transform.TransformPoint(
                        (Vector3)firstVoxel * terrain.VoxelSize
                        + Vector3.back * terrain.VoxelSize * 3f);
                Camera camera = cameraObject.AddComponent<Camera>();
                VoxelPlayerInteractor interactor =
                    player.AddComponent<VoxelPlayerInteractor>();
                VoxelMiningImpactEffect impactEffect =
                    player.AddComponent<VoxelMiningImpactEffect>();
                SetPrivateField(interactor, "viewCamera", camera);
                SetPrivateField(interactor, "terrain", terrain);
                SetPrivateField(
                    interactor,
                    "miningImpactEffect",
                    impactEffect);
                SetPrivateField(
                    interactor,
                    "raycastMask",
                    Physics.DefaultRaycastLayers);
                Physics.SyncTransforms();

                Assert.That(interactor.TryScheduleMineAtCrosshair(1f), Is.True);
                SetPrivateField(interactor, "pendingMineTime", Time.time - 1f);

                Assert.That(interactor.TryScheduleMineAtCrosshair(1f), Is.True);
                Assert.That(
                    impactEffect.ActiveParticleCount,
                    Is.GreaterThan(0),
                    "The settled brush hit should emit mining particles.");
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        firstVoxel.x,
                        firstVoxel.y,
                        firstVoxel.z).IsSolid(),
                    Is.False);
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        secondVoxel.x,
                        secondVoxel.y,
                        secondVoxel.z).IsSolid(),
                    Is.True);
                Assert.That(
                    GetPrivateField<Vector3Int>(interactor, "pendingMineVoxel"),
                    Is.EqualTo(secondVoxel));

                SetPrivateField(interactor, "pendingMineTime", Time.time - 1f);
                Assert.That(interactor.TryScheduleMineAtCrosshair(1f), Is.False);
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        secondVoxel.x,
                        secondVoxel.y,
                        secondVoxel.z).IsSolid(),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void MiningDestruction_QueuesPriorityMeshWithoutBuildingSynchronously()
        {
            VoxelTypeCatalog catalog =
                ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            try
            {
                var stone = new VoxelTypeId(2);
                catalog.SetDefinitions(
                    new[] { CreateDefinition(stone.Value, 1, "Stone") });

                GameObject terrainObject = Create("Queued Mining Terrain");
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                SetPrivateField(terrain, "voxelTypeCatalog", catalog);
                terrain.InitializeWorld();
                SetPrivateField(
                    terrain,
                    "usesExternalWorldRendering",
                    true);
                InfiniteVoxelChunk chunk =
                    terrain.World.EnsureChunk(Vector3Int.zero);
                chunk.Data.Fill(-1f, VoxelTypeId.Air);
                var minedVoxel = new Vector3Int(8, 8, 8);
                chunk.Data.SetSample(
                    minedVoxel.x,
                    minedVoxel.y,
                    minedVoxel.z,
                    1f,
                    stone);

                Assert.That(
                    terrain.TryMineVoxel(minedVoxel, out VoxelMiningResult result),
                    Is.True);
                Assert.That(result.Destroyed, Is.True);

                Queue<Vector3Int> priorityQueue =
                    GetPrivateField<Queue<Vector3Int>>(
                        terrain,
                        "priorityMeshQueue");
                HashSet<Vector3Int> priorityDirtyMeshes =
                    GetPrivateField<HashSet<Vector3Int>>(
                        terrain,
                        "priorityDirtyMeshes");
                Dictionary<Vector3Int, GameObject> chunkObjects =
                    GetPrivateField<Dictionary<Vector3Int, GameObject>>(
                        terrain,
                        "chunkObjects");

                Assert.That(priorityQueue.Count, Is.EqualTo(1));
                Assert.That(priorityQueue.Peek(), Is.EqualTo(Vector3Int.zero));
                Assert.That(priorityDirtyMeshes, Does.Contain(Vector3Int.zero));
                var externalDirtyMeshes = new HashSet<Vector3Int>();
                Assert.That(
                    terrain.CollectAdoptedWorldDirtyMeshes(
                        externalDirtyMeshes),
                    Is.EqualTo(1));
                Assert.That(
                    externalDirtyMeshes,
                    Is.EquivalentTo(new[] { Vector3Int.zero }));

                terrain.CompleteAdoptedWorldMeshRebuild();
                externalDirtyMeshes.Clear();
                Assert.That(
                    terrain.CollectAdoptedWorldDirtyMeshes(
                        externalDirtyMeshes),
                    Is.Zero);
                Assert.That(externalDirtyMeshes, Is.Empty);
                Assert.That(
                    chunkObjects,
                    Is.Empty,
                    "Mining must not build or apply a mesh in the interaction call.");
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void MiningAtChunkAndSectionBoundary_ReportsAffectedAdoptedSections()
        {
            VoxelTypeCatalog catalog =
                ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            try
            {
                var stone = new VoxelTypeId(2);
                catalog.SetDefinitions(
                    new[] { CreateDefinition(stone.Value, 1, "Stone") });

                GameObject terrainObject = Create("Boundary Mining Terrain");
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                SetPrivateField(terrain, "voxelTypeCatalog", catalog);
                terrain.InitializeWorld();
                SetPrivateField(
                    terrain,
                    "usesExternalWorldRendering",
                    true);

                Vector2Int[] columns =
                {
                    new Vector2Int(-1, -1),
                    new Vector2Int(0, -1),
                    new Vector2Int(-1, 0),
                    Vector2Int.zero,
                };
                for (int i = 0; i < columns.Length; i++)
                {
                    terrain.World.EnsureChunk(columns[i]).Data.Fill(
                        -1f,
                        VoxelTypeId.Air);
                }

                var minedVoxel = new Vector3Int(0, 32, 0);
                terrain.World.SetVoxel(
                    minedVoxel.x,
                    minedVoxel.y,
                    minedVoxel.z,
                    1f,
                    stone);

                Assert.That(
                    terrain.TryMineVoxel(
                        minedVoxel,
                        out VoxelMiningResult result),
                    Is.True);
                Assert.That(result.Destroyed, Is.True);

                var affected = new HashSet<Vector3Int>();
                Assert.That(
                    terrain.CollectAdoptedWorldDirtyMeshes(affected),
                    Is.EqualTo(8));
                Assert.That(
                    affected,
                    Is.EquivalentTo(new[]
                    {
                        new Vector3Int(-1, 0, -1),
                        new Vector3Int(0, 0, -1),
                        new Vector3Int(-1, 0, 0),
                        new Vector3Int(0, 0, 0),
                        new Vector3Int(-1, 1, -1),
                        new Vector3Int(0, 1, -1),
                        new Vector3Int(-1, 1, 0),
                        new Vector3Int(0, 1, 0),
                    }));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void PickaxeMiningSound_PlaysAtSuccessfulHitPositionOnly()
        {
            GameObject player = Create("Player");
            GameObject cameraObject = Create("ViewCamera");
            cameraObject.transform.SetParent(player.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            VoxelPlayerInteractor interactor =
                player.AddComponent<VoxelPlayerInteractor>();
            SetPrivateField(interactor, "viewCamera", camera);
            SetPrivateField(
                interactor,
                "raycastMask",
                Physics.DefaultRaycastLayers);

            SolidVoxelPrototype platform = SolidVoxelPrototype.Create(
                Vector3.forward * 2f,
                3,
                1f,
                0.5f,
                0f,
                null);
            objects.Add(platform.gameObject);
            Physics.SyncTransforms();

            SoundEffectCue cue =
                ScriptableObject.CreateInstance<SoundEffectCue>();
            int soundRequestCount = 0;
            SoundEffectPlaybackRequest received = default;
            System.Action<SoundEffectPlaybackRequest> observer = request =>
            {
                received = request;
                soundRequestCount++;
            };
            SoundEffectEvents.PlaybackRequested += observer;
            try
            {
                Assert.That(
                    interactor.TryScheduleMineAtCrosshair(
                        0f,
                        VoxelMiningBrushSettings.SingleVoxel,
                        cue),
                    Is.True);
                Assert.That(soundRequestCount, Is.EqualTo(1));
                Assert.That(received.Cue, Is.SameAs(cue));
                Assert.That(
                    received.Position.z,
                    Is.GreaterThan(player.transform.position.z));

                Physics.SyncTransforms();
                Assert.That(
                    interactor.TryScheduleMineAtCrosshair(
                        0f,
                        VoxelMiningBrushSettings.SingleVoxel,
                        cue),
                    Is.False);
                Assert.That(soundRequestCount, Is.EqualTo(1));
            }
            finally
            {
                SoundEffectEvents.PlaybackRequested -= observer;
                Object.DestroyImmediate(cue);
            }
        }

        [Test]
        public void RebuildingNonEmptySection_DoubleBuffersMeshAndReusesComponents()
        {
            GameObject terrainObject = Create("Reusable Chunk Terrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            SetPrivateField(terrain, "generateColliders", true);
            terrain.InitializeWorld();
            InfiniteVoxelChunk chunk = terrain.World.EnsureChunk(Vector3Int.zero);
            chunk.Data.Fill(-1f, VoxelTypeId.Air);
            chunk.Data.SetSample(8, 8, 8, 1f, VoxelTypeId.Default);

            InvokePrivate(terrain, "RebuildChunk", Vector3Int.zero);
            InvokePrivate(
                terrain,
                "ProcessPendingMeshPostProcesses",
                1,
                1000f);
            Dictionary<Vector3Int, GameObject> chunkObjects =
                GetPrivateField<Dictionary<Vector3Int, GameObject>>(
                    terrain,
                    "chunkObjects");
            Dictionary<Vector3Int, Mesh> chunkMeshes =
                GetPrivateField<Dictionary<Vector3Int, Mesh>>(
                    terrain,
                    "chunkMeshes");
            GameObject firstObject = chunkObjects[Vector3Int.zero];
            Mesh firstMesh = chunkMeshes[Vector3Int.zero];
            MeshFilter firstFilter = firstObject.GetComponent<MeshFilter>();
            MeshRenderer firstRenderer = firstObject.GetComponent<MeshRenderer>();
            MeshCollider firstCollider = firstObject.GetComponent<MeshCollider>();
            Assert.That(firstCollider, Is.Not.Null);
            Assert.That(firstCollider.sharedMesh, Is.SameAs(firstMesh));

            chunk.Data.SetSample(9, 8, 8, 1f, VoxelTypeId.Default);
            InvokePrivate(terrain, "RebuildChunk", Vector3Int.zero);
            Mesh secondMesh = chunkMeshes[Vector3Int.zero];

            Assert.That(chunkObjects[Vector3Int.zero], Is.SameAs(firstObject));
            Assert.That(secondMesh, Is.Not.SameAs(firstMesh));
            Assert.That(
                firstObject.GetComponent<MeshFilter>(),
                Is.SameAs(firstFilter));
            Assert.That(
                firstObject.GetComponent<MeshRenderer>(),
                Is.SameAs(firstRenderer));
            Assert.That(
                firstObject.GetComponent<MeshCollider>(),
                Is.SameAs(firstCollider));
            Assert.That(firstFilter.sharedMesh, Is.SameAs(secondMesh));
            Assert.That(
                firstCollider.sharedMesh,
                Is.SameAs(firstMesh),
                "The old collider mesh must remain attached while the new mesh "
                + "waits for deferred PhysX cooking.");

            InvokePrivate(
                terrain,
                "ProcessPendingMeshPostProcesses",
                1,
                1000f);

            Assert.That(firstCollider.sharedMesh, Is.SameAs(secondMesh));
            Assert.That(terrain.PooledChunkMeshCount, Is.EqualTo(1));
        }

        [Test]
        public void OreRelease_WaitsForEveryAffectedTerrainMesh()
        {
            GameObject terrainObject = Create("Ore Release Terrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            GameObject dropObject = Create("Recovered Ore");
            Rigidbody body = dropObject.AddComponent<Rigidbody>();
            dropObject.AddComponent<BoxCollider>();
            MinedOreDrop drop = dropObject.AddComponent<MinedOreDrop>();
            drop.Configure(
                new VoxelTypeId(3),
                1,
                1f,
                new Mesh(),
                1f,
                null,
                100,
                0.5f);
            var affectedMeshes = new HashSet<Vector3Int>
            {
                Vector3Int.zero,
                Vector3Int.right,
            };

            InvokePrivate(
                terrain,
                "RegisterOreTerrainRelease",
                drop,
                affectedMeshes);

            Assert.That(drop.IsWaitingForTerrainColliderRebuild, Is.True);
            Assert.That(body.isKinematic, Is.True);
            InvokePrivate(
                terrain,
                "NotifyOreTerrainMeshRebuilt",
                Vector3Int.zero);
            Assert.That(
                drop.IsWaitingForTerrainColliderRebuild,
                Is.True,
                "One committed section must not release an ore spanning two.");

            InvokePrivate(
                terrain,
                "NotifyOreTerrainMeshRebuilt",
                Vector3Int.right);

            Assert.That(
                drop.IsWaitingForTerrainColliderRebuild,
                Is.True,
                "Collider assignment alone must not release collision protection.");
            Assert.That(
                drop.Valuable.IsCollisionValueLossProtected,
                Is.True);

            InvokePrivate(terrain, "ProcessPendingColumnPhysics");

            Assert.That(drop.IsWaitingForTerrainColliderRebuild, Is.False);
            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.detectCollisions, Is.True);
        }

        [Test]
        public void CrosshairMining_DoesNotSearchPastTheHitSurfaceCell()
        {
            GameObject terrainObject = Create("Terrain");
            terrainObject.transform.position = Vector3.one * 1000f;
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            terrain.InitializeWorld();
            InfiniteVoxelChunk chunk =
                terrain.World.EnsureChunk(Vector3Int.zero);
            chunk.Data.Fill(-1f, VoxelTypeId.Air);
            var rearVoxel = new Vector3Int(0, 0, 1);
            chunk.Data.SetSample(
                rearVoxel.x,
                rearVoxel.y,
                rearVoxel.z,
                1f,
                VoxelTypeId.Default);

            // This collider represents a stale/incorrect visible surface around z=0.
            // The only solid sample is one full voxel behind its surface cell.
            GameObject target = Create("StaleVoxelCollider");
            target.transform.SetParent(terrainObject.transform, false);
            target.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;

            GameObject player = Create("Player");
            GameObject cameraObject = Create("ViewCamera");
            cameraObject.transform.SetParent(player.transform, false);
            cameraObject.transform.position =
                terrainObject.transform.position + Vector3.back;
            Camera camera = cameraObject.AddComponent<Camera>();
            VoxelPlayerInteractor interactor =
                player.AddComponent<VoxelPlayerInteractor>();
            SetPrivateField(interactor, "viewCamera", camera);
            SetPrivateField(interactor, "terrain", terrain);
            SetPrivateField(
                interactor,
                "raycastMask",
                Physics.DefaultRaycastLayers);
            Physics.SyncTransforms();

            Assert.That(interactor.TryMineAtCrosshair(out _), Is.False);
            Assert.That(
                terrain.World.GetSampleOrDefault(
                    rearVoxel.x,
                    rearVoxel.y,
                    rearVoxel.z).IsSolid(),
                Is.True);
        }

        [Test]
        public void MinecraftWorld_MinesTypedVoxelUsingTheSameDurabilityDefinition()
        {
            VoxelTypeCatalog catalog = ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            catalog.SetDefinitions(
                new[] { CreateDefinition(6, 2, "Test Ore") });
            GameObject terrainObject = Create("MinecraftTerrain");
            MinecraftCaveInfiniteWorld terrain =
                terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
            SetPrivateField(terrain, "voxelTypeCatalog", catalog);
            terrain.InitializeWorld();
            var coordinate = new Vector3Int(2, 3, 4);
            var type = new VoxelTypeId(6);
            terrain.World.EnsureChunk(Vector3Int.zero).Data.SetSample(
                coordinate.x,
                coordinate.y,
                coordinate.z,
                1f,
                type);
            Assert.That(terrain.TryMineVoxel(coordinate, out VoxelMiningResult first), Is.True);
            Assert.That(first.Destroyed, Is.False);
            Assert.That(terrain.TryMineVoxel(coordinate, out VoxelMiningResult second), Is.True);
            Assert.That(second.Destroyed, Is.True);
            Assert.That(terrain.World.GetSampleOrDefault(2, 3, 4).IsSolid(), Is.False);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void MiningOre_HarvestsWholeConnectedVeinAndPreservesMesh()
        {
            VoxelTypeCatalog catalog =
                ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            VoxelOreFeatureDefinition feature =
                ScriptableObject.CreateInstance<VoxelOreFeatureDefinition>();
            try
            {
                VoxelTypeDefinition stone =
                    CreateDefinition(2, 1, "Stone");
                VoxelTypeDefinition ore =
                    CreateDefinition(3, 1, "Test Ore");
                catalog.SetDefinitions(new[] { stone, ore });
                feature.Configure(
                    ore,
                    new[] { stone },
                    3109,
                    1,
                    1f,
                    MinecraftOreFeatureSettings.HeightDistribution.Uniform,
                    -8,
                    8,
                    0,
                    4,
                    0f,
                    2.5f,
                    0.25f,
                    37);

                GameObject terrainObject = Create("Ore Drop Terrain");
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                SetPrivateField(terrain, "voxelTypeCatalog", catalog);
                SetPrivateField(
                    terrain,
                    "oreFeatures",
                    new List<VoxelOreFeatureDefinition> { feature });
                terrain.InitializeWorld();

                InfiniteVoxelChunk leftChunk =
                    terrain.World.EnsureChunk(Vector3Int.zero);
                InfiniteVoxelChunk rightChunk =
                    terrain.World.EnsureChunk(Vector3Int.right);
                leftChunk.Data.Fill(1f, stone.TypeId);
                rightChunk.Data.Fill(1f, stone.TypeId);

                var first = new Vector3Int(31, 5, 6);
                var second = new Vector3Int(32, 5, 6);
                var diagonal = new Vector3Int(33, 6, 7);
                var disconnected = new Vector3Int(36, 5, 6);
                var component = new HashSet<Vector3Int>
                {
                    first,
                    second,
                    diagonal,
                };
                foreach (Vector3Int coordinate in component)
                {
                    terrain.World.SetVoxel(
                        coordinate.x,
                        coordinate.y,
                        coordinate.z,
                        1f,
                        ore.TypeId);
                }
                terrain.World.SetVoxel(
                    disconnected.x,
                    disconnected.y,
                    disconnected.z,
                    1f,
                    ore.TypeId);

                VoxelMeshData expectedMeshData =
                    MarchingCubesMesher.BuildTypeComponent(
                        terrain.World,
                        component,
                        ore.TypeId,
                        terrain.IsoLevel,
                        terrain.VoxelSize,
                        MarchingCubesVertexPlacement.DensityInterpolated);
                Vector3[] expectedVertices =
                    expectedMeshData.Vertices.ToArray();
                int[] expectedTriangles =
                    expectedMeshData.Triangles.ToArray();
                VoxelMeshMassProperties expectedMassProperties =
                    VoxelIntegrityRigidbodyFactory.CalculateMassProperties(
                        expectedVertices,
                        expectedTriangles);
                float expectedRepresentedVolume =
                    VoxelIntegrityRigidbodyFactory
                        .CalculateRepresentedFullVoxelVolume(
                            expectedMassProperties,
                            terrain.VoxelSize,
                            Vector3.one
                                * MinedOreDrop.RecoveredLinearScale);

                Assert.That(
                    terrain.TryMineVoxel(
                        first,
                        out VoxelMiningResult result),
                    Is.True);
                Assert.That(result.Destroyed, Is.True);
                Assert.That(terrain.ActiveOreDrops, Has.Count.EqualTo(1));

                MinedOreDrop drop = terrain.ActiveOreDrops[0];
                Assert.That(drop, Is.Not.Null);
                Assert.That(
                    drop.transform.lossyScale,
                    Is.EqualTo(terrain.transform.lossyScale));
                Assert.That(
                    drop.transform.rotation,
                    Is.EqualTo(terrain.transform.rotation));
                Assert.That(drop.VoxelType, Is.EqualTo(ore.TypeId));
                Assert.That(drop.VoxelCount, Is.EqualTo(component.Count));
                Assert.That(
                    drop.RepresentedFullVoxelVolume,
                    Is.EqualTo(expectedRepresentedVolume).Within(0.0001f));
                Assert.That(drop.MassDensity, Is.EqualTo(2.5f));
                Assert.That(
                    drop.Value,
                    Is.EqualTo(
                        MinedOreDrop.CalculateInitialValue(
                            expectedRepresentedVolume,
                            37)));
                Assert.That(drop.Mesh, Is.Not.Null);
                Assert.That(
                    drop.Mesh.vertexCount,
                    Is.GreaterThan(8),
                    "The vein must keep its Marching Cubes mesh, not become a Cube.");
                Assert.That(drop.Body, Is.Not.Null);
                Assert.That(
                    drop.Body.mass,
                    Is.EqualTo(2.5f * expectedRepresentedVolume)
                        .Within(0.001f));
                Assert.That(drop.Body.isKinematic, Is.False);
                Assert.That(drop.GetComponent<Collider>(), Is.Not.Null);
                Assert.That(
                    drop.GetComponent<MeshRenderer>().shadowCastingMode,
                    Is.EqualTo(
                        UnityEngine.Rendering.ShadowCastingMode.On));
                CollectionAssert.AreEqual(
                    expectedTriangles,
                    drop.Mesh.triangles);
                Vector3[] actualVertices = drop.Mesh.vertices;
                Assert.That(
                    actualVertices,
                    Has.Length.EqualTo(expectedVertices.Length));
                for (int i = 0; i < actualVertices.Length; i++)
                {
                    Vector3 actualTerrainLocal =
                        terrain.transform.InverseTransformPoint(
                            drop.transform.TransformPoint(actualVertices[i]));
                    Assert.That(
                        Vector3.Distance(
                            actualTerrainLocal,
                            expectedVertices[i]),
                        Is.LessThan(0.0001f),
                        $"Extracted mesh vertex {i} changed.");
                }
                foreach (Vector3Int coordinate in component)
                {
                    Assert.That(
                        terrain.World.GetSampleOrDefault(
                            coordinate.x,
                            coordinate.y,
                            coordinate.z).IsSolid(),
                        Is.False);
                }
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        disconnected.x,
                        disconnected.y,
                        disconnected.z).Type,
                    Is.EqualTo(ore.TypeId),
                    "A disconnected vein of the same type must remain.");

                var stoneCoordinate = new Vector3Int(7, 5, 6);
                terrain.World.SetVoxel(
                    stoneCoordinate.x,
                    stoneCoordinate.y,
                    stoneCoordinate.z,
                    1f,
                    stone.TypeId);
                Assert.That(
                    terrain.TryMineVoxel(stoneCoordinate, out _),
                    Is.True);
                Assert.That(
                    terrain.ActiveOreDrops,
                    Has.Count.EqualTo(1),
                    "Base stone should still disappear without becoming a drop.");
            }
            finally
            {
                Object.DestroyImmediate(feature);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void RecoveredOreMaterial_PreservesTextureStackAndUsesSubduedLighting()
        {
            Shader shader = Shader.Find(
                "Supernova/Lighting/Soft Falloff Lit");
            Assert.That(shader, Is.Not.Null);

            var source = new Material(shader);
            var albedo = new Texture2D(2, 2);
            var normal = new Texture2D(2, 2);
            var metallic = new Texture2D(2, 2);
            var height = new Texture2D(2, 2);
            Material recovered = null;
            GameObject owner = null;
            try
            {
                source.SetTexture("_BaseMap", albedo);
                source.SetTexture("_MainTex", albedo);
                source.SetTexture("_BumpMap", normal);
                source.SetTexture("_MetallicGlossMap", metallic);
                source.SetTexture("_ParallaxMap", height);
                var sourceColor = new Color(0.9f, 0.7f, 0.5f, 1f);
                source.SetColor("_BaseColor", sourceColor);
                source.SetColor("_Color", sourceColor);
                source.SetFloat("_Metallic", 0.72f);
                source.SetFloat("_Smoothness", 0.62f);
                source.EnableKeyword("_EMISSION");
                source.SetColor("_EmissionColor", Color.white);

                MethodInfo createMaterial =
                    typeof(MinecraftCaveInfiniteWorld).GetMethod(
                        "CreateRecoveredOreMaterial",
                        BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(createMaterial, Is.Not.Null);
                recovered = (Material)createMaterial.Invoke(
                    null,
                    new object[] { source, "Test Ore" });

                Assert.That(recovered, Is.Not.Null);
                Assert.That(recovered.GetTexture("_BaseMap"), Is.SameAs(albedo));
                Assert.That(recovered.GetTexture("_MainTex"), Is.SameAs(albedo));
                Assert.That(recovered.GetTexture("_BumpMap"), Is.SameAs(normal));
                Assert.That(
                    recovered.GetTexture("_MetallicGlossMap"),
                    Is.SameAs(metallic));
                Assert.That(
                    recovered.GetTexture("_ParallaxMap"),
                    Is.SameAs(height));
                Color recoveredColor = recovered.GetColor("_BaseColor");
                Assert.That(
                    recoveredColor.r,
                    Is.EqualTo(sourceColor.r * 0.82f).Within(0.0001f));
                Assert.That(
                    recoveredColor.g,
                    Is.EqualTo(sourceColor.g * 0.82f).Within(0.0001f));
                Assert.That(
                    recoveredColor.b,
                    Is.EqualTo(sourceColor.b * 0.82f).Within(0.0001f));
                Assert.That(recovered.GetFloat("_Metallic"), Is.EqualTo(0.35f));
                Assert.That(recovered.GetFloat("_Smoothness"), Is.EqualTo(0.4f));
                Assert.That(recovered.IsKeywordEnabled("_EMISSION"), Is.False);
                Assert.That(
                    recovered.GetColor("_EmissionColor").maxColorComponent,
                    Is.EqualTo(0f));

                owner = new GameObject("Recovered Ore Material Owner");
                MinedOreDrop drop = owner.AddComponent<MinedOreDrop>();
                drop.Configure(
                    new VoxelTypeId(3),
                    1,
                    1f,
                    null,
                    1f,
                    recovered);
                Object.DestroyImmediate(owner);
                owner = null;
                recovered = null;

                Assert.That(
                    albedo,
                    Is.Not.Null,
                    "Destroying a recovered material must not destroy its shared source textures.");
            }
            finally
            {
                if (owner != null) Object.DestroyImmediate(owner);
                if (recovered != null) Object.DestroyImmediate(recovered);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(albedo);
                Object.DestroyImmediate(normal);
                Object.DestroyImmediate(metallic);
                Object.DestroyImmediate(height);
            }
        }

        [Test]
        public void RecoveredOreTemplate_KeepsEffectsButUsesSourceBaseMap()
        {
            Shader shader = Shader.Find(
                "Supernova/Lighting/Soft Falloff Lit");
            Assert.That(shader, Is.Not.Null);

            var template = new Material(shader);
            var source = new Material(shader);
            var templateTexture = new Texture2D(2, 2);
            var sourceTexture = new Texture2D(2, 2);
            Material recovered = null;
            try
            {
                template.SetTexture("_BaseMap", templateTexture);
                template.SetTexture("_MainTex", templateTexture);
                var effectColor = new Color(0.2f, 0.8f, 1f, 1f);
                template.SetColor("_BaseColor", effectColor);
                template.SetColor("_Color", effectColor);
                template.SetFloat("_Metallic", 0.18f);
                template.SetFloat("_Smoothness", 0.77f);

                source.SetTexture("_BaseMap", sourceTexture);
                source.SetTexture("_MainTex", sourceTexture);
                var scale = new Vector2(1.5f, 0.75f);
                var offset = new Vector2(0.2f, 0.35f);
                source.SetTextureScale("_BaseMap", scale);
                source.SetTextureOffset("_BaseMap", offset);
                source.SetTextureScale("_MainTex", scale);
                source.SetTextureOffset("_MainTex", offset);

                MethodInfo cloneMaterial =
                    typeof(MinecraftCaveInfiniteWorld).GetMethod(
                        "CloneRecoveredOreMaterial",
                        BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(cloneMaterial, Is.Not.Null);
                recovered = (Material)cloneMaterial.Invoke(
                    null,
                    new object[] { template, source, "Test Ore" });

                Assert.That(recovered, Is.Not.Null);
                Assert.That(recovered, Is.Not.SameAs(template));
                Assert.That(recovered.shader, Is.SameAs(template.shader));
                Assert.That(
                    recovered.GetTexture("_BaseMap"),
                    Is.SameAs(sourceTexture));
                Assert.That(
                    recovered.GetTexture("_MainTex"),
                    Is.SameAs(sourceTexture));
                Assert.That(
                    recovered.GetTexture("_BaseMap"),
                    Is.Not.SameAs(templateTexture));
                Assert.That(
                    recovered.GetTextureScale("_BaseMap"),
                    Is.EqualTo(scale));
                Assert.That(
                    recovered.GetTextureOffset("_BaseMap"),
                    Is.EqualTo(offset));
                Color recoveredColor = recovered.GetColor("_BaseColor");
                Assert.That(
                    recoveredColor.r,
                    Is.EqualTo(effectColor.r).Within(0.0001f));
                Assert.That(
                    recoveredColor.g,
                    Is.EqualTo(effectColor.g).Within(0.0001f));
                Assert.That(
                    recoveredColor.b,
                    Is.EqualTo(effectColor.b).Within(0.0001f));
                Assert.That(
                    recoveredColor.a,
                    Is.EqualTo(effectColor.a).Within(0.0001f));
                Assert.That(
                    recovered.GetFloat("_Metallic"),
                    Is.EqualTo(0.18f));
                Assert.That(
                    recovered.GetFloat("_Smoothness"),
                    Is.EqualTo(0.77f));
            }
            finally
            {
                if (recovered != null) Object.DestroyImmediate(recovered);
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(templateTexture);
                Object.DestroyImmediate(sourceTexture);
            }
        }

        [Test]
        public void MiningPropagation_UsesAll26DirectionsAndDoesNotReturn()
        {
            VoxelTypeCatalog catalog = ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            try
            {
                var stone = new VoxelTypeId(2);
                catalog.SetDefinitions(
                    new[] { CreateDefinition(stone.Value, 1, "Stone") });

                GameObject terrainObject = Create("Mining Propagation Terrain");
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                SetPrivateField(terrain, "voxelTypeCatalog", catalog);
                terrain.InitializeWorld();
                InfiniteVoxelChunk chunk =
                    terrain.World.EnsureChunk(Vector3Int.zero);
                chunk.Data.Fill(-1f, VoxelTypeId.Air);

                var primary = new Vector3Int(8, 8, 8);
                for (int z = -2; z <= 2; z++)
                {
                    for (int y = -2; y <= 2; y++)
                    {
                        for (int x = -2; x <= 2; x++)
                        {
                            Vector3Int coordinate =
                                primary + new Vector3Int(x, y, z);
                            chunk.Data.SetSample(
                                coordinate.x,
                                coordinate.y,
                                coordinate.z,
                                1f,
                                stone);
                        }
                    }
                }

                var brush = new VoxelMiningBrushSettings(
                    4f,
                    terrain.VoxelSize,
                    terrain.VoxelSize,
                    1f,
                    1f,
                    128,
                    2f);

                Assert.That(
                    terrain.TryMineBrush(
                        primary,
                        Vector3.forward,
                        brush,
                        out VoxelMiningBrushResult result),
                    Is.True);

                Assert.That(result.TargetType, Is.EqualTo(stone));
                Assert.That(result.CandidateCount, Is.EqualTo(125));
                Assert.That(result.DamagedCount, Is.EqualTo(125));
                Assert.That(result.DestroyedCount, Is.EqualTo(27));
                Assert.That(result.PrimaryDestroyed, Is.True);

                Vector3Int faceNeighbour = primary + Vector3Int.right;
                Vector3Int edgeNeighbour = primary
                    + new Vector3Int(1, 1, 0);
                Vector3Int cornerNeighbour = primary
                    + new Vector3Int(1, 1, 1);
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        faceNeighbour.x,
                        faceNeighbour.y,
                        faceNeighbour.z).IsSolid(),
                    Is.False);
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        edgeNeighbour.x,
                        edgeNeighbour.y,
                        edgeNeighbour.z).IsSolid(),
                    Is.False);
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        cornerNeighbour.x,
                        cornerNeighbour.y,
                        cornerNeighbour.z).IsSolid(),
                    Is.False);

                Vector3Int secondLayer = primary + Vector3Int.right * 2;
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        secondLayer.x,
                        secondLayer.y,
                        secondLayer.z).IsSolid(),
                    Is.True,
                    "0.25 propagated damage must stop without destroying or returning.");
                Assert.That(
                    GetPrivateField<VoxelMiningProgress>(
                        terrain,
                        "miningProgress").DamagedVoxelCount,
                    Is.EqualTo(98));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void MiningPropagation_DoesNotCrossVoxelTypesInEitherDirection()
        {
            VoxelTypeCatalog catalog =
                ScriptableObject.CreateInstance<VoxelTypeCatalog>();
            try
            {
                var stone = new VoxelTypeId(2);
                var ore = new VoxelTypeId(3);
                catalog.SetDefinitions(
                    new[]
                    {
                        CreateDefinition(stone.Value, 1, "Stone"),
                        CreateDefinition(ore.Value, 1, "Ore"),
                    });

                GameObject terrainObject =
                    Create("Typed Mining Propagation Terrain");
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                SetPrivateField(terrain, "voxelTypeCatalog", catalog);
                terrain.InitializeWorld();
                InfiniteVoxelChunk chunk =
                    terrain.World.EnsureChunk(Vector3Int.zero);
                chunk.Data.Fill(-1f, VoxelTypeId.Air);

                var brush = new VoxelMiningBrushSettings(
                    4f,
                    terrain.VoxelSize,
                    terrain.VoxelSize,
                    1f,
                    1f,
                    128,
                    2f);

                var stonePrimary = new Vector3Int(8, 8, 8);
                Vector3Int oreBarrier = stonePrimary + Vector3Int.right;
                Vector3Int stoneBehindOre = oreBarrier + Vector3Int.right;
                chunk.Data.SetSample(
                    stonePrimary.x,
                    stonePrimary.y,
                    stonePrimary.z,
                    1f,
                    stone);
                chunk.Data.SetSample(
                    oreBarrier.x,
                    oreBarrier.y,
                    oreBarrier.z,
                    1f,
                    ore);
                chunk.Data.SetSample(
                    stoneBehindOre.x,
                    stoneBehindOre.y,
                    stoneBehindOre.z,
                    1f,
                    stone);

                Assert.That(
                    terrain.TryMineBrush(
                        stonePrimary,
                        Vector3.right,
                        brush,
                        out VoxelMiningBrushResult stoneResult),
                    Is.True);
                Assert.That(stoneResult.DestroyedCount, Is.EqualTo(1));
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        oreBarrier.x,
                        oreBarrier.y,
                        oreBarrier.z).IsSolid(),
                    Is.True,
                    "Stone propagation must not damage ore.");
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        stoneBehindOre.x,
                        stoneBehindOre.y,
                        stoneBehindOre.z).IsSolid(),
                    Is.True,
                    "Propagation must not pass through an ore barrier.");

                var orePrimary = new Vector3Int(16, 8, 8);
                Vector3Int stoneBarrier = orePrimary + Vector3Int.right;
                Vector3Int oreBehindStone = stoneBarrier + Vector3Int.right;
                chunk.Data.SetSample(
                    orePrimary.x,
                    orePrimary.y,
                    orePrimary.z,
                    1f,
                    ore);
                chunk.Data.SetSample(
                    stoneBarrier.x,
                    stoneBarrier.y,
                    stoneBarrier.z,
                    1f,
                    stone);
                chunk.Data.SetSample(
                    oreBehindStone.x,
                    oreBehindStone.y,
                    oreBehindStone.z,
                    1f,
                    ore);

                Assert.That(
                    terrain.TryMineBrush(
                        orePrimary,
                        Vector3.right,
                        brush,
                        out VoxelMiningBrushResult oreResult),
                    Is.True);
                Assert.That(oreResult.DestroyedCount, Is.EqualTo(1));
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        stoneBarrier.x,
                        stoneBarrier.y,
                        stoneBarrier.z).IsSolid(),
                    Is.True,
                    "Ore propagation must not damage stone.");
                Assert.That(
                    terrain.World.GetSampleOrDefault(
                        oreBehindStone.x,
                        oreBehindStone.y,
                        oreBehindStone.z).IsSolid(),
                    Is.True,
                    "Propagation must not pass through a stone barrier.");
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void PickaxeAsset_ConfiguresAlternatingDamageAndPropagation()
        {
            PlayerToolDefinition pickaxe =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.PickaxeTool);
            VoxelTypeDefinition stone =
                AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                    ProjectAssetPaths.Config.StoneVoxel);

            Assert.That(pickaxe, Is.Not.Null);
            Assert.That(stone, Is.Not.Null);
            Assert.That(
                pickaxe.AnimationTriggerMode,
                Is.EqualTo(PlayerToolAnimationTriggerMode.Periodic));
            Assert.That(pickaxe.ActionTriggerDelay, Is.EqualTo(0.42f));
            Assert.That(pickaxe.ActionCyclePeriod, Is.EqualTo(0.75f));
            Assert.That(pickaxe.ActionIsPeriodic, Is.True);
            Assert.That(pickaxe.MiningBrush.Power, Is.EqualTo(1f));
            Assert.That(pickaxe.MiningEvenHitMultiplier, Is.EqualTo(4f));
            Assert.That(
                pickaxe.GetMiningBrushForStrike(1).Power,
                Is.EqualTo(1f));
            Assert.That(
                pickaxe.GetMiningBrushForStrike(2).Power,
                Is.EqualTo(4f));
            Assert.That(
                pickaxe.GetMiningBrushForStrike(3).Power,
                Is.EqualTo(1f));
            Assert.That(
                pickaxe.MiningBrush.PropagationDivisor,
                Is.EqualTo(2f));
            Assert.That(
                pickaxe.MiningBrush.MaxAffectedSamples,
                Is.EqualTo(128));
            Assert.That(stone.Durability, Is.EqualTo(1));
        }

        [Test]
        public void MiningImpact_EmitsVoxelColoredDustAndChips()
        {
            GameObject host = Create("Mining Impact");
            VoxelMiningImpactEffect effect =
                host.AddComponent<VoxelMiningImpactEffect>();
            var primaryResult = new VoxelMiningResult(
                new Vector3Int(2, 3, 4),
                new VoxelTypeId(2),
                2,
                2,
                true);
            var brushResult = new VoxelMiningBrushResult(
                primaryResult.Coordinate,
                primaryResult.Type,
                3,
                3,
                2,
                primaryResult);

            effect.Play(
                new Vector3(1f, 2f, 3f),
                Vector3.up,
                Vector3.right,
                new Color(0.9f, 0.1f, 0.05f, 1f),
                brushResult);

            ParticleSystem[] systems =
                host.GetComponentsInChildren<ParticleSystem>();
            Assert.That(systems, Has.Length.EqualTo(2));
            Assert.That(effect.ActiveParticleCount, Is.GreaterThan(0));

            var particles = new ParticleSystem.Particle[32];
            int count = systems[0].GetParticles(particles);
            Assert.That(count, Is.GreaterThan(0));
            Assert.That(particles[0].startColor.r, Is.GreaterThan(
                particles[0].startColor.g));

            ParticleSystem chipSystem = null;
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i].name == "Mining Chips")
                {
                    chipSystem = systems[i];
                    break;
                }
            }
            Assert.That(chipSystem, Is.Not.Null);

            count = chipSystem.GetParticles(particles);
            Assert.That(count, Is.GreaterThan(0));
            for (int i = 0; i < count; i++)
            {
                Assert.That(
                    Vector3.Dot(particles[i].velocity, Vector3.left),
                    Is.GreaterThanOrEqualTo(0.44f),
                    "Every chip should initially recoil opposite the mining direction.");
            }
        }

        [Test]
        public void PlayerPrefab_WiresMiningImpactParticleMaterial()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.Player);
            Assert.That(player, Is.Not.Null);

            VoxelMiningImpactEffect effect =
                player.GetComponent<VoxelMiningImpactEffect>();
            Assert.That(effect, Is.Not.Null);
            Material material =
                GetPrivateField<Material>(effect, "particleMaterial");
            Assert.That(material, Is.Not.Null);
            Assert.That(
                material.shader.name,
                Is.EqualTo("Universal Render Pipeline/Particles/Unlit"));
        }

        private VoxelTypeDefinition CreateDefinition(
            ushort type,
            int durability,
            string displayName,
            Material material = null)
        {
            VoxelTypeDefinition definition =
                ScriptableObject.CreateInstance<VoxelTypeDefinition>();
            definition.Configure(type, displayName, durability, material);
            definitions.Add(definition);
            return definition;
        }

        private GameObject Create(string name)
        {
            var gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject;
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {target.GetType().Name}.{name}");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {target.GetType().Name}.{name}");
            return (T)field.GetValue(target);
        }

        private static void InvokePrivate(
            object target,
            string name,
            params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing method {target.GetType().Name}.{name}");
            method.Invoke(target, arguments);
        }
    }
}
