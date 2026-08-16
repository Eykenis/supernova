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

        /// <summary>
        /// Durability at or above this value reads as unbreakable; the crosshair
        /// shows only the bare "无法摧毁" state instead of a "硬度：" tier prefix.
        /// </summary>
        public const float IndestructibleDurability = 1000f;

        /// <summary>
        /// Maps a voxel durability to its on-screen crosshair tier line. Values
        /// below <see cref="IndestructibleDurability"/> render "硬度：{tier}",
        /// while 1000 and above render the bare "无法摧毁" state.
        /// </summary>
        public static string FormatDurabilityLabel(float durability)
        {
            if (durability >= IndestructibleDurability)
            {
                return "无法摧毁";
            }

            string tier = durability >= 100f ? "极高"
                : durability >= 60f ? "很高"
                : durability >= 35f ? "高"
                : durability >= 15f ? "中"
                : durability >= 5f ? "低"
                : "很低";

            return "硬度：" + tier;
        }

        /// <summary>
        /// Maps a fragile object's fragility (0-1, the fraction of damaging
        /// collision impulse converted into lost value) to its on-screen tier.
        /// Thresholds follow the percentage bands: 0-6% 极低, 7-15% 低,
        /// 16-29% 中, 30-49% 高, 50%+ 极高.
        /// </summary>
        public static string ResolveFragilityTier(float fragility)
        {
            float percentage = Mathf.Clamp01(fragility) * 100f;
            if (percentage >= 50f)
            {
                return "极高";
            }

            if (percentage >= 30f)
            {
                return "高";
            }

            if (percentage >= 16f)
            {
                return "中";
            }

            if (percentage >= 7f)
            {
                return "低";
            }

            return "极低";
        }

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

                string oreName = "已开采的矿物";
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
                        oreName = "已开采的" + oreVoxelDef.DisplayName;
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

            // Then resolve either fixed terrain or a detached voxel world. The
            // shared resolver filters dynamic compound boxes through the exact
            // visible Marching Cubes surface.
            IVoxelTerrain terrain = Terrain;
            if (!VoxelTargetResolver.TryRaycast(
                new Ray(origin, direction),
                interactionReach,
                raycastMask,
                terrain,
                out VoxelTargetHit voxelHit))
            {
                return default;
            }

            VoxelTypeId foundType = voxelHit.Sample.Type;
            VoxelTypeDefinition voxelDef = terrain != null
                && terrain.VoxelTypeCatalog != null
                    ? terrain.VoxelTypeCatalog.Find(foundType)
                    : null;
            if (voxelDef == null)
            {
                return new CrosshairLookAtInfo(
                    CrosshairTargetType.Voxel,
                    foundType.ToString(),
                    VoxelTypeUtility.ResolveDurability(foundType, null));
            }

            return new CrosshairLookAtInfo(
                CrosshairTargetType.Voxel,
                voxelDef.DisplayName,
                voxelDef.Durability);
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
                        "易碎程度：{0} / 重量：{1:F1} kg",
                        ResolveFragilityTier(info.FragilityOrDurability),
                        info.TotalWeight);
                }
                else if (info.TargetType == CrosshairTargetType.OreDrop)
                {
                    StatsLabel.text = string.Format(
                        "易碎程度：{0} / 重量：{1:F1} kg",
                        ResolveFragilityTier(info.FragilityOrDurability),
                        info.TotalWeight);
                }
                else
                {
                    StatsLabel.text = FormatDurabilityLabel(
                        info.FragilityOrDurability);
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
