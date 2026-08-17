using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Gameplay
{
    [CreateAssetMenu(fileName = "TreasureSpawnTable", menuName = "Supernova/World/Treasure Spawn Table")]
    public sealed class TreasureSpawnTable : ScriptableObject
    {
        [SerializeField] private List<TreasureDefinition> treasures =
            new List<TreasureDefinition>();
        [SerializeField, Min(0f)] private float spawnExclusionRadius = 12f;

        public IReadOnlyList<TreasureDefinition> Treasures => treasures;
        public float SpawnExclusionRadius => Mathf.Max(0f, spawnExclusionRadius);

        public void Configure(
            IEnumerable<TreasureDefinition> values,
            float exclusionRadius = 12f)
        {
            treasures = values != null
                ? new List<TreasureDefinition>(values)
                : new List<TreasureDefinition>();
            spawnExclusionRadius = Mathf.Max(0f, exclusionRadius);
        }

        /// <summary>
        /// Selects one configured treasure using its natural-world spawn chance
        /// as the relative weight. The caller supplies a deterministic [0, 1]
        /// roll so this method is safe to use for streamed structure markers.
        /// </summary>
        public TreasureDefinition SelectWeighted(float normalizedRoll)
        {
            float totalWeight = 0f;
            for (int i = 0; i < treasures.Count; i++)
            {
                TreasureDefinition treasure = treasures[i];
                if (treasure != null && treasure.Prefab != null)
                {
                    totalWeight += treasure.SpawnChance;
                }
            }
            if (totalWeight <= 0f)
            {
                return null;
            }

            float target = Mathf.Clamp01(normalizedRoll) * totalWeight;
            TreasureDefinition fallback = null;
            for (int i = 0; i < treasures.Count; i++)
            {
                TreasureDefinition treasure = treasures[i];
                if (treasure == null
                    || treasure.Prefab == null
                    || treasure.SpawnChance <= 0f)
                {
                    continue;
                }

                fallback = treasure;
                target -= treasure.SpawnChance;
                if (target <= 0f)
                {
                    return treasure;
                }
            }
            return fallback;
        }
    }
}
