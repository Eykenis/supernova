using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class VoxelColumnChunkTests
    {
        [Test]
        public void ColumnDimensions_Are32By32HorizontallyAnd256High()
        {
            Assert.That(VoxelColumnChunkData.Width, Is.EqualTo(32));
            Assert.That(VoxelColumnChunkData.Depth, Is.EqualTo(32));
            Assert.That(VoxelColumnChunkData.Height, Is.EqualTo(256));
            Assert.That(
                VoxelColumnChunkData.VoxelCount,
                Is.EqualTo(32 * 32 * 256));
        }

        [Test]
        public void WorldCoordinates_UseOnlyXZForColumnIndex()
        {
            Vector3Int low = InfiniteVoxelWorld.WorldToChunk(-1, 0, -33);
            Vector3Int high = InfiniteVoxelWorld.WorldToChunk(-1, 255, -33);

            Assert.That(low, Is.EqualTo(new Vector3Int(-1, 0, -2)));
            Assert.That(high, Is.EqualTo(low));
            Assert.That(
                InfiniteVoxelWorld.WorldToLocal(-1, 255, -33, high),
                Is.EqualTo(new Vector3Int(31, 255, 31)));

            var world = new InfiniteVoxelWorld();
            InfiniteVoxelChunk first = world.EnsureChunk(low);
            InfiniteVoxelChunk sameColumn =
                world.EnsureChunk(new Vector3Int(low.x, 99, low.z));
            Assert.That(sameColumn, Is.SameAs(first));
            Assert.That(world.ChunkCount, Is.EqualTo(1));
        }

        [Test]
        public void StreamingOffsets_FormAFlatRadiusFourDisk()
        {
            Assert.That(
                MinecraftCaveInfiniteWorld.StreamingOffsets,
                Has.Count.EqualTo(
                    MinecraftCaveInfiniteWorld.RequiredChunkCountAtRadius));
            Assert.That(
                MinecraftCaveInfiniteWorld.StreamingOffsets.All(
                    offset => offset.y == 0
                        && offset.x * offset.x + offset.z * offset.z
                            <= MinecraftCaveInfiniteWorld.GenerationRadiusInChunks
                            * MinecraftCaveInfiniteWorld.GenerationRadiusInChunks),
                Is.True);
        }

        [Test]
        public void BoundaryWriter_AssignsBedrockOnlyToTopAndBottomLayers()
        {
            var densities = new float[VoxelColumnChunkData.VoxelCount];
            var types = new VoxelTypeId[VoxelColumnChunkData.VoxelCount];
            var bedrock = new VoxelTypeId(4);
            MethodInfo method = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                "ApplyBoundaryBedrock",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            int written = (int)method.Invoke(
                null,
                new object[] { densities, types, bedrock });

            Assert.That(
                written,
                Is.EqualTo(
                    VoxelColumnChunkData.Width
                    * VoxelColumnChunkData.Depth
                    * 2));
            for (int z = 0; z < VoxelColumnChunkData.Depth; z++)
            {
                for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                {
                    Assert.That(types[
                        VoxelColumnChunkData.ToIndex(x, 0, z)], Is.EqualTo(bedrock));
                    Assert.That(types[
                        VoxelColumnChunkData.ToIndex(
                            x,
                            VoxelColumnChunkData.Height - 1,
                            z)], Is.EqualTo(bedrock));
                }
            }
            Assert.That(
                types[VoxelColumnChunkData.ToIndex(0, 1, 0)].IsAir,
                Is.True);
            Assert.That(
                MinecraftCaveInfiniteWorld.HighestSpawnY,
                Is.EqualTo(VoxelColumnChunkData.Height - 1 - 32));
            Assert.That(
                MinecraftCaveInfiniteWorld.LowestSpawnY,
                Is.EqualTo(VoxelColumnChunkData.Height - 1 - 160));
            Assert.That(
                MinecraftCaveInfiniteWorld.LowestSpawnY,
                Is.LessThan(MinecraftCaveInfiniteWorld.HighestSpawnY));
        }

        [Test]
        public void DensityInterpolator_PreservesGloballyAlignedLatticeSamples()
        {
            var field = new MinecraftCaveDensityField(
                18731,
                new MinecraftCaveSettings());
            var column = new Vector3Int(-1, 0, 1);
            float[] densities = MinecraftCaveDensityInterpolator.SampleColumn(
                column,
                field,
                CancellationToken.None);

            Assert.That(
                MinecraftCaveDensityInterpolator.CoarseSampleCount,
                Is.EqualTo(17 * 65 * 17));
            Assert.That(
                densities.Length,
                Is.EqualTo(VoxelColumnChunkData.VoxelCount));

            AssertLatticeSampleMatches(field, densities, column, 0, 0, 0);
            AssertLatticeSampleMatches(field, densities, column, 2, 4, 2);
            AssertLatticeSampleMatches(field, densities, column, 30, 252, 30);
        }

        [Test]
        public void AddChunkTakingOwnership_DoesNotCopyGeneratedArrays()
        {
            var densities = new float[VoxelColumnChunkData.VoxelCount];
            var types = new VoxelTypeId[VoxelColumnChunkData.VoxelCount];
            var world = new InfiniteVoxelWorld();
            world.AddChunkTakingOwnership(Vector3Int.zero, densities, types);

            int index = VoxelColumnChunkData.ToIndex(3, 7, 11);
            densities[index] = 0.75f;
            types[index] = new VoxelTypeId(9);

            Assert.That(
                world.TryGetSample(3, 7, 11, out VoxelSample sample),
                Is.True);
            Assert.That(sample.Density, Is.EqualTo(0.75f));
            Assert.That(sample.Type, Is.EqualTo(new VoxelTypeId(9)));
        }

        [Test]
        public void RemoveChunk_ReleasesOnlyTheRequestedColumn()
        {
            var world = new InfiniteVoxelWorld();
            InfiniteVoxelChunk retained = world.EnsureChunk(Vector2Int.zero);
            InfiniteVoxelChunk removed = world.EnsureChunk(Vector2Int.right);

            bool didRemove = world.RemoveChunk(Vector3Int.right, out InfiniteVoxelChunk result);

            Assert.That(didRemove, Is.True);
            Assert.That(result, Is.SameAs(removed));
            Assert.That(world.ChunkCount, Is.EqualTo(1));
            Assert.That(world.TryGetChunk(Vector2Int.right, out _), Is.False);
            Assert.That(
                world.TryGetChunk(Vector2Int.zero, out InfiniteVoxelChunk remaining),
                Is.True);
            Assert.That(remaining, Is.SameAs(retained));
            Assert.That(
                world.RemoveChunk(Vector2Int.right, out _),
                Is.False);
        }

        [Test]
        public void VoxelCacheCull_KeepsConfiguredRadiusAndRequiredOuterColumns()
        {
            var terrainObject = new GameObject("Voxel cache cull test");
            try
            {
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                var voxelWorld = new InfiniteVoxelWorld();
                typeof(MinecraftCaveInfiniteWorld).GetField(
                    "world",
                    BindingFlags.Instance | BindingFlags.NonPublic).SetValue(
                        terrain,
                        voxelWorld);

                voxelWorld.EnsureChunk(Vector2Int.zero);
                voxelWorld.EnsureChunk(new Vector2Int(3, 0));
                voxelWorld.EnsureChunk(new Vector2Int(4, 0));
                voxelWorld.EnsureChunk(new Vector2Int(0, -4));

                FieldInfo requiredChunksField =
                    typeof(MinecraftCaveInfiniteWorld).GetField(
                        "requiredChunks",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(requiredChunksField, Is.Not.Null);
                var requiredChunks =
                    (System.Collections.Generic.HashSet<Vector3Int>)
                    requiredChunksField.GetValue(terrain);
                requiredChunks.Add(new Vector3Int(4, 0, 0));

                MethodInfo cull = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                    "CullVoxelDataOutsideRetentionRadius",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(cull, Is.Not.Null);
                cull.Invoke(terrain, null);

                Assert.That(terrain.CachedVoxelColumnCount, Is.EqualTo(3));
                Assert.That(
                    voxelWorld.TryGetChunk(Vector2Int.zero, out _),
                    Is.True);
                Assert.That(
                    voxelWorld.TryGetChunk(new Vector2Int(3, 0), out _),
                    Is.True);
                Assert.That(
                    voxelWorld.TryGetChunk(new Vector2Int(4, 0), out _),
                    Is.True);
                Assert.That(
                    voxelWorld.TryGetChunk(new Vector2Int(0, -4), out _),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(terrainObject);
            }
        }

        [Test]
        public void GameplayVoxelOverride_ReappliesAfterColumnEviction()
        {
            var terrainObject = new GameObject("Voxel override test");
            try
            {
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                var voxelWorld = new InfiniteVoxelWorld();
                typeof(MinecraftCaveInfiniteWorld).GetField(
                    "world",
                    BindingFlags.Instance | BindingFlags.NonPublic).SetValue(
                        terrain,
                        voxelWorld);
                InfiniteVoxelChunk chunk = voxelWorld.EnsureChunk(Vector2Int.zero);
                chunk.Data.Fill(1f, VoxelTypeId.Default);
                var coordinate = new Vector3Int(3, 7, 11);

                Assert.That(
                    terrain.TrySetVoxelAndRebuild(
                        coordinate.x,
                        coordinate.y,
                        coordinate.z,
                        -1f,
                        VoxelTypeId.Air),
                    Is.True);
                Assert.That(terrain.GameplayVoxelOverrideCount, Is.EqualTo(1));
                Assert.That(
                    voxelWorld.RemoveChunk(Vector2Int.zero, out _),
                    Is.True);

                var densities = new float[VoxelColumnChunkData.VoxelCount];
                var types = new VoxelTypeId[VoxelColumnChunkData.VoxelCount];
                int index = VoxelColumnChunkData.ToIndex(
                    coordinate.x,
                    coordinate.y,
                    coordinate.z);
                densities[index] = 1f;
                types[index] = VoxelTypeId.Default;
                MethodInfo apply = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                    "ApplyGameplayVoxelOverrides",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(apply, Is.Not.Null);
                apply.Invoke(
                    terrain,
                    new object[] { Vector3Int.zero, densities, types });

                Assert.That(densities[index], Is.EqualTo(-1f));
                Assert.That(types[index], Is.EqualTo(VoxelTypeId.Air));
            }
            finally
            {
                Object.DestroyImmediate(terrainObject);
            }
        }




        [Test]
        public void MeshPostProcess_DefersColliderAndSurfaceWorkInStages()
        {
            var terrainObject = new GameObject("Mesh post-process test");
            try
            {
                MinecraftCaveInfiniteWorld terrain =
                    terrainObject.AddComponent<MinecraftCaveInfiniteWorld>();
                typeof(MinecraftCaveInfiniteWorld).GetField(
                    "world",
                    BindingFlags.Instance | BindingFlags.NonPublic).SetValue(
                        terrain,
                        new InfiniteVoxelWorld());
                typeof(MinecraftCaveInfiniteWorld).GetField(
                    "generateColliders",
                    BindingFlags.Instance | BindingFlags.NonPublic).SetValue(
                        terrain,
                        true);

                var data = new VoxelMeshData();
                data.Vertices.Add(Vector3.zero);
                data.Vertices.Add(Vector3.right);
                data.Vertices.Add(Vector3.forward);
                data.Triangles.Add(0);
                data.Triangles.Add(1);
                data.Triangles.Add(2);
                var coordinate = Vector3Int.zero;
                MethodInfo apply = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                    "ApplyChunkMeshData",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo process =
                    typeof(MinecraftCaveInfiniteWorld).GetMethod(
                        "ProcessPendingMeshPostProcesses",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(apply, Is.Not.Null);
                Assert.That(process, Is.Not.Null);
                apply.Invoke(terrain, new object[] { coordinate, data });

                Assert.That(terrain.PendingMeshPostProcessCount, Is.EqualTo(1));
                Assert.That(
                    terrain.GetComponentInChildren<MeshCollider>(true),
                    Is.Null,
                    "Collider cooking must not run in the mesh upload phase.");

                process.Invoke(terrain, new object[] { 1, 1000f });

                MeshCollider collider =
                    terrain.GetComponentInChildren<MeshCollider>(true);
                Assert.That(collider, Is.Not.Null);
                Assert.That(collider.sharedMesh, Is.Not.Null);
                Assert.That(terrain.PendingMeshPostProcessCount, Is.EqualTo(1));

                process.Invoke(terrain, new object[] { 1, 1000f });

                Assert.That(terrain.PendingMeshPostProcessCount, Is.Zero);
                Mesh firstColliderMesh = collider.sharedMesh;
                var replacementData = new VoxelMeshData();
                replacementData.Vertices.Add(Vector3.zero);
                replacementData.Vertices.Add(Vector3.right);
                replacementData.Vertices.Add(Vector3.up);
                replacementData.Triangles.Add(0);
                replacementData.Triangles.Add(1);
                replacementData.Triangles.Add(2);

                apply.Invoke(
                    terrain,
                    new object[] { coordinate, replacementData });

                MeshFilter replacementFilter =
                    collider.GetComponent<MeshFilter>();
                Assert.That(replacementFilter.sharedMesh, Is.Not.Null);
                Assert.That(
                    replacementFilter.sharedMesh,
                    Is.Not.SameAs(firstColliderMesh));
                Assert.That(
                    collider.sharedMesh,
                    Is.SameAs(firstColliderMesh),
                    "Rebuilding must keep the previous collision mesh attached "
                    + "until the deferred collider stage swaps in the new mesh.");

                process.Invoke(terrain, new object[] { 1, 1000f });

                Assert.That(
                    collider.sharedMesh,
                    Is.SameAs(replacementFilter.sharedMesh));
                Assert.That(terrain.PooledChunkMeshCount, Is.EqualTo(1));
                process.Invoke(terrain, new object[] { 1, 1000f });
                Assert.That(terrain.PendingMeshPostProcessCount, Is.Zero);

                MethodInfo destroy = typeof(MinecraftCaveInfiniteWorld).GetMethod(
                    "DestroyChunkObject",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo chunkObjectsField =
                    typeof(MinecraftCaveInfiniteWorld).GetField(
                        "chunkObjects",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(destroy, Is.Not.Null);
                Assert.That(chunkObjectsField, Is.Not.Null);
                GameObject firstChunkObject = collider.gameObject;

                destroy.Invoke(terrain, new object[] { coordinate, true });

                Assert.That(terrain.PooledChunkObjectCount, Is.EqualTo(1));
                var secondData = new VoxelMeshData();
                secondData.Vertices.Add(Vector3.zero);
                secondData.Vertices.Add(Vector3.right);
                secondData.Vertices.Add(Vector3.forward);
                secondData.Triangles.Add(0);
                secondData.Triangles.Add(1);
                secondData.Triangles.Add(2);
                var secondCoordinate = Vector3Int.right;
                apply.Invoke(
                    terrain,
                    new object[] { secondCoordinate, secondData });
                var chunkObjects =
                    (System.Collections.Generic.Dictionary<Vector3Int, GameObject>)
                    chunkObjectsField.GetValue(terrain);

                Assert.That(terrain.PooledChunkObjectCount, Is.Zero);
                Assert.That(
                    chunkObjects[secondCoordinate],
                    Is.SameAs(firstChunkObject));
            }
            finally
            {
                Object.DestroyImmediate(terrainObject);
            }
        }


        [Test]
        public void MeshSections_CoverTheFullColumnHeight()
        {
            Assert.That(
                MinecraftCaveInfiniteWorld.MeshSectionHeight
                * MinecraftCaveInfiniteWorld.MeshSectionsPerColumn,
                Is.EqualTo(VoxelColumnChunkData.Height));
        }

        private static void AssertLatticeSampleMatches(
            MinecraftCaveDensityField field,
            float[] densities,
            Vector3Int column,
            int localX,
            int localY,
            int localZ)
        {
            float expected = field.SampleFeatureDensity(
                new Vector3(
                    column.x * VoxelColumnChunkData.Width + localX,
                    localY,
                    column.z * VoxelColumnChunkData.Depth + localZ),
                MinecraftCaveType.Combined);
            float actual = densities[VoxelColumnChunkData.ToIndex(
                localX,
                localY,
                localZ)];
            Assert.That(actual, Is.EqualTo(expected).Within(0.000001f));
        }
    }
}
