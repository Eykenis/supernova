using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Opts one player out of PlayerPrefs-backed economy and loadout data.
    /// The configured state is reconstructed whenever this player instance starts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInventorySessionSettings : MonoBehaviour
    {
        [SerializeField]
        private bool isolatedFromPersistentData = true;
        [SerializeField]
        private PlayerInventoryItem[] initialOwnedItems =
        {
            PlayerInventoryItem.Pickaxe,
        };
        [SerializeField]
        private PlayerUpgrade[] initialUpgrades = { };
        [SerializeField]
        private PlayerInventoryItem[] initialQuickSlots =
        {
            PlayerInventoryItem.Pickaxe,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
            PlayerInventoryItem.Empty,
        };

        public bool IsolatedFromPersistentData =>
            isolatedFromPersistentData;
        public IReadOnlyList<PlayerInventoryItem> InitialOwnedItems =>
            initialOwnedItems ?? System.Array.Empty<PlayerInventoryItem>();
        public IReadOnlyList<PlayerUpgrade> InitialUpgrades =>
            initialUpgrades ?? System.Array.Empty<PlayerUpgrade>();
        public IReadOnlyList<PlayerInventoryItem> InitialQuickSlots =>
            initialQuickSlots ?? System.Array.Empty<PlayerInventoryItem>();

        public void ConfigurePickaxeOnly()
        {
            isolatedFromPersistentData = true;
            initialOwnedItems = new[] { PlayerInventoryItem.Pickaxe };
            initialUpgrades = System.Array.Empty<PlayerUpgrade>();
            initialQuickSlots = new[]
            {
                PlayerInventoryItem.Pickaxe,
                PlayerInventoryItem.Empty,
                PlayerInventoryItem.Empty,
                PlayerInventoryItem.Empty,
                PlayerInventoryItem.Empty,
            };
        }
    }
}
