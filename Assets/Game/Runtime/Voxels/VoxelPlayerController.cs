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
        private static readonly int ToolActionSpeed =
            Animator.StringToHash("ToolActionSpeed");
        private static readonly int ToolPrimaryActionState =
            Animator.StringToHash("Tool Primary Action");
        private static readonly int ToolContinuousActionState =
            Animator.StringToHash("Tool Continuous Action");
        private static readonly int ToolUpperBodyContinuousActionState =
            Animator.StringToHash(
                "Tool UpperBody Layer.Tool Continuous Action");
        private static readonly int ToolArmsContinuousActionState =
            Animator.StringToHash(
                "Crouch Tool Arms Layer.Tool Continuous Action");
        private static readonly int EquipmentLocomotionState =
            Animator.StringToHash("Base Layer.Equipment Locomotion");
        private static readonly int IdleState =
            Animator.StringToHash("Base Layer.Idle");
        private const string PrimaryActionPlaceholderClipName = "ToolPrimaryActionPlaceholder";
        private const string EquipmentLocomotionPlaceholderClipName =
            "EquipmentLocomotionPlaceholder";
        private const string RifleLocomotionLayerName =
            "Rifle Locomotion Layer";
        private const string RifleArmsLayerName = "Rifle Arms Layer";
        private const float ToolUpperBodyLayerBlendDuration = 0.12f;
        private const float CrouchArmsLayerBlendDuration = 0.12f;
        private const float RifleLocomotionLayerBlendDuration = 0.12f;

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
        private readonly Queue<float> pendingMiningAttackTimes =
            new Queue<float>();
        private readonly List<ScheduledToolAction> pendingToolActions =
            new List<ScheduledToolAction>();
        private readonly Dictionary<PlayerToolDefinition, float>
            nextToolActionCycleTimes =
                new Dictionary<PlayerToolDefinition, float>();
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
        private bool hasToolActionSpeed;
        private RuntimeAnimatorController baseAnimatorController;
        private AnimatorOverrideController toolAnimatorController;
        private AnimationClip primaryActionPlaceholderClip;
        private AnimationClip activePrimaryActionAnimation;
        private AnimationClip equipmentLocomotionPlaceholderClip;
        private AnimationClip activeEquipmentLocomotionAnimation;
        private bool equipmentLocomotionAnimationActive;
        private bool equipmentLocomotionExitRequested;
        private PlayerToolDefinition activeToolDefinition;
        private PlayerToolController subscribedToolController;
        private int pickaxeStrikeParity;
        private int crouchArmsLocomotionLayerIndex = -1;
        private int rifleLocomotionLayerIndex = -1;
        private int rifleArmsLayerIndex = -1;
        private int toolUpperBodyLayerIndex = -1;
        private int crouchToolArmsLayerIndex = -1;
        private int activeToolActionLayerIndex = -1;
        private float toolUpperBodyLayerTargetWeight;
        private float toolUpperBodyLayerWeight;
        private float crouchToolArmsLayerTargetWeight;
        private float crouchToolArmsLayerWeight;
        private float crouchArmsLocomotionLayerWeight;
        private float rifleLocomotionLayerTargetWeight;
        private float rifleLocomotionLayerWeight;
        private float rifleArmsLayerTargetWeight;
        private float rifleArmsLayerWeight;
        private bool toolUpperBodyActionObserved;

        private readonly struct ScheduledToolAction
        {
            public ScheduledToolAction(
                PlayerToolDefinition definition,
                float triggerTime)
            {
                Definition = definition;
                TriggerTime = triggerTime;
            }

            public PlayerToolDefinition Definition { get; }
            public float TriggerTime { get; }
        }

        public GameObject Owner => gameObject;
        public float CurrentHealth => vitals != null ? vitals.CurrentHealth : 0f;
        public float MaximumHealth => vitals != null ? vitals.MaximumHealth : Profile.MaximumHealth;
        public float CrouchPoseWeight => crouchArmsLocomotionLayerWeight;
        public bool IsRifleSelected => toolController != null
            && toolController.IsRifleSelected;
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
            SubscribeToToolSelection();
            ApplyToolActionAnimation(
                toolController != null
                    ? toolController.SelectedDefinition
                    : null);
            EnsureMotor();
            EnsureStateMachine();
            if (characterController != null) characterController.enabled = !debugFlyMode;
            stateMachine.Start(vitals.IsAlive ? PlayerCharacterState.Idle : PlayerCharacterState.Dead);
        }

        private void OnDisable()
        {
            UnsubscribeFromToolSelection();
            debugFlyMode = false;
            pendingMiningAttackTimes.Clear();
            pendingToolActions.Clear();
            nextToolActionCycleTimes.Clear();
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
            ApplyPendingToolActionsIfReady();
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

            TickCrouchArmsLocomotionLayerBlend(Time.deltaTime);
            TickRifleLocomotionLayerBlend(Time.deltaTime);
            TickRifleArmsLayerBlend(Time.deltaTime);
            TickToolUpperBodyLayerBlend(Time.deltaTime);
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
            bool cartTowClickConsumed = cartAttractor != null
                && cartAttractor.ConsumedCartTowClickThisFrame;
            return new PlayerInputSnapshot(
                movement,
                acceptsAction && Input.GetButtonDown("Jump"),
                primaryHeld && !towingCart && !cartTowClickConsumed
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

            if (input.JumpPressed && motor.IsGrounded && !input.CrouchHeld)
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
            if (input.CrouchHeld)
            {
                equipmentController?.CancelActiveLocomotionOverride();
                StopEquipmentLocomotionAnimation(true);
                return false;
            }

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
            bool rifleSelected = IsRifleSelected;
            rifleLocomotionLayerTargetWeight = rifleSelected
                && !jumping
                && !crouching
                && (walking
                    || idle
                    || activeToolDefinition != null
                        && activeToolDefinition.PrimaryAction
                            == PlayerToolPrimaryAction.FireRifle)
                ? 1f
                : 0f;
            rifleArmsLayerTargetWeight = rifleSelected
                && (jumping || crouching)
                ? 1f
                : 0f;
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
            hasToolActionSpeed = HasAnimatorParameter(
                ToolActionSpeed,
                AnimatorControllerParameterType.Float);
            if (hasToolActionSpeed) animator.SetFloat(ToolActionSpeed, 1f);
            crouchArmsLocomotionLayerIndex = animator != null
                && animator.runtimeAnimatorController != null
                ? animator.GetLayerIndex("Crouch Arms Locomotion Layer")
                : -1;
            rifleLocomotionLayerIndex = animator != null
                && animator.runtimeAnimatorController != null
                ? animator.GetLayerIndex(RifleLocomotionLayerName)
                : -1;
            rifleArmsLayerIndex = animator != null
                && animator.runtimeAnimatorController != null
                ? animator.GetLayerIndex(RifleArmsLayerName)
                : -1;
            toolUpperBodyLayerIndex = animator != null && animator.runtimeAnimatorController != null
                ? animator.GetLayerIndex("Tool UpperBody Layer")
                : -1;
            crouchToolArmsLayerIndex = animator != null
                && animator.runtimeAnimatorController != null
                ? animator.GetLayerIndex("Crouch Tool Arms Layer")
                : -1;
            activeToolActionLayerIndex = -1;
            toolUpperBodyLayerTargetWeight = 0f;
            toolUpperBodyLayerWeight = 0f;
            crouchToolArmsLayerTargetWeight = 0f;
            crouchToolArmsLayerWeight = 0f;
            crouchArmsLocomotionLayerWeight = 0f;
            rifleLocomotionLayerTargetWeight = 0f;
            rifleLocomotionLayerWeight = 0f;
            rifleArmsLayerTargetWeight = 0f;
            rifleArmsLayerWeight = 0f;
            toolUpperBodyActionObserved = false;
            if (crouchArmsLocomotionLayerIndex >= 0)
                animator.SetLayerWeight(crouchArmsLocomotionLayerIndex, 0f);
            if (rifleLocomotionLayerIndex >= 0)
                animator.SetLayerWeight(rifleLocomotionLayerIndex, 0f);
            if (rifleArmsLayerIndex >= 0)
                animator.SetLayerWeight(rifleArmsLayerIndex, 0f);
            if (toolUpperBodyLayerIndex >= 0)
                animator.SetLayerWeight(toolUpperBodyLayerIndex, 0f);
            if (crouchToolArmsLayerIndex >= 0)
                animator.SetLayerWeight(crouchToolArmsLayerIndex, 0f);
        }

        /// <summary>
        /// The Animator keeps the original one-shot trigger and transition timing. Tool data only
        /// replaces the placeholder clip, so gameplay input never controls animation completion.
        /// </summary>
        private void TriggerToolActionAnimation()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (!hasToolActionTrigger) return;
            ActivateToolUpperBodyLayer();
            animator.SetTrigger(ToolActionTrigger);
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
            SetToolActionAnimationSpeed(active ? activeToolDefinition : null);
            if (active) ActivateToolUpperBodyLayer();
            if (hasToolActionContinuousFlag)
                animator.SetBool(ToolActionContinuousFlag, active);
            if (active
                && activeToolDefinition != null
                && activeToolDefinition.PrimaryAction
                    == PlayerToolPrimaryAction.FireRifle)
            {
                EnterRifleContinuousActionImmediately();
            }
        }

        private void SetToolActionAnimationSpeed(PlayerToolDefinition definition)
        {
            if (!hasToolActionSpeed || animator == null) return;
            float multiplier = definition != null
                ? definition.FirearmAnimationSpeedMultiplier
                : 1f;
            animator.SetFloat(ToolActionSpeed, multiplier);
        }

        private void EnterRifleContinuousActionImmediately()
        {
            if (activeToolActionLayerIndex < 0) return;
            int stateHash = activeToolActionLayerIndex == crouchToolArmsLayerIndex
                ? ToolArmsContinuousActionState
                : ToolUpperBodyContinuousActionState;
            animator.Play(stateHash, activeToolActionLayerIndex, 0f);
        }

        private void ActivateToolUpperBodyLayer()
        {
            activeToolActionLayerIndex = ResolveToolActionLayerIndex();
            if (activeToolActionLayerIndex < 0) return;
            toolUpperBodyActionObserved = false;
            toolUpperBodyLayerTargetWeight = activeToolActionLayerIndex
                == toolUpperBodyLayerIndex ? 1f : 0f;
            crouchToolArmsLayerTargetWeight = activeToolActionLayerIndex
                == crouchToolArmsLayerIndex ? 1f : 0f;
        }

        private void TickCrouchArmsLocomotionLayerBlend(float deltaTime)
        {
            if (animator == null
                || animator.runtimeAnimatorController == null
                || crouchArmsLocomotionLayerIndex < 0)
            {
                return;
            }

            float targetWeight = input.CrouchHeld
                && motor != null
                && motor.IsGrounded
                ? 1f
                : 0f;
            float blendSpeed = 1f / CrouchArmsLayerBlendDuration;
            crouchArmsLocomotionLayerWeight = Mathf.MoveTowards(
                crouchArmsLocomotionLayerWeight,
                targetWeight,
                blendSpeed * deltaTime);
            animator.SetLayerWeight(
                crouchArmsLocomotionLayerIndex,
                crouchArmsLocomotionLayerWeight);
        }

        private void TickRifleLocomotionLayerBlend(float deltaTime)
        {
            if (animator == null
                || animator.runtimeAnimatorController == null
                || rifleLocomotionLayerIndex < 0)
            {
                return;
            }

            float blendSpeed = 1f / RifleLocomotionLayerBlendDuration;
            rifleLocomotionLayerWeight = Mathf.MoveTowards(
                rifleLocomotionLayerWeight,
                rifleLocomotionLayerTargetWeight,
                blendSpeed * deltaTime);
            animator.SetLayerWeight(
                rifleLocomotionLayerIndex,
                rifleLocomotionLayerWeight);
        }

        private void TickRifleArmsLayerBlend(float deltaTime)
        {
            if (animator == null
                || animator.runtimeAnimatorController == null
                || rifleArmsLayerIndex < 0)
            {
                return;
            }

            float blendSpeed = 1f / RifleLocomotionLayerBlendDuration;
            rifleArmsLayerWeight = Mathf.MoveTowards(
                rifleArmsLayerWeight,
                rifleArmsLayerTargetWeight,
                blendSpeed * deltaTime);
            animator.SetLayerWeight(rifleArmsLayerIndex, rifleArmsLayerWeight);
        }

        private void TickToolUpperBodyLayerBlend(float deltaTime)
        {
            if (animator == null
                || animator.runtimeAnimatorController == null)
            {
                return;
            }

            int desiredLayerIndex = ResolveToolActionLayerIndex();
            if (activeToolActionLayerIndex >= 0
                && desiredLayerIndex >= 0
                && desiredLayerIndex != activeToolActionLayerIndex
                && IsToolActionStateActive(desiredLayerIndex))
            {
                activeToolActionLayerIndex = desiredLayerIndex;
                toolUpperBodyLayerTargetWeight = activeToolActionLayerIndex
                    == toolUpperBodyLayerIndex ? 1f : 0f;
                crouchToolArmsLayerTargetWeight = activeToolActionLayerIndex
                    == crouchToolArmsLayerIndex ? 1f : 0f;
            }

            bool actionActive = activeToolActionLayerIndex >= 0
                && IsToolActionStateActive(activeToolActionLayerIndex);
            if (actionActive)
            {
                toolUpperBodyActionObserved = true;
            }
            else if (toolUpperBodyActionObserved)
            {
                toolUpperBodyActionObserved = false;
                toolUpperBodyLayerTargetWeight = 0f;
                crouchToolArmsLayerTargetWeight = 0f;
                activeToolActionLayerIndex = -1;
            }

            float blendSpeed = 1f / ToolUpperBodyLayerBlendDuration;
            toolUpperBodyLayerWeight = Mathf.MoveTowards(
                toolUpperBodyLayerWeight,
                toolUpperBodyLayerTargetWeight,
                blendSpeed * deltaTime);
            crouchToolArmsLayerWeight = Mathf.MoveTowards(
                crouchToolArmsLayerWeight,
                crouchToolArmsLayerTargetWeight,
                blendSpeed * deltaTime);
            if (toolUpperBodyLayerIndex >= 0)
                animator.SetLayerWeight(toolUpperBodyLayerIndex, toolUpperBodyLayerWeight);
            if (crouchToolArmsLayerIndex >= 0)
                animator.SetLayerWeight(crouchToolArmsLayerIndex, crouchToolArmsLayerWeight);
        }

        private int ResolveToolActionLayerIndex()
        {
            bool crouching = input.CrouchHeld
                && motor != null
                && motor.IsGrounded;
            PlayerToolDefinition definition = activeToolDefinition != null
                ? activeToolDefinition
                : toolController != null
                    ? toolController.SelectedDefinition
                    : null;
            bool rifleAction = definition != null
                && definition.PrimaryAction == PlayerToolPrimaryAction.FireRifle;
            return (crouching || rifleAction) && crouchToolArmsLayerIndex >= 0
                ? crouchToolArmsLayerIndex
                : toolUpperBodyLayerIndex;
        }

        private bool IsToolActionStateActive(int layerIndex)
        {
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(layerIndex);
            if (IsToolUpperBodyActionState(current)) return true;
            return animator.IsInTransition(layerIndex)
                && IsToolUpperBodyActionState(
                    animator.GetNextAnimatorStateInfo(layerIndex));
        }

        private static bool IsToolUpperBodyActionState(AnimatorStateInfo state)
        {
            return state.shortNameHash == ToolPrimaryActionState
                || state.shortNameHash == ToolContinuousActionState;
        }

        private bool CanStartToolAction(PlayerToolDefinition definition)
        {
            if (definition == null
                || !definition.HasPrimaryAction
                || !IsToolActionCycleReady(definition))
            {
                return false;
            }

            switch (definition.PrimaryAction)
            {
                case PlayerToolPrimaryAction.MineVoxel:
                    return true;
                case PlayerToolPrimaryAction.AttractCart:
                    return cartAttractor != null && cartAttractor.CanOperate;
                case PlayerToolPrimaryAction.ThrowPersistentLight:
                    return definition.ProjectilePrefab != null;
                case PlayerToolPrimaryAction.FireRifle:
                    return definition.FirearmProjectilePrefab != null
                        && toolController != null
                        && toolController.GetAmmunition(definition.Item)
                            > CountPendingToolActions(definition);
                case PlayerToolPrimaryAction.TowCart:
                    // FirstPersonCartAttractor handles towing as a click
                    // toggle before the held-action state machine runs.
                    return false;
                default:
                    return false;
            }
        }

        private bool IsToolActionCycleReady(PlayerToolDefinition definition)
        {
            return definition != null
                && (!nextToolActionCycleTimes.TryGetValue(
                        definition,
                        out float nextCycleTime)
                    || Time.time >= nextCycleTime);
        }

        private int CountPendingToolActions(PlayerToolDefinition definition)
        {
            int count = 0;
            for (int i = 0; i < pendingToolActions.Count; i++)
            {
                if (pendingToolActions[i].Definition == definition) count++;
            }
            return count;
        }

        private void ApplyToolActionAnimation(PlayerToolDefinition definition)
        {
            if (definition == null
                || definition.PrimaryActionAnimation == null
                || !EnsureToolAnimatorController())
            {
                return;
            }

            if (activePrimaryActionAnimation == definition.PrimaryActionAnimation)
                return;

            toolAnimatorController[PrimaryActionPlaceholderClipName] =
                definition.PrimaryActionAnimation;
            activePrimaryActionAnimation = definition.PrimaryActionAnimation;
        }

        private void SubscribeToToolSelection()
        {
            if (subscribedToolController == toolController) return;
            UnsubscribeFromToolSelection();
            subscribedToolController = toolController;
            if (subscribedToolController != null)
            {
                subscribedToolController.SelectionChanged +=
                    HandleToolSelectionChanged;
            }
        }

        private void UnsubscribeFromToolSelection()
        {
            if (subscribedToolController != null)
            {
                subscribedToolController.SelectionChanged -=
                    HandleToolSelectionChanged;
                subscribedToolController = null;
            }
        }

        private void HandleToolSelectionChanged(
            int slotIndex,
            PlayerInventoryItem item)
        {
            ApplyToolActionAnimation(
                toolController != null
                    ? toolController.SelectedDefinition
                    : null);
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
            activePrimaryActionAnimation = null;
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
            StartToolActionCycle(activeToolDefinition);
        }

        private void ExitToolAction()
        {
            if (activeToolDefinition != null
                && activeToolDefinition.PrimaryAction == PlayerToolPrimaryAction.AttractCart)
            {
                RemovePendingToolActions(activeToolDefinition);
                cartAttractor?.EndAttraction();
            }
            StopConfiguredToolActionAnimation();
            activeToolDefinition = null;
        }

        private bool StartToolActionCycle(PlayerToolDefinition definition)
        {
            if (!CanStartToolAction(definition)) return false;

            TriggerPeriodicToolActionAnimation();
            float triggerTime = Time.time + definition.ActionTriggerDelay;
            bool scheduled = definition.PrimaryAction
                == PlayerToolPrimaryAction.MineVoxel
                    ? ScheduleMiningToolAction(definition)
                    : ScheduleToolAction(definition, triggerTime);
            if (!scheduled) return false;

            nextToolActionCycleTimes[definition] =
                Time.time + definition.ActionCyclePeriod;
            return true;
        }

        private bool ScheduleMiningToolAction(PlayerToolDefinition definition)
        {
            float delay = definition.ActionTriggerDelay;
            ScheduleMiningAttack(delay);
            bool isPickaxe = definition.Item == PlayerInventoryItem.Pickaxe;
            int strikeNumber = isPickaxe ? pickaxeStrikeParity + 1 : 1;
            VoxelMiningBrushSettings brush =
                definition.GetMiningBrushForStrike(strikeNumber);
            bool scheduled = voxelInteractor != null
                && voxelInteractor.TryScheduleMineAtCrosshair(
                    delay,
                    brush);
            if (scheduled && isPickaxe)
            {
                pickaxeStrikeParity ^= 1;
            }

            // Empty swings remain valid action cycles because their delayed melee
            // overlap can still hit a creature or a rigidbody in front of the player.
            return true;
        }

        private bool ScheduleToolAction(
            PlayerToolDefinition definition,
            float triggerTime)
        {
            pendingToolActions.Add(
                new ScheduledToolAction(definition, triggerTime));
            ApplyPendingToolActionsIfReady();
            return true;
        }

        private void ApplyPendingToolActionsIfReady()
        {
            while (true)
            {
                int dueIndex = -1;
                float earliestTime = float.PositiveInfinity;
                for (int i = 0; i < pendingToolActions.Count; i++)
                {
                    ScheduledToolAction pending = pendingToolActions[i];
                    if (pending.TriggerTime > Time.time
                        || pending.TriggerTime >= earliestTime)
                    {
                        continue;
                    }

                    earliestTime = pending.TriggerTime;
                    dueIndex = i;
                }

                if (dueIndex < 0) return;
                PlayerToolDefinition definition =
                    pendingToolActions[dueIndex].Definition;
                pendingToolActions.RemoveAt(dueIndex);
                ExecuteConfiguredToolAction(definition);
            }
        }

        private void RemovePendingToolActions(PlayerToolDefinition definition)
        {
            for (int i = pendingToolActions.Count - 1; i >= 0; i--)
            {
                if (pendingToolActions[i].Definition == definition)
                    pendingToolActions.RemoveAt(i);
            }
        }

        private bool ExecuteConfiguredToolAction(
            PlayerToolDefinition definition)
        {
            if (definition == null) return false;
            switch (definition.PrimaryAction)
            {
                case PlayerToolPrimaryAction.AttractCart:
                    return cartAttractor != null
                        && cartAttractor.BeginAttraction();
                case PlayerToolPrimaryAction.ThrowPersistentLight:
                    return ThrowConfiguredProjectile(definition) != null;
                case PlayerToolPrimaryAction.FireRifle:
                    return FireConfiguredProjectile(definition) != null;
                default:
                    return false;
            }
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
                && stateSeconds >= activeToolDefinition.ActionTriggerDelay)
            {
                SelectGroundOrAirState();
                return;
            }

            if (activeToolDefinition.PrimaryAction
                == PlayerToolPrimaryAction.AttractCart)
            {
                TickAttractorToolAction();
                if (stateMachine.Current != PlayerCharacterState.ToolAction)
                    return;
            }

            if (actionHeld
                && activeToolDefinition.ActionIsPeriodic
                && IsToolActionCycleReady(activeToolDefinition)
                && !StartToolActionCycle(activeToolDefinition))
            {
                SelectGroundOrAirState();
            }
        }

        private void TickAttractorToolAction()
        {
            if (cartAttractor == null)
            {
                SelectGroundOrAirState();
                return;
            }

            if (!cartAttractor.IsActionActive)
            {
                if (CountPendingToolActions(activeToolDefinition) > 0) return;
                SelectGroundOrAirState();
                return;
            }

            cartAttractor.TickAttraction(input.AttractionDistanceSteps);
        }

        private BallisticProjectile FireConfiguredProjectile(
            PlayerToolDefinition definition)
        {
            if (definition == null
                || definition.FirearmProjectilePrefab == null
                || toolController == null
                || !toolController.TryConsumeAmmunition(definition.Item))
            {
                return null;
            }

            Transform aimOrigin = view != null ? view : transform;
            Vector3 forward = aimOrigin.forward.sqrMagnitude > 0.0001f
                ? aimOrigin.forward.normalized
                : transform.forward;
            Transform muzzle = toolController.EquippedWeaponMuzzle;
            Vector3 position = muzzle != null
                ? muzzle.position
                : aimOrigin.position + forward * 0.75f;
            Quaternion rotation = Quaternion.LookRotation(
                forward,
                aimOrigin.up.sqrMagnitude > 0.0001f
                    ? aimOrigin.up
                    : Vector3.up);

            BallisticProjectile projectile = Instantiate(
                definition.FirearmProjectilePrefab,
                position,
                rotation);
            projectile.name = definition.FirearmProjectilePrefab.name;
            projectile.Launch(forward * definition.ProjectileSpeed, gameObject);
            IgnoreOwnerCollisions(projectile);
            SpawnMuzzleFlash(definition, position, rotation, muzzle);
            return projectile;
        }

        private static void SpawnMuzzleFlash(
            PlayerToolDefinition definition,
            Vector3 position,
            Quaternion rotation,
            Transform muzzle)
        {
            if (definition.MuzzleFlashPrefab == null) return;

            GameObject effect = Instantiate(
                definition.MuzzleFlashPrefab,
                position,
                rotation);
            effect.name = definition.MuzzleFlashPrefab.name;
            if (muzzle != null) effect.transform.SetParent(muzzle, true);
            Destroy(effect, definition.MuzzleFlashLifetime);
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
            return projectile;
        }

        private void IgnoreOwnerCollisions(Component projectile)
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
            SuppressOverrideAnimationLayers();
        }

        private void TickHurt(float deltaTime)
        {
            stateSeconds += deltaTime;
            TickLocomotion(deltaTime, false);
            SuppressOverrideAnimationLayers();
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
            SuppressOverrideAnimationLayers();
        }

        private void TickDead(float deltaTime)
        {
            TickLocomotion(deltaTime, false);
            SetAnimationState(false, false, false);
            SuppressOverrideAnimationLayers();
        }

        private void SuppressOverrideAnimationLayers()
        {
            rifleLocomotionLayerTargetWeight = 0f;
            rifleLocomotionLayerWeight = 0f;
            rifleArmsLayerTargetWeight = 0f;
            rifleArmsLayerWeight = 0f;
            toolUpperBodyLayerTargetWeight = 0f;
            toolUpperBodyLayerWeight = 0f;
            crouchToolArmsLayerTargetWeight = 0f;
            crouchToolArmsLayerWeight = 0f;

            if (animator == null || animator.runtimeAnimatorController == null)
                return;

            if (rifleLocomotionLayerIndex >= 0)
                animator.SetLayerWeight(rifleLocomotionLayerIndex, 0f);
            if (rifleArmsLayerIndex >= 0)
                animator.SetLayerWeight(rifleArmsLayerIndex, 0f);
            if (toolUpperBodyLayerIndex >= 0)
                animator.SetLayerWeight(toolUpperBodyLayerIndex, 0f);
            if (crouchToolArmsLayerIndex >= 0)
                animator.SetLayerWeight(crouchToolArmsLayerIndex, 0f);
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
