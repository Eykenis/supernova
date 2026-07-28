#if UNITY_EDITOR
using System;
using Supernova.Gameplay;
using Supernova.Shop;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Supernova.Editor.Gameplay
{
    /// <summary>
    /// Keeps the first shop product asset and its authored Home scene anchor in place.
    /// </summary>
    public static class HomeShopAssetBuilder
    {
        private const int FlashlightPrice = 100;
        private const string SessionKey =
            "Supernova.HomeShopAssetBuilder.Ensured";

        [InitializeOnLoadMethod]
        private static void ScheduleEnsureAssets()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;

            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += EnsureAssetsAndScene;
        }

        [MenuItem("Tools/Supernova/Gameplay/Rebuild Home Shop")]
        public static void Rebuild()
        {
            ShopProductProfile profile = EnsureFlashlightProduct();
            EnsureHomeSceneShop(profile);
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
            Debug.Log("Rebuilt the Home shop and flashlight product.", profile);
        }

        private static void EnsureAssetsAndScene()
        {
            if (EditorApplication.isCompiling
                || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureAssetsAndScene;
                return;
            }

            ShopProductProfile profile = EnsureFlashlightProduct();
            EnsureHomeSceneShop(profile);
        }

        private static ShopProductProfile EnsureFlashlightProduct()
        {
            EnsureFolder(ProjectAssetPaths.Folders.Shop);
            ShopProductProfile profile =
                AssetDatabase.LoadAssetAtPath<ShopProductProfile>(
                    ProjectAssetPaths.Config.FlashlightProduct);
            if (profile != null)
                return profile;

            PlayerToolDefinition flashlight =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.FlashlightTool);
            Material wireframe =
                AssetDatabase.LoadAssetAtPath<Material>(
                    ProjectAssetPaths.Materials.FlashlightGlow);
            if (flashlight == null
                || flashlight.HeldModelPrefab == null
                || wireframe == null)
            {
                throw new InvalidOperationException(
                    "Cannot create the flashlight product because its "
                    + "centralized tool or material asset is missing.");
            }

            profile = ScriptableObject.CreateInstance<ShopProductProfile>();
            profile.name = "FlashlightProduct";
            profile.Configure(
                "flashlight",
                "照明灯",
                FlashlightPrice,
                PlayerInventoryItem.Flashlight,
                flashlight.HeldModelPrefab,
                wireframe);
            AssetDatabase.CreateAsset(
                profile,
                ProjectAssetPaths.Config.FlashlightProduct);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static void EnsureHomeSceneShop(
            ShopProductProfile profile)
        {
            Scene homeScene =
                SceneManager.GetSceneByPath(ProjectAssetPaths.Scenes.Home);
            bool wasLoaded = homeScene.IsValid() && homeScene.isLoaded;
            if (!wasLoaded)
            {
                homeScene = EditorSceneManager.OpenScene(
                    ProjectAssetPaths.Scenes.Home,
                    OpenSceneMode.Additive);
            }

            GameObject shopRoot = FindRoot(homeScene);
            bool changed = false;
            if (shopRoot == null)
            {
                shopRoot = new GameObject(
                    ProjectAssetPaths.LookupNames.HomeShopRoot);
                SceneManager.MoveGameObjectToScene(shopRoot, homeScene);
                shopRoot.transform.position =
                    new Vector3(24f, 1f, 18.8f);
                shopRoot.transform.rotation =
                    Quaternion.Euler(0f, 90f, 0f);
                changed = true;
            }

            HomeShopController controller =
                shopRoot.GetComponent<HomeShopController>();
            if (controller == null)
            {
                controller = shopRoot.AddComponent<HomeShopController>();
                changed = true;
            }

            SerializedObject serialized =
                new SerializedObject(controller);
            SerializedProperty product =
                serialized.FindProperty("productProfile");
            if (product.objectReferenceValue != profile)
            {
                product.objectReferenceValue = profile;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(homeScene);
                EditorSceneManager.SaveScene(homeScene);
            }

            if (!wasLoaded)
                EditorSceneManager.CloseScene(homeScene, true);
        }

        private static GameObject FindRoot(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name
                    == ProjectAssetPaths.LookupNames.HomeShopRoot)
                {
                    return roots[i];
                }
            }

            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path)
                ?.Replace('\\', '/');
            string child = System.IO.Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(parent)
                || string.IsNullOrWhiteSpace(child))
            {
                throw new InvalidOperationException(
                    "Cannot create invalid asset folder: " + path);
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
