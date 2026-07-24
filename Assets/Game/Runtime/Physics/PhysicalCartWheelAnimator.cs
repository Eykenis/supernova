using UnityEngine;

/// <summary>
/// Updates cart wheel visuals from the Rigidbody's real, physics-generated motion.
/// This component never applies forces, velocity, position, or rotation to the cart body.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class PhysicalCartWheelAnimator : MonoBehaviour
{
    [SerializeField] private Transform[] wheelPivots;
    [SerializeField, Min(0.001f)] private float wheelRadius = 0.125f;
    [SerializeField] private Vector3 localAxle = Vector3.right;
    [SerializeField] private Vector3 localRollingDirection = Vector3.forward;
    [SerializeField] private float rotationSign = -1f;
    [SerializeField, Min(0f)] private float sleepSpeedThreshold = 0.005f;

    private Rigidbody cartBody;

    private void Awake()
    {
        cartBody = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (wheelPivots == null || wheelPivots.Length == 0 || wheelRadius <= 0f)
        {
            return;
        }

        Vector3 axle = localAxle.sqrMagnitude > 0f ? localAxle.normalized : Vector3.right;
        Vector3 rollingDirection = transform.TransformDirection(
            localRollingDirection.sqrMagnitude > 0f ? localRollingDirection.normalized : Vector3.forward);

        for (int i = 0; i < wheelPivots.Length; i++)
        {
            Transform wheelPivot = wheelPivots[i];
            if (wheelPivot == null)
            {
                continue;
            }

            // GetPointVelocity includes both linear motion and yaw-induced velocity at each wheel.
            float rollingSpeed = Vector3.Dot(cartBody.GetPointVelocity(wheelPivot.position), rollingDirection);
            if (Mathf.Abs(rollingSpeed) < sleepSpeedThreshold)
            {
                continue;
            }

            float angleDegrees = rotationSign * rollingSpeed * Time.fixedDeltaTime / wheelRadius * Mathf.Rad2Deg;
            wheelPivot.Rotate(axle, angleDegrees, Space.Self);
        }
    }
}
