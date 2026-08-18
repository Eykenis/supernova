using System;
using Supernova.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Pauses a newly-created campaign and presents the four-page mission guide.
    /// The view is generated beneath the persistent HUD so it survives the menu
    /// to mission scene transition without adding scene-specific references.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NewGameGuideOverlay : MonoBehaviour
    {
        private const string PendingPreferenceKey =
            "Supernova.NewGameGuide.Pending";
        public const float BackdropOpacity = 0.24f;
        public const float GuidePanelWidth = 940f;
        public const float GuidePanelHeight = 700f;
        public const float MinimumImageCaptionGap = 20f;
        private const float GuideImageMaximumWidth = 800f;
        private const float GuideImageMaximumHeight = 450f;
        private static readonly string[] Captions =
        {
            "使用镐子开采矿物",
            "或寻找宝藏",
            "右键牵引已开采的矿物或宝藏",
            "将货物安全运回传送门以得分"
        };

        private static NewGameGuideOverlay activeGuide;

        private GameHudController owner;
        private Texture2D[] images;
        private GameObject canvasRoot;
        private RawImage guideImage;
        private TMP_Text pageIndicatorLabel;
        private TMP_Text captionLabel;
        private TMP_Text nextButtonLabel;
        private Button nextButton;
        private Button skipButton;
        private bool isOpen;
        private bool controlsCaptured;
        private bool marksCampaignProgressOnClose;
        private float timeScaleBeforeOpen = 1f;
        private CursorLockMode cursorLockBeforeOpen;
        private bool cursorVisibleBeforeOpen;
        private int currentPageIndex;

        public static bool IsOpen => activeGuide != null && activeGuide.isOpen;
        public static int GuidePageCount => Captions.Length;
        public static bool IsPendingForCurrentCampaign =>
            PlayerPrefs.GetInt(PendingPreferenceKey, 0) != 0;
        public bool IsVisible => isOpen;
        public int CurrentPageIndex => currentPageIndex;
        public RawImage GuideImage => guideImage;
        public TMP_Text CaptionLabel => captionLabel;
        public TMP_Text PageIndicatorLabel => pageIndicatorLabel;
        public Button NextButton => nextButton;
        public Button SkipButton => skipButton;

        public static string GetCaption(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= Captions.Length)
                throw new ArgumentOutOfRangeException(nameof(pageIndex));
            return Captions[pageIndex];
        }

        public static void MarkForNewCampaign()
        {
            PlayerPrefs.SetInt(PendingPreferenceKey, 1);
            PlayerPrefs.Save();
        }

        public static void MarkShownForCurrentCampaign()
        {
            PlayerPrefs.DeleteKey(PendingPreferenceKey);
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            activeGuide = null;
        }

        public static bool TryShow(GameHudController configuredOwner)
        {
            UiAssetReferences uiAssets = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI
                : null;
            Texture2D[] configuredImages = uiAssets != null
                ? uiAssets.NewGameGuideImages
                : null;
            if (!HasCompleteImageSet(configuredImages))
            {
                Debug.LogError(
                    "The preloaded game asset catalog does not contain the four "
                    + "new-game guide images.",
                    configuredOwner);
                return false;
            }

            if (configuredOwner == null)
                return false;

            NewGameGuideOverlay guide =
                configuredOwner.GetComponent<NewGameGuideOverlay>();
            if (guide == null)
                guide = configuredOwner.gameObject.AddComponent<NewGameGuideOverlay>();
            bool opened = guide.Open(configuredOwner, configuredImages);
            if (opened)
                guide.marksCampaignProgressOnClose = true;
            return opened;
        }

        public bool Open(
            GameHudController configuredOwner,
            Texture2D[] configuredImages)
        {
            if (!HasCompleteImageSet(configuredImages))
                return false;

            if (activeGuide != null && activeGuide != this)
                activeGuide.Close();

            owner = configuredOwner;
            marksCampaignProgressOnClose = false;
            images = new Texture2D[Captions.Length];
            Array.Copy(configuredImages, images, Captions.Length);
            EnsureView();
            if (canvasRoot == null)
                return false;

            currentPageIndex = 0;
            isOpen = true;
            activeGuide = this;
            canvasRoot.SetActive(true);
            owner?.SetGameplayHudVisibleForModal(false);
            CaptureGameplayControls();
            RefreshPage();

            EventSystem eventSystem =
                GameHudController.EnsureSingleEventSystem(transform);
            if (eventSystem != null && nextButton != null)
                eventSystem.SetSelectedGameObject(nextButton.gameObject);
            Canvas.ForceUpdateCanvases();
            return true;
        }

        public void Advance()
        {
            if (!isOpen)
                return;

            if (currentPageIndex < Captions.Length - 1)
            {
                currentPageIndex++;
                RefreshPage();
                return;
            }

            Close();
        }

        public void Skip()
        {
            Close();
        }

        public void Close()
        {
            if (!isOpen)
                return;

            isOpen = false;
            if (activeGuide == this)
                activeGuide = null;
            if (canvasRoot != null)
                canvasRoot.SetActive(false);

            RestoreGameplayControls();
            owner?.SetGameplayHudVisibleForModal(true);
            GameHudController.BlockGameplayInputAfterModalClose();
            if (marksCampaignProgressOnClose)
            {
                marksCampaignProgressOnClose = false;
                MarkShownForCurrentCampaign();
            }
        }

        private static bool HasCompleteImageSet(Texture2D[] configuredImages)
        {
            if (configuredImages == null
                || configuredImages.Length != Captions.Length)
            {
                return false;
            }

            for (int i = 0; i < configuredImages.Length; i++)
            {
                if (configuredImages[i] == null)
                    return false;
            }
            return true;
        }

        private void CaptureGameplayControls()
        {
            if (!Application.isPlaying || controlsCaptured)
                return;

            controlsCaptured = true;
            timeScaleBeforeOpen = Time.timeScale;
            cursorLockBeforeOpen = Cursor.lockState;
            cursorVisibleBeforeOpen = Cursor.visible;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void RestoreGameplayControls()
        {
            if (!controlsCaptured)
                return;

            controlsCaptured = false;
            Time.timeScale = timeScaleBeforeOpen;
            Cursor.lockState = cursorLockBeforeOpen;
            Cursor.visible = cursorVisibleBeforeOpen;
        }

        private void EnsureView()
        {
            if (canvasRoot != null)
                return;

            RectTransform canvasRect = CreateRect(
                UiHierarchyPaths.NewGameGuide.CanvasName,
                transform);
            canvasRoot = canvasRect.gameObject;
            Canvas canvas = canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = owner != null && owner.DesignTokens != null
                ? owner.DesignTokens.PauseSortingOrder + 50
                : 350;
            CanvasScaler scaler = canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasRoot.AddComponent<GraphicRaycaster>();

            RectTransform backdrop = CreateRect(
                UiHierarchyPaths.NewGameGuide.BackdropName,
                canvasRect);
            Stretch(backdrop);
            Image backdropImage = backdrop.gameObject.AddComponent<Image>();
            backdropImage.color = ResolveBackdropColor();
            backdropImage.raycastTarget = true;

            RectTransform panel = CreateRect(
                UiHierarchyPaths.NewGameGuide.PanelName,
                canvasRect);
            SetRect(
                panel,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(GuidePanelWidth, GuidePanelHeight));
            AngledPanelGraphic panelSurface =
                panel.gameObject.AddComponent<AngledPanelGraphic>();
            panelSurface.Configure(
                ResolveHudSlant() * 1.5f,
                ResolveHudDepth(),
                ResolveSurfaceColor(),
                ResolveShadowColor(),
                ResolvePanelHighlightColor());
            panelSurface.raycastTarget = true;

            TMP_Text header = CreateText(
                UiHierarchyPaths.NewGameGuide.HeaderName,
                panel,
                "任务指南",
                TextAlignmentOptions.Left);
            SetRect(
                (RectTransform)header.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-270f, 307f),
                new Vector2(260f, 42f));
            header.fontSize = 18f;
            header.characterSpacing = 6f;
            header.color = ResolveTextColor();

            pageIndicatorLabel = CreateText(
                UiHierarchyPaths.NewGameGuide.PageIndicatorName,
                panel,
                string.Empty,
                TextAlignmentOptions.Right);
            SetRect(
                (RectTransform)pageIndicatorLabel.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(280f, 307f),
                new Vector2(240f, 42f));
            pageIndicatorLabel.fontSize = 14f;
            pageIndicatorLabel.characterSpacing = 4f;
            pageIndicatorLabel.color = ResolveMutedColor();

            RectTransform divider = CreateRect("Divider", panel);
            SetRect(
                divider,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 278f),
                new Vector2(800f, 2f));
            Image dividerImage = divider.gameObject.AddComponent<Image>();
            dividerImage.color = ResolvePanelHighlightColor();
            dividerImage.raycastTarget = false;

            RectTransform imageRect = CreateRect(
                UiHierarchyPaths.NewGameGuide.ImageName,
                panel);
            SetRect(
                imageRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 29f),
                new Vector2(
                    GuideImageMaximumWidth,
                    GuideImageMaximumHeight));
            guideImage = imageRect.gameObject.AddComponent<RawImage>();
            guideImage.color = Color.white;
            guideImage.raycastTarget = false;
            Outline imageOutline = imageRect.gameObject.AddComponent<Outline>();
            imageOutline.effectColor = ResolvePanelHighlightColor();
            imageOutline.effectDistance = new Vector2(1f, -1f);

            captionLabel = CreateText(
                UiHierarchyPaths.NewGameGuide.CaptionName,
                panel,
                string.Empty,
                TextAlignmentOptions.Center);
            SetRect(
                (RectTransform)captionLabel.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -237f),
                new Vector2(800f, 42f));
            captionLabel.fontSize = 25f;
            captionLabel.enableAutoSizing = true;
            captionLabel.fontSizeMin = 18f;
            captionLabel.fontSizeMax = 25f;
            captionLabel.enableWordWrapping = true;
            captionLabel.color = ResolveTextColor();

            skipButton = CreateButton(
                UiHierarchyPaths.NewGameGuide.SkipButtonName,
                panel,
                "跳过引导",
                new Vector2(-290f, -306f),
                new Vector2(220f, 48f),
                false,
                out _);
            skipButton.onClick.AddListener(Skip);

            nextButton = CreateButton(
                UiHierarchyPaths.NewGameGuide.NextButtonName,
                panel,
                "下一步",
                new Vector2(290f, -306f),
                new Vector2(220f, 48f),
                true,
                out nextButtonLabel);
            nextButton.onClick.AddListener(Advance);

            Navigation skipNavigation = skipButton.navigation;
            skipNavigation.mode = Navigation.Mode.Explicit;
            skipNavigation.selectOnRight = nextButton;
            skipButton.navigation = skipNavigation;
            Navigation nextNavigation = nextButton.navigation;
            nextNavigation.mode = Navigation.Mode.Explicit;
            nextNavigation.selectOnLeft = skipButton;
            nextButton.navigation = nextNavigation;
            canvasRoot.SetActive(false);
        }

        private void RefreshPage()
        {
            if (!isOpen || images == null)
                return;

            Texture2D texture = images[currentPageIndex];
            guideImage.texture = texture;
            ResizeGuideImage(texture);
            captionLabel.text = Captions[currentPageIndex];
            pageIndicatorLabel.text =
                (currentPageIndex + 1).ToString("00")
                + "  /  " + Captions.Length.ToString("00");
            nextButtonLabel.text = currentPageIndex == Captions.Length - 1
                ? "开始任务"
                : "下一步";
            if (EventSystem.current != null && nextButton != null)
                EventSystem.current.SetSelectedGameObject(nextButton.gameObject);
        }

        private void ResizeGuideImage(Texture2D texture)
        {
            RectTransform imageRect = guideImage != null
                ? guideImage.transform as RectTransform
                : null;
            if (imageRect == null)
                return;

            Vector2 size = new Vector2(
                GuideImageMaximumWidth,
                GuideImageMaximumHeight);
            if (texture != null && texture.width > 0 && texture.height > 0)
            {
                float imageAspect = (float)texture.width / texture.height;
                float boundsAspect =
                    GuideImageMaximumWidth / GuideImageMaximumHeight;
                if (imageAspect > boundsAspect)
                    size.y = GuideImageMaximumWidth / imageAspect;
                else
                    size.x = GuideImageMaximumHeight * imageAspect;
            }
            imageRect.sizeDelta = size;
        }

        private Button CreateButton(
            string objectName,
            RectTransform parent,
            string label,
            Vector2 position,
            Vector2 size,
            bool primary,
            out TMP_Text labelText)
        {
            RectTransform rect = CreateRect(objectName, parent);
            SetRect(
                rect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                position,
                size);
            AngledPanelGraphic surface =
                rect.gameObject.AddComponent<AngledPanelGraphic>();
            surface.Configure(
                ResolveHudSlant(),
                ResolveHudDepth(),
                primary ? ResolvePrimaryColor() : ResolveSurfaceColor(),
                ResolveShadowColor(),
                ResolvePanelHighlightColor(),
                primary);
            surface.raycastTarget = true;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = surface;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.68f, 0.68f, 0.68f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.56f, 0.56f, 0.56f, 1f);
            colors.fadeDuration = owner != null && owner.DesignTokens != null
                ? owner.DesignTokens.QuickTransitionSeconds
                : 0.12f;
            button.colors = colors;

            labelText = CreateText(
                UiHierarchyPaths.NewGameGuide.LabelName,
                rect,
                label,
                TextAlignmentOptions.MidlineLeft);
            SetRect(
                (RectTransform)labelText.transform,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(-40f, 0f));
            labelText.fontSize = owner != null && owner.DesignTokens != null
                ? owner.DesignTokens.ControlSize
                : 18f;
            labelText.characterSpacing = 1.5f;
            labelText.color = primary
                ? new Color(0.025f, 0.03f, 0.035f, 1f)
                : ResolveTextColor();
            return button;
        }

        private Color ResolvePrimaryColor()
        {
            return owner != null && owner.DesignTokens != null
                ? owner.DesignTokens.HudPrimary
                : new Color(0.96f, 0.98f, 1f, 1f);
        }

        private Color ResolveSurfaceColor()
        {
            return owner != null && owner.DesignTokens != null
                ? owner.DesignTokens.HudSurface
                : new Color(0.035f, 0.045f, 0.055f, 0.84f);
        }

        private Color ResolveShadowColor()
        {
            return owner != null && owner.DesignTokens != null
                ? owner.DesignTokens.HudShadow
                : new Color(0f, 0f, 0f, 0.72f);
        }

        private Color ResolveMutedColor()
        {
            Color color = owner != null && owner.DesignTokens != null
                ? owner.DesignTokens.OverlaySecondary
                : new Color(0.96f, 0.98f, 1f, 0.58f);
            color.a = Mathf.Max(0.58f, color.a);
            return color;
        }

        private Color ResolveTextColor()
        {
            return owner != null && owner.DesignTokens != null
                ? owner.DesignTokens.HudPrimary
                : new Color(0.96f, 0.98f, 1f, 1f);
        }

        private Color ResolvePanelHighlightColor()
        {
            Color color = ResolvePrimaryColor();
            color.a = 0.34f;
            return color;
        }

        private float ResolveHudSlant()
        {
            return owner != null && owner.DesignTokens != null
                ? owner.DesignTokens.HudElementSlant
                : 9f;
        }

        private float ResolveHudDepth()
        {
            return owner != null && owner.DesignTokens != null
                ? owner.DesignTokens.HudExtrusionDepth
                : 5f;
        }

        private Color ResolveBackdropColor()
        {
            Color color = owner != null && owner.DesignTokens != null
                ? owner.DesignTokens.OverlayBackdrop
                : new Color(0.008f, 0.01f, 0.014f, BackdropOpacity);
            color.a = BackdropOpacity;
            return color;
        }

        private static RectTransform CreateRect(string objectName, Transform parent)
        {
            GameObject child = new GameObject(
                objectName,
                typeof(RectTransform));
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static TMP_Text CreateText(
            string objectName,
            RectTransform parent,
            string content,
            TextAlignmentOptions alignment)
        {
            RectTransform rect = CreateRect(objectName, parent);
            TextMeshProUGUI text =
                rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = 24f;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private void OnDestroy()
        {
            if (activeGuide == this)
                activeGuide = null;
            if (isOpen)
            {
                isOpen = false;
                RestoreGameplayControls();
            }
        }
    }
}
