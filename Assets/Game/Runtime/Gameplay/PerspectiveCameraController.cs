using System.Collections.Generic;
using Supernova.UI;
using Supernova.Voxels;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Supernova.Gameplay
{
    public enum PlayerViewMode
    {
        FirstPerson = 0,
        ThirdPerson = 2,
    }

    /// <summary>
    /// Switches between first-person and an independent orbiting third-person camera.
    /// The third-person view uses a non-allocating sphere cast and only treats
    /// static scene meshes as camera obstructions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerspectiveCameraController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform animatedHead;
        [SerializeField] private Camera controlledCamera;
        [SerializeField] private Renderer[] firstPersonHiddenRenderers;

        [Header("Input")]
        [SerializeField, Min(0.01f)] private float mouseSensitivity = 2f;
        [SerializeField] private bool lockCursorOnEnable = true;
        [SerializeField] private bool clickToRecaptureCursor = true;
        [FormerlySerializedAs("switchKey")]
        [SerializeField] private KeyCode switchViewKey = KeyCode.F5;
        [FormerlySerializedAs("initialMode")]
        [SerializeField] private PlayerViewMode initialViewMode = PlayerViewMode.FirstPerson;

        [Header("First person")]
        [SerializeField] private Vector3 firstPersonRootSpaceOffset =
            new Vector3(0f, 0.025f, 0.085f);
        [SerializeField] private bool collapseHeadBoneInFirstPerson = true;
        [SerializeField, Range(0.0001f, 0.1f)] private float firstPersonHeadScale = 0.001f;

        [Header("Upper body camera follow")]
        [SerializeField] private bool rotateUpperBodyWithCamera = true;
        [SerializeField] private bool upperBodyFollowInThirdPerson;
        [SerializeField, Range(0f, 89f)] private float maximumUpperBodyPitch = 75f;
        [SerializeField, Range(0f, 120f)] private float maximumUpperBodyYaw = 55f;
        [SerializeField, Min(0f)] private float upperBodyRotationSmoothSpeed = 18f;
        [SerializeField, Range(0f, 1f)] private float spineRotationWeight = 0.12f;
        [SerializeField, Range(0f, 1f)] private float chestRotationWeight = 0.26f;
        [SerializeField, Range(0f, 1f)] private float upperChestRotationWeight = 0.26f;
        [SerializeField, Range(0f, 1f)] private float neckRotationWeight = 0.14f;
        [SerializeField, Range(0f, 1f)] private float headRotationWeight = 0.22f;
        [Tooltip("Additional forward pitch applied to both upper arms while crouching.")]
        [SerializeField, Range(0f, 45f)] private float crouchArmForwardAngle = 12f;

        [Header("Third person")]
        [SerializeField] private Vector3 thirdPersonOffset = new Vector3(0f, 0.18f, -4f);
        [SerializeField, Min(0.01f)] private float thirdPersonTurnSmoothTime = 0.18f;

        [Header("Obstruction")]
        [FormerlySerializedAs("collisionRadius")]
        [SerializeField, Min(0.01f)] private float cameraCollisionRadius = 0.18f;
        [FormerlySerializedAs("collisionPadding")]
        [SerializeField, Min(0f)] private float cameraCollisionPadding = 0.06f;
        [FormerlySerializedAs("restoreSmoothTime")]
        [SerializeField, Min(0.01f)] private float cameraRestoreSmoothTime = 0.12f;

        private readonly RaycastHit[] obstructionHits = new RaycastHit[64];
        private PlayerViewMode currentMode;
        private float lookPitch;
        private float thirdPersonYaw;
        private bool hasThirdPersonYaw;
        private float currentExternalDistance;
        private float restoreVelocity;
        private Vector3 animatedHeadRestLocalScale = Vector3.one;
        private bool hasAnimatedHeadRestScale;
        private Animator characterAnimator;
        private Transform animatedSpine;
        private Transform animatedChest;
        private Transform animatedUpperChest;
        private Transform animatedNeck;
        private Transform animatedLeftUpperArm;
        private Transform animatedRightUpperArm;
        private VoxelPlayerController playerController;
        private float smoothedUpperBodyPitch;
        private float smoothedUpperBodyYaw;
        private bool cursorLockRequested;
        private bool hasApplicationFocus = true;
        private readonly List<ShadowBoneProxy> shadowBoneProxies =
            new List<ShadowBoneProxy>();
        private readonly List<ShadowRendererProxy> shadowRendererProxies =
            new List<ShadowRendererProxy>();
        private readonly Dictionary<Transform, Transform> shadowBoneMap =
            new Dictionary<Transform, Transform>();
        private Transform shadowProxySourceHead;
        private GameObject shadowProxyHeadRoot;

        private sealed class ShadowBoneProxy
        {
            public Transform Source;
            public Transform Proxy;
            public bool UsesHeadRestScale;
        }

        private sealed class ShadowRendererProxy
        {
            public SkinnedMeshRenderer Source;
            public SkinnedMeshRenderer Proxy;
            public ShadowCastingMode OriginalShadowCastingMode;
            public bool OriginalEnabled;
            public bool HiddenInFirstPerson;
        }

        public PlayerViewMode CurrentMode => currentMode;
        public Camera ControlledCamera => controlledCamera;
        public float MouseSensitivity => Mathf.Max(0.01f, mouseSensitivity);
        public bool LockCursorOnEnable => lockCursorOnEnable;
        public bool CursorLockRequested => cursorLockRequested;
        public float ThirdPersonTurnSmoothTime => Mathf.Max(0.01f, thirdPersonTurnSmoothTime);

        /// <summary>
        /// Restores visual state that is intentionally altered on the live first-person
        /// character after that character has been cloned for an isolated UI preview.
        /// </summary>
        public void RestoreCharacterPreviewVisibility(
            Animator sourceAnimator,
            Animator previewAnimator)
        {
            if (sourceAnimator == null || previewAnimator == null)
                return;

            CacheAnimatedHeadScale();
            Transform previewHead = previewAnimator.isHuman
                ? previewAnimator.GetBoneTransform(HumanBodyBones.Head)
                : null;
            if (previewHead != null && hasAnimatedHeadRestScale)
                previewHead.localScale = animatedHeadRestLocalScale;

            if (firstPersonHiddenRenderers == null)
                return;

            for (int i = 0; i < firstPersonHiddenRenderers.Length; i++)
            {
                Renderer sourceRenderer = firstPersonHiddenRenderers[i];
                if (sourceRenderer == null
                    || !sourceRenderer.transform.IsChildOf(sourceAnimator.transform))
                {
                    continue;
                }

                string relativePath = GetRelativePath(
                    sourceAnimator.transform,
                    sourceRenderer.transform);
                Transform previewTransform = string.IsNullOrEmpty(relativePath)
                    ? previewAnimator.transform
                    : previewAnimator.transform.Find(relativePath);
                Renderer previewRenderer = previewTransform != null
                    ? previewTransform.GetComponent<Renderer>()
                    : null;
                if (previewRenderer == null)
                    continue;

                previewRenderer.enabled = true;
                previewRenderer.forceRenderingOff = false;
                previewRenderer.shadowCastingMode = ShadowCastingMode.On;
                previewRenderer.receiveShadows = true;
            }
        }

        private void Awake()
        {
            ResolveReferences();
            SetMode(initialViewMode, true);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            hasApplicationFocus = true;
            cursorLockRequested = lockCursorOnEnable;
            if (cursorLockRequested) SetCursorLocked(true);
        }

        private void Update()
        {
            if (GameHudController.IsGameplayInputBlocked)
            {
                if (Cursor.lockState == CursorLockMode.Locked) SetCursorLocked(false);
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked
                && clickToRecaptureCursor
                && Input.GetMouseButtonDown(0))
            {
                cursorLockRequested = true;
                SetCursorLocked(true);
            }

            if (hasApplicationFocus
                && cursorLockRequested
                && Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorLocked(true);
            }

            if (Input.GetKeyDown(switchViewKey))
            {
                CycleMode();
            }
            if (Cursor.lockState == CursorLockMode.Locked) UpdateLook();
        }

        private void LateUpdate()
        {
            ResolveReferences();
            if (controlledCamera == null || playerRoot == null) return;

            SetFirstPersonRendererState(currentMode == PlayerViewMode.FirstPerson);
            UpdateUpperBodyPose();
            UpdateCrouchArmPose();
            SyncFirstPersonShadowProxies();
            if (currentMode == PlayerViewMode.FirstPerson)
            {
                UpdateFirstPersonPose();
            }
            else
            {
                UpdateThirdPersonPose();
            }
        }

        public void SetLookPitch(float pitch)
        {
            lookPitch = Mathf.Clamp(pitch, -89f, 89f);
        }

        public void AddLookYaw(float yawDelta)
        {
            EnsureThirdPersonYaw();
            thirdPersonYaw = Mathf.Repeat(thirdPersonYaw + yawDelta, 360f);
        }

        private void UpdateLook()
        {
            FirstPersonCartAttractor attractor =
                GetComponentInParent<FirstPersonCartAttractor>();
            if (attractor != null && attractor.IsManipulatingHeldObject)
            {
                return;
            }
            float mouseX = Input.GetAxis("Mouse X") * Mathf.Max(0.01f, mouseSensitivity);
            float mouseY = Input.GetAxis("Mouse Y") * Mathf.Max(0.01f, mouseSensitivity);
            if (currentMode == PlayerViewMode.ThirdPerson)
            {
                AddLookYaw(mouseX);
            }
            else if (playerRoot != null)
            {
                playerRoot.Rotate(0f, mouseX, 0f, Space.Self);
            }
            SetLookPitch(lookPitch - mouseY);
        }

        /// <summary>
        /// Assigns the character that owns this view. This supports camera rigs that
        /// live beside the player in the scene instead of beneath its hierarchy.
        /// </summary>
        public void SetPlayerRoot(Transform root)
        {
            if (root != null)
            {
                playerRoot = root;
                playerController = null;
            }
        }

        public void CycleMode()
        {
            SetMode(currentMode == PlayerViewMode.FirstPerson
                ? PlayerViewMode.ThirdPerson
                : PlayerViewMode.FirstPerson, false);
        }

        public void SetMode(PlayerViewMode mode, bool immediate)
        {
            PlayerViewMode previousMode = currentMode;
            // Old scenes may still contain the removed second-person enum value (1).
            // Treat every external/unknown value as third person.
            currentMode = mode == PlayerViewMode.FirstPerson
                ? PlayerViewMode.FirstPerson
                : PlayerViewMode.ThirdPerson;
            bool firstPerson = currentMode == PlayerViewMode.FirstPerson;
            SetFirstPersonRendererState(firstPerson);

            if (!firstPerson && previousMode != PlayerViewMode.ThirdPerson)
            {
                thirdPersonYaw = playerRoot != null
                    ? playerRoot.eulerAngles.y
                    : transform.eulerAngles.y;
                hasThirdPersonYaw = true;
            }
            currentExternalDistance = firstPerson || immediate
                ? thirdPersonOffset.magnitude
                : 0f;
            restoreVelocity = 0f;
            smoothedUpperBodyPitch = 0f;
            smoothedUpperBodyYaw = 0f;
        }

        public void Bind(
            Transform root,
            Transform head,
            Camera camera,
            Renderer[] renderersHiddenInFirstPerson)
        {
            playerRoot = root;
            playerController = null;
            animatedHead = head;
            controlledCamera = camera;
            firstPersonHiddenRenderers = renderersHiddenInFirstPerson;
            if (animatedHead != null && controlledCamera != null)
            {
                CacheAnimatedHeadScale();
                firstPersonRootSpaceOffset = playerRoot.InverseTransformVector(
                    controlledCamera.transform.position - animatedHead.position);
            }
            // Do not serialize a runtime-only first-person visibility state into a
            // prefab while binding it in the editor.
            SetFirstPersonRendererState(Application.isPlaying
                && currentMode == PlayerViewMode.FirstPerson);
        }

        private void UpdateFirstPersonPose()
        {
            Transform cameraTransform = controlledCamera.transform;
            // Position follows the head, while the offset remains in player-root space.
            // This remains stable even when the head bone is collapsed to hide head
            // vertices from a single-piece skinned body mesh.
            if (animatedHead != null)
            {
                cameraTransform.position = animatedHead.position
                    + playerRoot.TransformVector(firstPersonRootSpaceOffset);
            }

            // Follow the animated head position, but derive viewing rotation from the
            // player root. Different humanoid models use very different head-bone
            // local axes; multiplying pitch through the bone rotation can clamp or
            // invert downward looking after a model swap.
            cameraTransform.rotation = playerRoot.rotation
                * Quaternion.Euler(lookPitch, 0f, 0f);
        }

        private void UpdateThirdPersonPose()
        {
            EnsureThirdPersonYaw();
            Vector3 pivot = animatedHead != null
                ? animatedHead.position
                : playerRoot.position + playerRoot.up;
            Quaternion viewRotation = Quaternion.Euler(lookPitch, thirdPersonYaw, 0f);
            Vector3 desiredDisplacement = viewRotation * thirdPersonOffset;
            float desiredDistance = desiredDisplacement.magnitude;
            if (desiredDistance <= 0.001f)
            {
                controlledCamera.transform.SetPositionAndRotation(pivot, viewRotation);
                return;
            }

            Vector3 direction = desiredDisplacement / desiredDistance;
            float allowedDistance = FindAllowedDistance(pivot, direction, desiredDistance);
            if (allowedDistance < currentExternalDistance || currentExternalDistance <= 0f)
            {
                // Move toward the player immediately so the camera never remains behind geometry.
                currentExternalDistance = allowedDistance;
                restoreVelocity = 0f;
            }
            else
            {
                currentExternalDistance = Mathf.SmoothDamp(
                    currentExternalDistance,
                    allowedDistance,
                    ref restoreVelocity,
                    Mathf.Max(0.01f, cameraRestoreSmoothTime));
            }

            controlledCamera.transform.SetPositionAndRotation(
                pivot + direction * currentExternalDistance,
                viewRotation);
        }

        private void EnsureThirdPersonYaw()
        {
            if (hasThirdPersonYaw) return;
            thirdPersonYaw = playerRoot != null ? playerRoot.eulerAngles.y : transform.eulerAngles.y;
            hasThirdPersonYaw = true;
        }

        private float FindAllowedDistance(
            Vector3 origin,
            Vector3 direction,
            float desiredDistance)
        {
            int count = Physics.SphereCastNonAlloc(
                origin,
                Mathf.Max(0.01f, cameraCollisionRadius),
                direction,
                obstructionHits,
                desiredDistance,
                ~0,
                QueryTriggerInteraction.Ignore);
            float nearestDistance = desiredDistance;
            for (int i = 0; i < count; i++)
            {
                Collider collider = obstructionHits[i].collider;
                if (!IsSceneMeshObstruction(collider)) continue;
                nearestDistance = Mathf.Min(
                    nearestDistance,
                    Mathf.Max(0f, obstructionHits[i].distance - Mathf.Max(0f, cameraCollisionPadding)));
            }
            return nearestDistance;
        }

        private bool IsSceneMeshObstruction(Collider collider)
        {
            if (collider == null
                || collider.isTrigger
                || collider.attachedRigidbody != null
                || !(collider is MeshCollider))
            {
                return false;
            }

            Transform candidate = collider.transform;
            return candidate != playerRoot && !candidate.IsChildOf(playerRoot);
        }

        private void SetFirstPersonRendererState(bool firstPerson)
        {
            if (firstPerson && collapseHeadBoneInFirstPerson)
            {
                EnsureFirstPersonShadowProxies();
            }

            if (firstPersonHiddenRenderers != null)
            {
                for (int i = 0; i < firstPersonHiddenRenderers.Length; i++)
                {
                    Renderer renderer = firstPersonHiddenRenderers[i];
                    if (renderer == null) continue;
                    renderer.shadowCastingMode = firstPerson
                        ? ShadowCastingMode.ShadowsOnly
                        : ShadowCastingMode.On;
                    renderer.receiveShadows = !firstPerson;
                }
            }

            bool useShadowProxy = firstPerson
                && collapseHeadBoneInFirstPerson
                && animatedHead != null;
            SetFirstPersonShadowProxyState(useShadowProxy);
            if (animatedHead == null) return;
            CacheAnimatedHeadScale();
            animatedHead.localScale = useShadowProxy
                ? animatedHeadRestLocalScale * Mathf.Clamp(firstPersonHeadScale, 0.0001f, 0.1f)
                : animatedHeadRestLocalScale;
        }

        private void CacheAnimatedHeadScale()
        {
            if (animatedHead == null || hasAnimatedHeadRestScale) return;
            animatedHeadRestLocalScale = animatedHead.localScale;
            hasAnimatedHeadRestScale = true;
        }

        private void EnsureFirstPersonShadowProxies()
        {
            if (!Application.isPlaying || animatedHead == null || animatedHead.parent == null)
            {
                return;
            }
            if (shadowProxyHeadRoot != null && shadowProxySourceHead == animatedHead)
            {
                return;
            }

            DestroyFirstPersonShadowProxies();
            CacheAnimatedHeadScale();
            shadowProxySourceHead = animatedHead;
            Transform proxyHead = CloneShadowBoneHierarchy(
                animatedHead,
                animatedHead.parent,
                true);
            shadowProxyHeadRoot = proxyHead.gameObject;

            SkinnedMeshRenderer[] renderers =
                playerRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer source = renderers[i];
                if (source == null
                    || source.sharedMesh == null
                    || !source.gameObject.activeInHierarchy
                    || !UsesHeadBone(source))
                {
                    continue;
                }

                GameObject proxyObject = new GameObject(source.name + " Shadow Proxy");
                proxyObject.hideFlags = HideFlags.HideAndDontSave;
                proxyObject.layer = source.gameObject.layer;
                Transform proxyTransform = proxyObject.transform;
                proxyTransform.SetParent(source.transform.parent, false);
                proxyTransform.localPosition = source.transform.localPosition;
                proxyTransform.localRotation = source.transform.localRotation;
                proxyTransform.localScale = source.transform.localScale;

                SkinnedMeshRenderer proxy = proxyObject.AddComponent<SkinnedMeshRenderer>();
                proxy.sharedMesh = source.sharedMesh;
                proxy.sharedMaterials = source.sharedMaterials;
                proxy.bones = RemapShadowBones(source.bones);
                proxy.rootBone = RemapShadowBone(source.rootBone);
                proxy.quality = source.quality;
                proxy.updateWhenOffscreen = source.updateWhenOffscreen;
                proxy.skinnedMotionVectors = false;
                proxy.localBounds = source.localBounds;
                proxy.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                proxy.receiveShadows = false;
                proxy.enabled = false;

                shadowRendererProxies.Add(new ShadowRendererProxy
                {
                    Source = source,
                    Proxy = proxy,
                    OriginalShadowCastingMode = source.shadowCastingMode,
                    OriginalEnabled = source.enabled,
                    HiddenInFirstPerson = IsHiddenInFirstPerson(source),
                });
            }
        }

        private Transform CloneShadowBoneHierarchy(
            Transform source,
            Transform parent,
            bool usesHeadRestScale)
        {
            GameObject proxyObject = new GameObject(source.name + " Shadow Proxy");
            proxyObject.hideFlags = HideFlags.HideAndDontSave;
            Transform proxy = proxyObject.transform;
            proxy.SetParent(parent, false);
            proxy.localPosition = source.localPosition;
            proxy.localRotation = source.localRotation;
            proxy.localScale = usesHeadRestScale
                ? animatedHeadRestLocalScale
                : source.localScale;
            shadowBoneMap[source] = proxy;
            shadowBoneProxies.Add(new ShadowBoneProxy
            {
                Source = source,
                Proxy = proxy,
                UsesHeadRestScale = usesHeadRestScale,
            });

            for (int i = 0; i < source.childCount; i++)
            {
                CloneShadowBoneHierarchy(source.GetChild(i), proxy, false);
            }
            return proxy;
        }

        private bool UsesHeadBone(SkinnedMeshRenderer renderer)
        {
            Transform[] bones = renderer.bones;
            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = bones[i];
                if (bone == animatedHead
                    || (bone != null && bone.IsChildOf(animatedHead)))
                {
                    return true;
                }
            }
            return false;
        }

        private Transform[] RemapShadowBones(Transform[] bones)
        {
            Transform[] remappedBones = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                remappedBones[i] = RemapShadowBone(bones[i]);
            }
            return remappedBones;
        }

        private Transform RemapShadowBone(Transform bone)
        {
            if (bone != null && shadowBoneMap.TryGetValue(bone, out Transform proxy))
            {
                return proxy;
            }
            return bone;
        }

        private bool IsHiddenInFirstPerson(Renderer candidate)
        {
            if (firstPersonHiddenRenderers == null) return false;
            for (int i = 0; i < firstPersonHiddenRenderers.Length; i++)
            {
                if (firstPersonHiddenRenderers[i] == candidate) return true;
            }
            return false;
        }

        private void SetFirstPersonShadowProxyState(bool firstPerson)
        {
            if (shadowProxyHeadRoot != null)
            {
                shadowProxyHeadRoot.SetActive(firstPerson);
            }
            for (int i = 0; i < shadowRendererProxies.Count; i++)
            {
                ShadowRendererProxy entry = shadowRendererProxies[i];
                if (entry.Source == null || entry.Proxy == null) continue;
                entry.Proxy.enabled = firstPerson;
                if (firstPerson)
                {
                    if (entry.HiddenInFirstPerson)
                    {
                        entry.Source.enabled = false;
                    }
                    else
                    {
                        entry.Source.shadowCastingMode = ShadowCastingMode.Off;
                    }
                }
                else
                {
                    entry.Source.enabled = entry.OriginalEnabled;
                    entry.Source.shadowCastingMode = entry.OriginalShadowCastingMode;
                }
            }
        }

        private void SyncFirstPersonShadowProxies()
        {
            if (currentMode != PlayerViewMode.FirstPerson
                || !collapseHeadBoneInFirstPerson
                || shadowProxyHeadRoot == null
                || !shadowProxyHeadRoot.activeSelf)
            {
                return;
            }

            for (int i = 0; i < shadowBoneProxies.Count; i++)
            {
                ShadowBoneProxy entry = shadowBoneProxies[i];
                if (entry.Source == null || entry.Proxy == null) continue;
                entry.Proxy.localPosition = entry.Source.localPosition;
                entry.Proxy.localRotation = entry.Source.localRotation;
                entry.Proxy.localScale = entry.UsesHeadRestScale
                    ? animatedHeadRestLocalScale
                    : entry.Source.localScale;
            }

            for (int i = 0; i < shadowRendererProxies.Count; i++)
            {
                ShadowRendererProxy entry = shadowRendererProxies[i];
                if (entry.Source == null || entry.Proxy == null) continue;
                entry.Proxy.transform.localPosition = entry.Source.transform.localPosition;
                entry.Proxy.transform.localRotation = entry.Source.transform.localRotation;
                entry.Proxy.transform.localScale = entry.Source.transform.localScale;
                Mesh mesh = entry.Source.sharedMesh;
                if (mesh == null) continue;
                for (int blendShape = 0; blendShape < mesh.blendShapeCount; blendShape++)
                {
                    entry.Proxy.SetBlendShapeWeight(
                        blendShape,
                        entry.Source.GetBlendShapeWeight(blendShape));
                }
            }
        }

        private void DestroyFirstPersonShadowProxies()
        {
            for (int i = 0; i < shadowRendererProxies.Count; i++)
            {
                ShadowRendererProxy entry = shadowRendererProxies[i];
                if (entry.Source != null)
                {
                    entry.Source.enabled = entry.OriginalEnabled;
                    entry.Source.shadowCastingMode = entry.OriginalShadowCastingMode;
                }
                DestroyRuntimeObject(entry.Proxy != null ? entry.Proxy.gameObject : null);
            }
            shadowRendererProxies.Clear();

            DestroyRuntimeObject(shadowProxyHeadRoot);
            shadowProxyHeadRoot = null;
            shadowProxySourceHead = null;
            shadowBoneProxies.Clear();
            shadowBoneMap.Clear();
        }

        private static void DestroyRuntimeObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || target == root)
                return string.Empty;

            var names = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }
            if (current != root)
                return string.Empty;

            names.Reverse();
            return string.Join("/", names);
        }

        private void UpdateUpperBodyPose()
        {
            bool shouldFollow = rotateUpperBodyWithCamera
                && (currentMode == PlayerViewMode.FirstPerson || upperBodyFollowInThirdPerson);
            // The view model must match the camera on this frame; smoothing or the
            // third-person pitch clamp makes the hands drift at steep view angles.
            bool strictFirstPersonFollow = shouldFollow
                && currentMode == PlayerViewMode.FirstPerson;
            float targetPitch = strictFirstPersonFollow
                ? lookPitch
                : shouldFollow
                    ? Mathf.Clamp(lookPitch, -maximumUpperBodyPitch, maximumUpperBodyPitch)
                    : 0f;
            float targetYaw = 0f;
            if (shouldFollow && currentMode == PlayerViewMode.ThirdPerson)
            {
                EnsureThirdPersonYaw();
                targetYaw = Mathf.Clamp(
                    Mathf.DeltaAngle(playerRoot.eulerAngles.y, thirdPersonYaw),
                    -maximumUpperBodyYaw,
                    maximumUpperBodyYaw);
            }

            float smoothFactor = strictFirstPersonFollow
                ? 1f
                : upperBodyRotationSmoothSpeed <= 0f
                    ? 1f
                    : 1f - Mathf.Exp(-upperBodyRotationSmoothSpeed * Time.deltaTime);
            smoothedUpperBodyPitch = Mathf.LerpAngle(
                smoothedUpperBodyPitch,
                targetPitch,
                smoothFactor);
            smoothedUpperBodyYaw = Mathf.LerpAngle(
                smoothedUpperBodyYaw,
                targetYaw,
                smoothFactor);

            ApplyUpperBodyBoneRotation(animatedSpine, spineRotationWeight);
            ApplyUpperBodyBoneRotation(animatedChest, chestRotationWeight);
            ApplyUpperBodyBoneRotation(animatedUpperChest, upperChestRotationWeight);
            ApplyUpperBodyBoneRotation(animatedNeck, neckRotationWeight);
            ApplyUpperBodyBoneRotation(animatedHead, headRotationWeight);
            ApplyUpperArmViewCorrection(animatedLeftUpperArm);
            ApplyUpperArmViewCorrection(animatedRightUpperArm);
        }

        private void ApplyUpperArmViewCorrection(Transform upperArm)
        {
            if (upperArm == null) return;
            float inheritedTorsoWeight = spineRotationWeight
                + chestRotationWeight
                + upperChestRotationWeight;
            float correctionWeight = 1f - inheritedTorsoWeight;
            if (Mathf.Approximately(correctionWeight, 0f)) return;

            Quaternion pitchRotation = Quaternion.AngleAxis(
                smoothedUpperBodyPitch * correctionWeight,
                playerRoot.right);
            Quaternion yawRotation = Quaternion.AngleAxis(
                smoothedUpperBodyYaw * correctionWeight,
                playerRoot.up);
            upperArm.rotation = yawRotation * pitchRotation * upperArm.rotation;
        }

        private void ApplyUpperBodyBoneRotation(Transform bone, float weight)
        {
            if (bone == null || weight <= 0f) return;
            Quaternion pitchRotation = Quaternion.AngleAxis(
                smoothedUpperBodyPitch * weight,
                playerRoot.right);
            Quaternion yawRotation = Quaternion.AngleAxis(
                smoothedUpperBodyYaw * weight,
                playerRoot.up);
            bone.rotation = yawRotation * pitchRotation * bone.rotation;
        }

        private void UpdateCrouchArmPose()
        {
            if (playerController == null || crouchArmForwardAngle <= 0f) return;
            if (playerController.IsRifleSelected) return;
            float forwardAngle = crouchArmForwardAngle
                * Mathf.Clamp01(playerController.CrouchPoseWeight);
            if (forwardAngle <= 0f) return;

            ApplyArmForwardRotation(animatedLeftUpperArm, playerRoot.right, forwardAngle);
            ApplyArmForwardRotation(animatedRightUpperArm, playerRoot.right, forwardAngle);
        }

        private static void ApplyArmForwardRotation(
            Transform upperArm,
            Vector3 playerRight,
            float angle)
        {
            if (upperArm == null || playerRight.sqrMagnitude <= Mathf.Epsilon || angle <= 0f)
                return;
            upperArm.rotation = Quaternion.AngleAxis(-angle, playerRight.normalized)
                * upperArm.rotation;
        }

        private void ResolveUpperBodyBones(Animator animator)
        {
            if (animator == characterAnimator) return;
            characterAnimator = animator;
            animatedSpine = null;
            animatedChest = null;
            animatedUpperChest = null;
            animatedNeck = null;
            animatedLeftUpperArm = null;
            animatedRightUpperArm = null;
            if (characterAnimator == null || !characterAnimator.isHuman) return;
            animatedSpine = characterAnimator.GetBoneTransform(HumanBodyBones.Spine);
            animatedChest = characterAnimator.GetBoneTransform(HumanBodyBones.Chest);
            animatedUpperChest = characterAnimator.GetBoneTransform(HumanBodyBones.UpperChest);
            animatedNeck = characterAnimator.GetBoneTransform(HumanBodyBones.Neck);
            animatedLeftUpperArm = characterAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            animatedRightUpperArm = characterAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            hasApplicationFocus = hasFocus;
            if (!hasFocus)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                    cursorLockRequested = true;
                SetCursorLocked(false);
            }
            else if (cursorLockRequested && !GameHudController.IsModalMenuOpen)
            {
                SetCursorLocked(true);
            }
        }

        private void OnDisable()
        {
            SetFirstPersonRendererState(false);
            DestroyFirstPersonShadowProxies();
            cursorLockRequested = false;
            hasApplicationFocus = false;
            if (Application.isPlaying) SetCursorLocked(false);
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void ResolveReferences()
        {
            if (playerRoot == null) playerRoot = transform;
            if (playerController == null || playerController.transform != playerRoot)
                playerController = playerRoot.GetComponent<VoxelPlayerController>();
            if (characterAnimator == null)
            {
                ResolveUpperBodyBones(playerRoot.GetComponentInChildren<Animator>(false));
            }
            if (controlledCamera == null)
            {
                controlledCamera = GetComponentInChildren<Camera>(true);
            }
            if (animatedHead == null
                || !animatedHead.IsChildOf(playerRoot)
                || !animatedHead.gameObject.activeInHierarchy)
            {
                animatedHead = null;
                hasAnimatedHeadRestScale = false;
                Animator animator = playerRoot.GetComponentInChildren<Animator>(false);
                ResolveUpperBodyBones(animator);
                if (animator != null && animator.isHuman)
                {
                    animatedHead = animator.GetBoneTransform(HumanBodyBones.Head);
                }
                if (animatedHead == null && animator != null)
                {
                    Transform[] transforms = animator.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < transforms.Length; i++)
                    {
                        if (transforms[i].name == "Head")
                        {
                            animatedHead = transforms[i];
                            break;
                        }
                    }
                }
                CacheAnimatedHeadScale();
            }
        }
    }
}
