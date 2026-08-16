using Supernova.Infrastructure;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.Effects
{
    /// <summary>
    /// One pooled, world-space particle system shared by every rigidbody impact.
    /// Per-impact Emit calls avoid instantiating and destroying effect objects.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RigidbodyImpactSmokeEmitter : MonoBehaviour
    {
        public const int MinimumParticlesPerBurst = 4;
        public const int MaximumParticlesPerBurst = 14;
        public const int MaximumActiveParticles = 384;

        private static RigidbodyImpactSmokeEmitter instance;

        private ParticleSystem smokeParticles;
        private Material runtimeMaterial;

        public int ActiveParticleCount => smokeParticles != null
            ? smokeParticles.particleCount
            : 0;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            instance = null;
        }

        public static int CalculateParticleCount(float normalizedStrength)
        {
            return Mathf.RoundToInt(Mathf.Lerp(
                MinimumParticlesPerBurst,
                MaximumParticlesPerBurst,
                Mathf.Clamp01(normalizedStrength)));
        }

        internal static void RequestPlay(
            RigidbodyImpactFeedbackRequest request)
        {
            if (!Application.isPlaying || !request.IsValid)
            {
                return;
            }

            GetOrCreate().Play(request);
        }

        private static RigidbodyImpactSmokeEmitter GetOrCreate()
        {
            if (instance != null)
            {
                return instance;
            }

            instance = FindObjectOfType<RigidbodyImpactSmokeEmitter>();
            if (instance != null)
            {
                return instance;
            }

            var root = new GameObject("Rigidbody Impact Smoke");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<RigidbodyImpactSmokeEmitter>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                if (Application.isPlaying)
                {
                    Destroy(gameObject);
                }
                else
                {
                    DestroyImmediate(gameObject);
                }
                return;
            }

            instance = this;
            EnsureParticleSystem();
        }

        private void Play(RigidbodyImpactFeedbackRequest request)
        {
            EnsureParticleSystem();
            int available = Mathf.Max(
                0,
                MaximumActiveParticles - smokeParticles.particleCount);
            int count = Mathf.Min(
                available,
                CalculateParticleCount(request.NormalizedStrength));
            if (count <= 0)
            {
                return;
            }

            var random = new System.Random(request.RandomSeed);
            Vector3 normal = request.Normal.sqrMagnitude > 0.0001f
                ? request.Normal.normalized
                : Vector3.up;
            float strength = Mathf.Clamp01(request.NormalizedStrength);
            float baseSize = Mathf.Lerp(0.12f, 0.38f, strength);
            float baseSpeed = Mathf.Lerp(0.2f, 1.05f, strength);
            float baseLifetime = Mathf.Lerp(0.45f, 0.95f, strength);

            smokeParticles.Play(false);
            for (int i = 0; i < count; i++)
            {
                Vector3 scatter = RandomHemisphere(normal, random);
                Vector3 tangentDirection = Vector3.ProjectOnPlane(
                    scatter,
                    normal);
                if (tangentDirection.sqrMagnitude < 0.0001f)
                {
                    tangentDirection = Vector3.Cross(
                        normal,
                        Mathf.Abs(normal.y) < 0.95f
                            ? Vector3.up
                            : Vector3.right);
                }
                tangentDirection.Normalize();
                Vector3 direction = (
                    tangentDirection
                        * Mathf.Lerp(0.9f, 1.35f, Next01(random))
                    + normal * Mathf.Lerp(0.12f, 0.32f, Next01(random))
                    + Vector3.up * 0.18f).normalized;
                float brightness = Mathf.Lerp(0.72f, 1.08f, Next01(random));
                var emit = new ParticleSystem.EmitParams
                {
                    position = request.Position
                        + RandomTangentOffset(
                            normal,
                            baseSize * 0.8f,
                            random)
                        + normal * 0.02f,
                    velocity = direction
                        * baseSpeed
                        * Mathf.Lerp(0.65f, 1.25f, Next01(random)),
                    startLifetime = baseLifetime
                        * Mathf.Lerp(0.8f, 1.2f, Next01(random)),
                    startSize = baseSize
                        * Mathf.Lerp(0.7f, 1.45f, Next01(random)),
                    startColor = new Color(
                        0.62f * brightness,
                        0.59f * brightness,
                        0.54f * brightness,
                        Mathf.Lerp(0.62f, 0.82f, strength)),
                    rotation = Mathf.Lerp(0f, Mathf.PI * 2f, Next01(random)),
                    randomSeed = (uint)random.Next(1, int.MaxValue),
                };
                smokeParticles.Emit(emit, 1);
            }
        }

        private void EnsureParticleSystem()
        {
            if (smokeParticles != null)
            {
                return;
            }

            smokeParticles = GetComponent<ParticleSystem>();
            if (smokeParticles == null)
            {
                smokeParticles = gameObject.AddComponent<ParticleSystem>();
            }

            ParticleSystem.MainModule main = smokeParticles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaximumActiveParticles;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startSize = 1f;
            main.gravityModifier = -0.025f;
            main.stopAction = ParticleSystemStopAction.None;

            ParticleSystem.EmissionModule emission = smokeParticles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = smokeParticles.shape;
            shape.enabled = false;

            ParticleSystem.TextureSheetAnimationModule textureSheet =
                smokeParticles.textureSheetAnimation;
            textureSheet.enabled = true;
            textureSheet.mode = ParticleSystemAnimationMode.Grid;
            textureSheet.animation = ParticleSystemAnimationType.WholeSheet;
            textureSheet.numTilesX = 8;
            textureSheet.numTilesY = 8;
            textureSheet.cycleCount = 1;
            textureSheet.frameOverTime = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.Linear(0f, 0f, 1f, 1f));

            ParticleSystem.NoiseModule noise = smokeParticles.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = 0.11f;
            noise.frequency = 0.55f;
            noise.scrollSpeed = 0.22f;

            ParticleSystem.SizeOverLifetimeModule size =
                smokeParticles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.35f),
                    new Keyframe(0.18f, 0.9f),
                    new Keyframe(1f, 1.35f)));

            ParticleSystem.ColorOverLifetimeModule color =
                smokeParticles.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.25f, 0f),
                    new GradientAlphaKey(1f, 0.05f),
                    new GradientAlphaKey(0.75f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = fade;

            ParticleSystemRenderer renderer =
                GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = ResolveParticleMaterial();
            smokeParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private Material ResolveParticleMaterial()
        {
            EffectAssetReferences effects = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.Effects
                : null;
            if (effects != null && effects.CollisionSmokeMaterial != null)
            {
                return effects.CollisionSmokeMaterial;
            }

            Shader shader =
                Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null)
            {
                return null;
            }

            runtimeMaterial = new Material(shader)
            {
                name = "Rigidbody Impact Smoke (Runtime)",
                renderQueue = (int)RenderQueue.Transparent,
            };
            ConfigureTransparentMaterial(runtimeMaterial);
            return runtimeMaterial;
        }

        private static void ConfigureTransparentMaterial(Material material)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }
            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            }
            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat(
                    "_DstBlend",
                    (float)BlendMode.OneMinusSrcAlpha);
            }
            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }
        }

        private static Vector3 RandomHemisphere(
            Vector3 normal,
            System.Random random)
        {
            Vector3 direction;
            do
            {
                direction = new Vector3(
                    Mathf.Lerp(-1f, 1f, Next01(random)),
                    Mathf.Lerp(-1f, 1f, Next01(random)),
                    Mathf.Lerp(-1f, 1f, Next01(random)));
            }
            while (direction.sqrMagnitude < 0.0001f
                || direction.sqrMagnitude > 1f);

            direction.Normalize();
            return Vector3.Dot(direction, normal) < 0f
                ? -direction
                : direction;
        }

        private static Vector3 RandomTangentOffset(
            Vector3 normal,
            float radius,
            System.Random random)
        {
            Vector3 tangent = Vector3.Cross(
                normal,
                Mathf.Abs(normal.y) < 0.95f ? Vector3.up : Vector3.right);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            float angle = Mathf.Lerp(0f, Mathf.PI * 2f, Next01(random));
            float distance = Mathf.Sqrt(Next01(random)) * radius;
            return (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle))
                * distance;
        }

        private static float Next01(System.Random random)
        {
            return (float)random.NextDouble();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }

            if (runtimeMaterial == null)
            {
                return;
            }

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
