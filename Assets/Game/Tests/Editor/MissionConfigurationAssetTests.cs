using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using Supernova.MinecraftCaves.Creatures;
using Supernova.Missions;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class MissionConfigurationAssetTests
    {
        private const string LevelPath =
            ProjectAssetPaths.Config.FirstLevel;
        private const string WorldGenerationPath =
            ProjectAssetPaths.Config.WorldGeneration;

        [Test]
        public void FirstLevel_ComposesAllGenerationAndEvacuationConfiguration()
        {
            LevelConfiguration level =
                AssetDatabase.LoadAssetAtPath<LevelConfiguration>(LevelPath);
            MinecraftWorldGenerationConfiguration worldGeneration =
                AssetDatabase.LoadAssetAtPath<
                    MinecraftWorldGenerationConfiguration>(
                    WorldGenerationPath);

            Assert.That(level, Is.Not.Null);
            Assert.That(worldGeneration, Is.Not.Null);
            Assert.That(level.LevelNumber, Is.EqualTo(1));
            Assert.That(level.DisplayName, Is.EqualTo("FIRST DESCENT"));
            Assert.That(level.WorldGeneration, Is.SameAs(worldGeneration));
            Assert.That(level.MonsterGeneration, Is.Not.Null);
            Assert.That(level.TreasureGeneration, Is.Not.Null);
            Assert.That(level.HasCompleteGenerationConfiguration, Is.True);
            Assert.That(level.EvacuationCountdownSeconds, Is.EqualTo(180f));
            Assert.That(level.RequiredFunds, Is.EqualTo(100));
            Assert.That(level.HomeSceneName, Is.EqualTo("Home"));
            Assert.That(level.CaveSceneName, Is.EqualTo("InfiniteCaves"));
        }

        [Test]
        public void FirstLevel_UsesThreeMinuteEvacuationCountdown()
        {
            LevelConfiguration level =
                AssetDatabase.LoadAssetAtPath<LevelConfiguration>(LevelPath);

            Assert.That(level, Is.Not.Null);
            Assert.That(level.EvacuationCountdownSeconds, Is.EqualTo(180f));
        }

        [Test]
        public void DefaultWorldGeneration_ContainsCaveVoxelAndRuntimeParameters()
        {
            MinecraftWorldGenerationConfiguration configuration =
                AssetDatabase.LoadAssetAtPath<
                    MinecraftWorldGenerationConfiguration>(
                    WorldGenerationPath);

            Assert.That(configuration, Is.Not.Null);
            Assert.That(configuration.WorldSeed, Is.EqualTo(114514));
            Assert.That(configuration.Settings, Is.Not.Null);
            Assert.That(
                configuration.Settings.spaghettiFrequency,
                Is.EqualTo(0.025f).Within(0.0001f));
            Assert.That(
                configuration.Settings.spaghettiThickness,
                Is.EqualTo(0.13f).Within(0.0001f));
            Assert.That(configuration.VoxelTypeCatalog, Is.Not.Null);
            Assert.That(configuration.BaseSolidVoxelType, Is.Not.Null);
            Assert.That(configuration.BedrockVoxelType, Is.Not.Null);
            Assert.That(configuration.OreFeatures, Is.Not.Empty);
            Assert.That(configuration.SpawnPointStructureRule.IsConfigured, Is.True);
            Assert.That(configuration.MaxConcurrentGenerationJobs, Is.EqualTo(4));
            Assert.That(configuration.MeshesBuiltPerFrame, Is.EqualTo(1));
            AssertDepthScaling(configuration.OreDepthProbability);
            AssertDepthScaling(configuration.TreasureDepthProbability);
            AssertDepthScaling(configuration.MonsterDepthProbability);
        }

        [Test]
        public void WorldCategoryConfigurations_AreNotSerializedOnSceneComponent()
        {
            string[] directFields =
            {
                "worldGenerationConfiguration",
                "treasureSpawnTable",
                "monsterSpawnTable",
                "settings",
                "voxelTypeCatalog",
                "oreFeatures",
            };

            foreach (string fieldName in directFields)
            {
                FieldInfo field = typeof(MinecraftCaveInfiniteWorld).GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                Assert.That(
                    field.GetCustomAttribute<SerializeField>(),
                    Is.Null,
                    fieldName + " must only be supplied by LevelConfiguration.");
            }
        }

        [Test]
        public void BuildStartsAtHomeAndIncludesInfiniteCaves()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

            Assert.That(scenes, Has.Length.GreaterThanOrEqualTo(2));
            Assert.That(scenes[0].enabled, Is.True);
            Assert.That(scenes[0].path, Is.EqualTo(ProjectAssetPaths.Scenes.Home));
            Assert.That(scenes[1].enabled, Is.True);
            Assert.That(scenes[1].path, Is.EqualTo(ProjectAssetPaths.Scenes.InfiniteCaves));
            Assert.That(
                scenes.Any(scene =>
                    scene.path == ProjectAssetPaths.Scenes.MainMenu && scene.enabled),
                Is.False,
                "The first-level loop must boot into Home instead of the old menu.");
        }

        private static void AssertDepthScaling(DepthProbabilityProfile profile)
        {
            Assert.That(profile, Is.Not.Null);
            Assert.That(
                profile.EvaluateProbability(0.5f, 32, 256),
                Is.GreaterThan(
                    profile.EvaluateProbability(0.5f, 224, 256)));
        }
    }
}
