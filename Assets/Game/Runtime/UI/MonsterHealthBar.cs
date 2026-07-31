using Supernova.MinecraftCaves.Creatures;
using Supernova.Voxels;
using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Displays a billboard health bar above a monster while it is damaged or
    /// under the player's crosshair.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterHealthBar : MonoBehaviour
    {
        private const float CanvasScale = 0.005f;
        private const int CanvasSortingOrder = 210;
        private const float DefaultDamageVisibilitySeconds = 3f;
        private const float DefaultAimVisibilityGraceSeconds = 0.15f;
        private const float DefaultVerticalPadding = 0.25f;

        [SerializeField, Min(0f)] private float damageVisibilitySeconds =
            DefaultDamageVisibilitySeconds;
        [SerializeField, Min(0f)] private float aimVisibilityGraceSeconds =
            DefaultAimVisibilityGraceSeconds;
        [SerializeField, Min(0f)] private float verticalPadding =
            DefaultVerticalPadding;

        private CreatureBehaviorAgent monster;
        private RectTransform canvasRect;
        private RectTransform fillRect;
        private Camera worldCamera;
        private float visibleUntil;
        private Collider[] boundColliders = new Collider[0];
        private Renderer[] boundRenderers = new Renderer[0];

        public Canvas WorldCanvas { get; private set; }
        public Image BackgroundImage { get; private set; }
        public Image FillImage { get; private set; }
        public bool IsVisible =>
            WorldCanvas != null && WorldCanvas.gameObject.activeSelf;

        private void Awake()
        {
            EnsureView();
            Bind(GetComponent<CreatureBehaviorAgent>());
        }

        private void OnEnable()
        {
            Bind(GetComponent<CreatureBehaviorAgent>());
            SetVisible(false);
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        private void LateUpdate()
        {
            if (monster == null
                || !monster.isActiveAndEnabled
                || WorldCanvas == null)
            {
                SetVisible(false);
                return;
            }

            UpdateWorldPose();
            bool isAimed = MonsterCrosshairAimQuery.IsAimedAt(monster);
            if (isAimed)
            {
                visibleUntil = Mathf.Max(
                    visibleUntil,
                    Time.time + aimVisibilityGraceSeconds);
            }

            SetVisible(Time.time < visibleUntil);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (WorldCanvas == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(WorldCanvas.gameObject);
            }
            else
            {
                DestroyImmediate(WorldCanvas.gameObject);
            }
        }

        public void Bind(CreatureBehaviorAgent source)
        {
            if (monster == source)
            {
                EnsureView();
                RefreshHealth();
                UpdateWorldPose();
                return;
            }

            Unsubscribe();
            monster = source;
            CacheBoundsSources();
            if (monster != null)
            {
                monster.HealthChanged += HandleHealthChanged;
                monster.Damaged += HandleDamaged;
            }

            EnsureView();
            RefreshHealth();
            UpdateWorldPose();
        }

        private void Unsubscribe()
        {
            if (monster == null)
            {
                return;
            }

            monster.HealthChanged -= HandleHealthChanged;
            monster.Damaged -= HandleDamaged;
        }

        private void HandleHealthChanged(float current, float maximum)
        {
            SetHealth(current, maximum);
        }

        private void HandleDamaged(float amount, Vector3 point)
        {
            if (amount <= 0f)
            {
                return;
            }

            visibleUntil = Mathf.Max(
                visibleUntil,
                Time.time + damageVisibilitySeconds);
            SetVisible(true);
        }

        private void RefreshHealth()
        {
            SetHealth(
                monster != null ? monster.CurrentHealth : 0f,
                monster != null ? monster.MaximumHealth : 1f);
        }

        private void SetHealth(float current, float maximum)
        {
            float normalized = maximum > 0f
                ? Mathf.Clamp01(current / maximum)
                : 0f;
            if (fillRect != null)
            {
                Vector2 anchorMax = fillRect.anchorMax;
                anchorMax.x = normalized;
                fillRect.anchorMax = anchorMax;
            }

            if (FillImage != null)
            {
                FillImage.color = new Color(1f, 0.12f, 0.08f, 1f);
            }
        }

        private void EnsureView()
        {
            if (WorldCanvas != null)
            {
                return;
            }

            var canvasObject = new GameObject(
                $"{gameObject.name} Health Bar",
                typeof(RectTransform),
                typeof(Canvas));
            canvasObject.layer = gameObject.layer;
            canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(180f, 20f);
            canvasRect.localScale = Vector3.one * CanvasScale;

            WorldCanvas = canvasObject.GetComponent<Canvas>();
            WorldCanvas.renderMode = RenderMode.WorldSpace;
            WorldCanvas.overrideSorting = true;
            WorldCanvas.sortingOrder = CanvasSortingOrder;

            var backgroundObject = new GameObject(
                "Background",
                typeof(RectTransform),
                typeof(Image),
                typeof(Outline));
            backgroundObject.layer = gameObject.layer;
            RectTransform backgroundRect =
                backgroundObject.GetComponent<RectTransform>();
            backgroundRect.SetParent(canvasRect, false);
            StretchToParent(backgroundRect);
            BackgroundImage = backgroundObject.GetComponent<Image>();
            BackgroundImage.color = new Color(0.025f, 0.035f, 0.04f, 0.9f);
            BackgroundImage.raycastTarget = false;
            Outline outline = backgroundObject.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = false;

            var fillObject = new GameObject(
                "Fill",
                typeof(RectTransform),
                typeof(Image));
            fillObject.layer = gameObject.layer;
            fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.SetParent(backgroundRect, false);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(0f, 2f);
            fillRect.offsetMax = new Vector2(0f, -2f);
            FillImage = fillObject.GetComponent<Image>();
            FillImage.raycastTarget = false;

            RefreshHealth();
            SetVisible(false);
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void SetVisible(bool visible)
        {
            if (WorldCanvas != null
                && WorldCanvas.gameObject.activeSelf != visible)
            {
                WorldCanvas.gameObject.SetActive(visible);
            }
        }

        private void UpdateWorldPose()
        {
            if (canvasRect == null)
            {
                return;
            }

            if (TryGetWorldBounds(out Bounds bounds))
            {
                canvasRect.position = new Vector3(
                    bounds.center.x,
                    bounds.max.y + verticalPadding,
                    bounds.center.z);
            }
            else
            {
                canvasRect.position =
                    transform.position + Vector3.up * verticalPadding;
            }

            Camera camera = ResolveCamera();
            if (camera != null)
            {
                canvasRect.rotation = camera.transform.rotation;
            }
        }

        private bool TryGetWorldBounds(out Bounds bounds)
        {
            bool found = false;
            bounds = new Bounds(transform.position, Vector3.zero);

            for (int i = 0; i < boundColliders.Length; i++)
            {
                Collider candidate = boundColliders[i];
                if (candidate == null
                    || !candidate.enabled
                    || candidate.isTrigger)
                {
                    continue;
                }

                Encapsulate(candidate.bounds, ref bounds, ref found);
            }

            if (found)
            {
                return true;
            }

            for (int i = 0; i < boundRenderers.Length; i++)
            {
                Renderer candidate = boundRenderers[i];
                if (candidate != null && candidate.enabled)
                {
                    Encapsulate(candidate.bounds, ref bounds, ref found);
                }
            }

            return found;
        }

        private void CacheBoundsSources()
        {
            boundColliders = GetComponentsInChildren<Collider>(true);
            boundRenderers = GetComponentsInChildren<Renderer>(true);
        }

        private static void Encapsulate(
            Bounds candidate,
            ref Bounds bounds,
            ref bool found)
        {
            if (!found)
            {
                bounds = candidate;
                found = true;
            }
            else
            {
                bounds.Encapsulate(candidate);
            }
        }

        private Camera ResolveCamera()
        {
            if (worldCamera == null || !worldCamera.isActiveAndEnabled)
            {
                worldCamera = Camera.main;
            }

            if (worldCamera == null)
            {
                worldCamera = FindObjectOfType<Camera>();
            }

            return worldCamera;
        }
    }

    /// <summary>
    /// Shares one crosshair ray query between all monster health bars each frame.
    /// </summary>
    internal static class MonsterCrosshairAimQuery
    {
        private const int MaximumRaycastHits = 64;
        private static readonly RaycastHit[] Hits =
            new RaycastHit[MaximumRaycastHits];

        private static int lastQueryFrame = -1;
        private static CreatureBehaviorAgent aimedMonster;
        private static Camera viewCamera;

        public static bool IsAimedAt(CreatureBehaviorAgent monster)
        {
            if (lastQueryFrame != Time.frameCount)
            {
                lastQueryFrame = Time.frameCount;
                aimedMonster = FindAimedMonster();
            }

            return aimedMonster == monster;
        }

        private static CreatureBehaviorAgent FindAimedMonster()
        {
            Camera camera = ResolveCamera();
            if (camera == null)
            {
                return null;
            }

            int hitCount = Physics.RaycastNonAlloc(
                new Ray(camera.transform.position, camera.transform.forward),
                Hits,
                Mathf.Max(0.01f, camera.farClipPlane),
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            float nearestDistance = float.PositiveInfinity;
            Collider nearestCollider = null;
            for (int i = 0; i < hitCount; i++)
            {
                Collider candidate = Hits[i].collider;
                if (candidate == null
                    || candidate.GetComponentInParent<VoxelPlayerController>()
                        != null
                    || Hits[i].distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = Hits[i].distance;
                nearestCollider = candidate;
            }

            return nearestCollider != null
                ? nearestCollider.GetComponentInParent<CreatureBehaviorAgent>()
                : null;
        }

        private static Camera ResolveCamera()
        {
            if (viewCamera == null || !viewCamera.isActiveAndEnabled)
            {
                viewCamera = Camera.main;
            }

            if (viewCamera == null)
            {
                viewCamera = Object.FindObjectOfType<Camera>();
            }

            return viewCamera;
        }
    }
}
