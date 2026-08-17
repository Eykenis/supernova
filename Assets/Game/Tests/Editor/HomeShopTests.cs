using System.Linq;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.Shop;
using Supernova.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Supernova.Tests
{
    public sealed class HomeShopTests
    {
        private GameObject displayObject;

        [TearDown]
        public void TearDown()
        {
            if (displayObject != null)
                Object.DestroyImmediate(displayObject);
        }

        [Test]
        public void PlayerInventory_OnlyAcceptsOwnedItemsFromSavedConfiguration()
        {
            var inventory = new PlayerInventory(
                2,
                item => item != PlayerInventoryItem.Flashlight,
                new[]
                {
                    PlayerInventoryItem.Pickaxe,
                    PlayerInventoryItem.Bomb,
                    PlayerInventoryItem.Flashlight,
                    PlayerInventoryItem.Empty,
                });

            Assert.That(
                inventory.GetItemAtSlot(2),
                Is.EqualTo(PlayerInventoryItem.Empty));
            Assert.That(inventory.SelectedItem,
                Is.EqualTo(PlayerInventoryItem.Empty));

            Assert.That(
                inventory.SetItemAtSlot(
                    2,
                    PlayerInventoryItem.Flashlight),
                Is.True);
            Assert.That(
                inventory.GetItemAtSlot(2),
                Is.EqualTo(PlayerInventoryItem.Flashlight));
            Assert.That(
                inventory.SelectedItem,
                Is.EqualTo(PlayerInventoryItem.Flashlight));
        }

        [Test]
        public void Purchase_DeductsCreditsPersistsOwnershipAndRejectsRepeat()
        {
            ShopProductProfile profile =
                LoadFlashlightProduct();
            string creditsKey = PlayerEconomy.CreditsPreferenceKey;
            string ownershipKey =
                PlayerEconomy.GetItemOwnershipPreferenceKey(
                    PlayerInventoryItem.Flashlight);
            bool hadCredits = PlayerPrefs.HasKey(creditsKey);
            bool hadOwnership = PlayerPrefs.HasKey(ownershipKey);
            int previousCredits = PlayerPrefs.GetInt(creditsKey, 0);
            int previousOwnership = PlayerPrefs.GetInt(ownershipKey, 0);

            try
            {
                PlayerPrefs.SetInt(creditsKey, profile.Price + 25);
                PlayerPrefs.DeleteKey(ownershipKey);

                Assert.That(
                    PlayerEconomy.TryPurchase(profile),
                    Is.EqualTo(ShopPurchaseResult.Purchased));
                Assert.That(
                    PlayerEconomy.Credits,
                    Is.EqualTo(25));
                Assert.That(
                    PlayerEconomy.IsProductOwned(profile),
                    Is.True);
                Assert.That(
                    PlayerEconomy.TryPurchase(profile),
                    Is.EqualTo(ShopPurchaseResult.AlreadyOwned));
                Assert.That(
                    PlayerEconomy.Credits,
                    Is.EqualTo(25));
            }
            finally
            {
                RestorePreference(
                    creditsKey,
                    hadCredits,
                    previousCredits);
                RestorePreference(
                    ownershipKey,
                    hadOwnership,
                    previousOwnership);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void RepeatableMagnetUpgrade_IncreasesForceAndNextPrice()
        {
            ShopProductProfile profile = LoadMagnetUpgradeProduct();
            PlayerUpgrade upgrade = PlayerUpgrade.MagnetAttractionForce;
            string creditsKey = PlayerEconomy.CreditsPreferenceKey;
            string ownershipKey =
                PlayerEconomy.GetUpgradeOwnershipPreferenceKey(upgrade);
            string purchaseCountKey =
                PlayerEconomy.GetUpgradePurchaseCountPreferenceKey(upgrade);
            string upgradeValueKey =
                PlayerEconomy.GetUpgradeValuePreferenceKey(upgrade);
            bool hadCredits = PlayerPrefs.HasKey(creditsKey);
            bool hadOwnership = PlayerPrefs.HasKey(ownershipKey);
            bool hadPurchaseCount = PlayerPrefs.HasKey(purchaseCountKey);
            bool hadUpgradeValue = PlayerPrefs.HasKey(upgradeValueKey);
            int previousCredits = PlayerPrefs.GetInt(creditsKey, 0);
            int previousOwnership = PlayerPrefs.GetInt(ownershipKey, 0);
            int previousPurchaseCount =
                PlayerPrefs.GetInt(purchaseCountKey, 0);
            float previousUpgradeValue =
                PlayerPrefs.GetFloat(upgradeValueKey, 0f);

            try
            {
                PlayerPrefs.SetInt(creditsKey, 1000);
                PlayerPrefs.DeleteKey(ownershipKey);
                PlayerPrefs.DeleteKey(purchaseCountKey);
                PlayerPrefs.DeleteKey(upgradeValueKey);

                Assert.That(profile.IsConfigured, Is.True);
                Assert.That(profile.IsRepeatable, Is.True);
                Assert.That(
                    AssetDatabase.GetAssetPath(profile.DisplayPrefab),
                    Is.EqualTo(ProjectAssetPaths.ThirdParty.MagnetUpgradeModel));
                Assert.That(PlayerEconomy.GetCurrentPrice(profile), Is.EqualTo(100));

                Assert.That(
                    PlayerEconomy.TryPurchase(profile),
                    Is.EqualTo(ShopPurchaseResult.Purchased));
                Assert.That(PlayerEconomy.Credits, Is.EqualTo(900));
                Assert.That(PlayerEconomy.GetUpgradePurchaseCount(upgrade), Is.EqualTo(1));
                Assert.That(PlayerEconomy.GetUpgradeValue(upgrade), Is.EqualTo(100f));
                Assert.That(PlayerEconomy.GetCurrentPrice(profile), Is.EqualTo(200));

                Assert.That(
                    PlayerEconomy.TryPurchase(profile),
                    Is.EqualTo(ShopPurchaseResult.Purchased));
                Assert.That(PlayerEconomy.Credits, Is.EqualTo(700));
                Assert.That(PlayerEconomy.GetUpgradePurchaseCount(upgrade), Is.EqualTo(2));
                Assert.That(PlayerEconomy.GetUpgradeValue(upgrade), Is.EqualTo(200f));
                Assert.That(PlayerEconomy.GetCurrentPrice(profile), Is.EqualTo(300));
                Assert.That(PlayerEconomy.IsProductOwned(profile), Is.False);
                Assert.That(PlayerEconomy.IsUpgradeOwned(upgrade), Is.True);

                displayObject = new GameObject("Magnet Upgrade Test");
                FirstPersonMagnetInteractor magnet =
                    displayObject.AddComponent<FirstPersonMagnetInteractor>();
                Assert.That(magnet.BaseAttractionForce, Is.EqualTo(100f));
                Assert.That(magnet.AttractionForce, Is.EqualTo(300f));

                ShopProductDisplay display =
                    displayObject.AddComponent<ShopProductDisplay>();
                display.Configure(profile);
                display.SetTargeted(true);
                Assert.That(display.TargetCollider, Is.Not.Null);
                Assert.That(displayObject.transform.Find("Pickup Plate"), Is.Not.Null);
                Assert.That(displayObject.transform.Find("Product Display"), Is.Not.Null);
                Assert.That(display.WireframeRendererCount, Is.GreaterThan(0));
                StringAssert.Contains("$300", display.Label.text);
            }
            finally
            {
                RestorePreference(creditsKey, hadCredits, previousCredits);
                RestorePreference(
                    ownershipKey,
                    hadOwnership,
                    previousOwnership);
                RestorePreference(
                    purchaseCountKey,
                    hadPurchaseCount,
                    previousPurchaseCount);
                RestoreFloatPreference(
                    upgradeValueKey,
                    hadUpgradeValue,
                    previousUpgradeValue);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void ClearSavedProgress_RemovesGameplayDataButKeepsSystemSettings()
        {
            string creditsKey = PlayerEconomy.CreditsPreferenceKey;
            string ownershipKey =
                PlayerEconomy.GetItemOwnershipPreferenceKey(
                    PlayerInventoryItem.Flashlight);
            string slotKey = PlayerEconomy.GetQuickSlotPreferenceKey(0);
            const string systemSettingKey = "ui.fullscreen";
            string[] keys =
            {
                creditsKey,
                ownershipKey,
                slotKey,
                systemSettingKey,
            };
            bool[] existed = keys.Select(PlayerPrefs.HasKey).ToArray();
            int[] values = keys.Select(key =>
                PlayerPrefs.GetInt(key, 0)).ToArray();

            try
            {
                PlayerPrefs.SetInt(creditsKey, 480);
                PlayerPrefs.SetInt(ownershipKey, 1);
                PlayerPrefs.SetInt(
                    slotKey,
                    (int)PlayerInventoryItem.Flashlight);
                PlayerPrefs.SetInt(systemSettingKey, 1);

                PlayerEconomy.ClearSavedProgress();

                Assert.That(PlayerEconomy.Credits, Is.Zero);
                Assert.That(
                    PlayerEconomy.IsItemOwned(
                        PlayerInventoryItem.Flashlight),
                    Is.False);
                Assert.That(
                    PlayerEconomy.HasQuickSlotConfiguration(0),
                    Is.False);
                Assert.That(
                    PlayerPrefs.GetInt(systemSettingKey, 0),
                    Is.EqualTo(1));
            }
            finally
            {
                for (int i = 0; i < keys.Length; i++)
                    RestorePreference(keys[i], existed[i], values[i]);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void ProductDisplay_MatchesTreasureTextStyleAndOwnershipRenderMode()
        {
            ShopProductProfile profile =
                LoadFlashlightProduct();
            displayObject = new GameObject("Product Display Test");
            ShopProductDisplay display =
                displayObject.AddComponent<ShopProductDisplay>();

            display.Configure(profile);
            display.SetTargeted(true);

            Assert.That(display.TargetCollider, Is.Not.Null);
            Assert.That(display.Label.gameObject.activeInHierarchy, Is.True);
            Assert.That(
                display.Label.fontSize,
                Is.EqualTo(WorldValueTextStyle.FontSize));
            Assert.That(
                display.SolidRendererCount,
                Is.GreaterThan(0));
            Assert.That(
                display.WireframeRendererCount,
                Is.GreaterThan(0));
            MeshRenderer[] renderers =
                displayObject.GetComponentsInChildren<MeshRenderer>(true);
            MeshRenderer plate = renderers.SingleOrDefault(renderer =>
                renderer.gameObject.name == "Pickup Plate");
            MeshRenderer[] modelRenderers = renderers
                .Where(renderer => renderer != plate)
                .ToArray();
            Assert.That(plate, Is.Not.Null);
            Assert.That(
                modelRenderers.Length,
                Is.EqualTo(display.SolidRendererCount));
            Assert.That(
                renderers.Any(renderer =>
                    renderer.gameObject.name.EndsWith(" Wireframe")),
                Is.False,
                "Wireframe presentation must not create duplicate renderers.");

            if (display.IsOwned)
            {
                Assert.That(display.Label.text, Is.EqualTo("已拥有"));
                Assert.That(
                    display.Label.color,
                    Is.EqualTo(WorldValueTextStyle.OwnedColor));
                Assert.That(display.IsShowingSolid, Is.True);
                Assert.That(display.IsShowingWireframe, Is.False);
                Assert.That(
                    modelRenderers.Any(renderer =>
                        renderer.sharedMaterials.Any(material =>
                            material != profile.WireframeMaterial)),
                    Is.True);
            }
            else
            {
                Assert.That(
                    display.Label.text,
                    Does.StartWith("$100").And.Contain("购买"));
                Assert.That(
                    display.Label.color,
                    Is.EqualTo(PlayerEconomy.CanAfford(profile)
                        ? WorldValueTextStyle.ValueColor
                        : WorldValueTextStyle.LossColor));
                Assert.That(display.IsShowingSolid, Is.False);
                Assert.That(display.IsShowingWireframe, Is.True);
                Assert.That(
                    modelRenderers.All(renderer =>
                        renderer.sharedMaterials.All(material =>
                            material == profile.WireframeMaterial)),
                    Is.True);
            }
        }

        [Test]
        public void UnpurchasedProduct_UsesOriginalWireframeAndBluePickupLight()
        {
            ShopProductProfile profile = LoadFlashlightProduct();
            string ownershipKey =
                PlayerEconomy.GetItemOwnershipPreferenceKey(
                    PlayerInventoryItem.Flashlight);
            bool hadOwnership = PlayerPrefs.HasKey(ownershipKey);
            int previousOwnership = PlayerPrefs.GetInt(ownershipKey, 0);

            try
            {
                PlayerPrefs.DeleteKey(ownershipKey);
                displayObject = new GameObject("Unpurchased Product Display Test");
                ShopProductDisplay display =
                    displayObject.AddComponent<ShopProductDisplay>();
                display.Configure(profile);

                MeshRenderer[] modelRenderers = displayObject
                    .GetComponentsInChildren<MeshRenderer>(true)
                    .Where(renderer =>
                        renderer.gameObject.name != "Pickup Plate")
                    .ToArray();
                Assert.That(modelRenderers, Is.Not.Empty);
                Assert.That(
                    modelRenderers.All(renderer =>
                        renderer.sharedMaterials.All(material =>
                            material == profile.WireframeMaterial)),
                    Is.True);

                Transform lightTransform =
                    displayObject.transform.Find("Pickup Light");
                Assert.That(lightTransform, Is.Not.Null);
                Light pickupLight = lightTransform.GetComponent<Light>();
                Assert.That(pickupLight, Is.Not.Null);
                Assert.That(pickupLight.type, Is.EqualTo(LightType.Point));
                Assert.That(
                    pickupLight.color,
                    Is.EqualTo(new Color(0.15f, 0.75f, 1f)));
                Assert.That(pickupLight.range, Is.EqualTo(2.5f));
                Assert.That(pickupLight.intensity, Is.EqualTo(0.8f));
                Assert.That(pickupLight.shadows, Is.EqualTo(LightShadows.None));
            }
            finally
            {
                RestorePreference(
                    ownershipKey,
                    hadOwnership,
                    previousOwnership);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void HomeScene_HasFourProductAnchorsWithoutCustomShopModel()
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

            try
            {
                GameObject[] roots = homeScene.GetRootGameObjects();
                GameObject shop = roots.FirstOrDefault(
                    root => root.name
                        == ProjectAssetPaths.LookupNames.HomeShopRoot);

                Assert.That(shop, Is.Not.Null);
                HomeShopController[] controllers =
                    shop.GetComponentsInChildren<HomeShopController>(true);
                Assert.That(controllers, Has.Length.EqualTo(4));
                Assert.That(
                    controllers.Select(controller =>
                        controller.ProductProfile.ProductId),
                    Is.EquivalentTo(LoadProducts().Select(product =>
                        product.ProductId)));
                Assert.That(
                    shop.GetComponentsInChildren<Renderer>(true),
                    Is.Empty,
                    "The scene anchor must not introduce a custom shop-window model.");
            }
            finally
            {
                if (!wasLoaded)
                    EditorSceneManager.CloseScene(homeScene, true);
            }
        }

        private static ShopProductProfile LoadFlashlightProduct()
        {
            ShopProductProfile profile =
                AssetDatabase.LoadAssetAtPath<ShopProductProfile>(
                    ProjectAssetPaths.Config.FlashlightProduct);
            Assert.That(profile, Is.Not.Null);
            return profile;
        }

        private static ShopProductProfile LoadMagnetUpgradeProduct()
        {
            ShopProductProfile profile =
                AssetDatabase.LoadAssetAtPath<ShopProductProfile>(
                    ProjectAssetPaths.Config.MagnetUpgradeProduct);
            Assert.That(profile, Is.Not.Null);
            return profile;
        }

        private static ShopProductProfile[] LoadProducts()
        {
            string[] paths =
            {
                ProjectAssetPaths.Config.FlashlightProduct,
                ProjectAssetPaths.Config.SolidGunProduct,
                ProjectAssetPaths.Config.PortalGunProduct,
                ProjectAssetPaths.Config.MagnetUpgradeProduct,
            };
            return paths.Select(path =>
            {
                ShopProductProfile profile =
                    AssetDatabase.LoadAssetAtPath<ShopProductProfile>(path);
                Assert.That(profile, Is.Not.Null, path);
                return profile;
            }).ToArray();
        }

        private static void RestorePreference(
            string key,
            bool existed,
            int value)
        {
            if (existed)
                PlayerPrefs.SetInt(key, value);
            else
                PlayerPrefs.DeleteKey(key);
        }

        private static void RestoreFloatPreference(
            string key,
            bool existed,
            float value)
        {
            if (existed)
                PlayerPrefs.SetFloat(key, value);
            else
                PlayerPrefs.DeleteKey(key);
        }
    }
}
