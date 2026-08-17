using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels
{
    public static class VoxelTypeUtility
    {
        private sealed class RuntimeMaterialEntry
        {
            public Material Source;
            public Material Runtime;
        }

        private static readonly Dictionary<
            VoxelTypeDefinition,
            RuntimeMaterialEntry> runtimeMaterials =
                new Dictionary<VoxelTypeDefinition, RuntimeMaterialEntry>();

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
                Material configured = ResolveConfiguredMaterial(definition);
                materials[i] = configured != null ? configured : fallback;
            }
            return materials;
        }

        internal static void RefreshRuntimeMaterial(
            VoxelTypeDefinition definition)
        {
            if (definition == null
                || !runtimeMaterials.TryGetValue(
                    definition,
                    out RuntimeMaterialEntry entry)
                || entry == null
                || entry.Runtime == null)
            {
                return;
            }

            Material source = definition.Material;
            if (source == null)
            {
                return;
            }
            if (entry.Source != source)
            {
                entry.Runtime.shader = source.shader;
                entry.Runtime.CopyPropertiesFromMaterial(source);
                entry.Source = source;
            }
            definition.ApplyRenderingOverrides(entry.Runtime);
        }

        private static Material ResolveConfiguredMaterial(
            VoxelTypeDefinition definition)
        {
            if (definition == null || definition.Material == null)
            {
                return null;
            }

            Material source = definition.Material;
            if (!Application.isPlaying
                || !definition.HasRenderingOverrides(source))
            {
                return source;
            }

            if (!runtimeMaterials.TryGetValue(
                    definition,
                    out RuntimeMaterialEntry entry)
                || entry == null
                || entry.Runtime == null)
            {
                entry = new RuntimeMaterialEntry
                {
                    Source = source,
                    Runtime = new Material(source)
                    {
                        name = source.name + " (" + definition.name + " Runtime)",
                        hideFlags = HideFlags.DontSave,
                    },
                };
                runtimeMaterials[definition] = entry;
            }
            else if (entry.Source != source)
            {
                entry.Runtime.shader = source.shader;
                entry.Runtime.CopyPropertiesFromMaterial(source);
                entry.Source = source;
            }

            definition.ApplyRenderingOverrides(entry.Runtime);
            return entry.Runtime;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeMaterials()
        {
            foreach (RuntimeMaterialEntry entry in runtimeMaterials.Values)
            {
                if (entry == null || entry.Runtime == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(entry.Runtime);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(entry.Runtime);
                }
            }
            runtimeMaterials.Clear();
        }
    }
}
