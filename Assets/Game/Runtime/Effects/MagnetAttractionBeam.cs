using Supernova.Gameplay;
using UnityEngine;

namespace Supernova.Effects
{
    /// <summary>
    /// Draws a curved, flowing beam between the player and whatever the magnet tool is
    /// currently holding. Purely visual: it never touches the held Rigidbody.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MagnetAttractionBeam : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private FirstPersonCartAttractor attractor;
        [SerializeField] private Camera viewCamera;

        [Header("Shape")]
        [SerializeField, Range(4, 64)] private int segments = 24;
        [SerializeField, Min(0f)] private float sag = 0.6f;
        [SerializeField, Min(0f)] private float waveAmplitude = 0.12f;
        [SerializeField, Min(0.01f)] private float waveFrequency = 2.5f;
        [SerializeField, Min(0f)] private float startWidth = 0.05f;
        [SerializeField, Min(0f)] private float endWidth = 0.14f;

        [Header("Flow")]
        [SerializeField, Min(0f)] private float flowSpeed = 2.5f;
        [SerializeField, Min(0.01f)] private float flowBandLength = 0.35f;
        [SerializeField] private Color baseColor = new Color(0.25f, 0.85f, 1f, 0.35f);
        [SerializeField] private Color flowColor = new Color(0.85f, 0.98f, 1f, 0.9f);

        private LineRenderer line;
        private Material material;
        private Gradient gradient;
        private GradientColorKey[] colorKeys;
        private GradientAlphaKey[] alphaKeys;
        private float flowPhase;

        private void Awake()
        {
            ResolveReferences();
            EnsureLine();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureLine();
        }

        private void OnDisable()
        {
            if (line != null) line.enabled = false;
        }

        private void LateUpdate()
        {
            ResolveReferences();
            EnsureLine();

            if (attractor == null || !attractor.IsHolding || attractor.HeldBody == null || viewCamera == null)
            {
                line.enabled = false;
                return;
            }

            line.enabled = true;
            flowPhase += Time.deltaTime * flowSpeed;

            Vector3 start = viewCamera.transform.position + viewCamera.transform.forward * 0.15f;
            Vector3 end = attractor.HeldBody.worldCenterOfMass;
            Vector3 chord = end - start;
            Vector3 up = Vector3.up;
            Vector3 side = Vector3.Cross(chord.normalized, up);
            if (side.sqrMagnitude < 0.0001f) side = viewCamera.transform.right;
            side.Normalize();

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 point = Vector3.Lerp(start, end, t);
                float sagAmount = Mathf.Sin(t * Mathf.PI) * sag;
                float wave = Mathf.Sin(t * waveFrequency * Mathf.PI * 2f - flowPhase) * waveAmplitude
                    * Mathf.Sin(t * Mathf.PI);
                point += up * -sagAmount + side * wave;
                line.SetPosition(i, point);
            }

            UpdateFlowColor();
        }

        private void UpdateFlowColor()
        {
            if (gradient == null)
            {
                gradient = new Gradient();
                colorKeys = new GradientColorKey[8];
                alphaKeys = new GradientAlphaKey[8];
            }

            float loopedPhase = Mathf.Repeat(flowPhase * 0.15f, 1f);
            for (int i = 0; i < colorKeys.Length; i++)
            {
                float t = (float)i / (colorKeys.Length - 1);
                float wrappedDistance = Mathf.Abs(t - loopedPhase);
                wrappedDistance = Mathf.Min(wrappedDistance, 1f - wrappedDistance);
                float intensity = 1f - Mathf.Clamp01(wrappedDistance / flowBandLength);
                Color color = Color.Lerp(baseColor, flowColor, intensity);
                colorKeys[i] = new GradientColorKey(color, t);
                alphaKeys[i] = new GradientAlphaKey(color.a, t);
            }

            gradient.SetKeys(colorKeys, alphaKeys);
            line.colorGradient = gradient;
        }

        private void ResolveReferences()
        {
            if (attractor == null) attractor = GetComponent<FirstPersonCartAttractor>();
            if (viewCamera == null && attractor != null) viewCamera = ResolveCameraFromAttractor();
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>(true);
        }

        private Camera ResolveCameraFromAttractor()
        {
            var perspectiveCamera = GetComponentInChildren<PerspectiveCameraController>(true);
            return perspectiveCamera != null ? perspectiveCamera.ControlledCamera : null;
        }

        private void EnsureLine()
        {
            if (line != null) return;

            line = GetComponent<LineRenderer>();
            if (line == null) line = gameObject.AddComponent<LineRenderer>();

            line.positionCount = segments + 1;
            line.useWorldSpace = true;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.widthCurve = AnimationCurve.Linear(0f, startWidth, 1f, endWidth);
            line.enabled = false;

            if (material == null)
            {
                // LineRenderer drives color via vertex colors (colorGradient); Sprites/Default
                // is the standard shader that actually samples vertex color, unlike URP Unlit.
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                material = shader != null ? new Material(shader) : null;
            }
            if (material != null) line.material = material;
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
