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
            if (Input.GetMouseButton(0))
            {
                yaw += Input.GetAxis("Mouse X") * orbitSpeed;
                pitch -= Input.GetAxis("Mouse Y") * orbitSpeed;
                pitch = Mathf.Clamp(pitch, -10f, 70f);
            }

            float scroll = Input.mouseScrollDelta.y;
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
