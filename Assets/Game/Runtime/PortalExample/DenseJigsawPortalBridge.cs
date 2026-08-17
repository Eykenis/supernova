using System;
using System.Collections.Generic;
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
        public static event Action<DenseJigsawPortalBridge> InstanceEnabled;
        public static event Action<DenseJigsawPortalBridge> InstanceDisabled;

        public const string SpawnCheckpointPortalName =
            "Spawn Checkpoint Portal / 出生检查点传送门";

        [SerializeField] private MinecraftCaveInfiniteWorld world;
        [SerializeField] private SpawnPointSceneStructure landingCell;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private PortalExampleGate landingCellGate;
        [SerializeField] private PortalExampleGate checkpointGate;
        [SerializeField, Range(0.5f, 1f)]
        private float portalScale = 0.6f;
        [SerializeField, Min(0f)] private float supportClearance = 0.005f;
        [SerializeField, Min(0.001f)]
        private float spawnedPortalSurfaceOffset = 0.06f;
        [SerializeField, Min(1f)]
        private float spawnedPortalScaleMultiplier = 1.1f;

        private GameObject primaryCheckpoint;
        private PortalExampleTraveller playerTraveller;
        private readonly List<PortalExampleGate> spawnedCheckpointGates =
            new List<PortalExampleGate>();

        public event Action<PortalExampleGate> PortalAdded;

        public MinecraftCaveInfiniteWorld World => world;
        public SpawnPointSceneStructure LandingCell => landingCell;
        public PortalExampleGate LandingCellGate => landingCellGate;
        public PortalExampleGate CheckpointGate => checkpointGate;
        public float SpawnedPortalSurfaceOffset =>
            Mathf.Max(0.001f, spawnedPortalSurfaceOffset);
        public float SpawnedPortalScaleMultiplier =>
            Mathf.Max(1f, spawnedPortalScaleMultiplier);
        public IReadOnlyList<PortalExampleGate> SpawnedCheckpointGates
        {
            get
            {
                RemoveDestroyedSpawnedGates();
                return spawnedCheckpointGates;
            }
        }
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
            if (landingCell != null)
            {
                landingCell.Placed -= HandleLandingCellPlaced;
            }
            world = configuredWorld;
            landingCell = configuredLandingCell;
            playerRoot = configuredPlayerRoot;
            landingCellGate = configuredLandingCellGate;
            checkpointGate = configuredCheckpointGate;
            SubscribeToLandingCellPlacement();
            EnsurePlayerTraveller();
        }

        private void OnEnable()
        {
            if (world != null)
            {
                world.PrimarySpawnCheckpointCreated +=
                    HandlePrimarySpawnCheckpointCreated;
            }
            SubscribeToLandingCellPlacement();
            EnsurePlayerTraveller();
            InstanceEnabled?.Invoke(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeEvents()
        {
            InstanceEnabled = null;
            InstanceDisabled = null;
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
            InstanceDisabled?.Invoke(this);
            if (world != null)
            {
                world.PrimarySpawnCheckpointCreated -=
                    HandlePrimarySpawnCheckpointCreated;
            }
            if (landingCell != null)
            {
                landingCell.Placed -= HandleLandingCellPlaced;
            }
            if (playerTraveller != null)
            {
                playerTraveller.Teleported -= HandlePlayerTeleported;
                playerTraveller = null;
            }
        }

        private void HandlePrimarySpawnCheckpointCreated(GameObject checkpoint)
        {
            primaryCheckpoint = checkpoint;
            TryPlacePortals();
        }

        private void HandleLandingCellPlaced()
        {
            // The bridge follows the landing Cell, but the checkpoint gate must
            // stay attached to the generated checkpoint in world space.
            TryPlacePortals();
        }

        private void HandlePlayerTeleported(
            PortalExampleGate source,
            PortalExampleGate destination)
        {
            if (world == null
                || source != landingCellGate
                || destination != checkpointGate)
            {
                return;
            }

            world.BeginNaturalMonsterSpawningAfterPortalEntry();
        }

        public bool TryPlacePortals()
        {
            if (primaryCheckpoint == null
                || landingCell == null
                || landingCellGate == null
                || checkpointGate == null)
            {
                return false;
            }

            // The landing-cell gate is scene-authored and must retain the pose the
            // level designer assigned. Only the generated checkpoint gate follows
            // its runtime support object.
            PlaceCheckpointGate();
            landingCellGate.gameObject.SetActive(true);
            checkpointGate.gameObject.SetActive(true);
            PortalAdded?.Invoke(checkpointGate);
            return true;
        }

        /// <summary>
        /// Creates another spawn-checkpoint entrance on a hit surface. Every entrance
        /// is a clone of the authored checkpoint gate and leads to the one landing-cell
        /// gate owned by this bridge.
        /// </summary>
        public bool TryCreateSpawnCheckpointPortal(
            Vector3 supportPoint,
            Vector3 surfaceNormal,
            Vector3 preferredInPlaneUp,
            out PortalExampleGate createdGate)
        {
            return TryCreateSpawnCheckpointPortal(
                null,
                supportPoint,
                surfaceNormal,
                preferredInPlaneUp,
                out createdGate);
        }

        public bool TryCreateSpawnCheckpointPortal(
            Collider supportCollider,
            Vector3 supportPoint,
            Vector3 surfaceNormal,
            Vector3 preferredInPlaneUp,
            out PortalExampleGate createdGate)
        {
            createdGate = null;
            RemoveDestroyedSpawnedGates();
            if (landingCellGate == null || checkpointGate == null)
            {
                return false;
            }

            Vector3 normal = surfaceNormal.sqrMagnitude > 0.0001f
                ? surfaceNormal.normalized
                : Vector3.up;
            Vector3 inPlaneUp = ResolveInPlaneUp(
                normal,
                preferredInPlaneUp);

            GameObject portalObject = Instantiate(
                checkpointGate.gameObject,
                transform);
            portalObject.SetActive(false);
            portalObject.name = SpawnCheckpointPortalName;

            PortalExampleGate gate =
                portalObject.GetComponent<PortalExampleGate>();
            if (gate == null)
            {
                DestroyPortalObject(portalObject);
                return false;
            }

            gate.LinkTo(landingCellGate);
            gate.transform.localScale = Vector3.one
                * portalScale
                * SpawnedPortalScaleMultiplier;
            PlaceHorizontalGateOnSupport(
                gate,
                supportPoint,
                normal,
                inPlaneUp,
                SpawnedPortalSurfaceOffset);
            if (supportCollider != null)
            {
                PortalSurfaceDependency dependency =
                    portalObject.GetComponent<PortalSurfaceDependency>();
                if (dependency == null)
                {
                    dependency = portalObject
                        .AddComponent<PortalSurfaceDependency>();
                }
                dependency.Configure(supportCollider, supportPoint, normal);
            }
            portalObject.SetActive(true);
            spawnedCheckpointGates.Add(gate);
            PortalAdded?.Invoke(gate);
            createdGate = gate;
            return true;
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
                inPlaneUp.normalized,
                supportClearance);
        }

        private void PlaceHorizontalGateOnSupport(
            PortalExampleGate gate,
            Vector3 supportPoint,
            Vector3 normal,
            Vector3 inPlaneUp,
            float surfaceOffset)
        {
            gate.transform.SetPositionAndRotation(
                supportPoint + normal * Mathf.Max(0f, surfaceOffset),
                Quaternion.LookRotation(normal, inPlaneUp));
        }

        private Vector3 ResolveInPlaneUp(
            Vector3 normal,
            Vector3 preferredInPlaneUp)
        {
            Vector3 inPlaneUp = Vector3.ProjectOnPlane(
                preferredInPlaneUp,
                normal);
            if (inPlaneUp.sqrMagnitude < 0.0001f)
            {
                Vector3 worldForward = world != null
                    ? world.transform.forward
                    : transform.forward;
                inPlaneUp = Vector3.ProjectOnPlane(worldForward, normal);
            }
            if (inPlaneUp.sqrMagnitude < 0.0001f)
            {
                inPlaneUp = Vector3.ProjectOnPlane(Vector3.up, normal);
            }
            if (inPlaneUp.sqrMagnitude < 0.0001f)
            {
                inPlaneUp = Vector3.ProjectOnPlane(Vector3.right, normal);
            }
            return inPlaneUp.normalized;
        }

        private void RemoveDestroyedSpawnedGates()
        {
            for (int i = spawnedCheckpointGates.Count - 1; i >= 0; i--)
            {
                if (spawnedCheckpointGates[i] == null)
                {
                    spawnedCheckpointGates.RemoveAt(i);
                }
            }
        }

        private void SubscribeToLandingCellPlacement()
        {
            if (!isActiveAndEnabled || landingCell == null)
            {
                return;
            }

            landingCell.Placed -= HandleLandingCellPlaced;
            landingCell.Placed += HandleLandingCellPlaced;
        }

        private static void DestroyPortalObject(GameObject portalObject)
        {
            if (Application.isPlaying)
            {
                Destroy(portalObject);
            }
            else
            {
                DestroyImmediate(portalObject);
            }
        }

        private void EnsurePlayerTraveller()
        {
            if (playerRoot == null)
            {
                return;
            }

            PortalExampleTraveller resolved =
                playerRoot.GetComponent<PortalExampleTraveller>();
            if (resolved == null)
            {
                resolved =
                    playerRoot.gameObject.AddComponent<PortalExampleTraveller>();
            }
            if (resolved == playerTraveller)
            {
                return;
            }
            if (playerTraveller != null)
            {
                playerTraveller.Teleported -= HandlePlayerTeleported;
            }

            playerTraveller = resolved;
            if (isActiveAndEnabled)
            {
                playerTraveller.Teleported += HandlePlayerTeleported;
            }
        }
    }
}
