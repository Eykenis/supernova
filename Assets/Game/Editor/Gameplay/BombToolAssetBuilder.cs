#if UNITY_EDITOR
using System;
using Supernova.Gameplay;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Supernova.Editor.Gameplay
{
    /// <summary>
    /// Builds and registers the project-owned Bomb tool. Every asset location is
    /// resolved through <see cref="ProjectAssetPaths"/>.
    /// </summary>
    public static class BombToolAssetBuilder
    {
        private const string SessionKey =
            "Supernova.BombToolAssetBuilder.Ensured.V4";

        [InitializeOnLoadMethod]
        private static void ScheduleEnsureConfiguration()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;

            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += EnsureWhenReady;
        }

        [MenuItem("Tools/Supernova/Gameplay/Rebuild Bomb Tool Assets")]
        public static void Rebuild()
        {
            PlayerToolDefinition definition = EnsureConfiguration(true);
            Selection.activeObject = definition;
            EditorGUIUtility.PingObject(definition);
            Debug.Log(
                "Rebuilt the Bomb tool, projectile, held model, and player registration.",
                definition);
        }

        private static void EnsureWhenReady()
        {
            if (EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode)
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
            EnsureAssetFolder(ProjectAssetPaths.Folders.BombPrefabs);
            EnsureAssetFolder(ProjectAssetPaths.Folders.ExplosionEffectPrefabs);
            GameObject explosionEffect =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    ProjectAssetPaths.Prefabs.BombExplosionEffect);
            if (explosionEffect == null)
            {
                throw new InvalidOperationException(
                    "Cannot configure Bomb because its game-owned explosion "
                    + "effect is missing: "
                    + ProjectAssetPaths.Prefabs.BombExplosionEffect);
            }
            Material material = EnsureBombMaterial();
            GameObject heldModel = EnsureHeldModel(material, rebuild);
            BombProjectile projectile = EnsureProjectile(
                material,
                explosionEffect,
                rebuild);
            PlayerToolDefinition definition = EnsureDefinition(
                heldModel,
                projectile,
                explosionEffect);
            EnsurePlayerRegistration(definition);
            AssetDatabase.SaveAssets();
            return definition;
        }

        private static Material EnsureBombMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(
                ProjectAssetPaths.Materials.BombBody);
            if (existing != null)
                return existing;

            Material source = AssetDatabase.LoadAssetAtPath<Material>(
                ProjectAssetPaths.Materials.FlashlightBody);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Cannot create the Bomb material because the centralized "
                    + "Flashlight body material is missing: "
                    + ProjectAssetPaths.Materials.FlashlightBody);
            }

            var material = new Material(source)
            {
                name = "BombBody",
                color = new Color(0.16f, 0.22f, 0.08f, 1f),
            };
            AssetDatabase.CreateAsset(
                material,
                ProjectAssetPaths.Materials.BombBody);
            return material;
        }

        private static GameObject EnsureHeldModel(
            Material material,
            bool rebuild)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.BombHeld);
            if (existing != null && !rebuild)
                return existing;

            var root = new GameObject("BombHeld");
            try
            {
                AddBombVisual(root.transform, material);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    ProjectAssetPaths.Prefabs.BombHeld);
                if (saved == null)
                {
                    throw new InvalidOperationException(
                        "Failed to save Bomb held model: "
                        + ProjectAssetPaths.Prefabs.BombHeld);
                }
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static BombProjectile EnsureProjectile(
            Material material,
            GameObject explosionEffect,
            bool rebuild)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.BombProjectile);
            BombProjectile existingProjectile = existing != null
                ? existing.GetComponent<BombProjectile>()
                : null;
            if (!rebuild
                && existingProjectile != null
                && existingProjectile.ConfigurationVersion >= 4)
            {
                return existingProjectile;
            }

            var root = new GameObject("BombProjectile");
            try
            {
                AddBombVisual(root.transform, material);
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.mass = 0.75f;
                body.drag = 0.15f;
                body.angularDrag = 0.1f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode =
                    CollisionDetectionMode.ContinuousDynamic;
                SphereCollider collider = root.AddComponent<SphereCollider>();
                collider.radius = 0.2f;

                BombProjectile projectile = root.AddComponent<BombProjectile>();
                SerializedObject serialized = new SerializedObject(projectile);
                SetReference(serialized, "body", body);
                SetFloat(serialized, "fuseSeconds", 2f);
                SetFloat(serialized, "explosionRadius", 2f);
                SetFloat(serialized, "innerRadius", 1f);
                SetFloat(serialized, "innerMiningPower", 30f);
                SetFloat(serialized, "outerMiningPower", 10f);
                SetFloat(serialized, "propagationDivisor", 2f);
                SetFloat(serialized, "entityExplosionImpulse", 240f);
                SetFloat(serialized, "entityUpwardModifier", 0.6f);
                SetReference(
                    serialized,
                    "explosionEffectPrefab",
                    explosionEffect);
                SetFloat(serialized, "explosionEffectLifetime", 3f);
                SetInteger(serialized, "configurationVersion", 4);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    ProjectAssetPaths.Prefabs.BombProjectile);
                if (saved == null)
                {
                    throw new InvalidOperationException(
                        "Failed to save Bomb projectile: "
                        + ProjectAssetPaths.Prefabs.BombProjectile);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.BombProjectile);
            return prefab != null ? prefab.GetComponent<BombProjectile>() : null;
        }

        private static void AddBombVisual(
            Transform root,
            Material material)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Body";
            body.transform.SetParent(root, false);
            body.transform.localScale = Vector3.one * 0.4f;
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().sharedMaterial = material;

            GameObject fuse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fuse.name = "Fuse";
            fuse.transform.SetParent(root, false);
            fuse.transform.localPosition = new Vector3(0f, 0.23f, 0f);
            fuse.transform.localScale = new Vector3(0.045f, 0.08f, 0.045f);
            Object.DestroyImmediate(fuse.GetComponent<Collider>());
            fuse.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static PlayerToolDefinition EnsureDefinition(
            GameObject heldModel,
            BombProjectile projectile,
            GameObject explosionEffect)
        {
            if (heldModel == null
                || projectile == null
                || explosionEffect == null)
            {
                throw new InvalidOperationException(
                    "Cannot configure Bomb because a generated prefab is missing.");
            }

            PlayerToolDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.BombTool);
            if (definition == null)
            {
                definition =
                    ScriptableObject.CreateInstance<PlayerToolDefinition>();
                definition.name = "BombTool";
                AssetDatabase.CreateAsset(
                    definition,
                    ProjectAssetPaths.Config.BombTool);
            }

            AnimationClip animation =
                AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    ProjectAssetPaths.Animations.ToolPrimaryActionPlaceholder);
            SerializedObject serialized = new SerializedObject(definition);
            SetInteger(serialized, "item", (int)PlayerInventoryItem.Bomb);
            SetInteger(
                serialized,
                "primaryAction",
                (int)PlayerToolPrimaryAction.ThrowBomb);
            SetInteger(
                serialized,
                "animationTriggerMode",
                (int)PlayerToolAnimationTriggerMode.Single);
            SetReference(serialized, "primaryActionAnimation", animation);
            SetReference(serialized, "heldModelPrefab", heldModel);
            SetInteger(
                serialized,
                "heldModelMountStrategy",
                (int)HeldToolMountStrategy.SingleHand);
            SetBoolean(serialized, "allowMovementWhileUsing", true);
            SetFloat(serialized, "actionTriggerDelay", 0.15f);
            SetFloat(serialized, "actionCyclePeriod", 0.6f);
            SetBoolean(serialized, "actionIsPeriodic", false);
            SetReference(serialized, "projectilePrefab", null);
            SetReference(serialized, "bombProjectilePrefab", projectile);
            SetFloat(serialized, "bombEntityExplosionImpulse", 240f);
            SetReference(
                serialized,
                "bombExplosionEffectPrefab",
                explosionEffect);
            SetFloat(serialized, "bombExplosionEffectLifetime", 3f);
            SetFloat(serialized, "throwSpeed", 9f);
            SetFloat(serialized, "upwardThrowSpeed", 2f);
            SetFloat(serialized, "throwSpinSpeed", 7f);
            SetFloat(serialized, "throwForwardOffset", 0.8f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static void EnsurePlayerRegistration(
            PlayerToolDefinition definition)
        {
            GameObject player = PrefabUtility.LoadPrefabContents(
                ProjectAssetPaths.Prefabs.Player);
            if (player == null)
            {
                throw new InvalidOperationException(
                    "Cannot register Bomb because the player prefab is missing: "
                    + ProjectAssetPaths.Prefabs.Player);
            }

            try
            {
                PlayerToolController controller =
                    player.GetComponent<PlayerToolController>();
                if (controller == null)
                {
                    throw new InvalidOperationException(
                        "The player prefab has no PlayerToolController.");
                }

                SerializedObject serialized = new SerializedObject(controller);
                SerializedProperty definitions =
                    serialized.FindProperty("toolDefinitions");
                int index = FindDefinitionIndex(
                    definitions,
                    PlayerInventoryItem.Bomb);
                if (index < 0)
                {
                    index = definitions.arraySize;
                    definitions.arraySize++;
                }
                definitions.GetArrayElementAtIndex(index)
                    .objectReferenceValue = definition;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(
                    player,
                    ProjectAssetPaths.Prefabs.Player);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
            }
        }

        private static int FindDefinitionIndex(
            SerializedProperty definitions,
            PlayerInventoryItem item)
        {
            for (int i = 0; i < definitions.arraySize; i++)
            {
                PlayerToolDefinition definition =
                    definitions.GetArrayElementAtIndex(i)
                        .objectReferenceValue as PlayerToolDefinition;
                if (definition != null && definition.Item == item)
                    return i;
            }
            return -1;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
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
