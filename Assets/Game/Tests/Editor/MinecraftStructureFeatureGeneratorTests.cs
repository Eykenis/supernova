using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class MinecraftStructureFeatureGeneratorTests
    {
        private static readonly VoxelTypeId Stone = new VoxelTypeId(2);
        private static readonly VoxelTypeId StructureBrick = new VoxelTypeId(5);

        [Test]
        public void GenerateColumn_SameSeedCreatesSameHollowTrialChamber()
        {
            MinecraftStructureFeatureSettings feature = CreateFeature();
            Assert.That(
                MinecraftStructureFeatureGenerator.TryGetPlacement(
                    feature,
                    6667,
                    0,
                    0,
                    out MinecraftStructureFeatureGenerator.Placement placement),
                Is.True);
            Vector3Int column = InfiniteVoxelWorld.WorldToChunk(
                placement.Centre.x,
                placement.Centre.y,
                placement.Centre.z);

            float[] firstDensities = CreateSolidDensities();
            VoxelTypeId[] firstTypes = CreateTypes(Stone);
            float[] secondDensities = CreateSolidDensities();
            VoxelTypeId[] secondTypes = CreateTypes(Stone);

            int firstChanged = MinecraftStructureFeatureGenerator.GenerateColumn(
                column,
                firstDensities,
                firstTypes,
                6667,
                new[] { feature },
                1f,
                -1f);
            int secondChanged = MinecraftStructureFeatureGenerator.GenerateColumn(
                column,
                secondDensities,
                secondTypes,
                6667,
                new[] { feature },
                1f,
                -1f);

            Assert.That(firstChanged, Is.GreaterThan(0));
            Assert.That(secondChanged, Is.EqualTo(firstChanged));
            CollectionAssert.AreEqual(firstDensities, secondDensities);
            CollectionAssert.AreEqual(firstTypes, secondTypes);
            Assert.That(
                GetWorldType(firstTypes, column, placement.Centre + Vector3Int.up * 2),
                Is.EqualTo(VoxelTypeId.Air),
                "The room centre should be carved into navigable air.");
            Assert.That(
                GetWorldType(firstTypes, column, placement.Centre + Vector3Int.up),
                Is.EqualTo(StructureBrick),
                "The example chamber should contain its central raised dais.");
        }

        [Test]
        public void GenerateColumn_CrossColumnStructureIsOrderIndependentAtNegativeCoordinates()
        {
            MinecraftStructureFeatureSettings feature = CreateFeature(
                roomSize: new Vector3Int(31, 11, 25),
                entranceLength: 20,
                regionSizeInChunks: 4);
            const int seed = 18731;
            Assert.That(
                MinecraftStructureFeatureGenerator.TryGetPlacement(
                    feature,
                    seed,
                    -2,
                    -1,
                    out MinecraftStructureFeatureGenerator.Placement placement),
                Is.True);

            int radius = feature.MaximumHorizontalInfluence;
            Vector3Int minColumn = InfiniteVoxelWorld.WorldToChunk(
                placement.Centre.x - radius,
                0,
                placement.Centre.z - radius);
            Vector3Int maxColumn = InfiniteVoxelWorld.WorldToChunk(
                placement.Centre.x + radius,
                0,
                placement.Centre.z + radius);
            var coordinates = new List<Vector3Int>();
            for (int z = minColumn.z; z <= maxColumn.z; z++)
            {
                for (int x = minColumn.x; x <= maxColumn.x; x++)
                {
                    coordinates.Add(new Vector3Int(x, 0, z));
                }
            }

            var forward = new Dictionary<Vector3Int, VoxelTypeId[]>();
            int changedColumns = 0;
            foreach (Vector3Int coordinate in coordinates)
            {
                VoxelTypeId[] types = GenerateTypes(coordinate, feature, seed, out int changed);
                forward.Add(coordinate, types);
                if (changed > 0) changedColumns++;
            }

            coordinates.Reverse();
            foreach (Vector3Int coordinate in coordinates)
            {
                VoxelTypeId[] reverse = GenerateTypes(
                    coordinate,
                    feature,
                    seed,
                    out _);
                CollectionAssert.AreEqual(forward[coordinate], reverse);
            }
            Assert.That(changedColumns, Is.GreaterThan(1));
            Assert.That(placement.Centre.x, Is.LessThan(0));
            Assert.That(placement.Centre.z, Is.LessThan(0));
        }

        [Test]
        public void GenerateColumn_UsesEditableTemplateSamples()
        {
            var templateSize = new Vector3Int(7, 5, 7);
            var templateAnchor = new Vector3Int(3, 1, 3);
            int count = templateSize.x * templateSize.y * templateSize.z;
            float[] templateDensities = Enumerable.Repeat(-1f, count).ToArray();
            VoxelTypeId[] templateTypes = CreateTypes(VoxelTypeId.Air, count);
            Vector3Int authoredCell = templateAnchor + Vector3Int.up * 2;
            int authoredIndex = authoredCell.x
                + templateSize.x * (
                    authoredCell.y + templateSize.y * authoredCell.z);
            templateDensities[authoredIndex] = 0.35f;
            templateTypes[authoredIndex] = StructureBrick;
            var feature = new MinecraftStructureFeatureSettings(
                "editable_template_test",
                StructureBrick,
                4242,
                2,
                1f,
                96,
                96,
                new Vector3Int(7, 5, 7),
                1,
                0,
                3,
                3,
                0,
                templateSize,
                templateAnchor,
                templateDensities,
                templateTypes);
            Assert.That(
                MinecraftStructureFeatureGenerator.TryGetPlacement(
                    feature,
                    6667,
                    0,
                    0,
                    out MinecraftStructureFeatureGenerator.Placement placement),
                Is.True);
            Vector3Int column = InfiniteVoxelWorld.WorldToChunk(
                placement.Centre.x,
                placement.Centre.y,
                placement.Centre.z);
            float[] densities = CreateSolidDensities();
            VoxelTypeId[] types = CreateTypes(Stone);

            MinecraftStructureFeatureGenerator.GenerateColumn(
                column,
                densities,
                types,
                6667,
                new[] { feature },
                1f,
                -1f);

            Vector3Int authoredWorld = placement.Centre + Vector3Int.up * 2;
            Assert.That(GetWorldType(types, column, authoredWorld), Is.EqualTo(StructureBrick));
            Assert.That(
                GetWorldDensity(densities, column, authoredWorld),
                Is.EqualTo(0.35f));
            Assert.That(
                GetWorldType(types, column, placement.Centre + Vector3Int.up),
                Is.EqualTo(VoxelTypeId.Air),
                "An empty authored sample should carve procedural terrain.");
        }

        [Test]
        public void DefaultWorld_ReferencesConfiguredRandomTrialChamberFeature()
        {
            MinecraftWorldGenerationConfiguration world =
                AssetDatabase.LoadAssetAtPath<MinecraftWorldGenerationConfiguration>(
                    ProjectAssetPaths.Config.WorldGeneration);
            VoxelStructureFeatureDefinition feature =
                AssetDatabase.LoadAssetAtPath<VoxelStructureFeatureDefinition>(
                    ProjectAssetPaths.Config.TrialChamberFeature);
            VoxelTypeDefinition structureBrick =
                AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                    ProjectAssetPaths.Config.StructureBrickVoxel);
            VoxelStructureAsset template =
                AssetDatabase.LoadAssetAtPath<VoxelStructureAsset>(
                    ProjectAssetPaths.Structures.TrialChamberTemplate);

            Assert.That(world, Is.Not.Null);
            Assert.That(feature, Is.Not.Null);
            Assert.That(structureBrick, Is.Not.Null);
            Assert.That(template, Is.Not.Null);
            Assert.That(world.StructureFeatures, Contains.Item(feature));
            Assert.That(feature.StructureVoxelType, Is.EqualTo(structureBrick));
            Assert.That(feature.StructureTemplate, Is.EqualTo(template));
            Assert.That(feature.TryCreateSettings(out _, out string error), Is.True, error);
            Assert.That(
                world.VoxelTypeCatalog.Definitions.Select(item => item.TypeId),
                Contains.Item(structureBrick.TypeId));
        }

        [Test]
        public void StructureAuthoring_CanBindRandomFeatureTemplate()
        {
            VoxelStructureFeatureDefinition feature =
                AssetDatabase.LoadAssetAtPath<VoxelStructureFeatureDefinition>(
                    ProjectAssetPaths.Config.TrialChamberFeature);
            VoxelTypeCatalog catalog =
                AssetDatabase.LoadAssetAtPath<VoxelTypeCatalog>(
                    ProjectAssetPaths.Config.VoxelCatalog);
            var authoringObject = new GameObject("Feature Template Authoring Test");
            try
            {
                VoxelStructureAuthoring authoring =
                    authoringObject.AddComponent<VoxelStructureAuthoring>();
                authoring.ConfigureFeature(feature, catalog);

                Assert.That(authoring.StructureFeatureToEdit, Is.EqualTo(feature));
                Assert.That(
                    authoring.StructureToEdit,
                    Is.EqualTo(feature.StructureTemplate));
                Assert.That(authoring.Size, Is.EqualTo(feature.StructureTemplate.Size));
                Assert.That(
                    authoring.Anchor,
                    Is.EqualTo(feature.StructureTemplate.Anchor));
            }
            finally
            {
                Object.DestroyImmediate(authoringObject);
            }
        }

        private static MinecraftStructureFeatureSettings CreateFeature(
            Vector3Int? roomSize = null,
            int entranceLength = 14,
            int regionSizeInChunks = 3)
        {
            return new MinecraftStructureFeatureSettings(
                "test_trial_chamber",
                StructureBrick,
                7919,
                regionSizeInChunks,
                1f,
                96,
                96,
                roomSize ?? new Vector3Int(21, 10, 17),
                1,
                2,
                3,
                4,
                entranceLength);
        }

        private static VoxelTypeId[] GenerateTypes(
            Vector3Int coordinate,
            MinecraftStructureFeatureSettings feature,
            int seed,
            out int changed)
        {
            float[] densities = CreateSolidDensities();
            VoxelTypeId[] types = CreateTypes(Stone);
            changed = MinecraftStructureFeatureGenerator.GenerateColumn(
                coordinate,
                densities,
                types,
                seed,
                new[] { feature },
                1f,
                -1f);
            return types;
        }

        private static VoxelTypeId GetWorldType(
            VoxelTypeId[] types,
            Vector3Int column,
            Vector3Int world)
        {
            Vector3Int local = InfiniteVoxelWorld.WorldToLocal(
                world.x,
                world.y,
                world.z,
                column);
            return types[VoxelColumnChunkData.ToIndex(local.x, local.y, local.z)];
        }

        private static float GetWorldDensity(
            float[] densities,
            Vector3Int column,
            Vector3Int world)
        {
            Vector3Int local = InfiniteVoxelWorld.WorldToLocal(
                world.x,
                world.y,
                world.z,
                column);
            return densities[VoxelColumnChunkData.ToIndex(
                local.x,
                local.y,
                local.z)];
        }

        private static float[] CreateSolidDensities()
        {
            return Enumerable.Repeat(1f, VoxelColumnChunkData.VoxelCount).ToArray();
        }

        private static VoxelTypeId[] CreateTypes(VoxelTypeId type)
        {
            return Enumerable.Repeat(type, VoxelColumnChunkData.VoxelCount).ToArray();
        }

        private static VoxelTypeId[] CreateTypes(VoxelTypeId type, int count)
        {
            return Enumerable.Repeat(type, count).ToArray();
        }
    }
}
