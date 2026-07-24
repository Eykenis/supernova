using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels
{
    /// <summary>Gameplay and rendering properties shared by every voxel of one type.</summary>
    [Serializable]
    public sealed class VoxelTypeDefinition
    {
        [SerializeField, Min(1)] private ushort type = 1;
        [SerializeField, Min(1)] private int durability = 1;
        [SerializeField] private Material material;

        public VoxelTypeDefinition()
        {
        }

        public VoxelTypeDefinition(ushort type, int durability, Material material = null)
        {
            this.type = Math.Max((ushort)1, type);
            this.durability = Mathf.Max(1, durability);
            this.material = material;
        }

        public VoxelTypeId TypeId => new VoxelTypeId(Math.Max((ushort)1, type));
        public int Durability => Mathf.Max(1, durability);
        public Material Material => material;
    }

    public static class VoxelTypeUtility
    {
        public static VoxelTypeDefinition Find(
            VoxelTypeId type,
            IReadOnlyList<VoxelTypeDefinition> definitions)
        {
            if (type.IsAir || definitions == null) return null;
            for (int i = 0; i < definitions.Count; i++)
            {
                VoxelTypeDefinition definition = definitions[i];
                if (definition != null && definition.TypeId == type) return definition;
            }
            return null;
        }

        public static int ResolveDurability(
            VoxelTypeId type,
            IReadOnlyList<VoxelTypeDefinition> definitions)
        {
            VoxelTypeDefinition definition = Find(type, definitions);
            return definition != null ? definition.Durability : 1;
        }

        public static Material[] ResolveMaterials(
            VoxelMeshData meshData,
            Material fallback,
            IReadOnlyList<VoxelTypeDefinition> definitions)
        {
            if (meshData == null) throw new ArgumentNullException(nameof(meshData));

            IReadOnlyList<VoxelTypeId> types = meshData.SubmeshTypes;
            var materials = new Material[Mathf.Max(1, types.Count)];
            for (int i = 0; i < materials.Length; i++)
            {
                VoxelTypeDefinition definition = i < types.Count
                    ? Find(types[i], definitions)
                    : null;
                materials[i] = definition != null && definition.Material != null
                    ? definition.Material
                    : fallback;
            }
            return materials;
        }
    }
}
