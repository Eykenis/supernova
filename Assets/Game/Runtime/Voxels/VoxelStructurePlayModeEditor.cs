using UnityEngine;

namespace Supernova.Voxels
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class VoxelStructurePlayModeEditor : MonoBehaviour
    {
        [SerializeField] private VoxelStructureAuthoring authoring;
        [SerializeField] private Camera viewCamera;

        [Header("Debug Camera")]
        [SerializeField, Min(0.1f)] private float moveSpeed = 8f;
        [SerializeField, Min(1f)] private float fastMultiplier = 3f;
        [SerializeField, Min(0.1f)] private float lookSensitivity = 2.2f;

        [Header("Voxel Editing")]
        [SerializeField, Min(1f)] private float editDistance = 48f;
        [SerializeField, Min(0f)] private float autoSaveDelay = 0.2f;

        private float yaw;
        private float pitch;
        private bool cursorLocked;
        private bool savePending;
        private float saveAtTime;

        public float EditDistance => editDistance;
        public bool SavePending => savePending;

        public void Configure(
            VoxelStructureAuthoring structureAuthoring,
            Camera camera)
        {
            authoring = structureAuthoring;
            viewCamera = camera;
        }

        private void Awake()
        {
            if (viewCamera == null) viewCamera = GetComponent<Camera>();
            if (authoring == null) authoring = FindObjectOfType<VoxelStructureAuthoring>();
            authoring?.ReloadFromAssignedAsset();
        }

        private void OnEnable()
        {
            Vector3 angles = transform.eulerAngles;
            yaw = angles.y;
            pitch = NormalizeAngle(angles.x);
            SetCursorLocked(true);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SetCursorLocked(!cursorLocked);
                return;
            }

            if (!cursorLocked)
            {
                if (Input.GetMouseButtonDown(0)) SetCursorLocked(true);
                return;
            }

            UpdateLook();
            UpdateMovement();

            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.S))
            {
                SaveNow();
            }
            else if (Input.GetMouseButtonDown(0))
            {
                RemoveTargetedCell();
            }
            else if (Input.GetMouseButtonDown(1))
            {
                PlaceAdjacentCell();
            }

            if (savePending && Time.unscaledTime >= saveAtTime)
            {
                SaveNow();
            }
        }

        private void UpdateLook()
        {
            yaw += Input.GetAxisRaw("Mouse X") * lookSensitivity;
            pitch -= Input.GetAxisRaw("Mouse Y") * lookSensitivity;
            pitch = Mathf.Clamp(pitch, -88f, 88f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void UpdateMovement()
        {
            Vector3 movement = transform.right * Input.GetAxisRaw("Horizontal")
                + transform.forward * Input.GetAxisRaw("Vertical");
            if (Input.GetKey(KeyCode.E)) movement += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) movement += Vector3.down;
            if (movement.sqrMagnitude > 1f) movement.Normalize();

            float speed = Input.GetKey(KeyCode.LeftShift)
                ? moveSpeed * fastMultiplier
                : moveSpeed;
            transform.position += movement * (speed * Time.unscaledDeltaTime);
        }

        private void RemoveTargetedCell()
        {
            if (!TryRaycastCell(out RaycastHit hit, out VoxelStructureCellAuthoring cell))
            {
                return;
            }

            if (authoring.TryRemoveCell(cell)) ScheduleSave();
        }

        private void PlaceAdjacentCell()
        {
            if (!TryRaycastCell(out RaycastHit hit, out _))
            {
                return;
            }

            Vector3 local = authoring.transform.InverseTransformPoint(
                hit.point + hit.normal * 0.51f);
            var coordinate = new Vector3Int(
                Mathf.RoundToInt(local.x),
                Mathf.RoundToInt(local.y),
                Mathf.RoundToInt(local.z));
            if (authoring.TryCreatePaintCell(coordinate, out _)) ScheduleSave();
        }

        private bool TryRaycastCell(
            out RaycastHit hit,
            out VoxelStructureCellAuthoring cell)
        {
            hit = default;
            cell = null;
            if (authoring == null || viewCamera == null
                || !Physics.Raycast(
                    viewCamera.transform.position,
                    viewCamera.transform.forward,
                    out hit,
                    editDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            cell = hit.collider.GetComponent<VoxelStructureCellAuthoring>();
            return cell != null && cell.transform.IsChildOf(authoring.transform);
        }

        private void ScheduleSave()
        {
            savePending = true;
            saveAtTime = Time.unscaledTime + autoSaveDelay;
        }

        private void SaveNow()
        {
            if (!savePending && authoring == null) return;
            if (authoring != null && !authoring.TrySaveAssignedAsset(out string error))
            {
                Debug.LogError($"Failed to save voxel structure: {error}", authoring);
                return;
            }
            savePending = false;
        }

        private void OnDisable()
        {
            if (savePending) SaveNow();
            SetCursorLocked(false);
        }

        private void SetCursorLocked(bool value)
        {
            cursorLocked = value;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
