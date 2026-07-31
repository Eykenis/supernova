#if UNITY_EDITOR
using System;
using Supernova.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Supernova.Editor.Gameplay
{
    /// <summary>
    /// Builds the project-owned grab-hook prefab and definition from the source
    /// model, then wires both into the player prefab.
    /// </summary>
    public static class GrabHookAssetBuilder
    {
        private const float TargetModelSize = 0.75f;

        [InitializeOnLoadMethod]
        private static void ScheduleEnsureAssets()
        {
            EditorApplication.delayCall += EnsureAssetsAfterReload;
        }

        [MenuItem("Tools/Supernova/Gameplay/Rebuild Grab Hook Assets")]
        public static void RebuildAssets()
        {
            EnsureAssets(true);
        }

        private static void EnsureAssetsAfterReload()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            try
            {
                EnsureAssets(false);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void EnsureAssets(bool rebuild)
        {
            GameObject prefab = EnsureGrabHookPrefab(rebuild);
            PlayerToolDefinition definition =
                EnsureGrabHookDefinition(prefab, rebuild);
            EnsurePlayerWiring(definition);
            AssetDatabase.SaveAssets();
        }

        private static GameObject EnsureGrabHookPrefab(bool rebuild)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.GrabHook);
            if (existing != null && !rebuild)
                return existing;

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.GrabHookSourceModel);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "Missing grab-hook source model: "
                    + ProjectAssetPaths.Prefabs.GrabHookSourceModel);
            }

            GameObject root = new GameObject("GrabHook");
            try
            {
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(
                    source,
                    root.transform);
                model.name = source.name;
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                NormalizeModelSize(model);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    ProjectAssetPaths.Prefabs.GrabHook);
                if (saved == null)
                {
                    throw new InvalidOperationException(
                        "Failed to save grab-hook prefab: "
                        + ProjectAssetPaths.Prefabs.GrabHook);
                }
                return saved;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void NormalizeModelSize(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float largestDimension = Mathf.Max(
                bounds.size.x,
                bounds.size.y,
                bounds.size.z);
            if (largestDimension <= 0.0001f) return;

            float scale = TargetModelSize / largestDimension;
            model.transform.localScale *= scale;
        }

        private static PlayerToolDefinition EnsureGrabHookDefinition(
            GameObject prefab,
            bool rebuild)
        {
            PlayerToolDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.GrabHookTool);
            if (definition == null)
            {
                definition =
                    ScriptableObject.CreateInstance<PlayerToolDefinition>();
                AssetDatabase.CreateAsset(
                    definition,
                    ProjectAssetPaths.Config.GrabHookTool);
                rebuild = true;
            }

            SerializedObject serialized = new SerializedObject(definition);
            serialized.FindProperty("item").intValue =
                (int)PlayerInventoryItem.GrabHook;
            serialized.FindProperty("primaryAction").intValue =
                (int)PlayerToolPrimaryAction.FireGrabHook;
            serialized.FindProperty("animationTriggerMode").intValue =
                (int)PlayerToolAnimationTriggerMode.Single;
            serialized.FindProperty("primaryActionAnimation")
                .objectReferenceValue = null;
            serialized.FindProperty("heldModelPrefab").objectReferenceValue =
                prefab;
            serialized.FindProperty("heldModelMountStrategy").intValue =
                (int)HeldToolMountStrategy.SingleHand;
            serialized.FindProperty("allowMovementWhileUsing").boolValue =
                true;
            serialized.FindProperty("actionTriggerDelay").floatValue = 0f;
            serialized.FindProperty("actionCyclePeriod").floatValue = 0.2f;
            serialized.FindProperty("actionIsPeriodic").boolValue = false;
            serialized.FindProperty("grabHookProjectileModelPrefab")
                .objectReferenceValue = prefab;
            serialized.FindProperty("grabHookLaunchSpeed").floatValue = 36f;
            serialized.FindProperty("grabHookMaximumLength").floatValue = 30f;
            serialized.FindProperty("grabHookCollisionRadius").floatValue =
                0.12f;
            serialized.FindProperty("grabHookRetractSpeed").floatValue = 45f;
            serialized.FindProperty("grabHookAimPredictionDuration").floatValue =
                3f;
            serialized.FindProperty("grabHookAimPredictionStep").floatValue =
                0.05f;
            serialized.FindProperty("grabHookArrivalDistance").floatValue =
                1.25f;
            serialized.FindProperty("grabHookPullAcceleration").floatValue =
                32f;
            serialized.FindProperty("grabHookMaximumPullSpeed").floatValue =
                18f;
            serialized.FindProperty("grabHookRopeWidth").floatValue = 0.035f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static void EnsurePlayerWiring(
            PlayerToolDefinition grabHookDefinition)
        {
            GameObject player = PrefabUtility.LoadPrefabContents(
                ProjectAssetPaths.Prefabs.Player);
            try
            {
                PlayerToolController toolController =
                    player.GetComponent<PlayerToolController>();
                if (toolController == null)
                {
                    throw new InvalidOperationException(
                        "Player prefab is missing PlayerToolController: "
                        + ProjectAssetPaths.Prefabs.Player);
                }

                GrabHookController grabHook =
                    player.GetComponent<GrabHookController>();
                if (grabHook == null)
                    grabHook = player.AddComponent<GrabHookController>();

                SerializedObject serialized =
                    new SerializedObject(toolController);
                serialized.FindProperty("grabHook").objectReferenceValue =
                    grabHook;
                SerializedProperty definitions =
                    serialized.FindProperty("toolDefinitions");
                int index = FindDefinitionIndex(
                    definitions,
                    PlayerInventoryItem.GrabHook);
                if (index < 0)
                {
                    index = definitions.arraySize;
                    definitions.arraySize++;
                }
                definitions.GetArrayElementAtIndex(index)
                    .objectReferenceValue = grabHookDefinition;
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
    }
}
#endif
