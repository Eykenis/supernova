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
