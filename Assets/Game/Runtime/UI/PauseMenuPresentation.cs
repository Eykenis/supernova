using System.Collections.Generic;
using Supernova.Gameplay;
using Supernova.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Persona-inspired one-shot pause presentation. The portrait is rendered in an isolated
    /// world-space stage so the gameplay character and its materials are never modified.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PauseMenuPresentation : MonoBehaviour
    {
        private const float IntroDuration = 0.9f;

        private static readonly Color PortraitFieldColor = new Color32(4, 4, 6, 255);
        private static readonly Color InkColor = new Color32(8, 8, 10, 255);

        private RectTransform rootRect;
        private RectTransform inkSlash;
        private RectTransform foregroundRedSlash;
        private RectTransform paperSlash;
        private RectTransform portraitRect;
        private RectTransform menuRect;
        private CanvasGroup portraitGroup;
        private CanvasGroup menuGroup;
        private RawImage portraitImage;
        private Image backdrop;
        private TMP_Text title;
        private TMP_Text kicker;
        private Button backSlotButton;
        private TMP_Text backEquipmentName;
        private TMP_Text backEquipmentState;
        private TMP_Text backEquipmentHint;
        private PlayerEquipmentController equipmentSource;
        private bool visualsBuilt;
        private bool introPlaying;
        private float introElapsed;

        private GameObject renderStage;
        private GameObject portraitInstance;
        private Camera portraitCamera;
        private Animator portraitAnimator;
        private RenderTexture portraitTexture;
        private UiDesignTokens designTokens;
        private PausePortraitSettings portraitSettings;
        private RuntimeAnimatorController portraitController;
        private AnimatorOverrideController portraitOverrideController;
        private AnimationClip portraitPoseClip;
        private PauseCameraAnimationCurves portraitCameraAnimation;
        private float portraitHoldNormalizedTime = 0.995f;
        private float portraitYaw = -8f;
        private int poseSequence;
        private Vector3 portraitCameraBasePosition;
        private Quaternion portraitCameraBaseRotation;
        private float portraitCameraBaseFieldOfView;
        private Material bodyMaterial;
        private Material backgroundMaterial;
        private readonly List<Material> faceDetailMaterials = new List<Material>();
        private readonly List<PortraitRendererProxy> portraitRendererProxies =
            new List<PortraitRendererProxy>();
        private bool portraitRenderCallbackRegistered;
        private int portraitLayer = -1;
        private int portraitLayerMask;

        private Color OverlayBackdrop => designTokens != null
            ? designTokens.OverlayBackdrop
            : new Color(0.008f, 0.01f, 0.014f, 0.72f);
        private Color OverlaySurface => designTokens != null
            ? designTokens.OverlaySurface
            : new Color(1f, 1f, 1f, 0.055f);
        private Color OverlayPrimary => designTokens != null
            ? designTokens.OverlayPrimary
            : Color.white;
        private Color OverlaySecondary => designTokens != null
            ? designTokens.OverlaySecondary
            : new Color(1f, 1f, 1f, 0.58f);
        private Color OverlayDivider => designTokens != null
            ? designTokens.OverlayDivider
            : new Color(1f, 1f, 1f, 0.24f);
        private Color OverlayInverse => designTokens != null
            ? designTokens.OverlayInverse
            : new Color(0.018f, 0.02f, 0.025f, 1f);

        private sealed class PortraitRendererProxy
        {
            public Renderer Source;
            public SkinnedMeshRenderer SkinnedSource;
            public Transform ProxyTransform;
            public MeshRenderer ProxyRenderer;
            public Mesh BakedMesh;
        }

        public void BindEquipment(PlayerEquipmentController source)
        {
            equipmentSource = source;
            if (visualsBuilt)
            {
                BindEquipmentButton();
                RefreshEquipmentView();
            }
        }

        public void PlayIntro()
        {
            EnsureVisuals();
            if (Application.isPlaying)
            {
                EnsurePortrait();
                SelectNextPose();
            }

            introElapsed = 0f;
            introPlaying = true;
            SetIntroProgress(0f);
            if (Application.isPlaying)
                RestartPortraitAnimation();
        }

        public void StopPresentation()
        {
            introPlaying = false;
            if (portraitCamera != null)
                portraitCamera.enabled = false;
            if (portraitAnimator != null)
                portraitAnimator.enabled = false;
            SetPortraitProxiesVisible(false);
        }

        private void OnDisable()
        {
            StopPresentation();
        }

        private void OnDestroy()
        {
            UnregisterPortraitRenderCallback();
            if (backSlotButton != null)
                backSlotButton.onClick.RemoveListener(ToggleBackEquipment);
            if (portraitCamera != null)
                portraitCamera.targetTexture = null;
            if (portraitTexture != null)
            {
                portraitTexture.Release();
                Destroy(portraitTexture);
            }

            if (renderStage != null)
                Destroy(renderStage);
            if (portraitOverrideController != null)
                Destroy(portraitOverrideController);
            if (bodyMaterial != null)
                Destroy(bodyMaterial);
            if (backgroundMaterial != null)
                Destroy(backgroundMaterial);
            for (int i = 0; i < faceDetailMaterials.Count; i++)
            {
                if (faceDetailMaterials[i] != null)
                    Destroy(faceDetailMaterials[i]);
            }
            faceDetailMaterials.Clear();
            for (int i = 0; i < portraitRendererProxies.Count; i++)
            {
                if (portraitRendererProxies[i].BakedMesh != null)
                    Destroy(portraitRendererProxies[i].BakedMesh);
            }
            portraitRendererProxies.Clear();
        }

        private void Update()
        {
            if (!introPlaying)
                return;

            introElapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(introElapsed / IntroDuration);
            SetIntroProgress(progress);
            if (progress < 1f)
                return;

            introPlaying = false;
            FreezePortraitAtFinalPose();
        }

        private void EnsureVisuals()
        {
            if (visualsBuilt)
                return;

            rootRect = transform as RectTransform;
            if (rootRect == null)
                return;

            visualsBuilt = true;
            designTokens = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI.DesignTokens
                : null;
            backdrop = GetComponent<Image>();
            if (backdrop != null)
                backdrop.color = OverlayBackdrop;

            inkSlash = CreateImage("Portrait Field", transform, OverlaySurface);
            SetRect(inkSlash, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(390f, 0f), new Vector2(850f, 1080f));

            foregroundRedSlash = CreateImage("Portrait Divider", transform, OverlayPrimary);
            SetRect(foregroundRedSlash, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(872f, 0f), new Vector2(2f, 920f));

            paperSlash = CreateImage("Portrait Divider Echo", transform, OverlayDivider);
            SetRect(paperSlash, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(884f, 0f), new Vector2(1f, 620f));

            RectTransform portrait = CreateRect("Pause Portrait", transform);
            portraitRect = portrait;
            SetRect(portraitRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(420f, -8f), new Vector2(1040f, 1080f));
            portraitImage = portrait.gameObject.AddComponent<RawImage>();
            portraitImage.color = Color.white;
            portraitImage.raycastTarget = false;
            portraitGroup = portrait.gameObject.AddComponent<CanvasGroup>();
            portraitGroup.alpha = 0.22f;
            portraitGroup.interactable = false;
            portraitGroup.blocksRaycasts = false;

            menuRect = transform.Find(UiHierarchyPaths.Pause.Menu) as RectTransform;
            if (menuRect != null)
            {
                SetRect(menuRect, new Vector2(0.78f, 0.52f), new Vector2(0.78f, 0.52f),
                    new Vector2(0.5f, 0.5f), new Vector2(70f, -10f), new Vector2(560f, 650f));
                menuRect.localEulerAngles = Vector3.zero;
                menuGroup = menuRect.GetComponent<CanvasGroup>();
                if (menuGroup == null)
                    menuGroup = menuRect.gameObject.AddComponent<CanvasGroup>();

                Image menuImage = menuRect.GetComponent<Image>();
                if (menuImage != null)
                    menuImage.color = OverlaySurface;

                Outline menuOutline = menuRect.GetComponent<Outline>();
                if (menuOutline != null)
                {
                    menuOutline.effectColor = OverlayDivider;
                    menuOutline.effectDistance = new Vector2(1f, -1f);
                    menuOutline.useGraphicAlpha = false;
                }

                title = menuRect.Find(UiHierarchyPaths.Pause.Title) != null
                    ? menuRect.Find(UiHierarchyPaths.Pause.Title).GetComponent<TMP_Text>()
                    : null;
                if (title != null)
                {
                    title.text = "PAUSED";
                    title.fontSize = 42f;
                    title.fontStyle = FontStyles.Bold;
                    title.characterSpacing = 4f;
                    title.color = OverlayPrimary;
                    title.alignment = TextAlignmentOptions.Left;
                    SetRect((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(0f, 1f), new Vector2(30f, -26f), new Vector2(500f, 58f));
                }

                RectTransform resume = menuRect.Find(UiHierarchyPaths.Pause.Resume) as RectTransform;
                if (resume != null)
                {
                    SetRect(resume, new Vector2(1f, 0f), new Vector2(1f, 0f),
                        new Vector2(1f, 0f), new Vector2(-30f, 28f), new Vector2(500f, 58f));
                    Image resumeImage = resume.GetComponent<Image>();
                    if (resumeImage != null)
                        resumeImage.color = OverlayPrimary;

                    TMP_Text resumeLabel = resume.Find(UiHierarchyPaths.Pause.Label) != null
                        ? resume.Find(UiHierarchyPaths.Pause.Label).GetComponent<TMP_Text>()
                        : null;
                    if (resumeLabel != null)
                    {
                        resumeLabel.text = "RESUME  [ ESC ]";
                        resumeLabel.fontStyle = FontStyles.Bold;
                        resumeLabel.color = OverlayInverse;
                    }
                }

                backSlotButton = menuRect.Find(UiHierarchyPaths.Pause.BackSlot) != null
                    ? menuRect.Find(UiHierarchyPaths.Pause.BackSlot).GetComponent<Button>()
                    : null;
                if (backSlotButton != null)
                {
                    backEquipmentName = backSlotButton.transform.Find(UiHierarchyPaths.Pause.EquipmentName)
                        ?.GetComponent<TMP_Text>();
                    backEquipmentState = backSlotButton.transform.Find(UiHierarchyPaths.Pause.State)
                        ?.GetComponent<TMP_Text>();
                    backEquipmentHint = backSlotButton.transform.Find(UiHierarchyPaths.Pause.Hint)
                        ?.GetComponent<TMP_Text>();
                    BindEquipmentButton();
                    RefreshEquipmentView();
                }
            }

            kicker = CreateText("Pause Kicker", transform, "SUPERNOVA  //  FIELD PAUSE");
            SetRect((RectTransform)kicker.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-46f, -36f), new Vector2(430f, 50f));
            kicker.alignment = TextAlignmentOptions.Right;
            kicker.fontSize = 20f;
            kicker.fontStyle = FontStyles.Bold;
            kicker.characterSpacing = 5f;
            kicker.color = OverlaySecondary;

            inkSlash.SetSiblingIndex(0);
            portraitRect.SetSiblingIndex(1);
            foregroundRedSlash.SetSiblingIndex(2);
            paperSlash.SetSiblingIndex(3);
            if (menuRect != null)
                menuRect.SetAsLastSibling();
            kicker.transform.SetAsLastSibling();
            SciFiUiSkin.ApplyPauseMenu(transform);
        }

        private void BindEquipmentButton()
        {
            if (backSlotButton == null)
                return;
            backSlotButton.onClick.RemoveListener(ToggleBackEquipment);
            backSlotButton.onClick.AddListener(ToggleBackEquipment);
        }

        private void ToggleBackEquipment()
        {
            equipmentSource?.ToggleBackEquipment();
            RefreshEquipmentView();
        }

        private void RefreshEquipmentView()
        {
            if (backSlotButton == null)
                return;

            PlayerEquipmentDefinition equipped = equipmentSource != null
                ? equipmentSource.EquippedBack
                : null;
            PlayerEquipmentDefinition available = equipmentSource != null
                ? equipmentSource.AvailableBack
                : null;
            PlayerEquipmentDefinition shown = equipped != null ? equipped : available;
            backSlotButton.interactable = shown != null;

            if (backEquipmentName != null)
                backEquipmentName.text = shown != null
                    ? shown.DisplayName.ToUpperInvariant()
                    : "NO EQUIPMENT";
            if (backEquipmentState != null)
            {
                backEquipmentState.text = equipped != null
                    ? "EQUIPPED  //  REMOVE"
                    : shown != null
                        ? "STOWED  //  EQUIP"
                        : "EMPTY";
                backEquipmentState.color = equipped != null
                    ? OverlayPrimary
                    : OverlaySecondary;
            }
            if (backEquipmentHint != null)
                backEquipmentHint.text = shown != null
                    ? shown.InteractionHint
                    : "NO BACK MODULE AVAILABLE";
        }

        private void EnsurePortrait()
        {
            if (portraitInstance != null || portraitImage == null)
                return;

            UiAssetReferences assets = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.UI
                : null;
            portraitSettings = assets != null
                ? assets.PausePortraitSettings
                : null;
            GameObject prefab = portraitSettings != null
                ? portraitSettings.PortraitPrefab
                : null;
            portraitController = portraitSettings != null
                ? portraitSettings.PoseController
                : null;
            Material bodyTemplate = assets != null
                ? assets.PauseBodyMaterial
                : null;
            Material backgroundTemplate = assets != null
                ? assets.PauseBackgroundMaterial
                : null;
            if (prefab == null || portraitController == null || bodyTemplate == null
                || backgroundTemplate == null)
            {
                Debug.LogWarning(
                    "Pause portrait assets are missing from the preloaded game asset catalog.");
                return;
            }

            bodyMaterial = new Material(bodyTemplate) { name = "Pause Body (Runtime)" };
            backgroundMaterial = new Material(backgroundTemplate) { name = "Pause Background (Runtime)" };
            bodyMaterial.SetColor("_Color", OverlayPrimary);
            bodyMaterial.SetColor("_OutlineColor", InkColor);
            backgroundMaterial.SetColor("_Color", PortraitFieldColor);
            backgroundMaterial.SetColor("_OutlineColor", PortraitFieldColor);

            renderStage = new GameObject("Pause Portrait Render Stage");
            renderStage.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(renderStage);
            renderStage.transform.position = new Vector3(5000f, -5000f, 5000f);
            portraitLayer = LayerMask.NameToLayer(UiLayerNames.PausePortrait);
            if (portraitLayer < 0)
            {
                Debug.LogError(
                    $"The required UI layer '{UiLayerNames.PausePortrait}' is missing.");
                Destroy(renderStage);
                renderStage = null;
                return;
            }
            portraitLayerMask = 1 << portraitLayer;
            renderStage.layer = portraitLayer;

            portraitInstance = Instantiate(prefab, renderStage.transform);
            portraitInstance.name = "Aki Pause Portrait";
            portraitInstance.transform.localPosition = Vector3.zero;
            portraitInstance.transform.localRotation = Quaternion.Euler(0f, portraitYaw, 0f);

            portraitAnimator = portraitInstance.GetComponentInChildren<Animator>(true);
            if (portraitAnimator != null)
            {
                portraitAnimator.runtimeAnimatorController = portraitController;
                portraitAnimator.applyRootMotion = false;
                portraitAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
                portraitAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            ConfigurePortraitCloth();

            CreatePortraitRendererProxies();
            CreatePortraitCamera();
            portraitImage.texture = portraitTexture;
        }

        private void ConfigurePortraitCloth()
        {
            MagicaCloth2.MagicaCloth[] clothComponents =
                portraitInstance.GetComponentsInChildren<MagicaCloth2.MagicaCloth>(true);
            for (int i = 0; i < clothComponents.Length; i++)
            {
                if (clothComponents[i] != null)
                {
                    clothComponents[i].SerializeData.updateMode =
                        MagicaCloth2.ClothUpdateMode.Unscaled;
                }
            }
        }

        private void SelectNextPose()
        {
            PausePoseDefinition pose = portraitSettings != null
                ? portraitSettings.SelectPose(poseSequence)
                : null;
            poseSequence++;

            portraitPoseClip = pose != null ? pose.Clip : null;
            portraitHoldNormalizedTime = pose != null
                ? pose.HoldNormalizedTime
                : 0.995f;
            portraitYaw = pose != null ? pose.PortraitYaw : -8f;
            portraitCameraAnimation = pose != null ? pose.CameraAnimation : null;
            if (portraitInstance != null)
                portraitInstance.transform.localRotation = Quaternion.Euler(0f, portraitYaw, 0f);
            RebuildPoseOverride();
        }

        private void RebuildPoseOverride()
        {
            if (portraitAnimator == null)
                return;

            portraitAnimator.runtimeAnimatorController = portraitController;
            if (portraitOverrideController != null)
            {
                Destroy(portraitOverrideController);
                portraitOverrideController = null;
            }
            portraitAnimator.runtimeAnimatorController = CreatePoseController();
        }

        private void CreatePortraitRendererProxies()
        {
            Renderer[] renderers = portraitInstance.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer sourceRenderer = renderers[rendererIndex];
                sourceRenderer.forceRenderingOff = true;
                if (sourceRenderer.name.IndexOf(
                        "helmet",
                        System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                GameObject proxyObject = new GameObject(
                    "Pause Silhouette - " + sourceRenderer.name,
                    typeof(MeshFilter),
                    typeof(MeshRenderer));
                proxyObject.layer = portraitLayer;
                proxyObject.transform.SetParent(renderStage.transform, false);
                MeshFilter proxyFilter = proxyObject.GetComponent<MeshFilter>();
                MeshRenderer proxyRenderer = proxyObject.GetComponent<MeshRenderer>();
                proxyRenderer.sharedMaterials = CreateSilhouetteMaterials(sourceRenderer);
                proxyRenderer.shadowCastingMode = ShadowCastingMode.Off;
                proxyRenderer.receiveShadows = false;
                proxyRenderer.forceRenderingOff = true;

                var proxy = new PortraitRendererProxy
                {
                    Source = sourceRenderer,
                    SkinnedSource = sourceRenderer as SkinnedMeshRenderer,
                    ProxyTransform = proxyObject.transform,
                    ProxyRenderer = proxyRenderer
                };

                if (proxy.SkinnedSource != null)
                {
                    proxy.SkinnedSource.updateWhenOffscreen = true;
                    proxy.BakedMesh = new Mesh
                    {
                        name = "Pause Silhouette Mesh - " + sourceRenderer.name
                    };
                    proxy.BakedMesh.MarkDynamic();
                    proxyFilter.sharedMesh = proxy.BakedMesh;
                }
                else
                {
                    MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                    proxyFilter.sharedMesh = sourceFilter != null
                        ? sourceFilter.sharedMesh
                        : null;
                }

                portraitRendererProxies.Add(proxy);
            }

            UpdatePortraitRendererProxies(false);
        }

        private Material[] CreateSilhouetteMaterials(Renderer sourceRenderer)
        {
            Material[] sourceMaterials = sourceRenderer.sharedMaterials;
            int materialCount = Mathf.Max(sourceMaterials.Length, GetSubMeshCount(sourceRenderer));
            materialCount = Mathf.Max(1, materialCount);
            Material[] silhouetteMaterials = new Material[materialCount];
            for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
            {
                Material sourceMaterial = materialIndex < sourceMaterials.Length
                    ? sourceMaterials[materialIndex]
                    : null;
                string materialName = sourceMaterial != null
                    ? sourceMaterial.name
                    : string.Empty;
                if (IsFaceDetailPart(materialName))
                {
                    silhouetteMaterials[materialIndex] =
                        CreateFaceDetailMaterial(sourceMaterial, materialName);
                }
                else
                {
                    silhouetteMaterials[materialIndex] =
                        IsBackgroundPart(sourceRenderer.name, materialName)
                            ? backgroundMaterial
                            : bodyMaterial;
                }
            }

            return silhouetteMaterials;
        }

        private static int GetSubMeshCount(Renderer targetRenderer)
        {
            if (targetRenderer is SkinnedMeshRenderer skinnedRenderer)
            {
                return skinnedRenderer.sharedMesh != null
                    ? skinnedRenderer.sharedMesh.subMeshCount
                    : 0;
            }

            MeshFilter meshFilter = targetRenderer.GetComponent<MeshFilter>();
            return meshFilter != null && meshFilter.sharedMesh != null
                ? meshFilter.sharedMesh.subMeshCount
                : 0;
        }

        private void UpdatePortraitRendererProxies(bool visible)
        {
            for (int i = 0; i < portraitRendererProxies.Count; i++)
            {
                PortraitRendererProxy proxy = portraitRendererProxies[i];
                if (proxy.Source == null || proxy.ProxyRenderer == null)
                    continue;

                bool sourceVisible = proxy.Source.enabled
                    && proxy.Source.gameObject.activeInHierarchy;
                if (proxy.SkinnedSource != null && proxy.BakedMesh != null && sourceVisible)
                {
                    proxy.SkinnedSource.BakeMesh(proxy.BakedMesh);
                    proxy.BakedMesh.RecalculateBounds();
                }

                proxy.ProxyTransform.SetPositionAndRotation(
                    proxy.Source.transform.position,
                    proxy.Source.transform.rotation);
                proxy.ProxyTransform.localScale = proxy.Source.transform.lossyScale;
                proxy.ProxyRenderer.forceRenderingOff = !visible || !sourceVisible;
            }
        }

        private void SetPortraitProxiesVisible(bool visible)
        {
            for (int i = 0; i < portraitRendererProxies.Count; i++)
            {
                MeshRenderer proxyRenderer = portraitRendererProxies[i].ProxyRenderer;
                if (proxyRenderer != null)
                    proxyRenderer.forceRenderingOff = !visible;
            }
        }

        private static bool IsBackgroundPart(string rendererName, string materialName)
        {
            string partName = (rendererName + " " + materialName).ToLowerInvariant();
            return partName.Contains("hair")
                || partName.Contains("headphone")
                || partName.Contains("visor")
                || partName.Contains("metal")
                || partName.Contains("collar");
        }

        private static bool IsFaceDetailPart(string materialName)
        {
            string lowerName = materialName.ToLowerInvariant();
            return lowerName.Contains("skin")
                || lowerName.Contains("eye")
                || lowerName.Contains("brow");
        }

        private Material CreateFaceDetailMaterial(
            Material sourceMaterial,
            string materialName)
        {
            Material detailMaterial = new Material(bodyMaterial)
            {
                name = "Pause Face Detail - " + materialName + " (Runtime)"
            };
            detailMaterial.SetColor("_FeatureColor", InkColor);
            detailMaterial.SetFloat("_UseTextureMask", 1f);
            detailMaterial.SetFloat("_FeatureThreshold", 0.22f);
            detailMaterial.SetFloat("_FeatureSoftness", 0.035f);
            detailMaterial.SetFloat("_Cutoff", 0.05f);

            Texture detailTexture = sourceMaterial != null
                ? sourceMaterial.GetTexture("_MainTex")
                : null;
            if (detailTexture != null)
            {
                detailMaterial.SetTexture("_MainTex", detailTexture);
                detailMaterial.SetTextureScale(
                    "_MainTex",
                    sourceMaterial.GetTextureScale("_MainTex"));
                detailMaterial.SetTextureOffset(
                    "_MainTex",
                    sourceMaterial.GetTextureOffset("_MainTex"));
            }

            detailMaterial.SetFloat(
                "_OutlineWidth",
                materialName.ToLowerInvariant().Contains("skin") ? 0.002f : 0f);

            faceDetailMaterials.Add(detailMaterial);
            return detailMaterial;
        }

        private void CreatePortraitCamera()
        {
            portraitTexture = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32)
            {
                name = "Pause Portrait",
                antiAliasing = 4,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false
            };
            portraitTexture.Create();

            GameObject cameraObject = new GameObject("Pause Portrait Camera");
            cameraObject.transform.SetParent(renderStage.transform, false);
            portraitCamera = cameraObject.AddComponent<Camera>();
            portraitCamera.clearFlags = CameraClearFlags.SolidColor;
            portraitCamera.backgroundColor = PortraitFieldColor;
            portraitCamera.fieldOfView = 27f;
            portraitCamera.nearClipPlane = 0.05f;
            portraitCamera.farClipPlane = 30f;
            portraitCamera.allowHDR = false;
            portraitCamera.allowMSAA = true;
            portraitCamera.targetTexture = portraitTexture;
            portraitCamera.cullingMask = portraitLayerMask;
            RegisterPortraitRenderCallback();
            ExcludePortraitLayerFromOtherCameras();

            Bounds bounds = GetPortraitBounds();
            float distance = Mathf.Max(3.5f,
                bounds.size.y * 0.56f / Mathf.Tan(portraitCamera.fieldOfView * 0.5f * Mathf.Deg2Rad));
            Vector3 focus = bounds.center + Vector3.up * bounds.extents.y * 0.02f;
            portraitCamera.transform.position = focus
                + new Vector3(bounds.extents.x * 0.12f, 0f, distance);
            portraitCamera.transform.LookAt(focus);
            portraitCameraBasePosition = portraitCamera.transform.position;
            portraitCameraBaseRotation = portraitCamera.transform.rotation;
            portraitCameraBaseFieldOfView = portraitCamera.fieldOfView;
        }

        private void ExcludePortraitLayerFromOtherCameras()
        {
            if (portraitLayerMask == 0)
                return;

            Camera[] cameras = FindObjectsOfType<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i] != portraitCamera)
                    cameras[i].cullingMask &= ~portraitLayerMask;
            }
        }

        private void RegisterPortraitRenderCallback()
        {
            if (portraitRenderCallbackRegistered)
                return;

            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
            portraitRenderCallbackRegistered = true;
        }

        private void UnregisterPortraitRenderCallback()
        {
            if (!portraitRenderCallbackRegistered)
                return;

            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
            portraitRenderCallbackRegistered = false;
        }

        private void HandleBeginCameraRendering(
            ScriptableRenderContext context,
            Camera renderingCamera)
        {
            if (portraitInstance == null)
                return;

            if (renderingCamera == portraitCamera)
                UpdatePortraitRendererProxies(true);
            else
            {
                renderingCamera.cullingMask &= ~portraitLayerMask;
                SetPortraitProxiesVisible(false);
            }
        }

        private void HandleEndCameraRendering(
            ScriptableRenderContext context,
            Camera renderingCamera)
        {
            if (renderingCamera == portraitCamera)
                SetPortraitProxiesVisible(false);
        }

        private Bounds GetPortraitBounds()
        {
            Renderer[] renderers = portraitInstance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(portraitInstance.transform.position + Vector3.up, new Vector3(1f, 2f, 1f));

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private void RestartPortraitAnimation()
        {
            if (portraitAnimator == null || portraitCamera == null)
                return;

            portraitAnimator.enabled = true;
            portraitAnimator.speed =
                GetPoseClipLength() * GetHoldNormalizedTime() / IntroDuration;
            portraitAnimator.Rebind();
            portraitAnimator.Play(GetPoseStateName(), 0, 0f);
            portraitAnimator.Update(0f);
            portraitCamera.enabled = true;
        }

        private float GetPoseClipLength()
        {
            if (portraitPoseClip != null)
                return Mathf.Max(0.01f, portraitPoseClip.length);
            if (portraitController == null || portraitController.animationClips.Length == 0)
                return IntroDuration;
            return Mathf.Max(0.01f, portraitController.animationClips[0].length);
        }

        private RuntimeAnimatorController CreatePoseController()
        {
            if (portraitController == null || portraitPoseClip == null)
                return portraitController;

            portraitOverrideController = new AnimatorOverrideController(portraitController)
            {
                name = "Pause Pose Override (Runtime)"
            };
            List<KeyValuePair<AnimationClip, AnimationClip>> overrides =
                new List<KeyValuePair<AnimationClip, AnimationClip>>();
            portraitOverrideController.GetOverrides(overrides);
            for (int i = 0; i < overrides.Count; i++)
            {
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(
                    overrides[i].Key,
                    portraitPoseClip);
            }
            portraitOverrideController.ApplyOverrides(overrides);
            return portraitOverrideController;
        }

        private float GetHoldNormalizedTime()
        {
            return portraitHoldNormalizedTime;
        }

        private static string GetPoseStateName()
        {
            return GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.SceneLookups.PausePoseStateName
                : string.Empty;
        }

        private void FreezePortraitAtFinalPose()
        {
            if (portraitAnimator != null)
            {
                portraitAnimator.speed = 0f;
                portraitAnimator.Play(
                    GetPoseStateName(),
                    0,
                    GetHoldNormalizedTime());
                portraitAnimator.Update(0f);
                portraitAnimator.enabled = false;
            }

            if (portraitCamera != null)
            {
                portraitCamera.Render();
                portraitCamera.enabled = false;
            }
        }

        private void SetIntroProgress(float progress)
        {
            if (!visualsBuilt)
                return;

            float backgroundProgress = EaseOutCubic(progress);
            if (backdrop != null)
            {
                Color color = OverlayBackdrop;
                color.a *= Mathf.Clamp01(progress * 3.5f);
                backdrop.color = color;
            }

            float slashProgress = EaseOutBack(Mathf.Clamp01(progress / 0.72f));
            SetAnchoredX(inkSlash, Mathf.Lerp(-960f, 390f, slashProgress));
            float edgeProgress = EaseOutCubic(
                Mathf.Clamp01((progress - 0.04f) / 0.65f));
            SetAnchoredX(foregroundRedSlash, Mathf.Lerp(-600f, 872f, edgeProgress));
            SetAnchoredX(paperSlash, Mathf.Lerp(-600f, 886f, edgeProgress));

            float portraitProgress = EaseOutBack(Mathf.Clamp01((progress - 0.08f) / 0.72f));
            SetAnchoredX(portraitRect, Mathf.Lerp(-760f, 420f, portraitProgress));
            portraitRect.localEulerAngles = new Vector3(
                0f, 0f, Mathf.Lerp(-11f, -2.5f, portraitProgress));
            portraitRect.localScale = Vector3.one * Mathf.Lerp(1.14f, 1f, portraitProgress);
            if (portraitGroup != null)
                portraitGroup.alpha =
                    Mathf.Clamp01((progress - 0.04f) / 0.22f) * 0.22f;

            float menuProgress = EaseOutBack(Mathf.Clamp01((progress - 0.31f) / 0.62f));
            if (menuRect != null)
            {
                menuRect.anchoredPosition = Vector2.Lerp(
                    new Vector2(820f, 120f), new Vector2(70f, -10f), menuProgress);
                menuRect.localEulerAngles = Vector3.zero;
            }
            if (menuGroup != null)
                menuGroup.alpha = Mathf.Clamp01((progress - 0.28f) / 0.28f);

            if (kicker != null)
            {
                Color color = OverlaySecondary;
                color.a *= Mathf.Clamp01((progress - 0.45f) / 0.3f);
                kicker.color = color;
                ((RectTransform)kicker.transform).anchoredPosition =
                    new Vector2(-46f, Mathf.Lerp(-90f, -36f, backgroundProgress));
            }

            ApplyCameraAnimation(progress);
        }

        private void ApplyCameraAnimation(float normalizedTime)
        {
            if (portraitCamera == null)
                return;

            if (portraitCameraAnimation == null)
            {
                portraitCamera.transform.position = portraitCameraBasePosition;
                portraitCamera.transform.rotation = portraitCameraBaseRotation;
                portraitCamera.fieldOfView = portraitCameraBaseFieldOfView;
                return;
            }

            Vector3 localOffset =
                portraitCameraAnimation.EvaluateLocalPosition(normalizedTime);
            Vector3 localEuler =
                portraitCameraAnimation.EvaluateLocalEulerAngles(normalizedTime);
            portraitCamera.transform.position = portraitCameraBasePosition
                + portraitCameraBaseRotation * localOffset;
            portraitCamera.transform.rotation = portraitCameraBaseRotation
                * Quaternion.Euler(localEuler);
            portraitCamera.fieldOfView = Mathf.Clamp(
                portraitCameraBaseFieldOfView
                    + portraitCameraAnimation.EvaluateFieldOfViewOffset(normalizedTime),
                10f,
                80f);
        }

        private static float EaseOutCubic(float value)
        {
            float inverse = 1f - Mathf.Clamp01(value);
            return 1f - inverse * inverse * inverse;
        }

        private static float EaseOutBack(float value)
        {
            float shifted = Mathf.Clamp01(value) - 1f;
            const float overshoot = 1.70158f;
            return 1f + (overshoot + 1f) * shifted * shifted * shifted
                + overshoot * shifted * shifted;
        }

        private static void SetAnchoredX(RectTransform rect, float x)
        {
            if (rect == null)
                return;
            Vector2 position = rect.anchoredPosition;
            position.x = x;
            rect.anchoredPosition = position;
        }

        private static RectTransform CreateImage(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static TMP_Text CreateText(string name, Transform parent, string content)
        {
            RectTransform rect = CreateRect(name, parent);
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = content;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            return label;
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }
    }
}
