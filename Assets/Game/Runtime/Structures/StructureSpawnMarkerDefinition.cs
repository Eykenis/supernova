using System;
using Supernova.Gameplay;
using Supernova.MinecraftCaves.Creatures;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// An authored spawn point inside a structure. Markers let a designer place a
    /// specific gameplay object or player spawn at a specific spot instead of
    /// relying on world-level scatter that knows nothing about structures.
    /// </summary>
    [Serializable]
    public sealed class StructureSpawnMarkerDefinition
    {
        public enum Kind
        {
            Treasure,
            Monster,
            Checkpoint,
            PlayerSpawn,
        }

        [SerializeField] private string stableId = "marker";
        [SerializeField] private Kind kind = Kind.Treasure;
        [SerializeField] private TreasureDefinition treasure;
        [SerializeField] private MonsterSpawnDefinition monster;
        [SerializeField] private GameObject checkpointPrefab;

        [Header("Placement")]
        [Tooltip("Offset from the piece origin, in the piece's own axes.")]
        [SerializeField] private Vector3Int localOffset = new Vector3Int(0, 1, 0);
        [Tooltip("Yaw applied on top of the piece rotation, in degrees.")]
        [SerializeField, Range(0f, 360f)] private float yaw;
        [Tooltip("Chance this marker produces anything at all.")]
        [SerializeField, Range(0f, 1f)] private float spawnChance = 1f;
        [Tooltip("How many instances to place. Monsters spread around the point.")]
        [SerializeField, Min(1)] private int count = 1;
        [Tooltip("Horizontal scatter radius in voxels when Count is above one.")]
        [SerializeField, Min(0f)] private float scatterRadiusInVoxels = 1.5f;
        [Tooltip("Drop the spawn onto the first solid surface below the marker.")]
        [SerializeField] private bool snapToFloor = true;
        [Tooltip("How far down to look for that surface.")]
        [SerializeField, Min(0)] private int floorSearchDistance = 6;

        public string StableId => string.IsNullOrWhiteSpace(stableId)
            ? "marker"
            : stableId.Trim();
        public Kind MarkerKind => kind;
        public TreasureDefinition Treasure => treasure;
        public MonsterSpawnDefinition Monster => monster;
        public GameObject CheckpointPrefab => checkpointPrefab;

        /// <summary>
        /// True when this marker can actually produce something. An unconfigured
        /// marker is skipped rather than logging every time a column streams in.
        /// </summary>
        public bool IsConfigured
        {
            get
            {
                if (kind == Kind.Checkpoint)
                {
                    return checkpointPrefab != null;
                }
                if (kind == Kind.PlayerSpawn)
                {
                    return true;
                }
                return kind == Kind.Treasure
                    ? treasure != null && treasure.Prefab != null
                    : monster != null && monster.Prefab != null;
            }
        }

        public void Configure(
            string markerId,
            Kind markerKind,
            Vector3Int offset,
            float rotation = 0f,
            float chance = 1f,
            int instanceCount = 1,
            float scatter = 1.5f,
            bool snap = true,
            int searchDistance = 6)
        {
            stableId = markerId;
            kind = markerKind;
            localOffset = offset;
            yaw = rotation;
            spawnChance = chance;
            count = instanceCount;
            scatterRadiusInVoxels = scatter;
            snapToFloor = snap;
            floorSearchDistance = searchDistance;
            ClampConfiguration();
        }

        public void ConfigureTreasure(TreasureDefinition value)
        {
            treasure = value;
            kind = Kind.Treasure;
        }

        public void ConfigureMonster(MonsterSpawnDefinition value)
        {
            monster = value;
            kind = Kind.Monster;
        }

        internal StructureSpawnMarkerSettings CreateSettings()
        {
            ClampConfiguration();
            return new StructureSpawnMarkerSettings(
                StableId,
                kind,
                treasure,
                monster,
                checkpointPrefab,
                localOffset,
                yaw,
                spawnChance,
                count,
                scatterRadiusInVoxels,
                snapToFloor,
                floorSearchDistance);
        }

        internal void ClampConfiguration()
        {
            stableId = string.IsNullOrWhiteSpace(stableId)
                ? "marker"
                : stableId.Trim();
            yaw = Mathf.Repeat(yaw, 360f);
            spawnChance = Mathf.Clamp01(spawnChance);
            count = Mathf.Max(1, count);
            scatterRadiusInVoxels = Mathf.Max(0f, scatterRadiusInVoxels);
            floorSearchDistance = Mathf.Max(0, floorSearchDistance);
        }
    }

    /// <summary>
    /// Worker-thread-safe snapshot of one authored marker. Prefab references are
    /// carried through untouched; only the main thread ever instantiates them.
    /// </summary>
    public readonly struct StructureSpawnMarkerSettings
    {
        public StructureSpawnMarkerSettings(
            string stableId,
            StructureSpawnMarkerDefinition.Kind kind,
            TreasureDefinition treasure,
            MonsterSpawnDefinition monster,
            GameObject checkpointPrefab,
            Vector3Int localOffset,
            float yaw,
            float spawnChance,
            int count,
            float scatterRadiusInVoxels,
            bool snapToFloor,
            int floorSearchDistance)
        {
            StableId = string.IsNullOrWhiteSpace(stableId)
                ? "marker"
                : stableId.Trim();
            Kind = kind;
            Treasure = treasure;
            Monster = monster;
            CheckpointPrefab = checkpointPrefab;
            LocalOffset = localOffset;
            Yaw = yaw;
            SpawnChance = spawnChance < 0f
                ? 0f
                : spawnChance > 1f ? 1f : spawnChance;
            Count = Math.Max(1, count);
            ScatterRadiusInVoxels = Math.Max(0f, scatterRadiusInVoxels);
            SnapToFloor = snapToFloor;
            FloorSearchDistance = Math.Max(0, floorSearchDistance);
            Salt = ComputeStableSalt(StableId, kind);
        }

        public string StableId { get; }
        public StructureSpawnMarkerDefinition.Kind Kind { get; }
        public TreasureDefinition Treasure { get; }
        public MonsterSpawnDefinition Monster { get; }
        public GameObject CheckpointPrefab { get; }
        public Vector3Int LocalOffset { get; }
        public float Yaw { get; }
        public float SpawnChance { get; }
        public int Count { get; }
        public float ScatterRadiusInVoxels { get; }
        public bool SnapToFloor { get; }
        public int FloorSearchDistance { get; }

        /// <summary>
        /// Deterministic per-marker salt derived from the authored ID rather than
        /// <see cref="string.GetHashCode"/>, which is not stable across runs.
        /// </summary>
        public int Salt { get; }

        public bool IsConfigured
        {
            get
            {
                if (Kind == StructureSpawnMarkerDefinition.Kind.Checkpoint)
                {
                    return CheckpointPrefab != null;
                }
                if (Kind == StructureSpawnMarkerDefinition.Kind.PlayerSpawn)
                {
                    return true;
                }
                return Kind == StructureSpawnMarkerDefinition.Kind.Treasure
                    ? Treasure != null && Treasure.Prefab != null
                    : Monster != null && Monster.Prefab != null;
            }
        }

        private static int ComputeStableSalt(
            string stableId,
            StructureSpawnMarkerDefinition.Kind kind)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < stableId.Length; i++)
                {
                    hash ^= stableId[i];
                    hash *= 16777619u;
                }
                hash ^= (uint)kind;
                hash *= 16777619u;
                return (int)hash;
            }
        }
    }

    /// <summary>
    /// One resolved spawn produced by a marker, in terrain-local voxel space.
    /// Resolution happens during layout so it stays deterministic; the world
    /// instantiates these on the main thread once the owning column is meshed.
    /// </summary>
    public readonly struct StructureSpawnRequest
    {
        public StructureSpawnRequest(
            StructureSpawnMarkerDefinition.Kind kind,
            TreasureDefinition treasure,
            MonsterSpawnDefinition monster,
            Vector3Int voxelPosition,
            float yaw,
            bool snapToFloor,
            int floorSearchDistance)
        {
            Kind = kind;
            Treasure = treasure;
            Monster = monster;
            VoxelPosition = voxelPosition;
            Yaw = yaw;
            SnapToFloor = snapToFloor;
            FloorSearchDistance = floorSearchDistance;
        }

        public StructureSpawnMarkerDefinition.Kind Kind { get; }
        public TreasureDefinition Treasure { get; }
        public MonsterSpawnDefinition Monster { get; }
        public Vector3Int VoxelPosition { get; }
        public float Yaw { get; }
        public bool SnapToFloor { get; }
        public int FloorSearchDistance { get; }
    }

    /// <summary>
    /// One resolved checkpoint placement produced by the fixed spawn checkpoint
    /// hall, in terrain-local voxel space. Mirrors
    /// <see cref="StructureSpawnRequest"/> but carries the configured model,
    /// its anchor voxel, and the piece yaw.
    /// </summary>
    public readonly struct CheckpointSpawnRequest
    {
        public CheckpointSpawnRequest(
            GameObject prefab,
            Vector3Int voxelPosition,
            int floorY,
            float yaw,
            bool isSpawnCheckpoint = false)
        {
            Prefab = prefab;
            VoxelPosition = voxelPosition;
            FloorY = floorY;
            Yaw = yaw;
            IsSpawnCheckpoint = isSpawnCheckpoint;
        }

        public GameObject Prefab { get; }
        public Vector3Int VoxelPosition { get; }
        public int FloorY { get; }
        public float Yaw { get; }
        public bool IsSpawnCheckpoint { get; }
    }

    /// <summary>
    /// One authored player spawn inside a fixed-origin jigsaw piece.
    /// </summary>
    public readonly struct PlayerSpawnRequest
    {
        public PlayerSpawnRequest(Vector3Int voxelPosition, float yaw)
        {
            VoxelPosition = voxelPosition;
            Yaw = yaw;
        }

        public Vector3Int VoxelPosition { get; }
        public float Yaw { get; }
    }
}
