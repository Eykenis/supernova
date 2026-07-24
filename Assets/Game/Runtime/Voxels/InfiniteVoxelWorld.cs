using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels
{
    public sealed class InfiniteVoxelChunk
    {
        public InfiniteVoxelChunk(Vector3Int coordinate)
        {
            Coordinate = coordinate;
            Data = new VoxelChunkData(coordinate.x, coordinate.y, coordinate.z, 1f);
        }

        public Vector3Int Coordinate { get; }
        public VoxelChunkData Data { get; }
    }

    public sealed class InfiniteVoxelWorld
    {
        private readonly Dictionary<Vector3Int, InfiniteVoxelChunk> chunks =
            new Dictionary<Vector3Int, InfiniteVoxelChunk>();

        public IReadOnlyDictionary<Vector3Int, InfiniteVoxelChunk> Chunks => chunks;
        public int ChunkCount => chunks.Count;

        public bool TryGetChunk(Vector3Int coordinate, out InfiniteVoxelChunk chunk)
        {
            return chunks.TryGetValue(coordinate, out chunk);
        }

        public InfiniteVoxelChunk EnsureChunk(Vector3Int coordinate)
        {
            if (!chunks.TryGetValue(coordinate, out InfiniteVoxelChunk chunk))
            {
                chunk = new InfiniteVoxelChunk(coordinate);
                chunks.Add(coordinate, chunk);
            }

            return chunk;
        }

        public float GetDensityOrDefault(
            int worldX,
            int worldY,
            int worldZ,
            float outsideDensity = -1f)
        {
            return GetSampleOrDefault(worldX, worldY, worldZ, outsideDensity).Density;
        }

        public VoxelSample GetSampleOrDefault(
            int worldX,
            int worldY,
            int worldZ,
            float outsideDensity = -1f,
            VoxelTypeId? outsideType = null)
        {
            Vector3Int chunkCoordinate = WorldToChunk(worldX, worldY, worldZ);
            if (!chunks.TryGetValue(chunkCoordinate, out InfiniteVoxelChunk chunk))
            {
                return new VoxelSample(
                    outsideDensity,
                    outsideType ?? (outsideDensity >= 0f
                        ? VoxelTypeId.Default
                        : VoxelTypeId.Air));
            }

            Vector3Int local = WorldToLocal(worldX, worldY, worldZ, chunkCoordinate);
            return chunk.Data.GetSample(local.x, local.y, local.z);
        }

        public bool TryGetDensity(int worldX, int worldY, int worldZ, out float density)
        {
            if (TryGetSample(worldX, worldY, worldZ, out VoxelSample sample))
            {
                density = sample.Density;
                return true;
            }

            density = default;
            return false;
        }

        public bool TryGetSample(int worldX, int worldY, int worldZ, out VoxelSample sample)
        {
            Vector3Int chunkCoordinate = WorldToChunk(worldX, worldY, worldZ);
            if (!chunks.TryGetValue(chunkCoordinate, out InfiniteVoxelChunk chunk))
            {
                sample = default;
                return false;
            }

            Vector3Int local = WorldToLocal(worldX, worldY, worldZ, chunkCoordinate);
            sample = chunk.Data.GetSample(local.x, local.y, local.z);
            return true;
        }

        public void SetDensity(int worldX, int worldY, int worldZ, float density)
        {
            Vector3Int chunkCoordinate = WorldToChunk(worldX, worldY, worldZ);
            InfiniteVoxelChunk chunk = EnsureChunk(chunkCoordinate);
            Vector3Int local = WorldToLocal(worldX, worldY, worldZ, chunkCoordinate);
            chunk.Data[local.x, local.y, local.z] = density;
        }

        public void SetVoxel(
            int worldX,
            int worldY,
            int worldZ,
            float density,
            VoxelTypeId type)
        {
            Vector3Int chunkCoordinate = WorldToChunk(worldX, worldY, worldZ);
            InfiniteVoxelChunk chunk = EnsureChunk(chunkCoordinate);
            Vector3Int local = WorldToLocal(worldX, worldY, worldZ, chunkCoordinate);
            chunk.Data.SetSample(local.x, local.y, local.z, density, type);
        }

        public static Vector3Int WorldToChunk(int worldX, int worldY, int worldZ)
        {
            return new Vector3Int(
                FloorDiv(worldX, VoxelVolume.Size),
                FloorDiv(worldY, VoxelVolume.Size),
                FloorDiv(worldZ, VoxelVolume.Size));
        }

        public static Vector3Int WorldToLocal(
            int worldX,
            int worldY,
            int worldZ,
            Vector3Int chunkCoordinate)
        {
            return new Vector3Int(
                worldX - chunkCoordinate.x * VoxelVolume.Size,
                worldY - chunkCoordinate.y * VoxelVolume.Size,
                worldZ - chunkCoordinate.z * VoxelVolume.Size);
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }
    }
}