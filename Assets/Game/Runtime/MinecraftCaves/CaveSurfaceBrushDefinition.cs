using System.Collections.Generic;
using Supernova.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.MinecraftCaves
{
    public enum CaveSurfaceOrientation
    {
        Any,
        Upward,
        Downward,
        Wall,
    }

    public enum CaveSurfaceBrushRenderMode
    {
        Prefab,
        InstancedMesh,
    }

    /// <summary>
    /// Data-driven brush evaluated against exposed marching-cubes surfaces.
    /// Prefab mode preserves full prefab behaviour. Instanced-mesh mode is intended
    /// for dense, non-interactive decoration and creates no per-instance objects.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SurfaceBrush",
        menuName = "Supernova/World Generation/Cave Surface Brush")]
    public sealed class CaveSurfaceBrushDefinition : ScriptableObject
    {
        [Header("Rendering")]
        [SerializeField] private CaveSurfaceBrushRenderMode renderMode;

        [Header("Prefab Mode")]
        [SerializeField] private GameObject prefab;

        [Header("Instanced Mesh Mode")]
        [SerializeField] private Mesh instanceMesh;
        [SerializeField] private Material instanceMaterial;
        [SerializeField]
        private ShadowCastingMode instanceShadowCastingMode =
            ShadowCastingMode.Off;
        [SerializeField] private bool instanceReceiveShadows = true;
        [Tooltip("Zero disables distance culling for this brush.")]
        [SerializeField, Min(0f)] private float maximumDrawDistance = 45f;
        [Tooltip("Ordered near-to-far LOD meshes. Empty falls back to Instance Mesh.")]
        [SerializeField] private List<CaveSurfaceLodTier> lodTiers =
            new List<CaveSurfaceLodTier>();
        [Tooltip("Distance over which instances shrink away before the draw "
            + "distance, removing the hard pop. Zero disables the fade.")]
        [SerializeField, Min(0f)] private float fadeBandDistance = 12f;

        [Header("Attachment")]
        [SerializeField] private List<VoxelTypeDefinition> attachableVoxelTypes =
            new List<VoxelTypeDefinition>();

        [Header("Placement")]
        [SerializeField] private CaveSurfaceOrientation orientation;
        [SerializeField, Min(0)] private int seedSalt = 7919;
        [Tooltip("Expected placement count per square terrain-local unit.")]
        [SerializeField, Min(0f)] private float densityPerSquareUnit = 1f;
        [Tooltip("Minimum abs(dot(surface normal, up)) for floors and ceilings.")]
        [SerializeField, Range(0f, 1f)]
        private float minimumVerticalAlignment = 0.6f;
        [Tooltip("Maximum abs(dot(surface normal, up)) accepted by a wall brush.")]
        [SerializeField, Range(0f, 1f)]
        private float maximumWallVerticalAlignment = 0.4f;
        [SerializeField, Min(0f)] private float normalOffset = 0.01f;
        [Tooltip("Blends the stance away from the surface normal toward world up. "
            + "Zero lays grass flat on slopes; one keeps every blade upright.")]
        [SerializeField, Range(0f, 1f)] private float uprightBias = 0.65f;

        [Header("Random Scale")]
        [Tooltip("Scale range along the two axes tangent to the surface.")]
        [SerializeField] private Vector2 tangentScaleRange = Vector2.one;
        [Tooltip("Scale range along the prefab's local Y / surface-normal axis.")]
        [SerializeField] private Vector2 normalScaleRange = Vector2.one;

        [Header("Clumping")]
        [Tooltip("Horizontal clump cell size in voxels. Placements inside one "
            + "cell share height, width and facing.")]
        [SerializeField, Min(0.05f)] private float clumpHorizontalCellSize = 2.5f;
        [Tooltip("Vertical clump cell size in voxels, so stacked ledges differ.")]
        [SerializeField, Min(0.05f)] private float clumpVerticalCellSize = 3f;
        [SerializeField] private Vector2 clumpHeightRange =
            new Vector2(0.72f, 1.35f);
        [SerializeField] private Vector2 clumpWidthRange =
            new Vector2(0.85f, 1.2f);
        [Tooltip("Peak per-clump yaw bias in degrees, so patches share a lean.")]
        [SerializeField, Min(0f)] private float clumpYawBiasDegrees = 35f;

        [Header("Wind")]
        [SerializeField, Min(0f)] private float windStrength = 0.16f;
        [SerializeField, Min(0f)] private float windFrequency = 0.35f;
        [SerializeField, Min(0f)] private float windScrollSpeed = 0.45f;
        [SerializeField] private Vector2 windDirection = new Vector2(1f, 0.35f);
        [Tooltip("Higher values keep the blade base stiffer and bend only the tip.")]
        [SerializeField, Min(1f)] private float windBendExponent = 2f;

        public CaveSurfaceBrushRenderMode RenderMode => renderMode;
        public GameObject Prefab => prefab;
        public Mesh InstanceMesh => instanceMesh;
        public Material InstanceMaterial => instanceMaterial;
        public ShadowCastingMode InstanceShadowCastingMode =>
            instanceShadowCastingMode;
        public bool InstanceReceiveShadows => instanceReceiveShadows;
        public float MaximumDrawDistance => Mathf.Max(0f, maximumDrawDistance);
        public IReadOnlyList<CaveSurfaceLodTier> LodTiers => lodTiers;
        public float FadeBandDistance => Mathf.Max(0f, fadeBandDistance);
        public bool HasRenderableContent => renderMode ==
            CaveSurfaceBrushRenderMode.Prefab
                ? prefab != null
                : ResolveLodMesh(0f) != null && instanceMaterial != null;
        public IReadOnlyList<VoxelTypeDefinition> AttachableVoxelTypes =>
            attachableVoxelTypes;
        public CaveSurfaceOrientation Orientation => orientation;
        public int SeedSalt => seedSalt;
        public float DensityPerSquareUnit => Mathf.Max(0f, densityPerSquareUnit);
        public float MinimumVerticalAlignment =>
            Mathf.Clamp01(minimumVerticalAlignment);
        public float MaximumWallVerticalAlignment =>
            Mathf.Clamp01(maximumWallVerticalAlignment);
        public float NormalOffset => Mathf.Max(0f, normalOffset);
        public float UprightBias => Mathf.Clamp01(uprightBias);
        public Vector2 TangentScaleRange => SortAndClampScale(tangentScaleRange);
        public Vector2 NormalScaleRange => SortAndClampScale(normalScaleRange);
        public float ClumpHorizontalCellSize =>
            Mathf.Max(0.05f, clumpHorizontalCellSize);
        public float ClumpVerticalCellSize =>
            Mathf.Max(0.05f, clumpVerticalCellSize);
        public Vector2 ClumpHeightRange => SortAndClampScale(clumpHeightRange);
        public Vector2 ClumpWidthRange => SortAndClampScale(clumpWidthRange);
        public float ClumpYawBiasDegrees => Mathf.Max(0f, clumpYawBiasDegrees);
        public float WindStrength => Mathf.Max(0f, windStrength);
        public float WindFrequency => Mathf.Max(0f, windFrequency);
        public float WindScrollSpeed => Mathf.Max(0f, windScrollSpeed);
        public Vector2 WindDirection => windDirection.sqrMagnitude > Mathf.Epsilon
            ? windDirection.normalized
            : Vector2.right;
        public float WindBendExponent => Mathf.Max(1f, windBendExponent);

        /// <summary>
        /// Picks the LOD mesh covering <paramref name="distance"/>. Falls back to
        /// <see cref="InstanceMesh"/> when no tiers are authored, which is what
        /// assets serialised before LOD support deserialise to.
        /// </summary>
        public Mesh ResolveLodMesh(float distance)
        {
            if (lodTiers != null)
            {
                for (int i = 0; i < lodTiers.Count; i++)
                {
                    CaveSurfaceLodTier tier = lodTiers[i];
                    if (tier.Mesh == null)
                    {
                        continue;
                    }
                    if (tier.MaximumDistance <= 0f
                        || distance <= tier.MaximumDistance)
                    {
                        return tier.Mesh;
                    }
                }

                // Beyond the last authored bound, keep drawing the coarsest tier
                // rather than vanishing; the distance fade handles disappearance.
                for (int i = lodTiers.Count - 1; i >= 0; i--)
                {
                    if (lodTiers[i].Mesh != null)
                    {
                        return lodTiers[i].Mesh;
                    }
                }
            }
            return instanceMesh;
        }

        public bool CanAttachTo(VoxelTypeId type)
        {
            if (type.IsAir || attachableVoxelTypes == null)
            {
                return false;
            }

            for (int i = 0; i < attachableVoxelTypes.Count; i++)
            {
                VoxelTypeDefinition definition = attachableVoxelTypes[i];
                if (definition != null && definition.TypeId == type)
                {
                    return true;
                }
            }
            return false;
        }

        public bool MatchesOrientation(Vector3 outwardNormal)
        {
            float upDot = outwardNormal.normalized.y;
            switch (orientation)
            {
                case CaveSurfaceOrientation.Upward:
                    return upDot >= MinimumVerticalAlignment;
                case CaveSurfaceOrientation.Downward:
                    return upDot <= -MinimumVerticalAlignment;
                case CaveSurfaceOrientation.Wall:
                    return Mathf.Abs(upDot) <= MaximumWallVerticalAlignment;
                default:
                    return true;
            }
        }

        public void Configure(
            GameObject brushPrefab,
            IEnumerable<VoxelTypeDefinition> attachableTypes,
            CaveSurfaceOrientation surfaceOrientation,
            int brushSeedSalt,
            float density,
            float verticalAlignment,
            float wallVerticalAlignment,
            float offset,
            Vector2 tangentScale,
            Vector2 normalScale)
        {
            renderMode = CaveSurfaceBrushRenderMode.Prefab;
            prefab = brushPrefab;
            instanceMesh = null;
            instanceMaterial = null;
            ConfigurePlacement(
                attachableTypes,
                surfaceOrientation,
                brushSeedSalt,
                density,
                verticalAlignment,
                wallVerticalAlignment,
                offset,
                tangentScale,
                normalScale);
        }

        public void ConfigureInstanced(
            Mesh mesh,
            Material material,
            IEnumerable<VoxelTypeDefinition> attachableTypes,
            CaveSurfaceOrientation surfaceOrientation,
            int brushSeedSalt,
            float density,
            float verticalAlignment,
            float wallVerticalAlignment,
            float offset,
            Vector2 tangentScale,
            Vector2 normalScale,
            ShadowCastingMode shadowCastingMode,
            bool receiveShadows,
            float drawDistance)
        {
            renderMode = CaveSurfaceBrushRenderMode.InstancedMesh;
            prefab = null;
            instanceMesh = mesh;
            instanceMaterial = material;
            instanceShadowCastingMode = shadowCastingMode;
            instanceReceiveShadows = receiveShadows;
            maximumDrawDistance = Mathf.Max(0f, drawDistance);
            if (instanceMaterial != null)
            {
                instanceMaterial.enableInstancing = true;
            }
            ConfigurePlacement(
                attachableTypes,
                surfaceOrientation,
                brushSeedSalt,
                density,
                verticalAlignment,
                wallVerticalAlignment,
                offset,
                tangentScale,
                normalScale);
        }

        /// <summary>
        /// Sets the stylised-vegetation fields. Separate from
        /// <see cref="ConfigureInstanced"/>, whose parameter list is already long,
        /// so existing callers keep compiling and only grass opts in.
        /// </summary>
        public void ConfigureVegetation(
            IEnumerable<CaveSurfaceLodTier> tiers,
            float fadeBand,
            float stanceUprightBias,
            float clumpHorizontalSize,
            float clumpVerticalSize,
            Vector2 clumpHeight,
            Vector2 clumpWidth,
            float clumpYawBias,
            float strengthOfWind,
            float frequencyOfWind,
            float scrollSpeedOfWind,
            Vector2 directionOfWind,
            float bendExponentOfWind)
        {
            lodTiers = tiers != null
                ? new List<CaveSurfaceLodTier>(tiers)
                : new List<CaveSurfaceLodTier>();
            fadeBandDistance = Mathf.Max(0f, fadeBand);
            uprightBias = Mathf.Clamp01(stanceUprightBias);
            clumpHorizontalCellSize = Mathf.Max(0.05f, clumpHorizontalSize);
            clumpVerticalCellSize = Mathf.Max(0.05f, clumpVerticalSize);
            clumpHeightRange = SortAndClampScale(clumpHeight);
            clumpWidthRange = SortAndClampScale(clumpWidth);
            clumpYawBiasDegrees = Mathf.Max(0f, clumpYawBias);
            windStrength = Mathf.Max(0f, strengthOfWind);
            windFrequency = Mathf.Max(0f, frequencyOfWind);
            windScrollSpeed = Mathf.Max(0f, scrollSpeedOfWind);
            windDirection = directionOfWind.sqrMagnitude > Mathf.Epsilon
                ? directionOfWind.normalized
                : Vector2.right;
            windBendExponent = Mathf.Max(1f, bendExponentOfWind);
        }

        private void ConfigurePlacement(
            IEnumerable<VoxelTypeDefinition> attachableTypes,
            CaveSurfaceOrientation surfaceOrientation,
            int brushSeedSalt,
            float density,
            float verticalAlignment,
            float wallVerticalAlignment,
            float offset,
            Vector2 tangentScale,
            Vector2 normalScale)
        {
            attachableVoxelTypes = attachableTypes != null
                ? new List<VoxelTypeDefinition>(attachableTypes)
                : new List<VoxelTypeDefinition>();
            orientation = surfaceOrientation;
            seedSalt = brushSeedSalt;
            densityPerSquareUnit = Mathf.Max(0f, density);
            minimumVerticalAlignment = Mathf.Clamp01(verticalAlignment);
            maximumWallVerticalAlignment = Mathf.Clamp01(wallVerticalAlignment);
            normalOffset = Mathf.Max(0f, offset);
            tangentScaleRange = SortAndClampScale(tangentScale);
            normalScaleRange = SortAndClampScale(normalScale);
        }

        private void OnValidate()
        {
            if (attachableVoxelTypes == null)
            {
                attachableVoxelTypes = new List<VoxelTypeDefinition>();
            }
            if (lodTiers == null)
            {
                lodTiers = new List<CaveSurfaceLodTier>();
            }
            densityPerSquareUnit = Mathf.Max(0f, densityPerSquareUnit);
            minimumVerticalAlignment = Mathf.Clamp01(minimumVerticalAlignment);
            maximumWallVerticalAlignment =
                Mathf.Clamp01(maximumWallVerticalAlignment);
            normalOffset = Mathf.Max(0f, normalOffset);
            maximumDrawDistance = Mathf.Max(0f, maximumDrawDistance);
            fadeBandDistance = Mathf.Max(0f, fadeBandDistance);
            uprightBias = Mathf.Clamp01(uprightBias);
            tangentScaleRange = SortAndClampScale(tangentScaleRange);
            normalScaleRange = SortAndClampScale(normalScaleRange);

            // Assets serialised before clumping and wind existed deserialise with
            // these at zero, which would divide by zero in the blade shader. Repair
            // them to the authored defaults instead of trusting the stored value.
            if (clumpHorizontalCellSize < 0.05f) clumpHorizontalCellSize = 2.5f;
            if (clumpVerticalCellSize < 0.05f) clumpVerticalCellSize = 3f;
            if (clumpHeightRange == Vector2.zero)
            {
                clumpHeightRange = new Vector2(0.72f, 1.35f);
            }
            if (clumpWidthRange == Vector2.zero)
            {
                clumpWidthRange = new Vector2(0.85f, 1.2f);
            }
            clumpHeightRange = SortAndClampScale(clumpHeightRange);
            clumpWidthRange = SortAndClampScale(clumpWidthRange);
            clumpYawBiasDegrees = Mathf.Max(0f, clumpYawBiasDegrees);
            windStrength = Mathf.Max(0f, windStrength);
            windFrequency = Mathf.Max(0f, windFrequency);
            windScrollSpeed = Mathf.Max(0f, windScrollSpeed);
            if (windDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                windDirection = new Vector2(1f, 0.35f);
            }
            windBendExponent = Mathf.Max(1f, windBendExponent);

            if (renderMode == CaveSurfaceBrushRenderMode.InstancedMesh
                && instanceMaterial != null)
            {
                instanceMaterial.enableInstancing = true;
            }
        }

        private static Vector2 SortAndClampScale(Vector2 range)
        {
            float first = Mathf.Max(0.01f, range.x);
            float second = Mathf.Max(0.01f, range.y);
            return new Vector2(
                Mathf.Min(first, second),
                Mathf.Max(first, second));
        }
    }
}
