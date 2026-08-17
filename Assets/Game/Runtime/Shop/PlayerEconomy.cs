using System;
using System.Collections.Generic;
using Supernova.Gameplay;
using UnityEngine;

namespace Supernova.Shop
{
    public enum ShopPurchaseResult
    {
        Purchased = 0,
        AlreadyOwned = 1,
        InsufficientFunds = 2,
        InvalidProduct = 3,
    }

    /// <summary>
    /// Central persistence boundary for mission credits, inventory items, and upgrades.
    /// </summary>
    public static class PlayerEconomy
    {
        public const string CreditsPreferenceKey = "Supernova.Credits";
        private const string OwnedItemPreferencePrefix =
            "Supernova.OwnedInventoryItem.";
        private const string OwnedUpgradePreferencePrefix =
            "Supernova.OwnedUpgrade.";
        private const string UpgradeValuePreferencePrefix =
            "Supernova.UpgradeValue.";
        private const string UpgradePurchaseCountPreferencePrefix =
            "Supernova.UpgradePurchaseCount.";
        private const string QuickSlotPreferencePrefix =
            "Supernova.QuickSlot.";

        public static event Action<int> CreditsChanged;
        public static event Action<PlayerInventoryItem, bool>
            ItemOwnershipChanged;
        public static event Action<PlayerUpgrade, bool>
            UpgradeOwnershipChanged;
        public static event Action SavedProgressCleared;

        public static int Credits =>
            Mathf.Max(0, PlayerPrefs.GetInt(CreditsPreferenceKey, 0));

        public static bool IsItemOwned(PlayerInventoryItem item)
        {
            if (item == PlayerInventoryItem.Empty)
                return false;
            if (item == PlayerInventoryItem.Pickaxe
                || item == PlayerInventoryItem.Bomb)
            {
                return true;
            }

            return PlayerPrefs.GetInt(GetOwnedItemKey(item), 0) != 0;
        }

        public static string GetItemOwnershipPreferenceKey(
            PlayerInventoryItem item)
        {
            return GetOwnedItemKey(item);
        }

        public static bool SetItemOwned(
            PlayerInventoryItem item,
            bool owned,
            bool save = true)
        {
            if (item == PlayerInventoryItem.Empty)
                return false;
            if (!owned
                && (item == PlayerInventoryItem.Pickaxe
                    || item == PlayerInventoryItem.Bomb))
            {
                return false;
            }
            if (IsItemOwned(item) == owned)
                return false;

            string key = GetOwnedItemKey(item);
            if (owned)
                PlayerPrefs.SetInt(key, 1);
            else
                PlayerPrefs.DeleteKey(key);
            if (save)
                PlayerPrefs.Save();
            ItemOwnershipChanged?.Invoke(item, owned);
            return true;
        }

        public static bool IsProductOwned(ShopProductProfile product)
        {
            if (product == null) return false;
            if (product.IsRepeatable) return false;
            return product.GrantType == ShopProductGrantType.Upgrade
                ? IsUpgradeOwned(product.GrantedUpgrade)
                : IsItemOwned(product.GrantedItem);
        }

        public static bool IsUpgradeOwned(PlayerUpgrade upgrade)
        {
            return upgrade != PlayerUpgrade.None
                && PlayerPrefs.GetInt(GetOwnedUpgradeKey(upgrade), 0) != 0;
        }

        public static bool HasQuickSlotConfiguration(int slotIndex)
        {
            ValidateQuickSlotIndex(slotIndex);
            return PlayerPrefs.HasKey(GetQuickSlotKey(slotIndex));
        }

        public static PlayerInventoryItem GetQuickSlotItem(int slotIndex)
        {
            ValidateQuickSlotIndex(slotIndex);
            int value = PlayerPrefs.GetInt(
                GetQuickSlotKey(slotIndex),
                (int)PlayerInventoryItem.Empty);
            return Enum.IsDefined(typeof(PlayerInventoryItem), value)
                ? (PlayerInventoryItem)value
                : PlayerInventoryItem.Empty;
        }

        public static void SetQuickSlotItem(
            int slotIndex,
            PlayerInventoryItem item,
            bool save = true)
        {
            ValidateQuickSlotIndex(slotIndex);
            PlayerPrefs.SetInt(GetQuickSlotKey(slotIndex), (int)item);
            if (save)
                PlayerPrefs.Save();
        }

        public static string GetQuickSlotPreferenceKey(int slotIndex)
        {
            ValidateQuickSlotIndex(slotIndex);
            return GetQuickSlotKey(slotIndex);
        }

        public static string GetUpgradeOwnershipPreferenceKey(
            PlayerUpgrade upgrade)
        {
            return GetOwnedUpgradeKey(upgrade);
        }

        public static float GetUpgradeValue(PlayerUpgrade upgrade)
        {
            return upgrade == PlayerUpgrade.None
                ? 0f
                : Mathf.Max(
                    0f,
                    PlayerPrefs.GetFloat(GetUpgradeValueKey(upgrade), 0f));
        }

        public static int GetUpgradePurchaseCount(PlayerUpgrade upgrade)
        {
            return upgrade == PlayerUpgrade.None
                ? 0
                : Mathf.Max(
                    0,
                    PlayerPrefs.GetInt(
                        GetUpgradePurchaseCountKey(upgrade),
                        0));
        }

        public static string GetUpgradeValuePreferenceKey(
            PlayerUpgrade upgrade)
        {
            return GetUpgradeValueKey(upgrade);
        }

        public static string GetUpgradePurchaseCountPreferenceKey(
            PlayerUpgrade upgrade)
        {
            return GetUpgradePurchaseCountKey(upgrade);
        }

        public static int GetCurrentPrice(ShopProductProfile product)
        {
            if (product == null) return 0;
            int purchaseCount = product.IsRepeatable
                ? GetUpgradePurchaseCount(product.GrantedUpgrade)
                : 0;
            return product.GetPriceAfterPurchases(purchaseCount);
        }

        public static bool CanAfford(ShopProductProfile product)
        {
            return product != null && Credits >= GetCurrentPrice(product);
        }

        public static void AddCredits(int amount)
        {
            if (amount <= 0)
                return;

            SetCredits(Credits + amount);
        }

        public static ShopPurchaseResult TryPurchase(
            ShopProductProfile product)
        {
            if (product == null || !product.IsConfigured)
                return ShopPurchaseResult.InvalidProduct;
            if (!product.IsRepeatable && IsProductOwned(product))
                return ShopPurchaseResult.AlreadyOwned;
            if (!CanAfford(product))
                return ShopPurchaseResult.InsufficientFunds;

            int currentPrice = GetCurrentPrice(product);
            SetCredits(Credits - currentPrice, false);
            if (product.GrantType == ShopProductGrantType.Upgrade)
            {
                PlayerPrefs.SetInt(
                    GetOwnedUpgradeKey(product.GrantedUpgrade),
                    1);
                PlayerPrefs.SetFloat(
                    GetUpgradeValueKey(product.GrantedUpgrade),
                    GetUpgradeValue(product.GrantedUpgrade)
                        + product.UpgradeValue);
                if (product.IsRepeatable)
                {
                    int purchaseCount =
                        GetUpgradePurchaseCount(product.GrantedUpgrade);
                    PlayerPrefs.SetInt(
                        GetUpgradePurchaseCountKey(product.GrantedUpgrade),
                        purchaseCount < int.MaxValue
                            ? purchaseCount + 1
                            : int.MaxValue);
                }
            }
            else
            {
                PlayerPrefs.SetInt(
                    GetOwnedItemKey(product.GrantedItem),
                    1);
            }
            PlayerPrefs.Save();
            if (product.GrantType == ShopProductGrantType.Upgrade)
                UpgradeOwnershipChanged?.Invoke(
                    product.GrantedUpgrade,
                    true);
            else
                ItemOwnershipChanged?.Invoke(product.GrantedItem, true);
            return ShopPurchaseResult.Purchased;
        }

        public static void ClearSavedProgress()
        {
            int previousCredits = Credits;
            var removedItems = new List<PlayerInventoryItem>();
            var removedUpgrades = new List<PlayerUpgrade>();

            Array itemValues = Enum.GetValues(typeof(PlayerInventoryItem));
            for (int i = 0; i < itemValues.Length; i++)
            {
                PlayerInventoryItem item =
                    (PlayerInventoryItem)itemValues.GetValue(i);
                if (item != PlayerInventoryItem.Empty
                    && IsItemOwned(item)
                    && item != PlayerInventoryItem.Pickaxe
                    && item != PlayerInventoryItem.Bomb)
                {
                    removedItems.Add(item);
                }
                PlayerPrefs.DeleteKey(GetOwnedItemKey(item));
            }

            Array upgradeValues = Enum.GetValues(typeof(PlayerUpgrade));
            for (int i = 0; i < upgradeValues.Length; i++)
            {
                PlayerUpgrade upgrade =
                    (PlayerUpgrade)upgradeValues.GetValue(i);
                if (upgrade != PlayerUpgrade.None
                    && IsUpgradeOwned(upgrade))
                {
                    removedUpgrades.Add(upgrade);
                }
                PlayerPrefs.DeleteKey(GetOwnedUpgradeKey(upgrade));
                PlayerPrefs.DeleteKey(GetUpgradeValueKey(upgrade));
                PlayerPrefs.DeleteKey(GetUpgradePurchaseCountKey(upgrade));
            }

            PlayerPrefs.DeleteKey(CreditsPreferenceKey);
            DeleteQuickSlotPreferences();
            PlayerPrefs.Save();

            if (previousCredits != 0)
                CreditsChanged?.Invoke(0);
            for (int i = 0; i < removedItems.Count; i++)
                ItemOwnershipChanged?.Invoke(removedItems[i], false);
            for (int i = 0; i < removedUpgrades.Count; i++)
                UpgradeOwnershipChanged?.Invoke(removedUpgrades[i], false);
            SavedProgressCleared?.Invoke();

            // Ownership listeners may persist their refreshed loadout. Delete
            // the slot keys again so the next session uses authoring defaults.
            DeleteQuickSlotPreferences();
            PlayerPrefs.Save();
        }

        private static void SetCredits(int value, bool save = true)
        {
            int safeValue = Mathf.Max(0, value);
            PlayerPrefs.SetInt(CreditsPreferenceKey, safeValue);
            if (save)
                PlayerPrefs.Save();
            CreditsChanged?.Invoke(safeValue);
        }

        private static string GetOwnedItemKey(PlayerInventoryItem item)
        {
            return OwnedItemPreferencePrefix + (int)item;
        }

        private static string GetOwnedUpgradeKey(PlayerUpgrade upgrade)
        {
            return OwnedUpgradePreferencePrefix + (int)upgrade;
        }

        private static string GetUpgradeValueKey(PlayerUpgrade upgrade)
        {
            return UpgradeValuePreferencePrefix + (int)upgrade;
        }

        private static string GetUpgradePurchaseCountKey(
            PlayerUpgrade upgrade)
        {
            return UpgradePurchaseCountPreferencePrefix + (int)upgrade;
        }

        private static string GetQuickSlotKey(int slotIndex)
        {
            return QuickSlotPreferencePrefix + slotIndex;
        }

        private static void DeleteQuickSlotPreferences()
        {
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
                PlayerPrefs.DeleteKey(GetQuickSlotKey(i));
        }

        private static void ValidateQuickSlotIndex(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= PlayerInventory.SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }
    }
}
