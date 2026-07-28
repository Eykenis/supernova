using System;
using Supernova.Shop;
using UnityEngine;

namespace Supernova.Gameplay
{
    public enum PlayerInventoryItem
    {
        Empty = 0,
        Pickaxe = 1,
        Magnet = 2,
        Flashlight = 3,
    }

    // Kept for callers that used the tool API before the hotbar was introduced.
    public enum PlayerToolMode
    {
        None = 0,
        Pickaxe = 1,
        CartAttractor = 2,
        Flashlight = 3,
    }

    /// <summary>Fixed ten-slot player inventory used by the numeric hotbar.</summary>
    public sealed class PlayerInventory
    {
        public const int SlotCount = 10;

        private static readonly PlayerInventoryItem[] DefaultItems =
        {
            PlayerInventoryItem.Pickaxe,
            PlayerInventoryItem.Magnet,
            PlayerInventoryItem.Flashlight,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
        };

        private readonly PlayerInventoryItem[] items;
        private int selectedSlotIndex;

        public PlayerInventory(
            int initialSlotIndex = 0,
            Predicate<PlayerInventoryItem> ownsItem = null)
        {
            items = (PlayerInventoryItem[])DefaultItems.Clone();
            if (ownsItem != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    PlayerInventoryItem item = items[i];
                    if (item != PlayerInventoryItem.Empty && !ownsItem(item))
                        items[i] = PlayerInventoryItem.Empty;
                }
            }

            selectedSlotIndex = Mathf.Clamp(initialSlotIndex, 0, SlotCount - 1);
        }

        public event Action<int, PlayerInventoryItem> SelectionChanged;

        public int SelectedSlotIndex => selectedSlotIndex;
        public PlayerInventoryItem SelectedItem => items[selectedSlotIndex];

        public PlayerInventoryItem GetItemAtSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            return items[slotIndex];
        }

        public static PlayerInventoryItem GetDefaultItemAtSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            return DefaultItems[slotIndex];
        }

        public bool SetItemOwned(PlayerInventoryItem item, bool owned)
        {
            int slotIndex = Array.IndexOf(DefaultItems, item);
            if (slotIndex < 0 || item == PlayerInventoryItem.Empty)
                return false;

            PlayerInventoryItem nextItem = owned
                ? item
                : PlayerInventoryItem.Empty;
            if (items[slotIndex] == nextItem)
                return false;

            items[slotIndex] = nextItem;
            if (slotIndex == selectedSlotIndex)
                SelectionChanged?.Invoke(selectedSlotIndex, SelectedItem);
            return true;
        }

        public bool SelectSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            if (slotIndex == selectedSlotIndex) return false;

            selectedSlotIndex = slotIndex;
            SelectionChanged?.Invoke(selectedSlotIndex, SelectedItem);
            return true;
        }
    }

    /// <summary>
    /// Owns the player's ten-slot inventory selection and enables the tool represented by
    /// the selected slot. Number keys 1-9 select slots 0-8; number 0 selects slot 9.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class PlayerToolController : MonoBehaviour
    {
        [SerializeField, Range(0, PlayerInventory.SlotCount - 1)]
        private int initialSelectedSlot;
        [Tooltip("One definition per usable inventory item. The definition owns its left-click action and animation.")]
        [SerializeField] private PlayerToolDefinition[] toolDefinitions;
        [SerializeField] private FirstPersonCartAttractor cartAttractor;
        [Tooltip("Placeholder under the character hand where the selected tool model is instantiated.")]
        [SerializeField] private Transform toolModelMount;
        [SerializeField, Range(0, PlayerInventory.SlotCount - 1)]
        private int selectedSlotIndex;

        private PlayerInventory inventory;
        private GameObject equippedToolModel;
        private GameObject equippedToolModelPrefab;

        public event Action<int, PlayerInventoryItem> SelectionChanged;

        public PlayerInventory Inventory
        {
            get
            {
                EnsureInventory();
                return inventory;
            }
        }

        public int SelectedSlotIndex => Inventory.SelectedSlotIndex;
        public int SelectedSlotNumber => SelectedSlotIndex == 9 ? 0 : SelectedSlotIndex + 1;
        public PlayerInventoryItem SelectedItem => Inventory.SelectedItem;
        public PlayerToolDefinition SelectedDefinition => GetDefinition(SelectedItem);
        public PlayerToolMode CurrentTool => (PlayerToolMode)SelectedItem;
        public bool IsPickaxeSelected => SelectedItem == PlayerInventoryItem.Pickaxe;
        public bool IsCartAttractorSelected => SelectedItem == PlayerInventoryItem.Magnet;
        public bool IsFlashlightSelected => SelectedItem == PlayerInventoryItem.Flashlight;
        public GameObject EquippedToolModel => equippedToolModel;

        private void Awake()
        {
            ResolveReferences();
            EnsureInventory();
            ApplySelectedItem();
        }

        private void OnEnable()
        {
            PlayerEconomy.ItemOwnershipChanged += HandleItemOwnershipChanged;
            ResolveReferences();
            EnsureInventory();
            RefreshPurchasableItemOwnership();
            ApplySelectedItem();
        }

        private void OnDisable()
        {
            PlayerEconomy.ItemOwnershipChanged -= HandleItemOwnershipChanged;
            if (cartAttractor != null) cartAttractor.SetDeviceEnabled(false);
            ClearEquippedToolModel();
        }

        private void Update()
        {
            if (cartAttractor != null && cartAttractor.IsTowingCart) return;
            int requestedSlot = ReadRequestedSlot();
            if (requestedSlot >= 0) SelectSlot(requestedSlot);
        }

        public PlayerInventoryItem GetItemAtSlot(int slotIndex)
        {
            return Inventory.GetItemAtSlot(slotIndex);
        }

        public PlayerToolDefinition GetDefinition(PlayerInventoryItem item)
        {
            if (toolDefinitions == null) return null;
            for (int i = 0; i < toolDefinitions.Length; i++)
            {
                PlayerToolDefinition definition = toolDefinitions[i];
                if (definition != null && definition.Item == item) return definition;
            }

            return null;
        }

        public bool CanUseSelectedPrimaryAction()
        {
            PlayerToolDefinition definition = SelectedDefinition;
            if (definition == null || !definition.HasPrimaryAction) return false;
            return definition.PrimaryAction != PlayerToolPrimaryAction.AttractCart
                || (cartAttractor != null && cartAttractor.CanOperate);
        }

        public void SelectSlot(int slotIndex)
        {
            if (cartAttractor != null && cartAttractor.IsTowingCart) return;
            EnsureInventory();
            bool changed = inventory.SelectSlot(slotIndex);
            selectedSlotIndex = inventory.SelectedSlotIndex;
            ApplySelectedItem();
            if (changed)
                SelectionChanged?.Invoke(selectedSlotIndex, inventory.SelectedItem);
        }

        public void SelectTool(PlayerToolMode tool)
        {
            switch (tool)
            {
                case PlayerToolMode.CartAttractor:
                    SelectSlot(1);
                    break;
                case PlayerToolMode.Pickaxe:
                    SelectSlot(0);
                    break;
                case PlayerToolMode.Flashlight:
                    SelectSlot(2);
                    break;
                default:
                    SelectSlot(3);
                    break;
            }
        }

        private void EnsureInventory()
        {
            if (inventory != null) return;
            int slot = Application.isPlaying ? initialSelectedSlot : selectedSlotIndex;
            inventory = new PlayerInventory(slot, PlayerEconomy.IsItemOwned);
            selectedSlotIndex = inventory.SelectedSlotIndex;
        }

        public void RefreshPurchasableItemOwnership()
        {
            EnsureInventory();
            bool selectedItemChanged = false;
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                PlayerInventoryItem item =
                    PlayerInventory.GetDefaultItemAtSlot(i);
                if (item == PlayerInventoryItem.Empty)
                    continue;

                bool changed = inventory.SetItemOwned(
                    item,
                    PlayerEconomy.IsItemOwned(item));
                selectedItemChanged |= changed
                    && i == inventory.SelectedSlotIndex;
            }

            if (!selectedItemChanged)
                return;

            ApplySelectedItem();
            SelectionChanged?.Invoke(
                inventory.SelectedSlotIndex,
                inventory.SelectedItem);
        }

        private void HandleItemOwnershipChanged(
            PlayerInventoryItem item,
            bool isOwned)
        {
            EnsureInventory();
            int selectedSlot = inventory.SelectedSlotIndex;
            bool selectedItemChanged =
                inventory.GetItemAtSlot(selectedSlot) == item
                || PlayerInventory.GetDefaultItemAtSlot(selectedSlot) == item;
            if (!inventory.SetItemOwned(item, isOwned))
                return;

            if (selectedItemChanged)
            {
                ApplySelectedItem();
                SelectionChanged?.Invoke(
                    inventory.SelectedSlotIndex,
                    inventory.SelectedItem);
            }
        }

        private void ApplySelectedItem()
        {
            ResolveReferences();
            PlayerToolDefinition definition = SelectedDefinition;
            if (cartAttractor != null)
            {
                bool usesMagnet = definition != null
                    ? definition.PrimaryAction == PlayerToolPrimaryAction.AttractCart
                    : IsCartAttractorSelected;
                cartAttractor.SetDeviceEnabled(usesMagnet);
            }
            ApplyEquippedToolModel(definition);
        }

        private void ApplyEquippedToolModel(PlayerToolDefinition definition)
        {
            GameObject modelPrefab = definition != null
                ? definition.HeldModelPrefab
                : null;
            if (equippedToolModel != null
                && equippedToolModelPrefab == modelPrefab)
            {
                return;
            }

            ClearEquippedToolModel();
            if (toolModelMount == null || modelPrefab == null)
            {
                return;
            }

            equippedToolModel = Instantiate(modelPrefab, toolModelMount, false);
            equippedToolModel.name = modelPrefab.name;
            equippedToolModel.transform.localPosition = Vector3.zero;
            equippedToolModel.transform.localRotation = Quaternion.identity;
            equippedToolModel.transform.localScale = Vector3.one;
            equippedToolModelPrefab = modelPrefab;
        }

        private void ClearEquippedToolModel()
        {
            equippedToolModelPrefab = null;
            if (equippedToolModel == null)
            {
                return;
            }

            equippedToolModel.SetActive(false);
            equippedToolModel.transform.SetParent(null, false);
            if (Application.isPlaying)
            {
                Destroy(equippedToolModel);
            }
            else
            {
                DestroyImmediate(equippedToolModel);
            }
            equippedToolModel = null;
        }

        private void ResolveReferences()
        {
            if (cartAttractor == null)
                cartAttractor = GetComponent<FirstPersonCartAttractor>();
        }

        private static int ReadRequestedSlot()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) return 0;
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) return 1;
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) return 2;
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) return 3;
            if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) return 4;
            if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6)) return 5;
            if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7)) return 6;
            if (Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8)) return 7;
            if (Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9)) return 8;
            if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0)) return 9;
            return -1;
        }
    }
}
