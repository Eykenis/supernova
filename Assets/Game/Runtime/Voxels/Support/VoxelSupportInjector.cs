using System;
using System.Collections.Generic;
using Supernova.MinecraftCaves;
using Supernova.Voxels.Support.Prototype;
using UnityEngine;

namespace Supernova.Voxels.Support
{
    /// <summary>
    /// MonoBehaviour that transparently decorates the real <see cref="IVoxelTerrain"/>
    /// and intercepts mining operations to run structural support analysis.
    ///
    /// Every voxel destruction may trigger a cascade where unsupported voxels
    /// are converted into physics rubble via <see cref="VoxelCollapseSpawner"/>.
    ///
    /// <b>Usage:</b> attach this component to any GameObject in the scene
    /// (e.g. the Player or a dedicated manager).  At <c>Start</c> it finds
    /// the real terrain, wraps it, and sets itself as the <c>VoxelPlayerInteractor</c>'s
    /// terrain via reflection.  Because this component <em>is</em> an
    /// <see cref="IVoxelTerrain"/> and a <see cref="MonoBehaviour"/>, the
    /// interactor's <c>ResolveReferences</c> discovers it naturally.
    ///
    /// No existing code or prefab files are modified.
    /// </summary>
    [RequireComponent(typeof(VoxelCollapseSpawner))]
    [DisallowMultipleComponent]
    public sealed class VoxelSupportInjector : MonoBehaviour, IVoxelTerrain
    {
        [Header("Config")]
        [SerializeField] private VoxelSupportConfig config;

        // ── Cached state ──────────────────────────────────────────────
        private IVoxelTerrain realTerrain;
        private VoxelSupportGraph supportGraph;
        private VoxelCollapseSpawner spawner;
        private readonly List<Vector3Int> removedBuffer = new(32);

        // ── Unity lifecycle ───────────────────────────────────────────

        private void Awake()
        {
            spawner = GetComponent<VoxelCollapseSpawner>();
        }

        private void Start()
        {
            if (config == null)
            {
                Debug.LogWarning(
                    "[VoxelSupportInjector] No VoxelSupportConfig assigned — "
                    + "support analysis is disabled.", this);
                enabled = false;
                return;
            }

            supportGraph = new VoxelSupportGraph(config);
            Install();
        }

        private void Install()
        {
            // Find the real terrain and the interactor.
            MonoBehaviour[] allBehaviours = FindObjectsOfType<MonoBehaviour>();
            VoxelPlayerInteractor interactor = null;

            for (int i = 0; i < allBehaviours.Length; i++)
            {
                if (allBehaviours[i] == this) continue;
                if (allBehaviours[i] is IVoxelTerrain candidate)
                    realTerrain = candidate;
                if (allBehaviours[i] is VoxelPlayerInteractor inter)
                    interactor = inter;
            }

            if (realTerrain == null)
            {
                Debug.LogWarning(
                    "[VoxelSupportInjector] No real IVoxelTerrain found in scene "
                    + "— support analysis is disabled.", this);
                enabled = false;
                return;
            }

            // Swap the interactor's terrain reference to point at THIS
            // injector.  Because we implement IVoxelTerrain, all mining
            // calls will be intercepted.
            if (interactor != null)
            {
                var terrainField = typeof(VoxelPlayerInteractor).GetField(
                    "terrain",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);
                if (terrainField != null)
                {
                    terrainField.SetValue(interactor, (MonoBehaviour)this);
                }
            }

            Debug.Log(
                $"[VoxelSupportInjector] Wrapped terrain "
                + $"'{realTerrain.GetType().Name}' for support analysis. "
                + $"Interactor swapped: {interactor != null}.", this);
        }

        // ═══════════════════════════════════════════════════════════════
        //  IVoxelTerrain — passthrough properties
        // ═══════════════════════════════════════════════════════════════

        public Transform TerrainTransform =>
            realTerrain != null ? realTerrain.TerrainTransform : transform;
        public InfiniteVoxelWorld World => realTerrain?.World;
        public VoxelTypeCatalog VoxelTypeCatalog => realTerrain?.VoxelTypeCatalog;
        public float VoxelSize => realTerrain?.VoxelSize ?? 1f;
        public float IsoLevel => realTerrain?.IsoLevel ?? 0f;

        public Vector3Int WorldPositionToVoxel(Vector3 worldPosition)
        {
            return realTerrain != null
                ? realTerrain.WorldPositionToVoxel(worldPosition)
                : default;
        }

        // ═══════════════════════════════════════════════════════════════
        //  IVoxelTerrain — intercepted mining
        // ═══════════════════════════════════════════════════════════════

        public bool TryMineVoxel(Vector3Int coordinate, out VoxelMiningResult result)
        {
            bool mined = realTerrain.TryMineVoxel(coordinate, out result);
            if (mined && result.Destroyed)
                RunSupportAnalysis(new[] { coordinate });
            return mined;
        }

        public bool TryMineBrush(
            Vector3Int primaryCoordinate,
            Vector3 worldDirection,
            VoxelMiningBrushSettings settings,
            out VoxelMiningBrushResult result)
        {
            bool mined = realTerrain.TryMineBrush(
                primaryCoordinate, worldDirection, settings, out result);
            if (mined && result.DestroyedCount > 0)
            {
                // Only pass the primary coordinate as the removal seed.
                // The BFS itself will discover which neighbours are still
                // solid — we must NOT pre-mark neighbours as removed or the
                // BFS sees no connected component at all.
                RunSupportAnalysis(new[] { primaryCoordinate });
            }
            return mined;
        }

        public bool TryMineExplosion(
            Vector3 worldCenter,
            VoxelExplosionSettings settings,
            out VoxelExplosionResult result)
        {
            bool mined = realTerrain.TryMineExplosion(
                worldCenter,
                settings,
                out result);
            if (mined && result.DestroyedCount > 0)
            {
                RunSupportAnalysis(
                    new[] { realTerrain.WorldPositionToVoxel(worldCenter) });
            }
            return mined;
        }

        public bool TrySetVoxelAndRebuild(
            int worldX, int worldY, int worldZ,
            float density, VoxelTypeId type)
        {
            bool wasSolid = World != null
                            && World.TryGetSample(worldX, worldY, worldZ, out VoxelSample prev)
                            && prev.IsSolid(IsoLevel);

            bool ok = realTerrain.TrySetVoxelAndRebuild(
                worldX, worldY, worldZ, density, type);

            if (ok && wasSolid && density < IsoLevel)
                RunSupportAnalysis(new[] { new Vector3Int(worldX, worldY, worldZ) });

            return ok;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Support analysis → rubble + rebuild
        // ═══════════════════════════════════════════════════════════════

        private void RunSupportAnalysis(IReadOnlyList<Vector3Int> removedVoxels)
        {
            if (realTerrain == null) return;

            InfiniteVoxelWorld world = World;
            if (world == null) return;

            float iso = IsoLevel;
            Transform terrainXf = realTerrain.TerrainTransform;

            HashSet<Vector3Int> dirtyChunks;

            if (spawner.ActiveMode == VoxelCollapseSpawner.CollapseMode.BoundaryConnectivity)
            {
                // BoundaryConnectivity mode — spawner runs full
                // detection + rubble pipeline inline.
                dirtyChunks = spawner.DetectAndSpawn(
                    removedVoxels, world, iso,
                    VoxelSize, VoxelTypeCatalog, terrainXf,
                    preCollapsed: null);
            }
            else
            {
                // StressPropagation mode — run EBC_LB analysis first.
                if (supportGraph == null) return;

                SupportAnalysisResult analysis = supportGraph.Analyze(
                    removedVoxels,
                    solidity: pos =>
                        world.TryGetSample(pos.x, pos.y, pos.z, out VoxelSample s)
                        && s.IsSolid(iso),
                    isAnchor: pos => pos.y <= config.BedrockYThreshold);

                Debug.Log(
                    $"[VoxelSupportInjector] Analysis: "
                    + $"removed={removedVoxels.Count}, "
                    + $"collapsed={analysis.CollapsedVoxels?.Count ?? 0}, "
                    + $"fragile={analysis.FragileVoxels?.Count ?? 0}, "
                    + $"affected={analysis.AffectedVoxels?.Count ?? 0}, "
                    + $"cascade={analysis.CascadeIterationsUsed}");

                if (analysis.CollapsedVoxels == null
                    || analysis.CollapsedVoxels.Count == 0)
                {
                    return;
                }

                dirtyChunks = spawner.SpawnCollapseRubble(
                    analysis.CollapsedVoxels,
                    world, iso,
                    VoxelSize, VoxelTypeCatalog, terrainXf);
            }

            // ── Trigger mesh rebuilds for affected chunks ─────────────
            if (dirtyChunks.Count > 0)
            {
                EnqueueMeshRebuilds(dirtyChunks);
            }
        }

        /// <summary>
        /// Pushes dirty chunk-section coordinates into the real terrain's
        /// priority mesh queue via reflection (the queue is private).
        /// </summary>
        private void EnqueueMeshRebuilds(HashSet<Vector3Int> dirtyChunks)
        {
            if (realTerrain is not MonoBehaviour realMb) return;

            var worldType = realMb.GetType();  // MinecraftCaveInfiniteWorld
            var enqueueMethod = worldType.GetMethod(
                "EnqueuePriorityMeshes",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);

            if (enqueueMethod != null)
            {
                enqueueMethod.Invoke(realMb, new object[] { dirtyChunks });
            }
        }

        private static readonly Vector3Int[] SixNeighbourOffsets =
        {
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        };
    }
}
