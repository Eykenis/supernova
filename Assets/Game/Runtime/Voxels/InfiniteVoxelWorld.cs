using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels
{
    public sealed class InfiniteVoxelChunk
    {
        public InfiniteVoxelChunk(Vector2Int coordinate)
        {
            Coordinate = coordinate;
            Data = new VoxelColumnChunkData(coordinate.x, coordinate.y, 1f);
        }

        public InfiniteVoxelChunk(Vector3Int coordinate)
            : this(new Vector2Int(coordinate.x, coordinate.z))
        {
        }

        public InfiniteVoxelChunk(
            Vector2Int coordinate,
            VoxelColumnChunkData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (data.ChunkX != coordinate.x || data.ChunkZ != coordinate.y)
            {
                throw new ArgumentException(
                    "Column data origin does not match its world coordinate.",
                    nameof(data));
            }

            Coordinate = coordinate;
            Data = data;
        }

        public Vector2Int Coordinate { get; }
        public VoxelColumnChunkData Data { get; }
    }

    public sealed class InfiniteVoxelWorld
    {
        private readonly Dictionary<Vector2Int, InfiniteVoxelChunk> chunks =
            new Dictionary<Vector2Int, InfiniteVoxelChunk>();

        public IReadOnlyDictionary<Vector2Int, InfiniteVoxelChunk> Chunks => chunks;
        public int ChunkCount => chunks.Count;

        /// <summary>
        /// Reports exact sample mutations. The isolated integrity experiment
        /// subscribes only while a player destruction call is executing, so worlds
        /// without a subscriber retain their existing behaviour.
        /// </summary>
        public event Action<Vector3Int, VoxelSample, VoxelSample> SampleChanged;

        public bool TryGetChunk(Vector2Int coordinate, out InfiniteVoxelChunk chunk)
        {
            return chunks.TryGetValue(coordinate, out chunk);
        }

        public bool TryGetChunk(Vector3Int coordinate, out InfiniteVoxelChunk chunk)
        {
            return TryGetChunk(new Vector2Int(coordinate.x, coordinate.z), out chunk);
        }

        public InfiniteVoxelChunk EnsureChunk(Vector2Int coordinate)
        {
            if (!chunks.TryGetValue(coordinate, out InfiniteVoxelChunk chunk))
            {
                chunk = new InfiniteVoxelChunk(coordinate);
                chunks.Add(coordinate, chunk);
            }

            return chunk;
        }

        public InfiniteVoxelChunk EnsureChunk(Vector3Int coordinate)
        {
            return EnsureChunk(new Vector2Int(coordinate.x, coordinate.z));
        }

        public InfiniteVoxelChunk AddChunkTakingOwnership(
            Vector3Int coordinate,
            float[] densities,
            VoxelTypeId[] types)
        {
            var column = new Vector2Int(coordinate.x, coordinate.z);
            if (chunks.ContainsKey(column))
            {
                throw new InvalidOperationException(
                    $"Column {column} has already been committed.");
            }

            VoxelColumnChunkData data = VoxelColumnChunkData.TakeOwnership(
                column.x,
                column.y,
                densities,
                types);
            var chunk = new InfiniteVoxelChunk(column, data);
            chunks.Add(column, chunk);
            return chunk;
        }

        public bool RemoveChunk(
            Vector2Int coordinate,
            out InfiniteVoxelChunk removedChunk)
        {
            if (!chunks.TryGetValue(coordinate, out removedChunk))
            {
                return false;
            }

            chunks.Remove(coordinate);
            return true;
        }

        public bool RemoveChunk(
            Vector3Int coordinate,
            out InfiniteVoxelChunk removedChunk)
        {
            return RemoveChunk(
                new Vector2Int(coordinate.x, coordinate.z),
                out removedChunk);
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
            if (!IsWorldYInBounds(worldY))
            {
                return CreateOutsideSample(outsideDensity, outsideType);
            }

            Vector2Int chunkCoordinate = WorldToColumn(worldX, worldZ);
            if (!chunks.TryGetValue(chunkCoordinate, out InfiniteVoxelChunk chunk))
            {
                return CreateOutsideSample(outsideDensity, outsideType);
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
            if (!IsWorldYInBounds(worldY))
            {
                sample = default;
                return false;
            }

            Vector2Int chunkCoordinate = WorldToColumn(worldX, worldZ);
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
            ValidateWorldY(worldY);
            Vector2Int chunkCoordinate = WorldToColumn(worldX, worldZ);
            InfiniteVoxelChunk chunk = EnsureChunk(chunkCoordinate);
            Vector3Int local = WorldToLocal(worldX, worldY, worldZ, chunkCoordinate);
            VoxelSample previous = chunk.Data.GetSample(
                local.x,
                local.y,
                local.z);
            chunk.Data[local.x, local.y, local.z] = density;
            ReportSampleChange(
                new Vector3Int(worldX, worldY, worldZ),
                previous,
                chunk.Data.GetSample(local.x, local.y, local.z));
        }

        public void SetVoxel(
            int worldX,
            int worldY,
            int worldZ,
            float density,
            VoxelTypeId type)
        {
            ValidateWorldY(worldY);
            Vector2Int chunkCoordinate = WorldToColumn(worldX, worldZ);
            InfiniteVoxelChunk chunk = EnsureChunk(chunkCoordinate);
            Vector3Int local = WorldToLocal(worldX, worldY, worldZ, chunkCoordinate);
            VoxelSample previous = chunk.Data.GetSample(
                local.x,
                local.y,
                local.z);
            chunk.Data.SetSample(local.x, local.y, local.z, density, type);
            ReportSampleChange(
                new Vector3Int(worldX, worldY, worldZ),
                previous,
                chunk.Data.GetSample(local.x, local.y, local.z));
        }

        private void ReportSampleChange(
            Vector3Int coordinate,
            VoxelSample previous,
            VoxelSample current)
        {
            if (previous.Density == current.Density
                && previous.Type == current.Type)
            {
                return;
            }

            SampleChanged?.Invoke(coordinate, previous, current);
        }

        public static Vector2Int WorldToColumn(int worldX, int worldZ)
        {
            return new Vector2Int(
                FloorDiv(worldX, VoxelColumnChunkData.Width),
                FloorDiv(worldZ, VoxelColumnChunkData.Depth));
        }

        /// <summary>
        /// Compatibility form for callers that still carry voxel-space Y. Chunk Y is
        /// always zero because the complete world height belongs to one X/Z column.
        /// </summary>
        public static Vector3Int WorldToChunk(int worldX, int worldY, int worldZ)
        {
            Vector2Int column = WorldToColumn(worldX, worldZ);
            return new Vector3Int(
                column.x,
                0,
                column.y);
        }

        public static Vector3Int WorldToLocal(
            int worldX,
            int worldY,
            int worldZ,
            Vector2Int columnCoordinate)
        {
            return new Vector3Int(
                worldX - columnCoordinate.x * VoxelColumnChunkData.Width,
                worldY,
                worldZ - columnCoordinate.y * VoxelColumnChunkData.Depth);
        }

        public static Vector3Int WorldToLocal(
            int worldX,
            int worldY,
            int worldZ,
            Vector3Int chunkCoordinate)
        {
            return WorldToLocal(
                worldX,
                worldY,
                worldZ,
                new Vector2Int(chunkCoordinate.x, chunkCoordinate.z));
        }

        public static bool IsWorldYInBounds(int worldY)
        {
            return (uint)worldY < VoxelColumnChunkData.Height;
        }

        private static VoxelSample CreateOutsideSample(
            float outsideDensity,
            VoxelTypeId? outsideType)
        {
            return new VoxelSample(
                outsideDensity,
                outsideType ?? (outsideDensity >= 0f
                    ? VoxelTypeId.Default
                    : VoxelTypeId.Air));
        }

        private static void ValidateWorldY(int worldY)
        {
            if (!IsWorldYInBounds(worldY))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldY),
                    $"World Y {worldY} is outside 0.."
                    + $"{VoxelColumnChunkData.Height - 1}.");
            }
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }
    }
}
