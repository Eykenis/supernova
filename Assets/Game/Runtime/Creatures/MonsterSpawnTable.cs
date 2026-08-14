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
        [Tooltip("Natural monsters only spawn in chunks beyond this distance from "
            + "the player's current chunk.")]
        [SerializeField, Min(0)] private int playerExclusionRadiusInChunks = 1;
        [Tooltip("Maximum queued monster instances instantiated during one frame.")]
        [SerializeField, Min(1)] private int maximumMonsterSpawnsPerFrame = 1;
        [Tooltip("Unscaled seconds between natural monster spawn rolls.")]
        [SerializeField, Min(0.1f)] private float spawnAttemptIntervalSeconds = 5f;
        [Tooltip("Chance that one interval queues one random monster.")]
        [SerializeField, Range(0f, 1f)] private float spawnAttemptChance = 0.3f;
        [Tooltip(
            "Maximum number of randomly selected distant chunks searched for "
            + "a valid surface after a successful spawn roll.")]
        [SerializeField, Min(1)] private int candidateChunksPerSpawnAttempt = 4;

        public IReadOnlyList<MonsterSpawnDefinition> Monsters => monsters;
        public int MaximumActiveMonsters => Mathf.Max(0, maximumActiveMonsters);
        public int PlayerExclusionRadiusInChunks =>
            Mathf.Max(0, playerExclusionRadiusInChunks);
        public int MaximumMonsterSpawnsPerFrame =>
            Mathf.Max(1, maximumMonsterSpawnsPerFrame);
        public float SpawnAttemptIntervalSeconds =>
            Mathf.Max(0.1f, spawnAttemptIntervalSeconds);
        public float SpawnAttemptChance => Mathf.Clamp01(spawnAttemptChance);
        public int CandidateChunksPerSpawnAttempt =>
            Mathf.Max(1, candidateChunksPerSpawnAttempt);

        public void Configure(
            IEnumerable<MonsterSpawnDefinition> values,
            int maximumActive,
            int spawnsPerFrame = 1,
            int playerExclusionRadius = 1,
            float attemptIntervalSeconds = 5f,
            float attemptChance = 0.3f,
            int candidateChunksPerAttempt = 4)
        {
            monsters = values != null
                ? new List<MonsterSpawnDefinition>(values)
                : new List<MonsterSpawnDefinition>();
            maximumActiveMonsters = Mathf.Max(0, maximumActive);
            playerExclusionRadiusInChunks = Mathf.Max(
                0,
                playerExclusionRadius);
            maximumMonsterSpawnsPerFrame = Mathf.Max(1, spawnsPerFrame);
            spawnAttemptIntervalSeconds = Mathf.Max(
                0.1f,
                attemptIntervalSeconds);
            spawnAttemptChance = Mathf.Clamp01(attemptChance);
            candidateChunksPerSpawnAttempt = Mathf.Max(
                1,
                candidateChunksPerAttempt);
        }
    }
}
