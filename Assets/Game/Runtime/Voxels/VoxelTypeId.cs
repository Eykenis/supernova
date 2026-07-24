using System;

namespace Supernova.Voxels
{
    /// <summary>
    /// Stable identifier for a voxel material/type. Zero is reserved for air;
    /// solid voxel types start at one and can be defined by gameplay code or data.
    /// </summary>
    [Serializable]
    public struct VoxelTypeId : IEquatable<VoxelTypeId>, IComparable<VoxelTypeId>
    {
        public static readonly VoxelTypeId Air = new VoxelTypeId(0);
        public static readonly VoxelTypeId Default = new VoxelTypeId(1);

        public VoxelTypeId(ushort value)
        {
            Value = value;
        }

        public ushort Value;
        public bool IsAir => Value == 0;

        public int CompareTo(VoxelTypeId other) => Value.CompareTo(other.Value);
        public bool Equals(VoxelTypeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is VoxelTypeId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsAir ? "Air" : $"VoxelType({Value})";

        public static bool operator ==(VoxelTypeId left, VoxelTypeId right) => left.Equals(right);
        public static bool operator !=(VoxelTypeId left, VoxelTypeId right) => !left.Equals(right);
    }

    /// <summary>A density sample plus its discrete voxel type.</summary>
    public readonly struct VoxelSample
    {
        public VoxelSample(float density, VoxelTypeId type)
        {
            Density = density;
            Type = density >= 0f
                ? (type.IsAir ? VoxelTypeId.Default : type)
                : VoxelTypeId.Air;
        }

        public float Density { get; }
        public VoxelTypeId Type { get; }
        public bool IsSolid(float isoLevel = 0f) => Density >= isoLevel && !Type.IsAir;
    }
}
