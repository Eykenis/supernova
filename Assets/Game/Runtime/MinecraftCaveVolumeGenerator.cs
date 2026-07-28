using System;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Read-only adapter from the Minecraft cave density field to the project's
    /// existing voxel containers. Every sample is derived from its absolute position.
    /// </summary>
    public static class MinecraftCaveVolumeGenerator
    {
        public static void FillChunk(
            VoxelChunkData chunk,
            MinecraftCaveDensityField densityField,
            MinecraftCaveType type = MinecraftCaveType.Combined)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            FillWorldVolume(
                chunk.Volume,
                new Vector3Int(chunk.OriginX, chunk.OriginY, chunk.OriginZ),
                densityField,
                type);
        }

        public static void FillColumn(
            VoxelColumnChunkData column,
            MinecraftCaveDensityField densityField,
            MinecraftCaveType type = MinecraftCaveType.Combined)
        {
            if (column == null)
            {
                throw new ArgumentNullException(nameof(column));
            }
            if (densityField == null)
            {
                throw new ArgumentNullException(nameof(densityField));
            }

            for (int z = 0; z < VoxelColumnChunkData.Depth; z++)
            {
                for (int y = 0; y < VoxelColumnChunkData.Height; y++)
                {
                    for (int x = 0; x < VoxelColumnChunkData.Width; x++)
                    {
                        Vector3 worldPosition = new Vector3(
                            column.OriginX + x,
                            y,
                            column.OriginZ + z);
                        column[x, y, z] = densityField.SampleFeatureDensity(
                            worldPosition,
                            type);
                    }
                }
            }
        }

        public static void FillWorldVolume(
            VoxelVolume volume,
            Vector3Int worldOrigin,
            MinecraftCaveDensityField densityField,
            MinecraftCaveType type = MinecraftCaveType.Combined)
        {
            if (volume == null)
            {
                throw new ArgumentNullException(nameof(volume));
            }

            if (densityField == null)
            {
                throw new ArgumentNullException(nameof(densityField));
            }

            for (int z = 0; z < VoxelVolume.Size; z++)
            {
                for (int y = 0; y < VoxelVolume.Size; y++)
                {
                    for (int x = 0; x < VoxelVolume.Size; x++)
                    {
                        Vector3 worldPosition = (Vector3)(
                            worldOrigin + new Vector3Int(x, y, z));
                        volume[x, y, z] = densityField.SampleFeatureDensity(
                            worldPosition,
                            type);
                    }
                }
            }
        }

        public static void FillDisplayVolume(
            VoxelVolume volume,
            MinecraftCaveDensityField densityField,
            MinecraftCaveType type,
            bool cutaway)
        {
            if (volume == null)
            {
                throw new ArgumentNullException(nameof(volume));
            }

            if (densityField == null)
            {
                throw new ArgumentNullException(nameof(densityField));
            }

            float centre = (VoxelVolume.Size - 1) * 0.5f;
            float inverseCentre = 1f / centre;
            for (int z = 0; z < VoxelVolume.Size; z++)
            {
                for (int y = 0; y < VoxelVolume.Size; y++)
                {
                    for (int x = 0; x < VoxelVolume.Size; x++)
                    {
                        Vector3 worldPosition = new Vector3(x - centre, y - centre, z - centre);
                        volume[x, y, z] = densityField.SampleSolidDensity(
                            worldPosition,
                            worldPosition * inverseCentre,
                            type,
                            cutaway);
                    }
                }
            }
        }
    }
}
