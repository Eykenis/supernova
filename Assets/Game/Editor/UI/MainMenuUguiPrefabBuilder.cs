using Supernova.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Supernova.Editor.UI
{
    public static class MainMenuUguiPrefabBuilder
    {
        private const string ThemeFolder = ProjectAssetPaths.Folders.UiConfig;
        private const string ThemePath = ThemeFolder + "/DefaultUiDesignTokens.asset";
        private const string PrefabFolder = ProjectAssetPaths.Folders.UiViews;
        private const string PrefabPath = PrefabFolder + "/MainMenuCanvas.prefab";

        [MenuItem("Supernova/UI/Rebuild Main Menu UGUI Prefab")]
        public static void Rebuild()
        {
            EnsureFolder(ThemeFolder);
            EnsureFolder(PrefabFolder);
            UiDesignTokens tokens = LoadOrCreateTokens();
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;

            try
            {
                root = BuildRoot(tokens);
                SceneManager.MoveGameObjectToScene(root, previewScene);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (!success || prefab == null)
                    throw new UnityException("Failed to save main-menu UGUI prefab.");

                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssets();
                GameAssetCatalogBuilder.EnsureCatalog();
                Debug.Log("Rebuilt main-menu UGUI prefab at " + PrefabPath);
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static UiDesignTokens LoadOrCreateTokens()
        {
            UiDesignTokens tokens = AssetDatabase.LoadAssetAtPath<UiDesignTokens>(ThemePath);
            if (tokens != null) return tokens;

            tokens = ScriptableObject.CreateInstance<UiDesignTokens>();
            AssetDatabase.CreateAsset(tokens, ThemePath);
            AssetDatabase.SaveAssets();
            return tokens;
        }

        private static GameObject BuildRoot(UiDesignTokens tokens)
        {
            GameObject root = new GameObject(
                "MainMenuCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(UiCanvasPolicy),
                typeof(MainMenuView),
                typeof(SciFiUiStyler));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            Stretch(rootRect);

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            UiCanvasPolicy policy = root.GetComponent<UiCanvasPolicy>();
            policy.SetDesignTokens(tokens);

            RectTransform background = CreateRect(
                "Backdrop",
                rootRect,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            Image backgroundImage = background.gameObject.AddComponent<Image>();
            backgroundImage.color = tokens.Backdrop;
            backgroundImage.raycastTarget = false;

            CreateAmbientPanel(
                "Ambient Left",
                background,
                new Vector2(0f, 0.13f),
                new Vector2(0.23f, 1f),
                new Color(0.03f, 0.18f, 0.2f, 0.52f));
            CreateAmbientPanel(
                "Ambient Right",
                background,
                new Vector2(0.84f, 0f),
                new Vector2(1f, 0.38f),
                new Color(0.24f, 0.07f, 0.025f, 0.56f));

            RectTransform safeArea = CreateRect(
                "Safe Area",
                rootRect,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            safeArea.gameObject.AddComponent<UiSafeArea>();

            BuildHeader(safeArea, tokens);
            BuildHero(safeArea, tokens);
            MainMenuViewReferences viewReferences = BuildMenuCard(safeArea, tokens);
            BuildFooter(safeArea, tokens);

            MainMenuView view = root.GetComponent<MainMenuView>();
            SciFiUiStyler styler = root.GetComponent<SciFiUiStyler>();
            styler.Configure(SciFiUiScope.MainMenu);
            view.Configure(
                viewReferences.MainPanel,
                viewReferences.SettingsPanel,
                viewReferences.PlayButton,
                viewReferences.SettingsButton,
                viewReferences.QuitButton,
                viewReferences.BackButton,
                viewReferences.FullscreenToggle,
                viewReferences.VolumeSlider,
                viewReferences.VolumeValue,
                viewReferences.Status);
            view.ShowMainPanel();
            SciFiUiSkin.ApplyMainMenu(root.transform);
            return root;
        }

        private static void BuildHeader(RectTransform parent, UiDesignTokens tokens)
        {
            RectTransform header = CreateRect(
                "Header",
                parent,
                new Vector2(0.06f, 0.9f),
                new Vector2(0.94f, 0.97f),
                Vector2.zero,
                Vector2.zero);
            CreateText(
                "Brand",
                header,
                "S  SUPERNOVA\n    DEEP FIELD INITIATIVE",
                17f,
                tokens.TextPrimary,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0f),
                new Vector2(0.45f, 1f));
            CreateText(
                "Build",
                header,
                "EXPEDITION BUILD // 0.2",
                tokens.CaptionSize,
                tokens.TextSecondary,
                TextAlignmentOptions.MidlineRight,
                new Vector2(0.55f, 0f),
                Vector2.one);
            CreateImage(
                "Divider",
                header,
                tokens.Divider,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, -1f),
                new Vector2(0f, 1f),
                false);
        }

        private static void BuildHero(RectTransform parent, UiDesignTokens tokens)
        {
            RectTransform hero = CreateRect(
                "Hero",
                parent,
                new Vector2(0.08f, 0.21f),
                new Vector2(0.59f, 0.81f),
                Vector2.zero,
                Vector2.zero);

            CreateText(
                "Eyebrow",
                hero,
                "DEEP CAVE EXPEDITION // 07",
                tokens.CaptionSize,
                tokens.Accent,
                TextAlignmentOptions.BottomLeft,
                new Vector2(0f, 0.84f),
                new Vector2(1f, 0.94f));
            CreateText(
                "Title Small",
                hero,
                "SUPERNOVA",
                28f,
                tokens.TextSecondary,
                TextAlignmentOptions.BottomLeft,
                new Vector2(0f, 0.69f),
                new Vector2(1f, 0.84f));
            TMP_Text title = CreateText(
                "Title",
                hero,
                "DESCENT",
                tokens.DisplaySize,
                tokens.TextPrimary,
                TextAlignmentOptions.BottomLeft,
                new Vector2(0f, 0.47f),
                new Vector2(1f, 0.72f));
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 4f;

            CreateImage(
                "Title Divider",
                hero,
                tokens.Divider,
                new Vector2(0f, 0.45f),
                new Vector2(0.9f, 0.45f),
                new Vector2(0f, -1f),
                new Vector2(0f, 1f),
                false);
            CreateImage(
                "Title Accent",
                hero,
                tokens.Accent,
                new Vector2(0f, 0.45f),
                new Vector2(0.14f, 0.45f),
                new Vector2(0f, -2f),
                new Vector2(0f, 2f),
                false);
            CreateText(
                "Tagline",
                hero,
                "LIGHT FADES. THE WORLD KEEPS GROWING.",
                tokens.BodySize,
                new Color(0.66f, 0.82f, 0.83f, 1f),
                TextAlignmentOptions.TopLeft,
                new Vector2(0f, 0.32f),
                new Vector2(1f, 0.43f));
            TMP_Text description = CreateText(
                "Description",
                hero,
                "Enter an uncharted living cave. Recover what the dark has kept, "
                + "and find a way back.",
                tokens.BodySize,
                tokens.TextSecondary,
                TextAlignmentOptions.TopLeft,
                new Vector2(0f, 0.15f),
                new Vector2(0.86f, 0.33f));
            description.enableWordWrapping = true;
            description.lineSpacing = 8f;
            CreateText(
                "Stats",
                hero,
                "01  ACTIVE EXPEDITION        INFINITE  UNMAPPED DEPTH",
                tokens.CaptionSize,
                tokens.TextSecondary,
                TextAlignmentOptions.BottomLeft,
                new Vector2(0f, 0f),
                new Vector2(1f, 0.13f));
        }

        private static MainMenuViewReferences BuildMenuCard(
            RectTransform parent,
            UiDesignTokens tokens)
        {
            RectTransform card = CreateRect(
                "Expedition Control",
                parent,
                new Vector2(0.67f, 0.2f),
                new Vector2(0.93f, 0.8f),
                Vector2.zero,
                Vector2.zero);
            Image cardImage = card.gameObject.AddComponent<Image>();
            cardImage.color = tokens.Surface;
            cardImage.raycastTarget = true;
            Outline cardOutline = card.gameObject.AddComponent<Outline>();
            cardOutline.effectColor = tokens.Divider;
            cardOutline.effectDistance = new Vector2(1f, -1f);

            CreateText(
                "Overline",
                card,
                "EXPEDITION CONTROL",
                tokens.CaptionSize,
                tokens.TextSecondary,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0.07f, 0.86f),
                new Vector2(0.93f, 0.96f));

            RectTransform mainPanel = CreateRect(
                "Main Panel",
                card,
                new Vector2(0.07f, 0.08f),
                new Vector2(0.93f, 0.84f),
                Vector2.zero,
                Vector2.zero);
            Button playButton = CreateButton(
                "Begin Descent",
                mainPanel,
                "BEGIN DESCENT",
                tokens,
                true,
                new Vector2(0f, 0.72f),
                new Vector2(1f, 0.94f));
            Button settingsButton = CreateButton(
                "System Settings",
                mainPanel,
                "SYSTEM SETTINGS",
                tokens,
                false,
                new Vector2(0f, 0.47f),
                new Vector2(1f, 0.67f));
            Button quitButton = CreateButton(
                "Leave Expedition",
                mainPanel,
                "LEAVE EXPEDITION",
                tokens,
                false,
                new Vector2(0f, 0.21f),
                new Vector2(1f, 0.41f));
            TMP_Text status = CreateText(
                "Status",
                mainPanel,
                "SYSTEMS READY",
                tokens.CaptionSize,
                tokens.Success,
                TextAlignmentOptions.BottomLeft,
                new Vector2(0f, 0f),
                new Vector2(1f, 0.14f));

            RectTransform settingsPanel = CreateRect(
                "Settings Panel",
                card,
                new Vector2(0.07f, 0.07f),
                new Vector2(0.93f, 0.84f),
                Vector2.zero,
                Vector2.zero);
            CreateText(
                "Settings Title",
                settingsPanel,
                "SYSTEM SETTINGS",
                26f,
                tokens.TextPrimary,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.84f),
                new Vector2(1f, 1f));
            CreateText(
                "Display Section",
                settingsPanel,
                "DISPLAY",
                tokens.CaptionSize,
                tokens.Accent,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.71f),
                new Vector2(1f, 0.82f));
            Toggle fullscreen = CreateToggle(
                "Fullscreen",
                settingsPanel,
                "FULLSCREEN",
                tokens,
                new Vector2(0f, 0.58f),
                new Vector2(1f, 0.71f));
            CreateText(
                "Audio Section",
                settingsPanel,
                "AUDIO",
                tokens.CaptionSize,
                tokens.Accent,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.44f),
                new Vector2(1f, 0.55f));
            CreateText(
                "Volume Label",
                settingsPanel,
                "MASTER VOLUME",
                tokens.CaptionSize,
                tokens.TextSecondary,
                TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.33f),
                new Vector2(0.55f, 0.44f));
            Slider volume = CreateSlider(
                "Master Volume",
                settingsPanel,
                tokens,
                new Vector2(0f, 0.23f),
                new Vector2(0.82f, 0.33f));
            TMP_Text volumeValue = CreateText(
                "Volume Value",
                settingsPanel,
                "80%",
                tokens.CaptionSize,
                tokens.Focus,
                TextAlignmentOptions.MidlineRight,
                new Vector2(0.82f, 0.23f),
                new Vector2(1f, 0.33f));
            Button back = CreateButton(
                "Return",
                settingsPanel,
                "RETURN",
                tokens,
                false,
                new Vector2(0f, 0f),
                new Vector2(1f, 0.17f));
            settingsPanel.gameObject.SetActive(false);

            return new MainMenuViewReferences
            {
                MainPanel = mainPanel.gameObject,
                SettingsPanel = settingsPanel.gameObject,
                PlayButton = playButton,
                SettingsButton = settingsButton,
                QuitButton = quitButton,
                BackButton = back,
                FullscreenToggle = fullscreen,
                VolumeSlider = volume,
                VolumeValue = volumeValue,
                Status = status,
            };
        }

        private static void BuildFooter(RectTransform parent, UiDesignTokens tokens)
        {
            RectTransform footer = CreateRect(
                "Footer",
                parent,
                new Vector2(0.06f, 0.035f),
                new Vector2(0.94f, 0.1f),
                Vector2.zero,
                Vector2.zero);
            CreateImage(
                "Divider",
                footer,
                tokens.Divider,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -1f),
                new Vector2(0f, 1f),
                false);
            CreateText(
                "Controls",
                footer,
                "WASD  MOVE    |    MOUSE  LOOK    |    E  INTERACT",
                tokens.CaptionSize,
                tokens.TextSecondary,
                TextAlignmentOptions.MidlineLeft,
                Vector2.zero,
                new Vector2(0.75f, 1f));
            CreateText(
                "Signal",
                footer,
                "SIGNAL // STABLE",
                tokens.CaptionSize,
                tokens.Success,
                TextAlignmentOptions.MidlineRight,
                new Vector2(0.75f, 0f),
                Vector2.one);
        }

        private static void CreateAmbientPanel(
            string name,
            RectTransform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color color)
        {
            CreateImage(
                name,
                parent,
                color,
                anchorMin,
                anchorMax,
                Vector2.zero,
                Vector2.zero,
                false);
        }

        private static Button CreateButton(
            string name,
            RectTransform parent,
            string label,
            UiDesignTokens tokens,
            bool primary,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            RectTransform rect = CreateRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                Vector2.zero,
                Vector2.zero);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = primary ? tokens.Accent : tokens.SurfaceRaised;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = primary
                ? tokens.AccentHover
                : new Color(0.3f, 0.46f, 0.5f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.72f, 0.76f, 0.78f, 1f);
            colors.disabledColor = new Color(0.4f, 0.42f, 0.44f, 0.55f);
            colors.fadeDuration = tokens.QuickTransitionSeconds;
            button.colors = colors;

            TMP_Text text = CreateText(
                "Label",
                rect,
                label,
                tokens.ControlSize,
                tokens.TextPrimary,
                TextAlignmentOptions.MidlineLeft,
                Vector2.zero,
                Vector2.one,
                new Vector2(20f, 0f),
                new Vector2(-20f, 0f));
            text.fontStyle = FontStyles.Bold;
            text.characterSpacing = 1.5f;
            return button;
        }

        private static Toggle CreateToggle(
            string name,
            RectTransform parent,
            string label,
            UiDesignTokens tokens,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            RectTransform rect = CreateRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                Vector2.zero,
                Vector2.zero);
            Toggle toggle = rect.gameObject.AddComponent<Toggle>();
            CreateText(
                "Label",
                rect,
                label,
                tokens.ControlSize,
                tokens.TextSecondary,
                TextAlignmentOptions.MidlineLeft,
                Vector2.zero,
                new Vector2(0.78f, 1f));
            Image background = CreateImage(
                "Background",
                rect,
                tokens.SurfaceRaised,
                new Vector2(0.86f, 0.2f),
                new Vector2(1f, 0.8f),
                Vector2.zero,
                Vector2.zero,
                true);
            Image checkmark = CreateImage(
                "Checkmark",
                background.rectTransform,
                tokens.Accent,
                new Vector2(0.24f, 0.24f),
                new Vector2(0.76f, 0.76f),
                Vector2.zero,
                Vector2.zero,
                false);
            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.isOn = true;
            return toggle;
        }

        private static Slider CreateSlider(
            string name,
            RectTransform parent,
            UiDesignTokens tokens,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            RectTransform rect = CreateRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                Vector2.zero,
                Vector2.zero);
            Slider slider = rect.gameObject.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.value = 80f;
            slider.direction = Slider.Direction.LeftToRight;

            Image background = CreateImage(
                "Background",
                rect,
                tokens.SurfaceRaised,
                new Vector2(0f, 0.4f),
                new Vector2(1f, 0.6f),
                Vector2.zero,
                Vector2.zero,
                true);
            RectTransform fillArea = CreateRect(
                "Fill Area",
                rect,
                new Vector2(0f, 0.4f),
                new Vector2(1f, 0.6f),
                new Vector2(6f, 0f),
                new Vector2(-6f, 0f));
            Image fill = CreateImage(
                "Fill",
                fillArea,
                tokens.Focus,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                false);
            RectTransform handleArea = CreateRect(
                "Handle Slide Area",
                rect,
                new Vector2(0f, 0f),
                Vector2.one,
                new Vector2(8f, 0f),
                new Vector2(-8f, 0f));
            Image handle = CreateImage(
                "Handle",
                handleArea,
                tokens.TextPrimary,
                new Vector2(0f, 0.18f),
                new Vector2(0f, 0.82f),
                new Vector2(-6f, 0f),
                new Vector2(6f, 0f),
                true);
            slider.targetGraphic = handle;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            background.raycastTarget = true;
            return slider;
        }

        private static TMP_Text CreateText(
            string name,
            RectTransform parent,
            string value,
            float size,
            Color color,
            TextAlignmentOptions alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2? offsetMin = null,
            Vector2? offsetMax = null)
        {
            RectTransform rect = CreateRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                offsetMin ?? Vector2.zero,
                offsetMax ?? Vector2.zero);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            return text;
        }

        private static Image CreateImage(
            string name,
            RectTransform parent,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            bool raycastTarget)
        {
            RectTransform rect = CreateRect(
                name,
                parent,
                anchorMin,
                anchorMax,
                offsetMin,
                offsetMax);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        private static RectTransform CreateRect(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new UnityException("Invalid asset folder: " + path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private struct MainMenuViewReferences
        {
            public GameObject MainPanel;
            public GameObject SettingsPanel;
            public Button PlayButton;
            public Button SettingsButton;
            public Button QuitButton;
            public Button BackButton;
            public Toggle FullscreenToggle;
            public Slider VolumeSlider;
            public TMP_Text VolumeValue;
            public TMP_Text Status;
        }
    }
}
