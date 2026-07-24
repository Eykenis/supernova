using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures
{
    [CreateAssetMenu(
        fileName = "CreatureVoxelShape",
        menuName = "Minecraft Caves/Creature Voxel Shape")]
    public sealed class CreatureVoxelShape : ScriptableObject
    {
        [SerializeField, Min(0.0001f)] private float bakedVoxelSize = 1f;
        [SerializeField] private BoundsInt occupiedBounds;
        [SerializeField] private List<Vector3Int> occupiedVoxels = new List<Vector3Int>();

        public float BakedVoxelSize => bakedVoxelSize;
        public BoundsInt OccupiedBounds => occupiedBounds;
        public IReadOnlyList<Vector3Int> OccupiedVoxels => occupiedVoxels;
        public bool IsEmpty => occupiedVoxels == null || occupiedVoxels.Count == 0;

        public void SetBakedData(float voxelSize, IReadOnlyList<Vector3Int> voxels)
        {
            if (voxelSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(voxelSize));
            }

            bakedVoxelSize = voxelSize;
            occupiedVoxels.Clear();
            if (voxels != null)
            {
                for (int i = 0; i < voxels.Count; i++)
                {
                    occupiedVoxels.Add(voxels[i]);
                }
            }

            occupiedBounds = CalculateBounds(occupiedVoxels);
        }

        private static BoundsInt CalculateBounds(IReadOnlyList<Vector3Int> voxels)
        {
            if (voxels == null || voxels.Count == 0)
            {
                return new BoundsInt(Vector3Int.zero, Vector3Int.zero);
            }

            Vector3Int minimum = voxels[0];
            Vector3Int maximum = voxels[0];
            for (int i = 1; i < voxels.Count; i++)
            {
                minimum = Vector3Int.Min(minimum, voxels[i]);
                maximum = Vector3Int.Max(maximum, voxels[i]);
            }

            return new BoundsInt(minimum, maximum - minimum + Vector3Int.one);
        }
    }
}
