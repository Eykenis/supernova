using UnityEngine;

namespace Supernova.Voxels
{
    /// <summary>
    /// Identifies the voxel type represented by a mined, physical ore drop.
    /// Motion and attraction are handled by the Rigidbody and player magnet.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class MinedOreDrop : MonoBehaviour
    {
        public const float DefaultMassDensity = 10f;

        [SerializeField] private VoxelTypeId voxelType = VoxelTypeId.Default;
        [SerializeField, Min(1)] private int voxelCount = 1;
        [SerializeField, Min(0.001f)] private float massDensity =
            DefaultMassDensity;

        private Rigidbody cachedBody;
        private Mesh ownedMesh;
        private Material ownedMaterial;

        public VoxelTypeId VoxelType => voxelType;
        public int VoxelCount => voxelCount;
        public float MassDensity => massDensity;
        public Mesh Mesh => ownedMesh;
        public Rigidbody Body
        {
            get
            {
                if (cachedBody == null) cachedBody = GetComponent<Rigidbody>();
                return cachedBody;
            }
        }

        public void Configure(
            VoxelTypeId type,
            int representedVoxelCount,
            Mesh mesh,
            float density = DefaultMassDensity,
            Material material = null)
        {
            voxelType = type.IsAir ? VoxelTypeId.Default : type;
            voxelCount = Mathf.Max(1, representedVoxelCount);
            massDensity = Mathf.Max(0.001f, density);
            ownedMesh = mesh;
            ownedMaterial = material;
            Body.mass = massDensity * voxelCount;
        }

        private void OnDestroy()
        {
            if (ownedMesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(ownedMesh);
            }
            else
            {
                DestroyImmediate(ownedMesh);
            }
            ownedMesh = null;
            if (ownedMaterial != null)
            {
                Texture ownedTexture = ownedMaterial.HasProperty("_BaseMap")
                    ? ownedMaterial.GetTexture("_BaseMap")
                    : null;
                if (Application.isPlaying) Destroy(ownedMaterial);
                else DestroyImmediate(ownedMaterial);
                if (ownedTexture != null)
                {
                    if (Application.isPlaying) Destroy(ownedTexture);
                    else DestroyImmediate(ownedTexture);
                }
                ownedMaterial = null;
            }
        }
    }
}
