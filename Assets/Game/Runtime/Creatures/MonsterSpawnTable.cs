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
        [Tooltip("Cap for monsters placed by authored structure markers. Markers "
            + "get their own budget so a designed encounter is not skipped just "
            + "because the ambient monster cap happens to be full.")]
        [SerializeField, Min(0)] private int maximumMarkerMonsters = 24;
        [Tooltip("Maximum queued monster instances created during one frame.")]
        [SerializeField, Min(1)] private int maximumMonsterSpawnsPerFrame = 1;
        [Tooltip("Minimum unscaled time between starting queued monster groups.")]
        [SerializeField, Min(0f)] private float secondsBetweenMonsterGroups = 0.75f;

        public IReadOnlyList<MonsterSpawnDefinition> Monsters => monsters;
        public int MaximumActiveMonsters => Mathf.Max(0, maximumActiveMonsters);
        public int MaximumMarkerMonsters => Mathf.Max(0, maximumMarkerMonsters);
        public int MaximumMonsterSpawnsPerFrame =>
            Mathf.Max(1, maximumMonsterSpawnsPerFrame);
        public float SecondsBetweenMonsterGroups =>
            Mathf.Max(0f, secondsBetweenMonsterGroups);

        public void Configure(
            IEnumerable<MonsterSpawnDefinition> values,
            int maximumActive,
            int spawnsPerFrame = 1,
            float groupInterval = 0.75f,
            int maximumMarkerSpawns = 24)
        {
            monsters = values != null
                ? new List<MonsterSpawnDefinition>(values)
                : new List<MonsterSpawnDefinition>();
            maximumActiveMonsters = Mathf.Max(0, maximumActive);
            maximumMarkerMonsters = Mathf.Max(0, maximumMarkerSpawns);
            maximumMonsterSpawnsPerFrame = Mathf.Max(1, spawnsPerFrame);
            secondsBetweenMonsterGroups = Mathf.Max(0f, groupInterval);
        }
    }
}
