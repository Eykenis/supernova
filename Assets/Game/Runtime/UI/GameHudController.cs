using System;
using System.Collections.Generic;
using Supernova.Gameplay;
using Supernova.Infrastructure;
using Supernova.MinecraftCaves;
using Supernova.Missions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace Supernova.UI
{
    /// <summary>
    /// Runtime game HUD implemented with UGUI. The InfiniteCaves scene contains an editable
    /// Canvas hierarchy, while other scenes can still create the same default HUD at runtime.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class GameHudController : MonoBehaviour
    {
        [Header("Data Source")]
        [SerializeField] private MonoBehaviour healthSourceOverride;
        [SerializeField] private PlayerToolController inventorySourceOverride;
        [SerializeField, Min(0.05f)] private float sourceSearchInterval = 0.5f;

        [Header("Configuration")]
        [SerializeField] private UiDesignTokens designTokens;

        [Header("UGUI View")]
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private Canvas crosshairCanvas;
        [SerializeField] private GameObject healthPanel;
        [SerializeField] private RectTransform healthFill;
        [SerializeField] private Image healthFillImage;
        [SerializeField] private TMP_Text healthValueLabel;
        [SerializeField] private GameObject hotbarRoot;
        [SerializeField] private HeadingCompass headingCompass;

        [Header("Mission View")]
        [SerializeField] private Canvas missionOverlayCanvas;
        [SerializeField] private MissionUiView missionView;
        [SerializeField] private GameObject missionTimerRoot;
        [SerializeField] private TMP_Text missionTimerValueLabel;

        [Header("Pause Menu")]
        [SerializeField] private Canvas pauseCanvas;
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button pauseSettingsButton;
        [SerializeField] private Button quitToMenuButton;
        [SerializeField] private Button quitToDesktopButton;
        [SerializeField] private GameObject pauseMainOptions;
        [SerializeField] private GameObject pauseSettingsPanel;
        [SerializeField] private Button pauseSettingsBackButton;
        [SerializeField] private Toggle pauseFullscreenToggle;
        [SerializeField] private Slider pauseVolumeSlider;
        [SerializeField] private TMP_Text pauseVolumeValueLabel;

        [Header("Equipment Menu")]
        [SerializeField] private EquipmentLoadoutMenu equipmentMenu;

        [Header("Loading View")]
        [SerializeField] private Canvas loadingCanvas;
        [SerializeField] private CanvasGroup loadingFadeGroup;
        [SerializeField] private CanvasGroup loadingContentGroup;
        [SerializeField] private GameObject loadingPanel;
        [SerializeField] private RectTransform loadingSpinner;
        [SerializeField] private RectTransform loadingFill;
        [SerializeField] private TMP_Text loadingStatusLabel;
        [SerializeField] private TMP_Text loadingProgressLabel;
        [SerializeField, Min(1f)] private float loadingSpinnerDegreesPerSecond = 140f;
        [SerializeField, Min(0.05f)] private float loadingFadeDuration = 0.35f;

        private IDamageable healthSource;
        private PlayerToolController inventorySource;
        private GameHudPresenter presenter;
        private HotbarPresenter hotbarPresenter;
        private float nextSourceSearchTime;
        private float nextInventorySourceSearchTime;
        private float nextWorldSourceSearchTime;
        private MinecraftCaveInfiniteWorld loadingSource;
        private bool loadingRequestedVisible;
        private float displayedCurrentHealth = float.NaN;
        private float displayedMaximumHealth = float.NaN;
        private int displayedSlotIndex = -1;
        private MinecraftCaveGenerationStage displayedLoadingStage =
            (MinecraftCaveGenerationStage)(-1);
        private int displayedLoadingPercent = -1;
        private bool pauseMenuOpen;
        private float timeScaleBeforePause = 1f;
        private CursorLockMode cursorLockBeforePause;
        private bool cursorVisibleBeforePause;
        private static GameHudController pauseOwner;
        private static GameHudController runtimeHud;
        private static int gameplayInputBlockedThroughFrame = -1;
        private PauseMenuPresentation pausePresentation;
        private AngledPanelGraphic[] healthSegments = new AngledPanelGraphic[0];
        private readonly Image[] hotbarSlotBackgrounds = new Image[PlayerInventory.SlotCount];
        private readonly Outline[] hotbarSlotOutlines = new Outline[PlayerInventory.SlotCount];
        private readonly TMP_Text[] hotbarItemLabels = new TMP_Text[PlayerInventory.SlotCount];
        private const string FullscreenPreferenceKey = "ui.fullscreen";
        private const string VolumePreferenceKey = "ui.master-volume";

        public Canvas RootCanvas => rootCanvas;
        public Canvas CrosshairCanvas => crosshairCanvas;
        public HeadingCompass Compass => headingCompass;
        public Canvas PauseCanvas => pauseCanvas;
        public Canvas LoadingCanvas => loadingCanvas;
        public Canvas MissionOverlayCanvas => missionOverlayCanvas;
        public MissionUiView MissionView => missionView;
        public TMP_Text MissionTimerValueLabel => missionTimerValueLabel;
        public UiDesignTokens DesignTokens => designTokens;
        public bool IsPauseMenuVisible => pausePanel != null && pausePanel.activeSelf;
        public EquipmentLoadoutMenu EquipmentMenu => equipmentMenu;
        public bool IsEquipmentMenuVisible =>
            equipmentMenu != null && equipmentMenu.IsOpen;
        public bool IsLoadingVisible => loadingPanel != null && loadingPanel.activeSelf;
        public IDamageable HealthSource =>
            IsHealthSourceValid(healthSource) ? healthSource : null;
        public PlayerToolController InventorySource => inventorySource;
        public static bool IsPauseMenuOpen => pauseOwner != null && pauseOwner.pauseMenuOpen;
        public static bool IsModalMenuOpen =>
            IsPauseMenuOpen || EquipmentLoadoutMenu.IsAnyOpen;
        public static bool IsGameplayInputBlocked =>
            IsModalMenuOpen || Time.frameCount <= gameplayInputBlockedThroughFrame;
        public bool CanPauseGame =>
            isActiveAndEnabled
            && !IsEquipmentMenuVisible
            && !IsMainMenuActive()
            && !MissionGameLoop.IsSceneTransitioning
            && !IsLoadingBlockingPause();
        public bool CanOpenEquipmentMenu =>
            isActiveAndEnabled
            && !pauseMenuOpen
            && !IsMainMenuActive()
            && !MissionGameLoop.IsSceneTransitioning
            && !IsLoadingBlockingPause();

        public MissionUiView GetOrCreateMissionView()
        {
            if (missionView == null || missionOverlayCanvas == null)
                EnsureView();
            return missionView;
        }

        public void SetMissionTimeRemaining(float timeRemainingSeconds)
        {
            if (missionTimerRoot == null || missionTimerValueLabel == null)
                EnsureView();
            if (missionTimerRoot == null || missionTimerValueLabel == null)
                return;

            bool configuredVisible =
                designTokens == null || designTokens.ShowMissionTimer;
            missionTimerRoot.SetActive(configuredVisible);
            if (!configuredVisible)
                return;

            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(timeRemainingSeconds));
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            missionTimerValueLabel.text =
                minutes.ToString("00") + ":" + seconds.ToString("00");
        }

        public void HideMissionTimer()
        {
            if (missionTimerRoot != null)
                missionTimerRoot.SetActive(false);
        }

        public void RegisterAsRuntimeHud()
        {
            if (runtimeHud != null && runtimeHud != this)
                runtimeHud.DisableDuplicateHud();
            runtimeHud = this;
            enabled = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            runtimeHud = null;
            pauseOwner = null;
            gameplayInputBlockedThroughFrame = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRuntimeHud()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            GameHudController existing = runtimeHud;
            if (existing == null)
            {
                foreach (GameHudController candidate in
                    FindObjectsOfType<GameHudController>(true))
                {
                    if (candidate != null && candidate.gameObject.scene.IsValid())
                    {
                        existing = candidate;
                        break;
                    }
                }
            }

            foreach (GameHudController candidate in
                FindObjectsOfType<GameHudController>(true))
            {
                if (candidate != null && candidate != existing)
                    candidate.DisableDuplicateHud();
            }

            string mainMenuSceneName = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.SceneLookups.MainMenuSceneName
                : string.Empty;
            if (scene.name == mainMenuSceneName)
            {
                if (existing != null)
                    existing.SetGameplayViewVisible(false);
                EnsureSingleEventSystem(existing != null ? existing.transform : null);
                return;
            }

            if (existing == null)
            {
                GameObject hudObject = new GameObject("Game HUD");
                DontDestroyOnLoad(hudObject);
                existing = hudObject.AddComponent<GameHudController>();
                existing.RegisterAsRuntimeHud();
            }
            else
            {
                existing.SetGameplayViewVisible(true);
            }

            EnsureSingleEventSystem(existing.transform);
        }


        private void Awake()
        {
            ResolveConfiguration();
            loadingSource = FindObjectOfType<MinecraftCaveInfiniteWorld>();
            EnsureView();
            BindHealthSource(healthSourceOverride as IDamageable);
            BindInventorySource(inventorySourceOverride);
        }

        private void OnEnable()
        {
            nextSourceSearchTime = 0f;
            nextInventorySourceSearchTime = 0f;
            nextWorldSourceSearchTime = 0f;
            if (inventorySource != null)
                BindInventorySource(inventorySource);
            RefreshNow();
        }

        private void OnDisable()
        {
            ResumeGame();
            equipmentMenu?.Close();
            if (inventorySource != null)
            {
                inventorySource.SelectionChanged -= HandleInventorySelectionChanged;
                inventorySource.LoadoutChanged -= HandleLoadoutChanged;
            }
        }

        private void OnDestroy()
        {
            if (runtimeHud == this)
                runtimeHud = null;
        }

        private void Update()
        {
            if (pauseMenuOpen && !CanPauseGame)
                ResumeGame();

            if (IsEquipmentMenuVisible && !CanOpenEquipmentMenu)
                equipmentMenu.Close();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (IsEquipmentMenuVisible)
                    equipmentMenu.Close();
                else
                    TogglePauseMenu();
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                ToggleEquipmentMenu();
            }

            if (!IsHealthSourceValid(healthSource)
                && Time.unscaledTime >= nextSourceSearchTime)
            {
                healthSource = null;
                nextSourceSearchTime = Time.unscaledTime + sourceSearchInterval;
                BindHealthSource(FindPlayerHealthSource());
            }

            if (inventorySource == null && Time.unscaledTime >= nextInventorySourceSearchTime)
            {
                nextInventorySourceSearchTime = Time.unscaledTime + sourceSearchInterval;
                BindInventorySource(FindPlayerInventorySource());
            }

            if (loadingSource == null && Time.unscaledTime >= nextWorldSourceSearchTime)
            {
                nextWorldSourceSearchTime = Time.unscaledTime + sourceSearchInterval;
                loadingSource = FindObjectOfType<MinecraftCaveInfiniteWorld>();
            }

            RefreshNow();
            AnimateLoading();
        }

        public void TogglePauseMenu()
        {
            if (pauseMenuOpen) ResumeGame();
            else if (CanPauseGame) PauseGame();
        }

        public void ToggleEquipmentMenu()
        {
            EnsureEquipmentMenu();
            if (equipmentMenu == null)
                return;

            if (equipmentMenu.IsOpen)
                equipmentMenu.Close();
            else if (CanOpenEquipmentMenu)
                equipmentMenu.Open();
        }

        internal void SetGameplayHudVisibleForModal(bool visible)
        {
            if (rootCanvas != null)
                rootCanvas.gameObject.SetActive(visible);
            if (crosshairCanvas != null)
            {
                crosshairCanvas.gameObject.SetActive(
                    visible && (designTokens == null || designTokens.ShowCrosshair));
            }
        }

        internal static void BlockGameplayInputAfterModalClose()
        {
            gameplayInputBlockedThroughFrame = Mathf.Max(
                gameplayInputBlockedThroughFrame,
                Time.frameCount + 1);
        }

        public void PauseGame()
        {
            if (pauseMenuOpen || !CanPauseGame) return;
            equipmentMenu?.Close();
            if (pausePanel == null || resumeButton == null)
            {
                CacheViewReferences();
                if (pausePanel == null || resumeButton == null)
                    BuildPauseView();
            }

            pauseMenuOpen = true;
            pauseOwner = this;
            pausePanel.SetActive(true);
            SetGameplayHudVisibleForModal(false);
            pausePresentation = pausePanel.GetComponent<PauseMenuPresentation>();
            if (pausePresentation == null)
                pausePresentation = pausePanel.AddComponent<PauseMenuPresentation>();
            ShowPauseMainOptions();
            LoadPauseSettings();
            pausePresentation.PlayIntro();
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);

            if (!Application.isPlaying) return;
            timeScaleBeforePause = Time.timeScale;
            cursorLockBeforePause = Cursor.lockState;
            cursorVisibleBeforePause = Cursor.visible;
            Time.timeScale = 0f;
            SetCursorState(CursorLockMode.None, true);
        }

        public void ResumeGame()
        {
            if (pausePresentation != null)
                pausePresentation.StopPresentation();
            if (pausePanel != null) pausePanel.SetActive(false);
            if (!pauseMenuOpen) return;

            pauseMenuOpen = false;
            if (pauseOwner == this) pauseOwner = null;
            SetGameplayHudVisibleForModal(true);
            if (!Application.isPlaying) return;
            BlockGameplayInputAfterModalClose();
            Time.timeScale = timeScaleBeforePause;
            SetCursorState(cursorLockBeforePause, cursorVisibleBeforePause);
            PlayerPrefs.Save();
        }

        private bool IsLoadingBlockingPause()
        {
            if (loadingSource != null && !loadingSource.IsInitialLoadComplete)
                return true;
            if (loadingRequestedVisible)
                return true;
            return loadingPanel != null
                && loadingPanel.activeInHierarchy
                && (loadingFadeGroup == null || loadingFadeGroup.alpha > 0.001f);
        }

        private static bool IsMainMenuActive()
        {
            Scene scene = SceneManager.GetActiveScene();
            string mainMenuSceneName = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.SceneLookups.MainMenuSceneName
                : string.Empty;
            if (scene.IsValid() && scene.name == mainMenuSceneName)
                return true;
            return FindObjectOfType<MainMenuController>(true) != null;
        }

        private void SetGameplayViewVisible(bool visible)
        {
            EnsureView();
            if (!visible)
            {
                ResumeGame();
                equipmentMenu?.Close();
            }

            if (rootCanvas != null)
                rootCanvas.gameObject.SetActive(visible);
            if (crosshairCanvas != null)
            {
                crosshairCanvas.gameObject.SetActive(
                    visible && (designTokens == null || designTokens.ShowCrosshair));
            }
            if (loadingCanvas != null)
                loadingCanvas.gameObject.SetActive(visible);
            if (pauseCanvas != null)
                pauseCanvas.gameObject.SetActive(visible);
            if (equipmentMenu != null && equipmentMenu.Canvas != null)
                equipmentMenu.Canvas.gameObject.SetActive(visible);

            if (!visible)
                return;

            loadingSource = FindObjectOfType<MinecraftCaveInfiniteWorld>();
            nextSourceSearchTime = 0f;
            nextInventorySourceSearchTime = 0f;
            nextWorldSourceSearchTime = 0f;
            IDamageable configuredHealthSource =
                healthSourceOverride as IDamageable;
            BindHealthSource(IsHealthSourceValid(configuredHealthSource)
                ? configuredHealthSource
                : FindPlayerHealthSource());
            RefreshNow();
        }

        public void BindLoadingSource(MinecraftCaveInfiniteWorld source)
        {
            loadingSource = source;
            nextWorldSourceSearchTime = 0f;
            displayedLoadingStage = (MinecraftCaveGenerationStage)(-1);
            displayedLoadingPercent = -1;
            RefreshLoadingView();
        }

        public void BindHealthSource(IDamageable source)
        {
            healthSource = source;
            displayedCurrentHealth = float.NaN;
            displayedMaximumHealth = float.NaN;
            RefreshNow();
        }

        public void BindInventorySource(PlayerToolController source)
        {
            if (inventorySource != null)
            {
                inventorySource.SelectionChanged -= HandleInventorySelectionChanged;
                inventorySource.LoadoutChanged -= HandleLoadoutChanged;
            }

            inventorySource = source;
            if (inventorySource != null)
            {
                inventorySource.SelectionChanged += HandleInventorySelectionChanged;
                inventorySource.LoadoutChanged += HandleLoadoutChanged;
            }
            equipmentMenu?.BindInventory(inventorySource);
            displayedSlotIndex = -1;
            RefreshNow();
        }

        public void RefreshNow()
        {
            if (!IsHealthSourceValid(healthSource))
                healthSource = null;

            if (presenter != null && healthSource == null)
            {
                presenter.SetHealthVisible(false);
            }
            else if (presenter != null)
            {
                presenter.SetHealthVisible(true);
                float current = Mathf.Max(0f, healthSource.CurrentHealth);
                float maximum = Mathf.Max(0.01f, healthSource.MaximumHealth);
                if (!Mathf.Approximately(current, displayedCurrentHealth)
                    || !Mathf.Approximately(maximum, displayedMaximumHealth))
                {
                    displayedCurrentHealth = current;
                    displayedMaximumHealth = maximum;
                    presenter.SetHealth(current, maximum);
                }
            }

            RefreshHotbar();
            RefreshLoadingView();
        }

        private void RefreshHotbar()
        {
            if (hotbarPresenter == null) return;
            int slotIndex = inventorySource != null ? inventorySource.SelectedSlotIndex : 0;
            hotbarPresenter.SetInventory(inventorySource);
            if (slotIndex == displayedSlotIndex) return;

            displayedSlotIndex = slotIndex;
            hotbarPresenter.SetSelectedSlot(slotIndex);
        }

        private void RefreshLoadingView()
        {
            if (loadingPanel == null)
            {
                return;
            }

            loadingRequestedVisible = loadingSource != null
                && !loadingSource.IsInitialLoadComplete;
            if (loadingRequestedVisible && !loadingPanel.activeSelf)
            {
                loadingPanel.SetActive(true);
                if (loadingFadeGroup != null) loadingFadeGroup.alpha = 1f;
                if (loadingContentGroup != null) loadingContentGroup.alpha = 0f;
                displayedLoadingStage = (MinecraftCaveGenerationStage)(-1);
                displayedLoadingPercent = -1;
            }
            else if (!Application.isPlaying && !loadingRequestedVisible)
            {
                if (loadingFadeGroup != null) loadingFadeGroup.alpha = 0f;
                loadingPanel.SetActive(false);
            }

            if (!loadingRequestedVisible)
            {
                return;
            }

            float progress = Mathf.Clamp01(loadingSource.InitialLoadProgress);
            if (loadingFill != null)
            {
                Vector2 anchorMax = loadingFill.anchorMax;
                anchorMax.x = progress;
                loadingFill.anchorMax = anchorMax;
            }
            MinecraftCaveGenerationStage stage = loadingSource.GenerationStage;
            if (loadingStatusLabel != null && stage != displayedLoadingStage)
            {
                displayedLoadingStage = stage;
                loadingStatusLabel.text = GetLoadingStageLabel(stage);
            }

            int progressPercent = Mathf.RoundToInt(progress * 100f);
            if (loadingProgressLabel != null
                && progressPercent != displayedLoadingPercent)
            {
                displayedLoadingPercent = progressPercent;
                loadingProgressLabel.SetText("{0}%", progressPercent);
            }
        }

        private void DisableDuplicateHud()
        {
            ResumeGame();
            if (rootCanvas != null)
                rootCanvas.gameObject.SetActive(false);
            if (crosshairCanvas != null)
                crosshairCanvas.gameObject.SetActive(false);
            if (pauseCanvas != null)
                pauseCanvas.gameObject.SetActive(false);
            if (loadingCanvas != null)
                loadingCanvas.gameObject.SetActive(false);
            if (missionOverlayCanvas != null)
                missionOverlayCanvas.gameObject.SetActive(false);
            enabled = false;
        }

        private void AnimateLoading()
        {
            UpdateLoadingFade(Time.unscaledDeltaTime);
            if (loadingSpinner == null || loadingPanel == null || !loadingPanel.activeSelf
                || !loadingRequestedVisible)
            {
                return;
            }

            loadingSpinner.Rotate(
                0f,
                0f,
                -loadingSpinnerDegreesPerSecond * Time.unscaledDeltaTime,
                Space.Self);
        }

        private void UpdateLoadingFade(float deltaTime)
        {
            if (loadingPanel == null || loadingFadeGroup == null || loadingContentGroup == null)
            {
                return;
            }

            float step = loadingFadeDuration > 0f
                ? Mathf.Max(0f, deltaTime) / loadingFadeDuration
                : 1f;
            if (loadingRequestedVisible)
            {
                if (!loadingPanel.activeSelf) loadingPanel.SetActive(true);
                loadingFadeGroup.alpha = 1f;
                loadingContentGroup.alpha = Mathf.MoveTowards(
                    loadingContentGroup.alpha, 1f, step);
                return;
            }

            if (!loadingPanel.activeSelf)
            {
                return;
            }

            loadingFadeGroup.alpha = Mathf.MoveTowards(
                loadingFadeGroup.alpha, 0f, step);
            if (loadingFadeGroup.alpha <= 0f)
            {
                loadingPanel.SetActive(false);
                loadingFadeGroup.alpha = 1f;
                loadingContentGroup.alpha = 0f;
            }
        }

        private static string GetLoadingStageLabel(MinecraftCaveGenerationStage stage)
        {
            switch (stage)
            {
                case MinecraftCaveGenerationStage.Terrain:
                    return "GENERATING TERRAIN";
                case MinecraftCaveGenerationStage.Structures:
                    return "PLACING STRUCTURES";
                case MinecraftCaveGenerationStage.Meshes:
                    return "BUILDING CAVE MESHES";
                case MinecraftCaveGenerationStage.Ready:
                    return "READY";
                default:
                    return "PREPARING WORLD";
            }
        }

        private void HandleInventorySelectionChanged(int slotIndex, PlayerInventoryItem item)
        {
            displayedSlotIndex = -1;
            RefreshHotbar();
        }

        private void HandleLoadoutChanged()
        {
            RefreshHotbar();
        }

        [ContextMenu("Rebuild Default UGUI View")]
        public void RebuildDefaultView()
        {
            ResolveConfiguration();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            BuildDefaultView();
            BuildMissionView();
            BuildLoadingView();
            BuildPauseView();
            EnsureEquipmentMenu();
            SciFiUiSkin.ApplyGameHud(transform);
            ApplyReferenceHudLayout();
            CreatePresenter();
            RefreshNow();
        }

        private void EnsureView()
        {
            ResolveConfiguration();
            CacheViewReferences();
            if (rootCanvas == null || crosshairCanvas == null || healthPanel == null || healthFill == null
                || healthFillImage == null || healthValueLabel == null)
            {
                BuildDefaultView();
            }
            else if (hotbarRoot == null
                || hotbarRoot.transform.childCount
                    != PlayerInventory.SlotCount)
            {
                BuildHotbarView((RectTransform)rootCanvas.transform);
            }
            if (headingCompass == null)
                BuildCompassView();
            bool missionViewNeedsUpgrade =
                transform.Find(UiHierarchyPaths.Mission.Timer) == null;
            if (missionView == null || missionOverlayCanvas == null
                || missionViewNeedsUpgrade)
            {
                BuildMissionView();
            }
            if (loadingCanvas == null || loadingFadeGroup == null || loadingContentGroup == null
                || loadingPanel == null || loadingSpinner == null
                || loadingFill == null || loadingStatusLabel == null
                || loadingProgressLabel == null)
            {
                BuildLoadingView();
            }

            bool pauseViewNeedsUpgrade =
                transform.Find(UiHierarchyPaths.Pause.FullSettings) == null
                || transform.Find(UiHierarchyPaths.Pause.FullQuitToMenu) == null
                || transform.Find(UiHierarchyPaths.Pause.FullQuitToDesktop) == null
                || transform.Find(UiHierarchyPaths.Pause.FullSettingsPanel) == null;
            if (pauseCanvas == null || pausePanel == null || resumeButton == null
                || pauseViewNeedsUpgrade)
            {
                BuildPauseView();
            }

            EnsureEquipmentMenu();

            BindPauseMenuButtons();
            SciFiUiSkin.ApplyGameHud(transform);
            ApplyReferenceHudLayout();
            CreatePresenter();
            RefreshLoadingView();
        }

        private void EnsureEquipmentMenu()
        {
            if (equipmentMenu == null)
                equipmentMenu = GetComponent<EquipmentLoadoutMenu>();
            if (equipmentMenu == null)
                equipmentMenu = gameObject.AddComponent<EquipmentLoadoutMenu>();

            equipmentMenu.Initialize(this, designTokens);
            equipmentMenu.BindInventory(inventorySource);
        }

        private void CacheViewReferences()
        {
            Transform hudCanvasTransform = transform.Find(UiHierarchyPaths.Hud.RootCanvas);
            if (rootCanvas == null && hudCanvasTransform != null)
                rootCanvas = hudCanvasTransform.GetComponent<Canvas>();

            Transform crosshairCanvasTransform = transform.Find(UiHierarchyPaths.Hud.CrosshairCanvas);
            if (crosshairCanvas == null && crosshairCanvasTransform != null)
                crosshairCanvas = crosshairCanvasTransform.GetComponent<Canvas>();
            Transform panel = transform.Find(UiHierarchyPaths.Hud.HealthPanel);
            if (healthPanel == null && panel != null) healthPanel = panel.gameObject;

            Transform fill = transform.Find(UiHierarchyPaths.Hud.HealthFill);
            if (healthFill == null && fill != null) healthFill = fill as RectTransform;
            if (healthFillImage == null && fill != null) healthFillImage = fill.GetComponent<Image>();

            Transform value = transform.Find(UiHierarchyPaths.Hud.HealthValue);
            if (healthValueLabel == null && value != null)
                healthValueLabel = value.GetComponent<TMP_Text>();

            Transform pauseCanvasTransform = transform.Find(UiHierarchyPaths.Pause.Canvas);
            if (pauseCanvas == null && pauseCanvasTransform != null)
                pauseCanvas = pauseCanvasTransform.GetComponent<Canvas>();

            Transform pausePanelTransform = transform.Find(UiHierarchyPaths.Pause.Panel);
            if (pausePanel == null && pausePanelTransform != null)
                pausePanel = pausePanelTransform.gameObject;
            if (pausePresentation == null && pausePanel != null)
                pausePresentation = pausePanel.GetComponent<PauseMenuPresentation>();

            Transform resume = transform.Find(UiHierarchyPaths.Pause.FullResume);
            if (resumeButton == null && resume != null)
                resumeButton = resume.GetComponent<Button>();
            CachePauseMenuReferences();

            Transform loadingCanvasTransform = transform.Find(UiHierarchyPaths.Loading.Canvas);
            if (loadingCanvas == null && loadingCanvasTransform != null)
                loadingCanvas = loadingCanvasTransform.GetComponent<Canvas>();

            Transform loadingPanelTransform = transform.Find(UiHierarchyPaths.Loading.Panel);
            if (loadingPanel == null && loadingPanelTransform != null)
                loadingPanel = loadingPanelTransform.gameObject;

            Transform loadingContent = transform.Find(UiHierarchyPaths.Loading.Content);
            if (loadingContentGroup == null && loadingContent != null)
                loadingContentGroup = loadingContent.GetComponent<CanvasGroup>();
            if (loadingFadeGroup == null && loadingPanelTransform != null)
                loadingFadeGroup = loadingPanelTransform.GetComponent<CanvasGroup>();

            Transform spinner = transform.Find(UiHierarchyPaths.Loading.Spinner);
            if (loadingSpinner == null && spinner != null)
                loadingSpinner = spinner as RectTransform;

            Transform loadingFillTransform = transform.Find(
                UiHierarchyPaths.Loading.ProgressFill);
            if (loadingFill == null && loadingFillTransform != null)
                loadingFill = loadingFillTransform as RectTransform;

            Transform loadingStatus = transform.Find(
                UiHierarchyPaths.Loading.Status);
            if (loadingStatusLabel == null && loadingStatus != null)
                loadingStatusLabel = loadingStatus.GetComponent<TMP_Text>();

            Transform loadingProgress = transform.Find(
                UiHierarchyPaths.Loading.Progress);
            if (loadingProgressLabel == null && loadingProgress != null)
                loadingProgressLabel = loadingProgress.GetComponent<TMP_Text>();

            Transform hotbar = transform.Find(UiHierarchyPaths.Hud.Hotbar);
            if (hotbarRoot == null && hotbar != null) hotbarRoot = hotbar.gameObject;

            Transform compass = transform.Find(UiHierarchyPaths.Hud.Compass);
            if (headingCompass == null && compass != null)
                headingCompass = compass.GetComponent<HeadingCompass>();
            if (headingCompass != null)
                headingCompass.Configure(null, designTokens);

            Transform missionRoot = transform.Find(UiHierarchyPaths.Mission.Root);
            if (missionView == null && missionRoot != null)
                missionView = missionRoot.GetComponent<MissionUiView>();

            Transform missionOverlay = transform.Find(
                UiHierarchyPaths.Mission.OverlayCanvas);
            if (missionOverlayCanvas == null && missionOverlay != null)
                missionOverlayCanvas = missionOverlay.GetComponent<Canvas>();

            Transform missionTimer = transform.Find(
                UiHierarchyPaths.Mission.Timer);
            if (missionTimerRoot == null && missionTimer != null)
                missionTimerRoot = missionTimer.gameObject;
            Transform missionTimerValue = transform.Find(
                UiHierarchyPaths.Mission.TimerValue);
            if (missionTimerValueLabel == null && missionTimerValue != null)
                missionTimerValueLabel =
                    missionTimerValue.GetComponent<TMP_Text>();

            if (hotbar == null) return;

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                Transform slot = hotbar.Find(UiHierarchyPaths.Hud.SlotName(i + 1));
                if (slot == null) continue;
                hotbarSlotBackgrounds[i] = slot.GetComponent<Image>();
                hotbarSlotOutlines[i] = slot.GetComponent<Outline>();
                Transform itemLabel = slot.Find(UiHierarchyPaths.Hud.Item);
                if (itemLabel != null) hotbarItemLabels[i] = itemLabel.GetComponent<TMP_Text>();
            }
        }

        private void CreatePresenter()
        {
            presenter = new GameHudPresenter(
                healthPanel,
                healthFill,
                healthFillImage,
                healthValueLabel,
                healthSegments,
                designTokens);
            hotbarPresenter = new HotbarPresenter(
                hotbarSlotBackgrounds,
                hotbarSlotOutlines,
                hotbarItemLabels,
                designTokens);
            displayedCurrentHealth = float.NaN;
            displayedMaximumHealth = float.NaN;
            displayedSlotIndex = -1;
        }

        private void BuildDefaultView()
        {
            RectTransform rootRect = CreateRect(UiHierarchyPaths.Hud.RootCanvas, transform);
            rootCanvas = rootRect.gameObject.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = designTokens != null
                ? designTokens.HudSortingOrder
                : 100;

            CanvasScaler scaler = rootRect.gameObject.AddComponent<CanvasScaler>();
            ApplyCanvasPolicy(rootRect.gameObject, scaler);

            RectTransform crosshairRoot = CreateRect(UiHierarchyPaths.Hud.CrosshairCanvas, transform);
            crosshairCanvas = crosshairRoot.gameObject.AddComponent<Canvas>();
            crosshairCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            crosshairCanvas.sortingOrder = designTokens != null
                ? designTokens.CrosshairSortingOrder
                : 101;
            crosshairCanvas.pixelPerfect = true;

            CanvasScaler crosshairScaler = crosshairRoot.gameObject.AddComponent<CanvasScaler>();
            crosshairScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            crosshairScaler.scaleFactor = 1f;
            crosshairScaler.referencePixelsPerUnit = 100f;

            RectTransform crosshair = CreateRect("Crosshair", crosshairRoot);
            SetAnchoredRect(crosshair, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f));
            CreateCrosshairBar("Horizontal", crosshair, new Vector2(18f, 2f));
            CreateCrosshairBar("Vertical", crosshair, new Vector2(2f, 18f));

            RectTransform panel = CreateRect("Health Panel", rootRect);
            SetAnchoredRect(panel, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-24f, -24f), new Vector2(260f, 64f));
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.04f, 0.055f, 0.078f, 0.82f);
            panelImage.raycastTarget = false;
            Outline panelOutline = panel.gameObject.AddComponent<Outline>();
            panelOutline.effectColor = new Color(1f, 1f, 1f, 0.18f);
            panelOutline.effectDistance = new Vector2(1f, -1f);
            panelOutline.useGraphicAlpha = false;
            healthPanel = panel.gameObject;

            RectTransform header = CreateRect("Header", panel);
            SetAnchoredRect(header, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(14f, -8f), new Vector2(-28f, 22f));

            TMP_Text title = CreateText("Title", header, "HEALTH", TextAlignmentOptions.Left);
            SetAnchoredRect((RectTransform)title.transform, new Vector2(0f, 0f), new Vector2(0.5f, 1f),
                new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);

            healthValueLabel = CreateText("Value", header, "100 / 100", TextAlignmentOptions.Right);
            SetAnchoredRect((RectTransform)healthValueLabel.transform, new Vector2(0.5f, 0f), new Vector2(1f, 1f),
                new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
            healthValueLabel.fontSize = 13f;
            healthValueLabel.color = new Color(0.86f, 0.89f, 0.92f, 1f);

            RectTransform track = CreateRect("Track", panel);
            SetAnchoredRect(track, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(14f, 12f), new Vector2(-28f, 14f));
            Image trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = new Color(0.165f, 0.18f, 0.204f, 1f);
            trackImage.raycastTarget = false;

            healthFill = CreateRect("Fill", track);
            healthFill.anchorMin = Vector2.zero;
            healthFill.anchorMax = Vector2.one;
            healthFill.pivot = new Vector2(0f, 0.5f);
            healthFill.offsetMin = Vector2.zero;
            healthFill.offsetMax = Vector2.zero;
            healthFillImage = healthFill.gameObject.AddComponent<Image>();
            healthFillImage.color = new Color(0.21f, 0.8f, 0.38f, 1f);
            healthFillImage.raycastTarget = false;

            BuildHotbarView(rootRect);
            BuildCompassView();
            healthPanel.SetActive(designTokens == null || designTokens.ShowHealth);
            hotbarRoot.SetActive(designTokens == null || designTokens.ShowHotbar);
            crosshairRoot.gameObject.SetActive(
                designTokens == null || designTokens.ShowCrosshair);
        }

        private void ResolveConfiguration()
        {
            if (designTokens == null && GameAssetCatalog.Current != null)
                designTokens = GameAssetCatalog.Current.UI.DesignTokens;
        }

        private void BuildMissionView()
        {
            Transform existingMission = transform.Find(UiHierarchyPaths.Mission.Root);
            if (existingMission != null)
            {
                if (Application.isPlaying) Destroy(existingMission.gameObject);
                else DestroyImmediate(existingMission.gameObject);
            }

            Transform existingOverlay = transform.Find(
                UiHierarchyPaths.Mission.OverlayCanvas);
            if (existingOverlay != null)
            {
                if (Application.isPlaying) Destroy(existingOverlay.gameObject);
                else DestroyImmediate(existingOverlay.gameObject);
            }

            RectTransform missionRoot = CreateRect(
                "Mission",
                (RectTransform)rootCanvas.transform);
            missionRoot.anchorMin = Vector2.zero;
            missionRoot.anchorMax = Vector2.one;
            missionRoot.offsetMin = Vector2.zero;
            missionRoot.offsetMax = Vector2.zero;

            TMP_Text objective = CreateText(
                "Objective",
                missionRoot,
                string.Empty,
                TextAlignmentOptions.TopLeft);
            SetAnchoredRect(
                (RectTransform)objective.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                designTokens != null
                    ? designTokens.MissionObjectivePosition
                    : new Vector2(30f, -30f),
                designTokens != null
                    ? designTokens.MissionObjectiveSize
                    : new Vector2(600f, 160f));
            objective.fontSize = designTokens != null
                ? designTokens.MissionObjectiveFontSize
                : 28f;
            objective.color = designTokens != null
                ? designTokens.TextPrimary
                : new Color(0.82f, 0.96f, 1f);
            objective.enableWordWrapping = true;
            objective.gameObject.SetActive(
                designTokens == null || designTokens.ShowMissionObjective);

            TMP_Text prompt = CreateText(
                "Prompt",
                missionRoot,
                string.Empty,
                TextAlignmentOptions.Bottom);
            SetAnchoredRect(
                (RectTransform)prompt.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                designTokens != null
                    ? designTokens.MissionPromptPosition
                    : new Vector2(0f, 112f),
                designTokens != null
                    ? designTokens.MissionPromptSize
                    : new Vector2(1100f, 70f));
            prompt.fontSize = designTokens != null
                ? designTokens.MissionPromptFontSize
                : 25f;
            prompt.color = designTokens != null
                ? designTokens.TextPrimary
                : new Color(0.82f, 0.96f, 1f);
            prompt.enableWordWrapping = true;
            prompt.gameObject.SetActive(
                designTokens == null || designTokens.ShowMissionPrompt);

            RectTransform timer = CreateRect("Mission Timer", missionRoot);
            SetAnchoredRect(
                timer,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                designTokens != null
                    ? designTokens.MissionTimerPosition
                    : new Vector2(0f, -92f),
                designTokens != null
                    ? designTokens.MissionTimerSize
                    : new Vector2(180f, 62f));
            missionTimerRoot = timer.gameObject;

            RectTransform timerRule = CreateRect("Rule", timer);
            SetAnchoredRect(
                timerRule,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -2f),
                new Vector2(112f, 2f));
            Image timerRuleImage = timerRule.gameObject.AddComponent<Image>();
            Color timerColor = designTokens != null
                ? designTokens.HudPrimary
                : new Color(0.96f, 0.98f, 1f, 1f);
            timerRuleImage.color =
                new Color(timerColor.r, timerColor.g, timerColor.b, 0.52f);
            timerRuleImage.raycastTarget = false;

            TMP_Text timerCaption = CreateText(
                "Caption",
                timer,
                "TIME REMAINING",
                TextAlignmentOptions.Center);
            SetAnchoredRect(
                (RectTransform)timerCaption.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -14f),
                new Vector2(180f, 16f));
            timerCaption.fontSize = 9f;
            timerCaption.characterSpacing = 7f;
            timerCaption.color =
                new Color(timerColor.r, timerColor.g, timerColor.b, 0.65f);

            missionTimerValueLabel = CreateText(
                "Value",
                timer,
                "00:00",
                TextAlignmentOptions.Center);
            SetAnchoredRect(
                (RectTransform)missionTimerValueLabel.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -35f),
                new Vector2(180f, 34f));
            missionTimerValueLabel.fontSize = designTokens != null
                ? designTokens.MissionTimerFontSize
                : 28f;
            missionTimerValueLabel.characterSpacing = 2f;
            missionTimerValueLabel.color = timerColor;
            timer.gameObject.SetActive(false);

            RectTransform overlayRoot = CreateRect(
                UiHierarchyPaths.Mission.OverlayCanvas,
                transform);
            missionOverlayCanvas = overlayRoot.gameObject.AddComponent<Canvas>();
            missionOverlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            missionOverlayCanvas.sortingOrder = designTokens != null
                ? designTokens.MissionOverlaySortingOrder
                : 900;
            CanvasScaler overlayScaler =
                overlayRoot.gameObject.AddComponent<CanvasScaler>();
            ApplyCanvasPolicy(overlayRoot.gameObject, overlayScaler);
            overlayRoot.gameObject.AddComponent<GraphicRaycaster>();

            RectTransform result = CreateRect("Mission Result", overlayRoot);
            result.anchorMin = Vector2.zero;
            result.anchorMax = Vector2.one;
            result.offsetMin = Vector2.zero;
            result.offsetMax = Vector2.zero;
            Image resultBackdrop = result.gameObject.AddComponent<Image>();
            resultBackdrop.color = designTokens != null
                ? designTokens.MissionResultBackdrop
                : new Color(0.015f, 0.025f, 0.035f, 0.96f);

            TMP_Text resultText = CreateText(
                "Result Text",
                result,
                string.Empty,
                TextAlignmentOptions.Center);
            RectTransform resultTextRect = (RectTransform)resultText.transform;
            resultTextRect.anchorMin = Vector2.zero;
            resultTextRect.anchorMax = Vector2.one;
            Vector2 resultPadding = designTokens != null
                ? designTokens.MissionResultPadding
                : new Vector2(200f, 100f);
            resultTextRect.offsetMin = resultPadding;
            resultTextRect.offsetMax = -resultPadding;
            resultText.fontSize = designTokens != null
                ? designTokens.MissionResultFontSize
                : 42f;
            resultText.color = designTokens != null
                ? designTokens.TextPrimary
                : new Color(0.82f, 0.96f, 1f);
            resultText.enableWordWrapping = true;
            result.gameObject.SetActive(false);

            RectTransform fadeRect = CreateRect("Scene Fade", overlayRoot);
            fadeRect.anchorMin = Vector2.zero;
            fadeRect.anchorMax = Vector2.one;
            fadeRect.offsetMin = Vector2.zero;
            fadeRect.offsetMax = Vector2.zero;
            Image fadeImage = fadeRect.gameObject.AddComponent<Image>();
            fadeImage.color = designTokens != null
                ? designTokens.SceneFadeColor
                : Color.black;
            CanvasGroup fade = fadeRect.gameObject.AddComponent<CanvasGroup>();
            fade.alpha = 0f;
            fade.blocksRaycasts = true;
            fadeRect.gameObject.SetActive(false);

            missionView = missionRoot.gameObject.AddComponent<MissionUiView>();
            missionView.Configure(
                objective,
                prompt,
                result.gameObject,
                resultText,
                fade,
                designTokens);
        }

        private void BuildLoadingView()
        {
            Transform existing = transform.Find(UiHierarchyPaths.Loading.Canvas);
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }

            RectTransform loadingRoot = CreateRect(UiHierarchyPaths.Loading.Canvas, transform);
            loadingCanvas = loadingRoot.gameObject.AddComponent<Canvas>();
            loadingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            loadingCanvas.sortingOrder = designTokens != null
                ? designTokens.LoadingSortingOrder
                : 1000;
            CanvasScaler scaler = loadingRoot.gameObject.AddComponent<CanvasScaler>();
            ApplyCanvasPolicy(loadingRoot.gameObject, scaler);

            Color overlayBackdrop = designTokens != null
                ? designTokens.LoadingBackdrop
                : new Color(0.025f, 0.028f, 0.035f, 1f);
            Color overlayPrimary = designTokens != null
                ? designTokens.OverlayPrimary
                : Color.white;
            Color overlaySecondary = designTokens != null
                ? designTokens.OverlaySecondary
                : new Color(1f, 1f, 1f, 0.58f);
            Color overlayDivider = designTokens != null
                ? designTokens.OverlayDivider
                : new Color(1f, 1f, 1f, 0.24f);
            RectTransform panel = CreateRect("Loading Panel", loadingRoot);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            Image background = panel.gameObject.AddComponent<Image>();
            background.color = overlayBackdrop;
            background.raycastTarget = true;
            loadingPanel = panel.gameObject;
            loadingFadeGroup = panel.gameObject.AddComponent<CanvasGroup>();
            loadingFadeGroup.alpha = 1f;

            RectTransform content = CreateRect("Content", panel);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            loadingContentGroup = content.gameObject.AddComponent<CanvasGroup>();
            loadingContentGroup.alpha = 0f;

            TMP_Text brand = CreateText(
                "Brand", content, "SUPERNOVA  /  DESCENT", TextAlignmentOptions.Center);
            SetAnchoredRect((RectTransform)brand.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 166f), new Vector2(620f, 30f));
            brand.fontSize = 13f;
            brand.characterSpacing = 5f;
            brand.color = overlaySecondary;

            TMP_Text title = CreateText(
                "Title", content, "LOADING", TextAlignmentOptions.Center);
            SetAnchoredRect((RectTransform)title.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 106f), new Vector2(620f, 54f));
            title.fontSize = 42f;
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 6f;
            title.color = overlayPrimary;

            loadingSpinner = CreateRect("Spinner", content);
            SetAnchoredRect(loadingSpinner,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 31f), new Vector2(44f, 44f));
            Image spinnerFrame = loadingSpinner.gameObject.AddComponent<Image>();
            spinnerFrame.color = overlayPrimary;
            spinnerFrame.raycastTarget = false;
            RectTransform spinnerCore = CreateRect("Core", loadingSpinner);
            SetAnchoredRect(spinnerCore,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 38f));
            Image spinnerCoreImage = spinnerCore.gameObject.AddComponent<Image>();
            spinnerCoreImage.color = Color.clear;
            spinnerCoreImage.raycastTarget = false;

            loadingStatusLabel = CreateText(
                "Status", content, "PREPARING WORLD", TextAlignmentOptions.Center);
            SetAnchoredRect((RectTransform)loadingStatusLabel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -26f), new Vector2(620f, 28f));
            loadingStatusLabel.fontSize = 13f;
            loadingStatusLabel.characterSpacing = 3f;
            loadingStatusLabel.color = overlaySecondary;

            RectTransform track = CreateRect("Progress Track", content);
            SetAnchoredRect(track,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -66f),
                new Vector2(
                    520f,
                    designTokens != null
                        ? designTokens.LoadingProgressThickness
                        : 6f));
            Image trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = overlayDivider;
            trackImage.raycastTarget = false;

            loadingFill = CreateRect("Fill", track);
            loadingFill.anchorMin = Vector2.zero;
            loadingFill.anchorMax = new Vector2(0f, 1f);
            loadingFill.pivot = new Vector2(0f, 0.5f);
            loadingFill.offsetMin = Vector2.zero;
            loadingFill.offsetMax = Vector2.zero;
            Image fillImage = loadingFill.gameObject.AddComponent<Image>();
            fillImage.color = overlayPrimary;
            fillImage.raycastTarget = false;

            loadingProgressLabel = CreateText(
                "Progress", content, "0%", TextAlignmentOptions.Center);
            SetAnchoredRect((RectTransform)loadingProgressLabel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -95f), new Vector2(300f, 30f));
            loadingProgressLabel.fontSize = 14f;
            loadingProgressLabel.characterSpacing = 2f;
            loadingProgressLabel.color = overlayPrimary;

            TMP_Text hint = CreateText(
                "Hint", content, "PREPARING A SAFE LANDING...", TextAlignmentOptions.Center);
            SetAnchoredRect((RectTransform)hint.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(620f, 28f));
            hint.fontSize = 12f;
            hint.characterSpacing = 1.5f;
            hint.color = overlaySecondary;

            loadingRequestedVisible = false;
            loadingPanel.SetActive(false);
        }

        [ContextMenu("Rebuild Pause Menu View")]
        public void RebuildPauseMenuView()
        {
            BuildPauseView();
            SciFiUiSkin.ApplyGameHud(transform);
        }

        private void BuildPauseView()
        {
            Transform existing = transform.Find(UiHierarchyPaths.Pause.Canvas);
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }

            RectTransform pauseRoot = CreateRect(UiHierarchyPaths.Pause.Canvas, transform);
            pauseCanvas = pauseRoot.gameObject.AddComponent<Canvas>();
            pauseCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            pauseCanvas.sortingOrder = designTokens != null
                ? designTokens.PauseSortingOrder
                : 1100;
            CanvasScaler scaler = pauseRoot.gameObject.AddComponent<CanvasScaler>();
            ApplyCanvasPolicy(pauseRoot.gameObject, scaler);
            pauseRoot.gameObject.AddComponent<GraphicRaycaster>();

            Color overlayBackdrop = designTokens != null
                ? designTokens.PauseBackdrop
                : new Color(0.025f, 0.028f, 0.035f, 1f);
            Color overlayPrimary = designTokens != null
                ? designTokens.OverlayPrimary
                : Color.white;
            Color overlaySecondary = designTokens != null
                ? designTokens.OverlaySecondary
                : new Color(1f, 1f, 1f, 0.58f);
            Color overlayDivider = designTokens != null
                ? designTokens.OverlayDivider
                : new Color(1f, 1f, 1f, 0.24f);
            Color overlayInverse = designTokens != null
                ? designTokens.OverlayInverse
                : new Color(0.018f, 0.02f, 0.025f, 1f);
            Color systemSurface = overlayPrimary;
            Color systemDivider = new Color(
                overlayInverse.r,
                overlayInverse.g,
                overlayInverse.b,
                overlayDivider.a);

            RectTransform panel = CreateRect("Pause Panel", pauseRoot);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            Image backdrop = panel.gameObject.AddComponent<Image>();
            backdrop.color = overlayBackdrop;
            backdrop.raycastTarget = true;
            pausePanel = panel.gameObject;

            RectTransform systemFieldEdge = CreateRect(
                "System Field Edge",
                panel);
            SetAnchoredRect(
                systemFieldEdge,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0.5f),
                Vector2.zero,
                new Vector2(928f, 0f));
            PauseMenuWedgeGraphic wedgeEdge = systemFieldEdge.gameObject
                .AddComponent<PauseMenuWedgeGraphic>();
            wedgeEdge.Configure(264f, systemDivider);

            RectTransform systemField = CreateRect(
                UiHierarchyPaths.Pause.SystemField,
                panel);
            SetAnchoredRect(
                systemField,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0.5f),
                Vector2.zero,
                new Vector2(PauseMenuWedgeGraphic.SystemFieldWidth, 0f));
            PauseMenuWedgeGraphic wedge =
                systemField.gameObject.AddComponent<PauseMenuWedgeGraphic>();
            wedge.Configure(PauseMenuWedgeGraphic.SystemFieldTopInset, systemSurface);

            RectTransform menu = CreateRect(UiHierarchyPaths.Pause.Menu, panel);
            SetAnchoredRect(
                menu,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-40f, 0f),
                new Vector2(640f, 760f));
            menu.gameObject.AddComponent<CanvasGroup>();

            pauseMainOptions = CreateRect(
                UiHierarchyPaths.Pause.MainOptions,
                menu).gameObject;
            StretchToParent((RectTransform)pauseMainOptions.transform);
            BuildPauseMainOptions(
                (RectTransform)pauseMainOptions.transform,
                overlayInverse,
                overlayPrimary,
                overlayDivider);

            pauseSettingsPanel = CreateRect(
                UiHierarchyPaths.Pause.SettingsPanel,
                menu).gameObject;
            StretchToParent((RectTransform)pauseSettingsPanel.transform);
            BuildPauseSettingsPanel(
                (RectTransform)pauseSettingsPanel.transform,
                overlayInverse,
                overlayPrimary,
                systemDivider,
                overlayDivider,
                overlaySecondary);
            pauseSettingsPanel.SetActive(false);

            BindPauseMenuButtons();
            LoadPauseSettings();
            EnsureSingleEventSystem(transform);
            pausePanel.SetActive(pauseMenuOpen);
        }

        private void BuildPauseMainOptions(
            RectTransform parent,
            Color systemInk,
            Color buttonInk,
            Color buttonDivider)
        {
            CreatePauseHeader(parent, "PAUSED", systemInk);

            resumeButton = CreatePauseMenuButton(
                UiHierarchyPaths.Pause.Resume,
                parent,
                1,
                "RESUME",
                "ESC",
                -174f,
                buttonInk,
                buttonDivider);
            pauseSettingsButton = CreatePauseMenuButton(
                UiHierarchyPaths.Pause.Settings,
                parent,
                2,
                "SETTINGS",
                string.Empty,
                -260f,
                buttonInk,
                buttonDivider);
            quitToMenuButton = CreatePauseMenuButton(
                UiHierarchyPaths.Pause.QuitToMenu,
                parent,
                3,
                "QUIT TO MENU",
                string.Empty,
                -346f,
                buttonInk,
                buttonDivider);
            quitToDesktopButton = CreatePauseMenuButton(
                UiHierarchyPaths.Pause.QuitToDesktop,
                parent,
                4,
                "QUIT TO DESKTOP",
                string.Empty,
                -432f,
                buttonInk,
                buttonDivider);

            ConfigurePauseNavigation(
                resumeButton,
                pauseSettingsButton,
                quitToMenuButton,
                quitToDesktopButton);
        }

        private void BuildPauseSettingsPanel(
            RectTransform parent,
            Color systemInk,
            Color buttonInk,
            Color systemDivider,
            Color buttonDivider,
            Color secondary)
        {
            CreatePauseHeader(parent, "SETTINGS", systemInk);

            pauseFullscreenToggle = CreatePauseToggle(
                UiHierarchyPaths.Pause.Fullscreen,
                parent,
                "FULLSCREEN",
                -202f,
                systemInk,
                systemDivider);
            pauseVolumeSlider = CreatePauseSlider(
                UiHierarchyPaths.Pause.MasterVolume,
                parent,
                "MASTER VOLUME",
                -306f,
                systemInk,
                systemDivider,
                out pauseVolumeValueLabel);
            pauseSettingsBackButton = CreatePauseMenuButton(
                UiHierarchyPaths.Pause.SettingsBack,
                parent,
                0,
                "BACK",
                string.Empty,
                -470f,
                buttonInk,
                buttonDivider);

            TMP_Text backIndex = pauseSettingsBackButton.transform
                .Find("Index")?.GetComponent<TMP_Text>();
            if (backIndex != null)
                backIndex.text = "<";

            TMP_Text hint = CreateText(
                "Settings Hint",
                parent,
                "CHANGES ARE APPLIED IMMEDIATELY",
                TextAlignmentOptions.Left);
            SetAnchoredRect(
                (RectTransform)hint.transform,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(80f, 42f),
                new Vector2(500f, 28f));
            hint.fontSize = 11f;
            hint.characterSpacing = 2f;
            hint.color =
                new Color(systemInk.r, systemInk.g, systemInk.b, secondary.a);
        }

        private static void CreatePauseHeader(
            RectTransform parent,
            string titleText,
            Color inverse)
        {
            TMP_Text eyebrow = CreateText(
                UiHierarchyPaths.Pause.Eyebrow,
                parent,
                "SUPERNOVA  //  SYSTEM",
                TextAlignmentOptions.Left);
            SetAnchoredRect(
                (RectTransform)eyebrow.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(80f, -54f),
                new Vector2(500f, 28f));
            eyebrow.fontSize = 12f;
            eyebrow.fontStyle = FontStyles.Bold;
            eyebrow.characterSpacing = 4f;
            eyebrow.color = new Color(inverse.r, inverse.g, inverse.b, 0.56f);

            TMP_Text title = CreateText(
                UiHierarchyPaths.Pause.Title,
                parent,
                titleText,
                TextAlignmentOptions.Left);
            SetAnchoredRect(
                (RectTransform)title.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(76f, -82f),
                new Vector2(510f, 72f));
            title.fontSize = titleText.Length > 6 ? 44f : 48f;
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 4f;
            title.color = inverse;
        }

        private Button CreatePauseMenuButton(
            string name,
            RectTransform parent,
            int index,
            string text,
            string shortcut,
            float anchoredY,
            Color inverse,
            Color divider)
        {
            RectTransform rect = CreateRect(name, parent);
            SetAnchoredRect(
                rect,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(70f, anchoredY),
                new Vector2(520f, 72f));

            RectTransform angledRect = CreateRect("Angled Surface", rect);
            StretchToParent(angledRect);
            AngledPanelGraphic angled = angledRect.gameObject
                .AddComponent<AngledPanelGraphic>();
            float slant = designTokens != null
                ? designTokens.HudElementSlant * 1.8f
                : 16f;
            float depth = designTokens != null
                ? designTokens.HudExtrusionDepth
                : 5f;
            Color surface = designTokens != null
                ? designTokens.HudSurface
                : new Color(0.035f, 0.045f, 0.055f, 0.84f);
            Color shadow = designTokens != null
                ? designTokens.HudShadow
                : new Color(0f, 0f, 0f, 0.72f);
            Color highlight = designTokens != null
                ? designTokens.HudMuted
                : new Color(1f, 1f, 1f, 0.2f);
            bool reverse = designTokens == null
                || designTokens.HudHotbarReverseSlant;
            angled.Configure(
                slant,
                depth,
                surface,
                shadow,
                highlight,
                reverse);
            angled.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = angled;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.pressedColor = new Color(0.68f, 0.68f, 0.68f, 1f);
            colors.selectedColor = new Color(1.22f, 1.22f, 1.22f, 1f);
            colors.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.42f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            TMP_Text indexLabel = CreateText(
                "Index",
                rect,
                index > 0 ? index.ToString("00") : string.Empty,
                TextAlignmentOptions.Left);
            SetAnchoredRect(
                (RectTransform)indexLabel.transform,
                Vector2.zero,
                Vector2.one,
                new Vector2(0f, 0.5f),
                new Vector2(12f, 0f),
                new Vector2(42f, 0f));
            indexLabel.fontSize = 11f;
            indexLabel.fontStyle = FontStyles.Bold;
            indexLabel.characterSpacing = 2f;
            indexLabel.color =
                new Color(inverse.r, inverse.g, inverse.b, 0.46f);

            TMP_Text label = CreateText(
                UiHierarchyPaths.Pause.Label,
                rect,
                text,
                TextAlignmentOptions.Left);
            SetAnchoredRect(
                (RectTransform)label.transform,
                Vector2.zero,
                Vector2.one,
                new Vector2(0f, 0.5f),
                new Vector2(62f, 0f),
                new Vector2(-126f, 0f));
            label.fontSize = text.Length > 14 ? 24f : 29f;
            label.fontStyle = FontStyles.Bold;
            label.characterSpacing = 1.5f;
            label.color = inverse;

            if (!string.IsNullOrEmpty(shortcut))
            {
                TMP_Text shortcutLabel = CreateText(
                    "Shortcut",
                    rect,
                    "[ " + shortcut + " ]",
                    TextAlignmentOptions.Right);
                SetAnchoredRect(
                    (RectTransform)shortcutLabel.transform,
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(1f, 0.5f),
                    new Vector2(-14f, 0f),
                    new Vector2(100f, 0f));
                shortcutLabel.fontSize = 11f;
                shortcutLabel.fontStyle = FontStyles.Bold;
                shortcutLabel.characterSpacing = 2f;
                shortcutLabel.color =
                    new Color(inverse.r, inverse.g, inverse.b, 0.5f);
            }

            RectTransform rule = CreateRect("Rule", rect);
            SetAnchoredRect(
                rule,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                Vector2.zero,
                new Vector2(0f, 1f));
            Image ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.color = new Color(
                inverse.r,
                inverse.g,
                inverse.b,
                Mathf.Max(0.14f, divider.a * 0.72f));
            ruleImage.raycastTarget = false;
            return button;
        }

        private static Toggle CreatePauseToggle(
            string name,
            RectTransform parent,
            string labelText,
            float anchoredY,
            Color inverse,
            Color divider)
        {
            RectTransform rect = CreateRect(name, parent);
            SetAnchoredRect(
                rect,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(80f, anchoredY),
                new Vector2(500f, 72f));

            TMP_Text label = CreateText(
                UiHierarchyPaths.Pause.Label,
                rect,
                labelText,
                TextAlignmentOptions.Left);
            SetAnchoredRect(
                (RectTransform)label.transform,
                Vector2.zero,
                Vector2.one,
                new Vector2(0f, 0.5f),
                Vector2.zero,
                new Vector2(-120f, 0f));
            label.fontSize = 20f;
            label.fontStyle = FontStyles.Bold;
            label.characterSpacing = 1.5f;
            label.color = inverse;

            RectTransform indicator = CreateRect("Indicator", rect);
            SetAnchoredRect(
                indicator,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                Vector2.zero,
                new Vector2(82f, 34f));
            Image indicatorImage = indicator.gameObject.AddComponent<Image>();
            indicatorImage.color =
                new Color(inverse.r, inverse.g, inverse.b, 0.12f);

            RectTransform checkmark = CreateRect("Checkmark", indicator);
            SetAnchoredRect(
                checkmark,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-4f, 0f),
                new Vector2(35f, 26f));
            Image checkmarkImage = checkmark.gameObject.AddComponent<Image>();
            checkmarkImage.color = inverse;

            Toggle toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = indicatorImage;
            toggle.graphic = checkmarkImage;
            ColorBlock colors = toggle.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.82f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.64f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            toggle.colors = colors;

            RectTransform rule = CreateRect("Rule", rect);
            SetAnchoredRect(
                rule,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                Vector2.zero,
                new Vector2(0f, 1f));
            Image ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.color =
                new Color(inverse.r, inverse.g, inverse.b, divider.a);
            ruleImage.raycastTarget = false;
            return toggle;
        }

        private static Slider CreatePauseSlider(
            string name,
            RectTransform parent,
            string labelText,
            float anchoredY,
            Color inverse,
            Color divider,
            out TMP_Text valueLabel)
        {
            RectTransform rect = CreateRect(name, parent);
            SetAnchoredRect(
                rect,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(80f, anchoredY),
                new Vector2(500f, 102f));

            TMP_Text label = CreateText(
                UiHierarchyPaths.Pause.Label,
                rect,
                labelText,
                TextAlignmentOptions.Left);
            SetAnchoredRect(
                (RectTransform)label.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                Vector2.zero,
                new Vector2(-110f, 42f));
            label.fontSize = 20f;
            label.fontStyle = FontStyles.Bold;
            label.characterSpacing = 1.5f;
            label.color = inverse;

            valueLabel = CreateText(
                UiHierarchyPaths.Pause.VolumeValue,
                rect,
                "100%",
                TextAlignmentOptions.Right);
            SetAnchoredRect(
                (RectTransform)valueLabel.transform,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                Vector2.zero,
                new Vector2(100f, 42f));
            valueLabel.fontSize = 17f;
            valueLabel.fontStyle = FontStyles.Bold;
            valueLabel.characterSpacing = 2f;
            valueLabel.color = inverse;

            RectTransform track = CreateRect("Background", rect);
            SetAnchoredRect(
                track,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 18f),
                new Vector2(0f, 8f));
            Image trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color =
                new Color(inverse.r, inverse.g, inverse.b, 0.16f);

            RectTransform fillArea = CreateRect("Fill Area", rect);
            SetAnchoredRect(
                fillArea,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 18f),
                new Vector2(0f, 8f));
            RectTransform fill = CreateRect("Fill", fillArea);
            StretchToParent(fill);
            Image fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = inverse;

            RectTransform handleArea = CreateRect("Handle Slide Area", rect);
            SetAnchoredRect(
                handleArea,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 18f),
                new Vector2(-20f, 30f));
            RectTransform handle = CreateRect("Handle", handleArea);
            SetAnchoredRect(
                handle,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(18f, 30f));
            Image handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = inverse;

            Slider slider = rect.gameObject.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.wholeNumbers = false;
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            ColorBlock colors = slider.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.78f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.6f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            slider.colors = colors;

            RectTransform rule = CreateRect("Rule", rect);
            SetAnchoredRect(
                rule,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                Vector2.zero,
                new Vector2(0f, 1f));
            Image ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.color =
                new Color(inverse.r, inverse.g, inverse.b, divider.a);
            ruleImage.raycastTarget = false;
            return slider;
        }

        private void CachePauseMenuReferences()
        {
            pauseMainOptions = FindPauseObject(
                UiHierarchyPaths.Pause.FullMainOptions,
                pauseMainOptions);
            pauseSettingsPanel = FindPauseObject(
                UiHierarchyPaths.Pause.FullSettingsPanel,
                pauseSettingsPanel);
            resumeButton = FindPauseComponent(
                UiHierarchyPaths.Pause.FullResume,
                resumeButton);
            pauseSettingsButton = FindPauseComponent(
                UiHierarchyPaths.Pause.FullSettings,
                pauseSettingsButton);
            quitToMenuButton = FindPauseComponent(
                UiHierarchyPaths.Pause.FullQuitToMenu,
                quitToMenuButton);
            quitToDesktopButton = FindPauseComponent(
                UiHierarchyPaths.Pause.FullQuitToDesktop,
                quitToDesktopButton);
            pauseSettingsBackButton = FindPauseComponent(
                UiHierarchyPaths.Pause.FullSettingsBack,
                pauseSettingsBackButton);
            pauseFullscreenToggle = FindPauseComponent(
                UiHierarchyPaths.Pause.FullFullscreen,
                pauseFullscreenToggle);
            pauseVolumeSlider = FindPauseComponent(
                UiHierarchyPaths.Pause.FullMasterVolume,
                pauseVolumeSlider);

            Transform volume = transform.Find(
                UiHierarchyPaths.Pause.FullMasterVolume);
            if (volume != null)
            {
                pauseVolumeValueLabel = volume
                    .Find(UiHierarchyPaths.Pause.VolumeValue)
                    ?.GetComponent<TMP_Text>();
            }
        }

        private GameObject FindPauseObject(string path, GameObject current)
        {
            if (current != null)
                return current;
            Transform found = transform.Find(path);
            return found != null ? found.gameObject : null;
        }

        private T FindPauseComponent<T>(string path, T current)
            where T : Component
        {
            if (current != null)
                return current;
            Transform found = transform.Find(path);
            return found != null ? found.GetComponent<T>() : null;
        }

        private void BindPauseMenuButtons()
        {
            CachePauseMenuReferences();

            if (resumeButton != null)
            {
                resumeButton.onClick.RemoveListener(ResumeGame);
                resumeButton.onClick.AddListener(ResumeGame);
            }
            if (pauseSettingsButton != null)
            {
                pauseSettingsButton.onClick.RemoveListener(ShowPauseSettings);
                pauseSettingsButton.onClick.AddListener(ShowPauseSettings);
            }
            if (quitToMenuButton != null)
            {
                quitToMenuButton.onClick.RemoveListener(QuitToMainMenu);
                quitToMenuButton.onClick.AddListener(QuitToMainMenu);
            }
            if (quitToDesktopButton != null)
            {
                quitToDesktopButton.onClick.RemoveListener(QuitToDesktop);
                quitToDesktopButton.onClick.AddListener(QuitToDesktop);
            }
            if (pauseSettingsBackButton != null)
            {
                pauseSettingsBackButton.onClick.RemoveListener(
                    ShowPauseMainOptions);
                pauseSettingsBackButton.onClick.AddListener(
                    ShowPauseMainOptions);
            }
            if (pauseFullscreenToggle != null)
            {
                pauseFullscreenToggle.onValueChanged.RemoveListener(
                    OnPauseFullscreenChanged);
                pauseFullscreenToggle.onValueChanged.AddListener(
                    OnPauseFullscreenChanged);
            }
            if (pauseVolumeSlider != null)
            {
                pauseVolumeSlider.onValueChanged.RemoveListener(
                    OnPauseVolumeChanged);
                pauseVolumeSlider.onValueChanged.AddListener(
                    OnPauseVolumeChanged);
            }
        }

        private static void ConfigurePauseNavigation(params Selectable[] controls)
        {
            for (int i = 0; i < controls.Length; i++)
            {
                Selectable current = controls[i];
                if (current == null)
                    continue;

                Navigation navigation = current.navigation;
                navigation.mode = Navigation.Mode.Explicit;
                navigation.selectOnUp =
                    controls[(i - 1 + controls.Length) % controls.Length];
                navigation.selectOnDown =
                    controls[(i + 1) % controls.Length];
                current.navigation = navigation;
            }
        }

        private void ShowPauseSettings()
        {
            if (pauseMainOptions != null)
                pauseMainOptions.SetActive(false);
            if (pauseSettingsPanel != null)
                pauseSettingsPanel.SetActive(true);
            LoadPauseSettings();
            if (EventSystem.current != null && pauseFullscreenToggle != null)
            {
                EventSystem.current.SetSelectedGameObject(
                    pauseFullscreenToggle.gameObject);
            }
        }

        private void ShowPauseMainOptions()
        {
            if (pauseMainOptions != null)
                pauseMainOptions.SetActive(true);
            if (pauseSettingsPanel != null)
                pauseSettingsPanel.SetActive(false);
            if (EventSystem.current != null && resumeButton != null)
                EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
        }

        private void LoadPauseSettings()
        {
            bool fullscreen = PlayerPrefs.GetInt(
                FullscreenPreferenceKey,
                Screen.fullScreen ? 1 : 0) != 0;
            float volume = Mathf.Clamp01(
                PlayerPrefs.GetFloat(
                    VolumePreferenceKey,
                    AudioListener.volume));

            if (pauseFullscreenToggle != null)
                pauseFullscreenToggle.SetIsOnWithoutNotify(fullscreen);
            if (pauseVolumeSlider != null)
                pauseVolumeSlider.SetValueWithoutNotify(volume * 100f);
            SetPauseVolumeValue(volume * 100f);
        }

        private void OnPauseFullscreenChanged(bool value)
        {
            Screen.fullScreen = value;
            PlayerPrefs.SetInt(FullscreenPreferenceKey, value ? 1 : 0);
        }

        private void OnPauseVolumeChanged(float value)
        {
            float normalized = Mathf.Clamp01(value / 100f);
            AudioListener.volume = normalized;
            PlayerPrefs.SetFloat(VolumePreferenceKey, normalized);
            SetPauseVolumeValue(value);
        }

        private void SetPauseVolumeValue(float value)
        {
            if (pauseVolumeValueLabel != null)
            {
                pauseVolumeValueLabel.text =
                    Mathf.RoundToInt(value).ToString("00") + "%";
            }
        }

        public void QuitToMainMenu()
        {
            string mainMenuSceneName = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.SceneLookups.MainMenuSceneName
                : string.Empty;
            if (string.IsNullOrWhiteSpace(mainMenuSceneName)
                || !Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
            {
                Debug.LogError(
                    "Pause menu could not load the configured main-menu scene.");
                return;
            }

            ResumeGame();
            Time.timeScale = 1f;
            SceneManager.LoadSceneAsync(
                mainMenuSceneName,
                LoadSceneMode.Single);
        }

        public void QuitToDesktop()
        {
            PlayerPrefs.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        public static EventSystem EnsureSingleEventSystem(Transform fallbackParent)
        {
            EventSystem[] eventSystems = FindObjectsOfType<EventSystem>(true);
            EventSystem selected = EventSystem.current;
            if (selected == null || !selected.isActiveAndEnabled)
            {
                selected = null;
                for (int i = 0; i < eventSystems.Length; i++)
                {
                    if (eventSystems[i] != null && eventSystems[i].isActiveAndEnabled)
                    {
                        selected = eventSystems[i];
                        break;
                    }
                }
            }

            if (selected == null)
            {
                for (int i = 0; i < eventSystems.Length; i++)
                {
                    if (eventSystems[i] != null
                        && eventSystems[i].gameObject.activeInHierarchy)
                    {
                        selected = eventSystems[i];
                        break;
                    }
                }
            }

            if (selected == null)
            {
                GameObject eventSystemObject = new GameObject("EventSystem");
                if (fallbackParent != null && fallbackParent.gameObject.activeInHierarchy)
                    eventSystemObject.transform.SetParent(fallbackParent, false);
                selected = eventSystemObject.AddComponent<EventSystem>();
                eventSystemObject.AddComponent<StandaloneInputModule>();
                eventSystems = FindObjectsOfType<EventSystem>(true);
            }

            selected.enabled = true;
            BaseInputModule selectedInputModule = selected.GetComponent<BaseInputModule>();
            if (selectedInputModule == null)
                selectedInputModule = selected.gameObject.AddComponent<StandaloneInputModule>();
            selectedInputModule.enabled = true;

            for (int i = 0; i < eventSystems.Length; i++)
            {
                EventSystem candidate = eventSystems[i];
                if (candidate == null || candidate == selected)
                    continue;

                candidate.enabled = false;
                BaseInputModule[] inputModules = candidate.GetComponents<BaseInputModule>();
                for (int moduleIndex = 0; moduleIndex < inputModules.Length; moduleIndex++)
                    inputModules[moduleIndex].enabled = false;
            }

            return selected;
        }

        private void BuildCompassView()
        {
            if (rootCanvas == null)
                return;

            Transform existing = transform.Find(UiHierarchyPaths.Hud.Compass);
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }

            Color primary = designTokens != null
                ? designTokens.HudPrimary
                : new Color(0.96f, 0.98f, 1f, 1f);
            Vector2 position = designTokens != null
                ? designTokens.CompassPosition
                : new Vector2(0f, -12f);
            Vector2 size = designTokens != null
                ? designTokens.CompassSize
                : new Vector2(720f, 72f);

            RectTransform compass = CreateRect(
                UiHierarchyPaths.Hud.CompassName,
                rootCanvas.transform);
            SetAnchoredRect(
                compass,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                position,
                size);

            Image backdrop = compass.gameObject.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.08f);
            backdrop.raycastTarget = false;

            RectTransform viewport = CreateRect(
                UiHierarchyPaths.Hud.CompassViewportName,
                compass);
            SetAnchoredRect(
                viewport,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -2f),
                new Vector2(-20f, 50f));
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform ticks = CreateRect(
                UiHierarchyPaths.Hud.CompassTicksName,
                viewport);
            SetAnchoredRect(
                ticks,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 1f),
                Vector2.zero,
                Vector2.zero);

            for (int i = 0; i < HeadingCompass.TickViewCount; i++)
            {
                RectTransform tick = CreateRect(
                    UiHierarchyPaths.Hud.CompassTickName(i + 1),
                    ticks);
                SetAnchoredRect(
                    tick,
                    new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    Vector2.zero,
                    new Vector2(54f, 48f));
                tick.gameObject.AddComponent<CanvasGroup>();

                RectTransform line = CreateRect(
                    UiHierarchyPaths.Hud.CompassTickLine,
                    tick);
                SetAnchoredRect(
                    line,
                    new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(0f, -1f),
                    new Vector2(1f, 6f));
                Image lineImage = line.gameObject.AddComponent<Image>();
                lineImage.color =
                    new Color(primary.r, primary.g, primary.b, 0.82f);
                lineImage.raycastTarget = false;

                TMP_Text label = CreateText(
                    UiHierarchyPaths.Hud.CompassTickLabel,
                    tick,
                    string.Empty,
                    TextAlignmentOptions.Top);
                SetAnchoredRect(
                    (RectTransform)label.transform,
                    new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(0f, -18f),
                    new Vector2(54f, 24f));
                label.fontSize = 10f;
                label.fontStyle = FontStyles.Bold;
                label.color = primary;
            }

            RectTransform marker = CreateRect(
                UiHierarchyPaths.Hud.CompassBearingMarkerName,
                compass);
            SetAnchoredRect(
                marker,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -1f),
                new Vector2(2f, 18f));
            Image markerImage = marker.gameObject.AddComponent<Image>();
            Color accent = designTokens != null
                ? designTokens.Accent
                : primary;
            markerImage.color = accent;
            markerImage.raycastTarget = false;

            RectTransform bearingRule = CreateRect(
                UiHierarchyPaths.Hud.CompassBearingRuleName,
                compass);
            SetAnchoredRect(
                bearingRule,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -47f),
                new Vector2(68f, 2f));
            Image ruleImage = bearingRule.gameObject.AddComponent<Image>();
            ruleImage.color =
                new Color(primary.r, primary.g, primary.b, 0.72f);
            ruleImage.raycastTarget = false;

            TMP_Text heading = CreateText(
                UiHierarchyPaths.Hud.CompassHeadingName,
                compass,
                "000\u00B0",
                TextAlignmentOptions.Center);
            SetAnchoredRect(
                (RectTransform)heading.transform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -50f),
                new Vector2(92f, 20f));
            heading.fontSize = 11f;
            heading.fontStyle = FontStyles.Bold;
            heading.characterSpacing = 2f;
            heading.color =
                new Color(primary.r, primary.g, primary.b, 0.78f);

            headingCompass = compass.gameObject.AddComponent<HeadingCompass>();
            headingCompass.Configure(null, designTokens);
            compass.gameObject.SetActive(
                designTokens == null || designTokens.ShowCompass);
        }

        private void BuildHotbarView(RectTransform rootRect)
        {
            Transform existing = rootRect.Find("Hotbar");
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                hotbarSlotBackgrounds[i] = null;
                hotbarSlotOutlines[i] = null;
                hotbarItemLabels[i] = null;
            }

            RectTransform hotbar = CreateRect("Hotbar", rootRect);
            SetAnchoredRect(hotbar, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(272f, 56f));
            hotbarRoot = hotbar.gameObject;

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                RectTransform slot = CreateRect(
                    UiHierarchyPaths.Hud.SlotName(i + 1),
                    hotbar);
                SetAnchoredRect(slot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f), new Vector2(i * 56f, 0f), new Vector2(52f, 52f));

                Image background = slot.gameObject.AddComponent<Image>();
                background.color = new Color(0.045f, 0.055f, 0.065f, 0.9f);
                background.raycastTarget = false;
                hotbarSlotBackgrounds[i] = background;

                Outline outline = slot.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.7f, 0.75f, 0.8f, 0.45f);
                outline.effectDistance = new Vector2(1f, -1f);
                outline.useGraphicAlpha = false;
                hotbarSlotOutlines[i] = outline;

                string keyText = (i + 1).ToString();
                TMP_Text key = CreateText("Key", slot, keyText, TextAlignmentOptions.TopLeft);
                SetAnchoredRect((RectTransform)key.transform, Vector2.zero, Vector2.one,
                    new Vector2(0.5f, 0.5f), new Vector2(5f, -3f), new Vector2(-10f, -6f));
                key.fontSize = 11f;
                key.color = new Color(0.7f, 0.75f, 0.8f, 1f);

                TMP_Text item = CreateText(
                    "Item",
                    slot,
                    string.Empty,
                    TextAlignmentOptions.Center);
                SetAnchoredRect((RectTransform)item.transform, Vector2.zero, Vector2.one,
                    new Vector2(0.5f, 0.5f), new Vector2(3f, -6f), new Vector2(-6f, -16f));
                item.fontSize = i == 2 ? 7f : 9f;
                item.color = new Color(0.92f, 0.94f, 0.96f, 1f);
                hotbarItemLabels[i] = item;
            }
        }

        private void ApplyReferenceHudLayout()
        {
            Color primary = designTokens != null
                ? designTokens.HudPrimary
                : new Color(0.96f, 0.98f, 1f, 1f);
            Color surfaceColor = designTokens != null
                ? designTokens.HudSurface
                : new Color(0.035f, 0.045f, 0.055f, 0.84f);
            Color muted = designTokens != null
                ? designTokens.HudMuted
                : new Color(0.96f, 0.98f, 1f, 0.2f);
            Color shadow = designTokens != null
                ? designTokens.HudShadow
                : new Color(0f, 0f, 0f, 0.72f);
            Vector2 healthPosition = designTokens != null
                ? designTokens.HudHealthPosition
                : new Vector2(48f, 42f);
            Vector2 healthSize = designTokens != null
                ? designTokens.HudHealthSize
                : new Vector2(372f, 104f);
            Vector2 hotbarPosition = designTokens != null
                ? designTokens.HudHotbarPosition
                : new Vector2(-46f, 42f);
            Vector2 hotbarSize = designTokens != null
                ? designTokens.HudHotbarSize
                : new Vector2(320f, 78f);
            float healthTilt = designTokens != null
                ? designTokens.HudHealthTiltDegrees
                : 3.5f;
            float hotbarTilt = designTokens != null
                ? designTokens.HudHotbarTiltDegrees
                : -3.5f;
            bool healthReverseSlant = designTokens != null
                && designTokens.HudHealthReverseSlant;
            bool hotbarReverseSlant = designTokens == null
                || designTokens.HudHotbarReverseSlant;
            float slant = designTokens != null
                ? designTokens.HudElementSlant
                : 9f;
            float depth = designTokens != null
                ? designTokens.HudExtrusionDepth
                : 5f;

            RectTransform panel = healthPanel != null
                ? healthPanel.transform as RectTransform
                : null;
            if (panel != null)
            {
                SetAnchoredRect(
                    panel,
                    Vector2.zero,
                    Vector2.zero,
                    Vector2.zero,
                    healthPosition,
                    healthSize);
                panel.localRotation = Quaternion.Euler(0f, 0f, healthTilt);
                ClearLegacyPlate(panel);

                TMP_Text title = panel.Find(
                    UiHierarchyPaths.Hud.HealthHeaderTitle)?.GetComponent<TMP_Text>();
                TMP_Text value = panel.Find(
                    UiHierarchyPaths.Hud.HealthHeaderValue)?.GetComponent<TMP_Text>();
                RectTransform header = panel.Find(
                    UiHierarchyPaths.Hud.HealthHeader) as RectTransform;
                if (header != null)
                {
                    SetAnchoredRect(
                        header,
                        new Vector2(0f, 1f),
                        new Vector2(1f, 1f),
                        new Vector2(0.5f, 1f),
                        new Vector2(2f, -2f),
                        new Vector2(-4f, 48f));
                }
                if (title != null)
                {
                    title.text = "HEALTH";
                    title.fontSize = 14f;
                    title.characterSpacing = 10f;
                    title.color = new Color(primary.r, primary.g, primary.b, 0.72f);
                    title.alignment = TextAlignmentOptions.TopLeft;
                }
                if (value != null)
                {
                    value.fontSize = 24f;
                    value.characterSpacing = 2f;
                    value.color = primary;
                    value.alignment = TextAlignmentOptions.BottomLeft;
                }

                RectTransform track = panel.Find(
                    UiHierarchyPaths.Hud.HealthTrack) as RectTransform;
                if (track != null)
                {
                    SetAnchoredRect(
                        track,
                        new Vector2(0f, 0f),
                        new Vector2(1f, 0f),
                        new Vector2(0.5f, 0f),
                        new Vector2(2f, 2f),
                        new Vector2(-4f, 42f));
                    Image trackImage = track.GetComponent<Image>();
                    if (trackImage != null)
                        trackImage.color = Color.clear;
                    if (healthFillImage != null)
                        healthFillImage.color = Color.clear;

                    int segmentCount = designTokens != null
                        ? designTokens.HudHealthSegmentCount
                        : 8;
                    EnsureHealthSegments(
                        track,
                        Mathf.Max(3, segmentCount),
                        Mathf.Max(24f, healthSize.x - 4f),
                        slant,
                        depth,
                        primary,
                        muted,
                        shadow,
                        healthReverseSlant);
                }
            }

            RectTransform hotbar = hotbarRoot != null
                ? hotbarRoot.transform as RectTransform
                : null;
            if (hotbar != null)
            {
                SetAnchoredRect(
                    hotbar,
                    new Vector2(1f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 0f),
                    hotbarPosition,
                    hotbarSize);
                hotbar.localRotation = Quaternion.Euler(0f, 0f, hotbarTilt);

                const float slotGap = 6f;
                float slotWidth = Mathf.Max(
                    38f,
                    (hotbarSize.x - (PlayerInventory.SlotCount - 1) * slotGap)
                    / PlayerInventory.SlotCount);
                float slotHeight = Mathf.Max(48f, hotbarSize.y - 8f);
                for (int i = 0; i < PlayerInventory.SlotCount; i++)
                {
                    RectTransform slot = hotbar.Find(
                        UiHierarchyPaths.Hud.SlotName(i + 1)) as RectTransform;
                    if (slot == null)
                        continue;

                    SetAnchoredRect(
                        slot,
                        new Vector2(0f, 0.5f),
                        new Vector2(0f, 0.5f),
                        new Vector2(0f, 0.5f),
                        new Vector2(i * (slotWidth + slotGap), i * 0.35f),
                        new Vector2(slotWidth, slotHeight));
                    ClearLegacyPlate(slot);

                    AngledPanelGraphic angledSurface = EnsureAngledSurface(slot);
                    if (angledSurface != null)
                    {
                        angledSurface.Configure(
                            slant,
                            depth,
                            surfaceColor,
                            shadow,
                            new Color(primary.r, primary.g, primary.b, 0.25f),
                            hotbarReverseSlant);
                    }

                    TMP_Text key = slot.Find(
                        UiHierarchyPaths.Hud.Key)?.GetComponent<TMP_Text>();
                    if (key != null)
                    {
                        RectTransform keyRect = (RectTransform)key.transform;
                        SetAnchoredRect(
                            keyRect,
                            Vector2.zero,
                            Vector2.one,
                            new Vector2(0.5f, 0.5f),
                            new Vector2(7f, -3f),
                            new Vector2(-14f, -8f));
                        key.fontSize = 10f;
                        key.color = new Color(primary.r, primary.g, primary.b, 0.62f);
                        key.alignment = TextAlignmentOptions.TopLeft;
                    }

                    TMP_Text item = slot.Find(
                        UiHierarchyPaths.Hud.Item)?.GetComponent<TMP_Text>();
                    if (item != null)
                    {
                        RectTransform itemRect = (RectTransform)item.transform;
                        SetAnchoredRect(
                            itemRect,
                            Vector2.zero,
                            Vector2.one,
                            new Vector2(0.5f, 0.5f),
                            new Vector2(4f, -9f),
                            new Vector2(-8f, -18f));
                        item.fontSize = 8f;
                        item.characterSpacing = 1f;
                        item.color = primary;
                        item.alignment = TextAlignmentOptions.Center;
                    }
                }
            }

            RectTransform crosshair = transform.Find(
                UiHierarchyPaths.Hud.Crosshair) as RectTransform;
            if (crosshair != null)
            {
                crosshair.sizeDelta = new Vector2(12f, 12f);
                StyleCrosshairBar(crosshair, UiHierarchyPaths.Hud.Horizontal,
                    new Vector2(12f, 2f), primary);
                StyleCrosshairBar(crosshair, UiHierarchyPaths.Hud.Vertical,
                    new Vector2(2f, 12f), primary);
                StyleCrosshairBar(crosshair, UiHierarchyPaths.Decoration.Center,
                    new Vector2(2f, 2f), primary);
            }
        }

        private void EnsureHealthSegments(
            RectTransform track,
            int segmentCount,
            float availableWidth,
            float slant,
            float depth,
            Color active,
            Color inactive,
            Color shadow,
            bool reverseSlant)
        {
            Transform existingRoot = track.Find(
                UiHierarchyPaths.Hud.HealthSegmentsName);
            RectTransform segmentRoot = existingRoot as RectTransform;
            if (segmentRoot == null)
            {
                segmentRoot = CreateRect(
                    UiHierarchyPaths.Hud.HealthSegmentsName,
                    track);
            }

            segmentRoot.anchorMin = Vector2.zero;
            segmentRoot.anchorMax = Vector2.one;
            segmentRoot.pivot = new Vector2(0.5f, 0.5f);
            segmentRoot.offsetMin = Vector2.zero;
            segmentRoot.offsetMax = Vector2.zero;
            segmentRoot.SetAsLastSibling();

            for (int i = 0; i < segmentRoot.childCount; i++)
                segmentRoot.GetChild(i).gameObject.SetActive(false);

            healthSegments = new AngledPanelGraphic[segmentCount];
            const float segmentGap = 6f;
            float segmentWidth = Mathf.Max(
                12f,
                (availableWidth - (segmentCount - 1) * segmentGap) / segmentCount);
            for (int i = 0; i < segmentCount; i++)
            {
                string segmentName = UiHierarchyPaths.Hud.HealthSegmentPrefix + (i + 1);
                RectTransform segment = segmentRoot.Find(segmentName) as RectTransform;
                if (segment == null)
                    segment = CreateAngledRect(segmentName, segmentRoot);

                segment.gameObject.SetActive(true);
                SetAnchoredRect(
                    segment,
                    new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f),
                    new Vector2(i * (segmentWidth + segmentGap), 0f),
                    new Vector2(segmentWidth, 36f));

                EnsureCanvasRenderer(segment);
                AngledPanelGraphic graphic = segment.GetComponent<AngledPanelGraphic>();
                if (graphic == null)
                    graphic = segment.gameObject.AddComponent<AngledPanelGraphic>();
                graphic.Configure(
                    slant,
                    depth,
                    active,
                    shadow,
                    Color.Lerp(inactive, active, 0.48f),
                    reverseSlant);
                healthSegments[i] = graphic;
            }
        }

        private static AngledPanelGraphic EnsureAngledSurface(RectTransform parent)
        {
            RectTransform rect = parent.Find(
                UiHierarchyPaths.Hud.AngledSurface) as RectTransform;
            if (rect == null)
                rect = CreateAngledRect(
                    UiHierarchyPaths.Hud.AngledSurface,
                    parent);

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(1f, 1f);
            rect.offsetMax = new Vector2(-1f, -1f);
            rect.SetAsFirstSibling();

            EnsureCanvasRenderer(rect);
            AngledPanelGraphic graphic = rect.GetComponent<AngledPanelGraphic>();
            if (graphic == null)
                graphic = rect.gameObject.AddComponent<AngledPanelGraphic>();
            return graphic;
        }

        private static void EnsureCanvasRenderer(RectTransform rect)
        {
            if (rect == null || rect.GetComponent<CanvasRenderer>() != null)
                return;

            AngledPanelGraphic existingGraphic =
                rect.GetComponent<AngledPanelGraphic>();
            bool reenableGraphic = existingGraphic != null && existingGraphic.enabled;
            if (reenableGraphic)
                existingGraphic.enabled = false;
            rect.gameObject.AddComponent<CanvasRenderer>();
            if (reenableGraphic)
                existingGraphic.enabled = true;
        }

        private static RectTransform CreateAngledRect(
            string objectName,
            Transform parent)
        {
            GameObject child = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(AngledPanelGraphic));
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void ClearLegacyPlate(RectTransform rect)
        {
            Image image = rect.GetComponent<Image>();
            if (image != null)
                image.color = Color.clear;
            Outline outline = rect.GetComponent<Outline>();
            if (outline != null)
                outline.effectColor = Color.clear;
            Transform frame = rect.Find(UiHierarchyPaths.Decoration.Frame);
            if (frame != null)
                frame.gameObject.SetActive(false);
        }

        private static void StyleCrosshairBar(
            RectTransform root,
            string childPath,
            Vector2 size,
            Color color)
        {
            RectTransform rect = root.Find(childPath) as RectTransform;
            if (rect == null)
                return;
            rect.sizeDelta = size;
            Image image = rect.GetComponent<Image>();
            if (image != null)
                image.color = color;
            Outline outline = rect.GetComponent<Outline>();
            if (outline != null)
                outline.effectColor = new Color(0f, 0f, 0f, 0.78f);
        }

        private static RectTransform CreateRect(string objectName, Transform parent)
        {
            GameObject child = new GameObject(objectName, typeof(RectTransform));
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private void ApplyCanvasPolicy(GameObject canvasObject, CanvasScaler scaler)
        {
            UiCanvasPolicy policy = canvasObject.GetComponent<UiCanvasPolicy>();
            if (policy == null)
                policy = canvasObject.AddComponent<UiCanvasPolicy>();
            policy.SetDesignTokens(designTokens);
        }

        private static void CreateCrosshairBar(string objectName, RectTransform parent, Vector2 size)
        {
            RectTransform bar = CreateRect(objectName, parent);
            SetAnchoredRect(bar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, size);
            Image image = bar.gameObject.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;
            Outline outline = bar.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
        }

        private static TMP_Text CreateText(
            string objectName, RectTransform parent, string value, TextAlignmentOptions alignment)
        {
            RectTransform rect = CreateRect(objectName, parent);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = 14f;
            text.fontStyle = FontStyles.Bold;
            text.color = new Color(0.95f, 0.965f, 0.98f, 1f);
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        private static void SetAnchoredRect(
            RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static void SetCursorState(CursorLockMode lockMode, bool visible)
        {
            Cursor.lockState = lockMode;
            Cursor.visible = visible;
        }

        private static IDamageable FindPlayerHealthSource()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                IDamageable cameraOwner = FindDamageable(
                    mainCamera.GetComponentsInParent<MonoBehaviour>(true));
                if (cameraOwner != null) return cameraOwner;
            }

            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>();
            IDamageable fallback = null;
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (!(behaviour is IDamageable damageable) || damageable.Owner == null) continue;
                if (fallback == null) fallback = damageable;

                GameObject owner = damageable.Owner;
                if (owner.CompareTag("Player")
                    || owner.GetComponent<CharacterController>() != null)
                {
                    return damageable;
                }
            }

            return fallback;
        }

        private static bool IsHealthSourceValid(IDamageable source)
        {
            if (source == null) return false;
            return !(source is UnityEngine.Object unityObject)
                || unityObject != null;
        }

        private static PlayerToolController FindPlayerInventorySource()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                PlayerToolController cameraOwner =
                    mainCamera.GetComponentInParent<PlayerToolController>(true);
                if (cameraOwner != null) return cameraOwner;
            }

            return FindObjectOfType<PlayerToolController>();
        }

        private static IDamageable FindDamageable(MonoBehaviour[] behaviours)
        {
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IDamageable damageable) return damageable;
            }
            return null;
        }
    }

    /// <summary>Presentation-only adapter for the five configurable hotbar slots.</summary>
    public sealed class HotbarPresenter
    {
        private readonly Image[] backgrounds;
        private readonly Outline[] outlines;
        private readonly Image[] frames;
        private readonly AngledPanelGraphic[] angledSurfaces;
        private readonly TMP_Text[] itemLabels;
        private readonly TMP_Text[] keyLabels;
        private readonly Color primary;
        private readonly Color surface;
        private readonly Color shadow;
        private readonly PlayerInventoryItem[] displayedItems =
            new PlayerInventoryItem[PlayerInventory.SlotCount];
        private int selectedSlotIndex = -1;

        public HotbarPresenter(Image[] backgrounds, Outline[] outlines, TMP_Text[] itemLabels)
            : this(backgrounds, outlines, itemLabels, null)
        {
        }

        public HotbarPresenter(
            Image[] backgrounds,
            Outline[] outlines,
            TMP_Text[] itemLabels,
            UiDesignTokens designTokens)
        {
            this.backgrounds = backgrounds;
            this.outlines = outlines;
            this.itemLabels = itemLabels;
            primary = designTokens != null
                ? designTokens.HudPrimary
                : new Color(0.96f, 0.98f, 1f, 1f);
            surface = designTokens != null
                ? designTokens.HudSurface
                : new Color(0.035f, 0.045f, 0.055f, 0.84f);
            shadow = designTokens != null
                ? designTokens.HudShadow
                : new Color(0f, 0f, 0f, 0.72f);
            frames = new Image[PlayerInventory.SlotCount];
            angledSurfaces = new AngledPanelGraphic[PlayerInventory.SlotCount];
            keyLabels = new TMP_Text[PlayerInventory.SlotCount];
            for (int i = 0; i < frames.Length; i++)
            {
                if (backgrounds == null || i >= backgrounds.Length || backgrounds[i] == null)
                    continue;
                Transform frame = backgrounds[i].transform.Find(UiHierarchyPaths.Decoration.Frame);
                if (frame != null)
                {
                    frames[i] = frame.GetComponent<Image>();
                    frame.gameObject.SetActive(false);
                }
                Transform angled = backgrounds[i].transform.Find(
                    UiHierarchyPaths.Hud.AngledSurface);
                if (angled != null)
                    angledSurfaces[i] = angled.GetComponent<AngledPanelGraphic>();
                Transform key = backgrounds[i].transform.Find(UiHierarchyPaths.Hud.Key);
                if (key != null)
                    keyLabels[i] = key.GetComponent<TMP_Text>();
            }
            SetItemLabels();
        }

        public void SetSelectedSlot(int selectedIndex)
        {
            selectedSlotIndex = selectedIndex;
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                bool selected = i == selectedIndex;
                if (backgrounds != null && i < backgrounds.Length && backgrounds[i] != null)
                    backgrounds[i].color = Color.clear;
                if (outlines != null && i < outlines.Length && outlines[i] != null)
                {
                    outlines[i].effectColor = Color.clear;
                }
                if (frames[i] != null)
                    frames[i].gameObject.SetActive(false);
                if (angledSurfaces[i] != null)
                {
                    angledSurfaces[i].SetFrontColor(selected ? primary : surface);
                    angledSurfaces[i].SetDepthColor(shadow);
                }
                ApplyLabelColor(i, selected);
            }
        }

        public void SetInventory(PlayerToolController source)
        {
            if (itemLabels == null)
                return;

            for (int i = 0; i < itemLabels.Length; i++)
            {
                if (itemLabels[i] == null)
                    continue;

                PlayerInventoryItem item = source != null
                    ? source.GetItemAtSlot(i)
                    : PlayerInventory.GetDefaultItemAtSlot(i);
                if (displayedItems[i] == item)
                    continue;

                displayedItems[i] = item;
                itemLabels[i].text = GetItemLabel(item);
                itemLabels[i].fontSize =
                    item == PlayerInventoryItem.Flashlight
                        || item == PlayerInventoryItem.SolidGun
                            ? 7f
                            : 9f;
                ApplyLabelColor(i, i == selectedSlotIndex);
            }
        }

        private void SetItemLabels()
        {
            SetInventory(null);
        }

        private void ApplyLabelColor(int index, bool selected)
        {
            Color labelColor = selected
                ? new Color(0.025f, 0.03f, 0.035f, 1f)
                : primary;
            if (itemLabels != null && index < itemLabels.Length && itemLabels[index] != null)
                itemLabels[index].color = labelColor;
            if (keyLabels != null && index < keyLabels.Length && keyLabels[index] != null)
            {
                keyLabels[index].color = selected
                    ? new Color(0.025f, 0.03f, 0.035f, 0.68f)
                    : new Color(primary.r, primary.g, primary.b, 0.62f);
            }
        }

        public static string GetItemLabel(PlayerInventoryItem item)
        {
            switch (item)
            {
                case PlayerInventoryItem.Pickaxe:
                    return "PICKAXE";
                case PlayerInventoryItem.Magnet:
                    return "MAGNET";
                case PlayerInventoryItem.Flashlight:
                    return "FLASHLIGHT";
                case PlayerInventoryItem.Gun:
                    return "GUN";
                case PlayerInventoryItem.SMG:
                    return "SMG";
                case PlayerInventoryItem.SolidGun:
                    return "SOLIDGUN";
                case PlayerInventoryItem.Cart:
                    return "CART";
                case PlayerInventoryItem.GrabHook:
                    return "GRABHOOK";
                case PlayerInventoryItem.Bomb:
                    return "BOMB";
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>Presentation-only UGUI adapter.</summary>
    public sealed class GameHudPresenter
    {
        private readonly GameObject healthPanel;
        private readonly RectTransform healthFill;
        private readonly Image healthFillImage;
        private readonly TMP_Text healthValueLabel;
        private readonly AngledPanelGraphic[] healthSegments;
        private readonly Color primary;
        private readonly Color muted;
        private readonly Color danger;

        public GameHudPresenter(
            GameObject healthPanel,
            RectTransform healthFill,
            Image healthFillImage,
            TMP_Text healthValueLabel)
            : this(
                healthPanel,
                healthFill,
                healthFillImage,
                healthValueLabel,
                null,
                null)
        {
        }

        public GameHudPresenter(
            GameObject healthPanel,
            RectTransform healthFill,
            Image healthFillImage,
            TMP_Text healthValueLabel,
            AngledPanelGraphic[] healthSegments,
            UiDesignTokens designTokens)
        {
            this.healthPanel = healthPanel;
            this.healthFill = healthFill;
            this.healthFillImage = healthFillImage;
            this.healthValueLabel = healthValueLabel;
            this.healthSegments = healthSegments;
            primary = designTokens != null
                ? designTokens.HudPrimary
                : new Color(0.96f, 0.98f, 1f, 1f);
            muted = designTokens != null
                ? designTokens.HudMuted
                : new Color(0.96f, 0.98f, 1f, 0.2f);
            danger = designTokens != null
                ? designTokens.HudDanger
                : new Color(0.92f, 0.18f, 0.14f, 1f);
        }

        public void SetHealthVisible(bool visible)
        {
            if (healthPanel != null && healthPanel.activeSelf != visible)
                healthPanel.SetActive(visible);
        }

        public void SetHealth(float current, float maximum)
        {
            maximum = Mathf.Max(0.01f, maximum);
            current = Mathf.Clamp(current, 0f, maximum);
            float normalized = current / maximum;

            if (healthFill != null)
            {
                Vector2 anchorMax = healthFill.anchorMax;
                anchorMax.x = normalized;
                healthFill.anchorMax = anchorMax;
            }

            if (healthFillImage != null)
            {
                healthFillImage.color = healthSegments != null
                    && healthSegments.Length > 0
                        ? Color.clear
                        : Color.Lerp(danger, primary, normalized);
            }

            if (healthSegments != null && healthSegments.Length > 0)
            {
                int activeCount = normalized <= 0f
                    ? 0
                    : Mathf.Clamp(
                        Mathf.CeilToInt(normalized * healthSegments.Length),
                        1,
                        healthSegments.Length);
                Color activeColor = normalized <= 0.25f ? danger : primary;
                for (int i = 0; i < healthSegments.Length; i++)
                {
                    if (healthSegments[i] != null)
                    {
                        healthSegments[i].SetFrontColor(
                            i < activeCount ? activeColor : muted);
                    }
                }
            }

            if (healthValueLabel != null)
            {
                healthValueLabel.SetText(
                    "{0} / {1}",
                    Mathf.CeilToInt(current),
                    Mathf.CeilToInt(maximum));
            }
        }
    }
}
