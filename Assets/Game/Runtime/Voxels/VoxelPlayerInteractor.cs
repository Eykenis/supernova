using Supernova.Audio;
using Supernova.Effects;
using Supernova.Gameplay;
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
        private SolidVoxelPrototype pendingMinePlatform;
        private VoxelTargetReference pendingMineTarget;
        private Vector3 pendingMineDirection;
        private Vector3 pendingMinePoint;
        private Vector3 pendingMineNormal;
        private VoxelMiningBrushSettings pendingMineBrush =
            VoxelMiningBrushSettings.SingleVoxel;
        private SoundEffectCue pendingMineSound;
        private float pendingMineTime;

        private IVoxelTerrain Terrain => terrain as IVoxelTerrain;
        public IVoxelTerrain VoxelTerrain
        {
            get
            {
                ResolveReferences();
                return Terrain;
            }
        }

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
        }

        public bool TryScheduleMineAtCrosshair(float delay)
        {
            return TryScheduleMineAtCrosshair(
                delay,
                VoxelMiningBrushSettings.SingleVoxel,
                null);
        }

        public bool TryScheduleMineAtCrosshair(
            float delay,
            VoxelMiningBrushSettings brush)
        {
            return TryScheduleMineAtCrosshair(delay, brush, null);
        }

        public bool TryScheduleMineAtCrosshair(
            float delay,
            VoxelMiningBrushSettings brush,
            SoundEffectCue miningHitSound)
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

            bool foundVoxel = TryGetTarget(out VoxelTargetHit voxelHit);
            bool foundPlatform = TryGetPlatformTarget(
                out SolidVoxelPrototype platform,
                out RaycastHit platformHit);
            if (!foundVoxel && !foundPlatform)
            {
                return false;
            }

            if (foundPlatform
                && (!foundVoxel
                    || platformHit.distance * platformHit.distance
                        <= (voxelHit.Point - viewCamera.transform.position)
                            .sqrMagnitude))
            {
                float platformDelay = Mathf.Max(0f, delay);
                if (platformDelay <= 0f)
                {
                    bool mined = platform.DestroyByMining();
                    if (mined)
                        PlayMiningSound(miningHitSound, platformHit.point);
                    return mined;
                }

                pendingMinePlatform = platform;
                pendingMinePoint = platformHit.point;
                pendingMineNormal = platformHit.normal;
                pendingMineSound = miningHitSound;
                pendingMineTime = Time.time + platformDelay;
                hasPendingMine = true;
                return true;
            }

            float clampedDelay = Mathf.Max(0f, delay);
            if (clampedDelay <= 0f)
            {
                bool mined = voxelHit.Target.TryMineBrush(
                    viewCamera.transform.forward,
                    brush,
                    out VoxelMiningBrushResult result);
                if (mined)
                {
                    PlayMiningImpact(
                        voxelHit.Point,
                        voxelHit.Normal,
                        viewCamera.transform.forward,
                        result);
                    PlayMiningSound(miningHitSound, voxelHit.Point);
                }
                return mined;
            }

            pendingMineTarget = voxelHit.Target;
            pendingMineDirection = viewCamera.transform.forward;
            pendingMinePoint = voxelHit.Point;
            pendingMineNormal = voxelHit.Normal;
            pendingMineBrush = brush;
            pendingMineSound = miningHitSound;
            pendingMineTime = Time.time + clampedDelay;
            hasPendingMine = true;
            return true;
        }

        public bool TryMineAtCrosshair(out VoxelMiningResult result)
        {
            result = default;
            ResolveReferences();
            bool foundVoxel = TryGetTarget(out VoxelTargetHit voxelHit);
            if (TryGetPlatformTarget(
                    out SolidVoxelPrototype platform,
                    out RaycastHit platformHit)
                && (!foundVoxel
                    || platformHit.distance * platformHit.distance
                        <= (voxelHit.Point - viewCamera.transform.position)
                            .sqrMagnitude))
            {
                return platform.DestroyByMining();
            }

            if (!foundVoxel)
            {
                return false;
            }

            Vector3 mineDirection = viewCamera.transform.forward;
            bool mined = voxelHit.Target.TryMineVoxel(out result);
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
                    voxelHit.Point,
                    voxelHit.Normal,
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
            bool foundVoxel = TryGetTarget(out VoxelTargetHit voxelHit);
            if (TryGetPlatformTarget(
                    out SolidVoxelPrototype platform,
                    out RaycastHit platformHit)
                && (!foundVoxel
                    || platformHit.distance * platformHit.distance
                        <= (voxelHit.Point - viewCamera.transform.position)
                            .sqrMagnitude))
            {
                return platform.DestroyByMining();
            }

            if (!foundVoxel)
            {
                return false;
            }

            Vector3 mineDirection = viewCamera.transform.forward;
            bool mined = voxelHit.Target.TryMineBrush(
                mineDirection,
                brush,
                out result);
            if (mined)
            {
                PlayMiningImpact(
                    voxelHit.Point,
                    voxelHit.Normal,
                    mineDirection,
                    result);
            }
            return mined;
        }

        internal void ApplyPendingMineIfReady()
        {
            if (!hasPendingMine || Time.time < pendingMineTime) return;
            hasPendingMine = false;
            SolidVoxelPrototype platform = pendingMinePlatform;
            pendingMinePlatform = null;
            SoundEffectCue miningHitSound = pendingMineSound;
            pendingMineSound = null;
            if (platform != null)
            {
                if (platform.DestroyByMining())
                    PlayMiningSound(miningHitSound, pendingMinePoint);
                return;
            }

            bool mined = pendingMineTarget.TryMineBrush(
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
                PlayMiningSound(miningHitSound, pendingMinePoint);
            }
        }

        private void OnDisable()
        {
            hasPendingMine = false;
            pendingMinePlatform = null;
            pendingMineTarget = default;
            pendingMineSound = null;
        }

        private bool TryGetTarget(out VoxelTargetHit targetHit)
        {
            targetHit = default;

            IVoxelTerrain voxelTerrain = Terrain;
            if (viewCamera == null)
            {
                return false;
            }

            Vector3 camPos = viewCamera.transform.position;
            Vector3 forward = viewCamera.transform.forward;
            bool foundHit = VoxelTargetResolver.TryRaycast(
                new Ray(camPos, forward),
                Profile.InteractionReach,
                raycastMask,
                voxelTerrain,
                out targetHit);

            // Fallback for the "face pressed against a block" case: a non-convex
            // MeshCollider reports no hit when the ray starts inside it, so retry
            // from an origin pulled slightly back along the view direction.
            if (!foundHit)
            {
                float backstep = Profile.MineRayBackstep;
                if (backstep > 0f)
                {
                    foundHit = VoxelTargetResolver.TryRaycast(
                        new Ray(camPos - forward * backstep, forward),
                        Profile.InteractionReach + backstep,
                        raycastMask,
                        voxelTerrain,
                        out targetHit);
                }
            }

            return foundHit;
        }

        private bool TryGetPlatformTarget(
            out SolidVoxelPrototype platform,
            out RaycastHit platformHit)
        {
            platform = null;
            platformHit = default;
            if (viewCamera == null)
                return false;

            RaycastHit[] hits = Physics.RaycastAll(
                new Ray(
                    viewCamera.transform.position,
                    viewCamera.transform.forward),
                Profile.InteractionReach,
                raycastMask,
                QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
                return false;

            System.Array.Sort(
                hits,
                (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                SolidVoxelPrototype candidate =
                    hits[i].collider.GetComponentInParent<
                        SolidVoxelPrototype>();
                if (candidate == null)
                    continue;

                platform = candidate;
                platformHit = hits[i];
                return true;
            }
            return false;
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
            IVoxelTerrain voxelTerrain = Terrain;
            if (voxelTerrain != null
                && voxelTerrain.VoxelTypeCatalog != null)
            {
                voxelColor = VoxelTypeUtility.ResolveMaterialColor(
                    result.TargetType,
                    voxelTerrain.VoxelTypeCatalog.Definitions,
                    fallbackColor);
            }

            miningImpactEffect.Play(
                hitPoint,
                hitNormal,
                miningDirection,
                voxelColor,
                result);
        }

        private static void PlayMiningSound(
            SoundEffectCue miningHitSound,
            Vector3 hitPoint)
        {
            SoundEffectEvents.RequestPlay(miningHitSound, hitPoint);
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
                MonoBehaviour[] candidates = FindObjectsOfType<MonoBehaviour>();
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i] is IVoxelTerrain)
                    {
                        terrain = candidates[i];
                        break;
                    }
                }
            }

            if (miningImpactEffect == null)
            {
                miningImpactEffect = GetComponent<VoxelMiningImpactEffect>();
            }
        }

    }
}
