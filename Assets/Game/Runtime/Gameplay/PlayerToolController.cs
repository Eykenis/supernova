using System;
using Supernova.Inputs;
using System.Collections.Generic;
using Supernova.Shop;
using UnityEngine;

namespace Supernova.Gameplay
{
    public enum PlayerInventoryItem
    {
        Empty = 0,
        Pickaxe = 1,
        Flashlight = 3,
        SolidGun = 5,
        Bomb = 9,
        PortalGun = 10,
    }

    public enum PlayerUpgrade
    {
        None = 0,
        MagnetAttractionForce = 1,
    }

    [Serializable]
    public sealed class PlayerOwnedItems
    {
        [SerializeField] private List<PlayerInventoryItem> inventoryItems =
            new List<PlayerInventoryItem>
            {
                PlayerInventoryItem.Pickaxe,
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

        public void ResetTo(
            IReadOnlyList<PlayerInventoryItem> items,
            IReadOnlyList<PlayerUpgrade> startingUpgrades = null)
        {
            inventoryItems = new List<PlayerInventoryItem>();
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    PlayerInventoryItem item = items[i];
                    if (item != PlayerInventoryItem.Empty
                        && !inventoryItems.Contains(item))
                    {
                        inventoryItems.Add(item);
                    }
                }
            }

            upgrades = new List<PlayerUpgrade>();
            if (startingUpgrades == null) return;
            for (int i = 0; i < startingUpgrades.Count; i++)
            {
                PlayerUpgrade upgrade = startingUpgrades[i];
                if (upgrade != PlayerUpgrade.None
                    && !upgrades.Contains(upgrade))
                {
                    upgrades.Add(upgrade);
                }
            }
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
        Flashlight = 3,
        SolidGun = 5,
        Bomb = 9,
        PortalGun = 10,
    }

    /// <summary>
    /// Five configurable quick slots. Item ownership lives in <see cref="PlayerOwnedItems"/>;
    /// this type only tracks which owned items the player placed on the hotbar.
    /// </summary>
    public sealed class PlayerInventory
    {
        public const int SlotCount = 5;
        public const int FixedPickaxeSlotIndex = 0;

        private static readonly PlayerInventoryItem[] DefaultItems =
        {
            PlayerInventoryItem.Pickaxe,
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
                PlayerInventoryItem displacedFirstSlot =
                    configuredCount > FixedPickaxeSlotIndex
                        ? configuredItems[FixedPickaxeSlotIndex]
                        : PlayerInventoryItem.Empty;
                if (displacedFirstSlot == PlayerInventoryItem.Pickaxe
                    || displacedFirstSlot == PlayerInventoryItem.Empty
                    || (ownsItem != null && !ownsItem(displacedFirstSlot)))
                {
                    displacedFirstSlot = PlayerInventoryItem.Empty;
                }

                for (int i = FixedPickaxeSlotIndex + 1;
                    i < configuredCount;
                    i++)
                {
                    PlayerInventoryItem item = configuredItems[i];
                    if (item == PlayerInventoryItem.Pickaxe)
                    {
                        item = displacedFirstSlot;
                        displacedFirstSlot = PlayerInventoryItem.Empty;
                    }
                    if (item == PlayerInventoryItem.Empty
                        || ownsItem == null
                        || ownsItem(item))
                    {
                        SetItemAtSlot(i, item);
                    }
                }

                if (displacedFirstSlot != PlayerInventoryItem.Empty
                    && IndexOf(displacedFirstSlot) < 0)
                {
                    for (int i = FixedPickaxeSlotIndex + 1;
                        i < SlotCount;
                        i++)
                    {
                        if (items[i] != PlayerInventoryItem.Empty)
                            continue;
                        SetItemAtSlot(i, displacedFirstSlot);
                        break;
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

        public static bool IsFixedSlot(int slotIndex)
        {
            return slotIndex == FixedPickaxeSlotIndex;
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
            if (IsFixedSlot(slotIndex)
                || item == PlayerInventoryItem.Pickaxe)
            {
                return false;
            }

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
            if (item == PlayerInventoryItem.Empty
                || item == PlayerInventoryItem.Pickaxe)
                return false;

            int slotIndex = Array.IndexOf(items, item);
            if (slotIndex < 0)
                return false;

            items[slotIndex] = PlayerInventoryItem.Empty;
            return true;
        }

        internal bool TemporarilyRemoveItemAtSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            if (items[slotIndex] == PlayerInventoryItem.Empty)
                return false;

            items[slotIndex] = PlayerInventoryItem.Empty;
            return true;
        }

        internal bool RestoreTemporaryItemAtSlot(
            int slotIndex,
            PlayerInventoryItem item)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            if (items[slotIndex] != PlayerInventoryItem.Empty)
                return false;
            if (IsFixedSlot(slotIndex))
            {
                if (item != PlayerInventoryItem.Pickaxe)
                    return false;
            }
            else if (item == PlayerInventoryItem.Pickaxe)
            {
                return false;
            }

            items[slotIndex] = item;
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
        public static event Action<PlayerToolController> InstanceEnabled;
        public static event Action<PlayerToolController> InstanceDisabled;

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
        [Tooltip("Right-hand mount used by tools with the Single Hand mounting strategy.")]
        [SerializeField] private Transform toolModelMount;
        [SerializeField, Range(0, PlayerInventory.SlotCount - 1)]
        private int selectedSlotIndex;

        private PlayerInventory inventory;
        private PlayerAmmunitionInventory ammunitionInventory;
        private readonly Dictionary<PlayerInventoryItem, int> suspendedItemSlots =
            new Dictionary<PlayerInventoryItem, int>();
        private GameObject equippedToolModel;
        private GameObject equippedToolModelPrefab;
        private bool equippedToolModelHidden;
        private WeaponMuzzle equippedWeaponMuzzle;
        private Transform twoHandedModelMount;
        private PlayerInventorySessionSettings sessionSettings;
        private PlayerInventoryItem[] defaultConfiguredSlots;

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
        public bool IsFlashlightSelected => SelectedItem == PlayerInventoryItem.Flashlight;
        public bool UsesPersistentPlayerData
        {
            get
            {
                ResolveReferences();
                return sessionSettings == null
                    || !sessionSettings.IsolatedFromPersistentData;
            }
        }
        public bool IsFirearmSelected =>
            SelectedDefinition != null && SelectedDefinition.IsFirearm;
        public GameObject EquippedToolModel => equippedToolModel;
        public Transform EquippedWeaponMuzzle => equippedWeaponMuzzle != null
            ? equippedWeaponMuzzle.Origin
            : null;
        public Transform TwoHandedModelMount => twoHandedModelMount;

        private void Awake()
        {
            CacheDefaultConfiguredSlots();
            ResolveReferences();
            EnsureInventory();
            ApplySelectedItem();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeEvents()
        {
            InstanceEnabled = null;
            InstanceDisabled = null;
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (UsesPersistentPlayerData)
            {
                PlayerEconomy.ItemOwnershipChanged +=
                    HandleItemOwnershipChanged;
                PlayerEconomy.UpgradeOwnershipChanged +=
                    HandleUpgradeOwnershipChanged;
                PlayerEconomy.SavedProgressCleared +=
                    HandleSavedProgressCleared;
            }
            EnsureInventory();
            RefreshPurchasableItemOwnership();
            ApplySelectedItem();
            InstanceEnabled?.Invoke(this);
        }

        private void OnDisable()
        {
            InstanceDisabled?.Invoke(this);
            PlayerEconomy.ItemOwnershipChanged -= HandleItemOwnershipChanged;
            PlayerEconomy.UpgradeOwnershipChanged -=
                HandleUpgradeOwnershipChanged;
            PlayerEconomy.SavedProgressCleared -=
                HandleSavedProgressCleared;
            equippedToolModelHidden = false;
            ClearEquippedToolModel();
        }

        private void Update()
        {
            if (Supernova.UI.GameHudController.IsGameplayInputBlocked) return;
            int requestedSlot = ReadRequestedSlot();
            if (requestedSlot >= 0) SelectSlot(requestedSlot);
        }

        public PlayerInventoryItem GetItemAtSlot(int slotIndex)
        {
            return Inventory.GetItemAtSlot(slotIndex);
        }

        /// <summary>
        /// Returns the item represented by a hotbar slot even while that item is
        /// temporarily out of the player's hands, such as a thrown pickaxe.
        /// Gameplay selection continues to use <see cref="GetItemAtSlot"/>, which
        /// correctly reports the suspended slot as empty.
        /// </summary>
        public PlayerInventoryItem GetDisplayItemAtSlot(int slotIndex)
        {
            PlayerInventoryItem item = Inventory.GetItemAtSlot(slotIndex);
            if (item != PlayerInventoryItem.Empty)
                return item;

            foreach (KeyValuePair<PlayerInventoryItem, int> suspended
                in suspendedItemSlots)
            {
                if (suspended.Value == slotIndex)
                    return suspended.Key;
            }
            return PlayerInventoryItem.Empty;
        }

        public bool IsItemSuspendedAtSlot(int slotIndex)
        {
            Inventory.GetItemAtSlot(slotIndex);
            foreach (KeyValuePair<PlayerInventoryItem, int> suspended
                in suspendedItemSlots)
            {
                if (suspended.Value == slotIndex)
                    return true;
            }
            return false;
        }

        public bool OwnsItem(PlayerInventoryItem item)
        {
            EnsureInventory();
            return ownedItems.Owns(item);
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
            return true;
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
                && (!ownedItems.Owns(item) || IsItemSuspended(item)))
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

        public bool TryAddOwnedItem(PlayerInventoryItem item)
        {
            EnsureInventory();
            if (item == PlayerInventoryItem.Empty || ownedItems.Owns(item))
                return false;

            if (UsesPersistentPlayerData)
            {
                if (!PlayerEconomy.SetItemOwned(item, true))
                    return false;

                // The economy event normally updates this controller
                // synchronously. Keep this fallback for disabled/test callers.
                if (ownedItems.SetOwned(item, true))
                    OwnedItemsChanged?.Invoke();
            }
            else
            {
                ownedItems.SetOwned(item, true);
                OwnedItemsChanged?.Invoke();
            }

            PlayerInventoryItem previousSelectedItem =
                inventory.SelectedItem;
            bool loadoutChanged = false;
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                if (inventory.GetItemAtSlot(i) != PlayerInventoryItem.Empty)
                    continue;

                loadoutChanged = inventory.SetItemAtSlot(i, item);
                break;
            }

            if (!loadoutChanged)
                return true;

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

        /// <summary>
        /// Takes an owned item out of play without selling it. The slot it came
        /// from is remembered so <see cref="RestoreSuspendedItem"/> can put it
        /// back, and the gap is never written to the saved loadout.
        /// </summary>
        public bool SuspendItem(PlayerInventoryItem item)
        {
            EnsureInventory();
            if (item == PlayerInventoryItem.Empty
                || suspendedItemSlots.ContainsKey(item))
            {
                return false;
            }

            int slotIndex = inventory.IndexOf(item);
            PlayerInventoryItem previousSelectedItem = inventory.SelectedItem;
            if (slotIndex >= 0)
                inventory.TemporarilyRemoveItemAtSlot(slotIndex);
            suspendedItemSlots.Add(item, slotIndex);

            ApplySelectedItem();
            OwnedItemsChanged?.Invoke();
            LoadoutChanged?.Invoke();
            if (previousSelectedItem != inventory.SelectedItem)
            {
                SelectionChanged?.Invoke(
                    inventory.SelectedSlotIndex,
                    inventory.SelectedItem);
            }
            return true;
        }

        public bool RestoreSuspendedItem(PlayerInventoryItem item)
        {
            EnsureInventory();
            if (!suspendedItemSlots.TryGetValue(item, out int slotIndex))
                return false;

            suspendedItemSlots.Remove(item);
            PlayerInventoryItem previousSelectedItem = inventory.SelectedItem;
            if (slotIndex >= 0
                && inventory.GetItemAtSlot(slotIndex)
                    == PlayerInventoryItem.Empty)
            {
                inventory.RestoreTemporaryItemAtSlot(slotIndex, item);
            }

            ApplySelectedItem();
            OwnedItemsChanged?.Invoke();
            LoadoutChanged?.Invoke();
            if (previousSelectedItem != inventory.SelectedItem)
            {
                SelectionChanged?.Invoke(
                    inventory.SelectedSlotIndex,
                    inventory.SelectedItem);
            }
            return true;
        }

        public bool IsItemSuspended(PlayerInventoryItem item)
        {
            return item != PlayerInventoryItem.Empty
                && suspendedItemSlots.ContainsKey(item);
        }

        private void EnsureInventory()
        {
            if (inventory != null) return;
            ResolveReferences();
            if (UsesPersistentPlayerData)
            {
                SynchronizeOwnedItems();
            }
            else
            {
                ownedItems.ResetTo(
                    sessionSettings.InitialOwnedItems,
                    sessionSettings.InitialUpgrades);
                ApplySessionQuickSlots();
            }
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
        }

        private void HandleSavedProgressCleared()
        {
            if (!UsesPersistentPlayerData)
                return;

            PlayerInventoryItem previousSelectedItem = inventory != null
                ? inventory.SelectedItem
                : PlayerInventoryItem.Empty;
            CacheDefaultConfiguredSlots();
            configuredSlots = (PlayerInventoryItem[])defaultConfiguredSlots.Clone();
            suspendedItemSlots.Clear();
            SynchronizeOwnedItems();
            inventory = new PlayerInventory(
                initialSelectedSlot,
                ownedItems.Owns,
                configuredSlots);
            selectedSlotIndex = inventory.SelectedSlotIndex;
            CaptureConfiguredSlots();
            ammunitionInventory = null;
            EnsureAmmunitionInventory();
            ApplySelectedItem();
            OwnedItemsChanged?.Invoke();
            LoadoutChanged?.Invoke();
            if (previousSelectedItem != inventory.SelectedItem)
            {
                SelectionChanged?.Invoke(
                    inventory.SelectedSlotIndex,
                    inventory.SelectedItem);
            }
        }

        private void SynchronizeOwnedItems()
        {
            if (ownedItems == null) ownedItems = new PlayerOwnedItems();
            if (!UsesPersistentPlayerData)
                return;
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
        }

        private void LoadConfiguredSlots()
        {
            EnsureConfiguredSlotsArray();
            if (!Application.isPlaying || !UsesPersistentPlayerData)
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
                configuredSlots[i] = ResolveConfiguredSlotItem(i);
        }

        private void SaveConfiguredSlots()
        {
            if (!Application.isPlaying || !UsesPersistentPlayerData)
                return;

            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                PlayerEconomy.SetQuickSlotItem(
                    i,
                    ResolveConfiguredSlotItem(i),
                    false);
            }
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The saved loadout keeps a suspended item in its slot, so recovering a
        /// thrown tool restores the layout the player configured.
        /// </summary>
        private PlayerInventoryItem ResolveConfiguredSlotItem(int slotIndex)
        {
            foreach (KeyValuePair<PlayerInventoryItem, int> suspended
                in suspendedItemSlots)
            {
                if (suspended.Value == slotIndex) return suspended.Key;
            }
            return inventory.GetItemAtSlot(slotIndex);
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

        private void CacheDefaultConfiguredSlots()
        {
            if (defaultConfiguredSlots != null
                && defaultConfiguredSlots.Length == PlayerInventory.SlotCount)
            {
                return;
            }

            EnsureConfiguredSlotsArray();
            defaultConfiguredSlots =
                (PlayerInventoryItem[])configuredSlots.Clone();
        }

        private void ApplySessionQuickSlots()
        {
            EnsureConfiguredSlotsArray();
            for (int i = 0; i < configuredSlots.Length; i++)
                configuredSlots[i] = PlayerInventoryItem.Empty;

            IReadOnlyList<PlayerInventoryItem> initialSlots =
                sessionSettings.InitialQuickSlots;
            int count = Mathf.Min(initialSlots.Count, configuredSlots.Length);
            for (int i = 0; i < count; i++)
            {
                PlayerInventoryItem item = initialSlots[i];
                configuredSlots[i] = item == PlayerInventoryItem.Empty
                    || ownedItems.Owns(item)
                        ? item
                        : PlayerInventoryItem.Empty;
            }
        }

        private void ApplySelectedItem()
        {
            ResolveReferences();
            PlayerToolDefinition definition = SelectedDefinition;
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
            ApplyEquippedToolModelVisibility();
        }

        /// <summary>
        /// Temporarily hides the held tool, for actions that use empty hands. The
        /// model stays instantiated so its mount, muzzle, and pose are preserved.
        /// </summary>
        public void SetEquippedToolModelHidden(bool hidden)
        {
            if (equippedToolModelHidden == hidden) return;
            equippedToolModelHidden = hidden;
            ApplyEquippedToolModelVisibility();
        }

        public bool IsEquippedToolModelHidden => equippedToolModelHidden;

        private void ApplyEquippedToolModelVisibility()
        {
            if (equippedToolModel == null) return;
            Renderer[] renderers =
                equippedToolModel.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = !equippedToolModelHidden;
            }
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

            if (twoHandedModelMount == null)
            {
                GameObject mountObject = new GameObject("Two Handed Model Mount");
                twoHandedModelMount = mountObject.transform;
            }
            if (twoHandedModelMount.parent != mountParent)
                twoHandedModelMount.SetParent(mountParent, false);
            twoHandedModelMount.localPosition = Vector3.zero;
            twoHandedModelMount.localRotation = Quaternion.identity;
            twoHandedModelMount.localScale = Vector3.one;
            return twoHandedModelMount;
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
            if (sessionSettings == null)
                sessionSettings = GetComponent<PlayerInventorySessionSettings>();
        }

        private static int ReadRequestedSlot()
        {
            if (GameInput.Pressed(GameInputActionId.Hotbar1)) return 0;
            if (GameInput.Pressed(GameInputActionId.Hotbar2)) return 1;
            if (GameInput.Pressed(GameInputActionId.Hotbar3)) return 2;
            if (GameInput.Pressed(GameInputActionId.Hotbar4)) return 3;
            if (GameInput.Pressed(GameInputActionId.Hotbar5)) return 4;
            return -1;
        }
    }
}
