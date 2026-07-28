using System.Collections.Generic;
using Supernova.Gameplay;
using UnityEngine;

namespace Supernova.Voxels
{
    public enum PlayerCharacterState
    {
        Idle,
        Move,
        Jump,
        Fall,
        ToolAction,
        Hurt,
        Dead,
        CrouchIdle,
        CrouchMove,
    }

    /// <summary>
    /// Collects player input and adapts state-machine locomotion commands to a CharacterController.
    /// The states do not depend on CharacterController or Unity physics.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerProfile))]
    [RequireComponent(typeof(PlayerEquipmentController))]
    public sealed class VoxelPlayerController : MonoBehaviour, IDamageable
    {
        private static readonly int WalkFlag = Animator.StringToHash("walkFlag");
        private static readonly int JumpFlag = Animator.StringToHash("jumpFlag");
        private static readonly int IdleFlag = Animator.StringToHash("idleFlag");
        private static readonly int IdleBFlag = Animator.StringToHash("idleBFlag");
        private static readonly int SmileFlag = Animator.StringToHash("smileFlag");
        private static readonly int KocchiFlag = Animator.StringToHash("kocchiFlag");
        private static readonly int HitFlag = Animator.StringToHash("Hit");
        private static readonly int DieFlag = Animator.StringToHash("Die");
        private static readonly int RecoverFlag = Animator.StringToHash("Recover");
        private static readonly int CrouchFlag = Animator.StringToHash("crouchFlag");
        private static readonly int CrouchMoveFlag = Animator.StringToHash("crouchMoveFlag");
        private static readonly int ToolActionTrigger = Animator.StringToHash("ToolAction");
        private static readonly int ToolActionContinuousFlag =
            Animator.StringToHash("ToolActionContinuous");
        private static readonly int ToolPrimaryActionState =
            Animator.StringToHash("Base Layer.Tool Primary Action");
        private static readonly int EquipmentLocomotionState =
            Animator.StringToHash("Base Layer.Equipment Locomotion");
        private static readonly int IdleState =
            Animator.StringToHash("Base Layer.Idle");
        private const string PrimaryActionPlaceholderClipName = "ToolPrimaryActionPlaceholder";
        private const string EquipmentLocomotionPlaceholderClipName =
            "EquipmentLocomotionPlaceholder";

        [SerializeField] private Transform view;
        [SerializeField] private Animator animator;
        [Tooltip("Optional external target for kocchiFlag. A camera parented to this player is ignored.")]
        [SerializeField] private Transform kocchiTarget;

        [Header("Runtime")]
        [SerializeField] private PlayerCharacterState currentState;

        private CharacterController characterController;
        private PerspectiveCameraController perspectiveCamera;

        private FirstPersonCartAttractor cartAttractor;
        private PlayerToolController toolController;
        private PlayerEquipmentController equipmentController;
        private VoxelPlayerInteractor voxelInteractor;
        private PlayerProfile profile;
        private CharacterVitals vitals = new CharacterVitals();
        private IPlayerMotor motor;
        private CharacterStateMachine<PlayerCharacterState> stateMachine;
        private PlayerInputSnapshot input;
        private float thirdPersonTargetYaw;
        private float thirdPersonTurnVelocity;
        private bool hasThirdPersonTargetYaw;
        private float idleSeconds;
        private float stateSeconds;
        private float nextAttackTime;
        private float nextProjectileThrowTime;
        private readonly Queue<float> pendingMiningAttackTimes =
            new Queue<float>();
        private bool debugFlyMode;
        private bool hasWalkFlag;
        private bool hasJumpFlag;
        private bool hasIdleFlag;
        private bool hasIdleBFlag;
        private bool hasSmileFlag;
        private bool hasKocchiFlag;
        private bool hasHitFlag;
        private bool hasDieFlag;
        private bool hasRecoverFlag;
        private bool hasCrouchFlag;
        private bool hasCrouchMoveFlag;
        private bool hasToolActionTrigger;
        private bool hasToolActionContinuousFlag;
        private RuntimeAnimatorController baseAnimatorController;
        private AnimatorOverrideController toolAnimatorController;
        private AnimationClip primaryActionPlaceholderClip;
        private AnimationClip equipmentLocomotionPlaceholderClip;
        private AnimationClip activeEquipmentLocomotionAnimation;
        private bool equipmentLocomotionAnimationActive;
        private bool equipmentLocomotionExitRequested;
        private PlayerToolDefinition activeToolDefinition;
        private bool periodicToolAnimationObserved;
        private int pickaxeStrikeParity;
        private int lowerBodyLayerIndex = -1;
        private float lowerBodyLayerTargetWeight;
        private float lowerBodyLayerWeight;

        public GameObject Owner => gameObject;
        public float CurrentHealth => vitals != null ? vitals.CurrentHealth : 0f;
        public float MaximumHealth => vitals != null ? vitals.MaximumHealth : Profile.MaximumHealth;
        public bool IsAlive => vitals != null && vitals.IsAlive;
        public bool DebugFlyMode => debugFlyMode;
        public Animator CharacterAnimator => animator;
        public float VerticalVelocity => motor != null ? motor.VerticalVelocity : 0f;
        public PlayerCharacterState CurrentState => currentState;
        public bool IsCrouching => currentState == PlayerCharacterState.CrouchIdle
            || currentState == PlayerCharacterState.CrouchMove;

        private PlayerProfile Profile
        {
            get
            {
                if (profile == null) profile = GetComponent<PlayerProfile>();
                if (profile == null) profile = gameObject.AddComponent<PlayerProfile>();
                return profile;
            }
        }

        private void Awake()
        {
            ResolveReferences();
            EnsureVitals(true);
            CacheAnimatorParameters();
            BuildStateMachine();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureMotor();
            EnsureStateMachine();
            if (characterController != null) characterController.enabled = !debugFlyMode;
            stateMachine.Start(vitals.IsAlive ? PlayerCharacterState.Idle : PlayerCharacterState.Dead);
        }

        private void OnDisable()
        {
            debugFlyMode = false;
            pendingMiningAttackTimes.Clear();
            equipmentController?.CancelActiveLocomotionOverride();
            StopEquipmentLocomotionAnimation(false);
            idleSeconds = 0f;
            stateMachine?.Stop();
            motor?.ResetVerticalVelocity();
            ResolveReferences();
            if (characterController != null) characterController.enabled = true;
            SetAnimationState(false, false, true);
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            ResolveReferences();
            EnsureMotor();
            EnsureStateMachine();
            ApplyPendingMiningAttacksIfReady();
            if (characterController == null) return;

            if (Input.GetKeyDown(Profile.DebugToggleKey)) SetDebugFlyMode(!debugFlyMode);
            input = CaptureInput();
            equipmentController?.TickEquippedInteraction();
            if (debugFlyMode)
            {
                UpdateDebugFlyMovement(input.Move);
                SetAnimationState(false, false, true);
            }
            else if (TryUpdateEquipmentLocomotion())
            {
                motor.ResetVerticalVelocity();
            }
            else
            {
                stateMachine.Tick(Time.deltaTime);
            }

            TickLowerBodyLayerBlend(Time.deltaTime);
            currentState = stateMachine.Current;
            UpdateExpressionAnimation();
        }

        public bool ReceiveDamage(in DamageInfo damage)
        {
            EnsureVitals(false);
            if (!vitals.ApplyDamage(damage.Amount)) return false;

            ResolveReferences();
            EnsureMotor();
            EnsureStateMachine();
            if (!vitals.IsAlive)
            {
                stateMachine.Change(PlayerCharacterState.Dead);
            }
            else
            {
                stateMachine.Change(PlayerCharacterState.Hurt);
            }

            return true;
        }

        public void RestoreFullHealth()
        {
            EnsureVitals(false);
            vitals.RestoreFullHealth();
            EnsureStateMachine();
            stateMachine.Change(PlayerCharacterState.Idle);
        }

        private PlayerInputSnapshot CaptureInput()
        {
            Vector2 movement = new Vector2(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical"));
            if (movement.sqrMagnitude > 1f) movement.Normalize();
            bool acceptsAction = Cursor.lockState == CursorLockMode.Locked;
            bool primaryHeld = acceptsAction && Input.GetMouseButton(0);
            bool towingCart = cartAttractor != null && cartAttractor.IsTowingCart;
            return new PlayerInputSnapshot(
                movement,
                acceptsAction && Input.GetButtonDown("Jump"),
                primaryHeld && !towingCart
                    && toolController != null
                    && toolController.CanUseSelectedPrimaryAction(),
                Input.GetKey(Profile.CrouchKey),
                acceptsAction ? Input.mouseScrollDelta.y : 0f);
        }



        private void TickLocomotion(float deltaTime, bool acceptInput)
        {
            Vector2 movement = acceptInput ? input.Move : Vector2.zero;
            Vector3 worldMovement = GetWorldMovement(movement);
            UpdateThirdPersonFacing(worldMovement, deltaTime);
            // Crouch pose follows the held key regardless of acceptInput: an attack or
            // magnet action locks movement but should still show the crouched lower body.
            bool crouching = input.CrouchHeld && motor.IsGrounded;
            ConfigureMotor(crouching ? Profile.CrouchMoveSpeed : Profile.MoveSpeed);
            motor.Tick(worldMovement, deltaTime);

            bool grounded = motor.IsGrounded;
            bool moving = movement.sqrMagnitude >= Profile.MovingThreshold * Profile.MovingThreshold;
            crouching &= grounded;
            SetAnimationState(
                moving && grounded && !crouching,
                !grounded,
                !moving && grounded && !crouching,
                crouching,
                crouching && moving);

            if (!moving && grounded && !crouching)
            {
                idleSeconds += deltaTime;
                if (idleSeconds >= Profile.AlternateIdleDelay)
                {
                    if (hasIdleBFlag) animator.SetTrigger(IdleBFlag);
                    idleSeconds = 0f;
                }
            }
            else
            {
                idleSeconds = 0f;
            }
        }

        private void UpdateThirdPersonFacing(Vector3 worldMovement, float deltaTime)
        {
            bool thirdPerson = perspectiveCamera != null
                && perspectiveCamera.CurrentMode == PlayerViewMode.ThirdPerson;
            if (!thirdPerson)
            {
                hasThirdPersonTargetYaw = false;
                thirdPersonTurnVelocity = 0f;
                return;
            }

            if (worldMovement.sqrMagnitude > 0.0001f)
            {
                Vector3 direction = worldMovement.normalized;
                thirdPersonTargetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                hasThirdPersonTargetYaw = true;
            }

            if (!hasThirdPersonTargetYaw) return;

            float smoothedYaw = Mathf.SmoothDampAngle(
                transform.eulerAngles.y,
                thirdPersonTargetYaw,
                ref thirdPersonTurnVelocity,
                perspectiveCamera != null ? perspectiveCamera.ThirdPersonTurnSmoothTime : 0.18f,
                Mathf.Infinity,
                Mathf.Max(0f, deltaTime));
            transform.rotation = Quaternion.Euler(0f, smoothedYaw, 0f);
        }

        private Vector3 GetWorldMovement(Vector2 movement)
        {
            if (movement.sqrMagnitude <= 0.0001f) return Vector3.zero;

            if (perspectiveCamera == null
                || perspectiveCamera.CurrentMode != PlayerViewMode.ThirdPerson)
            {
                return transform.right * movement.x + transform.forward * movement.y;
            }

            Transform cameraTransform = perspectiveCamera.ControlledCamera != null
                ? perspectiveCamera.ControlledCamera.transform
                : view;
            if (cameraTransform == null)
                return transform.right * movement.x + transform.forward * movement.y;

            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            if (forward.sqrMagnitude <= 0.0001f) forward = transform.forward;
            else forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 worldMovement = right * movement.x + forward * movement.y;
            return worldMovement.sqrMagnitude > 1f ? worldMovement.normalized : worldMovement;
        }

        private bool TryEnterActionState()
        {
            if (input.PrimaryActionHeld
                && CanStartToolAction(toolController.SelectedDefinition))
            {
                stateMachine.Change(PlayerCharacterState.ToolAction);
                return true;
            }

            if (input.JumpPressed && motor.IsGrounded)
            {
                stateMachine.Change(PlayerCharacterState.Jump);
                return true;
            }

            return false;
        }

        private void SelectGroundOrAirState()
        {
            if (!motor.IsGrounded)
            {
                stateMachine.Change(motor.VerticalVelocity > 0f
                    ? PlayerCharacterState.Jump
                    : PlayerCharacterState.Fall);
            }
            else
            {
                bool moving = input.Move.sqrMagnitude
                    >= Profile.MovingThreshold * Profile.MovingThreshold;
                stateMachine.Change(ResolveGroundedLocomotionState(input.CrouchHeld, moving));
            }
        }

        private static PlayerCharacterState ResolveGroundedLocomotionState(
            bool crouching,
            bool moving)
        {
            if (crouching)
            {
                return moving
                    ? PlayerCharacterState.CrouchMove
                    : PlayerCharacterState.CrouchIdle;
            }
            return moving ? PlayerCharacterState.Move : PlayerCharacterState.Idle;
        }

        private void PerformAttack()
        {
            Vector3 forward = view != null ? view.forward : transform.forward;
            Vector3 origin = view != null
                ? view.position
                : transform.position + Vector3.up * 0.75f;
            Vector3 centre = origin + forward * (Profile.AttackReach * 0.5f);
            float radius = Mathf.Max(Profile.AttackRadius, Profile.AttackReach * 0.5f);
            MeleeCombat.DamageSphere(
                gameObject,
                centre,
                radius,
                forward,
                Profile.AttackMinimumForwardDot,
                Profile.AttackDamage,
                Profile.AttackImpulse,
                Profile.AttackLayers.value);
        }

        private void ScheduleMiningAttack(float delay)
        {
            pendingMiningAttackTimes.Enqueue(
                Time.time + Mathf.Max(0f, delay));
        }

        private void ApplyPendingMiningAttacksIfReady()
        {
            while (pendingMiningAttackTimes.Count > 0
                && Time.time >= pendingMiningAttackTimes.Peek())
            {
                pendingMiningAttackTimes.Dequeue();
                voxelInteractor?.ApplyPendingMineIfReady();
                PerformAttack();
            }
        }

        private void UpdateDebugFlyMovement(Vector2 moveInput)
        {
            Vector3 forward = view != null ? view.forward : transform.forward;
            Vector3 right = view != null ? view.right : transform.right;
            Vector3 movement = right * moveInput.x + forward * moveInput.y;
            if (Input.GetKey(KeyCode.Space)) movement += Vector3.up;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C)) movement -= Vector3.up;
            if (movement.sqrMagnitude > 1f) movement.Normalize();
            float multiplier = Input.GetKey(KeyCode.LeftShift) ? Profile.DebugFlySpeedMultiplier : 1f;
            transform.position += movement * Profile.DebugFlySpeed * multiplier * Time.deltaTime;
        }

        private bool TryUpdateEquipmentLocomotion()
        {
            if (equipmentController == null
                || !equipmentController.IsLocomotionOverrideActive)
            {
                StopEquipmentLocomotionAnimation(true);
                return false;
            }

            if (stateMachine.Current == PlayerCharacterState.ToolAction)
                stateMachine.Change(PlayerCharacterState.Idle);

            Vector3 worldMovement = GetWorldMovement(input.Move);
            UpdateThirdPersonFacing(worldMovement, Time.deltaTime);
            bool handled = equipmentController.TryHandleLocomotion(
                characterController,
                worldMovement,
                Profile.MoveSpeed,
                Time.deltaTime);
            if (!handled)
                return false;

            StartEquipmentLocomotionAnimation(
                equipmentController.ActiveLocomotionAnimation);
            bool moving = input.Move.sqrMagnitude
                >= Profile.MovingThreshold * Profile.MovingThreshold;
            PlayerCharacterState locomotionState = moving
                ? PlayerCharacterState.Move
                : PlayerCharacterState.Idle;
            if (stateMachine.Current != locomotionState)
                stateMachine.Change(locomotionState);
            return true;
        }

        private void UpdateExpressionAnimation()
        {
            if (animator == null
                || animator.runtimeAnimatorController == null
                || !animator.isInitialized)
            {
                return;
            }
            if (hasSmileFlag) animator.SetBool(SmileFlag, Input.GetKey(Profile.SmileKey));
            if (hasHitFlag && Input.GetKeyDown(Profile.HitKey))
                animator.SetTrigger(HitFlag);
            if (hasDieFlag && Input.GetKeyDown(Profile.DieKey))
                animator.SetTrigger(DieFlag);
            if (hasRecoverFlag && Input.GetKeyDown(Profile.RecoverKey))
                animator.SetTrigger(RecoverFlag);

            bool kocchi = false;
            Transform target = kocchiTarget;
            if (target == null && Camera.main != null && !Camera.main.transform.IsChildOf(transform))
            {
                target = Camera.main.transform;
            }

            if (target != null && Profile.KocchiDistance > 0f)
            {
                kocchi = (target.position - transform.position).sqrMagnitude
                    < Profile.KocchiDistance * Profile.KocchiDistance;
            }

            if (hasKocchiFlag) animator.SetBool(KocchiFlag, kocchi);
        }

        private void SetAnimationState(
            bool walking,
            bool jumping,
            bool idle,
            bool crouching = false,
            bool crouchMoving = false)
        {
            if (animator == null
                || animator.runtimeAnimatorController == null
                || !animator.isInitialized)
            {
                return;
            }
            if (hasWalkFlag) animator.SetBool(WalkFlag, walking);
            if (hasJumpFlag) animator.SetBool(JumpFlag, jumping);
            if (hasIdleFlag) animator.SetBool(IdleFlag, idle);
            if (hasCrouchFlag) animator.SetBool(CrouchFlag, crouching);
            if (hasCrouchMoveFlag) animator.SetBool(CrouchMoveFlag, crouchMoving);

            // The lower-body masked layer only carries crouch leg poses; keep it fully
            // silent while standing so the base layer's own legs show through unmodified.
            // The actual weight eases toward this target in Update() instead of snapping,
            // so standing up/crouching down blends instead of popping.
            if (lowerBodyLayerIndex >= 0)
                lowerBodyLayerTargetWeight = crouching ? 1f : 0f;
        }

        private void TickLowerBodyLayerBlend(float deltaTime)
        {
            if (lowerBodyLayerIndex < 0 || animator == null || animator.runtimeAnimatorController == null) return;
            float blendSpeed = 1f / Profile.CrouchBlendDuration;
            lowerBodyLayerWeight = Mathf.MoveTowards(
                lowerBodyLayerWeight, lowerBodyLayerTargetWeight, blendSpeed * deltaTime);
            animator.SetLayerWeight(lowerBodyLayerIndex, lowerBodyLayerWeight);
        }

        public void SetDebugFlyMode(bool enabled)
        {
            ResolveReferences();
            EnsureMotor();
            EnsureStateMachine();
            if (enabled && stateMachine.IsRunning
                && stateMachine.Current == PlayerCharacterState.ToolAction)
            {
                stateMachine.Change(PlayerCharacterState.Idle);
            }
            debugFlyMode = enabled;
            if (enabled)
                equipmentController?.CancelActiveLocomotionOverride();
            idleSeconds = 0f;
            motor?.ResetVerticalVelocity();
            if (characterController != null) characterController.enabled = !enabled;
        }

        public void SetAnimator(Animator characterAnimator)
        {
            if (animator != characterAnimator) ResetToolAnimatorController();
            animator = characterAnimator;
            if (animator != null) animator.applyRootMotion = false;
            CacheAnimatorParameters();
        }

        private void ResolveReferences()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }
            if (perspectiveCamera == null)
            {
                perspectiveCamera = GetComponent<PerspectiveCameraController>();
                if (perspectiveCamera == null)
                    perspectiveCamera = Object.FindObjectOfType<PerspectiveCameraController>();
            }

            if (perspectiveCamera != null) perspectiveCamera.SetPlayerRoot(transform);

            if (cartAttractor == null)
            {
                cartAttractor = GetComponent<FirstPersonCartAttractor>();
            }
            if (toolController == null)
            {
                toolController = GetComponent<PlayerToolController>();
            }
            if (equipmentController == null)
            {
                equipmentController = GetComponent<PlayerEquipmentController>();
                if (equipmentController == null)
                    equipmentController = gameObject.AddComponent<PlayerEquipmentController>();
            }
            if (voxelInteractor == null)
            {
                voxelInteractor = GetComponent<VoxelPlayerInteractor>();
            }
            if (view == null)
            {
                Camera childCamera = GetComponentInChildren<Camera>(true);
                if (childCamera != null) view = childCamera.transform;
            }

            if (animator == null || !animator.gameObject.activeInHierarchy)
            {
                Animator resolvedAnimator = GetComponentInChildren<Animator>(false);
                if (resolvedAnimator != animator)
                {
                    ResetToolAnimatorController();
                    animator = resolvedAnimator;
                    if (animator != null) animator.applyRootMotion = false;
                    CacheAnimatorParameters();
                }
            }
            else if (animator.applyRootMotion)
            {
                // CharacterController owns world movement. Root motion must never
                // move the visual hierarchy independently into voxel geometry.
                animator.applyRootMotion = false;
            }

        }

        private void EnsureMotor()
        {
            if (characterController == null) return;
            if (motor == null)
            {
                motor = new CharacterControllerMotor(
                    characterController,
                    Profile.MoveSpeed,
                    Profile.JumpHeight,
                    Profile.Gravity,
                    Profile.GroundedForce);
            }
            else
            {
                motor.Configure(
                    Profile.MoveSpeed,
                    Profile.JumpHeight,
                    Profile.Gravity,
                    Profile.GroundedForce);
            }
        }

        private void ConfigureMotor(float moveSpeed)
        {
            motor.Configure(
                moveSpeed,
                Profile.JumpHeight,
                Profile.Gravity,
                Profile.GroundedForce);
        }

        private void EnsureVitals(bool refill)
        {
            if (vitals == null)
            {
                vitals = new CharacterVitals();
                refill = true;
            }
            vitals.Initialize(Profile.MaximumHealth, refill);
        }

        private void CacheAnimatorParameters()
        {
            hasWalkFlag = HasAnimatorParameter(WalkFlag, AnimatorControllerParameterType.Bool);
            hasJumpFlag = HasAnimatorParameter(JumpFlag, AnimatorControllerParameterType.Bool);
            hasIdleFlag = HasAnimatorParameter(IdleFlag, AnimatorControllerParameterType.Bool);
            hasIdleBFlag = HasAnimatorParameter(IdleBFlag, AnimatorControllerParameterType.Trigger);
            hasSmileFlag = HasAnimatorParameter(SmileFlag, AnimatorControllerParameterType.Bool);
            hasKocchiFlag = HasAnimatorParameter(KocchiFlag, AnimatorControllerParameterType.Bool);
            hasHitFlag = HasAnimatorParameter(HitFlag, AnimatorControllerParameterType.Trigger);
            hasDieFlag = HasAnimatorParameter(DieFlag, AnimatorControllerParameterType.Trigger);
            hasRecoverFlag = HasAnimatorParameter(RecoverFlag, AnimatorControllerParameterType.Trigger);
            hasCrouchFlag = HasAnimatorParameter(CrouchFlag, AnimatorControllerParameterType.Bool);
            hasCrouchMoveFlag = HasAnimatorParameter(
                CrouchMoveFlag,
                AnimatorControllerParameterType.Bool);
            hasToolActionTrigger = HasAnimatorParameter(
                ToolActionTrigger,
                AnimatorControllerParameterType.Trigger);
            hasToolActionContinuousFlag = HasAnimatorParameter(
                ToolActionContinuousFlag,
                AnimatorControllerParameterType.Bool);
            lowerBodyLayerIndex = animator != null && animator.runtimeAnimatorController != null
                ? animator.GetLayerIndex("LowerBody Layer")
                : -1;
        }

        /// <summary>
        /// The Animator keeps the original one-shot trigger and transition timing. Tool data only
        /// replaces the placeholder clip, so gameplay input never controls animation completion.
        /// </summary>
        private void TriggerToolActionAnimation()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (hasToolActionTrigger) animator.SetTrigger(ToolActionTrigger);
        }

        private void StartConfiguredToolActionAnimation()
        {
            if (activeToolDefinition == null) return;
            switch (activeToolDefinition.AnimationTriggerMode)
            {
                case PlayerToolAnimationTriggerMode.Single:
                    TriggerToolActionAnimation();
                    break;
                case PlayerToolAnimationTriggerMode.Continuous:
                    SetContinuousToolActionAnimation(true);
                    break;
            }
        }

        private void TriggerPeriodicToolActionAnimation()
        {
            if (activeToolDefinition != null
                && activeToolDefinition.AnimationTriggerMode
                    == PlayerToolAnimationTriggerMode.Periodic)
            {
                TriggerToolActionAnimation();
            }
        }

        private void StopConfiguredToolActionAnimation()
        {
            if (activeToolDefinition != null
                && activeToolDefinition.AnimationTriggerMode
                    == PlayerToolAnimationTriggerMode.Continuous)
            {
                SetContinuousToolActionAnimation(false);
            }
        }

        private void SetContinuousToolActionAnimation(bool active)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (hasToolActionContinuousFlag)
                animator.SetBool(ToolActionContinuousFlag, active);
        }

        private bool CanStartToolAction(PlayerToolDefinition definition)
        {
            if (definition == null || !definition.HasPrimaryAction) return false;
            switch (definition.PrimaryAction)
            {
                case PlayerToolPrimaryAction.MineVoxel:
                    return IsPeriodicToolActionCycleComplete();
                case PlayerToolPrimaryAction.AttractCart:
                    return cartAttractor != null && cartAttractor.CanOperate;
                case PlayerToolPrimaryAction.ThrowPersistentLight:
                    return definition.ProjectilePrefab != null
                        && Time.time >= nextProjectileThrowTime;
                default:
                    return false;
            }
        }

        private void ApplyToolActionAnimation(PlayerToolDefinition definition)
        {
            if (definition == null
                || definition.PrimaryActionAnimation == null
                || !EnsureToolAnimatorController())
            {
                return;
            }

            toolAnimatorController[PrimaryActionPlaceholderClipName] =
                definition.PrimaryActionAnimation;
        }

        private void StartEquipmentLocomotionAnimation(AnimationClip animation)
        {
            if (animation == null
                || !EnsureToolAnimatorController()
                || equipmentLocomotionPlaceholderClip == null)
            {
                return;
            }

            bool animationChanged = activeEquipmentLocomotionAnimation != animation;
            if (animationChanged)
            {
                toolAnimatorController[EquipmentLocomotionPlaceholderClipName] = animation;
                activeEquipmentLocomotionAnimation = animation;
            }

            equipmentLocomotionAnimationActive = true;
            bool isTransitioningAway =
                IsTransitioningAwayFromEquipmentLocomotion();
            equipmentLocomotionExitRequested = false;
            if (IsEquipmentLocomotionStateActive() && !isTransitioningAway)
            {
                if (animationChanged)
                {
                    animator.CrossFadeInFixedTime(
                        EquipmentLocomotionState,
                        0.08f,
                        0,
                        0f);
                }
                return;
            }

            SetAnimationState(false, false, false);
            animator.CrossFadeInFixedTime(
                EquipmentLocomotionState,
                0.12f,
                0,
                0f);
        }

        private void StopEquipmentLocomotionAnimation(bool crossFadeToIdle)
        {
            if (equipmentLocomotionExitRequested)
            {
                if (!IsEquipmentLocomotionStateActive())
                    equipmentLocomotionExitRequested = false;
                return;
            }

            if (!equipmentLocomotionAnimationActive
                && !IsEquipmentLocomotionStateActive())
                return;

            equipmentLocomotionAnimationActive = false;
            activeEquipmentLocomotionAnimation = null;
            if (crossFadeToIdle
                && animator != null
                && animator.runtimeAnimatorController != null
                && animator.isInitialized)
            {
                SetAnimationState(false, false, true);
                animator.CrossFadeInFixedTime(IdleState, 0.12f, 0, 0f);
                equipmentLocomotionExitRequested = true;
            }
        }

        private bool IsTransitioningAwayFromEquipmentLocomotion()
        {
            if (animator == null
                || animator.runtimeAnimatorController == null
                || !animator.isInitialized
                || !animator.IsInTransition(0))
            {
                return false;
            }

            return animator.GetCurrentAnimatorStateInfo(0).fullPathHash
                    == EquipmentLocomotionState
                && animator.GetNextAnimatorStateInfo(0).fullPathHash
                    != EquipmentLocomotionState;
        }

        private bool IsEquipmentLocomotionStateActive()
        {
            if (animator == null
                || animator.runtimeAnimatorController == null
                || !animator.isInitialized)
                return false;

            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            if (current.fullPathHash == EquipmentLocomotionState)
                return true;
            return animator.IsInTransition(0)
                && animator.GetNextAnimatorStateInfo(0).fullPathHash
                    == EquipmentLocomotionState;
        }

        private bool EnsureToolAnimatorController()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            if (toolAnimatorController != null
                && primaryActionPlaceholderClip != null
                && equipmentLocomotionPlaceholderClip != null)
            {
                return true;
            }

            baseAnimatorController = animator.runtimeAnimatorController;
            AnimationClip[] clips = baseAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null) continue;
                if (clips[i].name == PrimaryActionPlaceholderClipName)
                    primaryActionPlaceholderClip = clips[i];
                else if (clips[i].name == EquipmentLocomotionPlaceholderClipName)
                    equipmentLocomotionPlaceholderClip = clips[i];
            }

            if (primaryActionPlaceholderClip == null
                || equipmentLocomotionPlaceholderClip == null)
            {
                Debug.LogError(
                    $"Animator '{baseAnimatorController.name}' has no "
                    + "required runtime animation placeholder clips.",
                    this);
                return false;
            }

            toolAnimatorController = new AnimatorOverrideController(baseAnimatorController)
            {
                name = $"{baseAnimatorController.name} (Runtime Tool Override)",
            };
            animator.runtimeAnimatorController = toolAnimatorController;
            return true;
        }

        private void ResetToolAnimatorController()
        {
            if (animator != null
                && toolAnimatorController != null
                && animator.runtimeAnimatorController == toolAnimatorController
                && baseAnimatorController != null)
            {
                animator.runtimeAnimatorController = baseAnimatorController;
            }

            baseAnimatorController = null;
            toolAnimatorController = null;
            primaryActionPlaceholderClip = null;
            equipmentLocomotionPlaceholderClip = null;
            activeEquipmentLocomotionAnimation = null;
            equipmentLocomotionAnimationActive = false;
            equipmentLocomotionExitRequested = false;
        }

        private bool HasAnimatorParameter(int nameHash, AnimatorControllerParameterType type)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].nameHash == nameHash && parameters[i].type == type) return true;
            }
            return false;
        }

        private void BuildStateMachine()
        {
            stateMachine = new CharacterStateMachine<PlayerCharacterState>();
            stateMachine.Add(new PlayerState(this, PlayerCharacterState.Idle, TickIdle));
            stateMachine.Add(new PlayerState(this, PlayerCharacterState.Move, TickMove));
            stateMachine.Add(new PlayerState(this, PlayerCharacterState.Jump, TickJump, EnterJump));
            stateMachine.Add(new PlayerState(this, PlayerCharacterState.Fall, TickFall));
            stateMachine.Add(new PlayerState(
                this,
                PlayerCharacterState.ToolAction,
                TickToolAction,
                EnterToolAction,
                ExitToolAction));
            stateMachine.Add(new PlayerState(this, PlayerCharacterState.Hurt, TickHurt, EnterHurt, ExitHurt));
            stateMachine.Add(new PlayerState(this, PlayerCharacterState.Dead, TickDead, EnterDead));
            stateMachine.Add(new PlayerState(this, PlayerCharacterState.CrouchIdle, TickCrouch));
            stateMachine.Add(new PlayerState(this, PlayerCharacterState.CrouchMove, TickCrouch));
        }

        private void EnsureStateMachine()
        {
            if (stateMachine == null) BuildStateMachine();
            if (!stateMachine.IsRunning)
                stateMachine.Start(vitals.IsAlive ? PlayerCharacterState.Idle : PlayerCharacterState.Dead);
        }

        private void TickIdle(float deltaTime)
        {
            if (TryEnterActionState()) return;
            TickLocomotion(deltaTime, true);
            SelectGroundOrAirState();
        }

        private void TickMove(float deltaTime)
        {
            if (TryEnterActionState()) return;
            TickLocomotion(deltaTime, true);
            SelectGroundOrAirState();
        }

        private void TickCrouch(float deltaTime)
        {
            if (TryEnterActionState()) return;
            TickLocomotion(deltaTime, true);
            SelectGroundOrAirState();
        }

        private void EnterJump()
        {
            motor.RequestJump();
            SetAnimationState(false, true, false);
        }

        private void TickJump(float deltaTime)
        {
            if (TryEnterActionState()) return;
            TickLocomotion(deltaTime, true);
            if (motor.IsGrounded) SelectGroundOrAirState();
            else if (motor.VerticalVelocity <= 0f) stateMachine.Change(PlayerCharacterState.Fall);
        }

        private void TickFall(float deltaTime)
        {
            if (TryEnterActionState()) return;
            TickLocomotion(deltaTime, true);
            if (motor.IsGrounded) SelectGroundOrAirState();
        }

        private void EnterToolAction()
        {
            EnsureMotor();
            stateSeconds = 0f;
            activeToolDefinition = toolController != null
                ? toolController.SelectedDefinition
                : null;
            bool grounded = motor != null && motor.IsGrounded;
            bool crouching = input.CrouchHeld && grounded;
            SetAnimationState(false, !grounded, false, crouching);
            ApplyToolActionAnimation(activeToolDefinition);
            StartConfiguredToolActionAnimation();

            if (activeToolDefinition == null) return;
            switch (activeToolDefinition.PrimaryAction)
            {
                case PlayerToolPrimaryAction.MineVoxel:
                    TriggerMineSwing();
                    break;
                case PlayerToolPrimaryAction.AttractCart:
                    cartAttractor?.BeginAttraction();
                    break;
                case PlayerToolPrimaryAction.ThrowPersistentLight:
                    ThrowConfiguredProjectile(activeToolDefinition);
                    break;
            }
        }

        private void ExitToolAction()
        {
            if (activeToolDefinition != null
                && activeToolDefinition.PrimaryAction == PlayerToolPrimaryAction.AttractCart)
            {
                cartAttractor?.EndAttraction();
            }
            StopConfiguredToolActionAnimation();
            activeToolDefinition = null;
        }

        // One mining cycle starts the configured animation and schedules its impact.
        // The next cycle waits for that Animator state to finish, so the visual and
        // gameplay cadence share the same source of truth.
        private void TriggerMineSwing()
        {
            periodicToolAnimationObserved = false;
            TriggerPeriodicToolActionAnimation();
            ScheduleMiningAttack(Profile.VoxelDestructionDelay);
            bool isPickaxe = activeToolDefinition != null
                && activeToolDefinition.Item == PlayerInventoryItem.Pickaxe;
            int strikeNumber = isPickaxe ? pickaxeStrikeParity + 1 : 1;
            VoxelMiningBrushSettings brush = activeToolDefinition != null
                ? activeToolDefinition.GetMiningBrushForStrike(strikeNumber)
                : VoxelMiningBrushSettings.SingleVoxel;
            bool scheduled = voxelInteractor != null
                && voxelInteractor.TryScheduleMineAtCrosshair(
                    Profile.VoxelDestructionDelay,
                    brush);
            if (scheduled && isPickaxe)
            {
                pickaxeStrikeParity ^= 1;
            }

            AnimationClip clip = activeToolDefinition != null
                ? activeToolDefinition.PrimaryActionAnimation
                : null;
            float fallbackDuration = clip != null
                ? Mathf.Max(0.02f, clip.length)
                : Profile.MineInterval;
            nextAttackTime = Time.time + fallbackDuration;
        }

        private void TickToolAction(float deltaTime)
        {
            if (activeToolDefinition == null
                || toolController == null
                || toolController.SelectedDefinition != activeToolDefinition)
            {
                SelectGroundOrAirState();
                return;
            }

            stateSeconds += deltaTime;
            TickLocomotion(deltaTime, activeToolDefinition.AllowMovementWhileUsing);
            bool actionHeld = input.PrimaryActionHeld;
            if (!actionHeld
                && (activeToolDefinition.PrimaryAction != PlayerToolPrimaryAction.MineVoxel
                    || stateSeconds >= Profile.AttackDuration))
            {
                SelectGroundOrAirState();
                return;
            }

            switch (activeToolDefinition.PrimaryAction)
            {
                case PlayerToolPrimaryAction.MineVoxel:
                    TickMiningToolAction(actionHeld);
                    return;
                case PlayerToolPrimaryAction.AttractCart:
                    TickAttractorToolAction();
                    return;
                case PlayerToolPrimaryAction.ThrowPersistentLight:
                    return;
                default:
                    SelectGroundOrAirState();
                    return;
            }
        }

        private void TickMiningToolAction(bool actionHeld)
        {
            if (actionHeld && IsPeriodicToolActionCycleComplete()) TriggerMineSwing();
        }

        private bool IsPeriodicToolActionCycleComplete()
        {
            if (animator == null
                || animator.runtimeAnimatorController == null
                || !hasToolActionTrigger)
            {
                return Time.time >= nextAttackTime;
            }

            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
            bool animationIsPlaying = current.fullPathHash == ToolPrimaryActionState;
            if (animator.IsInTransition(0))
            {
                AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
                animationIsPlaying |= next.fullPathHash == ToolPrimaryActionState;
            }

            if (animationIsPlaying)
            {
                periodicToolAnimationObserved = true;
                return false;
            }

            return periodicToolAnimationObserved || Time.time >= nextAttackTime;
        }

        private void TickAttractorToolAction()
        {
            if (cartAttractor == null || !cartAttractor.IsActionActive)
            {
                SelectGroundOrAirState();
                return;
            }

            cartAttractor.TickAttraction(input.AttractionDistanceSteps);
        }

        private PersistentLightProjectile ThrowConfiguredProjectile(
            PlayerToolDefinition definition)
        {
            if (definition == null || definition.ProjectilePrefab == null)
                return null;

            Transform origin = view != null ? view : transform;
            Vector3 forward = origin.forward.sqrMagnitude > 0.0001f
                ? origin.forward.normalized
                : transform.forward;
            Vector3 position = origin.position
                + forward * definition.ThrowForwardOffset;
            PersistentLightProjectile projectile = Instantiate(
                definition.ProjectilePrefab,
                position,
                Quaternion.LookRotation(forward, Vector3.up));
            projectile.name = definition.ProjectilePrefab.name;
            projectile.Launch(
                forward * definition.ThrowSpeed
                    + Vector3.up * definition.UpwardThrowSpeed,
                Random.onUnitSphere * definition.ThrowSpinSpeed);
            IgnoreOwnerCollisions(projectile);
            nextProjectileThrowTime = Time.time + definition.ThrowCooldown;
            return projectile;
        }

        private void IgnoreOwnerCollisions(PersistentLightProjectile projectile)
        {
            if (projectile == null) return;
            Collider[] ownerColliders = GetComponentsInChildren<Collider>(true);
            Collider[] projectileColliders =
                projectile.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < ownerColliders.Length; i++)
            {
                if (ownerColliders[i] == null) continue;
                for (int j = 0; j < projectileColliders.Length; j++)
                {
                    if (projectileColliders[j] != null)
                    {
                        Physics.IgnoreCollision(
                            ownerColliders[i],
                            projectileColliders[j],
                            true);
                    }
                }
            }
        }

        private void EnterHurt()
        {
            stateSeconds = 0f;
            if (animator != null && hasHitFlag) animator.SetTrigger(HitFlag);
            SetAnimationState(false, motor != null && !motor.IsGrounded, false);
        }

        private void TickHurt(float deltaTime)
        {
            stateSeconds += deltaTime;
            TickLocomotion(deltaTime, false);
            if (stateSeconds >= Profile.HurtDuration) SelectGroundOrAirState();
        }

        private void ExitHurt()
        {
            if (vitals.IsAlive && animator != null && hasRecoverFlag)
                animator.SetTrigger(RecoverFlag);
        }

        private void EnterDead()
        {
            if (animator != null && hasDieFlag) animator.SetTrigger(DieFlag);
            SetAnimationState(false, false, false);
        }

        private void TickDead(float deltaTime)
        {
            TickLocomotion(deltaTime, false);
            SetAnimationState(false, false, false);
        }



        private readonly struct PlayerInputSnapshot
        {
            public PlayerInputSnapshot(
                Vector2 move,
                bool jumpPressed,
                bool primaryActionHeld,
                bool crouchHeld,
                float attractionDistanceSteps)
            {
                Move = move;
                JumpPressed = jumpPressed;
                PrimaryActionHeld = primaryActionHeld;
                CrouchHeld = crouchHeld;
                AttractionDistanceSteps = attractionDistanceSteps;
            }

            public Vector2 Move { get; }
            public bool JumpPressed { get; }
            public bool PrimaryActionHeld { get; }
            public bool CrouchHeld { get; }
            public float AttractionDistanceSteps { get; }
        }

        private sealed class PlayerState : ICharacterState<PlayerCharacterState>
        {
            private readonly VoxelPlayerController owner;
            private readonly System.Action<float> tick;
            private readonly System.Action enter;
            private readonly System.Action exit;

            public PlayerState(
                VoxelPlayerController owner,
                PlayerCharacterState id,
                System.Action<float> tick,
                System.Action enter = null,
                System.Action exit = null)
            {
                this.owner = owner;
                Id = id;
                this.tick = tick;
                this.enter = enter;
                this.exit = exit;
            }

            public PlayerCharacterState Id { get; }
            public void Enter() { owner.currentState = Id; enter?.Invoke(); }
            public void Tick(float deltaTime) { tick(deltaTime); }
            public void Exit() { exit?.Invoke(); }
        }

        private interface IPlayerMotor
        {
            bool IsGrounded { get; }
            float VerticalVelocity { get; }
            void Configure(float speed, float height, float gravityValue, float groundForce);
            void RequestJump();
            void Tick(Vector3 planarMovement, float deltaTime);
            void ResetVerticalVelocity();
        }

        private sealed class CharacterControllerMotor : IPlayerMotor
        {
            private readonly CharacterController controller;
            private float moveSpeed;
            private float jumpHeight;
            private float gravity;
            private float groundedForce;

            public CharacterControllerMotor(
                CharacterController controller,
                float moveSpeed,
                float jumpHeight,
                float gravity,
                float groundedForce)
            {
                this.controller = controller;
                Configure(moveSpeed, jumpHeight, gravity, groundedForce);
            }

            public bool IsGrounded => controller != null && controller.enabled && controller.isGrounded;
            public float VerticalVelocity { get; private set; }

            public void Configure(float speed, float height, float gravityValue, float groundForce)
            {
                moveSpeed = Mathf.Max(0f, speed);
                jumpHeight = Mathf.Max(0f, height);
                gravity = Mathf.Max(0f, gravityValue);
                groundedForce = Mathf.Max(0f, groundForce);
            }

            public void RequestJump()
            {
                if (IsGrounded) VerticalVelocity = Mathf.Sqrt(jumpHeight * 2f * gravity);
            }

            public void Tick(Vector3 planarMovement, float deltaTime)
            {
                if (controller == null || !controller.enabled) return;
                bool wasGrounded = controller.isGrounded;
                if (wasGrounded && VerticalVelocity <= 0f)
                    VerticalVelocity = -groundedForce;
                else
                    VerticalVelocity -= gravity * deltaTime;

                if (planarMovement.sqrMagnitude > 1f) planarMovement.Normalize();
                Vector3 velocity = planarMovement * moveSpeed + Vector3.up * VerticalVelocity;
                controller.Move(velocity * deltaTime);
            }

            public void ResetVerticalVelocity()
            {
                VerticalVelocity = 0f;
            }
        }
    }
}
