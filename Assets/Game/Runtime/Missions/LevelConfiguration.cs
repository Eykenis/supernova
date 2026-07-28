using Supernova.Gameplay;
using Supernova.MinecraftCaves;
using Supernova.MinecraftCaves.Creatures;
using UnityEngine;
using UnityEngine.Serialization;

namespace Supernova.Missions
{
    [CreateAssetMenu(
        fileName = "LevelConfiguration",
        menuName = "Supernova/Levels/Level Configuration")]
    public sealed class LevelConfiguration : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private int levelNumber = 1;
        [SerializeField] private string displayName = "FIRST DESCENT";

        [Header("Configuration Composition")]
        [SerializeField]
        private MinecraftWorldGenerationConfiguration worldGeneration;
        [SerializeField] private MonsterSpawnTable monsterGeneration;
        [SerializeField] private TreasureSpawnTable treasureGeneration;

        [Header("Evacuation")]
        [SerializeField, Min(10f)] private float timeLimitSeconds = 300f;
        [FormerlySerializedAs("requiredValue")]
        [SerializeField, Min(1)] private int requiredFunds = 100;
        [SerializeField, Min(1)] private int oreUnitValue = 10;

        [Header("Scenes")]
        [SerializeField] private string caveSceneName;
        [SerializeField] private string homeSceneName;

        public int LevelNumber => Mathf.Max(1, levelNumber);
        public string DisplayName => displayName;
        public MinecraftWorldGenerationConfiguration WorldGeneration =>
            worldGeneration;
        public MonsterSpawnTable MonsterGeneration => monsterGeneration;
        public TreasureSpawnTable TreasureGeneration => treasureGeneration;
        public float TimeLimitSeconds => Mathf.Max(10f, timeLimitSeconds);
        public int RequiredFunds => Mathf.Max(1, requiredFunds);
        public int RequiredValue => RequiredFunds;
        public int OreUnitValue => Mathf.Max(1, oreUnitValue);
        public string CaveSceneName => caveSceneName;
        public string HomeSceneName => homeSceneName;

        public bool HasCompleteGenerationConfiguration =>
            worldGeneration != null
            && monsterGeneration != null
            && treasureGeneration != null;
    }
}
