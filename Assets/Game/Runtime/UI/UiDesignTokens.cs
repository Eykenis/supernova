using UnityEngine;

namespace Supernova.UI
{
    /// <summary>
    /// Shared runtime UI design tokens. Screens and widgets should read visual constants from
    /// this asset instead of embedding unrelated colors and scale rules in each controller.
    /// </summary>
    [CreateAssetMenu(fileName = "UiDesignTokens", menuName = "Supernova/UI/Design Tokens")]
    public sealed class UiDesignTokens : ScriptableObject
    {
        [Header("Viewport")]
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);
        [SerializeField, Range(0f, 1f)] private float matchWidthOrHeight = 0.5f;
        [SerializeField, Min(1f)] private float referencePixelsPerUnit = 100f;

        [Header("Palette")]
        [SerializeField] private Color backdrop = new Color(0.012f, 0.02f, 0.032f, 1f);
        [SerializeField] private Color surface = new Color(0.035f, 0.055f, 0.075f, 0.97f);
        [SerializeField] private Color surfaceRaised = new Color(0.06f, 0.09f, 0.115f, 1f);
        [SerializeField] private Color textPrimary = new Color(0.93f, 0.95f, 0.95f, 1f);
        [SerializeField] private Color textSecondary = new Color(0.58f, 0.68f, 0.7f, 1f);
        [SerializeField] private Color accent = new Color(0.94f, 0.35f, 0.12f, 1f);
        [SerializeField] private Color accentHover = new Color(1f, 0.46f, 0.2f, 1f);
        [SerializeField] private Color focus = new Color(0.31f, 0.82f, 0.86f, 1f);
        [SerializeField] private Color success = new Color(0.28f, 0.74f, 0.5f, 1f);
        [SerializeField] private Color divider = new Color(0.31f, 0.48f, 0.5f, 0.38f);

        [Header("Typography")]
        [SerializeField, Min(12)] private int bodySize = 18;
        [SerializeField, Min(12)] private int controlSize = 18;
        [SerializeField, Min(12)] private int captionSize = 14;
        [SerializeField, Min(20)] private int displaySize = 76;

        [Header("Motion")]
        [SerializeField, Min(0f)] private float quickTransitionSeconds = 0.12f;
        [SerializeField, Min(0f)] private float screenTransitionSeconds = 0.25f;

        public Vector2 ReferenceResolution => referenceResolution;
        public float MatchWidthOrHeight => matchWidthOrHeight;
        public float ReferencePixelsPerUnit => referencePixelsPerUnit;
        public Color Backdrop => backdrop;
        public Color Surface => surface;
        public Color SurfaceRaised => surfaceRaised;
        public Color TextPrimary => textPrimary;
        public Color TextSecondary => textSecondary;
        public Color Accent => accent;
        public Color AccentHover => accentHover;
        public Color Focus => focus;
        public Color Success => success;
        public Color Divider => divider;
        public int BodySize => bodySize;
        public int ControlSize => controlSize;
        public int CaptionSize => captionSize;
        public int DisplaySize => displaySize;
        public float QuickTransitionSeconds => quickTransitionSeconds;
        public float ScreenTransitionSeconds => screenTransitionSeconds;
    }
}
