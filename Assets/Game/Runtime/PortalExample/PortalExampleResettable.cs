using UnityEngine;

namespace Supernova.PortalExample
{
    [DisallowMultipleComponent]
    public sealed class PortalExampleResettable : MonoBehaviour
    {
        [SerializeField] private Transform resetPoint;
        [SerializeField] private float resetBelowHeight = -8f;

        private Rigidbody body;
        private CharacterController characterController;
        private PortalExampleFirstPersonController firstPersonController;
        private Vector3 initialPosition;
        private Quaternion initialRotation;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            characterController = GetComponent<CharacterController>();
            firstPersonController =
                GetComponent<PortalExampleFirstPersonController>();
            initialPosition = transform.position;
            initialRotation = transform.rotation;
        }

        private void Update()
        {
            if (transform.position.y < resetBelowHeight)
            {
                ResetNow();
            }
        }

        public void ResetNow()
        {
            Vector3 position = resetPoint != null
                ? resetPoint.position
                : initialPosition;
            Quaternion rotation = resetPoint != null
                ? resetPoint.rotation
                : initialRotation;
            bool controllerWasEnabled =
                characterController != null && characterController.enabled;

            if (controllerWasEnabled)
            {
                characterController.enabled = false;
            }

            transform.SetPositionAndRotation(position, rotation);

            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            if (firstPersonController != null)
            {
                firstPersonController.ClearVelocity();
            }

            if (controllerWasEnabled)
            {
                characterController.enabled = true;
            }

            Physics.SyncTransforms();
        }
    }
}
