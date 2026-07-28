using UnityEngine;

namespace Supernova.Gameplay
{
    [CreateAssetMenu(fileName = "Treasure", menuName = "Supernova/World/Treasure")]
    public sealed class TreasureDefinition : ScriptableObject
    {
        [SerializeField] private GameObject prefab;
        [SerializeField, Min(0)] private int value = 50;
        [SerializeField, Min(0.01f)] private float weight = 2f;
        [SerializeField, Range(0f, 1f)] private float spawnChance = 0.08f;
        [SerializeField, Min(1)] private int attemptsPerChunk = 2;
        [SerializeField, Range(0f, 45f)] private float maximumSurfaceSlope = 12f;
        [SerializeField, Min(0.1f)] private float requiredHeadroom = 1.5f;

        public GameObject Prefab => prefab;
        public int Value => Mathf.Max(0, value);
        public float Weight => Mathf.Max(0.01f, weight);
        public float SpawnChance => Mathf.Clamp01(spawnChance);
        public int AttemptsPerChunk => Mathf.Max(1, attemptsPerChunk);
        public float MaximumSurfaceSlope => Mathf.Clamp(maximumSurfaceSlope, 0f, 45f);
        public float RequiredHeadroom => Mathf.Max(0.1f, requiredHeadroom);

        public void Configure(GameObject model, int treasureValue, float mass,
            float chance, int attempts, float maximumSlope = 12f)
        {
            prefab = model;
            value = Mathf.Max(0, treasureValue);
            weight = Mathf.Max(0.01f, mass);
            spawnChance = Mathf.Clamp01(chance);
            attemptsPerChunk = Mathf.Max(1, attempts);
            maximumSurfaceSlope = Mathf.Clamp(maximumSlope, 0f, 45f);
        }
    }
}
