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
        public void PlayerOwnedItems_SerializesInventoryAndUpgradesTogether()
        {
            var ownedItems = new PlayerOwnedItems();

            Assert.That(
                ownedItems.SetOwned(PlayerInventoryItem.Gun, true),
                Is.True);
            Assert.That(
                ownedItems.SetOwned(PlayerUpgrade.AttractionModule, true),
                Is.True);

            string json = JsonUtility.ToJson(ownedItems);
            var restored = JsonUtility.FromJson<PlayerOwnedItems>(json);
            Assert.That(restored.Owns(PlayerInventoryItem.Gun), Is.True);
            Assert.That(
                restored.Owns(PlayerUpgrade.AttractionModule),
                Is.True);
        }

        [Test]
        public void ShopProducts_AreAllConfigured()
        {
            ShopProductProfile[] products = LoadProducts();

            Assert.That(products, Has.Length.EqualTo(7));
            Assert.That(
                products.Select(product => product.ProductId),
                Is.EquivalentTo(new[]
                {
                    "gun",
                    "smg",
                    "flashlight",
                    "solid-gun",
                    "portal-gun",
                    "attraction-module",
                    "cart",
                }));
            Assert.That(
                products.All(product => product.IsConfigured),
                Is.True);
            Assert.That(
                products.All(product =>
                    product.WireframeMaterial != null
                    && AssetDatabase.GetAssetPath(
                        product.WireframeMaterial)
                    == ProjectAssetPaths.Materials.ShopGeometryWireframe),
                Is.True);

            ShopProductProfile upgrade = products.Single(product =>
                product.GrantType == ShopProductGrantType.Upgrade);
            Assert.That(
                upgrade.GrantedUpgrade,
                Is.EqualTo(PlayerUpgrade.AttractionModule));
            Assert.That(upgrade.UpgradeValue, Is.EqualTo(400f));

            ShopProductProfile cart = products.Single(product =>
                product.ProductId == "cart");
            Assert.That(cart.Price, Is.EqualTo(250));
            Assert.That(
                cart.GrantedItem,
                Is.EqualTo(PlayerInventoryItem.Cart));
            Assert.That(
                AssetDatabase.GetAssetPath(cart.DisplayPrefab),
                Is.EqualTo(ProjectAssetPaths.ThirdParty.EmptyCart));

            PlayerToolDefinition cartTool =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.CartTool);
            Assert.That(cartTool, Is.Not.Null);
            Assert.That(cartTool.Item, Is.EqualTo(PlayerInventoryItem.Cart));
            Assert.That(
                cartTool.PrimaryAction,
                Is.EqualTo(PlayerToolPrimaryAction.TowCart));
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
        public void CartPurchase_AddsItemToBackpackWithoutAutoAssigningQuickSlot()
        {
            ShopProductProfile profile = LoadProducts().Single(product =>
                product.ProductId == "cart");
            string creditsKey = PlayerEconomy.CreditsPreferenceKey;
            string ownershipKey =
                PlayerEconomy.GetItemOwnershipPreferenceKey(
                    PlayerInventoryItem.Cart);
            bool hadCredits = PlayerPrefs.HasKey(creditsKey);
            bool hadOwnership = PlayerPrefs.HasKey(ownershipKey);
            int previousCredits = PlayerPrefs.GetInt(creditsKey, 0);
            int previousOwnership = PlayerPrefs.GetInt(ownershipKey, 0);

            try
            {
                PlayerPrefs.SetInt(creditsKey, profile.Price);
                PlayerPrefs.DeleteKey(ownershipKey);
                var inventory = new PlayerInventory(
                    3,
                    PlayerEconomy.IsItemOwned);

                Assert.That(
                    inventory.GetItemAtSlot(3),
                    Is.EqualTo(PlayerInventoryItem.Empty));
                Assert.That(
                    PlayerEconomy.TryPurchase(profile),
                    Is.EqualTo(ShopPurchaseResult.Purchased));
                Assert.That(
                    PlayerEconomy.IsItemOwned(PlayerInventoryItem.Cart),
                    Is.True);
                Assert.That(
                    inventory.GetItemAtSlot(3),
                    Is.EqualTo(PlayerInventoryItem.Empty),
                    "Purchasing an item must not silently overwrite the loadout.");
                Assert.That(
                    inventory.SetItemAtSlot(
                        3,
                        PlayerInventoryItem.Cart),
                    Is.True);
                Assert.That(
                    inventory.GetItemAtSlot(3),
                    Is.EqualTo(PlayerInventoryItem.Cart));
                Assert.That(PlayerEconomy.Credits, Is.Zero);
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
        public void UpgradePurchase_PersistsAttractionModuleOwnership()
        {
            ShopProductProfile profile = LoadProducts().Single(product =>
                product.GrantType == ShopProductGrantType.Upgrade);
            string creditsKey = PlayerEconomy.CreditsPreferenceKey;
            string ownershipKey =
                PlayerEconomy.GetUpgradeOwnershipPreferenceKey(
                    PlayerUpgrade.AttractionModule);
            bool hadCredits = PlayerPrefs.HasKey(creditsKey);
            bool hadOwnership = PlayerPrefs.HasKey(ownershipKey);
            int previousCredits = PlayerPrefs.GetInt(creditsKey, 0);
            int previousOwnership = PlayerPrefs.GetInt(ownershipKey, 0);

            try
            {
                PlayerPrefs.SetInt(creditsKey, profile.Price);
                PlayerPrefs.DeleteKey(ownershipKey);

                Assert.That(
                    PlayerEconomy.TryPurchase(profile),
                    Is.EqualTo(ShopPurchaseResult.Purchased));
                Assert.That(PlayerEconomy.Credits, Is.Zero);
                Assert.That(
                    PlayerEconomy.IsUpgradeOwned(
                        PlayerUpgrade.AttractionModule),
                    Is.True);
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
            MeshRenderer[] renderers =
                displayObject.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(
                renderers.Length,
                Is.EqualTo(display.SolidRendererCount));
            Assert.That(
                renderers.Any(renderer =>
                    renderer.gameObject.name.EndsWith(" Wireframe")),
                Is.False,
                "Wireframe presentation must not create duplicate renderers.");

            if (display.IsOwned)
            {
                Assert.That(display.Label.text, Is.EqualTo("OWNED"));
                Assert.That(
                    display.Label.color,
                    Is.EqualTo(WorldValueTextStyle.OwnedColor));
                Assert.That(display.IsShowingSolid, Is.True);
                Assert.That(display.IsShowingWireframe, Is.False);
                Assert.That(
                    renderers.Any(renderer =>
                        renderer.sharedMaterials.Any(material =>
                            material != profile.WireframeMaterial)),
                    Is.True);
            }
            else
            {
                Assert.That(
                    display.Label.text,
                    Is.EqualTo("$100\nPRESS E TO BUY"));
                Assert.That(
                    display.Label.color,
                    Is.EqualTo(PlayerEconomy.CanAfford(profile)
                        ? WorldValueTextStyle.ValueColor
                        : WorldValueTextStyle.LossColor));
                Assert.That(display.IsShowingSolid, Is.False);
                Assert.That(display.IsShowingWireframe, Is.True);
                Assert.That(
                    renderers.All(renderer =>
                        renderer.sharedMaterials.All(material =>
                            material == profile.WireframeMaterial)),
                    Is.True);
            }
        }

        [Test]
        public void CartProductDisplay_DisablesPrefabPhysics()
        {
            ShopProductProfile profile = LoadProducts().Single(product =>
                product.ProductId == "cart");
            displayObject = new GameObject("Cart Product Display Test");
            ShopProductDisplay display =
                displayObject.AddComponent<ShopProductDisplay>();

            display.Configure(profile);

            Rigidbody[] bodies =
                displayObject.GetComponentsInChildren<Rigidbody>(true);
            Collider[] colliders =
                displayObject.GetComponentsInChildren<Collider>(true);
            Assert.That(bodies, Is.Not.Empty);
            Assert.That(
                bodies.All(body => body.isKinematic && !body.useGravity),
                Is.True);
            Assert.That(
                colliders
                    .Where(collider => collider != display.TargetCollider)
                    .All(collider => !collider.enabled),
                Is.True);
        }

        [Test]
        public void HomeScene_HasSevenProductAnchorsWithoutCustomShopModel()
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
                Assert.That(controllers, Has.Length.EqualTo(7));
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

        private static ShopProductProfile[] LoadProducts()
        {
            string[] paths =
            {
                ProjectAssetPaths.Config.GunProduct,
                ProjectAssetPaths.Config.SmgProduct,
                ProjectAssetPaths.Config.FlashlightProduct,
                ProjectAssetPaths.Config.SolidGunProduct,
                ProjectAssetPaths.Config.PortalGunProduct,
                ProjectAssetPaths.Config.AttractionModuleProduct,
                ProjectAssetPaths.Config.CartProduct,
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
    }
}
