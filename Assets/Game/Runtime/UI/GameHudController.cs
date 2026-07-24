using Supernova.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

        private IDamageable healthSource;
        private PlayerToolController inventorySource;
        private GameHudPresenter presenter;
        private HotbarPresenter hotbarPresenter;
        private float nextSourceSearchTime;
        private float nextInventorySourceSearchTime;
        private float displayedCurrentHealth = float.NaN;
        private float displayedMaximumHealth = float.NaN;
        private int displayedSlotIndex = -1;
        private readonly Image[] hotbarSlotBackgrounds = new Image[PlayerInventory.SlotCount];
        private readonly Outline[] hotbarSlotOutlines = new Outline[PlayerInventory.SlotCount];
        private readonly TMP_Text[] hotbarItemLabels = new TMP_Text[PlayerInventory.SlotCount];

        public Canvas RootCanvas => rootCanvas;
        public Canvas CrosshairCanvas => crosshairCanvas;
        public IDamageable HealthSource => healthSource;
        public PlayerToolController InventorySource => inventorySource;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRuntimeHud()
        {
            if (FindObjectOfType<GameHudController>() != null) return;

            GameObject hudObject = new GameObject("Game HUD");
            DontDestroyOnLoad(hudObject);
            hudObject.AddComponent<GameHudController>();
        }

        private void Awake()
        {
            EnsureView();
            BindHealthSource(healthSourceOverride as IDamageable);
            BindInventorySource(inventorySourceOverride);
        }

        private void OnEnable()
        {
            nextSourceSearchTime = 0f;
            nextInventorySourceSearchTime = 0f;
            if (inventorySource != null)
                BindInventorySource(inventorySource);
            RefreshNow();
        }

        private void OnDisable()
        {
            if (inventorySource != null)
                inventorySource.SelectionChanged -= HandleInventorySelectionChanged;
        }

        private void Update()
        {
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

            RefreshNow();
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
        }

        private void RefreshHotbar()
        {
            if (hotbarPresenter == null) return;
            int slotIndex = inventorySource != null ? inventorySource.SelectedSlotIndex : 0;
            if (slotIndex == displayedSlotIndex) return;

            displayedSlotIndex = slotIndex;
            hotbarPresenter.SetSelectedSlot(slotIndex);
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

            CreatePresenter();
        }

        private void CacheViewReferences()
        {
            Transform hudCanvasTransform = transform.Find("HUD Canvas");
            if (rootCanvas == null && hudCanvasTransform != null)
                rootCanvas = hudCanvasTransform.GetComponent<Canvas>();

            Transform crosshairCanvasTransform = transform.Find("Crosshair Canvas");
            if (crosshairCanvas == null && crosshairCanvasTransform != null)
                crosshairCanvas = crosshairCanvasTransform.GetComponent<Canvas>();

            Transform panel = transform.Find("HUD Canvas/Health Panel");
            if (healthPanel == null && panel != null) healthPanel = panel.gameObject;

            Transform fill = transform.Find("HUD Canvas/Health Panel/Track/Fill");
            if (healthFill == null && fill != null) healthFill = fill as RectTransform;
            if (healthFillImage == null && fill != null) healthFillImage = fill.GetComponent<Image>();

            Transform value = transform.Find("HUD Canvas/Health Panel/Header/Value");
            if (healthValueLabel == null && value != null)
                healthValueLabel = value.GetComponent<TMP_Text>();

            Transform hotbar = transform.Find("HUD Canvas/Hotbar");
            if (hotbarRoot == null && hotbar != null) hotbarRoot = hotbar.gameObject;
            if (hotbar == null) return;

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                Transform slot = hotbar.Find($"Slot {i + 1}");
                if (slot == null) continue;
                hotbarSlotBackgrounds[i] = slot.GetComponent<Image>();
                hotbarSlotOutlines[i] = slot.GetComponent<Outline>();
                Transform itemLabel = slot.Find("Item");
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
            RectTransform rootRect = CreateRect("HUD Canvas", transform);
            rootCanvas = rootRect.gameObject.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 100;

            CanvasScaler scaler = rootRect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform crosshairRoot = CreateRect("Crosshair Canvas", transform);
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
            SetAnchoredRect(panel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(24f, 24f), new Vector2(260f, 64f));
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

                string itemText = i == 0 ? "PICKAXE" : i == 1 ? "MAGNET" : string.Empty;
                TMP_Text item = CreateText("Item", slot, itemText, TextAlignmentOptions.Center);
                SetAnchoredRect((RectTransform)item.transform, Vector2.zero, Vector2.one,
                    new Vector2(0.5f, 0.5f), new Vector2(3f, -6f), new Vector2(-6f, -16f));
                item.fontSize = 9f;
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
        private static readonly Color IdleColor = new Color(0.045f, 0.055f, 0.065f, 0.9f);
        private static readonly Color SelectedColor = new Color(0.17f, 0.2f, 0.18f, 0.96f);
        private static readonly Color IdleOutline = new Color(0.7f, 0.75f, 0.8f, 0.45f);
        private static readonly Color SelectedOutline = new Color(0.95f, 0.78f, 0.22f, 1f);

        private readonly Image[] backgrounds;
        private readonly Outline[] outlines;
        private readonly TMP_Text[] itemLabels;

        public HotbarPresenter(Image[] backgrounds, Outline[] outlines, TMP_Text[] itemLabels)
        {
            this.backgrounds = backgrounds;
            this.outlines = outlines;
            this.itemLabels = itemLabels;
            SetItemLabels();
        }

        public void SetSelectedSlot(int selectedSlotIndex)
        {
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                bool selected = i == selectedSlotIndex;
                if (backgrounds != null && i < backgrounds.Length && backgrounds[i] != null)
                    backgrounds[i].color = selected ? SelectedColor : IdleColor;
                if (outlines != null && i < outlines.Length && outlines[i] != null)
                {
                    outlines[i].effectColor = selected ? SelectedOutline : IdleOutline;
                    outlines[i].effectDistance = selected ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
                }
            }
        }

        private void SetItemLabels()
        {
            if (itemLabels == null) return;
            for (int i = 0; i < itemLabels.Length; i++)
            {
                if (itemLabels[i] == null) continue;
                itemLabels[i].text = i == 0 ? "PICKAXE" : i == 1 ? "MAGNET" : string.Empty;
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

