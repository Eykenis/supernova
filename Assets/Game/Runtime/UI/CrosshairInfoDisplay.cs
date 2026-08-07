using Supernova.Gameplay;
using Supernova.Voxels;
using TMPro;
using UnityEngine;

namespace Supernova.UI
{
    internal enum CrosshairTargetType
    {
        None,
        Treasure,
        OreDrop,
        Voxel,
    }

    internal readonly struct CrosshairLookAtInfo
    {
        public CrosshairTargetType TargetType { get; }
        public string DisplayName { get; }
        public float FragilityOrDurability { get; }
        public float TotalWeight { get; }
        public int Durability { get; }

        public CrosshairLookAtInfo(
            CrosshairTargetType targetType,
            string displayName,
            float fragilityOrDurability,
            float totalWeight = 0f,
            int durability = 0)
        {
            TargetType = targetType;
            DisplayName = displayName ?? string.Empty;
            FragilityOrDurability = fragilityOrDurability;
            TotalWeight = totalWeight;
            Durability = durability;
        }

        public bool IsValid => TargetType != CrosshairTargetType.None;
    }

    /// <summary>
    /// Detects treasures and voxels under the player's crosshair and displays
    /// a screen-space info panel with their gameplay properties.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrosshairInfoDisplay : MonoBehaviour
    {
        private const float HideDelay = 0.15f;
        private const float AlphaLerpRate = 12f;
        private const float TerrainResolveInterval = 2f;

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
        [SerializeField] private MonoBehaviour terrainSource;

        public TMP_Text NameLabel { get; set; }
        public TMP_Text StatsLabel { get; set; }
        public GameObject RootObject { get; set; }
        public CanvasGroup RootCanvasGroup { get; set; }
        public UiDesignTokens DesignTokens { get; set; }

        private IVoxelTerrain voxelTerrain;
        private int raycastMask;
        private float interactionReach;
        private float nextTerrainResolveTime;
        private float visibilityAlpha;
        private float targetAlpha;
        private float hideCountdown;
        private bool wasVisible;
        private CrosshairTargetType currentType;

        private IVoxelTerrain Terrain
        {
            get
            {
                if (voxelTerrain == null && Time.unscaledTime >= nextTerrainResolveTime)
                {
                    nextTerrainResolveTime = Time.unscaledTime + TerrainResolveInterval;
                    ResolveTerrain();
                }
                return voxelTerrain;
            }
        }

        private void Awake()
        {
            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            raycastMask = ignoreRaycastLayer >= 0
                ? ~(1 << ignoreRaycastLayer)
                : Physics.DefaultRaycastLayers;
        }

        private void OnEnable()
        {
            visibilityAlpha = 0f;
            targetAlpha = 0f;
            hideCountdown = 0f;
        }

        /// <summary>
        /// Called each frame by GameHudController.Update.
        /// </summary>
        public void Refresh()
        {
            if (RootObject == null)
            {
                return;
            }

            if (GameHudController.IsGameplayInputBlocked)
            {
                HideImmediate();
                return;
            }

            ResolveReferences();
            CrosshairLookAtInfo info = DetectTarget();

            if (info.IsValid)
            {
                ShowInfo(info);
            }
            else
            {
                StartHide();
            }

            UpdateAlpha(Time.unscaledDeltaTime);
        }

        public void HideImmediate()
        {
            targetAlpha = 0f;
            visibilityAlpha = 0f;
            hideCountdown = 0f;
            currentType = CrosshairTargetType.None;
            ApplyAlpha();
        }

        private CrosshairLookAtInfo DetectTarget()
        {
            if (viewCamera == null)
            {
                return default;
            }

            Vector3 origin = viewCamera.transform.position;
            Vector3 direction = viewCamera.transform.forward;

            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                direction,
                interactionReach,
                raycastMask,
                QueryTriggerInteraction.Ignore);

            if (hits.Length == 0)
            {
                return default;
            }

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            // Check for treasure pickups first (closest wins).
            for (int i = 0; i < hits.Length; i++)
            {
                TreasurePickup treasure = hits[i].collider
                    .GetComponentInParent<TreasurePickup>();
                if (treasure != null && treasure.Definition != null)
                {
                    return new CrosshairLookAtInfo(
                        CrosshairTargetType.Treasure,
                        treasure.Definition.DisplayName,
                        treasure.Valuable != null
                            ? treasure.Valuable.Fragility
                            : 0f,
                        treasure.Definition.Weight);
                }
            }

            // Check for mined ore drops.
            for (int i = 0; i < hits.Length; i++)
            {
                MinedOreDrop oreDrop = hits[i].collider
                    .GetComponentInParent<MinedOreDrop>();
                if (oreDrop == null)
                {
                    continue;
                }

                string oreName = "Ore";
                float durability = 1f;
                IVoxelTerrain terrainForOre = Terrain;
                if (terrainForOre != null
                    && terrainForOre.VoxelTypeCatalog != null)
                {
                    VoxelTypeDefinition oreVoxelDef =
                        terrainForOre.VoxelTypeCatalog.Find(
                            oreDrop.VoxelType);
                    if (oreVoxelDef != null)
                    {
                        oreName = oreVoxelDef.DisplayName;
                        durability = oreVoxelDef.Durability;
                    }
                }

                float fragility = oreDrop.Valuable != null
                    ? oreDrop.Valuable.Fragility
                    : 0f;
                return new CrosshairLookAtInfo(
                    CrosshairTargetType.OreDrop,
                    oreName,
                    fragility,
                    oreDrop.Body != null
                        ? oreDrop.Body.mass
                        : 0f,
                    Mathf.RoundToInt(durability));
            }

            // Then check for terrain voxels.
            IVoxelTerrain terrain = Terrain;
            if (terrain == null)
            {
                return default;
            }

            Transform terrainTransform = terrain.TerrainTransform;
            for (int i = 0; i < hits.Length; i++)
            {
                Transform t = hits[i].transform;
                if (t != terrainTransform && !t.IsChildOf(terrainTransform))
                {
                    continue;
                }

                // Resolve the voxel coordinate using the 8-corner cell sample
                // approach (same as VoxelPlayerInteractor.TryResolveCellSample)
                // so all surface angles are covered, not just head-on hits.
                float voxelSize = terrain.VoxelSize;
                Vector3 samplePos = terrainTransform.InverseTransformPoint(
                    hits[i].point) / voxelSize;
                Vector3Int cellOrigin = new Vector3Int(
                    Mathf.FloorToInt(samplePos.x),
                    Mathf.FloorToInt(samplePos.y),
                    Mathf.FloorToInt(samplePos.z));

                InfiniteVoxelWorld world = terrain.World;
                if (world == null)
                {
                    continue;
                }

                VoxelTypeId foundType = VoxelTypeId.Air;
                bool foundSolid = false;
                for (int corner = 0; corner < CellCornerOffsets.Length; corner++)
                {
                    Vector3Int candidate = cellOrigin + CellCornerOffsets[corner];
                    if (!world.TryGetSample(
                            candidate.x,
                            candidate.y,
                            candidate.z,
                            out VoxelSample sample)
                        || !sample.IsSolid(terrain.IsoLevel))
                    {
                        continue;
                    }

                    foundType = sample.Type;
                    foundSolid = true;
                    break;
                }

                if (!foundSolid)
                {
                    continue;
                }

                VoxelTypeDefinition voxelDef = terrain.VoxelTypeCatalog != null
                    ? terrain.VoxelTypeCatalog.Find(foundType)
                    : null;
                if (voxelDef == null)
                {
                    continue;
                }

                return new CrosshairLookAtInfo(
                    CrosshairTargetType.Voxel,
                    voxelDef.DisplayName,
                    voxelDef.Durability);
            }

            return default;
        }

        private void ShowInfo(CrosshairLookAtInfo info)
        {
            if (NameLabel != null)
            {
                NameLabel.text = info.DisplayName;
            }

            if (StatsLabel != null)
            {
                if (info.TargetType == CrosshairTargetType.Treasure)
                {
                    StatsLabel.text = string.Format(
                        "FRAGILITY {0:P0}   /   WEIGHT {1:F1} kg",
                        info.FragilityOrDurability,
                        info.TotalWeight);
                }
                else if (info.TargetType == CrosshairTargetType.OreDrop)
                {
                    StatsLabel.text = string.Format(
                        "FRAGILITY {0:P0}   /   WEIGHT {1:F1} kg",
                        info.FragilityOrDurability,
                        info.TotalWeight);
                }
                else
                {
                    StatsLabel.text = string.Format(
                        "DURABILITY {0}",
                        Mathf.RoundToInt(info.FragilityOrDurability));
                }
            }

            currentType = info.TargetType;
            targetAlpha = 1f;
            hideCountdown = 0f;

            // Snap alpha on first frame of a new target to avoid lerp lag.
            if (!wasVisible)
            {
                visibilityAlpha = 1f;
                ApplyAlpha();
            }
        }

        private void StartHide()
        {
            if (hideCountdown <= 0f)
            {
                hideCountdown = HideDelay;
            }
        }

        private void UpdateAlpha(float deltaTime)
        {
            if (hideCountdown > 0f)
            {
                hideCountdown -= deltaTime;
                if (hideCountdown <= 0f)
                {
                    targetAlpha = 0f;
                }
            }

            float previous = visibilityAlpha;
            visibilityAlpha = Mathf.MoveTowards(
                visibilityAlpha,
                targetAlpha,
                AlphaLerpRate * deltaTime);

            if (!Mathf.Approximately(previous, visibilityAlpha))
            {
                ApplyAlpha();
            }

            wasVisible = visibilityAlpha > 0.01f;
        }

        private void ApplyAlpha()
        {
            if (RootCanvasGroup != null)
            {
                RootCanvasGroup.alpha = visibilityAlpha;
            }

            if (RootObject == null)
            {
                return;
            }

            bool shouldBeActive = visibilityAlpha > 0.001f;
            if (RootObject.activeSelf != shouldBeActive)
            {
                RootObject.SetActive(shouldBeActive);
            }
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

            PlayerProfile profile = viewCamera != null
                ? viewCamera.GetComponentInParent<PlayerProfile>()
                : null;
            interactionReach = profile != null
                ? profile.InteractionReach
                : 3f;
        }

        private void ResolveTerrain()
        {
            MonoBehaviour[] candidates = FindObjectsOfType<MonoBehaviour>();
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] is IVoxelTerrain terrain)
                {
                    terrainSource = candidates[i];
                    voxelTerrain = terrain;
                    return;
                }
            }
        }
    }
}
