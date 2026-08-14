using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.Infrastructure;
using Supernova.MinecraftCaves;
using Supernova.MinecraftCaves.Creatures;
using Supernova.Missions;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class MissionConfigurationAssetTests
    {
        private static readonly string[] LevelPaths =
        {
            ProjectAssetPaths.Config.FirstLevel,
            ProjectAssetPaths.Config.SecondLevel,
            ProjectAssetPaths.Config.ThirdLevel,
        };
        private const string WorldGenerationPath =
            ProjectAssetPaths.Config.WorldGeneration;

        [Test]
        public void FirstLevel_ComposesAllGenerationAndEvacuationConfiguration()
        {
            LevelConfiguration level =
                AssetDatabase.LoadAssetAtPath<LevelConfiguration>(LevelPaths[0]);
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
            Assert.That(level.MissionTimeLimitSeconds, Is.EqualTo(180f));
            Assert.That(level.WorldSeed, Is.EqualTo(6667));
            Assert.That(level.RequiredFunds, Is.EqualTo(200));
            Assert.That(level.HomeSceneName, Is.EqualTo("Home"));
            Assert.That(level.CaveSceneName, Is.EqualTo("DenseJigsawRegion"));
        }

        [Test]
        public void FirstLevel_UsesThreeMinuteMissionCountdown()
        {
            LevelConfiguration level =
                AssetDatabase.LoadAssetAtPath<LevelConfiguration>(LevelPaths[0]);

            Assert.That(level, Is.Not.Null);
            Assert.That(level.MissionTimeLimitSeconds, Is.EqualTo(180f));
        }

        [Test]
        public void Campaign_ContainsThreeOrderedLevelsWithDistinctSeedsAndFunds()
        {
            LevelConfiguration[] levels = LevelPaths
                .Select(AssetDatabase.LoadAssetAtPath<LevelConfiguration>)
                .ToArray();
            GameAssetCatalog catalog =
                AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(
                    ProjectAssetPaths.Config.GameAssetCatalog);

            Assert.That(levels, Has.All.Not.Null);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Missions.Levels, Has.Count.EqualTo(3));
            Assert.That(catalog.Missions.DefaultLevel, Is.SameAs(levels[0]));

            for (int i = 0; i < levels.Length; i++)
            {
                Assert.That(levels[i].LevelNumber, Is.EqualTo(i + 1));
                Assert.That(levels[i].HasCompleteGenerationConfiguration, Is.True);
                Assert.That(catalog.Missions.Levels[i], Is.SameAs(levels[i]));
            }

            Assert.That(levels.Select(level => level.WorldSeed).Distinct().Count(),
                Is.EqualTo(levels.Length));
            Assert.That(levels.Select(level => level.RequiredFunds).Distinct().Count(),
                Is.EqualTo(levels.Length));
        }

        [Test]
        public void CampaignProgress_SuccessAdvancesAndFailureRetriesCurrentLevel()
        {
            LevelConfiguration[] levels = LevelPaths
                .Select(AssetDatabase.LoadAssetAtPath<LevelConfiguration>)
                .ToArray();
            var progress = new MissionCampaignProgress(levels, levels[0]);

            Assert.That(progress.CurrentLevel, Is.SameAs(levels[0]));
            Assert.That(progress.RecordOutcome(MissionOutcome.Fired), Is.False);
            Assert.That(progress.CurrentLevel, Is.SameAs(levels[0]));

            Assert.That(progress.RecordOutcome(MissionOutcome.Success), Is.True);
            Assert.That(progress.CurrentLevel, Is.SameAs(levels[1]));
            Assert.That(progress.RecordOutcome(MissionOutcome.LostInCaves), Is.False);
            Assert.That(progress.CurrentLevel, Is.SameAs(levels[1]));

            Assert.That(progress.RecordOutcome(MissionOutcome.Success), Is.True);
            Assert.That(progress.CurrentLevel, Is.SameAs(levels[2]));
            Assert.That(progress.RecordOutcome(MissionOutcome.Success), Is.False);
            Assert.That(progress.CurrentLevel, Is.SameAs(levels[2]));
            Assert.That(progress.IsComplete, Is.True);
        }

        [Test]
        public void EachLevel_OverridesTheSharedWorldConfigurationSeed()
        {
            LevelConfiguration[] levels = LevelPaths
                .Select(path =>
                    AssetDatabase.LoadAssetAtPath<LevelConfiguration>(path))
                .ToArray();

            foreach (LevelConfiguration level in levels)
            {
                var worldObject = new GameObject("Level Seed World");
                try
                {
                    MinecraftCaveInfiniteWorld world =
                        worldObject.AddComponent<MinecraftCaveInfiniteWorld>();
                    Assert.That(world.ApplyLevelConfiguration(level), Is.True);
                    FieldInfo seedField = typeof(MinecraftCaveInfiniteWorld).GetField(
                        "worldSeed",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    Assert.That(seedField, Is.Not.Null);
                    Assert.That(seedField.GetValue(world), Is.EqualTo(level.WorldSeed));
                }
                finally
                {
                    Object.DestroyImmediate(worldObject);
                }
            }
        }

        [Test]
        public void DefaultWorldGeneration_ContainsCaveVoxelAndRuntimeParameters()
        {
            MinecraftWorldGenerationConfiguration configuration =
                AssetDatabase.LoadAssetAtPath<
                    MinecraftWorldGenerationConfiguration>(
                    WorldGenerationPath);

            Assert.That(configuration, Is.Not.Null);
            Assert.That(configuration.WorldSeed, Is.EqualTo(116));
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
            Assert.That(configuration.MaxConcurrentGenerationJobs, Is.EqualTo(1));
            Assert.That(configuration.MeshesBuiltPerFrame, Is.EqualTo(1));
            AssertDepthScaling(configuration.OreDepthProbability);
            AssertDepthScaling(configuration.TreasureDepthProbability);
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
        public void BuildStartsAtHomeAndIncludesDefaultCaveScenes()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

            Assert.That(scenes, Has.Length.GreaterThanOrEqualTo(2));
            // The game boots into the Home scene at build index 0.
            Assert.That(scenes[0].enabled, Is.True);
            Assert.That(scenes[0].path, Is.EqualTo(ProjectAssetPaths.Scenes.Home));
            // DenseJigsawRegion is the default mission cave (FirstLevel) and must
            // be loadable at runtime by name.
            Assert.That(
                scenes.Any(scene =>
                    scene.path == ProjectAssetPaths.Scenes.DenseJigsawRegion
                    && scene.enabled),
                Is.True,
                "The default mission cave scene (DenseJigsawRegion) must be "
                + "enabled in the build.");
            // InfiniteCaves remains a valid cave scene for other levels
            // (e.g. SecondLevel) and must stay loadable.
            Assert.That(
                scenes.Any(scene =>
                    scene.path == ProjectAssetPaths.Scenes.InfiniteCaves
                    && scene.enabled),
                Is.True,
                "InfiniteCaves must remain enabled for non-default levels.");
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
