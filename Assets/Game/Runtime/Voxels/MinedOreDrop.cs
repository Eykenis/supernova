using UnityEngine;
using Supernova.Gameplay;

namespace Supernova.Voxels
{
    /// <summary>
    /// Identifies the voxel type represented by a mined, physical ore drop.
    /// Motion and attraction are handled by the Rigidbody and player magnet.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(ValuableObject))]
    public sealed class MinedOreDrop :
        MonoBehaviour,
        ValuableObject.IBreakEffect
    {
        public const float DefaultMassDensity = 10f;
        public const float RecoveredLinearScale = 0.68f;

        [SerializeField] private VoxelTypeId voxelType = VoxelTypeId.Default;
        [SerializeField, Min(1)] private int voxelCount = 1;
        [SerializeField, Min(0.000001f)]
        private float representedFullVoxelVolume = 1f;
        [SerializeField, Min(0.001f)] private float massDensity =
            DefaultMassDensity;

        private Rigidbody cachedBody;
        private ValuableObject cachedValuable;
        private Mesh ownedMesh;
        private Material ownedMaterial;
        private bool isWaitingForTerrainColliderRebuild;
        private bool restoreIsKinematic;
        private bool restoreDetectCollisions;
        private Vector3 suspendedVelocity;
        private Vector3 suspendedAngularVelocity;

        public VoxelTypeId VoxelType => voxelType;
        public int VoxelCount => voxelCount;
        public float RepresentedFullVoxelVolume =>
            Mathf.Max(0f, representedFullVoxelVolume);
        public float MassDensity => massDensity;
        public Mesh Mesh => ownedMesh;
        public bool IsWaitingForTerrainColliderRebuild =>
            isWaitingForTerrainColliderRebuild;
        public BreakFragmentEffect LastBreakEffect { get; private set; }
        public ValuableObject Valuable
        {
            get
            {
                if (cachedValuable == null)
                    cachedValuable = GetComponent<ValuableObject>();
                return cachedValuable;
            }
        }
        public int Value => Valuable != null ? Valuable.CurrentValue : 0;
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
            float representedVolumeInFullVoxels,
            Mesh mesh,
            float density = DefaultMassDensity,
            Material material = null,
            int valuePerFullVoxel = 1,
            float fragility = 0.25f)
        {
            voxelType = type.IsAir ? VoxelTypeId.Default : type;
            voxelCount = Mathf.Max(1, representedVoxelCount);
            representedFullVoxelVolume = Mathf.Max(
                0.000001f,
                representedVolumeInFullVoxels);
            massDensity = Mathf.Max(0.001f, density);
            ownedMesh = mesh;
            ownedMaterial = material;
            Body.mass = Mathf.Max(
                0.01f,
                massDensity * representedFullVoxelVolume);
            Valuable.Configure(
                CalculateInitialValue(
                    representedFullVoxelVolume,
                    valuePerFullVoxel),
                fragility);
        }

        public static int CalculateInitialValue(
            float representedVolumeInFullVoxels,
            int valuePerFullVoxel)
        {
            return Mathf.Max(
                1,
                Mathf.RoundToInt(
                    Mathf.Max(0f, representedVolumeInFullVoxels)
                        * Mathf.Max(1, valuePerFullVoxel)));
        }

        internal void SuspendForTerrainColliderRebuild()
        {
            if (isWaitingForTerrainColliderRebuild)
            {
                return;
            }

            Rigidbody body = Body;
            restoreIsKinematic = body.isKinematic;
            restoreDetectCollisions = body.detectCollisions;
            suspendedVelocity = body.velocity;
            suspendedAngularVelocity = body.angularVelocity;
            isWaitingForTerrainColliderRebuild = true;

            Valuable.SetCollisionValueLossProtected(this, true);
            body.detectCollisions = false;
            body.isKinematic = true;
        }

        internal void ReleaseAfterTerrainColliderRebuild()
        {
            if (!isWaitingForTerrainColliderRebuild)
            {
                return;
            }

            Rigidbody body = Body;
            isWaitingForTerrainColliderRebuild = false;
            body.isKinematic = restoreIsKinematic;
            body.detectCollisions = restoreDetectCollisions;
            if (!restoreIsKinematic)
            {
                body.velocity = suspendedVelocity;
                body.angularVelocity = suspendedAngularVelocity;
                body.WakeUp();
            }
            Valuable.SetCollisionValueLossProtected(this, false);
        }

        public bool TrySpawnBreakEffect(
            ValuableObject.BreakContext context)
        {
            Mesh sourceMesh = ownedMesh;
            MeshRenderer sourceRenderer = GetComponent<MeshRenderer>();
            if (sourceMesh == null || sourceRenderer == null)
            {
                return false;
            }

            int fragmentCount = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Sqrt(voxelCount)) + 3,
                4,
                8);
            System.Collections.Generic.IReadOnlyList
                <MeshFragmentBuilder.Fragment> fragments =
                MeshFragmentBuilder.Build(
                    sourceMesh,
                    fragmentCount,
                    context.RandomSeed);
            if (fragments.Count == 0)
            {
                return false;
            }

            Material transferredMaterial = ownedMaterial;
            LastBreakEffect =
                BreakFragmentEffect.SpawnMeshes(
                    $"{gameObject.name} Fragments",
                    fragments,
                    sourceRenderer.sharedMaterials,
                    context,
                    transferredMaterial,
                    null);
            if (LastBreakEffect == null)
            {
                return false;
            }

            ownedMaterial = null;
            return true;
        }

        private void OnDestroy()
        {
            if (ownedMesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(ownedMesh);
                }
                else
                {
                    DestroyImmediate(ownedMesh);
                }
                ownedMesh = null;
            }

            if (ownedMaterial != null)
            {
                if (Application.isPlaying) Destroy(ownedMaterial);
                else DestroyImmediate(ownedMaterial);
                ownedMaterial = null;
            }
        }
    }
}
