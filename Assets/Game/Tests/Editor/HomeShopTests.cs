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
        public void HomeScene_HasThreeProductAnchorsWithoutCustomShopModel()
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
                Assert.That(controllers, Has.Length.EqualTo(3));
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
                ProjectAssetPaths.Config.FlashlightProduct,
                ProjectAssetPaths.Config.SolidGunProduct,
                ProjectAssetPaths.Config.PortalGunProduct,
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
