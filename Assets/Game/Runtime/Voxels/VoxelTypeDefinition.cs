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
        [SerializeField, HideInInspector] private ushort type = 1;
        [SerializeField] private string displayName = "Voxel";
        [Tooltip("Types in the same group mesh into one continuous surface.")]
        [SerializeField] private VoxelGroup group = VoxelGroup.Stone;
        [SerializeField, Min(1)] private int durability = 1;
        [Tooltip("Stops voxel-integrity traversal and anchors connected terrain.")]
        [SerializeField] private bool structuralSupport;
        [SerializeField] private Material material;

        public VoxelTypeId TypeId => new VoxelTypeId(Math.Max((ushort)1, type));
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName.Trim();
        public VoxelGroup Group => group;
        public int Durability => Mathf.Max(1, durability);
        public bool IsStructuralSupport => structuralSupport;
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

        public void ConfigureGroup(VoxelGroup value)
        {
            group = value;
        }

        public void ConfigureStructuralSupport(bool value)
        {
            structuralSupport = value;
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            AssignUniqueTypeId();
#endif
            type = Math.Max((ushort)1, type);
            durability = Mathf.Max(1, durability);
            if (displayName == null) displayName = string.Empty;
        }

#if UNITY_EDITOR
        private void AssignUniqueTypeId()
        {
            string selfPath = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(selfPath))
            {
                return;
            }

            string selfGuid =
                UnityEditor.AssetDatabase.AssetPathToGUID(selfPath);
            string[] guids = UnityEditor.AssetDatabase.FindAssets(
                "t:VoxelTypeDefinition");
            var usedTypes = new System.Collections.Generic.HashSet<ushort>();
            VoxelTypeDefinition duplicateWith = null;

            foreach (string guid in guids)
            {
                if (guid == selfGuid)
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

                if (def.type == type)
                {
                    duplicateWith = def;
                }

                usedTypes.Add(def.type);
            }

            if (duplicateWith == null)
            {
                return;
            }

            ushort next = 1;
            while (usedTypes.Contains(next) && next < ushort.MaxValue)
            {
                next++;
            }

            Debug.LogWarning(
                $"Voxel type '{name}' had duplicate type {type} (conflicts with "
                + $"'{duplicateWith.name}'). Auto-assigned unique type {next}.",
                this);
            type = next;
        }
#endif
    }
}
