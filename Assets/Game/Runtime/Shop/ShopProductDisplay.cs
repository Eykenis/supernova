using System;
using System.Collections.Generic;
using Supernova.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Supernova.Shop
{
    /// <summary>
    /// Owns one shop product's solid/wireframe presentation and targeted world label.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopProductDisplay : MonoBehaviour
    {
        private const float LabelPadding = 0.25f;

        private readonly List<MeshRenderer> solidRenderers =
            new List<MeshRenderer>();
        private readonly List<MeshRenderer> wireframeRenderers =
            new List<MeshRenderer>();
        private readonly List<Mesh> generatedWireframeMeshes =
            new List<Mesh>();

        private ShopProductProfile profile;
        private Camera worldCamera;
        private RectTransform labelCanvasRect;
        private Text label;
        private bool isTargeted;
        private GameObject modelInstance;

        public ShopProductProfile Profile => profile;
        public Collider TargetCollider { get; private set; }
        public Text Label => label;
        public bool IsTargeted => isTargeted;
        public bool IsOwned =>
            profile != null && PlayerEconomy.IsProductOwned(profile);
        public int SolidRendererCount => solidRenderers.Count;
        public int WireframeRendererCount => wireframeRenderers.Count;
        public bool IsShowingSolid => HasEnabledRenderer(solidRenderers);
        public bool IsShowingWireframe =>
            HasEnabledRenderer(wireframeRenderers);

        private void OnEnable()
        {
            PlayerEconomy.CreditsChanged += HandleCreditsChanged;
            PlayerEconomy.ItemOwnershipChanged +=
                HandleItemOwnershipChanged;
            RefreshView();
        }

        private void OnDisable()
        {
            PlayerEconomy.CreditsChanged -= HandleCreditsChanged;
            PlayerEconomy.ItemOwnershipChanged -=
                HandleItemOwnershipChanged;
        }

        private void LateUpdate()
        {
            if (labelCanvasRect == null || !isTargeted)
                return;

            Camera camera = ResolveCamera();
            if (camera != null)
                labelCanvasRect.rotation = camera.transform.rotation;
        }

        private void OnDestroy()
        {
            DestroyGeneratedWireframeMeshes();
        }

        private void DestroyGeneratedWireframeMeshes()
        {
            for (int i = 0; i < generatedWireframeMeshes.Count; i++)
            {
                Mesh mesh = generatedWireframeMeshes[i];
                if (mesh == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(mesh);
                else
                    DestroyImmediate(mesh);
            }
            generatedWireframeMeshes.Clear();
        }

        public void Configure(
            ShopProductProfile product,
            Camera camera = null)
        {
            profile = product;
            worldCamera = camera;
            BuildView();
            RefreshView();
            SetTargeted(false);
        }

        public void SetTargeted(bool targeted)
        {
            isTargeted = targeted;
            if (labelCanvasRect != null)
                labelCanvasRect.gameObject.SetActive(targeted);
            if (targeted)
                RefreshView();
        }

        public void RefreshView()
        {
            if (profile == null)
                return;

            bool owned = PlayerEconomy.IsProductOwned(profile);
            for (int i = 0; i < solidRenderers.Count; i++)
            {
                if (solidRenderers[i] != null)
                    solidRenderers[i].enabled = owned;
            }
            for (int i = 0; i < wireframeRenderers.Count; i++)
            {
                if (wireframeRenderers[i] != null)
                    wireframeRenderers[i].enabled = !owned;
            }

            if (label == null)
                return;

            if (owned)
            {
                label.text = "已拥有";
                label.color = WorldValueTextStyle.OwnedColor;
            }
            else
            {
                label.text = $"${profile.Price}\n按 E 购买";
                label.color = PlayerEconomy.CanAfford(profile)
                    ? WorldValueTextStyle.ValueColor
                    : WorldValueTextStyle.LossColor;
            }
        }

        private void BuildView()
        {
            ClearGeneratedView();
            if (profile == null || profile.DisplayPrefab == null)
                return;

            modelInstance = Instantiate(
                profile.DisplayPrefab,
                transform,
                false);
            modelInstance.name = profile.DisplayName + " Display";
            Transform modelTransform = modelInstance.transform;
            modelTransform.localPosition = profile.DisplayLocalPosition;
            modelTransform.localRotation =
                Quaternion.Euler(profile.DisplayLocalEulerAngles);
            modelTransform.localScale = profile.DisplayLocalScale;

            MeshRenderer[] renderers =
                modelInstance.GetComponentsInChildren<MeshRenderer>(true);
            solidRenderers.AddRange(renderers);
            BuildWireframes();

            Bounds localBounds = CalculateLocalBounds(renderers);
            BoxCollider target = gameObject.AddComponent<BoxCollider>();
            target.center = localBounds.center;
            target.size = localBounds.size;
            TargetCollider = target;
            BuildLabel(localBounds);
        }

        private void ClearGeneratedView()
        {
            DestroyGeneratedWireframeMeshes();
            solidRenderers.Clear();
            wireframeRenderers.Clear();
            if (TargetCollider != null)
            {
                if (Application.isPlaying)
                    Destroy(TargetCollider);
                else
                    DestroyImmediate(TargetCollider);
                TargetCollider = null;
            }
            if (modelInstance != null)
            {
                if (Application.isPlaying)
                    Destroy(modelInstance);
                else
                    DestroyImmediate(modelInstance);
                modelInstance = null;
            }
            if (labelCanvasRect != null)
            {
                if (Application.isPlaying)
                    Destroy(labelCanvasRect.gameObject);
                else
                    DestroyImmediate(labelCanvasRect.gameObject);
                labelCanvasRect = null;
                label = null;
            }
        }

        private void BuildWireframes()
        {
            MeshFilter[] filters =
                modelInstance.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter sourceFilter = filters[i];
                MeshRenderer sourceRenderer =
                    sourceFilter.GetComponent<MeshRenderer>();
                Mesh sourceMesh = sourceFilter.sharedMesh;
                if (sourceRenderer == null || sourceMesh == null)
                    continue;

                Mesh wireframeMesh = CreateWireframeMesh(sourceMesh);
                if (wireframeMesh == null)
                    continue;

                var wireframeObject = new GameObject(
                    sourceFilter.gameObject.name + " Wireframe",
                    typeof(MeshFilter),
                    typeof(MeshRenderer));
                wireframeObject.layer = sourceFilter.gameObject.layer;
                Transform wireframeTransform = wireframeObject.transform;
                wireframeTransform.SetParent(sourceFilter.transform, false);
                wireframeTransform.localPosition = Vector3.zero;
                wireframeTransform.localRotation = Quaternion.identity;
                wireframeTransform.localScale = Vector3.one;

                wireframeObject.GetComponent<MeshFilter>().sharedMesh =
                    wireframeMesh;
                MeshRenderer wireframeRenderer =
                    wireframeObject.GetComponent<MeshRenderer>();
                wireframeRenderer.sharedMaterial =
                    profile.WireframeMaterial;
                wireframeRenderer.shadowCastingMode =
                    ShadowCastingMode.Off;
                wireframeRenderer.receiveShadows = false;

                generatedWireframeMeshes.Add(wireframeMesh);
                wireframeRenderers.Add(wireframeRenderer);
            }
        }

        private static Mesh CreateWireframeMesh(Mesh source)
        {
            int[] triangles = source.triangles;
            if (triangles == null || triangles.Length < 3)
                return null;

            var edges = new HashSet<ulong>();
            var lineIndices = new List<int>(triangles.Length * 2);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                AddEdge(triangles[i], triangles[i + 1], edges, lineIndices);
                AddEdge(triangles[i + 1], triangles[i + 2], edges, lineIndices);
                AddEdge(triangles[i + 2], triangles[i], edges, lineIndices);
            }

            var mesh = new Mesh
            {
                name = source.name + " Shop Wireframe"
            };
            mesh.indexFormat = source.indexFormat;
            mesh.vertices = source.vertices;
            mesh.SetIndices(
                lineIndices.ToArray(),
                MeshTopology.Lines,
                0,
                true);
            return mesh;
        }

        private static void AddEdge(
            int a,
            int b,
            ISet<ulong> edges,
            ICollection<int> lineIndices)
        {
            uint minimum = (uint)Math.Min(a, b);
            uint maximum = (uint)Math.Max(a, b);
            ulong edgeKey = ((ulong)minimum << 32) | maximum;
            if (!edges.Add(edgeKey))
                return;

            lineIndices.Add(a);
            lineIndices.Add(b);
        }

        private Bounds CalculateLocalBounds(Renderer[] renderers)
        {
            bool found = false;
            Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                Bounds worldBounds = renderer.bounds;
                Vector3 center = worldBounds.center;
                Vector3 extents = worldBounds.extents;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 worldCorner = center + new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);
                    Vector3 localCorner =
                        transform.InverseTransformPoint(worldCorner);
                    if (!found)
                    {
                        localBounds = new Bounds(
                            localCorner,
                            Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localCorner);
                    }
                }
            }

            if (!found)
                localBounds = new Bounds(Vector3.zero, Vector3.one);
            localBounds.Expand(0.08f);
            return localBounds;
        }

        private void BuildLabel(Bounds localBounds)
        {
            var canvasObject = new GameObject(
                profile.DisplayName + " Shop UI",
                typeof(RectTransform),
                typeof(Canvas));
            canvasObject.layer = gameObject.layer;
            labelCanvasRect =
                canvasObject.GetComponent<RectTransform>();
            labelCanvasRect.SetParent(transform, false);
            labelCanvasRect.sizeDelta = new Vector2(220f, 88f);
            labelCanvasRect.localScale =
                Vector3.one * WorldValueTextStyle.CanvasScale;
            labelCanvasRect.localPosition = new Vector3(
                localBounds.center.x,
                localBounds.max.y + LabelPadding,
                localBounds.center.z);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 205;

            var labelObject = new GameObject(
                "Product State",
                typeof(RectTransform),
                typeof(Text),
                typeof(Outline));
            labelObject.layer = gameObject.layer;
            RectTransform labelRect =
                labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(labelCanvasRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            label = labelObject.GetComponent<Text>();
            WorldValueTextStyle.ApplyValueLabel(
                label,
                WorldValueTextStyle.ValueColor);
        }

        private Camera ResolveCamera()
        {
            if (worldCamera == null || !worldCamera.isActiveAndEnabled)
                worldCamera = Camera.main;
            if (worldCamera == null)
                worldCamera = FindObjectOfType<Camera>();
            return worldCamera;
        }

        private void HandleCreditsChanged(int credits)
        {
            RefreshView();
        }

        private void HandleItemOwnershipChanged(
            Gameplay.PlayerInventoryItem item,
            bool owned)
        {
            if (profile != null && profile.GrantedItem == item)
                RefreshView();
        }

        private static bool HasEnabledRenderer(
            IList<MeshRenderer> renderers)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] != null && renderers[i].enabled)
                    return true;
            }

            return false;
        }
    }
}
