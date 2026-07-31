using NUnit.Framework;
using Supernova.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
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
            Assert.That(
                prefab.transform.Find(
                    UiHierarchyPaths.MainMenu.ExpeditionFrame)?.GetComponent<Image>(),
                Is.Not.Null);

            MainMenuView view = prefab.GetComponent<MainMenuView>();
            Assert.That(view.PlayButton, Is.Not.Null);
            Assert.That(view.SettingsButton, Is.Not.Null);
            Assert.That(view.QuitButton, Is.Not.Null);
            Assert.That(view.SettingsBackButton, Is.Not.Null);
            Assert.That(view.FullscreenToggle, Is.Not.Null);
            Assert.That(view.VolumeSlider, Is.Not.Null);

            TMP_Text[] labels = prefab.GetComponentsInChildren<TMP_Text>(true);
            Assert.That(labels, Is.Not.Empty);
            foreach (TMP_Text label in labels)
                Assert.That(label.fontSize, Is.GreaterThanOrEqualTo(14f), label.name);
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
    }
}
