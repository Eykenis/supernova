using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures
{
    [CreateAssetMenu(
        fileName = "MonsterSpawn",
        menuName = "Supernova/World/Monster Spawn")]
    public sealed class MonsterSpawnDefinition : ScriptableObject
    {
        [SerializeField] private GameObject prefab;
        [Tooltip("Chance for each large-scale spawn cell to contain this monster group.")]
        [SerializeField, Range(0f, 1f)] private float spawnChance = 0.65f;
        [Tooltip("Surface placement attempts for each member of the group.")]
        [SerializeField, Min(1)] private int attemptsPerChunk = 4;
        [SerializeField, Min(1)] private int minimumGroupSize = 3;
        [SerializeField, Min(1)] private int maximumGroupSize = 5;
        [SerializeField, Min(0f)] private float groupRadiusInVoxels = 8f;
        [SerializeField, Min(0.1f)] private float requiredHeadroom = 1.5f;
        [SerializeField] private float spawnHeightOffset;

        public GameObject Prefab => prefab;
        public float SpawnChance => Mathf.Clamp01(spawnChance);
        public int AttemptsPerChunk => Mathf.Max(1, attemptsPerChunk);
        public int MinimumGroupSize => Mathf.Max(1, minimumGroupSize);
        public int MaximumGroupSize => Mathf.Max(MinimumGroupSize, maximumGroupSize);
        public float GroupRadiusInVoxels => Mathf.Max(0f, groupRadiusInVoxels);
        public float RequiredHeadroom => Mathf.Max(0.1f, requiredHeadroom);
        public float SpawnHeightOffset => spawnHeightOffset;

        public void Configure(
            GameObject monsterPrefab,
            float chance,
            int attempts,
            float headroom = 1.5f,
            float heightOffset = 0f,
            int minimumGroup = 3,
            int maximumGroup = 5,
            float groupRadius = 8f)
        {
            prefab = monsterPrefab;
            spawnChance = Mathf.Clamp01(chance);
            attemptsPerChunk = Mathf.Max(1, attempts);
            minimumGroupSize = Mathf.Max(1, minimumGroup);
            maximumGroupSize = Mathf.Max(minimumGroupSize, maximumGroup);
            groupRadiusInVoxels = Mathf.Max(0f, groupRadius);
            requiredHeadroom = Mathf.Max(0.1f, headroom);
            spawnHeightOffset = heightOffset;
        }
    }
}
