using System.Collections.Generic;
using Supernova.Inputs;
using Supernova.Voxels.Integrity;
using UnityEngine;

namespace Supernova.Voxels.IntegrityExperiment
{
    /// <summary>
    /// Self-contained interactive harness. It owns an in-memory voxel column and
    /// is deliberately absent from every existing scene. Click removes one voxel,
    /// right-click removes a bomb-sized sphere, and the prototype-reset action
    /// reconstructs the fixture.
    /// </summary>
    public sealed class VoxelIntegrityExperimentController : MonoBehaviour
    {
        private const float IsoLevel = 0f;
        private const float VoxelSize = 1f;
        private const float BombRadius = 1.75f;
        private static readonly VoxelTypeId StoneType = VoxelTypeId.Default;
        private static readonly VoxelTypeId BedrockType = new VoxelTypeId(2);

        [SerializeField] private Camera experimentCamera;
        [SerializeField] private Material stoneMaterial;
        [SerializeField] private Material bedrockMaterial;
        [SerializeField] private Material rigidbodyMaterial;

        private InfiniteVoxelWorld world;
        private InfiniteVoxelIntegrityMap map;
        private VoxelIntegritySearch search;
        private readonly Dictionary<Vector3Int, GameObject> visuals =
            new Dictionary<Vector3Int, GameObject>();
        private readonly List<GameObject> rigidbodies = new List<GameObject>();
        private Material ownedStoneMaterial;
        private Material ownedBedrockMaterial;
        private Material ownedRigidbodyMaterial;

        private void Start()
        {
            search = new VoxelIntegritySearch();
            ResetExperiment();
        }

        private void Update()
        {
            if (GameInput.Pressed(GameInputActionId.PrototypeReset))
            {
                ResetExperiment();
                return;
            }

            if (GameInput.Pressed(GameInputActionId.Click))
                TryDestroyFromPointer(false);
            if (GameInput.Pressed(GameInputActionId.RightClick))
                TryDestroyFromPointer(true);
        }

        private void OnDestroy()
        {
            DestroyOwnedMaterial(ownedStoneMaterial);
            DestroyOwnedMaterial(ownedBedrockMaterial);
            DestroyOwnedMaterial(ownedRigidbodyMaterial);
        }

        public void ResetExperiment()
        {
            foreach (GameObject visual in visuals.Values)
            {
                if (visual != null)
                    Destroy(visual);
            }
            visuals.Clear();
            for (int i = 0; i < rigidbodies.Count; i++)
            {
                if (rigidbodies[i] != null)
                    Destroy(rigidbodies[i]);
            }
            rigidbodies.Clear();

            world = new InfiniteVoxelWorld();
            InfiniteVoxelChunk chunk = world.EnsureChunk(Vector2Int.zero);
            chunk.Data.Fill(-1f, VoxelTypeId.Air);
            map = new InfiniteVoxelIntegrityMap(world, IsoLevel, BedrockType);

            BuildBedrockSupportedFixture();
            BuildUnloadedBoundaryFixture();
            BuildBombFixture();
        }

        private void BuildBedrockSupportedFixture()
        {
            SetVoxel(new Vector3Int(7, 1, 10), BedrockType);
            SetVoxel(new Vector3Int(7, 2, 10), StoneType);
            SetVoxel(new Vector3Int(7, 3, 10), StoneType);
            SetVoxel(new Vector3Int(7, 4, 10), StoneType);
            SetVoxel(new Vector3Int(8, 4, 10), StoneType);
            SetVoxel(new Vector3Int(9, 4, 10), StoneType);
            SetVoxel(new Vector3Int(9, 5, 10), StoneType);
        }

        private void BuildUnloadedBoundaryFixture()
        {
            for (int x = 19; x < VoxelColumnChunkData.Width; x++)
                SetVoxel(new Vector3Int(x, 5, 17), StoneType);
        }

        private void BuildBombFixture()
        {
            SetVoxel(new Vector3Int(13, 1, 23), BedrockType);
            for (int y = 2; y <= 5; y++)
                SetVoxel(new Vector3Int(13, y, 23), StoneType);
            for (int x = 11; x <= 15; x++)
                SetVoxel(new Vector3Int(x, 6, 23), StoneType);
            for (int z = 21; z <= 25; z++)
                SetVoxel(new Vector3Int(13, 6, z), StoneType);
        }

        private void SetVoxel(Vector3Int coordinate, VoxelTypeId type)
        {
            world.SetVoxel(
                coordinate.x,
                coordinate.y,
                coordinate.z,
                1f,
                type);
            CreateStaticVisual(coordinate, type);
        }

        private void CreateStaticVisual(Vector3Int coordinate, VoxelTypeId type)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = type == BedrockType
                ? $"Bedrock_{coordinate.x}_{coordinate.y}_{coordinate.z}"
                : $"Stone_{coordinate.x}_{coordinate.y}_{coordinate.z}";
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = (Vector3)coordinate * VoxelSize;
            visual.transform.localScale = Vector3.one * VoxelSize;
            visual.GetComponent<MeshRenderer>().sharedMaterial = type == BedrockType
                ? ResolveBedrockMaterial()
                : ResolveStoneMaterial();
            VoxelIntegrityExperimentVoxel marker =
                visual.AddComponent<VoxelIntegrityExperimentVoxel>();
            marker.Configure(coordinate);
            visuals[coordinate] = visual;
        }

        private void TryDestroyFromPointer(bool bomb)
        {
            Camera camera = experimentCamera != null
                ? experimentCamera
                : Camera.main;
            if (camera == null)
                return;

            Ray ray = camera.ScreenPointToRay(
                GameInput.ReadVector2(GameInputActionId.Point));
            if (!Physics.Raycast(ray, out RaycastHit hit, 200f)
                || !hit.collider.TryGetComponent(
                    out VoxelIntegrityExperimentVoxel voxel))
            {
                return;
            }

            if (bomb)
                DestroyBomb(voxel.Coordinate);
            else
                DestroyVoxels(new[] { voxel.Coordinate });
        }

        private void DestroyBomb(Vector3Int centre)
        {
            int radius = Mathf.CeilToInt(BombRadius);
            float radiusSquared = BombRadius * BombRadius;
            var removed = new List<Vector3Int>();
            for (int z = -radius; z <= radius; z++)
                for (int y = -radius; y <= radius; y++)
                    for (int x = -radius; x <= radius; x++)
                    {
                        var offset = new Vector3Int(x, y, z);
                        if (offset.sqrMagnitude > radiusSquared)
                            continue;
                        Vector3Int coordinate = centre + offset;
                        if (map.GetCell(coordinate) == VoxelIntegrityCell.Solid)
                            removed.Add(coordinate);
                    }
            DestroyVoxels(removed);
        }

        private void DestroyVoxels(IReadOnlyCollection<Vector3Int> removed)
        {
            var actuallyRemoved = new List<Vector3Int>();
            foreach (Vector3Int coordinate in removed)
            {
                if (map.GetCell(coordinate) != VoxelIntegrityCell.Solid)
                    continue;

                world.SetVoxel(
                    coordinate.x,
                    coordinate.y,
                    coordinate.z,
                    -1f,
                    VoxelTypeId.Air);
                DestroyVisual(coordinate);
                actuallyRemoved.Add(coordinate);
            }
            if (actuallyRemoved.Count == 0)
                return;

            VoxelIntegrityResult result = search.Analyze(actuallyRemoved, map);
            for (int i = 0; i < result.Components.Count; i++)
            {
                VoxelIntegrityComponent component = result.Components[i];
                if (component.IsSupported || component.Voxels.Count == 0)
                    continue;

                GameObject body = VoxelIntegrityRigidbodyFactory.Create(
                    component.Voxels,
                    VoxelSize,
                    transform,
                    ResolveRigidbodyMaterial());
                rigidbodies.Add(body);

                for (int voxelIndex = 0;
                    voxelIndex < component.Voxels.Count;
                    voxelIndex++)
                {
                    Vector3Int coordinate = component.Voxels[voxelIndex];
                    world.SetVoxel(
                        coordinate.x,
                        coordinate.y,
                        coordinate.z,
                        -1f,
                        VoxelTypeId.Air);
                    DestroyVisual(coordinate);
                }
            }
        }

        private void DestroyVisual(Vector3Int coordinate)
        {
            if (!visuals.TryGetValue(coordinate, out GameObject visual))
                return;
            visuals.Remove(coordinate);
            if (visual != null)
                Destroy(visual);
        }

        private Material ResolveStoneMaterial()
        {
            if (stoneMaterial != null)
                return stoneMaterial;
            if (ownedStoneMaterial == null)
                ownedStoneMaterial = CreateMaterial(new Color(0.45f, 0.5f, 0.57f));
            return ownedStoneMaterial;
        }

        private Material ResolveBedrockMaterial()
        {
            if (bedrockMaterial != null)
                return bedrockMaterial;
            if (ownedBedrockMaterial == null)
                ownedBedrockMaterial = CreateMaterial(new Color(0.12f, 0.14f, 0.17f));
            return ownedBedrockMaterial;
        }

        private Material ResolveRigidbodyMaterial()
        {
            if (rigidbodyMaterial != null)
                return rigidbodyMaterial;
            if (ownedRigidbodyMaterial == null)
                ownedRigidbodyMaterial = CreateMaterial(new Color(0.88f, 0.43f, 0.12f));
            return ownedRigidbodyMaterial;
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            return new Material(shader) { color = color };
        }

        private static void DestroyOwnedMaterial(Material material)
        {
            if (material != null)
                Destroy(material);
        }

        private void OnGUI()
        {
            const string instructions =
                "Voxel Integrity Experiment\n"
                + "Left click: pickaxe (single voxel)\n"
                + "Right click: bomb radius\n"
                + "R: reset fixture\n"
                + "Orange shapes are exact compound rigid bodies";
            GUI.Box(new Rect(16f, 16f, 330f, 116f), instructions);
        }
    }

    public sealed class VoxelIntegrityExperimentVoxel : MonoBehaviour
    {
        public Vector3Int Coordinate { get; private set; }

        public void Configure(Vector3Int coordinate)
        {
            Coordinate = coordinate;
        }
    }
}
