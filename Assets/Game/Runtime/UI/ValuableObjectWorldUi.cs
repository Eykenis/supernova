using System.Collections.Generic;
using Supernova.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    [DisallowMultipleComponent]
    public sealed class ValuableObjectWorldUi : MonoBehaviour
    {
        private const float VerticalPadding = 0.25f;
        private const int MaximumPooledViews = 128;
        private static readonly Stack<PooledView> ViewPool =
            new Stack<PooledView>();

        private ValuableObject valuable;
        private RectTransform canvasRect;
        private Camera worldCamera;
        private Collider[] boundColliders = new Collider[0];
        private Renderer[] boundRenderers = new Renderer[0];

        public Canvas WorldCanvas { get; private set; }
        public TMP_Text ValueLabel { get; private set; }
        public ValueLossPopup LastLossPopup { get; private set; }
        public static int PooledViewCount => ViewPool.Count;

        private void Awake()
        {
            Bind(GetComponent<ValuableObject>());
        }

        private void OnEnable()
        {
            EnsureView();
            CacheBoundsSources();
            RefreshValue();
            UpdateWorldPose();
        }

        private void OnDisable()
        {
            ReleaseView();
        }

        private void LateUpdate()
        {
            if (valuable != null && WorldCanvas != null)
            {
                UpdateWorldPose();
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ReleaseView();
        }

        public void Bind(ValuableObject source)
        {
            if (valuable == source)
            {
                EnsureView();
                CacheBoundsSources();
                RefreshValue();
                UpdateWorldPose();
                return;
            }

            Unsubscribe();
            valuable = source;
            if (valuable != null)
            {
                valuable.ValueChanged += HandleValueChanged;
                valuable.ValueLost += HandleValueLost;
            }

            CacheBoundsSources();
            EnsureView();
            RefreshValue();
            UpdateWorldPose();
        }

        private void Unsubscribe()
        {
            if (valuable == null)
            {
                return;
            }
            valuable.ValueChanged -= HandleValueChanged;
            valuable.ValueLost -= HandleValueLost;
        }

        private void HandleValueChanged(int currentValue)
        {
            if (ValueLabel != null)
            {
                ValueLabel.text = "$" + currentValue;
            }
        }

        private void HandleValueLost(int lostValue, Vector3 collisionPoint)
        {
            if (lostValue <= 0)
            {
                return;
            }
            LastLossPopup = ValueLossPopup.Create(
                collisionPoint,
                lostValue,
                ResolveCamera());
        }

        private void RefreshValue()
        {
            if (ValueLabel != null)
            {
                ValueLabel.text = valuable != null
                    ? "$" + valuable.CurrentValue
                    : "$0";
            }
        }

        private void EnsureView()
        {
            if (WorldCanvas != null)
            {
                return;
            }

            while (ViewPool.Count > 0)
            {
                PooledView pooled = ViewPool.Pop();
                if (pooled.Canvas == null)
                {
                    continue;
                }
                AssignView(pooled);
                PrepareViewForUse();
                return;
            }

            var canvasObject = new GameObject(
                gameObject.name + " Value UI",
                typeof(RectTransform),
                typeof(Canvas));
            canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(180f, 44f);
            canvasRect.localScale =
                Vector3.one * WorldValueTextStyle.CanvasScale;

            WorldCanvas = canvasObject.GetComponent<Canvas>();
            WorldCanvas.renderMode = RenderMode.WorldSpace;
            WorldCanvas.overrideSorting = true;
            WorldCanvas.sortingOrder = 200;

            var labelObject = new GameObject(
                "Value",
                typeof(RectTransform),
                typeof(TextMeshProUGUI),
                typeof(Outline));
            RectTransform labelRect =
                labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(canvasRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label =
                labelObject.GetComponent<TextMeshProUGUI>();
            WorldValueTextStyle.ApplyValueLabel(
                label,
                WorldValueTextStyle.ValueColor);
            ValueLabel = label;
            PrepareViewForUse();
        }

        private void AssignView(PooledView pooled)
        {
            canvasRect = pooled.CanvasRect;
            WorldCanvas = pooled.Canvas;
            ValueLabel = pooled.Label;
        }

        private void PrepareViewForUse()
        {
            GameObject canvasObject = WorldCanvas.gameObject;
            canvasObject.name = gameObject.name + " Value UI";
            canvasObject.layer = gameObject.layer;
            if (ValueLabel != null)
            {
                ValueLabel.gameObject.layer = gameObject.layer;
            }
            canvasRect.sizeDelta = new Vector2(180f, 44f);
            canvasRect.localScale =
                Vector3.one * WorldValueTextStyle.CanvasScale;
            canvasObject.SetActive(true);
        }

        private void ReleaseView()
        {
            if (WorldCanvas == null)
            {
                ClearViewReferences();
                return;
            }

            GameObject canvasObject = WorldCanvas.gameObject;
            if (!Application.isPlaying)
            {
                DestroyImmediate(canvasObject);
                ClearViewReferences();
                return;
            }

            canvasObject.SetActive(false);
            if (ValueLabel != null)
            {
                ValueLabel.text = "$0";
            }
            var pooled = new PooledView(
                canvasRect,
                WorldCanvas,
                ValueLabel);
            ClearViewReferences();
            if (ViewPool.Count < MaximumPooledViews)
            {
                ViewPool.Push(pooled);
            }
            else
            {
                Destroy(canvasObject);
            }
        }

        private void ClearViewReferences()
        {
            canvasRect = null;
            WorldCanvas = null;
            ValueLabel = null;
            worldCamera = null;
            LastLossPopup = null;
        }

        private void CacheBoundsSources()
        {
            if (valuable == null)
            {
                boundColliders = new Collider[0];
                boundRenderers = new Renderer[0];
                return;
            }
            boundColliders =
                valuable.GetComponentsInChildren<Collider>(true);
            boundRenderers =
                valuable.GetComponentsInChildren<Renderer>(true);
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
                    bounds.max.y + VerticalPadding,
                    bounds.center.z);
            }
            else
            {
                canvasRect.position =
                    transform.position + Vector3.up * VerticalPadding;
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
                Collider collider = boundColliders[i];
                if (collider == null
                    || !collider.enabled
                    || collider.isTrigger)
                {
                    continue;
                }
                if (!found)
                {
                    bounds = collider.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
            if (found)
            {
                return true;
            }

            for (int i = 0; i < boundRenderers.Length; i++)
            {
                Renderer renderer = boundRenderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found;
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

        private readonly struct PooledView
        {
            public PooledView(
                RectTransform canvasRect,
                Canvas canvas,
                TMP_Text label)
            {
                CanvasRect = canvasRect;
                Canvas = canvas;
                Label = label;
            }

            public RectTransform CanvasRect { get; }
            public Canvas Canvas { get; }
            public TMP_Text Label { get; }
        }
    }
}
