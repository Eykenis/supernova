using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Toggles cart towing when the player clicks a CartHandle. The handle, rather
    /// than the cart centre, is pulled to a socket beside the player.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class FirstPersonCartAttractor : MonoBehaviour
    {
        [Header("Interaction")]
        [SerializeField] private bool deviceEnabled = true;
        [SerializeField] private PerspectiveCameraController perspectiveCamera;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Transform playerRoot;

        [Header("Acquisition")]
        [SerializeField, Min(0.1f)] private float acquisitionDistance = 3.5f;
        [SerializeField] private LayerMask targetLayers = ~0;

        [Header("Physical hold")]
        [SerializeField] private Vector3 playerSocketOffset = new Vector3(0.85f, 0.75f, 0.35f);
        [SerializeField, Min(0.2f)] private float minimumHoldDistance = 0.5f;
        [SerializeField, Min(0.2f)] private float holdDistance = 2f;
        [SerializeField, Min(0.2f)] private float maximumHoldDistance = 6f;
        [SerializeField, Min(0f)] private float scrollDistancePerStep = 0.35f;
        [Tooltip("Maximum attraction force in newtons. Rigidbody mass determines acceleration.")]
        [SerializeField, Min(0f)] private float attractionForce = 800f;
        [Tooltip("Prevents very light objects from being launched by the magnet.")]
        [SerializeField, Min(0f)] private float maximumAttractionAcceleration = 40f;
        [Tooltip("Position spring strength. Lower force is used as the object approaches the hold point.")]
        [SerializeField, Min(0f)] private float positionSpring = 300f;
        [Tooltip("Velocity damping in newtons per metre/second. Total force remains capped.")]
        [SerializeField, Min(0f)] private float forceDamping = 90f;
        [SerializeField, Min(0f)] private float yawTorque = 120f;
        [SerializeField, Min(0f)] private float yawDamping = 20f;
        [Header("Magnet orientation")]
        [SerializeField, Min(0f)] private float orientationSpring = 55f;
        [SerializeField, Min(0f)] private float orientationDamping = 14f;
        [SerializeField, Min(0f)] private float maximumOrientationTorque = 180f;
        [SerializeField, Min(0f)] private float rotationDegreesPerMouseUnit = 5f;
        [SerializeField, Min(0.5f)] private float breakDistance = 8f;

        private readonly RaycastHit[] acquisitionHits = new RaycastHit[32];
        private Rigidbody heldBody;
        private CartHandle heldHandle;
        private CharacterController playerController;
        private bool actionActive;
        private Quaternion heldTargetRotation;
        private bool hasHeldTargetRotation;

        public bool DeviceEnabled => deviceEnabled;
        public bool IsHolding => heldBody != null;
        public bool IsTowingCart => heldHandle != null && heldBody != null;
        public bool IsRotatingHeldObject => IsHolding
            && !IsTowingCart
            && Input.GetMouseButton(2);
        public Rigidbody HeldBody => heldBody;
        public bool IsActionActive => actionActive;
        public float HoldDistance => holdDistance;
        public float AttractionForce => attractionForce;
        public bool CanOperate => deviceEnabled && CanOperateInFirstPerson;
        public bool ConsumesPrimaryAction => isActiveAndEnabled
            && CanOperate;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (!Application.isPlaying
                || Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            if (IsRotatingHeldObject)
            {
                UpdateHeldTargetRotation(
                    Input.GetAxis("Mouse X"),
                    Input.GetAxis("Mouse Y"));
            }

            if (!Input.GetMouseButtonDown(0)) return;

            if (IsTowingCart)
            {
                EndAttraction();
                return;
            }

            BeginHandleTow();
        }

        public bool BeginHandleTow()
        {
            ResolveReferences();
            if (!isActiveAndEnabled || !CanOperateInFirstPerson)
            {
                return false;
            }

            actionActive = TryAcquireTarget(true);
            return actionActive;
        }

        public bool BeginAttraction()
        {
            ResolveReferences();
            if (!isActiveAndEnabled || !CanOperate)
            {
                EndAttraction();
                return false;
            }

            actionActive = true;
            if (heldBody == null) TryAcquireTarget(false);
            return true;
        }

        public void TickAttraction()
        {
            ResolveReferences();
            bool canContinue = IsTowingCart
                ? CanOperateInFirstPerson
                : CanOperate;
            if (!actionActive || !isActiveAndEnabled || !canContinue)
            {
                EndAttraction();
                return;
            }

            if (heldBody == null && !IsTowingCart) TryAcquireTarget(false);
        }

        public void TickAttraction(float scrollSteps)
        {
            if (!IsTowingCart) AdjustHoldDistance(scrollSteps);
            TickAttraction();
        }

        public void AdjustHoldDistance(float scrollSteps)
        {
            if (Mathf.Abs(scrollSteps) <= 0.001f) return;
            float minimum = Mathf.Max(0.2f, minimumHoldDistance);
            float maximum = Mathf.Max(minimum, maximumHoldDistance);
            holdDistance = Mathf.Clamp(
                holdDistance + scrollSteps * Mathf.Max(0f, scrollDistancePerStep),
                minimum,
                maximum);
        }

        public void EndAttraction()
        {
            actionActive = false;
            heldHandle = null;
            hasHeldTargetRotation = false;
            Release();
        }

        private void FixedUpdate()
        {
            if (!actionActive || heldBody == null) return;
            bool canContinue = IsTowingCart
                ? CanOperateInFirstPerson
                : CanOperate;
            if (!canContinue || heldBody.isKinematic)
            {
                EndAttraction();
                return;
            }

            Vector3 forward = GetPlanarForward();
            Vector3 desiredPosition = IsTowingCart
                ? playerRoot.TransformPoint(playerSocketOffset)
                : viewCamera.transform.position
                    + viewCamera.transform.forward.normalized
                    * Mathf.Max(0.2f, holdDistance);
            Vector3 handlePosition = heldHandle != null
                ? heldHandle.AttachmentPoint.position
                : heldBody.worldCenterOfMass;

            Vector3 error = desiredPosition - handlePosition;
            if (error.sqrMagnitude > breakDistance * breakDistance)
            {
                EndAttraction();
                return;
            }

            Vector3 targetVelocity = playerController != null
                ? playerController.velocity
                : Vector3.zero;
            Vector3 bodyVelocity = heldBody.velocity;
            Vector3 force = CalculateAttractionForce(
                error,
                targetVelocity - bodyVelocity);
            if (!IsTowingCart)
            {
                force = Vector3.ClampMagnitude(
                    force,
                    heldBody.mass * Mathf.Max(0f, maximumAttractionAcceleration));
            }
            heldBody.AddForceAtPosition(force, handlePosition, ForceMode.Force);

            if (IsTowingCart)
            {
                float yawError = Vector3.SignedAngle(
                    heldBody.transform.forward,
                    forward,
                    Vector3.up) * Mathf.Deg2Rad;
                float yawVelocity = Vector3.Dot(heldBody.angularVelocity, Vector3.up);
                float torque = yawError * Mathf.Max(0f, yawTorque)
                    - yawVelocity * Mathf.Max(0f, yawDamping);
                torque = Mathf.Clamp(
                    torque,
                    -Mathf.Max(0f, yawTorque),
                    Mathf.Max(0f, yawTorque));
                heldBody.AddTorque(Vector3.up * torque, ForceMode.Force);
            }
            else if (hasHeldTargetRotation)
            {
                heldBody.AddTorque(
                    CalculateOrientationTorque(
                        heldBody.rotation,
                        heldBody.angularVelocity),
                    ForceMode.Acceleration);
            }
        }

        private Vector3 CalculateAttractionForce(
            Vector3 positionError,
            Vector3 relativeVelocity)
        {
            float forceLimit = Mathf.Max(0f, attractionForce);
            if (forceLimit <= 0f)
            {
                return Vector3.zero;
            }

            Vector3 force = positionError * Mathf.Max(0f, positionSpring);
            force += relativeVelocity * Mathf.Max(0f, forceDamping);
            return Vector3.ClampMagnitude(force, forceLimit);
        }

        private Vector3 CalculateOrientationTorque(
            Quaternion currentRotation,
            Vector3 angularVelocity)
        {
            Quaternion error = heldTargetRotation
                * Quaternion.Inverse(currentRotation);
            error.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (axis.sqrMagnitude < 0.0001f || float.IsNaN(axis.x))
            {
                axis = Vector3.zero;
            }

            Vector3 torque = axis.normalized
                * (angle * Mathf.Deg2Rad * Mathf.Max(0f, orientationSpring))
                - angularVelocity * Mathf.Max(0f, orientationDamping);
            return Vector3.ClampMagnitude(
                torque,
                Mathf.Max(0f, maximumOrientationTorque));
        }

        private void UpdateHeldTargetRotation(float mouseX, float mouseY)
        {
            if (!hasHeldTargetRotation || viewCamera == null) return;

            float horizontal = Mathf.Abs(mouseX);
            float vertical = Mathf.Abs(mouseY);
            float degrees = Mathf.Max(0f, rotationDegreesPerMouseUnit);
            // One dominant axis per frame produces four deliberate directions:
            // left/right yaw and up/down pitch, without accidental roll.
            if (horizontal >= vertical && horizontal > 0.001f)
            {
                heldTargetRotation = Quaternion.AngleAxis(
                    mouseX * degrees,
                    viewCamera.transform.up) * heldTargetRotation;
            }
            else if (vertical > 0.001f)
            {
                heldTargetRotation = Quaternion.AngleAxis(
                    -mouseY * degrees,
                    viewCamera.transform.right) * heldTargetRotation;
            }
        }

        public void SetDeviceEnabled(bool value)
        {
            deviceEnabled = value;
            if (!value && !IsTowingCart) EndAttraction();
        }

        public void Release()
        {
            heldBody = null;
        }

        private bool TryAcquireTarget(bool requireCartHandle)
        {
            if (viewCamera == null) return false;

            Transform cameraTransform = viewCamera.transform;
            int count = Physics.RaycastNonAlloc(
                cameraTransform.position,
                cameraTransform.forward,
                acquisitionHits,
                Mathf.Max(0.1f, acquisitionDistance),
                targetLayers,
                QueryTriggerInteraction.Ignore);

            float focusedHitDistance = float.PositiveInfinity;
            Collider focusedCollider = null;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = acquisitionHits[i];
                Collider collider = hit.collider;
                if (collider == null || IsOwnedByPlayer(collider.transform)) continue;
                if (hit.distance >= focusedHitDistance) continue;

                focusedHitDistance = hit.distance;
                focusedCollider = collider;
            }

            if (focusedCollider == null) return false;

            CartHandle handle = focusedCollider.GetComponentInParent<CartHandle>();
            Rigidbody body = requireCartHandle
                ? (handle != null ? handle.CartBody : null)
                : focusedCollider.attachedRigidbody;
            if (body == null || body.isKinematic) return false;
            bool bodyIsCart = body.GetComponentInChildren<CartHandle>(true) != null;
            if (requireCartHandle ? handle == null : bodyIsCart) return false;

            heldHandle = requireCartHandle ? handle : null;
            heldBody = body;
            heldTargetRotation = body.rotation;
            hasHeldTargetRotation = !requireCartHandle;
            heldBody.WakeUp();
            return true;
        }

        private bool CanOperateInFirstPerson => perspectiveCamera != null
            && perspectiveCamera.CurrentMode == PlayerViewMode.FirstPerson
            && viewCamera != null
            && playerRoot != null;

        private Vector3 GetPlanarForward()
        {
            Vector3 forward = viewCamera != null
                ? Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up)
                : Vector3.zero;
            if (forward.sqrMagnitude < 0.0001f && playerRoot != null)
                forward = Vector3.ProjectOnPlane(playerRoot.forward, Vector3.up);
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        private bool IsOwnedByPlayer(Transform candidate)
        {
            return playerRoot != null
                && (candidate == playerRoot || candidate.IsChildOf(playerRoot));
        }

        private void ResolveReferences()
        {
            if (playerRoot == null) playerRoot = transform;
            if (playerController == null)
                playerController = playerRoot.GetComponent<CharacterController>();
            if (perspectiveCamera == null)
                perspectiveCamera = playerRoot.GetComponentInChildren<PerspectiveCameraController>(true);
            if (viewCamera == null && perspectiveCamera != null)
                viewCamera = perspectiveCamera.ControlledCamera;
            if (viewCamera == null)
                viewCamera = playerRoot.GetComponentInChildren<Camera>(true);
        }

        private void OnDisable()
        {
            EndAttraction();
        }

        private void OnValidate()
        {
            minimumHoldDistance = Mathf.Max(0.2f, minimumHoldDistance);
            maximumHoldDistance = Mathf.Max(minimumHoldDistance, maximumHoldDistance);
            holdDistance = Mathf.Clamp(holdDistance, minimumHoldDistance, maximumHoldDistance);
            scrollDistancePerStep = Mathf.Max(0f, scrollDistancePerStep);
            attractionForce = Mathf.Max(0f, attractionForce);
            maximumAttractionAcceleration =
                Mathf.Max(0f, maximumAttractionAcceleration);
            positionSpring = Mathf.Max(0f, positionSpring);
            forceDamping = Mathf.Max(0f, forceDamping);
            yawTorque = Mathf.Max(0f, yawTorque);
            yawDamping = Mathf.Max(0f, yawDamping);
            orientationSpring = Mathf.Max(0f, orientationSpring);
            orientationDamping = Mathf.Max(0f, orientationDamping);
            maximumOrientationTorque = Mathf.Max(0f, maximumOrientationTorque);
            rotationDegreesPerMouseUnit =
                Mathf.Max(0f, rotationDegreesPerMouseUnit);
            breakDistance = Mathf.Max(0.5f, breakDistance);
        }
    }
}
