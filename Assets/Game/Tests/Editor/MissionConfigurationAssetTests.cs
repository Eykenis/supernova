using System.Linq;
using NUnit.Framework;
using Supernova.Missions;
using UnityEditor;

namespace Supernova.Tests
{
    public sealed class MissionConfigurationAssetTests
    {
        private const string MissionPath =
            "Assets/Game/Resources/Missions/FirstMission.asset";

        [Test]
        public void FirstMission_DefinesLevelOneQuotaTimerAndScenes()
        {
            MissionDefinition mission =
                AssetDatabase.LoadAssetAtPath<MissionDefinition>(MissionPath);

            Assert.That(mission, Is.Not.Null);
            Assert.That(mission.LevelNumber, Is.EqualTo(1));
            Assert.That(mission.DisplayName, Is.EqualTo("FIRST DESCENT"));
            Assert.That(mission.TimeLimitSeconds, Is.EqualTo(300f));
            Assert.That(mission.RequiredValue, Is.EqualTo(100));
            Assert.That(mission.OreUnitValue, Is.EqualTo(10));
            Assert.That(mission.HomeSceneName, Is.EqualTo("Home"));
            Assert.That(mission.CaveSceneName, Is.EqualTo("InfiniteCaves"));
        }

        [Test]
        public void BuildStartsAtHomeAndIncludesInfiniteCaves()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

            Assert.That(scenes, Has.Length.GreaterThanOrEqualTo(2));
            Assert.That(scenes[0].enabled, Is.True);
            Assert.That(scenes[0].path, Is.EqualTo("Assets/Scenes/Home.scene"));
            Assert.That(scenes[1].enabled, Is.True);
            Assert.That(scenes[1].path, Is.EqualTo("Assets/Scenes/InfiniteCaves.scene"));
            Assert.That(
                scenes.Any(scene =>
                    scene.path == "Assets/Scenes/MainMenu.unity" && scene.enabled),
                Is.False,
                "The first-level loop must boot into Home instead of the old menu.");
        }
    }
}
