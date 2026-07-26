using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels
{
    /// <summary>Persistent gameplay and rendering configuration for all voxel types.</summary>
    [CreateAssetMenu(
        fileName = "VoxelTypeCatalog",
        menuName = "Supernova/Voxels/Voxel Type Catalog")]
    public sealed class VoxelTypeCatalog : ScriptableObject
    {
        [SerializeField] private List<VoxelTypeDefinition> definitions =
            new List<VoxelTypeDefinition>();

        public IReadOnlyList<VoxelTypeDefinition> Definitions => definitions;

        public VoxelTypeDefinition Find(VoxelTypeId type)
        {
            return VoxelTypeUtility.Find(type, definitions);
        }

        public void SetDefinitions(IEnumerable<VoxelTypeDefinition> values)
        {
            definitions = values != null
                ? new List<VoxelTypeDefinition>(values)
                : new List<VoxelTypeDefinition>();
        }

        private void OnValidate()
        {
            if (definitions == null)
            {
                definitions = new List<VoxelTypeDefinition>();
            }

            var ids = new HashSet<VoxelTypeId>();
            for (int i = definitions.Count - 1; i >= 0; i--)
            {
                VoxelTypeDefinition definition = definitions[i];
                if (definition == null || !ids.Add(definition.TypeId))
                {
                    Debug.LogWarning(
                        $"Voxel type catalog '{name}' contains a null or duplicate entry at index {i}.",
                        this);
                }
            }
        }
    }
}
