using UnityEngine;

namespace Supernova.PortalExample
{
    [DisallowMultipleComponent]
    public sealed class PortalExampleHud : MonoBehaviour
    {
        [SerializeField] private PortalExampleFloorButton floorButton;

        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle objectiveStyle;
        private Texture2D panelTexture;

        private void OnGUI()
        {
            EnsureStyles();

            float width = Mathf.Min(820f, Screen.width - 40f);
            Rect panel = new Rect(
                (Screen.width - width) * 0.5f,
                18f,
                width,
                100f);
            GUI.Box(panel, GUIContent.none, bodyStyle);
            GUI.Label(
                new Rect(panel.x + 20f, panel.y + 12f, panel.width - 40f, 28f),
                "APERTURE-STYLE SPATIAL TRANSFER TEST / 空间传送测试",
                titleStyle);
            GUI.Label(
                new Rect(panel.x + 20f, panel.y + 43f, panel.width - 40f, 24f),
                "WASD 移动  ·  鼠标观察  ·  空格跳跃  ·  E 拿起/放下  ·  左键投掷  ·  R 重置  ·  Esc 释放鼠标",
                bodyStyle);

            string objective = floorButton != null && floorButton.IsPressed
                ? "✓ 测试方块已就位：出口门已开启"
                : "目标：将测试方块送过蓝色传送门，并放到橙色传送门旁的红色按钮上";
            GUI.Label(
                new Rect(panel.x + 20f, panel.y + 69f, panel.width - 40f, 24f),
                objective,
                objectiveStyle);

            float centerX = Screen.width * 0.5f;
            float centerY = Screen.height * 0.5f;
            Color previousColor = GUI.color;
            GUI.color = new Color(0.82f, 0.94f, 1f, 0.9f);
            GUI.DrawTexture(
                new Rect(centerX - 1f, centerY - 7f, 2f, 14f),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(centerX - 7f, centerY - 1f, 14f, 2f),
                Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            panelTexture = new Texture2D(1, 1)
            {
                name = "PortalExampleHudPanel"
            };
            panelTexture.SetPixel(0, 0, new Color(0.025f, 0.045f, 0.065f, 0.88f));
            panelTexture.Apply();

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.78f, 0.93f, 1f) }
            };
            bodyStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                normal =
                {
                    background = panelTexture,
                    textColor = new Color(0.88f, 0.91f, 0.94f)
                }
            };
            objectiveStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.72f, 0.25f) }
            };
        }

        private void OnDestroy()
        {
            if (panelTexture != null)
            {
                Destroy(panelTexture);
            }
        }
    }
}
