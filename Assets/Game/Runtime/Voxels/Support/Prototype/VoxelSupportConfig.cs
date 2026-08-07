using UnityEngine;

namespace Supernova.Voxels.Support.Prototype
{
    /// <summary>
    /// Configuration for the voxel support graph system prototype.
    /// All tunables are exposed for rapid iteration in the Editor.
    /// </summary>
    [CreateAssetMenu(
        fileName = "VoxelSupportConfig",
        menuName = "Supernova/Prototypes/Voxel Support Config")]
    public sealed class VoxelSupportConfig : ScriptableObject
    {
        [Header("Search Bounds")]
        [Tooltip("Maximum BFS expansion radius from the removal point, in voxels.")]
        [SerializeField, Min(1)]
        private int maxSearchRadius = 16;

        [Tooltip("Maximum number of voxels in a single affected sub-graph before "
                 + "we cap the BFS and fall back to a conservative estimate.")]
        [SerializeField, Min(64)]
        private int maxSubGraphVoxels = 512;

        [Header("Cascade")]
        [Tooltip("Maximum cascade iterations per removal event. "
                 + "Each iteration may discover newly unsupported voxels.")]
        [SerializeField, Min(0), Range(0, 10)]
        private int maxCascadeIterations = 5;

        [Tooltip("Max collapsed voxels per frame. Surplus is deferred to the next frame.")]
        [SerializeField, Min(1)]
        private int maxCollapsesPerFrame = 256;

        [Header("Anchors")]
        [Tooltip("World Y level at or below which solid voxels count as bedrock anchors.")]
        [SerializeField, Range(0, 16)]
        private int bedrockYThreshold = 0;

        [Tooltip("When true, any voxel with no neighbours below is treated as a potential "
                 + "fall risk even before removal — useful for spotting cantilevers.")]
        [SerializeField]
        private bool highlightUnsupportedOnStart = true;

        [Header("Visualization")]
        [Tooltip("Material used for stable / anchored voxels.")]
        [SerializeField]
        private Material stableMaterial;

        [Tooltip("Material used for voxels at risk (single support path).")]
        [SerializeField]
        private Material atRiskMaterial;

        [Tooltip("Material used for collapsed / floating voxels detected after removal.")]
        [SerializeField]
        private Material collapsedMaterial;

        // --- Read-only accessors (keep fields private) ---

        public int MaxSearchRadius => maxSearchRadius;
        public int MaxSubGraphVoxels => Mathf.Max(64, maxSubGraphVoxels);
        public int MaxCascadeIterations => maxCascadeIterations;
        public int MaxCollapsesPerFrame => maxCollapsesPerFrame;
        public int BedrockYThreshold => bedrockYThreshold;
        public bool HighlightUnsupportedOnStart => highlightUnsupportedOnStart;
        public Material StableMaterial => stableMaterial;
        public Material AtRiskMaterial => atRiskMaterial;
        public Material CollapsedMaterial => collapsedMaterial;
    }
}
