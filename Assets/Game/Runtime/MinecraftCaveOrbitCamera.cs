using Supernova.Inputs;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    [DisallowMultipleComponent]
    public sealed class MinecraftCaveOrbitCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float distance = 54f;
        [SerializeField] private float yaw;
        [SerializeField] private float pitch = 18f;
        [SerializeField] private float orbitSpeed = 3.2f;
        [SerializeField] private float zoomSpeed = 7f;
        [SerializeField] private Vector2 distanceRange = new Vector2(28f, 82f);

        public void Configure(Transform newTarget, float newDistance, float newPitch)
        {
            target = newTarget;
            distance = newDistance;
            pitch = newPitch;
            ApplyTransform();
        }

        private void OnEnable()
        {
            ApplyTransform();
        }

        private void LateUpdate()
        {
            if (GameInput.Held(GameInputActionId.SpectatorOrbitHold))
            {
                Vector2 look = GameInput.ReadVector2(GameInputActionId.Look);
                yaw += look.x * orbitSpeed;
                pitch -= look.y * orbitSpeed;
                pitch = Mathf.Clamp(pitch, -10f, 70f);
            }

            // The scroll control reports raw platform deltas (120 per notch on
            // Windows), so only the direction drives the tuned zoom step.
            float scroll = Mathf.Sign(
                GameInput.ReadVector2(GameInputActionId.ScrollWheel).y);
            if (Mathf.Abs(scroll) > 0.001f)
            {
                distance = Mathf.Clamp(
                    distance - scroll * zoomSpeed,
                    distanceRange.x,
                    distanceRange.y);
            }

            ApplyTransform();
        }

        private void ApplyTransform()
        {
            if (target == null)
            {
                return;
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = target.position + rotation * (Vector3.back * distance);
            transform.rotation = rotation;
        }
    }
}
