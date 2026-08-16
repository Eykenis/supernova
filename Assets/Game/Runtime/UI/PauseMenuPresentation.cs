using System.Collections.Generic;
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

        private static readonly Color PortraitClearColor = new Color(0f, 0f, 0f, 0f);
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
        private PausePortraitAnimationCurves portraitAnimation;
        private float portraitHoldNormalizedTime = 0.995f;
        private float portraitYaw = -8f;
        private int poseSequence;
        private Vector3 portraitBaseLocalPosition;
        private Quaternion portraitBaseLocalRotation;
        private Vector3 portraitBaseLocalScale = Vector3.one;
        private Material bodyMaterial;
        private Material backgroundMaterial;
        private readonly List<Material> faceDetailMaterials = new List<Material>();
        private readonly List<PortraitRendererProxy> portraitRendererProxies =
            new List<PortraitRendererProxy>();
        private bool portraitRenderCallbackRegistered;
        private int portraitLayer = -1;
        private int portraitLayerMask;

        private Color OverlayBackdrop => designTokens != null
            ? designTokens.PauseBackdrop
            : new Color(0.025f, 0.028f, 0.035f, 1f);
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

            inkSlash = CreateRect("Portrait Field", transform);
            inkSlash.anchorMin = Vector2.zero;
            inkSlash.anchorMax = Vector2.one;
            inkSlash.offsetMin = Vector2.zero;
            inkSlash.offsetMax = Vector2.zero;
            float referenceWidth = designTokens != null
                ? designTokens.ReferenceResolution.x
                : 1920f;
            float portraitBottomEdge = referenceWidth
                - PauseMenuWedgeGraphic.SystemFieldWidth;
            float portraitTopEdge = portraitBottomEdge
                + PauseMenuWedgeGraphic.SystemFieldTopInset;
            PausePortraitFieldGraphic portraitField = inkSlash.gameObject
                .AddComponent<PausePortraitFieldGraphic>();
            portraitField.Configure(
                portraitBottomEdge,
                portraitTopEdge,
                OverlayBackdrop);
            Mask portraitMask = inkSlash.gameObject.AddComponent<Mask>();
            portraitMask.showMaskGraphic = true;

            foregroundRedSlash = CreateImage("Portrait Divider", transform, Color.clear);
            SetRect(foregroundRedSlash, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(1058f, 0f), new Vector2(2f, 1080f));

            paperSlash = CreateImage("Portrait Divider Echo", transform, Color.clear);
            SetRect(paperSlash, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(1074f, 0f), new Vector2(1f, 860f));

            RectTransform portrait = CreateRect("Pause Portrait", inkSlash);
            portraitRect = portrait;
            SetRect(portraitRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(470f, -8f), new Vector2(1180f, 1080f));
            portraitImage = portrait.gameObject.AddComponent<RawImage>();
            portraitImage.color = Color.white;
            portraitImage.raycastTarget = false;
            portraitGroup = portrait.gameObject.AddComponent<CanvasGroup>();
            portraitGroup.alpha = 0.46f;
            portraitGroup.interactable = false;
            portraitGroup.blocksRaycasts = false;

            menuRect = transform.Find(UiHierarchyPaths.Pause.Menu) as RectTransform;
            if (menuRect != null)
            {
                SetRect(menuRect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(640f, 760f));
                menuRect.localEulerAngles = Vector3.zero;
                menuGroup = menuRect.GetComponent<CanvasGroup>();
                if (menuGroup == null)
                    menuGroup = menuRect.gameObject.AddComponent<CanvasGroup>();

                Image menuImage = menuRect.GetComponent<Image>();
                if (menuImage != null)
                    menuImage.color = Color.clear;

                Outline menuOutline = menuRect.GetComponent<Outline>();
                if (menuOutline != null)
                {
                    menuOutline.effectColor = OverlayDivider;
                    menuOutline.effectDistance = new Vector2(1f, -1f);
                    menuOutline.useGraphicAlpha = false;
                }

                Transform mainOptions = menuRect.Find(UiHierarchyPaths.Pause.MainOptions);
                title = mainOptions != null
                    ? mainOptions.Find(UiHierarchyPaths.Pause.Title)?.GetComponent<TMP_Text>()
                    : null;
                if (title != null)
                {
                    title.text = "游戏暂停";
                    title.fontSize = 48f;
                    title.fontStyle = FontStyles.Bold;
                    title.characterSpacing = 4f;
                    title.color = OverlayInverse;
                    title.alignment = TextAlignmentOptions.Left;
                }

                RectTransform resume = menuRect.Find(
                    UiHierarchyPaths.Pause.MainOptions
                    + "/"
                    + UiHierarchyPaths.Pause.Resume) as RectTransform;
                if (resume != null)
                {
                    TMP_Text resumeLabel = resume.Find(UiHierarchyPaths.Pause.Label) != null
                        ? resume.Find(UiHierarchyPaths.Pause.Label).GetComponent<TMP_Text>()
                        : null;
                    if (resumeLabel != null)
                    {
                        resumeLabel.text = "返回";
                        resumeLabel.fontStyle = FontStyles.Bold;
                        resumeLabel.color = OverlayPrimary;
                    }
                }
            }

            inkSlash.SetSiblingIndex(0);
            foregroundRedSlash.SetSiblingIndex(1);
            paperSlash.SetSiblingIndex(2);
            if (menuRect != null)
                menuRect.SetAsLastSibling();
            SciFiUiSkin.ApplyPauseMenu(transform);
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
            backgroundMaterial.SetColor("_Color", OverlayBackdrop);
            backgroundMaterial.SetColor("_OutlineColor", OverlayBackdrop);

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
            portraitBaseLocalScale = portraitInstance.transform.localScale;
            CapturePortraitBaseTransform();

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
            portraitAnimation = pose != null ? pose.PortraitAnimation : null;
            if (portraitInstance != null)
            {
                portraitInstance.transform.localPosition = Vector3.zero;
                portraitInstance.transform.localRotation = Quaternion.Euler(0f, portraitYaw, 0f);
                portraitInstance.transform.localScale = portraitBaseLocalScale;
                CapturePortraitBaseTransform();
            }
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
            portraitCamera.backgroundColor = PortraitClearColor;
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
        }

        private void ExcludePortraitLayerFromOtherCameras()
        {
            if (portraitLayerMask == 0)
                return;

            Camera[] cameras = FindObjectsOfType<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null
                    && cameras[i] != portraitCamera
                    && cameras[i].targetTexture == null)
                {
                    cameras[i].cullingMask &= ~portraitLayerMask;
                }
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
            else if (renderingCamera.targetTexture != null
                && (renderingCamera.cullingMask & portraitLayerMask) != 0)
            {
                return;
            }
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
                backdrop.color = OverlayBackdrop;

            float edgeProgress = EaseOutCubic(
                Mathf.Clamp01((progress - 0.04f) / 0.65f));
            SetAnchoredX(foregroundRedSlash, Mathf.Lerp(-600f, 1058f, edgeProgress));
            SetAnchoredX(paperSlash, Mathf.Lerp(-600f, 1074f, edgeProgress));

            float portraitProgress = EaseOutBack(Mathf.Clamp01((progress - 0.08f) / 0.72f));
            SetAnchoredX(portraitRect, Mathf.Lerp(-820f, 470f, portraitProgress));
            portraitRect.localEulerAngles = new Vector3(
                0f, 0f, Mathf.Lerp(-11f, -2.5f, portraitProgress));
            portraitRect.localScale = Vector3.one * Mathf.Lerp(1.14f, 1f, portraitProgress);
            if (portraitGroup != null)
                portraitGroup.alpha =
                    Mathf.Clamp01((progress - 0.04f) / 0.22f) * 0.46f;

            float menuProgress = EaseOutBack(Mathf.Clamp01((progress - 0.31f) / 0.62f));
            if (menuRect != null)
            {
                menuRect.anchoredPosition = Vector2.Lerp(
                    new Vector2(720f, 100f), new Vector2(-40f, 0f), menuProgress);
                menuRect.localEulerAngles = Vector3.zero;
            }
            if (menuGroup != null)
                menuGroup.alpha = Mathf.Clamp01((progress - 0.28f) / 0.28f);

            ApplyPortraitAnimation(progress);
        }

        private void CapturePortraitBaseTransform()
        {
            if (portraitInstance == null)
                return;

            Transform portraitTransform = portraitInstance.transform;
            portraitBaseLocalPosition = portraitTransform.localPosition;
            portraitBaseLocalRotation = portraitTransform.localRotation;
            portraitBaseLocalScale = portraitTransform.localScale;
        }

        private void ApplyPortraitAnimation(float normalizedTime)
        {
            if (portraitInstance == null)
                return;

            Transform portraitTransform = portraitInstance.transform;
            if (portraitAnimation == null)
            {
                portraitTransform.localPosition = portraitBaseLocalPosition;
                portraitTransform.localRotation = portraitBaseLocalRotation;
                portraitTransform.localScale = portraitBaseLocalScale;
                return;
            }

            Vector3 localOffset = portraitAnimation.EvaluateLocalPosition(normalizedTime);
            Vector3 localEuler = portraitAnimation.EvaluateLocalEulerAngles(normalizedTime);
            portraitTransform.localPosition = portraitBaseLocalPosition + localOffset;
            portraitTransform.localRotation = portraitBaseLocalRotation * Quaternion.Euler(localEuler);
            portraitTransform.localScale = portraitBaseLocalScale
                * portraitAnimation.EvaluateScaleMultiplier(normalizedTime);
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
