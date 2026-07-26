using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Holds an attractable Rigidbody in front of the first-person player using only
    /// spring/damper forces. The target remains dynamic and keeps normal collisions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FirstPersonCartAttractor : MonoBehaviour
    {
        [Header("Device")]
        [SerializeField] private bool deviceEnabled = true;
        [SerializeField] private PerspectiveCameraController perspectiveCamera;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Transform playerRoot;

        [Header("Acquisition")]
        [SerializeField, Min(0.1f)] private float acquisitionDistance = 3.5f;
        [SerializeField] private LayerMask targetLayers = ~0;

        [Header("Soft hold")]
        [SerializeField, Min(0.2f)] private float holdDistance = 2f;
        [SerializeField, Min(0f)] private float positionSpring = 22f;
        [SerializeField, Min(0f)] private float positionDamping = 8f;
        [SerializeField, Min(0f)] private float maximumAcceleration = 45f;
        [SerializeField, Min(0f)] private float yawSpring = 9f;
        [SerializeField, Min(0f)] private float yawDamping = 4f;
        [SerializeField, Min(0f)] private float maximumAngularAcceleration = 18f;
        [SerializeField, Min(0.5f)] private float breakDistance = 5f;

        private readonly RaycastHit[] acquisitionHits = new RaycastHit[32];
        private PhysicsAttractable heldTarget;
        private Rigidbody heldBody;
        private CharacterController playerController;
        private bool actionActive;

        public bool DeviceEnabled => deviceEnabled;
        public bool IsHolding => heldBody != null;
        public Rigidbody HeldBody => heldBody;
        public bool IsActionActive => actionActive;
        public bool CanOperate => deviceEnabled && CanOperateInFirstPerson;
        public bool ConsumesPrimaryAction => isActiveAndEnabled
            && CanOperate;

        private void Awake()
        {
            ResolveReferences();
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
            if (heldBody == null) TryAcquireTarget();
            return true;
        }

        public void TickAttraction()
        {
            ResolveReferences();
            if (!actionActive || !isActiveAndEnabled || !CanOperate)
            {
                EndAttraction();
                return;
            }

            if (heldBody == null) TryAcquireTarget();
        }

        public void EndAttraction()
        {
            actionActive = false;
            Release();
        }

        private void FixedUpdate()
        {
            if (!actionActive || heldBody == null) return;
            if (!CanOperate
                || heldTarget == null || !heldTarget.CanBeAttracted)
            {
                EndAttraction();
                return;
            }

            Vector3 cameraForward = viewCamera.transform.forward.normalized;
            Vector3 forward = GetPlanarForward();
            Vector3 desiredPosition = viewCamera.transform.position
                + cameraForward * Mathf.Max(0.2f, holdDistance);
            Vector3 centreOfMass = heldBody.worldCenterOfMass;

            Vector3 error = desiredPosition - centreOfMass;
            if (error.sqrMagnitude > breakDistance * breakDistance)
            {
                EndAttraction();
                return;
            }

            Vector3 targetVelocity = playerController != null
                ? playerController.velocity
                : Vector3.zero;
            Vector3 bodyVelocity = heldBody.velocity;
            Vector3 acceleration = error * Mathf.Max(0f, positionSpring)
                + (targetVelocity - bodyVelocity) * Mathf.Max(0f, positionDamping);
            acceleration = Vector3.ClampMagnitude(acceleration, Mathf.Max(0f, maximumAcceleration));
            heldBody.AddForce(acceleration, ForceMode.Acceleration);

            float yawError = Vector3.SignedAngle(heldBody.transform.forward, forward, Vector3.up)
                * Mathf.Deg2Rad;
            float yawVelocity = Vector3.Dot(heldBody.angularVelocity, Vector3.up);
            float angularAcceleration = yawError * Mathf.Max(0f, yawSpring)
                - yawVelocity * Mathf.Max(0f, yawDamping);
            angularAcceleration = Mathf.Clamp(
                angularAcceleration,
                -Mathf.Max(0f, maximumAngularAcceleration),
                Mathf.Max(0f, maximumAngularAcceleration));
            heldBody.AddTorque(Vector3.up * angularAcceleration, ForceMode.Acceleration);
        }

        public void SetDeviceEnabled(bool value)
        {
            deviceEnabled = value;
            if (!value) EndAttraction();
        }

        public void Release()
        {
            heldTarget = null;
            heldBody = null;
        }

        private bool TryAcquireTarget()
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
            PhysicsAttractable focusedTarget = body != null
                ? body.GetComponent<PhysicsAttractable>()
                : null;
            if (focusedTarget == null || !focusedTarget.CanBeAttracted) return false;

            heldTarget = focusedTarget;
            heldBody = focusedTarget.Body;
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
    }
}
