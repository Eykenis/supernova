#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Supernova.Gameplay;
using Supernova.Shop;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Supernova.Editor.Gameplay
{
    /// <summary>
    /// Builds the Home shop products and their scene interaction stands.
    /// All asset locations are centralized in ProjectAssetPaths.
    /// </summary>
    public static class HomeShopAssetBuilder
    {
        private const int FlashlightPrice = 100;
        private const int SolidGunPrice = 650;
        private const int PortalGunPrice = SolidGunPrice;
        private const int MagnetUpgradeBasePrice = 100;
        private const int MagnetUpgradePriceIncrease = 100;
        private const float MagnetUpgradeForceIncrease = 100f;
        private const string ProductAnchorPrefix = "Shop Product ";
        private const string SessionKey =
            "Supernova.HomeShopAssetBuilder.Ensured.V7";
        private static bool waitingForEditMode;

        [InitializeOnLoadMethod]
        private static void ScheduleEnsureAssets()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += EnsureWhenReady;
        }

        [MenuItem("Tools/Supernova/Gameplay/Rebuild Home Shop")]
        public static void Rebuild()
        {
            IReadOnlyList<ShopProductProfile> products =
                EnsureAssetsAndScene();
            Selection.activeObject = products[0];
            EditorGUIUtility.PingObject(products[0]);
            Debug.Log("Rebuilt the Home shop.", products[0]);
        }

        private static void EnsureWhenReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (!waitingForEditMode)
                {
                    waitingForEditMode = true;
                    EditorApplication.playModeStateChanged +=
                        HandlePlayModeStateChanged;
                }
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureWhenReady;
                return;
            }

            try
            {
                EnsureAssetsAndScene();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void HandlePlayModeStateChanged(
            PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
                return;

            waitingForEditMode = false;
            EditorApplication.playModeStateChanged -=
                HandlePlayModeStateChanged;
            EditorApplication.delayCall += EnsureWhenReady;
        }

        private static IReadOnlyList<ShopProductProfile> EnsureAssetsAndScene()
        {
            EnsureFolder(ProjectAssetPaths.Folders.Shop);
            EnsureFolder(ProjectAssetPaths.Folders.ShopMaterials);

            Material wireframe = LoadRequired<Material>(
                ProjectAssetPaths.Materials.ShopGeometryWireframe);
            PlayerToolDefinition flashlightTool =
                LoadRequired<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.FlashlightTool);
            GameObject solidGunModel = LoadRequired<GameObject>(
                ProjectAssetPaths.Prefabs.SolidGun);
            GameObject portalGunModel = LoadRequired<GameObject>(
                ProjectAssetPaths.Prefabs.PortalGun);
            GameObject magnetUpgradeModel = LoadRequired<GameObject>(
                ProjectAssetPaths.ThirdParty.MagnetUpgradeModel);

            var products = new List<ShopProductProfile>
            {
                EnsureItemProduct(
                    ProjectAssetPaths.Config.FlashlightProduct,
                    "FlashlightProduct",
                    "flashlight",
                    "照明灯",
                    FlashlightPrice,
                    PlayerInventoryItem.Flashlight,
                    flashlightTool.HeldModelPrefab,
                    wireframe),
                EnsureItemProduct(
                    ProjectAssetPaths.Config.SolidGunProduct,
                    "SolidGunProduct",
                    "solid-gun",
                    "地形发生器",
                    SolidGunPrice,
                    PlayerInventoryItem.SolidGun,
                    solidGunModel,
                    wireframe),
                EnsureItemProduct(
                    ProjectAssetPaths.Config.PortalGunProduct,
                    "PortalGunProduct",
                    "portal-gun",
                    "传送门发生器",
                    PortalGunPrice,
                    PlayerInventoryItem.PortalGun,
                    portalGunModel,
                    wireframe),
                EnsureUpgradeProduct(
                    ProjectAssetPaths.Config.MagnetUpgradeProduct,
                    "MagnetUpgradeProduct",
                    "magnet-force-upgrade",
                    "磁力升级",
                    MagnetUpgradeBasePrice,
                    PlayerUpgrade.MagnetAttractionForce,
                    MagnetUpgradeForceIncrease,
                    true,
                    MagnetUpgradePriceIncrease,
                    magnetUpgradeModel,
                    wireframe),
            };

            EnsureHomeSceneShop(products);
            AssetDatabase.SaveAssets();
            return products;
        }

        private static ShopProductProfile EnsureItemProduct(
            string path,
            string assetName,
            string id,
            string displayName,
            int price,
            PlayerInventoryItem item,
            GameObject displayPrefab,
            Material wireframe)
        {
            ShopProductProfile profile =
                AssetDatabase.LoadAssetAtPath<ShopProductProfile>(path);
            if (profile == null)
            {
                profile =
                    ScriptableObject.CreateInstance<ShopProductProfile>();
                profile.name = assetName;
                AssetDatabase.CreateAsset(profile, path);
            }

            profile.Configure(
                id,
                displayName,
                price,
                item,
                displayPrefab,
                wireframe);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static ShopProductProfile EnsureUpgradeProduct(
            string path,
            string assetName,
            string id,
            string displayName,
            int price,
            PlayerUpgrade upgrade,
            float upgradeValue,
            bool repeatable,
            int priceIncreasePerPurchase,
            GameObject displayPrefab,
            Material wireframe)
        {
            ShopProductProfile profile =
                AssetDatabase.LoadAssetAtPath<ShopProductProfile>(path);
            if (profile == null)
            {
                profile =
                    ScriptableObject.CreateInstance<ShopProductProfile>();
                profile.name = assetName;
                AssetDatabase.CreateAsset(profile, path);
            }

            profile.ConfigureUpgrade(
                id,
                displayName,
                price,
                upgrade,
                upgradeValue,
                repeatable,
                priceIncreasePerPurchase,
                displayPrefab,
                wireframe);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void EnsureHomeSceneShop(
            IReadOnlyList<ShopProductProfile> products)
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
            if (shopRoot == null)
            {
                shopRoot = new GameObject(
                    ProjectAssetPaths.LookupNames.HomeShopRoot);
                SceneManager.MoveGameObjectToScene(shopRoot, homeScene);
                shopRoot.transform.position =
                    new Vector3(24f, 1f, 18.8f);
                shopRoot.transform.rotation =
                    Quaternion.Euler(0f, 90f, 0f);
            }

            HomeShopController legacy =
                shopRoot.GetComponent<HomeShopController>();
            if (legacy != null) Object.DestroyImmediate(legacy);

            var wantedAnchors = new HashSet<string>();
            for (int i = 0; i < products.Count; i++)
                wantedAnchors.Add(ProductAnchorPrefix + products[i].ProductId);
            for (int i = shopRoot.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = shopRoot.transform.GetChild(i);
                if (child.name.StartsWith(
                        ProductAnchorPrefix,
                        StringComparison.Ordinal)
                    && !wantedAnchors.Contains(child.name))
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }

            for (int i = 0; i < products.Count; i++)
            {
                string anchorName = ProductAnchorPrefix + products[i].ProductId;
                Transform anchor = shopRoot.transform.Find(anchorName);
                if (anchor == null)
                {
                    var anchorObject = new GameObject(anchorName);
                    anchor = anchorObject.transform;
                    anchor.SetParent(shopRoot.transform, false);
                }
                anchor.localPosition =
                    new Vector3(
                        (i - (products.Count - 1) * 0.5f) * 5f,
                        0f,
                        0f);
                anchor.localRotation = Quaternion.identity;
                anchor.localScale = Vector3.one;

                HomeShopController controller =
                    anchor.GetComponent<HomeShopController>();
                if (controller == null)
                    controller =
                        anchor.gameObject.AddComponent<HomeShopController>();
                SerializedObject serialized = new SerializedObject(controller);
                serialized.FindProperty("productProfile")
                    .objectReferenceValue = products[i];
                serialized.FindProperty("productLocalPosition")
                    .vector3Value = Vector3.zero;
                serialized.FindProperty("interactionDistance")
                    .floatValue = 2.4f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(homeScene);
            EditorSceneManager.SaveScene(homeScene);
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

        private static T LoadRequired<T>(string path)
            where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    "Required centralized asset is missing: " + path);
            }
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
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
