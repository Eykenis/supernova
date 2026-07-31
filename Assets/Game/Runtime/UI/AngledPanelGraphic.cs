using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Resolution-independent slanted HUD plate with a small offset back face.
    /// It gives runtime widgets depth without relying on a baked sprite.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [DisallowMultipleComponent]
    public sealed class AngledPanelGraphic : MaskableGraphic
    {
        [SerializeField, Min(0f)] private float slant = 10f;
        [SerializeField, Min(0f)] private float extrusionDepth = 5f;
        [SerializeField] private bool reverse;
        [SerializeField] private Color depthColor = new Color(0f, 0f, 0f, 0.72f);
        [SerializeField] private Color highlightColor = new Color(1f, 1f, 1f, 0.4f);

        public float Slant => slant;
        public float Depth => extrusionDepth;
        public bool Reverse => reverse;

        public void Configure(
            float configuredSlant,
            float configuredDepth,
            Color front,
            Color back,
            Color highlight,
            bool reverseDirection = false)
        {
            slant = Mathf.Max(0f, configuredSlant);
            extrusionDepth = Mathf.Max(0f, configuredDepth);
            reverse = reverseDirection;
            color = front;
            depthColor = back;
            highlightColor = highlight;
            raycastTarget = false;
            SetVerticesDirty();
        }

        public void SetFrontColor(Color value)
        {
            color = value;
            SetVerticesDirty();
        }

        public void SetDepthColor(Color value)
        {
            depthColor = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            // The authored HUD canvas is a Screen Space canvas whose serialized transform
            // scale is zero. PixelAdjustRect can therefore collapse custom geometry even
            // though built-in UGUI graphics still render. Local rect space is the stable
            // source for CanvasRenderer vertices.
            Rect rect = rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            float safeDepth = Mathf.Min(extrusionDepth, rect.height * 0.35f);
            float safeSlant = Mathf.Min(slant, rect.width * 0.35f);
            Vector2 backOffset = new Vector2(
                reverse ? safeDepth : -safeDepth,
                -safeDepth);

            GetFrontCorners(
                rect,
                safeSlant,
                safeDepth,
                out Vector2 bottomLeft,
                out Vector2 topLeft,
                out Vector2 topRight,
                out Vector2 bottomRight);

            AddQuad(
                vertexHelper,
                bottomLeft + backOffset,
                topLeft + backOffset,
                topRight + backOffset,
                bottomRight + backOffset,
                depthColor);
            AddQuad(
                vertexHelper,
                bottomLeft,
                topLeft,
                topRight,
                bottomRight,
                color);

            float highlightThickness = Mathf.Clamp(rect.height * 0.045f, 1f, 3f);
            Vector2 edgeDirection = (topRight - topLeft).normalized;
            Vector2 edgeNormal = new Vector2(edgeDirection.y, -edgeDirection.x)
                * highlightThickness;
            AddQuad(
                vertexHelper,
                topLeft,
                topLeft + edgeNormal,
                topRight + edgeNormal,
                topRight,
                highlightColor);
        }

        private void GetFrontCorners(
            Rect rect,
            float safeSlant,
            float safeDepth,
            out Vector2 bottomLeft,
            out Vector2 topLeft,
            out Vector2 topRight,
            out Vector2 bottomRight)
        {
            float bottom = rect.yMin + safeDepth;
            if (reverse)
            {
                bottomLeft = new Vector2(rect.xMin + safeSlant, bottom);
                topLeft = new Vector2(rect.xMin, rect.yMax);
                topRight = new Vector2(rect.xMax - safeSlant, rect.yMax);
                bottomRight = new Vector2(rect.xMax, bottom);
                return;
            }

            bottomLeft = new Vector2(rect.xMin, bottom);
            topLeft = new Vector2(rect.xMin + safeSlant, rect.yMax);
            topRight = new Vector2(rect.xMax, rect.yMax);
            bottomRight = new Vector2(rect.xMax - safeSlant, bottom);
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
