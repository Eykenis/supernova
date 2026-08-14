using Supernova.Inputs;
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
            if (GameInput.Pressed(GameInputActionId.SpectatorLookHold))
            {
                looking = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (GameInput.Released(GameInputActionId.SpectatorLookHold))
            {
                looking = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (looking)
            {
                Vector2 look = GameInput.ReadVector2(GameInputActionId.Look);
                yaw += look.x * lookSensitivity;
                pitch -= look.y * lookSensitivity;
                pitch = Mathf.Clamp(pitch, -88f, 88f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            Vector2 move = GameInput.ReadVector2(GameInputActionId.Move);
            Vector3 movement = transform.right * move.x
                + transform.forward * move.y;
            if (GameInput.Held(GameInputActionId.SpectatorUp))
            {
                movement += Vector3.up;
            }
            if (GameInput.Held(GameInputActionId.SpectatorDown))
            {
                movement += Vector3.down;
            }
            if (movement.sqrMagnitude > 1f)
            {
                movement.Normalize();
            }

            float speed = GameInput.Held(GameInputActionId.SpectatorFast)
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
