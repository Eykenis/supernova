using Supernova.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace Supernova.Effects
{
    /// <summary>
    /// Emits a short dust puff and physical-looking chips at the actual voxel hit.
    /// The systems are created lazily so the player prefab only needs this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoxelMiningImpactEffect : MonoBehaviour
    {
        private const int MaximumParticlesPerSystem = 96;

        [Header("Rendering")]
        [SerializeField] private Material particleMaterial;

        [Header("Dust")]
        [SerializeField, Range(0, 24)] private int baseDustCount = 6;
        [SerializeField, Min(0.01f)] private float dustLifetime = 0.42f;
        [SerializeField, Min(0.001f)] private float dustSize = 0.12f;
        [SerializeField, Min(0f)] private float dustSpeed = 0.55f;

        [Header("Chips")]
        [SerializeField, Range(0, 24)] private int baseChipCount = 4;
        [SerializeField, Min(0.01f)] private float chipLifetime = 0.68f;
        [SerializeField, Min(0.001f)] private float chipSize = 0.045f;
        [SerializeField, Min(0f)] private float chipSpeed = 1.35f;
        [SerializeField, Min(0f)] private float chipGravity = 1.1f;

        private ParticleSystem dustParticles;
        private ParticleSystem chipParticles;
        private Material runtimeMaterial;
        private uint burstSequence;

        public int ActiveParticleCount =>
            (dustParticles != null ? dustParticles.particleCount : 0)
            + (chipParticles != null ? chipParticles.particleCount : 0);

        public void Play(
            Vector3 position,
            Vector3 surfaceNormal,
            Color voxelColor,
            VoxelMiningBrushResult result)
        {
            if (result.DamagedCount <= 0)
            {
                return;
            }

            EnsureParticleSystems();

            Vector3 normal = surfaceNormal.sqrMagnitude > 0.0001f
                ? surfaceNormal.normalized
                : Vector3.up;
            int destroyedBoost = Mathf.Clamp(result.DestroyedCount, 0, 8);
            int dustCount = Mathf.Min(
                MaximumParticlesPerSystem,
                baseDustCount
                + Mathf.Clamp(result.DamagedCount - 1, 0, 4)
                + destroyedBoost);
            int chipCount = Mathf.Min(
                MaximumParticlesPerSystem,
                baseChipCount + destroyedBoost * 2);

            int seed = unchecked(
                result.PrimaryCoordinate.GetHashCode()
                ^ (int)(++burstSequence * 2654435761u));
            var random = new System.Random(seed);
            Color opaqueColor = new Color(
                Mathf.Clamp01(voxelColor.r),
                Mathf.Clamp01(voxelColor.g),
                Mathf.Clamp01(voxelColor.b),
                1f);

            dustParticles.Play(false);
            chipParticles.Play(false);
            EmitDust(position, normal, opaqueColor, dustCount, random);
            EmitChips(position, normal, opaqueColor, chipCount, random);
        }

        private void EmitDust(
            Vector3 position,
            Vector3 normal,
            Color color,
            int count,
            System.Random random)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 scatter = RandomHemisphere(normal, random);
                Vector3 direction =
                    (normal * 0.65f + scatter * 0.8f).normalized;
                float brightness = Lerp(0.72f, 1.08f, Next01(random));
                var emit = new ParticleSystem.EmitParams
                {
                    position = position
                        + RandomTangentOffset(normal, dustSize * 0.45f, random)
                        + normal * 0.01f,
                    velocity = direction
                        * dustSpeed
                        * Lerp(0.55f, 1.25f, Next01(random)),
                    startLifetime = dustLifetime
                        * Lerp(0.75f, 1.25f, Next01(random)),
                    startSize = dustSize
                        * Lerp(0.65f, 1.45f, Next01(random)),
                    startColor = Tint(color, brightness, 0.52f),
                    randomSeed = (uint)random.Next(1, int.MaxValue),
                };
                dustParticles.Emit(emit, 1);
            }
        }

        private void EmitChips(
            Vector3 position,
            Vector3 normal,
            Color color,
            int count,
            System.Random random)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 scatter = RandomHemisphere(normal, random);
                Vector3 direction =
                    (normal * 1.1f + scatter * 0.85f + Vector3.up * 0.2f)
                    .normalized;
                float brightness = Lerp(0.68f, 1.16f, Next01(random));
                var emit = new ParticleSystem.EmitParams
                {
                    position = position
                        + RandomTangentOffset(normal, chipSize, random)
                        + normal * 0.015f,
                    velocity = direction
                        * chipSpeed
                        * Lerp(0.65f, 1.35f, Next01(random)),
                    startLifetime = chipLifetime
                        * Lerp(0.8f, 1.2f, Next01(random)),
                    startSize = chipSize
                        * Lerp(0.65f, 1.5f, Next01(random)),
                    rotation = Lerp(
                        0f,
                        Mathf.PI * 2f,
                        Next01(random)),
                    startColor = Tint(color, brightness, 0.95f),
                    randomSeed = (uint)random.Next(1, int.MaxValue),
                };
                chipParticles.Emit(emit, 1);
            }
        }

        private void EnsureParticleSystems()
        {
            Material material = ResolveParticleMaterial();
            if (dustParticles == null)
            {
                dustParticles = CreateParticleSystem(
                    "Mining Dust",
                    0f,
                    material,
                    true);
            }

            if (chipParticles == null)
            {
                chipParticles = CreateParticleSystem(
                    "Mining Chips",
                    chipGravity,
                    material,
                    false);
            }
        }

        private ParticleSystem CreateParticleSystem(
            string objectName,
            float gravity,
            Material material,
            bool softFade)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            ParticleSystem particles = child.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaximumParticlesPerSystem;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startSize = 1f;
            main.gravityModifier = gravity;
            main.stopAction = ParticleSystemStopAction.None;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = false;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
                particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                softFade
                    ? new[]
                    {
                        new GradientAlphaKey(0.15f, 0f),
                        new GradientAlphaKey(1f, 0.12f),
                        new GradientAlphaKey(0f, 1f),
                    }
                    : new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 0.72f),
                        new GradientAlphaKey(0f, 1f),
                    });
            colorOverLifetime.color = fade;

            ParticleSystemRenderer renderer =
                child.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = material;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private Material ResolveParticleMaterial()
        {
            if (particleMaterial != null)
            {
                return particleMaterial;
            }

            if (runtimeMaterial != null)
            {
                return runtimeMaterial;
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
                name = "Mining Particles (Runtime)",
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
            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0f);
            }
            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            }
            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
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
                    Lerp(-1f, 1f, Next01(random)),
                    Lerp(-1f, 1f, Next01(random)),
                    Lerp(-1f, 1f, Next01(random)));
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
            float angle = Lerp(0f, Mathf.PI * 2f, Next01(random));
            float distance = Mathf.Sqrt(Next01(random)) * radius;
            return (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle))
                * distance;
        }

        private static Color Tint(Color color, float brightness, float alpha)
        {
            return new Color(
                Mathf.Clamp01(color.r * brightness),
                Mathf.Clamp01(color.g * brightness),
                Mathf.Clamp01(color.b * brightness),
                alpha);
        }

        private static float Next01(System.Random random)
        {
            return (float)random.NextDouble();
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private void OnDisable()
        {
            if (dustParticles != null)
            {
                dustParticles.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (chipParticles != null)
            {
                chipParticles.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
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
}
