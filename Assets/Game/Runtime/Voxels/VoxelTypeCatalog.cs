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

#if UNITY_EDITOR
            AutoRegisterDefinitions();
#endif

            var ids = new HashSet<VoxelTypeId>();
            for (int i = definitions.Count - 1; i >= 0; i--)
            {
                VoxelTypeDefinition definition = definitions[i];
                if (definition == null || !ids.Add(definition.TypeId))
                {
                    Debug.LogError(
                        $"Voxel type catalog '{name}' contains a null or duplicate entry at index {i}.",
                        this);
                }
            }
        }

#if UNITY_EDITOR
        private void AutoRegisterDefinitions()
        {
            string selfPath = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(selfPath))
            {
                return;
            }

            string[] guids = UnityEditor.AssetDatabase.FindAssets(
                "t:VoxelTypeDefinition");
            var registeredGuids = new HashSet<string>();
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] != null)
                {
                    string path =
                        UnityEditor.AssetDatabase.GetAssetPath(definitions[i]);
                    if (!string.IsNullOrEmpty(path))
                    {
                        registeredGuids.Add(
                            UnityEditor.AssetDatabase.AssetPathToGUID(path));
                    }
                }
            }

            foreach (string guid in guids)
            {
                if (registeredGuids.Contains(guid))
                {
                    continue;
                }

                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var def = UnityEditor.AssetDatabase
                    .LoadAssetAtPath<VoxelTypeDefinition>(path);
                if (def == null)
                {
                    continue;
                }

                definitions.Add(def);
                Debug.Log(
                    $"Voxel type catalog '{name}' auto-registered "
                    + $"'{def.name}' ({path}).",
                    this);
            }
        }
#endif
    }
}
