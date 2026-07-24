using System.Collections;
using Supernova.Effects;
using UnityEngine;

namespace Supernova.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public sealed class TimedBomb : MonoBehaviour
    {
        [Header("Fuse")]
        [SerializeField, Min(0.05f)] private float fuseSeconds = 2.5f;
        [Header("Area effect")]
        [SerializeField, Min(0.1f)] private float radius = 3.5f;
        [SerializeField, Min(0f)] private float damage = 100f;
        [SerializeField, Min(0f)] private float impulse = 8f;
        [SerializeField, Range(0f, 1f)] private float terrainRandomness = 0.28f;
        [SerializeField] private LayerMask physicsLayers = ~0;
        [Header("Presentation")]
        [SerializeField] private Renderer indicatorRenderer;
        [SerializeField] private Color safeColor = new Color(0.12f, 0.12f, 0.12f);
        [SerializeField] private Color warningColor = new Color(1f, 0.08f, 0.02f);

        private Rigidbody body;
        private MaterialPropertyBlock propertyBlock;
        private float detonationTime;
        private bool armed;
        private bool detonated;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (indicatorRenderer == null) indicatorRenderer = GetComponentInChildren<Renderer>();
            propertyBlock = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            Arm();
        }

        public void Arm()
        {
            detonationTime = Time.time + fuseSeconds;
            armed = true;
            detonated = false;
        }

        public void Launch(Vector3 velocity, Vector3 angularVelocity)
        {
            if (body == null) body = GetComponent<Rigidbody>();
            body.velocity = velocity;
            body.angularVelocity = angularVelocity;
            Arm();
        }

        private void Update()
        {
            if (!armed || detonated) return;
            float remaining = detonationTime - Time.time;
            UpdateIndicator(remaining);
            if (remaining <= 0f) Detonate();
        }

        public void Detonate()
        {
            if (detonated) return;
            detonated = true;
            armed = false;

            int seed = unchecked(GetInstanceID() * 397 ^ Time.frameCount);
            var context = new AreaEffectContext(
                transform.position, radius, damage, impulse, terrainRandomness, seed, gameObject);
            AreaEffectDispatcher.Dispatch(context, physicsLayers.value);
            SpawnFlash(transform.position, radius);
            Destroy(gameObject);
        }

        private void UpdateIndicator(float remaining)
        {
            if (indicatorRenderer == null) return;
            float normalized = Mathf.Clamp01(remaining / fuseSeconds);
            float pulse = Mathf.PingPong(Time.time * Mathf.Lerp(8f, 2f, normalized), 1f);
            Color color = Color.Lerp(warningColor, safeColor, normalized) * Mathf.Lerp(0.65f, 1.5f, pulse);
            indicatorRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            indicatorRenderer.SetPropertyBlock(propertyBlock);
        }

        private static void SpawnFlash(Vector3 position, float effectRadius)
        {
            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "ExplosionFlash";
            flash.transform.position = position;
            flash.transform.localScale = Vector3.one * 0.15f;
            Collider collider = flash.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = flash.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                var material = new Material(shader) { color = new Color(1f, 0.22f, 0.02f, 1f) };
                renderer.material = material;
            }
            flash.AddComponent<ExplosionFlash>().Initialize(effectRadius);
        }
    }

    public sealed class ExplosionFlash : MonoBehaviour
    {
        private float targetScale;
        private float elapsed;
        public void Initialize(float radius) => targetScale = radius * 2f;
        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / 0.22f);
            transform.localScale = Vector3.one * Mathf.Lerp(0.15f, targetScale, 1f - (1f - t) * (1f - t));
            if (elapsed >= 0.22f)
            {
                Renderer r = GetComponent<Renderer>();
                if (r != null && r.material != null) Destroy(r.material);
                Destroy(gameObject);
            }
        }
    }
}
