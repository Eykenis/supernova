#if UNITY_EDITOR
using System;
using System.Linq;
using Supernova.Infrastructure;
using Supernova.Missions;
using Supernova.UI;
using UnityEditor;
using UnityEngine;

public static class GameAssetCatalogBuilder
{
    [InitializeOnLoadMethod]
    private static void ScheduleEnsureCatalog()
    {
        EditorApplication.delayCall += () => EnsureCatalog();
    }

    [MenuItem("Tools/Supernova/Infrastructure/Rebuild Game Asset Catalog")]
    public static void RebuildCatalog()
    {
        GameAssetCatalog catalog = EnsureCatalog();
        Selection.activeObject = catalog;
        EditorGUIUtility.PingObject(catalog);
        Debug.Log("Rebuilt and preloaded the centralized game asset catalog.", catalog);
    }

    public static GameAssetCatalog EnsureCatalog()
    {
        GameAssetCatalog catalog =
            AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(
                ProjectAssetPaths.Config.GameAssetCatalog);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<GameAssetCatalog>();
            AssetDatabase.CreateAsset(
                catalog,
                ProjectAssetPaths.Config.GameAssetCatalog);
        }

        SerializedObject serialized = new SerializedObject(catalog);
        SetReference<LevelConfiguration>(
            serialized,
            "missions.defaultLevel",
            ProjectAssetPaths.Config.FirstLevel);
        SetReference<Font>(
            serialized,
            "missions.uiFont",
            ProjectAssetPaths.Ui.RuntimeFont);
        SetReference<GameObject>(
            serialized,
            "ui.mainMenuPrefab",
            ProjectAssetPaths.Prefabs.MainMenu);
        SetReference<UiDesignTokens>(
            serialized,
            "ui.designTokens",
            ProjectAssetPaths.Config.UiDesignTokens);
        SetReference<PausePortraitSettings>(
            serialized,
            "ui.pausePortraitSettings",
            ProjectAssetPaths.Ui.PauseSettings);
        SetReference<Material>(
            serialized,
            "ui.pauseBodyMaterial",
            ProjectAssetPaths.Ui.PauseBodyMaterial);
        SetReference<Material>(
            serialized,
            "ui.pauseBackgroundMaterial",
            ProjectAssetPaths.Ui.PauseBackgroundMaterial);
        SetReference<Sprite>(
            serialized,
            "ui.primaryFrame",
            ProjectAssetPaths.Ui.PrimaryFrame);
        SetReference<Sprite>(
            serialized,
            "ui.wideFrame",
            ProjectAssetPaths.Ui.WideFrame);
        SetReference<Sprite>(
            serialized,
            "ui.slotFrame",
            ProjectAssetPaths.Ui.SlotFrame);
        SetReference<Sprite>(
            serialized,
            "ui.thinFrame",
            ProjectAssetPaths.Ui.ThinFrame);
        SetReference<Sprite>(
            serialized,
            "ui.hudPanelFrame",
            ProjectAssetPaths.Ui.HudPanel);
        SetReference<Sprite>(
            serialized,
            "ui.slotCleanFrame",
            ProjectAssetPaths.Ui.SlotClean);
        SetReference<Sprite>(
            serialized,
            "ui.buttonCleanFrame",
            ProjectAssetPaths.Ui.ButtonClean);
        SetReference<Sprite>(
            serialized,
            "ui.progressCleanFrame",
            ProjectAssetPaths.Ui.ProgressClean);
        SetReference<Sprite>(
            serialized,
            "ui.pauseCardFrame",
            ProjectAssetPaths.Ui.PauseCard);
        SetReference<Sprite>(
            serialized,
            "ui.loadingDial",
            ProjectAssetPaths.Ui.LoadingDial);
        SetReference<Texture2D>(
            serialized,
            "ui.telemetryBackdrop",
            ProjectAssetPaths.Ui.TelemetryBackdrop);
        SetString(
            serialized,
            "sceneLookups.mainMenuSceneName",
            ProjectAssetPaths.LookupNames.MainMenuScene);
        SetString(
            serialized,
            "sceneLookups.missionCellObjectName",
            ProjectAssetPaths.LookupNames.MissionCell);
        SetString(
            serialized,
            "sceneLookups.authoredCartObjectName",
            ProjectAssetPaths.LookupNames.AuthoredCart);
        SetString(
            serialized,
            "sceneLookups.pausePoseStateName",
            ProjectAssetPaths.LookupNames.PausePoseState);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EnsurePreloaded(catalog);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        return catalog;
    }

    private static void SetReference<T>(
        SerializedObject serialized,
        string propertyPath,
        string assetPath)
        where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (asset == null)
            throw new InvalidOperationException(
                $"Missing {typeof(T).Name} required by the game asset catalog: {assetPath}");
        SerializedProperty property = serialized.FindProperty(propertyPath);
        if (property == null)
            throw new InvalidOperationException(
                $"Catalog property was not found: {propertyPath}");
        property.objectReferenceValue = asset;
    }

    private static void SetString(
        SerializedObject serialized,
        string propertyPath,
        string value)
    {
        SerializedProperty property = serialized.FindProperty(propertyPath);
        if (property == null)
            throw new InvalidOperationException(
                $"Catalog property was not found: {propertyPath}");
        property.stringValue = value;
    }

    private static void EnsurePreloaded(GameAssetCatalog catalog)
    {
        UnityEngine.Object[] preloaded = PlayerSettings.GetPreloadedAssets()
            .Where(asset => asset != null && !(asset is GameAssetCatalog))
            .Concat(new UnityEngine.Object[] { catalog })
            .ToArray();
        PlayerSettings.SetPreloadedAssets(preloaded);
    }
}
#endif
