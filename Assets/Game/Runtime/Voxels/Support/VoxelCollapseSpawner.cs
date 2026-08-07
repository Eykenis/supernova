using System.Collections.Generic;
using Supernova.MinecraftCaves;
using Supernova.Voxels.Support.Prototype;
using UnityEngine;

namespace Supernova.Voxels.Support
{
    /// <summary>
    /// Creates rigid-body rubble GameObjects for collapsed voxel sets.
    ///
    /// Supports two collapse-detection modes, selectable via
    /// <see cref="collapseMode"/>:
    ///
    /// <b>BoundaryConnectivity</b> (default) — After a voxel is removed,
    /// collects the 6-connected solid sub-graph reachable from each of the
    /// removal's 6 neighbours.  If the sub-graph contains no world-boundary
    /// voxel (bedrock on any of the six faces), the entire sub-graph is
    /// converted to physics rubble and cleared.
    ///
    /// <b>StressPropagation</b> — Uses <see cref="VoxelSupportGraph.Analyze"/>
    /// (EBC_LB dual-BFS).  Collapsed sets are pre-computed and this spawner
    /// only creates the rubble meshes.
    /// </summary>
    public sealed class VoxelCollapseSpawner : MonoBehaviour
    {
        public enum CollapseMode
        {
            StressPropagation,
            BoundaryConnectivity,
        }

        // ═══════════════════════════════════════════════════════════════
        //  Inspector
        // ═══════════════════════════════════════════════════════════════

        [Header("Collapse mode")]
        [SerializeField]
        private CollapseMode collapseMode = CollapseMode.BoundaryConnectivity;

        [Header("Boundary mode")]
        [SerializeField]
        [Tooltip("Max BFS radius from removal.")]
        private int boundarySearchRadius = 8;

        [SerializeField]
        [Tooltip("Max voxels in a single component.")]
        private int maxComponentVoxels = 1024;

        [Header("Rubble tuning")]
        [SerializeField]
        private int maxRubblePiecesPerFrame = 8;

        [SerializeField]
        private float baseEjectionSpeed = 1.8f;

        [SerializeField]
        [Range(0.4f, 1.2f)]
        private float rubbleScale = 0.85f;

        [SerializeField]
        private Material fallbackMaterial;

        [Header("Debug")]
        [SerializeField]
        private bool logSpawnEvents = true;

        // ═══════════════════════════════════════════════════════════════
        //  Public API
        // ═══════════════════════════════════════════════════════════════

        public CollapseMode ActiveMode => collapseMode;

        public HashSet<Vector3Int> DetectAndSpawn(
            IReadOnlyList<Vector3Int> removedWorldPositions,
            InfiniteVoxelWorld world,
            float isoLevel,
            float voxelSize,
            VoxelTypeCatalog catalog,
            Transform terrainTransform,
            IReadOnlyList<Vector3Int> preCollapsed = null)
        {
            if (collapseMode == CollapseMode.BoundaryConnectivity)
            {
                return RunBoundaryConnectivityMode(
                    removedWorldPositions, world, isoLevel,
                    voxelSize, catalog, terrainTransform);
            }
            return SpawnCollapseRubble(
                preCollapsed, world, isoLevel,
                voxelSize, catalog, terrainTransform);
        }

        public HashSet<Vector3Int> SpawnCollapseRubble(
            IReadOnlyList<Vector3Int> collapsedVoxels,
            InfiniteVoxelWorld world,
            float isoLevel,
            float voxelSize,
            VoxelTypeCatalog catalog,
            Transform terrainTransform)
        {
            return SpawnRubbleFromSet(
                collapsedVoxels, world, isoLevel,
                voxelSize, catalog, terrainTransform);
        }

        // ═══════════════════════════════════════════════════════════════
        //  Boundary Connectivity mode
        // ═══════════════════════════════════════════════════════════════

        private HashSet<Vector3Int> RunBoundaryConnectivityMode(
            IReadOnlyList<Vector3Int> removedWorldPositions,
            InfiniteVoxelWorld world,
            float isoLevel,
            float voxelSize,
            VoxelTypeCatalog catalog,
            Transform terrainTransform)
        {
            if (removedWorldPositions == null || removedWorldPositions.Count == 0)
                return new HashSet<Vector3Int>();

            int minX, maxX, minZ, maxZ;
            ComputeWorldExtents(world, out minX, out maxX, out minZ, out maxZ);
            int minY = 0;
            int maxY = VoxelColumnChunkData.Height - 1;

            HashSet<Vector3Int> removedSet = new(removedWorldPositions);

            // Step 1: gather ALL solid neighbours of ALL removed voxels
            // into a single seed set.
            HashSet<Vector3Int> allSeeds = new();
            foreach (Vector3Int removed in removedWorldPositions)
            {
                foreach (Vector3Int offset in SixNeighbour)
                {
                    Vector3Int n = removed + offset;
                    if (removedSet.Contains(n)) continue;
                    if (!IsSolid(n, world, isoLevel)) continue;
                    allSeeds.Add(n);
                }
            }

            if (allSeeds.Count == 0)
                return new HashSet<Vector3Int>();

            // Step 2: single unified BFS from all seeds together,
            // treating removedSet as air.
            HashSet<Vector3Int> unifiedComponent = CollectUnifiedComponent(
                allSeeds, removedSet, world, isoLevel,
                minX, maxX, minY, maxY, minZ, maxZ);

            // Step 3: does this component touch ANY world boundary?
            bool touchesBoundary = false;
            foreach (Vector3Int pos in unifiedComponent)
            {
                if (pos.x <= minX || pos.x >= maxX
                    || pos.y <= minY || pos.y >= maxY
                    || pos.z <= minZ || pos.z >= maxZ)
                {
                    touchesBoundary = true;
                    break;
                }
            }

            // Step 4: if not touching any boundary → the entire
            // component is floating → spawn rubble.
            if (!touchesBoundary && unifiedComponent.Count > 0)
            {
                List<Vector3Int> collapseList = new(unifiedComponent);
                return SpawnRubbleFromSet(
                    collapseList, world, isoLevel,
                    voxelSize, catalog, terrainTransform);
            }

            return new HashSet<Vector3Int>();
        }

        /// <summary>
        /// Multi-source BFS from all seeds simultaneously.  Treats voxels
        /// in <paramref name="removedSet"/> as air.  Capped at 4096 voxels.
        private static HashSet<Vector3Int> CollectUnifiedComponent(
            HashSet<Vector3Int> seeds,
            HashSet<Vector3Int> removedSet,
            InfiniteVoxelWorld world,
            float isoLevel,
            int minX, int maxX, int minY, int maxY, int minZ, int maxZ)
        {
            const int maxVoxels = 4096;
            const int maxRadius = 24;

            HashSet<Vector3Int> component = new(64);
            Queue<Vector3Int> queue = new(64);
            HashSet<Vector3Int> visited = new(64);

            foreach (Vector3Int seed in seeds)
            {
                component.Add(seed);
                visited.Add(seed);
                queue.Enqueue(seed);
            }

            while (queue.Count > 0 && component.Count < maxVoxels)
            {
                Vector3Int current = queue.Dequeue();

                foreach (Vector3Int dir in SixNeighbour)
                {
                    Vector3Int n = current + dir;
                    if (n.x < minX || n.x > maxX) continue;
                    if (n.y < minY || n.y > maxY) continue;
                    if (n.z < minZ || n.z > maxZ) continue;
                    if (removedSet.Contains(n)) continue;
                    if (!IsSolid(n, world, isoLevel)) continue;
                    if (!visited.Add(n)) continue;

                    queue.Enqueue(n);
                    component.Add(n);
                }
            }

            while (queue.Count > 0)
                component.Add(queue.Dequeue());

            return component;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Shared rubble spawner
        // ═══════════════════════════════════════════════════════════════

        private HashSet<Vector3Int> SpawnRubbleFromSet(
            IReadOnlyList<Vector3Int> collapsedVoxels,
            InfiniteVoxelWorld world,
            float isoLevel,
            float voxelSize,
            VoxelTypeCatalog catalog,
            Transform terrainTransform)
        {
            HashSet<Vector3Int> dirtyChunks = new();
            if (collapsedVoxels == null || collapsedVoxels.Count == 0)
                return dirtyChunks;

            HashSet<Vector3Int> remaining = new(collapsedVoxels);
            List<HashSet<Vector3Int>> components = new(4);

            while (remaining.Count > 0
                   && components.Count < maxRubblePiecesPerFrame)
            {
                Vector3Int seed = default;
                foreach (Vector3Int v in remaining) { seed = v; break; }

                HashSet<Vector3Int> component = FloodFill(seed, remaining);
                remaining.ExceptWith(component);
                components.Add(component);
            }

            for (int i = 0; i < components.Count; i++)
            {
                HashSet<Vector3Int> component = components[i];
                if (component.Count == 0) continue;

                VoxelTypeId dominantType = ResolveDominantType(component, world);
                VoxelMeshData meshData = MarchingCubesMesher.BuildTypeComponent(
                    world, component, dominantType,
                    isoLevel, voxelSize,
                    MarchingCubesVertexPlacement.EdgeMidpoint);

                if (meshData.Vertices.Count == 0)
                {
                    ClearComponentVoxels(component, world, isoLevel, dirtyChunks);
                    continue;
                }

                CreateRubblePiece(
                    component, dominantType, meshData,
                    voxelSize, catalog, terrainTransform);
                ClearComponentVoxels(component, world, isoLevel, dirtyChunks);

                if (logSpawnEvents)
                {
                    Debug.Log(
                        $"[VoxelCollapseSpawner] Rubble "
                        + $"({component.Count} voxels, {meshData.TriangleCount} tris) "
                        + $"mode={collapseMode}");
                }
            }

            return dirtyChunks;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Helpers
        // ═══════════════════════════════════════════════════════════════

        private static readonly Vector3Int[] SixNeighbour =
        {
            new( 1, 0, 0), new(-1, 0, 0),
            new( 0, 1, 0), new( 0,-1, 0),
            new( 0, 0, 1), new( 0, 0,-1),
        };

        private static HashSet<Vector3Int> FloodFill(
            Vector3Int seed, HashSet<Vector3Int> candidateSet)
        {
            HashSet<Vector3Int> comp = new(32) { seed };
            Queue<Vector3Int> q = new(32); q.Enqueue(seed);
            while (q.Count > 0)
            {
                Vector3Int c = q.Dequeue();
                foreach (Vector3Int o in SixNeighbour)
                {
                    Vector3Int n = c + o;
                    if (!candidateSet.Contains(n)) continue;
                    if (!comp.Add(n)) continue;
                    q.Enqueue(n);
                }
            }
            return comp;
        }

        private static bool IsSolid(Vector3Int pos, InfiniteVoxelWorld world, float iso)
        {
            return world.TryGetSample(pos.x, pos.y, pos.z, out VoxelSample s)
                   && s.IsSolid(iso);
        }

        private static int Manhattan(Vector3Int a, Vector3Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z);
        }

        private static void ComputeWorldExtents(
            InfiniteVoxelWorld world,
            out int minX, out int maxX,
            out int minZ, out int maxZ)
        {
            minX = int.MaxValue; maxX = int.MinValue;
            minZ = int.MaxValue; maxZ = int.MinValue;
            foreach (var kv in world.Chunks)
            {
                int cx = kv.Key.x * VoxelColumnChunkData.Width;
                int cz = kv.Key.y * VoxelColumnChunkData.Depth;
                if (cx < minX) minX = cx;
                if (cx + VoxelColumnChunkData.Width - 1 > maxX) maxX = cx + VoxelColumnChunkData.Width - 1;
                if (cz < minZ) minZ = cz;
                if (cz + VoxelColumnChunkData.Depth - 1 > maxZ) maxZ = cz + VoxelColumnChunkData.Depth - 1;
            }
            if (minX == int.MaxValue) { minX = -16; maxX = 16; minZ = -16; maxZ = 16; }
        }

        private static VoxelTypeId ResolveDominantType(
            HashSet<Vector3Int> component, InfiniteVoxelWorld world)
        {
            Dictionary<ushort, int> counts = new(4);
            foreach (Vector3Int pos in component)
            {
                if (world.TryGetSample(pos.x, pos.y, pos.z, out VoxelSample s) && s.IsSolid(0f))
                {
                    ushort k = s.Type.Value;
                    counts[k] = counts.TryGetValue(k, out int c) ? c + 1 : 1;
                }
            }
            ushort best = 0; int bestC = 0;
            foreach (var kv in counts)
                if (kv.Value > bestC) { bestC = kv.Value; best = kv.Key; }
            return bestC > 0 ? new VoxelTypeId(best) : VoxelTypeId.Default;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Rubble GameObject creation
        // ═══════════════════════════════════════════════════════════════

        private void CreateRubblePiece(
            HashSet<Vector3Int> component, VoxelTypeId type, VoxelMeshData meshData,
            float voxelSize, VoxelTypeCatalog catalog, Transform terrainTransform)
        {
            Vector3 centre = Vector3.zero;
            foreach (Vector3Int p in component) centre += (Vector3)p * voxelSize;
            centre /= component.Count;
            for (int i = 0; i < meshData.Vertices.Count; i++)
                meshData.Vertices[i] -= centre;

            Mesh mesh = meshData.CreateMesh($"Rubble_{type.Value}_{component.Count}");
            mesh.hideFlags = HideFlags.DontSave;

            var go = new GameObject($"Rubble_{type.Value}_{component.Count}vx");
            go.hideFlags = HideFlags.DontSave;
            Vector3 worldPos = terrainTransform != null
                ? terrainTransform.TransformPoint(centre) : centre;
            go.transform.SetPositionAndRotation(worldPos, UnityEngine.Random.rotation);
            float s = voxelSize * rubbleScale;
            go.transform.localScale = new Vector3(s, s, s);

            MeshFilter f = go.AddComponent<MeshFilter>();
            f.sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            Material mat = ResolveMaterial(type, catalog);
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            Mesh colliderMesh = BuildAabbHull(component, centre, voxelSize);
            MeshCollider mc = go.AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = colliderMesh;
            colliderMesh.hideFlags = HideFlags.DontSave;

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = component.Count * 0.25f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            Vector3 ejection = (UnityEngine.Random.insideUnitSphere + Vector3.up * 0.8f).normalized;
            body.velocity = ejection * (baseEjectionSpeed + UnityEngine.Random.Range(-0.3f, 0.3f));
            body.angularVelocity = UnityEngine.Random.insideUnitSphere * 2.5f;
        }

        private Material ResolveMaterial(VoxelTypeId type, VoxelTypeCatalog catalog)
        {
            if (catalog != null)
            {
                VoxelTypeDefinition def = catalog.Find(type);
                if (def != null && def.Material != null) return def.Material;
            }
            return fallbackMaterial != null ? fallbackMaterial : EnsureFallback();
        }

        private Material EnsureFallback()
        {
            if (fallbackMaterial != null) return fallbackMaterial;
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            fallbackMaterial = new Material(s) { color = new Color(0.4f, 0.38f, 0.35f) };
            return fallbackMaterial;
        }

        private static Mesh BuildAabbHull(
            HashSet<Vector3Int> component, Vector3 centre, float voxelSize)
        {
            float h = voxelSize * 0.5f;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (Vector3Int p in component)
            {
                Vector3 b = (Vector3)p * voxelSize - centre;
                Vector3 c0 = b + new Vector3(-h, -h, -h);
                Vector3 c1 = b + new Vector3( h,  h,  h);
                min = Vector3.Min(min, c0);
                max = Vector3.Max(max, c1);
            }
            if (float.IsInfinity(min.x))
            {
                min = new Vector3(-h, -h, -h);
                max = new Vector3( h,  h,  h);
            }

            Vector3[] verts =
            {
                new(min.x, min.y, min.z), new(max.x, min.y, min.z),
                new(min.x, max.y, min.z), new(max.x, max.y, min.z),
                new(min.x, min.y, max.z), new(max.x, min.y, max.z),
                new(min.x, max.y, max.z), new(max.x, max.y, max.z),
            };
            int[] tris =
            {
                0,2,1, 1,2,3, 4,5,6, 5,7,6,
                0,1,4, 1,5,4, 2,6,3, 3,6,7,
                0,4,2, 2,4,6, 1,3,5, 3,7,5,
            };
            Mesh m = new() { hideFlags = HideFlags.DontSave };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static void ClearComponentVoxels(
            HashSet<Vector3Int> component,
            InfiniteVoxelWorld world,
            float isoLevel,
            HashSet<Vector3Int> dirtyChunks)
        {
            VoxelTypeId air = VoxelTypeId.Air;
            float airD = isoLevel - 1f;
            foreach (Vector3Int pos in component)
            {
                world.SetVoxel(pos.x, pos.y, pos.z, airD, air);
                Vector3Int cc = InfiniteVoxelWorld.WorldToChunk(pos.x, pos.y, pos.z);
                int sec = pos.y / MinecraftCaveInfiniteWorld.MeshSectionHeight;
                dirtyChunks.Add(new Vector3Int(cc.x, sec, cc.z));
            }
        }
    }
}
