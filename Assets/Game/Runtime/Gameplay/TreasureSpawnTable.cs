using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Gameplay
{
    [CreateAssetMenu(fileName = "TreasureSpawnTable", menuName = "Supernova/World/Treasure Spawn Table")]
    public sealed class TreasureSpawnTable : ScriptableObject
    {
        [SerializeField] private List<TreasureDefinition> treasures =
            new List<TreasureDefinition>();

        public IReadOnlyList<TreasureDefinition> Treasures => treasures;
        public void Configure(IEnumerable<TreasureDefinition> values)
        {
            treasures = values != null
                ? new List<TreasureDefinition>(values)
                : new List<TreasureDefinition>();
        }
    }
}
