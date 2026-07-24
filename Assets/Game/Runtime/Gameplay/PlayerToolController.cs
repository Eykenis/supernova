using System;
using UnityEngine;

namespace Supernova.Gameplay
{
    public enum PlayerInventoryItem
    {
        Empty = 0,
        Pickaxe = 1,
        Magnet = 2,
    }

    // Kept for callers that used the tool API before the hotbar was introduced.
    public enum PlayerToolMode
    {
        None = 0,
        Pickaxe = 1,
        CartAttractor = 2,
    }

    /// <summary>Fixed ten-slot player inventory used by the numeric hotbar.</summary>
    public sealed class PlayerInventory
    {
        public const int SlotCount = 10;

        private static readonly PlayerInventoryItem[] Items =
        {
            PlayerInventoryItem.Pickaxe,
            PlayerInventoryItem.Magnet,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
        };

        private int selectedSlotIndex;

        public PlayerInventory(int initialSlotIndex = 0)
        {
            selectedSlotIndex = Mathf.Clamp(initialSlotIndex, 0, SlotCount - 1);
        }

        public event Action<int, PlayerInventoryItem> SelectionChanged;

        public int SelectedSlotIndex => selectedSlotIndex;
        public PlayerInventoryItem SelectedItem => Items[selectedSlotIndex];

        public PlayerInventoryItem GetItemAtSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            return Items[slotIndex];
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
        [SerializeField] private FirstPersonCartAttractor cartAttractor;
        [SerializeField, Range(0, PlayerInventory.SlotCount - 1)]
        private int selectedSlotIndex;

        private PlayerInventory inventory;

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
        public PlayerToolMode CurrentTool => (PlayerToolMode)SelectedItem;
        public bool IsPickaxeSelected => SelectedItem == PlayerInventoryItem.Pickaxe;
        public bool IsCartAttractorSelected => SelectedItem == PlayerInventoryItem.Magnet;

        private void Awake()
        {
            ResolveReferences();
            EnsureInventory();
            ApplySelectedItem();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureInventory();
            ApplySelectedItem();
        }

        private void OnDisable()
        {
            if (cartAttractor != null) cartAttractor.SetDeviceEnabled(false);
        }

        private void Update()
        {
            int requestedSlot = ReadRequestedSlot();
            if (requestedSlot >= 0) SelectSlot(requestedSlot);
        }

        public PlayerInventoryItem GetItemAtSlot(int slotIndex)
        {
            return Inventory.GetItemAtSlot(slotIndex);
        }

        public void SelectSlot(int slotIndex)
        {
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
                default:
                    SelectSlot(2);
                    break;
            }
        }

        private void EnsureInventory()
        {
            if (inventory != null) return;
            int slot = Application.isPlaying ? initialSelectedSlot : selectedSlotIndex;
            inventory = new PlayerInventory(slot);
            selectedSlotIndex = inventory.SelectedSlotIndex;
        }

        private void ApplySelectedItem()
        {
            ResolveReferences();
            if (cartAttractor != null)
                cartAttractor.SetDeviceEnabled(IsCartAttractorSelected);
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
