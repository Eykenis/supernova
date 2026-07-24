using System;

namespace Supernova.Voxels
{
    /// <summary>A dense typed scalar field containing exactly 32 x 32 x 32 voxel samples.</summary>
    [Serializable]
    public sealed class VoxelVolume
    {
        public const int Size = 32;
        public const int VoxelCount = Size * Size * Size;

        private readonly float[] densities;
        private readonly VoxelTypeId[] types;

        public VoxelVolume(float initialDensity = 0f, VoxelTypeId? initialType = null)
        {
            densities = new float[VoxelCount];
            types = new VoxelTypeId[VoxelCount];
            Fill(initialDensity, initialType ?? VoxelTypeId.Default);
        }

        public int Count => densities.Length;

        public float this[int x, int y, int z]
        {
            get
            {
                ValidateCoordinates(x, y, z);
                return densities[ToIndex(x, y, z)];
            }
            set => SetDensity(x, y, z, value);
        }

        public bool IsInBounds(int x, int y, int z)
        {
            return (uint)x < Size && (uint)y < Size && (uint)z < Size;
        }

        public bool TryGet(int x, int y, int z, out float density)
        {
            if (!IsInBounds(x, y, z))
            {
                density = default;
                return false;
            }

            density = densities[ToIndex(x, y, z)];
            return true;
        }

        public bool TryGetSample(int x, int y, int z, out VoxelSample sample)
        {
            if (!IsInBounds(x, y, z))
            {
                sample = default;
                return false;
            }

            int index = ToIndex(x, y, z);
            sample = new VoxelSample(densities[index], types[index]);
            return true;
        }

        public VoxelSample GetSample(int x, int y, int z)
        {
            ValidateCoordinates(x, y, z);
            int index = ToIndex(x, y, z);
            return new VoxelSample(densities[index], types[index]);
        }

        public VoxelTypeId GetType(int x, int y, int z)
        {
            ValidateCoordinates(x, y, z);
            return types[ToIndex(x, y, z)];
        }

        public float GetOrDefault(int x, int y, int z, float outsideDensity = 0f)
        {
            return IsInBounds(x, y, z) ? densities[ToIndex(x, y, z)] : outsideDensity;
        }

        public VoxelSample GetSampleOrDefault(
            int x,
            int y,
            int z,
            float outsideDensity = -1f,
            VoxelTypeId? outsideType = null)
        {
            return IsInBounds(x, y, z)
                ? GetSample(x, y, z)
                : new VoxelSample(outsideDensity, outsideType ?? VoxelTypeId.Air);
        }

        public void SetDensity(int x, int y, int z, float density)
        {
            ValidateCoordinates(x, y, z);
            int index = ToIndex(x, y, z);
            densities[index] = density;
            if (density < 0f)
            {
                types[index] = VoxelTypeId.Air;
            }
            else if (types[index].IsAir)
            {
                types[index] = VoxelTypeId.Default;
            }
        }

        public void SetSample(int x, int y, int z, float density, VoxelTypeId type)
        {
            ValidateCoordinates(x, y, z);
            int index = ToIndex(x, y, z);
            densities[index] = density;
            types[index] = density >= 0f
                ? (type.IsAir ? VoxelTypeId.Default : type)
                : VoxelTypeId.Air;
        }

        public void SetType(int x, int y, int z, VoxelTypeId type)
        {
            ValidateCoordinates(x, y, z);
            int index = ToIndex(x, y, z);
            types[index] = densities[index] >= 0f
                ? (type.IsAir ? VoxelTypeId.Default : type)
                : VoxelTypeId.Air;
        }

        public void Fill(float density)
        {
            Fill(density, VoxelTypeId.Default);
        }

        public void Fill(float density, VoxelTypeId type)
        {
            Array.Fill(densities, density);
            Array.Fill(
                types,
                density >= 0f
                    ? (type.IsAir ? VoxelTypeId.Default : type)
                    : VoxelTypeId.Air);
        }

        public float[] CopyDensities() => (float[])densities.Clone();
        public VoxelTypeId[] CopyTypes() => (VoxelTypeId[])types.Clone();

        private static int ToIndex(int x, int y, int z)
        {
            return x + Size * (y + Size * z);
        }

        private static void ValidateCoordinates(int x, int y, int z)
        {
            if ((uint)x >= Size || (uint)y >= Size || (uint)z >= Size)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Voxel coordinate ({x}, {y}, {z}) is outside the 0..{Size - 1} range.");
            }
        }
    }
}
