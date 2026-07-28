using System.Collections.Generic;
using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures
{
    [CreateAssetMenu(
        fileName = "MonsterSpawnTable",
        menuName = "Supernova/World/Monster Spawn Table")]
    public sealed class MonsterSpawnTable : ScriptableObject
    {
        [SerializeField] private List<MonsterSpawnDefinition> monsters =
            new List<MonsterSpawnDefinition>();
        [SerializeField, Min(0)] private int maximumActiveMonsters = 32;
        [SerializeField, Min(0f)] private float spawnExclusionRadius = 12f;
        [Tooltip("Width of one large-scale monster spawn cell, measured in chunks.")]
        [SerializeField, Min(3)] private int spawnCellSizeInChunks = 6;

        public IReadOnlyList<MonsterSpawnDefinition> Monsters => monsters;
        public int MaximumActiveMonsters => Mathf.Max(0, maximumActiveMonsters);
        public float SpawnExclusionRadius => Mathf.Max(0f, spawnExclusionRadius);
        public int SpawnCellSizeInChunks => Mathf.Max(3, spawnCellSizeInChunks);

        public void Configure(
            IEnumerable<MonsterSpawnDefinition> values,
            int maximumActive,
            float exclusionRadius,
            int cellSizeInChunks = 6)
        {
            monsters = values != null
                ? new List<MonsterSpawnDefinition>(values)
                : new List<MonsterSpawnDefinition>();
            maximumActiveMonsters = Mathf.Max(0, maximumActive);
            spawnExclusionRadius = Mathf.Max(0f, exclusionRadius);
            spawnCellSizeInChunks = Mathf.Max(3, cellSizeInChunks);
        }
    }
}
