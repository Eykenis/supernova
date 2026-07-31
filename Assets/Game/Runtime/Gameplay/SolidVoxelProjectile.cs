using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Non-offensive SolidGun ammunition. Its first impact creates an independent,
    /// mineable horizontal platform with a radial growth animation.
    /// </summary>
    public sealed class SolidVoxelProjectile : BallisticProjectile
    {
        [SerializeField] private Material platformMaterial;
        [SerializeField, Range(1, 16)]
        private int platformDiameter = 5;
        [SerializeField, Min(0.01f)] private float platformUnitSize = 0.42f;
        [SerializeField, Min(0.01f)] private float platformThickness = 0.2f;
        [SerializeField, Min(0.01f)] private float growthDuration = 0.6f;
        [SerializeField, HideInInspector] private int configurationVersion = 5;

        public Material PlatformMaterial => platformMaterial;
        public int PlatformDiameter => Mathf.Clamp(platformDiameter, 1, 16);
        public float PlatformUnitSize => Mathf.Max(0.01f, platformUnitSize);
        public float PlatformThickness => Mathf.Max(0.01f, platformThickness);
        public float GrowthDuration => Mathf.Max(0.01f, growthDuration);
        public int ConfigurationVersion => configurationVersion;
        public SolidVoxelPrototype LastCreatedPrototype { get; private set; }

        protected override void ProcessImpact(
            Collider hit,
            Vector3 point,
            Vector3 normal,
            Vector3 impactVelocity,
            Vector3 direction)
        {
            LastCreatedPrototype = SolidVoxelPrototype.Create(
                point,
                PlatformDiameter,
                PlatformUnitSize,
                PlatformThickness,
                GrowthDuration,
                platformMaterial);
        }
    }
}
