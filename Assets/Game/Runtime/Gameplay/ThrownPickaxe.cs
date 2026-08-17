using System.Collections.Generic;
using Supernova.Audio;
using Supernova.Effects;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// A pickaxe in flight after a right-click throw. It tumbles while airborne,
    /// then buries its head in the first surface it touches and stays there until
    /// the player walks up to it or drags it back with the magnet.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(RigidbodyImpactFeedback))]
    public sealed class ThrownPickaxe : MonoBehaviour
    {
        private static readonly HashSet<ThrownPickaxe> Active =
            new HashSet<ThrownPickaxe>();

        public enum ThrownPickaxeState
        {
            Idle = 0,
            Flying = 1,
            Pinned = 2,
            Returning = 3,
        }

        private const string SpinStateName = "Spin";
        private const string PinnedStateName = "Pinned";
        private const float SelfCollisionGrace = 0.12f;
        /// <summary>
        /// Distance down the shaft, away from the buried head, used as the point that
        /// has to be visible for the magnet to latch on.
        /// </summary>
        private const float VisibleHandleOffset = 0.45f;
        /// <summary>Head-to-butt axis of the authored pickaxe mesh.</summary>
        private static readonly Vector3 DefaultShaftLocalDirection = Vector3.down;
        /// <summary>Head spike axis of the authored pickaxe mesh.</summary>
        private static readonly Vector3 DefaultSpikeLocalDirection = Vector3.left;

        [SerializeField] private Rigidbody body;
        [Tooltip("Child transform driven by the spin and pinned clips.")]
        [SerializeField] private Transform spinPivot;
        [SerializeField] private Animator spinAnimator;
        [Tooltip("Head tip in root local space, measured with the spin pivot at rest.")]
        [SerializeField] private Vector3 headTipLocalPosition = Vector3.right * 0.45f;
        [Tooltip("Direction the head tip points in root local space.")]
        [SerializeField] private Vector3 headTipLocalDirection = Vector3.right;
        [Tooltip("Direction from the head towards the handle butt, in root local space.")]
        [SerializeField] private Vector3 shaftLocalDirection = Vector3.down;
        [Tooltip("How deep the head sinks into the surface it strikes.")]
        [SerializeField, Min(0f)] private float pinDepth = 0.12f;
        [Tooltip("Shallowest angle, in degrees off the struck surface, the spike is allowed to embed at. Higher values bite in more steeply and never lie flat.")]
        [SerializeField, Range(0f, 90f)] private float minimumBiteAngle = 35f;
        [Tooltip("Seconds the authored wobble clip plays after contact before the pickaxe freezes.")]
        [SerializeField, Min(0f)] private float pinSettleDuration = 0.45f;
        [Tooltip("Range at which the player counts as being at the pickaxe. Informational only: recall is triggered by the throw key, not by walking close.")]
        [SerializeField, Min(0.1f)] private float pickupDistance = 1.6f;

        [Header("Recall Flight")]
        [Tooltip("Speed at which a recalled pickaxe flies towards the player.")]
        [SerializeField, Min(0.1f)] private float recallSpeed = 9f;
        [Tooltip("Extra acceleration applied while the recall flight closes in.")]
        [SerializeField, Min(0f)] private float recallAcceleration = 26f;
        [Tooltip("Distance from the player at which the recall completes.")]
        [SerializeField, Min(0.05f)] private float recallAbsorbDistance = 0.45f;
        [Tooltip("Revolutions per second while flying back to the player.")]
        [SerializeField, Min(0f)] private float recallSpinRevolutions = 3.2f;
        [Tooltip("Safety timeout so a blocked recall can never strand the pickaxe.")]
        [SerializeField, Min(0.2f)] private float recallTimeout = 4f;

        private PlayerToolController owner;
        private Transform ownerTransform;
        private PlayerInventoryItem suspendedItem = PlayerInventoryItem.Pickaxe;
        private ThrownPickaxeState state;
        private float launchTime;
        private float pinTime;
        private bool pinFrozen;
        private float recallStartTime;
        private Vector3 lastFlightVelocity;
        private float recallCurrentSpeed;
        private SoundEffectCue terrainImpactSound;

        public ThrownPickaxeState State => state;
        public bool IsFlying => state == ThrownPickaxeState.Flying;
        public bool IsPinned => state == ThrownPickaxeState.Pinned;
        public bool IsReturning => state == ThrownPickaxeState.Returning;
        public bool CanBeRecovered => state != ThrownPickaxeState.Idle;
        public Vector3 HeadTipLocalPosition => headTipLocalPosition;
        public Vector3 HeadTipLocalDirection =>
            headTipLocalDirection.sqrMagnitude > 0.0001f
                ? headTipLocalDirection.normalized
                : Vector3.right;
        public float PinDepth => Mathf.Max(0f, pinDepth);
        public Vector3 ShaftLocalDirection =>
            shaftLocalDirection.sqrMagnitude > 0.0001f
                ? shaftLocalDirection.normalized
                : DefaultShaftLocalDirection;
        public float MinimumBiteAngle => Mathf.Clamp(minimumBiteAngle, 0f, 90f);
        public float PinSettleDuration => Mathf.Max(0f, pinSettleDuration);
        public float PickupDistance => Mathf.Max(0.1f, pickupDistance);
        public float RecallSpeed => Mathf.Max(0.1f, recallSpeed);
        public float RecallAbsorbDistance => Mathf.Max(0.05f, recallAbsorbDistance);
        public PlayerInventoryItem SuspendedItem => suspendedItem;
        public Rigidbody Body => ResolveBody();
        public Vector3 Position => transform.position;
        public static IEnumerable<ThrownPickaxe> ActiveInstances => Active;
        /// <summary>
        /// A point on the exposed handle rather than the root, which sits near the
        /// buried head. Sightline checks must aim here or the terrain the pickaxe is
        /// embedded in would always count as blocking it.
        /// </summary>
        public Vector3 VisiblePosition =>
            transform.position
            + transform.rotation * (ShaftLocalDirection * VisibleHandleOffset);

        private void Awake()
        {
            ResolveReferences();
            RigidbodyImpactFeedback.Ensure(Body);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            Active.Clear();
        }

        private void OnEnable()
        {
            Active.Add(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }

        /// <summary>
        /// Sends the pickaxe on its way. <paramref name="toolOwner"/> receives the
        /// item back when the throw is recovered.
        /// </summary>
        public void Launch(
            Vector3 velocity,
            PlayerToolController toolOwner,
            PlayerInventoryItem item,
            float spinRevolutionsPerSecond,
            float configuredPickupDistance = -1f,
            SoundEffectCue configuredTerrainImpactSound = null)
        {
            ResolveReferences();
            owner = toolOwner;
            ownerTransform = toolOwner != null ? toolOwner.transform : null;
            suspendedItem = item;
            terrainImpactSound = configuredTerrainImpactSound;
            if (configuredPickupDistance > 0f)
                pickupDistance = configuredPickupDistance;

            Vector3 direction = velocity.sqrMagnitude > 0.0001f
                ? velocity.normalized
                : transform.forward;
            transform.rotation = CalculateFlightRotation(
                direction,
                HeadTipLocalDirection);
            if (spinPivot != null)
                spinPivot.localRotation = Quaternion.identity;

            Rigidbody resolvedBody = ResolveBody();
            resolvedBody.isKinematic = false;
            resolvedBody.useGravity = true;
            resolvedBody.velocity = velocity;
            resolvedBody.angularVelocity = Vector3.zero;
            resolvedBody.WakeUp();

            state = ThrownPickaxeState.Flying;
            pinFrozen = false;
            launchTime = Time.time;
            lastFlightVelocity = velocity;
            PlaySpin(spinRevolutionsPerSecond);
        }

        /// <summary>
        /// Orients the pickaxe so its head tip is buried at
        /// <paramref name="contactPoint"/> along <paramref name="travelDirection"/>,
        /// then parents it to whatever it struck so it rides along.
        /// </summary>
        public void Pin(
            Vector3 contactPoint,
            Vector3 travelDirection,
            Vector3 surfaceNormal,
            Transform attachTo)
        {
            ResolveReferences();
            bool playTerrainImpact = state == ThrownPickaxeState.Flying;
            Vector3 inward = CalculateEmbedDirection(
                travelDirection,
                surfaceNormal,
                MinimumBiteAngle);
            Quaternion pinnedRotation = CalculatePinRotation(
                HeadTipLocalDirection,
                inward,
                ShaftLocalDirection,
                surfaceNormal);
            Vector3 pinnedPosition = contactPoint
                + inward * PinDepth
                - pinnedRotation * headTipLocalPosition;

            Rigidbody resolvedBody = ResolveBody();
            // Clear the momentum before going kinematic; a kinematic body rejects
            // velocity writes and logs a warning.
            if (!resolvedBody.isKinematic)
            {
                resolvedBody.velocity = Vector3.zero;
                resolvedBody.angularVelocity = Vector3.zero;
            }
            resolvedBody.useGravity = false;
            resolvedBody.isKinematic = true;
            // Interpolation renders a blend between the previous and current physics
            // poses. Left on, the teleport below shows one frame part-way between the
            // tumbling flight pose and the buried pose, which flashes an orientation
            // the pickaxe never actually holds.
            resolvedBody.interpolation = RigidbodyInterpolation.None;

            // Stop the spin before moving the root. The animator writes the pivot in
            // its own update pass, so a stale spin angle would otherwise survive one
            // more frame on top of the new pinned pose.
            StopSpinAnimation();

            // Snap straight to the buried pose. Interpolating towards it instead
            // would slide the whole pickaxe across the surface for the duration of
            // the blend, which reads as the wrong end going in first.
            transform.SetPositionAndRotation(pinnedPosition, pinnedRotation);

            if (attachTo != null && attachTo != transform)
                transform.SetParent(attachTo, true);

            state = ThrownPickaxeState.Pinned;
            pinFrozen = false;
            pinTime = Time.time;
            PlayPinned();
            if (playTerrainImpact)
                SoundEffectEvents.RequestPlay(terrainImpactSound, contactPoint);
        }

        /// <summary>
        /// Halts the spin clip and clears the pivot in the same frame, so no stale
        /// tumble angle can be composed on top of the pinned pose.
        /// </summary>
        private void StopSpinAnimation()
        {
            if (spinAnimator != null && spinAnimator.enabled)
            {
                spinAnimator.speed = 0f;
                // Flush the animator so its pending pivot write cannot land after
                // the pinned pose has been applied.
                if (spinAnimator.runtimeAnimatorController != null)
                    spinAnimator.Update(0f);
                spinAnimator.enabled = false;
            }
            if (spinPivot != null)
                spinPivot.localRotation = Quaternion.identity;
        }

        /// <summary>Returns the pickaxe to its owner's inventory.</summary>
        public bool Recover()
        {
            if (state == ThrownPickaxeState.Idle) return false;

            state = ThrownPickaxeState.Idle;
            owner?.RestoreSuspendedItem(suspendedItem);
            owner = null;
            transform.SetParent(null, true);
            gameObject.SetActive(false);
            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
            return true;
        }

        /// <summary>
        /// Direction from the player towards this pickaxe, used by the magnet to
        /// drag the player in instead of reeling the pickaxe to the view.
        /// </summary>
        public Vector3 GetPullDirection(Vector3 playerPosition)
        {
            Vector3 offset = transform.position - playerPosition;
            return offset.sqrMagnitude > 0.0001f
                ? offset.normalized
                : Vector3.zero;
        }

        /// <summary>
        /// Whether the player is close enough to be considered at the pickaxe. Recall
        /// is player-driven, so this is only informational (HUD prompts and tests); it
        /// no longer triggers a pickup on its own.
        /// </summary>
        public bool IsWithinPickupRange(Vector3 playerPosition)
        {
            return Vector3.Distance(transform.position, playerPosition)
                <= PickupDistance;
        }

        /// <summary>
        /// Flight orientation with the head leading along <paramref name="travelDirection"/>.
        /// The pickaxe tumbles within the vertical plane it flies through, so it stays
        /// broadside to the player rather than flashing edge-on.
        /// </summary>
        /// <param name="spikeLocalDirection">
        /// The head spike axis in root local space. The authored mesh points its spike
        /// down local -X, so assuming +X here would fly the pickaxe backwards, head
        /// trailing, and bury the handle first on impact.
        /// </param>
        public static Quaternion CalculateFlightRotation(
            Vector3 travelDirection,
            Vector3 spikeLocalDirection)
        {
            Vector3 direction = travelDirection.sqrMagnitude > 0.0001f
                ? travelDirection.normalized
                : Vector3.forward;
            Vector3 spike = spikeLocalDirection.sqrMagnitude > 0.0001f
                ? spikeLocalDirection.normalized
                : Vector3.left;
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.98f
                ? Vector3.forward
                : Vector3.up;
            // The clips spin the pivot about its local Z, so local Z has to be the
            // tumble axis: point it sideways, across the direction of travel. The
            // pickaxe then tumbles within the vertical plane it flies through and
            // stays broadside to the player. Yawing it instead leaves the tumble plane
            // perpendicular to that, which flashes an edge-on, flattened pickaxe.
            Vector3 tumbleAxis = Vector3.Cross(up, direction);
            if (tumbleAxis.sqrMagnitude < 0.0001f)
                tumbleAxis = Vector3.Cross(Vector3.forward, direction);
            if (tumbleAxis.sqrMagnitude < 0.0001f)
                tumbleAxis = Vector3.right;
            tumbleAxis.Normalize();

            // Build the basis that puts the spike on the travel direction and the
            // tumble axis sideways, then express it as a rotation of the local axes.
            Vector3 localZ = tumbleAxis;
            Vector3 localY = Vector3.Cross(localZ, direction).normalized;
            Quaternion basis = Quaternion.LookRotation(localZ, localY);
            // basis maps local +X onto the travel direction; correct for the mesh's
            // actual spike axis so the head, not the tail, leads.
            return basis * Quaternion.FromToRotation(spike, Vector3.right);
        }

        /// <summary>
        /// Flight orientation using the authored pickaxe's spike axis.
        /// </summary>
        public static Quaternion CalculateFlightRotation(Vector3 travelDirection)
        {
            return CalculateFlightRotation(
                travelDirection,
                DefaultSpikeLocalDirection);
        }

        /// <summary>
        /// Embed direction for the spike. The raw travel direction is not usable on
        /// its own: a flat throw into the ground would drive the spike almost along
        /// the surface, so the pickaxe ends up lying parallel to it. Blend the travel
        /// direction towards the inward surface normal so the spike always bites in
        /// at a believable angle while still leaning the way it was thrown.
        /// </summary>
        public static Vector3 CalculateEmbedDirection(
            Vector3 travelDirection,
            Vector3 surfaceNormal,
            float minimumBiteAngleDegrees)
        {
            Vector3 inward = surfaceNormal.sqrMagnitude > 0.0001f
                ? -surfaceNormal.normalized
                : Vector3.zero;
            Vector3 travel = travelDirection.sqrMagnitude > 0.0001f
                ? travelDirection.normalized
                : Vector3.zero;
            if (inward == Vector3.zero)
                return travel == Vector3.zero ? Vector3.forward : travel;
            if (travel == Vector3.zero) return inward;

            // How far the travel direction is from driving straight in. Wide angles
            // are the grazing hits that used to leave the pickaxe lying flat.
            float offAxis = Vector3.Angle(travel, inward);
            float limit = Mathf.Clamp(90f - minimumBiteAngleDegrees, 0f, 90f);
            if (offAxis <= limit) return travel;

            // Rotate the travel direction towards the inward normal until it clears
            // the minimum bite angle, preserving the side it came from.
            return Vector3.RotateTowards(
                travel,
                inward,
                (offAxis - limit) * Mathf.Deg2Rad,
                0f).normalized;
        }

        /// <summary>
        /// Aligns the head spike with <paramref name="inward"/>, then rolls the
        /// pickaxe about that spike so the handle leans out along
        /// <paramref name="outwardHint"/> instead of through the struck surface.
        /// <paramref name="shaftLocalDirection"/> points from the head towards the
        /// handle butt; <paramref name="outwardHint"/> is normally the surface normal.
        /// </summary>
        public static Quaternion CalculatePinRotation(
            Vector3 tipLocalDirection,
            Vector3 inward,
            Vector3 shaftLocalDirection,
            Vector3 outwardHint)
        {
            Vector3 tip = tipLocalDirection.sqrMagnitude > 0.0001f
                ? tipLocalDirection.normalized
                : Vector3.right;
            Vector3 target = inward.sqrMagnitude > 0.0001f
                ? inward.normalized
                : Vector3.forward;
            Vector3 shaft = shaftLocalDirection.sqrMagnitude > 0.0001f
                ? shaftLocalDirection.normalized
                : Vector3.down;
            if (outwardHint.sqrMagnitude < 0.0001f) outwardHint = Vector3.up;
            else outwardHint = outwardHint.normalized;
            Quaternion aligned = Quaternion.FromToRotation(tip, target);

            // FromToRotation leaves the roll about the spike undefined, and the shaft
            // is perpendicular to the spike. Roll so the handle leans out of the
            // surface: away from the struck face and, failing that, upward. Rolling
            // purely towards world up is wrong on a ceiling, where it drives the
            // handle into the geometry and leaves nothing exposed to grab.
            Vector3 rollAxis = target;
            Vector3 shaftWorld = aligned * shaft;
            // Only the component perpendicular to the spike is controllable by roll.
            Vector3 desired = outwardHint - Vector3.Project(outwardHint, rollAxis);
            if (desired.sqrMagnitude < 0.0001f)
            {
                // The surface faces along the spike, so "outward" gives no guidance.
                // Fall back to the most upright handle.
                desired = Vector3.up - Vector3.Project(Vector3.up, rollAxis);
                if (desired.sqrMagnitude < 0.0001f) return aligned;
            }

            Vector3 projectedShaft =
                shaftWorld - Vector3.Project(shaftWorld, rollAxis);
            if (projectedShaft.sqrMagnitude < 0.0001f) return aligned;

            float twist = Vector3.SignedAngle(
                projectedShaft.normalized,
                desired.normalized,
                rollAxis);
            return Quaternion.AngleAxis(twist, rollAxis) * aligned;
        }

        /// <summary>
        /// Aligns the head tip with <paramref name="inward"/> using the pickaxe's
        /// default shaft axis and treating the embed direction as the surface normal.
        /// </summary>
        public static Quaternion CalculatePinRotation(
            Vector3 tipLocalDirection,
            Vector3 inward)
        {
            return CalculatePinRotation(
                tipLocalDirection,
                inward,
                DefaultShaftLocalDirection,
                -inward);
        }

        /// <summary>
        /// Aligns the head tip with <paramref name="inward"/>, inferring the outward
        /// direction from the embed direction.
        /// </summary>
        public static Quaternion CalculatePinRotation(
            Vector3 tipLocalDirection,
            Vector3 inward,
            Vector3 shaftLocalDirection)
        {
            return CalculatePinRotation(
                tipLocalDirection,
                inward,
                shaftLocalDirection,
                -inward);
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            float deltaTime = Time.deltaTime;
            if (state == ThrownPickaxeState.Pinned
                && !pinFrozen
                && Time.time - pinTime >= PinSettleDuration)
            {
                FreezePinnedPose();
            }

            if (state == ThrownPickaxeState.Returning)
            {
                TickRecallFlight(deltaTime);
            }
            // Recall is entirely player-driven: pressing the throw key again calls
            // BeginRecall. Walking close by used to trigger it automatically, which
            // meant the pickaxe could be snatched away while the player was still
            // using the rope.
        }

        /// <summary>
        /// Pulls the pickaxe out of the surface and starts its flight back to the
        /// player. The pickaxe is only absorbed once that flight arrives.
        /// </summary>
        public bool BeginRecall()
        {
            if (state == ThrownPickaxeState.Idle
                || state == ThrownPickaxeState.Returning)
            {
                return false;
            }

            ResolveReferences();
            transform.SetParent(null, true);
            Rigidbody resolvedBody = ResolveBody();
            // A kinematic body rejects velocity writes, so clear momentum first.
            if (!resolvedBody.isKinematic)
            {
                resolvedBody.velocity = Vector3.zero;
                resolvedBody.angularVelocity = Vector3.zero;
            }
            resolvedBody.useGravity = false;
            resolvedBody.isKinematic = true;
            DisableColliders();

            state = ThrownPickaxeState.Returning;
            pinFrozen = false;
            recallStartTime = Time.time;
            recallCurrentSpeed = RecallSpeed;
            PlaySpin(Mathf.Max(0f, recallSpinRevolutions));
            return true;
        }

        private void TickRecallFlight(float deltaTime)
        {
            if (ownerTransform == null
                || Time.time - recallStartTime >= Mathf.Max(0.2f, recallTimeout))
            {
                // Never strand the pickaxe: hand it back even if the flight stalls.
                Recover();
                return;
            }

            Vector3 destination = GetOwnerPosition();
            Vector3 offset = destination - transform.position;
            float distance = offset.magnitude;
            if (distance <= RecallAbsorbDistance)
            {
                Recover();
                return;
            }

            recallCurrentSpeed += Mathf.Max(0f, recallAcceleration) * deltaTime;
            Vector3 direction = offset / distance;
            transform.position += direction
                * Mathf.Min(recallCurrentSpeed * deltaTime, distance);
            // Lead with the head so the flight reads as the pickaxe homing in.
            transform.rotation = CalculateFlightRotation(
                direction,
                HeadTipLocalDirection);
        }

        private void FixedUpdate()
        {
            // Remember the free-flight velocity so the collision callback can use the
            // direction the pickaxe was actually travelling, not the bounce.
            if (state != ThrownPickaxeState.Flying) return;
            Vector3 velocity = ResolveBody().velocity;
            if (velocity.sqrMagnitude > 0.0001f) lastFlightVelocity = velocity;
        }

        private void DisableColliders()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = false;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            TryPinFromCollision(collision);
        }

        /// <summary>
        /// A pickaxe that bounced without pinning stays in contact, so no further
        /// OnCollisionEnter arrives and it would roll and spin forever. Keep pinning
        /// from the ongoing contact as a fallback.
        /// </summary>
        private void OnCollisionStay(Collision collision)
        {
            TryPinFromCollision(collision);
        }

        private void TryPinFromCollision(Collision collision)
        {
            if (state != ThrownPickaxeState.Flying) return;
            if (collision.contactCount == 0) return;
            // Never pin into the thrower. Owner colliders are also ignored at launch,
            // so this is only a guard for anything parented under them later.
            if (ownerTransform != null
                && collision.transform != null
                && collision.transform.IsChildOf(ownerTransform))
            {
                return;
            }

            ContactPoint contact = collision.GetContact(0);
            // The body's current velocity has already been changed by the impact and
            // can even point back out of the surface, which would invert the buried
            // pose. Use the velocity recorded on the last frame of free flight.
            Vector3 travel = lastFlightVelocity.sqrMagnitude > 0.0001f
                ? lastFlightVelocity
                : collision.relativeVelocity;
            // relativeVelocity points from this body towards the other one's frame,
            // so make sure the direction actually goes into the surface.
            if (Vector3.Dot(travel, contact.normal) > 0f)
                travel = Vector3.Reflect(travel, contact.normal);
            Pin(
                contact.point,
                travel,
                contact.normal,
                collision.rigidbody != null
                    ? collision.rigidbody.transform
                    : contact.otherCollider != null
                        ? contact.otherCollider.transform
                        : null);
        }

        private Vector3 GetOwnerPosition()
        {
            if (ownerTransform == null) return transform.position;
            CharacterController character =
                ownerTransform.GetComponent<CharacterController>();
            return character != null
                ? character.bounds.center
                : ownerTransform.position;
        }

        private void PlaySpin(float revolutionsPerSecond)
        {
            if (spinAnimator == null
                || spinAnimator.runtimeAnimatorController == null)
            {
                return;
            }

            spinAnimator.enabled = true;
            spinAnimator.speed = Mathf.Max(0f, revolutionsPerSecond);
            spinAnimator.Play(SpinStateName, 0, 0f);
        }

        private void PlayPinned()
        {
            if (spinAnimator == null
                || spinAnimator.runtimeAnimatorController == null)
            {
                if (spinPivot != null)
                    spinPivot.localRotation = Quaternion.identity;
                return;
            }

            spinAnimator.enabled = true;
            spinAnimator.speed = 1f;
            spinAnimator.Play(PinnedStateName, 0, 0f);
            // Play() only takes effect on the animator's next update, so without an
            // immediate evaluation the pivot would render one more frame holding the
            // spin state's angle on top of the freshly pinned pose.
            spinAnimator.Update(0f);
        }

        private void FreezePinnedPose()
        {
            pinFrozen = true;
            if (spinPivot != null)
                spinPivot.localRotation = Quaternion.identity;
            if (spinAnimator != null)
                spinAnimator.enabled = false;
        }

        private Rigidbody ResolveBody()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            return body;
        }

        private void ResolveReferences()
        {
            ResolveBody();
            if (spinPivot == null)
            {
                Animator childAnimator = GetComponentInChildren<Animator>(true);
                spinPivot = childAnimator != null
                    ? childAnimator.transform
                    : transform;
            }
            if (spinAnimator == null)
                spinAnimator = spinPivot.GetComponent<Animator>();
        }
    }
}
