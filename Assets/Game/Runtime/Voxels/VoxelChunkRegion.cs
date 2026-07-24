using Random = System.Random;
using System;
using UnityEngine;
using System.Collections.Generic;

namespace Supernova.Voxels
{
    [Serializable]
    public sealed class VoxelChunkRegion
    {
        public const int ChunkCountX = 8;
        public const int ChunkCountZ = 8;
        public const int ChunkCount = ChunkCountX * ChunkCountZ;
        public const int WorldSizeX = ChunkCountX * VoxelVolume.Size;
        public const int WorldSizeY = VoxelVolume.Size;
        public const int WorldSizeZ = ChunkCountZ * VoxelVolume.Size;
        public const int TotalVoxelCount = ChunkCount * VoxelVolume.VoxelCount;
        public const long DensityMemoryBytes = (long)TotalVoxelCount * sizeof(float);

        private readonly VoxelChunkData[] chunks;

        public VoxelChunkRegion(float initialDensity = 0f)
        {
            chunks = new VoxelChunkData[ChunkCount];
            for (int chunkZ = 0; chunkZ < ChunkCountZ; chunkZ++)
            {
                for (int chunkX = 0; chunkX < ChunkCountX; chunkX++)
                {
                    chunks[ToChunkIndex(chunkX, chunkZ)] =
                        new VoxelChunkData(chunkX, chunkZ, initialDensity);
                }
            }
        }

        public IReadOnlyList<VoxelChunkData> Chunks => chunks;
        public int Count => chunks.Length;
        public int LastSeed { get; private set; }
        public int EmptyVoxelCount => TotalVoxelCount - SolidVoxelCount;
        public int SolidVoxelCount { get; private set; }

        public VoxelChunkData this[int chunkX, int chunkZ]
        {
            get
            {
                ValidateChunkCoordinates(chunkX, chunkZ);
                return chunks[ToChunkIndex(chunkX, chunkZ)];
            }
        }

        public bool IsChunkInBounds(int chunkX, int chunkZ)
        {
            return (uint)chunkX < ChunkCountX && (uint)chunkZ < ChunkCountZ;
        }

        public bool TryGetChunk(int chunkX, int chunkZ, out VoxelChunkData chunk)
        {
            if (!IsChunkInBounds(chunkX, chunkZ))
            {
                chunk = null;
                return false;
            }

            chunk = chunks[ToChunkIndex(chunkX, chunkZ)];
            return true;
        }

        public bool IsWorldVoxelInBounds(int worldX, int worldY, int worldZ)
        {
            return (uint)worldX < WorldSizeX
                && (uint)worldY < WorldSizeY
                && (uint)worldZ < WorldSizeZ;
        }

        public float GetWorldVoxel(int worldX, int worldY, int worldZ)
        {
            ValidateWorldCoordinates(worldX, worldY, worldZ);
            ResolveWorldCoordinates(
                worldX,
                worldZ,
                out int chunkX,
                out int chunkZ,
                out int localX,
                out int localZ);
            return this[chunkX, chunkZ][localX, worldY, localZ];
        }

        public VoxelSample GetWorldSample(int worldX, int worldY, int worldZ)
        {
            ValidateWorldCoordinates(worldX, worldY, worldZ);
            ResolveWorldCoordinates(
                worldX,
                worldZ,
                out int chunkX,
                out int chunkZ,
                out int localX,
                out int localZ);
            return this[chunkX, chunkZ].GetSample(localX, worldY, localZ);
        }

public void SetWorldVoxel(int worldX, int worldY, int worldZ, float density)
        {
            ValidateWorldCoordinates(worldX, worldY, worldZ);
            ResolveWorldCoordinates(
                worldX,
                worldZ,
                out int chunkX,
                out int chunkZ,
                out int localX,
                out int localZ);

            VoxelChunkData chunk = this[chunkX, chunkZ];
            float previousDensity = chunk[localX, worldY, localZ];
            chunk[localX, worldY, localZ] = density;
            if (previousDensity >= 0f && density < 0f)
            {
                SolidVoxelCount--;
            }
            else if (previousDensity < 0f && density >= 0f)
            {
                SolidVoxelCount++;
            }
        }

        public bool TryGetWorldVoxel(int worldX, int worldY, int worldZ, out float density)
        {
            if (!IsWorldVoxelInBounds(worldX, worldY, worldZ))
            {
                density = default;
                return false;
            }

            density = GetWorldVoxel(worldX, worldY, worldZ);
            return true;
        }

        public float GetWorldVoxelOrDefault(
            int worldX,
            int worldY,
            int worldZ,
            float outsideDensity = 0f)
        {
            return TryGetWorldVoxel(worldX, worldY, worldZ, out float density)
                ? density
                : outsideDensity;
        }

        public VoxelSample GetWorldSampleOrDefault(
            int worldX,
            int worldY,
            int worldZ,
            float outsideDensity = -1f,
            VoxelTypeId? outsideType = null)
        {
            return IsWorldVoxelInBounds(worldX, worldY, worldZ)
                ? GetWorldSample(worldX, worldY, worldZ)
                : new VoxelSample(
                    outsideDensity,
                    outsideType ?? (outsideDensity >= 0f
                        ? VoxelTypeId.Default
                        : VoxelTypeId.Air));
        }

public void Randomize(int seed)
        {
            var random = new Random(seed);
            int solidCount = 0;

            foreach (VoxelChunkData chunk in chunks)
            {
                for (int localZ = 0; localZ < VoxelVolume.Size; localZ++)
                {
                    for (int localY = 0; localY < VoxelVolume.Size; localY++)
                    {
                        for (int localX = 0; localX < VoxelVolume.Size; localX++)
                        {
                            float density = (float)(random.NextDouble() * 2.0 - 1.0);
                            chunk[localX, localY, localZ] = density;
                            if (density >= 0f)
                            {
                                solidCount++;
                            }
                        }
                    }
                }
            }

            LastSeed = seed;
            SolidVoxelCount = solidCount;
        }

        public void Fill(float density)
        {
            foreach (VoxelChunkData chunk in chunks)
            {
                chunk.Fill(density);
            }

            SolidVoxelCount = density >= 0f ? TotalVoxelCount : 0;
        }

        public void CarveWithPerlinSdf(
            int seed,
            float noiseScale = 0.105f,
            float noiseStrength = 0.9f,
            float bodyRadius = 0.82f)
        {
            if (noiseScale <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(noiseScale));
            }

            if (noiseStrength < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(noiseStrength));
            }

            if (bodyRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(bodyRadius));
            }

            Fill(1f);

            var random = new Random(seed);
            float offsetX = (float)random.NextDouble() * 1000f;
            float offsetY = (float)random.NextDouble() * 1000f;
            float offsetZ = (float)random.NextDouble() * 1000f;
            float halfX = (WorldSizeX - 1) * 0.5f;
            float halfY = (WorldSizeY - 1) * 0.5f;
            float halfZ = (WorldSizeZ - 1) * 0.5f;
            int carvedCount = 0;

            foreach (VoxelChunkData chunk in chunks)
            {
                for (int localZ = 0; localZ < VoxelVolume.Size; localZ++)
                {
                    int worldZ = chunk.OriginZ + localZ;
                    float normalizedZ = (worldZ - halfZ) / halfZ;

                    for (int localY = 0; localY < VoxelVolume.Size; localY++)
                    {
                        float normalizedY = (localY - halfY) / halfY;

                        for (int localX = 0; localX < VoxelVolume.Size; localX++)
                        {
                            int worldX = chunk.OriginX + localX;
                            float normalizedX = (worldX - halfX) / halfX;
                            float distance = Mathf.Sqrt(
                                normalizedX * normalizedX
                                + normalizedY * normalizedY
                                + normalizedZ * normalizedZ);
                            float noise = SampleThreeDimensionalNoise(
                                worldX,
                                localY,
                                worldZ,
                                noiseScale,
                                offsetX,
                                offsetY,
                                offsetZ);
                            float sdf = distance
                                - bodyRadius
                                - (noise - 0.5f) * noiseStrength;

                            if (sdf <= 0f)
                            {
                                chunk[localX, localY, localZ] = -1f;
                                carvedCount++;
                            }
                        }
                    }
                }
            }

            LastSeed = seed;
            SolidVoxelCount = TotalVoxelCount - carvedCount;
        }

        private static float SampleThreeDimensionalNoise(
            float x,
            float y,
            float z,
            float noiseScale,
            float offsetX,
            float offsetY,
            float offsetZ)
        {
            x *= noiseScale;
            y *= noiseScale;
            z *= noiseScale;
            float xy = Mathf.PerlinNoise(x + offsetX, y + offsetY);
            float yz = Mathf.PerlinNoise(y + offsetY, z + offsetZ);
            float xz = Mathf.PerlinNoise(x + offsetX, z + offsetZ);
            float yx = Mathf.PerlinNoise(y + offsetY, x + offsetX);
            float zy = Mathf.PerlinNoise(z + offsetZ, y + offsetY);
            float zx = Mathf.PerlinNoise(z + offsetZ, x + offsetX);
            return (xy + yz + xz + yx + zy + zx) / 6f;
        }

        private static int ToChunkIndex(int chunkX, int chunkZ)
        {
            return chunkX + ChunkCountX * chunkZ;
        }

        private static void ResolveWorldCoordinates(
            int worldX,
            int worldZ,
            out int chunkX,
            out int chunkZ,
            out int localX,
            out int localZ)
        {
            chunkX = worldX / VoxelVolume.Size;
            chunkZ = worldZ / VoxelVolume.Size;
            localX = worldX % VoxelVolume.Size;
            localZ = worldZ % VoxelVolume.Size;
        }

        private static void ValidateChunkCoordinates(int chunkX, int chunkZ)
        {
            if ((uint)chunkX >= ChunkCountX || (uint)chunkZ >= ChunkCountZ)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkX),
                    $"Chunk coordinate ({chunkX}, {chunkZ}) is outside " +
                    $"0..{ChunkCountX - 1}, 0..{ChunkCountZ - 1}.");
            }
        }

        private static void ValidateWorldCoordinates(int worldX, int worldY, int worldZ)
        {
            if ((uint)worldX >= WorldSizeX
                || (uint)worldY >= WorldSizeY
                || (uint)worldZ >= WorldSizeZ)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldX),
                    $"World voxel ({worldX}, {worldY}, {worldZ}) is outside " +
                    $"0..{WorldSizeX - 1}, 0..{WorldSizeY - 1}, 0..{WorldSizeZ - 1}.");
            }
        }
    }
}
