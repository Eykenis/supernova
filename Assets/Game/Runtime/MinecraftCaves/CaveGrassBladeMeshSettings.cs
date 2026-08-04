using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Authoring parameters for one generated grass blade LOD tier. Struct fields
    /// cannot carry initialisers, so <see cref="Lod0"/> and friends are the only
    /// source of sane defaults; a value deserialised from an older asset arrives
    /// fully zeroed and must be repaired before use.
    /// </summary>
    [System.Serializable]
    public struct CaveGrassBladeMeshSettings
    {
        [Tooltip("Blades emitted per instance.")]
        [Min(1)] public int bladeCount;
        [Tooltip("Quads stacked along each blade. One segment cannot curve.")]
        [Min(1)] public int segmentsPerBlade;
        [Tooltip("Blade length before per-instance scaling.")]
        [Min(0.01f)] public float height;
        [Tooltip("Blade half-width at the root.")]
        [Min(0.001f)] public float rootHalfWidth;
        [Tooltip("Fraction of the root width retained at the tip.")]
        [Range(0f, 1f)] public float tipWidthFraction;
        [Tooltip("Forward lean of the tip, in blade lengths.")]
        public float restingBend;
        [Tooltip("Deterministic seed for blade yaw and height jitter.")]
        public int seed;

        /// <summary>Near tier: full segment count for smooth wind curvature.</summary>
        public static CaveGrassBladeMeshSettings Lod0 => new CaveGrassBladeMeshSettings
        {
            bladeCount = 5,
            segmentsPerBlade = 5,
            height = 0.3f,
            rootHalfWidth = 0.018f,
            tipWidthFraction = 0.15f,
            restingBend = 0.18f,
            seed = 20260804,
        };

        /// <summary>
        /// Mid tier. Fewer blades, so they widen to hold the same silhouette area
        /// across the tier boundary (the Ghost of Tsushima compensation trick).
        /// </summary>
        public static CaveGrassBladeMeshSettings Lod1
        {
            get
            {
                CaveGrassBladeMeshSettings settings = Lod0;
                settings.bladeCount = 4;
                settings.segmentsPerBlade = 3;
                settings.rootHalfWidth = 0.024f;
                return settings;
            }
        }

        /// <summary>Far tier: one segment, widest blades, no curvature.</summary>
        public static CaveGrassBladeMeshSettings Lod2
        {
            get
            {
                CaveGrassBladeMeshSettings settings = Lod0;
                settings.bladeCount = 3;
                settings.segmentsPerBlade = 1;
                settings.rootHalfWidth = 0.034f;
                return settings;
            }
        }

        /// <summary>
        /// Clamps a value that may have been deserialised as all-zero. Returns a
        /// usable copy rather than mutating, so callers can repair asset fields.
        /// </summary>
        public CaveGrassBladeMeshSettings Sanitized()
        {
            CaveGrassBladeMeshSettings defaults = Lod0;
            return new CaveGrassBladeMeshSettings
            {
                bladeCount = Mathf.Max(1, bladeCount),
                segmentsPerBlade = Mathf.Max(1, segmentsPerBlade),
                height = height > 0f ? height : defaults.height,
                rootHalfWidth = rootHalfWidth > 0f
                    ? rootHalfWidth
                    : defaults.rootHalfWidth,
                tipWidthFraction = Mathf.Clamp01(tipWidthFraction),
                restingBend = restingBend,
                seed = seed,
            };
        }
    }
}
