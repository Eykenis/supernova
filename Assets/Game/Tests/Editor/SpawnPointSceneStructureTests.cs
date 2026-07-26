using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class SpawnPointSceneStructureTests
    {
        private GameObject root;
        private GameObject markerObject;
        private GameObject playerObject;

        [TearDown]
        public void TearDown()
        {
            if (playerObject != null) Object.DestroyImmediate(playerObject);
            if (root != null) Object.DestroyImmediate(root);
        }

        [Test]
        public void PlaceAt_AlignsTheAuthoredPlayerMarkerWithTheWorldSpawnPose()
        {
            root = new GameObject("Cell");
            markerObject = new GameObject("Player Spawn");
            markerObject.transform.SetParent(root.transform, false);
            markerObject.transform.localPosition = new Vector3(1.5f, 0.02f, -2f);
            markerObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            SpawnPointSceneStructure structure =
                root.AddComponent<SpawnPointSceneStructure>();
            structure.Configure(markerObject.transform);
            Vector3 requestedPosition = new Vector3(-35f, 52f, 29f);
            Quaternion requestedRotation = Quaternion.Euler(0f, 35f, 0f);

            structure.PlaceAt(requestedPosition, requestedRotation);

            Assert.That(
                Vector3.Distance(markerObject.transform.position, requestedPosition),
                Is.LessThan(0.0001f));
            Assert.That(
                Quaternion.Angle(markerObject.transform.rotation, requestedRotation),
                Is.LessThan(0.001f));
        }

        [Test]
        public void Tick_OpensNearTheDoorAndClosesWithDistanceHysteresis()
        {
            root = new GameObject("Door");
            GameObject leafObject = new GameObject("Door Leaf");
            leafObject.transform.SetParent(root.transform, false);
            leafObject.AddComponent<BoxCollider>().size =
                new Vector3(2f, 2f, 0.25f);
            playerObject = new GameObject("Player");
            playerObject.AddComponent<CharacterController>();

            ProximitySlidingDoor door =
                root.AddComponent<ProximitySlidingDoor>();
            door.Configure(
                leafObject.transform,
                playerObject.transform,
                new Vector3(0f, -3f, 0f),
                1.8f,
                3f,
                10f);

            playerObject.transform.position = Vector3.zero;
            Physics.SyncTransforms();
            door.Tick(1f);

            Assert.That(door.IsOpenRequested, Is.True);
            Assert.That(leafObject.transform.localPosition.y, Is.EqualTo(-3f).Within(0.001f));

            playerObject.transform.position = Vector3.forward * 2f;
            Physics.SyncTransforms();
            door.Tick(1f);
            Assert.That(
                door.IsOpenRequested,
                Is.True,
                "The close radius should be larger than the open radius.");

            playerObject.transform.position = Vector3.forward * 4f;
            Physics.SyncTransforms();
            door.Tick(1f);

            Assert.That(door.IsOpenRequested, Is.False);
            Assert.That(leafObject.transform.localPosition.y, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void CarveTerrainClearance_RemovesRockAboveTheCellFloorOnly()
        {
            root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Cell";
            root.transform.position = new Vector3(0f, 2f, 0f);
            root.transform.localScale = new Vector3(4f, 4f, 4f);
            SpawnPointSceneStructure structure =
                root.AddComponent<SpawnPointSceneStructure>();
            var world = new Supernova.Voxels.InfiniteVoxelWorld();
            for (int y = -1; y <= 5; y++)
            {
                for (int z = -3; z <= 3; z++)
                {
                    for (int x = -3; x <= 3; x++)
                    {
                        world.SetDensity(x, y, z, 1f);
                    }
                }
            }

            int cleared = structure.CarveTerrainClearance(
                world,
                root.transform.parent,
                1f,
                -1f);

            Assert.That(cleared, Is.EqualTo(0));

            GameObject terrainObject = new GameObject("Terrain");
            int clearedWithTerrain = structure.CarveTerrainClearance(
                world,
                terrainObject.transform,
                1f,
                -1f);

            Assert.That(clearedWithTerrain, Is.GreaterThan(0));
            Assert.That(world.GetDensityOrDefault(0, 2, 0), Is.LessThan(0f));
            Assert.That(
                world.GetDensityOrDefault(0, 2, 3),
                Is.LessThan(0f),
                "The clearance must continue from the player marker through the exit.");
            Assert.That(
                world.GetDensityOrDefault(0, 1, 0),
                Is.GreaterThanOrEqualTo(0f),
                "Terrain below the authored Cell floor must remain as support.");
            Object.DestroyImmediate(terrainObject);
        }

        [Test]
        public void CardinalSearch_IgnoresDiagonalChunkAndChoosesNearestCardinalCave()
        {
            var world = new InfiniteVoxelWorld();
            Vector3Int spawn = new Vector3Int(31, 10, 31);
            Vector3Int spawnChunk = Vector3Int.zero;
            world.EnsureChunk(spawnChunk + Vector3Int.right);
            world.EnsureChunk(spawnChunk + Vector3Int.left);
            world.EnsureChunk(spawnChunk + new Vector3Int(0, 0, 1));
            world.EnsureChunk(spawnChunk + new Vector3Int(0, 0, -1));
            world.EnsureChunk(spawnChunk + new Vector3Int(1, 0, 1));

            CreateStandablePocket(world, new Vector3Int(45, 10, 28));
            CreateStandablePocket(world, new Vector3Int(28, 10, 40));
            CreateStandablePocket(world, new Vector3Int(33, 10, 33));

            bool found = CardinalCaveConnectionSearch.TryFindNearest(
                world,
                spawn,
                0f,
                5,
                1,
                0,
                out CardinalCaveTarget target);

            Assert.That(found, Is.True);
            Assert.That(target.Chunk, Is.EqualTo(new Vector3Int(0, 0, 1)));
            Assert.That(
                target.ChunkDirection,
                Is.EqualTo(new Vector3Int(0, 0, 1)));
            Assert.That(
                target.AirVoxel,
                Is.EqualTo(new Vector3Int(28, 10, 40)));
        }

        [Test]
        public void CardinalSearch_RejectsCavePointInsideMinimumExitDistance()
        {
            var world = new InfiniteVoxelWorld();
            Vector3Int spawn = new Vector3Int(31, 10, 16);
            world.EnsureChunk(Vector3Int.right);
            CreateStandablePocket(world, new Vector3Int(33, 10, 16));
            CreateStandablePocket(world, new Vector3Int(48, 10, 16));

            bool found = CardinalCaveConnectionSearch.TryFindNearest(
                world,
                spawn,
                0f,
                5,
                1,
                10,
                out CardinalCaveTarget target);

            Assert.That(found, Is.True);
            Assert.That(target.AirVoxel, Is.EqualTo(new Vector3Int(48, 10, 16)));
        }

        [Test]
        public void CardinalSearch_UsesVerticalLayerOfCardinalNeighbourColumn()
        {
            var world = new InfiniteVoxelWorld();
            Vector3Int spawn = new Vector3Int(16, 33, 16);
            world.EnsureChunk(new Vector3Int(1, 1, 0));
            world.EnsureChunk(new Vector3Int(1, 0, 0));
            CreateStandablePocket(world, new Vector3Int(33, 27, 16));

            bool found = CardinalCaveConnectionSearch.TryFindNearest(
                world,
                spawn,
                0f,
                5,
                1,
                0,
                out CardinalCaveTarget target);

            Assert.That(found, Is.True);
            Assert.That(target.Chunk, Is.EqualTo(new Vector3Int(1, 0, 0)));
            Assert.That(target.ChunkDirection, Is.EqualTo(Vector3Int.right));
            Assert.That(target.AirVoxel, Is.EqualTo(new Vector3Int(33, 27, 16)));
        }

        [Test]
        public void CarveTerrainClearance_FollowsConfiguredSlopedExitTarget()
        {
            root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Cell";
            root.transform.position = new Vector3(0f, 2f, 0f);
            root.transform.localScale = new Vector3(4f, 4f, 4f);
            SpawnPointSceneStructure structure =
                root.AddComponent<SpawnPointSceneStructure>();
            structure.SetExitTarget(new Vector3(0f, 4f, 8f));
            var world = new InfiniteVoxelWorld();
            for (int y = -1; y <= 9; y++)
            {
                for (int z = -3; z <= 9; z++)
                {
                    for (int x = -4; x <= 4; x++)
                    {
                        world.SetDensity(x, y, z, 1f);
                    }
                }
            }
            GameObject terrainObject = new GameObject("Terrain");

            int cleared = structure.CarveTerrainClearance(
                world,
                terrainObject.transform,
                1f,
                -1f);

            Assert.That(cleared, Is.GreaterThan(0));
            Assert.That(
                world.GetDensityOrDefault(0, 4, 8),
                Is.LessThan(0f),
                "The passage must reach the selected cave floor.");
            Assert.That(
                world.GetDensityOrDefault(0, 3, 4),
                Is.LessThan(0f),
                "The passage must interpolate its floor toward a higher cave.");
            Assert.That(
                world.GetDensityOrDefault(0, 2, 6),
                Is.GreaterThanOrEqualTo(0f),
                "Rock below the sloped passage floor must remain as support.");
            Object.DestroyImmediate(terrainObject);
        }

        [Test]
        public void CarveTerrainClearance_ScalesPassageDimensionsWithCell()
        {
            root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Cell";
            root.transform.localScale = Vector3.one * 0.7f;
            SpawnPointSceneStructure structure =
                root.AddComponent<SpawnPointSceneStructure>();
            structure.SetExitTarget(new Vector3(0f, 0f, 8f));
            var world = new InfiniteVoxelWorld();
            for (int y = -1; y <= 5; y++)
            {
                for (int z = -2; z <= 9; z++)
                {
                    for (int x = -4; x <= 4; x++)
                    {
                        world.SetDensity(x, y, z, 1f);
                    }
                }
            }
            GameObject terrainObject = new GameObject("Terrain");

            structure.CarveTerrainClearance(
                world,
                terrainObject.transform,
                1f,
                -1f);

            Assert.That(
                world.GetDensityOrDefault(2, 1, 6),
                Is.LessThan(0f),
                "The scaled passage should remain wide enough for the player.");
            Assert.That(
                world.GetDensityOrDefault(3, 1, 6),
                Is.GreaterThanOrEqualTo(0f),
                "The 0.7 Cell must not keep the original six-metre passage width.");
            Assert.That(
                world.GetDensityOrDefault(0, 4, 6),
                Is.GreaterThanOrEqualTo(0f),
                "The 0.7 Cell must also reduce the passage height.");
            Object.DestroyImmediate(terrainObject);
        }

        private static void CreateStandablePocket(
            InfiniteVoxelWorld world,
            Vector3Int airVoxel)
        {
            for (int zOffset = -1; zOffset <= 1; zOffset++)
            {
                for (int xOffset = -1; xOffset <= 1; xOffset++)
                {
                    for (int yOffset = 0; yOffset < 5; yOffset++)
                    {
                        world.SetDensity(
                            airVoxel.x + xOffset,
                            airVoxel.y + yOffset,
                            airVoxel.z + zOffset,
                            -1f);
                    }
                }
            }
        }
    }
}
