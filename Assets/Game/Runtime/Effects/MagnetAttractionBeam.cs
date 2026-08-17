using Supernova.Gameplay;
using Supernova.Voxels;
using UnityEngine;
using UnityEngine.Serialization;

namespace Supernova.Effects
{
    /// <summary>
    /// Draws a layered, rune-like energy tether between the player's palm and the
    /// current magnet target. The effect is visual only and never touches physics.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MagnetAttractionBeam : MonoBehaviour
    {
        private const float Tau = Mathf.PI * 2f;
        private const int TargetRingSegments = 48;

        private static readonly int EnergyColorId =
            Shader.PropertyToID("_EnergyColor");
        private static readonly int HotColorId =
            Shader.PropertyToID("_HotColor");
        private static readonly int PhaseId = Shader.PropertyToID("_Phase");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int BandDensityId =
            Shader.PropertyToID("_BandDensity");
        private static readonly int PatternStrengthId =
            Shader.PropertyToID("_PatternStrength");
        private static readonly int EdgePowerId =
            Shader.PropertyToID("_EdgePower");
        private static readonly int ParticleModeId =
            Shader.PropertyToID("_ParticleMode");

        [Header("Source")]
        [SerializeField] private FirstPersonMagnetInteractor attractor;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Animator characterAnimator;
        [Tooltip("Optional explicit beam origin. Assign the first-person right-palm bone or a child anchor here.")]
        [SerializeField] private Transform rightPalmAnchor;

        private Transform rightHand;
        private Transform rightMiddleProximal;

        [Header("Shape")]
        [SerializeField, Range(12, 96)] private int segments = 48;
        [FormerlySerializedAs("sag")]
        [SerializeField, Min(0f)] private float arcHeight = 0.4f;
        [SerializeField, Min(0f)] private float startWidth = 0.045f;
        [SerializeField, Min(0f)] private float endWidth = 0.09f;
        [SerializeField, Range(1f, 8f)] private float auraWidthMultiplier = 3.4f;
        [SerializeField, Min(0f)] private float helixRadius = 0.13f;
        [SerializeField, Range(0.5f, 8f)] private float helixTurns = 3.25f;
        [SerializeField, Min(0f)] private float helixWidth = 0.018f;

        [Header("Energy")]
        [SerializeField] private Material beamMaterial;
        [FormerlySerializedAs("flowSpeed")]
        [SerializeField, Min(0f)] private float pulseSpeed = 1.8f;
        [SerializeField, Range(0f, 0.5f)] private float pulseStrength = 0.14f;
        [SerializeField, Min(0f)] private float twistSpeed = 0.8f;
        [Tooltip("Normalized length used to blend from palm energy into the target color.")]
        [SerializeField, Range(0.01f, 0.5f)] private float startFadeLength = 0.12f;
        [FormerlySerializedAs("baseColor")]
        [SerializeField, ColorUsage(true, true)]
        private Color energyColor = new Color(0.035f, 1.25f, 0.42f, 0.72f);
        [FormerlySerializedAs("flowColor")]
        [SerializeField, ColorUsage(true, true)]
        private Color targetColor = new Color(0.62f, 1.65f, 0.72f, 0.96f);

        [Header("Load Feedback")]
        [SerializeField, Min(0.01f)] private float weightColorResponse = 9f;
        [SerializeField, ColorUsage(true, true)]
        private Color difficultEnergyColor =
            new Color(1.55f, 0.055f, 0.018f, 0.78f);
        [SerializeField, ColorUsage(true, true)]
        private Color difficultTargetColor =
            new Color(2f, 0.32f, 0.045f, 0.98f);

        [Header("Target Halo")]
        [SerializeField, Min(0.05f)] private float targetRingRadius = 0.32f;
        [SerializeField, Min(0.001f)] private float targetRingWidth = 0.024f;
        [SerializeField, Range(0f, 64f)] private float sparksPerSecond = 18f;

        private LineRenderer line;
        private LineRenderer coreLine;
        private LineRenderer firstHelixLine;
        private LineRenderer secondHelixLine;
        private LineRenderer innerTargetRing;
        private LineRenderer outerTargetRing;
        private ParticleSystem targetSparks;
        private ParticleSystemRenderer targetSparksRenderer;
        private Material runtimeMaterial;
        private MaterialPropertyBlock propertyBlock;
        private Gradient gradient;
        private GradientColorKey[] colorKeys;
        private GradientAlphaKey[] alphaKeys;
        private float pulsePhase;
        private float flowPhase;
        private float twistPhase;
        private Color currentEnergyColor;
        private Color currentTargetColor;
        private bool hasCurrentPalette;

        private Color ActiveEnergyColor => hasCurrentPalette
            ? currentEnergyColor
            : energyColor;
        private Color ActiveTargetColor => hasCurrentPalette
            ? currentTargetColor
            : targetColor;

        private void Awake()
        {
            ResolveReferences();
            EnsureLine();
            EnsureVisualLayers();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureLine();
            EnsureVisualLayers();
        }

        private void OnDisable()
        {
            SetVisualsActive(false);
        }

        private void LateUpdate()
        {
            ResolveReferences();
            EnsureLine();
            EnsureVisualLayers();

            if (attractor == null || !attractor.HasAttractionBeamTarget)
            {
                SetVisualsActive(false);
                return;
            }

            SetVisualsActive(true);
            UpdateLoadPalette(Time.deltaTime);
            AdvanceAnimation(Time.deltaTime);

            Vector3 start = ResolveBeamStart();
            Vector3 end = attractor.AttractionBeamTarget;
            UpdateBeamGeometry(start, end);
            UpdateTargetHalo(start, end, ResolveTargetRadius());
            UpdateBeamColor();
            UpdateShaderProperties();
        }

        private void UpdateLoadPalette(float deltaTime)
        {
            float difficulty = ResolveLoadDifficulty();
            Color wantedEnergy = Color.Lerp(
                energyColor,
                difficultEnergyColor,
                difficulty);
            Color wantedTarget = Color.Lerp(
                targetColor,
                difficultTargetColor,
                difficulty);

            if (!hasCurrentPalette)
            {
                currentEnergyColor = wantedEnergy;
                currentTargetColor = wantedTarget;
                hasCurrentPalette = true;
            }
            else
            {
                float response = Mathf.Max(0.01f, weightColorResponse);
                float blend = 1f - Mathf.Exp(
                    -response * Mathf.Max(0f, deltaTime));
                currentEnergyColor = Color.Lerp(
                    currentEnergyColor,
                    wantedEnergy,
                    blend);
                currentTargetColor = Color.Lerp(
                    currentTargetColor,
                    wantedTarget,
                    blend);
            }

            if (targetSparks != null)
            {
                ParticleSystem.MainModule main = targetSparks.main;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    ActiveEnergyColor,
                    ActiveTargetColor);
            }
        }

        private float ResolveLoadDifficulty()
        {
            if (attractor == null) return 0f;

            Rigidbody targetBody = attractor.HeldBody;
            if (targetBody == null && attractor.TowedPickaxe != null)
            {
                targetBody = attractor.TowedPickaxe.Body;
            }
            return targetBody != null
                ? Mathf.Clamp01(attractor.GetAttractionLoadRatio(targetBody))
                : 0f;
        }

        private void AdvanceAnimation(float deltaTime)
        {
            flowPhase = Mathf.Repeat(flowPhase + deltaTime * pulseSpeed, 4096f);
            twistPhase = Mathf.Repeat(twistPhase + deltaTime * twistSpeed, Tau);
            pulsePhase = Mathf.Repeat(
                pulsePhase + deltaTime * pulseSpeed * Tau,
                Tau);
        }

        private void UpdateBeamGeometry(Vector3 start, Vector3 end)
        {
            int pointCount = Mathf.Max(12, segments) + 1;
            SetPositionCount(line, pointCount);
            SetPositionCount(coreLine, pointCount);
            SetPositionCount(firstHelixLine, pointCount);
            SetPositionCount(secondHelixLine, pointCount);

            for (int i = 0; i < pointCount; i++)
            {
                float t = (float)i / (pointCount - 1);
                Vector3 center = CalculateCurvePoint(start, end, t);
                line.SetPosition(i, center);
                coreLine.SetPosition(i, center);
                firstHelixLine.SetPosition(
                    i,
                    CalculateHelixPoint(start, end, t, 0f));
                secondHelixLine.SetPosition(
                    i,
                    CalculateHelixPoint(start, end, t, Mathf.PI));
            }
        }

        private Vector3 CalculateCurvePoint(Vector3 start, Vector3 end, float t)
        {
            return Vector3.Lerp(start, end, t)
                + Vector3.up * CalculateArcHeight(t);
        }

        private Vector3 CalculateHelixPoint(
            Vector3 start,
            Vector3 end,
            float t,
            float strandOffset)
        {
            t = Mathf.Clamp01(t);
            Vector3 center = CalculateCurvePoint(start, end, t);
            Vector3 tangent = CalculateCurveTangent(start, end, t);
            CalculateBeamFrame(tangent, out Vector3 side, out Vector3 normal);

            float angle = t * helixTurns * Tau + twistPhase + strandOffset;
            float endpointEnvelope = Mathf.Sin(t * Mathf.PI);
            float radius = helixRadius * endpointEnvelope;
            return center
                + side * (Mathf.Cos(angle) * radius)
                + normal * (Mathf.Sin(angle) * radius);
        }

        private Vector3 CalculateCurveTangent(
            Vector3 start,
            Vector3 end,
            float t)
        {
            const float sampleDistance = 0.01f;
            float before = Mathf.Max(0f, t - sampleDistance);
            float after = Mathf.Min(1f, t + sampleDistance);
            Vector3 tangent = CalculateCurvePoint(start, end, after)
                - CalculateCurvePoint(start, end, before);
            if (tangent.sqrMagnitude < 0.000001f)
            {
                tangent = end - start;
            }
            return tangent.sqrMagnitude > 0.000001f
                ? tangent.normalized
                : Vector3.forward;
        }

        private void CalculateBeamFrame(
            Vector3 tangent,
            out Vector3 side,
            out Vector3 normal)
        {
            Vector3 referenceUp = viewCamera != null
                ? viewCamera.transform.up
                : Vector3.up;
            side = Vector3.Cross(tangent, referenceUp);
            if (side.sqrMagnitude < 0.0001f)
            {
                Vector3 referenceRight = viewCamera != null
                    ? viewCamera.transform.right
                    : transform.right;
                side = Vector3.Cross(tangent, referenceRight);
            }
            side.Normalize();
            normal = Vector3.Cross(side, tangent).normalized;
        }

        private float CalculateArcHeight(float t)
        {
            t = Mathf.Clamp01(t);
            return 4f * t * (1f - t) * arcHeight;
        }

        private void UpdateTargetHalo(
            Vector3 start,
            Vector3 end,
            float radius)
        {
            Vector3 tangent = CalculateCurveTangent(start, end, 1f);
            CalculateBeamFrame(tangent, out Vector3 side, out Vector3 normal);
            UpdateTargetRing(
                innerTargetRing,
                end,
                tangent,
                side,
                normal,
                radius * 0.82f,
                twistPhase,
                1f);
            UpdateTargetRing(
                outerTargetRing,
                end,
                tangent,
                side,
                normal,
                radius * 1.18f,
                -twistPhase * 0.72f,
                -1f);

            if (targetSparks == null) return;
            targetSparks.transform.position = end;
            ParticleSystem.ShapeModule shape = targetSparks.shape;
            shape.radius = radius;
        }

        private static void UpdateTargetRing(
            LineRenderer ring,
            Vector3 center,
            Vector3 tangent,
            Vector3 side,
            Vector3 normal,
            float radius,
            float phase,
            float direction)
        {
            if (ring == null) return;
            for (int i = 0; i < TargetRingSegments; i++)
            {
                float angle = (float)i / TargetRingSegments * Tau
                    + phase * direction;
                float runeRipple = Mathf.Sin(angle * 6f - phase * 2f)
                    * radius * 0.055f;
                Vector3 radial = side * Mathf.Cos(angle)
                    + normal * (Mathf.Sin(angle) * 0.72f);
                ring.SetPosition(
                    i,
                    center + radial * (radius + runeRipple)
                        + tangent * (Mathf.Sin(angle * 3f + phase)
                            * radius * 0.11f));
            }
        }

        private float ResolveTargetRadius()
        {
            float radius = Mathf.Max(0.05f, targetRingRadius);
            Rigidbody body = attractor != null ? attractor.HeldBody : null;
            if (body == null) return radius;

            Collider[] colliders = body.GetComponentsInChildren<Collider>();
            bool hasBounds = false;
            Bounds bounds = default;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider targetCollider = colliders[i];
                if (targetCollider == null || !targetCollider.enabled) continue;
                if (!hasBounds)
                {
                    bounds = targetCollider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(targetCollider.bounds);
                }
            }

            if (!hasBounds) return radius;
            float boundsRadius = Mathf.Max(
                bounds.extents.x,
                Mathf.Max(bounds.extents.y, bounds.extents.z));
            return Mathf.Clamp(
                Mathf.Max(radius, boundsRadius * 1.12f),
                radius,
                1.5f);
        }

        private void UpdateBeamColor()
        {
            if (gradient == null)
            {
                gradient = new Gradient();
                colorKeys = new GradientColorKey[3];
                alphaKeys = new GradientAlphaKey[2];
            }

            float pulse = 1f + Mathf.Sin(pulsePhase) * pulseStrength;
            Color beamEnergy = ActiveEnergyColor;
            Color beamTarget = ActiveTargetColor;
            Color pulsedEnergy = MultiplyRgb(beamEnergy, pulse);
            Color pulsedTarget = MultiplyRgb(beamTarget, pulse);
            float transition = Mathf.Clamp(startFadeLength, 0.01f, 0.5f);

            colorKeys[0] = new GradientColorKey(pulsedEnergy, 0f);
            colorKeys[1] = new GradientColorKey(pulsedEnergy, transition);
            colorKeys[2] = new GradientColorKey(pulsedTarget, 1f);
            alphaKeys[0] = new GradientAlphaKey(beamEnergy.a, 0f);
            alphaKeys[1] = new GradientAlphaKey(beamTarget.a, 1f);

            gradient.SetKeys(colorKeys, alphaKeys);
            line.colorGradient = gradient;
            if (coreLine != null) coreLine.colorGradient = gradient;
            if (firstHelixLine != null) firstHelixLine.colorGradient = gradient;
            if (secondHelixLine != null) secondHelixLine.colorGradient = gradient;
            if (innerTargetRing != null) innerTargetRing.colorGradient = gradient;
            if (outerTargetRing != null) outerTargetRing.colorGradient = gradient;
        }

        private void UpdateShaderProperties()
        {
            ApplyShaderStyle(line, 0.24f, 3.5f, 0.32f, 1.35f, 0f);
            ApplyShaderStyle(coreLine, 0.92f, 8.5f, 0.9f, 3.4f, 0f);
            ApplyShaderStyle(firstHelixLine, 0.78f, 11f, 1f, 2.1f, 0f);
            ApplyShaderStyle(secondHelixLine, 0.62f, 13f, 1f, 2.1f, 0f);
            ApplyShaderStyle(innerTargetRing, 0.88f, 12f, 1f, 2.6f, 0f);
            ApplyShaderStyle(outerTargetRing, 0.5f, 7f, 0.8f, 1.8f, 0f);
            ApplyShaderStyle(targetSparksRenderer, 0.82f, 1f, 0f, 1.5f, 1f);
        }

        private void ApplyShaderStyle(
            Renderer renderer,
            float alpha,
            float bandDensity,
            float patternStrength,
            float edgePower,
            float particleMode)
        {
            if (renderer == null) return;
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
            propertyBlock.Clear();
            propertyBlock.SetColor(EnergyColorId, ActiveEnergyColor);
            propertyBlock.SetColor(HotColorId, ActiveTargetColor);
            propertyBlock.SetFloat(PhaseId, flowPhase);
            propertyBlock.SetFloat(AlphaId, alpha);
            propertyBlock.SetFloat(BandDensityId, bandDensity);
            propertyBlock.SetFloat(PatternStrengthId, patternStrength);
            propertyBlock.SetFloat(EdgePowerId, edgePower);
            propertyBlock.SetFloat(ParticleModeId, particleMode);
            renderer.SetPropertyBlock(propertyBlock);
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
            if (attractor == null)
                attractor = GetComponent<FirstPersonMagnetInteractor>();
            if (viewCamera == null && attractor != null)
                viewCamera = ResolveCameraFromAttractor();
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
                return viewCamera.transform.position
                    + viewCamera.transform.forward * 0.15f;
            }

            return transform.position;
        }

        private Camera ResolveCameraFromAttractor()
        {
            var perspectiveCamera =
                GetComponentInChildren<PerspectiveCameraController>(true);
            return perspectiveCamera != null
                ? perspectiveCamera.ControlledCamera
                : null;
        }

        private void EnsureLine()
        {
            if (line != null)
            {
                return;
            }

            line = GetComponent<LineRenderer>();
            if (line == null) line = gameObject.AddComponent<LineRenderer>();

            ConfigureLineRenderer(
                line,
                Mathf.Max(12, segments) + 1,
                startWidth * auraWidthMultiplier,
                endWidth * auraWidthMultiplier,
                false);
            line.enabled = false;
        }

        private void EnsureVisualLayers()
        {
            if (!Application.isPlaying) return;
            Material material = ResolveMaterial();
            if (line != null && line.sharedMaterial != material)
            {
                line.sharedMaterial = material;
            }

            coreLine = EnsureChildLine(
                coreLine,
                "Magnet Energy Core",
                Mathf.Max(12, segments) + 1,
                startWidth,
                endWidth,
                false);
            firstHelixLine = EnsureChildLine(
                firstHelixLine,
                "Magnet Rune Helix A",
                Mathf.Max(12, segments) + 1,
                helixWidth,
                helixWidth,
                false);
            secondHelixLine = EnsureChildLine(
                secondHelixLine,
                "Magnet Rune Helix B",
                Mathf.Max(12, segments) + 1,
                helixWidth * 0.78f,
                helixWidth * 0.78f,
                false);
            innerTargetRing = EnsureChildLine(
                innerTargetRing,
                "Magnet Target Rune Inner",
                TargetRingSegments,
                targetRingWidth,
                targetRingWidth,
                true);
            outerTargetRing = EnsureChildLine(
                outerTargetRing,
                "Magnet Target Rune Outer",
                TargetRingSegments,
                targetRingWidth * 0.68f,
                targetRingWidth * 0.68f,
                true);
            EnsureTargetSparks(material);
        }

        private LineRenderer EnsureChildLine(
            LineRenderer renderer,
            string objectName,
            int pointCount,
            float widthStart,
            float widthEnd,
            bool loop)
        {
            if (renderer != null)
            {
                Material existingMaterial = ResolveMaterial();
                if (renderer.sharedMaterial != existingMaterial)
                {
                    renderer.sharedMaterial = existingMaterial;
                }
                return renderer;
            }

            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            renderer = child.AddComponent<LineRenderer>();

            ConfigureLineRenderer(
                renderer,
                pointCount,
                widthStart,
                widthEnd,
                loop);
            renderer.sharedMaterial = ResolveMaterial();
            renderer.enabled = false;
            return renderer;
        }

        private static void ConfigureLineRenderer(
            LineRenderer renderer,
            int pointCount,
            float widthStart,
            float widthEnd,
            bool loop)
        {
            renderer.positionCount = pointCount;
            renderer.useWorldSpace = true;
            renderer.loop = loop;
            renderer.alignment = LineAlignment.View;
            renderer.textureMode = LineTextureMode.Tile;
            renderer.textureScale = Vector2.one;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.generateLightingData = false;
            renderer.numCapVertices = loop ? 0 : 4;
            renderer.numCornerVertices = 4;
            renderer.widthCurve = AnimationCurve.Linear(
                0f,
                Mathf.Max(0f, widthStart),
                1f,
                Mathf.Max(0f, widthEnd));
        }

        private static void SetPositionCount(
            LineRenderer renderer,
            int pointCount)
        {
            if (renderer != null && renderer.positionCount != pointCount)
            {
                renderer.positionCount = pointCount;
            }
        }

        private void EnsureTargetSparks(Material material)
        {
            if (targetSparks == null)
            {
                var child = new GameObject("Magnet Target Sparks");
                child.transform.SetParent(transform, false);
                targetSparks = child.AddComponent<ParticleSystem>();
                targetSparksRenderer =
                    child.GetComponent<ParticleSystemRenderer>();
                ConfigureTargetSparks();
            }

            if (targetSparksRenderer != null)
            {
                targetSparksRenderer.sharedMaterial = material;
            }
        }

        private void ConfigureTargetSparks()
        {
            ParticleSystem.MainModule main = targetSparks.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 32;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.025f, 0.16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.075f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                ActiveEnergyColor,
                ActiveTargetColor);

            ParticleSystem.EmissionModule emission = targetSparks.emission;
            emission.rateOverTime = sparksPerSecond;

            ParticleSystem.ShapeModule shape = targetSparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = targetRingRadius;
            shape.radiusThickness = 0.12f;

            ParticleSystem.NoiseModule noise = targetSparks.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = 0.12f;
            noise.frequency = 0.45f;
            noise.scrollSpeed = 0.3f;

            ParticleSystem.SizeOverLifetimeModule size =
                targetSparks.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.16f, 1f),
                    new Keyframe(1f, 0f)));

            ParticleSystem.ColorOverLifetimeModule color =
                targetSparks.colorOverLifetime;
            color.enabled = true;
            var particleGradient = new Gradient();
            particleGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = particleGradient;

            targetSparksRenderer.renderMode =
                ParticleSystemRenderMode.Billboard;
            targetSparksRenderer.sortingOrder = 4;
        }

        private Material ResolveMaterial()
        {
            if (beamMaterial != null) return beamMaterial;
            if (runtimeMaterial != null) return runtimeMaterial;

            Shader shader = Shader.Find(MagnetEffectShaderNames.EnergyRibbon)
                ?? Shader.Find(MagnetEffectShaderNames.SpriteFallback)
                ?? Shader.Find(MagnetEffectShaderNames.LegacyParticleFallback);
            runtimeMaterial = shader != null ? new Material(shader) : null;
            if (runtimeMaterial != null)
            {
                runtimeMaterial.name = "Runtime Magnet Energy Ribbon";
            }
            return runtimeMaterial;
        }

        private void SetVisualsActive(bool active)
        {
            SetRendererEnabled(line, active);
            SetRendererEnabled(coreLine, active);
            SetRendererEnabled(firstHelixLine, active);
            SetRendererEnabled(secondHelixLine, active);
            SetRendererEnabled(innerTargetRing, active);
            SetRendererEnabled(outerTargetRing, active);

            if (!active)
            {
                hasCurrentPalette = false;
            }

            if (targetSparks == null) return;
            if (active)
            {
                if (!targetSparks.isPlaying) targetSparks.Play();
            }
            else if (targetSparks.isPlaying)
            {
                targetSparks.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }

        }

        private static void SetRendererEnabled(Renderer renderer, bool enabled)
        {
            if (renderer != null) renderer.enabled = enabled;
        }

        private void OnValidate()
        {
            segments = Mathf.Clamp(segments, 12, 96);
            arcHeight = Mathf.Max(0f, arcHeight);
            startWidth = Mathf.Max(0f, startWidth);
            endWidth = Mathf.Max(0f, endWidth);
            helixRadius = Mathf.Max(0f, helixRadius);
            helixWidth = Mathf.Max(0f, helixWidth);
            targetRingRadius = Mathf.Max(0.05f, targetRingRadius);
            targetRingWidth = Mathf.Max(0.001f, targetRingWidth);
            weightColorResponse = Mathf.Max(0.01f, weightColorResponse);
        }

        private void OnDestroy()
        {
            if (runtimeMaterial == null) return;
            if (Application.isPlaying)
            {
                Destroy(runtimeMaterial);
            }
            else
            {
                DestroyImmediate(runtimeMaterial);
            }
        }
    }
}
