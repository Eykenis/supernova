using TMPro;
using Supernova.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    public enum SciFiUiScope
    {
        MainMenu,
        GameHud,
    }

    /// <summary>
    /// Applies the shared sci-fi visual language from the centralized UI asset catalog.
    /// The source sheets remain the art master; runtime screens use referenced sprites
    /// so controls stay readable and resolution independent.
    /// </summary>
    public static class SciFiUiSkin
    {
        private const string DecorationName = UiHierarchyPaths.Decoration.Frame;
        private const string PatternName = UiHierarchyPaths.Decoration.Telemetry;

        private static readonly Color Surface =
            new Color32(6, 20, 29, 244);
        private static readonly Color SurfaceRaised =
            new Color32(10, 35, 46, 248);
        private static readonly Color Accent =
            new Color32(91, 226, 249, 255);
        private static readonly Color AccentMuted =
            new Color32(75, 157, 173, 210);
        private static readonly Color TextPrimary =
            new Color32(225, 246, 250, 255);
        private static readonly Color TextSecondary =
            new Color32(126, 163, 172, 255);
        private static readonly Color Warning =
            new Color32(255, 159, 67, 255);

        public static Color AccentColor => Accent;
        public static Color TextPrimaryColor => TextPrimary;
        public static Color TextSecondaryColor => TextSecondary;

        public static bool HasRequiredAssets =>
            GetPrimaryFrame() != null
            && GetWideFrame() != null
            && GetSlotFrame() != null
            && GetThinFrame() != null
            && GetHudPanelFrame() != null
            && GetSlotCleanFrame() != null
            && GetButtonCleanFrame() != null
            && GetProgressCleanFrame() != null
            && GetPauseCardFrame() != null
            && GetLoadingDial() != null
            && GetTelemetryBackdrop() != null;

        /// <summary>
        /// Creates the initial main-menu look when the editor prefab builder is
        /// explicitly run. Runtime presentation must use the colors serialized on
        /// MainMenuCanvas and must not call this method.
        /// </summary>
        public static void ApplyMainMenuAuthoringDefaults(Transform root)
        {
            if (root == null)
                return;

            UiDesignTokens tokens = GetDesignTokens();
            Color menuPrimary = tokens != null
                ? tokens.OverlayPrimary
                : Color.white;
            Color menuSecondary = tokens != null
                ? tokens.OverlaySecondary
                : new Color(1f, 1f, 1f, 0.58f);
            Color menuDivider = tokens != null
                ? tokens.OverlayDivider
                : new Color(1f, 1f, 1f, 0.24f);
            Color menuSurface = tokens != null
                ? tokens.HudSurface
                : new Color(0.035f, 0.045f, 0.055f, 0.84f);

            RectTransform backdrop = FindRect(root, UiHierarchyPaths.MainMenu.Backdrop);
            if (backdrop != null)
            {
                Image background = backdrop.GetComponent<Image>();
                if (background != null)
                {
                    background.sprite = null;
                }
                DisableDecoration(backdrop, PatternName);
                SetImageColor(
                    backdrop,
                    "Ambient Left",
                    new Color(0f, 0f, 0f, 0.04f));
                SetImageColor(
                    backdrop,
                    "Ambient Right",
                    new Color(0f, 0f, 0f, 0.32f));
            }

            RectTransform hero = FindRect(root, UiHierarchyPaths.MainMenu.Hero);
            if (hero != null)
                DisableDecoration(hero, DecorationName);

            RectTransform card = FindRect(root, UiHierarchyPaths.MainMenu.ExpeditionControl);
            if (card != null)
                StylePanel(card, menuSurface);

            StyleButton(root, UiHierarchyPaths.MainMenu.ContinueGame, false);
            StyleButton(root, UiHierarchyPaths.MainMenu.NewGame, true);
            StyleButton(root, UiHierarchyPaths.MainMenu.Tutorial, false);
            StyleButton(root, UiHierarchyPaths.MainMenu.SystemSettings, false);
            StyleButton(root, UiHierarchyPaths.MainMenu.LeaveExpedition, false);
            StyleButton(root, UiHierarchyPaths.MainMenu.Return, false);
            StyleButton(root, UiHierarchyPaths.MainMenu.OverwriteConfirm, true);
            StyleButton(root, UiHierarchyPaths.MainMenu.OverwriteCancel, false);
            SetImageColor(
                root,
                UiHierarchyPaths.MainMenu.HeaderDivider,
                menuDivider);
            SetImageColor(
                root,
                UiHierarchyPaths.MainMenu.FooterDivider,
                menuDivider);

            RectTransform toggleBackground = FindRect(
                root,
                UiHierarchyPaths.MainMenu.FullscreenBackground);
            if (toggleBackground != null)
            {
                Image image = toggleBackground.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = null;
                    image.color = Color.clear;
                }
                DisableDecoration(toggleBackground, DecorationName);
                Toggle toggle = toggleBackground.GetComponentInParent<Toggle>();
                if (toggle != null && image != null)
                    toggle.targetGraphic = image;
            }
            SetImageColor(
                root,
                UiHierarchyPaths.MainMenu.FullscreenCheckmark,
                menuPrimary);

            RectTransform sliderBackground = FindRect(
                root,
                UiHierarchyPaths.MainMenu.MasterVolumeBackground);
            if (sliderBackground != null)
            {
                Image image = sliderBackground.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = null;
                    image.color = Color.clear;
                }
                DisableDecoration(sliderBackground, DecorationName);
            }
            SetImageColor(
                root,
                UiHierarchyPaths.MainMenu.MasterVolumeFill,
                menuPrimary);
            SetImageColor(
                root,
                UiHierarchyPaths.MainMenu.MasterVolumeHandle,
                menuPrimary);

            ApplyTypography(root);
            TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                TMP_Text label = labels[i];
                if (label == null || label.color.a < 0.01f)
                    continue;
                Color source = label.color;
                float luminance = source.r * 0.2126f
                    + source.g * 0.7152f
                    + source.b * 0.0722f;
                float gray = Mathf.Clamp(luminance, menuSecondary.a, 1f);
                label.color = new Color(
                    gray,
                    gray,
                    gray,
                    source.a);
            }

            SetTextColor(
                root,
                UiHierarchyPaths.MainMenu.BeginDescentLabel,
                tokens != null
                    ? tokens.OverlayInverse
                    : new Color(0.018f, 0.02f, 0.025f, 1f));
        }

        public static void ApplyGameHud(Transform root)
        {
            if (root == null)
                return;

            ApplyTypography(root);
            RectTransform health = FindRect(root, UiHierarchyPaths.Hud.HealthPanel);
            if (health != null)
            {
                Image image = health.GetComponent<Image>();
                if (image != null)
                    image.color = Color.clear;
                Outline outline = health.GetComponent<Outline>();
                if (outline != null)
                    outline.effectColor = Color.clear;
                Transform frame = health.Find(UiHierarchyPaths.Decoration.Frame);
                if (frame != null)
                    frame.gameObject.SetActive(false);
                SetTextColor(
                    health,
                    UiHierarchyPaths.Hud.HealthHeaderTitle,
                    new Color(1f, 1f, 1f, 0.72f));
                SetTextColor(
                    health,
                    UiHierarchyPaths.Hud.HealthHeaderValue,
                    Color.white);
                SetImageColor(health, UiHierarchyPaths.Hud.HealthTrack, Color.clear);
            }

            RectTransform hotbar = FindRect(root, UiHierarchyPaths.Hud.Hotbar);
            if (hotbar != null)
            {
                for (int i = 0; i < hotbar.childCount; i++)
                {
                    RectTransform slot = hotbar.GetChild(i) as RectTransform;
                    if (slot == null)
                        continue;
                    Image image = slot.GetComponent<Image>();
                    if (image != null)
                        image.color = Color.clear;
                    Transform frame = slot.Find(UiHierarchyPaths.Decoration.Frame);
                    if (frame != null)
                        frame.gameObject.SetActive(false);
                }
            }

            RectTransform crosshair = FindRect(root, UiHierarchyPaths.Hud.Crosshair);
            if (crosshair != null)
            {
                SetImageColor(crosshair, UiHierarchyPaths.Hud.Horizontal, Color.white);
                SetImageColor(crosshair, UiHierarchyPaths.Hud.Vertical, Color.white);
                EnsureCenterDot(crosshair);
                SetImageColor(
                    crosshair,
                    UiHierarchyPaths.Decoration.Center,
                    Color.white);
            }

            ApplyLoading(root);
            RectTransform pausePanel = FindRect(root, UiHierarchyPaths.Pause.Panel);
            ApplyPauseMenu(pausePanel);
        }

        public static void ApplyPauseMenu(Transform pausePanel)
        {
            if (pausePanel == null)
                return;
            ApplyTypography(pausePanel);

            UiDesignTokens tokens = GetDesignTokens();
            Color systemInk = tokens != null
                ? tokens.OverlayInverse
                : new Color(0.018f, 0.02f, 0.025f, 1f);
            string mainOptions = UiHierarchyPaths.Pause.Menu
                + "/"
                + UiHierarchyPaths.Pause.MainOptions;
            string settingsPanel = UiHierarchyPaths.Pause.Menu
                + "/"
                + UiHierarchyPaths.Pause.SettingsPanel;
            SetTextColor(
                pausePanel,
                mainOptions + "/" + UiHierarchyPaths.Pause.Title,
                systemInk);
            SetTextColor(
                pausePanel,
                settingsPanel + "/" + UiHierarchyPaths.Pause.Title,
                systemInk);
            SetTextColor(
                pausePanel,
                settingsPanel
                    + "/"
                    + UiHierarchyPaths.Pause.MasterVolume
                    + "/"
                    + UiHierarchyPaths.Pause.VolumeValue,
                systemInk);
        }

        private static void ApplyLoading(Transform root)
        {
            RectTransform panel = FindRect(root, UiHierarchyPaths.Loading.Panel);
            if (panel == null)
                return;

            UiDesignTokens tokens = GetDesignTokens();
            Color backdrop = tokens != null
                ? tokens.LoadingBackdrop
                : new Color(0.025f, 0.028f, 0.035f, 1f);
            Color primary = tokens != null
                ? tokens.OverlayPrimary
                : Color.white;
            Color secondary = tokens != null
                ? tokens.OverlaySecondary
                : new Color(1f, 1f, 1f, 0.58f);
            Color divider = tokens != null
                ? tokens.OverlayDivider
                : new Color(1f, 1f, 1f, 0.24f);

            Image background = panel.GetComponent<Image>();
            if (background != null)
                background.color = backdrop;
            DisableDecoration(panel, PatternName);

            RectTransform spinner = FindRect(panel, UiHierarchyPaths.Loading.LocalSpinner);
            if (spinner != null)
            {
                spinner.sizeDelta = new Vector2(44f, 44f);
                Image spinnerImage = spinner.GetComponent<Image>();
                if (spinnerImage != null)
                {
                    spinnerImage.sprite = null;
                    spinnerImage.type = Image.Type.Simple;
                    spinnerImage.preserveAspect = false;
                    spinnerImage.color = primary;
                }

                Transform core = spinner.Find(UiHierarchyPaths.Loading.Core);
                if (core != null)
                {
                    Image coreImage = core.GetComponent<Image>();
                    if (coreImage != null)
                        coreImage.color = Color.clear;
                    RectTransform coreRect = core as RectTransform;
                    if (coreRect != null)
                        coreRect.sizeDelta = new Vector2(38f, 38f);
                    core.gameObject.SetActive(true);
                }
            }

            RectTransform progressTrack = FindRect(panel, UiHierarchyPaths.Loading.LocalProgressTrack);
            if (progressTrack != null)
            {
                Vector2 trackSize = progressTrack.sizeDelta;
                trackSize.y = tokens != null
                    ? tokens.LoadingProgressThickness
                    : 6f;
                progressTrack.sizeDelta = trackSize;
                Image track = progressTrack.GetComponent<Image>();
                if (track != null)
                    track.color = divider;
                DisableDecoration(progressTrack, DecorationName);
            }

            SetImageColor(panel, UiHierarchyPaths.Loading.LocalProgressFill, primary);
            SetTextColor(panel, UiHierarchyPaths.Loading.Brand, secondary);
            SetTextColor(panel, UiHierarchyPaths.Loading.Title, primary);
            SetTextColor(panel, UiHierarchyPaths.Loading.LocalStatus, secondary);
            SetTextColor(panel, UiHierarchyPaths.Loading.LocalProgress, primary);
            SetTextColor(panel, UiHierarchyPaths.Loading.Hint, secondary);
        }

        private static UiDesignTokens GetDesignTokens()
        {
            return GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.DesignTokens
                : null;
        }

        private static void DisableDecoration(Transform parent, string objectName)
        {
            if (parent == null)
                return;
            Transform decoration = parent.Find(objectName);
            if (decoration != null)
                decoration.gameObject.SetActive(false);
        }

        private static void StyleButton(Transform root, string path, bool primary)
        {
            RectTransform rect = FindRect(root, path);
            if (rect == null)
                return;

            Image image = rect.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.enabled = false;
            }

            AngledPanelGraphic angledSurface = EnsureMenuAngledSurface(
                rect,
                primary);

            Button button = rect.GetComponent<Button>();
            if (button != null)
            {
                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = primary
                    ? new Color(0.82f, 0.82f, 0.82f, 1f)
                    : new Color(0.28f, 0.3f, 0.34f, 1f);
                colors.selectedColor = colors.highlightedColor;
                colors.pressedColor = new Color(0.62f, 0.62f, 0.62f, 1f);
                colors.disabledColor = new Color(0.34f, 0.34f, 0.34f, 0.6f);
                colors.fadeDuration = 0.12f;
                button.colors = colors;
                if (angledSurface != null)
                    button.targetGraphic = angledSurface;
            }

            DisableDecoration(rect, DecorationName);
            UiDesignTokens tokens = GetDesignTokens();
            SetTextColor(
                rect,
                UiHierarchyPaths.Pause.Label,
                primary
                    ? tokens != null
                        ? tokens.OverlayInverse
                        : new Color(0.018f, 0.02f, 0.025f, 1f)
                    : tokens != null
                        ? tokens.OverlayPrimary
                        : Color.white);
        }

        private static AngledPanelGraphic EnsureMenuAngledSurface(
            RectTransform parent,
            bool primary)
        {
            if (parent == null)
                return null;

            Transform existing = parent.Find(
                UiHierarchyPaths.MainMenu.AngledSurface);
            RectTransform rect = existing as RectTransform;
            AngledPanelGraphic graphic = existing != null
                ? existing.GetComponent<AngledPanelGraphic>()
                : null;
            if (rect == null || graphic == null)
            {
                if (existing != null)
                    existing.gameObject.SetActive(false);
                GameObject surface = new GameObject(
                    UiHierarchyPaths.MainMenu.AngledSurface,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(AngledPanelGraphic));
                rect = surface.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                graphic = surface.GetComponent<AngledPanelGraphic>();
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsFirstSibling();

            UiDesignTokens tokens = GetDesignTokens();
            float slant = tokens != null
                ? tokens.HudElementSlant * 1.8f
                : 16f;
            float depth = tokens != null
                ? tokens.HudExtrusionDepth
                : 5f;
            Color front = primary
                ? tokens != null
                    ? tokens.OverlayPrimary
                    : Color.white
                : tokens != null
                    ? tokens.HudSurface
                    : new Color(0.035f, 0.045f, 0.055f, 0.84f);
            Color back = tokens != null
                ? tokens.HudShadow
                : new Color(0f, 0f, 0f, 0.72f);
            Color highlight = tokens != null
                ? tokens.HudMuted
                : new Color(1f, 1f, 1f, 0.2f);
            bool reverse = tokens == null || tokens.HudHotbarReverseSlant;
            graphic.Configure(
                slant,
                depth,
                front,
                back,
                highlight,
                reverse);
            graphic.raycastTarget = true;
            return graphic;
        }

        private static void StylePanel(
            RectTransform rect,
            Color fillColor)
        {
            Image image = rect.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.color = fillColor;
            }

            Outline outline = rect.GetComponent<Outline>();
            if (outline != null)
                outline.effectColor = Color.clear;
            DisableDecoration(rect, DecorationName);
        }

        private static Image EnsureFrame(
            RectTransform parent,
            Sprite sprite,
            Color color,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            if (parent == null || sprite == null)
                return null;

            Transform existing = parent.Find(DecorationName);
            RectTransform rect;
            Image image;
            if (existing != null)
            {
                rect = existing as RectTransform;
                image = existing.GetComponent<Image>();
            }
            else
            {
                GameObject decoration = new GameObject(
                    DecorationName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                rect = decoration.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                image = decoration.GetComponent<Image>();
            }

            if (rect == null || image == null)
                return null;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.fillCenter = false;
            image.color = color;
            image.raycastTarget = false;
            rect.SetAsLastSibling();
            return image;
        }

        private static void EnsureTelemetry(RectTransform parent, Color color)
        {
            Texture2D texture = GetTelemetryBackdrop();
            if (parent == null || texture == null)
                return;

            Transform existing = parent.Find(PatternName);
            RectTransform rect;
            RawImage rawImage;
            if (existing != null)
            {
                rect = existing as RectTransform;
                rawImage = existing.GetComponent<RawImage>();
            }
            else
            {
                GameObject pattern = new GameObject(
                    PatternName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RawImage));
                rect = pattern.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                rawImage = pattern.GetComponent<RawImage>();
            }

            if (rect == null || rawImage == null)
                return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rawImage.texture = texture;
            rawImage.color = color;
            rawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            rawImage.raycastTarget = false;
            rect.SetAsFirstSibling();
        }

        private static void EnsureCenterDot(RectTransform crosshair)
        {
            const string centerDotName = UiHierarchyPaths.Decoration.Center;
            Transform existing = crosshair.Find(centerDotName);
            RectTransform rect;
            Image image;
            if (existing != null)
            {
                rect = existing as RectTransform;
                image = existing.GetComponent<Image>();
            }
            else
            {
                GameObject center = new GameObject(
                    centerDotName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                rect = center.GetComponent<RectTransform>();
                rect.SetParent(crosshair, false);
                image = center.GetComponent<Image>();
            }

            if (rect == null || image == null)
                return;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(3f, 3f);
            image.color = Accent;
            image.raycastTarget = false;
        }

        private static void ApplyTypography(Transform root)
        {
            TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                TMP_Text label = labels[i];
                if (label == null)
                    continue;
                label.extraPadding = true;
                if (label.color.a < 0.01f)
                    continue;

                string objectName = label.gameObject.name;
                if (objectName.Contains("Title")
                    || objectName == "Brand"
                    || objectName == "Value"
                    || objectName == "Progress")
                {
                    label.color = objectName == "Brand" || objectName == "Progress"
                        ? Accent
                        : TextPrimary;
                }
            }
        }

        private static void SetTextColor(Transform root, string path, Color color)
        {
            Transform target = root.Find(path);
            if (target == null)
                return;
            TMP_Text text = target.GetComponent<TMP_Text>();
            if (text != null)
                text.color = color;
        }

        private static void SetImageColor(Transform root, string path, Color color)
        {
            Transform target = root.Find(path);
            if (target == null)
                return;
            Image image = target.GetComponent<Image>();
            if (image != null)
                image.color = color;
        }

        private static void SetAnchoredPositionY(Transform root, string path, float y)
        {
            RectTransform rect = root.Find(path) as RectTransform;
            if (rect == null)
                return;
            Vector2 position = rect.anchoredPosition;
            position.y = y;
            rect.anchoredPosition = position;
        }

        private static RectTransform FindRect(Transform root, string path)
        {
            Transform target = root.Find(path);
            return target as RectTransform;
        }

        private static Sprite GetPrimaryFrame()
        {
            return GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.PrimaryFrame
                : null;
        }

        private static Sprite GetWideFrame()
        {
            return GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.WideFrame
                : null;
        }

        private static Sprite GetSlotFrame()
        {
            return GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.SlotFrame
                : null;
        }

        private static Sprite GetThinFrame()
        {
            return GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.ThinFrame
                : null;
        }

        private static Sprite GetHudPanelFrame()
        {
            return GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.HudPanelFrame
                : null;
        }

        private static Sprite GetSlotCleanFrame()
        {
            return GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.SlotCleanFrame
                : null;
        }

        private static Sprite GetButtonCleanFrame()
        {
            return GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.ButtonCleanFrame
                : null;
        }

        private static Sprite GetProgressCleanFrame()
        {
            return GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.ProgressCleanFrame
                : null;
        }

        private static Sprite GetPauseCardFrame()
        {
            return GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.PauseCardFrame
                : null;
        }

        private static Sprite GetLoadingDial()
        {
            return GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.LoadingDial
                : null;
        }

        private static Texture2D GetTelemetryBackdrop()
        {
            return GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.TelemetryBackdrop
                : null;
        }
    }

}
