using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.Inputs;
using Supernova.MinecraftCaves;
using Supernova.UI;
using TMPro;
using UnityEditor;
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

        /// <summary>
        /// The crosshair durability line reports a tier instead of the raw number,
        /// and the unbreakable sentinel (bedrock's int.MaxValue durability) must
        /// resolve to a stable label rather than a negative value.
        /// </summary>
        [TestCase(1f, "硬度：很低")]
        [TestCase(80f, "硬度：很高")]
        [TestCase(999f, "硬度：极高")]
        [TestCase(1000f, "无法摧毁")]
        [TestCase(2147483647f, "无法摧毁")]
        public void CrosshairVoxelStats_ReportsDurabilityTier(
            float durability,
            string expected)
        {
            hudObject = new GameObject("Crosshair Info");
            CrosshairInfoDisplay display =
                hudObject.AddComponent<CrosshairInfoDisplay>();
            var statsObject = new GameObject("Stats", typeof(RectTransform));
            statsObject.transform.SetParent(hudObject.transform, false);
            TMP_Text stats = statsObject.AddComponent<TextMeshProUGUI>();
            display.StatsLabel = stats;

            System.Type infoType = typeof(CrosshairInfoDisplay).Assembly
                .GetType("Supernova.UI.CrosshairLookAtInfo");
            System.Type targetType = typeof(CrosshairInfoDisplay).Assembly
                .GetType("Supernova.UI.CrosshairTargetType");
            Assert.That(infoType, Is.Not.Null);
            Assert.That(targetType, Is.Not.Null);

            object info = System.Activator.CreateInstance(
                infoType,
                new object[]
                {
                    System.Enum.Parse(targetType, "Voxel"),
                    "Bedrock",
                    durability,
                    0f,
                    0,
                });
            MethodInfo showInfo = typeof(CrosshairInfoDisplay).GetMethod(
                "ShowInfo",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(showInfo, Is.Not.Null);
            showInfo.Invoke(display, new[] { info });

            Assert.That(stats.text, Is.EqualTo(expected));
        }

        [TestCase(0f, "硬度：很低")]
        [TestCase(4f, "硬度：很低")]
        [TestCase(5f, "硬度：低")]
        [TestCase(14f, "硬度：低")]
        [TestCase(15f, "硬度：中")]
        [TestCase(34f, "硬度：中")]
        [TestCase(35f, "硬度：高")]
        [TestCase(59f, "硬度：高")]
        [TestCase(60f, "硬度：很高")]
        [TestCase(99f, "硬度：很高")]
        [TestCase(100f, "硬度：极高")]
        [TestCase(999f, "硬度：极高")]
        [TestCase(1000f, "无法摧毁")]
        [TestCase(2147483647f, "无法摧毁")]
        public void FormatDurabilityLabel_MapsDurabilityToTier(
            float durability,
            string expected)
        {
            Assert.That(
                CrosshairInfoDisplay.FormatDurabilityLabel(durability),
                Is.EqualTo(expected));
        }

        [TestCase(0f, "极低")]
        [TestCase(0.01f, "极低")]
        [TestCase(0.06f, "极低")]
        [TestCase(0.07f, "低")]
        [TestCase(0.15f, "低")]
        [TestCase(0.16f, "中")]
        [TestCase(0.25f, "中")]
        [TestCase(0.29f, "中")]
        [TestCase(0.30f, "高")]
        [TestCase(0.49f, "高")]
        [TestCase(0.50f, "极高")]
        [TestCase(0.90f, "极高")]
        [TestCase(1f, "极高")]
        public void ResolveFragilityTier_MapsFragilityToTier(
            float fragility,
            string expected)
        {
            Assert.That(
                CrosshairInfoDisplay.ResolveFragilityTier(fragility),
                Is.EqualTo(expected));
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
            Transform sceneFade =
                hudObject.transform.Find(UiHierarchyPaths.Mission.SceneFade);
            Assert.That(sceneFade, Is.Not.Null);
            Canvas sceneTransitionCanvas = sceneFade.GetComponent<Canvas>();
            Assert.That(sceneTransitionCanvas, Is.Not.Null);
            Assert.That(sceneTransitionCanvas.overrideSorting, Is.True);
            Assert.That(sceneFade.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            Assert.That(
                sceneTransitionCanvas.sortingOrder,
                Is.GreaterThan(controller.PauseCanvas.sortingOrder));
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
            Assert.That(healthPanel.localScale, Is.EqualTo(Vector3.one * 1.15f));
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
            Assert.That(
                hotbar.childCount,
                Is.EqualTo(PlayerInventory.SlotCount));
            TMP_Text actionHints = hudObject.transform.Find(
                    UiHierarchyPaths.Hud.HotbarActionHintsLabel)
                ?.GetComponent<TMP_Text>();
            Assert.That(actionHints, Is.Not.Null);
            Assert.That(actionHints.text, Does.Contain("牵引"));
            Assert.That(actionHints.text, Does.Contain("蹲下"));
            Assert.That(actionHints.text, Does.Not.Contain("LMB"));
            Assert.That(actionHints.text, Does.Not.Contain("RMB"));
            Assert.That(actionHints.transform.parent, Is.Not.SameAs(hotbar));
            Assert.That(
                actionHints.transform.parent.localEulerAngles.z,
                Is.EqualTo(0f).Within(0.01f));
            Assert.That(actionHints.fontSize, Is.EqualTo(21f));
            Assert.That(
                actionHints.alignment,
                Is.EqualTo(TextAlignmentOptions.BottomRight));
            Assert.That(
                actionHints.transform.parent.localScale,
                Is.EqualTo(Vector3.one * 1.15f));
            RectTransform hotbarRect = (RectTransform)hotbar;
            Assert.That(hotbarRect.anchorMin, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(hotbarRect.anchorMax, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(hotbarRect.pivot, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(hotbarRect.localScale, Is.EqualTo(Vector3.one * 1.15f));
            for (int i = 1; i <= PlayerInventory.SlotCount; i++)
            {
                Assert.That(
                    hotbar.Find(UiHierarchyPaths.Hud.SlotItem(i))
                        ?.GetComponent<TMP_Text>().text,
                    Is.Empty);
                Assert.That(
                    hotbar.Find(UiHierarchyPaths.Hud.SlotKey(i))
                        ?.GetComponent<TMP_Text>().text,
                    Is.EqualTo(InputPromptResolver.Token(
                        (GameInputActionId)(
                            (int)GameInputActionId.Hotbar1 + i - 1))));
                Assert.That(
                    hotbar.Find(UiHierarchyPaths.Hud.SlotKey(i))
                        ?.GetComponent<TMP_Text>().fontSize,
                    Is.EqualTo(14f));
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
        public void FpsDebugWindow_StartsHiddenAndCanBeToggled()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller =
                hudObject.AddComponent<GameHudController>();

            controller.RebuildDefaultView();

            Transform debugWindow = hudObject.transform.Find(
                UiHierarchyPaths.Debug.Window);
            TMP_Text fpsValue = hudObject.transform.Find(
                UiHierarchyPaths.Debug.FpsValue)?.GetComponent<TMP_Text>();
            Assert.That(controller.DebugCanvas, Is.Not.Null);
            Assert.That(debugWindow, Is.Not.Null);
            Assert.That(fpsValue, Is.SameAs(controller.FpsDebugValueLabel));
            Assert.That(fpsValue.text, Is.EqualTo("FPS  --"));
            Assert.That(controller.IsFpsDebugVisible, Is.False);

            controller.ToggleFpsDebugWindow();

            Assert.That(controller.IsFpsDebugVisible, Is.True);
            controller.ToggleFpsDebugWindow();
            Assert.That(controller.IsFpsDebugVisible, Is.False);
        }

        [Test]
        public void FpsDebugWindow_ReportsAverageUnscaledFrameRate()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller =
                hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();
            controller.SetFpsDebugVisible(true);
            MethodInfo updateFps = typeof(GameHudController).GetMethod(
                "UpdateFpsDebugWindow",
                BindingFlags.Instance | BindingFlags.NonPublic);

            for (int i = 0; i < 13; i++)
                updateFps.Invoke(controller, new object[] { 0.02f });

            Assert.That(controller.FpsDebugValueLabel.text, Is.EqualTo("FPS  50"));
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
        public void PauseMenu_UsesDiagonalSystemLayoutAndFourPrimaryActions()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller = hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();

            Transform panel = hudObject.transform.Find(UiHierarchyPaths.Pause.Panel);
            Transform resume = panel.Find(UiHierarchyPaths.Pause.MenuResume);
            Selectable[] options = panel.GetComponentsInChildren<Selectable>(true);
            Transform systemField = panel.Find(UiHierarchyPaths.Pause.SystemField);

            Assert.That(panel.gameObject.activeSelf, Is.False);
            Assert.That(
                panel.GetComponent<Image>().color.a,
                Is.EqualTo(1f).Within(0.001f),
                "The pause overlay should fully cover gameplay with opaque dark gray.");
            Assert.That(
                options.Length,
                Is.EqualTo(10),
                "Primary actions, settings controls, and binding-menu actions are expected.");
            Assert.That(
                systemField.GetComponent<PauseMenuWedgeGraphic>(),
                Is.Not.Null);
            Color systemFieldColor = systemField
                .GetComponent<PauseMenuWedgeGraphic>().color;
            Assert.That(systemFieldColor.r, Is.GreaterThan(0.9f));
            Assert.That(systemFieldColor.g, Is.GreaterThan(0.9f));
            Assert.That(systemFieldColor.b, Is.GreaterThan(0.9f));
            Assert.That(systemFieldColor.a, Is.EqualTo(1f).Within(0.001f));
            Assert.That(resume.GetComponent<Button>(), Is.Not.Null);
            TMP_Text resumeLabel = resume.Find(UiHierarchyPaths.Pause.Label)
                .GetComponent<TMP_Text>();
            Assert.That(resumeLabel.text, Is.EqualTo("RESUME"));
            Assert.That(resumeLabel.color.r, Is.EqualTo(1f).Within(0.001f));
            Assert.That(resumeLabel.color.g, Is.EqualTo(1f).Within(0.001f));
            Assert.That(resumeLabel.color.b, Is.EqualTo(1f).Within(0.001f));
            TMP_Text title = panel.Find(
                    UiHierarchyPaths.Pause.Menu
                    + "/"
                    + UiHierarchyPaths.Pause.MainOptions
                    + "/"
                    + UiHierarchyPaths.Pause.Title)
                .GetComponent<TMP_Text>();
            Assert.That(title.color.r, Is.LessThan(0.1f));
            Assert.That(title.color.g, Is.LessThan(0.1f));
            Assert.That(title.color.b, Is.LessThan(0.1f));
            Assert.That(
                hudObject.transform.Find(UiHierarchyPaths.Pause.FullSettings)
                    .GetComponent<Button>(),
                Is.Not.Null);
            Assert.That(
                hudObject.transform.Find(UiHierarchyPaths.Pause.FullControls)
                    .GetComponent<Button>(),
                Is.Not.Null);
            Assert.That(
                hudObject.transform.Find(
                    UiHierarchyPaths.Pause.FullInputBindingsPanel)
                    .GetComponent<InputBindingSettingsView>(),
                Is.Not.Null);
            Assert.That(
                hudObject.transform.Find(UiHierarchyPaths.Pause.FullQuitToMenu)
                    .GetComponent<Button>(),
                Is.Not.Null);
            Assert.That(
                hudObject.transform.Find(UiHierarchyPaths.Pause.FullQuitToDesktop)
                    .GetComponent<Button>(),
                Is.Not.Null);
            Assert.That(panel.Find("Menu/Quick Slots"), Is.Null);
            Assert.That(panel.Find("Menu/Backpack"), Is.Null);
            Assert.That(panel.Find("Menu/Back Slot"), Is.Null);
            Assert.That(
                panel.Find(UiHierarchyPaths.Pause.MenuFrame),
                Is.Null,
                "The pause menu should not depend on the legacy sci-fi frame.");
            Assert.That(controller.PauseCanvas.sortingOrder,
                Is.GreaterThan(controller.LoadingCanvas.sortingOrder));

            controller.PauseGame();
            Assert.That(controller.IsPauseMenuVisible, Is.True);
            Assert.That(GameHudController.IsPauseMenuOpen, Is.True);
            Assert.That(controller.RootCanvas.gameObject.activeSelf, Is.False);
            Assert.That(controller.CrosshairCanvas.gameObject.activeSelf, Is.False);
            Assert.That(
                panel.Find("Portrait Field")?.GetComponent<Mask>(),
                Is.Not.Null,
                "The portrait RenderTexture must be clipped before the translucent menu region.");
            PausePortraitFieldGraphic portraitField = panel.Find("Portrait Field")
                ?.GetComponent<PausePortraitFieldGraphic>();
            Assert.That(portraitField, Is.Not.Null);
            Assert.That(
                portraitField.BottomEdgeFromLeft,
                Is.EqualTo(1920f - PauseMenuWedgeGraphic.SystemFieldWidth)
                    .Within(0.001f));
            Assert.That(
                portraitField.TopEdgeFromLeft,
                Is.EqualTo(
                    1920f
                    - PauseMenuWedgeGraphic.SystemFieldWidth
                    + PauseMenuWedgeGraphic.SystemFieldTopInset)
                    .Within(0.001f));

            controller.ResumeGame();
            Assert.That(controller.IsPauseMenuVisible, Is.False);
            Assert.That(GameHudController.IsPauseMenuOpen, Is.False);
            Assert.That(controller.RootCanvas.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void EquipmentMenu_UsesFullWidthWithoutPortraitAndKeepsItemCells()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller =
                hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();

            EquipmentLoadoutMenu menu = controller.EquipmentMenu;
            Transform panel = hudObject.transform.Find(
                UiHierarchyPaths.Equipment.Panel);
            RectTransform configuration = (RectTransform)panel.Find(
                UiHierarchyPaths.Equipment.Configuration);
            Transform slots = hudObject.transform.Find(
                UiHierarchyPaths.Equipment.FullSlots);
            Transform ownedGrid = hudObject.transform.Find(
                UiHierarchyPaths.Equipment.FullOwnedGrid);

            Assert.That(menu, Is.Not.Null);
            Assert.That(panel.gameObject.activeSelf, Is.False);
            Assert.That(panel.GetComponent<Image>().color.a, Is.LessThan(1f));
            Assert.That(panel.Find(UiHierarchyPaths.Equipment.PortraitRegion), Is.Null);
            Assert.That(configuration.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(configuration.anchorMax, Is.EqualTo(Vector2.one));

            for (int i = 1; i <= PlayerInventory.SlotCount; i++)
            {
                Transform slot = slots.Find(
                    UiHierarchyPaths.Equipment.SlotName(i));
                Assert.That(slot, Is.Not.Null);
                Assert.That(slot.GetComponent<Button>(), Is.Not.Null);
                EquipmentMenuInteraction interaction =
                    slot.GetComponent<EquipmentMenuInteraction>();
                Assert.That(interaction, Is.Not.Null);
                Assert.That(interaction.IsEquipmentSlotTarget, Is.True);
                Assert.That(interaction.Index, Is.EqualTo(i - 1));
                Assert.That(
                    slot.Find("Angled Surface")
                        .GetComponent<AngledPanelGraphic>().color.a,
                    Is.EqualTo(1f).Within(0.001f));
                Image icon = slot.Find(UiHierarchyPaths.Equipment.Icon)
                    .GetComponent<Image>();
                Assert.That(icon, Is.Not.Null);
                Assert.That(icon.preserveAspect, Is.True);
                Assert.That(icon.raycastTarget, Is.False);
            }

            for (int i = 1; i <= EquipmentLoadoutMenu.OwnedGridCellCount; i++)
            {
                Transform cell = ownedGrid.Find(
                    UiHierarchyPaths.Equipment.OwnedCellName(i));
                Assert.That(cell, Is.Not.Null);
                Assert.That(cell.GetComponent<Button>(), Is.Not.Null);
                EquipmentMenuInteraction interaction =
                    cell.GetComponent<EquipmentMenuInteraction>();
                Assert.That(interaction, Is.Not.Null);
                Assert.That(interaction.IsOwnedItemSource, Is.True);
                Assert.That(interaction.Index, Is.EqualTo(i - 1));
                Assert.That(
                    cell.Find("Angled Surface")
                        .GetComponent<AngledPanelGraphic>().color.a,
                    Is.EqualTo(1f).Within(0.001f));
                Image icon = cell.Find(UiHierarchyPaths.Equipment.Icon)
                    .GetComponent<Image>();
                Assert.That(icon, Is.Not.Null);
                Assert.That(icon.preserveAspect, Is.True);
                Assert.That(icon.raycastTarget, Is.False);
            }

            menu.Open();
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(GameHudController.IsModalMenuOpen, Is.True);
            Assert.That(controller.RootCanvas.gameObject.activeSelf, Is.False);
            Assert.That(controller.CrosshairCanvas.gameObject.activeSelf, Is.False);
            menu.Close();
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(controller.RootCanvas.gameObject.activeSelf, Is.True);
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
                Is.EqualTo(1f).Within(0.001f),
                "The loading overlay should use an opaque dark-gray background.");
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
            Assert.That(
                ((RectTransform)panel.Find(
                    UiHierarchyPaths.Loading.LocalProgressTrack)).sizeDelta.y,
                Is.EqualTo(controller.DesignTokens != null
                    ? controller.DesignTokens.LoadingProgressThickness
                    : 6f).Within(0.001f));
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
        public void MainMenuPresentation_KeepsTransitionOverlayAlive()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller =
                hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();
            MethodInfo setMainMenuPresentation = typeof(GameHudController).GetMethod(
                "SetMainMenuPresentationActive",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(setMainMenuPresentation, Is.Not.Null);

            setMainMenuPresentation.Invoke(controller, new object[] { true });

            Assert.That(hudObject.activeSelf, Is.True);
            Assert.That(controller.RootCanvas.gameObject.activeSelf, Is.False);
            Assert.That(
                controller.MissionOverlayCanvas.gameObject.activeSelf,
                Is.True,
                "The persistent overlay must remain renderable during scene fades.");

            setMainMenuPresentation.Invoke(controller, new object[] { false });

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
        public void HotbarItemIcons_StayCenteredBelowSlotKeys()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller =
                hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();

            for (int i = 1; i <= PlayerInventory.SlotCount; i++)
            {
                RectTransform icon = hudObject.transform.Find(
                    UiHierarchyPaths.Hud.HotbarSlotIcon(i))
                    as RectTransform;
                Assert.That(icon, Is.Not.Null);
                Assert.That(
                    icon.anchoredPosition,
                    Is.EqualTo(new Vector2(14f, 0f)));
                Assert.That(
                    icon.sizeDelta,
                    Is.EqualTo(new Vector2(24f, 24f)));
            }
        }

        [Test]
        public void HotbarPresenter_InitializesEmptySlotsInsteadOfKeepingAuthoredGray()
        {
            hudObject = new GameObject("Hotbar");
            var backgrounds = new Image[PlayerInventory.SlotCount];
            var outlines = new Outline[PlayerInventory.SlotCount];
            var itemLabels = new TMP_Text[PlayerInventory.SlotCount];
            Color authoredGray = new Color(0.5f, 0.5f, 0.5f, 1f);

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                var slot = new GameObject(
                    $"Slot {i + 1}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Outline));
                slot.transform.SetParent(hudObject.transform, false);
                backgrounds[i] = slot.GetComponent<Image>();
                backgrounds[i].color = authoredGray;
                outlines[i] = slot.GetComponent<Outline>();
                outlines[i].effectColor = authoredGray;

                var label = new GameObject(
                    "Item",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI));
                label.transform.SetParent(slot.transform, false);
                itemLabels[i] = label.GetComponent<TMP_Text>();
                itemLabels[i].text = "STALE";
                itemLabels[i].color = authoredGray;
            }

            new HotbarPresenter(backgrounds, outlines, itemLabels);

            Color expectedLabelColor = new Color(0.96f, 0.98f, 1f, 1f);
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                Assert.That(backgrounds[i].color, Is.EqualTo(Color.clear));
                Assert.That(outlines[i].effectColor, Is.EqualTo(Color.clear));
                Assert.That(itemLabels[i].text, Is.Empty);
                Assert.That(itemLabels[i].color, Is.EqualTo(expectedLabelColor));
            }
        }

        [Test]
        public void HotbarRefresh_RestoresVisualsAndShowsSuspendedPickaxe()
        {
            sourceObject = new GameObject("Player");
            sourceObject.AddComponent<PlayerInventorySessionSettings>()
                .ConfigurePickaxeOnly();
            PlayerToolController inventory =
                sourceObject.AddComponent<PlayerToolController>();
            FieldInfo definitions = typeof(PlayerToolController).GetField(
                "toolDefinitions",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(definitions, Is.Not.Null);
            definitions.SetValue(
                inventory,
                new[]
                {
                    AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                        ProjectAssetPaths.Config.PickaxeTool),
                });
            hudObject = new GameObject("Game HUD");
            GameHudController controller =
                hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();
            controller.BindInventorySource(inventory);

            TMP_Text label = hudObject.transform.Find(
                    UiHierarchyPaths.Hud.HotbarSlot(1)
                    + "/" + UiHierarchyPaths.Hud.Item)
                .GetComponent<TMP_Text>();
            Image icon = hudObject.transform.Find(
                    UiHierarchyPaths.Hud.HotbarSlotIcon(1))
                .GetComponent<Image>();
            AngledPanelGraphic surface = hudObject.transform.Find(
                    UiHierarchyPaths.Hud.HotbarSlotAngledSurface(1))
                .GetComponent<AngledPanelGraphic>();
            Color expectedLabelColor = label.color;
            Color expectedIconColor = icon.color;
            Color expectedSurfaceColor = surface.color;

            label.text = string.Empty;
            label.color = Color.magenta;
            label.gameObject.SetActive(false);
            icon.color = Color.magenta;
            surface.SetFrontColor(Color.magenta);
            controller.RefreshNow();

            Assert.That(label.gameObject.activeSelf, Is.True);
            Assert.That(label.text, Is.EqualTo("探险镐"));
            Assert.That(
                hudObject.transform.Find(
                        UiHierarchyPaths.Hud.HotbarActionHintsLabel)
                    .GetComponent<TMP_Text>().text,
                Does.Contain("挥镐"));
            Assert.That(label.color, Is.EqualTo(expectedLabelColor));
            Assert.That(icon.color, Is.EqualTo(expectedIconColor));
            Assert.That(surface.color, Is.EqualTo(expectedSurfaceColor));

            Assert.That(
                inventory.SuspendItem(PlayerInventoryItem.Pickaxe),
                Is.True);
            controller.RefreshNow();

            Assert.That(
                inventory.GetItemAtSlot(0),
                Is.EqualTo(PlayerInventoryItem.Empty));
            Assert.That(
                inventory.GetDisplayItemAtSlot(0),
                Is.EqualTo(PlayerInventoryItem.Pickaxe));
            Assert.That(inventory.IsItemSuspendedAtSlot(0), Is.True);
            Assert.That(label.text, Is.EqualTo("探险镐\n已投掷"));
            Assert.That(icon.color, Is.Not.EqualTo(expectedIconColor));
            Assert.That(icon.color.r, Is.EqualTo(icon.color.g).Within(0.03f));
            Assert.That(icon.color.g, Is.EqualTo(icon.color.b).Within(0.03f));
            Assert.That(surface.color, Is.EqualTo(expectedSurfaceColor));

            Assert.That(
                inventory.RestoreSuspendedItem(PlayerInventoryItem.Pickaxe),
                Is.True);
            controller.RefreshNow();

            Assert.That(label.text, Is.EqualTo("探险镐"));
            Assert.That(label.color, Is.EqualTo(expectedLabelColor));
            Assert.That(icon.color, Is.EqualTo(expectedIconColor));
            Assert.That(surface.color, Is.EqualTo(expectedSurfaceColor));
        }

        [Test]
        public void HotbarCooldown_ShowsSlantedFillAndRoundedSeconds()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller =
                hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();

            FieldInfo presenterField = typeof(GameHudController).GetField(
                "hotbarPresenter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(presenterField, Is.Not.Null);
            HotbarPresenter hotbar =
                (HotbarPresenter)presenterField.GetValue(controller);
            hotbar.SetCooldown(0, 0.31f, 0.7f);

            HotbarCooldownOverlayGraphic overlay = hudObject.transform.Find(
                    UiHierarchyPaths.Hud.HotbarSlotCooldownOverlay(1))
                ?.GetComponent<HotbarCooldownOverlayGraphic>();
            TMP_Text label = hudObject.transform.Find(
                    UiHierarchyPaths.Hud.HotbarSlotCooldownLabel(1))
                ?.GetComponent<TMP_Text>();
            Assert.That(overlay, Is.Not.Null);
            Assert.That(label, Is.Not.Null);
            Assert.That(overlay.gameObject.activeSelf, Is.True);
            Assert.That(overlay.FillAmount, Is.EqualTo(0.31f / 0.7f).Within(0.001f));
            Assert.That(label.gameObject.activeSelf, Is.True);
            Assert.That(label.text, Is.EqualTo("0.4s"));

            hotbar.SetCooldown(0, 0f, 0.7f);

            Assert.That(overlay.gameObject.activeSelf, Is.False);
            Assert.That(label.gameObject.activeSelf, Is.False);
            Assert.That(label.text, Is.Empty);
        }

        [Test]
        public void PauseSettings_SwitchesPanelsWithoutExposingLoadoutConfiguration()
        {
            hudObject = new GameObject("Game HUD");
            GameHudController controller =
                hudObject.AddComponent<GameHudController>();
            controller.RebuildDefaultView();

            GameObject mainOptions = hudObject.transform.Find(
                UiHierarchyPaths.Pause.FullMainOptions).gameObject;
            GameObject settingsPanel = hudObject.transform.Find(
                UiHierarchyPaths.Pause.FullSettingsPanel).gameObject;
            Button settings = hudObject.transform.Find(
                UiHierarchyPaths.Pause.FullSettings).GetComponent<Button>();
            Button back = hudObject.transform.Find(
                UiHierarchyPaths.Pause.FullSettingsBack).GetComponent<Button>();

            Assert.That(mainOptions.activeSelf, Is.True);
            Assert.That(settingsPanel.activeSelf, Is.False);
            settings.onClick.Invoke();
            Assert.That(mainOptions.activeSelf, Is.False);
            Assert.That(settingsPanel.activeSelf, Is.True);
            Assert.That(
                hudObject.transform.Find(UiHierarchyPaths.Pause.FullFullscreen)
                    .GetComponent<Toggle>(),
                Is.Not.Null);
            Assert.That(
                hudObject.transform.Find(UiHierarchyPaths.Pause.FullMasterVolume)
                    .GetComponent<Slider>(),
                Is.Not.Null);

            back.onClick.Invoke();
            Assert.That(mainOptions.activeSelf, Is.True);
            Assert.That(settingsPanel.activeSelf, Is.False);
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
