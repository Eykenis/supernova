using System;
using System.Threading;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Minecraft-style noise-cell sampling for the expensive combined density
    /// router. Values are evaluated at a coarse globally aligned lattice and
    /// trilinearly expanded into the complete voxel column.
    /// </summary>
    public static class MinecraftCaveDensityInterpolator
    {
        public const int HorizontalCellSize = 2;
        public const int VerticalCellSize = 4;

        private const int CoarseSizeX =
            VoxelColumnChunkData.Width / HorizontalCellSize + 1;
        private const int CoarseSizeY =
            VoxelColumnChunkData.Height / VerticalCellSize + 1;
        private const int CoarseSizeZ =
            VoxelColumnChunkData.Depth / HorizontalCellSize + 1;

        public const int CoarseSampleCount =
            CoarseSizeX * CoarseSizeY * CoarseSizeZ;

        public static float[] SampleColumn(
            Vector3Int columnCoordinate,
            MinecraftCaveDensityField densityField,
            CancellationToken cancellationToken = default)
        {
            return SampleColumn(
                columnCoordinate,
                densityField,
                VoxelColumnChunkData.Height,
                cancellationToken);
        }

        public static float[] SampleColumn(
            Vector3Int columnCoordinate,
            MinecraftCaveDensityField densityField,
            int effectiveHeight,
            CancellationToken cancellationToken = default)
        {
            if (densityField == null)
            {
                throw new ArgumentNullException(nameof(densityField));
            }

            int sampledHeight = Math.Max(
                VerticalCellSize,
                Math.Min(VoxelColumnChunkData.Height, effectiveHeight));
            int coarseSizeY = (sampledHeight - 1) / VerticalCellSize + 2;

            int originX =
                columnCoordinate.x * VoxelColumnChunkData.Width;
            int originZ =
                columnCoordinate.z * VoxelColumnChunkData.Depth;
            var coarse = new float[
                CoarseSizeX * coarseSizeY * CoarseSizeZ];
            int coarseIndex = 0;
            for (int z = 0; z < CoarseSizeZ; z++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int worldZ = originZ + z * HorizontalCellSize;
                for (int y = 0; y < coarseSizeY; y++)
                {
                    int worldY = y * VerticalCellSize;
                    for (int x = 0; x < CoarseSizeX; x++)
                    {
                        int worldX = originX + x * HorizontalCellSize;
                        coarse[coarseIndex++] =
                            densityField.SampleFeatureDensity(
                                new Vector3(worldX, worldY, worldZ),
                                MinecraftCaveType.Combined);
                    }
                }
            }

            var densities = new float[VoxelColumnChunkData.VoxelCount];
            for (int index = 0; index < densities.Length; index++)
            {
                densities[index] = -1f;
            }
            for (int z = 0; z < VoxelColumnChunkData.Depth; z++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int coarseZ = z / HorizontalCellSize;
                float blendZ =
                    (z % HorizontalCellSize) / (float)HorizontalCellSize;
                int zOffset = coarseZ * CoarseSizeX * coarseSizeY;
                int nextZOffset = zOffset + CoarseSizeX * coarseSizeY;

                for (int y = 0; y < sampledHeight; y++)
                {
                    int coarseY = y / VerticalCellSize;
                    float blendY =
                        (y % VerticalCellSize) / (float)VerticalCellSize;
                    int row = coarseY * CoarseSizeX;
                    int nextRow = row + CoarseSizeX;

                    for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                    {
                        int coarseX = x / HorizontalCellSize;
                        float blendX =
                            (x % HorizontalCellSize)
                            / (float)HorizontalCellSize;

                        int bottom = zOffset + row + coarseX;
                        int bottomNextZ = nextZOffset + row + coarseX;
                        int top = zOffset + nextRow + coarseX;
                        int topNextZ = nextZOffset + nextRow + coarseX;

                        float bottomX = Lerp(
                            coarse[bottom],
                            coarse[bottom + 1],
                            blendX);
                        float bottomXNextZ = Lerp(
                            coarse[bottomNextZ],
                            coarse[bottomNextZ + 1],
                            blendX);
                        float topX = Lerp(
                            coarse[top],
                            coarse[top + 1],
                            blendX);
                        float topXNextZ = Lerp(
                            coarse[topNextZ],
                            coarse[topNextZ + 1],
                            blendX);
                        float bottomPlane = Lerp(
                            bottomX,
                            topX,
                            blendY);
                        float nextZPlane = Lerp(
                            bottomXNextZ,
                            topXNextZ,
                            blendY);
                        densities[VoxelColumnChunkData.ToIndex(x, y, z)] = Lerp(
                            bottomPlane,
                            nextZPlane,
                            blendZ);
                    }
                }
            }

            return densities;
        }

        private static float Lerp(float from, float to, float amount)
        {
            return from + (to - from) * amount;
        }
    }
}
