using Supernova.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using ToolkitButton = UnityEngine.UIElements.Button;
using ToolkitLabel = UnityEngine.UIElements.Label;
using ToolkitSlider = UnityEngine.UIElements.Slider;
using ToolkitToggle = UnityEngine.UIElements.Toggle;
using ToolkitVisualElement = UnityEngine.UIElements.VisualElement;

[DisallowMultipleComponent]
public sealed class MainMenuController : MonoBehaviour
{
    private const string DefaultViewResourcePath = "UI/MainMenuCanvas";
    private const string FullscreenPreferenceKey = "ui.fullscreen";
    private const string VolumePreferenceKey = "ui.master-volume";

    [SerializeField] private string gameplaySceneName = "InfiniteCaves";
    [SerializeField] private GameObject uguiViewPrefab;

    private MainMenuView uguiView;
    private UIDocument legacyDocument;
    private ToolkitButton legacyPlayButton;
    private ToolkitButton legacySettingsButton;
    private ToolkitButton legacyQuitButton;
    private ToolkitButton legacyBackButton;
    private ToolkitVisualElement legacyMainPanel;
    private ToolkitVisualElement legacySettingsPanel;
    private ToolkitToggle legacyFullscreenToggle;
    private ToolkitSlider legacyVolumeSlider;
    private ToolkitLabel legacyVolumeValue;
    private ToolkitLabel legacyStatusLabel;

    private void OnEnable()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        if (TryEnableUguiView())
        {
            BindUguiEvents();
            LoadUguiSettings();
            ShowMainMenu();
            return;
        }

        BindLegacyToolkitView();
        LoadLegacySettings();
        ShowMainMenu();
    }

    private void OnDisable()
    {
        UnbindUguiEvents();
        UnbindLegacyEvents();
        PlayerPrefs.Save();
    }

    private bool TryEnableUguiView()
    {
        uguiView = GetComponentInChildren<MainMenuView>(true);
        if (uguiView == null)
        {
            GameObject prefab = uguiViewPrefab != null
                ? uguiViewPrefab
                : Resources.Load<GameObject>(DefaultViewResourcePath);
            if (prefab == null) return false;

            GameObject instance = Instantiate(prefab, transform);
            instance.name = prefab.name;
            uguiView = instance.GetComponent<MainMenuView>();
        }

        if (uguiView == null) return false;

        legacyDocument = GetComponent<UIDocument>();
        if (legacyDocument != null) legacyDocument.enabled = false;
        EnsureEventSystem();
        return true;
    }

    private void BindUguiEvents()
    {
        if (uguiView.PlayButton != null) uguiView.PlayButton.onClick.AddListener(StartGame);
        if (uguiView.SettingsButton != null)
            uguiView.SettingsButton.onClick.AddListener(ShowSettings);
        if (uguiView.QuitButton != null) uguiView.QuitButton.onClick.AddListener(QuitGame);
        if (uguiView.SettingsBackButton != null)
            uguiView.SettingsBackButton.onClick.AddListener(ShowMainMenu);
        if (uguiView.FullscreenToggle != null)
            uguiView.FullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
        if (uguiView.VolumeSlider != null)
            uguiView.VolumeSlider.onValueChanged.AddListener(OnVolumeChanged);
    }

    private void UnbindUguiEvents()
    {
        if (uguiView == null) return;
        if (uguiView.PlayButton != null) uguiView.PlayButton.onClick.RemoveListener(StartGame);
        if (uguiView.SettingsButton != null)
            uguiView.SettingsButton.onClick.RemoveListener(ShowSettings);
        if (uguiView.QuitButton != null) uguiView.QuitButton.onClick.RemoveListener(QuitGame);
        if (uguiView.SettingsBackButton != null)
            uguiView.SettingsBackButton.onClick.RemoveListener(ShowMainMenu);
        if (uguiView.FullscreenToggle != null)
            uguiView.FullscreenToggle.onValueChanged.RemoveListener(OnFullscreenChanged);
        if (uguiView.VolumeSlider != null)
            uguiView.VolumeSlider.onValueChanged.RemoveListener(OnVolumeChanged);
    }

    private void LoadUguiSettings()
    {
        bool fullscreen = PlayerPrefs.GetInt(
            FullscreenPreferenceKey,
            Screen.fullScreen ? 1 : 0) != 0;
        float volume = PlayerPrefs.GetFloat(VolumePreferenceKey, AudioListener.volume);
        volume = Mathf.Clamp01(volume);

        if (uguiView.FullscreenToggle != null) uguiView.FullscreenToggle.isOn = fullscreen;
        if (uguiView.VolumeSlider != null) uguiView.VolumeSlider.value = volume * 100f;
        uguiView.SetVolumeValue(volume * 100f);
        Screen.fullScreen = fullscreen;
        AudioListener.volume = volume;
    }

    private void BindLegacyToolkitView()
    {
        legacyDocument = GetComponent<UIDocument>();
        if (legacyDocument == null) return;
        legacyDocument.enabled = true;

        ToolkitVisualElement root = legacyDocument.rootVisualElement;
        legacyPlayButton = root.Q<ToolkitButton>("play-button");
        legacySettingsButton = root.Q<ToolkitButton>("settings-button");
        legacyQuitButton = root.Q<ToolkitButton>("quit-button");
        legacyBackButton = root.Q<ToolkitButton>("settings-back-button");
        legacyMainPanel = root.Q<ToolkitVisualElement>("main-panel");
        legacySettingsPanel = root.Q<ToolkitVisualElement>("settings-panel");
        legacyFullscreenToggle = root.Q<ToolkitToggle>("fullscreen-toggle");
        legacyVolumeSlider = root.Q<ToolkitSlider>("volume-slider");
        legacyVolumeValue = root.Q<ToolkitLabel>("volume-value");
        legacyStatusLabel = root.Q<ToolkitLabel>("status-label");

        if (legacyPlayButton != null) legacyPlayButton.clicked += StartGame;
        if (legacySettingsButton != null) legacySettingsButton.clicked += ShowSettings;
        if (legacyQuitButton != null) legacyQuitButton.clicked += QuitGame;
        if (legacyBackButton != null) legacyBackButton.clicked += ShowMainMenu;
        if (legacyFullscreenToggle != null)
            legacyFullscreenToggle.RegisterValueChangedCallback(OnLegacyFullscreenChanged);
        if (legacyVolumeSlider != null)
            legacyVolumeSlider.RegisterValueChangedCallback(OnLegacyVolumeChanged);
    }

    private void UnbindLegacyEvents()
    {
        if (legacyPlayButton != null) legacyPlayButton.clicked -= StartGame;
        if (legacySettingsButton != null) legacySettingsButton.clicked -= ShowSettings;
        if (legacyQuitButton != null) legacyQuitButton.clicked -= QuitGame;
        if (legacyBackButton != null) legacyBackButton.clicked -= ShowMainMenu;
        if (legacyFullscreenToggle != null)
            legacyFullscreenToggle.UnregisterValueChangedCallback(OnLegacyFullscreenChanged);
        if (legacyVolumeSlider != null)
            legacyVolumeSlider.UnregisterValueChangedCallback(OnLegacyVolumeChanged);
    }

    private void LoadLegacySettings()
    {
        bool fullscreen = PlayerPrefs.GetInt(
            FullscreenPreferenceKey,
            Screen.fullScreen ? 1 : 0) != 0;
        float volume = Mathf.Clamp01(
            PlayerPrefs.GetFloat(VolumePreferenceKey, AudioListener.volume));
        if (legacyFullscreenToggle != null) legacyFullscreenToggle.value = fullscreen;
        if (legacyVolumeSlider != null) legacyVolumeSlider.value = volume * 100f;
        UpdateLegacyVolumeLabel(volume * 100f);
        Screen.fullScreen = fullscreen;
        AudioListener.volume = volume;
    }

    private void StartGame()
    {
        if (!Application.CanStreamedLevelBeLoaded(gameplaySceneName))
        {
            SetStatus("GAMEPLAY SCENE NOT IN BUILD");
            Debug.LogError("Main menu could not load scene: " + gameplaySceneName);
            return;
        }

        Time.timeScale = 1f;
        if (uguiView != null && uguiView.PlayButton != null)
            uguiView.PlayButton.interactable = false;
        if (legacyPlayButton != null)
        {
            legacyPlayButton.SetEnabled(false);
            legacyPlayButton.text = "LOADING CAVES...";
        }
        SetStatus("DESCENT SEQUENCE STARTED");
        SceneManager.LoadSceneAsync(gameplaySceneName, LoadSceneMode.Single);
    }

    private void ShowSettings()
    {
        if (uguiView != null) uguiView.ShowSettingsPanel();
        if (legacyMainPanel != null) legacyMainPanel.EnableInClassList("is-hidden", true);
        if (legacySettingsPanel != null)
            legacySettingsPanel.EnableInClassList("is-visible", true);
    }

    private void ShowMainMenu()
    {
        if (uguiView != null) uguiView.ShowMainPanel();
        if (legacyMainPanel != null) legacyMainPanel.EnableInClassList("is-hidden", false);
        if (legacySettingsPanel != null)
            legacySettingsPanel.EnableInClassList("is-visible", false);
        SetStatus("SYSTEMS READY");
    }

    private void OnFullscreenChanged(bool value)
    {
        ApplyFullscreen(value);
    }

    private void OnLegacyFullscreenChanged(ChangeEvent<bool> evt)
    {
        ApplyFullscreen(evt.newValue);
    }

    private static void ApplyFullscreen(bool value)
    {
        Screen.fullScreen = value;
        PlayerPrefs.SetInt(FullscreenPreferenceKey, value ? 1 : 0);
    }

    private void OnVolumeChanged(float value)
    {
        ApplyVolume(value);
    }

    private void OnLegacyVolumeChanged(ChangeEvent<float> evt)
    {
        ApplyVolume(evt.newValue);
    }

    private void ApplyVolume(float value)
    {
        float normalized = Mathf.Clamp01(value / 100f);
        AudioListener.volume = normalized;
        PlayerPrefs.SetFloat(VolumePreferenceKey, normalized);
        if (uguiView != null) uguiView.SetVolumeValue(value);
        UpdateLegacyVolumeLabel(value);
    }

    private void UpdateLegacyVolumeLabel(float value)
    {
        if (legacyVolumeValue != null)
            legacyVolumeValue.text = Mathf.RoundToInt(value).ToString("00") + "%";
    }

    private void SetStatus(string message)
    {
        if (uguiView != null) uguiView.SetStatus(message);
        if (legacyStatusLabel != null) legacyStatusLabel.text = message;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
