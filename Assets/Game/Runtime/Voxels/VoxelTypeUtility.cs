using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels
{
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

        public static Color ResolveMaterialColor(
            VoxelTypeId type,
            IReadOnlyList<VoxelTypeDefinition> definitions,
            Color fallback)
        {
            VoxelTypeDefinition definition = Find(type, definitions);
            Material material = definition != null ? definition.Material : null;
            if (material == null)
            {
                return fallback;
            }

            if (material.HasProperty("_BaseColor"))
            {
                return material.GetColor("_BaseColor");
            }

            if (material.HasProperty("_Color"))
            {
                return material.GetColor("_Color");
            }

            return fallback;
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
