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
        [SerializeField] private CaveBiomeDefinition fallbackBiome;
        [SerializeField] private List<CaveBiomeSelection> selections =
            new List<CaveBiomeSelection>();

        public float NoiseFrequency => Mathf.Max(0.00001f, noiseFrequency);
        public int SeedSalt => seedSalt;
        public CaveBiomeDefinition FallbackBiome => fallbackBiome;
        public IReadOnlyList<CaveBiomeSelection> Selections => selections;

        public CaveBiomeDefinition Evaluate(Vector3 worldVoxelPosition, int worldSeed)
        {
            float noise = MinecraftCaveNoise.NormalNoise(
                worldVoxelPosition * NoiseFrequency,
                worldSeed ^ seedSalt,
                2);
            if (selections != null)
            {
                for (int i = 0; i < selections.Count; i++)
                {
                    CaveBiomeSelection selection = selections[i];
                    if (selection != null && selection.Contains(noise))
                    {
                        return selection.Biome;
                    }
                }
            }
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
