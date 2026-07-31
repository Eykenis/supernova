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
        [Tooltip("Maximum queued monster instances created during one frame.")]
        [SerializeField, Min(1)] private int maximumMonsterSpawnsPerFrame = 1;
        [Tooltip("Minimum unscaled time between starting queued monster groups.")]
        [SerializeField, Min(0f)] private float secondsBetweenMonsterGroups = 0.75f;

        public IReadOnlyList<MonsterSpawnDefinition> Monsters => monsters;
        public int MaximumActiveMonsters => Mathf.Max(0, maximumActiveMonsters);
        public int MaximumMonsterSpawnsPerFrame =>
            Mathf.Max(1, maximumMonsterSpawnsPerFrame);
        public float SecondsBetweenMonsterGroups =>
            Mathf.Max(0f, secondsBetweenMonsterGroups);

        public void Configure(
            IEnumerable<MonsterSpawnDefinition> values,
            int maximumActive,
            int spawnsPerFrame = 1,
            float groupInterval = 0.75f)
        {
            monsters = values != null
                ? new List<MonsterSpawnDefinition>(values)
                : new List<MonsterSpawnDefinition>();
            maximumActiveMonsters = Mathf.Max(0, maximumActive);
            maximumMonsterSpawnsPerFrame = Mathf.Max(1, spawnsPerFrame);
            secondsBetweenMonsterGroups = Mathf.Max(0f, groupInterval);
        }
    }
}
