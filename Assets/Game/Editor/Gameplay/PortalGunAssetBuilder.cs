#if UNITY_EDITOR
using System;
using Supernova.Audio;
using Supernova.Gameplay;
using Supernova.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Supernova.Editor.Gameplay
{
    /// <summary>
    /// Builds PortalGun ammunition and configuration from the existing SolidGun
    /// assets, then registers the tool on the player prefab.
    /// </summary>
    public static class PortalGunAssetBuilder
    {
        private const string SessionKey =
            "Supernova.PortalGunAssetBuilder.Ensured.V2";
        private const int ConfigurationVersion = 1;

        [InitializeOnLoadMethod]
        private static void ScheduleEnsureConfiguration()
        {
            if (SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += EnsureWhenReady;
        }

        [MenuItem("Tools/Supernova/Gameplay/Rebuild PortalGun Configuration")]
        public static void Rebuild()
        {
            PlayerToolDefinition definition = EnsureConfiguration(true);
            Selection.activeObject = definition;
            EditorGUIUtility.PingObject(definition);
            Debug.Log(
                "Rebuilt the PortalGun tool, projectile, and player registration.",
                definition);
        }

        private static void EnsureWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureWhenReady;
                return;
            }

            try
            {
                EnsureConfiguration(false);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static PlayerToolDefinition EnsureConfiguration(bool rebuild)
        {
            GameObject projectile = EnsureProjectilePrefab(rebuild);
            PlayerToolDefinition definition = EnsureToolDefinition(projectile);
            EnsurePlayerRegistration(definition);
            EnsureIconRegistration();
            AssetDatabase.SaveAssets();
            return definition;
        }

        private static GameObject EnsureProjectilePrefab(bool rebuild)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.PortalGunProjectile);
            PortalGunProjectile existingProjectile = existing != null
                ? existing.GetComponent<PortalGunProjectile>()
                : null;
            if (!rebuild
                && existingProjectile != null
                && existingProjectile.ConfigurationVersion
                    >= ConfigurationVersion)
            {
                return existing;
            }

            GameObject source = PrefabUtility.LoadPrefabContents(
                ProjectAssetPaths.Prefabs.SolidVoxelProjectile);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Cannot build PortalGun ammunition because the centralized "
                    + "SolidGun projectile prefab is missing.");
            }

            try
            {
                source.name = "PortalGunProjectile";
                BallisticProjectile sourceProjectile =
                    source.GetComponent<BallisticProjectile>();
                if (sourceProjectile != null)
                {
                    Object.DestroyImmediate(sourceProjectile);
                }

                PortalGunProjectile projectile =
                    source.AddComponent<PortalGunProjectile>();
                SerializedObject serialized = new SerializedObject(projectile);
                SetReference(
                    serialized,
                    "body",
                    source.GetComponent<Rigidbody>());
                SetFloat(serialized, "damage", 0f);
                SetFloat(serialized, "treasureImpulseMultiplier", 0f);
                SetFloat(serialized, "maximumLifetime", 5f);
                SetInteger(
                    serialized,
                    "configurationVersion",
                    ConfigurationVersion);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    source,
                    ProjectAssetPaths.Prefabs.PortalGunProjectile);
                if (saved == null)
                {
                    throw new InvalidOperationException(
                        "Unity could not save the PortalGun projectile prefab.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(source);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.PortalGunProjectile);
        }

        private static PlayerToolDefinition EnsureToolDefinition(
            GameObject projectilePrefab)
        {
            PlayerToolDefinition solidGun =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.SolidGunTool);
            GameObject portalGunModel =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    ProjectAssetPaths.Prefabs.PortalGun);
            SoundEffectCue shotSound =
                AssetDatabase.LoadAssetAtPath<SoundEffectCue>(
                    ProjectAssetPaths.Config.PortalGunShotSound);
            PortalGunProjectile projectile = projectilePrefab != null
                ? projectilePrefab.GetComponent<PortalGunProjectile>()
                : null;
            if (solidGun == null
                || portalGunModel == null
                || shotSound == null
                || projectile == null)
            {
                throw new InvalidOperationException(
                    "Cannot configure PortalGun because its SolidGun source "
                    + "configuration or generated projectile is missing.");
            }

            PlayerToolDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.PortalGunTool);
            if (definition == null)
            {
                definition =
                    ScriptableObject.CreateInstance<PlayerToolDefinition>();
                AssetDatabase.CreateAsset(
                    definition,
                    ProjectAssetPaths.Config.PortalGunTool);
            }

            EditorUtility.CopySerialized(solidGun, definition);
            definition.name = "PortalGunTool";
            SerializedObject serialized = new SerializedObject(definition);
            SetInteger(serialized, "item", (int)PlayerInventoryItem.PortalGun);
            serialized.FindProperty("primaryActionHint").stringValue = "发射传送门";
            SetReference(
                serialized,
                "primaryActionSound",
                shotSound);
            SetReference(
                serialized,
                "heldModelPrefab",
                portalGunModel);
            SetReference(
                serialized,
                "firearmProjectilePrefab",
                projectile);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static void EnsurePlayerRegistration(
            PlayerToolDefinition definition)
        {
            GameObject playerRoot = PrefabUtility.LoadPrefabContents(
                ProjectAssetPaths.Prefabs.Player);
            if (playerRoot == null)
            {
                throw new InvalidOperationException(
                    "Cannot register PortalGun because the player prefab is missing.");
            }

            try
            {
                PlayerToolController controller =
                    playerRoot.GetComponent<PlayerToolController>();
                if (controller == null)
                {
                    throw new InvalidOperationException(
                        "The player prefab has no PlayerToolController.");
                }

                SerializedObject serialized = new SerializedObject(controller);
                SerializedProperty definitions =
                    serialized.FindProperty("toolDefinitions");
                for (int i = 0; i < definitions.arraySize; i++)
                {
                    if (definitions.GetArrayElementAtIndex(i)
                            .objectReferenceValue == definition)
                    {
                        return;
                    }
                }

                int index = definitions.arraySize;
                definitions.InsertArrayElementAtIndex(index);
                definitions.GetArrayElementAtIndex(index)
                    .objectReferenceValue = definition;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(
                    playerRoot,
                    ProjectAssetPaths.Prefabs.Player);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(playerRoot);
            }
        }

        private static void EnsureIconRegistration()
        {
            EquipmentIconCatalog catalog =
                AssetDatabase.LoadAssetAtPath<EquipmentIconCatalog>(
                    ProjectAssetPaths.Config.EquipmentIconCatalog);
            if (catalog == null)
            {
                throw new InvalidOperationException(
                    "Cannot register the PortalGun icon because the centralized "
                    + "equipment icon catalog is missing.");
            }

            Sprite solidGunIcon = catalog.GetIcon(
                PlayerInventoryItem.SolidGun);
            if (solidGunIcon == null)
            {
                throw new InvalidOperationException(
                    "Cannot register the PortalGun icon because the SolidGun "
                    + "icon is missing from the equipment icon catalog.");
            }

            SerializedObject serialized = new SerializedObject(catalog);
            SerializedProperty entries = serialized.FindProperty("entries");
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("item").intValue
                    != (int)PlayerInventoryItem.PortalGun)
                {
                    continue;
                }

                entry.FindPropertyRelative("icon").objectReferenceValue =
                    solidGunIcon;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(catalog);
                return;
            }

            int index = entries.arraySize;
            entries.InsertArrayElementAtIndex(index);
            SerializedProperty portalGunEntry =
                entries.GetArrayElementAtIndex(index);
            portalGunEntry.FindPropertyRelative("item").intValue =
                (int)PlayerInventoryItem.PortalGun;
            portalGunEntry.FindPropertyRelative("icon").objectReferenceValue =
                solidGunIcon;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void SetReference(
            SerializedObject serialized,
            string propertyName,
            Object value)
        {
            serialized.FindProperty(propertyName).objectReferenceValue = value;
        }

        private static void SetFloat(
            SerializedObject serialized,
            string propertyName,
            float value)
        {
            serialized.FindProperty(propertyName).floatValue = value;
        }

        private static void SetInteger(
            SerializedObject serialized,
            string propertyName,
            int value)
        {
            serialized.FindProperty(propertyName).intValue = value;
        }
    }
}
#endif
