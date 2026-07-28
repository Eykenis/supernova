using UnityEngine;

namespace Supernova.Missions
{
    [CreateAssetMenu(fileName = "MissionDefinition", menuName = "Supernova/Missions/Mission Definition")]
    public sealed class MissionDefinition : ScriptableObject
    {
        [SerializeField] private int levelNumber = 1;
        [SerializeField] private string displayName = "FIRST DESCENT";
        [SerializeField, Min(10f)] private float timeLimitSeconds = 300f;
        [SerializeField, Min(1)] private int requiredValue = 100;
        [SerializeField, Min(1)] private int oreUnitValue = 10;
        [SerializeField] private string caveSceneName = "InfiniteCaves";
        [SerializeField] private string homeSceneName = "Home";

        public int LevelNumber => Mathf.Max(1, levelNumber);
        public string DisplayName => displayName;
        public float TimeLimitSeconds => Mathf.Max(10f, timeLimitSeconds);
        public int RequiredValue => Mathf.Max(1, requiredValue);
        public int OreUnitValue => Mathf.Max(1, oreUnitValue);
        public string CaveSceneName => caveSceneName;
        public string HomeSceneName => homeSceneName;
    }
}
