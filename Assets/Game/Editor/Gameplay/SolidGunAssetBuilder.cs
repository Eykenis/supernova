#if UNITY_EDITOR
using System;
using Supernova.Audio;
using Supernova.Gameplay;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Supernova.Editor.Gameplay
{
    /// <summary>
    /// Builds the SolidGun projectile/configuration and registers the new tool on
    /// the player prefab. All asset locations come from ProjectAssetPaths.
    /// </summary>
    public static class SolidGunAssetBuilder
    {
        private const string SessionKey =
            "Supernova.SolidGunAssetBuilder.Ensured.V7";
        private const float ProjectileSpeed = 55f;
        private const float ActionCyclePeriod = 1f / 1.5f;
        private const int InitialAmmunition = 36;
        private const int PlatformDiameter = 5;
        private const float PlatformUnitSize = 0.42f;
        private const float PlatformThickness = 0.2f;
        private const float GrowthDuration = 0.6f;

        [InitializeOnLoadMethod]
        private static void ScheduleEnsureConfiguration()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;

            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += EnsureWhenReady;
        }

        [MenuItem("Tools/Supernova/Gameplay/Rebuild SolidGun Configuration")]
        public static void Rebuild()
        {
            PlayerToolDefinition definition = EnsureConfiguration(true);
            Selection.activeObject = definition;
            EditorGUIUtility.PingObject(definition);
            Debug.Log(
                "Rebuilt the SolidGun tool, projectile, and player registration.",
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
            PlayerToolDefinition definition =
                EnsureToolDefinition(projectile);
            EnsurePlayerRegistration(definition);
            AssetDatabase.SaveAssets();
            return definition;
        }

        private static GameObject EnsureProjectilePrefab(bool rebuild)
        {
            Material platformMaterial = EnsurePlatformMaterial();
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.SolidVoxelProjectile);
            if (existing != null
                && existing.GetComponent<SolidVoxelProjectile>() != null
                && existing.GetComponent<SolidVoxelProjectile>()
                    .ConfigurationVersion >= 5)
            {
                return existing;
            }
            throw new InvalidOperationException(
                "The centralized SolidGun projectile prefab is missing or "
                + "out of date.");
        }

        private static Material EnsurePlatformMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(
                ProjectAssetPaths.Materials.SolidPlatform);
            if (existing != null)
                return existing;

            VoxelTypeDefinition stone =
                AssetDatabase.LoadAssetAtPath<VoxelTypeDefinition>(
                    ProjectAssetPaths.Config.StoneVoxel);
            if (stone == null || stone.Material == null)
            {
                throw new InvalidOperationException(
                    "Cannot create the SolidGun platform material because "
                    + "the centralized Stone voxel material is missing.");
            }

            var material = new Material(stone.Material)
            {
                name = "SolidPlatform",
            };
            AssetDatabase.CreateAsset(
                material,
                ProjectAssetPaths.Materials.SolidPlatform);
            return material;
        }


        private static PlayerToolDefinition EnsureToolDefinition(
            GameObject projectilePrefab)
        {
            GameObject solidGun = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.SolidGun);
            AnimationClip fireAnimation =
                AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    ProjectAssetPaths.Animations.FireContinuous);
            GameObject muzzleFlash = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.MuzzleFlash);
            SoundEffectCue shotSound =
                AssetDatabase.LoadAssetAtPath<SoundEffectCue>(
                    ProjectAssetPaths.Config.SolidGunShotSound);
            SolidVoxelProjectile projectile = projectilePrefab != null
                ? projectilePrefab.GetComponent<SolidVoxelProjectile>()
                : null;
            if (solidGun == null
                || fireAnimation == null
                || muzzleFlash == null
                || shotSound == null
                || projectile == null)
            {
                throw new InvalidOperationException(
                    "Cannot configure SolidGun because a centralized model, "
                    + "animation, muzzle flash, or projectile is missing.");
            }

            PlayerToolDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.SolidGunTool);
            if (definition == null)
            {
                definition =
                    ScriptableObject.CreateInstance<PlayerToolDefinition>();
                definition.name = "SolidGunTool";
                AssetDatabase.CreateAsset(
                    definition,
                    ProjectAssetPaths.Config.SolidGunTool);
            }

            SerializedObject serialized = new SerializedObject(definition);
            SetInteger(
                serialized,
                "item",
                (int)PlayerInventoryItem.SolidGun);
            SetInteger(
                serialized,
                "primaryAction",
                (int)PlayerToolPrimaryAction.FireProjectile);
            SetReference(
                serialized,
                "primaryActionSound",
                shotSound);
            SetInteger(
                serialized,
                "animationTriggerMode",
                (int)PlayerToolAnimationTriggerMode.Continuous);
            SetReference(
                serialized,
                "primaryActionAnimation",
                fireAnimation);
            SetReference(serialized, "heldModelPrefab", solidGun);
            SetInteger(
                serialized,
                "heldModelMountStrategy",
                (int)HeldToolMountStrategy.TwoHanded);
            SetBoolean(serialized, "allowMovementWhileUsing", true);
            SetFloat(serialized, "actionTriggerDelay", 0f);
            SetFloat(serialized, "actionCyclePeriod", ActionCyclePeriod);
            SetBoolean(serialized, "actionIsPeriodic", true);
            SetReference(
                serialized,
                "firearmProjectilePrefab",
                projectile);
            SetFloat(serialized, "projectileSpeed", ProjectileSpeed);
            SetInteger(serialized, "initialAmmunition", InitialAmmunition);
            SetReference(
                serialized,
                "muzzleFlashPrefab",
                muzzleFlash);
            SetFloat(
                serialized,
                "muzzleFlashLifetime",
                0.75f);
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
                    "Cannot register SolidGun because the player prefab is missing.");
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
                bool alreadyRegistered = false;
                for (int i = 0; i < definitions.arraySize; i++)
                {
                    if (definitions.GetArrayElementAtIndex(i)
                            .objectReferenceValue == definition)
                    {
                        alreadyRegistered = true;
                        break;
                    }
                }

                if (alreadyRegistered)
                    return;

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

        private static void SetBoolean(
            SerializedObject serialized,
            string propertyName,
            bool value)
        {
            serialized.FindProperty(propertyName).boolValue = value;
        }
    }
}
#endif
