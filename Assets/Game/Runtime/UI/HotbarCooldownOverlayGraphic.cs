using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Draws the remaining portion of a hotbar cooldown inside the same slanted
    /// silhouette as the slot surface. The fill retreats from top to bottom as
    /// the tool becomes ready.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [DisallowMultipleComponent]
    public sealed class HotbarCooldownOverlayGraphic : MaskableGraphic
    {
        [SerializeField, Range(0f, 1f)] private float fillAmount;
        [SerializeField, Min(0f)] private float slant = 10f;
        [SerializeField, Min(0f)] private float extrusionDepth = 5f;
        [SerializeField] private bool reverse;

        public float FillAmount => fillAmount;

        public void Configure(
            float configuredSlant,
            float configuredDepth,
            Color overlayColor,
            bool reverseDirection)
        {
            slant = Mathf.Max(0f, configuredSlant);
            extrusionDepth = Mathf.Max(0f, configuredDepth);
            reverse = reverseDirection;
            color = overlayColor;
            raycastTarget = false;
            SetVerticesDirty();
        }

        public void SetFillAmount(float value)
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(fillAmount, clamped))
                return;

            fillAmount = clamped;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = rectTransform.rect;
            if (fillAmount <= 0f || rect.width <= 0f || rect.height <= 0f)
                return;

            float safeDepth = Mathf.Min(extrusionDepth, rect.height * 0.35f);
            float safeSlant = Mathf.Min(slant, rect.width * 0.35f);
            float bottom = rect.yMin + safeDepth;

            Vector2 bottomLeft;
            Vector2 topLeft;
            Vector2 topRight;
            Vector2 bottomRight;
            if (reverse)
            {
                bottomLeft = new Vector2(rect.xMin + safeSlant, bottom);
                topLeft = new Vector2(rect.xMin, rect.yMax);
                topRight = new Vector2(rect.xMax - safeSlant, rect.yMax);
                bottomRight = new Vector2(rect.xMax, bottom);
            }
            else
            {
                bottomLeft = new Vector2(rect.xMin, bottom);
                topLeft = new Vector2(rect.xMin + safeSlant, rect.yMax);
                topRight = new Vector2(rect.xMax, rect.yMax);
                bottomRight = new Vector2(rect.xMax - safeSlant, bottom);
            }

            Vector2 filledLeft = Vector2.Lerp(bottomLeft, topLeft, fillAmount);
            Vector2 filledRight = Vector2.Lerp(bottomRight, topRight, fillAmount);
            AddQuad(
                vertexHelper,
                bottomLeft,
                filledLeft,
                filledRight,
                bottomRight,
                color);
        }

        private static void AddQuad(
            VertexHelper vertexHelper,
            Vector2 bottomLeft,
            Vector2 topLeft,
            Vector2 topRight,
            Vector2 bottomRight,
            Color vertexColor)
        {
            int startIndex = vertexHelper.currentVertCount;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = vertexColor;

            vertex.position = bottomLeft;
            vertexHelper.AddVert(vertex);
            vertex.position = topLeft;
            vertexHelper.AddVert(vertex);
            vertex.position = topRight;
            vertexHelper.AddVert(vertex);
            vertex.position = bottomRight;
            vertexHelper.AddVert(vertex);

            vertexHelper.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
            vertexHelper.AddTriangle(startIndex, startIndex + 2, startIndex + 3);
        }
    }
}
