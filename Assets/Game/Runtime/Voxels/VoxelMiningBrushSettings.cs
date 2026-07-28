using System;
using UnityEngine;

namespace Supernova.Voxels
{
    /// <summary>
    /// Immutable, tool-owned parameters for one directional mining impact.
    /// Distances are expressed in world units and converted by the terrain.
    /// </summary>
    public readonly struct VoxelMiningBrushSettings
    {
        public static readonly VoxelMiningBrushSettings SingleVoxel =
            new VoxelMiningBrushSettings(1f, 0f, 0f, 1f, 1f, 1, 2f);

        public VoxelMiningBrushSettings(
            float power,
            float radius,
            float depth,
            float falloffExponent,
            float minimumPowerFraction,
            int maxAffectedSamples,
            float propagationDivisor = 2f)
        {
            Power = Mathf.Max(0.01f, power);
            Radius = Mathf.Max(0f, radius);
            Depth = Mathf.Max(0f, depth);
            FalloffExponent = Mathf.Max(0.01f, falloffExponent);
            MinimumPowerFraction = Mathf.Clamp01(minimumPowerFraction);
            MaxAffectedSamples = Math.Max(1, maxAffectedSamples);
            PropagationDivisor = Mathf.Max(1f, propagationDivisor);
        }

        public float Power { get; }
        public float Radius { get; }
        public float Depth { get; }
        public float FalloffExponent { get; }
        public float MinimumPowerFraction { get; }
        public int MaxAffectedSamples { get; }
        public float PropagationDivisor { get; }
        public bool IsSingleVoxel =>
            Radius <= 0f || Depth <= 0f || MaxAffectedSamples <= 1;

        public VoxelMiningBrushSettings WithPower(float power)
        {
            return new VoxelMiningBrushSettings(
                power,
                Radius,
                Depth,
                FalloffExponent,
                MinimumPowerFraction,
                MaxAffectedSamples,
                PropagationDivisor);
        }
    }
}
