using Supernova.MinecraftCaves;
using Supernova.Voxels;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Displays the generated mission spawn point in screen space and clamps it to the
    /// safe screen edge when it is outside the camera view.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class SpawnPointIndicator : MonoBehaviour
    {
        private const int CanvasSortingOrder = 102;

        [Header("Tracking")]
        [SerializeField, Min(0f)] private float hideDistance = 5f;
        [SerializeField, Min(0.05f)] private float sourceSearchInterval = 0.5f;

        [Header("Screen Placement")]
        [SerializeField, Min(0f)] private float edgePadding = 56f;

        private Canvas indicatorCanvas;
        private RectTransform canvasRect;
        private RectTransform markerRect;
        private RectTransform arrowRect;
        private TMP_Text distanceLabel;
        private Camera targetCamera;
        private Transform player;
        private SpawnPointSceneStructure spawnStructure;
        private MinecraftCaveInfiniteWorld caveWorld;
        private float nextSourceSearchTime;

        public float HideDistance => hideDistance;
        public bool IsVisible => markerRect != null && markerRect.gameObject.activeSelf;

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
            SetVisible(false);
        }

        private void OnEnable()
        {
            nextSourceSearchTime = 0f;
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        private void LateUpdate()
        {
            RefreshNow();
        }

        public void RefreshNow()
        {
            if (Time.unscaledTime >= nextSourceSearchTime)
            {
                nextSourceSearchTime =
                    Time.unscaledTime + sourceSearchInterval;
                ResolveTrackedObjects();
            }

            if (indicatorCanvas == null || canvasRect == null
                || markerRect == null || arrowRect == null
                || targetCamera == null || player == null
                || !TryGetSpawnPosition(out Vector3 spawnPosition))
            {
                SetVisible(false);
                return;
            }

            float distance = Vector3.Distance(player.position, spawnPosition);
            if (!Layout.ShouldShow(
                    player.position,
                    spawnPosition,
                    hideDistance))
            {
                SetVisible(false);
                return;
            }

            Rect screenBounds = GetScreenBounds(targetCamera);
            if (screenBounds.width <= 0f || screenBounds.height <= 0f)
            {
                SetVisible(false);
                return;
            }

            Vector3 projectedPoint =
                targetCamera.WorldToScreenPoint(spawnPosition);
            Placement placement =
                Layout.Calculate(projectedPoint, screenBounds, edgePadding);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    placement.ScreenPosition,
                    null,
                    out Vector2 localPosition))
            {
                SetVisible(false);
                return;
            }

            markerRect.anchoredPosition = localPosition;
            arrowRect.localRotation = Quaternion.Euler(
                0f,
                0f,
                Vector2.SignedAngle(Vector2.up, placement.Direction));
            distanceLabel.SetText("传送门\n{0:0}m", distance);
            SetVisible(true);
        }

        public void BindForTesting(
            Camera camera,
            Transform playerTransform,
            SpawnPointSceneStructure structure)
        {
            targetCamera = camera;
            player = playerTransform;
            spawnStructure = structure;
            caveWorld = null;
            nextSourceSearchTime = float.PositiveInfinity;
        }

        private void ResetTrackedObjects()
        {
            targetCamera = null;
            player = null;
            spawnStructure = null;
            caveWorld = null;
            nextSourceSearchTime = 0f;
            SetVisible(false);
        }

        private void ResolveTrackedObjects()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (player == null)
            {
                VoxelPlayerController playerController =
                    FindObjectOfType<VoxelPlayerController>();
                if (playerController != null)
                {
                    player = playerController.transform;
                }
                else if (targetCamera != null)
                {
                    CharacterController characterController =
                        targetCamera.GetComponentInParent<CharacterController>();
                    if (characterController != null)
                    {
                        player = characterController.transform;
                    }
                }
            }

            if (spawnStructure == null)
            {
                spawnStructure =
                    FindObjectOfType<SpawnPointSceneStructure>(true);
            }

            if (caveWorld == null)
            {
                caveWorld = FindObjectOfType<MinecraftCaveInfiniteWorld>();
            }
        }

        private bool TryGetSpawnPosition(out Vector3 spawnPosition)
        {
            if (spawnStructure != null
                && spawnStructure.PlayerSpawnPoint != null)
            {
                spawnPosition = spawnStructure.PlayerSpawnPoint.position;
                return true;
            }

            if (caveWorld != null && caveWorld.IsInitialLoadComplete)
            {
                spawnPosition = caveWorld.SpawnWorldPosition;
                return true;
            }

            spawnPosition = default;
            return false;
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
            {
                return cameraRect;
            }

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
                markerRect = transform.Find(
                    UiHierarchyPaths.SpawnIndicator.Marker) as RectTransform;
                arrowRect = transform.Find(
                    UiHierarchyPaths.SpawnIndicator.Arrow) as RectTransform;
                Transform distanceTransform = transform.Find(
                    UiHierarchyPaths.SpawnIndicator.Distance);
                distanceLabel = distanceTransform != null
                    ? distanceTransform.GetComponent<TMP_Text>()
                    : null;
            }

            if (indicatorCanvas != null && canvasRect != null
                && markerRect != null && arrowRect != null
                && distanceLabel != null)
            {
                return;
            }

            if (canvasTransform != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(canvasTransform.gameObject);
                }
                else
                {
                    DestroyImmediate(canvasTransform.gameObject);
                }
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
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            markerRect = CreateRect(
                UiHierarchyPaths.SpawnIndicator.MarkerName,
                canvasRect);
            SetAnchoredRect(
                markerRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(100f, 92f));

            arrowRect = CreateRect(
                UiHierarchyPaths.SpawnIndicator.ArrowName,
                markerRect);
            SetAnchoredRect(
                arrowRect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 20f),
                new Vector2(32f, 42f));
            SpawnPointArrowGraphic arrow =
                arrowRect.gameObject.AddComponent<SpawnPointArrowGraphic>();
            arrow.color = new Color(0.28f, 0.86f, 1f, 0.96f);
            arrow.raycastTarget = false;
            Outline arrowOutline = arrowRect.gameObject.AddComponent<Outline>();
            arrowOutline.effectColor = new Color(0f, 0.04f, 0.07f, 0.9f);
            arrowOutline.effectDistance = new Vector2(2f, -2f);
            arrowOutline.useGraphicAlpha = false;

            RectTransform labelRect = CreateRect(
                UiHierarchyPaths.SpawnIndicator.DistanceName,
                markerRect);
            SetAnchoredRect(
                labelRect,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                Vector2.zero,
                new Vector2(100f, 38f));
            TextMeshProUGUI label =
                labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = "传送门\n0m";
            label.fontSize = 14f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.9f, 0.97f, 1f, 1f);
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            Outline labelOutline = labelRect.gameObject.AddComponent<Outline>();
            labelOutline.effectColor = new Color(0f, 0.04f, 0.07f, 0.95f);
            labelOutline.effectDistance = new Vector2(1f, -1f);
            labelOutline.useGraphicAlpha = false;
            distanceLabel = label;
        }

        private void SetVisible(bool visible)
        {
            if (markerRect != null && markerRect.gameObject.activeSelf != visible)
            {
                markerRect.gameObject.SetActive(visible);
            }
        }

        private static RectTransform CreateRect(
            string objectName,
            Transform parent)
        {
            var child = new GameObject(objectName, typeof(RectTransform));
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
                Vector3 spawnPosition,
                float hiddenRadius)
            {
                return Vector3.Distance(playerPosition, spawnPosition)
                    > Mathf.Max(0f, hiddenRadius);
            }

            public static Placement Calculate(
                Vector3 projectedScreenPoint,
                Rect screenBounds,
                float edgePadding)
            {
                float maximumPadding = Mathf.Max(
                    0f,
                    Mathf.Min(screenBounds.width, screenBounds.height) * 0.5f
                    - 1f);
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
                {
                    direction = -direction;
                }
                if (direction.sqrMagnitude <= 0.0001f)
                {
                    direction = Vector2.down;
                }
                direction.Normalize();

                if (!isBehindCamera && paddedBounds.Contains(screenPosition))
                {
                    return new Placement(
                        screenPosition,
                        direction,
                        false);
                }

                float horizontalScale = Mathf.Abs(direction.x) > 0.0001f
                    ? paddedBounds.width * 0.5f / Mathf.Abs(direction.x)
                    : float.PositiveInfinity;
                float verticalScale = Mathf.Abs(direction.y) > 0.0001f
                    ? paddedBounds.height * 0.5f / Mathf.Abs(direction.y)
                    : float.PositiveInfinity;
                float edgeScale = Mathf.Min(
                    horizontalScale,
                    verticalScale);
                Vector2 edgePosition = center + direction * edgeScale;
                edgePosition.x = Mathf.Clamp(
                    edgePosition.x,
                    paddedBounds.xMin,
                    paddedBounds.xMax);
                edgePosition.y = Mathf.Clamp(
                    edgePosition.y,
                    paddedBounds.yMin,
                    paddedBounds.yMax);
                return new Placement(edgePosition, direction, true);
            }
        }
    }

    internal sealed class SpawnPointArrowGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect drawRect = rectTransform.rect;
            float halfWidth = drawRect.width * 0.5f;
            float halfHeight = drawRect.height * 0.5f;
            float shoulderY = drawRect.height * 0.02f;
            float shaftHalfWidth = drawRect.width * 0.14f;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;

            AddVertex(vertexHelper, vertex, 0f, halfHeight);
            AddVertex(vertexHelper, vertex, halfWidth, shoulderY);
            AddVertex(vertexHelper, vertex, -halfWidth, shoulderY);
            vertexHelper.AddTriangle(0, 1, 2);

            int shaftStart = vertexHelper.currentVertCount;
            AddVertex(
                vertexHelper,
                vertex,
                -shaftHalfWidth,
                shoulderY + 1f);
            AddVertex(
                vertexHelper,
                vertex,
                shaftHalfWidth,
                shoulderY + 1f);
            AddVertex(
                vertexHelper,
                vertex,
                shaftHalfWidth,
                -halfHeight);
            AddVertex(
                vertexHelper,
                vertex,
                -shaftHalfWidth,
                -halfHeight);
            vertexHelper.AddTriangle(
                shaftStart,
                shaftStart + 1,
                shaftStart + 2);
            vertexHelper.AddTriangle(
                shaftStart,
                shaftStart + 2,
                shaftStart + 3);
        }

        private static void AddVertex(
            VertexHelper vertexHelper,
            UIVertex vertex,
            float x,
            float y)
        {
            vertex.position = new Vector3(x, y);
            vertexHelper.AddVert(vertex);
        }
    }
}
