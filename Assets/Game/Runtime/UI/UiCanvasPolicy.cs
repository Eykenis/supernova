using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Enforces the shared screen-space scaling contract on a Canvas.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class UiCanvasPolicy : MonoBehaviour
    {
        private static readonly Vector2 DefaultReferenceResolution = new Vector2(1920f, 1080f);

        [SerializeField] private UiDesignTokens designTokens;
        [SerializeField] private Vector2 fallbackReferenceResolution = new Vector2(1920f, 1080f);
        [SerializeField, Range(0f, 1f)] private float fallbackMatchWidthOrHeight = 0.5f;

        public UiDesignTokens DesignTokens => designTokens;

        private void OnEnable()
        {
            ApplyPolicy();
        }

        private void OnValidate()
        {
            ApplyPolicy();
        }

        public void SetDesignTokens(UiDesignTokens tokens)
        {
            designTokens = tokens;
            ApplyPolicy();
        }

        [ContextMenu("Apply Canvas Policy")]
        public void ApplyPolicy()
        {
            Canvas canvas = GetComponent<Canvas>();
            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (canvas == null || scaler == null || canvas.renderMode == RenderMode.WorldSpace)
                return;

            Vector2 resolution = designTokens != null
                ? designTokens.ReferenceResolution
                : fallbackReferenceResolution;
            if (resolution.x <= 0f || resolution.y <= 0f)
                resolution = DefaultReferenceResolution;

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = resolution;
            scaler.matchWidthOrHeight = designTokens != null
                ? designTokens.MatchWidthOrHeight
                : fallbackMatchWidthOrHeight;
            scaler.referencePixelsPerUnit = designTokens != null
                ? designTokens.ReferencePixelsPerUnit
                : 100f;
        }
    }
}
