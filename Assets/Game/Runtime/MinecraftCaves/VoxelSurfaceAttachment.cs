using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Records the exact solid voxel and generated section owning a brushed prefab.
    /// The attachment is parented below that section, so section rebuild/unload destroys it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoxelSurfaceAttachment : MonoBehaviour
    {
        [SerializeField] private Vector3Int anchorVoxel;
        [SerializeField] private Vector3Int meshSection;
        [SerializeField] private CaveBiomeDefinition biome;
        [SerializeField] private CaveSurfaceBrushDefinition brush;

        public Vector3Int AnchorVoxel => anchorVoxel;
        public Vector3Int MeshSection => meshSection;
        public CaveBiomeDefinition Biome => biome;
        public CaveSurfaceBrushDefinition Brush => brush;

        public void Configure(
            Vector3Int voxel,
            Vector3Int section,
            CaveBiomeDefinition biomeDefinition,
            CaveSurfaceBrushDefinition brushDefinition)
        {
            anchorVoxel = voxel;
            meshSection = section;
            biome = biomeDefinition;
            brush = brushDefinition;
        }
    }
}
