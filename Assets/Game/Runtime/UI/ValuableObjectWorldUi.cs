using Supernova.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Keeps a valuable object's current value readable above its world bounds.
    /// The generated canvas is not parented to the object so model scale and
    /// rotation cannot distort the label.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ValuableObjectWorldUi : MonoBehaviour
    {
        private const float VerticalPadding = 0.25f;

        private ValuableObject valuable;
        private RectTransform canvasRect;
        private Camera worldCamera;

        public Canvas WorldCanvas { get; private set; }
        public TMP_Text ValueLabel { get; private set; }
        public ValueLossPopup LastLossPopup { get; private set; }

        private void Awake()
        {
            EnsureView();
            Bind(GetComponent<ValuableObject>());
        }

        private void OnEnable()
        {
            if (WorldCanvas != null)
            {
                WorldCanvas.gameObject.SetActive(true);
            }
        }

        private void OnDisable()
        {
            if (WorldCanvas != null)
            {
                WorldCanvas.gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (valuable == null || WorldCanvas == null)
            {
                return;
            }

            UpdateWorldPose();
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

        public void Bind(ValuableObject source)
        {
            if (valuable == source)
            {
                EnsureView();
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
                ValueLabel.text = $"${currentValue}";
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
                    ? $"${valuable.CurrentValue}"
                    : "$0";
            }
        }

        private void EnsureView()
        {
            if (WorldCanvas != null)
            {
                return;
            }

            var canvasObject = new GameObject(
                $"{gameObject.name} Value UI",
                typeof(RectTransform),
                typeof(Canvas));
            canvasObject.layer = gameObject.layer;
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
            labelObject.layer = gameObject.layer;
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
        }

        private void UpdateWorldPose()
        {
            if (canvasRect == null)
            {
                return;
            }

            Bounds bounds;
            if (TryGetWorldBounds(out bounds))
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

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (!collider.enabled || collider.isTrigger)
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

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!renderer.enabled)
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
    }
}
