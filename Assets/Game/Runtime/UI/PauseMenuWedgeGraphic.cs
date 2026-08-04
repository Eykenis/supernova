using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Full-height pause-menu field with the diagonal leading edge used by the
    /// black-and-white HUD language. The shape is generated at runtime so it
    /// remains crisp at every canvas resolution.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [DisallowMultipleComponent]
    public sealed class PauseMenuWedgeGraphic : MaskableGraphic
    {
        public const float SystemFieldWidth = 920f;
        public const float SystemFieldTopInset = 260f;

        [SerializeField, Min(0f)] private float topInset = 310f;

        public float TopInset => topInset;

        public void Configure(float configuredTopInset, Color front)
        {
            topInset = Mathf.Max(0f, configuredTopInset);
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

            float safeInset = Mathf.Min(topInset, rect.width * 0.72f);
            Color32 vertexColor = color;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = vertexColor;

            vertex.position = new Vector2(rect.xMin, rect.yMin);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector2(rect.xMin + safeInset, rect.yMax);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector2(rect.xMax, rect.yMax);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector2(rect.xMax, rect.yMin);
            vertexHelper.AddVert(vertex);

            vertexHelper.AddTriangle(0, 1, 2);
            vertexHelper.AddTriangle(0, 2, 3);
        }
    }
}
