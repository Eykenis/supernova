using UnityEngine;

namespace Supernova.Voxels
{
    /// <summary>Summary of one batched mining impact.</summary>
    public readonly struct VoxelMiningBrushResult
    {
        public VoxelMiningBrushResult(
            Vector3Int primaryCoordinate,
            VoxelTypeId targetType,
            int candidateCount,
            int damagedCount,
            int destroyedCount,
            VoxelMiningResult primaryResult)
        {
            PrimaryCoordinate = primaryCoordinate;
            TargetType = targetType;
            CandidateCount = Mathf.Max(0, candidateCount);
            DamagedCount = Mathf.Max(0, damagedCount);
            DestroyedCount = Mathf.Max(0, destroyedCount);
            PrimaryResult = primaryResult;
        }

        public Vector3Int PrimaryCoordinate { get; }
        public VoxelTypeId TargetType { get; }
        public int CandidateCount { get; }
        public int DamagedCount { get; }
        public int DestroyedCount { get; }
        public VoxelMiningResult PrimaryResult { get; }
        public bool PrimaryDestroyed => PrimaryResult.Destroyed;
    }
}
