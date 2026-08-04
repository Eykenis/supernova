using System.Collections.Generic;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    [CreateAssetMenu(
        fileName = "CaveBiome",
        menuName = "Supernova/World Generation/Cave Biome")]
    public sealed class CaveBiomeDefinition : ScriptableObject
    {
        [SerializeField] private string stableId = "cave-biome";
        [SerializeField] private string displayName = "Cave Biome";
        [SerializeField] private List<CaveSurfaceBrushDefinition> surfaceBrushes =
            new List<CaveSurfaceBrushDefinition>();

        [Header("Vegetation Tint")]
        [Tooltip("Colour at the base of a grass blade. Darker than the tip.")]
        [SerializeField]
        private Color vegetationRootColor = new Color(0.055f, 0.184f, 0.078f);
        [Tooltip("Colour at the tip of a grass blade.")]
        [SerializeField]
        private Color vegetationTipColor = new Color(0.34f, 0.61f, 0.208f);
        [Tooltip("Backlit rim colour approximating light through a thin blade.")]
        [SerializeField]
        private Color vegetationRimColor = new Color(0.53f, 0.79f, 0.35f);
        [Tooltip("Per-clump tint spread along the root-to-tip ramp.")]
        [SerializeField, Range(0f, 1f)] private float vegetationTintVariation = 0.3f;
        [Tooltip("Scales the brush wind strength so a biome can be calm or windy.")]
        [SerializeField, Min(0f)] private float vegetationWindResponse = 1f;

        public string StableId => string.IsNullOrWhiteSpace(stableId)
            ? name
            : stableId.Trim();
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName.Trim();
        public IReadOnlyList<CaveSurfaceBrushDefinition> SurfaceBrushes =>
            surfaceBrushes;
        public Color VegetationRootColor => vegetationRootColor;
        public Color VegetationTipColor => vegetationTipColor;
        public Color VegetationRimColor => vegetationRimColor;
        public float VegetationTintVariation =>
            Mathf.Clamp01(vegetationTintVariation);
        public float VegetationWindResponse =>
            Mathf.Max(0f, vegetationWindResponse);

        public void Configure(
            string id,
            string biomeDisplayName,
            IEnumerable<CaveSurfaceBrushDefinition> brushes)
        {
            stableId = id ?? string.Empty;
            displayName = biomeDisplayName ?? string.Empty;
            surfaceBrushes = brushes != null
                ? new List<CaveSurfaceBrushDefinition>(brushes)
                : new List<CaveSurfaceBrushDefinition>();
        }

        /// <summary>
        /// Sets the vegetation tint. Kept separate from <see cref="Configure"/> so
        /// existing three-argument callers stay valid; grass colour lives on the
        /// biome rather than the material so one shared material serves every
        /// biome, following Minecraft's grass colormap model.
        /// </summary>
        public void ConfigureVegetationTint(
            Color rootColor,
            Color tipColor,
            Color rimColor,
            float tintVariation,
            float windResponse)
        {
            vegetationRootColor = rootColor;
            vegetationTipColor = tipColor;
            vegetationRimColor = rimColor;
            vegetationTintVariation = Mathf.Clamp01(tintVariation);
            vegetationWindResponse = Mathf.Max(0f, windResponse);
        }

        private void OnValidate()
        {
            if (stableId == null) stableId = string.Empty;
            if (displayName == null) displayName = string.Empty;
            if (surfaceBrushes == null)
            {
                surfaceBrushes = new List<CaveSurfaceBrushDefinition>();
            }
            vegetationTintVariation = Mathf.Clamp01(vegetationTintVariation);
            vegetationWindResponse = Mathf.Max(0f, vegetationWindResponse);
        }
    }
}
