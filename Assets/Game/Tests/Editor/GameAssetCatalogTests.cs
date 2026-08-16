using System;
using System.Linq;
using NUnit.Framework;
using Supernova.Audio;
using Supernova.Gameplay;
using Supernova.Infrastructure;
using Supernova.UI;
using UnityEditor;
using UnityEngine;

public sealed class GameAssetCatalogTests
{
    [Test]
    public void Catalog_IsCompleteAndPreloaded()
    {
        GameAssetCatalog catalog =
            AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(
                ProjectAssetPaths.Config.GameAssetCatalog);

        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.IsComplete, Is.True);
        Assert.That(catalog.Effects, Is.Not.Null);
        Assert.That(catalog.Effects.CollisionSmokeMaterial, Is.Not.Null);
        Assert.That(
            catalog.Effects.CollisionSmokeMaterial.shader.name,
            Is.EqualTo("Supernova/Effects/Collision Dust"));
        Assert.That(
            PlayerSettings.GetPreloadedAssets().Contains(catalog),
            Is.True,
            "The runtime catalog must be loaded before scene bootstrap code runs.");
    }

    [Test]
    public void MainMenuDestination_UsesTheDefaultLevelHomeScene()
    {
        GameAssetCatalog catalog =
            AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(
                ProjectAssetPaths.Config.GameAssetCatalog);

        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.Missions.DefaultLevel, Is.Not.Null);
        Assert.That(
            catalog.SceneLookups.MainMenuSceneName,
            Is.EqualTo(catalog.Missions.DefaultLevel.HomeSceneName));
        Assert.That(
            catalog.SceneLookups.MainMenuSceneName,
            Is.EqualTo(ProjectAssetPaths.LookupNames.HomeScene));
    }

    [Test]
    public void TutorialDestination_UsesTheConfiguredSpawnShelterScene()
    {
        GameAssetCatalog catalog =
            AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(
                ProjectAssetPaths.Config.GameAssetCatalog);

        Assert.That(catalog, Is.Not.Null);
        Assert.That(
            catalog.SceneLookups.TutorialSceneName,
            Is.EqualTo(ProjectAssetPaths.LookupNames.SpawnShelterStoneTestScene));
        Assert.That(
            AssetDatabase.LoadAssetAtPath<SceneAsset>(
                ProjectAssetPaths.Scenes.SpawnShelterStoneTest),
            Is.Not.Null);
    }

    [TestCaseSource(nameof(RequiredAssetPaths))]
    public void RequiredCentralizedPath_Resolves(string assetPath)
    {
        Assert.That(
            AssetDatabase.LoadMainAssetAtPath(assetPath),
            Is.Not.Null,
            assetPath);
    }

    [Test]
    public void EquipmentIconCatalog_ContainsEveryEquipmentThumbnail()
    {
        EquipmentIconCatalog icons =
            AssetDatabase.LoadAssetAtPath<EquipmentIconCatalog>(
                ProjectAssetPaths.Config.EquipmentIconCatalog);

        Assert.That(icons, Is.Not.Null);
        PlayerInventoryItem[] equipmentItems =
            Enum.GetValues(typeof(PlayerInventoryItem))
                .Cast<PlayerInventoryItem>()
                .Where(item => item != PlayerInventoryItem.Empty)
                .GroupBy(item => (int)item)
                .Select(group => group.First())
                .ToArray();
        foreach (PlayerInventoryItem item in equipmentItems)
        {
            Sprite icon = icons.GetIcon(item);
            Assert.That(icon, Is.Not.Null, item.ToString());
            Assert.That(icon.texture.width, Is.EqualTo(384), item.ToString());
            Assert.That(icon.texture.height, Is.EqualTo(384), item.ToString());
        }
    }

    [Test]
    public void EquipmentPortraitSettings_UsesIndependentAnimationClip()
    {
        EquipmentPortraitSettings settings =
            AssetDatabase.LoadAssetAtPath<EquipmentPortraitSettings>(
                ProjectAssetPaths.Config.EquipmentPortraitSettings);

        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.AnimationClips, Is.Not.Null);
    }

    [Test]
    public void MissionAudioCues_UseExpectedSoundEffectClips()
    {
        GameAssetCatalog catalog =
            AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(
                ProjectAssetPaths.Config.GameAssetCatalog);

        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.Audio, Is.Not.Null);
        Assert.That(catalog.Audio.IsComplete, Is.True);
        AssertCueClips(
            catalog.Audio.CoinDeposit,
            ProjectAssetPaths.Audio.Coin1,
            ProjectAssetPaths.Audio.Coin2);
        AssertCueClips(
            catalog.Audio.CaveAmbience,
            ProjectAssetPaths.Audio.Ambience);
        AssertCueClips(
            catalog.Audio.CashGrowing,
            ProjectAssetPaths.Audio.CashGrowing);
        AssertCueClips(
            catalog.Audio.MissionStart,
            ProjectAssetPaths.Audio.Start);
        AssertCueClips(
            catalog.Audio.MissionReady,
            ProjectAssetPaths.Audio.Ready);
    }

    private static void AssertCueClips(
        SoundEffectCue cue,
        params string[] expectedClipPaths)
    {
        Assert.That(cue, Is.Not.Null);
        SerializedObject serializedCue = new SerializedObject(cue);
        SerializedProperty clips = serializedCue.FindProperty("clips");
        Assert.That(clips.arraySize, Is.EqualTo(expectedClipPaths.Length));
        for (int i = 0; i < expectedClipPaths.Length; i++)
        {
            AudioClip expected = AssetDatabase.LoadAssetAtPath<AudioClip>(
                expectedClipPaths[i]);
            Assert.That(expected, Is.Not.Null, expectedClipPaths[i]);
            Assert.That(
                clips.GetArrayElementAtIndex(i).objectReferenceValue,
                Is.SameAs(expected));
        }
    }

    private static readonly string[] RequiredAssetPaths =
    {
        ProjectAssetPaths.Config.GameAssetCatalog,
        ProjectAssetPaths.Config.CoinDepositSound,
        ProjectAssetPaths.Config.CaveAmbienceSound,
        ProjectAssetPaths.Config.CashGrowingSound,
        ProjectAssetPaths.Config.MissionStartSound,
        ProjectAssetPaths.Config.MissionReadySound,
        ProjectAssetPaths.Config.UiDesignTokens,
        ProjectAssetPaths.Config.EquipmentIconCatalog,
        ProjectAssetPaths.Config.EquipmentPortraitSettings,
        ProjectAssetPaths.Config.FirstLevel,
        ProjectAssetPaths.Config.SecondLevel,
        ProjectAssetPaths.Config.ThirdLevel,
        ProjectAssetPaths.Config.WorldGeneration,
        ProjectAssetPaths.Config.MonsterSpawnTable,
        ProjectAssetPaths.Config.TreasureSpawnTable,
        ProjectAssetPaths.Config.FlashlightProduct,
        ProjectAssetPaths.Config.SolidGunProduct,
        ProjectAssetPaths.Config.PortalGunProduct,
        ProjectAssetPaths.Config.SolidGunTool,
        ProjectAssetPaths.Config.PortalGunTool,
        ProjectAssetPaths.Materials.ShopGeometryWireframeShader,
        ProjectAssetPaths.Materials.ShopGeometryWireframe,
        ProjectAssetPaths.Shaders.MagnetEnergyRibbon,
        ProjectAssetPaths.Materials.MagnetEnergyRibbon,
        ProjectAssetPaths.Materials.CollisionDust,
        ProjectAssetPaths.Materials.SolidPlatform,
        ProjectAssetPaths.Materials.MissionCellConsole,
        ProjectAssetPaths.Materials.CaveTerrainPhysics,
        ProjectAssetPaths.Animations.PlayerController,
        ProjectAssetPaths.Animations.Mining,
        ProjectAssetPaths.Animations.Hover,
        ProjectAssetPaths.Animations.SciFiDoorController,
        ProjectAssetPaths.Animations.SciFiDoorOpen,
        ProjectAssetPaths.Prefabs.Player,
        ProjectAssetPaths.Prefabs.FlashlightProjectile,
        ProjectAssetPaths.Prefabs.SolidGun,
        ProjectAssetPaths.Prefabs.PortalGun,
        ProjectAssetPaths.Prefabs.MainMenu,
        ProjectAssetPaths.Ui.PauseSettings,
        ProjectAssetPaths.Ui.PrimaryFrame,
        ProjectAssetPaths.Ui.TelemetryBackdrop,
        ProjectAssetPaths.Scenes.Home,
        ProjectAssetPaths.Scenes.InfiniteCaves,
    };
}
