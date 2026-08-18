using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Serialized references for the editable UGUI main-menu prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuView : MonoBehaviour
    {
        public const float ContinueGameWidthFraction = 0.618f;

        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private Button playButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private TMP_Text continueSaveSummaryLabel;
        [SerializeField] private Button tutorialButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button settingsBackButton;
        [SerializeField] private Toggle fullscreenToggle;
        [SerializeField] private Slider volumeSlider;
        [SerializeField] private TMP_Text volumeValueLabel;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private TMP_Text versionLabel;
        [SerializeField] private GameObject overwriteConfirmationPanel;
        [SerializeField] private Button overwriteConfirmButton;
        [SerializeField] private Button overwriteCancelButton;
        [SerializeField] private MainMenuCharacterOverlay characterOverlay;

        public Button PlayButton => playButton;
        public Button ContinueButton => continueButton;
        public TMP_Text ContinueSaveSummaryLabel => continueSaveSummaryLabel;
        public Button TutorialButton => tutorialButton;
        public Button SettingsButton => settingsButton;
        public Button QuitButton => quitButton;
        public Button SettingsBackButton => settingsBackButton;
        public Toggle FullscreenToggle => fullscreenToggle;
        public Slider VolumeSlider => volumeSlider;
        public TMP_Text VersionLabel => versionLabel;
        public GameObject OverwriteConfirmationPanel =>
            overwriteConfirmationPanel;
        public Button OverwriteConfirmButton => overwriteConfirmButton;
        public Button OverwriteCancelButton => overwriteCancelButton;
        public MainMenuCharacterOverlay CharacterOverlay => characterOverlay;

        public CanvasGroup PrepareHomePresentation()
        {
            Transform root = transform;
            Transform hero = root.Find(UiHierarchyPaths.MainMenu.Hero);
            if (hero != null)
                hero.gameObject.SetActive(false);

            CanvasGroup group = GetComponent<CanvasGroup>();
            if (group == null)
                group = gameObject.AddComponent<CanvasGroup>();
            return group;
        }

        public void Configure(
            GameObject main,
            GameObject settings,
            Button play,
            Button continueGame,
            TMP_Text continueSaveSummary,
            Button tutorial,
            Button openSettings,
            Button quit,
            Button back,
            Toggle fullscreen,
            Slider volume,
            TMP_Text volumeValue,
            TMP_Text status,
            TMP_Text version,
            GameObject overwriteConfirmation,
            Button overwriteConfirm,
            Button overwriteCancel,
            MainMenuCharacterOverlay overlay)
        {
            mainPanel = main;
            settingsPanel = settings;
            playButton = play;
            continueButton = continueGame;
            continueSaveSummaryLabel = continueSaveSummary;
            tutorialButton = tutorial;
            settingsButton = openSettings;
            quitButton = quit;
            settingsBackButton = back;
            fullscreenToggle = fullscreen;
            volumeSlider = volume;
            volumeValueLabel = volumeValue;
            statusLabel = status;
            versionLabel = version;
            overwriteConfirmationPanel = overwriteConfirmation;
            overwriteConfirmButton = overwriteConfirm;
            overwriteCancelButton = overwriteCancel;
            characterOverlay = overlay;
            RefreshVersionLabel();
        }

        private void OnEnable()
        {
            RefreshVersionLabel();
        }

        public void RefreshVersionLabel()
        {
            if (versionLabel != null)
                versionLabel.text = FormatVersionLabel(Application.version);
        }

        public static string FormatVersionLabel(string version)
        {
            string versionPrefix = !string.IsNullOrEmpty(version)
                && (version[0] == 'v' || version[0] == 'V')
                    ? string.Empty
                    : "v";
            return "版本号: " + versionPrefix + version;
        }

        public void SetContinueGameVisible(bool visible)
        {
            EnsureContinueButton();
            SetButtonLabel(playButton, "    新游戏");
            if (continueButton != null)
            {
                SetButtonLabel(continueButton, "    继续游戏");
                continueButton.gameObject.SetActive(visible);
            }

            SetButtonAnchors(
                continueButton,
                0f,
                0.75f,
                ContinueGameWidthFraction,
                0.94f);
            SetButtonAnchors(
                playButton,
                visible ? ContinueGameWidthFraction : 0f,
                0.75f,
                1f,
                0.94f);
            SetButtonAnchors(
                tutorialButton,
                0f,
                0.54f,
                1f,
                0.71f);
            SetButtonAnchors(
                settingsButton,
                0f,
                0.33f,
                1f,
                0.50f);
            SetButtonAnchors(
                quitButton,
                0f,
                0.12f,
                1f,
                0.29f);
        }

        public void SetContinueGameSummary(int credits, int levelNumber)
        {
            if (continueSaveSummaryLabel == null)
                return;

            continueSaveSummaryLabel.text =
                "存款：$" + Mathf.Max(0, credits)
                + "\n第" + Mathf.Max(1, levelNumber) + "关";
        }

        public bool ShowOverwriteConfirmation()
        {
            if (overwriteConfirmationPanel == null)
                return false;

            overwriteConfirmationPanel.SetActive(true);
            return true;
        }

        public void HideOverwriteConfirmation()
        {
            if (overwriteConfirmationPanel != null)
                overwriteConfirmationPanel.SetActive(false);
        }

        private void EnsureContinueButton()
        {
            if (continueButton != null || playButton == null)
                return;

            GameObject clone = Instantiate(
                playButton.gameObject,
                playButton.transform.parent);
            clone.name = "Continue Game";
            clone.transform.SetSiblingIndex(playButton.transform.GetSiblingIndex());
            continueButton = clone.GetComponent<Button>();
            SetButtonAnchors(
                continueButton,
                0f,
                0.75f,
                ContinueGameWidthFraction,
                0.94f);
        }

        private static void SetButtonLabel(Button button, string text)
        {
            if (button == null)
                return;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = text;
        }

        private static void SetButtonAnchors(
            Button button,
            float minimumX,
            float minimumY,
            float maximumX,
            float maximumY)
        {
            if (button == null)
                return;

            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(minimumX, minimumY);
            rect.anchorMax = new Vector2(maximumX, maximumY);
        }

        public void ShowMainPanel()
        {
            if (mainPanel != null) mainPanel.SetActive(true);
            if (settingsPanel != null) settingsPanel.SetActive(false);
            HideOverwriteConfirmation();
        }

        public void ShowSettingsPanel()
        {
            if (mainPanel != null) mainPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(true);
            HideOverwriteConfirmation();
        }

        public void SetVolumeValue(float value)
        {
            if (volumeValueLabel != null)
                volumeValueLabel.text = Mathf.RoundToInt(value).ToString("00") + "%";
        }

        public void SetStatus(string message)
        {
            if (statusLabel != null) statusLabel.text = message;
        }

    }
}
