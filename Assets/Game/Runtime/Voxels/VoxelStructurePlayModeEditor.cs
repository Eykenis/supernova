using Supernova.Inputs;
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
        private bool fillModeEnabled;
        private bool hasFirstFillPoint;
        private bool hasSecondFillPoint;
        private Vector3Int firstFillPoint;
        private Vector3Int secondFillPoint;

        public float EditDistance => editDistance;
        public bool SavePending => savePending;
        public bool FillModeEnabled => fillModeEnabled;
        public bool HasFirstFillPoint => hasFirstFillPoint;
        public bool HasSecondFillPoint => hasSecondFillPoint;
        public Vector3Int FirstFillPoint => firstFillPoint;
        public Vector3Int SecondFillPoint => secondFillPoint;

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
            if (GameInput.Pressed(GameInputActionId.Cancel))
            {
                SetCursorLocked(!cursorLocked);
                return;
            }

            if (GameInput.Pressed(GameInputActionId.StructureToggleFillMode))
            {
                ToggleFillMode();
            }

            if (!cursorLocked)
            {
                if (GameInput.Pressed(GameInputActionId.StructureErase))
                    SetCursorLocked(true);
                return;
            }

            UpdateLook();
            UpdateMovement();

            if (GameInput.Pressed(GameInputActionId.StructureSave))
            {
                SaveNow();
            }
            else if (fillModeEnabled
                     && GameInput.Pressed(GameInputActionId.StructureFill))
            {
                FillSelectedBox();
            }
            else if (fillModeEnabled
                     && GameInput.Pressed(
                         GameInputActionId.StructureClearFillBox))
            {
                ClearSelectedBox();
            }
            else if (GameInput.Pressed(GameInputActionId.StructureErase))
            {
                if (fillModeEnabled) SelectFillPoint(true);
                else RemoveTargetedCell();
            }
            else if (GameInput.Pressed(GameInputActionId.StructurePaint))
            {
                if (fillModeEnabled) SelectFillPoint(false);
                else PlaceAdjacentCell();
            }

            if (savePending && Time.unscaledTime >= saveAtTime)
            {
                SaveNow();
            }
        }

        private void UpdateLook()
        {
            Vector2 look = GameInput.ReadVector2(GameInputActionId.Look);
            yaw += look.x * lookSensitivity;
            pitch -= look.y * lookSensitivity;
            pitch = Mathf.Clamp(pitch, -88f, 88f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void UpdateMovement()
        {
            Vector2 move = GameInput.ReadVector2(GameInputActionId.Move);
            if (fillModeEnabled
                && move.x > 0f
                && GameInput.Held(GameInputActionId.StructureClearFillBox))
            {
                move.x = 0f;
            }
            Vector3 movement = transform.right * move.x
                + transform.forward * move.y;
            if (GameInput.Held(GameInputActionId.SpectatorUp))
                movement += Vector3.up;
            if (GameInput.Held(GameInputActionId.SpectatorDown))
                movement += Vector3.down;
            if (movement.sqrMagnitude > 1f) movement.Normalize();

            float speed = GameInput.Held(GameInputActionId.SpectatorFast)
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

        private void ToggleFillMode()
        {
            fillModeEnabled = !fillModeEnabled;
            hasFirstFillPoint = false;
            hasSecondFillPoint = false;
        }

        private void SelectFillPoint(bool firstPoint)
        {
            if (!TryRaycastCell(out _, out VoxelStructureCellAuthoring cell))
            {
                return;
            }

            Vector3Int coordinate = Vector3Int.RoundToInt(
                cell.transform.localPosition);
            if (firstPoint)
            {
                firstFillPoint = coordinate;
                hasFirstFillPoint = true;
            }
            else
            {
                secondFillPoint = coordinate;
                hasSecondFillPoint = true;
            }
        }

        private void FillSelectedBox()
        {
            if (authoring == null
                || !hasFirstFillPoint
                || !hasSecondFillPoint)
            {
                return;
            }

            if (authoring.TryFillPaintBox(
                    firstFillPoint,
                    secondFillPoint,
                    out int changedCellCount)
                && changedCellCount > 0)
            {
                ScheduleSave();
            }
        }

        private void ClearSelectedBox()
        {
            if (authoring == null
                || !hasFirstFillPoint
                || !hasSecondFillPoint)
            {
                return;
            }

            if (authoring.TryClearBox(
                    firstFillPoint,
                    secondFillPoint,
                    out int removedCellCount)
                && removedCellCount > 0)
            {
                ScheduleSave();
            }
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

        private void OnGUI()
        {
            const float width = 390f;
            float height = fillModeEnabled ? 92f : 44f;
            GUILayout.BeginArea(new Rect(16f, 16f, width, height), GUI.skin.box);
            GUILayout.Label(fillModeEnabled
                ? "Voxel Edit Mode: FILL (F5 to switch)"
                : "Voxel Edit Mode: PAINT (F5 to switch)");
            if (fillModeEnabled)
            {
                string first = hasFirstFillPoint
                    ? firstFillPoint.ToString()
                    : "not selected";
                string second = hasSecondFillPoint
                    ? secondFillPoint.ToString()
                    : "not selected";
                GUILayout.Label($"LMB first: {first}    RMB second: {second}");
                GUILayout.Label(
                    "Ctrl+G fills with Paint Voxel Type; Ctrl+D clears the box.");
            }
            GUILayout.EndArea();
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
