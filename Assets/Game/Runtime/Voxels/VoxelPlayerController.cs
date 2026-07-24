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
        Attack,
        Hurt,
        Dead,
        CrouchIdle,
        CrouchMove,
        MagnetAttract,
    }

    /// <summary>
    /// Collects player input and adapts state-machine locomotion commands to a CharacterController.
    /// The states do not depend on CharacterController or Unity physics.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerProfile))]
    public sealed class VoxelPlayerController : MonoBehaviour, IDamageable
    {
        private static readonly int WalkFlag = Animator.StringToHash("walkFlag");
        private static readonly int JumpFlag = Animator.StringToHash("jumpFlag");
        private static readonly int IdleFlag = Animator.StringToHash("idleFlag");
        private static readonly int IdleBFlag = Animator.StringToHash("idleBFlag");
        private static readonly int SmileFlag = Animator.StringToHash("smileFlag");
        private static readonly int KocchiFlag = Animator.StringToHash("kocchiFlag");
        private static readonly int MineFlag = Animator.StringToHash("Mine");
        private static readonly int HitFlag = Animator.StringToHash("Hit");
        private static readonly int DieFlag = Animator.StringToHash("Die");
        private static readonly int RecoverFlag = Animator.StringToHash("Recover");
        private static readonly int CrouchFlag = Animator.StringToHash("crouchFlag");
        private static readonly int CrouchMoveFlag = Animator.StringToHash("crouchMoveFlag");
        private static readonly int PrimaryActionFlag = Animator.StringToHash("primaryActionFlag");
        private static readonly int ToolIndexParam = Animator.StringToHash("toolIndex");

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
        private bool attackApplied;
        private bool debugFlyMode;
        private bool hasWalkFlag;
        private bool hasJumpFlag;
        private bool hasIdleFlag;
        private bool hasIdleBFlag;
        private bool hasSmileFlag;
        private bool hasKocchiFlag;
        private bool hasMineFlag;
        private bool hasHitFlag;
        private bool hasDieFlag;
        private bool hasRecoverFlag;
        private bool hasCrouchFlag;
        private bool hasCrouchMoveFlag;
        private bool hasPrimaryActionFlag;
        private bool hasToolIndexParam;
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
            if (characterController == null) return;

            if (Input.GetKeyDown(Profile.DebugToggleKey)) SetDebugFlyMode(!debugFlyMode);
            input = CaptureInput();
            if (debugFlyMode)
            {
                UpdateDebugFlyMovement(input.Move);
                SetAnimationState(false, false, true);
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
            bool pickaxeSelected = toolController == null || toolController.IsPickaxeSelected;
            bool magnetSelected = toolController != null
                && toolController.IsCartAttractorSelected
                && cartAttractor != null
                && cartAttractor.CanOperate;
            return new PlayerInputSnapshot(
                movement,
                acceptsAction && Input.GetButtonDown("Jump"),
                primaryHeld && pickaxeSelected,
                primaryHeld && magnetSelected,
                Input.GetKey(Profile.CrouchKey));
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
            if (input.MagnetHeld)
            {
                stateMachine.Change(PlayerCharacterState.MagnetAttract);
                return true;
            }

            if (input.AttackPressed && Time.time >= nextAttackTime)
            {
                stateMachine.Change(PlayerCharacterState.Attack);
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

        private void UpdateExpressionAnimation()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
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
            if (animator == null || animator.runtimeAnimatorController == null) return;
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
                && stateMachine.Current == PlayerCharacterState.MagnetAttract)
            {
                stateMachine.Change(PlayerCharacterState.Idle);
            }
            debugFlyMode = enabled;
            idleSeconds = 0f;
            motor?.ResetVerticalVelocity();
            if (characterController != null) characterController.enabled = !enabled;
        }

        public void SetAnimator(Animator characterAnimator)
        {
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
                Animator previousAnimator = animator;
                animator = GetComponentInChildren<Animator>(false);
                if (animator != null)
                {
                    animator.applyRootMotion = false;
                    if (animator != previousAnimator) CacheAnimatorParameters();
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
            hasMineFlag = HasAnimatorParameter(MineFlag, AnimatorControllerParameterType.Trigger);
            hasHitFlag = HasAnimatorParameter(HitFlag, AnimatorControllerParameterType.Trigger);
            hasDieFlag = HasAnimatorParameter(DieFlag, AnimatorControllerParameterType.Trigger);
            hasRecoverFlag = HasAnimatorParameter(RecoverFlag, AnimatorControllerParameterType.Trigger);
            hasCrouchFlag = HasAnimatorParameter(CrouchFlag, AnimatorControllerParameterType.Bool);
            hasCrouchMoveFlag = HasAnimatorParameter(
                CrouchMoveFlag,
                AnimatorControllerParameterType.Bool);
            hasPrimaryActionFlag = HasAnimatorParameter(
                PrimaryActionFlag,
                AnimatorControllerParameterType.Bool);
            hasToolIndexParam = HasAnimatorParameter(
                ToolIndexParam,
                AnimatorControllerParameterType.Int);
            lowerBodyLayerIndex = animator != null && animator.runtimeAnimatorController != null
                ? animator.GetLayerIndex("LowerBody Layer")
                : -1;
        }

        /// <summary>
        /// Generic hook for "left-click action" states (Attack, MagnetAttract, ...). The tool
        /// currently equipped is exposed as an int so the Animator Controller can branch to a
        /// different clip per tool without any change to this state machine.
        /// </summary>
        private void SetPrimaryActionState(bool active)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (hasPrimaryActionFlag) animator.SetBool(PrimaryActionFlag, active);
            if (hasToolIndexParam)
            {
                int toolIndex = toolController != null ? (int)toolController.SelectedItem : 0;
                animator.SetInteger(ToolIndexParam, toolIndex);
            }
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
                PlayerCharacterState.Attack,
                TickAttack,
                EnterAttack,
                ExitAttack));
            stateMachine.Add(new PlayerState(
                this,
                PlayerCharacterState.MagnetAttract,
                TickMagnetAttract,
                EnterMagnetAttract,
                ExitMagnetAttract));
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

        private void EnterAttack()
        {
            stateSeconds = 0f;
            attackApplied = false;
            TriggerMineSwing();
            bool crouching = input.CrouchHeld && motor.IsGrounded;
            SetAnimationState(false, !motor.IsGrounded, false, crouching);
            SetPrimaryActionState(true);
        }

        private void ExitAttack()
        {
            SetPrimaryActionState(false);
        }

        // Performs one mining swing (animation + scheduled voxel hit) and sets the
        // cadence gate. A real hit uses the full interval; an empty swing uses a
        // short whiff cooldown so grazing a block edge doesn't stall the player.
        private void TriggerMineSwing()
        {
            if (animator != null && hasMineFlag) animator.SetTrigger(MineFlag);
            bool minedSomething = voxelInteractor != null
                && voxelInteractor.TryScheduleMineAtCrosshair(Profile.VoxelDestructionDelay);
            nextAttackTime = Time.time + (minedSomething
                ? Profile.MineInterval
                : Profile.MineWhiffCooldown);
        }

        private void TickAttack(float deltaTime)
        {
            stateSeconds += deltaTime;
            TickLocomotion(deltaTime, false);
            if (!attackApplied && stateSeconds >= Profile.AttackWindup)
            {
                attackApplied = true;
                PerformAttack();
            }

            // While the button stays held, keep swinging on cadence so digging is
            // continuous instead of one block per click. We stay in the Attack
            // state and re-trigger in place rather than re-entering the state.
            if (input.AttackPressed)
            {
                if (Time.time >= nextAttackTime)
                {
                    TriggerMineSwing();
                }
                return;
            }

            if (stateSeconds >= Profile.AttackDuration) SelectGroundOrAirState();
        }

        private void EnterMagnetAttract()
        {
            cartAttractor?.BeginAttraction();
            SetPrimaryActionState(true);
        }

        private void TickMagnetAttract(float deltaTime)
        {
            if (!input.MagnetHeld || cartAttractor == null || !cartAttractor.IsActionActive)
            {
                SelectGroundOrAirState();
                return;
            }

            cartAttractor.TickAttraction();
            TickLocomotion(deltaTime, true);
        }

        private void ExitMagnetAttract()
        {
            cartAttractor?.EndAttraction();
            SetPrimaryActionState(false);
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
                bool attackPressed,
                bool magnetHeld,
                bool crouchHeld)
            {
                Move = move;
                JumpPressed = jumpPressed;
                AttackPressed = attackPressed;
                MagnetHeld = magnetHeld;
                CrouchHeld = crouchHeld;
            }

            public Vector2 Move { get; }
            public bool JumpPressed { get; }
            public bool AttackPressed { get; }
            public bool MagnetHeld { get; }
            public bool CrouchHeld { get; }
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
