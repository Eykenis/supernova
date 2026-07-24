using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures
{
    public readonly struct CreatureMovementCommand
    {
        public CreatureMovementCommand(
            int id,
            Vector3 horizontalDirection,
            Vector3 worldUp,
            int riseInVoxels)
        {
            Id = id;
            HorizontalDirection = Vector3.ProjectOnPlane(
                horizontalDirection,
                worldUp).normalized;
            WorldUp = worldUp.normalized;
            RiseInVoxels = Mathf.Max(0, riseInVoxels);
        }

        public int Id { get; }
        public Vector3 HorizontalDirection { get; }
        public Vector3 WorldUp { get; }
        public int RiseInVoxels { get; }
        public bool ShouldJump => RiseInVoxels > 0;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CreaturePhysicsMotor : MonoBehaviour
    {
        [SerializeField] private Rigidbody body;
        [SerializeField, Min(0.01f)] private float movementSpeed = 1.26f;
        [SerializeField, Min(0.01f)] private float maximumHorizontalAcceleration = 15.12f;
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
            movementSpeed = voxelSize * 3f;
            maximumHorizontalAcceleration = voxelSize * 36f;
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
            Vector3 desiredHorizontalVelocity = hasCommand
                ? command.HorizontalDirection * movementSpeed
                : Vector3.zero;
            ApplyHorizontalVelocity(desiredHorizontalVelocity, up);

            Vector3 turnDirection = hasCommand
                ? command.HorizontalDirection
                : hasFacing ? facingDirection : Vector3.zero;
            RotateTowards(turnDirection, up);
            if (!hasCommand)
            {
                return;
            }

            if (command.ShouldJump && command.Id != lastJumpCommandId)
            {
                ApplyJumpImpulse(command.RiseInVoxels, up);
                lastJumpCommandId = command.Id;
            }
        }

        private void ApplyHorizontalVelocity(Vector3 desiredVelocity, Vector3 up)
        {
            Vector3 currentHorizontalVelocity = Vector3.ProjectOnPlane(body.velocity, up);
            Vector3 velocityChange = desiredVelocity - currentHorizontalVelocity;
            float maximumChange = maximumHorizontalAcceleration * Time.fixedDeltaTime;
            body.AddForce(
                Vector3.ClampMagnitude(velocityChange, maximumChange),
                ForceMode.VelocityChange);
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
