using System;
using System.Collections.Generic;
using Supernova.Inputs;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Voxels.Support.Prototype
{
    /// <summary>
    /// Scene runner for the voxel-support prototype.
    ///
    /// Creates a small voxel volume with a demo terrain layout, renders it as
    /// coloured cubes, and lets the player click to destroy individual voxels.
    /// After each destruction the support graph is re-analysed and the
    /// visualization updates to show stable / fragile / collapsed voxels.
    ///
    /// This prototype uses <see cref="VoxelVolume"/> (the existing 32³ data
    /// store) and the pure-logic <see cref="VoxelSupportGraph"/>.  It does NOT
    /// modify any existing production code.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class VoxelSupportPrototype : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────
        [Header("Config")]
        [SerializeField] private VoxelSupportConfig config;
        [SerializeField, Min(1)] private int volumeSize = 16;

        [Header("Visualization")]
        [SerializeField] private float cubeSize = 0.95f;
        [SerializeField] private Material defaultSolidMaterial;
        [SerializeField] private Material bedrockMaterial;
        [SerializeField] private Material fragileMaterial;
        [SerializeField] private Material collapsedMaterial;
        [SerializeField] private Material airMaterial;

        // ── Runtime state ────────────────────────────────────────────
        private VoxelVolume volume;
        private VoxelSupportGraph supportGraph;
        private readonly Dictionary<Vector3Int, GameObject> cubeLookup = new(4096);
        private readonly Dictionary<Vector3Int, MeshRenderer> rendererLookup = new(4096);
        private Camera cam;

        // ── Unity callbacks ──────────────────────────────────────────

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (config == null)
            {
                Debug.LogError(
                    "[VoxelSupportPrototype] Assign a VoxelSupportConfig asset in the Inspector.",
                    this);
                enabled = false;
                return;
            }

            supportGraph = new VoxelSupportGraph(config);

            // Build the volume.
            volume = new VoxelVolume(initialDensity: -1f, VoxelTypeId.Air);
            BuildDemoTerrain();

            // Instantiate visualization cubes.
            BuildVisualization();

            // Run an initial analysis so fragile voxels are highlighted on start.
            RunFullSupportAnalysis(new List<Vector3Int>());
        }

        private void Update()
        {
            if (GameInput.Pressed(GameInputActionId.PrototypeReset))
            {
                ResetScene();
                return;
            }

            if (GameInput.Pressed(GameInputActionId.Click))
            {
                TryDestroyVoxelUnderCursor();
            }
        }

        // ── Scene reset ──────────────────────────────────────────────

        private void ResetScene()
        {
            // Tear down old visualization.
            foreach (GameObject go in cubeLookup.Values)
            {
                if (go != null) Destroy(go);
            }

            cubeLookup.Clear();
            rendererLookup.Clear();

            // Rebuild.
            volume.Fill(-1f, VoxelTypeId.Air);
            BuildDemoTerrain();
            BuildVisualization();
            RunFullSupportAnalysis(new List<Vector3Int>());
        }

        // ── Demo terrain builder ─────────────────────────────────────

        /// <summary>
        /// Builds a small demo terrain that exercises edge cases:
        /// - A solid bedrock floor at Y=0.
        /// - A pillar (single-width stack) — removing its base should collapse it.
        /// - An arch (two pillars + bridge) — removing one pillar should collapse the bridge.
        /// - A floating block — should be immediately flagged as unsupported.
        /// - A cantilever shelf.
        /// </summary>
        private void BuildDemoTerrain()
        {
            int size = volumeSize;

            // ── Bedrock floor (Y = 0) ──
            for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
            {
                SetSolid(x, 0, z);
            }

            // ── Solid platform (Y = 1..2) covering most of the floor ──
            for (int x = 1; x < size - 1; x++)
            for (int z = 1; z < size - 1; z++)
            {
                SetSolid(x, 1, z);
                if (x > 2 && x < size - 3 && z > 2 && z < size - 3)
                    SetSolid(x, 2, z);
            }

            // ── Pillar (X = 4, Z = 4, Y = 3..8) ──
            for (int y = 3; y <= 8; y++)
                SetSolid(4, y, 4);

            // ── Arch: two pillars (X=7,Z=7 and X=10,Z=7) + bridge ──
            for (int y = 3; y <= 6; y++)
            {
                SetSolid(7, y, 7);
                SetSolid(10, y, 7);
            }

            // Bridge at Y = 7 spanning X = 7..10
            for (int x = 7; x <= 10; x++)
                SetSolid(x, 7, 7);

            // Bridge top at Y = 8
            for (int x = 7; x <= 10; x++)
                SetSolid(x, 8, 7);

            // ── Cantilever shelf (X = 12..14, Z = 4, Y = 4, only one side supported) ──
            for (int z = 4; z <= 8; z++)
                SetSolid(12, 4, z);

            // Support pillar at Z = 4
            for (int y = 3; y <= 4; y++)
                SetSolid(12, y, 4);

            // ── Floating block (no connection to anything) ──
            SetSolid(size - 3, size / 2, size - 3);
        }

        private void SetSolid(int x, int y, int z)
        {
            if (!volume.IsInBounds(x, y, z)) return;
            volume.SetSample(x, y, z, 1f, VoxelTypeId.Default);
        }

        private bool IsSolid(int x, int y, int z)
        {
            return volume.IsInBounds(x, y, z)
                   && volume.GetSample(x, y, z).IsSolid(0f);
        }

        // ── Visualization ────────────────────────────────────────────

        private void BuildVisualization()
        {
            Transform parent = new GameObject("VoxelCubes").transform;
            parent.SetParent(transform, worldPositionStays: false);

            int size = volumeSize;
            for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            for (int z = 0; z < size; z++)
            {
                Vector3Int pos = new(x, y, z);
                bool solid = IsSolid(x, y, z);
                bool isBedrock = y <= config.BedrockYThreshold && solid;

                GameObject cube = CreateVoxelCube(pos, solid, isBedrock, parent);
                cubeLookup[pos] = cube;
                if (cube.TryGetComponent(out MeshRenderer mr))
                    rendererLookup[pos] = mr;
            }
        }

        private GameObject CreateVoxelCube(
            Vector3Int pos, bool solid, bool isBedrock, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Voxel_{pos.x}_{pos.y}_{pos.z}";

            // Strip the default sphere collider added by CreatePrimitive side-effect.
            // Actually CreatePrimitive creates a Cube with BoxCollider — that's fine.
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = new Vector3(pos.x, pos.y, pos.z);
            go.transform.localScale = Vector3.one * cubeSize;

            if (go.TryGetComponent(out MeshRenderer mr))
            {
                mr.material = solid
                    ? (isBedrock ? (bedrockMaterial ?? defaultSolidMaterial) : defaultSolidMaterial)
                    : airMaterial;
            }

            // Tag for raycasting.
            go.tag = "Untagged";

            return go;
        }

        // ── Interaction ──────────────────────────────────────────────

        private void TryDestroyVoxelUnderCursor()
        {
            Ray ray = cam.ScreenPointToRay(
                GameInput.ReadVector2(GameInputActionId.Point));
            if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance: 200f))
                return;

            // Find which voxel cube was hit.
            foreach (var kv in cubeLookup)
            {
                if (kv.Value == hit.collider.gameObject)
                {
                    Vector3Int pos = kv.Key;
                    if (IsSolid(pos.x, pos.y, pos.z))
                    {
                        DestroyVoxel(pos);
                    }

                    return;
                }
            }
        }

        private void DestroyVoxel(Vector3Int pos)
        {
            Debug.Log($"[VoxelSupportPrototype] Destroying voxel at {pos}");

            // Mutate the volume.
            volume.SetSample(pos.x, pos.y, pos.z, -1f, VoxelTypeId.Air);

            // Turn the cube transparent.
            if (rendererLookup.TryGetValue(pos, out MeshRenderer mr))
            {
                mr.material = airMaterial;
            }

            // Run support analysis.
            var removed = new List<Vector3Int> { pos };
            RunFullSupportAnalysis(removed);
        }

        // ── Analysis ─────────────────────────────────────────────────

        private void RunFullSupportAnalysis(IReadOnlyList<Vector3Int> removedVoxels)
        {
            SupportAnalysisResult result;
            if (removedVoxels == null || removedVoxels.Count == 0)
            {
                result = supportGraph.FullScan(
                    solidity: p => IsSolid(p.x, p.y, p.z),
                    isAnchor: p => p.y <= config.BedrockYThreshold && IsSolid(p.x, p.y, p.z),
                    volumeSizeX: volumeSize,
                    volumeSizeY: volumeSize,
                    volumeSizeZ: volumeSize);
            }
            else
            {
                result = supportGraph.Analyze(
                    removedVoxels,
                    solidity: p => IsSolid(p.x, p.y, p.z),
                    isAnchor: p => p.y <= config.BedrockYThreshold && IsSolid(p.x, p.y, p.z));
            }

            // Update visualization.
            HashSet<Vector3Int> collapsedSet = new(result.CollapsedVoxels ?? Array.Empty<Vector3Int>());
            HashSet<Vector3Int> fragileSet = new(result.FragileVoxels ?? Array.Empty<Vector3Int>());

            foreach (var kv in rendererLookup)
            {
                Vector3Int pos = kv.Key;
                if (!IsSolid(pos.x, pos.y, pos.z))
                {
                    // Already air — keep transparent.
                    kv.Value.material = airMaterial;
                    continue;
                }

                if (collapsedSet.Contains(pos))
                    kv.Value.material = collapsedMaterial;
                else if (fragileSet.Contains(pos))
                    kv.Value.material = fragileMaterial;
                else if (pos.y <= config.BedrockYThreshold)
                    kv.Value.material = bedrockMaterial ?? defaultSolidMaterial;
                else
                    kv.Value.material = defaultSolidMaterial;
            }

            // Log summary.
            int collapsed = result.CollapsedVoxels?.Count ?? 0;
            int fragile = result.FragileVoxels?.Count ?? 0;
            int affected = result.AffectedVoxels?.Count ?? 0;

            Debug.Log(
                $"[VoxelSupportPrototype] Analysis complete: "
                + $"{collapsed} collapsed, {fragile} fragile, "
                + $"{affected} affected, "
                + $"{result.CascadeIterationsUsed} cascade iterations.");
        }

        // ── Gizmos (Scene View overlay) ──────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || volume == null) return;

            int size = volumeSize;
            Gizmos.color = Color.yellow;
            for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            for (int z = 0; z < size; z++)
            {
                if (!IsSolid(x, y, z)) continue;

                // Draw wireframe for all solid voxels for orientation.
                Vector3 center = new(x, y, z);
                Gizmos.DrawWireCube(center, Vector3.one * cubeSize);
            }
        }
#endif
    }
}
