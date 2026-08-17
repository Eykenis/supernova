using System.Collections.Generic;
using Supernova.Inputs;
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

        private sealed class RendererPresentation
        {
            public RendererPresentation(
                MeshRenderer renderer,
                Material[] solidMaterials,
                Material[] wireframeMaterials)
            {
                Renderer = renderer;
                SolidMaterials = solidMaterials;
                WireframeMaterials = wireframeMaterials;
                SolidShadowCastingMode = renderer.shadowCastingMode;
                SolidReceivesShadows = renderer.receiveShadows;
            }

            public MeshRenderer Renderer { get; }
            public Material[] SolidMaterials { get; }
            public Material[] WireframeMaterials { get; }
            public ShadowCastingMode SolidShadowCastingMode { get; }
            public bool SolidReceivesShadows { get; }
        }

        private readonly List<RendererPresentation> renderers =
            new List<RendererPresentation>();

        private ShopProductProfile profile;
        private Camera worldCamera;
        private RectTransform labelCanvasRect;
        private Text label;
        private bool isTargeted;
        private GameObject plateObject;
        private GameObject lightObject;
        private Transform displayRoot;
        private GameObject modelInstance;

        [SerializeField] private float displaySpinDegreesPerSecond = 24f;

        public ShopProductProfile Profile => profile;
        public Collider TargetCollider { get; private set; }
        public Text Label => label;
        public bool IsTargeted => isTargeted;
        public bool IsOwned =>
            profile != null && PlayerEconomy.IsProductOwned(profile);
        public int SolidRendererCount => renderers.Count;
        public int WireframeRendererCount => renderers.Count;
        public bool IsShowingSolid =>
            IsOwned && HasEnabledRenderer();
        public bool IsShowingWireframe =>
            profile != null && !IsOwned && HasEnabledRenderer();

        private void OnEnable()
        {
            PlayerEconomy.CreditsChanged += HandleCreditsChanged;
            PlayerEconomy.ItemOwnershipChanged +=
                HandleItemOwnershipChanged;
            PlayerEconomy.UpgradeOwnershipChanged +=
                HandleUpgradeOwnershipChanged;
            RefreshView();
        }

        private void OnDisable()
        {
            PlayerEconomy.CreditsChanged -= HandleCreditsChanged;
            PlayerEconomy.ItemOwnershipChanged -=
                HandleItemOwnershipChanged;
            PlayerEconomy.UpgradeOwnershipChanged -=
                HandleUpgradeOwnershipChanged;
        }

        private void LateUpdate()
        {
            if (displayRoot != null)
            {
                displayRoot.Rotate(
                    Vector3.up,
                    displaySpinDegreesPerSecond * Time.deltaTime,
                    Space.World);
            }

            if (labelCanvasRect == null || !isTargeted)
                return;

            Camera camera = ResolveCamera();
            if (camera != null)
                labelCanvasRect.rotation = camera.transform.rotation;
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
            ApplyRendererState(owned);

            if (label == null)
                return;

            if (owned)
            {
                label.text = "已拥有";
                label.color = WorldValueTextStyle.OwnedColor;
            }
            else
            {
                InputPromptTextRuntime.SetText(
                    label,
                    "$" + PlayerEconomy.GetCurrentPrice(profile)
                        + " 按 {{input:Gameplay/Interact}} 购买");
                label.color = PlayerEconomy.CanAfford(profile)
                    ? WorldValueTextStyle.ValueColor
                    : WorldValueTextStyle.LossColor;
            }
        }

        private void BuildView()
        {
            ClearGeneratedView();
            if (profile == null)
                return;

            BuildPlate();
            BuildPickupLight();

            MeshRenderer[] modelRenderers = new MeshRenderer[0];
            if (profile.DisplayPrefab != null)
            {
                var displayObject = new GameObject("Product Display");
                displayRoot = displayObject.transform;
                displayRoot.SetParent(transform, false);
                displayRoot.localPosition = new Vector3(0f, 0.65f, 0f);

                modelInstance = Instantiate(
                    profile.DisplayPrefab,
                    displayRoot,
                    false);
                modelInstance.name = profile.DisplayName + " Display";
                PrepareModelInstance();
                Transform modelTransform = modelInstance.transform;
                modelTransform.localPosition = profile.DisplayLocalPosition;
                modelTransform.localRotation =
                    Quaternion.Euler(profile.DisplayLocalEulerAngles);
                modelTransform.localScale = profile.DisplayLocalScale;

                modelRenderers =
                    modelInstance.GetComponentsInChildren<MeshRenderer>(true);
                CacheRendererPresentations(modelRenderers);
            }

            Bounds localBounds = CalculateLocalBounds(modelRenderers);
            Renderer plateRenderer = plateObject != null
                ? plateObject.GetComponent<Renderer>()
                : null;
            if (plateRenderer != null)
                EncapsulateRendererBounds(ref localBounds, plateRenderer);
            BoxCollider target = gameObject.AddComponent<BoxCollider>();
            target.center = localBounds.center;
            target.size = localBounds.size;
            TargetCollider = target;
            BuildLabel(localBounds);
        }

        private void PrepareModelInstance()
        {
            Rigidbody[] bodies =
                modelInstance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.useGravity = false;
                body.isKinematic = true;
            }

            Collider[] colliders =
                modelInstance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }

        private void BuildPlate()
        {
            plateObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plateObject.name = "Pickup Plate";
            plateObject.transform.SetParent(transform, false);
            plateObject.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            plateObject.transform.localScale = new Vector3(0.7f, 0.08f, 0.7f);

            Collider plateCollider = plateObject.GetComponent<Collider>();
            if (plateCollider != null)
            {
                if (Application.isPlaying)
                    Destroy(plateCollider);
                else
                    DestroyImmediate(plateCollider);
            }
        }

        private void BuildPickupLight()
        {
            lightObject = new GameObject("Pickup Light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.8f, 0f);

            Light pickupLight = lightObject.AddComponent<Light>();
            pickupLight.type = LightType.Point;
            pickupLight.color = new Color(0.15f, 0.75f, 1f);
            pickupLight.range = 2.5f;
            pickupLight.intensity = 0.8f;
            pickupLight.shadows = LightShadows.None;
        }

        private void ClearGeneratedView()
        {
            renderers.Clear();
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
            if (displayRoot != null)
            {
                if (Application.isPlaying)
                    Destroy(displayRoot.gameObject);
                else
                    DestroyImmediate(displayRoot.gameObject);
                displayRoot = null;
                modelInstance = null;
            }
            if (plateObject != null)
            {
                if (Application.isPlaying)
                    Destroy(plateObject);
                else
                    DestroyImmediate(plateObject);
                plateObject = null;
            }
            if (lightObject != null)
            {
                if (Application.isPlaying)
                    Destroy(lightObject);
                else
                    DestroyImmediate(lightObject);
                lightObject = null;
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

        private void CacheRendererPresentations(
            MeshRenderer[] modelRenderers)
        {
            for (int i = 0; i < modelRenderers.Length; i++)
            {
                MeshRenderer renderer = modelRenderers[i];
                if (renderer == null)
                    continue;

                Material[] solidMaterials = renderer.sharedMaterials;
                Material[] wireframeMaterials =
                    new Material[solidMaterials.Length];
                for (int materialIndex = 0;
                     materialIndex < wireframeMaterials.Length;
                     materialIndex++)
                {
                    wireframeMaterials[materialIndex] =
                        profile.WireframeMaterial;
                }

                renderers.Add(new RendererPresentation(
                    renderer,
                    solidMaterials,
                    wireframeMaterials));
            }
        }

        private void ApplyRendererState(bool owned)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                RendererPresentation presentation = renderers[i];
                MeshRenderer renderer = presentation.Renderer;
                if (renderer == null)
                    continue;

                renderer.enabled = true;
                renderer.sharedMaterials = owned
                    ? presentation.SolidMaterials
                    : presentation.WireframeMaterials;
                renderer.shadowCastingMode = owned
                    ? presentation.SolidShadowCastingMode
                    : ShadowCastingMode.Off;
                renderer.receiveShadows = owned
                    && presentation.SolidReceivesShadows;
            }
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

        private void EncapsulateRendererBounds(
            ref Bounds localBounds,
            Renderer renderer)
        {
            Bounds worldBounds = renderer.bounds;
            Vector3 center = worldBounds.center;
            Vector3 extents = worldBounds.extents;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 worldCorner = center + new Vector3(
                    (corner & 1) == 0 ? -extents.x : extents.x,
                    (corner & 2) == 0 ? -extents.y : extents.y,
                    (corner & 4) == 0 ? -extents.z : extents.z);
                localBounds.Encapsulate(
                    transform.InverseTransformPoint(worldCorner));
            }
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
            if (profile != null
                && profile.GrantType == ShopProductGrantType.InventoryItem
                && profile.GrantedItem == item)
                RefreshView();
        }

        private void HandleUpgradeOwnershipChanged(
            Gameplay.PlayerUpgrade upgrade,
            bool owned)
        {
            if (profile != null
                && profile.GrantType == ShopProductGrantType.Upgrade
                && profile.GrantedUpgrade == upgrade)
            {
                RefreshView();
            }
        }

        private bool HasEnabledRenderer()
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                MeshRenderer renderer = renderers[i].Renderer;
                if (renderer != null && renderer.enabled)
                    return true;
            }

            return false;
        }
    }
}
