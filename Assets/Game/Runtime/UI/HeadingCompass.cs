using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Keeps a top-screen compass strip centered on the active gameplay camera heading.
    /// Unity forward (+Z) is treated as north and headings increase clockwise.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeadingCompass : MonoBehaviour
    {
        public const float TickIntervalDegrees = 5f;
        public const int TickViewCount = 31;

        [SerializeField] private Transform headingSource;
        [SerializeField] private RectTransform tickContainer;
        [SerializeField] private TMP_Text currentHeadingLabel;
        [SerializeField, Range(60f, 180f)] private float visibleDegrees = 105f;

        private RectTransform[] tickViews = new RectTransform[0];
        private CanvasGroup[] tickGroups = new CanvasGroup[0];
        private Image[] tickLines = new Image[0];
        private TMP_Text[] tickLabels = new TMP_Text[0];
        private int displayedHeading = -1;

        public Transform HeadingSource => headingSource;
        public float CurrentHeading { get; private set; }

        public void Configure(Transform source, UiDesignTokens designTokens)
        {
            if (source != null)
                headingSource = source;
            if (designTokens != null)
                visibleDegrees = designTokens.CompassVisibleDegrees;

            CacheViewReferences();
            RefreshHeading(headingSource != null
                ? headingSource.eulerAngles.y
                : CurrentHeading);
        }

        public void SetHeadingSource(Transform source)
        {
            headingSource = source;
        }

        public void RefreshHeading(float headingDegrees)
        {
            CacheViewReferences();
            CurrentHeading = NormalizeHeading(headingDegrees);

            int roundedCurrentHeading = Mathf.RoundToInt(CurrentHeading) % 360;
            if (currentHeadingLabel != null
                && roundedCurrentHeading != displayedHeading)
            {
                displayedHeading = roundedCurrentHeading;
                currentHeadingLabel.text =
                    roundedCurrentHeading.ToString("000") + "\u00B0";
            }
            if (tickContainer == null || tickViews.Length == 0)
                return;

            float width = tickContainer.rect.width;
            if (width <= 0f)
                width = ((RectTransform)transform).rect.width;
            float pixelsPerDegree = width / Mathf.Max(1f, visibleDegrees);
            int halfCount = tickViews.Length / 2;
            float firstHeading =
                Mathf.Floor(CurrentHeading / TickIntervalDegrees)
                * TickIntervalDegrees
                - halfCount * TickIntervalDegrees;

            for (int i = 0; i < tickViews.Length; i++)
            {
                float tickHeading = NormalizeHeading(
                    firstHeading + i * TickIntervalDegrees);
                float delta = Mathf.DeltaAngle(CurrentHeading, tickHeading);
                Vector2 position = tickViews[i].anchoredPosition;
                position.x = delta * pixelsPerDegree;
                tickViews[i].anchoredPosition = position;

                int roundedHeading = Mathf.RoundToInt(tickHeading) % 360;
                bool cardinal = roundedHeading % 90 == 0;
                bool intercardinal = roundedHeading % 45 == 0;
                bool numbered = roundedHeading % 15 == 0;
                int tickHeight = cardinal ? 18 : intercardinal ? 14 : numbered ? 10 : 6;

                RectTransform lineRect = (RectTransform)tickLines[i].transform;
                lineRect.sizeDelta = new Vector2(cardinal ? 2f : 1f, tickHeight);
                tickLabels[i].text = numbered || cardinal || intercardinal
                    ? GetHeadingLabel(roundedHeading)
                    : string.Empty;
                tickLabels[i].fontSize = cardinal
                    ? 18f
                    : intercardinal
                        ? 12f
                        : 10f;

                float edge = Mathf.Abs(delta) / (visibleDegrees * 0.5f);
                tickGroups[i].alpha = 1f - Mathf.SmoothStep(0.48f, 1f, edge);
            }
        }

        public static float NormalizeHeading(float headingDegrees)
        {
            float normalized = Mathf.Repeat(headingDegrees, 360f);
            return Mathf.Approximately(normalized, 360f) ? 0f : normalized;
        }

        public static string GetHeadingLabel(int headingDegrees)
        {
            int normalized = Mathf.RoundToInt(NormalizeHeading(headingDegrees));
            switch (normalized)
            {
                case 0:
                    return "N";
                case 45:
                    return "NE";
                case 90:
                    return "E";
                case 135:
                    return "SE";
                case 180:
                    return "S";
                case 225:
                    return "SW";
                case 270:
                    return "W";
                case 315:
                    return "NW";
                default:
                    return normalized.ToString();
            }
        }

        private void LateUpdate()
        {
            if (headingSource == null)
            {
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                    headingSource = mainCamera.transform;
            }

            if (headingSource != null)
                RefreshHeading(headingSource.eulerAngles.y);
        }

        private void CacheViewReferences()
        {
            if (tickContainer == null)
            {
                Transform viewport = transform.Find(
                    UiHierarchyPaths.Hud.CompassViewportName);
                tickContainer = viewport != null
                    ? viewport.Find(UiHierarchyPaths.Hud.CompassTicksName)
                        as RectTransform
                    : null;
            }

            if (currentHeadingLabel == null)
            {
                Transform heading = transform.Find(
                    UiHierarchyPaths.Hud.CompassHeadingName);
                if (heading != null)
                    currentHeadingLabel = heading.GetComponent<TMP_Text>();
            }

            if (tickContainer == null
                || tickViews.Length == tickContainer.childCount)
            {
                return;
            }

            int count = tickContainer.childCount;
            tickViews = new RectTransform[count];
            tickGroups = new CanvasGroup[count];
            tickLines = new Image[count];
            tickLabels = new TMP_Text[count];
            for (int i = 0; i < count; i++)
            {
                Transform tick = tickContainer.GetChild(i);
                tickViews[i] = tick as RectTransform;
                tickGroups[i] = tick.GetComponent<CanvasGroup>();
                tickLines[i] = tick.Find(UiHierarchyPaths.Hud.CompassTickLine)
                    .GetComponent<Image>();
                tickLabels[i] = tick.Find(UiHierarchyPaths.Hud.CompassTickLabel)
                    .GetComponent<TMP_Text>();
            }
        }
    }
}
