using Supernova.MinecraftCaves;
using UnityEngine;

namespace Supernova.PortalExample
{
    /// <summary>
    /// Connects the external Dense-region landing Cell to the one checkpoint
    /// produced by the fixed-origin spawn hall. Portal rendering and traversal
    /// stay inside the isolated PortalExample feature.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DenseJigsawPortalBridge : MonoBehaviour
    {
        [SerializeField] private MinecraftCaveInfiniteWorld world;
        [SerializeField] private SpawnPointSceneStructure landingCell;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private PortalExampleGate landingCellGate;
        [SerializeField] private PortalExampleGate checkpointGate;
        [SerializeField, Min(0.5f)]
        private float landingGateForwardDistance = 0.9f;
        [SerializeField, Range(0.5f, 1f)]
        private float portalScale = 0.6f;
        [SerializeField, Min(0f)] private float supportClearance = 0.005f;

        private GameObject primaryCheckpoint;

        public MinecraftCaveInfiniteWorld World => world;
        public SpawnPointSceneStructure LandingCell => landingCell;
        public PortalExampleGate LandingCellGate => landingCellGate;
        public PortalExampleGate CheckpointGate => checkpointGate;
        public bool IsLinked => primaryCheckpoint != null
            && landingCellGate != null
            && checkpointGate != null
            && landingCellGate.gameObject.activeSelf
            && checkpointGate.gameObject.activeSelf;

        public void Configure(
            MinecraftCaveInfiniteWorld configuredWorld,
            SpawnPointSceneStructure configuredLandingCell,
            Transform configuredPlayerRoot,
            PortalExampleGate configuredLandingCellGate,
            PortalExampleGate configuredCheckpointGate)
        {
            world = configuredWorld;
            landingCell = configuredLandingCell;
            playerRoot = configuredPlayerRoot;
            landingCellGate = configuredLandingCellGate;
            checkpointGate = configuredCheckpointGate;
        }

        private void OnEnable()
        {
            if (world != null)
            {
                world.PrimarySpawnCheckpointCreated +=
                    HandlePrimarySpawnCheckpointCreated;
            }
        }

        private void Start()
        {
            EnsurePlayerTraveller();
            if (world != null && world.PrimarySpawnCheckpoint != null)
            {
                primaryCheckpoint = world.PrimarySpawnCheckpoint;
            }
            TryPlacePortals();
        }

        private void OnDisable()
        {
            if (world != null)
            {
                world.PrimarySpawnCheckpointCreated -=
                    HandlePrimarySpawnCheckpointCreated;
            }
        }

        private void HandlePrimarySpawnCheckpointCreated(GameObject checkpoint)
        {
            primaryCheckpoint = checkpoint;
            TryPlacePortals();
        }

        public bool TryPlacePortals()
        {
            if (primaryCheckpoint == null
                || landingCell == null
                || landingCell.PlayerSpawnPoint == null
                || landingCellGate == null
                || checkpointGate == null)
            {
                return false;
            }

            PlaceLandingCellGate();
            PlaceCheckpointGate();
            landingCellGate.gameObject.SetActive(true);
            checkpointGate.gameObject.SetActive(true);
            return true;
        }

        private void PlaceLandingCellGate()
        {
            Transform spawn = landingCell.PlayerSpawnPoint;
            landingCellGate.transform.localScale =
                Vector3.one * portalScale;
            Vector3 up = spawn.up.normalized;
            Vector3 forward = Vector3.ProjectOnPlane(spawn.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = landingCell.transform.forward;
            }
            forward.Normalize();

            Vector3 supportPoint = spawn.position
                + forward * landingGateForwardDistance;
            PlaceVerticalGateOnSupport(
                landingCellGate,
                supportPoint,
                -forward,
                up);
        }

        private void PlaceCheckpointGate()
        {
            checkpointGate.transform.localScale = Vector3.one * portalScale;
            Vector3 up = world != null
                ? world.transform.up.normalized
                : Vector3.up;
            Vector3 supportPoint = primaryCheckpoint.transform.position;
            Renderer[] renderers =
                primaryCheckpoint.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                float upperExtent = Mathf.Abs(up.x) * bounds.extents.x
                    + Mathf.Abs(up.y) * bounds.extents.y
                    + Mathf.Abs(up.z) * bounds.extents.z;
                supportPoint = bounds.center + up * upperExtent;
            }

            Vector3 inPlaneUp = world != null
                ? Vector3.ProjectOnPlane(
                    world.AuthoredSpawnWorldPosition - supportPoint,
                    up)
                : Vector3.zero;
            if (inPlaneUp.sqrMagnitude < 0.0001f)
            {
                inPlaneUp = Vector3.ProjectOnPlane(
                    -primaryCheckpoint.transform.forward,
                    up);
            }
            if (inPlaneUp.sqrMagnitude < 0.0001f)
            {
                inPlaneUp = Vector3.ProjectOnPlane(Vector3.forward, up);
            }

            PlaceHorizontalGateOnSupport(
                checkpointGate,
                supportPoint,
                up,
                inPlaneUp.normalized);
        }

        private void PlaceVerticalGateOnSupport(
            PortalExampleGate gate,
            Vector3 supportPoint,
            Vector3 forward,
            Vector3 up)
        {
            gate.transform.SetPositionAndRotation(
                supportPoint,
                Quaternion.LookRotation(forward, up));

            float minimumHeight = ResolveVisualMinimumHeight(gate, up);
            gate.transform.position += up * (supportClearance - minimumHeight);
        }

        private void PlaceHorizontalGateOnSupport(
            PortalExampleGate gate,
            Vector3 supportPoint,
            Vector3 normal,
            Vector3 inPlaneUp)
        {
            gate.transform.SetPositionAndRotation(
                supportPoint + normal * supportClearance,
                Quaternion.LookRotation(normal, inPlaneUp));
        }

        private static float ResolveVisualMinimumHeight(
            PortalExampleGate gate,
            Vector3 up)
        {
            MeshFilter[] filters = gate.GetComponentsInChildren<MeshFilter>(true);
            float minimum = float.PositiveInfinity;
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                Bounds bounds = filter.sharedMesh.bounds;
                Vector3 boundsMinimum = bounds.min;
                Vector3 boundsMaximum = bounds.max;
                for (int x = 0; x <= 1; x++)
                {
                    for (int y = 0; y <= 1; y++)
                    {
                        for (int z = 0; z <= 1; z++)
                        {
                            Vector3 localCorner = new Vector3(
                                x == 0 ? boundsMinimum.x : boundsMaximum.x,
                                y == 0 ? boundsMinimum.y : boundsMaximum.y,
                                z == 0 ? boundsMinimum.z : boundsMaximum.z);
                            Vector3 worldCorner =
                                filter.transform.TransformPoint(localCorner);
                            minimum = Mathf.Min(
                                minimum,
                                Vector3.Dot(
                                    worldCorner - gate.transform.position,
                                    up));
                        }
                    }
                }
            }

            return float.IsPositiveInfinity(minimum) ? 0f : minimum;
        }

        private void EnsurePlayerTraveller()
        {
            if (playerRoot != null
                && playerRoot.GetComponent<PortalExampleTraveller>() == null)
            {
                playerRoot.gameObject.AddComponent<PortalExampleTraveller>();
            }
        }
    }
}
