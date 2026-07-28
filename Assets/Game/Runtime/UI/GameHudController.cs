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

        [Header("UGUI View")]
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private Canvas crosshairCanvas;
        [SerializeField] private GameObject healthPanel;
        [SerializeField] private RectTransform healthFill;
        [SerializeField] private Image healthFillImage;
        [SerializeField] private TMP_Text healthValueLabel;
        [SerializeField] private GameObject hotbarRoot;

        [Header("Pause Menu")]
        [SerializeField] private Canvas pauseCanvas;
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private Button resumeButton;

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
        private PlayerEquipmentController equipmentSource;
        private GameHudPresenter presenter;
        private HotbarPresenter hotbarPresenter;
        private float nextSourceSearchTime;
        private float nextInventorySourceSearchTime;
        private float nextEquipmentSourceSearchTime;
        private float nextWorldSourceSearchTime;
        private MinecraftCaveInfiniteWorld loadingSource;
        private bool loadingRequestedVisible;
        private float displayedCurrentHealth = float.NaN;
        private float displayedMaximumHealth = float.NaN;
        private int displayedSlotIndex = -1;
        private bool pauseMenuOpen;
        private float timeScaleBeforePause = 1f;
        private CursorLockMode cursorLockBeforePause;
        private bool cursorVisibleBeforePause;
        private static GameHudController pauseOwner;
        private PauseMenuPresentation pausePresentation;
        private readonly Image[] hotbarSlotBackgrounds = new Image[PlayerInventory.SlotCount];
        private readonly Outline[] hotbarSlotOutlines = new Outline[PlayerInventory.SlotCount];
        private readonly TMP_Text[] hotbarItemLabels = new TMP_Text[PlayerInventory.SlotCount];

        public Canvas RootCanvas => rootCanvas;
        public Canvas CrosshairCanvas => crosshairCanvas;
        public Canvas PauseCanvas => pauseCanvas;
        public Canvas LoadingCanvas => loadingCanvas;
        public bool IsPauseMenuVisible => pausePanel != null && pausePanel.activeSelf;
        public bool IsLoadingVisible => loadingPanel != null && loadingPanel.activeSelf;
        public IDamageable HealthSource => healthSource;
        public PlayerToolController InventorySource => inventorySource;
        public static bool IsPauseMenuOpen => pauseOwner != null && pauseOwner.pauseMenuOpen;
        public bool CanPauseGame =>
            isActiveAndEnabled
            && !IsMainMenuActive()
            && !MissionGameLoop.IsSceneTransitioning
            && !IsLoadingBlockingPause();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRuntimeHud()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            GameHudController existing = null;
            foreach (GameHudController candidate in
                FindObjectsOfType<GameHudController>(true))
            {
                if (candidate != null && candidate.gameObject.scene.IsValid())
                {
                    existing = candidate;
                    break;
                }
            }

            string mainMenuSceneName = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.SceneLookups.MainMenuSceneName
                : string.Empty;
            if (scene.name == mainMenuSceneName)
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }

            if (existing == null)
            {
                GameObject hudObject = new GameObject("Game HUD");
                DontDestroyOnLoad(hudObject);
                hudObject.AddComponent<GameHudController>();
            }
            else if (!existing.gameObject.activeSelf)
            {
                existing.gameObject.SetActive(true);
            }
        }


        private void Awake()
        {
            loadingSource = FindObjectOfType<MinecraftCaveInfiniteWorld>();
            EnsureView();
            BindHealthSource(healthSourceOverride as IDamageable);
            BindInventorySource(inventorySourceOverride);
            equipmentSource = FindObjectOfType<PlayerEquipmentController>();
        }

        private void OnEnable()
        {
            nextSourceSearchTime = 0f;
            nextInventorySourceSearchTime = 0f;
            nextEquipmentSourceSearchTime = 0f;
            nextWorldSourceSearchTime = 0f;
            if (inventorySource != null)
                BindInventorySource(inventorySource);
            RefreshNow();
        }

        private void OnDisable()
        {
            ResumeGame();
            if (inventorySource != null)
                inventorySource.SelectionChanged -= HandleInventorySelectionChanged;
        }

        private void Update()
        {
            if (pauseMenuOpen && !CanPauseGame)
                ResumeGame();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                TogglePauseMenu();
            }

            if (healthSource == null && Time.unscaledTime >= nextSourceSearchTime)
            {
                nextSourceSearchTime = Time.unscaledTime + sourceSearchInterval;
                BindHealthSource(FindPlayerHealthSource());
            }

            if (inventorySource == null && Time.unscaledTime >= nextInventorySourceSearchTime)
            {
                nextInventorySourceSearchTime = Time.unscaledTime + sourceSearchInterval;
                BindInventorySource(FindPlayerInventorySource());
            }

            if (equipmentSource == null
                && Time.unscaledTime >= nextEquipmentSourceSearchTime)
            {
                nextEquipmentSourceSearchTime = Time.unscaledTime + sourceSearchInterval;
                equipmentSource = FindObjectOfType<PlayerEquipmentController>();
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

        public void PauseGame()
        {
            if (pauseMenuOpen || !CanPauseGame) return;
            if (pausePanel == null || resumeButton == null)
            {
                CacheViewReferences();
                if (pausePanel == null || resumeButton == null)
                    BuildPauseView();
            }

            pauseMenuOpen = true;
            pauseOwner = this;
            pausePanel.SetActive(true);
            pausePresentation = pausePanel.GetComponent<PauseMenuPresentation>();
            if (pausePresentation == null)
                pausePresentation = pausePanel.AddComponent<PauseMenuPresentation>();
            if (equipmentSource == null)
                equipmentSource = FindObjectOfType<PlayerEquipmentController>();
            pausePresentation.BindEquipment(equipmentSource);
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
            if (!Application.isPlaying) return;
            Time.timeScale = timeScaleBeforePause;
            SetCursorState(cursorLockBeforePause, cursorVisibleBeforePause);
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

        public void BindLoadingSource(MinecraftCaveInfiniteWorld source)
        {
            loadingSource = source;
            nextWorldSourceSearchTime = 0f;
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
                inventorySource.SelectionChanged -= HandleInventorySelectionChanged;

            inventorySource = source;
            if (inventorySource != null)
                inventorySource.SelectionChanged += HandleInventorySelectionChanged;
            displayedSlotIndex = -1;
            RefreshNow();
        }

        public void RefreshNow()
        {
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
            if (loadingStatusLabel != null)
                loadingStatusLabel.text = GetLoadingStageLabel(loadingSource.GenerationStage);
            if (loadingProgressLabel != null)
                loadingProgressLabel.text = $"{Mathf.RoundToInt(progress * 100f)}%";
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

        [ContextMenu("Rebuild Default UGUI View")]
        public void RebuildDefaultView()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            BuildDefaultView();
            BuildLoadingView();
            BuildPauseView();
            SciFiUiSkin.ApplyGameHud(transform);
            CreatePresenter();
            RefreshNow();
        }

        private void EnsureView()
        {
            CacheViewReferences();
            if (rootCanvas == null || crosshairCanvas == null || healthPanel == null || healthFill == null
                || healthFillImage == null || healthValueLabel == null)
            {
                BuildDefaultView();
            }
            else if (hotbarRoot == null)
            {
                BuildHotbarView((RectTransform)rootCanvas.transform);
            }
            if (loadingCanvas == null || loadingFadeGroup == null || loadingContentGroup == null
                || loadingPanel == null || loadingSpinner == null
                || loadingFill == null || loadingStatusLabel == null
                || loadingProgressLabel == null)
            {
                BuildLoadingView();
            }

            bool pauseViewNeedsUpgrade =
                transform.Find(UiHierarchyPaths.Pause.FullBackSlot) == null;
            if (pauseCanvas == null || pausePanel == null || resumeButton == null
                || pauseViewNeedsUpgrade)
            {
                BuildPauseView();
            }

            SciFiUiSkin.ApplyGameHud(transform);
            CreatePresenter();
            RefreshLoadingView();
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
            if (hotbar == null) return;

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                Transform slot = hotbar.Find($"Slot {i + 1}");
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
                healthPanel, healthFill, healthFillImage, healthValueLabel);
            hotbarPresenter = new HotbarPresenter(
                hotbarSlotBackgrounds, hotbarSlotOutlines, hotbarItemLabels);
            displayedSlotIndex = -1;
        }

        private void BuildDefaultView()
        {
            RectTransform rootRect = CreateRect(UiHierarchyPaths.Hud.RootCanvas, transform);
            rootCanvas = rootRect.gameObject.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 100;

            CanvasScaler scaler = rootRect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform crosshairRoot = CreateRect(UiHierarchyPaths.Hud.CrosshairCanvas, transform);
            crosshairCanvas = crosshairRoot.gameObject.AddComponent<Canvas>();
            crosshairCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            crosshairCanvas.sortingOrder = 101;
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
            loadingCanvas.sortingOrder = 1000;
            CanvasScaler scaler = loadingRoot.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform panel = CreateRect("Loading Panel", loadingRoot);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            Image background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0.018f, 0.026f, 0.041f, 1f);
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
                "Brand", content, "SUPERNOVA", TextAlignmentOptions.Center);
            SetAnchoredRect((RectTransform)brand.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 184f), new Vector2(560f, 44f));
            brand.fontSize = 32f;
            brand.characterSpacing = 8f;
            brand.color = new Color(0.42f, 0.91f, 1f, 1f);

            TMP_Text title = CreateText(
                "Title", content, "LOADING WORLD", TextAlignmentOptions.Center);
            SetAnchoredRect((RectTransform)title.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 132f), new Vector2(560f, 36f));
            title.fontSize = 18f;
            title.characterSpacing = 3f;
            title.color = new Color(0.82f, 0.86f, 0.9f, 1f);

            loadingSpinner = CreateRect("Spinner", content);
            SetAnchoredRect(loadingSpinner,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 54f), new Vector2(62f, 62f));
            Image spinnerFrame = loadingSpinner.gameObject.AddComponent<Image>();
            spinnerFrame.color = new Color(0.22f, 0.82f, 0.94f, 1f);
            spinnerFrame.raycastTarget = false;
            RectTransform spinnerCore = CreateRect("Core", loadingSpinner);
            SetAnchoredRect(spinnerCore,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48f, 48f));
            Image spinnerCoreImage = spinnerCore.gameObject.AddComponent<Image>();
            spinnerCoreImage.color = background.color;
            spinnerCoreImage.raycastTarget = false;

            loadingStatusLabel = CreateText(
                "Status", content, "PREPARING WORLD", TextAlignmentOptions.Center);
            SetAnchoredRect((RectTransform)loadingStatusLabel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -18f), new Vector2(620f, 28f));
            loadingStatusLabel.fontSize = 15f;
            loadingStatusLabel.characterSpacing = 2f;

            RectTransform track = CreateRect("Progress Track", content);
            SetAnchoredRect(track,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -64f), new Vector2(520f, 12f));
            Image trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = new Color(0.12f, 0.15f, 0.19f, 1f);
            trackImage.raycastTarget = false;

            loadingFill = CreateRect("Fill", track);
            loadingFill.anchorMin = Vector2.zero;
            loadingFill.anchorMax = new Vector2(0f, 1f);
            loadingFill.pivot = new Vector2(0f, 0.5f);
            loadingFill.offsetMin = Vector2.zero;
            loadingFill.offsetMax = Vector2.zero;
            Image fillImage = loadingFill.gameObject.AddComponent<Image>();
            fillImage.color = new Color(0.22f, 0.82f, 0.94f, 1f);
            fillImage.raycastTarget = false;

            loadingProgressLabel = CreateText(
                "Progress", content, "0%", TextAlignmentOptions.Center);
            SetAnchoredRect((RectTransform)loadingProgressLabel.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -94f), new Vector2(300f, 30f));
            loadingProgressLabel.fontSize = 17f;
            loadingProgressLabel.color = new Color(0.7f, 0.93f, 0.98f, 1f);

            TMP_Text hint = CreateText(
                "Hint", content, "PREPARING A SAFE LANDING...", TextAlignmentOptions.Center);
            SetAnchoredRect((RectTransform)hint.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(620f, 28f));
            hint.fontSize = 12f;
            hint.characterSpacing = 1.5f;
            hint.color = new Color(0.42f, 0.5f, 0.58f, 1f);

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
            pauseCanvas.sortingOrder = 1100;
            CanvasScaler scaler = pauseRoot.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            pauseRoot.gameObject.AddComponent<GraphicRaycaster>();

            RectTransform panel = CreateRect("Pause Panel", pauseRoot);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            Image backdrop = panel.gameObject.AddComponent<Image>();
            backdrop.color = new Color(0.012f, 0.02f, 0.032f, 0.88f);
            backdrop.raycastTarget = true;
            pausePanel = panel.gameObject;

            RectTransform menu = CreateRect("Menu", panel);
            SetAnchoredRect(menu,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 430f));
            Image menuImage = menu.gameObject.AddComponent<Image>();
            menuImage.color = new Color(0.04f, 0.055f, 0.078f, 0.98f);
            Outline menuOutline = menu.gameObject.AddComponent<Outline>();
            menuOutline.effectColor = new Color(0.42f, 0.91f, 1f, 0.45f);
            menuOutline.effectDistance = new Vector2(1f, -1f);
            menuOutline.useGraphicAlpha = false;

            TMP_Text title = CreateText("Title", menu, "SYSTEM PAUSED", TextAlignmentOptions.Left);
            SetAnchoredRect((RectTransform)title.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(30f, -28f), new Vector2(500f, 44f));
            title.fontSize = 24f;
            title.characterSpacing = 5f;
            title.color = new Color(0.42f, 0.91f, 1f, 1f);

            TMP_Text loadout = CreateText(
                "Loadout Header",
                menu,
                "LOADOUT  /  EQUIPMENT",
                TextAlignmentOptions.Left);
            SetAnchoredRect((RectTransform)loadout.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(30f, -84f), new Vector2(500f, 28f));
            loadout.fontSize = 12f;
            loadout.characterSpacing = 3f;
            loadout.color = new Color(0.55f, 0.65f, 0.72f, 1f);

            RectTransform backSlot = CreateRect("Back Slot", menu);
            SetAnchoredRect(backSlot,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -122f), new Vector2(500f, 140f));
            Image backSlotImage = backSlot.gameObject.AddComponent<Image>();
            backSlotImage.color = new Color(0.025f, 0.08f, 0.11f, 0.96f);
            Button backSlotButton = backSlot.gameObject.AddComponent<Button>();
            backSlotButton.targetGraphic = backSlotImage;
            ColorBlock backColors = backSlotButton.colors;
            backColors.normalColor = Color.white;
            backColors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            backColors.pressedColor = new Color(0.72f, 0.84f, 0.9f, 1f);
            backColors.selectedColor = backColors.highlightedColor;
            backSlotButton.colors = backColors;
            Outline backOutline = backSlot.gameObject.AddComponent<Outline>();
            backOutline.effectColor = new Color(0.28f, 0.86f, 1f, 0.7f);
            backOutline.effectDistance = new Vector2(1f, -1f);
            backOutline.useGraphicAlpha = false;

            TMP_Text slotName = CreateText(
                "Slot Name", backSlot, "BACK MODULE", TextAlignmentOptions.TopLeft);
            SetAnchoredRect((RectTransform)slotName.transform,
                Vector2.zero, Vector2.one, new Vector2(0f, 1f),
                new Vector2(18f, -14f), new Vector2(-36f, -24f));
            slotName.fontSize = 12f;
            slotName.characterSpacing = 2f;
            slotName.color = new Color(0.55f, 0.65f, 0.72f, 1f);

            TMP_Text equipmentName = CreateText(
                "Equipment Name", backSlot, "NO EQUIPMENT", TextAlignmentOptions.Left);
            SetAnchoredRect((RectTransform)equipmentName.transform,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(18f, 5f), new Vector2(310f, 38f));
            equipmentName.fontSize = 23f;
            equipmentName.fontStyle = FontStyles.Bold;
            equipmentName.color = new Color(0.86f, 0.96f, 1f, 1f);

            TMP_Text equipmentState = CreateText(
                "State", backSlot, "EMPTY", TextAlignmentOptions.TopRight);
            SetAnchoredRect((RectTransform)equipmentState.transform,
                Vector2.zero, Vector2.one, new Vector2(1f, 1f),
                new Vector2(-18f, -14f), new Vector2(-36f, -24f));
            equipmentState.fontSize = 12f;
            equipmentState.characterSpacing = 2f;
            equipmentState.color = new Color(0.28f, 0.86f, 1f, 1f);

            TMP_Text equipmentHint = CreateText(
                "Hint", backSlot, "NO MODULE AVAILABLE", TextAlignmentOptions.BottomLeft);
            SetAnchoredRect((RectTransform)equipmentHint.transform,
                Vector2.zero, Vector2.one, new Vector2(0f, 0f),
                new Vector2(18f, 12f), new Vector2(-36f, -24f));
            equipmentHint.fontSize = 10f;
            equipmentHint.characterSpacing = 1f;
            equipmentHint.color = new Color(0.55f, 0.65f, 0.72f, 1f);

            RectTransform resume = CreateRect("Resume", menu);
            SetAnchoredRect(resume,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(500f, 58f));
            Image resumeImage = resume.gameObject.AddComponent<Image>();
            resumeImage.color = new Color(0.12f, 0.5f, 0.6f, 1f);
            resumeButton = resume.gameObject.AddComponent<Button>();
            resumeButton.targetGraphic = resumeImage;
            ColorBlock colors = resumeButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.16f, 1.16f, 1.16f, 1f);
            colors.pressedColor = new Color(0.78f, 0.86f, 0.9f, 1f);
            colors.selectedColor = colors.highlightedColor;
            resumeButton.colors = colors;
            Navigation navigation = resumeButton.navigation;
            navigation.mode = Navigation.Mode.None;
            resumeButton.navigation = navigation;
            resumeButton.onClick.AddListener(ResumeGame);

            TMP_Text resumeLabel = CreateText(
                "Label", resume, "RESUME", TextAlignmentOptions.Center);
            SetAnchoredRect((RectTransform)resumeLabel.transform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            resumeLabel.fontSize = 18f;
            resumeLabel.characterSpacing = 2f;

            EnsureEventSystem();
            pausePanel.SetActive(pauseMenuOpen);
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.transform.SetParent(transform, false);
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();
        }

        private void BuildHotbarView(RectTransform rootRect)
        {
            RectTransform hotbar = CreateRect("Hotbar", rootRect);
            SetAnchoredRect(hotbar, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(556f, 56f));
            hotbarRoot = hotbar.gameObject;

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                RectTransform slot = CreateRect($"Slot {i + 1}", hotbar);
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

                string keyText = i == PlayerInventory.SlotCount - 1 ? "0" : (i + 1).ToString();
                TMP_Text key = CreateText("Key", slot, keyText, TextAlignmentOptions.TopLeft);
                SetAnchoredRect((RectTransform)key.transform, Vector2.zero, Vector2.one,
                    new Vector2(0.5f, 0.5f), new Vector2(5f, -3f), new Vector2(-10f, -6f));
                key.fontSize = 11f;
                key.color = new Color(0.7f, 0.75f, 0.8f, 1f);

                string itemText = i == 0
                    ? "PICKAXE"
                    : i == 1
                        ? "MAGNET"
                        : i == 2
                            ? "FLASHLIGHT"
                            : string.Empty;
                TMP_Text item = CreateText("Item", slot, itemText, TextAlignmentOptions.Center);
                SetAnchoredRect((RectTransform)item.transform, Vector2.zero, Vector2.one,
                    new Vector2(0.5f, 0.5f), new Vector2(3f, -6f), new Vector2(-6f, -16f));
                item.fontSize = i == 2 ? 7f : 9f;
                item.color = new Color(0.92f, 0.94f, 0.96f, 1f);
                hotbarItemLabels[i] = item;
            }
        }

        private static RectTransform CreateRect(string objectName, Transform parent)
        {
            GameObject child = new GameObject(objectName, typeof(RectTransform));
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
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

    /// <summary>Presentation-only adapter for the ten hotbar slots.</summary>
    public sealed class HotbarPresenter
    {
        private static readonly Color IdleFrame =
            new Color(0.36f, 0.89f, 0.98f, 0.46f);
        private static readonly Color SelectedFrame =
            new Color(0.95f, 0.78f, 0.22f, 1f);

        private readonly Image[] backgrounds;
        private readonly Outline[] outlines;
        private readonly Image[] frames;
        private readonly TMP_Text[] itemLabels;

        public HotbarPresenter(Image[] backgrounds, Outline[] outlines, TMP_Text[] itemLabels)
        {
            this.backgrounds = backgrounds;
            this.outlines = outlines;
            this.itemLabels = itemLabels;
            frames = new Image[PlayerInventory.SlotCount];
            for (int i = 0; i < frames.Length; i++)
            {
                if (backgrounds == null || i >= backgrounds.Length || backgrounds[i] == null)
                    continue;
                Transform frame = backgrounds[i].transform.Find(UiHierarchyPaths.Decoration.Frame);
                if (frame != null)
                    frames[i] = frame.GetComponent<Image>();
            }
            SetItemLabels();
        }

        public void SetSelectedSlot(int selectedSlotIndex)
        {
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                bool selected = i == selectedSlotIndex;
                if (backgrounds != null && i < backgrounds.Length && backgrounds[i] != null)
                    backgrounds[i].color = Color.clear;
                if (outlines != null && i < outlines.Length && outlines[i] != null)
                {
                    outlines[i].effectColor = Color.clear;
                }
                if (frames[i] != null)
                {
                    frames[i].color = selected ? SelectedFrame : IdleFrame;
                }
            }
        }

        private void SetItemLabels()
        {
            if (itemLabels == null) return;
            for (int i = 0; i < itemLabels.Length; i++)
            {
                if (itemLabels[i] == null) continue;
                itemLabels[i].text = i == 0
                    ? "PICKAXE"
                    : i == 1
                        ? "MAGNET"
                        : i == 2
                            ? "FLASHLIGHT"
                            : string.Empty;
                itemLabels[i].fontSize = i == 2 ? 7f : 9f;
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

        public GameHudPresenter(
            GameObject healthPanel,
            RectTransform healthFill,
            Image healthFillImage,
            TMP_Text healthValueLabel)
        {
            this.healthPanel = healthPanel;
            this.healthFill = healthFill;
            this.healthFillImage = healthFillImage;
            this.healthValueLabel = healthValueLabel;
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
                healthFillImage.color = Color.Lerp(
                    new Color(0.86f, 0.18f, 0.14f),
                    new Color(0.21f, 0.8f, 0.38f),
                    normalized);
            }

            if (healthValueLabel != null)
                healthValueLabel.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(maximum)}";
        }
    }
}
