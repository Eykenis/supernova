using System.Collections;
using Supernova.Gameplay;
using Supernova.Infrastructure;
using Supernova.Missions;
using Supernova.UI;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using ToolkitButton = UnityEngine.UIElements.Button;
using ToolkitLabel = UnityEngine.UIElements.Label;
using ToolkitSlider = UnityEngine.UIElements.Slider;
using ToolkitToggle = UnityEngine.UIElements.Toggle;
using ToolkitVisualElement = UnityEngine.UIElements.VisualElement;

[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class MainMenuController : MonoBehaviour
{
    private const string FullscreenPreferenceKey = "ui.fullscreen";
    private const string VolumePreferenceKey = "ui.master-volume";

    [Header("Scene references")]
    [SerializeField] private MainMenuView uguiView;
    [SerializeField] private PerspectiveCameraController perspectiveCamera;
    [SerializeField] private Animator menuCharacterAnimator;
    [SerializeField] private PlayerToolController playerToolController;

    [Header("Home presentation")]
    [SerializeField, Min(0.05f)] private float cameraTransitionSeconds = 1.65f;
    [SerializeField, Min(0.05f)] private float menuFadeSeconds = 0.55f;
    [SerializeField, Range(0f, 1f)] private float firstPersonVisibilityPoint = 0.5f;
    [SerializeField, Range(15f, 120f)] private float menuFieldOfView = 42f;
    [SerializeField] private Vector3 menuCameraOffset = new Vector3(0.3f, 1.2f, 1f);
    [SerializeField] private Vector3 menuLookTargetOffset = new Vector3(-0.5f, 0.5f, 0f);

    [Header("Menu character")]
    [SerializeField] private AnimationClip menuIdleAnimation;
    [SerializeField, Min(0f)] private float menuIdlePlaybackSpeed = 1f;
    [SerializeField] private bool loopMenuIdleAnimation = true;
    [SerializeField] private bool applyMenuIdleFootIk = true;

    private static MainMenuController activeIntegratedMenu;

    private CanvasGroup menuCanvasGroup;
    private UIDocument legacyDocument;
    private ToolkitButton legacyPlayButton;
    private ToolkitButton legacyTutorialButton;
    private ToolkitButton legacySettingsButton;
    private ToolkitButton legacyQuitButton;
    private ToolkitButton legacyBackButton;
    private ToolkitVisualElement legacyMainPanel;
    private ToolkitVisualElement legacySettingsPanel;
    private ToolkitToggle legacyFullscreenToggle;
    private ToolkitSlider legacyVolumeSlider;
    private ToolkitLabel legacyVolumeValue;
    private ToolkitLabel legacyStatusLabel;
    private Camera controlledCamera;
    private Transform playerRoot;
    private bool integratedHomeMode;
    private bool transitionStarted;
    private bool transitionCompleted;
    private bool firstPersonVisibilityStarted;
    private float transitionStartTime;
    private Vector3 transitionStartPosition;
    private Quaternion transitionStartRotation = Quaternion.identity;
    private float transitionStartFieldOfView;
    private float gameplayFieldOfView;
    private bool hasGameplayFieldOfView;
    private PlayableGraph menuIdleGraph;
    private MainMenuCharacterOverlay characterOverlay;
    private CanvasGroup tutorialSceneFade;
    private string tutorialSceneName;
    private bool tutorialTransition;
    private bool tutorialSceneLoadStarted;

    public static bool IsIntegratedMenuActive =>
        activeIntegratedMenu != null
        && activeIntegratedMenu.isActiveAndEnabled
        && !activeIntegratedMenu.transitionCompleted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        activeIntegratedMenu = null;
    }

    public static bool IsIntegratedHomeScene(Scene scene)
    {
        LevelConfiguration level = GameAssetCatalog.Current != null
            ? GameAssetCatalog.Current.Missions.DefaultLevel
            : null;
        return scene.IsValid()
            && level != null
            && scene.name == level.HomeSceneName;
    }

    private void OnEnable()
    {
        if (!Application.isPlaying)
            return;

        integratedHomeMode = IsIntegratedHomeScene(gameObject.scene.IsValid()
            ? gameObject.scene
            : SceneManager.GetActiveScene());
        if (integratedHomeMode)
        {
            activeIntegratedMenu = this;
            Time.timeScale = 1f;
        }

        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        if (TryEnableUguiView())
        {
            BindUguiEvents();
            LoadUguiSettings();
            ShowMainMenu();
        }
        else
        {
            BindLegacyToolkitView();
            LoadLegacySettings();
            ShowMainMenu();
        }

        if (integratedHomeMode)
        {
            PrepareIntegratedHomePresentation();
            StartCoroutine(RefreshIntegratedPresentationNextFrame());
        }
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
            return;

        UnbindUguiEvents();
        UnbindLegacyEvents();
        StopMenuIdleAnimation();
        characterOverlay?.StopOverlay();
        SetEquippedToolHidden(false);
        PlayerPrefs.Save();

        if (!transitionCompleted && tutorialSceneFade != null)
        {
            tutorialSceneFade.alpha = 0f;
            tutorialSceneFade.gameObject.SetActive(false);
        }

        if (activeIntegratedMenu != this)
            return;

        activeIntegratedMenu = null;
        if (!transitionCompleted)
        {
            SetGameplayPresentationActive(true);
            perspectiveCamera?.SetMenuPresentationActive(false);
            RestoreGameplayFieldOfView();
        }
    }

    private IEnumerator RefreshIntegratedPresentationNextFrame()
    {
        yield return null;
        if (!transitionCompleted && activeIntegratedMenu == this)
        {
            SetGameplayPresentationActive(false);
            ResolveCameraReferences();
            SetEquippedToolHidden(true);
            StartMenuIdleAnimation();
        }
    }

    private bool TryEnableUguiView()
    {
        if (uguiView == null)
            uguiView = GetComponentInChildren<MainMenuView>(true);
        if (uguiView == null)
        {
            Debug.LogError(
                "Home Main Menu must contain a MainMenuView in the scene.",
                this);
            return false;
        }

        legacyDocument = GetComponent<UIDocument>();
        if (legacyDocument != null) legacyDocument.enabled = false;
        GameHudController.EnsureSingleEventSystem(transform);
        return true;
    }

    private void PrepareIntegratedHomePresentation()
    {
        if (uguiView != null)
        {
            menuCanvasGroup = uguiView.PrepareHomePresentation();
            if (menuCanvasGroup != null)
            {
                menuCanvasGroup.alpha = 1f;
                menuCanvasGroup.interactable = true;
                menuCanvasGroup.blocksRaycasts = true;
            }
        }

        SetGameplayPresentationActive(false);
        ResolveCameraReferences();
        perspectiveCamera?.SetMenuPresentationActive(true);
        SetEquippedToolHidden(true);
        StartMenuIdleAnimation();
        ApplyMenuFieldOfView();
        characterOverlay = uguiView != null ? uguiView.CharacterOverlay : null;
        characterOverlay?.Begin(playerRoot, controlledCamera);
    }

    private void ResolveCameraReferences()
    {
        if (perspectiveCamera == null)
            perspectiveCamera = FindObjectOfType<PerspectiveCameraController>(true);
        if (perspectiveCamera != null)
        {
            controlledCamera = perspectiveCamera.ControlledCamera;
            playerRoot = perspectiveCamera.PlayerRoot;
            perspectiveCamera.SetMenuPresentationActive(true);
        }

        if (playerRoot == null)
        {
            Supernova.Voxels.VoxelPlayerController player =
                FindObjectOfType<Supernova.Voxels.VoxelPlayerController>(true);
            if (player != null) playerRoot = player.transform;
        }
        if (controlledCamera == null)
            controlledCamera = Camera.main;

        if (playerRoot != null)
        {
            Supernova.Voxels.VoxelPlayerController player =
                playerRoot.GetComponent<Supernova.Voxels.VoxelPlayerController>();
            if (menuCharacterAnimator == null && player != null)
                menuCharacterAnimator = player.CharacterAnimator;
            if (menuCharacterAnimator == null)
                menuCharacterAnimator = playerRoot.GetComponentInChildren<Animator>(true);
            if (playerToolController == null)
                playerToolController = playerRoot.GetComponent<PlayerToolController>();
        }

        if (!hasGameplayFieldOfView && controlledCamera != null)
        {
            gameplayFieldOfView = controlledCamera.fieldOfView;
            hasGameplayFieldOfView = true;
        }
    }

    private void SetEquippedToolHidden(bool hidden)
    {
        if (playerToolController == null && playerRoot != null)
            playerToolController = playerRoot.GetComponent<PlayerToolController>();
        playerToolController?.SetEquippedToolModelHidden(hidden);
    }

    private void ApplyMenuFieldOfView()
    {
        if (controlledCamera != null)
            controlledCamera.fieldOfView = Mathf.Clamp(menuFieldOfView, 15f, 120f);
    }

    private void RestoreGameplayFieldOfView()
    {
        if (controlledCamera != null && hasGameplayFieldOfView)
            controlledCamera.fieldOfView = gameplayFieldOfView;
    }

    private void StartMenuIdleAnimation()
    {
        if (menuIdleGraph.IsValid()
            || menuCharacterAnimator == null
            || menuIdleAnimation == null)
        {
            return;
        }

        menuIdleGraph = PlayableGraph.Create(name + " Menu Idle");
        menuIdleGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        AnimationClipPlayable idlePlayable =
            AnimationClipPlayable.Create(menuIdleGraph, menuIdleAnimation);
        idlePlayable.SetApplyFootIK(applyMenuIdleFootIk);
        idlePlayable.SetOverrideLoopTime(true);
        idlePlayable.SetLoopTime(loopMenuIdleAnimation);
        idlePlayable.SetSpeed(Mathf.Max(0f, menuIdlePlaybackSpeed));
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(
            menuIdleGraph,
            "Menu Character Idle",
            menuCharacterAnimator);
        output.SetSourcePlayable(idlePlayable);
        menuIdleGraph.Play();
    }

    private void StopMenuIdleAnimation()
    {
        if (!menuIdleGraph.IsValid())
            return;

        menuIdleGraph.Destroy();
        menuIdleGraph = default;
    }

    private void LateUpdate()
    {
        if (!integratedHomeMode
            || transitionCompleted
            || activeIntegratedMenu != this)
        {
            return;
        }

        ResolveCameraReferences();
        SetEquippedToolHidden(true);
        if (controlledCamera == null || playerRoot == null)
        {
            if (transitionStarted
                && Time.unscaledTime - transitionStartTime >= cameraTransitionSeconds)
            {
                UpdateTutorialFade(1f);
                CompleteRequestedTransition();
            }
            return;
        }

        Transform cameraTransform = controlledCamera.transform;
        Vector3 gameplayPosition = cameraTransform.position;
        Quaternion gameplayRotation = cameraTransform.rotation;
        GetMenuCameraPose(out Vector3 menuPosition, out Quaternion menuRotation);

        if (!transitionStarted)
        {
            cameraTransform.SetPositionAndRotation(menuPosition, menuRotation);
            ApplyMenuFieldOfView();
            characterOverlay?.SyncWithSourceCamera();
            return;
        }

        float elapsed = Time.unscaledTime - transitionStartTime;
        float cameraProgress = Mathf.Clamp01(
            elapsed / Mathf.Max(0.05f, cameraTransitionSeconds));
        float easedCameraProgress = SmoothStep(cameraProgress);
        UpdateTutorialFade(easedCameraProgress);
        if (!firstPersonVisibilityStarted
            && cameraProgress >= firstPersonVisibilityPoint)
        {
            firstPersonVisibilityStarted = true;
            perspectiveCamera?.SetMenuFirstPersonVisibilityActive(true);
        }
        cameraTransform.SetPositionAndRotation(
            Vector3.Lerp(
                transitionStartPosition,
                gameplayPosition,
                easedCameraProgress),
            Quaternion.Slerp(
                transitionStartRotation,
                gameplayRotation,
                easedCameraProgress));
        if (hasGameplayFieldOfView)
        {
            controlledCamera.fieldOfView = Mathf.Lerp(
                transitionStartFieldOfView,
                gameplayFieldOfView,
                cameraProgress);
        }
        characterOverlay?.SyncWithSourceCamera();

        if (menuCanvasGroup != null)
        {
            float fadeProgress = Mathf.Clamp01(
                elapsed / Mathf.Max(0.05f, menuFadeSeconds));
            menuCanvasGroup.alpha = 1f - SmoothStep(fadeProgress);
        }

        if (cameraProgress >= 1f)
            CompleteRequestedTransition();
    }

    private void GetMenuCameraPose(
        out Vector3 position,
        out Quaternion rotation)
    {
        position = playerRoot.TransformPoint(menuCameraOffset);
        Vector3 target = playerRoot.TransformPoint(menuLookTargetOffset);
        Vector3 direction = target - position;
        rotation = direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction.normalized, playerRoot.up)
            : playerRoot.rotation;
    }

    private static float SmoothStep(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private void BeginIntegratedTransition()
    {
        if (transitionStarted || transitionCompleted)
            return;

        transitionStarted = true;
        firstPersonVisibilityStarted = false;
        transitionStartTime = Time.unscaledTime;
        StopMenuIdleAnimation();
        if (controlledCamera != null)
        {
            transitionStartPosition = controlledCamera.transform.position;
            transitionStartRotation = controlledCamera.transform.rotation;
            transitionStartFieldOfView = controlledCamera.fieldOfView;
        }
        if (menuCanvasGroup != null)
        {
            menuCanvasGroup.interactable = false;
            menuCanvasGroup.blocksRaycasts = false;
        }
        if (uguiView != null && uguiView.PlayButton != null)
            uguiView.PlayButton.interactable = false;
        if (uguiView != null && uguiView.TutorialButton != null)
            uguiView.TutorialButton.interactable = false;
        SetStatus(tutorialTransition
            ? "LOADING TUTORIAL..."
            : "ENTERING HOME BASE...");
    }

    private void CompleteRequestedTransition()
    {
        if (tutorialTransition)
            CompleteTutorialTransition();
        else
            CompleteIntegratedTransition();
    }

    private void UpdateTutorialFade(float progress)
    {
        if (!tutorialTransition || tutorialSceneFade == null)
            return;
        tutorialSceneFade.alpha = Mathf.Clamp01(progress);
    }

    private bool PrepareTutorialFade()
    {
        MissionGameLoop gameLoop = MissionGameLoop.Instance;
        tutorialSceneFade = gameLoop != null
            ? gameLoop.PrepareSceneFadeFromTransparent()
            : null;
        return tutorialSceneFade != null;
    }

    private void CompleteTutorialTransition()
    {
        if (tutorialSceneLoadStarted || transitionCompleted)
            return;

        MissionGameLoop gameLoop = MissionGameLoop.Instance;
        if (gameLoop == null
            || !gameLoop.BeginSceneLoadFromBlack(tutorialSceneName))
        {
            Debug.LogError(
                "Main menu could not begin the tutorial scene transition: "
                + tutorialSceneName,
                this);
            return;
        }

        tutorialSceneLoadStarted = true;
        transitionCompleted = true;
        UpdateTutorialFade(1f);
        activeIntegratedMenu = null;
        StopMenuIdleAnimation();
        characterOverlay?.StopOverlay();
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;
        Destroy(gameObject);
    }

    private void CompleteIntegratedTransition()
    {
        if (transitionCompleted)
            return;

        transitionCompleted = true;
        activeIntegratedMenu = null;
        if (menuCanvasGroup != null)
            menuCanvasGroup.alpha = 0f;
        StopMenuIdleAnimation();
        characterOverlay?.StopOverlay();
        perspectiveCamera?.SetMenuPresentationActive(false);
        RestoreGameplayFieldOfView();
        SetEquippedToolHidden(false);
        SetGameplayPresentationActive(true);
        GameHudController.BlockGameplayInputAfterModalClose();
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;
        Destroy(gameObject);
    }

    private static void SetGameplayPresentationActive(bool active)
    {
        GameHudController[] controllers =
            FindObjectsOfType<GameHudController>(true);
        for (int i = 0; i < controllers.Length; i++)
        {
            if (controllers[i] != null)
                controllers[i].SetMainMenuPresentationActive(!active);
        }

        SpawnPointIndicator[] spawnIndicators =
            FindObjectsOfType<SpawnPointIndicator>(true);
        for (int i = 0; i < spawnIndicators.Length; i++)
        {
            if (spawnIndicators[i] != null)
                spawnIndicators[i].enabled = active;
        }
    }

    private void BindUguiEvents()
    {
        if (uguiView.PlayButton != null) uguiView.PlayButton.onClick.AddListener(StartGame);
        if (uguiView.TutorialButton != null)
            uguiView.TutorialButton.onClick.AddListener(StartTutorial);
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
        if (uguiView.TutorialButton != null)
            uguiView.TutorialButton.onClick.RemoveListener(StartTutorial);
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
        legacyTutorialButton = root.Q<ToolkitButton>("tutorial-button");
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
        if (legacyTutorialButton != null)
            legacyTutorialButton.clicked += StartTutorial;
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
        if (legacyTutorialButton != null)
            legacyTutorialButton.clicked -= StartTutorial;
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
        if (integratedHomeMode)
        {
            BeginIntegratedTransition();
            return;
        }

        string gameplaySceneName = GameAssetCatalog.Current != null
            && GameAssetCatalog.Current.Missions.DefaultLevel != null
                ? GameAssetCatalog.Current.Missions.DefaultLevel.CaveSceneName
                : string.Empty;

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
            legacyPlayButton.text = "加载中……";
        }
        SetStatus("准备启动探险任务……");
        MissionGameLoop gameLoop = FindObjectOfType<MissionGameLoop>(true);
        if (gameLoop == null || !gameLoop.BeginFirstMission())
        {
            Debug.LogWarning(
                "Mission game loop was unavailable; loading gameplay without "
                + "the mission transition.");
            SceneManager.LoadSceneAsync(gameplaySceneName, LoadSceneMode.Single);
        }
    }

    private void StartTutorial()
    {
        if (transitionStarted || transitionCompleted)
            return;

        string tutorialSceneName = GameAssetCatalog.Current != null
            ? GameAssetCatalog.Current.SceneLookups.TutorialSceneName
            : string.Empty;
        if (!Application.CanStreamedLevelBeLoaded(tutorialSceneName))
        {
            SetStatus("新手教程场景未加入构建");
            Debug.LogError(
                "Main menu could not load tutorial scene: "
                + tutorialSceneName);
            return;
        }

        Time.timeScale = 1f;
        if (integratedHomeMode)
        {
            this.tutorialSceneName = tutorialSceneName;
            tutorialTransition = true;
            tutorialSceneLoadStarted = false;
            if (!PrepareTutorialFade())
            {
                tutorialTransition = false;
                this.tutorialSceneName = string.Empty;
                SetStatus("TUTORIAL FADE UI UNAVAILABLE");
                Debug.LogError(
                    "Main menu could not find the persistent scene fade UI.",
                    this);
                return;
            }

            BeginIntegratedTransition();
            return;
        }

        if (uguiView != null && uguiView.TutorialButton != null)
            uguiView.TutorialButton.interactable = false;
        if (legacyTutorialButton != null)
            legacyTutorialButton.SetEnabled(false);
        SetStatus("正在载入新手教程…");
        SceneManager.LoadSceneAsync(tutorialSceneName, LoadSceneMode.Single);
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

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
