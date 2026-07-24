using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Voxels
{
    public readonly struct VoxelMiningResult
    {
        public VoxelMiningResult(
            Vector3Int coordinate,
            VoxelTypeId type,
            int durability,
            int accumulatedHits,
            bool destroyed)
        {
            Coordinate = coordinate;
            Type = type;
            Durability = Mathf.Max(1, durability);
            AccumulatedHits = Mathf.Clamp(accumulatedHits, 0, Durability);
            Destroyed = destroyed;
        }

        public Vector3Int Coordinate { get; }
        public VoxelTypeId Type { get; }
        public int Durability { get; }
        public int AccumulatedHits { get; }
        public int RemainingHits => Mathf.Max(0, Durability - AccumulatedHits);
        public bool Destroyed { get; }
    }

    /// <summary>Stores partial mining damage per world voxel coordinate.</summary>
    public sealed class VoxelMiningProgress
    {
        private readonly Dictionary<Vector3Int, DamageState> damageByVoxel =
            new Dictionary<Vector3Int, DamageState>();

        public int DamagedVoxelCount => damageByVoxel.Count;

        public bool TryApplyHit(
            Vector3Int coordinate,
            VoxelSample sample,
            int durability,
            out VoxelMiningResult result)
        {
            result = default;
            if (!sample.IsSolid()) return false;

            int requiredHits = Mathf.Max(1, durability);
            int accumulatedHits = 1;
            if (damageByVoxel.TryGetValue(coordinate, out DamageState state)
                && state.Type == sample.Type)
            {
                accumulatedHits = state.AccumulatedHits + 1;
            }
            else if (TryInheritNeighbourProgress(coordinate, sample.Type, out int inheritedHits))
            {
                // The crosshair jittered onto an adjacent same-type block between
                // clicks. Carry over the accumulated progress so mining doesn't
                // silently reset and stall.
                accumulatedHits = inheritedHits + 1;
            }

            bool destroyed = accumulatedHits >= requiredHits;
            accumulatedHits = Mathf.Min(accumulatedHits, requiredHits);
            if (destroyed)
            {
                damageByVoxel.Remove(coordinate);
            }
            else
            {
                damageByVoxel[coordinate] = new DamageState(sample.Type, accumulatedHits);
            }

            result = new VoxelMiningResult(
                coordinate,
                sample.Type,
                requiredHits,
                accumulatedHits,
                destroyed);
            return true;
        }

        // Looks for an in-progress same-type block in the 6-neighbourhood of the
        // coordinate. When found, its progress is consumed (removed) so mining
        // stays continuous even if the target coordinate drifts by one voxel.
        private bool TryInheritNeighbourProgress(
            Vector3Int coordinate,
            VoxelTypeId type,
            out int inheritedHits)
        {
            inheritedHits = 0;
            if (damageByVoxel.Count == 0) return false;

            for (int axis = 0; axis < 3; axis++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3Int neighbour = coordinate;
                    neighbour[axis] += sign;
                    if (damageByVoxel.TryGetValue(neighbour, out DamageState state)
                        && state.Type == type)
                    {
                        inheritedHits = state.AccumulatedHits;
                        damageByVoxel.Remove(neighbour);
                        return true;
                    }
                }
            }

            return false;
        }

        public void Reset(Vector3Int coordinate)
        {
            damageByVoxel.Remove(coordinate);
        }

        public void Clear()
        {
            damageByVoxel.Clear();
        }

        private readonly struct DamageState
        {
            public DamageState(VoxelTypeId type, int accumulatedHits)
            {
                Type = type;
                AccumulatedHits = accumulatedHits;
            }

            public VoxelTypeId Type { get; }
            public int AccumulatedHits { get; }
        }
    }

    public interface IVoxelTerrain
    {
        Transform TerrainTransform { get; }
        InfiniteVoxelWorld World { get; }
        float VoxelSize { get; }
        Vector3Int WorldPositionToVoxel(Vector3 worldPosition);
        bool TryMineVoxel(Vector3Int coordinate, out VoxelMiningResult result);
        bool TrySetVoxelAndRebuild(
            int worldX,
            int worldY,
            int worldZ,
            float density,
            VoxelTypeId type);
    }
}
