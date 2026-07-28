using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>Shared typography and colors for world-space value labels.</summary>
    public static class WorldValueTextStyle
    {
        private static readonly string[] PreferredLocalizedFonts =
        {
            "Microsoft YaHei UI",
            "Noto Sans SC",
            "Noto Sans CJK SC",
            "Arial Unicode MS",
            "Arial",
        };

        private static Font localizedFont;

        public const float CanvasScale = 0.005f;
        public const float FontSize = 28f;

        public static readonly Color ValueColor =
            new Color(0.24f, 1f, 0.38f, 1f);
        public static readonly Color LossColor =
            new Color(1f, 0.2f, 0.16f, 1f);
        public static readonly Color OwnedColor =
            new Color(0.55f, 0.59f, 0.62f, 1f);

        public static void ApplyValueLabel(
            TextMeshProUGUI label,
            Color color)
        {
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = FontSize;
            label.fontStyle = FontStyles.Bold;
            label.color = color;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.outlineColor = new Color32(0, 0, 0, 230);
            label.outlineWidth = 0.22f;

            Outline outline = label.GetComponent<Outline>();
            if (outline == null)
                return;

            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
        }

        public static void ApplyValueLabel(
            Text label,
            Color color)
        {
            Font font = ResolveLocalizedFont();
            if (font != null)
                label.font = font;
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = Mathf.RoundToInt(FontSize);
            label.fontStyle = FontStyle.Bold;
            label.color = color;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;

            Outline outline = label.GetComponent<Outline>();
            if (outline == null)
                return;

            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
        }

        private static Font ResolveLocalizedFont()
        {
            if (localizedFont != null)
                return localizedFont;

            localizedFont =
                Font.CreateDynamicFontFromOSFont(
                    PreferredLocalizedFonts,
                    Mathf.RoundToInt(FontSize));
            if (localizedFont != null)
                localizedFont.name = "Runtime World Value CJK Font";
            return localizedFont;
        }
    }
}
