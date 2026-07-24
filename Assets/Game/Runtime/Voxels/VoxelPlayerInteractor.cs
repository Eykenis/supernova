using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using UnityEngine;

namespace Supernova.Voxels
{
    [RequireComponent(typeof(PlayerProfile))]
    public sealed class VoxelPlayerInteractor : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private MonoBehaviour terrain;
        private PlayerProfile profile;
        private int raycastMask;
        private bool hasPendingMine;
        private Vector3Int pendingMineVoxel;
        private float pendingMineTime;

        private IVoxelTerrain Terrain => terrain as IVoxelTerrain;

        private PlayerProfile Profile
        {
            get
            {
                if (profile == null) profile = GetComponent<PlayerProfile>();
                if (profile == null) profile = gameObject.AddComponent<PlayerProfile>();
                return profile;
            }
        }

        private void Awake()
        {
            ResolveReferences();
            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            raycastMask = ignoreRaycastLayer >= 0
                ? ~(1 << ignoreRaycastLayer)
                : Physics.DefaultRaycastLayers;
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ApplyPendingMineIfReady();

            TryGetTarget(
                out _,
                out _,
                out Vector3Int placeVoxel,
                out bool canPlace);

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            if (Input.GetMouseButtonDown(1) && canPlace)
            {
                Terrain.TrySetVoxelAndRebuild(
                    placeVoxel.x,
                    placeVoxel.y,
                    placeVoxel.z,
                    1f,
                    VoxelTypeId.Default);
            }
        }

        public bool TryScheduleMineAtCrosshair(float delay)
        {
            ResolveReferences();
            if (!TryGetTarget(
                    out Vector3Int removeVoxel,
                    out bool canRemove,
                    out _,
                    out _)
                || !canRemove)
            {
                return false;
            }

            float clampedDelay = Mathf.Max(0f, delay);
            if (clampedDelay <= 0f)
            {
                return Terrain.TryMineVoxel(removeVoxel, out _);
            }

            pendingMineVoxel = removeVoxel;
            pendingMineTime = Time.time + clampedDelay;
            hasPendingMine = true;
            return true;
        }

        public bool TryMineAtCrosshair(out VoxelMiningResult result)
        {
            result = default;
            ResolveReferences();
            if (!TryGetTarget(
                    out Vector3Int removeVoxel,
                    out bool canRemove,
                    out _,
                    out _)
                || !canRemove)
            {
                return false;
            }

            return Terrain.TryMineVoxel(removeVoxel, out result);
        }

        private void ApplyPendingMineIfReady()
        {
            if (!hasPendingMine || Time.time < pendingMineTime) return;
            hasPendingMine = false;
            IVoxelTerrain voxelTerrain = Terrain;
            if (voxelTerrain != null)
            {
                voxelTerrain.TryMineVoxel(pendingMineVoxel, out _);
            }
        }

        private void OnDisable()
        {
            hasPendingMine = false;
        }

        private bool TryGetTarget(
            out Vector3Int removeVoxel,
            out bool canRemove,
            out Vector3Int placeVoxel,
            out bool canPlace)
        {
            removeVoxel = default;
            placeVoxel = default;
            canRemove = false;
            canPlace = false;

            IVoxelTerrain voxelTerrain = Terrain;
            if (viewCamera == null || voxelTerrain == null || voxelTerrain.World == null)
            {
                return false;
            }

            Vector3 camPos = viewCamera.transform.position;
            Vector3 forward = viewCamera.transform.forward;

            // Primary ray from the true camera position (the proven behavior).
            bool foundHit = TryRaycastTerrain(
                new Ray(camPos, forward),
                Profile.InteractionReach,
                voxelTerrain,
                out RaycastHit hit);

            // Fallback for the "face pressed against a block" case: a non-convex
            // MeshCollider reports no hit when the ray starts inside it, so retry
            // from an origin pulled slightly back along the view direction.
            if (!foundHit)
            {
                float backstep = Profile.MineRayBackstep;
                if (backstep > 0f)
                {
                    foundHit = TryRaycastTerrain(
                        new Ray(camPos - forward * backstep, forward),
                        Profile.InteractionReach + backstep,
                        voxelTerrain,
                        out hit);
                }
            }

            if (!foundHit)
            {
                return false;
            }

            // Resolve the target by marching along the view ray from the hit point
            // instead of offsetting by the interpolated surface normal. The smooth
            // mesh normal is not axis-aligned, so normal-based rounding lands on the
            // wrong (often air) voxel near edges/slopes, causing the on/off flicker.
            InfiniteVoxelWorld world = voxelTerrain.World;
            float step = voxelTerrain.VoxelSize * 0.25f;
            Vector3Int lastAir = voxelTerrain.WorldPositionToVoxel(hit.point - forward * step);
            bool haveAir = world.TryGetDensity(lastAir.x, lastAir.y, lastAir.z, out float airDensity)
                && airDensity < 0f;

            for (float d = 0f; d <= voxelTerrain.VoxelSize * 1.5f; d += step)
            {
                Vector3Int probe = voxelTerrain.WorldPositionToVoxel(hit.point + forward * d);
                if (world.TryGetDensity(probe.x, probe.y, probe.z, out float density)
                    && density >= 0f)
                {
                    removeVoxel = probe;
                    canRemove = true;
                    if (haveAir)
                    {
                        placeVoxel = lastAir;
                        canPlace = true;
                    }
                    return true;
                }

                lastAir = probe;
                haveAir = true;
            }

            return false;
        }

        // Returns the nearest terrain hit along the ray, skipping any non-terrain
        // colliders (e.g. the player's own capsule) the backstepped origin may cross.
        private bool TryRaycastTerrain(
            Ray ray,
            float maxDistance,
            IVoxelTerrain voxelTerrain,
            out RaycastHit terrainHit)
        {
            terrainHit = default;
            RaycastHit[] hits = Physics.RaycastAll(
                ray,
                maxDistance,
                raycastMask,
                QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
            {
                return false;
            }

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Transform terrainTransform = voxelTerrain.TerrainTransform;
            for (int i = 0; i < hits.Length; i++)
            {
                Transform t = hits[i].transform;
                if (t == terrainTransform || t.IsChildOf(terrainTransform))
                {
                    terrainHit = hits[i];
                    return true;
                }
            }

            return false;
        }

        private void ResolveReferences()
        {
            if (viewCamera == null)
            {
                viewCamera = GetComponentInChildren<Camera>(true);
                if (viewCamera == null)
                {
                    viewCamera = Camera.main;
                }
            }

            if (Terrain == null)
            {
                terrain = FindObjectOfType<MinecraftCaveInfiniteWorld>();
            }
        }

    }
}
