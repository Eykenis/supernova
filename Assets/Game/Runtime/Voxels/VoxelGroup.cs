using System;
using System.Collections.Generic;

namespace Supernova.Voxels
{
    /// <summary>
    /// Coarse family a voxel type belongs to. The mesher polygonises one surface
    /// per group rather than per type, so voxels in the same group join into a
    /// continuous surface instead of meeting at a seam.
    /// </summary>
    public enum VoxelGroup
    {
        /// <summary>Built geometry: plain fill and every structure masonry palette.</summary>
        Structure,

        /// <summary>Natural terrain rock, including the world boundary.</summary>
        Stone,

        /// <summary>Mineable resource veins.</summary>
        Ore,
    }

    /// <summary>
    /// Immutable voxel type to group lookup. Mesh building runs on worker
    /// threads, so the catalog is snapshotted into a plain array rather than
    /// queried live.
    /// </summary>
    public readonly struct VoxelGroupMap
    {
        /// <summary>
        /// Group keys for unmapped types start here, well above the enum values,
        /// so a type missing from the catalog keeps its own surface instead of
        /// silently merging into a group it was never assigned to.
        /// </summary>
        private const int UnmappedKeyOffset = 1 << 16;

        private readonly int[] groupKeyByType;

        private VoxelGroupMap(int[] keys)
        {
            groupKeyByType = keys;
        }

        /// <summary>
        /// True when this map carries assignments. A default map groups nothing,
        /// which reproduces the original one-surface-per-type behaviour.
        /// </summary>
        public bool IsConfigured => groupKeyByType != null;

        public static VoxelGroupMap FromDefinitions(
            IReadOnlyList<VoxelTypeDefinition> definitions)
        {
            if (definitions == null || definitions.Count == 0)
            {
                return default;
            }

            int highestType = 0;
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] != null)
                {
                    highestType = Math.Max(
                        highestType,
                        definitions[i].TypeId.Value);
                }
            }
            if (highestType == 0)
            {
                return default;
            }

            var keys = new int[highestType + 1];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = UnmappedKeyOffset + i;
            }
            for (int i = 0; i < definitions.Count; i++)
            {
                VoxelTypeDefinition definition = definitions[i];
                if (definition != null)
                {
                    keys[definition.TypeId.Value] = (int)definition.Group;
                }
            }
            return new VoxelGroupMap(keys);
        }

        /// <summary>Builds a map directly from type/group pairs, for tests and tools.</summary>
        public static VoxelGroupMap FromPairs(
            IReadOnlyList<KeyValuePair<VoxelTypeId, VoxelGroup>> pairs)
        {
            if (pairs == null || pairs.Count == 0)
            {
                return default;
            }
            int highestType = 0;
            for (int i = 0; i < pairs.Count; i++)
            {
                highestType = Math.Max(highestType, pairs[i].Key.Value);
            }
            var keys = new int[highestType + 1];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = UnmappedKeyOffset + i;
            }
            for (int i = 0; i < pairs.Count; i++)
            {
                keys[pairs[i].Key.Value] = (int)pairs[i].Value;
            }
            return new VoxelGroupMap(keys);
        }

        /// <summary>
        /// Surface identity used while polygonising. Two solid samples sharing a
        /// key belong to one surface; differing keys produce a boundary.
        /// </summary>
        public int GetGroupKey(VoxelTypeId type)
        {
            if (groupKeyByType == null || type.Value >= groupKeyByType.Length)
            {
                return UnmappedKeyOffset + type.Value;
            }
            return groupKeyByType[type.Value];
        }

        public bool TryGetGroup(VoxelTypeId type, out VoxelGroup group)
        {
            int key = GetGroupKey(type);
            if (key >= UnmappedKeyOffset)
            {
                group = default;
                return false;
            }
            group = (VoxelGroup)key;
            return true;
        }
    }
}
