using System;
using System.Collections.Generic;
using Supernova.Gameplay;
using Supernova.Inputs;
using Supernova.Infrastructure;
using Supernova.MinecraftCaves;
using Supernova.Missions;
using Supernova.Shop;
using Supernova.Voxels;
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

        [Header("Configuration")]
        [SerializeField] private UiDesignTokens designTokens;

        [Header("UGUI View")]
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private Canvas crosshairCanvas;
        [SerializeField] private RectTransform crosshairRoot;
        [SerializeField] private Image crosshairHorizontal;
        [SerializeField] private Image crosshairVertical;
        [SerializeField] private Image crosshairCenter;
        [SerializeField] private GameObject healthPanel;
        [SerializeField] private RectTransform healthFill;
        [SerializeField] private Image healthFillImage;
        [SerializeField] private TMP_Text healthValueLabel;
        [SerializeField] private TMP_Text magnetForceLabel;
        [SerializeField] private GameObject hotbarRoot;
        [SerializeField] private TMP_Text hotbarActionHintsLabel;
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
                [SerializeField] private Button pauseControlsButton;
        [SerializeField] private InputBindingSettingsView inputBindingSettingsView;

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

        [Header("Crosshair Info")]
        [SerializeField] private Canvas crosshairInfoCanvas;
        [SerializeField] private CrosshairInfoDisplay crosshairInfoDisplay;

        [Header("Debug View")]
        [SerializeField] private Canvas debugCanvas;
        [SerializeField] private GameObject fpsDebugWindow;
        [SerializeField] private TMP_Text fpsDebugValueLabel;
        [SerializeField, Min(0.05f)] private float fpsRefreshInterval = 0.25f;

        private IDamageable healthSource;
        private VoxelPlayerController healthEventSource;
        private PlayerToolController inventorySource;
        private VoxelPlayerController toolCooldownSource;
        private GameHudPresenter presenter;
        private HotbarPresenter hotbarPresenter;
        private MinecraftCaveInfiniteWorld loadingSource;
        private FirstPersonMagnetInteractor magnetAttractor;
        private float magnetCrosshairBlend;
        private bool magnetCrosshairTargetAvailable;
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
        private bool mainMenuSettingsOpen;
        private Action mainMenuSettingsClosed;
        private AngledPanelGraphic[] healthSegments = new AngledPanelGraphic[0];
        private readonly Image[] hotbarSlotBackgrounds = new Image[PlayerInventory.SlotCount];
        private readonly Outline[] hotbarSlotOutlines = new Outline[PlayerInventory.SlotCount];
        private readonly Image[] hotbarItemIcons = new Image[PlayerInventory.SlotCount];
        private readonly TMP_Text[] hotbarItemLabels = new TMP_Text[PlayerInventory.SlotCount];
        private readonly HotbarCooldownOverlayGraphic[] hotbarCooldownOverlays =
            new HotbarCooldownOverlayGraphic[PlayerInventory.SlotCount];
        private readonly TMP_Text[] hotbarCooldownLabels =
            new TMP_Text[PlayerInventory.SlotCount];
        private static readonly GameInputActionId[] HotbarActionIds =
        {
            GameInputActionId.Hotbar1,
            GameInputActionId.Hotbar2,
            GameInputActionId.Hotbar3,
            GameInputActionId.Hotbar4,
            GameInputActionId.Hotbar5,
        };
        private float fpsAccumulatedTime;
        private int fpsAccumulatedFrames;
        private const string FullscreenPreferenceKey = "ui.fullscreen";
        private const string VolumePreferenceKey = "ui.master-volume";
        /// <summary>
        /// Crosshair tint while the magnet has something to grab. The crosshair is
        /// only two pixels thick, so a pale gold reads as warm white; this is heavily
        /// saturated on purpose.
        /// </summary>
        private static readonly Color MagnetTargetCrosshairColor =
            new Color(1f, 0.55f, 0f, 1f);
        /// <summary>
        /// Scale applied on top of the tint. Hue alone is hard to judge on a 2px
        /// shape, so the crosshair also grows to signal the state change.
        /// </summary>
        private const float MagnetTargetCrosshairScale = 1.6f;
        private const float CrosshairStateBlendSpeed = 14f;

        public Canvas RootCanvas => rootCanvas;
        public Canvas CrosshairCanvas => crosshairCanvas;
        public HeadingCompass Compass => headingCompass;
        public Canvas PauseCanvas => pauseCanvas;
        public Canvas LoadingCanvas => loadingCanvas;
        public Canvas MissionOverlayCanvas => missionOverlayCanvas;
        public MissionUiView MissionView => missionView;
        public TMP_Text MissionTimerValueLabel => missionTimerValueLabel;
        public TMP_Text MagnetForceLabel => magnetForceLabel;
        public UiDesignTokens DesignTokens => designTokens;
        public bool IsPauseMenuVisible => pausePanel != null && pausePanel.activeSelf;
        public bool IsMainMenuSettingsVisible =>
            mainMenuSettingsOpen
            && pausePanel != null
            && pausePanel.activeSelf;
        public EquipmentLoadoutMenu EquipmentMenu => equipmentMenu;
        public bool IsEquipmentMenuVisible =>
            equipmentMenu != null && equipmentMenu.IsOpen;
        public bool IsLoadingVisible => loadingPanel != null && loadingPanel.activeSelf;
        public CrosshairInfoDisplay CrosshairInfo => crosshairInfoDisplay;
        public Canvas DebugCanvas => debugCanvas;
        public TMP_Text FpsDebugValueLabel => fpsDebugValueLabel;
        public bool IsFpsDebugVisible =>
            fpsDebugWindow != null && fpsDebugWindow.activeSelf;
        public IDamageable HealthSource =>
            IsHealthSourceValid(healthSource) ? healthSource : null;
        public PlayerToolController InventorySource => inventorySource;
        public static bool IsPauseMenuOpen => pauseOwner != null && pauseOwner.pauseMenuOpen;
        public static bool IsModalMenuOpen =>
            IsPauseMenuOpen
            || EquipmentLoadoutMenu.IsAnyOpen
            || NewGameGuideOverlay.IsOpen;
        public static bool IsGameplayInputBlocked =>
            IsModalMenuOpen
            || MainMenuController.IsIntegratedMenuActive
            || Time.frameCount <= gameplayInputBlockedThroughFrame;
        public bool CanPauseGame =>
            isActiveAndEnabled
            && !IsEquipmentMenuVisible
            && !NewGameGuideOverlay.IsOpen
            && !IsMainMenuActive()
            && !MissionGameLoop.IsSceneTransitioning
            && !IsLoadingBlockingPause();
        public bool CanOpenEquipmentMenu =>
            isActiveAndEnabled
            && !pauseMenuOpen
            && !NewGameGuideOverlay.IsOpen
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
            if (scene.name == mainMenuSceneName
                && !MainMenuController.IsIntegratedHomeScene(scene))
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
            EnsureView();
        }

        private void OnEnable()
        {
            PlayerToolController.InstanceEnabled -=
                HandlePlayerToolControllerEnabled;
            PlayerToolController.InstanceEnabled +=
                HandlePlayerToolControllerEnabled;
            PlayerToolController.InstanceDisabled -=
                HandlePlayerToolControllerDisabled;
            PlayerToolController.InstanceDisabled +=
                HandlePlayerToolControllerDisabled;
            MinecraftCaveInfiniteWorld.InstanceEnabled -=
                HandleLoadingSourceEnabled;
            MinecraftCaveInfiniteWorld.InstanceEnabled +=
                HandleLoadingSourceEnabled;
            MinecraftCaveInfiniteWorld.InstanceDisabled -=
                HandleLoadingSourceDisabled;
            MinecraftCaveInfiniteWorld.InstanceDisabled +=
                HandleLoadingSourceDisabled;
            FirstPersonMagnetInteractor.InstanceEnabled -=
                HandleMagnetAttractorEnabled;
            FirstPersonMagnetInteractor.InstanceEnabled +=
                HandleMagnetAttractorEnabled;
            FirstPersonMagnetInteractor.InstanceDisabled -=
                HandleMagnetAttractorDisabled;
            FirstPersonMagnetInteractor.InstanceDisabled +=
                HandleMagnetAttractorDisabled;
            PlayerEconomy.UpgradeOwnershipChanged -=
                HandleHudUpgradeOwnershipChanged;
            PlayerEconomy.UpgradeOwnershipChanged +=
                HandleHudUpgradeOwnershipChanged;
            ResetFpsDebugCounter();
            RebindSceneSources();
        }

        private void OnDisable()
        {
            PlayerToolController.InstanceEnabled -=
                HandlePlayerToolControllerEnabled;
            PlayerToolController.InstanceDisabled -=
                HandlePlayerToolControllerDisabled;
            MinecraftCaveInfiniteWorld.InstanceEnabled -=
                HandleLoadingSourceEnabled;
            MinecraftCaveInfiniteWorld.InstanceDisabled -=
                HandleLoadingSourceDisabled;
            FirstPersonMagnetInteractor.InstanceEnabled -=
                HandleMagnetAttractorEnabled;
            FirstPersonMagnetInteractor.InstanceDisabled -=
                HandleMagnetAttractorDisabled;
            PlayerEconomy.UpgradeOwnershipChanged -=
                HandleHudUpgradeOwnershipChanged;
            ResumeGame();
            equipmentMenu?.Close();
            BindMagnetAttractor(null);
            BindLoadingSource(null);
            BindHealthSource(null);
            BindInventorySource(null);
        }

        private void OnDestroy()
        {
            if (runtimeHud == this)
                runtimeHud = null;
        }

        private void Update()
        {
            if (GameInput.Pressed(GameInputActionId.DebugHud))
                ToggleFpsDebugWindow();

            UpdateFpsDebugWindow(Time.unscaledDeltaTime);

            if (pauseMenuOpen && !CanPauseGame)
                ResumeGame();

            if (IsEquipmentMenuVisible && !CanOpenEquipmentMenu)
                equipmentMenu.Close();

            if (GameInput.Pressed(GameInputActionId.Pause))
            {
                if (IsEquipmentMenuVisible)
                    equipmentMenu.Close();
                else
                    TogglePauseMenu();
            }

            if (GameInput.Pressed(GameInputActionId.ToggleLoadout))
            {
                ToggleEquipmentMenu();
            }

            AnimateLoading();
            if (crosshairInfoDisplay != null
                && crosshairInfoCanvas != null
                && crosshairInfoCanvas.isActiveAndEnabled)
            {
                crosshairInfoDisplay.Refresh();
            }
            AnimateMagnetCrosshairTint();
        }

        /// <summary>
        /// Highlights the crosshair while right click would actually latch onto
        /// something. The crosshair is a thin two-pixel cross, so colour alone is hard
        /// to read: it also scales up and brightens its outline into a glow.
        /// </summary>
        private void AnimateMagnetCrosshairTint()
        {
            if (crosshairCanvas == null
                || !crosshairCanvas.isActiveAndEnabled
                || !ResolveCrosshairArms())
            {
                return;
            }

            float targetBlend =
                magnetCrosshairTargetAvailable ? 1f : 0f;
            if (Mathf.Approximately(
                    magnetCrosshairBlend,
                    targetBlend))
            {
                return;
            }

            magnetCrosshairBlend = Mathf.MoveTowards(
                magnetCrosshairBlend,
                targetBlend,
                CrosshairStateBlendSpeed * Time.unscaledDeltaTime);
            ApplyMagnetCrosshairVisual();
        }

        private void ApplyMagnetCrosshairVisual()
        {
            Color tint = Color.Lerp(
                Color.white,
                MagnetTargetCrosshairColor,
                magnetCrosshairBlend);
            float scale = Mathf.Lerp(
                1f,
                MagnetTargetCrosshairScale,
                magnetCrosshairBlend);
            crosshairRoot.localScale = new Vector3(scale, scale, 1f);
            ApplyCrosshairArmState(crosshairHorizontal, tint);
            ApplyCrosshairArmState(crosshairVertical, tint);
            if (crosshairCenter != null)
                crosshairCenter.color = tint;
        }

        private void HandleMagnetTargetAvailabilityChanged(
            bool available)
        {
            magnetCrosshairTargetAvailable = available;
        }

        private static void ApplyCrosshairArmState(Image arm, Color tint)
        {
            if (arm == null) return;

            arm.color = tint;
            arm.rectTransform.localScale = Vector3.one;
            Outline outline = arm.GetComponent<Outline>();
            // UGUI Outline draws four displaced copies of the source mesh. When the
            // crosshair is scaled those copies look like several overlapping crosses,
            // not a thicker contour, so this effect must not be used here.
            if (outline != null)
                outline.enabled = false;
        }

        private bool ResolveCrosshairArms()
        {
            if (crosshairRoot != null
                && crosshairHorizontal != null
                && crosshairVertical != null)
                return true;

            crosshairRoot = transform.Find(
                UiHierarchyPaths.Hud.Crosshair) as RectTransform;
            Transform horizontal =
                transform.Find(UiHierarchyPaths.Hud.CrosshairHorizontal);
            if (horizontal != null)
                crosshairHorizontal = horizontal.GetComponent<Image>();
            Transform vertical =
                transform.Find(UiHierarchyPaths.Hud.CrosshairVertical);
            if (vertical != null)
                crosshairVertical = vertical.GetComponent<Image>();
            if (crosshairRoot != null)
            {
                Transform center = crosshairRoot.Find(
                    UiHierarchyPaths.Decoration.Center);
                if (center != null)
                    crosshairCenter = center.GetComponent<Image>();
            }
            return crosshairRoot != null
                && crosshairHorizontal != null
                && crosshairVertical != null;
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

        public void ToggleFpsDebugWindow()
        {
            SetFpsDebugVisible(!IsFpsDebugVisible);
        }

        public void SetFpsDebugVisible(bool visible)
        {
            if (fpsDebugWindow == null)
            {
                CacheViewReferences();
                if (fpsDebugWindow == null)
                    BuildFpsDebugView();
            }

            if (fpsDebugWindow == null)
                return;

            fpsDebugWindow.SetActive(visible);
            ResetFpsDebugCounter();
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

        public bool ShowMainMenuSettings(Action closed)
        {
            EnsureView();
            CachePauseMenuReferences();
            if (pauseCanvas == null
                || pausePanel == null
                || pauseSettingsPanel == null)
            {
                return false;
            }

            mainMenuSettingsOpen = true;
            mainMenuSettingsClosed = closed;
            pauseCanvas.gameObject.SetActive(true);
            pausePanel.SetActive(true);
            pausePresentation = pausePanel.GetComponent<PauseMenuPresentation>();
            if (pausePresentation == null)
                pausePresentation = pausePanel.AddComponent<PauseMenuPresentation>();
            ShowPauseSettings();
            pausePresentation.PlayIntro();
            return true;
        }

        public void HideMainMenuSettings()
        {
            HideMainMenuSettings(false);
        }

        public void ResumeGame()
        {
            HideMainMenuSettings(false);
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
            return FindObjectOfType<MainMenuController>(true) != null;
        }

        internal void SetMainMenuPresentationActive(bool active)
        {
            SetGameplayViewVisible(!active);
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
            if (crosshairInfoCanvas != null)
                crosshairInfoCanvas.gameObject.SetActive(visible);
            if (debugCanvas != null)
                debugCanvas.gameObject.SetActive(visible);

            if (!visible)
            {
                crosshairInfoDisplay?.HideImmediate();
                BindMagnetAttractor(null);
                BindLoadingSource(null);
                BindHealthSource(null);
                BindInventorySource(null);
                return;
            }

            RebindSceneSources();
        }

        private void RebindSceneSources()
        {
            MinecraftCaveInfiniteWorld world =
                FindObjectOfType<MinecraftCaveInfiniteWorld>();
            BindLoadingSource(world);

            IDamageable configuredHealthSource =
                healthSourceOverride as IDamageable;
            BindHealthSource(IsHealthSourceValid(configuredHealthSource)
                ? configuredHealthSource
                : FindPlayerHealthSource());
            BindInventorySource(inventorySourceOverride != null
                ? inventorySourceOverride
                : FindPlayerInventorySource());
            FirstPersonMagnetInteractor playerMagnet =
                inventorySource != null
                    ? inventorySource.GetComponent<
                        FirstPersonMagnetInteractor>()
                    : null;
            BindMagnetAttractor(
                playerMagnet != null
                    ? playerMagnet
                    : FindObjectOfType<FirstPersonMagnetInteractor>());
            if (crosshairInfoDisplay != null)
            {
                crosshairInfoDisplay.BindTerrainSource(
                    world != null ? world : FindVoxelTerrainSource());
            }
        }

        public void BindLoadingSource(MinecraftCaveInfiniteWorld source)
        {
            if (loadingSource != null)
            {
                loadingSource.InitialLoadProgressChanged -=
                    HandleInitialLoadProgressChanged;
            }

            loadingSource = source;
            if (loadingSource != null)
            {
                loadingSource.InitialLoadProgressChanged +=
                    HandleInitialLoadProgressChanged;
            }
            displayedLoadingStage = (MinecraftCaveGenerationStage)(-1);
            displayedLoadingPercent = -1;
            RefreshLoadingView();
        }

        public void BindHealthSource(IDamageable source)
        {
            if (healthEventSource != null)
                healthEventSource.HealthChanged -= HandleHealthChanged;

            healthSource = source;
            healthEventSource = source as VoxelPlayerController;
            if (healthEventSource != null)
                healthEventSource.HealthChanged += HandleHealthChanged;
            displayedCurrentHealth = float.NaN;
            displayedMaximumHealth = float.NaN;
            RefreshHealthView();
        }

        public void BindInventorySource(PlayerToolController source)
        {
            if (inventorySource != null)
            {
                inventorySource.SelectionChanged -= HandleInventorySelectionChanged;
                inventorySource.LoadoutChanged -= HandleLoadoutChanged;
            }
            if (toolCooldownSource != null)
            {
                toolCooldownSource.ToolActionCooldownChanged -=
                    HandleToolActionCooldownChanged;
                toolCooldownSource.ToolActionCooldownsCleared -=
                    HandleToolActionCooldownsCleared;
            }

            inventorySource = source;
            toolCooldownSource = inventorySource != null
                ? inventorySource.GetComponent<VoxelPlayerController>()
                : null;
            if (inventorySource != null)
            {
                inventorySource.SelectionChanged += HandleInventorySelectionChanged;
                inventorySource.LoadoutChanged += HandleLoadoutChanged;
            }
            if (toolCooldownSource != null)
            {
                toolCooldownSource.ToolActionCooldownChanged +=
                    HandleToolActionCooldownChanged;
                toolCooldownSource.ToolActionCooldownsCleared +=
                    HandleToolActionCooldownsCleared;
            }
            equipmentMenu?.BindInventory(inventorySource);
            displayedSlotIndex = -1;
            RefreshHotbar();
        }

        private void BindMagnetAttractor(
            FirstPersonMagnetInteractor source)
        {
            if (magnetAttractor != null)
            {
                magnetAttractor.TargetAvailabilityChanged -=
                    HandleMagnetTargetAvailabilityChanged;
            }

            magnetAttractor = source;
            magnetCrosshairTargetAvailable = false;
            if (magnetAttractor != null)
            {
                magnetAttractor.TargetAvailabilityChanged +=
                    HandleMagnetTargetAvailabilityChanged;
                magnetAttractor.RefreshTargetAvailability();
            }
            RefreshMagnetForceView();
        }

        public void RefreshNow()
        {
            RefreshHealthView();
            RefreshMagnetForceView();
            RefreshHotbar();
            RefreshLoadingView();
        }

        private void RefreshMagnetForceView()
        {
            if (magnetForceLabel == null)
                return;

            bool hasMagnet = magnetAttractor != null;
            magnetForceLabel.gameObject.SetActive(hasMagnet);
            if (hasMagnet)
            {
                magnetForceLabel.text = FormatMagnetForceLabel(
                    magnetAttractor.AttractionForce);
            }
        }

        public static string FormatMagnetForceLabel(float force)
        {
            float safeForce = float.IsNaN(force) || float.IsInfinity(force)
                ? 0f
                : Mathf.Max(0f, force);
            return "当前最大磁力："
                + safeForce.ToString(
                    "0.#",
                    System.Globalization.CultureInfo.InvariantCulture)
                + "N";
        }

        private void RefreshHealthView()
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
        }

        private void RefreshHotbar()
        {
            RefreshHotbarActionHints();
            if (hotbarPresenter == null) return;
            int slotIndex = inventorySource != null ? inventorySource.SelectedSlotIndex : 0;
            hotbarPresenter.SetInventory(inventorySource);
            RefreshHotbarCooldowns();
            displayedSlotIndex = slotIndex;
            hotbarPresenter.SetSelectedSlot(slotIndex);
        }

        private void RefreshHotbarActionHints()
        {
            if (hotbarActionHintsLabel == null)
                return;

            PlayerToolDefinition selected = inventorySource != null
                ? inventorySource.SelectedDefinition
                : null;
            string primaryHint = selected != null
                ? selected.PrimaryActionHint
                : string.Empty;
            string value = string.Empty;
            if (!string.IsNullOrWhiteSpace(primaryHint))
            {
                value = InputPromptResolver.Token(
                        GameInputActionId.PrimaryAction)
                    + "  "
                    + primaryHint
                    + "\n";
            }

            value += InputPromptResolver.Token(
                    GameInputActionId.SecondaryAction)
                + "  牵引\n"
                + InputPromptResolver.Token(GameInputActionId.Crouch)
                + "  蹲下\n"
                + InputPromptResolver.Token(GameInputActionId.ToggleLoadout)
                + "  打开背包";
            InputPromptTextRuntime.SetText(hotbarActionHintsLabel, value);
        }

        private bool RefreshHotbarCooldowns()
        {
            if (hotbarPresenter == null)
                return false;

            if (toolCooldownSource == null && inventorySource != null)
            {
                toolCooldownSource =
                    inventorySource.GetComponent<VoxelPlayerController>();
            }

            bool hasActiveCooldown = false;
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                float remainingSeconds = 0f;
                float durationSeconds = 0f;
                PlayerInventoryItem item = inventorySource != null
                    ? inventorySource.GetItemAtSlot(i)
                    : PlayerInventoryItem.Empty;
                if (toolCooldownSource != null)
                {
                    bool active = toolCooldownSource.TryGetToolActionCooldown(
                        item,
                        out remainingSeconds,
                        out durationSeconds);
                    hasActiveCooldown |= active;
                }

                hotbarPresenter.SetCooldown(
                    i,
                    remainingSeconds,
                    durationSeconds);
            }
            return hasActiveCooldown;
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
            if (crosshairInfoCanvas != null)
                crosshairInfoCanvas.gameObject.SetActive(false);
            if (debugCanvas != null)
                debugCanvas.gameObject.SetActive(false);
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

            float contentStep = loadingFadeDuration > 0f
                ? Mathf.Max(0f, deltaTime) / loadingFadeDuration
                : 1f;
            float backdropStep = LoadingTransitionDuration > 0f
                ? Mathf.Max(0f, deltaTime) / LoadingTransitionDuration
                : 1f;
            if (loadingRequestedVisible)
            {
                if (!loadingPanel.activeSelf) loadingPanel.SetActive(true);
                loadingFadeGroup.alpha = 1f;
                loadingContentGroup.alpha = Mathf.MoveTowards(
                    loadingContentGroup.alpha, 1f, contentStep);
                return;
            }

            if (!loadingPanel.activeSelf)
            {
                return;
            }

            // Drop the loading copy quickly, then let the dark backdrop linger as
            // the screen-to-gameplay transition instead of an abrupt cut.
            loadingContentGroup.alpha = Mathf.MoveTowards(
                loadingContentGroup.alpha, 0f, contentStep);
            loadingFadeGroup.alpha = Mathf.MoveTowards(
                loadingFadeGroup.alpha, 0f, backdropStep);
            if (loadingFadeGroup.alpha <= 0f)
            {
                loadingPanel.SetActive(false);
                loadingFadeGroup.alpha = 1f;
                loadingContentGroup.alpha = 0f;
            }
        }

        private float LoadingTransitionDuration => designTokens != null
            ? designTokens.LoadingTransitionSeconds
            : 3f;

        private static string GetLoadingStageLabel(MinecraftCaveGenerationStage stage)
        {
            switch (stage)
            {
                case MinecraftCaveGenerationStage.Terrain:
                    return "正在补充燃料……";
                case MinecraftCaveGenerationStage.Structures:
                    return "正在穿梭……";
                case MinecraftCaveGenerationStage.Meshes:
                    return "正在星际跃迁……";
                case MinecraftCaveGenerationStage.Ready:
                    return "就绪！";
                default:
                    return "正在整备……";
            }
        }

        private void HandleInventorySelectionChanged(int slotIndex, PlayerInventoryItem item)
        {
            displayedSlotIndex = slotIndex;
            hotbarPresenter?.SetSelectedSlot(slotIndex);
            RefreshHotbarActionHints();
        }

        private void HandleLoadoutChanged()
        {
            RefreshHotbar();
        }

        private void HandleHealthChanged(float current, float maximum)
        {
            if (presenter == null)
                return;
            displayedCurrentHealth = Mathf.Max(0f, current);
            displayedMaximumHealth = Mathf.Max(0.01f, maximum);
            presenter.SetHealthVisible(true);
            presenter.SetHealth(
                displayedCurrentHealth,
                displayedMaximumHealth);
        }

        private void HandleToolActionCooldownChanged(
            PlayerInventoryItem item,
            float remainingSeconds,
            float durationSeconds)
        {
            if (hotbarPresenter == null || inventorySource == null)
                return;

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                if (inventorySource.GetItemAtSlot(i) != item)
                    continue;
                hotbarPresenter.SetCooldown(
                    i,
                    remainingSeconds,
                    durationSeconds);
                return;
            }
        }

        private void HandleToolActionCooldownsCleared()
        {
            RefreshHotbarCooldowns();
        }

        private void HandleInitialLoadProgressChanged(
            MinecraftCaveGenerationStage stage,
            float progress,
            bool complete)
        {
            RefreshLoadingView();
        }

        private void HandlePlayerToolControllerEnabled(
            PlayerToolController source)
        {
            if (IsGameplayViewActive)
                RebindSceneSources();
        }

        private void HandlePlayerToolControllerDisabled(
            PlayerToolController source)
        {
            if (source != inventorySource)
                return;
            BindInventorySource(null);
            BindHealthSource(null);
            BindMagnetAttractor(null);
        }

        private void HandleLoadingSourceEnabled(
            MinecraftCaveInfiniteWorld source)
        {
            if (!IsGameplayViewActive)
                return;
            BindLoadingSource(source);
            crosshairInfoDisplay?.BindTerrainSource(source);
        }

        private void HandleLoadingSourceDisabled(
            MinecraftCaveInfiniteWorld source)
        {
            if (source != loadingSource)
                return;
            BindLoadingSource(null);
            crosshairInfoDisplay?.BindTerrainSource(null);
        }

        private void HandleMagnetAttractorEnabled(
            FirstPersonMagnetInteractor source)
        {
            PlayerToolController owner = source != null
                ? source.GetComponent<PlayerToolController>()
                : null;
            if (IsGameplayViewActive
                && (inventorySource == null || owner == inventorySource))
            {
                BindMagnetAttractor(source);
            }
        }

        private void HandleMagnetAttractorDisabled(
            FirstPersonMagnetInteractor source)
        {
            if (source == magnetAttractor)
                BindMagnetAttractor(null);
        }

        private void HandleHudUpgradeOwnershipChanged(
            PlayerUpgrade upgrade,
            bool owned)
        {
            if (upgrade == PlayerUpgrade.MagnetAttractionForce)
                RefreshMagnetForceView();
        }

        private bool IsGameplayViewActive =>
            rootCanvas != null
            && rootCanvas.gameObject.activeInHierarchy;

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
            BuildCrosshairInfoView();
            BuildFpsDebugView();
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
            else if (HotbarViewNeedsUpgrade())
            {
                BuildHotbarView((RectTransform)rootCanvas.transform);
            }
            if (headingCompass == null)
                BuildCompassView();
            if (magnetForceLabel == null && rootCanvas != null)
            {
                BuildMagnetForceView(
                    (RectTransform)rootCanvas.transform);
            }
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
                || transform.Find(UiHierarchyPaths.Pause.FullSettingsPanel) == null
                || transform.Find(UiHierarchyPaths.Pause.FullControls) == null
                || transform.Find(UiHierarchyPaths.Pause.FullInputBindingsPanel) == null;
            if (pauseCanvas == null || pausePanel == null || resumeButton == null
                || pauseViewNeedsUpgrade)
            {
                BuildPauseView();
            }

            EnsureEquipmentMenu();

            if (crosshairInfoCanvas == null || crosshairInfoDisplay == null)
                BuildCrosshairInfoView();

            if (debugCanvas == null || fpsDebugWindow == null
                || fpsDebugValueLabel == null)
            {
                BuildFpsDebugView();
            }

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

            Transform magnetForce = transform.Find(
                UiHierarchyPaths.Hud.MagnetForce);
            if (magnetForceLabel == null && magnetForce != null)
                magnetForceLabel = magnetForce.GetComponent<TMP_Text>();

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
            Transform hotbarActionHints = transform.Find(
                UiHierarchyPaths.Hud.HotbarActionHintsLabel);
            if (hotbarActionHintsLabel == null && hotbarActionHints != null)
            {
                hotbarActionHintsLabel =
                    hotbarActionHints.GetComponent<TMP_Text>();
            }

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

            Transform debugCanvasTransform = transform.Find(
                UiHierarchyPaths.Debug.Canvas);
            if (debugCanvas == null && debugCanvasTransform != null)
                debugCanvas = debugCanvasTransform.GetComponent<Canvas>();

            Transform debugWindow = transform.Find(
                UiHierarchyPaths.Debug.Window);
            if (fpsDebugWindow == null && debugWindow != null)
                fpsDebugWindow = debugWindow.gameObject;

            Transform fpsValue = transform.Find(
                UiHierarchyPaths.Debug.FpsValue);
            if (fpsDebugValueLabel == null && fpsValue != null)
                fpsDebugValueLabel = fpsValue.GetComponent<TMP_Text>();

            if (hotbar == null) return;

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                Transform slot = hotbar.Find(UiHierarchyPaths.Hud.SlotName(i + 1));
                if (slot == null) continue;
                hotbarSlotBackgrounds[i] = slot.GetComponent<Image>();
                hotbarSlotOutlines[i] = slot.GetComponent<Outline>();
                Transform itemIcon = slot.Find(UiHierarchyPaths.Hud.Icon);
                if (itemIcon != null)
                    hotbarItemIcons[i] = itemIcon.GetComponent<Image>();
                Transform itemLabel = slot.Find(UiHierarchyPaths.Hud.Item);
                if (itemLabel != null) hotbarItemLabels[i] = itemLabel.GetComponent<TMP_Text>();
                Transform cooldownOverlay = slot.Find(
                    UiHierarchyPaths.Hud.CooldownOverlay);
                if (cooldownOverlay != null)
                {
                    hotbarCooldownOverlays[i] = cooldownOverlay.GetComponent<
                        HotbarCooldownOverlayGraphic>();
                }
                Transform cooldownLabel = slot.Find(
                    UiHierarchyPaths.Hud.CooldownLabel);
                if (cooldownLabel != null)
                {
                    hotbarCooldownLabels[i] =
                        cooldownLabel.GetComponent<TMP_Text>();
                }
            }

            Transform crosshairInfoRoot = transform.Find(
                UiHierarchyPaths.Crosshair.Canvas);
            if (crosshairInfoCanvas == null && crosshairInfoRoot != null)
                crosshairInfoCanvas =
                    crosshairInfoRoot.GetComponent<Canvas>();
            if (crosshairInfoDisplay == null)
                crosshairInfoDisplay =
                    GetComponent<CrosshairInfoDisplay>();
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
                hotbarItemIcons,
                hotbarItemLabels,
                hotbarCooldownOverlays,
                hotbarCooldownLabels,
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
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(27f, 27f));
            CreateCrosshairBar("Horizontal", crosshair, new Vector2(27f, 3f));
            CreateCrosshairBar("Vertical", crosshair, new Vector2(3f, 27f));

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
            BuildMagnetForceView(rootRect);
            healthPanel.SetActive(designTokens == null || designTokens.ShowHealth);
            hotbarRoot.SetActive(designTokens == null || designTokens.ShowHotbar);
            crosshairRoot.gameObject.SetActive(
                designTokens == null || designTokens.ShowCrosshair);
        }

        private void BuildMagnetForceView(RectTransform rootRect)
        {
            Transform existing = rootRect.Find(
                UiHierarchyPaths.Hud.MagnetForceName);
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }

            Color primary = designTokens != null
                ? designTokens.HudPrimary
                : new Color(0.96f, 0.98f, 1f, 1f);
            magnetForceLabel = CreateText(
                UiHierarchyPaths.Hud.MagnetForceName,
                rootRect,
                "当前最大磁力：--N",
                TextAlignmentOptions.TopRight);
            SetAnchoredRect(
                (RectTransform)magnetForceLabel.transform,
                Vector2.one,
                Vector2.one,
                Vector2.one,
                new Vector2(-48f, -42f),
                new Vector2(420f, 42f));
            magnetForceLabel.fontSize = 21f;
            magnetForceLabel.fontStyle = FontStyles.Bold;
            magnetForceLabel.characterSpacing = 1.2f;
            magnetForceLabel.color = primary;
            magnetForceLabel.raycastTarget = false;
            Outline outline =
                magnetForceLabel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.76f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;
            RefreshMagnetForceView();
        }

        private void BuildFpsDebugView()
        {
            Transform existing = transform.Find(UiHierarchyPaths.Debug.Canvas);
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }

            Color primary = designTokens != null
                ? designTokens.HudPrimary
                : new Color(0.96f, 0.98f, 1f, 1f);
            Color surface = designTokens != null
                ? designTokens.HudSurface
                : new Color(0.035f, 0.045f, 0.055f, 0.9f);
            Color muted = designTokens != null
                ? designTokens.HudMuted
                : new Color(0.96f, 0.98f, 1f, 0.45f);

            RectTransform canvasRoot = CreateRect(
                UiHierarchyPaths.Debug.CanvasName,
                transform);
            debugCanvas = canvasRoot.gameObject.AddComponent<Canvas>();
            debugCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            debugCanvas.sortingOrder = designTokens != null
                ? designTokens.PauseSortingOrder + 100
                : 1200;

            CanvasScaler scaler =
                canvasRoot.gameObject.AddComponent<CanvasScaler>();
            ApplyCanvasPolicy(canvasRoot.gameObject, scaler);

            RectTransform window = CreateRect(
                UiHierarchyPaths.Debug.WindowName,
                canvasRoot);
            SetAnchoredRect(
                window,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -24f),
                new Vector2(210f, 66f));
            Image windowImage = window.gameObject.AddComponent<Image>();
            windowImage.color = new Color(
                surface.r,
                surface.g,
                surface.b,
                Mathf.Max(0.9f, surface.a));
            windowImage.raycastTarget = false;
            Outline outline = window.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(
                primary.r,
                primary.g,
                primary.b,
                0.22f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
            fpsDebugWindow = window.gameObject;

            RectTransform accent = CreateRect(
                UiHierarchyPaths.Debug.AccentName,
                window);
            SetAnchoredRect(
                accent,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0.5f),
                Vector2.zero,
                new Vector2(3f, 0f));
            Image accentImage = accent.gameObject.AddComponent<Image>();
            accentImage.color = primary;
            accentImage.raycastTarget = false;

            TMP_Text header = CreateText(
                UiHierarchyPaths.Debug.HeaderName,
                window,
                "{{input:Debug/Hud}}",
                TextAlignmentOptions.Left);
            SetAnchoredRect(
                (RectTransform)header.transform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                new Vector2(14f, -7f),
                new Vector2(-24f, 18f));
            header.fontSize = 10f;
            header.characterSpacing = 2f;
            header.color = new Color(
                muted.r,
                muted.g,
                muted.b,
                Mathf.Max(0.58f, muted.a));

            fpsDebugValueLabel = CreateText(
                UiHierarchyPaths.Debug.FpsValueName,
                window,
                "FPS  --",
                TextAlignmentOptions.Left);
            SetAnchoredRect(
                (RectTransform)fpsDebugValueLabel.transform,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 0f),
                new Vector2(14f, 7f),
                new Vector2(-24f, 34f));
            fpsDebugValueLabel.fontSize = 24f;
            fpsDebugValueLabel.characterSpacing = 1f;
            fpsDebugValueLabel.color = primary;

            fpsDebugWindow.SetActive(false);
            ResetFpsDebugCounter();
        }

        private void UpdateFpsDebugWindow(float unscaledDeltaTime)
        {
            if (!IsFpsDebugVisible || fpsDebugValueLabel == null
                || unscaledDeltaTime <= 0f)
            {
                return;
            }

            fpsAccumulatedTime += unscaledDeltaTime;
            fpsAccumulatedFrames++;
            if (fpsAccumulatedTime < Mathf.Max(0.05f, fpsRefreshInterval))
                return;

            float framesPerSecond = fpsAccumulatedFrames / fpsAccumulatedTime;
            fpsDebugValueLabel.SetText("FPS  {0:0}", framesPerSecond);
            fpsAccumulatedTime = 0f;
            fpsAccumulatedFrames = 0;
        }

        private void ResetFpsDebugCounter()
        {
            fpsAccumulatedTime = 0f;
            fpsAccumulatedFrames = 0;
            if (fpsDebugValueLabel != null)
                fpsDebugValueLabel.text = "FPS  --";
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
            prompt.gameObject.SetActive(false);

            TMP_Text evacuationPrompt = CreateText(
                UiHierarchyPaths.Mission.EarlyEvacuationPromptName,
                missionRoot,
                string.Empty,
                TextAlignmentOptions.Bottom);
            SetAnchoredRect(
                (RectTransform)evacuationPrompt.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                designTokens != null
                    ? designTokens.MissionPromptPosition
                    : new Vector2(0f, 112f),
                designTokens != null
                    ? designTokens.MissionPromptSize
                    : new Vector2(1100f, 70f));
            evacuationPrompt.fontSize = designTokens != null
                ? designTokens.MissionPromptFontSize
                : 25f;
            evacuationPrompt.color = designTokens != null
                ? designTokens.TextPrimary
                : new Color(0.82f, 0.96f, 1f);
            evacuationPrompt.enableWordWrapping = true;
            evacuationPrompt.gameObject.SetActive(false);

            RectTransform evacuationProgress = CreateRect(
                UiHierarchyPaths.Mission.EarlyEvacuationProgressName,
                missionRoot);
            SetAnchoredRect(
                evacuationProgress,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 168f),
                new Vector2(420f, 10f));
            Image evacuationProgressTrack =
                evacuationProgress.gameObject.AddComponent<Image>();
            Color progressTrackColor = designTokens != null
                ? designTokens.HudMuted
                : new Color(0.96f, 0.98f, 1f, 0.2f);
            evacuationProgressTrack.color = new Color(
                progressTrackColor.r,
                progressTrackColor.g,
                progressTrackColor.b,
                Mathf.Max(0.24f, progressTrackColor.a));
            evacuationProgressTrack.raycastTarget = false;

            RectTransform evacuationProgressFill = CreateRect(
                UiHierarchyPaths.Mission.EarlyEvacuationProgressFillName,
                evacuationProgress);
            evacuationProgressFill.anchorMin = Vector2.zero;
            evacuationProgressFill.anchorMax = new Vector2(0f, 1f);
            evacuationProgressFill.pivot = new Vector2(0f, 0.5f);
            evacuationProgressFill.offsetMin = Vector2.zero;
            evacuationProgressFill.offsetMax = Vector2.zero;
            Image evacuationProgressFillImage =
                evacuationProgressFill.gameObject.AddComponent<Image>();
            evacuationProgressFillImage.color = designTokens != null
                ? designTokens.HudPrimary
                : new Color(0.96f, 0.98f, 1f, 1f);
            evacuationProgressFillImage.raycastTarget = false;
            evacuationProgress.gameObject.SetActive(false);

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
                "剩余时间",
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
            Canvas sceneTransitionCanvas = fadeRect.gameObject.AddComponent<Canvas>();
            sceneTransitionCanvas.overrideSorting = true;
            sceneTransitionCanvas.sortingOrder = designTokens != null
                ? designTokens.SceneTransitionSortingOrder
                : 2000;
            fadeRect.gameObject.AddComponent<GraphicRaycaster>();
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
                evacuationPrompt,
                evacuationProgress.gameObject,
                evacuationProgressFill,
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

            TMP_Text title = CreateText(
                "Title", content, "加载中", TextAlignmentOptions.Center);
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
                "Status", content, "正在整备……", TextAlignmentOptions.Center);
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
                "Hint", content, "准备安全降落……", TextAlignmentOptions.Center);
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
            inputBindingSettingsView = InputBindingSettingsView.Create(
                menu,
                ShowPauseSettings);

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
            CreatePauseHeader(parent, "游戏暂停", systemInk);

            resumeButton = CreatePauseMenuButton(
                UiHierarchyPaths.Pause.Resume,
                parent,
                1,
                "返回",
                "{{input:UI/Pause}}",
                -174f,
                buttonInk,
                buttonDivider);
            pauseSettingsButton = CreatePauseMenuButton(
                UiHierarchyPaths.Pause.Settings,
                parent,
                2,
                "设置",
                string.Empty,
                -260f,
                buttonInk,
                buttonDivider);
            quitToMenuButton = CreatePauseMenuButton(
                UiHierarchyPaths.Pause.QuitToMenu,
                parent,
                3,
                "返回主菜单",
                string.Empty,
                -346f,
                buttonInk,
                buttonDivider);
            quitToDesktopButton = CreatePauseMenuButton(
                UiHierarchyPaths.Pause.QuitToDesktop,
                parent,
                4,
                "退出游戏",
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
            CreatePauseHeader(parent, "设置", systemInk);

            pauseFullscreenToggle = CreatePauseToggle(
                UiHierarchyPaths.Pause.Fullscreen,
                parent,
                "全屏",
                -186f,
                systemInk,
                systemDivider);
            pauseVolumeSlider = CreatePauseSlider(
                UiHierarchyPaths.Pause.MasterVolume,
                parent,
                "主音量",
                -282f,
                systemInk,
                systemDivider,
                out pauseVolumeValueLabel);
            pauseControlsButton = CreatePauseMenuButton(
                UiHierarchyPaths.Pause.Controls,
                parent,
                0,
                "控制",
                string.Empty,
                -392f,
                buttonInk,
                buttonDivider);
            TMP_Text controlsIndex = pauseControlsButton.transform
                .Find("Index")?.GetComponent<TMP_Text>();
            if (controlsIndex != null)
                controlsIndex.text = ">";

            pauseSettingsBackButton = CreatePauseMenuButton(
                UiHierarchyPaths.Pause.SettingsBack,
                parent,
                0,
                "返回",
                string.Empty,
                -486f,
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
                "",
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
                        inputBindingSettingsView = FindPauseComponent(
                UiHierarchyPaths.Pause.FullInputBindingsPanel,
                inputBindingSettingsView);
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
                        pauseControlsButton = FindPauseComponent(
                UiHierarchyPaths.Pause.FullControls,
                pauseControlsButton);
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
                        if (pauseControlsButton != null)
            {
                pauseControlsButton.onClick.RemoveListener(ShowInputBindings);
                pauseControlsButton.onClick.AddListener(ShowInputBindings);
            }
            if (pauseSettingsBackButton != null)
            {
                pauseSettingsBackButton.onClick.RemoveListener(
                    ShowPauseMainOptions);
                pauseSettingsBackButton.onClick.RemoveListener(
                    HandlePauseSettingsBack);
                pauseSettingsBackButton.onClick.AddListener(
                    HandlePauseSettingsBack);
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
            inputBindingSettingsView?.Hide();
            if (pauseSettingsPanel != null)
                pauseSettingsPanel.SetActive(true);
            LoadPauseSettings();
            if (EventSystem.current != null && pauseFullscreenToggle != null)
            {
                EventSystem.current.SetSelectedGameObject(
                    pauseFullscreenToggle.gameObject);
            }
        }

        private void ShowInputBindings()
        {
            if (pauseMainOptions != null)
                pauseMainOptions.SetActive(false);
            if (pauseSettingsPanel != null)
                pauseSettingsPanel.SetActive(false);
            inputBindingSettingsView?.Show();
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }


        private void ShowPauseMainOptions()
        {
            if (pauseMainOptions != null)
                pauseMainOptions.SetActive(true);
            if (pauseSettingsPanel != null)
                pauseSettingsPanel.SetActive(false);
            inputBindingSettingsView?.Hide();
            if (EventSystem.current != null && resumeButton != null)
                EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
        }

        private void HandlePauseSettingsBack()
        {
            if (!mainMenuSettingsOpen)
            {
                ShowPauseMainOptions();
                return;
            }

            HideMainMenuSettings(true);
        }

        private void HideMainMenuSettings(bool notifyClosed)
        {
            if (!mainMenuSettingsOpen)
                return;

            Action closed = mainMenuSettingsClosed;
            mainMenuSettingsOpen = false;
            mainMenuSettingsClosed = null;
            pausePresentation?.StopPresentation();
            inputBindingSettingsView?.Hide();
            if (pauseSettingsPanel != null)
                pauseSettingsPanel.SetActive(false);
            if (pauseMainOptions != null)
                pauseMainOptions.SetActive(true);
            if (pausePanel != null)
                pausePanel.SetActive(false);
            if (pauseCanvas != null)
                pauseCanvas.gameObject.SetActive(false);

            if (notifyClosed)
                closed?.Invoke();
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

            Time.timeScale = 1f;
            MissionGameLoop gameLoop = MissionGameLoop.Instance;
            if (gameLoop != null
                && gameLoop.BeginSceneLoadWithFade(mainMenuSceneName))
            {
                return;
            }

            ResumeGame();
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

        private void BuildCrosshairInfoView()
        {
            Transform existing = transform.Find(
                UiHierarchyPaths.Crosshair.Canvas);
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }

            Color overlayPrimary = designTokens != null
                ? designTokens.OverlayPrimary
                : Color.white;
            Color overlaySecondary = designTokens != null
                ? designTokens.OverlaySecondary
                : new Color(1f, 1f, 1f, 0.58f);
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

            RectTransform canvasRoot = CreateRect(
                UiHierarchyPaths.Crosshair.Canvas,
                transform);
            crosshairInfoCanvas = canvasRoot.gameObject.AddComponent<Canvas>();
            crosshairInfoCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            crosshairInfoCanvas.sortingOrder = 500;
            CanvasScaler scaler =
                canvasRoot.gameObject.AddComponent<CanvasScaler>();
            ApplyCanvasPolicy(canvasRoot.gameObject, scaler);

            GameObject panelObject = new GameObject("Info Panel");
            RectTransform panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.SetParent(canvasRoot, false);
            SetAnchoredRect(
                panelRect,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 180f),
                new Vector2(380f, 84f));

            AngledPanelGraphic panelGraphic =
                panelObject.AddComponent<AngledPanelGraphic>();
            float slant = designTokens != null
                ? designTokens.HudElementSlant * 1.2f
                : 10f;
            float depth = designTokens != null
                ? designTokens.HudExtrusionDepth * 0.6f
                : 3f;
            panelGraphic.Configure(
                slant,
                depth,
                surface,
                shadow,
                highlight,
                reverse);
            panelGraphic.raycastTarget = false;

            CanvasGroup panelGroup =
                panelObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;

            RectTransform rule = CreateRect(
                UiHierarchyPaths.Crosshair.RuleLine,
                panelRect);
            SetAnchoredRect(
                rule,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -2f),
                new Vector2(-16f, 1f));
            Image ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.color = new Color(
                overlayPrimary.r,
                overlayPrimary.g,
                overlayPrimary.b,
                0.24f);
            ruleImage.raycastTarget = false;

            TMP_Text nameLabel = CreateText(
                UiHierarchyPaths.Crosshair.NameLabel,
                panelRect,
                string.Empty,
                TextAlignmentOptions.Center);
            SetAnchoredRect(
                (RectTransform)nameLabel.transform,
                new Vector2(0f, 0.55f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -8f),
                new Vector2(-24f, 0f));
            nameLabel.fontSize = 20f;
            nameLabel.fontStyle = FontStyles.Bold;
            nameLabel.characterSpacing = 1.5f;
            nameLabel.color = overlayPrimary;
            nameLabel.enableWordWrapping = false;

            TMP_Text statsLabel = CreateText(
                UiHierarchyPaths.Crosshair.StatsLabel,
                panelRect,
                string.Empty,
                TextAlignmentOptions.Center);
            SetAnchoredRect(
                (RectTransform)statsLabel.transform,
                new Vector2(0f, 0f),
                new Vector2(1f, 0.55f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(-24f, 0f));
            statsLabel.fontSize = 15f;
            statsLabel.fontStyle = FontStyles.Normal;
            statsLabel.characterSpacing = 1f;
            statsLabel.color = overlaySecondary;
            statsLabel.enableWordWrapping = false;

            crosshairInfoDisplay =
                gameObject.AddComponent<CrosshairInfoDisplay>();
            crosshairInfoDisplay.NameLabel = nameLabel;
            crosshairInfoDisplay.StatsLabel = statsLabel;
            crosshairInfoDisplay.RootObject = panelObject;
            crosshairInfoDisplay.RootCanvasGroup = panelGroup;
            crosshairInfoDisplay.DesignTokens = designTokens;

            canvasRoot.gameObject.SetActive(
                designTokens == null || designTokens.ShowCrosshair);
        }

        private void BuildHotbarView(RectTransform rootRect)
        {
            Transform existing = rootRect.Find(
                UiHierarchyPaths.Hud.HotbarName);
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }
            Transform existingHints = rootRect.Find(
                UiHierarchyPaths.Hud.HotbarActionHintsName);
            if (existingHints != null)
            {
                if (Application.isPlaying) Destroy(existingHints.gameObject);
                else DestroyImmediate(existingHints.gameObject);
            }
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                hotbarSlotBackgrounds[i] = null;
                hotbarSlotOutlines[i] = null;
                hotbarItemIcons[i] = null;
                hotbarItemLabels[i] = null;
                hotbarCooldownOverlays[i] = null;
                hotbarCooldownLabels[i] = null;
            }
            hotbarActionHintsLabel = null;

            RectTransform hotbar = CreateRect(
                UiHierarchyPaths.Hud.HotbarName,
                rootRect);
            SetAnchoredRect(hotbar, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(272f, 56f));
            hotbarRoot = hotbar.gameObject;

            RectTransform hints = CreateRect(
                UiHierarchyPaths.Hud.HotbarActionHintsName,
                rootRect);
            hotbarActionHintsLabel = CreateText(
                UiHierarchyPaths.Hud.HotbarActionHintsLabelName,
                hints,
                string.Empty,
                TextAlignmentOptions.BottomRight);
            SetAnchoredRect(
                (RectTransform)hotbarActionHintsLabel.transform,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            hotbarActionHintsLabel.enableWordWrapping = false;
            Outline hintsOutline =
                hotbarActionHintsLabel.gameObject.AddComponent<Outline>();
            hintsOutline.effectColor = new Color(0f, 0f, 0f, 0.82f);
            hintsOutline.effectDistance = new Vector2(1f, -1f);
            hintsOutline.useGraphicAlpha = true;

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

                RectTransform iconRect = CreateRect(
                    UiHierarchyPaths.Hud.Icon,
                    slot);
                Image icon = iconRect.gameObject.AddComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = false;
                hotbarItemIcons[i] = icon;

                string keyText = InputPromptResolver.Token(
                    HotbarActionIds[i]);
                TMP_Text key = CreateText("Key", slot, keyText, TextAlignmentOptions.TopLeft);
                InputPromptTextRuntime.SetText(key, keyText);
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

        private bool HotbarViewNeedsUpgrade()
        {
            if (hotbarRoot == null || hotbarActionHintsLabel == null)
                return true;

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                if (hotbarRoot.transform.Find(
                        UiHierarchyPaths.Hud.SlotName(i + 1)) == null)
                {
                    return true;
                }
            }
            return false;
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
            float hudVisualScale = designTokens != null
                ? designTokens.HudVisualScale
                : 1.15f;
            Vector2 hotbarHintsOffset = designTokens != null
                ? designTokens.HudHotbarHintsOffset
                : new Vector2(0f, 14f);
            Vector2 hotbarHintsSize = designTokens != null
                ? designTokens.HudHotbarHintsSize
                : new Vector2(360f, 112f);
            float hotbarHintsFontSize = designTokens != null
                ? designTokens.HudHotbarHintsFontSize
                : 21f;
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
                panel.localScale = Vector3.one * hudVisualScale;
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
                hotbar.localScale = Vector3.one * hudVisualScale;

                RectTransform hints = transform.Find(
                    UiHierarchyPaths.Hud.HotbarActionHints)
                    as RectTransform;
                if (hints != null)
                {
                    SetAnchoredRect(
                        hints,
                        new Vector2(1f, 0f),
                        new Vector2(1f, 0f),
                        new Vector2(1f, 0f),
                        hotbarPosition
                            + new Vector2(
                                hotbarHintsOffset.x,
                                hotbarSize.y + hotbarHintsOffset.y),
                        hotbarHintsSize);
                    hints.localRotation = Quaternion.identity;
                    hints.localScale = Vector3.one * hudVisualScale;
                }
                if (hotbarActionHintsLabel != null)
                {
                    hotbarActionHintsLabel.fontSize =
                        hotbarHintsFontSize;
                    hotbarActionHintsLabel.fontStyle =
                        FontStyles.Bold;
                    hotbarActionHintsLabel.characterSpacing = 1.2f;
                    hotbarActionHintsLabel.lineSpacing = 4f;
                    hotbarActionHintsLabel.color = new Color(
                        primary.r,
                        primary.g,
                        primary.b,
                        0.82f);
                    hotbarActionHintsLabel.alignment =
                        TextAlignmentOptions.BottomRight;
                }

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

                    Image icon = EnsureHotbarItemIcon(slot, i);
                    if (icon != null)
                    {
                        SetAnchoredRect(
                            (RectTransform)icon.transform,
                            new Vector2(0.5f, 0.5f),
                            new Vector2(0.5f, 0.5f),
                            new Vector2(0.5f, 0.5f),
                            new Vector2(14f, 0f),
                            new Vector2(24f, 24f));
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
                        key.fontSize = 14f;
                        key.color = new Color(primary.r, primary.g, primary.b, 0.86f);
                        key.alignment = TextAlignmentOptions.TopLeft;
                    }

                    TMP_Text item = EnsureHotbarItemLabel(slot, i);
                    if (item != null)
                    {
                        RectTransform itemRect = (RectTransform)item.transform;
                        SetAnchoredRect(
                            itemRect,
                            Vector2.zero,
                            Vector2.one,
                            new Vector2(0.5f, 0.5f),
                            new Vector2(4f, -19f),
                            new Vector2(-8f, -36f));
                        item.fontSize = 10f;
                        item.characterSpacing = 1f;
                        item.color = primary;
                        item.alignment = TextAlignmentOptions.Center;
                    }

                    EnsureHotbarCooldownView(
                        slot,
                        i,
                        slant,
                        depth,
                        primary,
                        surfaceColor,
                        shadow,
                        hotbarReverseSlant);
                }
            }

            RectTransform compass = transform.Find(
                UiHierarchyPaths.Hud.Compass) as RectTransform;
            if (compass != null)
                compass.localScale = Vector3.one * hudVisualScale;

            RectTransform crosshair = transform.Find(
                UiHierarchyPaths.Hud.Crosshair) as RectTransform;
            if (crosshair != null)
            {
                crosshair.localScale = Vector3.one * hudVisualScale;
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

        private Image EnsureHotbarItemIcon(
            RectTransform slot,
            int slotIndex)
        {
            RectTransform iconRect = slot.Find(
                UiHierarchyPaths.Hud.Icon) as RectTransform;
            if (iconRect == null)
                iconRect = CreateRect(UiHierarchyPaths.Hud.Icon, slot);

            Image icon = iconRect.GetComponent<Image>();
            if (icon == null)
                icon = iconRect.gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            hotbarItemIcons[slotIndex] = icon;
            return icon;
        }

        private TMP_Text EnsureHotbarItemLabel(
            RectTransform slot,
            int slotIndex)
        {
            RectTransform itemRect = slot.Find(
                UiHierarchyPaths.Hud.Item) as RectTransform;
            TMP_Text item = itemRect != null
                ? itemRect.GetComponent<TMP_Text>()
                : null;
            if (item == null)
            {
                item = CreateText(
                    UiHierarchyPaths.Hud.Item,
                    slot,
                    string.Empty,
                    TextAlignmentOptions.Center);
            }
            hotbarItemLabels[slotIndex] = item;
            return item;
        }

        private void EnsureHotbarCooldownView(
            RectTransform slot,
            int slotIndex,
            float slant,
            float depth,
            Color primary,
            Color surface,
            Color shadow,
            bool reverse)
        {
            RectTransform overlayRect = slot.Find(
                UiHierarchyPaths.Hud.CooldownOverlay) as RectTransform;
            bool createdOverlay = overlayRect == null;
            if (overlayRect == null)
            {
                overlayRect = CreateRect(
                    UiHierarchyPaths.Hud.CooldownOverlay,
                    slot);
            }

            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.pivot = new Vector2(0.5f, 0.5f);
            overlayRect.offsetMin = new Vector2(1f, 1f);
            overlayRect.offsetMax = new Vector2(-1f, -1f);
            overlayRect.SetSiblingIndex(Mathf.Min(1, slot.childCount - 1));

            HotbarCooldownOverlayGraphic overlay =
                overlayRect.GetComponent<HotbarCooldownOverlayGraphic>();
            if (overlay == null)
            {
                overlay = overlayRect.gameObject.AddComponent<
                    HotbarCooldownOverlayGraphic>();
            }
            Color overlayColor = Color.Lerp(surface, primary, 0.38f);
            overlayColor.a = 0.88f;
            overlay.Configure(slant, depth, overlayColor, reverse);
            if (createdOverlay)
                overlay.gameObject.SetActive(false);
            hotbarCooldownOverlays[slotIndex] = overlay;

            RectTransform labelRect = slot.Find(
                UiHierarchyPaths.Hud.CooldownLabel) as RectTransform;
            bool createdLabel = labelRect == null;
            TMP_Text label;
            if (labelRect == null)
            {
                label = CreateText(
                    UiHierarchyPaths.Hud.CooldownLabel,
                    slot,
                    string.Empty,
                    TextAlignmentOptions.Center);
                labelRect = (RectTransform)label.transform;
            }
            else
            {
                label = labelRect.GetComponent<TMP_Text>();
            }

            if (label != null)
            {
                SetAnchoredRect(
                    labelRect,
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(0.5f, 0f),
                    new Vector2(0f, 4f),
                    new Vector2(-4f, 17f));
                label.fontSize = 12f;
                label.fontStyle = FontStyles.Bold;
                label.characterSpacing = 0.5f;
                label.color = primary;
                label.alignment = TextAlignmentOptions.Center;
                label.transform.SetAsLastSibling();
                Shadow labelShadow = label.GetComponent<Shadow>();
                if (labelShadow == null)
                    labelShadow = label.gameObject.AddComponent<Shadow>();
                labelShadow.effectColor = shadow;
                labelShadow.effectDistance = new Vector2(1f, -1f);
                labelShadow.useGraphicAlpha = true;
                if (createdLabel)
                    label.gameObject.SetActive(false);
            }
            hotbarCooldownLabels[slotIndex] = label;
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

        private static MonoBehaviour FindVoxelTerrainSource()
        {
            MonoBehaviour[] behaviours =
                FindObjectsOfType<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IVoxelTerrain)
                    return behaviours[i];
            }
            return null;
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
        private readonly Image[] itemIcons;
        private readonly TMP_Text[] itemLabels;
        private readonly TMP_Text[] keyLabels;
        private readonly HotbarCooldownOverlayGraphic[] cooldownOverlays;
        private readonly TMP_Text[] cooldownLabels;
        private readonly Color primary;
        private readonly Color surface;
        private readonly Color shadow;
        private readonly PlayerInventoryItem[] displayedItems =
            new PlayerInventoryItem[PlayerInventory.SlotCount];
        private readonly bool[] displayedItemsSuspended =
            new bool[PlayerInventory.SlotCount];
        private int selectedSlotIndex = -1;

        public HotbarPresenter(Image[] backgrounds, Outline[] outlines, TMP_Text[] itemLabels)
            : this(backgrounds, outlines, null, itemLabels, null, null, null)
        {
        }

        public HotbarPresenter(
            Image[] backgrounds,
            Outline[] outlines,
            Image[] itemIcons,
            TMP_Text[] itemLabels,
            HotbarCooldownOverlayGraphic[] cooldownOverlays,
            TMP_Text[] cooldownLabels,
            UiDesignTokens designTokens)
        {
            this.backgrounds = backgrounds;
            this.outlines = outlines;
            this.itemIcons = itemIcons;
            this.itemLabels = itemLabels;
            this.cooldownOverlays = cooldownOverlays;
            this.cooldownLabels = cooldownLabels;
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
            SetSelectedSlot(-1);
            SetItemLabels();
            ClearCooldowns();
        }

        public HotbarPresenter(
            Image[] backgrounds,
            Outline[] outlines,
            TMP_Text[] itemLabels,
            UiDesignTokens designTokens)
            : this(
                backgrounds,
                outlines,
                null,
                itemLabels,
                null,
                null,
                designTokens)
        {
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
                    angledSurfaces[i].gameObject.SetActive(true);
                    angledSurfaces[i].SetFrontColor(selected ? primary : surface);
                    angledSurfaces[i].SetDepthColor(shadow);
                }
                ApplyItemIcon(
                    i,
                    displayedItems[i],
                    selected,
                    displayedItemsSuspended[i]);
                ApplyLabelColor(i, selected, displayedItemsSuspended[i]);
            }
        }

        public void SetInventory(PlayerToolController source)
        {
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                PlayerInventoryItem item = source != null
                    ? source.GetDisplayItemAtSlot(i)
                    : PlayerInventory.GetDefaultItemAtSlot(i);
                bool suspended = source != null
                    && source.IsItemSuspendedAtSlot(i);
                displayedItems[i] = item;
                displayedItemsSuspended[i] = suspended;

                if (itemLabels != null
                    && i < itemLabels.Length
                    && itemLabels[i] != null)
                {
                    itemLabels[i].gameObject.SetActive(true);
                    itemLabels[i].text = GetDisplayLabel(item, suspended);
                    itemLabels[i].fontSize = suspended
                        || item == PlayerInventoryItem.Flashlight
                        || item == PlayerInventoryItem.SolidGun
                        || item == PlayerInventoryItem.PortalGun
                            ? 9f
                            : 11f;
                    itemLabels[i].lineSpacing = suspended ? -8f : 0f;
                }

                bool selected = i == selectedSlotIndex;
                ApplyItemIcon(i, item, selected, suspended);
                ApplyLabelColor(i, selected, suspended);
            }
        }

        private void SetItemLabels()
        {
            SetInventory(null);
        }

        public void SetCooldown(
            int slotIndex,
            float remainingSeconds,
            float durationSeconds)
        {
            if (slotIndex < 0 || slotIndex >= PlayerInventory.SlotCount)
                return;

            bool active = remainingSeconds > 0f && durationSeconds > 0f;
            float fillAmount = active
                ? Mathf.Clamp01(remainingSeconds / durationSeconds)
                : 0f;
            if (cooldownOverlays != null
                && slotIndex < cooldownOverlays.Length
                && cooldownOverlays[slotIndex] != null)
            {
                cooldownOverlays[slotIndex].SetFillAmount(fillAmount);
                cooldownOverlays[slotIndex].gameObject.SetActive(active);
            }

            if (cooldownLabels != null
                && slotIndex < cooldownLabels.Length
                && cooldownLabels[slotIndex] != null)
            {
                cooldownLabels[slotIndex].text = active
                    ? FormatCooldownSeconds(remainingSeconds)
                    : string.Empty;
                cooldownLabels[slotIndex].gameObject.SetActive(active);
            }
        }

        public static string FormatCooldownSeconds(float remainingSeconds)
        {
            float displayedSeconds = Mathf.Ceil(
                Mathf.Max(0f, remainingSeconds) * 10f) / 10f;
            return displayedSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                + "s";
        }

        private void ClearCooldowns()
        {
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
                SetCooldown(i, 0f, 0f);
        }

        private void ApplyItemIcon(
            int index,
            PlayerInventoryItem item,
            bool selected,
            bool suspended)
        {
            if (itemIcons == null
                || index >= itemIcons.Length
                || itemIcons[index] == null)
            {
                return;
            }

            EquipmentIconCatalog catalog = GameAssetCatalog.Current != null
                && GameAssetCatalog.Current.UI != null
                    ? GameAssetCatalog.Current.UI.EquipmentIcons
                    : null;
            Sprite sprite = catalog != null ? catalog.GetIcon(item) : null;
            itemIcons[index].sprite = sprite;
            itemIcons[index].color = ResolveItemColor(selected, suspended);
            itemIcons[index].enabled =
                item != PlayerInventoryItem.Empty && sprite != null;
        }

        private void ApplyLabelColor(
            int index,
            bool selected,
            bool suspended)
        {
            Color labelColor = ResolveItemColor(selected, suspended);
            if (itemLabels != null && index < itemLabels.Length && itemLabels[index] != null)
                itemLabels[index].color = labelColor;
            if (keyLabels != null && index < keyLabels.Length && keyLabels[index] != null)
            {
                keyLabels[index].gameObject.SetActive(true);
                keyLabels[index].color = selected
                    ? new Color(0.025f, 0.03f, 0.035f, 0.92f)
                    : new Color(primary.r, primary.g, primary.b, 0.86f);
            }
        }

        private Color ResolveItemColor(bool selected, bool suspended)
        {
            if (suspended)
            {
                return selected
                    ? new Color(0.32f, 0.34f, 0.36f, 1f)
                    : new Color(0.55f, 0.57f, 0.59f, 1f);
            }
            return selected
                ? new Color(0.025f, 0.03f, 0.035f, 1f)
                : primary;
        }

        public static string GetDisplayLabel(
            PlayerInventoryItem item,
            bool suspended)
        {
            string label = GetItemLabel(item);
            return suspended && !string.IsNullOrEmpty(label)
                ? label + "\n已投掷"
                : label;
        }

        public static string GetItemLabel(PlayerInventoryItem item)
        {
            switch (item)
            {
                case PlayerInventoryItem.Pickaxe:
                    return "探险镐";
                case PlayerInventoryItem.Flashlight:
                    return "照明灯";
                case PlayerInventoryItem.SolidGun:
                    return "地形发生器";
                case PlayerInventoryItem.PortalGun:
                    return "传送门发生器";
                case PlayerInventoryItem.Bomb:
                    return "炸弹";
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
