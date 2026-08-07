using Supernova.Gameplay;
using UnityEngine;

namespace Supernova.PortalExample
{
    [DisallowMultipleComponent]
    public sealed class PortalExampleTraveller : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float reentryDelay = 0.12f;
        [SerializeField, Min(0f)] private float exitOffset = 0.09f;

        private Rigidbody body;
        private CharacterController characterController;
        private PortalExampleFirstPersonController firstPersonController;
        private PerspectiveCameraController perspectiveCameraController;
        private float nextTeleportTime;

        public bool CanTeleport => Time.time >= nextTeleportTime;

        private void Awake()
        {
            ResolveComponents();
        }

        internal bool TryGetWorldVelocity(out Vector3 velocity)
        {
            ResolveComponents();
            if (body != null)
            {
                velocity = body.velocity;
                return true;
            }

            if (characterController != null)
            {
                velocity = characterController.velocity;
                return true;
            }

            velocity = Vector3.zero;
            return false;
        }

        public bool Teleport(
            PortalExampleGate source,
            PortalExampleGate destination)
        {
            if (!CanTeleport || source == null || destination == null)
            {
                return false;
            }

            ResolveComponents();

            Matrix4x4 mapping = PortalExampleSpace.BuildMapping(
                source.transform,
                destination.transform);
            Camera controlledCamera = perspectiveCameraController != null
                ? perspectiveCameraController.ControlledCamera
                : null;
            bool hasCameraTransition = controlledCamera != null;
            Vector3 mappedCameraPosition = hasCameraTransition
                ? mapping.MultiplyPoint3x4(controlledCamera.transform.position)
                    + destination.transform.forward * exitOffset
                : Vector3.zero;
            Quaternion mappedCameraRotation = hasCameraTransition
                ? PortalExampleSpace.MapRotation(
                    mapping,
                    controlledCamera.transform.rotation)
                : Quaternion.identity;
            Vector3 position = mapping.MultiplyPoint3x4(transform.position)
                + destination.transform.forward * exitOffset;
            Quaternion rotation = PortalExampleSpace.MapRotation(
                mapping,
                transform.rotation);

            if (characterController != null)
            {
                Vector3 uprightForward = Vector3.ProjectOnPlane(
                    rotation * Vector3.forward,
                    Vector3.up);
                if (uprightForward.sqrMagnitude <= 0.001f)
                {
                    uprightForward = Vector3.ProjectOnPlane(
                        destination.transform.up,
                        Vector3.up);
                }
                if (uprightForward.sqrMagnitude <= 0.001f)
                {
                    uprightForward = Vector3.ProjectOnPlane(
                        transform.forward,
                        Vector3.up);
                }
                if (uprightForward.sqrMagnitude > 0.001f)
                {
                    rotation = Quaternion.LookRotation(
                        uprightForward.normalized,
                        Vector3.up);
                }
            }

            Vector3 velocity = body != null ? body.velocity : Vector3.zero;
            Vector3 angularVelocity =
                body != null ? body.angularVelocity : Vector3.zero;
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
                body.velocity = mapping.MultiplyVector(velocity);
                body.angularVelocity = mapping.MultiplyVector(angularVelocity);
            }

            if (firstPersonController != null)
            {
                firstPersonController.MapVelocity(mapping);
            }

            if (controllerWasEnabled)
            {
                characterController.enabled = true;
            }

            if (hasCameraTransition)
            {
                perspectiveCameraController.BeginPortalTransition(
                    mappedCameraPosition,
                    mappedCameraRotation);
            }

            nextTeleportTime = Time.time + reentryDelay;
            Physics.SyncTransforms();
            return true;
        }

        private void ResolveComponents()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }
            if (firstPersonController == null)
            {
                firstPersonController =
                    GetComponent<PortalExampleFirstPersonController>();
            }
            if (perspectiveCameraController == null)
            {
                perspectiveCameraController =
                    GetComponentInChildren<PerspectiveCameraController>(true);
            }
        }
    }
}
