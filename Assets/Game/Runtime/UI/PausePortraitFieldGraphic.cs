using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Opaque left-hand pause field whose leading edge exactly complements the
    /// translucent system wedge. It also supplies the stencil geometry used to
    /// clip the portrait RenderTexture to that diagonal edge.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [DisallowMultipleComponent]
    public sealed class PausePortraitFieldGraphic : MaskableGraphic
    {
        [SerializeField, Min(0f)] private float bottomEdgeFromLeft = 1000f;
        [SerializeField, Min(0f)] private float topEdgeFromLeft = 1260f;

        public float BottomEdgeFromLeft => bottomEdgeFromLeft;
        public float TopEdgeFromLeft => topEdgeFromLeft;

        public void Configure(
            float configuredBottomEdgeFromLeft,
            float configuredTopEdgeFromLeft,
            Color front)
        {
            bottomEdgeFromLeft = Mathf.Max(0f, configuredBottomEdgeFromLeft);
            topEdgeFromLeft = Mathf.Max(0f, configuredTopEdgeFromLeft);
            color = front;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            float bottomX = rect.xMin
                + Mathf.Clamp(bottomEdgeFromLeft, 0f, rect.width);
            float topX = rect.xMin
                + Mathf.Clamp(topEdgeFromLeft, 0f, rect.width);
            Color32 vertexColor = color;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = vertexColor;

            vertex.position = new Vector2(rect.xMin, rect.yMin);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector2(bottomX, rect.yMin);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector2(topX, rect.yMax);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector2(rect.xMin, rect.yMax);
            vertexHelper.AddVert(vertex);

            vertexHelper.AddTriangle(0, 1, 2);
            vertexHelper.AddTriangle(0, 2, 3);
        }
    }
}
