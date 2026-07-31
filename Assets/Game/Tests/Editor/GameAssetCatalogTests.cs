using System.Linq;
using NUnit.Framework;
using Supernova.Infrastructure;
using UnityEditor;

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

    private static readonly string[] RequiredAssetPaths =
    {
        ProjectAssetPaths.Config.GameAssetCatalog,
        ProjectAssetPaths.Config.UiDesignTokens,
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
