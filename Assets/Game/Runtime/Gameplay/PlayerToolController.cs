using System;
using System.Collections.Generic;
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
        Gun = 4,
        Rifle = Gun,
        SolidGun = 5,
        SMG = 6,
        Cart = 7,
    }

    public enum PlayerUpgrade
    {
        None = 0,
        AttractionModule = 1,
    }

    [Serializable]
    public sealed class PlayerOwnedItems
    {
        [SerializeField] private List<PlayerInventoryItem> inventoryItems =
            new List<PlayerInventoryItem>
            {
                PlayerInventoryItem.Pickaxe,
                PlayerInventoryItem.Magnet,
            };
        [SerializeField] private List<PlayerUpgrade> upgrades =
            new List<PlayerUpgrade>();

        public IReadOnlyList<PlayerInventoryItem> InventoryItems
        {
            get
            {
                EnsureCollections();
                return inventoryItems;
            }
        }
        public IReadOnlyList<PlayerUpgrade> Upgrades
        {
            get
            {
                EnsureCollections();
                return upgrades;
            }
        }

        public bool Owns(PlayerInventoryItem item)
        {
            EnsureCollections();
            return item != PlayerInventoryItem.Empty
                && inventoryItems.Contains(item);
        }

        public bool Owns(PlayerUpgrade upgrade)
        {
            EnsureCollections();
            return upgrade != PlayerUpgrade.None
                && upgrades.Contains(upgrade);
        }

        public bool SetOwned(PlayerInventoryItem item, bool owned)
        {
            EnsureCollections();
            if (item == PlayerInventoryItem.Empty) return false;
            bool current = inventoryItems.Contains(item);
            if (current == owned) return false;
            if (owned)
                inventoryItems.Add(item);
            else
                inventoryItems.Remove(item);
            return true;
        }

        public bool SetOwned(PlayerUpgrade upgrade, bool owned)
        {
            EnsureCollections();
            if (upgrade == PlayerUpgrade.None) return false;
            bool current = upgrades.Contains(upgrade);
            if (current == owned) return false;
            if (owned)
                upgrades.Add(upgrade);
            else
                upgrades.Remove(upgrade);
            return true;
        }

        private void EnsureCollections()
        {
            if (inventoryItems == null)
                inventoryItems = new List<PlayerInventoryItem>();
            if (upgrades == null)
                upgrades = new List<PlayerUpgrade>();
        }
    }

    // Kept for callers that used the tool API before the hotbar was introduced.
    public enum PlayerToolMode
    {
        None = 0,
        Pickaxe = 1,
        CartAttractor = 2,
        Flashlight = 3,
        Gun = 4,
        Rifle = Gun,
        SolidGun = 5,
        SMG = 6,
        Cart = 7,
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
            PlayerInventoryItem.Gun,
            PlayerInventoryItem.SolidGun,
            PlayerInventoryItem.SMG,
            PlayerInventoryItem.Cart,
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

    /// <summary>Runtime ammunition stock keyed by the firearm's inventory item.</summary>
    public sealed class PlayerAmmunitionInventory
    {
        private readonly Dictionary<PlayerInventoryItem, int> counts =
            new Dictionary<PlayerInventoryItem, int>();

        public event Action<PlayerInventoryItem, int> AmmunitionChanged;

        public int Get(PlayerInventoryItem item)
        {
            return counts.TryGetValue(item, out int count) ? count : 0;
        }

        public void Initialize(PlayerInventoryItem item, int count)
        {
            if (item == PlayerInventoryItem.Empty || counts.ContainsKey(item)) return;
            counts.Add(item, Mathf.Max(0, count));
        }

        public bool TryConsume(PlayerInventoryItem item, int amount = 1)
        {
            amount = Mathf.Max(1, amount);
            int count = Get(item);
            if (count < amount) return false;

            count -= amount;
            counts[item] = count;
            AmmunitionChanged?.Invoke(item, count);
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
        [Tooltip("Inspector-visible snapshot of the items and upgrades owned by this player.")]
        [SerializeField] private PlayerOwnedItems ownedItems =
            new PlayerOwnedItems();
        [Tooltip("One definition per usable inventory item. The definition owns its left-click action and animation.")]
        [SerializeField] private PlayerToolDefinition[] toolDefinitions;
        [SerializeField] private FirstPersonCartAttractor cartAttractor;
        [Tooltip("Right-hand mount used by tools with the Single Hand mounting strategy.")]
        [SerializeField] private Transform toolModelMount;
        [SerializeField, Range(0, PlayerInventory.SlotCount - 1)]
        private int selectedSlotIndex;

        private PlayerInventory inventory;
        private PlayerAmmunitionInventory ammunitionInventory;
        private GameObject equippedToolModel;
        private GameObject equippedToolModelPrefab;
        private WeaponMuzzle equippedWeaponMuzzle;
        private Transform rifleModelMount;

        public event Action<int, PlayerInventoryItem> SelectionChanged;
        public event Action<PlayerInventoryItem, int> AmmunitionChanged;

        public PlayerInventory Inventory
        {
            get
            {
                EnsureInventory();
                return inventory;
            }
        }

        public int SelectedSlotIndex => Inventory.SelectedSlotIndex;
        public PlayerOwnedItems OwnedItems => ownedItems;
        public int SelectedSlotNumber => SelectedSlotIndex == 9 ? 0 : SelectedSlotIndex + 1;
        public PlayerInventoryItem SelectedItem => Inventory.SelectedItem;
        public PlayerToolDefinition SelectedDefinition => GetDefinition(SelectedItem);
        public PlayerToolMode CurrentTool => (PlayerToolMode)SelectedItem;
        public bool IsPickaxeSelected => SelectedItem == PlayerInventoryItem.Pickaxe;
        public bool IsCartAttractorSelected => SelectedItem == PlayerInventoryItem.Magnet;
        public bool IsFlashlightSelected => SelectedItem == PlayerInventoryItem.Flashlight;
        public bool IsCartSelected => SelectedItem == PlayerInventoryItem.Cart;
        public bool IsRifleSelected =>
            SelectedDefinition != null && SelectedDefinition.IsFirearm;
        public GameObject EquippedToolModel => equippedToolModel;
        public Transform EquippedWeaponMuzzle => equippedWeaponMuzzle != null
            ? equippedWeaponMuzzle.Origin
            : null;
        public Transform RifleModelMount => rifleModelMount;

        private void Awake()
        {
            ResolveReferences();
            EnsureInventory();
            ApplySelectedItem();
        }

        private void OnEnable()
        {
            PlayerEconomy.ItemOwnershipChanged += HandleItemOwnershipChanged;
            PlayerEconomy.UpgradeOwnershipChanged +=
                HandleUpgradeOwnershipChanged;
            ResolveReferences();
            EnsureInventory();
            RefreshPurchasableItemOwnership();
            ApplySelectedItem();
        }

        private void OnDisable()
        {
            PlayerEconomy.ItemOwnershipChanged -= HandleItemOwnershipChanged;
            PlayerEconomy.UpgradeOwnershipChanged -=
                HandleUpgradeOwnershipChanged;
            if (cartAttractor != null)
            {
                cartAttractor.SetDeviceEnabled(false);
                cartAttractor.SetCartTowEnabled(false);
            }
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
            if (definition.IsFirearm)
            {
                return definition.FirearmProjectilePrefab != null
                    && GetAmmunition(definition.Item) > 0;
            }
            if (definition.PrimaryAction == PlayerToolPrimaryAction.TowCart)
                return false;
            return definition.PrimaryAction != PlayerToolPrimaryAction.AttractCart
                || (cartAttractor != null && cartAttractor.CanOperate);
        }

        public int GetAmmunition(PlayerInventoryItem item)
        {
            EnsureAmmunitionInventory();
            return ammunitionInventory.Get(item);
        }

        public bool TryConsumeAmmunition(PlayerInventoryItem item)
        {
            EnsureAmmunitionInventory();
            return ammunitionInventory.TryConsume(item);
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
                case PlayerToolMode.Rifle:
                    SelectSlot(3);
                    break;
                case PlayerToolMode.SolidGun:
                    SelectSlot(4);
                    break;
                case PlayerToolMode.SMG:
                    SelectSlot(5);
                    break;
                case PlayerToolMode.Cart:
                    SelectSlot(6);
                    break;
                default:
                    SelectSlot(3);
                    break;
            }
        }

        private void EnsureInventory()
        {
            if (inventory != null) return;
            SynchronizeOwnedItems();
            int slot = Application.isPlaying ? initialSelectedSlot : selectedSlotIndex;
            inventory = new PlayerInventory(slot, ownedItems.Owns);
            selectedSlotIndex = inventory.SelectedSlotIndex;
        }

        private void EnsureAmmunitionInventory()
        {
            if (ammunitionInventory != null) return;

            ammunitionInventory = new PlayerAmmunitionInventory();
            ammunitionInventory.AmmunitionChanged += HandleAmmunitionChanged;
            if (toolDefinitions == null) return;
            for (int i = 0; i < toolDefinitions.Length; i++)
            {
                PlayerToolDefinition definition = toolDefinitions[i];
                if (definition != null && definition.IsFirearm)
                {
                    ammunitionInventory.Initialize(
                        definition.Item,
                        definition.InitialAmmunition);
                }
            }
        }

        private void HandleAmmunitionChanged(PlayerInventoryItem item, int count)
        {
            AmmunitionChanged?.Invoke(item, count);
        }

        public void RefreshPurchasableItemOwnership()
        {
            EnsureInventory();
            SynchronizeOwnedItems();
            bool selectedItemChanged = false;
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                PlayerInventoryItem item =
                    PlayerInventory.GetDefaultItemAtSlot(i);
                if (item == PlayerInventoryItem.Empty)
                    continue;

                bool changed = inventory.SetItemOwned(
                    item,
                    ownedItems.Owns(item));
                selectedItemChanged |= changed
                    && i == inventory.SelectedSlotIndex;
            }

            ApplyAttractionModuleUpgrade();
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
            ownedItems.SetOwned(item, isOwned);
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

        private void HandleUpgradeOwnershipChanged(
            PlayerUpgrade upgrade,
            bool isOwned)
        {
            if (ownedItems == null) ownedItems = new PlayerOwnedItems();
            ownedItems.SetOwned(upgrade, isOwned);
            ApplyAttractionModuleUpgrade();
        }

        private void SynchronizeOwnedItems()
        {
            if (ownedItems == null) ownedItems = new PlayerOwnedItems();
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                PlayerInventoryItem item =
                    PlayerInventory.GetDefaultItemAtSlot(i);
                if (item == PlayerInventoryItem.Empty) continue;
                ownedItems.SetOwned(
                    item,
                    PlayerEconomy.IsItemOwned(item));
            }
            ownedItems.SetOwned(
                PlayerUpgrade.AttractionModule,
                PlayerEconomy.IsUpgradeOwned(
                    PlayerUpgrade.AttractionModule));
        }

        private void ApplyAttractionModuleUpgrade()
        {
            ResolveReferences();
            if (cartAttractor == null) return;
            bool upgraded = ownedItems != null
                && ownedItems.Owns(PlayerUpgrade.AttractionModule);
            cartAttractor.SetAttractionForceUpgrade(
                upgraded
                    ? FirstPersonCartAttractor.AttractionModuleUpgradeForce
                    : 0f);
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
                bool usesCartTow = definition != null
                    ? definition.PrimaryAction == PlayerToolPrimaryAction.TowCart
                    : IsCartSelected;
                cartAttractor.SetCartTowEnabled(usesCartTow);
            }
            ApplyEquippedToolModel(definition);
        }

        private void ApplyEquippedToolModel(PlayerToolDefinition definition)
        {
            GameObject modelPrefab = definition != null
                ? definition.HeldModelPrefab
                : null;
            Transform modelMount = modelPrefab != null
                ? ResolveModelMount(definition)
                : null;
            if (equippedToolModel != null
                && equippedToolModelPrefab == modelPrefab
                && equippedToolModel.transform.parent == modelMount)
            {
                return;
            }

            ClearEquippedToolModel();
            if (modelMount == null || modelPrefab == null)
            {
                return;
            }

            equippedToolModel = Instantiate(modelPrefab, modelMount, false);
            equippedToolModel.name = modelPrefab.name;
            if (definition.HeldModelMountStrategy == HeldToolMountStrategy.SingleHand)
            {
                equippedToolModel.transform.localPosition = Vector3.zero;
                equippedToolModel.transform.localRotation = Quaternion.identity;
                equippedToolModel.transform.localScale = Vector3.one;
            }
            else
            {
                equippedToolModel.transform.localPosition =
                    modelPrefab.transform.localPosition;
                equippedToolModel.transform.localRotation =
                    modelPrefab.transform.localRotation;
                equippedToolModel.transform.localScale =
                    modelPrefab.transform.localScale;
            }
            equippedToolModelPrefab = modelPrefab;
            equippedWeaponMuzzle =
                equippedToolModel.GetComponentInChildren<WeaponMuzzle>(true);
        }

        private Transform ResolveModelMount(PlayerToolDefinition definition)
        {
            if (definition == null
                || definition.HeldModelMountStrategy == HeldToolMountStrategy.SingleHand)
            {
                return toolModelMount;
            }

            Animator animator = GetComponentInChildren<Animator>(false);
            Transform mountParent = null;
            if (animator != null && animator.isHuman)
            {
                mountParent = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            }
            if (mountParent == null)
                mountParent = toolModelMount != null
                    ? toolModelMount
                    : transform;

            if (rifleModelMount == null)
            {
                GameObject mountObject = new GameObject("Rifle Model Mount");
                rifleModelMount = mountObject.transform;
            }
            if (rifleModelMount.parent != mountParent)
                rifleModelMount.SetParent(mountParent, false);
            rifleModelMount.localPosition = Vector3.zero;
            rifleModelMount.localRotation = Quaternion.identity;
            rifleModelMount.localScale = Vector3.one;
            return rifleModelMount;
        }

        private void ClearEquippedToolModel()
        {
            equippedToolModelPrefab = null;
            equippedWeaponMuzzle = null;
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
