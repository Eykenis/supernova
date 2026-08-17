using System;
using System.Collections.Generic;
using Supernova.Inputs;
using Supernova.MinecraftCaves.Creatures;
using Supernova.Shop;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Handles the first-person magnet, including ordinary rigidbody attraction
    /// and the thrown-pickaxe rope interaction.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class FirstPersonMagnetInteractor : MonoBehaviour
    {
        public static event Action<FirstPersonMagnetInteractor> InstanceEnabled;
        public static event Action<FirstPersonMagnetInteractor> InstanceDisabled;

        private const float MagnetAimAssistRadius = 0.35f;
        private const float MagnetOcclusionTolerance = 0.05f;
        /// <summary>
        /// Slack allowed before the rope counts as taut. Without a tolerance, floating
        /// point jitter at full extension makes the constraint flicker on and off.
        /// </summary>
        private const float RopeTautTolerance = 0.05f;
        [Header("Interaction")]
        [SerializeField] private bool deviceEnabled = true;
        [SerializeField] private PerspectiveCameraController perspectiveCamera;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Transform playerRoot;

        [Header("Acquisition")]
        [SerializeField, Min(0.1f)] private float acquisitionDistance = 3.5f;
        [SerializeField] private LayerMask targetLayers = ~0;

        [Header("Physical hold")]
        [SerializeField, Min(0.2f)] private float minimumHoldDistance = 0.5f;
        [SerializeField, Min(0.2f)] private float holdDistance = 2f;
        [SerializeField, Min(0.2f)] private float maximumHoldDistance = 6f;
        [SerializeField, Min(0f)] private float scrollDistancePerStep = 0.35f;
        [Tooltip("Maximum attraction force in newtons. Rigidbody mass determines acceleration.")]
        [SerializeField, Min(0f)] private float attractionForce = 100f;
        [Tooltip("Prevents very light objects from being launched by the magnet.")]
        [SerializeField, Min(0f)] private float maximumAttractionAcceleration = 40f;
        [Tooltip("Position spring strength. Lower force is used as the object approaches the hold point.")]
        [SerializeField, Min(0f)] private float positionSpring = 300f;
        [Tooltip("Velocity damping in newtons per metre/second. Total force remains capped.")]
        [SerializeField, Min(0f)] private float forceDamping = 90f;
        [Header("Magnet orientation")]
        [SerializeField, Min(0f)] private float orientationSpring = 55f;
        [SerializeField, Min(0f)] private float orientationDamping = 14f;
        [SerializeField, Min(0f)] private float maximumOrientationTorque = 180f;
        [SerializeField, Min(0f)] private float rotationDegreesPerMouseUnit = 5f;
        [Tooltip("Maximum upward force applied to a held object, in newtons.")]
        [SerializeField, Min(0f)] private float baseMaximumLiftForce = 300f;
        [Tooltip("Reduces maximum upward force as the object's actual height above its acquisition point increases.")]
        [SerializeField, Min(0f)] private float liftForceFalloffPerMeter = 0.6f;
        [SerializeField, Min(0.5f)] private float breakDistance = 8f;

        private readonly RaycastHit[] acquisitionHits = new RaycastHit[32];
        private readonly Collider[] aimAssistColliders = new Collider[64];
        private readonly HashSet<Rigidbody> aimAssistBodies =
            new HashSet<Rigidbody>();
        private readonly List<Collider> targetColliderBuffer =
            new List<Collider>(16);
        private Rigidbody heldBody;
        private ValuableObject heldValuableObject;
        private CreatureBehaviorAgent heldCreature;
        private ThrownPickaxe towedPickaxe;
        private PlayerToolDefinition pickaxePullDefinition;
        private float ropeLength;
        private bool ropeAttached;
        private bool ropeWasTaut;
        private float ropeReelRequest;
        private Vector3 ropeSwingInput;
        private Vector3 ropeSwingInputTarget;
        private VoxelPlayerController playerMotorOwner;
        private CharacterController playerController;
        private bool magnetActionActive;
        private Quaternion heldTargetRotation;
        private bool hasHeldTargetRotation;
        private float magnetPickupHeight;
        private PlayerToolController toolController;
        private bool targetAvailabilityInitialized;
        private bool targetAvailable;

        public event Action<bool> TargetAvailabilityChanged;

        public bool DeviceEnabled => deviceEnabled;
        public bool IsHolding => heldBody != null;
        /// <summary>
        /// True while the magnet is dragging the player towards a thrown pickaxe
        /// instead of reeling an object into the view.
        /// </summary>
        public bool IsPullingTowardsPickaxe => towedPickaxe != null;
        public ThrownPickaxe TowedPickaxe => towedPickaxe;
        public bool IsRotatingHeldObject => IsHolding
            && GameInput.Held(GameInputActionId.MagnetRotate);
        public bool IsManipulatingHeldObject => IsRotatingHeldObject;
        public Rigidbody HeldBody => heldBody;
        public ValuableObject HeldValuableObject => heldValuableObject;
        public bool IsActionActive => magnetActionActive;
        /// <summary>
        /// True only while an active magnet action has actually latched onto a
        /// rigidbody or a thrown pickaxe. Holding the input with no target does not
        /// count as attraction.
        /// </summary>
        public bool IsAttractingTarget =>
            magnetActionActive && HasAttractionBeamTarget;
        /// <summary>
        /// Whether the magnet beam should be drawn. Both reeling an object in and
        /// pulling towards a thrown pickaxe are beam-visible states.
        /// </summary>
        public bool HasAttractionBeamTarget =>
            heldBody != null || towedPickaxe != null;
        /// <summary>
        /// World point the magnet beam terminates at, for either attraction mode.
        /// </summary>
        public Vector3 AttractionBeamTarget
        {
            get
            {
                if (towedPickaxe != null) return towedPickaxe.Position;
                return heldBody != null
                    ? heldBody.worldCenterOfMass
                    : transform.position;
            }
        }
        public float HoldDistance => holdDistance;
        public float BaseAttractionForce => Mathf.Max(0f, attractionForce);
        public float AttractionForce => BaseAttractionForce
            + PlayerEconomy.GetUpgradeValue(
                PlayerUpgrade.MagnetAttractionForce);

        /// <summary>
        /// Returns how much of the magnet's current upward capacity the body's
        /// weight consumes. A value of one means the magnet can only just oppose
        /// gravity; values above one mean the body cannot be lifted.
        /// </summary>
        public float GetAttractionLoadRatio(Rigidbody body)
        {
            if (body == null) return 0f;

            float bodyMass = Mathf.Max(0f, body.mass);
            float availableForce = Mathf.Min(
                AttractionForce,
                CalculateMaximumLiftForce(body.worldCenterOfMass.y));
            availableForce = Mathf.Min(
                availableForce,
                bodyMass * Mathf.Max(0f, maximumAttractionAcceleration));
            float requiredForce = bodyMass * Physics.gravity.magnitude;
            if (requiredForce <= 0f) return 0f;
            if (availableForce <= 0f) return float.PositiveInfinity;
            return requiredForce / availableForce;
        }

        public bool CanOperate => deviceEnabled && CanOperateInFirstPerson;
        public bool ConsumesPrimaryAction => isActiveAndEnabled
            && CanOperate;

        private void Awake()
        {
            ResolveReferences();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeEvents()
        {
            InstanceEnabled = null;
            InstanceDisabled = null;
        }

        private void OnEnable()
        {
            ResolveReferences();
            targetAvailabilityInitialized = false;
            InstanceEnabled?.Invoke(this);
        }

        private void Update()
        {
            if (!Application.isPlaying
                || Cursor.lockState != CursorLockMode.Locked)
            {
                SetTargetAvailability(false);
                return;
            }

            if (IsRotatingHeldObject)
            {
                Vector2 look = GameInput.ReadVector2(GameInputActionId.Look);
                UpdateHeldTargetRotation(look.x, look.y);
            }

            EvaluateTargetAvailability(false);
        }

        /// <summary>
        /// Publishes the current aim state to subscribers. The HUD calls this once
        /// when it binds; subsequent changes are published from this component.
        /// </summary>
        public void RefreshTargetAvailability()
        {
            EvaluateTargetAvailability(true);
        }

        private void EvaluateTargetAvailability(bool force)
        {
            if (TargetAvailabilityChanged == null)
                return;

            PlayerToolDefinition pickaxe = toolController != null
                ? toolController.GetDefinition(PlayerInventoryItem.Pickaxe)
                : null;
            bool available = HasAvailableMagnetTarget(pickaxe);
            if (force)
                targetAvailabilityInitialized = false;
            SetTargetAvailability(available);
        }

        private void SetTargetAvailability(bool available)
        {
            if (targetAvailabilityInitialized
                && targetAvailable == available)
            {
                return;
            }

            targetAvailabilityInitialized = true;
            targetAvailable = available;
            TargetAvailabilityChanged?.Invoke(available);
        }

        /// <summary>
        /// Starts a magnet action. <paramref name="pickaxeDefinition"/> supplies the
        /// tuning used when the acquired target is a thrown pickaxe; pass null to
        /// disable pickaxe retrieval for this action.
        /// </summary>
        public bool BeginAttraction(
            PlayerToolDefinition pickaxeDefinition = null)
        {
            ResolveReferences();
            if (!isActiveAndEnabled || !CanOperate)
            {
                EndAttraction();
                return false;
            }
            magnetActionActive = true;
            pickaxePullDefinition = pickaxeDefinition;
            if (heldBody == null && towedPickaxe == null)
            {
                ReleaseCaughtCreature();
                AcquireMagnetTargetOrThrownPickaxe();
            }
            return true;
        }

        public void TickAttraction()
        {
            ResolveReferences();
            if (!magnetActionActive || !isActiveAndEnabled || !CanOperate)
            {
                EndAttraction();
                return;
            }

            if (towedPickaxe != null)
            {
                TickPickaxePull(Time.deltaTime);
                return;
            }

            if (heldBody == null)
            {
                ReleaseCaughtCreature();
                AcquireMagnetTargetOrThrownPickaxe();
            }
        }

        /// <summary>
        /// A thrown pickaxe is too heavy to reel in, so the magnet drags the player
        /// towards it instead. Walking into it picks it back up.
        /// </summary>
        private void TickPickaxePull(float deltaTime)
        {
            if (towedPickaxe == null) return;
            if (!towedPickaxe.CanBeRecovered
                || pickaxePullDefinition == null)
            {
                ReleasePickaxePull();
                return;
            }

            Vector3 playerPosition = GetPlayerPullOrigin();
            if (towedPickaxe.IsReturning)
            {
                // The pickaxe is flying home under its own power now; stop dragging
                // the player and let its recall flight finish.
                ReleasePickaxePull();
                return;
            }
            // Reaching the pickaxe no longer recalls it. Swinging in close is a normal
            // part of using the rope, so recall is left to the throw key.

            if (Vector3.Distance(playerPosition, towedPickaxe.Position)
                > pickaxePullDefinition.PickaxeMagnetRange)
            {
                ReleasePickaxePull();
                return;
            }

            // Aim and sightline are only required to START the pull. Once latched, the
            // hold lasts until right click is released, so the player can look around
            // and swing through corners without the rope dropping out.
            TickRope(deltaTime, playerPosition);
        }

        /// <summary>
        /// Drives the rope for one frame: reel, then jolt on the frame it goes taut,
        /// then constrain, then let the movement keys pump the swing.
        /// </summary>
        private void TickRope(float deltaTime, Vector3 playerPosition)
        {
            if (playerMotorOwner == null) return;

            Vector3 anchor = towedPickaxe.Position;
            float distance = Vector3.Distance(playerPosition, anchor);
            if (!ropeAttached)
            {
                // Attach at the current distance so the rope never teleports the
                // player, then winch in from there.
                ropeLength = distance;
                ropeAttached = true;
                ropeWasTaut = false;
            }

            float previousLength = ropeLength;
            // Scroll steps arrive as whole metres. Spend them at the manual reel rate
            // instead of all at once, otherwise a single click would demand a metre of
            // travel inside one frame and launch the player at the speed cap.
            float manualStep = 0f;
            if (Mathf.Abs(ropeReelRequest) > 0.0001f)
            {
                float budget =
                    pickaxePullDefinition.RopeManualReelSpeed * deltaTime;
                manualStep = Mathf.Clamp(ropeReelRequest, -budget, budget);
                ropeReelRequest -= manualStep;
            }
            float reelMetres =
                pickaxePullDefinition.RopeReelInSpeed * deltaTime + manualStep;
            ropeLength = RopeConstraint.ApplyReel(
                ropeLength,
                reelMetres,
                1f,
                1f,
                pickaxePullDefinition.RopeMinimumLength,
                pickaxePullDefinition.PickaxeMagnetRange);
            // The player closing in on the anchor takes up slack: the rope can never
            // span more than the current distance, or it would stop going taut once
            // the winch had drawn the player inside its length.
            ropeLength = Mathf.Min(ropeLength, distance);

            Vector3 anchorToPlayer = playerPosition - anchor;
            // The rope is only taut when the player is actually at full extension.
            // Comparing against the reeled length instead would report taut forever
            // once the winch shortened past the real distance, and the constraint
            // would then fight gravity every frame.
            bool taut = distance >= ropeLength - RopeTautTolerance;
            if (taut && !ropeWasTaut)
            {
                // One-shot jolt so the catch reads as a rope snapping tight rather
                // than a gradual pull starting up.
                Vector3 yank = RopeConstraint.CalculateYankImpulse(
                    playerMotorOwner.CombinedVelocity,
                    anchorToPlayer,
                    pickaxePullDefinition.RopeYankStrength,
                    pickaxePullDefinition.RopeMaximumYankSpeed);
                playerMotorOwner.AddExternalVelocity(
                    yank,
                    pickaxePullDefinition.RopeMaximumSpeed);
            }
            ropeWasTaut = taut;

            // Shortening the rope has to actually haul the player in. The constraint
            // alone only removes outward velocity; it can never create inward motion,
            // so without this the winch would shorten the rope and nothing would move.
            float shortenedBy = previousLength - ropeLength;
            if (shortenedBy > 0f && distance > 0.0001f && deltaTime > 0f)
            {
                // Set the inward speed rather than adding to it. Adding every frame
                // would compound into a runaway launch towards the anchor.
                Vector3 inward = -anchorToPlayer / distance;
                // Never exceed the rate the rope is actually being taken in at, so a
                // reel feels like a winch rather than a catapult.
                float maximumReelSpeed = Mathf.Max(
                    pickaxePullDefinition.RopeReelInSpeed,
                    pickaxePullDefinition.RopeManualReelSpeed);
                float reelSpeed = Mathf.Min(
                    shortenedBy / deltaTime,
                    maximumReelSpeed);
                Vector3 velocity = playerMotorOwner.CombinedVelocity;
                float currentInward = Vector3.Dot(velocity, inward);
                if (currentInward < reelSpeed)
                {
                    playerMotorOwner.AddExternalVelocity(
                        inward * (reelSpeed - currentInward),
                        pickaxePullDefinition.RopeMaximumSpeed);
                }
            }

            // The constraint itself: cancels only outward radial motion, so the
            // tangential component survives and the player swings.
            playerMotorOwner.ApplyRopeConstraint(anchor, ropeLength);

            // Ease movement intent in and out before turning it into thrust. This
            // prevents a key press or direction reversal from kicking the player
            // around the arc at full acceleration in a single frame.
            float inputBlend = 1f - Mathf.Exp(
                -pickaxePullDefinition.RopeSwingInputResponse
                * Mathf.Max(0f, deltaTime));
            ropeSwingInput = Vector3.Lerp(
                ropeSwingInput,
                ropeSwingInputTarget,
                inputBlend);

            // Movement keys become swing thrust along the arc. Pushing towards or
            // away from the anchor does nothing, because a rope cannot be pushed
            // along its own length.
            if (ropeSwingInput.sqrMagnitude > 0.0001f)
            {
                Vector3 thrust = RopeConstraint.CalculateSwingThrust(
                    ropeSwingInput,
                    anchorToPlayer,
                    pickaxePullDefinition.RopeSwingAcceleration);
                playerMotorOwner.AddExternalAcceleration(
                    thrust,
                    deltaTime,
                    pickaxePullDefinition.RopeMaximumSpeed);
            }
            ropeSwingInputTarget = Vector3.zero;
        }

        /// <summary>
        /// Feeds this frame's movement intent in as swing thrust. The player controller
        /// calls this because it owns the camera-relative movement basis.
        /// </summary>
        public void SetRopeSwingInput(Vector3 worldDirection)
        {
            ropeSwingInputTarget = Vector3.ClampMagnitude(worldDirection, 1f);
        }

        /// <summary>Scroll input for reeling the rope in and out this frame.</summary>
        public void RequestRopeReel(float metres)
        {
            ropeReelRequest += metres;
        }

        public bool IsRopeTaut => ropeAttached && ropeWasTaut;
        public float RopeLength => ropeLength;

        private void ReleasePickaxePull()
        {
            if (towedPickaxe == null) return;
            towedPickaxe = null;
            ropeAttached = false;
            ropeWasTaut = false;
            ropeReelRequest = 0f;
            ropeSwingInput = Vector3.zero;
            ropeSwingInputTarget = Vector3.zero;
            // Keep the swing momentum instead of zeroing it: letting go at the bottom
            // of an arc should fling the player, which is the payoff for swinging.
            if (playerMotorOwner == null) return;

            float keep = pickaxePullDefinition != null
                ? pickaxePullDefinition.RopeReleaseMomentum
                : 1f;
            if (keep <= 0f)
            {
                playerMotorOwner.ClearExternalVelocity();
                return;
            }
            if (keep < 1f)
            {
                playerMotorOwner.SetCombinedVelocity(
                    playerMotorOwner.CombinedVelocity * keep);
            }
        }

        private bool AcquireMagnetTargetOrThrownPickaxe()
        {
            return TryAcquireMagnetTarget()
                || TryAcquireThrownPickaxe();
        }

        /// <summary>
        /// Looks for the pickaxe the player threw. Unlike ordinary magnet targets it
        /// can be latched from far away and does not need an unobstructed sightline,
        /// so a pickaxe lost behind terrain is still recoverable.
        /// </summary>
        private bool TryAcquireThrownPickaxe()
        {
            if (!TryFindThrownPickaxe(
                    pickaxePullDefinition,
                    out ThrownPickaxe best))
            {
                return false;
            }

            towedPickaxe = best;
            return true;
        }

        /// <summary>
        /// Finds the recoverable thrown pickaxe the player is looking closest to,
        /// without latching onto it.
        /// </summary>
        private bool TryFindThrownPickaxe(
            PlayerToolDefinition pickaxeDefinition,
            out ThrownPickaxe best)
        {
            best = null;
            if (pickaxeDefinition == null || viewCamera == null) return false;

            Transform cameraTransform = viewCamera.transform;
            float range = pickaxeDefinition.PickaxeMagnetRange;
            // Require the pickaxe to be near the crosshair. A forward-hemisphere test
            // would accept anything on screen, so the player could not choose which
            // pickaxe to pull or aim away to stop pulling.
            float minimumAlignment = Mathf.Cos(
                pickaxeDefinition.PickaxeMagnetAimAngle * Mathf.Deg2Rad);
            float bestScore = float.PositiveInfinity;
            foreach (ThrownPickaxe candidate
                in ThrownPickaxe.ActiveInstances)
            {
                if (candidate == null || !candidate.CanBeRecovered) continue;

                Vector3 toPickaxe = candidate.Position - cameraTransform.position;
                float distance = toPickaxe.magnitude;
                if (distance > range || distance <= 0.0001f) continue;

                // Prefer whatever the player is looking closest to.
                float alignment = Vector3.Dot(
                    toPickaxe / distance,
                    cameraTransform.forward);
                if (alignment < minimumAlignment) continue;

                // The pickaxe has to be genuinely visible, not merely aimed at
                // through a wall.
                if (!HasClearPickaxeSightline(candidate)) continue;

                float score = (1f - alignment) * range + distance * 0.01f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best != null;
        }

        public void TickAttraction(float scrollSteps)
        {
            AdjustHoldDistance(scrollSteps);
            TickAttraction();
        }

        public void AdjustHoldDistance(float scrollSteps)
        {
            if (Mathf.Abs(scrollSteps) <= 0.001f) return;
            float minimum = Mathf.Max(0.2f, minimumHoldDistance);
            float maximum = Mathf.Max(minimum, maximumHoldDistance);
            holdDistance = Mathf.Clamp(
                holdDistance + scrollSteps * Mathf.Max(0f, scrollDistancePerStep),
                minimum,
                maximum);
        }

        public void EndAttraction()
        {
            magnetActionActive = false;
            hasHeldTargetRotation = false;
            pickaxePullDefinition = null;
            ReleasePickaxePull();
            Release();
        }

        private void FixedUpdate()
        {
            if (heldBody == null)
            {
                ReleaseCaughtCreature();
                return;
            }
            if (!magnetActionActive)
            {
                return;
            }
            if (!CanOperate || heldBody.isKinematic)
            {
                EndAttraction();
                return;
            }

            Vector3 desiredPosition = CalculateDesiredHoldPosition();
            Vector3 handlePosition = heldBody.worldCenterOfMass;

            Vector3 error = desiredPosition - handlePosition;
            if (error.sqrMagnitude > breakDistance * breakDistance)
            {
                EndAttraction();
                return;
            }

            Vector3 targetVelocity = playerController != null
                ? playerController.velocity
                : Vector3.zero;
            Vector3 bodyVelocity = heldBody.velocity;
            Vector3 relativeVelocity = targetVelocity - bodyVelocity;
            Vector3 force = CalculateMagnetAttractionForce(
                error,
                relativeVelocity,
                heldBody.worldCenterOfMass.y,
                heldBody.mass);
            heldBody.AddForceAtPosition(force, handlePosition, ForceMode.Force);

            if (hasHeldTargetRotation)
            {
                heldBody.AddTorque(
                    CalculateOrientationTorque(
                        heldBody.rotation,
                        heldBody.angularVelocity),
                    ForceMode.Acceleration);
            }
        }

        private Vector3 CalculateMagnetAttractionForce(
            Vector3 positionError,
            Vector3 relativeVelocity,
            float currentBodyHeight,
            float bodyMass)
        {
            float spring = Mathf.Max(0f, positionSpring);
            float damping = Mathf.Max(0f, forceDamping);
            Vector3 force = positionError * spring
                + relativeVelocity * damping;
            float maximumLiftForce = CalculateMaximumLiftForce(
                currentBodyHeight);
            float requestedVerticalSpring = positionError.y * spring;
            if (requestedVerticalSpring > maximumLiftForce)
            {
                // Once lift is saturated, normal damping would be clipped away by
                // the lift cap. Retain only the part that removes upward kinetic
                // energy, and never let damping exceed the available lift while
                // the object is falling.
                float upwardMotionDamping = Mathf.Min(
                    0f,
                    relativeVelocity.y * damping);
                force.y = maximumLiftForce + upwardMotionDamping;
            }

            force = Vector3.ClampMagnitude(
                force,
                AttractionForce);
            force = Vector3.ClampMagnitude(
                force,
                Mathf.Max(0f, bodyMass)
                    * Mathf.Max(0f, maximumAttractionAcceleration));
            return LimitMagnetLiftForce(force, currentBodyHeight);
        }

        private Vector3 LimitMagnetLiftForce(
            Vector3 force,
            float currentBodyHeight)
        {
            float maximumLiftForce = CalculateMaximumLiftForce(
                currentBodyHeight);
            if (force.y > maximumLiftForce)
            {
                force.y = maximumLiftForce;
            }
            return force;
        }

        private float CalculateMaximumLiftForce(float currentBodyHeight)
        {
            float liftedHeight = Mathf.Max(
                0f,
                currentBodyHeight - magnetPickupHeight);
            float falloff = Mathf.Max(0f, liftForceFalloffPerMeter);
            return Mathf.Max(0f, baseMaximumLiftForce)
                / (1f + liftedHeight * falloff);
        }

        private Vector3 CalculateOrientationTorque(
            Quaternion currentRotation,
            Vector3 angularVelocity)
        {
            Quaternion error = heldTargetRotation
                * Quaternion.Inverse(currentRotation);
            error.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (axis.sqrMagnitude < 0.0001f || float.IsNaN(axis.x))
            {
                axis = Vector3.zero;
            }

            Vector3 torque = axis.normalized
                * (angle * Mathf.Deg2Rad * Mathf.Max(0f, orientationSpring))
                - angularVelocity * Mathf.Max(0f, orientationDamping);
            return Vector3.ClampMagnitude(
                torque,
                Mathf.Max(0f, maximumOrientationTorque));
        }

        private void UpdateHeldTargetRotation(float mouseX, float mouseY)
        {
            if (!hasHeldTargetRotation || viewCamera == null) return;

            float horizontal = Mathf.Abs(mouseX);
            float vertical = Mathf.Abs(mouseY);
            float degrees = Mathf.Max(0f, rotationDegreesPerMouseUnit);
            // One dominant axis per frame produces four deliberate directions:
            // left/right yaw and up/down pitch, without accidental roll.
            if (horizontal >= vertical && horizontal > 0.001f)
            {
                heldTargetRotation = Quaternion.AngleAxis(
                    mouseX * degrees,
                    viewCamera.transform.up) * heldTargetRotation;
            }
            else if (vertical > 0.001f)
            {
                heldTargetRotation = Quaternion.AngleAxis(
                    -mouseY * degrees,
                    viewCamera.transform.right) * heldTargetRotation;
            }
        }

        public void SetDeviceEnabled(bool value)
        {
            deviceEnabled = value;
            if (!value) EndAttraction();
        }

        public void Release()
        {
            ReleaseCaughtCreature();
            heldBody = null;
            heldValuableObject = null;
            magnetPickupHeight = 0f;
        }

        private bool TryAcquireMagnetTarget()
        {
            if (!TryFindMagnetTarget(out Rigidbody focusedBody)) return false;

            CaptureMagnetTarget(focusedBody);
            return true;
        }

        /// <summary>
        /// Resolves what the magnet would grab right now without taking hold of it.
        /// The crosshair uses this to show when a pull is actually available.
        /// </summary>
        public bool TryFindMagnetTarget(out Rigidbody focusedBody)
        {
            focusedBody = null;
            ResolveReferences();
            if (viewCamera == null) return false;

            Transform cameraTransform = viewCamera.transform;
            int count = Physics.RaycastNonAlloc(
                cameraTransform.position,
                cameraTransform.forward,
                acquisitionHits,
                Mathf.Max(0.1f, acquisitionDistance),
                targetLayers,
                QueryTriggerInteraction.Ignore);

            float focusedHitDistance = float.PositiveInfinity;
            Collider focusedCollider = null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = acquisitionHits[i];
                Collider collider = hit.collider;
                if (collider == null || IsOwnedByPlayer(collider.transform))
                {
                    continue;
                }
                if (hit.distance >= focusedHitDistance) continue;

                focusedHitDistance = hit.distance;
                focusedCollider = collider;
            }

            Rigidbody directBody = focusedCollider != null
                ? focusedCollider.attachedRigidbody
                : null;
            if (IsValidMagnetTarget(directBody))
            {
                focusedBody = directBody;
                return true;
            }

            return TryFindMagnetTargetNearSightline(
                cameraTransform,
                out focusedBody);
        }

        /// <summary>
        /// Whether right click would currently latch onto something: either an
        /// ordinary body within reach, or a thrown pickaxe to be pulled towards.
        /// </summary>
        public bool HasAvailableMagnetTarget(
            PlayerToolDefinition pickaxeDefinition = null)
        {
            if (!isActiveAndEnabled || !CanOperate) return false;
            // Already holding or pulling: the crosshair should stay highlighted.
            if (heldBody != null || towedPickaxe != null) return true;
            if (TryFindMagnetTarget(out _)) return true;

            return pickaxeDefinition != null
                && TryFindThrownPickaxe(pickaxeDefinition, out _);
        }

        private bool TryFindMagnetTargetNearSightline(
            Transform cameraTransform,
            out Rigidbody focusedBody)
        {
            focusedBody = null;
            float maximumDistance = Mathf.Max(0.1f, acquisitionDistance);
            float bestScore = float.PositiveInfinity;
            Vector3 searchCenter = cameraTransform.position
                + cameraTransform.forward * (maximumDistance * 0.5f);
            float searchRadius = maximumDistance * 0.5f
                + MagnetAimAssistRadius;
            int colliderCount = Physics.OverlapSphereNonAlloc(
                searchCenter,
                searchRadius,
                aimAssistColliders,
                targetLayers,
                QueryTriggerInteraction.Ignore);

            aimAssistBodies.Clear();
            for (int colliderIndex = 0;
                colliderIndex < colliderCount;
                colliderIndex++)
            {
                Collider collider = aimAssistColliders[colliderIndex];
                if (collider != null && collider.attachedRigidbody != null)
                    aimAssistBodies.Add(collider.attachedRigidbody);
            }

            foreach (Rigidbody body in aimAssistBodies)
            {
                if (!IsValidMagnetTarget(body)
                    || !TryGetMagnetTargetBounds(body, out Bounds bounds))
                {
                    continue;
                }

                Vector3 toCenter = bounds.center - cameraTransform.position;
                float forwardDistance = Vector3.Dot(
                    toCenter,
                    cameraTransform.forward);
                if (forwardDistance <= 0f
                    || forwardDistance > maximumDistance)
                {
                    continue;
                }

                Vector3 sightlinePoint = cameraTransform.position
                    + cameraTransform.forward * forwardDistance;
                float lateralDistanceSquared =
                    bounds.SqrDistance(sightlinePoint);
                if (lateralDistanceSquared
                    > MagnetAimAssistRadius * MagnetAimAssistRadius)
                {
                    continue;
                }

                float surfaceDistance = Vector3.Distance(
                    cameraTransform.position,
                    bounds.ClosestPoint(cameraTransform.position));
                if (surfaceDistance > maximumDistance
                    || !HasClearMagnetLineOfSight(
                        cameraTransform.position,
                        bounds.center,
                        body))
                {
                    continue;
                }

                float score = lateralDistanceSquared * 4f
                    + surfaceDistance * 0.01f;
                if (score < bestScore)
                {
                    bestScore = score;
                    focusedBody = body;
                }
            }

            return focusedBody != null;
        }

        private bool TryGetMagnetTargetBounds(
            Rigidbody body,
            out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            targetColliderBuffer.Clear();
            body.GetComponentsInChildren(true, targetColliderBuffer);
            for (int i = 0; i < targetColliderBuffer.Count; i++)
            {
                Collider collider = targetColliderBuffer[i];
                if (collider == null
                    || !collider.enabled
                    || collider.isTrigger
                    || (targetLayers.value & (1 << collider.gameObject.layer)) == 0)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = collider.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return found;
        }

        private bool HasClearMagnetLineOfSight(
            Vector3 origin,
            Vector3 target,
            Rigidbody targetBody)
        {
            return HasClearLineOfSight(
                origin,
                target,
                targetBody != null ? targetBody.transform : null,
                targetBody);
        }

        /// <summary>
        /// Whether nothing blocks the straight line from <paramref name="origin"/> to
        /// <paramref name="target"/>. Colliders belonging to the player or to the
        /// target itself are ignored.
        /// </summary>
        private bool HasClearLineOfSight(
            Vector3 origin,
            Vector3 target,
            Transform targetRoot,
            Rigidbody targetBody)
        {
            Vector3 offset = target - origin;
            float distance = offset.magnitude;
            if (distance <= 0.001f)
            {
                return true;
            }

            int count = Physics.RaycastNonAlloc(
                origin,
                offset / distance,
                acquisitionHits,
                distance,
                targetLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = acquisitionHits[i].collider;
                if (collider == null
                    || IsOwnedByPlayer(collider.transform)
                    || BelongsToBody(collider, targetBody)
                    || (targetRoot != null
                        && collider.transform.IsChildOf(targetRoot))
                    || acquisitionHits[i].distance
                        >= distance - MagnetOcclusionTolerance)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether the player can actually see <paramref name="pickaxe"/>. A pickaxe
        /// buried behind terrain must not be latchable even when it is inside the aim
        /// cone and within range.
        /// </summary>
        private bool HasClearPickaxeSightline(ThrownPickaxe pickaxe)
        {
            if (pickaxe == null || viewCamera == null) return false;

            return HasClearLineOfSight(
                viewCamera.transform.position,
                pickaxe.VisiblePosition,
                pickaxe.transform,
                pickaxe.Body);
        }

        private bool IsValidMagnetTarget(Rigidbody body)
        {
            return body != null
                && !body.isKinematic
                && body.gameObject.activeInHierarchy
                && !IsOwnedByPlayer(body.transform)
                // A pickaxe still in flight is a plain dynamic body. It must fall
                // through to the pull path instead of being reeled into the view.
                && body.GetComponentInChildren<ThrownPickaxe>(true) == null;
        }

        private void CaptureMagnetTarget(Rigidbody body)
        {
            ReleaseCaughtCreature();
            heldBody = body;
            heldCreature = FindCreature(body);
            heldCreature?.SetCaught(true);
            ResolveHeldValuableObject();
            heldTargetRotation = body.rotation;
            hasHeldTargetRotation = true;
            magnetPickupHeight = body.worldCenterOfMass.y;
            // Hold the object where it already is instead of at whatever distance the
            // previous grab happened to end on. A persisted distance yanks the object
            // towards or away from the player the instant it is grabbed.
            holdDistance = CalculateInitialHoldDistance(body);
            body.WakeUp();
        }

        /// <summary>
        /// The distance the object currently sits at, clamped into the magnet's working
        /// range, so grabbing something never moves it.
        /// </summary>
        private float CalculateInitialHoldDistance(Rigidbody body)
        {
            if (body == null || viewCamera == null) return holdDistance;

            float minimum = Mathf.Max(0.2f, minimumHoldDistance);
            float maximum = Mathf.Max(minimum, maximumHoldDistance);
            // Measure along the view direction, because that is the axis the hold
            // point is placed on.
            Vector3 toBody =
                body.worldCenterOfMass - viewCamera.transform.position;
            float forwardDistance = Vector3.Dot(
                toBody,
                viewCamera.transform.forward);
            return Mathf.Clamp(forwardDistance, minimum, maximum);
        }

        private static CreatureBehaviorAgent FindCreature(Rigidbody body)
        {
            if (body == null)
            {
                return null;
            }

            CreatureBehaviorAgent creature =
                body.GetComponent<CreatureBehaviorAgent>();
            if (creature == null)
            {
                creature = body.GetComponentInParent<CreatureBehaviorAgent>();
            }
            if (creature == null)
            {
                creature =
                    body.GetComponentInChildren<CreatureBehaviorAgent>(true);
            }
            return creature;
        }

        private void ReleaseCaughtCreature()
        {
            if (heldCreature != null)
            {
                heldCreature.SetCaught(false);
            }
            heldCreature = null;
        }

        private void ResolveHeldValuableObject()
        {
            heldValuableObject = heldBody != null
                ? heldBody.GetComponent<ValuableObject>()
                : null;
            if (heldValuableObject == null)
            {
                heldValuableObject =
                    heldBody != null
                        ? heldBody.GetComponentInChildren<ValuableObject>(true)
                        : null;
            }
        }

        private static bool BelongsToBody(Collider collider, Rigidbody body)
        {
            return collider != null
                && body != null
                && (collider.attachedRigidbody == body
                    || collider.transform == body.transform
                    || collider.transform.IsChildOf(body.transform));
        }

        private bool CanOperateInFirstPerson => perspectiveCamera != null
            && perspectiveCamera.CurrentMode == PlayerViewMode.FirstPerson
            && viewCamera != null
            && playerRoot != null;


        private bool IsOwnedByPlayer(Transform candidate)
        {
            return playerRoot != null
                && (candidate == playerRoot || candidate.IsChildOf(playerRoot));
        }

        private Vector3 GetPlayerPullOrigin()
        {
            if (playerController != null) return playerController.bounds.center;
            return playerRoot != null ? playerRoot.position : transform.position;
        }

        private void ResolveReferences()
        {
            if (playerRoot == null) playerRoot = transform;
            if (playerController == null)
                playerController = playerRoot.GetComponent<CharacterController>();
            if (playerMotorOwner == null)
                playerMotorOwner = playerRoot.GetComponent<VoxelPlayerController>();
            if (toolController == null)
                toolController = playerRoot.GetComponent<PlayerToolController>();
            if (perspectiveCamera == null)
                perspectiveCamera = playerRoot.GetComponentInChildren<PerspectiveCameraController>(true);
            if (viewCamera == null && perspectiveCamera != null)
                viewCamera = perspectiveCamera.ControlledCamera;
            if (viewCamera == null)
                viewCamera = playerRoot.GetComponentInChildren<Camera>(true);
        }

        private void OnDisable()
        {
            SetTargetAvailability(false);
            InstanceDisabled?.Invoke(this);
            EndAttraction();
        }

        private void OnValidate()
        {
            minimumHoldDistance = Mathf.Max(0.2f, minimumHoldDistance);
            maximumHoldDistance = Mathf.Max(minimumHoldDistance, maximumHoldDistance);
            holdDistance = Mathf.Clamp(holdDistance, minimumHoldDistance, maximumHoldDistance);
            scrollDistancePerStep = Mathf.Max(0f, scrollDistancePerStep);
            attractionForce = Mathf.Max(0f, attractionForce);
            maximumAttractionAcceleration =
                Mathf.Max(0f, maximumAttractionAcceleration);
            positionSpring = Mathf.Max(0f, positionSpring);
            forceDamping = Mathf.Max(0f, forceDamping);
            orientationSpring = Mathf.Max(0f, orientationSpring);
            orientationDamping = Mathf.Max(0f, orientationDamping);
            maximumOrientationTorque = Mathf.Max(0f, maximumOrientationTorque);
            rotationDegreesPerMouseUnit =
                Mathf.Max(0f, rotationDegreesPerMouseUnit);
            baseMaximumLiftForce = Mathf.Max(0f, baseMaximumLiftForce);
            liftForceFalloffPerMeter =
                Mathf.Max(0f, liftForceFalloffPerMeter);
            breakDistance = Mathf.Max(0.5f, breakDistance);
        }

        private Vector3 CalculateDesiredHoldPosition()
        {
            return viewCamera.transform.position
                + viewCamera.transform.forward.normalized
                * Mathf.Max(0.2f, holdDistance);
        }
    }
}
