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
        private const int GunPrice = 300;
        private const int SmgPrice = 500;
        private const int FlashlightPrice = 100;
        private const int SolidGunPrice = 650;
        private const int PortalGunPrice = SolidGunPrice;
        private const int AttractionModulePrice = 450;
        private const int CartPrice = 250;
        private const string ProductAnchorPrefix = "Shop Product ";
        private const string SessionKey =
            "Supernova.HomeShopAssetBuilder.Ensured.V5";
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
            PlayerToolDefinition gunTool = LoadRequired<PlayerToolDefinition>(
                ProjectAssetPaths.Config.RifleTool);
            PlayerToolDefinition flashlightTool =
                LoadRequired<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.FlashlightTool);
            GameObject smgModel = LoadRequired<GameObject>(
                ProjectAssetPaths.Prefabs.Smg);
            GameObject solidGunModel = LoadRequired<GameObject>(
                ProjectAssetPaths.Prefabs.SolidGun);
            GameObject attractionModule = EnsureAttractionModuleDisplay();
            GameObject cartModel = LoadRequired<GameObject>(
                ProjectAssetPaths.ThirdParty.EmptyCart);
            PlayerToolDefinition smgTool = EnsureSmgTool(gunTool, smgModel);
            PlayerToolDefinition cartTool = EnsureCartTool();

            var products = new List<ShopProductProfile>
            {
                EnsureItemProduct(
                    ProjectAssetPaths.Config.GunProduct,
                    "GunProduct",
                    "gun",
                    "Gun",
                    GunPrice,
                    PlayerInventoryItem.Gun,
                    gunTool.HeldModelPrefab,
                    wireframe),
                EnsureItemProduct(
                    ProjectAssetPaths.Config.SmgProduct,
                    "SMGProduct",
                    "smg",
                    "SMG",
                    SmgPrice,
                    PlayerInventoryItem.SMG,
                    smgModel,
                    wireframe),
                EnsureItemProduct(
                    ProjectAssetPaths.Config.FlashlightProduct,
                    "FlashlightProduct",
                    "flashlight",
                    "FlashLight",
                    FlashlightPrice,
                    PlayerInventoryItem.Flashlight,
                    flashlightTool.HeldModelPrefab,
                    wireframe),
                EnsureItemProduct(
                    ProjectAssetPaths.Config.SolidGunProduct,
                    "SolidGunProduct",
                    "solid-gun",
                    "SolidGun",
                    SolidGunPrice,
                    PlayerInventoryItem.SolidGun,
                    solidGunModel,
                    wireframe),
                EnsureItemProduct(
                    ProjectAssetPaths.Config.PortalGunProduct,
                    "PortalGunProduct",
                    "portal-gun",
                    "PortalGun",
                    PortalGunPrice,
                    PlayerInventoryItem.PortalGun,
                    solidGunModel,
                    wireframe),
                EnsureUpgradeProduct(
                    attractionModule,
                    wireframe),
                EnsureCartProduct(
                    cartModel,
                    wireframe),
            };

            EnsurePlayerRegistration(smgTool, cartTool);
            EnsureHomeSceneShop(products);
            AssetDatabase.SaveAssets();
            return products;
        }

        private static PlayerToolDefinition EnsureSmgTool(
            PlayerToolDefinition gunTool,
            GameObject smgModel)
        {
            PlayerToolDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.SmgTool);
            if (definition == null)
            {
                definition =
                    ScriptableObject.CreateInstance<PlayerToolDefinition>();
                AssetDatabase.CreateAsset(
                    definition,
                    ProjectAssetPaths.Config.SmgTool);
            }

            EditorUtility.CopySerialized(gunTool, definition);
            definition.name = "SMGTool";
            SerializedObject serialized = new SerializedObject(definition);
            serialized.FindProperty("item").intValue =
                (int)PlayerInventoryItem.SMG;
            serialized.FindProperty("heldModelPrefab").objectReferenceValue =
                smgModel;
            serialized.FindProperty("actionCyclePeriod").floatValue = 1f / 12f;
            serialized.FindProperty("actionIsPeriodic").boolValue = true;
            serialized.FindProperty("initialAmmunition").intValue = 180;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static PlayerToolDefinition EnsureCartTool()
        {
            PlayerToolDefinition definition =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.CartTool);
            if (definition == null)
            {
                definition =
                    ScriptableObject.CreateInstance<PlayerToolDefinition>();
                AssetDatabase.CreateAsset(
                    definition,
                    ProjectAssetPaths.Config.CartTool);
            }

            definition.name = "CartTool";
            SerializedObject serialized = new SerializedObject(definition);
            serialized.FindProperty("item").intValue =
                (int)PlayerInventoryItem.Cart;
            serialized.FindProperty("primaryAction").intValue =
                (int)PlayerToolPrimaryAction.TowCart;
            serialized.FindProperty("animationTriggerMode").intValue =
                (int)PlayerToolAnimationTriggerMode.Single;
            serialized.FindProperty("primaryActionAnimation")
                .objectReferenceValue = null;
            serialized.FindProperty("heldModelPrefab")
                .objectReferenceValue = null;
            serialized.FindProperty("heldModelMountStrategy").intValue =
                (int)HeldToolMountStrategy.SingleHand;
            serialized.FindProperty("allowMovementWhileUsing").boolValue =
                true;
            serialized.FindProperty("actionTriggerDelay").floatValue = 0f;
            serialized.FindProperty("actionCyclePeriod").floatValue = 0.02f;
            serialized.FindProperty("actionIsPeriodic").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
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
            GameObject displayPrefab,
            Material wireframe)
        {
            ShopProductProfile profile =
                AssetDatabase.LoadAssetAtPath<ShopProductProfile>(
                    ProjectAssetPaths.Config.AttractionModuleProduct);
            if (profile == null)
            {
                profile =
                    ScriptableObject.CreateInstance<ShopProductProfile>();
                profile.name = "AttractionModuleProduct";
                AssetDatabase.CreateAsset(
                    profile,
                    ProjectAssetPaths.Config.AttractionModuleProduct);
            }

            profile.ConfigureUpgrade(
                "attraction-module",
                "牵引模块升级 +400N",
                AttractionModulePrice,
                PlayerUpgrade.AttractionModule,
                FirstPersonCartAttractor.AttractionModuleUpgradeForce,
                displayPrefab,
                wireframe);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static ShopProductProfile EnsureCartProduct(
            GameObject displayPrefab,
            Material wireframe)
        {
            ShopProductProfile profile = EnsureItemProduct(
                ProjectAssetPaths.Config.CartProduct,
                "CartProduct",
                "cart",
                "Cart",
                CartPrice,
                PlayerInventoryItem.Cart,
                displayPrefab,
                wireframe);
            profile.ConfigureDisplayTransform(
                new Vector3(0f, -0.35f, 0f),
                new Vector3(0f, 90f, 0f),
                Vector3.one * 0.65f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static GameObject EnsureAttractionModuleDisplay()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProjectAssetPaths.Prefabs.AttractionModuleDisplay);
            if (existing != null) return existing;

            var root = new GameObject("AttractionModuleDisplay");
            try
            {
                GameObject core = GameObject.CreatePrimitive(
                    PrimitiveType.Cube);
                core.name = "Module Core";
                core.transform.SetParent(root.transform, false);
                core.transform.localScale =
                    new Vector3(0.7f, 0.35f, 0.9f);
                Object.DestroyImmediate(core.GetComponent<Collider>());

                for (int side = -1; side <= 1; side += 2)
                {
                    GameObject coil = GameObject.CreatePrimitive(
                        PrimitiveType.Cylinder);
                    coil.name = side < 0 ? "Left Coil" : "Right Coil";
                    coil.transform.SetParent(root.transform, false);
                    coil.transform.localPosition =
                        new Vector3(side * 0.48f, 0f, 0f);
                    coil.transform.localRotation =
                        Quaternion.Euler(0f, 0f, 90f);
                    coil.transform.localScale =
                        new Vector3(0.28f, 0.18f, 0.28f);
                    Object.DestroyImmediate(coil.GetComponent<Collider>());
                }

                return PrefabUtility.SaveAsPrefabAsset(
                    root,
                    ProjectAssetPaths.Prefabs.AttractionModuleDisplay);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void EnsurePlayerRegistration(
            params PlayerToolDefinition[] requiredTools)
        {
            GameObject playerRoot = PrefabUtility.LoadPrefabContents(
                ProjectAssetPaths.Prefabs.Player);
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
                for (int toolIndex = 0;
                     toolIndex < requiredTools.Length;
                     toolIndex++)
                {
                    PlayerToolDefinition requiredTool =
                        requiredTools[toolIndex];
                    bool found = false;
                    for (int i = 0; i < definitions.arraySize; i++)
                    {
                        if (definitions.GetArrayElementAtIndex(i)
                                .objectReferenceValue == requiredTool)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        int index = definitions.arraySize;
                        definitions.InsertArrayElementAtIndex(index);
                        definitions.GetArrayElementAtIndex(index)
                            .objectReferenceValue = requiredTool;
                    }
                }

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
                        (i - (products.Count - 1) * 0.5f) * 2.2f,
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
