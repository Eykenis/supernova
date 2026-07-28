using TMPro;
using UnityEngine;

namespace Supernova.UI
{
    /// <summary>
    /// A short-lived world-space damage number spawned at a collision point.
    /// It rises at a constant speed while fading out.
    /// </summary>
    public sealed class ValueLossPopup : MonoBehaviour
    {
        private const float CanvasScale = 0.005f;
        private const float DefaultLifetime = 1.25f;
        private const float DefaultRiseSpeed = 0.8f;

        private Camera worldCamera;
        private float lifetime = DefaultLifetime;
        private float riseSpeed = DefaultRiseSpeed;
        private float age;
        private Material overlayMaterial;

        public Canvas WorldCanvas { get; private set; }
        public TMP_Text Label { get; private set; }
        public float NormalizedAge =>
            lifetime > 0f ? Mathf.Clamp01(age / lifetime) : 1f;

        public static ValueLossPopup Create(
            Vector3 collisionPoint,
            int lostValue,
            Camera camera = null)
        {
            var popupObject = new GameObject(
                "Value Loss Popup",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(ValueLossPopup));
            RectTransform popupRect =
                popupObject.GetComponent<RectTransform>();
            popupRect.sizeDelta = new Vector2(180f, 44f);
            popupRect.localScale = Vector3.one * CanvasScale;
            popupRect.position = collisionPoint;

            Canvas canvas = popupObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 210;

            var labelObject = new GameObject(
                "Loss",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            RectTransform labelRect =
                labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(popupRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label =
                labelObject.GetComponent<TextMeshProUGUI>();
            label.text = $"-${Mathf.Max(0, lostValue)}";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 30f;
            label.fontStyle = FontStyles.Bold;
            label.color = WorldValueTextStyle.LossColor;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            popupObject.GetComponent<ValueLossPopup>()
                .ApplyOverlayMaterial(label);
            label.outlineColor = new Color32(0, 0, 0, 235);
            label.outlineWidth = 0.24f;

            ValueLossPopup popup =
                popupObject.GetComponent<ValueLossPopup>();
            popup.WorldCanvas = canvas;
            popup.Label = label;
            popup.worldCamera = camera;
            popup.FaceCamera();
            return popup;
        }

        private void OnDestroy()
        {
            if (overlayMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(overlayMaterial);
            }
            else
            {
                DestroyImmediate(overlayMaterial);
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Tick(float deltaTime)
        {
            if (Label == null)
            {
                return;
            }

            float safeDeltaTime = Mathf.Max(0f, deltaTime);
            age += safeDeltaTime;
            transform.position +=
                Vector3.up * (riseSpeed * safeDeltaTime);

            Color color = Label.color;
            color.a = 1f - NormalizedAge;
            Label.color = color;
            FaceCamera();

            if (age >= lifetime && Application.isPlaying)
            {
                Destroy(gameObject);
            }
        }

        private void FaceCamera()
        {
            if (worldCamera == null || !worldCamera.isActiveAndEnabled)
            {
                worldCamera = Camera.main;
            }

            if (worldCamera == null)
            {
                worldCamera = FindObjectOfType<Camera>();
            }

            if (worldCamera != null)
            {
                transform.rotation = worldCamera.transform.rotation;
            }
        }

        private void ApplyOverlayMaterial(TMP_Text label)
        {
            Shader overlayShader =
                Shader.Find("TextMeshPro/Distance Field Overlay");
            Material baseMaterial = label.fontSharedMaterial;
            if (overlayShader == null || baseMaterial == null)
            {
                return;
            }

            overlayMaterial = new Material(baseMaterial)
            {
                name = "Value Loss Overlay",
                shader = overlayShader
            };
            label.fontSharedMaterial = overlayMaterial;
        }
    }
}
