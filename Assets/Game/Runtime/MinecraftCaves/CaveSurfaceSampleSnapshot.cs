using System;
using System.Buffers;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Pooled copy of the one-voxel border needed by surface attachment tests.
    /// Captured on the main thread; queried only as immutable data by mesh workers.
    /// </summary>
    internal sealed class CaveSurfaceSampleSnapshot : IDisposable
    {
        private const int Border = 1;
        private readonly int originX;
        private readonly int originY;
        private readonly int originZ;
        private readonly int sizeX;
        private readonly int sizeY;
        private readonly int sizeZ;
        private readonly int sampleCount;
        private VoxelSample[] samples;
        private bool[] available;

        private CaveSurfaceSampleSnapshot(
            int originX,
            int originY,
            int originZ,
            int sizeX,
            int sizeY,
            int sizeZ,
            VoxelSample[] samples,
            bool[] available)
        {
            this.originX = originX;
            this.originY = originY;
            this.originZ = originZ;
            this.sizeX = sizeX;
            this.sizeY = sizeY;
            this.sizeZ = sizeZ;
            sampleCount = sizeX * sizeY * sizeZ;
            this.samples = samples;
            this.available = available;
        }

        public static CaveSurfaceSampleSnapshot Capture(
            InfiniteVoxelWorld world,
            Vector3Int columnCoordinate,
            int startY,
            int sectionHeight)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }
            if (sectionHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sectionHeight));
            }

            int sizeX = VoxelColumnChunkData.Width + Border * 2 + 1;
            int sizeY = sectionHeight + Border * 2 + 1;
            int sizeZ = VoxelColumnChunkData.Depth + Border * 2 + 1;
            int count = sizeX * sizeY * sizeZ;
            VoxelSample[] samples =
                ArrayPool<VoxelSample>.Shared.Rent(count);
            bool[] available = ArrayPool<bool>.Shared.Rent(count);
            var columns = new VoxelColumnChunkData[9];
            for (int z = -1; z <= 1; z++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    columns[(z + 1) * 3 + x + 1] =
                        world.TryGetChunk(
                            new Vector2Int(
                                columnCoordinate.x + x,
                                columnCoordinate.z + z),
                            out InfiniteVoxelChunk chunk)
                                ? chunk.Data
                                : null;
                }
            }

            int originX =
                columnCoordinate.x * VoxelColumnChunkData.Width - Border;
            int originY = startY - Border;
            int originZ =
                columnCoordinate.z * VoxelColumnChunkData.Depth - Border;
            try
            {
                int index = 0;
                for (int z = 0; z < sizeZ; z++)
                {
                    ResolveHorizontalSample(
                        z - Border,
                        VoxelColumnChunkData.Depth,
                        out int columnOffsetZ,
                        out int localZ);
                    for (int y = 0; y < sizeY; y++)
                    {
                        int worldY = originY + y;
                        bool yInBounds =
                            InfiniteVoxelWorld.IsWorldYInBounds(worldY);
                        for (int x = 0; x < sizeX; x++, index++)
                        {
                            ResolveHorizontalSample(
                                x - Border,
                                VoxelColumnChunkData.Width,
                                out int columnOffsetX,
                                out int localX);
                            VoxelColumnChunkData column = columns[
                                (columnOffsetZ + 1) * 3
                                + columnOffsetX + 1];
                            bool hasSample = yInBounds && column != null;
                            available[index] = hasSample;
                            samples[index] = hasSample
                                ? column.GetSampleUnchecked(
                                    localX,
                                    worldY,
                                    localZ)
                                : default;
                        }
                    }
                }

                return new CaveSurfaceSampleSnapshot(
                    originX,
                    originY,
                    originZ,
                    sizeX,
                    sizeY,
                    sizeZ,
                    samples,
                    available);
            }
            catch
            {
                ArrayPool<VoxelSample>.Shared.Return(samples);
                ArrayPool<bool>.Shared.Return(available);
                throw;
            }
        }

        public bool TryGetSample(
            int worldX,
            int worldY,
            int worldZ,
            out VoxelSample sample)
        {
            int x = worldX - originX;
            int y = worldY - originY;
            int z = worldZ - originZ;
            if ((uint)x >= sizeX
                || (uint)y >= sizeY
                || (uint)z >= sizeZ
                || samples == null)
            {
                sample = default;
                return false;
            }

            int index = x + sizeX * (y + sizeY * z);
            if (!available[index])
            {
                sample = default;
                return false;
            }

            sample = samples[index];
            return true;
        }

        public void Dispose()
        {
            VoxelSample[] ownedSamples = samples;
            bool[] ownedAvailable = available;
            samples = null;
            available = null;
            if (ownedSamples != null)
            {
                ArrayPool<VoxelSample>.Shared.Return(ownedSamples);
            }
            if (ownedAvailable != null)
            {
                Array.Clear(ownedAvailable, 0, sampleCount);
                ArrayPool<bool>.Shared.Return(ownedAvailable);
            }
        }

        private static void ResolveHorizontalSample(
            int relative,
            int dimension,
            out int columnOffset,
            out int local)
        {
            if (relative < 0)
            {
                columnOffset = -1;
                local = dimension - 1;
            }
            else if (relative >= dimension)
            {
                columnOffset = 1;
                local = relative - dimension;
            }
            else
            {
                columnOffset = 0;
                local = relative;
            }
        }
    }
}
