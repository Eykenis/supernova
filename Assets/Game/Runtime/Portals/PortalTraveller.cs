using System.Collections;
using UnityEngine;

namespace Supernova.Portals
{
    [DisallowMultipleComponent]
    public sealed class PortalTraveller : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float reentryDelay = 0.12f;
        [SerializeField, Min(0f)] private float exitOffset = 0.08f;

        private Rigidbody body;
        private CharacterController characterController;

        public bool IsTeleporting { get; private set; }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            characterController = GetComponent<CharacterController>();
        }

        public void Teleport(Portal source, Portal destination)
        {
            if (IsTeleporting || source == null || destination == null)
            {
                return;
            }

            Matrix4x4 mapping = source.GetMappingMatrix(destination);
            Vector3 position = mapping.MultiplyPoint3x4(transform.position)
                + destination.transform.forward * exitOffset;
            Vector3 forward = mapping.MultiplyVector(transform.forward);
            Vector3 up = mapping.MultiplyVector(transform.up);
            Quaternion rotation = Quaternion.LookRotation(forward, up);

            Vector3 velocity = body != null ? body.velocity : Vector3.zero;
            Vector3 angularVelocity =
                body != null ? body.angularVelocity : Vector3.zero;

            bool controllerWasEnabled =
                characterController != null && characterController.enabled;
            if (controllerWasEnabled)
            {
                characterController.enabled = false;
            }

            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
                body.velocity = mapping.MultiplyVector(velocity);
                body.angularVelocity = mapping.MultiplyVector(angularVelocity);
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }

            if (controllerWasEnabled)
            {
                characterController.enabled = true;
            }

            StartCoroutine(ReleaseTeleportLock());
        }

        private IEnumerator ReleaseTeleportLock()
        {
            IsTeleporting = true;
            yield return new WaitForSeconds(reentryDelay);
            IsTeleporting = false;
        }
    }
}
