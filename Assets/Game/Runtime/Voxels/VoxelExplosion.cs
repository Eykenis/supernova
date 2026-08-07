using UnityEngine;

namespace Supernova.Voxels
{
    /// <summary>
    /// World-space strength bands for one voxel explosion. Direct blast damage is
    /// combined with the strongest same-type BFS path that reaches each sample.
    /// </summary>
    public readonly struct VoxelExplosionSettings
    {
        public static readonly VoxelExplosionSettings Bomb =
            new VoxelExplosionSettings(2f, 1f, 30f, 10f, 2f);

        public VoxelExplosionSettings(
            float radius,
            float innerRadius,
            float innerPower,
            float outerPower,
            float propagationDivisor)
        {
            Radius = Mathf.Max(0.01f, radius);
            InnerRadius = Mathf.Clamp(innerRadius, 0f, Radius);
            InnerPower = Mathf.Max(0.01f, innerPower);
            OuterPower = Mathf.Max(0.01f, outerPower);
            PropagationDivisor = Mathf.Max(1f, propagationDivisor);
        }

        public float Radius { get; }
        public float InnerRadius { get; }
        public float InnerPower { get; }
        public float OuterPower { get; }
        public float PropagationDivisor { get; }

        public float GetPower(float worldDistance)
        {
            if (worldDistance < 0f || worldDistance > Radius)
                return 0f;
            return worldDistance <= InnerRadius ? InnerPower : OuterPower;
        }
    }

    /// <summary>Summary of the terrain damage caused by one explosion.</summary>
    public readonly struct VoxelExplosionResult
    {
        public VoxelExplosionResult(
            Vector3 worldCenter,
            int candidateCount,
            int damagedCount,
            int destroyedCount)
        {
            WorldCenter = worldCenter;
            CandidateCount = Mathf.Max(0, candidateCount);
            DamagedCount = Mathf.Max(0, damagedCount);
            DestroyedCount = Mathf.Max(0, destroyedCount);
        }

        public Vector3 WorldCenter { get; }
        public int CandidateCount { get; }
        public int DamagedCount { get; }
        public int DestroyedCount { get; }
    }
}
