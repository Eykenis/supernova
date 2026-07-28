using System.Linq;
using NUnit.Framework;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class MinecraftOreConfigurationAssetTests
    {
        private const string FeaturePath =
            ProjectAssetPaths.Config.OreFeature;
        private const string WorldGenerationPath =
            ProjectAssetPaths.Config.WorldGeneration;

        [Test]
        public void DefaultOreFeature_ReferencesIndependentOreAndStoneDefinitions()
        {
            VoxelOreFeatureDefinition feature =
                AssetDatabase.LoadAssetAtPath<VoxelOreFeatureDefinition>(
                    FeaturePath);

            Assert.That(feature, Is.Not.Null);
            Assert.That(feature.ResultVoxelType, Is.Not.Null);
            Assert.That(feature.ResultVoxelType.TypeId, Is.EqualTo(new VoxelTypeId(3)));
            Assert.That(feature.ResultVoxelType.DisplayName, Is.EqualTo("Ore"));
            Assert.That(feature.ResultVoxelType.Material, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(feature.ResultVoxelType.Material),
                Is.EqualTo(ProjectAssetPaths.Materials.Ore));
            Assert.That(feature.ReplaceableVoxelTypes, Has.Count.EqualTo(1));
            Assert.That(
                feature.ReplaceableVoxelTypes[0].TypeId,
                Is.EqualTo(new VoxelTypeId(2)));

            Assert.That(feature.AttemptsPerRegion, Is.EqualTo(8));
            Assert.That(feature.PlacementChance, Is.EqualTo(1f));
            Assert.That(
                feature.HeightDistribution,
                Is.EqualTo(
                    MinecraftOreFeatureSettings.HeightDistribution.Trapezoid));
            Assert.That(feature.MinHeight, Is.EqualTo(1));
            Assert.That(
                feature.MaxHeight,
                Is.EqualTo(VoxelColumnChunkData.Height - 2),
                "The default ore pass must cover the high caves while excluding top bedrock.");
            Assert.That(feature.Size, Is.EqualTo(8));
            Assert.That(feature.DiscardChanceOnAirExposure, Is.EqualTo(0.5f));
            Assert.That(
                feature.TryCreateSettings(out _, out string error),
                Is.True,
                error);
        }

        [Test]
        public void DefaultOreFeature_GeneratesOreAboveLegacyHeightLimit()
        {
            VoxelOreFeatureDefinition feature =
                AssetDatabase.LoadAssetAtPath<VoxelOreFeatureDefinition>(FeaturePath);
            Assert.That(
                feature.TryCreateSettings(
                    out MinecraftOreFeatureSettings settings,
                    out string error),
                Is.True,
                error);

            float[] densities = Enumerable
                .Repeat(1f, VoxelColumnChunkData.VoxelCount)
                .ToArray();
            VoxelTypeId stone = feature.ReplaceableVoxelTypes[0].TypeId;
            VoxelTypeId[] types = Enumerable
                .Repeat(stone, VoxelColumnChunkData.VoxelCount)
                .ToArray();

            MinecraftOreFeatureGenerator.GenerateColumn(
                Vector3Int.zero,
                densities,
                types,
                18731,
                new[] { settings },
                (_, _, _) => 1f);

            bool hasHighOre = false;
            for (int z = 0; z < VoxelColumnChunkData.Depth && !hasHighOre; z++)
            {
                for (int y = 65; y < VoxelColumnChunkData.Height - 1 && !hasHighOre; y++)
                {
                    for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                    {
                        if (types[VoxelColumnChunkData.ToIndex(x, y, z)]
                            == settings.ResultType)
                        {
                            hasHighOre = true;
                            break;
                        }
                    }
                }
            }

            Assert.That(
                hasHighOre,
                Is.True,
                "The configured first-level ore pass should place veins above Y=64.");
        }

        [Test]
        public void FirstLevelWorldGeneration_UsesStoneBaseAndDefaultOreFeature()
        {
            MinecraftWorldGenerationConfiguration configuration =
                AssetDatabase.LoadAssetAtPath<
                    MinecraftWorldGenerationConfiguration>(
                    WorldGenerationPath);
            VoxelOreFeatureDefinition feature =
                AssetDatabase.LoadAssetAtPath<VoxelOreFeatureDefinition>(
                    FeaturePath);

            Assert.That(configuration, Is.Not.Null);
            Assert.That(configuration.BaseSolidVoxelType, Is.Not.Null);
            Assert.That(
                configuration.BaseSolidVoxelType.TypeId,
                Is.EqualTo(new VoxelTypeId(2)));
            Assert.That(configuration.OreFeatures, Has.Count.EqualTo(1));
            Assert.That(configuration.OreFeatures[0], Is.SameAs(feature));
        }
    }
}
