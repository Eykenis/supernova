using Supernova.UI;
using Supernova.Infrastructure;

using UnityEditor;
using UnityEngine;

namespace Supernova.Editor.UI
{
    public static class SciFiUiAssetBuilder
    {
        private const string AssetFolder = ProjectAssetPaths.Folders.SciFiUi;

        [MenuItem("Supernova/UI/Rebuild Sci-Fi UI System")]
        public static void Rebuild()
        {
            ConfigureSprite("FramePrimary.png", new Vector4(78f, 62f, 78f, 62f));
            ConfigureSprite("FrameWide.png", new Vector4(72f, 58f, 72f, 58f));
            ConfigureSprite("FrameSlot.png", new Vector4(66f, 54f, 66f, 54f));
            ConfigureSprite("FrameThin.png", new Vector4(56f, 46f, 56f, 46f));
            ConfigureSprite("HudPanelClean.png", new Vector4(52f, 44f, 52f, 44f));
            ConfigureSprite("SlotClean.png", new Vector4(48f, 42f, 48f, 42f));
            ConfigureSprite("ButtonClean.png", new Vector4(44f, 36f, 44f, 36f));
            ConfigureSprite("ProgressClean.png", new Vector4(48f, 38f, 48f, 38f));
            ConfigureSprite("PauseCardClean.png", new Vector4(52f, 44f, 52f, 44f));
            ConfigureSprite("LoadingDial.png", Vector4.zero);
            ConfigureBackdrop("TelemetryBackdrop.jpg");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            MainMenuUguiPrefabBuilder.Rebuild();
            AssetDatabase.SaveAssets();

            GameAssetCatalog catalog = GameAssetCatalogBuilder.EnsureCatalog();
            if (!catalog.IsComplete)
                throw new UnityException(
                    "The centralized game asset catalog is incomplete after UI import.");

            Debug.Log(
                "Rebuilt the sci-fi UI system from Assets/UI/UIs. "
                + "Main menu, HUD, loading, and pause controls now share the same skin.");
        }

        private static void ConfigureSprite(string fileName, Vector4 border)
        {
            string path = AssetFolder + "/" + fileName;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new UnityException("Could not configure UI sprite: " + path);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = border;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }

        private static void ConfigureBackdrop(string fileName)
        {
            string path = AssetFolder + "/" + fileName;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new UnityException("Could not configure UI backdrop: " + path);

            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }
    }
}
