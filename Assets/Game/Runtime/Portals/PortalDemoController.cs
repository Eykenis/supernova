using UnityEngine;

namespace Supernova.Portals
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PortalTraveller))]
    public sealed class PortalDemoController : MonoBehaviour
    {
        [SerializeField] private Transform view;
        [SerializeField, Min(0f)] private float moveSpeed = 5f;
        [SerializeField, Min(0f)] private float lookSensitivity = 2f;
        [SerializeField, Min(0f)] private float gravity = 18f;

        private CharacterController controller;
        private float pitch;
        private float verticalVelocity;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
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
            Look();
            Move();

            if (Input.GetKeyDown(KeyCode.R))
            {
                transform.SetPositionAndRotation(
                    new Vector3(0f, 1.1f, -7f),
                    Quaternion.identity);
                verticalVelocity = 0f;
            }
        }

        private void Look()
        {
            float yaw = Input.GetAxis("Mouse X") * lookSensitivity;
            float pitchDelta = Input.GetAxis("Mouse Y") * lookSensitivity;
            transform.Rotate(Vector3.up, yaw, Space.World);

            pitch = Mathf.Clamp(pitch - pitchDelta, -85f, 85f);
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

            Vector3 planarVelocity = transform.TransformDirection(input) * moveSpeed;
            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }
            else
            {
                verticalVelocity -= gravity * Time.deltaTime;
            }

            controller.Move(
                (planarVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
        }
    }
}
