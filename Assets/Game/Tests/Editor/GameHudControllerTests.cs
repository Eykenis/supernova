using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using Supernova.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Supernova.Tests
{
    public sealed class GameHudControllerTests
    {
        private GameObject hudObject;
        private GameObject sourceObject;
        private GameObject menuObject;

        [TearDown]
        public void TearDown()
        {
            if (hudObject != null) Object.DestroyImmediate(hudObject);
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
            if (menuObject != null) Object.DestroyImmediate(menuObject);
        }

        [Test]
        public void RebuildDefaultView_CreatesUguiCrosshairAndHealthWidget()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();

            controller.RebuildDefaultView();

            Assert.That(hudObject.GetComponentInChildren<Canvas>(), Is.Not.Null);
            Assert.That(hudObject.GetComponentInChildren<CanvasScaler>(), Is.Not.Null);
            Assert.That(hudObject.transform.Find(UiHierarchyPaths.Hud.CrosshairHorizontal)?.GetComponent<Image>(), Is.Not.Null);
            Assert.That(hudObject.transform.Find(UiHierarchyPaths.Hud.CrosshairVertical)?.GetComponent<Image>(), Is.Not.Null);
            Assert.That(hudObject.transform.Find(UiHierarchyPaths.Hud.HealthPanel), Is.Not.Null);
            Assert.That(
                hudObject.transform.Find(UiHierarchyPaths.Hud.HealthFrame)
                    ?.GetComponent<Image>(),
                Is.Not.Null);
            Assert.That(hudObject.transform.Find(UiHierarchyPaths.Hud.HealthFill)?.GetComponent<Image>(), Is.Not.Null);
            Assert.That(hudObject.transform.Find(UiHierarchyPaths.Hud.HealthValue)?.GetComponent<TMP_Text>(), Is.Not.Null);
            RectTransform healthPanel =
                (RectTransform)hudObject.transform.Find(UiHierarchyPaths.Hud.HealthPanel);
            Assert.That(healthPanel.anchorMin, Is.EqualTo(Vector2.one));
            Assert.That(healthPanel.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(healthPanel.pivot, Is.EqualTo(Vector2.one));
            Assert.That(healthPanel.anchoredPosition, Is.EqualTo(new Vector2(-24f, -24f)));
            Transform hotbar = hudObject.transform.Find(UiHierarchyPaths.Hud.Hotbar);
            Assert.That(hotbar, Is.Not.Null);
            Assert.That(hotbar.childCount, Is.EqualTo(PlayerInventory.SlotCount));
            Assert.That(hotbar.Find(UiHierarchyPaths.Hud.SlotItem(1))?.GetComponent<TMP_Text>().text, Is.EqualTo("PICKAXE"));
            Assert.That(hotbar.Find(UiHierarchyPaths.Hud.SlotItem(2))?.GetComponent<TMP_Text>().text, Is.EqualTo("MAGNET"));
            Assert.That(
                hotbar.Find(UiHierarchyPaths.Hud.SlotItem(3))?.GetComponent<TMP_Text>().text,
                Is.EqualTo("FLASHLIGHT"));
            Assert.That(hotbar.Find(UiHierarchyPaths.Hud.SlotKey(10))?.GetComponent<TMP_Text>().text, Is.EqualTo("0"));
            Assert.That(
                hotbar.Find(UiHierarchyPaths.Hud.SlotFrame(1))?.GetComponent<Image>(),
                Is.Not.Null);
        }

        [Test]
        public void PauseMenu_HasResumeAndBackEquipmentSlotAndTogglesVisibility()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();

            Transform panel = hudObject.transform.Find(UiHierarchyPaths.Pause.Panel);
            Transform resume = panel.Find(UiHierarchyPaths.Pause.MenuResume);
            Selectable[] options = panel.GetComponentsInChildren<Selectable>(true);

            Assert.That(panel.gameObject.activeSelf, Is.False);
            Assert.That(options, Has.Length.EqualTo(2));
            Assert.That(resume.GetComponent<Button>(), Is.Not.Null);
            Assert.That(resume.Find(UiHierarchyPaths.Pause.Label).GetComponent<TMP_Text>().text, Is.EqualTo("RESUME"));
            Transform backSlot = panel.Find(UiHierarchyPaths.Pause.MenuBackSlot);
            Assert.That(backSlot.GetComponent<Button>(), Is.Not.Null);
            Assert.That(
                backSlot.Find(UiHierarchyPaths.Pause.SlotName).GetComponent<TMP_Text>().text,
                Is.EqualTo("BACK MODULE"));
            Assert.That(
                panel.Find(UiHierarchyPaths.Pause.MenuFrame)?.GetComponent<Image>(),
                Is.Not.Null);
            Assert.That(controller.PauseCanvas.sortingOrder,
                Is.GreaterThan(controller.LoadingCanvas.sortingOrder));

            controller.PauseGame();
            Assert.That(controller.IsPauseMenuVisible, Is.True);
            Assert.That(GameHudController.IsPauseMenuOpen, Is.True);

            controller.ResumeGame();
            Assert.That(controller.IsPauseMenuVisible, Is.False);
            Assert.That(GameHudController.IsPauseMenuOpen, Is.False);
        }

        [Test]
        public void LoadingView_CoversGameplayUntilTheInitialWorldIsReady()
        {
            sourceObject = new GameObject("Caves");
            MinecraftCaveInfiniteWorld terrain =
                sourceObject.AddComponent<MinecraftCaveInfiniteWorld>();
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();
            controller.BindLoadingSource(terrain);

            controller.RebuildDefaultView();
            controller.RefreshNow();

            Transform panel = hudObject.transform.Find(UiHierarchyPaths.Loading.Panel);
            TMP_Text progress = hudObject.transform.Find(
                UiHierarchyPaths.Loading.Progress).GetComponent<TMP_Text>();
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.gameObject.activeSelf, Is.True);
            Assert.That(progress.text, Is.EqualTo("0%"));
            Assert.That(
                panel.Find(UiHierarchyPaths.Loading.LocalSpinner).GetComponent<Image>().sprite,
                Is.Not.Null);
            Assert.That(
                panel.Find(UiHierarchyPaths.Decoration.Telemetry)?.GetComponent<RawImage>(),
                Is.Not.Null);
            Assert.That(controller.LoadingCanvas.sortingOrder,
                Is.GreaterThan(controller.CrosshairCanvas.sortingOrder));

            FieldInfo readyField = typeof(MinecraftCaveInfiniteWorld).GetField(
                "initialLoadComplete", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(readyField, Is.Not.Null);
            readyField.SetValue(terrain, true);
            controller.RefreshNow();

            Assert.That(panel.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void PauseMenu_IsBlockedWhileInitialWorldIsLoading()
        {
            sourceObject = new GameObject("Caves");
            MinecraftCaveInfiniteWorld terrain =
                sourceObject.AddComponent<MinecraftCaveInfiniteWorld>();
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();
            controller.BindLoadingSource(terrain);
            controller.RebuildDefaultView();
            controller.RefreshNow();

            Assert.That(controller.CanPauseGame, Is.False);
            controller.PauseGame();

            Assert.That(controller.IsPauseMenuVisible, Is.False);
            Assert.That(GameHudController.IsPauseMenuOpen, Is.False);
        }

        [Test]
        public void PauseMenu_IsBlockedOnMainMenuPage()
        {
            menuObject = new GameObject("Main Menu");
            menuObject.SetActive(false);
            menuObject.AddComponent<MainMenuController>();
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();

            Assert.That(controller.CanPauseGame, Is.False);
            controller.TogglePauseMenu();

            Assert.That(controller.IsPauseMenuVisible, Is.False);
            Assert.That(GameHudController.IsPauseMenuOpen, Is.False);
        }

        [Test]
        public void Presenter_UpdatesHealthTextAndFillPercentage()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();

            GameObject panel = hudObject.transform.Find(UiHierarchyPaths.Hud.HealthPanel).gameObject;
            RectTransform fill = (RectTransform)hudObject.transform.Find(UiHierarchyPaths.Hud.HealthFill);
            Image fillImage = fill.GetComponent<Image>();
            TMP_Text value = hudObject.transform.Find(UiHierarchyPaths.Hud.HealthValue).GetComponent<TMP_Text>();
            var presenter = new GameHudPresenter(panel, fill, fillImage, value);

            presenter.SetHealth(25f, 100f);

            Assert.That(value.text, Is.EqualTo("25 / 100"));
            Assert.That(fill.anchorMax.x, Is.EqualTo(0.25f).Within(0.001f));
        }

        [Test]
        public void InventorySelection_UpdatesHotbarHighlight()
        {
            sourceObject = new GameObject("Player");
            PlayerToolController inventory = sourceObject.AddComponent<PlayerToolController>();
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();

            controller.RebuildDefaultView();
            controller.BindInventorySource(inventory);

            inventory.SelectSlot(1);

            Image first = hudObject.transform.Find(UiHierarchyPaths.Hud.HotbarSlot(1)).GetComponent<Image>();
            Image second = hudObject.transform.Find(UiHierarchyPaths.Hud.HotbarSlot(2)).GetComponent<Image>();
            Image firstFrame = hudObject.transform.Find(
                UiHierarchyPaths.Hud.HotbarSlotFrame(1)).GetComponent<Image>();
            Image secondFrame = hudObject.transform.Find(
                UiHierarchyPaths.Hud.HotbarSlotFrame(2)).GetComponent<Image>();
            Assert.That(first.color, Is.EqualTo(Color.clear));
            Assert.That(second.color, Is.EqualTo(Color.clear));
            Assert.That(secondFrame.color, Is.Not.EqualTo(firstFrame.color));
            Assert.That(controller.InventorySource, Is.SameAs(inventory));
        }
    }

    internal sealed class FakeDamageable : MonoBehaviour, IDamageable
    {
        private float current;
        private float maximum;

        public GameObject Owner => gameObject;
        public float CurrentHealth => current;
        public float MaximumHealth => maximum;
        public bool IsAlive => current > 0f;

        public void SetHealth(float currentHealth, float maximumHealth)
        {
            maximum = maximumHealth;
            current = currentHealth;
        }

        public bool ReceiveDamage(in DamageInfo damage)
        {
            current = Mathf.Max(0f, current - damage.Amount);
            return true;
        }
    }
}
