using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels
{
    /// <summary>A persistent, fixed scalar-and-type voxel field used by structure passes.</summary>
    [CreateAssetMenu(
        fileName = "VoxelStructure",
        menuName = "Supernova/Voxels/Voxel Structure")]
    public sealed class VoxelStructureAsset : ScriptableObject
    {
        private const int MaximumAxisSize = 128;

        [SerializeField] private Vector3Int size = new Vector3Int(16, 8, 16);
        [Tooltip("Local sample that is aligned with the generation rule position.")]
        [SerializeField] private Vector3Int anchor = new Vector3Int(8, 1, 8);
        [Tooltip("Player position relative to Anchor, measured in voxel units.")]
        [SerializeField] private Vector3 playerSpawnOffset = new Vector3(0f, 1.25f, 0f);
        [SerializeField, HideInInspector] private float[] densities = Array.Empty<float>();
        [SerializeField, HideInInspector] private ushort[] types = Array.Empty<ushort>();

        public Vector3Int Size => size;
        public Vector3Int Anchor => anchor;
        public Vector3 PlayerSpawnOffset => playerSpawnOffset;
        public int SampleCount => size.x * size.y * size.z;

        public VoxelSample GetSample(int x, int y, int z)
        {
            ValidateCoordinate(x, y, z);
            EnsureStorage();
            int index = ToIndex(x, y, z);
            return new VoxelSample(densities[index], new VoxelTypeId(types[index]));
        }

        /// <summary>
        /// Copies the dense field into thread-safe arrays for background world
        /// generation. ScriptableObject storage is never exposed to worker tasks.
        /// </summary>
        public void CopyData(
            out float[] densitySnapshot,
            out VoxelTypeId[] typeSnapshot)
        {
            EnsureStorage();
            densitySnapshot = (float[])densities.Clone();
            typeSnapshot = new VoxelTypeId[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                typeSnapshot[i] = new VoxelTypeId(types[i]);
            }
        }

        public void SetData(
            Vector3Int newSize,
            Vector3Int newAnchor,
            Vector3 newPlayerSpawnOffset,
            float[] newDensities,
            ushort[] newTypes)
        {
            ValidateSize(newSize);
            int count = newSize.x * newSize.y * newSize.z;
            if (newDensities == null || newDensities.Length != count)
            {
                throw new ArgumentException("Density count must match structure dimensions.", nameof(newDensities));
            }
            if (newTypes == null || newTypes.Length != count)
            {
                throw new ArgumentException("Type count must match structure dimensions.", nameof(newTypes));
            }

            size = newSize;
            anchor = ClampCoordinate(newAnchor, size);
            playerSpawnOffset = newPlayerSpawnOffset;
            densities = (float[])newDensities.Clone();
            types = (ushort[])newTypes.Clone();
            NormalizeSamples();
        }

        public Vector3Int GetWorldOrigin(Vector3Int worldAnchor, Vector3Int ruleOffset)
        {
            return worldAnchor + ruleOffset - anchor;
        }

        public Vector3 GetPlayerSpawnVoxel(Vector3Int worldAnchor, Vector3Int ruleOffset)
        {
            return (Vector3)(worldAnchor + ruleOffset) + playerSpawnOffset;
        }

        public HashSet<Vector3Int> GetAffectedChunks(
            Vector3Int worldAnchor,
            Vector3Int ruleOffset)
        {
            Vector3Int origin = GetWorldOrigin(worldAnchor, ruleOffset);
            Vector3Int last = origin + size - Vector3Int.one;
            Vector3Int minChunk = InfiniteVoxelWorld.WorldToChunk(origin.x, origin.y, origin.z);
            Vector3Int maxChunk = InfiniteVoxelWorld.WorldToChunk(last.x, last.y, last.z);
            var result = new HashSet<Vector3Int>();
            for (int z = minChunk.z; z <= maxChunk.z; z++)
            {
                for (int y = minChunk.y; y <= maxChunk.y; y++)
                {
                    for (int x = minChunk.x; x <= maxChunk.x; x++)
                    {
                        result.Add(new Vector3Int(x, y, z));
                    }
                }
            }
            return result;
        }

        public int Apply(
            InfiniteVoxelWorld world,
            Vector3Int worldAnchor,
            Vector3Int ruleOffset)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            EnsureStorage();
            Vector3Int origin = GetWorldOrigin(worldAnchor, ruleOffset);
            int index = 0;
            int written = 0;
            for (int z = 0; z < size.z; z++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    for (int x = 0; x < size.x; x++)
                    {
                        float density = densities[index];
                        var type = new VoxelTypeId(types[index]);
                        int worldY = origin.y + y;
                        index++;
                        if (!InfiniteVoxelWorld.IsWorldYInBounds(worldY))
                        {
                            continue;
                        }
                        world.SetVoxel(
                            origin.x + x,
                            worldY,
                            origin.z + z,
                            density,
                            density >= 0f ? type : VoxelTypeId.Air);
                        written++;
                    }
                }
            }
            return written;
        }

        private void OnValidate()
        {
            size = new Vector3Int(
                Mathf.Clamp(size.x, 1, MaximumAxisSize),
                Mathf.Clamp(size.y, 1, MaximumAxisSize),
                Mathf.Clamp(size.z, 1, MaximumAxisSize));
            anchor = ClampCoordinate(anchor, size);
            EnsureStorage();
            NormalizeSamples();
        }

        private void EnsureStorage()
        {
            int count = SampleCount;
            if (densities == null || densities.Length != count)
            {
                densities = CreateFilledArray(count, -1f);
            }
            if (types == null || types.Length != count)
            {
                types = new ushort[count];
            }
        }

        private void NormalizeSamples()
        {
            for (int i = 0; i < densities.Length; i++)
            {
                if (densities[i] < 0f)
                {
                    types[i] = VoxelTypeId.Air.Value;
                }
                else if (types[i] == VoxelTypeId.Air.Value)
                {
                    types[i] = VoxelTypeId.Default.Value;
                }
            }
        }

        private int ToIndex(int x, int y, int z)
        {
            return x + size.x * (y + size.y * z);
        }

        private void ValidateCoordinate(int x, int y, int z)
        {
            if ((uint)x >= size.x || (uint)y >= size.y || (uint)z >= size.z)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Structure coordinate ({x}, {y}, {z}) is outside {size}.");
            }
        }

        private static void ValidateSize(Vector3Int value)
        {
            if (value.x < 1 || value.y < 1 || value.z < 1
                || value.x > MaximumAxisSize
                || value.y > MaximumAxisSize
                || value.z > MaximumAxisSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"Each structure axis must be between 1 and {MaximumAxisSize}.");
            }
        }

        private static Vector3Int ClampCoordinate(Vector3Int value, Vector3Int dimensions)
        {
            return new Vector3Int(
                Mathf.Clamp(value.x, 0, dimensions.x - 1),
                Mathf.Clamp(value.y, 0, dimensions.y - 1),
                Mathf.Clamp(value.z, 0, dimensions.z - 1));
        }

        private static float[] CreateFilledArray(int count, float value)
        {
            var result = new float[count];
            for (int i = 0; i < count; i++) result[i] = value;
            return result;
        }
    }

    [Serializable]
    public sealed class SpawnPointStructureRule
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private VoxelStructureAsset structure;
        [SerializeField] private Vector3Int offset;

        public bool Enabled => enabled;
        public VoxelStructureAsset Structure => structure;
        public Vector3Int Offset => offset;
        public bool IsConfigured => enabled && structure != null;

        public void Configure(VoxelStructureAsset value, Vector3Int placementOffset)
        {
            structure = value;
            offset = placementOffset;
            enabled = value != null;
        }

        public void CollectRequiredChunks(Vector3Int spawnVoxel, ISet<Vector3Int> chunks)
        {
            if (chunks == null) throw new ArgumentNullException(nameof(chunks));
            if (!IsConfigured) return;
            foreach (Vector3Int coordinate in structure.GetAffectedChunks(spawnVoxel, offset))
            {
                chunks.Add(coordinate);
            }
        }

        public int Apply(InfiniteVoxelWorld world, Vector3Int spawnVoxel)
        {
            return IsConfigured ? structure.Apply(world, spawnVoxel, offset) : 0;
        }

        public Vector3 GetPlayerSpawnVoxel(Vector3Int spawnVoxel)
        {
            return IsConfigured
                ? structure.GetPlayerSpawnVoxel(spawnVoxel, offset)
                : (Vector3)spawnVoxel;
        }
    }
}
