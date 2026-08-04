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
        [FormerlySerializedAs("timeLimitSeconds")]
        [SerializeField, Min(10f)]
        private float evacuationCountdownSeconds = 180f;
        [FormerlySerializedAs("requiredValue")]
        [SerializeField, Min(1)] private int requiredFunds = 100;

        [Header("Scenes")]
        [SerializeField] private string caveSceneName;
        [SerializeField] private string homeSceneName;

        public int LevelNumber => Mathf.Max(1, levelNumber);
        public string DisplayName => displayName;
        public MinecraftWorldGenerationConfiguration WorldGeneration =>
            worldGeneration;
        public MonsterSpawnTable MonsterGeneration => monsterGeneration;
        public TreasureSpawnTable TreasureGeneration => treasureGeneration;
        public float EvacuationCountdownSeconds =>
            Mathf.Max(10f, evacuationCountdownSeconds);
        public int RequiredFunds => Mathf.Max(1, requiredFunds);
        public int RequiredValue => RequiredFunds;
        public string CaveSceneName => caveSceneName;
        public string HomeSceneName => homeSceneName;

        public bool HasCompleteGenerationConfiguration =>
            worldGeneration != null
            && monsterGeneration != null
            && treasureGeneration != null;
    }
}
