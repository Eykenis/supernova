using Supernova.Effects;
using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using UnityEngine;

namespace Supernova.Voxels
{
    [RequireComponent(typeof(PlayerProfile))]
    public sealed class VoxelPlayerInteractor : MonoBehaviour
    {
        private static readonly Vector3Int[] CellCornerOffsets =
        {
            new Vector3Int(0, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(1, 1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(1, 0, 1),
            new Vector3Int(0, 1, 1),
            new Vector3Int(1, 1, 1),
        };

        [SerializeField] private Camera viewCamera;
        [SerializeField] private MonoBehaviour terrain;
        [SerializeField] private VoxelMiningImpactEffect miningImpactEffect;
        private PlayerProfile profile;
        private int raycastMask;
        private bool hasPendingMine;
        private Vector3Int pendingMineVoxel;
        private Vector3 pendingMineDirection;
        private Vector3 pendingMinePoint;
        private Vector3 pendingMineNormal;
        private VoxelMiningBrushSettings pendingMineBrush =
            VoxelMiningBrushSettings.SingleVoxel;
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
                out bool canPlace,
                out _,
                out _,
                out _);

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
            return TryScheduleMineAtCrosshair(
                delay,
                VoxelMiningBrushSettings.SingleVoxel);
        }

        public bool TryScheduleMineAtCrosshair(
            float delay,
            VoxelMiningBrushSettings brush)
        {
            ResolveReferences();

            // The player controller and this component have no guaranteed Update order.
            // Settle an expired hit here before selecting the next voxel, otherwise the
            // next swing can overwrite the single pending slot before Update applies it.
            ApplyPendingMineIfReady();
            if (hasPendingMine)
            {
                return false;
            }

            if (!TryGetTarget(
                    out Vector3Int removeVoxel,
                    out bool canRemove,
                    out _,
                    out _,
                    out Vector3 mineDirection,
                    out Vector3 hitPoint,
                    out Vector3 hitNormal)
                || !canRemove)
            {
                return false;
            }

            float clampedDelay = Mathf.Max(0f, delay);
            if (clampedDelay <= 0f)
            {
                bool mined = Terrain.TryMineBrush(
                    removeVoxel,
                    mineDirection,
                    brush,
                    out VoxelMiningBrushResult result);
                if (mined)
                {
                    PlayMiningImpact(
                        hitPoint,
                        hitNormal,
                        mineDirection,
                        result);
                }
                return mined;
            }

            pendingMineVoxel = removeVoxel;
            pendingMineDirection = mineDirection;
            pendingMinePoint = hitPoint;
            pendingMineNormal = hitNormal;
            pendingMineBrush = brush;
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
                    out _,
                    out Vector3 mineDirection,
                    out Vector3 hitPoint,
                    out Vector3 hitNormal)
                || !canRemove)
            {
                return false;
            }

            bool mined = Terrain.TryMineVoxel(removeVoxel, out result);
            if (mined)
            {
                var brushResult = new VoxelMiningBrushResult(
                    result.Coordinate,
                    result.Type,
                    1,
                    1,
                    result.Destroyed ? 1 : 0,
                    result);
                PlayMiningImpact(
                    hitPoint,
                    hitNormal,
                    mineDirection,
                    brushResult);
            }
            return mined;
        }

        public bool TryMineBrushAtCrosshair(
            VoxelMiningBrushSettings brush,
            out VoxelMiningBrushResult result)
        {
            result = default;
            ResolveReferences();
            if (!TryGetTarget(
                    out Vector3Int removeVoxel,
                    out bool canRemove,
                    out _,
                    out _,
                    out Vector3 mineDirection,
                    out Vector3 hitPoint,
                    out Vector3 hitNormal)
                || !canRemove)
            {
                return false;
            }

            bool mined = Terrain.TryMineBrush(
                removeVoxel,
                mineDirection,
                brush,
                out result);
            if (mined)
            {
                PlayMiningImpact(
                    hitPoint,
                    hitNormal,
                    mineDirection,
                    result);
            }
            return mined;
        }

        private void ApplyPendingMineIfReady()
        {
            if (!hasPendingMine || Time.time < pendingMineTime) return;
            hasPendingMine = false;
            IVoxelTerrain voxelTerrain = Terrain;
            if (voxelTerrain != null)
            {
                bool mined = voxelTerrain.TryMineBrush(
                    pendingMineVoxel,
                    pendingMineDirection,
                    pendingMineBrush,
                    out VoxelMiningBrushResult result);
                if (mined)
                {
                    PlayMiningImpact(
                        pendingMinePoint,
                        pendingMineNormal,
                        pendingMineDirection,
                        result);
                }
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
            out bool canPlace,
            out Vector3 mineDirection,
            out Vector3 hitPoint,
            out Vector3 hitNormal)
        {
            removeVoxel = default;
            placeVoxel = default;
            canRemove = false;
            canPlace = false;
            mineDirection = default;
            hitPoint = default;
            hitNormal = default;

            IVoxelTerrain voxelTerrain = Terrain;
            if (viewCamera == null || voxelTerrain == null || voxelTerrain.World == null)
            {
                return false;
            }

            Vector3 camPos = viewCamera.transform.position;
            Vector3 forward = viewCamera.transform.forward;
            mineDirection = forward;

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

            hitPoint = hit.point;
            hitNormal = hit.normal;

            // A Marching Cubes triangle belongs to one grid cell. Resolve only from
            // the eight samples around that surface cell instead of marching farther
            // into the terrain, which could select a solid sample behind the visible
            // surface. Sampling just inside/outside also handles hits on cell borders.
            float sideOffset = voxelTerrain.VoxelSize * 0.05f;
            canRemove = TryResolveCellSample(
                hit.point + forward * sideOffset,
                hit.point,
                camPos,
                forward,
                voxelTerrain,
                true,
                out removeVoxel);
            canPlace = TryResolveCellSample(
                hit.point - forward * sideOffset,
                hit.point,
                camPos,
                forward,
                voxelTerrain,
                false,
                out placeVoxel);
            return canRemove || canPlace;
        }

        private static bool TryResolveCellSample(
            Vector3 pointOnSide,
            Vector3 surfacePoint,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            IVoxelTerrain voxelTerrain,
            bool requireSolid,
            out Vector3Int coordinate)
        {
            coordinate = default;
            Transform terrainTransform = voxelTerrain.TerrainTransform;
            float voxelSize = voxelTerrain.VoxelSize;
            Vector3 samplePosition =
                terrainTransform.InverseTransformPoint(pointOnSide) / voxelSize;
            var cellOrigin = new Vector3Int(
                Mathf.FloorToInt(samplePosition.x),
                Mathf.FloorToInt(samplePosition.y),
                Mathf.FloorToInt(samplePosition.z));

            bool found = false;
            float bestSurfaceDistance = float.PositiveInfinity;
            float bestRayDepth = float.PositiveInfinity;
            float tieTolerance = voxelSize * voxelSize * 0.0001f;
            InfiniteVoxelWorld world = voxelTerrain.World;

            for (int i = 0; i < CellCornerOffsets.Length; i++)
            {
                Vector3Int candidate = cellOrigin + CellCornerOffsets[i];
                if (!world.TryGetSample(
                        candidate.x,
                        candidate.y,
                        candidate.z,
                        out VoxelSample sample)
                    || sample.IsSolid(voxelTerrain.IsoLevel) != requireSolid)
                {
                    continue;
                }

                Vector3 candidateWorld = terrainTransform.TransformPoint(
                    (Vector3)candidate * voxelSize);
                float surfaceDistance =
                    (candidateWorld - surfacePoint).sqrMagnitude;
                float rayDepth = Vector3.Dot(
                    candidateWorld - rayOrigin,
                    rayDirection);
                if (surfaceDistance < bestSurfaceDistance - tieTolerance
                    || (Mathf.Abs(surfaceDistance - bestSurfaceDistance)
                        <= tieTolerance
                        && rayDepth < bestRayDepth))
                {
                    coordinate = candidate;
                    bestSurfaceDistance = surfaceDistance;
                    bestRayDepth = rayDepth;
                    found = true;
                }
            }

            return found;
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

        private void PlayMiningImpact(
            Vector3 hitPoint,
            Vector3 hitNormal,
            Vector3 miningDirection,
            VoxelMiningBrushResult result)
        {
            if (miningImpactEffect == null)
            {
                return;
            }

            var fallbackColor = new Color(0.46f, 0.49f, 0.5f, 1f);
            Color voxelColor = fallbackColor;
            var minecraftTerrain = terrain as MinecraftCaveInfiniteWorld;
            if (minecraftTerrain != null
                && minecraftTerrain.VoxelTypeCatalog != null)
            {
                voxelColor = VoxelTypeUtility.ResolveMaterialColor(
                    result.TargetType,
                    minecraftTerrain.VoxelTypeCatalog.Definitions,
                    fallbackColor);
            }

            miningImpactEffect.Play(
                hitPoint,
                hitNormal,
                miningDirection,
                voxelColor,
                result);
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

            if (miningImpactEffect == null)
            {
                miningImpactEffect = GetComponent<VoxelMiningImpactEffect>();
            }
        }

    }
}
