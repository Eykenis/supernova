using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Toggles cart towing when the Cart tool is enabled and the player clicks
    /// a CartHandle. The tow preserves the cart's world-space offset and
    /// orientation relative to the moving player.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class FirstPersonCartAttractor : MonoBehaviour
    {
        public const float AttractionModuleUpgradeForce = 400f;

        [Header("Interaction")]
        [SerializeField] private bool deviceEnabled = true;
        [SerializeField] private bool cartTowEnabled;
        [SerializeField] private PerspectiveCameraController perspectiveCamera;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Transform playerRoot;

        [Header("Acquisition")]
        [SerializeField, Min(0.1f)] private float cartHandleAcquisitionDistance = 2f;
        [SerializeField, Min(0.1f)] private float acquisitionDistance = 3.5f;
        [SerializeField] private LayerMask targetLayers = ~0;

        [Header("Physical hold")]
        [SerializeField, Min(0.2f)] private float minimumHoldDistance = 0.5f;
        [SerializeField, Min(0.2f)] private float holdDistance = 2f;
        [SerializeField, Min(0.2f)] private float maximumHoldDistance = 6f;
        [SerializeField, Min(0f)] private float scrollDistancePerStep = 0.35f;
        [Tooltip("Maximum attraction force in newtons. Rigidbody mass determines acceleration.")]
        [SerializeField, Min(0f)] private float attractionForce = 800f;
        [SerializeField, Min(0f)] private float attractionForceUpgrade;
        [Tooltip("Prevents very light objects from being launched by the magnet.")]
        [SerializeField, Min(0f)] private float maximumAttractionAcceleration = 40f;
        [Tooltip("Position spring strength. Lower force is used as the object approaches the hold point.")]
        [SerializeField, Min(0f)] private float positionSpring = 300f;
        [Tooltip("Velocity damping in newtons per metre/second. Total force remains capped.")]
        [SerializeField, Min(0f)] private float forceDamping = 90f;
        [Header("Magnet orientation")]
        [SerializeField, Min(0f)] private float orientationSpring = 55f;
        [SerializeField, Min(0f)] private float orientationDamping = 14f;
        [SerializeField, Min(0f)] private float maximumOrientationTorque = 180f;
        [SerializeField, Min(0f)] private float rotationDegreesPerMouseUnit = 5f;
        [Header("Magnet height control")]
        [Tooltip("World-space height added to the magnet hold point per Mouse Y unit while right mouse is held.")]
        [SerializeField, Min(0f)] private float heightDistancePerMouseUnit = 0.15f;
        [SerializeField, Min(0f)] private float maximumHeightOffset = 3f;
        [Tooltip("Maximum upward force at the height where the object was acquired, in newtons.")]
        [SerializeField, Min(0f)] private float baseMaximumLiftForce = 300f;
        [Tooltip("Reduces maximum upward force as the object's actual height above its acquisition point increases.")]
        [SerializeField, Min(0f)] private float liftForceFalloffPerMeter = 0.6f;
        [SerializeField, Min(0.5f)] private float breakDistance = 8f;

        private readonly RaycastHit[] acquisitionHits = new RaycastHit[32];
        private Rigidbody heldBody;
        private CartHandle heldHandle;
        private ValuableObject heldValuableObject;
        private CharacterController playerController;
        private bool magnetActionActive;
        private Quaternion heldTargetRotation;
        private bool hasHeldTargetRotation;
        private Vector3 cartTowWorldOffset;
        private Vector3 cartHandleLocalDirection;
        private float magnetHeightOffset;
        private float magnetPickupHeight;
        private int cartTowClickConsumedFrame = -1;

        public bool DeviceEnabled => deviceEnabled;
        public bool CartTowEnabled => cartTowEnabled;
        public bool IsHolding => heldBody != null;
        public bool IsTowingCart => heldHandle != null && heldBody != null;
        public bool IsRotatingHeldObject => IsHolding
            && !IsTowingCart
            && Input.GetMouseButton(2);
        public bool IsAdjustingHeldObjectHeight => magnetActionActive
            && IsHolding
            && !IsTowingCart
            && Input.GetMouseButton(1);
        public bool IsManipulatingHeldObject => IsRotatingHeldObject
            || IsAdjustingHeldObjectHeight;
        public bool ConsumedCartTowClickThisFrame =>
            cartTowClickConsumedFrame == Time.frameCount;
        public Rigidbody HeldBody => heldBody;
        public ValuableObject HeldValuableObject => heldValuableObject;
        public bool IsActionActive => magnetActionActive;
        public float HoldDistance => holdDistance;
        public float BaseAttractionForce => Mathf.Max(0f, attractionForce);
        public float AttractionForce =>
            BaseAttractionForce + Mathf.Max(0f, attractionForceUpgrade);
        public float CartHandleAcquisitionDistance => cartHandleAcquisitionDistance;
        public bool CanOperate => deviceEnabled && CanOperateInFirstPerson;
        public bool CanTowCart => cartTowEnabled && CanOperateInFirstPerson;
        public bool ConsumesPrimaryAction => isActiveAndEnabled
            && (CanOperate || CanTowCart);

        public void SetAttractionForceUpgrade(float forceBonus)
        {
            attractionForceUpgrade = Mathf.Max(0f, forceBonus);
        }

        public void SetCartTowEnabled(bool value)
        {
            cartTowEnabled = value;
            if (!cartTowEnabled && IsTowingCart)
                EndHandleTow();
        }

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
            else if (IsAdjustingHeldObjectHeight)
            {
                AdjustMagnetHeight(Input.GetAxis("Mouse Y"));
            }

            if (!Input.GetMouseButtonDown(0)) return;

            if (IsTowingCart)
            {
                EndHandleTow();
                cartTowClickConsumedFrame = Time.frameCount;
                return;
            }

            if (BeginHandleTow())
            {
                cartTowClickConsumedFrame = Time.frameCount;
            }
        }

        public bool BeginHandleTow()
        {
            ResolveReferences();
            if (!isActiveAndEnabled || !CanTowCart)
            {
                return false;
            }

            if (!TryAcquireCartHandle()) return false;

            magnetActionActive = false;
            magnetHeightOffset = 0f;
            return true;
        }

        public bool BeginAttraction()
        {
            ResolveReferences();
            if (!isActiveAndEnabled || !CanOperate)
            {
                EndAttraction();
                return false;
            }
            if (IsTowingCart) return false;

            magnetActionActive = true;
            if (heldBody == null) TryAcquireMagnetTarget();
            return true;
        }

        public void TickAttraction()
        {
            ResolveReferences();
            if (!magnetActionActive || !isActiveAndEnabled || !CanOperate)
            {
                EndAttraction();
                return;
            }

            if (heldBody == null) TryAcquireMagnetTarget();
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

        public void AdjustMagnetHeight(float mouseY)
        {
            if (!magnetActionActive
                || IsTowingCart
                || Mathf.Abs(mouseY) <= 0.001f)
            {
                return;
            }

            float limit = Mathf.Max(0f, maximumHeightOffset);
            magnetHeightOffset = Mathf.Clamp(
                magnetHeightOffset
                    + mouseY * Mathf.Max(0f, heightDistancePerMouseUnit),
                -limit,
                limit);
        }

        public void EndAttraction()
        {
            magnetActionActive = false;
            magnetHeightOffset = 0f;
            hasHeldTargetRotation = false;
            if (!IsTowingCart) Release();
        }

        public void EndHandleTow()
        {
            heldHandle = null;
            hasHeldTargetRotation = false;
            Release();
        }

        private void EndCurrentInteraction()
        {
            if (IsTowingCart)
            {
                EndHandleTow();
            }
            else
            {
                EndAttraction();
            }
        }

        private void FixedUpdate()
        {
            if ((!magnetActionActive && !IsTowingCart) || heldBody == null)
            {
                return;
            }
            bool canContinue = IsTowingCart
                ? CanTowCart
                : CanOperate;
            if (!canContinue || heldBody.isKinematic)
            {
                EndCurrentInteraction();
                return;
            }

            Vector3 desiredPosition = CalculateDesiredHoldPosition();
            Vector3 handlePosition = heldHandle != null
                ? heldHandle.AttachmentPoint.position
                : heldBody.worldCenterOfMass;

            Vector3 error = desiredPosition - handlePosition;
            if (error.sqrMagnitude > breakDistance * breakDistance)
            {
                EndCurrentInteraction();
                return;
            }

            Vector3 targetVelocity = playerController != null
                ? playerController.velocity
                : Vector3.zero;
            Vector3 bodyVelocity = heldBody.velocity;
            Vector3 relativeVelocity = targetVelocity - bodyVelocity;
            Vector3 force = IsTowingCart
                ? CalculateAttractionForce(error, relativeVelocity)
                : CalculateMagnetAttractionForce(
                    error,
                    relativeVelocity,
                    heldBody.worldCenterOfMass.y,
                    heldBody.mass);
            heldBody.AddForceAtPosition(force, handlePosition, ForceMode.Force);

            if (hasHeldTargetRotation)
            {
                if (IsTowingCart)
                {
                    Vector3 directionToPlayer =
                        playerRoot.position - heldBody.worldCenterOfMass;
                    heldTargetRotation = CalculateCartTowTargetRotation(
                        heldTargetRotation,
                        cartHandleLocalDirection,
                        directionToPlayer);
                }

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
            float forceLimit = AttractionForce;
            if (forceLimit <= 0f)
            {
                return Vector3.zero;
            }

            Vector3 force = positionError * Mathf.Max(0f, positionSpring);
            force += relativeVelocity * Mathf.Max(0f, forceDamping);
            return Vector3.ClampMagnitude(force, forceLimit);
        }

        private Quaternion CalculateCartTowTargetRotation(
            Quaternion currentTargetRotation,
            Vector3 handleLocalDirection,
            Vector3 directionToPlayer)
        {
            Vector3 currentHandleDirection =
                currentTargetRotation * handleLocalDirection;
            currentHandleDirection =
                Vector3.ProjectOnPlane(currentHandleDirection, Vector3.up);
            directionToPlayer =
                Vector3.ProjectOnPlane(directionToPlayer, Vector3.up);
            if (currentHandleDirection.sqrMagnitude < 0.0001f
                || directionToPlayer.sqrMagnitude < 0.0001f)
            {
                return currentTargetRotation;
            }

            float yaw = Vector3.SignedAngle(
                currentHandleDirection,
                directionToPlayer,
                Vector3.up);
            return Quaternion.AngleAxis(yaw, Vector3.up)
                * currentTargetRotation;
        }

        private Vector3 CalculateMagnetAttractionForce(
            Vector3 positionError,
            Vector3 relativeVelocity,
            float currentBodyHeight,
            float bodyMass)
        {
            float spring = Mathf.Max(0f, positionSpring);
            float damping = Mathf.Max(0f, forceDamping);
            Vector3 force = positionError * spring
                + relativeVelocity * damping;
            float maximumLiftForce = CalculateMaximumLiftForce(
                currentBodyHeight);
            float requestedVerticalSpring = positionError.y * spring;
            if (requestedVerticalSpring > maximumLiftForce)
            {
                // Once lift is saturated, normal damping would be clipped away by
                // the lift cap. Retain only the part that removes upward kinetic
                // energy, and never let damping exceed the available lift while
                // the object is falling.
                float upwardMotionDamping = Mathf.Min(
                    0f,
                    relativeVelocity.y * damping);
                force.y = maximumLiftForce + upwardMotionDamping;
            }

            force = Vector3.ClampMagnitude(
                force,
                AttractionForce);
            force = Vector3.ClampMagnitude(
                force,
                Mathf.Max(0f, bodyMass)
                    * Mathf.Max(0f, maximumAttractionAcceleration));
            return LimitMagnetLiftForce(force, currentBodyHeight);
        }

        private Vector3 LimitMagnetLiftForce(
            Vector3 force,
            float currentBodyHeight)
        {
            float maximumLiftForce = CalculateMaximumLiftForce(
                currentBodyHeight);
            if (force.y > maximumLiftForce)
            {
                force.y = maximumLiftForce;
            }
            return force;
        }

        private float CalculateMaximumLiftForce(float currentBodyHeight)
        {
            float liftedHeight = Mathf.Max(
                0f,
                currentBodyHeight - magnetPickupHeight);
            float falloff = Mathf.Max(0f, liftForceFalloffPerMeter);
            return Mathf.Max(0f, baseMaximumLiftForce)
                / (1f + liftedHeight * falloff);
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
            heldValuableObject = null;
            cartTowWorldOffset = Vector3.zero;
            cartHandleLocalDirection = Vector3.zero;
            magnetHeightOffset = 0f;
            magnetPickupHeight = 0f;
        }

        private bool TryAcquireCartHandle()
        {
            if (viewCamera == null) return false;

            Transform cameraTransform = viewCamera.transform;
            int count = Physics.RaycastNonAlloc(
                cameraTransform.position,
                cameraTransform.forward,
                acquisitionHits,
                Mathf.Max(0.1f, cartHandleAcquisitionDistance),
                targetLayers,
                QueryTriggerInteraction.Ignore);

            float handleHitDistance = float.PositiveInfinity;
            CartHandle focusedHandle = null;
            Rigidbody focusedBody = null;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = acquisitionHits[i];
                Collider collider = hit.collider;
                if (collider == null || IsOwnedByPlayer(collider.transform)) continue;
                CartHandle handle = collider.GetComponentInParent<CartHandle>();
                Rigidbody body = handle != null ? handle.CartBody : null;
                if (body == null || body.isKinematic) continue;
                if (hit.distance >= handleHitDistance) continue;

                handleHitDistance = hit.distance;
                focusedHandle = handle;
                focusedBody = body;
            }

            if (focusedHandle == null || focusedBody == null) return false;

            // The authored cart uses compound colliders. From normal camera height
            // its own tray can be hit just before the handle collider. Those colliders
            // must not hide their own handle, while unrelated geometry still blocks it.
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = acquisitionHits[i];
                Collider collider = hit.collider;
                if (collider == null
                    || IsOwnedByPlayer(collider.transform)
                    || hit.distance >= handleHitDistance
                    || BelongsToBody(collider, focusedBody))
                {
                    continue;
                }

                return false;
            }

            heldHandle = focusedHandle;
            heldBody = focusedBody;
            ResolveHeldValuableObject();
            heldTargetRotation = focusedBody.rotation;
            hasHeldTargetRotation = true;
            cartTowWorldOffset =
                focusedHandle.AttachmentPoint.position - playerRoot.position;
            Vector3 handleDirection =
                focusedHandle.AttachmentPoint.position
                - focusedBody.worldCenterOfMass;
            cartHandleLocalDirection =
                Quaternion.Inverse(focusedBody.rotation) * handleDirection;
            focusedBody.WakeUp();
            return true;
        }

        private bool TryAcquireMagnetTarget()
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

            Rigidbody body = focusedCollider.attachedRigidbody;
            if (body == null
                || body.isKinematic
                || body.GetComponentInChildren<CartHandle>(true) != null)
            {
                return false;
            }

            heldHandle = null;
            heldBody = body;
            ResolveHeldValuableObject();
            heldTargetRotation = body.rotation;
            hasHeldTargetRotation = true;
            magnetHeightOffset = 0f;
            magnetPickupHeight = body.worldCenterOfMass.y;
            body.WakeUp();
            return true;
        }

        private void ResolveHeldValuableObject()
        {
            heldValuableObject = heldBody != null
                ? heldBody.GetComponent<ValuableObject>()
                : null;
            if (heldValuableObject == null)
            {
                heldValuableObject =
                    heldBody != null
                        ? heldBody.GetComponentInChildren<ValuableObject>(true)
                        : null;
            }
        }

        private static bool BelongsToBody(Collider collider, Rigidbody body)
        {
            return collider != null
                && body != null
                && (collider.attachedRigidbody == body
                    || collider.transform == body.transform
                    || collider.transform.IsChildOf(body.transform));
        }

        private bool CanOperateInFirstPerson => perspectiveCamera != null
            && perspectiveCamera.CurrentMode == PlayerViewMode.FirstPerson
            && viewCamera != null
            && playerRoot != null;


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
            EndHandleTow();
        }

        private void OnValidate()
        {
            cartHandleAcquisitionDistance =
                Mathf.Max(0.1f, cartHandleAcquisitionDistance);
            minimumHoldDistance = Mathf.Max(0.2f, minimumHoldDistance);
            maximumHoldDistance = Mathf.Max(minimumHoldDistance, maximumHoldDistance);
            holdDistance = Mathf.Clamp(holdDistance, minimumHoldDistance, maximumHoldDistance);
            scrollDistancePerStep = Mathf.Max(0f, scrollDistancePerStep);
            attractionForce = Mathf.Max(0f, attractionForce);
            attractionForceUpgrade =
                Mathf.Max(0f, attractionForceUpgrade);
            maximumAttractionAcceleration =
                Mathf.Max(0f, maximumAttractionAcceleration);
            positionSpring = Mathf.Max(0f, positionSpring);
            forceDamping = Mathf.Max(0f, forceDamping);
            orientationSpring = Mathf.Max(0f, orientationSpring);
            orientationDamping = Mathf.Max(0f, orientationDamping);
            maximumOrientationTorque = Mathf.Max(0f, maximumOrientationTorque);
            rotationDegreesPerMouseUnit =
                Mathf.Max(0f, rotationDegreesPerMouseUnit);
            heightDistancePerMouseUnit =
                Mathf.Max(0f, heightDistancePerMouseUnit);
            maximumHeightOffset = Mathf.Max(0f, maximumHeightOffset);
            baseMaximumLiftForce = Mathf.Max(0f, baseMaximumLiftForce);
            liftForceFalloffPerMeter =
                Mathf.Max(0f, liftForceFalloffPerMeter);
            breakDistance = Mathf.Max(0.5f, breakDistance);
        }

        private Vector3 CalculateDesiredHoldPosition()
        {
            if (IsTowingCart)
            {
                return playerRoot.position + cartTowWorldOffset;
            }

            return viewCamera.transform.position
                + viewCamera.transform.forward.normalized
                * Mathf.Max(0.2f, holdDistance)
                + Vector3.up * magnetHeightOffset;
        }
    }
}
