using System.Collections.Generic;
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
        private const string PortraitPrefabResource = "Pause/PausePortrait";
        private const string PortraitControllerResource = "Pause/PausePortrait";
        private const string PortraitSettingsResource = "Pause/PausePortraitSettings";
        private const string BodyMaterialResource = "Pause/PauseSilhouetteBody";
        private const string BackgroundMaterialResource = "Pause/PauseSilhouetteBackground";
        private const string PoseStateName = "Base Layer.PausePose";
        private const float IntroDuration = 0.9f;

        private static readonly Color BackgroundColor = new Color32(22, 5, 12, 255);
        private static readonly Color PortraitFieldColor = new Color32(22, 0, 13, 255);
        private static readonly Color InkColor = new Color32(10, 7, 11, 255);
        private static readonly Color AccentColor = new Color32(222, 24, 49, 255);
        private static readonly Color PaperColor = new Color32(247, 237, 218, 255);

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
        private bool visualsBuilt;
        private bool introPlaying;
        private float introElapsed;

        private GameObject renderStage;
        private GameObject portraitInstance;
        private Camera portraitCamera;
        private Animator portraitAnimator;
        private RenderTexture portraitTexture;
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
        }

        private void OnDisable()
        {
            StopPresentation();
        }

        private void OnDestroy()
        {
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
            backdrop = GetComponent<Image>();
            if (backdrop != null)
                backdrop.color = BackgroundColor;

            inkSlash = CreateImage("P5 Ink Slash", transform, InkColor);
            SetRect(inkSlash, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(310f, 0f), new Vector2(1010f, 1450f));
            inkSlash.localEulerAngles = new Vector3(0f, 0f, -8f);

            foregroundRedSlash = CreateImage("P5 Foreground Red Slash", transform, AccentColor);
            SetRect(foregroundRedSlash, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(865f, -30f), new Vector2(112f, 1420f));
            foregroundRedSlash.localEulerAngles = new Vector3(0f, 0f, -11f);

            paperSlash = CreateImage("P5 Paper Slash", transform, PaperColor);
            SetRect(paperSlash, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(865f, -30f), new Vector2(34f, 1420f));
            paperSlash.localEulerAngles = new Vector3(0f, 0f, -11f);

            RectTransform portrait = CreateRect("Pause Portrait", transform);
            portraitRect = portrait;
            SetRect(portraitRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(420f, -8f), new Vector2(1040f, 1080f));
            portraitImage = portrait.gameObject.AddComponent<RawImage>();
            portraitImage.color = Color.white;
            portraitImage.raycastTarget = false;
            portraitGroup = portrait.gameObject.AddComponent<CanvasGroup>();
            portraitGroup.interactable = false;
            portraitGroup.blocksRaycasts = false;

            menuRect = transform.Find("Menu") as RectTransform;
            if (menuRect != null)
            {
                SetRect(menuRect, new Vector2(0.78f, 0.54f), new Vector2(0.78f, 0.54f),
                    new Vector2(0.5f, 0.5f), new Vector2(100f, -15f), new Vector2(500f, 250f));
                menuRect.localEulerAngles = new Vector3(0f, 0f, -3f);
                menuGroup = menuRect.GetComponent<CanvasGroup>();
                if (menuGroup == null)
                    menuGroup = menuRect.gameObject.AddComponent<CanvasGroup>();

                Image menuImage = menuRect.GetComponent<Image>();
                if (menuImage != null)
                    menuImage.color = AccentColor;

                Outline menuOutline = menuRect.GetComponent<Outline>();
                if (menuOutline != null)
                {
                    menuOutline.effectColor = PaperColor;
                    menuOutline.effectDistance = new Vector2(4f, -4f);
                }

                title = menuRect.Find("Title") != null
                    ? menuRect.Find("Title").GetComponent<TMP_Text>()
                    : null;
                if (title != null)
                {
                    title.text = "PAUSE";
                    title.fontSize = 50f;
                    title.fontStyle = FontStyles.Bold | FontStyles.Italic;
                    title.characterSpacing = -1f;
                    title.color = PaperColor;
                    title.alignment = TextAlignmentOptions.Left;
                    SetRect((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(0f, 1f), new Vector2(28f, -18f), new Vector2(420f, 72f));
                }

                RectTransform resume = menuRect.Find("Resume") as RectTransform;
                if (resume != null)
                {
                    SetRect(resume, new Vector2(1f, 0f), new Vector2(1f, 0f),
                        new Vector2(1f, 0f), new Vector2(-26f, 24f), new Vector2(325f, 68f));
                    Image resumeImage = resume.GetComponent<Image>();
                    if (resumeImage != null)
                        resumeImage.color = InkColor;

                    TMP_Text resumeLabel = resume.Find("Label") != null
                        ? resume.Find("Label").GetComponent<TMP_Text>()
                        : null;
                    if (resumeLabel != null)
                    {
                        resumeLabel.text = "RESUME  /  ESC";
                        resumeLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
                        resumeLabel.color = PaperColor;
                    }
                }
            }

            kicker = CreateText("Pause Kicker", transform, "BREAK // 05");
            SetRect((RectTransform)kicker.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(1f, 1f), new Vector2(-46f, -36f), new Vector2(430f, 50f));
            kicker.alignment = TextAlignmentOptions.Right;
            kicker.fontSize = 20f;
            kicker.fontStyle = FontStyles.Bold | FontStyles.Italic;
            kicker.characterSpacing = 5f;
            kicker.color = PaperColor;

            inkSlash.SetSiblingIndex(0);
            portraitRect.SetSiblingIndex(1);
            foregroundRedSlash.SetSiblingIndex(2);
            paperSlash.SetSiblingIndex(3);
            if (menuRect != null)
                menuRect.SetAsLastSibling();
            kicker.transform.SetAsLastSibling();
        }

        private void EnsurePortrait()
        {
            if (portraitInstance != null || portraitImage == null)
                return;

            portraitSettings = Resources.Load<PausePortraitSettings>(PortraitSettingsResource);
            GameObject prefab = portraitSettings != null && portraitSettings.PortraitPrefab != null
                ? portraitSettings.PortraitPrefab
                : Resources.Load<GameObject>(PortraitPrefabResource);
            portraitController = portraitSettings != null
                && portraitSettings.PoseController != null
                    ? portraitSettings.PoseController
                    : Resources.Load<RuntimeAnimatorController>(PortraitControllerResource);
            Material bodyTemplate = Resources.Load<Material>(BodyMaterialResource);
            Material backgroundTemplate = Resources.Load<Material>(BackgroundMaterialResource);
            if (prefab == null || portraitController == null || bodyTemplate == null
                || backgroundTemplate == null)
            {
                Debug.LogWarning(
                    "Pause portrait resources are missing. Run Tools/Supernova/UI/Rebuild Pause Portrait Assets.");
                return;
            }

            bodyMaterial = new Material(bodyTemplate) { name = "Pause Body (Runtime)" };
            backgroundMaterial = new Material(backgroundTemplate) { name = "Pause Background (Runtime)" };
            backgroundMaterial.SetColor("_Color", PortraitFieldColor);
            backgroundMaterial.SetColor("_OutlineColor", PortraitFieldColor);

            renderStage = new GameObject("Pause Portrait Render Stage");
            renderStage.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(renderStage);
            renderStage.transform.position = new Vector3(5000f, -5000f, 5000f);

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

            ApplySilhouetteMaterials();
            CreatePortraitCamera();
            portraitImage.texture = portraitTexture;
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

        private void ApplySilhouetteMaterials()
        {
            Renderer[] renderers = portraitInstance.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer targetRenderer = renderers[rendererIndex];
                if (targetRenderer.name.ToLowerInvariant().Contains("helmet"))
                {
                    // The source visor is a closed transparent shell. A solid-color replacement
                    // would cover the face, so the whole helmet dissolves into the portrait field.
                    targetRenderer.enabled = false;
                    continue;
                }

                Material[] sourceMaterials = targetRenderer.sharedMaterials;
                Material[] silhouetteMaterials = new Material[sourceMaterials.Length];
                for (int materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++)
                {
                    Material sourceMaterial = sourceMaterials[materialIndex];
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
                            IsBackgroundPart(targetRenderer.name, materialName)
                                ? backgroundMaterial
                                : bodyMaterial;
                    }
                }

                targetRenderer.sharedMaterials = silhouetteMaterials;
                targetRenderer.shadowCastingMode = ShadowCastingMode.Off;
                targetRenderer.receiveShadows = false;
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
            portraitAnimator.Play(PoseStateName, 0, 0f);
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

        private void FreezePortraitAtFinalPose()
        {
            if (portraitAnimator != null)
            {
                portraitAnimator.speed = 0f;
                portraitAnimator.Play(PoseStateName, 0, GetHoldNormalizedTime());
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
                Color color = BackgroundColor;
                color.a = Mathf.Lerp(0f, 1f, Mathf.Clamp01(progress * 3.5f));
                backdrop.color = color;
            }

            float slashProgress = EaseOutBack(Mathf.Clamp01(progress / 0.72f));
            SetAnchoredX(inkSlash, Mathf.Lerp(-1180f, 310f, slashProgress));
            float edgeProgress = EaseOutCubic(
                Mathf.Clamp01((progress - 0.04f) / 0.65f));
            SetAnchoredX(foregroundRedSlash, Mathf.Lerp(-600f, 865f, edgeProgress));
            SetAnchoredX(paperSlash, Mathf.Lerp(-600f, 865f, edgeProgress));

            float portraitProgress = EaseOutBack(Mathf.Clamp01((progress - 0.08f) / 0.72f));
            SetAnchoredX(portraitRect, Mathf.Lerp(-760f, 420f, portraitProgress));
            portraitRect.localEulerAngles = new Vector3(
                0f, 0f, Mathf.Lerp(-11f, -2.5f, portraitProgress));
            portraitRect.localScale = Vector3.one * Mathf.Lerp(1.14f, 1f, portraitProgress);
            if (portraitGroup != null)
                portraitGroup.alpha = Mathf.Clamp01((progress - 0.04f) / 0.22f);

            float menuProgress = EaseOutBack(Mathf.Clamp01((progress - 0.31f) / 0.62f));
            if (menuRect != null)
            {
                menuRect.anchoredPosition = Vector2.Lerp(
                    new Vector2(820f, 130f), new Vector2(100f, -15f), menuProgress);
                menuRect.localEulerAngles = new Vector3(
                    0f, 0f, Mathf.Lerp(7f, -3f, menuProgress));
            }
            if (menuGroup != null)
                menuGroup.alpha = Mathf.Clamp01((progress - 0.28f) / 0.28f);

            if (kicker != null)
            {
                Color color = PaperColor;
                color.a = Mathf.Clamp01((progress - 0.45f) / 0.3f);
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
