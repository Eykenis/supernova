using System;
using UnityEngine;

namespace Supernova.Voxels
{
    [DisallowMultipleComponent]
    public sealed class VoxelStructureCellAuthoring : MonoBehaviour
    {
        [SerializeField, Min(0.001f)] private float density = 1f;
        [SerializeField, Min(1)] private ushort voxelType = 1;

        public float Density => Mathf.Max(0.001f, density);
        public VoxelTypeId Type => new VoxelTypeId(Math.Max((ushort)1, voxelType));

        public void Configure(float value, VoxelTypeId type)
        {
            density = Mathf.Max(0.001f, value);
            voxelType = type.IsAir ? VoxelTypeId.Default.Value : type.Value;
        }
    }
}
