using System;
using System.Collections.Generic;
using Supernova.Gameplay;
using UnityEngine;

namespace Supernova.UI
{
    [Serializable]
    public sealed class EquipmentIconEntry
    {
        [SerializeField] private PlayerInventoryItem item;
        [SerializeField] private Sprite icon;

        public PlayerInventoryItem Item => item;
        public Sprite Icon => icon;
    }

    /// <summary>
    /// Runtime lookup for editor-baked equipment thumbnails. Keeping this separate
    /// from tool definitions lets UI presentation change without coupling gameplay
    /// configuration to imported texture assets.
    /// </summary>
    [CreateAssetMenu(
        fileName = "EquipmentIconCatalog",
        menuName = "Supernova/UI/Equipment Icon Catalog")]
    public sealed class EquipmentIconCatalog : ScriptableObject
    {
        [SerializeField] private List<EquipmentIconEntry> entries =
            new List<EquipmentIconEntry>();

        public IReadOnlyList<EquipmentIconEntry> Entries => entries;

        public Sprite GetIcon(PlayerInventoryItem item)
        {
            if (item == PlayerInventoryItem.Empty || entries == null)
                return null;

            for (int i = 0; i < entries.Count; i++)
            {
                EquipmentIconEntry entry = entries[i];
                if (entry != null && entry.Item == item)
                    return entry.Icon;
            }

            return null;
        }
    }
}
