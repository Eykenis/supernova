using System;

namespace Supernova.Voxels
{
    [Serializable]
    public sealed class VoxelChunkData
    {
        private readonly VoxelVolume volume;

        public VoxelChunkData(int chunkX, int chunkZ, float initialDensity = 0f)
            : this(chunkX, 0, chunkZ, initialDensity)
        {
        }

        public VoxelChunkData(
            int chunkX,
            int chunkY,
            int chunkZ,
            float initialDensity = 0f,
            VoxelTypeId? initialType = null)
        {
            ChunkX = chunkX;
            ChunkY = chunkY;
            ChunkZ = chunkZ;
            volume = new VoxelVolume(initialDensity, initialType);
        }

        public int ChunkY { get; }
        public int ChunkX { get; }
        public int ChunkZ { get; }
        public int OriginY => ChunkY * VoxelVolume.Size;
        public int OriginX => ChunkX * VoxelVolume.Size;
        public int OriginZ => ChunkZ * VoxelVolume.Size;
        public VoxelVolume Volume => volume;
        public int VoxelCount => volume.Count;

        public float this[int localX, int localY, int localZ]
        {
            get => volume[localX, localY, localZ];
            set => volume[localX, localY, localZ] = value;
        }

        public bool TryGet(int localX, int localY, int localZ, out float density)
        {
            return volume.TryGet(localX, localY, localZ, out density);
        }

        public VoxelSample GetSample(int localX, int localY, int localZ)
        {
            return volume.GetSample(localX, localY, localZ);
        }

        public VoxelTypeId GetType(int localX, int localY, int localZ)
        {
            return volume.GetType(localX, localY, localZ);
        }

        public void SetSample(
            int localX,
            int localY,
            int localZ,
            float density,
            VoxelTypeId type)
        {
            volume.SetSample(localX, localY, localZ, density, type);
        }

        public void Fill(float density) => volume.Fill(density);
        public void Fill(float density, VoxelTypeId type) => volume.Fill(density, type);
    }
}
