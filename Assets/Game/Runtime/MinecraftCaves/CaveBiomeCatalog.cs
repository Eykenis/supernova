using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    [Serializable]
    public sealed class CaveBiomeSelection
    {
        [SerializeField] private CaveBiomeDefinition biome;
        [SerializeField, Range(-1f, 1f)] private float minimumNoise;
        [SerializeField, Range(-1f, 1f)] private float maximumNoise = 1f;

        public CaveBiomeSelection(
            CaveBiomeDefinition biome,
            float minimumNoise,
            float maximumNoise)
        {
            this.biome = biome;
            this.minimumNoise = Mathf.Min(minimumNoise, maximumNoise);
            this.maximumNoise = Mathf.Max(minimumNoise, maximumNoise);
        }

        public CaveBiomeDefinition Biome => biome;
        public float MinimumNoise => Mathf.Min(minimumNoise, maximumNoise);
        public float MaximumNoise => Mathf.Max(minimumNoise, maximumNoise);

        public bool Contains(float noise)
        {
            return biome != null
                && noise >= MinimumNoise
                && noise <= MaximumNoise;
        }

        /// <summary>
        /// Returns a smooth 0..1 coverage inside this selection. Noise-domain
        /// endpoints (-1 and 1) are not treated as biome boundaries because
        /// noise cannot cross beyond them.
        /// </summary>
        public float EvaluateInteriorCoverage(float noise, float fadeWidth)
        {
            if (!Contains(noise))
            {
                return 0f;
            }

            float width = Mathf.Max(0f, fadeWidth);
            if (width <= Mathf.Epsilon)
            {
                return 1f;
            }

            float boundaryDistance = float.PositiveInfinity;
            if (MinimumNoise > -1f)
            {
                boundaryDistance = noise - MinimumNoise;
            }
            if (MaximumNoise < 1f)
            {
                boundaryDistance = Mathf.Min(
                    boundaryDistance,
                    MaximumNoise - noise);
            }
            if (float.IsPositiveInfinity(boundaryDistance))
            {
                return 1f;
            }

            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(boundaryDistance / width));
        }
    }

    /// <summary>
    /// Selects a cave biome from deterministic low-frequency 3D world noise.
    /// Entries are evaluated in list order; the fallback covers unmatched values.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CaveBiomeCatalog",
        menuName = "Supernova/World Generation/Cave Biome Catalog")]
    public sealed class CaveBiomeCatalog : ScriptableObject
    {
        [SerializeField, Min(0.00001f)] private float noiseFrequency = 0.008f;
        [SerializeField] private int seedSalt = 15485863;
        [SerializeField] private Material terrainSurfaceMaterial;
        [SerializeField] private CaveBiomeDefinition fallbackBiome;
        [SerializeField] private List<CaveBiomeSelection> selections =
            new List<CaveBiomeSelection>();

        public float NoiseFrequency => Mathf.Max(0.00001f, noiseFrequency);
        public int SeedSalt => seedSalt;
        public Material TerrainSurfaceMaterial => terrainSurfaceMaterial;
        public CaveBiomeDefinition FallbackBiome => fallbackBiome;
        public IReadOnlyList<CaveBiomeSelection> Selections => selections;

        public CaveBiomeDefinition Evaluate(Vector3 worldVoxelPosition, int worldSeed)
        {
            return EvaluateSurface(
                worldVoxelPosition,
                worldSeed,
                out _);
        }

        public float EvaluateNoise(Vector3 worldVoxelPosition, int worldSeed)
        {
            return MinecraftCaveNoise.NormalNoise(
                worldVoxelPosition * NoiseFrequency,
                worldSeed ^ seedSalt,
                2);
        }

        /// <summary>
        /// Evaluates the biome and its smooth interior coverage at an exact
        /// world-voxel position. The coverage is used by visual layers to fade
        /// toward zero at selection boundaries.
        /// </summary>
        public CaveBiomeDefinition EvaluateSurface(
            Vector3 worldVoxelPosition,
            int worldSeed,
            out float interiorCoverage)
        {
            float noise = EvaluateNoise(worldVoxelPosition, worldSeed);
            if (selections != null)
            {
                for (int i = 0; i < selections.Count; i++)
                {
                    CaveBiomeSelection selection = selections[i];
                    if (selection != null && selection.Contains(noise))
                    {
                        interiorCoverage = selection.EvaluateInteriorCoverage(
                            noise,
                            selection.Biome.TerrainSurfaceEdgeFade);
                        return selection.Biome;
                    }
                }
            }
            interiorCoverage = 1f;
            return fallbackBiome;
        }

        public void Configure(
            float frequency,
            int catalogSeedSalt,
            CaveBiomeDefinition fallback,
            IEnumerable<CaveBiomeSelection> biomeSelections)
        {
            noiseFrequency = Mathf.Max(0.00001f, frequency);
            seedSalt = catalogSeedSalt;
            fallbackBiome = fallback;
            selections = biomeSelections != null
                ? new List<CaveBiomeSelection>(biomeSelections)
                : new List<CaveBiomeSelection>();
        }

        public void ConfigureTerrainSurfaceMaterial(Material material)
        {
            terrainSurfaceMaterial = material;
        }

        private void OnValidate()
        {
            noiseFrequency = Mathf.Max(0.00001f, noiseFrequency);
            if (selections == null)
            {
                selections = new List<CaveBiomeSelection>();
            }
        }
    }
}
