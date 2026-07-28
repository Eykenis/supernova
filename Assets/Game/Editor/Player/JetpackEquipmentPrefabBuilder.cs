using System.Collections.Generic;
using Supernova.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Supernova.EditorTools
{
    public static class JetpackEquipmentPrefabBuilder
    {
        private const string SourcePrefabPath =
            "Assets/3rd/P05_Aki & Mika/Model_DATA/Prefab/Physics_MagicaCloth2/"
            + "P05_ASTRO_PlusBP_Mika Variant.prefab";
        private const string VfxPrefabPath =
            "Assets/3rd/P05_Aki & Mika/Model_DATA/Prefab/VFX/BackPuck_VFX.prefab";
        private const string OutputPrefabPath =
            "Assets/Game/Prefabs/Equipment/Jetpack.prefab";
        private const string DefinitionPath =
            "Assets/Game/Config/Equipment/Jetpack.asset";

        [InitializeOnLoadMethod]
        private static void BuildMissingUpgradedPrefab()
        {
            EditorApplication.delayCall += () =>
            {
                GameObject output =
                    AssetDatabase.LoadAssetAtPath<GameObject>(OutputPrefabPath);
                if (output == null
                    || output.GetComponent<PlayerEquipmentVisual>() == null
                    || output.transform.Find("BackPack_Main") == null)
                {
                    Build();
                }
            };
        }

        [MenuItem("Supernova/Equipment/Rebuild Jetpack Visual")]
        public static void Build()
        {
            GameObject source = PrefabUtility.LoadPrefabContents(SourcePrefabPath);
            if (source == null)
            {
                Debug.LogError($"Jetpack source prefab is missing: {SourcePrefabPath}");
                return;
            }

            GameObject outputRoot = new GameObject("Jetpack");
            try
            {
                List<PlayerEquipmentVisual.SkinnedRendererBinding> bindings =
                    new List<PlayerEquipmentVisual.SkinnedRendererBinding>();
                CopyBackpackRenderer(source, outputRoot, "P05_BackPack", bindings);
                Transform sourceBoneRoot =
                    FindDescendant(source.transform, "BackPack_Main");
                if (sourceBoneRoot == null)
                    throw new UnityException("Could not find backpack bone root 'BackPack_Main'.");
                Transform equipmentBoneRoot =
                    CopyBoneHierarchy(sourceBoneRoot, outputRoot.transform);

                GameObject vfx = CreateVfx(source, outputRoot);
                PlayerEquipmentVisual visual =
                    outputRoot.AddComponent<PlayerEquipmentVisual>();
                visual.Configure(
                    bindings.ToArray(),
                    equipmentBoneRoot,
                    vfx,
                    true);

                GameObject saved =
                    PrefabUtility.SaveAsPrefabAsset(outputRoot, OutputPrefabPath);
                if (saved == null)
                    throw new UnityException("Could not save the jetpack equipment prefab.");

                UpdateEquipmentDefinition(saved);
                Debug.Log(
                    "Rebuilt Jetpack.prefab from P05_ASTRO_PlusBP_Mika Variant "
                    + "and BackPuck_VFX.");
            }
            finally
            {
                Object.DestroyImmediate(outputRoot);
                PrefabUtility.UnloadPrefabContents(source);
            }
        }

        private static void CopyBackpackRenderer(
            GameObject sourceRoot,
            GameObject outputRoot,
            string objectName,
            ICollection<PlayerEquipmentVisual.SkinnedRendererBinding> bindings)
        {
            Transform sourceTransform = FindDescendant(sourceRoot.transform, objectName);
            SkinnedMeshRenderer sourceRenderer =
                sourceTransform != null
                    ? sourceTransform.GetComponent<SkinnedMeshRenderer>()
                    : null;
            if (sourceRenderer == null)
                throw new UnityException(
                    $"Could not find skinned backpack renderer '{objectName}'.");

            string rootBoneName =
                sourceRenderer.rootBone != null ? sourceRenderer.rootBone.name : string.Empty;
            Transform[] sourceBones = sourceRenderer.bones;
            string[] boneNames = new string[sourceBones.Length];
            for (int i = 0; i < sourceBones.Length; i++)
                boneNames[i] = sourceBones[i] != null ? sourceBones[i].name : string.Empty;

            GameObject rendererObject = new GameObject(objectName);
            rendererObject.transform.SetParent(outputRoot.transform, false);
            CopyTransformRelativeToRoot(
                sourceRoot.transform,
                sourceTransform,
                rendererObject.transform);

            SkinnedMeshRenderer outputRenderer =
                rendererObject.AddComponent<SkinnedMeshRenderer>();
            EditorUtility.CopySerialized(sourceRenderer, outputRenderer);
            outputRenderer.rootBone = null;
            outputRenderer.bones = new Transform[sourceBones.Length];

            bindings.Add(new PlayerEquipmentVisual.SkinnedRendererBinding(
                outputRenderer,
                rootBoneName,
                boneNames));
        }

        private static GameObject CreateVfx(
            GameObject sourceRoot,
            GameObject outputRoot)
        {
            GameObject vfxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VfxPrefabPath);
            if (vfxPrefab == null)
                throw new UnityException($"Jetpack VFX prefab is missing: {VfxPrefabPath}");

            GameObject vfx = (GameObject)PrefabUtility.InstantiatePrefab(vfxPrefab);
            vfx.name = "BackPuck_VFX";
            vfx.transform.SetParent(outputRoot.transform, false);

            Transform sourceVfx = FindDescendant(sourceRoot.transform, "BackPuck_VFX");
            if (sourceVfx != null)
                CopyTransformRelativeToRoot(
                    sourceRoot.transform,
                    sourceVfx,
                    vfx.transform);

            vfx.SetActive(false);
            return vfx;
        }

        private static Transform CopyBoneHierarchy(
            Transform source,
            Transform destinationParent)
        {
            GameObject copy = new GameObject(source.name);
            Transform copyTransform = copy.transform;
            copyTransform.SetParent(destinationParent, false);
            copyTransform.localPosition = source.localPosition;
            copyTransform.localRotation = source.localRotation;
            copyTransform.localScale = source.localScale;

            for (int i = 0; i < source.childCount; i++)
                CopyBoneHierarchy(source.GetChild(i), copyTransform);

            return copyTransform;
        }

        private static void CopyTransformRelativeToRoot(
            Transform sourceRoot,
            Transform source,
            Transform destination)
        {
            destination.localPosition =
                sourceRoot.InverseTransformPoint(source.position);
            destination.localRotation =
                Quaternion.Inverse(sourceRoot.rotation) * source.rotation;
            Vector3 rootScale = sourceRoot.lossyScale;
            Vector3 sourceScale = source.lossyScale;
            destination.localScale = new Vector3(
                SafeDivide(sourceScale.x, rootScale.x),
                SafeDivide(sourceScale.y, rootScale.y),
                SafeDivide(sourceScale.z, rootScale.z));
        }

        private static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) > 0.00001f ? value / divisor : value;
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == objectName)
                    return transforms[i];
            }

            return null;
        }

        private static void UpdateEquipmentDefinition(GameObject visualPrefab)
        {
            PlayerEquipmentDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerEquipmentDefinition>(DefinitionPath);
            if (definition == null)
                return;

            SerializedObject serializedDefinition = new SerializedObject(definition);
            serializedDefinition.FindProperty("visualPrefab").objectReferenceValue =
                visualPrefab;
            serializedDefinition.FindProperty("localPosition").vector3Value =
                Vector3.zero;
            serializedDefinition.FindProperty("localEulerAngles").vector3Value =
                Vector3.zero;
            serializedDefinition.FindProperty("localScale").vector3Value =
                Vector3.one;
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
        }
    }
}
