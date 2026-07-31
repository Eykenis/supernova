using System;
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

        public static event Action<int> CreditsChanged;
        public static event Action<PlayerInventoryItem, bool>
            ItemOwnershipChanged;
        public static event Action<PlayerUpgrade, bool>
            UpgradeOwnershipChanged;

        public static int Credits =>
            Mathf.Max(0, PlayerPrefs.GetInt(CreditsPreferenceKey, 0));

        public static bool IsItemOwned(PlayerInventoryItem item)
        {
            if (item == PlayerInventoryItem.Empty)
                return false;
            if (item == PlayerInventoryItem.Pickaxe
                || item == PlayerInventoryItem.Magnet)
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

        public static bool IsProductOwned(ShopProductProfile product)
        {
            if (product == null) return false;
            return product.GrantType == ShopProductGrantType.Upgrade
                ? IsUpgradeOwned(product.GrantedUpgrade)
                : IsItemOwned(product.GrantedItem);
        }

        public static bool IsUpgradeOwned(PlayerUpgrade upgrade)
        {
            return upgrade != PlayerUpgrade.None
                && PlayerPrefs.GetInt(GetOwnedUpgradeKey(upgrade), 0) != 0;
        }

        public static string GetUpgradeOwnershipPreferenceKey(
            PlayerUpgrade upgrade)
        {
            return GetOwnedUpgradeKey(upgrade);
        }

        public static bool CanAfford(ShopProductProfile product)
        {
            return product != null && Credits >= product.Price;
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
            if (IsProductOwned(product))
                return ShopPurchaseResult.AlreadyOwned;
            if (!CanAfford(product))
                return ShopPurchaseResult.InsufficientFunds;

            SetCredits(Credits - product.Price, false);
            if (product.GrantType == ShopProductGrantType.Upgrade)
                PlayerPrefs.SetInt(
                    GetOwnedUpgradeKey(product.GrantedUpgrade),
                    1);
            else
                PlayerPrefs.SetInt(
                    GetOwnedItemKey(product.GrantedItem),
                    1);
            PlayerPrefs.Save();
            if (product.GrantType == ShopProductGrantType.Upgrade)
                UpgradeOwnershipChanged?.Invoke(
                    product.GrantedUpgrade,
                    true);
            else
                ItemOwnershipChanged?.Invoke(product.GrantedItem, true);
            return ShopPurchaseResult.Purchased;
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
    }
}
