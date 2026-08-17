using System.Reflection;
using NUnit.Framework;
using Supernova.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Supernova.Tests
{
    public sealed class UiFoundationTests
    {
        private const string MainMenuPrefabPath =
            ProjectAssetPaths.Prefabs.MainMenu;

        [Test]
        public void SafeAreaCalculator_ConvertsPixelsToNormalizedAnchors()
        {
            UiSafeArea.CalculateAnchors(
                new Rect(100f, 50f, 1720f, 980f),
                new Vector2(1920f, 1080f),
                out Vector2 anchorMin,
                out Vector2 anchorMax);

            Assert.That(anchorMin.x, Is.EqualTo(100f / 1920f).Within(0.0001f));
            Assert.That(anchorMin.y, Is.EqualTo(50f / 1080f).Within(0.0001f));
            Assert.That(anchorMax.x, Is.EqualTo(1820f / 1920f).Within(0.0001f));
            Assert.That(anchorMax.y, Is.EqualTo(1030f / 1080f).Within(0.0001f));
        }

        [Test]
        public void CanvasPolicy_AppliesSharedScaleContract()
        {
            GameObject canvasObject = new GameObject(
                "Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(UiCanvasPolicy));
            UiDesignTokens tokens = ScriptableObject.CreateInstance<UiDesignTokens>();

            try
            {
                canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                UiCanvasPolicy policy = canvasObject.GetComponent<UiCanvasPolicy>();
                policy.SetDesignTokens(tokens);
                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();

                Assert.That(
                    scaler.uiScaleMode,
                    Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
                Assert.That(
                    scaler.screenMatchMode,
                    Is.EqualTo(CanvasScaler.ScreenMatchMode.MatchWidthOrHeight));
                Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1920f, 1080f)));
                Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(tokens);
                Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void MainMenuPrefab_UsesUguiThemeAndReadableTypeScale()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MainMenuPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(SciFiUiSkin.HasRequiredAssets, Is.True);
            Assert.That(prefab.GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<CanvasScaler>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<UiCanvasPolicy>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<MainMenuView>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<SciFiUiStyler>(), Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<UiSafeArea>(true), Is.Not.Null);

            MainMenuView view = prefab.GetComponent<MainMenuView>();
            Assert.That(view.PlayButton, Is.Not.Null);
            Assert.That(view.ContinueButton, Is.Not.Null);
            Assert.That(view.ContinueSaveSummaryLabel, Is.Not.Null);
            Assert.That(view.TutorialButton, Is.Not.Null);
            Assert.That(view.SettingsButton, Is.Not.Null);
            Assert.That(view.QuitButton, Is.Not.Null);
            Assert.That(view.SettingsBackButton, Is.Not.Null);
            Assert.That(view.FullscreenToggle, Is.Not.Null);
            Assert.That(view.VolumeSlider, Is.Not.Null);
            Assert.That(view.OverwriteConfirmationPanel, Is.Not.Null);
            Assert.That(view.OverwriteConfirmButton, Is.Not.Null);
            Assert.That(view.OverwriteCancelButton, Is.Not.Null);
            Assert.That(view.CharacterOverlay, Is.Not.Null);

            TMP_Text[] labels = prefab.GetComponentsInChildren<TMP_Text>(true);
            Assert.That(labels, Is.Not.Empty);
            foreach (TMP_Text label in labels)
                Assert.That(label.fontSize, Is.GreaterThanOrEqualTo(14f), label.name);

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            try
            {
                Assert.That(instance, Is.Not.Null);
                MainMenuView runtimeView = instance.GetComponent<MainMenuView>();
                runtimeView.SetContinueGameVisible(false);
                Assert.That(runtimeView.ContinueButton, Is.Not.Null);
                Assert.That(
                    runtimeView.ContinueButton.gameObject.activeSelf,
                    Is.False);
                RectTransform newGameRect =
                    runtimeView.PlayButton.transform as RectTransform;
                Assert.That(
                    newGameRect.anchorMin.x,
                    Is.EqualTo(0f).Within(0.0001f));
                Assert.That(
                    newGameRect.anchorMax.x,
                    Is.EqualTo(1f).Within(0.0001f));
                AngledPanelGraphic newGameSurface =
                    runtimeView.PlayButton.GetComponentInChildren<
                        AngledPanelGraphic>(true);
                Assert.That(newGameSurface, Is.Not.Null);
                Assert.That(newGameSurface.color.r, Is.GreaterThan(0.8f));
                Assert.That(newGameSurface.color.g, Is.GreaterThan(0.8f));
                Assert.That(newGameSurface.color.b, Is.GreaterThan(0.8f));
                runtimeView.SetContinueGameVisible(true);
                Assert.That(
                    runtimeView.ContinueButton.gameObject.activeSelf,
                    Is.True);
                RectTransform continueRect =
                    runtimeView.ContinueButton.transform as RectTransform;
                Assert.That(
                    continueRect.anchorMin.x,
                    Is.EqualTo(0f).Within(0.0001f));
                Assert.That(
                    continueRect.anchorMax.x,
                    Is.EqualTo(MainMenuView.ContinueGameWidthFraction)
                        .Within(0.0001f));
                Assert.That(
                    newGameRect.anchorMin.x,
                    Is.EqualTo(MainMenuView.ContinueGameWidthFraction)
                        .Within(0.0001f));
                Assert.That(
                    newGameRect.anchorMax.x,
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(
                    continueRect.anchorMin.y,
                    Is.EqualTo(newGameRect.anchorMin.y).Within(0.0001f));
                Assert.That(
                    continueRect.anchorMax.y,
                    Is.EqualTo(newGameRect.anchorMax.y).Within(0.0001f));
                runtimeView.SetContinueGameSummary(275, 3);
                Assert.That(
                    runtimeView.ContinueSaveSummaryLabel.text,
                    Is.EqualTo("存款：$275\n第3关"));
                Assert.That(
                    runtimeView.ShowOverwriteConfirmation(),
                    Is.True);
                Assert.That(
                    runtimeView.OverwriteConfirmationPanel.activeSelf,
                    Is.True);
                runtimeView.HideOverwriteConfirmation();
                Assert.That(
                    runtimeView.OverwriteConfirmationPanel.activeSelf,
                    Is.False);
                Image backdrop = instance.transform.Find(
                    UiHierarchyPaths.MainMenu.Backdrop)?.GetComponent<Image>();
                Assert.That(backdrop, Is.Not.Null);

                Color configuredGraphicColor =
                    new Color(0.18f, 0.24f, 0.31f, 0.47f);
                Graphic[] configuredGraphics =
                    instance.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < configuredGraphics.Length; i++)
                    configuredGraphics[i].color = configuredGraphicColor;

                ColorBlock configuredButtonColors = ColorBlock.defaultColorBlock;
                configuredButtonColors.normalColor =
                    new Color(0.11f, 0.21f, 0.31f, 0.41f);
                configuredButtonColors.highlightedColor =
                    new Color(0.12f, 0.22f, 0.32f, 0.42f);
                configuredButtonColors.selectedColor =
                    new Color(0.13f, 0.23f, 0.33f, 0.43f);
                configuredButtonColors.pressedColor =
                    new Color(0.14f, 0.24f, 0.34f, 0.44f);
                configuredButtonColors.disabledColor =
                    new Color(0.15f, 0.25f, 0.35f, 0.45f);
                Button[] configuredButtons =
                    instance.GetComponentsInChildren<Button>(true);
                for (int i = 0; i < configuredButtons.Length; i++)
                    configuredButtons[i].colors = configuredButtonColors;

                SciFiUiStyler runtimeStyler =
                    instance.GetComponent<SciFiUiStyler>();
                Assert.That(runtimeStyler, Is.Not.Null);
                runtimeStyler.enabled = false;
                runtimeStyler.enabled = true;
                CanvasGroup group = runtimeView.PrepareHomePresentation();
                Assert.That(group, Is.Not.Null);
                for (int i = 0; i < configuredGraphics.Length; i++)
                {
                    Assert.That(
                        configuredGraphics[i].color,
                        Is.EqualTo(configuredGraphicColor),
                        configuredGraphics[i].name
                        + " must preserve its EditMode color.");
                }
                for (int i = 0; i < configuredButtons.Length; i++)
                {
                    AssertColorBlock(
                        configuredButtons[i].colors,
                        configuredButtonColors,
                        configuredButtons[i].name);
                }
                Assert.That(
                    instance.transform.Find(UiHierarchyPaths.MainMenu.Overline),
                    Is.Null,
                    "The redundant MAIN MENU // HOME BASE overline must stay removed.");
                MainMenuCharacterOverlay overlay = runtimeView.CharacterOverlay;
                Assert.That(overlay.OverlayImage.raycastTarget, Is.False);
                Assert.That(
                    overlay.GetComponent<CanvasGroup>().ignoreParentGroups,
                    Is.True,
                    "The character composite must remain above the fading menu group.");
                Assert.That(
                    instance.transform.Find("Safe Area/Header/Brand")
                        ?.GetComponent<TMP_Text>().text,
                    Is.Empty);
                Assert.That(
                    instance.transform.Find("Safe Area/Header/Build")
                        ?.GetComponent<TMP_Text>().text,
                    Is.Empty);
                Image themeTitle = instance.transform.Find(
                    UiHierarchyPaths.MainMenu.Title)?.GetComponent<Image>();
                Assert.That(themeTitle, Is.Not.Null);
                Assert.That(themeTitle.sprite, Is.Not.Null);
                Assert.That(themeTitle.preserveAspect, Is.True);
                Assert.That(themeTitle.raycastTarget, Is.False);
                Assert.That(themeTitle.GetComponent<TMP_Text>(), Is.Null);
                Assert.That(
                    themeTitle.GetComponentInParent<LayoutGroup>(),
                    Is.Null,
                    "The title RectTransform must remain freely editable in EditMode.");
                Transform characterOverlay =
                    runtimeView.CharacterOverlay.transform;
                Transform titleCanvasBranch = themeTitle.transform.parent;
                Assert.That(
                    characterOverlay.GetSiblingIndex(),
                    Is.GreaterThan(titleCanvasBranch.GetSiblingIndex()),
                    "The player composite must render after and obscure the title.");
                AssertMenuButtonPresentation(
                    instance.transform,
                    UiHierarchyPaths.MainMenu.ContinueGame,
                    "    继续游戏",
                    46f,
                    26f);
                AssertMenuButtonPresentation(
                    instance.transform,
                    UiHierarchyPaths.MainMenu.NewGame,
                    "    新游戏",
                    46f,
                    26f);
                AssertMenuButtonPresentation(
                    instance.transform,
                    UiHierarchyPaths.MainMenu.Tutorial,
                    "    新手教程",
                    26f);
                AssertMenuButtonPresentation(
                    instance.transform,
                    UiHierarchyPaths.MainMenu.SystemSettings,
                    "    设置",
                    6f);
                AssertMenuButtonPresentation(
                    instance.transform,
                    UiHierarchyPaths.MainMenu.LeaveExpedition,
                    "    退出游戏",
                    -14f);
                Assert.That(
                    instance.transform.Find("Safe Area/Footer/Controls")
                        ?.GetComponent<TMP_Text>().text,
                    Is.EqualTo("版本号: v1.0.0"));
                Assert.That(
                    instance.transform.Find("Safe Area/Footer/Signal")
                        ?.GetComponent<TMP_Text>().text,
                    Is.Empty);
                Transform hero = instance.transform.Find(
                    UiHierarchyPaths.MainMenu.Hero);
                Assert.That(
                    hero == null || !hero.gameObject.activeSelf,
                    Is.True,
                    "Home supplies the visible player instead of the old hero panel.");
                Transform frame = instance.transform.Find(
                    UiHierarchyPaths.MainMenu.ExpeditionFrame);
                Assert.That(
                    frame == null || !frame.gameObject.activeSelf,
                    Is.True,
                    "Decorative frame textures must be disabled in the Home menu.");
                Image card = instance.transform.Find(
                    UiHierarchyPaths.MainMenu.ExpeditionControl)?.GetComponent<Image>();
                Assert.That(card, Is.Not.Null);
                Assert.That(card.sprite, Is.Null);
                Assert.That(
                    instance.transform.Find(
                        UiHierarchyPaths.MainMenu.BeginDescent
                        + "/"
                        + UiHierarchyPaths.MainMenu.AngledSurface)
                        ?.GetComponent<AngledPanelGraphic>(),
                    Is.Not.Null,
                    "Menu buttons must use the gameplay HUD's procedural geometry.");
                Assert.That(
                    instance.transform.Find(
                        UiHierarchyPaths.MainMenu.Tutorial
                        + "/"
                        + UiHierarchyPaths.MainMenu.AngledSurface)
                        ?.GetComponent<AngledPanelGraphic>(),
                    Is.Not.Null,
                    "The tutorial row must use the gameplay HUD's procedural geometry.");
            }
            finally
            {
                if (instance != null)
                    Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void HomeScene_ContainsEditableMainMenuAndSceneReferences()
        {
            Scene homeScene = SceneManager.GetSceneByPath(ProjectAssetPaths.Scenes.Home);
            bool closeAfterTest = !homeScene.IsValid() || !homeScene.isLoaded;
            if (closeAfterTest)
            {
                homeScene = EditorSceneManager.OpenScene(
                    ProjectAssetPaths.Scenes.Home,
                    OpenSceneMode.Additive);
            }

            try
            {
                GameObject menuRoot = null;
                GameObject[] roots = homeScene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i].name == UiHierarchyPaths.MainMenu.SceneRoot)
                    {
                        menuRoot = roots[i];
                        break;
                    }
                }

                Assert.That(menuRoot, Is.Not.Null);
                MainMenuController controller =
                    menuRoot.GetComponent<MainMenuController>();
                Assert.That(controller, Is.Not.Null);
                Assert.That(
                    menuRoot.GetComponentInChildren<MainMenuView>(true),
                    Is.Not.Null);

                SerializedObject serializedController =
                    new SerializedObject(controller);
                Assert.That(
                    serializedController.FindProperty("uguiView").objectReferenceValue,
                    Is.Not.Null);
                Assert.That(
                    serializedController.FindProperty("perspectiveCamera")
                        .objectReferenceValue,
                    Is.Not.Null);
                Assert.That(
                    serializedController.FindProperty("menuCharacterAnimator")
                        .objectReferenceValue,
                    Is.Not.Null);
                Assert.That(
                    serializedController.FindProperty("playerToolController")
                        .objectReferenceValue,
                    Is.Not.Null);
                Assert.That(
                    serializedController.FindProperty("menuIdleAnimation"),
                    Is.Not.Null);
                Assert.That(
                    serializedController.FindProperty("menuFieldOfView").floatValue,
                    Is.InRange(15f, 120f));
            }
            finally
            {
                if (closeAfterTest)
                    EditorSceneManager.CloseScene(homeScene, true);
            }
        }

        [Test]
        public void PausePortrait_UsesConfiguredLayerAndSupportedShader()
        {
            int portraitLayer = LayerMask.NameToLayer(UiLayerNames.PausePortrait);
            Material bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                ProjectAssetPaths.Ui.PauseBodyMaterial);

            Assert.That(portraitLayer, Is.GreaterThanOrEqualTo(0));
            Assert.That(bodyMaterial, Is.Not.Null);
            Assert.That(bodyMaterial.shader, Is.Not.Null);
            Assert.That(bodyMaterial.shader.isSupported, Is.True);
            Assert.That(ShaderUtil.GetShaderMessages(bodyMaterial.shader), Is.Empty);
        }

        [TestCase(0.54f, 0.55f, 1.65f, 0.2f, false, false)]
        [TestCase(0.55f, 0.55f, 1.65f, 0.2f, true, false)]
        [TestCase(1.44f, 0.55f, 1.65f, 0.2f, true, false)]
        [TestCase(1.46f, 0.55f, 1.65f, 0.2f, true, true)]
        [TestCase(1.5f, 1.6f, 1.65f, 0.2f, false, false)]
        public void MainMenuTransition_ReleasesCharacterAfterUiAndEntersFirstPersonNearEnd(
            float elapsed,
            float fadeDuration,
            float cameraDuration,
            float firstPersonLeadDuration,
            bool expectedUiHidden,
            bool expectedFirstPerson)
        {
            MethodInfo hasMenuUiFinishedFading =
                typeof(MainMenuController).GetMethod(
                    "HasMenuUiFinishedFading",
                    BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo shouldActivateFirstPerson =
                typeof(MainMenuController).GetMethod(
                    "ShouldActivateFirstPerson",
                    BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(hasMenuUiFinishedFading, Is.Not.Null);
            Assert.That(shouldActivateFirstPerson, Is.Not.Null);
            Assert.That(
                hasMenuUiFinishedFading.Invoke(
                    null,
                    new object[] { elapsed, fadeDuration }),
                Is.EqualTo(expectedUiHidden));
            Assert.That(
                shouldActivateFirstPerson.Invoke(
                    null,
                    new object[]
                    {
                        elapsed,
                        cameraDuration,
                        fadeDuration,
                        firstPersonLeadDuration,
                    }),
                Is.EqualTo(expectedFirstPerson));
        }

        private static void AssertColorBlock(
            ColorBlock actual,
            ColorBlock expected,
            string objectName)
        {
            Assert.That(actual.normalColor, Is.EqualTo(expected.normalColor), objectName);
            Assert.That(
                actual.highlightedColor,
                Is.EqualTo(expected.highlightedColor),
                objectName);
            Assert.That(
                actual.selectedColor,
                Is.EqualTo(expected.selectedColor),
                objectName);
            Assert.That(actual.pressedColor, Is.EqualTo(expected.pressedColor), objectName);
            Assert.That(
                actual.disabledColor,
                Is.EqualTo(expected.disabledColor),
                objectName);
        }

        private static void AssertMenuButtonPresentation(
            Transform root,
            string path,
            string expectedText,
            float expectedY,
            float expectedFontSize = 30f)
        {
            RectTransform button = root.Find(path) as RectTransform;
            TMP_Text label = button != null
                ? button.Find(UiHierarchyPaths.Pause.Label)?.GetComponent<TMP_Text>()
                : null;

            Assert.That(button, Is.Not.Null, path);
            Assert.That(label, Is.Not.Null, path + " label");
            Assert.That(label.text, Is.EqualTo(expectedText), path);
            Assert.That(
                label.fontSize,
                Is.EqualTo(expectedFontSize),
                path);
            Assert.That(button.anchoredPosition.y, Is.EqualTo(expectedY), path);
        }
    }
}
