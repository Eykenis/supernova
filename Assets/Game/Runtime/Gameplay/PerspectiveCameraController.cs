using Supernova.UI;
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
    /// The third-person view uses a non-allocating sphere cast and treats every
    /// non-player Collider (including triggers) as obstruction.
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
        private float smoothedUpperBodyPitch;
        private float smoothedUpperBodyYaw;
        private bool cursorLockRequested;
        private bool hasApplicationFocus = true;

        public PlayerViewMode CurrentMode => currentMode;
        public Camera ControlledCamera => controlledCamera;
        public float MouseSensitivity => Mathf.Max(0.01f, mouseSensitivity);
        public bool LockCursorOnEnable => lockCursorOnEnable;
        public bool CursorLockRequested => cursorLockRequested;
        public float ThirdPersonTurnSmoothTime => Mathf.Max(0.01f, thirdPersonTurnSmoothTime);

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
            if (GameHudController.IsPauseMenuOpen)
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
                QueryTriggerInteraction.Collide);
            float nearestDistance = desiredDistance;
            for (int i = 0; i < count; i++)
            {
                Collider collider = obstructionHits[i].collider;
                if (collider == null || IsOwnedByPlayer(collider.transform)) continue;
                nearestDistance = Mathf.Min(
                    nearestDistance,
                    Mathf.Max(0f, obstructionHits[i].distance - Mathf.Max(0f, cameraCollisionPadding)));
            }
            return nearestDistance;
        }

        private bool IsOwnedByPlayer(Transform candidate)
        {
            return candidate == playerRoot || candidate.IsChildOf(playerRoot);
        }

        private void SetFirstPersonRendererState(bool firstPerson)
        {
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

            if (animatedHead == null || !collapseHeadBoneInFirstPerson) return;
            CacheAnimatedHeadScale();
            animatedHead.localScale = firstPerson
                ? animatedHeadRestLocalScale * Mathf.Clamp(firstPersonHeadScale, 0.0001f, 0.1f)
                : animatedHeadRestLocalScale;
        }

        private void CacheAnimatedHeadScale()
        {
            if (animatedHead == null || hasAnimatedHeadRestScale) return;
            animatedHeadRestLocalScale = animatedHead.localScale;
            hasAnimatedHeadRestScale = true;
        }

        private void UpdateUpperBodyPose()
        {
            bool shouldFollow = rotateUpperBodyWithCamera
                && (currentMode == PlayerViewMode.FirstPerson || upperBodyFollowInThirdPerson);
            float targetPitch = shouldFollow
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

            float smoothFactor = upperBodyRotationSmoothSpeed <= 0f
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

        private void ResolveUpperBodyBones(Animator animator)
        {
            if (animator == characterAnimator) return;
            characterAnimator = animator;
            animatedSpine = null;
            animatedChest = null;
            animatedUpperChest = null;
            animatedNeck = null;
            if (characterAnimator == null || !characterAnimator.isHuman) return;
            animatedSpine = characterAnimator.GetBoneTransform(HumanBodyBones.Spine);
            animatedChest = characterAnimator.GetBoneTransform(HumanBodyBones.Chest);
            animatedUpperChest = characterAnimator.GetBoneTransform(HumanBodyBones.UpperChest);
            animatedNeck = characterAnimator.GetBoneTransform(HumanBodyBones.Neck);
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
            else if (cursorLockRequested && !GameHudController.IsPauseMenuOpen)
            {
                SetCursorLocked(true);
            }
        }

        private void OnDisable()
        {
            SetFirstPersonRendererState(false);
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
