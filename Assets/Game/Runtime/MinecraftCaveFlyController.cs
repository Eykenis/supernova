using UnityEngine;

namespace Supernova.MinecraftCaves
{
    [DisallowMultipleComponent]
    public sealed class MinecraftCaveFlyController : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float moveSpeed = 12f;
        [SerializeField, Min(1f)] private float fastMultiplier = 3f;
        [SerializeField, Min(0.1f)] private float lookSensitivity = 2.2f;

        private float yaw;
        private float pitch;
        private bool looking;

        private void OnEnable()
        {
            Vector3 angles = transform.eulerAngles;
            yaw = angles.y;
            pitch = NormalizeAngle(angles.x);
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(1))
            {
                looking = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (Input.GetMouseButtonUp(1))
            {
                looking = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (looking)
            {
                yaw += Input.GetAxisRaw("Mouse X") * lookSensitivity;
                pitch -= Input.GetAxisRaw("Mouse Y") * lookSensitivity;
                pitch = Mathf.Clamp(pitch, -88f, 88f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            Vector3 movement = transform.right * Input.GetAxisRaw("Horizontal")
                + transform.forward * Input.GetAxisRaw("Vertical");
            if (Input.GetKey(KeyCode.E))
            {
                movement += Vector3.up;
            }
            if (Input.GetKey(KeyCode.Q))
            {
                movement += Vector3.down;
            }
            if (movement.sqrMagnitude > 1f)
            {
                movement.Normalize();
            }

            float speed = Input.GetKey(KeyCode.LeftShift)
                ? moveSpeed * fastMultiplier
                : moveSpeed;
            transform.position += movement * (speed * Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            if (looking)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                looking = false;
            }
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
