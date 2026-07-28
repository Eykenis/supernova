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
        public void PlayerInventory_OwnershipPredicateLocksAndUnlocksFlashlight()
        {
            var inventory = new PlayerInventory(
                2,
                item => item != PlayerInventoryItem.Flashlight);

            Assert.That(
                inventory.GetItemAtSlot(2),
                Is.EqualTo(PlayerInventoryItem.Empty));
            Assert.That(inventory.SelectedItem,
                Is.EqualTo(PlayerInventoryItem.Empty));

            Assert.That(
                inventory.SetItemOwned(
                    PlayerInventoryItem.Flashlight,
                    true),
                Is.True);
            Assert.That(
                inventory.GetItemAtSlot(2),
                Is.EqualTo(PlayerInventoryItem.Flashlight));
            Assert.That(
                inventory.SelectedItem,
                Is.EqualTo(PlayerInventoryItem.Flashlight));
        }

        [Test]
        public void FlashlightProduct_HasPriceAndConfiguredDisplayAssets()
        {
            ShopProductProfile profile =
                LoadFlashlightProduct();
            PlayerToolDefinition tool =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.FlashlightTool);

            Assert.That(profile.IsConfigured, Is.True);
            Assert.That(profile.ProductId, Is.EqualTo("flashlight"));
            Assert.That(profile.DisplayName, Is.EqualTo("照明灯"));
            Assert.That(profile.Price, Is.EqualTo(100));
            Assert.That(
                profile.GrantedItem,
                Is.EqualTo(PlayerInventoryItem.Flashlight));
            Assert.That(
                profile.DisplayPrefab,
                Is.SameAs(tool.HeldModelPrefab));
            Assert.That(profile.WireframeMaterial, Is.Not.Null);
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

            if (display.IsOwned)
            {
                Assert.That(display.Label.text, Is.EqualTo("已拥有"));
                Assert.That(
                    display.Label.color,
                    Is.EqualTo(WorldValueTextStyle.OwnedColor));
                Assert.That(display.IsShowingSolid, Is.True);
                Assert.That(display.IsShowingWireframe, Is.False);
            }
            else
            {
                Assert.That(
                    display.Label.text,
                    Is.EqualTo("$100\n按 E 购买"));
                Assert.That(
                    display.Label.color,
                    Is.EqualTo(PlayerEconomy.CanAfford(profile)
                        ? WorldValueTextStyle.ValueColor
                        : WorldValueTextStyle.LossColor));
                Assert.That(display.IsShowingSolid, Is.False);
                Assert.That(display.IsShowingWireframe, Is.True);
            }
        }

        [Test]
        public void HomeScene_HasNearbyShopAnchorWithoutCustomShopModel()
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
                GameObject player = roots.FirstOrDefault(
                    root => root.name == "Player");

                Assert.That(shop, Is.Not.Null);
                Assert.That(player, Is.Not.Null);
                Assert.That(
                    shop.GetComponent<HomeShopController>(),
                    Is.Not.Null);
                Assert.That(
                    shop.GetComponent<HomeShopController>()
                        .ProductProfile,
                    Is.SameAs(LoadFlashlightProduct()));
                Assert.That(
                    Vector3.Distance(
                        shop.transform.position,
                        player.transform.position),
                    Is.LessThan(5f));
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
    }
}
