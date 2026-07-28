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
        [SerializeField] private Color backdrop = new Color(0.012f, 0.035f, 0.05f, 1f);
        [SerializeField] private Color surface = new Color(0.024f, 0.078f, 0.11f, 0.97f);
        [SerializeField] private Color surfaceRaised = new Color(0.04f, 0.14f, 0.18f, 1f);
        [SerializeField] private Color textPrimary = new Color(0.88f, 0.96f, 0.98f, 1f);
        [SerializeField] private Color textSecondary = new Color(0.49f, 0.64f, 0.68f, 1f);
        [SerializeField] private Color accent = new Color(0.36f, 0.89f, 0.98f, 1f);
        [SerializeField] private Color accentHover = new Color(0.58f, 0.96f, 1f, 1f);
        [SerializeField] private Color focus = new Color(0.36f, 0.89f, 0.98f, 1f);
        [SerializeField] private Color success = new Color(0.28f, 0.74f, 0.5f, 1f);
        [SerializeField] private Color divider = new Color(0.32f, 0.75f, 0.82f, 0.42f);

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
