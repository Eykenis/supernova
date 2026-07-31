using UnityEngine;

namespace Supernova.Effects
{
    /// <summary>Project-owned equivalent of the light pulse used by KriptoFX MuzzleFlash1.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class MuzzleFlashLightCurve : MonoBehaviour
    {
        public AnimationCurve LightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public float GraphTimeMultiplier = 1f;
        public float GraphIntensityMultiplier = 1f;

        private Light lightSource;
        private float startTime;
        private bool canUpdate;

        private void Awake()
        {
            lightSource = GetComponent<Light>();
            lightSource.intensity = LightCurve.Evaluate(0f);
        }

        private void OnEnable()
        {
            if (lightSource == null) lightSource = GetComponent<Light>();
            startTime = Time.time;
            canUpdate = true;
            lightSource.enabled = true;
        }

        private void Update()
        {
            if (!canUpdate) return;

            float duration = Mathf.Max(0.0001f, GraphTimeMultiplier);
            float elapsed = Time.time - startTime;
            lightSource.intensity = LightCurve.Evaluate(elapsed / duration)
                * GraphIntensityMultiplier;
            if (elapsed < duration) return;

            canUpdate = false;
            lightSource.enabled = false;
        }
    }
}
