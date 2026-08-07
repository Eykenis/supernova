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
            bool destroyed,
            float excessDamage = 0f)
        {
            Coordinate = coordinate;
            Type = type;
            Durability = Mathf.Max(1, durability);
            AccumulatedHits = Mathf.Clamp(accumulatedHits, 0, Durability);
            Destroyed = destroyed;
            ExcessDamage = Mathf.Max(0f, excessDamage);
        }

        public Vector3Int Coordinate { get; }
        public VoxelTypeId Type { get; }
        public int Durability { get; }
        public int AccumulatedHits { get; }
        public int RemainingHits => Mathf.Max(0, Durability - AccumulatedHits);
        public bool Destroyed { get; }
        public float ExcessDamage { get; }
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
            return TryApplyDamage(
                coordinate,
                sample,
                durability,
                1f,
                true,
                out result);
        }

        public bool TryApplyDamage(
            Vector3Int coordinate,
            VoxelSample sample,
            int durability,
            float damage,
            bool inheritNeighbourProgress,
            out VoxelMiningResult result)
        {
            result = default;
            if (!sample.IsSolid() || damage <= 0f) return false;

            int requiredHits = Mathf.Max(1, durability);
            float accumulatedDamage = damage;
            float damageBeforeHit = 0f;
            if (damageByVoxel.TryGetValue(coordinate, out DamageState state)
                && state.Type == sample.Type)
            {
                damageBeforeHit = state.AccumulatedDamage;
                accumulatedDamage = state.AccumulatedDamage + damage;
            }
            else if (inheritNeighbourProgress
                && TryInheritNeighbourProgress(
                    coordinate,
                    sample.Type,
                    out float inheritedDamage))
            {
                // The crosshair jittered onto an adjacent same-type block between
                // clicks. Carry over the accumulated progress so mining doesn't
                // silently reset and stall.
                damageBeforeHit = inheritedDamage;
                accumulatedDamage = inheritedDamage + damage;
            }

            bool destroyed = accumulatedDamage >= requiredHits;
            float remainingDurabilityBeforeHit =
                Mathf.Max(0f, requiredHits - damageBeforeHit);
            float excessDamage = destroyed
                ? Mathf.Max(0f, damage - remainingDurabilityBeforeHit)
                : 0f;
            accumulatedDamage = Mathf.Min(accumulatedDamage, requiredHits);
            if (destroyed)
            {
                damageByVoxel.Remove(coordinate);
            }
            else
            {
                damageByVoxel[coordinate] =
                    new DamageState(sample.Type, accumulatedDamage);
            }

            result = new VoxelMiningResult(
                coordinate,
                sample.Type,
                requiredHits,
                Mathf.CeilToInt(accumulatedDamage),
                destroyed,
                excessDamage);
            return true;
        }

        // Looks for an in-progress same-type block in the 6-neighbourhood of the
        // coordinate. When found, its progress is consumed (removed) so mining
        // stays continuous even if the target coordinate drifts by one voxel.
        private bool TryInheritNeighbourProgress(
            Vector3Int coordinate,
            VoxelTypeId type,
            out float inheritedDamage)
        {
            inheritedDamage = 0f;
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
                        inheritedDamage = state.AccumulatedDamage;
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
            public DamageState(VoxelTypeId type, float accumulatedDamage)
            {
                Type = type;
                AccumulatedDamage = accumulatedDamage;
            }

            public VoxelTypeId Type { get; }
            public float AccumulatedDamage { get; }
        }
    }

    public interface IVoxelTerrain
    {
        Transform TerrainTransform { get; }
        InfiniteVoxelWorld World { get; }
        float VoxelSize { get; }
        float IsoLevel { get; }
        Vector3Int WorldPositionToVoxel(Vector3 worldPosition);
        bool TryMineVoxel(Vector3Int coordinate, out VoxelMiningResult result);
        bool TryMineBrush(
            Vector3Int primaryCoordinate,
            Vector3 worldDirection,
            VoxelMiningBrushSettings settings,
            out VoxelMiningBrushResult result);
        bool TryMineExplosion(
            Vector3 worldCenter,
            VoxelExplosionSettings settings,
            out VoxelExplosionResult result);
        bool TrySetVoxelAndRebuild(
            int worldX,
            int worldY,
            int worldZ,
            float density,
            VoxelTypeId type);
    }
}
