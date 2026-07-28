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
    }
}
