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
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private Button playButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button settingsBackButton;
        [SerializeField] private Toggle fullscreenToggle;
        [SerializeField] private Slider volumeSlider;
        [SerializeField] private TMP_Text volumeValueLabel;
        [SerializeField] private TMP_Text statusLabel;

        public Button PlayButton => playButton;
        public Button SettingsButton => settingsButton;
        public Button QuitButton => quitButton;
        public Button SettingsBackButton => settingsBackButton;
        public Toggle FullscreenToggle => fullscreenToggle;
        public Slider VolumeSlider => volumeSlider;

        public void Configure(
            GameObject main,
            GameObject settings,
            Button play,
            Button openSettings,
            Button quit,
            Button back,
            Toggle fullscreen,
            Slider volume,
            TMP_Text volumeValue,
            TMP_Text status)
        {
            mainPanel = main;
            settingsPanel = settings;
            playButton = play;
            settingsButton = openSettings;
            quitButton = quit;
            settingsBackButton = back;
            fullscreenToggle = fullscreen;
            volumeSlider = volume;
            volumeValueLabel = volumeValue;
            statusLabel = status;
        }

        public void ShowMainPanel()
        {
            if (mainPanel != null) mainPanel.SetActive(true);
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        public void ShowSettingsPanel()
        {
            if (mainPanel != null) mainPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(true);
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
