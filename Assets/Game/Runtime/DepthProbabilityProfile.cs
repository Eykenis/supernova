using System;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Scales a configured base probability by voxel depth. Y=0 is the deepest
    /// point and Y=worldHeight-1 is the shallowest point.
    /// </summary>
    [Serializable]
    public sealed class DepthProbabilityProfile
    {
        [SerializeField, Min(0f)] private float shallowMultiplier = 0.35f;
        [SerializeField, Min(0f)] private float deepMultiplier = 1f;
        [SerializeField, Min(0.01f)] private float curveExponent = 1.25f;

        public float ShallowMultiplier => Math.Max(0f, shallowMultiplier);
        public float DeepMultiplier =>
            Math.Max(ShallowMultiplier, deepMultiplier);
        public float CurveExponent => Math.Max(0.01f, curveExponent);

        public float EvaluateMultiplier(int voxelY, int worldHeight)
        {
            double normalizedDepth = worldHeight <= 1
                ? 1.0
                : 1.0 - Clamp01((double)voxelY / (worldHeight - 1));
            double curvedDepth = Math.Pow(normalizedDepth, CurveExponent);
            return (float)(
                ShallowMultiplier
                + (DeepMultiplier - ShallowMultiplier) * curvedDepth);
        }

        public float EvaluateProbability(
            float baseProbability,
            int voxelY,
            int worldHeight)
        {
            return Clamp01(
                Math.Max(0f, baseProbability)
                * EvaluateMultiplier(voxelY, worldHeight));
        }

        public void Configure(
            float shallowDepthMultiplier,
            float deepDepthMultiplier,
            float exponent)
        {
            shallowMultiplier = Math.Max(0f, shallowDepthMultiplier);
            deepMultiplier = Math.Max(
                shallowMultiplier,
                deepDepthMultiplier);
            curveExponent = Math.Max(0.01f, exponent);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            return value > 1.0 ? 1.0 : value;
        }
    }
}
