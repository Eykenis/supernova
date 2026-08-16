using System.Collections.Generic;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Data-driven placement and shape configuration for one normal ore pass.
    /// Voxel appearance and mining behaviour remain on the referenced type assets.
    /// </summary>
    [CreateAssetMenu(
        fileName = "OreFeature",
        menuName = "Supernova/Voxels/Minecraft Ore Feature")]
    public sealed class VoxelOreFeatureDefinition : ScriptableObject
    {
        [Header("Target")]
        [SerializeField] private VoxelTypeDefinition resultVoxelType;
        [SerializeField] private List<VoxelTypeDefinition> replaceableVoxelTypes =
            new List<VoxelTypeDefinition>();

        [Header("Placement (per 16 x 16 region)")]
        [SerializeField] private int seedSalt = 3109;
        [SerializeField, Min(0)] private int attemptsPerRegion = 8;
        [SerializeField, Range(0f, 1f)] private float placementChance = 1f;
        [SerializeField]
        private MinecraftOreFeatureSettings.HeightDistribution heightDistribution =
            MinecraftOreFeatureSettings.HeightDistribution.Trapezoid;
        [SerializeField] private int minHeight = -64;
        [SerializeField] private int maxHeight = 64;
        [SerializeField, Min(0)] private int plateau;

        [Header("Normal Ore Blob")]
        [SerializeField, Range(1, 64)] private int size = 8;
        [SerializeField, Range(0f, 1f)]
        private float discardChanceOnAirExposure = 0.5f;

        [Header("Mined Ore")]
        [Tooltip("Funds contributed by one full voxel volume of this ore.")]
        [SerializeField, Min(1)] private int oreUnitValue = 10;
        [Tooltip("Rigidbody mass contributed by one full voxel volume of this ore.")]
        [SerializeField, Min(0.001f)] private float massDensity = 10f;
        [Tooltip("Fraction of damaging collision impulse converted into lost value.")]
        [SerializeField, Range(0f, 1f)] private float fragility = 0.25f;
        [Tooltip("Effect template used by recovered valuable bodies. Runtime "
            + "instances keep this material's shader and effect settings, but "
            + "always use the ore voxel material's Base Map.")]
        [SerializeField] private Material recoveredMaterial;

        public VoxelTypeDefinition ResultVoxelType => resultVoxelType;
        public IReadOnlyList<VoxelTypeDefinition> ReplaceableVoxelTypes =>
            replaceableVoxelTypes;
        public int SeedSalt => seedSalt;
        public int AttemptsPerRegion => attemptsPerRegion;
        public float PlacementChance => placementChance;
        public MinecraftOreFeatureSettings.HeightDistribution HeightDistribution =>
            heightDistribution;
        public int MinHeight => minHeight;
        public int MaxHeight => maxHeight;
        public int Plateau => plateau;
        public int Size => size;
        public float DiscardChanceOnAirExposure =>
            discardChanceOnAirExposure;
        public int OreUnitValue => Mathf.Max(1, oreUnitValue);
        public float MassDensity => Mathf.Max(0.001f, massDensity);
        public float Fragility => Mathf.Clamp01(fragility);
        public Material RecoveredMaterial => recoveredMaterial;

        public void Configure(
            VoxelTypeDefinition result,
            IEnumerable<VoxelTypeDefinition> replaceable,
            int featureSeedSalt,
            int attempts,
            float chance,
            MinecraftOreFeatureSettings.HeightDistribution distribution,
            int minimumHeight,
            int maximumHeight,
            int heightPlateau,
            int veinSize,
            float airExposureDiscardChance,
            float rigidbodyMassDensity = 10f,
            float minedFragility = 0.25f,
            int minedOreUnitValue = 10,
            Material minedOreMaterial = null)
        {
            resultVoxelType = result;
            replaceableVoxelTypes = replaceable != null
                ? new List<VoxelTypeDefinition>(replaceable)
                : new List<VoxelTypeDefinition>();
            seedSalt = featureSeedSalt;
            attemptsPerRegion = Mathf.Max(0, attempts);
            placementChance = Mathf.Clamp01(chance);
            heightDistribution = distribution;
            minHeight = Mathf.Min(minimumHeight, maximumHeight);
            maxHeight = Mathf.Max(minimumHeight, maximumHeight);
            plateau = Mathf.Max(0, heightPlateau);
            size = Mathf.Clamp(veinSize, 1, 64);
            discardChanceOnAirExposure =
                Mathf.Clamp01(airExposureDiscardChance);
            oreUnitValue = Mathf.Max(1, minedOreUnitValue);
            massDensity = Mathf.Max(0.001f, rigidbodyMassDensity);
            fragility = Mathf.Clamp01(minedFragility);
            recoveredMaterial = minedOreMaterial;
        }

        public bool TryCreateSettings(
            out MinecraftOreFeatureSettings settings,
            out string error)
        {
            if (resultVoxelType == null)
            {
                settings = default;
                error = $"Ore feature '{name}' has no result voxel type.";
                return false;
            }

            var replacements = new List<VoxelTypeId>();
            if (replaceableVoxelTypes != null)
            {
                for (int i = 0; i < replaceableVoxelTypes.Count; i++)
                {
                    VoxelTypeDefinition definition = replaceableVoxelTypes[i];
                    if (definition != null
                        && !replacements.Contains(definition.TypeId))
                    {
                        replacements.Add(definition.TypeId);
                    }
                }
            }
            if (replacements.Count == 0)
            {
                settings = default;
                error =
                    $"Ore feature '{name}' has no replaceable voxel types.";
                return false;
            }

            settings = new MinecraftOreFeatureSettings(
                resultVoxelType.TypeId,
                replacements,
                seedSalt,
                attemptsPerRegion,
                placementChance,
                heightDistribution,
                minHeight,
                maxHeight,
                plateau,
                size,
                discardChanceOnAirExposure);
            error = string.Empty;
            return true;
        }

        private void OnValidate()
        {
            if (replaceableVoxelTypes == null)
            {
                replaceableVoxelTypes = new List<VoxelTypeDefinition>();
            }
            attemptsPerRegion = Mathf.Max(0, attemptsPerRegion);
            placementChance = Mathf.Clamp01(placementChance);
            if (maxHeight < minHeight)
            {
                maxHeight = minHeight;
            }
            plateau = Mathf.Max(0, plateau);
            size = Mathf.Clamp(size, 1, 64);
            discardChanceOnAirExposure =
                Mathf.Clamp01(discardChanceOnAirExposure);
            oreUnitValue = Mathf.Max(1, oreUnitValue);
            massDensity = Mathf.Max(0.001f, massDensity);
            fragility = Mathf.Clamp01(fragility);
        }
    }
}
