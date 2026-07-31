using System.Collections.Generic;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures
{
    /// <summary>
    /// Navigation-only support supplied by independent runtime meshes. Nothing in
    /// this registry changes cave density or requests a terrain mesh rebuild.
    /// </summary>
    public static class DynamicCreatureNavigation
    {
        private const int MaximumLinkHorizontalDistance = 3;
        private const int MaximumLinkDrop = 3;

        private static readonly Vector3Int[] HorizontalDirections =
        {
            new Vector3Int(-1, 0, -1),
            new Vector3Int(0, 0, -1),
            new Vector3Int(1, 0, -1),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 1),
            new Vector3Int(0, 0, 1),
            new Vector3Int(1, 0, 1),
        };

        private static readonly int[] LinkVerticalOffsets =
        {
            0, 1, -1, 2, -2, 3, -3, 4,
        };

        private static readonly Dictionary<MinecraftCaveInfiniteWorld, WorldState>
            States = new Dictionary<MinecraftCaveInfiniteWorld, WorldState>();

        public static void RegisterPlatform(
            MinecraftCaveInfiniteWorld caveWorld,
            Object owner,
            Vector3 worldCenter,
            float worldRadius,
            float worldTopOffset)
        {
            if (caveWorld == null || owner == null)
            {
                return;
            }

            WorldState state = GetOrCreateState(caveWorld);
            state.Platforms[owner.GetInstanceID()] = new PlatformData(
                worldCenter,
                Mathf.Max(0.01f, worldRadius),
                worldTopOffset);
            Rebuild(state);
        }

        public static void UnregisterPlatform(
            MinecraftCaveInfiniteWorld caveWorld,
            Object owner)
        {
            if (caveWorld == null
                || owner == null
                || !States.TryGetValue(caveWorld, out WorldState state)
                || !state.Platforms.Remove(owner.GetInstanceID()))
            {
                return;
            }

            Rebuild(state);
            if (state.Platforms.Count == 0)
            {
                States.Remove(caveWorld);
            }
        }

        public static bool ContainsSupport(
            MinecraftCaveInfiniteWorld caveWorld,
            Vector3Int support)
        {
            return caveWorld != null
                && States.TryGetValue(caveWorld, out WorldState state)
                && state.Supports.Contains(support);
        }

        public static int GetRevision(MinecraftCaveInfiniteWorld caveWorld)
        {
            return caveWorld != null
                && States.TryGetValue(caveWorld, out WorldState state)
                ? state.Revision
                : 0;
        }

        public static void GetTraversalLinks(
            MinecraftCaveInfiniteWorld caveWorld,
            Vector3Int fromSupport,
            List<CreatureTraversalLink> results)
        {
            results.Clear();
            if (caveWorld == null
                || !States.TryGetValue(caveWorld, out WorldState state)
                || !state.Links.TryGetValue(
                    fromSupport,
                    out List<CreatureTraversalLink> links))
            {
                return;
            }

            results.AddRange(links);
        }

        public static bool TryGetTraversalLink(
            MinecraftCaveInfiniteWorld caveWorld,
            Vector3Int fromSupport,
            Vector3Int toSupport,
            out CreatureTraversalLink link)
        {
            link = default;
            if (caveWorld == null
                || !States.TryGetValue(caveWorld, out WorldState state)
                || !state.Links.TryGetValue(
                    fromSupport,
                    out List<CreatureTraversalLink> links))
            {
                return false;
            }

            for (int i = 0; i < links.Count; i++)
            {
                if (links[i].ToSupport != toSupport)
                {
                    continue;
                }

                link = links[i];
                return true;
            }

            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            States.Clear();
        }

        private static WorldState GetOrCreateState(
            MinecraftCaveInfiniteWorld caveWorld)
        {
            if (!States.TryGetValue(caveWorld, out WorldState state))
            {
                state = new WorldState(caveWorld);
                States.Add(caveWorld, state);
            }

            return state;
        }

        private static void Rebuild(WorldState state)
        {
            state.Supports.Clear();
            state.Links.Clear();
            foreach (PlatformData platform in state.Platforms.Values)
            {
                AddPlatformSupports(state, platform);
            }

            foreach (Vector3Int support in state.Supports)
            {
                AddPlatformLinks(state, support);
            }

            state.Revision++;
        }

        private static void AddPlatformSupports(
            WorldState state,
            PlatformData platform)
        {
            MinecraftCaveInfiniteWorld caveWorld = state.CaveWorld;
            float voxelSize = Mathf.Max(0.01f, caveWorld.VoxelSize);
            Vector3 localCenter =
                caveWorld.transform.InverseTransformPoint(platform.WorldCenter)
                / voxelSize;
            Vector3 localTop =
                caveWorld.transform.InverseTransformPoint(
                    platform.WorldCenter + Vector3.up * platform.WorldTopOffset)
                / voxelSize;
            float radiusInVoxels = platform.WorldRadius / voxelSize;
            int supportY = Mathf.RoundToInt(localTop.y) - 1;
            int minimumX = Mathf.FloorToInt(localCenter.x - radiusInVoxels);
            int maximumX = Mathf.CeilToInt(localCenter.x + radiusInVoxels);
            int minimumZ = Mathf.FloorToInt(localCenter.z - radiusInVoxels);
            int maximumZ = Mathf.CeilToInt(localCenter.z + radiusInVoxels);

            for (int z = minimumZ; z <= maximumZ; z++)
            {
                for (int x = minimumX; x <= maximumX; x++)
                {
                    float localX = x - localCenter.x;
                    float localZ = z - localCenter.z;
                    if (!IsInsideRegularPolygon(
                            localX,
                            localZ,
                            radiusInVoxels,
                            SolidVoxelPrototype.PlatformSides))
                    {
                        continue;
                    }

                    state.Supports.Add(new Vector3Int(x, supportY, z));
                }
            }
        }

        private static bool IsInsideRegularPolygon(
            float x,
            float z,
            float radius,
            int sideCount)
        {
            float inradius = radius * Mathf.Cos(Mathf.PI / sideCount);
            for (int side = 0; side < sideCount; side++)
            {
                float angle = (side + 0.5f) * Mathf.PI * 2f / sideCount;
                float projection =
                    x * Mathf.Cos(angle) + z * Mathf.Sin(angle);
                if (projection > inradius + 0.001f)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddPlatformLinks(
            WorldState state,
            Vector3Int fromSupport)
        {
            for (int directionIndex = 0;
                directionIndex < HorizontalDirections.Length;
                directionIndex++)
            {
                Vector3Int direction = HorizontalDirections[directionIndex];
                bool found = false;
                for (int distance = 1;
                    distance <= MaximumLinkHorizontalDistance && !found;
                    distance++)
                {
                    Vector3Int horizontal =
                        new Vector3Int(direction.x * distance, 0, direction.z * distance);
                    for (int yIndex = 0;
                        yIndex < LinkVerticalOffsets.Length;
                        yIndex++)
                    {
                        int verticalOffset = LinkVerticalOffsets[yIndex];
                        Vector3Int candidate = fromSupport
                            + horizontal
                            + Vector3Int.up * verticalOffset;
                        if (!IsBasicStandable(state, candidate))
                        {
                            continue;
                        }

                        if (!CanUseNormalTransition(
                                state,
                                fromSupport,
                                candidate))
                        {
                            AddLink(state, fromSupport, candidate);
                        }

                        if (!CanUseNormalTransition(
                                state,
                                candidate,
                                fromSupport))
                        {
                            AddLink(state, candidate, fromSupport);
                        }

                        found = true;
                        break;
                    }
                }
            }
        }

        private static bool CanUseNormalTransition(
            WorldState state,
            Vector3Int fromSupport,
            Vector3Int toSupport)
        {
            Vector3Int delta = toSupport - fromSupport;
            if (Mathf.Abs(delta.x) > 1
                || Mathf.Abs(delta.z) > 1
                || (delta.x == 0 && delta.z == 0))
            {
                return false;
            }

            Vector3Int adjacent = fromSupport + new Vector3Int(
                System.Math.Sign(delta.x),
                0,
                System.Math.Sign(delta.z));
            if (!TryGetCombinedSolid(
                    state,
                    adjacent,
                    out bool adjacentSolid))
            {
                return false;
            }

            if (adjacentSolid)
            {
                for (int rise = 0; rise <= 1; rise++)
                {
                    Vector3Int candidate =
                        adjacent + Vector3Int.up * rise;
                    if (!IsBasicStandable(state, candidate))
                    {
                        continue;
                    }

                    return candidate == toSupport;
                }

                return false;
            }

            for (int drop = 1; drop <= MaximumLinkDrop; drop++)
            {
                Vector3Int candidate =
                    adjacent + Vector3Int.down * drop;
                if (!TryGetCombinedSolid(
                        state,
                        candidate,
                        out bool candidateSolid))
                {
                    return false;
                }

                if (!candidateSolid)
                {
                    continue;
                }

                return candidate == toSupport
                    && IsBasicStandable(state, candidate);
            }

            return false;
        }

        private static bool IsBasicStandable(
            WorldState state,
            Vector3Int support)
        {
            return TryGetCombinedSolid(state, support, out bool supportSolid)
                && supportSolid
                && TryGetCombinedSolid(
                    state,
                    support + Vector3Int.up,
                    out bool footSolid)
                && !footSolid;
        }

        private static bool TryGetCombinedSolid(
            WorldState state,
            Vector3Int voxel,
            out bool isSolid)
        {
            if (state.Supports.Contains(voxel))
            {
                isSolid = true;
                return true;
            }

            InfiniteVoxelWorld world = state.CaveWorld.World;
            if (world == null
                || !world.TryGetDensity(
                    voxel.x,
                    voxel.y,
                    voxel.z,
                    out float density))
            {
                isSolid = false;
                return false;
            }

            isSolid = density >= state.CaveWorld.IsoLevel;
            return true;
        }

        private static void AddLink(
            WorldState state,
            Vector3Int from,
            Vector3Int to)
        {
            Vector3Int delta = to - from;
            int horizontalCost = Mathf.Abs(delta.x) + Mathf.Abs(delta.z);
            int verticalCost = delta.y > 0
                ? delta.y * 2
                : Mathf.Abs(delta.y);
            float horizontalDistance = new Vector2(delta.x, delta.z).magnitude;
            var link = new CreatureTraversalLink(
                to,
                horizontalCost + verticalCost,
                horizontalDistance,
                delta.y);

            if (!state.Links.TryGetValue(
                    from,
                    out List<CreatureTraversalLink> links))
            {
                links = new List<CreatureTraversalLink>();
                state.Links.Add(from, links);
            }

            for (int i = 0; i < links.Count; i++)
            {
                if (links[i].ToSupport == to)
                {
                    return;
                }
            }

            links.Add(link);
        }

        private readonly struct PlatformData
        {
            public PlatformData(
                Vector3 worldCenter,
                float worldRadius,
                float worldTopOffset)
            {
                WorldCenter = worldCenter;
                WorldRadius = worldRadius;
                WorldTopOffset = worldTopOffset;
            }

            public Vector3 WorldCenter { get; }
            public float WorldRadius { get; }
            public float WorldTopOffset { get; }
        }

        private sealed class WorldState
        {
            public WorldState(MinecraftCaveInfiniteWorld caveWorld)
            {
                CaveWorld = caveWorld;
            }

            public MinecraftCaveInfiniteWorld CaveWorld { get; }
            public Dictionary<int, PlatformData> Platforms { get; } =
                new Dictionary<int, PlatformData>();
            public HashSet<Vector3Int> Supports { get; } =
                new HashSet<Vector3Int>();
            public Dictionary<Vector3Int, List<CreatureTraversalLink>> Links { get; } =
                new Dictionary<Vector3Int, List<CreatureTraversalLink>>();
            public int Revision { get; set; }
        }
    }
}
