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

        [Header("Runtime Layers")]
        [SerializeField] private int hudSortingOrder = 100;
        [SerializeField] private int crosshairSortingOrder = 101;
        [SerializeField] private int missionOverlaySortingOrder = 900;
        [SerializeField] private int loadingSortingOrder = 1000;
        [SerializeField] private int pauseSortingOrder = 1100;

        [Header("Runtime Widgets")]
        [SerializeField] private bool showHealth = true;
        [SerializeField] private bool showHotbar = true;
        [SerializeField] private bool showCrosshair = true;
        [SerializeField] private bool showCompass = true;
        [SerializeField] private bool showMissionObjective = true;
        [SerializeField] private bool showMissionPrompt = true;
        [SerializeField] private bool showMissionTimer = true;

        [Header("Gameplay HUD Layout")]
        [SerializeField] private Vector2 compassPosition = new Vector2(0f, -12f);
        [SerializeField] private Vector2 compassSize = new Vector2(720f, 72f);
        [SerializeField, Range(60f, 180f)] private float compassVisibleDegrees = 105f;
        [SerializeField] private Vector2 hudHealthPosition = new Vector2(48f, 42f);
        [SerializeField] private Vector2 hudHealthSize = new Vector2(372f, 104f);
        [SerializeField] private Vector2 hudHotbarPosition = new Vector2(-46f, 42f);
        [SerializeField] private Vector2 hudHotbarSize = new Vector2(640f, 78f);
        [SerializeField, Range(3, 16)] private int hudHealthSegmentCount = 8;
        [SerializeField, Range(-10f, 10f)] private float hudHealthTiltDegrees = 3.5f;
        [SerializeField, Range(-10f, 10f)] private float hudHotbarTiltDegrees = -3.5f;
        [SerializeField] private bool hudHealthReverseSlant;
        [SerializeField] private bool hudHotbarReverseSlant = true;
        [SerializeField, Range(0f, 24f)] private float hudElementSlant = 9f;
        [SerializeField, Range(0f, 12f)] private float hudExtrusionDepth = 5f;

        [Header("Gameplay HUD Palette")]
        [SerializeField] private Color hudPrimary = new Color(0.96f, 0.98f, 1f, 1f);
        [SerializeField] private Color hudSurface = new Color(0.035f, 0.045f, 0.055f, 0.84f);
        [SerializeField] private Color hudMuted = new Color(0.96f, 0.98f, 1f, 0.2f);
        [SerializeField] private Color hudShadow = new Color(0f, 0f, 0f, 0.72f);
        [SerializeField] private Color hudDanger = new Color(0.92f, 0.18f, 0.14f, 1f);

        [Header("Mission Layout")]
        [SerializeField] private Vector2 missionObjectivePosition =
            new Vector2(30f, -30f);
        [SerializeField] private Vector2 missionObjectiveSize =
            new Vector2(600f, 160f);
        [SerializeField] private Vector2 missionPromptPosition =
            new Vector2(0f, 112f);
        [SerializeField] private Vector2 missionPromptSize =
            new Vector2(1100f, 70f);
        [SerializeField] private Vector2 missionTimerPosition =
            new Vector2(0f, -92f);
        [SerializeField] private Vector2 missionTimerSize =
            new Vector2(180f, 62f);
        [SerializeField] private Vector2 missionResultPadding =
            new Vector2(200f, 100f);
        [SerializeField, Min(12)] private int missionObjectiveFontSize = 28;
        [SerializeField, Min(12)] private int missionPromptFontSize = 25;
        [SerializeField, Min(12)] private int missionTimerFontSize = 28;
        [SerializeField, Min(20)] private int missionResultFontSize = 42;
        [SerializeField] private Color missionResultBackdrop =
            new Color(0.015f, 0.025f, 0.035f, 0.96f);
        [SerializeField] private Color sceneFadeColor = Color.black;

        [Header("Mission Motion")]
        [SerializeField, Min(0f)] private float sceneFadeOutSeconds = 0.65f;
        [SerializeField, Min(0f)] private float sceneFadeInSeconds = 0.55f;

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
        public int HudSortingOrder => hudSortingOrder;
        public int CrosshairSortingOrder => crosshairSortingOrder;
        public int MissionOverlaySortingOrder => missionOverlaySortingOrder;
        public int LoadingSortingOrder => loadingSortingOrder;
        public int PauseSortingOrder => pauseSortingOrder;
        public bool ShowHealth => showHealth;
        public bool ShowHotbar => showHotbar;
        public bool ShowCrosshair => showCrosshair;
        public bool ShowCompass => showCompass;
        public bool ShowMissionObjective => showMissionObjective;
        public bool ShowMissionPrompt => showMissionPrompt;
        public bool ShowMissionTimer => showMissionTimer;
        public Vector2 CompassPosition => compassPosition;
        public Vector2 CompassSize => compassSize;
        public float CompassVisibleDegrees => compassVisibleDegrees;
        public Vector2 HudHealthPosition => hudHealthPosition;
        public Vector2 HudHealthSize => hudHealthSize;
        public Vector2 HudHotbarPosition => hudHotbarPosition;
        public Vector2 HudHotbarSize => hudHotbarSize;
        public int HudHealthSegmentCount => hudHealthSegmentCount;
        public float HudHealthTiltDegrees => hudHealthTiltDegrees;
        public float HudHotbarTiltDegrees => hudHotbarTiltDegrees;
        public bool HudHealthReverseSlant => hudHealthReverseSlant;
        public bool HudHotbarReverseSlant => hudHotbarReverseSlant;
        public float HudElementSlant => hudElementSlant;
        public float HudExtrusionDepth => hudExtrusionDepth;
        public Color HudPrimary => hudPrimary;
        public Color HudSurface => hudSurface;
        public Color HudMuted => hudMuted;
        public Color HudShadow => hudShadow;
        public Color HudDanger => hudDanger;
        public Vector2 MissionObjectivePosition => missionObjectivePosition;
        public Vector2 MissionObjectiveSize => missionObjectiveSize;
        public Vector2 MissionPromptPosition => missionPromptPosition;
        public Vector2 MissionPromptSize => missionPromptSize;
        public Vector2 MissionTimerPosition => missionTimerPosition;
        public Vector2 MissionTimerSize => missionTimerSize;
        public Vector2 MissionResultPadding => missionResultPadding;
        public int MissionObjectiveFontSize => missionObjectiveFontSize;
        public int MissionPromptFontSize => missionPromptFontSize;
        public int MissionTimerFontSize => missionTimerFontSize;
        public int MissionResultFontSize => missionResultFontSize;
        public Color MissionResultBackdrop => missionResultBackdrop;
        public Color SceneFadeColor => sceneFadeColor;
        public float SceneFadeOutSeconds => sceneFadeOutSeconds;
        public float SceneFadeInSeconds => sceneFadeInSeconds;
    }
}
