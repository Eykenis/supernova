using System.Collections.Generic;
using Supernova.Audio;
using Supernova.Inputs;
using Supernova.Gameplay;
using Supernova.Infrastructure;
using Supernova.MinecraftCaves;
using Supernova.Missions;
using Supernova.UI;
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
        private const string FirearmLocomotionLayerName =
            "Firearm Locomotion Layer";
        private const string FirearmArmsLayerName = "Firearm Arms Layer";
        private const float ToolUpperBodyLayerBlendDuration = 0.12f;
        private const float CrouchArmsLayerBlendDuration = 0.12f;
        private const float FirearmLocomotionLayerBlendDuration = 0.12f;
        private const float StandingMovementSoundPitch = 1.75f;
        private const float CrouchingMovementSoundPitch = 1f;
        private const float MagnetSoundVolumeScale = 0.5f;
        private const float MagnetSoundFadeSeconds = 0.2f;
        private const float ConfiguredThrowSoundVolumeScale = 0.5f;
        private const float SmallLandingSoundSpeedThreshold = 8f;
        private const float BigLandingSoundSpeedThreshold = 16f;
        private const int CrouchClearanceHitCapacity = 32;
        /// <summary>Metres of rope let out or taken in per scroll-wheel step.</summary>
        private const float RopeReelMetresPerStep = 1.5f;
        /// <summary>Scroll magnitude below which the wheel counts as untouched.</summary>
        private const float ScrollDeadZone = 0.01f;
        /// <summary>
        /// Squared metres below which a rope position correction is not worth a
        /// CharacterController.Move call.
        /// </summary>
        private const float RopeCorrectionEpsilon = 0.0000001f;

        [SerializeField] private Transform view;
        [SerializeField] private Animator animator;
        [Tooltip("Optional external target for kocchiFlag. A camera parented to this player is ignored.")]
        [SerializeField] private Transform kocchiTarget;

        [Header("Sound")]
        [Tooltip("Loop played while the character is walking. Standing movement uses 1.75x pitch and crouched movement uses the cue's original pitch.")]
        [SerializeField] private SoundEffectCue movementSound;
        [Tooltip("Movement loop used throughout the Home scene. It keeps the same standing and crouched pitch multipliers as the default movement loop.")]
        [SerializeField] private SoundEffectCue homeCellMovementSound;
        [Tooltip("Loop played while a magnet attraction is active. The audio manager resumes this clip from its last global sample offset.")]
        [SerializeField] private SoundEffectCue magnetSound;

        [Header("Runtime")]
        [SerializeField] private PlayerCharacterState currentState;

        private CharacterController characterController;
        private PerspectiveCameraController perspectiveCamera;

        private FirstPersonMagnetInteractor magnetInteractor;
        private PickaxeThrowController pickaxeThrow;
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
        private readonly Queue<ScheduledMiningAttack> pendingMiningAttacks =
            new Queue<ScheduledMiningAttack>();
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
        private bool magnetHoldAnimationActive;
        private PlayerToolDefinition magnetHoldAnimationDefinition;

        private bool throwKeyRearmed = true;
        private PlayerToolController subscribedToolController;
        private int pickaxeStrikeParity;
        private int crouchArmsLocomotionLayerIndex = -1;
        private int firearmLocomotionLayerIndex = -1;
        private int firearmArmsLayerIndex = -1;
        private int toolUpperBodyLayerIndex = -1;
        private int crouchToolArmsLayerIndex = -1;
        private int activeToolActionLayerIndex = -1;
        private float toolUpperBodyLayerTargetWeight;
        private float toolUpperBodyLayerWeight;
        private float crouchToolArmsLayerTargetWeight;
        private float crouchToolArmsLayerWeight;
        private float crouchArmsLocomotionLayerWeight;
        private float firearmLocomotionLayerTargetWeight;
        private float firearmLocomotionLayerWeight;
        private float firearmArmsLayerTargetWeight;
        private float firearmArmsLayerWeight;
        private bool toolUpperBodyActionObserved;
        private int movementSoundLoopId;
        private bool movementSoundPlaying;
        private float activeMovementSoundPitch;
        private SoundEffectCue activeMovementSoundCue;
        private int magnetSoundLoopId;
        private bool magnetSoundPlaying;
        private bool landingSoundGroundStateInitialized;
        private bool wasGroundedForLandingSound;
        private float maximumAirborneFallSpeed;
        private readonly Collider[] crouchClearanceHits =
            new Collider[CrouchClearanceHitCapacity];
        private bool controllerDimensionsCached;
        private bool crouchColliderActive;
        private float standingControllerHeight;
        private Vector3 standingControllerCenter;
        private float crouchingControllerHeight;
        private Vector3 crouchingControllerCenter;

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

        private readonly struct ScheduledMiningAttack
        {
            public ScheduledMiningAttack(
                float triggerTime,
                SoundEffectCue monsterHitSound)
            {
                TriggerTime = triggerTime;
                MonsterHitSound = monsterHitSound;
            }

            public float TriggerTime { get; }
            public SoundEffectCue MonsterHitSound { get; }
        }

        public GameObject Owner => gameObject;
        public float CurrentHealth => vitals != null ? vitals.CurrentHealth : 0f;
        public float MaximumHealth => vitals != null ? vitals.MaximumHealth : Profile.MaximumHealth;
        public float CrouchPoseWeight => crouchArmsLocomotionLayerWeight;
        public bool IsFirearmSelected => toolController != null
            && toolController.IsFirearmSelected;
        public bool IsAlive => vitals != null && vitals.IsAlive;
        public bool DebugFlyMode => debugFlyMode;
        public Animator CharacterAnimator => animator;
        public float VerticalVelocity => motor != null ? motor.VerticalVelocity : 0f;
        public PlayerCharacterState CurrentState => currentState;
        public bool IsCrouching => crouchColliderActive;

        /// <summary>
        /// Returns the active action-cycle cooldown for an inventory item. Timers
        /// are stored per tool definition, so unequipped hotbar slots keep their
        /// own remaining time.
        /// </summary>
        public bool TryGetToolActionCooldown(
            PlayerInventoryItem item,
            out float remainingSeconds,
            out float durationSeconds)
        {
            remainingSeconds = 0f;
            durationSeconds = 0f;
            ResolveReferences();
            PlayerToolDefinition definition = toolController != null
                ? toolController.GetDefinition(item)
                : null;
            if (definition == null || !definition.IsFirearm)
                return false;

            durationSeconds = definition.ActionCyclePeriod;
            if (!nextToolActionCycleTimes.TryGetValue(
                    definition,
                    out float nextCycleTime))
            {
                return false;
            }

            remainingSeconds = Mathf.Clamp(
                nextCycleTime - Time.time,
                0f,
                durationSeconds);
            return remainingSeconds > 0f;
        }

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
            movementSoundLoopId = SoundEffectEvents.CreateLoopId();
            magnetSoundLoopId = SoundEffectEvents.CreateLoopId();
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
            ResetLandingSoundTracking();
            EnsureStateMachine();
            if (characterController != null) characterController.enabled = !debugFlyMode;
            stateMachine.Start(vitals.IsAlive ? PlayerCharacterState.Idle : PlayerCharacterState.Dead);
        }

        private void OnDisable()
        {
            StopMovementSound();
            ResetLandingSoundTracking();
            UnsubscribeFromToolSelection();
            CancelSecondaryAction();
            debugFlyMode = false;
            pendingMiningAttacks.Clear();
            pendingToolActions.Clear();
            nextToolActionCycleTimes.Clear();
            equipmentController?.CancelActiveLocomotionOverride();
            StopEquipmentLocomotionAnimation(false);
            idleSeconds = 0f;
            stateMachine?.Stop();
            motor?.ResetVerticalVelocity();
            motor?.ResetExternalVelocity();
            ResolveReferences();
            if (characterController != null) characterController.enabled = true;
            SetAnimationState(false, false, true);
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
            if (GameHudController.IsGameplayInputBlocked)
            {
                // Opening a menu mid-drag must not leave the magnet latched or an
                // aimed throw charged, because right-click release is never seen.
                CancelSecondaryAction();
                StopMovementSound();
                return;
            }

            ResolveReferences();
            EnsureMotor();
            EnsureStateMachine();
            ApplyPendingMiningAttacksIfReady();
            ApplyPendingToolActionsIfReady();
            if (characterController == null)
            {
                StopMovementSound();
                return;
            }

            if (GameInput.Pressed(GameInputActionId.DebugFlyToggle))
                SetDebugFlyMode(!debugFlyMode);
            input = CaptureInput();
            UpdateCrouchCollider(
                input.CrouchHeld
                && motor != null
                && motor.IsGrounded);
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

            TickSecondaryAction();
            TickCrouchArmsLocomotionLayerBlend(Time.deltaTime);
            TickFirearmLocomotionLayerBlend(Time.deltaTime);
            TickFirearmArmsLayerBlend(Time.deltaTime);
            TickToolUpperBodyLayerBlend(Time.deltaTime);
            TickMagnetHoldAnimationLoop();

            currentState = stateMachine.Current;
            UpdateLandingSound();
            UpdateMovementSound();
            UpdateExpressionAnimation();
        }

        private void UpdateMovementSound()
        {
            if (!TryGetActiveMovementSoundPitch(out float pitch))
            {
                StopMovementSound();
                return;
            }

            SoundEffectCue selectedSound = SelectMovementSound(
                movementSound,
                homeCellMovementSound,
                IsInHomeScene());
            if (selectedSound == null)
            {
                StopMovementSound();
                return;
            }

            if (movementSoundLoopId == 0)
                movementSoundLoopId = SoundEffectEvents.CreateLoopId();
            if (movementSoundPlaying
                && activeMovementSoundCue == selectedSound
                && Mathf.Approximately(activeMovementSoundPitch, pitch))
            {
                return;
            }

            movementSoundPlaying = SoundEffectEvents.RequestLoop(
                movementSoundLoopId,
                selectedSound,
                transform,
                1f,
                pitch);
            activeMovementSoundCue = movementSoundPlaying
                ? selectedSound
                : null;
            activeMovementSoundPitch = movementSoundPlaying ? pitch : 0f;
        }

        private bool IsInHomeScene()
        {
            LevelConfiguration level =
                MissionGameLoop.CurrentLevelConfiguration;
            string homeSceneName = level != null
                ? level.HomeSceneName
                : GameAssetCatalog.Current != null
                    && GameAssetCatalog.Current.SceneLookups != null
                        ? GameAssetCatalog.Current.SceneLookups.MainMenuSceneName
                        : null;
            return IsHomeScene(
                UnityEngine.SceneManagement.SceneManager
                    .GetActiveScene().name,
                homeSceneName);
        }

        private static bool IsHomeScene(
            string currentSceneName,
            string homeSceneName)
        {
            return !string.IsNullOrEmpty(homeSceneName)
                && currentSceneName == homeSceneName;
        }

        private static SoundEffectCue SelectMovementSound(
            SoundEffectCue defaultSound,
            SoundEffectCue homeSceneSound,
            bool isInHomeScene)
        {
            return isInHomeScene && homeSceneSound != null
                ? homeSceneSound
                : defaultSound;
        }


        private void StopMovementSound()
        {
            if (movementSoundPlaying && movementSoundLoopId != 0)
                SoundEffectEvents.RequestStopLoop(movementSoundLoopId);
            movementSoundPlaying = false;
            activeMovementSoundPitch = 0f;
            activeMovementSoundCue = null;
        }

        private void UpdateLandingSound()
        {
            if (motor == null || debugFlyMode)
            {
                ResetLandingSoundTracking();
                return;
            }

            bool grounded = motor.IsGrounded;
            float downwardYAxisSpeed =
                GetDownwardYAxisSpeed(motor.CombinedVelocity);
            if (!landingSoundGroundStateInitialized)
            {
                landingSoundGroundStateInitialized = true;
                wasGroundedForLandingSound = grounded;
                maximumAirborneFallSpeed = grounded
                    ? 0f
                    : downwardYAxisSpeed;
                return;
            }

            if (!grounded)
            {
                maximumAirborneFallSpeed = Mathf.Max(
                    maximumAirborneFallSpeed,
                    downwardYAxisSpeed);
            }
            else if (!wasGroundedForLandingSound)
            {
                float landingYAxisSpeed = Mathf.Max(
                    maximumAirborneFallSpeed,
                    GetDownwardYAxisSpeed(
                        new Vector3(0f, motor.VerticalVelocity, 0f)));
                AudioAssetReferences audio = GameAssetCatalog.Current != null
                    ? GameAssetCatalog.Current.Audio
                    : null;
                SoundEffectCue cue = SelectLandingSound(
                    landingYAxisSpeed,
                    audio != null ? audio.PlayerFallSmall : null,
                    audio != null ? audio.PlayerFallBig : null);
                SoundEffectEvents.RequestPlay(cue, transform.position);
                maximumAirborneFallSpeed = 0f;
            }

            wasGroundedForLandingSound = grounded;
        }

        private void ResetLandingSoundTracking()
        {
            landingSoundGroundStateInitialized = false;
            wasGroundedForLandingSound = false;
            maximumAirborneFallSpeed = 0f;
        }

        private static SoundEffectCue SelectLandingSound(
            float downwardYAxisSpeed,
            SoundEffectCue smallCue,
            SoundEffectCue bigCue)
        {
            if (downwardYAxisSpeed >= BigLandingSoundSpeedThreshold)
                return bigCue != null ? bigCue : smallCue;
            if (downwardYAxisSpeed >= SmallLandingSoundSpeedThreshold)
                return smallCue;
            return null;
        }

        private static float GetDownwardYAxisSpeed(Vector3 velocity)
        {
            return Mathf.Max(0f, -velocity.y);
        }



        private void UpdateMagnetSound(bool active)
        {
            if (!active || magnetSound == null)
            {
                StopMagnetSound();
                return;
            }

            if (magnetSoundPlaying) return;
            if (magnetSoundLoopId == 0)
                magnetSoundLoopId = SoundEffectEvents.CreateLoopId();

            magnetSoundPlaying = SoundEffectEvents.RequestLoop(
                magnetSoundLoopId,
                magnetSound,
                transform,
                MagnetSoundVolumeScale,
                1f,
                MagnetSoundFadeSeconds);
        }

        private void StopMagnetSound()
        {
            if (magnetSoundPlaying && magnetSoundLoopId != 0)
            {
                SoundEffectEvents.RequestStopLoop(
                    magnetSoundLoopId,
                    MagnetSoundFadeSeconds);
            }
            magnetSoundPlaying = false;
        }

        private static bool TryGetMovementSoundPitch(
            PlayerCharacterState state,
            out float pitch)
        {
            switch (state)
            {
                case PlayerCharacterState.Move:
                    pitch = StandingMovementSoundPitch;
                    return true;
                case PlayerCharacterState.CrouchMove:
                    pitch = CrouchingMovementSoundPitch;
                    return true;
                default:
                    pitch = 0f;
                    return false;
            }
        }

        private bool TryGetActiveMovementSoundPitch(out float pitch)
        {
            if (TryGetMovementSoundPitch(currentState, out pitch))
                return true;

            bool movementInputActive =
                input.Move.sqrMagnitude
                >= Profile.MovingThreshold * Profile.MovingThreshold;
            bool swinging = magnetInteractor != null
                && magnetInteractor.IsPullingTowardsPickaxe;
            return currentState == PlayerCharacterState.ToolAction
                && TryGetToolActionMovementSoundPitch(
                    activeToolDefinition != null
                        && activeToolDefinition.AllowMovementWhileUsing,
                    motor != null && motor.IsGrounded,
                    crouchColliderActive,
                    movementInputActive,
                    swinging,
                    out pitch);
        }

        private static bool TryGetToolActionMovementSoundPitch(
            bool toolAllowsMovement,
            bool grounded,
            bool crouching,
            bool movementInputActive,
            bool swinging,
            out float pitch)
        {
            if (!toolAllowsMovement
                || !grounded
                || !movementInputActive
                || swinging)
            {
                pitch = 0f;
                return false;
            }

            pitch = crouching
                ? CrouchingMovementSoundPitch
                : StandingMovementSoundPitch;
            return true;
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

        /// <summary>
        /// Integrates mass-independent acceleration into the CharacterController
        /// motor without directly changing the player's position.
        /// </summary>
        public void AddExternalAcceleration(
            Vector3 acceleration,
            float deltaTime,
            float maximumSpeed)
        {
            ResolveReferences();
            EnsureMotor();
            motor?.AddExternalAcceleration(
                acceleration,
                deltaTime,
                maximumSpeed);
        }

        /// <summary>
        /// The player's current gravity-plus-external velocity, which is what a rope
        /// constraint has to operate on.
        /// </summary>
        public Vector3 CombinedVelocity
        {
            get
            {
                ResolveReferences();
                EnsureMotor();
                return motor != null ? motor.CombinedVelocity : Vector3.zero;
            }
        }

        /// <summary>
        /// Applies a rope's distance constraint to the player. Only the outward radial
        /// velocity is removed, so the player keeps swinging along the arc, and the
        /// player is pulled back onto the rope's sphere so the rope cannot stretch.
        /// Returns true while the rope is taut.
        /// </summary>
        public bool ApplyRopeConstraint(Vector3 anchor, float ropeLength)
        {
            ResolveReferences();
            EnsureMotor();
            if (motor == null) return false;

            Vector3 anchorToPlayer = GetRopeAttachPoint() - anchor;
            Vector3 constrained = RopeConstraint.ApplyTautConstraint(
                motor.CombinedVelocity,
                anchorToPlayer,
                ropeLength,
                out bool taut);
            if (taut) motor.SetCombinedVelocity(constrained);

            // Cancelling velocity is not enough on its own. Gravity is integrated and
            // applied before this runs, so every frame leaks a sub-frame of outward
            // displacement; without a positional fix the rope creeps longer and the
            // velocity correction overshoots, which reads as vertical jitter.
            Vector3 correction = RopeConstraint.CalculatePositionCorrection(
                anchorToPlayer,
                ropeLength);
            if (correction.sqrMagnitude > RopeCorrectionEpsilon
                && characterController != null
                && characterController.enabled)
            {
                characterController.Move(correction);
                taut = true;
            }
            return taut;
        }

        /// <summary>Adds an instantaneous velocity change, such as a rope yank.</summary>
        public void AddExternalVelocity(Vector3 velocity, float maximumSpeed)
        {
            ResolveReferences();
            EnsureMotor();
            motor?.AddExternalVelocity(velocity, maximumSpeed);
        }

        /// <summary>
        /// Overwrites the player's gravity-plus-external velocity. Used when releasing
        /// a rope to scale the swing momentum the player flies away with.
        /// </summary>
        public void SetCombinedVelocity(Vector3 velocity)
        {
            ResolveReferences();
            EnsureMotor();
            motor?.SetCombinedVelocity(velocity);
        }

        /// <summary>
        /// The point a rope is treated as attached to. Using the capsule centre rather
        /// than the feet keeps the swing radius stable as the player rotates.
        /// </summary>
        public Vector3 GetRopeAttachPoint()
        {
            ResolveReferences();
            return characterController != null
                ? characterController.bounds.center
                : transform.position;
        }

        /// <summary>
        /// Advances the motor by one step with an explicit movement input. Exists so
        /// motor behaviour such as external-velocity decay can be verified without
        /// entering play mode.
        /// </summary>
        public void StepMotor(Vector3 planarMovement, float deltaTime)
        {
            ResolveReferences();
            EnsureMotor();
            motor?.Tick(planarMovement, deltaTime);
        }

        public void ClearExternalVelocity()
        {
            ResolveReferences();
            EnsureMotor();
            motor?.ResetExternalVelocity();
        }

        public void ClearVerticalVelocity()
        {
            ResolveReferences();
            EnsureMotor();
            motor?.ResetVerticalVelocity();
        }

        private PlayerInputSnapshot CaptureInput()
        {
            Vector2 movement = GameInput.ReadVector2(GameInputActionId.Move);
            if (movement.sqrMagnitude > 1f) movement.Normalize();
            bool acceptsAction = Cursor.lockState == CursorLockMode.Locked;
            bool primaryHeld = acceptsAction
                && GameInput.Held(GameInputActionId.PrimaryAction);
            return new PlayerInputSnapshot(
                movement,
                acceptsAction
                    && GameInput.Pressed(GameInputActionId.Jump),
                primaryHeld
                    && toolController != null
                    && toolController.CanUseSelectedPrimaryAction(),
                acceptsAction
                    && GameInput.Held(GameInputActionId.SecondaryAction),
                acceptsAction
                    && GameInput.Held(GameInputActionId.ThrowPickaxe),
                GameInput.Held(GameInputActionId.Crouch),
                acceptsAction
                    ? ReadScrollSteps()
                    : 0f);
        }

        /// <summary>
        /// Scroll wheel as a signed step count. Mathf.Sign must not be used here: it
        /// returns +1 for an input of exactly zero, which would report a full scroll
        /// step on every idle frame.
        /// </summary>
        private static float ReadScrollSteps()
        {
            float scroll =
                GameInput.ReadVector2(GameInputActionId.HotbarScroll).y;
            if (scroll > ScrollDeadZone) return 1f;
            if (scroll < -ScrollDeadZone) return -1f;
            return 0f;
        }



        private void TickLocomotion(float deltaTime, bool acceptInput)
        {
            // While swinging on a rope the movement keys pump the swing instead of
            // walking, so they must not also drive the ground motor.
            bool swinging = magnetInteractor != null
                && magnetInteractor.IsPullingTowardsPickaxe;
            Vector2 movement = acceptInput && !swinging
                ? input.Move
                : Vector2.zero;
            Vector3 worldMovement = GetWorldMovement(movement);
            UpdateThirdPersonFacing(worldMovement, deltaTime);
            // The physical posture owns crouch state so a blocked stand-up keeps the
            // slower movement and crouch animation even after the key is released.
            bool crouching = crouchColliderActive && motor.IsGrounded;
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

            if (input.JumpPressed
                && motor.IsGrounded
                && !input.CrouchHeld
                && !crouchColliderActive)
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
                stateMachine.Change(ResolveGroundedLocomotionState(
                    crouchColliderActive,
                    moving));
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

        private void PerformAttack(SoundEffectCue monsterHitSound)
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
                out int damagedMonsterCount,
                Profile.AttackLayers.value);
            if (damagedMonsterCount > 0)
            {
                SoundEffectEvents.RequestPlay(
                    monsterHitSound,
                    centre);
            }
        }

        private void ScheduleMiningAttack(
            float delay,
            SoundEffectCue monsterHitSound)
        {
            pendingMiningAttacks.Enqueue(
                new ScheduledMiningAttack(
                    Time.time + Mathf.Max(0f, delay),
                    monsterHitSound));
        }

        private void ApplyPendingMiningAttacksIfReady()
        {
            while (pendingMiningAttacks.Count > 0
                && Time.time >= pendingMiningAttacks.Peek().TriggerTime)
            {
                ScheduledMiningAttack attack =
                    pendingMiningAttacks.Dequeue();
                voxelInteractor?.ApplyPendingMineIfReady();
                PerformAttack(attack.MonsterHitSound);
            }
        }

        private void UpdateDebugFlyMovement(Vector2 moveInput)
        {
            Vector3 forward = view != null ? view.forward : transform.forward;
            Vector3 right = view != null ? view.right : transform.right;
            Vector3 movement = right * moveInput.x + forward * moveInput.y;
            if (GameInput.Held(GameInputActionId.DebugFlyUp))
                movement += Vector3.up;
            if (GameInput.Held(GameInputActionId.DebugFlyDown))
                movement -= Vector3.up;
            if (movement.sqrMagnitude > 1f) movement.Normalize();
            float multiplier = GameInput.Held(GameInputActionId.DebugFlyFast)
                ? Profile.DebugFlySpeedMultiplier
                : 1f;
            transform.position += movement
                * Profile.DebugFlySpeed
                * multiplier
                * Time.deltaTime;
        }

        private bool TryUpdateEquipmentLocomotion()
        {
            if (input.CrouchHeld || crouchColliderActive)
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
            if (hasSmileFlag)
            {
                animator.SetBool(
                    SmileFlag,
                    GameInput.Held(GameInputActionId.DebugSmile));
            }
            if (hasHitFlag && GameInput.Pressed(GameInputActionId.DebugHit))
                animator.SetTrigger(HitFlag);
            if (hasDieFlag && GameInput.Pressed(GameInputActionId.DebugDie))
                animator.SetTrigger(DieFlag);
            if (hasRecoverFlag
                && GameInput.Pressed(GameInputActionId.DebugRecover))
            {
                animator.SetTrigger(RecoverFlag);
            }

            bool kocchi = false;
            Transform target = kocchiTarget;
            if (target == null && Camera.main != null
                && !Camera.main.transform.IsChildOf(transform))
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
            bool firearmSelected = IsFirearmSelected;
            firearmLocomotionLayerTargetWeight = firearmSelected
                && !jumping
                && !crouching
                && (walking
                    || idle
                    || activeToolDefinition != null
                        && activeToolDefinition.PrimaryAction
                            == PlayerToolPrimaryAction.FireProjectile)
                ? 1f
                : 0f;
            firearmArmsLayerTargetWeight = firearmSelected
                && (jumping || crouching)
                ? 1f
                : 0f;
        }

        public void SetDebugFlyMode(bool enabled)
        {
            ResolveReferences();
            EnsureMotor();
            ResetLandingSoundTracking();
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
            CacheControllerDimensions();
            if (perspectiveCamera == null)
            {
                perspectiveCamera = GetComponent<PerspectiveCameraController>();
                if (perspectiveCamera == null)
                    perspectiveCamera = Object.FindObjectOfType<PerspectiveCameraController>();
            }

            if (perspectiveCamera != null) perspectiveCamera.SetPlayerRoot(transform);

            if (magnetInteractor == null)
            {
                magnetInteractor = GetComponent<FirstPersonMagnetInteractor>();
            }
            if (pickaxeThrow == null)
            {
                pickaxeThrow = GetComponent<PickaxeThrowController>();
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
            // Gravity always applies. A rope that cancelled gravity could not swing:
            // the fall is what builds the pendulum in the first place.
            motor.Configure(
                moveSpeed,
                Profile.JumpHeight,
                Profile.Gravity,
                Profile.GroundedForce);
        }

        private void CacheControllerDimensions()
        {
            if (controllerDimensionsCached || characterController == null) return;

            standingControllerHeight = characterController.height;
            standingControllerCenter = characterController.center;
            float minimumHeight = characterController.radius * 2f;
            crouchingControllerHeight = Mathf.Clamp(
                Profile.CrouchColliderHeight,
                minimumHeight,
                standingControllerHeight);
            crouchingControllerCenter = standingControllerCenter;
            crouchingControllerCenter.y = standingControllerCenter.y
                - (standingControllerHeight - crouchingControllerHeight) * 0.5f;
            controllerDimensionsCached = true;
        }

        private void UpdateCrouchCollider(bool crouchRequested)
        {
            if (characterController == null)
                characterController = GetComponent<CharacterController>();
            CacheControllerDimensions();
            if (!controllerDimensionsCached || characterController == null) return;

            if (crouchRequested)
            {
                ApplyControllerDimensions(
                    crouchingControllerHeight,
                    crouchingControllerCenter,
                    true);
                return;
            }

            if (!crouchColliderActive || CanUseStandingControllerDimensions())
            {
                ApplyControllerDimensions(
                    standingControllerHeight,
                    standingControllerCenter,
                    false);
            }
        }

        private void ApplyControllerDimensions(
            float height,
            Vector3 center,
            bool crouching)
        {
            characterController.height = height;
            characterController.center = center;
            crouchColliderActive = crouching
                && crouchingControllerHeight < standingControllerHeight;
        }

        private bool CanUseStandingControllerDimensions()
        {
            if (standingControllerHeight <= crouchingControllerHeight) return true;

            Vector3 lossyScale = transform.lossyScale;
            float heightScale = Mathf.Abs(lossyScale.y);
            float radiusScale = Mathf.Max(
                Mathf.Abs(lossyScale.x),
                Mathf.Abs(lossyScale.z));
            float worldRadius = characterController.radius * radiusScale;
            float worldHeight = standingControllerHeight * heightScale;
            float worldSkinWidth = characterController.skinWidth * radiusScale;
            float queryRadius = Mathf.Max(0.001f, worldRadius - worldSkinWidth);
            float halfSegment = Mathf.Max(0f, worldHeight * 0.5f - worldRadius);
            Vector3 worldCenter = transform.TransformPoint(standingControllerCenter);
            Vector3 axisOffset = transform.up * halfSegment;
            int hitCount = Physics.OverlapCapsuleNonAlloc(
                worldCenter - axisOffset,
                worldCenter + axisOffset,
                queryRadius,
                crouchClearanceHits,
                ~0,
                QueryTriggerInteraction.Ignore);

            bool blocked = false;
            for (int i = 0; i < hitCount; i++)
            {
                Collider candidate = crouchClearanceHits[i];
                crouchClearanceHits[i] = null;
                if (candidate == null
                    || candidate == characterController
                    || candidate.transform == transform
                    || candidate.transform.IsChildOf(transform)
                    || Physics.GetIgnoreLayerCollision(
                        gameObject.layer,
                        candidate.gameObject.layer)
                    || Physics.GetIgnoreCollision(characterController, candidate))
                {
                    continue;
                }

                blocked = true;
            }

            // A full buffer can hide an uninspected overlap. Staying crouched is the
            // safe fallback in an unusually dense collision area.
            return !blocked && hitCount < crouchClearanceHits.Length;
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
            firearmLocomotionLayerIndex = animator != null
                && animator.runtimeAnimatorController != null
                ? animator.GetLayerIndex(FirearmLocomotionLayerName)
                : -1;
            firearmArmsLayerIndex = animator != null
                && animator.runtimeAnimatorController != null
                ? animator.GetLayerIndex(FirearmArmsLayerName)
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
            firearmLocomotionLayerTargetWeight = 0f;
            firearmLocomotionLayerWeight = 0f;
            firearmArmsLayerTargetWeight = 0f;
            firearmArmsLayerWeight = 0f;
            toolUpperBodyActionObserved = false;
            if (crouchArmsLocomotionLayerIndex >= 0)
                animator.SetLayerWeight(crouchArmsLocomotionLayerIndex, 0f);
            if (firearmLocomotionLayerIndex >= 0)
                animator.SetLayerWeight(firearmLocomotionLayerIndex, 0f);
            if (firearmArmsLayerIndex >= 0)
                animator.SetLayerWeight(firearmArmsLayerIndex, 0f);
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
                    == PlayerToolPrimaryAction.FireProjectile)
            {
                EnterFirearmContinuousActionImmediately();
            }
        }

        private void SetToolActionAnimationSpeed(PlayerToolDefinition definition)
        {
            float multiplier = definition != null
                ? definition.FirearmAnimationSpeedMultiplier
                : 1f;
            SetToolActionAnimationSpeedMultiplier(multiplier);
        }

        private void SetToolActionAnimationSpeedMultiplier(float multiplier)
        {
            if (!hasToolActionSpeed || animator == null) return;
            animator.SetFloat(ToolActionSpeed, multiplier);
        }

        private void EnterFirearmContinuousActionImmediately()
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

            float targetWeight = crouchColliderActive
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

        private void TickFirearmLocomotionLayerBlend(float deltaTime)
        {
            if (animator == null
                || animator.runtimeAnimatorController == null
                || firearmLocomotionLayerIndex < 0)
            {
                return;
            }

            float blendSpeed = 1f / FirearmLocomotionLayerBlendDuration;
            firearmLocomotionLayerWeight = Mathf.MoveTowards(
                firearmLocomotionLayerWeight,
                firearmLocomotionLayerTargetWeight,
                blendSpeed * deltaTime);
            animator.SetLayerWeight(
                firearmLocomotionLayerIndex,
                firearmLocomotionLayerWeight);
        }

        private void TickFirearmArmsLayerBlend(float deltaTime)
        {
            if (animator == null
                || animator.runtimeAnimatorController == null
                || firearmArmsLayerIndex < 0)
            {
                return;
            }

            float blendSpeed = 1f / FirearmLocomotionLayerBlendDuration;
            firearmArmsLayerWeight = Mathf.MoveTowards(
                firearmArmsLayerWeight,
                firearmArmsLayerTargetWeight,
                blendSpeed * deltaTime);
            animator.SetLayerWeight(firearmArmsLayerIndex, firearmArmsLayerWeight);
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
            bool crouching = crouchColliderActive
                && motor != null
                && motor.IsGrounded;
            PlayerToolDefinition definition = activeToolDefinition != null
                ? activeToolDefinition
                : toolController != null
                    ? toolController.SelectedDefinition
                    : null;
            bool firearmAction = definition != null
                && definition.PrimaryAction
                    == PlayerToolPrimaryAction.FireProjectile;
            return (crouching || firearmAction) && crouchToolArmsLayerIndex >= 0
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
                case PlayerToolPrimaryAction.ThrowPersistentLight:
                    return definition.ProjectilePrefab != null;
                case PlayerToolPrimaryAction.ThrowBomb:
                    return definition.BombProjectilePrefab != null;
                case PlayerToolPrimaryAction.FireProjectile:
                    return definition.FirearmProjectilePrefab != null
                        && toolController != null
                        && toolController.GetAmmunition(definition.Item)
                            > CountPendingToolActions(definition);
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
            if (definition == null) return;
            ApplyPlaceholderAnimation(definition.PrimaryActionAnimation);
        }

        /// <summary>
        /// Points the shared upper-body placeholder slot at <paramref name="clip"/>.
        /// Both the primary action and the magnet hold pose drive the same slot.
        /// </summary>
        private bool ApplyPlaceholderAnimation(AnimationClip clip)
        {
            if (clip == null || !EnsureToolAnimatorController()) return false;
            if (activePrimaryActionAnimation == clip) return true;

            toolAnimatorController[PrimaryActionPlaceholderClipName] = clip;
            activePrimaryActionAnimation = clip;
            return true;
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
            // The primary action takes over the shared upper-body slot.
            ApplyMagnetHoldAnimation(false);
            activeToolDefinition = toolController != null
                ? toolController.SelectedDefinition
                : null;
            bool grounded = motor != null && motor.IsGrounded;
            bool crouching = crouchColliderActive && grounded;
            SetAnimationState(false, !grounded, false, crouching);
            ApplyToolActionAnimation(activeToolDefinition);
            StartConfiguredToolActionAnimation();

            if (activeToolDefinition == null) return;
            StartToolActionCycle(activeToolDefinition);
        }

        private void ExitToolAction()
        {
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

            if (definition.PrimaryAction == PlayerToolPrimaryAction.MineVoxel)
            {
                SoundEffectEvents.RequestPlay(
                    definition.PrimaryActionSound,
                    transform.position);
            }

            nextToolActionCycleTimes[definition] =
                Time.time + definition.ActionCyclePeriod;
            return true;
        }

        private bool ScheduleMiningToolAction(PlayerToolDefinition definition)
        {
            float delay = definition.ActionTriggerDelay;
            bool isPickaxe = definition.Item == PlayerInventoryItem.Pickaxe;
            ScheduleMiningAttack(
                delay,
                isPickaxe ? definition.MonsterHitSound : null);
            int strikeNumber = isPickaxe ? pickaxeStrikeParity + 1 : 1;
            VoxelMiningBrushSettings brush =
                definition.GetMiningBrushForStrike(strikeNumber);
            bool scheduled = voxelInteractor != null
                && voxelInteractor.TryScheduleMineAtCrosshair(
                    delay,
                    brush,
                    isPickaxe ? definition.MiningHitSound : null);
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

            bool executed;
            switch (definition.PrimaryAction)
            {
                case PlayerToolPrimaryAction.ThrowPersistentLight:
                    executed = ThrowConfiguredProjectile(definition) != null;
                    break;
                case PlayerToolPrimaryAction.ThrowBomb:
                    executed = ThrowConfiguredBomb(definition) != null;
                    break;
                case PlayerToolPrimaryAction.FireProjectile:
                    executed = FireConfiguredProjectile(definition) != null;
                    break;
                default:
                    executed = false;
                    break;
            }

            if (executed)
            {
                bool usesConfiguredThrowSound = definition.ThrowSound != null;
                SoundEffectEvents.RequestPlay(
                    usesConfiguredThrowSound
                        ? definition.ThrowSound
                        : definition.PrimaryActionSound,
                    transform.position,
                    usesConfiguredThrowSound
                        ? ConfiguredThrowSoundVolumeScale
                        : 1f);
            }

            return executed;
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

            if (actionHeld
                && activeToolDefinition.ActionIsPeriodic
                && IsToolActionCycleReady(activeToolDefinition)
                && !StartToolActionCycle(activeToolDefinition))
            {
                SelectGroundOrAirState();
            }
        }

        /// <summary>
        /// Right click is always the magnet, whatever tool is held, and the pickaxe
        /// throw has its own key. Both run alongside the locomotion state machine so
        /// they stay available while the player moves and mines.
        /// </summary>
        private void TickSecondaryAction()
        {
            TickPickaxeThrowAction(input.ThrowPickaxeHeld);
            TickMagnetSecondaryAction(input.SecondaryActionHeld);
        }

        private void TickMagnetSecondaryAction(bool held)
        {
            if (magnetInteractor == null)
            {
                StopMagnetSound();
                return;
            }

            if (!held)
            {
                if (magnetInteractor.IsActionActive)
                    magnetInteractor.EndAttraction();
                StopMagnetSound();
                ApplyMagnetHoldAnimation(false);
                return;
            }

            if (!magnetInteractor.IsActionActive)
            {
                magnetInteractor.BeginAttraction(ResolvePickaxeDefinition());
            }
            else
            {
                // While the rope is live the scroll wheel reels it instead of moving
                // the ordinary magnet's hold point, and the movement keys drive the
                // swing rather than walking.
                if (magnetInteractor.IsPullingTowardsPickaxe)
                {
                    magnetInteractor.SetRopeSwingInput(
                        GetWorldMovement(input.Move));
                    // RequestRopeReel is positive-shortens. Scrolling forward gives
                    // +1 and should draw the player in, so the step maps straight
                    // through with no inversion.
                    magnetInteractor.RequestRopeReel(
                        input.AttractionDistanceSteps * RopeReelMetresPerStep);
                    magnetInteractor.TickAttraction();
                }
                else
                {
                    magnetInteractor.TickAttraction(
                        input.AttractionDistanceSteps);
                }
            }

            bool actionActive = magnetInteractor.IsActionActive;
            UpdateMagnetSound(magnetInteractor.IsAttractingTarget);
            ApplyMagnetHoldAnimation(actionActive);
        }

        /// <summary>
        /// Holds the two-handed magnet pose while attracting. A primary action owns
        /// the same upper-body layer, so mining keeps priority over the pose.
        /// </summary>
        private void ApplyMagnetHoldAnimation(bool active)
        {
            bool wanted = active && activeToolDefinition == null;
            if (magnetHoldAnimationActive == wanted) return;

            magnetHoldAnimationActive = wanted;
            // Hide the equipped model while the magnet pose owns the hands so it
            // cannot clip through the held animation.
            toolController?.SetEquippedToolModelHidden(wanted);
            if (wanted)
            {
                PlayerToolDefinition definition = ResolveMagnetHoldDefinition();
                AnimationClip clip = definition != null
                    ? definition.MagnetHoldAnimation
                    : null;
                if (clip == null)
                {
                    magnetHoldAnimationActive = false;
                    toolController?.SetEquippedToolModelHidden(false);
                    return;
                }
                if (!ApplyPlaceholderAnimation(clip))
                {
                    magnetHoldAnimationActive = false;
                    toolController?.SetEquippedToolModelHidden(false);
                    return;
                }

                magnetHoldAnimationDefinition = definition;
                SetContinuousToolActionAnimation(true);
                SetToolActionAnimationSpeedMultiplier(
                    definition.MagnetHoldLoopSpeedMultiplier);
                PlayMagnetHoldAnimationAt(
                    definition.MagnetHoldLoopStartNormalized);
                return;
            }

            SetContinuousToolActionAnimation(false);
            magnetHoldAnimationDefinition = null;
        }

        private PlayerToolDefinition ResolveMagnetHoldDefinition()
        {
            PlayerToolDefinition selected = toolController != null
                ? toolController.SelectedDefinition
                : null;
            if (selected != null && selected.MagnetHoldAnimation != null)
                return selected;

            // An empty slot still gets the magnet, so fall back to the pickaxe's
            // configured pose rather than leaving the hands in the locomotion pose.
            PlayerToolDefinition pickaxe = ResolvePickaxeDefinition();
            return pickaxe != null && pickaxe.MagnetHoldAnimation != null
                ? pickaxe
                : null;
        }

        private void TickMagnetHoldAnimationLoop()
        {
            if (!magnetHoldAnimationActive
                || magnetHoldAnimationDefinition == null
                || animator == null
                || animator.runtimeAnimatorController == null
                || activeToolActionLayerIndex < 0)
            {
                return;
            }

            AnimatorStateInfo state =
                animator.GetCurrentAnimatorStateInfo(activeToolActionLayerIndex);
            if (state.shortNameHash != ToolContinuousActionState
                || animator.IsInTransition(activeToolActionLayerIndex))
            {
                return;
            }

            float loopStart =
                magnetHoldAnimationDefinition.MagnetHoldLoopStartNormalized;
            float loopEnd =
                magnetHoldAnimationDefinition.MagnetHoldLoopEndNormalized;
            float blendDuration =
                magnetHoldAnimationDefinition.MagnetHoldLoopBlendNormalized;

            // A very slow frame can step past the whole blend window. Recover by
            // wrapping immediately instead of cross-fading from the clip's unrelated
            // first frame, which Unity would already have sampled at this point.
            if (state.normalizedTime >= loopEnd)
            {
                PlayMagnetHoldAnimationAt(
                    WrapMagnetHoldNormalizedTime(
                        state.normalizedTime,
                        loopStart,
                        loopEnd));
                return;
            }

            float blendStart = GetMagnetHoldLoopBlendStart(
                loopStart,
                loopEnd,
                blendDuration);
            if (blendDuration <= 0f || state.normalizedTime < blendStart) return;

            CrossFadeMagnetHoldAnimation(loopStart, blendDuration);
        }

        private void PlayMagnetHoldAnimationAt(float normalizedTime)
        {
            if (animator == null || activeToolActionLayerIndex < 0) return;
            int stateHash = activeToolActionLayerIndex == crouchToolArmsLayerIndex
                ? ToolArmsContinuousActionState
                : ToolUpperBodyContinuousActionState;
            animator.Play(stateHash, activeToolActionLayerIndex, normalizedTime);
        }

        private void CrossFadeMagnetHoldAnimation(
            float normalizedTime,
            float normalizedDuration)
        {
            if (animator == null || activeToolActionLayerIndex < 0) return;
            int stateHash = activeToolActionLayerIndex == crouchToolArmsLayerIndex
                ? ToolArmsContinuousActionState
                : ToolUpperBodyContinuousActionState;
            animator.CrossFade(
                stateHash,
                normalizedDuration,
                activeToolActionLayerIndex,
                normalizedTime);
        }

        private static float GetMagnetHoldLoopBlendStart(
            float loopStart,
            float loopEnd,
            float blendDuration)
        {
            float start = Mathf.Clamp(loopStart, 0f, 0.95f);
            float end = Mathf.Clamp(loopEnd, start + 0.05f, 1f);
            float maximumBlend = (end - start) * 0.45f;
            return end - Mathf.Clamp(blendDuration, 0f, maximumBlend);
        }

        private static float WrapMagnetHoldNormalizedTime(
            float normalizedTime,
            float loopStart,
            float loopEnd)
        {
            float start = Mathf.Clamp(loopStart, 0f, 0.95f);
            float end = Mathf.Clamp(loopEnd, start + 0.05f, 1f);
            if (normalizedTime < end) return normalizedTime;

            return start + Mathf.Repeat(normalizedTime - start, end - start);
        }

        /// <summary>
        /// Hold the throw key to aim, release to launch. The key must be released and
        /// pressed again to start another throw, so recovering a pickaxe while still
        /// holding the key cannot immediately fling it back out.
        /// </summary>
        private void TickPickaxeThrowAction(bool held)
        {
            if (pickaxeThrow == null) return;

            if (!held)
            {
                throwKeyRearmed = true;
                if (!pickaxeThrow.IsAiming) return;
                if (Cursor.lockState == CursorLockMode.Locked)
                    pickaxeThrow.ReleaseThrow();
                else
                    pickaxeThrow.CancelAim();
                return;
            }

            if (pickaxeThrow.IsAiming || !throwKeyRearmed) return;

            // With a pickaxe already out, the key recalls it instead of aiming a
            // second throw. Consume the press either way so holding the key cannot
            // recall and immediately re-throw.
            if (pickaxeThrow.HasThrowInFlight)
            {
                throwKeyRearmed = false;
                // Drop the rope first, so the magnet is not left towing a pickaxe
                // that has started flying home.
                if (magnetInteractor != null
                    && magnetInteractor.IsPullingTowardsPickaxe)
                {
                    magnetInteractor.EndAttraction();
                }
                pickaxeThrow.RecallThrow();
                return;
            }

            PlayerToolDefinition pickaxe = ResolvePickaxeDefinition();
            if (pickaxe == null) return;
            // A held key that cannot start an aim is consumed, so releasing it later
            // never counts as a throw.
            if (!pickaxeThrow.BeginAim(pickaxe))
                throwKeyRearmed = false;
        }

        private void CancelPickaxeThrowAim()
        {
            if (pickaxeThrow != null && pickaxeThrow.IsAiming)
                pickaxeThrow.CancelAim();
        }

        private void CancelSecondaryAction()
        {
            CancelPickaxeThrowAim();
            if (magnetInteractor != null
                && magnetInteractor.IsActionActive)
            {
                magnetInteractor.EndAttraction();
            }
            StopMagnetSound();
            ApplyMagnetHoldAnimation(false);
        }

        /// <summary>
        /// The pickaxe definition supplies the magnet's retrieval tuning, so a
        /// thrown pickaxe can be recovered while holding any other tool.
        /// </summary>
        private PlayerToolDefinition ResolvePickaxeDefinition()
        {
            return toolController != null
                ? toolController.GetDefinition(PlayerInventoryItem.Pickaxe)
                : null;
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

        private BombProjectile ThrowConfiguredBomb(
            PlayerToolDefinition definition)
        {
            if (definition == null || definition.BombProjectilePrefab == null)
                return null;

            Transform origin = view != null ? view : transform;
            Vector3 forward = origin.forward.sqrMagnitude > 0.0001f
                ? origin.forward.normalized
                : transform.forward;
            Vector3 position = origin.position
                + forward * definition.ThrowForwardOffset;
            BombProjectile projectile = Instantiate(
                definition.BombProjectilePrefab,
                position,
                Quaternion.LookRotation(forward, Vector3.up));
            projectile.name = definition.BombProjectilePrefab.name;
            projectile.Launch(
                forward * definition.ThrowSpeed
                    + Vector3.up * definition.UpwardThrowSpeed,
                Random.onUnitSphere * definition.ThrowSpinSpeed,
                voxelInteractor != null ? voxelInteractor.VoxelTerrain : null,
                definition.BombEntityExplosionImpulse,
                definition.BombExplosionEffectPrefab,
                definition.BombExplosionEffectLifetime);
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
            ResolveReferences();
            perspectiveCamera?.SetMode(PlayerViewMode.ThirdPerson, false);
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
            firearmLocomotionLayerTargetWeight = 0f;
            firearmLocomotionLayerWeight = 0f;
            firearmArmsLayerTargetWeight = 0f;
            firearmArmsLayerWeight = 0f;
            toolUpperBodyLayerTargetWeight = 0f;
            toolUpperBodyLayerWeight = 0f;
            crouchToolArmsLayerTargetWeight = 0f;
            crouchToolArmsLayerWeight = 0f;

            if (animator == null || animator.runtimeAnimatorController == null)
                return;

            if (firearmLocomotionLayerIndex >= 0)
                animator.SetLayerWeight(firearmLocomotionLayerIndex, 0f);
            if (firearmArmsLayerIndex >= 0)
                animator.SetLayerWeight(firearmArmsLayerIndex, 0f);
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
                bool secondaryActionHeld,
                bool throwPickaxeHeld,
                bool crouchHeld,
                float attractionDistanceSteps)
            {
                Move = move;
                JumpPressed = jumpPressed;
                PrimaryActionHeld = primaryActionHeld;
                SecondaryActionHeld = secondaryActionHeld;
                ThrowPickaxeHeld = throwPickaxeHeld;
                CrouchHeld = crouchHeld;
                AttractionDistanceSteps = attractionDistanceSteps;
            }

            public Vector2 Move { get; }
            public bool JumpPressed { get; }
            public bool PrimaryActionHeld { get; }
            public bool SecondaryActionHeld { get; }
            public bool ThrowPickaxeHeld { get; }
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
            void AddExternalAcceleration(
                Vector3 acceleration,
                float deltaTime,
                float maximumSpeed);
            void ResetExternalVelocity();
            /// <summary>
            /// Gravity and external motion combined. A rope has to constrain the whole
            /// velocity, but the motor keeps the fall in VerticalVelocity and
            /// everything else in externalVelocity, so it has to be composed here.
            /// </summary>
            Vector3 CombinedVelocity { get; }
            /// <summary>
            /// Replaces the combined velocity, splitting it back into the motor's
            /// vertical and external channels.
            /// </summary>
            void SetCombinedVelocity(Vector3 velocity);
            void AddExternalVelocity(Vector3 velocity, float maximumSpeed);
        }

        private sealed class CharacterControllerMotor : IPlayerMotor
        {
            /// <summary>Per-second fraction of external momentum lost while standing.</summary>
            private const float GroundedExternalDamping = 8f;
            /// <summary>Light air drag, so a released swing still carries the player.</summary>
            private const float AirborneExternalDamping = 0.35f;
            /// <summary>Below this squared speed the residue is snapped to zero.</summary>
            private const float ExternalVelocityCutoff = 0.01f;
            /// <summary>
            /// Fraction of the into-surface momentum removed on impact. Just under one
            /// leaves a trace of push so the player still settles against the surface
            /// rather than detaching from it.
            /// </summary>
            private const float ImpactAbsorption = 0.9f;
            /// <summary>
            /// Squared metres of undelivered displacement below which an impact is
            /// treated as ordinary contact jitter rather than a real collision.
            /// </summary>
            private const float BlockedDisplacementEpsilon = 0.0000001f;

            private readonly CharacterController controller;
            private float moveSpeed;
            private float jumpHeight;
            private float gravity;
            private float groundedForce;
            private Vector3 externalVelocity;

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
                Vector3 velocity = planarMovement * moveSpeed
                    + Vector3.up * VerticalVelocity
                    + externalVelocity;
                Vector3 intended = velocity * deltaTime;
                Vector3 startPosition = controller.transform.position;
                CollisionFlags collisions = controller.Move(intended);
                if ((collisions & CollisionFlags.Sides) != 0)
                {
                    // Only sideways impacts need this. Ground and ceiling contact is
                    // handled below, and reacting to their blocked displacement too
                    // would absorb ordinary standing-still momentum.
                    AbsorbBlockedMomentum(
                        intended,
                        controller.transform.position - startPosition);
                }
                if ((collisions & CollisionFlags.Below) != 0
                    && externalVelocity.y < 0f)
                {
                    externalVelocity.y = 0f;
                }
                if ((collisions & CollisionFlags.Above) != 0
                    && externalVelocity.y > 0f)
                {
                    externalVelocity.y = 0f;
                }

                DecayExternalVelocity(wasGrounded, deltaTime);
            }

            /// <summary>
            /// Removes the part of the external momentum that a wall just refused to
            /// let through. Whatever horizontal displacement the controller did not
            /// deliver is the direction it was blocked in, so cancelling the velocity
            /// along that direction stops the player pressing into the wall after
            /// hitting it, while the sideways part survives so they slide along it
            /// instead of sticking.
            /// </summary>
            private void AbsorbBlockedMomentum(
                Vector3 intended,
                Vector3 actual)
            {
                if (externalVelocity.sqrMagnitude <= 0.000001f) return;

                // Vertical blocking is the ground and ceiling's business, and mixing it
                // in here would let standing on a floor eat horizontal momentum.
                Vector3 blocked = intended - actual;
                blocked.y = 0f;
                if (blocked.sqrMagnitude <= BlockedDisplacementEpsilon) return;

                Vector3 blockedDirection = blocked.normalized;
                // Only momentum heading into the obstruction is absorbed; motion along
                // or away from it is left untouched.
                float intoSurface = Vector3.Dot(externalVelocity, blockedDirection);
                if (intoSurface <= 0f) return;

                externalVelocity -= blockedDirection
                    * (intoSurface * Mathf.Clamp01(ImpactAbsorption));
            }

            /// <summary>
            /// Bleeds off external velocity so momentum handed to the motor by a rope,
            /// an explosion, or a pull eventually stops. Without this the player keeps
            /// drifting forever, because nothing else ever reduces it.
            /// </summary>
            private void DecayExternalVelocity(bool grounded, float deltaTime)
            {
                if (externalVelocity.sqrMagnitude <= 0.000001f)
                {
                    externalVelocity = Vector3.zero;
                    return;
                }

                // Standing on ground scrubs momentum quickly (boots on rock); in the
                // air only light drag applies, so a swing release still carries.
                float damping = grounded
                    ? GroundedExternalDamping
                    : AirborneExternalDamping;
                externalVelocity *= Mathf.Clamp01(1f - damping * deltaTime);
                if (externalVelocity.sqrMagnitude < ExternalVelocityCutoff)
                    externalVelocity = Vector3.zero;
            }

            public void ResetVerticalVelocity()
            {
                VerticalVelocity = 0f;
            }

            public void AddExternalAcceleration(
                Vector3 acceleration,
                float deltaTime,
                float maximumSpeed)
            {
                if (deltaTime <= 0f || acceleration.sqrMagnitude <= 0f)
                    return;

                externalVelocity += acceleration * deltaTime;
                externalVelocity = Vector3.ClampMagnitude(
                    externalVelocity,
                    Mathf.Max(0f, maximumSpeed));
            }

            public void ResetExternalVelocity()
            {
                externalVelocity = Vector3.zero;
            }

            public Vector3 CombinedVelocity =>
                externalVelocity + Vector3.up * VerticalVelocity;

            public void SetCombinedVelocity(Vector3 velocity)
            {
                // Keep the fall in the vertical channel and the rest external, so
                // grounding, jumping and ceiling clamps keep working unchanged.
                VerticalVelocity = velocity.y;
                externalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            }

            public void AddExternalVelocity(
                Vector3 velocity,
                float maximumSpeed)
            {
                if (velocity.sqrMagnitude <= 0f) return;

                externalVelocity += velocity;
                externalVelocity = Vector3.ClampMagnitude(
                    externalVelocity,
                    Mathf.Max(0f, maximumSpeed));
            }
        }
    }
}
