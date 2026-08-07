using UnityEngine;

namespace Supernova.PortalExample
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PortalExampleTraveller))]
    [RequireComponent(typeof(PortalExampleResettable))]
    public sealed class PortalExampleFirstPersonController : MonoBehaviour
    {
        [SerializeField] private Transform view;
        [SerializeField, Min(0f)] private float moveSpeed = 5.5f;
        [SerializeField, Min(0f)] private float groundAcceleration = 28f;
        [SerializeField, Min(0f)] private float airAcceleration = 7f;
        [SerializeField, Min(0f)] private float jumpHeight = 1.25f;
        [SerializeField, Min(0f)] private float gravity = 19f;
        [SerializeField, Min(0f)] private float lookSensitivity = 2f;

        private CharacterController controller;
        private PortalExampleResettable resettable;
        private Vector3 velocity;
        private float pitch;

        public Vector3 Velocity => velocity;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            resettable = GetComponent<PortalExampleResettable>();
            if (view == null && Camera.main != null)
            {
                view = Camera.main.transform;
            }
        }

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                bool shouldLock = Cursor.lockState != CursorLockMode.Locked;
                Cursor.lockState = shouldLock
                    ? CursorLockMode.Locked
                    : CursorLockMode.None;
                Cursor.visible = !shouldLock;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                resettable.ResetNow();
                return;
            }

            Look();
            Move();
        }

        public void MapVelocity(Matrix4x4 mapping)
        {
            velocity = mapping.MultiplyVector(velocity);
        }

        public void ClearVelocity()
        {
            velocity = Vector3.zero;
        }

        private void Look()
        {
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            float yaw = Input.GetAxis("Mouse X") * lookSensitivity;
            float pitchDelta = Input.GetAxis("Mouse Y") * lookSensitivity;
            transform.Rotate(Vector3.up, yaw, Space.World);

            pitch = Mathf.Clamp(pitch - pitchDelta, -86f, 86f);
            if (view != null)
            {
                view.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        private void Move()
        {
            Vector3 input = new Vector3(
                Input.GetAxisRaw("Horizontal"),
                0f,
                Input.GetAxisRaw("Vertical"));
            input = Vector3.ClampMagnitude(input, 1f);

            Vector3 desiredPlanarVelocity =
                transform.TransformDirection(input) * moveSpeed;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(
                velocity,
                Vector3.up);
            float acceleration = controller.isGrounded
                ? groundAcceleration
                : airAcceleration;
            planarVelocity = Vector3.MoveTowards(
                planarVelocity,
                desiredPlanarVelocity,
                acceleration * Time.deltaTime);

            velocity.x = planarVelocity.x;
            velocity.z = planarVelocity.z;
            if (controller.isGrounded && velocity.y < 0f)
            {
                velocity.y = -2f;
            }

            if (controller.isGrounded && Input.GetKeyDown(KeyCode.Space))
            {
                velocity.y = Mathf.Sqrt(2f * gravity * jumpHeight);
            }

            velocity.y -= gravity * Time.deltaTime;
            CollisionFlags flags = controller.Move(velocity * Time.deltaTime);
            if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f)
            {
                velocity.y = 0f;
            }
        }
    }
}
