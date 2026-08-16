#if UNITY_EDITOR
using System;
using System.Linq;
using Supernova.Audio;
using Supernova.Infrastructure;
using Supernova.Missions;
using Supernova.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

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
        GameInputAssetBuilder.EnsureInputActionsAsset();
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
        SetReferenceArray<LevelConfiguration>(
            serialized,
            "missions.levels",
            ProjectAssetPaths.Config.FirstLevel,
            ProjectAssetPaths.Config.SecondLevel,
            ProjectAssetPaths.Config.ThirdLevel);
        SetReference<Font>(
            serialized,
            "missions.uiFont",
            ProjectAssetPaths.Ui.RuntimeFont);
        SetReference<InputActionAsset>(
            serialized,
            "input.actions",
            ProjectAssetPaths.Config.GameInputActions);
        SetReference<SoundEffectCue>(
            serialized,
            "audio.coinDeposit",
            ProjectAssetPaths.Config.CoinDepositSound);
        SetReference<SoundEffectCue>(
            serialized,
            "audio.caveAmbience",
            ProjectAssetPaths.Config.CaveAmbienceSound);
        SetReference<SoundEffectCue>(
            serialized,
            "audio.cashGrowing",
            ProjectAssetPaths.Config.CashGrowingSound);
        SetReference<SoundEffectCue>(
            serialized,
            "audio.missionStart",
            ProjectAssetPaths.Config.MissionStartSound);
        SetReference<SoundEffectCue>(
            serialized,
            "audio.missionReady",
            ProjectAssetPaths.Config.MissionReadySound);
        SetReference<SoundEffectCue>(
            serialized,
            "audio.creatureRun",
            ProjectAssetPaths.Config.CreatureRunSound);
        SetReference<SoundEffectCue>(
            serialized,
            "audio.creatureAttack",
            ProjectAssetPaths.Config.CreatureAttackSound);
        SetReference<SoundEffectCue>(
            serialized,
            "audio.creatureHitPlayer",
            ProjectAssetPaths.Config.CreatureHitPlayerSound);
        SetReference<SoundEffectCue>(
            serialized,
            "audio.playerFallSmall",
            ProjectAssetPaths.Config.PlayerFallSmallSound);
        SetReference<SoundEffectCue>(
            serialized,
            "audio.playerFallBig",
            ProjectAssetPaths.Config.PlayerFallBigSound);
        SetReference<SoundEffectCue>(
            serialized,
            "audio.bombFuse",
            ProjectAssetPaths.Config.BombFuseSound);
        SetReference<SoundEffectCue>(
            serialized,
            "audio.bombExplosion",
            ProjectAssetPaths.Config.BombExplosionSound);
        SetReference<Material>(
            serialized,
            "effects.collisionSmokeMaterial",
            ProjectAssetPaths.Materials.CollisionDust);
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
        EquipmentIconBaker.EnsureIconCatalogAsset();
        SetReference<EquipmentIconCatalog>(
            serialized,
            "ui.equipmentIcons",
            ProjectAssetPaths.Config.EquipmentIconCatalog);
        EnsureEquipmentPortraitSettingsAsset();
        SetReference<EquipmentPortraitSettings>(
            serialized,
            "ui.equipmentPortraitSettings",
            ProjectAssetPaths.Config.EquipmentPortraitSettings);
        SetReference<Material>(
            serialized,
            "ui.missionCellConsoleMaterial",
            ProjectAssetPaths.Materials.MissionCellConsole);
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
            ProjectAssetPaths.LookupNames.HomeScene);
        SetString(
            serialized,
            "sceneLookups.tutorialSceneName",
            ProjectAssetPaths.LookupNames.SpawnShelterStoneTestScene);
        SetString(
            serialized,
            "sceneLookups.missionCellObjectName",
            ProjectAssetPaths.LookupNames.MissionCell);
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

    private static void SetReferenceArray<T>(
        SerializedObject serialized,
        string propertyPath,
        params string[] assetPaths)
        where T : UnityEngine.Object
    {
        SerializedProperty property = serialized.FindProperty(propertyPath);
        if (property == null || !property.isArray)
            throw new InvalidOperationException(
                $"Catalog array property was not found: {propertyPath}");

        property.arraySize = assetPaths.Length;
        for (int i = 0; i < assetPaths.Length; i++)
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPaths[i]);
            if (asset == null)
                throw new InvalidOperationException(
                    $"Missing {typeof(T).Name} required by the game asset catalog: "
                    + assetPaths[i]);
            property.GetArrayElementAtIndex(i).objectReferenceValue = asset;
        }
    }

    private static void EnsureEquipmentPortraitSettingsAsset()
    {
        EquipmentPortraitSettings settings =
            AssetDatabase.LoadAssetAtPath<EquipmentPortraitSettings>(
                ProjectAssetPaths.Config.EquipmentPortraitSettings);
        if (settings != null)
            return;

        settings = ScriptableObject.CreateInstance<EquipmentPortraitSettings>();
        AssetDatabase.CreateAsset(
            settings,
            ProjectAssetPaths.Config.EquipmentPortraitSettings);
        SerializedObject serialized = new SerializedObject(settings);
        SetReference<AnimationClip>(
            serialized,
            "animationClip",
            ProjectAssetPaths.ThirdParty.WaitAnimation);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
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
