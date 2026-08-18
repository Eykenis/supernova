using NUnit.Framework;
using Supernova.Infrastructure;
using Supernova.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Supernova.Tests
{
    public sealed class NewGameGuideOverlayTests
    {
        [Test]
        public void GuideContent_UsesTheRequestedOrderedCaptions()
        {
            Assert.That(NewGameGuideOverlay.GuidePageCount, Is.EqualTo(4));
            Assert.That(
                NewGameGuideOverlay.GetCaption(0),
                Is.EqualTo("使用镐子开采矿物"));
            Assert.That(
                NewGameGuideOverlay.GetCaption(1),
                Is.EqualTo("或寻找宝藏"));
            Assert.That(
                NewGameGuideOverlay.GetCaption(2),
                Is.EqualTo("右键牵引已开采的矿物或宝藏"));
            Assert.That(
                NewGameGuideOverlay.GetCaption(3),
                Is.EqualTo("将货物安全运回传送门以得分"));
        }

        [Test]
        public void GuideImages_ArePreloadedInGlobalCatalogOrder()
        {
            GameAssetCatalog catalog = GameAssetCatalogBuilder.EnsureCatalog();

            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.UI.NewGameGuideImages, Has.Length.EqualTo(4));
            Assert.That(
                AssetDatabase.GetAssetPath(catalog.UI.NewGameGuideImages[0]),
                Is.EqualTo(ProjectAssetPaths.Textures.NewGameGuideMineOre));
            Assert.That(
                AssetDatabase.GetAssetPath(catalog.UI.NewGameGuideImages[1]),
                Is.EqualTo(ProjectAssetPaths.Textures.NewGameGuideFindTreasure));
            Assert.That(
                AssetDatabase.GetAssetPath(catalog.UI.NewGameGuideImages[2]),
                Is.EqualTo(ProjectAssetPaths.Textures.NewGameGuidePullCargo));
            Assert.That(
                AssetDatabase.GetAssetPath(catalog.UI.NewGameGuideImages[3]),
                Is.EqualTo(ProjectAssetPaths.Textures.NewGameGuideDeliverCargo));
        }

        [Test]
        public void GuideNavigation_AdvancesAndOffersSkip()
        {
            var root = new GameObject("New Game Guide Test");
            var textures = new Texture2D[NewGameGuideOverlay.GuidePageCount];
            try
            {
                for (int i = 0; i < textures.Length; i++)
                    textures[i] = new Texture2D(16, 9);
                NewGameGuideOverlay guide =
                    root.AddComponent<NewGameGuideOverlay>();

                Assert.That(guide.Open(null, textures), Is.True);
                Assert.That(guide.IsVisible, Is.True);
                Assert.That(GameHudController.IsGameplayInputBlocked, Is.True);
                Assert.That(guide.CurrentPageIndex, Is.Zero);
                Assert.That(
                    guide.CaptionLabel.text,
                    Is.EqualTo(NewGameGuideOverlay.GetCaption(0)));
                Assert.That(
                    root.transform.Find(UiHierarchyPaths.NewGameGuide.Image)
                        ?.GetComponent<RawImage>(),
                    Is.SameAs(guide.GuideImage));
                Assert.That(
                    root.transform.Find(UiHierarchyPaths.NewGameGuide.Caption)
                        ?.GetComponent<TMP_Text>(),
                    Is.SameAs(guide.CaptionLabel));
                Image backdrop = root.transform
                    .Find(UiHierarchyPaths.NewGameGuide.Backdrop)
                    ?.GetComponent<Image>();
                RectTransform panel = root.transform
                    .Find(UiHierarchyPaths.NewGameGuide.Panel)
                    as RectTransform;
                RectTransform imageRect = root.transform
                    .Find(UiHierarchyPaths.NewGameGuide.Image)
                    as RectTransform;
                RectTransform captionRect = root.transform
                    .Find(UiHierarchyPaths.NewGameGuide.Caption)
                    as RectTransform;
                Assert.That(backdrop, Is.Not.Null);
                Assert.That(
                    backdrop.color.a,
                    Is.EqualTo(NewGameGuideOverlay.BackdropOpacity).Within(0.001f));
                Assert.That(panel, Is.Not.Null);
                Assert.That(
                    panel.GetComponent<AngledPanelGraphic>(),
                    Is.Not.Null);
                Assert.That(panel.GetComponent<Image>(), Is.Null);
                Assert.That(
                    panel.sizeDelta.x,
                    Is.EqualTo(NewGameGuideOverlay.GuidePanelWidth));
                Assert.That(
                    panel.sizeDelta.y,
                    Is.EqualTo(NewGameGuideOverlay.GuidePanelHeight));
                Assert.That(imageRect, Is.Not.Null);
                Assert.That(captionRect, Is.Not.Null);
                float imageBottom = imageRect.anchoredPosition.y
                    - imageRect.rect.height * imageRect.pivot.y;
                float captionTop = captionRect.anchoredPosition.y
                    + captionRect.rect.height * (1f - captionRect.pivot.y);
                Assert.That(
                    imageBottom - captionTop,
                    Is.GreaterThanOrEqualTo(
                        NewGameGuideOverlay.MinimumImageCaptionGap));
                Assert.That(
                    guide.NextButton.targetGraphic,
                    Is.TypeOf<AngledPanelGraphic>());
                Assert.That(
                    guide.SkipButton.targetGraphic,
                    Is.TypeOf<AngledPanelGraphic>());
                Assert.That(
                    guide.NextButton.GetComponent<Image>(),
                    Is.Null);
                Assert.That(
                    guide.SkipButton.GetComponent<Image>(),
                    Is.Null);

                guide.NextButton.onClick.Invoke();
                Assert.That(guide.CurrentPageIndex, Is.EqualTo(1));
                Assert.That(
                    guide.CaptionLabel.text,
                    Is.EqualTo(NewGameGuideOverlay.GetCaption(1)));

                guide.NextButton.onClick.Invoke();
                guide.NextButton.onClick.Invoke();
                Assert.That(guide.CurrentPageIndex, Is.EqualTo(3));
                Assert.That(
                    guide.NextButton.GetComponentInChildren<TMP_Text>().text,
                    Is.EqualTo("开始任务"));

                guide.NextButton.onClick.Invoke();
                Assert.That(guide.IsVisible, Is.False);

                Assert.That(guide.Open(null, textures), Is.True);
                guide.SkipButton.onClick.Invoke();
                Assert.That(guide.IsVisible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                for (int i = 0; i < textures.Length; i++)
                {
                    if (textures[i] != null)
                        Object.DestroyImmediate(textures[i]);
                }
            }
        }

        [Test]
        public void PendingMarker_IsCreatedForEachNewCampaignAndClearedAfterGuide()
        {
            bool wasPending =
                NewGameGuideOverlay.IsPendingForCurrentCampaign;
            try
            {
                NewGameGuideOverlay.MarkShownForCurrentCampaign();
                Assert.That(
                    NewGameGuideOverlay.IsPendingForCurrentCampaign,
                    Is.False);

                NewGameGuideOverlay.MarkForNewCampaign();
                Assert.That(
                    NewGameGuideOverlay.IsPendingForCurrentCampaign,
                    Is.True);

                NewGameGuideOverlay.MarkShownForCurrentCampaign();
                Assert.That(
                    NewGameGuideOverlay.IsPendingForCurrentCampaign,
                    Is.False);
            }
            finally
            {
                if (wasPending)
                    NewGameGuideOverlay.MarkForNewCampaign();
                else
                    NewGameGuideOverlay.MarkShownForCurrentCampaign();
            }
        }
    }
}
