using System;
using UnityEngine;

namespace Supernova.Voxels
{
    /// <summary>
    /// Independent gameplay and rendering configuration for one voxel type.
    /// </summary>
    [CreateAssetMenu(
        fileName = "VoxelType",
        menuName = "Supernova/Voxels/Voxel Type Definition")]
    public sealed class VoxelTypeDefinition : ScriptableObject
    {
        [SerializeField, Min(1)] private ushort type = 1;
        [SerializeField] private string displayName = "Voxel";
        [SerializeField, Min(1)] private int durability = 1;
        [SerializeField] private Material material;

        public VoxelTypeId TypeId => new VoxelTypeId(Math.Max((ushort)1, type));
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName.Trim();
        public int Durability => Mathf.Max(1, durability);
        public Material Material => material;

        public void Configure(
            ushort type,
            string displayName,
            int durability,
            Material material = null)
        {
            this.type = Math.Max((ushort)1, type);
            this.displayName = displayName ?? string.Empty;
            this.durability = Mathf.Max(1, durability);
            this.material = material;
        }

        private void OnValidate()
        {
            type = Math.Max((ushort)1, type);
            durability = Mathf.Max(1, durability);
            if (displayName == null) displayName = string.Empty;
        }
    }
}
