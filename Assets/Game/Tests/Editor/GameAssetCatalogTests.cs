using System;
using System.Linq;
using NUnit.Framework;
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
        Assert.That(
            PlayerSettings.GetPreloadedAssets().Contains(catalog),
            Is.True,
            "The runtime catalog must be loaded before scene bootstrap code runs.");
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

    private static readonly string[] RequiredAssetPaths =
    {
        ProjectAssetPaths.Config.GameAssetCatalog,
        ProjectAssetPaths.Config.UiDesignTokens,
        ProjectAssetPaths.Config.EquipmentIconCatalog,
        ProjectAssetPaths.Config.EquipmentPortraitSettings,
        ProjectAssetPaths.Config.FirstLevel,
        ProjectAssetPaths.Config.WorldGeneration,
        ProjectAssetPaths.Config.MonsterSpawnTable,
        ProjectAssetPaths.Config.TreasureSpawnTable,
        ProjectAssetPaths.Config.GunProduct,
        ProjectAssetPaths.Config.SmgProduct,
        ProjectAssetPaths.Config.FlashlightProduct,
        ProjectAssetPaths.Config.SolidGunProduct,
        ProjectAssetPaths.Config.AttractionModuleProduct,
        ProjectAssetPaths.Config.SmgTool,
        ProjectAssetPaths.Materials.ShopGeometryWireframeShader,
        ProjectAssetPaths.Materials.ShopGeometryWireframe,
        ProjectAssetPaths.Materials.SolidPlatform,
        ProjectAssetPaths.Materials.CaveTerrainPhysics,
        ProjectAssetPaths.Animations.PlayerController,
        ProjectAssetPaths.Animations.Mining,
        ProjectAssetPaths.Animations.Hover,
        ProjectAssetPaths.Animations.SciFiDoorController,
        ProjectAssetPaths.Animations.SciFiDoorOpen,
        ProjectAssetPaths.Prefabs.Player,
        ProjectAssetPaths.Prefabs.FlashlightProjectile,
        ProjectAssetPaths.Prefabs.AttractionModuleDisplay,
        ProjectAssetPaths.Prefabs.MainMenu,
        ProjectAssetPaths.Ui.PauseSettings,
        ProjectAssetPaths.Ui.PrimaryFrame,
        ProjectAssetPaths.Ui.TelemetryBackdrop,
        ProjectAssetPaths.Scenes.Home,
        ProjectAssetPaths.Scenes.InfiniteCaves,
    };
}
