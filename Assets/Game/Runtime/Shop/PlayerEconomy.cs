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
    /// Central persistence boundary for mission credits and purchased inventory items.
    /// </summary>
    public static class PlayerEconomy
    {
        public const string CreditsPreferenceKey = "Supernova.Credits";
        private const string OwnedItemPreferencePrefix =
            "Supernova.OwnedInventoryItem.";

        public static event Action<int> CreditsChanged;
        public static event Action<PlayerInventoryItem, bool>
            ItemOwnershipChanged;

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
            return product != null && IsItemOwned(product.GrantedItem);
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
            PlayerPrefs.SetInt(
                GetOwnedItemKey(product.GrantedItem),
                1);
            PlayerPrefs.Save();
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
    }
}
