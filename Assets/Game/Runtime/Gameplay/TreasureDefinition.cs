using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Gameplay
{
    [CreateAssetMenu(fileName = "Treasure", menuName = "Supernova/World/Treasure")]
    public sealed class TreasureDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private GameObject prefab;
        [SerializeField, Min(0)] private int value = 50;
        [SerializeField, Min(0.01f)] private float weight = 2f;
        [SerializeField, Range(0f, 1f)] private float fragility = 0.5f;
        [SerializeField, Range(0f, 1f)] private float spawnChance = 0.08f;
        [SerializeField, Min(1)] private int attemptsPerChunk = 2;
        [SerializeField, Range(0f, 45f)] private float maximumSurfaceSlope = 12f;
        [SerializeField, Min(0.1f)] private float requiredHeadroom = 1.5f;
        [Tooltip(
            "Optional bomb tool whose explosion is emitted when this treasure "
            + "is destroyed.")]
        [SerializeField] private PlayerToolDefinition destructionExplosionTool;
        [Tooltip(
            "Each prefab is one complete pre-cut fragment arrangement. "
            + "Exactly one variant is selected when this treasure breaks.")]
        [SerializeField] private List<GameObject> fractureVariants =
            new List<GameObject>();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName.Trim();
        public GameObject Prefab => prefab;
        public int Value => Mathf.Max(0, value);
        public float Weight => Mathf.Max(0.01f, weight);
        public float Fragility => Mathf.Clamp01(fragility);
        public float SpawnChance => Mathf.Clamp01(spawnChance);
        public int AttemptsPerChunk => Mathf.Max(1, attemptsPerChunk);
        public float MaximumSurfaceSlope => Mathf.Clamp(maximumSurfaceSlope, 0f, 45f);
        public float RequiredHeadroom => Mathf.Max(0.1f, requiredHeadroom);
        public PlayerToolDefinition DestructionExplosionTool =>
            destructionExplosionTool;
        public IReadOnlyList<GameObject> FractureVariants =>
            fractureVariants;

        public void Configure(GameObject model, int treasureValue, float mass,
            float chance, int attempts, float maximumSlope = 12f,
            float objectFragility = 0.5f,
            IEnumerable<GameObject> breakVariants = null)
        {
            prefab = model;
            value = Mathf.Max(0, treasureValue);
            weight = Mathf.Max(0.01f, mass);
            fragility = Mathf.Clamp01(objectFragility);
            spawnChance = Mathf.Clamp01(chance);
            attemptsPerChunk = Mathf.Max(1, attempts);
            maximumSurfaceSlope = Mathf.Clamp(maximumSlope, 0f, 45f);
            if (breakVariants != null)
            {
                ConfigureFractureVariants(breakVariants);
            }
        }

        public void ConfigureFractureVariants(
            IEnumerable<GameObject> variants)
        {
            fractureVariants = variants != null
                ? new List<GameObject>(variants)
                : new List<GameObject>();
        }

        public void ConfigureDisplayName(string value)
        {
            displayName = value ?? string.Empty;
        }

        public void ConfigureDestructionExplosion(
            PlayerToolDefinition explosionTool)
        {
            destructionExplosionTool = explosionTool;
        }

        public GameObject GetFractureVariant(int selectionSeed)
        {
            int validCount = 0;
            for (int i = 0; i < fractureVariants.Count; i++)
            {
                if (fractureVariants[i] != null)
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return null;
            }

            int selected = selectionSeed % validCount;
            if (selected < 0)
            {
                selected += validCount;
            }

            for (int i = 0; i < fractureVariants.Count; i++)
            {
                if (fractureVariants[i] == null)
                {
                    continue;
                }

                if (selected == 0)
                {
                    return fractureVariants[i];
                }
                selected--;
            }

            return null;
        }
    }
}
