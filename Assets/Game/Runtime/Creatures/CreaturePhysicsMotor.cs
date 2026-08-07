using UnityEngine;
using UnityEngine.Serialization;

namespace Supernova.MinecraftCaves.Creatures
{
    public readonly struct CreatureMovementCommand
    {
        public CreatureMovementCommand(
            int id,
            Vector3 horizontalDirection,
            Vector3 worldUp,
            int riseInVoxels)
            : this(
                id,
                horizontalDirection,
                worldUp,
                riseInVoxels,
                false,
                default)
        {
        }

        private CreatureMovementCommand(
            int id,
            Vector3 horizontalDirection,
            Vector3 worldUp,
            int riseInVoxels,
            bool isTraversalLink,
            Vector3 targetWorldPosition)
        {
            Id = id;
            HorizontalDirection = Vector3.ProjectOnPlane(
                horizontalDirection,
                worldUp).normalized;
            WorldUp = worldUp.normalized;
            RiseInVoxels = Mathf.Max(0, riseInVoxels);
            IsTraversalLink = isTraversalLink;
            TargetWorldPosition = targetWorldPosition;
        }

        public static CreatureMovementCommand TraverseTo(
            int id,
            Vector3 horizontalDirection,
            Vector3 worldUp,
            Vector3 targetWorldPosition)
        {
            return new CreatureMovementCommand(
                id,
                horizontalDirection,
                worldUp,
                0,
                true,
                targetWorldPosition);
        }

        public int Id { get; }
        public Vector3 HorizontalDirection { get; }
        public Vector3 WorldUp { get; }
        public int RiseInVoxels { get; }
        public bool IsTraversalLink { get; }
        public Vector3 TargetWorldPosition { get; }
        public bool ShouldJump => RiseInVoxels > 0;
    }

    [DisallowMultipleComponent]
    public sealed class CreaturePhysicsMotor : MonoBehaviour
    {
        [SerializeField] private Rigidbody body;
        [FormerlySerializedAs("movementSpeed")]
        [Tooltip("Maximum speed along the commanded movement direction.")]
        [SerializeField, Min(0.01f)] private float maximumHorizontalSpeed = 1.26f;
        [FormerlySerializedAs("maximumHorizontalAcceleration")]
        [Tooltip("Continuous force applied along the commanded movement direction.")]
        [SerializeField, Min(0.01f)] private float movementForce = 15.12f;
        [SerializeField, Min(0.01f)] private float voxelSize = 0.42f;
        [SerializeField, Min(1f)] private float jumpVelocityMultiplier = 1.1f;
        [SerializeField, Min(1f)] private float turnSpeedInDegrees = 540f;

        private CreatureMovementCommand command;
        private bool hasCommand;
        private bool hasFacing;
        private Vector3 facingDirection;
        private Vector3 facingUp = Vector3.up;
        private int lastJumpCommandId = int.MinValue;

        public bool HasCommand => hasCommand;
        public float MaximumHorizontalSpeed =>
            Mathf.Max(0.01f, maximumHorizontalSpeed);
        public float HorizontalSpeed
        {
            get
            {
                if (body == null)
                {
                    return 0f;
                }
                Vector3 up = hasCommand
                    && command.WorldUp.sqrMagnitude > 0.5f
                        ? command.WorldUp
                        : hasFacing && facingUp.sqrMagnitude > 0.5f
                            ? facingUp
                            : Vector3.up;
                return Vector3.ProjectOnPlane(body.velocity, up).magnitude;
            }
        }
        public float NormalizedHorizontalSpeed =>
            HorizontalSpeed / MaximumHorizontalSpeed;

        private void Reset()
        {
            body = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            body = body != null ? body : GetComponent<Rigidbody>();
        }

        public void Configure(Rigidbody rigidbody, float worldVoxelSize)
        {
            body = rigidbody;
            voxelSize = Mathf.Max(0.01f, worldVoxelSize);
            maximumHorizontalSpeed = voxelSize * 3f;
            movementForce = voxelSize * 36f;
        }

        public void Submit(in CreatureMovementCommand value)
        {
            command = value;
            hasCommand = true;
            hasFacing = false;
        }

        public void Stop()
        {
            hasCommand = false;
            hasFacing = false;
        }

        public void Face(Vector3 direction, Vector3 worldUp)
        {
            Vector3 up = worldUp.sqrMagnitude > 0.5f ? worldUp.normalized : Vector3.up;
            facingDirection = Vector3.ProjectOnPlane(direction, up).normalized;
            facingUp = up;
            hasFacing = facingDirection.sqrMagnitude > 0.0001f;
        }

        public void ApplyImpulse(Vector3 impulse)
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (body == null || body.isKinematic || impulse.sqrMagnitude <= 0f) return;
            body.AddForce(impulse, ForceMode.Impulse);
        }

        private void FixedUpdate()
        {
            if (body == null || body.isKinematic)
            {
                return;
            }

            Vector3 up = hasCommand && command.WorldUp.sqrMagnitude > 0.5f
                ? command.WorldUp
                : hasFacing ? facingUp : Vector3.up;
            bool isTraversal = hasCommand && command.IsTraversalLink;
            if (!isTraversal)
            {
                Vector3 movementDirection = hasCommand
                    ? command.HorizontalDirection
                    : Vector3.zero;
                ApplyHorizontalForce(movementDirection, up);
            }

            Vector3 turnDirection = hasCommand
                ? command.HorizontalDirection
                : hasFacing ? facingDirection : Vector3.zero;
            RotateTowards(turnDirection, up);
            if (!hasCommand)
            {
                return;
            }

            if (command.Id == lastJumpCommandId)
            {
                return;
            }

            if (command.IsTraversalLink)
            {
                ApplyTraversalImpulse(command.TargetWorldPosition, up);
                lastJumpCommandId = command.Id;
            }
            else if (command.ShouldJump)
            {
                ApplyJumpImpulse(command.RiseInVoxels, up);
                lastJumpCommandId = command.Id;
            }
        }

        private void ApplyHorizontalForce(Vector3 direction, Vector3 up)
        {
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector3 currentHorizontalVelocity = Vector3.ProjectOnPlane(body.velocity, up);
            float speedInMovementDirection =
                Vector3.Dot(currentHorizontalVelocity, direction);
            float remainingSpeed =
                maximumHorizontalSpeed - speedInMovementDirection;
            if (remainingSpeed <= 0f)
            {
                return;
            }

            float fixedDeltaTime = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            float forceWithoutOvershoot =
                remainingSpeed * body.mass / fixedDeltaTime;
            float appliedForce = Mathf.Min(movementForce, forceWithoutOvershoot);
            body.AddForce(direction * appliedForce, ForceMode.Force);
        }

        private void ApplyJumpImpulse(int riseInVoxels, Vector3 up)
        {
            float height = Mathf.Max(1, riseInVoxels) * voxelSize;
            float gravity = Mathf.Abs(Vector3.Dot(Physics.gravity, -up));
            if (gravity < 0.01f)
            {
                gravity = 9.81f;
            }

            float targetSpeed = Mathf.Sqrt(2f * gravity * height) * jumpVelocityMultiplier;
            float currentSpeed = Vector3.Dot(body.velocity, up);
            body.AddForce(
                up * Mathf.Max(0f, targetSpeed - currentSpeed),
                ForceMode.VelocityChange);
        }

        private void ApplyTraversalImpulse(Vector3 targetWorldPosition, Vector3 up)
        {
            Vector3 displacement = targetWorldPosition - body.position;
            float verticalDisplacement = Vector3.Dot(displacement, up);
            Vector3 horizontalDisplacement =
                Vector3.ProjectOnPlane(displacement, up);
            float gravity = Mathf.Abs(Vector3.Dot(Physics.gravity, -up));
            if (gravity < 0.01f)
            {
                gravity = 9.81f;
            }

            float requestedApex = Mathf.Max(0f, verticalDisplacement)
                + voxelSize * 0.75f;
            float verticalSpeed =
                Mathf.Sqrt(2f * gravity * requestedApex)
                * jumpVelocityMultiplier;
            float actualApex =
                verticalSpeed * verticalSpeed / (2f * gravity);
            float ascentTime = verticalSpeed / gravity;
            float descentDistance =
                Mathf.Max(0.01f, actualApex - verticalDisplacement);
            float descentTime =
                Mathf.Sqrt(2f * descentDistance / gravity);
            float flightTime = Mathf.Max(
                Time.fixedDeltaTime,
                ascentTime + descentTime);
            Vector3 targetVelocity =
                horizontalDisplacement / flightTime + up * verticalSpeed;
            body.AddForce(
                targetVelocity - body.velocity,
                ForceMode.VelocityChange);
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
