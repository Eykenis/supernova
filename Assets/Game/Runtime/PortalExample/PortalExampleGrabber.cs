using UnityEngine;

namespace Supernova.PortalExample
{
    [DisallowMultipleComponent]
    public sealed class PortalExampleGrabber : MonoBehaviour
    {
        [SerializeField] private Transform view;
        [SerializeField, Min(0.5f)] private float reach = 3.2f;
        [SerializeField, Min(0.5f)] private float holdDistance = 2.1f;
        [SerializeField, Min(1f)] private float followStrength = 16f;
        [SerializeField, Min(0f)] private float throwSpeed = 10f;

        private Rigidbody heldBody;
        private bool heldBodyUsedGravity;
        private float heldBodyDrag;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (heldBody == null)
                {
                    TryPickup();
                }
                else
                {
                    Release(Vector3.zero);
                }
            }

            if (heldBody != null && Input.GetMouseButtonDown(0))
            {
                Release(view.forward * throwSpeed);
            }
        }

        private void FixedUpdate()
        {
            if (heldBody == null || view == null)
            {
                return;
            }

            Vector3 target = view.position + view.forward * holdDistance;
            Vector3 delta = target - heldBody.worldCenterOfMass;
            if (delta.sqrMagnitude > reach * reach * 4f)
            {
                Release(Vector3.zero);
                return;
            }

            heldBody.velocity = delta * followStrength;
            heldBody.angularVelocity *= 0.85f;
        }

        private void OnDisable()
        {
            if (heldBody != null)
            {
                Release(Vector3.zero);
            }
        }

        private void TryPickup()
        {
            if (view == null || !Physics.Raycast(
                    view.position,
                    view.forward,
                    out RaycastHit hit,
                    reach,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return;
            }

            PortalExamplePickup pickup =
                hit.collider.GetComponentInParent<PortalExamplePickup>();
            if (pickup == null)
            {
                return;
            }

            heldBody = pickup.GetComponent<Rigidbody>();
            heldBodyUsedGravity = heldBody.useGravity;
            heldBodyDrag = heldBody.drag;
            heldBody.useGravity = false;
            heldBody.drag = 8f;
            heldBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        private void Release(Vector3 launchVelocity)
        {
            if (heldBody == null)
            {
                return;
            }

            heldBody.useGravity = heldBodyUsedGravity;
            heldBody.drag = heldBodyDrag;
            heldBody.velocity += launchVelocity;
            heldBody = null;
        }
    }
}
