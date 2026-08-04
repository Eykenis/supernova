using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Data-driven definition for a deterministic structure placed once per
    /// X/Z region. The first example shape is an underground trial chamber.
    /// </summary>
    [CreateAssetMenu(
        fileName = "StructureFeature",
        menuName = "Supernova/Voxels/Minecraft Structure Feature")]
    public sealed class VoxelStructureFeatureDefinition : ScriptableObject
    {
        [Header("Identity and Material")]
        [SerializeField] private string stableId = "trial_chamber";
        [SerializeField] private VoxelTypeDefinition structureVoxelType;
        [Tooltip("Optional dense voxel template authored in VoxelStructureEditor. Empty samples carve terrain.")]
        [SerializeField] private VoxelStructureAsset structureTemplate;

        [Header("Random Placement")]
        [SerializeField] private int seedSalt = 7919;
        [SerializeField, Min(2)] private int regionSizeInChunks = 6;
        [SerializeField, Range(0f, 1f)] private float placementChance = 0.65f;
        [SerializeField] private int minFloorHeight = 72;
        [SerializeField] private int maxFloorHeight = 188;

        [Header("Trial Chamber Shape")]
        [SerializeField] private Vector3Int roomSize = new Vector3Int(21, 10, 17);
        [SerializeField, Range(1, 4)] private int wallThickness = 1;
        [SerializeField, Range(0, 8)] private int foundationDepth = 2;
        [SerializeField, Range(1, 9)] private int entranceWidth = 3;
        [SerializeField, Range(2, 12)] private int entranceHeight = 4;
        [SerializeField, Range(0, 32)] private int entranceLength = 14;

        public string StableId => stableId;
        public VoxelTypeDefinition StructureVoxelType => structureVoxelType;
        public VoxelStructureAsset StructureTemplate => structureTemplate;
        public int SeedSalt => seedSalt;
        public int RegionSizeInChunks => regionSizeInChunks;
        public float PlacementChance => placementChance;
        public int MinFloorHeight => minFloorHeight;
        public int MaxFloorHeight => maxFloorHeight;
        public Vector3Int RoomSize => roomSize;
        public int WallThickness => wallThickness;
        public int FoundationDepth => foundationDepth;
        public int EntranceWidth => entranceWidth;
        public int EntranceHeight => entranceHeight;
        public int EntranceLength => entranceLength;

        public void Configure(
            string featureId,
            VoxelTypeDefinition voxelType,
            int featureSeedSalt,
            int placementRegionSizeInChunks,
            float chance,
            int minimumFloorHeight,
            int maximumFloorHeight,
            Vector3Int dimensions,
            int shellThickness,
            int supportDepth,
            int doorwayWidth,
            int doorwayHeight,
            int doorwayLength,
            VoxelStructureAsset template = null)
        {
            stableId = featureId;
            structureVoxelType = voxelType;
            seedSalt = featureSeedSalt;
            regionSizeInChunks = placementRegionSizeInChunks;
            placementChance = chance;
            minFloorHeight = minimumFloorHeight;
            maxFloorHeight = maximumFloorHeight;
            roomSize = dimensions;
            wallThickness = shellThickness;
            foundationDepth = supportDepth;
            entranceWidth = doorwayWidth;
            entranceHeight = doorwayHeight;
            entranceLength = doorwayLength;
            structureTemplate = template;
            ClampConfiguration();
        }

        public void SetStructureTemplate(VoxelStructureAsset template)
        {
            structureTemplate = template;
        }

        public bool TryCreateSettings(
            out MinecraftStructureFeatureSettings settings,
            out string error)
        {
            if (structureVoxelType == null)
            {
                settings = default;
                error = $"Structure feature '{name}' has no structure voxel type.";
                return false;
            }

            try
            {
                Vector3Int templateSize = default;
                Vector3Int templateAnchor = default;
                float[] templateDensities = null;
                VoxelTypeId[] templateTypes = null;
                if (structureTemplate != null)
                {
                    templateSize = structureTemplate.Size;
                    templateAnchor = structureTemplate.Anchor;
                    structureTemplate.CopyData(
                        out templateDensities,
                        out templateTypes);
                }
                settings = new MinecraftStructureFeatureSettings(
                    stableId,
                    structureVoxelType.TypeId,
                    seedSalt,
                    regionSizeInChunks,
                    placementChance,
                    minFloorHeight,
                    maxFloorHeight,
                    roomSize,
                    wallThickness,
                    foundationDepth,
                    entranceWidth,
                    entranceHeight,
                    entranceLength,
                    templateSize,
                    templateAnchor,
                    templateDensities,
                    templateTypes);
                error = string.Empty;
                return true;
            }
            catch (System.Exception exception)
            {
                settings = default;
                error = $"Structure feature '{name}' is invalid: {exception.Message}";
                return false;
            }
        }

        private void OnValidate()
        {
            ClampConfiguration();
        }

        private void ClampConfiguration()
        {
            if (stableId == null) stableId = string.Empty;
            regionSizeInChunks = Mathf.Max(2, regionSizeInChunks);
            placementChance = Mathf.Clamp01(placementChance);
            minFloorHeight = Mathf.Clamp(
                minFloorHeight,
                1,
                VoxelColumnChunkData.Height - 2);
            maxFloorHeight = Mathf.Clamp(
                maxFloorHeight,
                minFloorHeight,
                VoxelColumnChunkData.Height - 2);
            roomSize = new Vector3Int(
                ClampOdd(roomSize.x, 7, 127),
                Mathf.Clamp(roomSize.y, 5, 64),
                ClampOdd(roomSize.z, 7, 127));
            maxFloorHeight = Mathf.Min(
                maxFloorHeight,
                VoxelColumnChunkData.Height - roomSize.y - 1);
            minFloorHeight = Mathf.Min(minFloorHeight, maxFloorHeight);
            wallThickness = Mathf.Clamp(
                wallThickness,
                1,
                Mathf.Min(roomSize.x, roomSize.z) / 2 - 2);
            foundationDepth = Mathf.Clamp(foundationDepth, 0, 8);
            entranceWidth = ClampOdd(
                entranceWidth,
                1,
                Mathf.Min(9, roomSize.x - wallThickness * 2 - 2));
            entranceHeight = Mathf.Clamp(entranceHeight, 2, roomSize.y - 2);
            entranceLength = Mathf.Clamp(entranceLength, 0, 32);
        }

        private static int ClampOdd(int value, int minimum, int maximum)
        {
            int result = Mathf.Clamp(value, minimum, maximum);
            if ((result & 1) == 0)
            {
                result = result < maximum ? result + 1 : result - 1;
            }
            return result;
        }
    }
}
