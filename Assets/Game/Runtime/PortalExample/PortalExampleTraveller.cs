using System;
using Supernova.Gameplay;
using UnityEngine;
using UnityEngine.Serialization;

namespace Supernova.PortalExample
{
    [DisallowMultipleComponent]
    public sealed class PortalExampleTraveller : MonoBehaviour
    {
        [FormerlySerializedAs("reentryDelay")]
        [SerializeField, Min(0.1f)] private float teleportCooldown = 0.75f;
        [SerializeField, Min(0f)] private float exitOffset = 0.09f;

        private Rigidbody body;
        private CharacterController characterController;
        private PortalExampleFirstPersonController firstPersonController;
        private PerspectiveCameraController perspectiveCameraController;
        private PortalExampleSeamlessVisual seamlessVisual;
        private float nextTeleportTime;

        public float TeleportCooldown => Mathf.Max(0.1f, teleportCooldown);
        public bool CanTeleport => Time.unscaledTime >= nextTeleportTime;
        public bool IsTraversingPortal => seamlessVisual != null
            && seamlessVisual.IsActive;
        public event Action<PortalExampleGate, PortalExampleGate> Teleported;

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

        internal bool UsesCharacterController
        {
            get
            {
                ResolveComponents();
                return characterController != null;
            }
        }

        internal bool TryGetCharacterController(
            out CharacterController resolvedController)
        {
            ResolveComponents();
            resolvedController = characterController;
            return resolvedController != null;
        }

        internal bool TryGetRigidbody(out Rigidbody resolvedBody)
        {
            ResolveComponents();
            resolvedBody = body;
            return resolvedBody != null;
        }

        internal void BeginPortalTraversal(
            PortalExampleGate source,
            PortalExampleGate destination)
        {
            if (!TryGetRigidbody(out Rigidbody resolvedBody))
            {
                return;
            }
            if (seamlessVisual == null)
            {
                seamlessVisual = new PortalExampleSeamlessVisual(
                    this,
                    resolvedBody);
            }
            seamlessVisual.Begin(
                source,
                destination,
                source != null ? source.SeamlessClipShader : null);
        }

        internal void CompletePortalTraversal(PortalExampleGate exitedGate)
        {
            if (seamlessVisual != null
                && seamlessVisual.UsesGate(exitedGate))
            {
                seamlessVisual.End();
            }
        }

        internal void CancelPortalTraversal(PortalExampleGate disabledGate)
        {
            CompletePortalTraversal(disabledGate);
        }

        public bool Teleport(
            PortalExampleGate source,
            PortalExampleGate destination)
        {
            if (!CanTeleport || source == null || destination == null)
            {
                return false;
            }

            // Arm the traveller-level guard before moving or synchronizing any
            // collider. This prevents destination trigger callbacks from sending
            // the same player straight back during the transfer frame.
            nextTeleportTime = Time.unscaledTime + TeleportCooldown;

            ResolveComponents();
            bool useSeamlessRigidbodyTransfer = body != null
                && seamlessVisual != null
                && seamlessVisual.IsActive;
            float resolvedExitOffset = useSeamlessRigidbodyTransfer
                ? 0.001f
                : exitOffset;

            Matrix4x4 mapping = PortalExampleSpace.BuildMapping(
                source.transform,
                destination.transform);
            Camera controlledCamera = perspectiveCameraController != null
                ? perspectiveCameraController.ControlledCamera
                : null;
            bool hasCameraTransition = controlledCamera != null;
            Vector3 mappedCameraPosition = hasCameraTransition
                ? mapping.MultiplyPoint3x4(controlledCamera.transform.position)
                    + destination.transform.forward * resolvedExitOffset
                : Vector3.zero;
            Quaternion mappedCameraRotation = hasCameraTransition
                ? PortalExampleSpace.MapRotation(
                    mapping,
                    controlledCamera.transform.rotation)
                : Quaternion.identity;
            Vector3 position = mapping.MultiplyPoint3x4(transform.position)
                + destination.transform.forward * resolvedExitOffset;
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

                if (useSeamlessRigidbodyTransfer)
                {
                    seamlessVisual.CommitPhysicalTransfer(
                        source,
                        destination);
                }
                else
                {
                    Physics.SyncTransforms();
                    Vector3 exitCorrection = CalculateRigidbodyExitCorrection(
                        destination,
                        body);
                    if (exitCorrection.sqrMagnitude > 0f)
                    {
                        position += exitCorrection;
                        transform.position = position;
                        body.position = position;
                        if (hasCameraTransition)
                        {
                            mappedCameraPosition += exitCorrection;
                        }
                    }
                }
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

            Physics.SyncTransforms();
            Teleported?.Invoke(source, destination);
            return true;
        }

        private void LateUpdate()
        {
            if (seamlessVisual != null && seamlessVisual.IsActive)
            {
                seamlessVisual.UpdateVisuals();
            }
        }

        private void OnDisable()
        {
            seamlessVisual?.End();
        }

        private void OnDestroy()
        {
            seamlessVisual?.End();
        }

        private Vector3 CalculateRigidbodyExitCorrection(
            PortalExampleGate destination,
            Rigidbody resolvedBody)
        {
            Vector3 normal = destination.transform.forward;
            Vector3 planePosition = destination.transform.position;
            float minimumSide = float.PositiveInfinity;
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                Collider collider = colliders[index];
                if (collider == null || !collider.enabled
                    || !collider.gameObject.activeInHierarchy
                    || collider.attachedRigidbody != resolvedBody)
                {
                    continue;
                }

                Bounds bounds = collider.bounds;
                Vector3 extent = bounds.extents;
                float projectedExtent = Mathf.Abs(normal.x) * extent.x
                    + Mathf.Abs(normal.y) * extent.y
                    + Mathf.Abs(normal.z) * extent.z;
                float centerSide = Vector3.Dot(
                    bounds.center - planePosition,
                    normal);
                minimumSide = Mathf.Min(
                    minimumSide,
                    centerSide - projectedExtent);
            }

            if (float.IsPositiveInfinity(minimumSide)
                || minimumSide >= exitOffset)
            {
                return Vector3.zero;
            }
            return normal * (exitOffset - minimumSide);
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
