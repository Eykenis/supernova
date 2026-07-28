using System;
using System.Linq;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class MinecraftOreFeatureGeneratorTests
    {
        private static readonly VoxelTypeId Stone = new VoxelTypeId(2);
        private static readonly VoxelTypeId Ore = new VoxelTypeId(3);

        [Test]
        public void GenerateChunk_SameSeedProducesIdenticalTypedSamples()
        {
            MinecraftOreFeatureSettings feature = CreateFeature(
                attempts: 12,
                size: 12);
            float[] firstDensities = CreateSolidDensities();
            float[] secondDensities = CreateSolidDensities();
            VoxelTypeId[] firstTypes = CreateTypes(Stone);
            VoxelTypeId[] secondTypes = CreateTypes(Stone);

            int firstCount = MinecraftOreFeatureGenerator.GenerateChunk(
                Vector3Int.zero,
                firstDensities,
                firstTypes,
                18731,
                new[] { feature });
            int secondCount = MinecraftOreFeatureGenerator.GenerateChunk(
                Vector3Int.zero,
                secondDensities,
                secondTypes,
                18731,
                new[] { feature });

            Assert.That(firstCount, Is.GreaterThan(0));
            Assert.That(secondCount, Is.EqualTo(firstCount));
            CollectionAssert.AreEqual(firstTypes, secondTypes);
        }

        [Test]
        public void GenerateChunk_OnlyReplacesConfiguredBaseType()
        {
            MinecraftOreFeatureSettings feature = CreateFeature(
                attempts: 24,
                size: 16);
            float[] densities = CreateSolidDensities();
            VoxelTypeId[] types = new VoxelTypeId[VoxelVolume.VoxelCount];
            for (int z = 0; z < VoxelVolume.Size; z++)
            {
                for (int y = 0; y < VoxelVolume.Size; y++)
                {
                    for (int x = 0; x < VoxelVolume.Size; x++)
                    {
                        types[ToIndex(x, y, z)] = (x & 1) == 0
                            ? Stone
                            : VoxelTypeId.Default;
                    }
                }
            }
            VoxelTypeId[] before = (VoxelTypeId[])types.Clone();

            int changed = MinecraftOreFeatureGenerator.GenerateChunk(
                Vector3Int.zero,
                densities,
                types,
                90210,
                new[] { feature });

            Assert.That(changed, Is.GreaterThan(0));
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] == Ore)
                {
                    Assert.That(before[i], Is.EqualTo(Stone));
                }
                else if (before[i] == VoxelTypeId.Default)
                {
                    Assert.That(types[i], Is.EqualTo(VoxelTypeId.Default));
                }
            }
        }

        [Test]
        public void GenerateChunk_AdjacentChunksAreOrderIndependentAndCanShareVein()
        {
            MinecraftOreFeatureSettings feature = CreateFeature(
                attempts: 48,
                size: 48,
                minimumHeight: 15,
                maximumHeight: 17);
            Vector3Int leftCoordinate = Vector3Int.zero;
            Vector3Int rightCoordinate = Vector3Int.right;

            VoxelTypeId[] leftFirst = GenerateTypes(leftCoordinate, feature, 44117);
            VoxelTypeId[] rightSecond = GenerateTypes(rightCoordinate, feature, 44117);
            VoxelTypeId[] rightFirst = GenerateTypes(rightCoordinate, feature, 44117);
            VoxelTypeId[] leftSecond = GenerateTypes(leftCoordinate, feature, 44117);

            CollectionAssert.AreEqual(leftFirst, leftSecond);
            CollectionAssert.AreEqual(rightFirst, rightSecond);

            bool hasCrossBorderPair = Enumerable.Range(0, VoxelVolume.Size)
                .SelectMany(y => Enumerable.Range(0, VoxelVolume.Size)
                    .Select(z => (y, z)))
                .Any(position =>
                    leftFirst[ToIndex(
                        VoxelVolume.Size - 1,
                        position.y,
                        position.z)] == Ore
                    && rightFirst[ToIndex(0, position.y, position.z)] == Ore);
            Assert.That(
                hasCrossBorderPair,
                Is.True,
                "Expected at least one ore vein to continue across the X chunk border.");
        }

        [Test]
        public void GenerateChunk_FullAirExposureDiscardRejectsExposedCandidates()
        {
            MinecraftOreFeatureSettings visibleFeature = CreateFeature(
                attempts: 32,
                size: 20,
                discardChance: 0f);
            MinecraftOreFeatureSettings buriedFeature = CreateFeature(
                attempts: 32,
                size: 20,
                discardChance: 1f);
            float[] densities = new float[VoxelVolume.VoxelCount];
            VoxelTypeId[] types = new VoxelTypeId[VoxelVolume.VoxelCount];
            for (int z = 0; z < VoxelVolume.Size; z++)
            {
                for (int y = 0; y < VoxelVolume.Size; y++)
                {
                    for (int x = 0; x < VoxelVolume.Size; x++)
                    {
                        bool solid = (x & 1) == 0;
                        int index = ToIndex(x, y, z);
                        densities[index] = solid ? 1f : -1f;
                        types[index] = solid ? Stone : VoxelTypeId.Air;
                    }
                }
            }

            int visibleCount = MinecraftOreFeatureGenerator.GenerateChunk(
                Vector3Int.zero,
                (float[])densities.Clone(),
                (VoxelTypeId[])types.Clone(),
                713,
                new[] { visibleFeature },
                (_, _, _) => -1f);
            VoxelTypeId[] buriedTypes = (VoxelTypeId[])types.Clone();
            int buriedCount = MinecraftOreFeatureGenerator.GenerateChunk(
                Vector3Int.zero,
                (float[])densities.Clone(),
                buriedTypes,
                713,
                new[] { buriedFeature },
                (_, _, _) => -1f);

            Assert.That(visibleCount, Is.GreaterThan(0));
            Assert.That(buriedCount, Is.Zero);
            Assert.That(buriedTypes, Has.None.EqualTo(Ore));
        }

        [Test]
        public void GenerateColumn_DepthProfileFavorsDeepOreAttempts()
        {
            var profile = new DepthProbabilityProfile();
            profile.Configure(0f, 1f, 1f);
            var shallowFeature = new MinecraftOreFeatureSettings(
                Ore,
                new[] { Stone },
                3109,
                64,
                1f,
                MinecraftOreFeatureSettings.HeightDistribution.Uniform,
                VoxelColumnChunkData.Height - 1,
                VoxelColumnChunkData.Height - 1,
                0,
                8,
                0f);
            var deepFeature = new MinecraftOreFeatureSettings(
                Ore,
                new[] { Stone },
                3109,
                64,
                1f,
                MinecraftOreFeatureSettings.HeightDistribution.Uniform,
                1,
                1,
                0,
                8,
                0f);
            float[] densities = Enumerable
                .Repeat(1f, VoxelColumnChunkData.VoxelCount)
                .ToArray();

            int shallowCount = MinecraftOreFeatureGenerator.GenerateColumn(
                Vector3Int.zero,
                densities,
                Enumerable
                    .Repeat(Stone, VoxelColumnChunkData.VoxelCount)
                    .ToArray(),
                18731,
                new[] { shallowFeature },
                depthProbability: profile);
            int deepCount = MinecraftOreFeatureGenerator.GenerateColumn(
                Vector3Int.zero,
                densities,
                Enumerable
                    .Repeat(Stone, VoxelColumnChunkData.VoxelCount)
                    .ToArray(),
                18731,
                new[] { deepFeature },
                depthProbability: profile);

            Assert.That(shallowCount, Is.Zero);
            Assert.That(deepCount, Is.GreaterThan(0));
        }

        private static MinecraftOreFeatureSettings CreateFeature(
            int attempts,
            int size,
            int minimumHeight = 0,
            int maximumHeight = VoxelVolume.Size - 1,
            float discardChance = 0f)
        {
            return new MinecraftOreFeatureSettings(
                Ore,
                new[] { Stone },
                3109,
                attempts,
                1f,
                MinecraftOreFeatureSettings.HeightDistribution.Trapezoid,
                minimumHeight,
                maximumHeight,
                0,
                size,
                discardChance);
        }

        private static VoxelTypeId[] GenerateTypes(
            Vector3Int coordinate,
            MinecraftOreFeatureSettings feature,
            int seed)
        {
            float[] densities = CreateSolidDensities();
            VoxelTypeId[] types = CreateTypes(Stone);
            MinecraftOreFeatureGenerator.GenerateChunk(
                coordinate,
                densities,
                types,
                seed,
                new[] { feature });
            return types;
        }

        private static float[] CreateSolidDensities()
        {
            return Enumerable.Repeat(1f, VoxelVolume.VoxelCount).ToArray();
        }

        private static VoxelTypeId[] CreateTypes(VoxelTypeId type)
        {
            return Enumerable.Repeat(type, VoxelVolume.VoxelCount).ToArray();
        }

        private static int ToIndex(int x, int y, int z)
        {
            return x + VoxelVolume.Size * (y + VoxelVolume.Size * z);
        }
    }
}
