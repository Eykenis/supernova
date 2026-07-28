using System;

namespace Supernova.Voxels
{
    /// <summary>
    /// One horizontally streamed world column. X/Z select the column while Y always
    /// spans the complete finite world height.
    /// </summary>
    [Serializable]
    public sealed class VoxelColumnChunkData
    {
        public const int Width = 32;
        public const int Height = 256;
        public const int Depth = 32;
        public const int VoxelCount = Width * Height * Depth;

        private readonly float[] densities;
        private readonly VoxelTypeId[] types;

        public VoxelColumnChunkData(
            int chunkX,
            int chunkZ,
            float initialDensity = 0f,
            VoxelTypeId? initialType = null)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
            densities = new float[VoxelCount];
            types = new VoxelTypeId[VoxelCount];
            Fill(initialDensity, initialType ?? VoxelTypeId.Default);
        }

        private VoxelColumnChunkData(
            int chunkX,
            int chunkZ,
            float[] ownedDensities,
            VoxelTypeId[] ownedTypes)
        {
            if (ownedDensities == null
                || ownedDensities.Length != VoxelCount)
            {
                throw new ArgumentException(
                    $"Density array must contain {VoxelCount} samples.",
                    nameof(ownedDensities));
            }
            if (ownedTypes == null || ownedTypes.Length != VoxelCount)
            {
                throw new ArgumentException(
                    $"Type array must contain {VoxelCount} samples.",
                    nameof(ownedTypes));
            }

            ChunkX = chunkX;
            ChunkZ = chunkZ;
            densities = ownedDensities;
            types = ownedTypes;
        }

        public int ChunkX { get; }
        public int ChunkZ { get; }
        public int OriginX => ChunkX * Width;
        public int OriginY => 0;
        public int OriginZ => ChunkZ * Depth;
        public int Count => VoxelCount;

        public float this[int localX, int localY, int localZ]
        {
            get
            {
                ValidateCoordinates(localX, localY, localZ);
                return densities[ToIndex(localX, localY, localZ)];
            }
            set => SetDensity(localX, localY, localZ, value);
        }

        public bool IsInBounds(int localX, int localY, int localZ)
        {
            return (uint)localX < Width
                && (uint)localY < Height
                && (uint)localZ < Depth;
        }

        public bool TryGet(
            int localX,
            int localY,
            int localZ,
            out float density)
        {
            if (!IsInBounds(localX, localY, localZ))
            {
                density = default;
                return false;
            }

            density = densities[ToIndex(localX, localY, localZ)];
            return true;
        }

        public VoxelSample GetSample(int localX, int localY, int localZ)
        {
            ValidateCoordinates(localX, localY, localZ);
            int index = ToIndex(localX, localY, localZ);
            return new VoxelSample(densities[index], types[index]);
        }

        public VoxelTypeId GetType(int localX, int localY, int localZ)
        {
            ValidateCoordinates(localX, localY, localZ);
            return types[ToIndex(localX, localY, localZ)];
        }

        public void SetDensity(
            int localX,
            int localY,
            int localZ,
            float density)
        {
            ValidateCoordinates(localX, localY, localZ);
            int index = ToIndex(localX, localY, localZ);
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

        public void SetSample(
            int localX,
            int localY,
            int localZ,
            float density,
            VoxelTypeId type)
        {
            ValidateCoordinates(localX, localY, localZ);
            int index = ToIndex(localX, localY, localZ);
            densities[index] = density;
            types[index] = density >= 0f
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

        public static VoxelColumnChunkData TakeOwnership(
            int chunkX,
            int chunkZ,
            float[] densities,
            VoxelTypeId[] types)
        {
            return new VoxelColumnChunkData(
                chunkX,
                chunkZ,
                densities,
                types);
        }

        public static int ToIndex(int x, int y, int z)
        {
            return x + Width * (y + Height * z);
        }

        private static void ValidateCoordinates(int x, int y, int z)
        {
            if ((uint)x >= Width || (uint)y >= Height || (uint)z >= Depth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Column coordinate ({x}, {y}, {z}) is outside "
                    + $"{Width}x{Height}x{Depth}.");
            }
        }
    }
}
