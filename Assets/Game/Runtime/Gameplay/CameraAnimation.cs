using UnityEngine;

[DisallowMultipleComponent]
public sealed class CameraAnimation : MonoBehaviour
{
    [Header("Movement Detection")]
    [Tooltip("The transform whose movement drives the camera bob. Defaults to the camera's parent.")]
    [SerializeField] private Transform movementSource;
    [SerializeField, Min(0f)] private float movementThreshold = 0.05f;

    [Header("Head Bob")]
    [SerializeField, Min(0f)] private float bobAmplitude = 0.055f;
    [SerializeField, Min(0f)] private float bobFrequency = 16f;
    [SerializeField, Min(0.01f)] private float transitionSmoothTime = 0.08f;

    private Vector3 baseLocalPosition;
    private Vector3 previousSourcePosition;
    private float bobTime;
    private float currentOffset;
    private float offsetVelocity;

    private void Awake()
    {
        if (movementSource == null)
        {
            movementSource = transform.parent != null ? transform.parent : transform;
        }

        baseLocalPosition = transform.localPosition;
        previousSourcePosition = movementSource.position;
    }

    private void OnEnable()
    {
        if (movementSource != null)
        {
            previousSourcePosition = movementSource.position;
        }
    }

    private void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f || movementSource == null)
        {
            return;
        }

        Vector3 movement = movementSource.position - previousSourcePosition;
        previousSourcePosition = movementSource.position;

        // Ignore vertical motion so jumping or falling does not trigger walking bob.
        movement.y = 0f;
        float speed = movement.magnitude / deltaTime;
        bool isMoving = speed > movementThreshold;

        float targetOffset = 0f;
        if (isMoving)
        {
            bobTime += deltaTime * bobFrequency;
            targetOffset = Mathf.Sin(bobTime) * bobAmplitude;
        }
        else
        {
            bobTime = 0f;
        }

        currentOffset = Mathf.SmoothDamp(
            currentOffset,
            targetOffset,
            ref offsetVelocity,
            transitionSmoothTime,
            Mathf.Infinity,
            deltaTime);

        transform.localPosition = baseLocalPosition + Vector3.up * currentOffset;
    }

    private void OnDisable()
    {
        currentOffset = 0f;
        offsetVelocity = 0f;
        bobTime = 0f;
        transform.localPosition = baseLocalPosition;
    }
}
