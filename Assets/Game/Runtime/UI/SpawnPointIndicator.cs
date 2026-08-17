using System.Collections.Generic;
using Supernova.Gameplay;
using Supernova.PortalExample;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Displays the initial checkpoint portal and every player-created portal in
    /// screen space. Markers clamp to the safe screen edge without rotating.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class SpawnPointIndicator : MonoBehaviour
    {
        private const int CanvasSortingOrder = 102;

        [Header("Tracking")]
        [SerializeField, Min(0f)] private float hideDistance = 5f;
        [SerializeField, Min(0f)] private float fadeStartDistance = 35f;
        [SerializeField, Min(0f)] private float invisibleDistance = 150f;

        [Header("Screen Placement")]
        [SerializeField, Min(0f)] private float edgePadding = 42f;

        private readonly Dictionary<PortalExampleGate, MarkerView> markers =
            new Dictionary<PortalExampleGate, MarkerView>();
        private readonly List<PortalExampleGate> stalePortals =
            new List<PortalExampleGate>();

        private Canvas indicatorCanvas;
        private RectTransform canvasRect;
        private RectTransform markerTemplate;
        private TMP_Text templateChevron;
        private TMP_Text templateDistanceLabel;
        private Camera targetCamera;
        private Transform player;
        private DenseJigsawPortalBridge portalBridge;

        public float HideDistance => hideDistance;
        public float FadeStartDistance => fadeStartDistance;
        public float InvisibleDistance => invisibleDistance;
        public bool IsVisible
        {
            get
            {
                foreach (MarkerView marker in markers.Values)
                {
                    if (marker.Root != null && marker.Root.gameObject.activeSelf)
                        return true;
                }
                return false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRuntimeIndicator()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SpawnPointIndicator indicator =
                FindObjectOfType<SpawnPointIndicator>(true);
            if (indicator == null)
            {
                var indicatorObject =
                    new GameObject(UiHierarchyPaths.SpawnIndicator.RuntimeRoot);
                DontDestroyOnLoad(indicatorObject);
                indicator = indicatorObject.AddComponent<SpawnPointIndicator>();
            }

            indicator.ResetTrackedObjects();
        }

        private void Awake()
        {
            EnsureView();
            SetAllMarkersVisible(false);
        }

        private void OnEnable()
        {
            DenseJigsawPortalBridge.InstanceEnabled -=
                HandlePortalBridgeEnabled;
            DenseJigsawPortalBridge.InstanceEnabled +=
                HandlePortalBridgeEnabled;
            DenseJigsawPortalBridge.InstanceDisabled -=
                HandlePortalBridgeDisabled;
            DenseJigsawPortalBridge.InstanceDisabled +=
                HandlePortalBridgeDisabled;
            PlayerToolController.InstanceEnabled -=
                HandlePlayerToolControllerEnabled;
            PlayerToolController.InstanceEnabled +=
                HandlePlayerToolControllerEnabled;
            PlayerToolController.InstanceDisabled -=
                HandlePlayerToolControllerDisabled;
            PlayerToolController.InstanceDisabled +=
                HandlePlayerToolControllerDisabled;
            ResolveTrackedObjects();
        }

        private void OnDisable()
        {
            DenseJigsawPortalBridge.InstanceEnabled -=
                HandlePortalBridgeEnabled;
            DenseJigsawPortalBridge.InstanceDisabled -=
                HandlePortalBridgeDisabled;
            PlayerToolController.InstanceEnabled -=
                HandlePlayerToolControllerEnabled;
            PlayerToolController.InstanceDisabled -=
                HandlePlayerToolControllerDisabled;
            BindPortalBridge(null);
            SetAllMarkersVisible(false);
        }

        private void LateUpdate()
        {
            RefreshNow();
        }

        public void RefreshNow()
        {
            PruneDestroyedPortals();
            if (indicatorCanvas == null
                || canvasRect == null
                || targetCamera == null
                || player == null
                || markers.Count == 0)
            {
                SetAllMarkersVisible(false);
                return;
            }

            Rect screenBounds = GetScreenBounds(targetCamera);
            if (screenBounds.width <= 0f || screenBounds.height <= 0f)
            {
                SetAllMarkersVisible(false);
                return;
            }

            foreach (KeyValuePair<PortalExampleGate, MarkerView> pair
                in markers)
            {
                PortalExampleGate portal = pair.Key;
                MarkerView marker = pair.Value;
                if (portal == null || !portal.isActiveAndEnabled)
                {
                    marker.SetVisible(false);
                    continue;
                }

                Vector3 portalPosition = portal.transform.position;
                float distance = Vector3.Distance(
                    player.position,
                    portalPosition);
                float alpha = Layout.CalculateDistanceAlpha(
                    distance,
                    fadeStartDistance,
                    invisibleDistance);
                if (!Layout.ShouldShow(
                        player.position,
                        portalPosition,
                        hideDistance)
                    || alpha <= 0f)
                {
                    marker.SetVisible(false);
                    continue;
                }

                Vector3 projectedPoint =
                    targetCamera.WorldToScreenPoint(portalPosition);
                Placement placement =
                    Layout.Calculate(projectedPoint, screenBounds, edgePadding);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRect,
                        placement.ScreenPosition,
                        null,
                        out Vector2 localPosition))
                {
                    marker.SetVisible(false);
                    continue;
                }

                marker.Root.anchoredPosition = localPosition;
                marker.Root.localRotation = Quaternion.identity;
                marker.Chevron.rectTransform.localRotation =
                    Quaternion.identity;
                marker.DistanceLabel.SetText(
                    "传送门\n{0:0}m",
                    distance);
                marker.CanvasGroup.alpha = alpha;
                marker.SetVisible(true);
            }
        }

        private void ResetTrackedObjects()
        {
            targetCamera = null;
            player = null;
            BindPortalBridge(null);
            SetAllMarkersVisible(false);
            ResolveTrackedObjects();
        }

        private void ResolveTrackedObjects()
        {
            targetCamera = Camera.main;
            PlayerToolController playerTools = targetCamera != null
                ? targetCamera.GetComponentInParent<
                    PlayerToolController>(true)
                : null;
            if (playerTools == null)
                playerTools = FindObjectOfType<PlayerToolController>();
            player = playerTools != null
                ? playerTools.transform
                : targetCamera != null
                    ? targetCamera.transform.root
                    : null;

            BindPortalBridge(
                FindObjectOfType<DenseJigsawPortalBridge>());
        }

        private void BindPortalBridge(DenseJigsawPortalBridge source)
        {
            if (portalBridge != null)
                portalBridge.PortalAdded -= HandlePortalAdded;

            portalBridge = source;
            ClearPortalMarkers();
            if (portalBridge == null)
                return;

            portalBridge.PortalAdded += HandlePortalAdded;
            AddPortal(portalBridge.CheckpointGate);
            IReadOnlyList<PortalExampleGate> spawned =
                portalBridge.SpawnedCheckpointGates;
            for (int i = 0; i < spawned.Count; i++)
                AddPortal(spawned[i]);
        }

        private void HandlePortalBridgeEnabled(
            DenseJigsawPortalBridge source)
        {
            BindPortalBridge(source);
        }

        private void HandlePortalBridgeDisabled(
            DenseJigsawPortalBridge source)
        {
            if (source == portalBridge)
                BindPortalBridge(null);
        }

        private void HandlePlayerToolControllerEnabled(
            PlayerToolController source)
        {
            ResolveTrackedObjects();
        }

        private void HandlePlayerToolControllerDisabled(
            PlayerToolController source)
        {
            if (source != null && source.transform == player)
            {
                targetCamera = null;
                player = null;
                SetAllMarkersVisible(false);
            }
        }

        private void HandlePortalAdded(PortalExampleGate portal)
        {
            AddPortal(portal);
        }

        private void AddPortal(PortalExampleGate portal)
        {
            if (portal == null || markers.ContainsKey(portal))
                return;
            markers.Add(portal, CreateMarker());
        }

        private MarkerView CreateMarker()
        {
            GameObject markerObject = Instantiate(
                markerTemplate.gameObject,
                canvasRect,
                false);
            markerObject.name =
                UiHierarchyPaths.SpawnIndicator.RuntimeMarkerName;
            RectTransform root =
                markerObject.GetComponent<RectTransform>();
            CanvasGroup canvasGroup =
                markerObject.GetComponent<CanvasGroup>();
            TMP_Text chevron = root.Find(
                UiHierarchyPaths.SpawnIndicator.ChevronName)
                .GetComponent<TMP_Text>();
            TMP_Text label = root.Find(
                UiHierarchyPaths.SpawnIndicator.DistanceName)
                .GetComponent<TMP_Text>();
            var marker = new MarkerView(
                root,
                canvasGroup,
                chevron,
                label);
            marker.SetVisible(false);
            return marker;
        }

        private void PruneDestroyedPortals()
        {
            stalePortals.Clear();
            foreach (KeyValuePair<PortalExampleGate, MarkerView> pair
                in markers)
            {
                if (pair.Key == null)
                    stalePortals.Add(pair.Key);
            }

            for (int i = 0; i < stalePortals.Count; i++)
            {
                PortalExampleGate portal = stalePortals[i];
                if (!markers.TryGetValue(portal, out MarkerView marker))
                    continue;
                DestroyMarkerObject(marker.Root);
                markers.Remove(portal);
            }
            stalePortals.Clear();
        }

        private void ClearPortalMarkers()
        {
            foreach (MarkerView marker in markers.Values)
                DestroyMarkerObject(marker.Root);
            markers.Clear();
            stalePortals.Clear();
        }

        private void SetAllMarkersVisible(bool visible)
        {
            foreach (MarkerView marker in markers.Values)
                marker.SetVisible(visible);
        }

        private static void DestroyMarkerObject(RectTransform marker)
        {
            if (marker == null)
                return;
            if (Application.isPlaying)
                Destroy(marker.gameObject);
            else
                DestroyImmediate(marker.gameObject);
        }

        private static Rect GetScreenBounds(Camera camera)
        {
            Rect cameraRect = camera.pixelRect;
            Rect safeArea = Screen.safeArea;
            float minimumX = Mathf.Max(cameraRect.xMin, safeArea.xMin);
            float minimumY = Mathf.Max(cameraRect.yMin, safeArea.yMin);
            float maximumX = Mathf.Min(cameraRect.xMax, safeArea.xMax);
            float maximumY = Mathf.Min(cameraRect.yMax, safeArea.yMax);
            if (maximumX <= minimumX || maximumY <= minimumY)
                return cameraRect;

            return Rect.MinMaxRect(
                minimumX,
                minimumY,
                maximumX,
                maximumY);
        }

        private void EnsureView()
        {
            Transform canvasTransform =
                transform.Find(UiHierarchyPaths.SpawnIndicator.Canvas);
            if (canvasTransform != null)
            {
                indicatorCanvas = canvasTransform.GetComponent<Canvas>();
                canvasRect = canvasTransform as RectTransform;
                markerTemplate = transform.Find(
                    UiHierarchyPaths.SpawnIndicator.Marker)
                    as RectTransform;
                Transform chevronTransform = transform.Find(
                    UiHierarchyPaths.SpawnIndicator.Chevron);
                templateChevron = chevronTransform != null
                    ? chevronTransform.GetComponent<TMP_Text>()
                    : null;
                Transform distanceTransform = transform.Find(
                    UiHierarchyPaths.SpawnIndicator.Distance);
                templateDistanceLabel = distanceTransform != null
                    ? distanceTransform.GetComponent<TMP_Text>()
                    : null;
            }

            if (indicatorCanvas != null
                && canvasRect != null
                && markerTemplate != null
                && templateChevron != null
                && templateDistanceLabel != null)
            {
                markerTemplate.gameObject.SetActive(false);
                return;
            }

            if (canvasTransform != null)
            {
                if (Application.isPlaying)
                    Destroy(canvasTransform.gameObject);
                else
                    DestroyImmediate(canvasTransform.gameObject);
            }

            BuildView();
        }

        private void BuildView()
        {
            canvasRect = CreateRect(
                UiHierarchyPaths.SpawnIndicator.CanvasName,
                transform);
            indicatorCanvas = canvasRect.gameObject.AddComponent<Canvas>();
            indicatorCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            indicatorCanvas.sortingOrder = CanvasSortingOrder;

            CanvasScaler scaler =
                canvasRect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            markerTemplate = CreateRect(
                UiHierarchyPaths.SpawnIndicator.MarkerName,
                canvasRect);
            SetAnchoredRect(
                markerTemplate,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(88f, 54f));
            markerTemplate.gameObject.AddComponent<CanvasGroup>();

            RectTransform chevronRect = CreateRect(
                UiHierarchyPaths.SpawnIndicator.ChevronName,
                markerTemplate);
            SetAnchoredRect(
                chevronRect,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                Vector2.zero,
                new Vector2(24f, 18f));
            TextMeshProUGUI chevron =
                chevronRect.gameObject.AddComponent<TextMeshProUGUI>();
            chevron.text = "▼";
            chevron.fontSize = 15f;
            chevron.fontStyle = FontStyles.Bold;
            chevron.alignment = TextAlignmentOptions.Center;
            chevron.color = new Color(0.28f, 0.86f, 1f, 0.96f);
            chevron.enableWordWrapping = false;
            chevron.raycastTarget = false;
            Outline chevronOutline =
                chevronRect.gameObject.AddComponent<Outline>();
            chevronOutline.effectColor =
                new Color(0f, 0.04f, 0.07f, 0.9f);
            chevronOutline.effectDistance = new Vector2(1f, -1f);
            chevronOutline.useGraphicAlpha = false;
            templateChevron = chevron;

            RectTransform labelRect = CreateRect(
                UiHierarchyPaths.SpawnIndicator.DistanceName,
                markerTemplate);
            SetAnchoredRect(
                labelRect,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                Vector2.zero,
                new Vector2(88f, 34f));
            TextMeshProUGUI label =
                labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = "传送门\n0m";
            label.fontSize = 12f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.9f, 0.97f, 1f, 1f);
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            Outline labelOutline =
                labelRect.gameObject.AddComponent<Outline>();
            labelOutline.effectColor =
                new Color(0f, 0.04f, 0.07f, 0.95f);
            labelOutline.effectDistance = new Vector2(1f, -1f);
            labelOutline.useGraphicAlpha = false;
            templateDistanceLabel = label;
            markerTemplate.gameObject.SetActive(false);
        }

        private void OnValidate()
        {
            hideDistance = Mathf.Max(0f, hideDistance);
            fadeStartDistance = Mathf.Max(
                hideDistance,
                fadeStartDistance);
            invisibleDistance = Mathf.Max(
                fadeStartDistance + 0.01f,
                invisibleDistance);
            edgePadding = Mathf.Max(0f, edgePadding);
        }

        private static RectTransform CreateRect(
            string objectName,
            Transform parent)
        {
            var child = new GameObject(
                objectName,
                typeof(RectTransform));
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void SetAnchoredRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private sealed class MarkerView
        {
            public MarkerView(
                RectTransform root,
                CanvasGroup canvasGroup,
                TMP_Text chevron,
                TMP_Text distanceLabel)
            {
                Root = root;
                CanvasGroup = canvasGroup;
                Chevron = chevron;
                DistanceLabel = distanceLabel;
            }

            public RectTransform Root { get; }
            public CanvasGroup CanvasGroup { get; }
            public TMP_Text Chevron { get; }
            public TMP_Text DistanceLabel { get; }

            public void SetVisible(bool visible)
            {
                if (Root != null
                    && Root.gameObject.activeSelf != visible)
                {
                    Root.gameObject.SetActive(visible);
                }
            }
        }

        public readonly struct Placement
        {
            public Placement(
                Vector2 screenPosition,
                Vector2 direction,
                bool isClamped)
            {
                ScreenPosition = screenPosition;
                Direction = direction;
                IsClamped = isClamped;
            }

            public Vector2 ScreenPosition { get; }
            public Vector2 Direction { get; }
            public bool IsClamped { get; }
        }

        public static class Layout
        {
            public static bool ShouldShow(
                Vector3 playerPosition,
                Vector3 targetPosition,
                float hiddenRadius)
            {
                return Vector3.Distance(
                    playerPosition,
                    targetPosition)
                    > Mathf.Max(0f, hiddenRadius);
            }

            public static float CalculateDistanceAlpha(
                float distance,
                float fadeStart,
                float invisibleAt)
            {
                float safeFadeStart = Mathf.Max(0f, fadeStart);
                float safeInvisibleAt = Mathf.Max(
                    safeFadeStart + 0.01f,
                    invisibleAt);
                if (distance <= safeFadeStart)
                    return 1f;
                if (distance >= safeInvisibleAt)
                    return 0f;
                return 1f - Mathf.InverseLerp(
                    safeFadeStart,
                    safeInvisibleAt,
                    distance);
            }

            public static Placement Calculate(
                Vector3 projectedScreenPoint,
                Rect screenBounds,
                float edgePadding)
            {
                float maximumPadding = Mathf.Max(
                    0f,
                    Mathf.Min(
                        screenBounds.width,
                        screenBounds.height) * 0.5f - 1f);
                float padding = Mathf.Clamp(
                    edgePadding,
                    0f,
                    maximumPadding);
                Rect paddedBounds = Rect.MinMaxRect(
                    screenBounds.xMin + padding,
                    screenBounds.yMin + padding,
                    screenBounds.xMax - padding,
                    screenBounds.yMax - padding);
                Vector2 center = screenBounds.center;
                var screenPosition = new Vector2(
                    projectedScreenPoint.x,
                    projectedScreenPoint.y);
                bool isBehindCamera = projectedScreenPoint.z <= 0f;
                Vector2 direction = screenPosition - center;
                if (isBehindCamera)
                    direction = -direction;
                if (direction.sqrMagnitude <= 0.0001f)
                    direction = Vector2.down;
                direction.Normalize();

                if (!isBehindCamera
                    && paddedBounds.Contains(screenPosition))
                {
                    return new Placement(
                        screenPosition,
                        direction,
                        false);
                }

                float horizontalScale =
                    Mathf.Abs(direction.x) > 0.0001f
                        ? paddedBounds.width * 0.5f
                            / Mathf.Abs(direction.x)
                        : float.PositiveInfinity;
                float verticalScale =
                    Mathf.Abs(direction.y) > 0.0001f
                        ? paddedBounds.height * 0.5f
                            / Mathf.Abs(direction.y)
                        : float.PositiveInfinity;
                float edgeScale = Mathf.Min(
                    horizontalScale,
                    verticalScale);
                Vector2 edgePosition =
                    center + direction * edgeScale;
                edgePosition.x = Mathf.Clamp(
                    edgePosition.x,
                    paddedBounds.xMin,
                    paddedBounds.xMax);
                edgePosition.y = Mathf.Clamp(
                    edgePosition.y,
                    paddedBounds.yMin,
                    paddedBounds.yMax);
                return new Placement(
                    edgePosition,
                    direction,
                    true);
            }
        }
    }
}

