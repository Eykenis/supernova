using System.Collections.Generic;
using Supernova.Effects;
using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CreaturePhysicsMotor : MonoBehaviour
    {
        private static readonly List<CreaturePhysicsMotor> ActiveMotors =
            new List<CreaturePhysicsMotor>();

        [SerializeField] private Rigidbody body;
        [Tooltip(
            "Smaller non-trigger collider selected as the only solid collider "
            + "pair between active creatures.")]
        [SerializeField] private Collider crowdCollider;
        [SerializeField, Min(0.01f)] private float animationReferenceSpeed = 1.26f;
        [SerializeField, Min(1f)] private float turnSpeedInDegrees = 540f;
        [Tooltip(
            "Scales jump take-off speed. Values above one absorb the difference "
            + "between the voxel graph's whole-cube layers and the interpolated "
            + "Marching Cubes surface the body actually rests on.")]
        [SerializeField, Min(1f)] private float jumpSpeedMultiplier = 1.15f;
        [Tooltip("Grace period where the creature still counts as grounded after "
            + "losing contact, absorbing single-frame separations on slopes.")]
        [SerializeField, Min(0f)] private float coyoteTime = 0.1f;
        [Tooltip("Maximum surface angle from up that still counts as ground.")]
        [SerializeField, Range(1f, 89f)] private float maximumGroundAngle = 55f;

        private bool hasFacing;
        private Vector3 facingDirection;
        private Vector3 facingUp = Vector3.up;
        private bool isRegisteredForCrowdCollisions;
        private bool hasMoveCommand;
        private Vector3 moveDirection;
        private float moveTargetSpeed;
        private float moveAcceleration;
        private float pendingJumpSpeed;
        private bool hasPendingJump;
        private int pendingJumpCommandId;
        // Sentinel below any real identifier so the first request is never mistaken
        // for one that already fired.
        private int lastFiredJumpCommandId = int.MinValue;
        private float lastGroundedTime = float.NegativeInfinity;
        private int groundContactCount;
        private PhysicMaterial frictionlessMaterial;

        public Collider CrowdCollider => crowdCollider;

        /// <summary>
        /// True while the creature rests on a surface. Derived from solved
        /// collision contacts rather than a cast, because creatures disable
        /// collisions against each other's body colliders and a cast would happily
        /// report a neighbour's ignored collider as ground.
        /// </summary>
        public bool IsGrounded =>
            groundContactCount > 0
            || Time.time - lastGroundedTime <= coyoteTime;
        public float HorizontalSpeed
        {
            get
            {
                if (body == null)
                {
                    return 0f;
                }

                Vector3 up = hasFacing && facingUp.sqrMagnitude > 0.5f
                    ? facingUp
                    : Vector3.up;
                return Vector3.ProjectOnPlane(body.velocity, up).magnitude;
            }
        }
        public float NormalizedHorizontalSpeed =>
            HorizontalSpeed / Mathf.Max(0.01f, animationReferenceSpeed);

        /// <summary>
        /// Speed the motor was last told to travel at, in metres per second, or
        /// zero when no movement is commanded. Comparing measured speed against
        /// this rather than against <see cref="NormalizedHorizontalSpeed"/> keeps
        /// navigation on one metric: the animation reference speed is a
        /// presentation value and need not match the commanded speed at all.
        /// </summary>
        public float CommandedSpeed => hasMoveCommand ? moveTargetSpeed : 0f;

        /// <summary>
        /// Measured horizontal speed as a fraction of the commanded speed. Returns
        /// zero while no movement is commanded, so a parked creature never reads as
        /// blocked.
        /// </summary>
        public float CommandedSpeedFraction =>
            hasMoveCommand && moveTargetSpeed > 0.0001f
                ? HorizontalSpeed / moveTargetSpeed
                : 0f;

        private void Reset()
        {
            body = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            body = body != null ? body : GetComponent<Rigidbody>();
            // [ExecuteAlways] runs Awake in the editor too; only touch materials in
            // play mode so the authored prefab colliders are never rewritten.
            if (Application.isPlaying)
            {
                RigidbodyImpactFeedback.Ensure(body);
                ApplyFrictionlessColliders();
            }
        }

        /// <summary>
        /// Replaces the friction material on the creature's own colliders with a
        /// zero-friction one. The cave terrain uses a high-friction material that
        /// combines with Maximum, so lateral velocity written to the Rigidbody is
        /// otherwise cancelled by the contact solver every physics step and the
        /// creature never moves horizontally even though it is commanded to. Only
        /// the horizontal drive is affected; grounding and jumps are unchanged.
        /// </summary>
        private void ApplyFrictionlessColliders()
        {
            if (frictionlessMaterial == null)
            {
                frictionlessMaterial = new PhysicMaterial("CreatureFrictionless")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    frictionCombine = PhysicMaterialCombine.Minimum,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider != null && !collider.isTrigger)
                {
                    collider.sharedMaterial = frictionlessMaterial;
                }
            }
        }

        private void OnEnable()
        {
            RegisterCrowdCollisionPairs();
        }

        private void OnDisable()
        {
            UnregisterCrowdCollisionPairs();
        }

        private void OnDestroy()
        {
            UnregisterCrowdCollisionPairs();
        }

        public void Configure(Rigidbody rigidbody)
        {
            Configure(rigidbody, crowdCollider);
        }

        public void Configure(
            Rigidbody rigidbody,
            Collider creatureCrowdCollider)
        {
            bool refreshCrowdPairs = isRegisteredForCrowdCollisions
                && crowdCollider != creatureCrowdCollider;
            if (refreshCrowdPairs)
            {
                RefreshRegisteredCrowdCollisionPairs(false);
            }

            body = rigidbody;
            crowdCollider = creatureCrowdCollider;
            RigidbodyImpactFeedback.Ensure(body);

            if (refreshCrowdPairs)
            {
                RefreshRegisteredCrowdCollisionPairs(true);
            }
        }

        public void Stop()
        {
            hasFacing = false;
            hasMoveCommand = false;
            moveTargetSpeed = 0f;
            // Drop a queued jump so leaving a movement state cannot fire one later.
            hasPendingJump = false;
            pendingJumpSpeed = 0f;
        }

        public void Face(Vector3 direction, Vector3 worldUp)
        {
            facingDirection = Vector3.ProjectOnPlane(direction, worldUp)
                .normalized;
            facingUp = worldUp.sqrMagnitude > 0.5f
                ? worldUp.normalized
                : Vector3.up;
            hasFacing = facingDirection.sqrMagnitude > 0.0001f;
        }

        /// <summary>
        /// Requests horizontal travel along a world direction and faces the same
        /// way. Vertical motion stays with gravity and collision response.
        /// </summary>
        public void MoveTowards(
            Vector3 worldDirection,
            Vector3 worldUp,
            float targetSpeed,
            float acceleration)
        {
            Face(worldDirection, worldUp);
            moveDirection = Vector3.ProjectOnPlane(worldDirection, facingUp)
                .normalized;
            hasMoveCommand = moveDirection.sqrMagnitude > 0.0001f;
            moveTargetSpeed = Mathf.Max(0f, targetSpeed);
            moveAcceleration = Mathf.Max(1f, acceleration);
        }

        /// <summary>
        /// Queues a single jump that reaches roughly the requested height. The
        /// command identifier makes the impulse fire once: repeating the same
        /// identifier every frame will not stack take-off speed while airborne.
        /// </summary>
        public void RequestJump(float heightInMeters, int commandId)
        {
            if (commandId == lastFiredJumpCommandId || heightInMeters <= 0f)
            {
                return;
            }

            pendingJumpSpeed = ResolveJumpSpeed(
                heightInMeters,
                facingUp,
                jumpSpeedMultiplier);
            pendingJumpCommandId = commandId;
            hasPendingJump = true;
        }

        /// <summary>Take-off speed that reaches a height under scene gravity.</summary>
        public static float ResolveJumpSpeed(
            float heightInMeters,
            Vector3 worldUp,
            float multiplier)
        {
            Vector3 up = worldUp.sqrMagnitude > 0.5f ? worldUp.normalized : Vector3.up;
            float gravity = Mathf.Abs(Vector3.Dot(Physics.gravity, up));
            if (gravity <= Mathf.Epsilon)
            {
                gravity = Mathf.Abs(Physics.gravity.y);
            }

            return Mathf.Sqrt(2f * gravity * Mathf.Max(0f, heightInMeters))
                * Mathf.Max(1f, multiplier);
        }

        public void ApplyImpulse(Vector3 impulse)
        {
            if (body == null || body.isKinematic) return;
            body.AddForce(impulse, ForceMode.VelocityChange);
        }

        private void RegisterCrowdCollisionPairs()
        {
            if (isRegisteredForCrowdCollisions)
            {
                return;
            }

            for (int i = ActiveMotors.Count - 1; i >= 0; i--)
            {
                CreaturePhysicsMotor other = ActiveMotors[i];
                if (other == null)
                {
                    ActiveMotors.RemoveAt(i);
                    continue;
                }

                SetCrowdCollisionPair(other, true);
            }

            ActiveMotors.Add(this);
            isRegisteredForCrowdCollisions = true;
        }

        private void UnregisterCrowdCollisionPairs()
        {
            if (!isRegisteredForCrowdCollisions)
            {
                return;
            }

            for (int i = ActiveMotors.Count - 1; i >= 0; i--)
            {
                CreaturePhysicsMotor other = ActiveMotors[i];
                if (other == null || other == this)
                {
                    ActiveMotors.RemoveAt(i);
                    continue;
                }

                SetCrowdCollisionPair(other, false);
            }

            isRegisteredForCrowdCollisions = false;
        }

        private void RefreshRegisteredCrowdCollisionPairs(bool active)
        {
            for (int i = ActiveMotors.Count - 1; i >= 0; i--)
            {
                CreaturePhysicsMotor other = ActiveMotors[i];
                if (other == null)
                {
                    ActiveMotors.RemoveAt(i);
                    continue;
                }

                if (other != this)
                {
                    SetCrowdCollisionPair(other, active);
                }
            }
        }

        private void SetCrowdCollisionPair(
            CreaturePhysicsMotor other,
            bool active)
        {
            Collider[] ownColliders = GetComponentsInChildren<Collider>(true);
            Collider[] otherColliders =
                other.GetComponentsInChildren<Collider>(true);
            for (int ownIndex = 0; ownIndex < ownColliders.Length; ownIndex++)
            {
                Collider ownCollider = ownColliders[ownIndex];
                if (ownCollider == null || ownCollider.isTrigger)
                {
                    continue;
                }

                for (int otherIndex = 0;
                    otherIndex < otherColliders.Length;
                    otherIndex++)
                {
                    Collider otherCollider = otherColliders[otherIndex];
                    if (otherCollider != null && !otherCollider.isTrigger)
                    {
                        bool hasCrowdCollision = crowdCollider != null
                            && crowdCollider.enabled
                            && !crowdCollider.isTrigger
                            && other.crowdCollider != null
                            && other.crowdCollider.enabled
                            && !other.crowdCollider.isTrigger;
                        bool keepCrowdCollision = hasCrowdCollision
                            && ownCollider == crowdCollider
                            && otherCollider == other.crowdCollider;
                        Physics.IgnoreCollision(
                            ownCollider,
                            otherCollider,
                            active
                                && hasCrowdCollision
                                && !keepCrowdCollision);
                    }
                }
            }
        }

        private void FixedUpdate()
        {
            // [ExecuteAlways] keeps crowd collision pairs correct while editing,
            // but locomotion must never drive a Rigidbody outside play mode.
            if (!Application.isPlaying)
            {
                return;
            }

            if (body == null || body.isKinematic)
            {
                return;
            }

            if (groundContactCount > 0)
            {
                lastGroundedTime = Time.time;
            }

            ApplyHorizontalVelocity();
            ApplyPendingJump();

            if (hasFacing)
            {
                RotateTowards(facingDirection, facingUp);
            }
        }

        /// <summary>
        /// Steers the horizontal velocity component toward the commanded speed and
        /// leaves the vertical component untouched, so gravity, falling and
        /// collision response continue to own vertical motion.
        /// </summary>
        private void ApplyHorizontalVelocity()
        {
            Vector3 up = facingUp.sqrMagnitude > 0.5f ? facingUp : Vector3.up;
            Vector3 velocity = body.velocity;
            float verticalSpeed = Vector3.Dot(velocity, up);
            Vector3 horizontal = velocity - up * verticalSpeed;
            Vector3 desired = hasMoveCommand
                ? moveDirection * moveTargetSpeed
                : Vector3.zero;
            Vector3 steered = Vector3.MoveTowards(
                horizontal,
                desired,
                Mathf.Max(1f, moveAcceleration) * Time.fixedDeltaTime);
            body.velocity = steered + up * verticalSpeed;
        }

        private void ApplyPendingJump()
        {
            if (!hasPendingJump
                || pendingJumpSpeed <= 0f
                || pendingJumpCommandId == lastFiredJumpCommandId
                || !IsGrounded)
            {
                return;
            }

            Vector3 up = facingUp.sqrMagnitude > 0.5f ? facingUp : Vector3.up;
            float verticalSpeed = Vector3.Dot(body.velocity, up);
            body.AddForce(
                up * (pendingJumpSpeed - verticalSpeed),
                ForceMode.VelocityChange);
            lastFiredJumpCommandId = pendingJumpCommandId;
            hasPendingJump = false;
            pendingJumpSpeed = 0f;
            // Clear grounded state immediately so the coyote grace period cannot
            // let a second impulse through before the body has actually left.
            groundContactCount = 0;
            lastGroundedTime = float.NegativeInfinity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            RefreshGroundContacts(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            RefreshGroundContacts(collision);
        }

        private void OnCollisionExit(Collision collision)
        {
            groundContactCount = 0;
        }

        private void RefreshGroundContacts(Collision collision)
        {
            Vector3 up = facingUp.sqrMagnitude > 0.5f ? facingUp : Vector3.up;
            float minimumAlignment = Mathf.Cos(
                maximumGroundAngle * Mathf.Deg2Rad);
            int contacts = 0;
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (Vector3.Dot(collision.GetContact(i).normal, up)
                    >= minimumAlignment)
                {
                    contacts++;
                }
            }

            if (contacts > 0)
            {
                groundContactCount = contacts;
                lastGroundedTime = Time.time;
            }
        }

        private void RotateTowards(Vector3 direction, Vector3 up)
        {
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion target = Quaternion.LookRotation(direction, up);
            body.MoveRotation(Quaternion.RotateTowards(
                body.rotation,
                target,
                turnSpeedInDegrees * Time.fixedDeltaTime));
        }
    }
}
