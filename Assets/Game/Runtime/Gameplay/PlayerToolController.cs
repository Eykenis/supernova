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
        GrabHook = 8,
        Bomb = 9,
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
        GrabHook = 8,
        Bomb = 9,
    }

    /// <summary>
    /// Five configurable quick slots. Item ownership lives in <see cref="PlayerOwnedItems"/>;
    /// this type only tracks which owned items the player placed on the hotbar.
    /// </summary>
    public sealed class PlayerInventory
    {
        public const int SlotCount = 5;

        private static readonly PlayerInventoryItem[] DefaultItems =
        {
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
            Predicate<PlayerInventoryItem> ownsItem = null,
            IReadOnlyList<PlayerInventoryItem> configuredItems = null)
        {
            items = (PlayerInventoryItem[])DefaultItems.Clone();
            if (configuredItems != null)
            {
                int configuredCount = Mathf.Min(
                    configuredItems.Count,
                    SlotCount);
                for (int i = 0; i < configuredCount; i++)
                {
                    PlayerInventoryItem item = configuredItems[i];
                    if (item == PlayerInventoryItem.Empty
                        || ownsItem == null
                        || ownsItem(item))
                    {
                        SetItemAtSlot(i, item);
                    }
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

        public int IndexOf(PlayerInventoryItem item)
        {
            return Array.IndexOf(items, item);
        }

        public bool SetItemAtSlot(
            int slotIndex,
            PlayerInventoryItem item)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));

            bool changed = false;
            if (item != PlayerInventoryItem.Empty)
            {
                int existingSlot = Array.IndexOf(items, item);
                if (existingSlot >= 0 && existingSlot != slotIndex)
                {
                    items[existingSlot] = PlayerInventoryItem.Empty;
                    changed = true;
                }
            }

            if (items[slotIndex] == item)
                return changed;

            items[slotIndex] = item;
            return true;
        }

        public bool RemoveItem(PlayerInventoryItem item)
        {
            if (item == PlayerInventoryItem.Empty)
                return false;

            int slotIndex = Array.IndexOf(items, item);
            if (slotIndex < 0)
                return false;

            items[slotIndex] = PlayerInventoryItem.Empty;
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
    /// Owns the player's five configurable quick slots and enables the selected tool.
    /// Number keys 1-5 select slots 0-4.
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
        [Tooltip("Fallback quick-slot configuration used until the player saves a loadout.")]
        [SerializeField] private PlayerInventoryItem[] configuredSlots =
        {
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
        };
        [Tooltip("One definition per usable inventory item. The definition owns its left-click action and animation.")]
        [SerializeField] private PlayerToolDefinition[] toolDefinitions;
        [SerializeField] private FirstPersonCartAttractor cartAttractor;
        [SerializeField] private GrabHookController grabHook;
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
        public event Action LoadoutChanged;
        public event Action OwnedItemsChanged;
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
        public int SelectedSlotNumber => SelectedSlotIndex + 1;
        public PlayerInventoryItem SelectedItem => Inventory.SelectedItem;
        public PlayerToolDefinition SelectedDefinition => GetDefinition(SelectedItem);
        public PlayerToolMode CurrentTool => (PlayerToolMode)SelectedItem;
        public bool IsPickaxeSelected => SelectedItem == PlayerInventoryItem.Pickaxe;
        public bool IsCartAttractorSelected => SelectedItem == PlayerInventoryItem.Magnet;
        public bool IsFlashlightSelected => SelectedItem == PlayerInventoryItem.Flashlight;
        public bool IsCartSelected => SelectedItem == PlayerInventoryItem.Cart;
        public bool IsGrabHookSelected =>
            SelectedItem == PlayerInventoryItem.GrabHook;
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
            if (grabHook != null) grabHook.SetDeviceEnabled(false);
            ClearEquippedToolModel();
        }

        private void Update()
        {
            if (Supernova.UI.GameHudController.IsGameplayInputBlocked) return;
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
            if (definition.PrimaryAction == PlayerToolPrimaryAction.FireGrabHook)
                return grabHook != null
                    && grabHook.CanUsePrimaryAction(definition);
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
            EnsureInventory();
            int slotIndex = inventory.IndexOf((PlayerInventoryItem)tool);
            if (slotIndex >= 0)
                SelectSlot(slotIndex);
        }

        public bool ConfigureSlot(
            int slotIndex,
            PlayerInventoryItem item)
        {
            EnsureInventory();
            if (item != PlayerInventoryItem.Empty
                && !ownedItems.Owns(item))
            {
                return false;
            }

            PlayerInventoryItem previousSelectedItem =
                inventory.SelectedItem;
            if (!inventory.SetItemAtSlot(slotIndex, item))
                return false;

            CaptureConfiguredSlots();
            SaveConfiguredSlots();
            ApplySelectedItem();
            LoadoutChanged?.Invoke();
            if (previousSelectedItem != inventory.SelectedItem)
            {
                SelectionChanged?.Invoke(
                    inventory.SelectedSlotIndex,
                    inventory.SelectedItem);
            }
            return true;
        }

        private void EnsureInventory()
        {
            if (inventory != null) return;
            SynchronizeOwnedItems();
            int slot = Application.isPlaying ? initialSelectedSlot : selectedSlotIndex;
            LoadConfiguredSlots();
            inventory = new PlayerInventory(
                slot,
                ownedItems.Owns,
                configuredSlots);
            selectedSlotIndex = inventory.SelectedSlotIndex;
            CaptureConfiguredSlots();
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
            PlayerInventoryItem previousSelectedItem =
                inventory.SelectedItem;
            bool loadoutChanged = false;
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                PlayerInventoryItem item = inventory.GetItemAtSlot(i);
                if (item != PlayerInventoryItem.Empty
                    && !ownedItems.Owns(item))
                {
                    loadoutChanged |= inventory.SetItemAtSlot(
                        i,
                        PlayerInventoryItem.Empty);
                }
            }

            ApplyAttractionModuleUpgrade();
            OwnedItemsChanged?.Invoke();
            if (loadoutChanged)
            {
                CaptureConfiguredSlots();
                SaveConfiguredSlots();
                ApplySelectedItem();
                LoadoutChanged?.Invoke();
                if (previousSelectedItem != inventory.SelectedItem)
                {
                    SelectionChanged?.Invoke(
                        inventory.SelectedSlotIndex,
                        inventory.SelectedItem);
                }
            }
        }

        private void HandleItemOwnershipChanged(
            PlayerInventoryItem item,
            bool isOwned)
        {
            EnsureInventory();
            ownedItems.SetOwned(item, isOwned);
            OwnedItemsChanged?.Invoke();
            if (isOwned)
                return;

            PlayerInventoryItem previousSelectedItem =
                inventory.SelectedItem;
            if (inventory.RemoveItem(item))
            {
                CaptureConfiguredSlots();
                SaveConfiguredSlots();
                ApplySelectedItem();
                LoadoutChanged?.Invoke();
                if (previousSelectedItem != inventory.SelectedItem)
                {
                    SelectionChanged?.Invoke(
                        inventory.SelectedSlotIndex,
                        inventory.SelectedItem);
                }
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
            Array values = Enum.GetValues(typeof(PlayerInventoryItem));
            for (int i = 0; i < values.Length; i++)
            {
                PlayerInventoryItem item =
                    (PlayerInventoryItem)values.GetValue(i);
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

        private void LoadConfiguredSlots()
        {
            EnsureConfiguredSlotsArray();
            if (!Application.isPlaying)
                return;

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                if (PlayerEconomy.HasQuickSlotConfiguration(i))
                {
                    configuredSlots[i] =
                        PlayerEconomy.GetQuickSlotItem(i);
                }
            }
        }

        private void CaptureConfiguredSlots()
        {
            EnsureConfiguredSlotsArray();
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
                configuredSlots[i] = inventory.GetItemAtSlot(i);
        }

        private void SaveConfiguredSlots()
        {
            if (!Application.isPlaying)
                return;

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                PlayerEconomy.SetQuickSlotItem(
                    i,
                    inventory.GetItemAtSlot(i),
                    false);
            }
            PlayerPrefs.Save();
        }

        private void EnsureConfiguredSlotsArray()
        {
            if (configuredSlots != null
                && configuredSlots.Length == PlayerInventory.SlotCount)
            {
                return;
            }

            PlayerInventoryItem[] resized =
                new PlayerInventoryItem[PlayerInventory.SlotCount];
            if (configuredSlots != null)
            {
                Array.Copy(
                    configuredSlots,
                    resized,
                    Mathf.Min(
                        configuredSlots.Length,
                        resized.Length));
            }
            configuredSlots = resized;
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
            if (grabHook != null)
            {
                grabHook.SetDeviceEnabled(
                    definition != null
                    && definition.PrimaryAction
                        == PlayerToolPrimaryAction.FireGrabHook);
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
            if (grabHook == null)
                grabHook = GetComponent<GrabHookController>();
        }

        private static int ReadRequestedSlot()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) return 0;
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) return 1;
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) return 2;
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) return 3;
            if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) return 4;
            return -1;
        }
    }
}
