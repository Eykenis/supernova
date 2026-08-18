using System.Collections.Generic;
using Supernova.Gameplay;
using Supernova.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// TAB-only loadout workspace. It owns a modal UGUI canvas, five quick-slot
    /// selectors, and a twelve-cell owned-item grid.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EquipmentLoadoutMenu : MonoBehaviour
    {
        public const int OwnedGridCellCount = 12;

        private readonly Button[] slotButtons =
            new Button[PlayerInventory.SlotCount];
        private readonly AngledPanelGraphic[] slotSurfaces =
            new AngledPanelGraphic[PlayerInventory.SlotCount];
        private readonly TMP_Text[] slotIndexLabels =
            new TMP_Text[PlayerInventory.SlotCount];
        private readonly TMP_Text[] slotItemLabels =
            new TMP_Text[PlayerInventory.SlotCount];
        private readonly Image[] slotIcons =
            new Image[PlayerInventory.SlotCount];
        private readonly Button[] ownedButtons =
            new Button[OwnedGridCellCount];
        private readonly AngledPanelGraphic[] ownedSurfaces =
            new AngledPanelGraphic[OwnedGridCellCount];
        private readonly TMP_Text[] ownedItemLabels =
            new TMP_Text[OwnedGridCellCount];
        private readonly TMP_Text[] ownedStateLabels =
            new TMP_Text[OwnedGridCellCount];
        private readonly Image[] ownedIcons =
            new Image[OwnedGridCellCount];
        private readonly PlayerInventoryItem[] displayedOwnedItems =
            new PlayerInventoryItem[OwnedGridCellCount];

        private GameHudController owner;
        private UiDesignTokens designTokens;
        private PlayerToolController inventorySource;
        private Canvas canvas;
        private GameObject panel;
        private int configuringSlotIndex;
        private bool isOpen;
        private float timeScaleBeforeOpen = 1f;
        private CursorLockMode cursorLockBeforeOpen;
        private bool cursorVisibleBeforeOpen;

        private int draggedOwnedCellIndex = -1;
        private RectTransform dragVisual;

        public static bool IsAnyOpen { get; private set; }
        public bool IsOpen => isOpen;
        public Canvas Canvas => canvas;
        public GameObject Panel => panel;
        public int ConfiguringSlotIndex => configuringSlotIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            IsAnyOpen = false;
        }

        private Color Primary => Opaque(designTokens != null
            ? designTokens.HudPrimary
            : new Color(0.96f, 0.98f, 1f, 1f));
        private Color Surface => Opaque(designTokens != null
            ? designTokens.HudSurface
            : new Color(0.035f, 0.045f, 0.055f, 1f));
        private Color Muted => Opaque(designTokens != null
            ? designTokens.HudMuted
            : new Color(0.3f, 0.32f, 0.34f, 1f));
        private Color Shadow => Opaque(designTokens != null
            ? designTokens.HudShadow
            : new Color(0f, 0f, 0f, 1f));
        private Color Inverse => Opaque(designTokens != null
            ? designTokens.OverlayInverse
            : new Color(0.018f, 0.02f, 0.025f, 1f));
        private float Slant => designTokens != null
            ? designTokens.HudElementSlant * 1.6f
            : 14f;
        private float Depth => designTokens != null
            ? designTokens.HudExtrusionDepth
            : 5f;
        private bool ReverseSlant => designTokens == null
            || designTokens.HudHotbarReverseSlant;

        public void Initialize(GameHudController configuredOwner, UiDesignTokens tokens)
        {
            owner = configuredOwner;
            designTokens = tokens;
            EnsureView();
        }

        public void BindInventory(PlayerToolController source)
        {
            if (inventorySource == source)
            {
                RefreshView();
                return;
            }

            if (inventorySource != null)
            {
                inventorySource.LoadoutChanged -= HandleInventoryChanged;
                inventorySource.OwnedItemsChanged -= HandleInventoryChanged;
            }

            inventorySource = source;
            if (inventorySource != null)
            {
                inventorySource.LoadoutChanged += HandleInventoryChanged;
                inventorySource.OwnedItemsChanged += HandleInventoryChanged;
                configuringSlotIndex = inventorySource.SelectedSlotIndex;
            }
            RefreshView();
        }

        public void Open()
        {
            EnsureView();
            if (isOpen || panel == null)
                return;

            isOpen = true;
            IsAnyOpen = true;
            panel.SetActive(true);
            owner?.SetGameplayHudVisibleForModal(false);
            configuringSlotIndex = inventorySource != null
                ? inventorySource.SelectedSlotIndex
                : 0;
            RefreshView();

            if (Application.isPlaying)
            {
                timeScaleBeforeOpen = Time.timeScale;
                cursorLockBeforeOpen = Cursor.lockState;
                cursorVisibleBeforeOpen = Cursor.visible;
                Time.timeScale = 0f;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (EventSystem.current != null && slotButtons[configuringSlotIndex] != null)
            {
                EventSystem.current.SetSelectedGameObject(
                    slotButtons[configuringSlotIndex].gameObject);
            }
        }

        public void Close()
        {
            EndOwnedItemDrag();
            if (panel != null)
                panel.SetActive(false);
            if (!isOpen)
                return;

            isOpen = false;
            IsAnyOpen = false;
            owner?.SetGameplayHudVisibleForModal(true);
            if (!Application.isPlaying)
                return;

            GameHudController.BlockGameplayInputAfterModalClose();
            Time.timeScale = timeScaleBeforeOpen;
            Cursor.lockState = cursorLockBeforeOpen;
            Cursor.visible = cursorVisibleBeforeOpen;
            PlayerPrefs.Save();
        }

        public void RebuildView()
        {
            bool reopen = isOpen;
            Close();
            DestroyView();
            EnsureView();
            if (reopen)
                Open();
        }

        private void OnDisable()
        {
            Close();
        }

        private void OnDestroy()
        {
            if (inventorySource != null)
            {
                inventorySource.LoadoutChanged -= HandleInventoryChanged;
                inventorySource.OwnedItemsChanged -= HandleInventoryChanged;
            }
            if (IsAnyOpen && isOpen)
                IsAnyOpen = false;
        }

        private void HandleInventoryChanged()
        {
            RefreshView();
        }

        private void EnsureView()
        {
            if (canvas != null && panel != null)
                return;

            RectTransform canvasRect = CreateRect(
                UiHierarchyPaths.Equipment.Canvas,
                transform);
            canvas = canvasRect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = designTokens != null
                ? designTokens.PauseSortingOrder - 1
                : 1099;
            CanvasScaler scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
            UiCanvasPolicy policy = canvasRect.gameObject.AddComponent<UiCanvasPolicy>();
            policy.SetDesignTokens(designTokens);
            canvasRect.gameObject.AddComponent<GraphicRaycaster>();

            RectTransform panelRect = CreateRect("Equipment Panel", canvasRect);
            Stretch(panelRect);
            Image backdrop = panelRect.gameObject.AddComponent<Image>();
            Color backdropColor = designTokens != null
                ? designTokens.OverlayBackdrop
                : new Color(0.008f, 0.01f, 0.014f, 0.72f);
            backdropColor.a = 0.78f;
            backdrop.color = backdropColor;
            backdrop.raycastTarget = true;
            panel = panelRect.gameObject;

            BuildConfigurationRegion(panelRect);
            GameHudController.EnsureSingleEventSystem(transform);
            SciFiUiSkin.ApplyPauseMenu(panelRect);
            panel.SetActive(false);
            RefreshView();
        }

        private void BuildConfigurationRegion(RectTransform parent)
        {
            RectTransform configuration = CreateRect(
                UiHierarchyPaths.Equipment.Configuration,
                parent);
            configuration.anchorMin = Vector2.zero;
            configuration.anchorMax = Vector2.one;
            configuration.offsetMin = Vector2.zero;
            configuration.offsetMax = Vector2.zero;

            TMP_Text title = CreateText(
                "Title",
                configuration,
                "背包",
                TextAlignmentOptions.Left);
            SetRect(
                (RectTransform)title.transform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(46f, -38f),
                new Vector2(520f, 64f));
            title.fontSize = 42f;
            title.characterSpacing = 4f;
            title.color = Primary;

            TMP_Text closeHint = CreateText(
                "Close Hint",
                configuration,
                "TAB 关闭",
                TextAlignmentOptions.Right);
            SetRect(
                (RectTransform)closeHint.transform,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-42f, -42f),
                new Vector2(260f, 36f));
            closeHint.fontSize = 13f;
            closeHint.characterSpacing = 2f;
            closeHint.color = Primary;

            RectTransform slots = CreateRect("Equipped Slots", configuration);
            slots.anchorMin = new Vector2(0f, 0f);
            slots.anchorMax = new Vector2(0.34f, 1f);
            slots.offsetMin = new Vector2(38f, 58f);
            slots.offsetMax = new Vector2(-18f, -126f);
            BuildEquippedSlots(slots);

            RectTransform grid = CreateRect("Owned Grid", configuration);
            grid.anchorMin = new Vector2(0.34f, 0f);
            grid.anchorMax = Vector2.one;
            grid.offsetMin = new Vector2(18f, 58f);
            grid.offsetMax = new Vector2(-38f, -126f);
            BuildOwnedGrid(grid);
        }

        private void BuildEquippedSlots(RectTransform parent)
        {
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                int slotIndex = i;
                RectTransform rect = CreateRect(
                    UiHierarchyPaths.Equipment.SlotName(i + 1),
                    parent);
                SetRect(
                    rect,
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, -54f - i * 106f),
                    new Vector2(0f, 86f));
                slotButtons[i] = CreatePlateButton(
                    rect,
                    out slotSurfaces[i]);
                slotButtons[i].onClick.AddListener(
                    () => SelectConfiguringSlot(slotIndex));
                rect.gameObject.AddComponent<EquipmentMenuInteraction>()
                    .ConfigureEquipmentSlot(this, slotIndex);

                slotIndexLabels[i] = CreateText(
                    "Index",
                    rect,
                    (i + 1).ToString("00"),
                    TextAlignmentOptions.Left);
                SetRect(
                    (RectTransform)slotIndexLabels[i].transform,
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(0f, 0.5f),
                    new Vector2(18f, 0f),
                    new Vector2(42f, -10f));
                slotIndexLabels[i].fontSize = 12f;
                slotIndexLabels[i].characterSpacing = 2f;

                slotIcons[i] = CreateItemIcon(rect);
                SetRect(
                    (RectTransform)slotIcons[i].transform,
                    new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(91f, 0f),
                    new Vector2(58f, 58f));

                slotItemLabels[i] = CreateText(
                    "Item",
                    rect,
                    "空",
                    TextAlignmentOptions.Left);
                SetRect(
                    (RectTransform)slotItemLabels[i].transform,
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(0f, 0.5f),
                    new Vector2(128f, 0f),
                    new Vector2(-144f, -8f));
                slotItemLabels[i].fontSize = 16f;
                slotItemLabels[i].characterSpacing = 1f;
            }

            ConfigureVerticalNavigation(slotButtons);
        }

        private void BuildOwnedGrid(RectTransform parent)
        {
            const int columns = 4;
            const float horizontalGap = 14f;
            const float verticalGap = 16f;
            const float cellHeight = 150f;
            for (int i = 0; i < OwnedGridCellCount; i++)
            {
                int cellIndex = i;
                int column = i % columns;
                int row = i / columns;
                RectTransform rect = CreateRect(
                    UiHierarchyPaths.Equipment.OwnedCellName(i + 1),
                    parent);
                float minX = column / (float)columns;
                float maxX = (column + 1) / (float)columns;
                rect.anchorMin = new Vector2(minX, 1f);
                rect.anchorMax = new Vector2(maxX, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(
                    column == 0 ? 0f : horizontalGap * 0.5f,
                    -54f - row * (cellHeight + verticalGap));
                rect.sizeDelta = new Vector2(-horizontalGap, cellHeight);
                ownedButtons[i] = CreatePlateButton(
                    rect,
                    out ownedSurfaces[i]);
                rect.gameObject.AddComponent<EquipmentMenuInteraction>()
                    .ConfigureOwnedItem(this, cellIndex);

                ownedIcons[i] = CreateItemIcon(rect);
                SetRect(
                    (RectTransform)ownedIcons[i].transform,
                    new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(0f, -26f),
                    new Vector2(94f, 82f));

                TMP_Text cellNumber = CreateText(
                    "Cell Number",
                    rect,
                    (i + 1).ToString("00"),
                    TextAlignmentOptions.TopLeft);
                SetRect(
                    (RectTransform)cellNumber.transform,
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(0f, 1f),
                    new Vector2(14f, -12f),
                    new Vector2(-28f, -24f));
                cellNumber.fontSize = 10f;
                cellNumber.characterSpacing = 1.5f;
                cellNumber.color = Primary;

                ownedItemLabels[i] = CreateText(
                    "Item",
                    rect,
                    "空",
                    TextAlignmentOptions.BottomLeft);
                SetRect(
                    (RectTransform)ownedItemLabels[i].transform,
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(0f, 0f),
                    new Vector2(14f, 38f),
                    new Vector2(-28f, -66f));
                ownedItemLabels[i].fontSize = 16f;
                ownedItemLabels[i].characterSpacing = 0.8f;

                ownedStateLabels[i] = CreateText(
                    "State",
                    rect,
                    "未装备",
                    TextAlignmentOptions.BottomLeft);
                SetRect(
                    (RectTransform)ownedStateLabels[i].transform,
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(0f, 0f),
                    new Vector2(14f, 12f),
                    new Vector2(-28f, 24f));
                ownedStateLabels[i].fontSize = 9f;
                ownedStateLabels[i].characterSpacing = 1.5f;
            }
        }

        private Button CreatePlateButton(
            RectTransform rect,
            out AngledPanelGraphic surface)
        {
            RectTransform angledRect = CreateRect("Angled Surface", rect);
            Stretch(angledRect);
            surface = angledRect.gameObject.AddComponent<AngledPanelGraphic>();
            surface.Configure(
                Slant,
                Depth,
                Surface,
                Shadow,
                Primary,
                ReverseSlant);
            surface.raycastTarget = true;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = surface;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.selectedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.68f, 0.68f, 0.68f, 1f);
            colors.disabledColor = new Color(0.56f, 0.56f, 0.56f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            return button;
        }

        private static Image CreateItemIcon(RectTransform parent)
        {
            RectTransform iconRect = CreateRect(
                UiHierarchyPaths.Equipment.Icon,
                parent);
            Image icon = iconRect.gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;
            return icon;
        }

        private void SelectConfiguringSlot(int slotIndex)
        {
            configuringSlotIndex = Mathf.Clamp(
                slotIndex,
                0,
                PlayerInventory.SlotCount - 1);
            RefreshView();
        }

        public bool TryAssignOwnedCellToSlot(int cellIndex, int slotIndex)
        {
            if (inventorySource == null
                || cellIndex < 0
                || cellIndex >= displayedOwnedItems.Length
                || slotIndex < 0
                || slotIndex >= PlayerInventory.SlotCount)
            {
                return false;
            }

            PlayerInventoryItem item = displayedOwnedItems[cellIndex];
            if (item == PlayerInventoryItem.Empty
                || item == PlayerInventoryItem.Pickaxe
                || PlayerInventory.IsFixedSlot(slotIndex))
                return false;

            configuringSlotIndex = slotIndex;
            bool changed = inventorySource.ConfigureSlot(slotIndex, item);
            RefreshView();
            return changed;
        }

        internal bool BeginOwnedItemDrag(int cellIndex, Vector2 screenPosition)
        {
            if (cellIndex < 0
                || cellIndex >= displayedOwnedItems.Length
                || displayedOwnedItems[cellIndex] == PlayerInventoryItem.Empty
                || displayedOwnedItems[cellIndex]
                    == PlayerInventoryItem.Pickaxe)
            {
                return false;
            }

            draggedOwnedCellIndex = cellIndex;
            CreateDragVisual(displayedOwnedItems[cellIndex]);
            UpdateOwnedItemDrag(screenPosition);
            return true;
        }

        internal void UpdateOwnedItemDrag(Vector2 screenPosition)
        {
            if (dragVisual == null || canvas == null)
                return;

            RectTransform canvasRect = canvas.transform as RectTransform;
            if (canvasRect != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    screenPosition,
                    canvas.renderMode == RenderMode.ScreenSpaceOverlay
                        ? null
                        : canvas.worldCamera,
                    out Vector2 localPoint))
            {
                dragVisual.anchoredPosition = localPoint;
            }
        }

        internal void DropDraggedItemOnSlot(int slotIndex)
        {
            if (draggedOwnedCellIndex < 0)
                return;
            TryAssignOwnedCellToSlot(draggedOwnedCellIndex, slotIndex);
        }

        internal void EndOwnedItemDrag()
        {
            draggedOwnedCellIndex = -1;
            if (dragVisual == null)
                return;

            dragVisual.gameObject.SetActive(false);
            if (Application.isPlaying)
                Destroy(dragVisual.gameObject);
            else
                DestroyImmediate(dragVisual.gameObject);
            dragVisual = null;
        }

        private void RefreshView()
        {
            if (panel == null)
                return;

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                PlayerInventoryItem item = inventorySource != null
                    ? inventorySource.GetItemAtSlot(i)
                    : PlayerInventoryItem.Empty;
                bool selected = i == configuringSlotIndex;
                if (slotSurfaces[i] != null)
                    slotSurfaces[i].SetFrontColor(selected ? Primary : Surface);
                Color textColor = selected ? Inverse : Primary;
                if (slotIndexLabels[i] != null)
                    slotIndexLabels[i].color = textColor;
                if (slotItemLabels[i] != null)
                {
                    string itemLabel = HotbarPresenter.GetItemLabel(item);
                    slotItemLabels[i].text = string.IsNullOrEmpty(itemLabel)
                        ? "空"
                        : itemLabel;
                    slotItemLabels[i].color = textColor;
                }
                ApplyItemIcon(slotIcons[i], item, textColor);
            }

            List<PlayerInventoryItem> ownedItems = GetDistinctOwnedItems();
            for (int i = 0; i < OwnedGridCellCount; i++)
            {
                PlayerInventoryItem item = i < ownedItems.Count
                    ? ownedItems[i]
                    : PlayerInventoryItem.Empty;
                displayedOwnedItems[i] = item;
                bool occupied = item != PlayerInventoryItem.Empty;
                int assignedSlot = occupied && inventorySource != null
                    ? inventorySource.Inventory.IndexOf(item)
                    : -1;
                if (ownedButtons[i] != null)
                    ownedButtons[i].interactable = occupied;
                if (ownedSurfaces[i] != null)
                {
                    ownedSurfaces[i].SetFrontColor(
                        assignedSlot >= 0 ? Primary : occupied ? Surface : Muted);
                }

                Color textColor = assignedSlot >= 0 ? Inverse : Primary;
                if (ownedItemLabels[i] != null)
                {
                    ownedItemLabels[i].text = occupied
                        ? HotbarPresenter.GetItemLabel(item)
                        : "空";
                    ownedItemLabels[i].color = textColor;
                }
                if (ownedStateLabels[i] != null)
                {
                    ownedStateLabels[i].text =
                        item == PlayerInventoryItem.Pickaxe
                        ? "固定在 1 槽"
                        : assignedSlot >= 0
                        ? "已装备在 " + (assignedSlot + 1) + " 槽"
                        : occupied
                            ? ""
                            : "空";
                    ownedStateLabels[i].color = textColor;
                }
                ApplyItemIcon(ownedIcons[i], item, textColor);
            }
        }

        private void CreateDragVisual(PlayerInventoryItem item)
        {
            EndOwnedItemDrag();
            draggedOwnedCellIndex = System.Array.IndexOf(
                displayedOwnedItems,
                item);

            dragVisual = CreateRect("Equipment Drag Preview", canvas.transform);
            SetRect(
                dragVisual,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(210f, 72f));
            dragVisual.SetAsLastSibling();

            RectTransform surfaceRect = CreateRect("Angled Surface", dragVisual);
            Stretch(surfaceRect);
            AngledPanelGraphic surfaceGraphic =
                surfaceRect.gameObject.AddComponent<AngledPanelGraphic>();
            surfaceGraphic.Configure(
                Slant,
                Depth,
                Surface,
                Shadow,
                Primary,
                ReverseSlant);
            surfaceGraphic.raycastTarget = false;

            Image icon = CreateItemIcon(dragVisual);
            SetRect(
                (RectTransform)icon.transform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(54f, 0f),
                new Vector2(62f, 62f));
            ApplyItemIcon(icon, item, Primary);

            TMP_Text label = CreateText(
                "Item",
                dragVisual,
                HotbarPresenter.GetItemLabel(item),
                TextAlignmentOptions.Left);
            SetRect(
                (RectTransform)label.transform,
                Vector2.zero,
                Vector2.one,
                new Vector2(0f, 0.5f),
                new Vector2(96f, 0f),
                new Vector2(-112f, -8f));
            label.fontSize = 16f;
            label.characterSpacing = 1f;
            label.color = Primary;

            CanvasGroup group = dragVisual.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0.94f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        private void ApplyItemIcon(
            Image image,
            PlayerInventoryItem item,
            Color color)
        {
            if (image == null)
                return;

            EquipmentIconCatalog catalog = GameAssetCatalog.Current != null
                && GameAssetCatalog.Current.UI != null
                ? GameAssetCatalog.Current.UI.EquipmentIcons
                : null;
            Sprite sprite = catalog != null ? catalog.GetIcon(item) : null;
            image.sprite = sprite;
            image.color = color;
            image.enabled = sprite != null;
        }

        private List<PlayerInventoryItem> GetDistinctOwnedItems()
        {
            var result = new List<PlayerInventoryItem>();
            if (inventorySource == null || inventorySource.OwnedItems == null)
                return result;

            var seen = new HashSet<int>();
            IReadOnlyList<PlayerInventoryItem> items =
                inventorySource.OwnedItems.InventoryItems;
            for (int i = 0; i < items.Count && result.Count < OwnedGridCellCount; i++)
            {
                PlayerInventoryItem item = items[i];
                if (item != PlayerInventoryItem.Empty && seen.Add((int)item))
                    result.Add(item);
            }
            return result;
        }

        private void DestroyView()
        {
            if (canvas != null)
            {
                if (Application.isPlaying)
                    Destroy(canvas.gameObject);
                else
                    DestroyImmediate(canvas.gameObject);
            }
            canvas = null;
            panel = null;
        }

        private static void ConfigureVerticalNavigation(Button[] buttons)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null)
                    continue;
                Navigation navigation = buttons[i].navigation;
                navigation.mode = Navigation.Mode.Explicit;
                navigation.selectOnUp = buttons[(i - 1 + buttons.Length) % buttons.Length];
                navigation.selectOnDown = buttons[(i + 1) % buttons.Length];
                buttons[i].navigation = navigation;
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

        private static TMP_Text CreateText(
            string objectName,
            RectTransform parent,
            string content,
            TextAlignmentOptions alignment)
        {
            RectTransform rect = CreateRect(objectName, parent);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = 14f;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static Color Opaque(Color color)
        {
            color.a = 1f;
            return color;
        }
    }
}
