using Supernova.Gameplay;
using Supernova.Voxels;
using UnityEngine;
using UnityEngine.Serialization;

namespace Supernova.Effects
{
    /// <summary>
    /// Draws a stable energy arc between the player and whatever the magnet tool is
    /// currently holding. Purely visual: it never touches the held Rigidbody.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MagnetAttractionBeam : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private FirstPersonCartAttractor attractor;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Animator characterAnimator;
        [Tooltip("Optional explicit beam origin. Assign the first-person right-palm bone or a child anchor here.")]
        [SerializeField] private Transform rightPalmAnchor;

        private Transform rightHand;
        private Transform rightMiddleProximal;

        [Header("Shape")]
        [SerializeField, Range(4, 32)] private int segments = 16;
        [FormerlySerializedAs("sag")]
        [SerializeField, Min(0f)] private float arcHeight = 0.4f;
        [SerializeField, Min(0f)] private float startWidth = 0.035f;
        [SerializeField, Min(0f)] private float endWidth = 0.065f;

        [Header("Energy")]
        [FormerlySerializedAs("flowSpeed")]
        [SerializeField, Min(0f)] private float pulseSpeed = 1.5f;
        [SerializeField, Range(0f, 0.5f)] private float pulseStrength = 0.12f;
        [Tooltip("Normalized beam length used for the source-end color transition.")]
        [SerializeField, Range(0.01f, 0.5f)] private float startFadeLength = 0.12f;
        [FormerlySerializedAs("baseColor")]
        [SerializeField, ColorUsage(true, true)]
        private Color energyColor = new Color(0.08f, 0.95f, 0.48f, 0.65f);
        [FormerlySerializedAs("flowColor")]
        [SerializeField, ColorUsage(true, true)]
        private Color targetColor = new Color(0.55f, 1f, 0.7f, 0.95f);

        private LineRenderer line;
        private Material material;
        private Gradient gradient;
        private GradientColorKey[] colorKeys;
        private GradientAlphaKey[] alphaKeys;
        private float pulsePhase;

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

            if (attractor == null || !attractor.HasAttractionBeamTarget)
            {
                line.enabled = false;
                return;
            }

            line.enabled = true;
            pulsePhase = Mathf.Repeat(
                pulsePhase + Time.deltaTime * pulseSpeed * Mathf.PI * 2f,
                Mathf.PI * 2f);

            Vector3 start = ResolveBeamStart();
            Vector3 end = attractor.AttractionBeamTarget;

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                line.SetPosition(i, CalculateCurvePoint(start, end, t));
            }

            UpdateBeamColor();
        }

        private Vector3 CalculateCurvePoint(Vector3 start, Vector3 end, float t)
        {
            return Vector3.Lerp(start, end, t)
                + Vector3.up * CalculateArcHeight(t);
        }

        private float CalculateArcHeight(float t)
        {
            t = Mathf.Clamp01(t);
            return 4f * t * (1f - t) * arcHeight;
        }

        private void UpdateBeamColor()
        {
            if (gradient == null)
            {
                gradient = new Gradient();
                colorKeys = new GradientColorKey[3];
                alphaKeys = new GradientAlphaKey[4];
            }

            float pulse = 1f + Mathf.Sin(pulsePhase) * pulseStrength;
            Color pulsedEnergy = MultiplyRgb(energyColor, pulse);
            Color pulsedTarget = MultiplyRgb(targetColor, pulse);

            colorKeys[0] = new GradientColorKey(pulsedEnergy, 0f);
            colorKeys[1] = new GradientColorKey(pulsedEnergy, 0.82f);
            colorKeys[2] = new GradientColorKey(pulsedTarget, 1f);

            alphaKeys[0] = new GradientAlphaKey(
                Mathf.Clamp01(energyColor.a * pulse),
                0f);
            alphaKeys[1] = new GradientAlphaKey(
                Mathf.Clamp01(energyColor.a * pulse),
                startFadeLength);
            alphaKeys[2] = new GradientAlphaKey(
                Mathf.Clamp01(energyColor.a * pulse),
                0.82f);
            alphaKeys[3] = new GradientAlphaKey(
                Mathf.Clamp01(targetColor.a * pulse),
                1f);

            gradient.SetKeys(colorKeys, alphaKeys);
            line.colorGradient = gradient;
        }

        private static Color MultiplyRgb(Color color, float multiplier)
        {
            return new Color(
                color.r * multiplier,
                color.g * multiplier,
                color.b * multiplier,
                color.a);
        }

        private void ResolveReferences()
        {
            if (attractor == null) attractor = GetComponent<FirstPersonCartAttractor>();
            if (viewCamera == null && attractor != null) viewCamera = ResolveCameraFromAttractor();
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>(true);
            ResolveHandBones();
        }

        private void ResolveHandBones()
        {
            VoxelPlayerController player = GetComponent<VoxelPlayerController>();
            Animator resolvedAnimator = player != null ? player.CharacterAnimator : null;
            if (resolvedAnimator == null)
            {
                resolvedAnimator = GetComponentInChildren<Animator>(true);
            }

            if (resolvedAnimator != null && resolvedAnimator != characterAnimator)
            {
                characterAnimator = resolvedAnimator;
                rightHand = null;
                rightMiddleProximal = null;
            }

            if (characterAnimator == null || !characterAnimator.isHuman) return;

            if (rightHand == null)
            {
                rightHand = characterAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            }
            if (rightMiddleProximal == null)
            {
                rightMiddleProximal = characterAnimator.GetBoneTransform(
                    HumanBodyBones.RightMiddleProximal);
            }
        }

        private Vector3 ResolveBeamStart()
        {
            if (rightPalmAnchor != null)
            {
                return rightPalmAnchor.position;
            }

            if (rightHand != null && rightMiddleProximal != null)
            {
                // Humanoid hand bones originate at the wrist. Halfway towards the
                // middle-finger knuckle gives a stable point in the palm itself.
                return Vector3.Lerp(
                    rightHand.position,
                    rightMiddleProximal.position,
                    0.5f);
            }

            if (rightHand != null)
            {
                return rightHand.position;
            }

            if (viewCamera != null)
            {
                return viewCamera.transform.position + viewCamera.transform.forward * 0.15f;
            }

            return transform.position;
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
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
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
            if (material == null) return;
            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }
    }
}
