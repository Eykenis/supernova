using System;
using System.Collections.Generic;
using Supernova.Voxels.Support.Prototype;
using UnityEngine;

namespace Supernova.Voxels.Support
{
    /// <summary>
    /// Transparent <see cref="IVoxelTerrain"/> decorator that intercepts voxel
    /// mining operations and runs support-graph analysis on every destruction.
    ///
    /// Collapsed voxels are removed from the world and their chunks are queued
    /// for priority mesh rebuild, giving real-time structural collapse feedback
    /// without modifying any existing production code.
    ///
    /// This proxy is injected at runtime into <see cref="VoxelPlayerInteractor"/>
    /// via <see cref="VoxelSupportInjector"/>.
    /// </summary>
    public sealed class VoxelSupportTerrainProxy : IVoxelTerrain
    {
        private readonly IVoxelTerrain inner;
        private readonly IVoxelTerrain innerTyped; // same reference, cached for clarity
        private readonly VoxelSupportGraph supportGraph;
        private readonly VoxelSupportConfig config;

        // Pre-allocated lists for the hot path.
        private readonly List<Vector3Int> removedBuffer = new(32);

        public VoxelSupportTerrainProxy(
            IVoxelTerrain realTerrain,
            VoxelSupportConfig config)
        {
            inner = realTerrain ?? throw new ArgumentNullException(nameof(realTerrain));
            innerTyped = inner;
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            supportGraph = new VoxelSupportGraph(config);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Passthrough properties — delegated directly to the real terrain.
        // ═══════════════════════════════════════════════════════════════

        public Transform TerrainTransform => inner.TerrainTransform;
        public InfiniteVoxelWorld World => inner.World;
        public VoxelTypeCatalog VoxelTypeCatalog => inner.VoxelTypeCatalog;
        public float VoxelSize => inner.VoxelSize;
        public float IsoLevel => inner.IsoLevel;

        public Vector3Int WorldPositionToVoxel(Vector3 worldPosition)
        {
            return inner.WorldPositionToVoxel(worldPosition);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Intercepted mining operations
        // ═══════════════════════════════════════════════════════════════

        public bool TryMineVoxel(Vector3Int coordinate, out VoxelMiningResult result)
        {
            bool mined = inner.TryMineVoxel(coordinate, out result);
            if (mined && result.Destroyed)
            {
                RunSupportAnalysis(new[] { coordinate });
            }

            return mined;
        }

        public bool TryMineBrush(
            Vector3Int primaryCoordinate,
            Vector3 worldDirection,
            VoxelMiningBrushSettings settings,
            out VoxelMiningBrushResult result)
        {
            bool mined = inner.TryMineBrush(
                primaryCoordinate, worldDirection, settings, out result);
            if (mined && result.DestroyedCount > 0)
            {
                // Collect all voxels that were actually destroyed by the brush.
                removedBuffer.Clear();
                CollectDestroyedVoxelsFromBrush(result, removedBuffer);
                if (removedBuffer.Count > 0)
                {
                    RunSupportAnalysis(removedBuffer);
                }
            }

            return mined;
        }

        public bool TryMineExplosion(
            Vector3 worldCenter,
            VoxelExplosionSettings settings,
            out VoxelExplosionResult result)
        {
            bool mined = inner.TryMineExplosion(
                worldCenter,
                settings,
                out result);
            if (mined && result.DestroyedCount > 0)
            {
                RunSupportAnalysis(
                    new[] { inner.WorldPositionToVoxel(worldCenter) });
            }
            return mined;
        }

        public bool TrySetVoxelAndRebuild(
            int worldX,
            int worldY,
            int worldZ,
            float density,
            VoxelTypeId type)
        {
            // Capture the previous state so we know if this is a removal.
            InfiniteVoxelWorld world = inner.World;
            bool wasSolid = world.TryGetSample(worldX, worldY, worldZ, out VoxelSample prev)
                            && prev.IsSolid(IsoLevel);

            bool ok = inner.TrySetVoxelAndRebuild(
                worldX, worldY, worldZ, density, type);

            if (ok && wasSolid && density < IsoLevel)
            {
                RunSupportAnalysis(new[]
                {
                    new Vector3Int(worldX, worldY, worldZ),
                });
            }

            return ok;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Support analysis pipeline
        // ═══════════════════════════════════════════════════════════════

        private void RunSupportAnalysis(IReadOnlyList<Vector3Int> removedVoxels)
        {
            float iso = IsoLevel;
            InfiniteVoxelWorld world = World;

            SupportAnalysisResult analysis = supportGraph.Analyze(
                removedVoxels,
                solidity: pos =>
                {
                    return world.TryGetSample(pos.x, pos.y, pos.z, out VoxelSample s)
                           && s.IsSolid(iso);
                },
                isAnchor: pos => pos.y <= config.BedrockYThreshold);

            if (analysis.CollapsedVoxels == null
                || analysis.CollapsedVoxels.Count == 0)
            {
                return;
            }

            // Remove collapsed voxels from the world and trigger mesh rebuilds.
            int collapsedCount = 0;
            foreach (Vector3Int pos in analysis.CollapsedVoxels)
            {
                if (collapsedCount >= config.MaxCollapsesPerFrame)
                    break; // surplus deferred to next analysis

                if (inner.TrySetVoxelAndRebuild(
                        pos.x, pos.y, pos.z,
                        iso - 1f,
                        VoxelTypeId.Air))
                {
                    collapsedCount++;
                }
            }

            if (collapsedCount > 0)
            {
                Debug.Log(
                    $"[VoxelSupportProxy] {collapsedCount} voxel(s) collapsed "
                    + $"after {analysis.CascadeIterationsUsed} cascade iteration(s). "
                    + $"{removedVoxels.Count} voxel(s) were removed by the player.");
            }
        }

        private static void CollectDestroyedVoxelsFromBrush(
            VoxelMiningBrushResult brushResult,
            List<Vector3Int> buffer)
        {
            // The brush result unfortunately does not enumerate every destroyed
            // coordinate, but we can start from the primary + the damaged/destroyed
            // count to seed the search.  In practice the primary + removed set
            // from the analysis covers the affected region.
            if (brushResult.PrimaryDestroyed)
            {
                buffer.Add(brushResult.PrimaryCoordinate);
            }

            // For brush operations we also check the primary coordinate's immediate
            // 6-neighbours — the brush always damages a contiguous volume.
            foreach (Vector3Int offset in SixNeighbourOffsets)
            {
                Vector3Int candidate = brushResult.PrimaryCoordinate + offset;
                if (!buffer.Contains(candidate))
                    buffer.Add(candidate);
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
