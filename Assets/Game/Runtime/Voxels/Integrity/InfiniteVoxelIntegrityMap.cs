using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels.Integrity
{
    /// <summary>
    /// Isolated adapter over the core streamed voxel world. It does not subscribe
    /// to or modify any production scene or mining entry point.
    /// </summary>
    public sealed class InfiniteVoxelIntegrityMap : IVoxelIntegrityMap
    {
        private readonly InfiniteVoxelWorld world;
        private readonly float isoLevel;
        private readonly HashSet<VoxelTypeId> supportTypes;
        private readonly HashSet<Vector2Int> loadedColumns;

        public InfiniteVoxelIntegrityMap(
            InfiniteVoxelWorld world,
            float isoLevel,
            VoxelTypeId supportType)
            : this(world, isoLevel, new[] { supportType })
        {
        }

        public InfiniteVoxelIntegrityMap(
            InfiniteVoxelWorld world,
            float isoLevel,
            IEnumerable<VoxelTypeId> configuredSupportTypes)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.isoLevel = isoLevel;
            supportTypes = configuredSupportTypes != null
                ? new HashSet<VoxelTypeId>(configuredSupportTypes)
                : new HashSet<VoxelTypeId>();
            loadedColumns = new HashSet<Vector2Int>(world.Chunks.Keys);
        }


        public VoxelIntegrityCell GetCell(Vector3Int coordinate)
        {
            if (!InfiniteVoxelWorld.IsWorldYInBounds(coordinate.y)
                || !world.TryGetSample(
                    coordinate.x,
                    coordinate.y,
                    coordinate.z,
                    out VoxelSample sample))
            {
                return VoxelIntegrityCell.Unloaded;
            }
            if (!sample.IsSolid(isoLevel))
                return VoxelIntegrityCell.Air;
            return supportTypes.Contains(sample.Type)
                ? VoxelIntegrityCell.StructuralSupport
                : VoxelIntegrityCell.Solid;
        }

        public int EstimateDistanceToUnloadedBoundary(Vector3Int coordinate)
        {
            Vector2Int column = InfiniteVoxelWorld.WorldToColumn(
                coordinate.x,
                coordinate.z);
            if (!loadedColumns.Contains(column))
                return 0;

            int localX = coordinate.x
                - column.x * VoxelColumnChunkData.Width;
            int localZ = coordinate.z
                - column.y * VoxelColumnChunkData.Depth;
            int negativeX = localX + 1;
            int positiveX = VoxelColumnChunkData.Width - localX;
            int negativeZ = localZ + 1;
            int positiveZ = VoxelColumnChunkData.Depth - localZ;

            for (Vector2Int next = column + Vector2Int.left;
                loadedColumns.Contains(next);
                next += Vector2Int.left)
            {
                negativeX += VoxelColumnChunkData.Width;
            }
            for (Vector2Int next = column + Vector2Int.right;
                loadedColumns.Contains(next);
                next += Vector2Int.right)
            {
                positiveX += VoxelColumnChunkData.Width;
            }
            for (Vector2Int next = column + Vector2Int.down;
                loadedColumns.Contains(next);
                next += Vector2Int.down)
            {
                negativeZ += VoxelColumnChunkData.Depth;
            }
            for (Vector2Int next = column + Vector2Int.up;
                loadedColumns.Contains(next);
                next += Vector2Int.up)
            {
                positiveZ += VoxelColumnChunkData.Depth;
            }

            int negativeY = coordinate.y + 1;
            int positiveY = VoxelColumnChunkData.Height - coordinate.y;
            return Mathf.Min(
                Mathf.Min(negativeX, positiveX),
                Mathf.Min(
                    Mathf.Min(negativeY, positiveY),
                    Mathf.Min(negativeZ, positiveZ)));
        }


    }
}
