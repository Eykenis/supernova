using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using Supernova.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Supernova.Tests
{
    public sealed class GameHudControllerTests
    {
        private GameObject hudObject;
        private GameObject sourceObject;
        private GameObject menuObject;
        private GameObject eventSystemObject;

        [TearDown]
        public void TearDown()
        {
            if (hudObject != null) Object.DestroyImmediate(hudObject);
            if (sourceObject != null) Object.DestroyImmediate(sourceObject);
            if (menuObject != null) Object.DestroyImmediate(menuObject);
            if (eventSystemObject != null) Object.DestroyImmediate(eventSystemObject);
        }

        [Test]
        public void RebuildDefaultView_CreatesUguiCrosshairAndHealthWidget()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();

            controller.RebuildDefaultView();

            Assert.That(hudObject.GetComponentInChildren<Canvas>(), Is.Not.Null);
            Assert.That(hudObject.GetComponentInChildren<CanvasScaler>(), Is.Not.Null);
            Assert.That(controller.MissionView, Is.Not.Null);
            Assert.That(controller.MissionOverlayCanvas, Is.Not.Null);
            Assert.That(
                hudObject.transform.Find(UiHierarchyPaths.Mission.Objective),
                Is.Not.Null);
            Assert.That(
                hudObject.transform.Find(UiHierarchyPaths.Mission.SceneFade),
                Is.Not.Null);
            Assert.That(
                hudObject.transform.Find(UiHierarchyPaths.Mission.TimerValue)
                    ?.GetComponent<TMP_Text>(),
                Is.Not.Null);
            RectTransform missionTimer =
                (RectTransform)hudObject.transform.Find(
                    UiHierarchyPaths.Mission.Timer);
            Assert.That(missionTimer.anchorMin, Is.EqualTo(new Vector2(0.5f, 1f)));
            Assert.That(missionTimer.anchoredPosition, Is.EqualTo(
                new Vector2(0f, -92f)));
            HeadingCompass compass = hudObject.transform.Find(
                UiHierarchyPaths.Hud.Compass)?.GetComponent<HeadingCompass>();
            Assert.That(compass, Is.Not.Null);
            Assert.That(controller.Compass, Is.SameAs(compass));
            RectTransform compassRect = (RectTransform)compass.transform;
            Assert.That(compassRect.anchorMin, Is.EqualTo(new Vector2(0.5f, 1f)));
            Assert.That(compassRect.anchorMax, Is.EqualTo(new Vector2(0.5f, 1f)));
            Assert.That(compassRect.anchoredPosition, Is.EqualTo(
                new Vector2(0f, -12f)));
            Transform compassTicks = hudObject.transform.Find(
                UiHierarchyPaths.Hud.CompassTicks);
            Assert.That(compassTicks, Is.Not.Null);
            Assert.That(compassTicks.childCount, Is.EqualTo(
                HeadingCompass.TickViewCount));
            Assert.That(
                hudObject.transform.Find(UiHierarchyPaths.Hud.CompassHeading)
                    ?.GetComponent<TMP_Text>(),
                Is.Not.Null);
            Assert.That(hudObject.transform.Find(UiHierarchyPaths.Hud.CrosshairHorizontal)?.GetComponent<Image>(), Is.Not.Null);
            Assert.That(hudObject.transform.Find(UiHierarchyPaths.Hud.CrosshairVertical)?.GetComponent<Image>(), Is.Not.Null);
            Assert.That(hudObject.transform.Find(UiHierarchyPaths.Hud.HealthPanel), Is.Not.Null);
            Assert.That(
                hudObject.transform.Find(UiHierarchyPaths.Hud.HealthSegment(1))
                    ?.GetComponent<AngledPanelGraphic>(),
                Is.Not.Null);
            Assert.That(hudObject.transform.Find(UiHierarchyPaths.Hud.HealthFill)?.GetComponent<Image>(), Is.Not.Null);
            Assert.That(hudObject.transform.Find(UiHierarchyPaths.Hud.HealthValue)?.GetComponent<TMP_Text>(), Is.Not.Null);
            RectTransform healthPanel =
                (RectTransform)hudObject.transform.Find(UiHierarchyPaths.Hud.HealthPanel);
            Assert.That(healthPanel.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(healthPanel.anchorMax, Is.EqualTo(Vector2.zero));
            Assert.That(healthPanel.pivot, Is.EqualTo(Vector2.zero));
            Assert.That(healthPanel.anchoredPosition, Is.EqualTo(new Vector2(48f, 42f)));
            Assert.That(healthPanel.localEulerAngles.z, Is.EqualTo(3.5f).Within(0.01f));
            bool healthReverse = hudObject.transform.Find(
                UiHierarchyPaths.Hud.HealthSegment(1))
                .GetComponent<AngledPanelGraphic>().Reverse;
            for (int i = 2; i <= 8; i++)
            {
                Assert.That(
                    hudObject.transform.Find(
                        UiHierarchyPaths.Hud.HealthSegment(i))
                        .GetComponent<AngledPanelGraphic>().Reverse,
                    Is.EqualTo(healthReverse),
                    "Every health segment must use one consistent slant direction.");
            }
            Transform hotbar = hudObject.transform.Find(UiHierarchyPaths.Hud.Hotbar);
            Assert.That(hotbar, Is.Not.Null);
            Assert.That(hotbar.childCount, Is.EqualTo(PlayerInventory.SlotCount));
            RectTransform hotbarRect = (RectTransform)hotbar;
            Assert.That(hotbarRect.anchorMin, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(hotbarRect.anchorMax, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(hotbarRect.pivot, Is.EqualTo(new Vector2(1f, 0f)));
            for (int i = 1; i <= PlayerInventory.SlotCount; i++)
            {
                Assert.That(
                    hotbar.Find(UiHierarchyPaths.Hud.SlotItem(i))
                        ?.GetComponent<TMP_Text>().text,
                    Is.Empty);
                Assert.That(
                    hotbar.Find(UiHierarchyPaths.Hud.SlotKey(i))
                        ?.GetComponent<TMP_Text>().text,
                    Is.EqualTo(i.ToString()));
            }
            Assert.That(
                hotbar.Find(UiHierarchyPaths.Hud.SlotAngledSurface(1))
                    ?.GetComponent<AngledPanelGraphic>(),
                Is.Not.Null);
            bool hotbarReverse = hotbar.Find(
                UiHierarchyPaths.Hud.SlotAngledSurface(1))
                .GetComponent<AngledPanelGraphic>().Reverse;
            for (int i = 2; i <= PlayerInventory.SlotCount; i++)
            {
                Assert.That(
                    hotbar.Find(UiHierarchyPaths.Hud.SlotAngledSurface(i))
                        .GetComponent<AngledPanelGraphic>().Reverse,
                    Is.EqualTo(hotbarReverse),
                    "Every item slot must use one consistent slant direction.");
            }
            Assert.That(
                ((RectTransform)hotbar).localEulerAngles.z,
                Is.EqualTo(356.5f).Within(0.01f));
        }

        [Test]
        public void Compass_FormatsCardinalsAndTracksWrappedHeading()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();

            HeadingCompass compass = controller.Compass;
            compass.RefreshHeading(90f);

            Transform ticks = hudObject.transform.Find(
                UiHierarchyPaths.Hud.CompassTicks);
            TMP_Text centeredLabel = ticks.GetChild(
                    HeadingCompass.TickViewCount / 2)
                .Find(UiHierarchyPaths.Hud.CompassTickLabel)
                .GetComponent<TMP_Text>();
            TMP_Text heading = hudObject.transform.Find(
                    UiHierarchyPaths.Hud.CompassHeading)
                .GetComponent<TMP_Text>();

            Assert.That(centeredLabel.text, Is.EqualTo("E"));
            Assert.That(heading.text, Is.EqualTo("090\u00B0"));
            Assert.That(HeadingCompass.GetHeadingLabel(315), Is.EqualTo("NW"));
            Assert.That(HeadingCompass.NormalizeHeading(-1f),
                Is.EqualTo(359f).Within(0.001f));
        }

        [Test]
        public void PauseMenu_HasLoadoutBackpackAndEquipmentControls()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();

            Transform panel = hudObject.transform.Find(UiHierarchyPaths.Pause.Panel);
            Transform resume = panel.Find(UiHierarchyPaths.Pause.MenuResume);
            Selectable[] options = panel.GetComponentsInChildren<Selectable>(true);

            Assert.That(panel.gameObject.activeSelf, Is.False);
            Assert.That(
                panel.GetComponent<Image>().color.a,
                Is.LessThan(1f),
                "The pause overlay should preserve the transparent in-game HUD style.");
            Assert.That(
                options.Length,
                Is.GreaterThanOrEqualTo(PlayerInventory.SlotCount + 3));
            Assert.That(
                panel.Find(
                    UiHierarchyPaths.Pause.Menu
                    + "/"
                    + UiHierarchyPaths.Pause.QuickSlots),
                Is.Not.Null);
            Assert.That(
                panel.Find(
                    UiHierarchyPaths.Pause.Menu
                    + "/"
                    + UiHierarchyPaths.Pause.Backpack),
                Is.Not.Null);
            TMP_Text firstQuickSlotLabel = panel.Find(
                    UiHierarchyPaths.Pause.Menu
                    + "/"
                    + UiHierarchyPaths.Pause.QuickSlots
                    + "/"
                    + UiHierarchyPaths.Pause.QuickSlotName(1)
                    + "/"
                    + UiHierarchyPaths.Pause.SlotItem)
                .GetComponent<TMP_Text>();
            Assert.That(
                firstQuickSlotLabel.color,
                Is.EqualTo(controller.DesignTokens != null
                    ? controller.DesignTokens.OverlayInverse
                    : new Color(0.018f, 0.02f, 0.025f, 1f)));
            Assert.That(resume.GetComponent<Button>(), Is.Not.Null);
            Assert.That(resume.Find(UiHierarchyPaths.Pause.Label).GetComponent<TMP_Text>().text, Is.EqualTo("RESUME"));
            Transform backSlot = panel.Find(UiHierarchyPaths.Pause.MenuBackSlot);
            Assert.That(backSlot.GetComponent<Button>(), Is.Not.Null);
            Assert.That(
                backSlot.Find(UiHierarchyPaths.Pause.SlotName).GetComponent<TMP_Text>().text,
                Is.EqualTo("BACK MODULE"));
            Assert.That(
                panel.Find(UiHierarchyPaths.Pause.MenuFrame),
                Is.Null,
                "The pause menu should not depend on the legacy sci-fi frame.");
            Assert.That(
                resume.GetComponent<Image>().color,
                Is.EqualTo(controller.DesignTokens != null
                    ? controller.DesignTokens.OverlayPrimary
                    : Color.white));
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
        public void RebuildDefaultView_ReusesSceneEventSystem()
        {
            eventSystemObject = new GameObject(
                "Scene EventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            EventSystem sceneEventSystem = eventSystemObject.GetComponent<EventSystem>();
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();

            controller.RebuildDefaultView();

            Assert.That(
                GameHudController.EnsureSingleEventSystem(hudObject.transform),
                Is.SameAs(sceneEventSystem));
            Assert.That(
                hudObject.GetComponentsInChildren<EventSystem>(true),
                Is.Empty,
                "The HUD must reuse the scene input system instead of creating a second one.");
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
            Assert.That(
                panel.GetComponent<Image>().color.a,
                Is.LessThan(1f),
                "The loading overlay should keep the game visible behind it.");
            Assert.That(progress.text, Is.EqualTo("0%"));
            Assert.That(
                panel.Find(UiHierarchyPaths.Loading.LocalSpinner).GetComponent<Image>().sprite,
                Is.Null,
                "The loading indicator should use the minimal built-in white shape.");
            Assert.That(
                panel.Find(UiHierarchyPaths.Decoration.Telemetry),
                Is.Null,
                "The loading screen should not use the legacy telemetry texture.");
            Assert.That(
                panel.Find(UiHierarchyPaths.Loading.LocalProgressFill)
                    .GetComponent<Image>().color,
                Is.EqualTo(controller.DesignTokens != null
                    ? controller.DesignTokens.OverlayPrimary
                    : Color.white));
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
        public void GameplayVisibility_KeepsTransitionOverlayAlive()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller =
                hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();
            MethodInfo setVisible = typeof(GameHudController).GetMethod(
                "SetGameplayViewVisible",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(setVisible, Is.Not.Null);

            setVisible.Invoke(controller, new object[] { false });

            Assert.That(hudObject.activeSelf, Is.True);
            Assert.That(controller.RootCanvas.gameObject.activeSelf, Is.False);
            Assert.That(
                controller.MissionOverlayCanvas.gameObject.activeSelf,
                Is.True,
                "The persistent overlay must remain renderable during scene fades.");

            setVisible.Invoke(controller, new object[] { true });

            Assert.That(controller.RootCanvas.gameObject.activeSelf, Is.True);
            Assert.That(controller.PauseCanvas.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void GameplayVisibility_RebindsDestroyedHealthSourceToCurrentPlayer()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller =
                hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();

            GameObject previousPlayer = new GameObject("Previous Player");
            FakeDamageable previousHealth =
                previousPlayer.AddComponent<FakeDamageable>();
            previousHealth.SetHealth(100f, 100f);
            controller.BindHealthSource(previousHealth);
            Object.DestroyImmediate(previousPlayer);

            sourceObject = new GameObject("Current Player");
            sourceObject.AddComponent<CharacterController>();
            FakeDamageable currentHealth =
                sourceObject.AddComponent<FakeDamageable>();
            currentHealth.SetHealth(70f, 100f);

            MethodInfo setVisible = typeof(GameHudController).GetMethod(
                "SetGameplayViewVisible",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(setVisible, Is.Not.Null);
            setVisible.Invoke(controller, new object[] { true });

            Assert.That(controller.HealthSource, Is.SameAs(currentHealth));
            TMP_Text value = hudObject.transform.Find(
                    UiHierarchyPaths.Hud.HealthValue)
                .GetComponent<TMP_Text>();
            Assert.That(value.text, Is.EqualTo("70 / 100"));
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
            AngledPanelGraphic firstSurface = hudObject.transform.Find(
                UiHierarchyPaths.Hud.HotbarSlotAngledSurface(1))
                .GetComponent<AngledPanelGraphic>();
            AngledPanelGraphic secondSurface = hudObject.transform.Find(
                UiHierarchyPaths.Hud.HotbarSlotAngledSurface(2))
                .GetComponent<AngledPanelGraphic>();
            Assert.That(first.color, Is.EqualTo(Color.clear));
            Assert.That(second.color, Is.EqualTo(Color.clear));
            Assert.That(secondSurface.color, Is.Not.EqualTo(firstSurface.color));
            Assert.That(controller.InventorySource, Is.SameAs(inventory));
        }

        [Test]
        public void PauseLoadout_AssignsOwnedBackpackItemToChosenQuickSlot()
        {
            sourceObject = new GameObject("Player");
            PlayerToolController inventory =
                sourceObject.AddComponent<PlayerToolController>();
            hudObject = new GameObject("Game HUD");
            GameHudController controller =
                hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();
            controller.BindInventorySource(inventory);

            Transform quickSlots = hudObject.transform.Find(
                UiHierarchyPaths.Pause.FullQuickSlots);
            Button secondSlot = quickSlots.Find(
                    UiHierarchyPaths.Pause.QuickSlotName(2))
                .GetComponent<Button>();
            Transform backpack = hudObject.transform.Find(
                UiHierarchyPaths.Pause.FullBackpack);
            Button pickaxe = backpack.Find(
                    UiHierarchyPaths.Pause.BackpackItemName(
                        PlayerInventoryItem.Pickaxe))
                .GetComponent<Button>();

            secondSlot.onClick.Invoke();
            pickaxe.onClick.Invoke();

            Assert.That(
                inventory.GetItemAtSlot(1),
                Is.EqualTo(PlayerInventoryItem.Pickaxe));
            Assert.That(
                hudObject.transform.Find(
                        UiHierarchyPaths.Hud.SlotItem(2))
                    .GetComponent<TMP_Text>().text,
                Is.EqualTo("PICKAXE"));
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
