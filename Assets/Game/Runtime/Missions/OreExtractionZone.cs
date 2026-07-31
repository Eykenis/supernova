using System.Collections.Generic;
using Supernova.Voxels;
using Supernova.Gameplay;
using UnityEngine;

namespace Supernova.Missions
{
    [DisallowMultipleComponent]
    public sealed class OreExtractionZone : MonoBehaviour
    {
        private readonly Dictionary<int, StoredResource> storedResources =
            new Dictionary<int, StoredResource>();
        private MissionGameLoop owner;
        public int CurrentStoredValue
        {
            get
            {
                RemoveDestroyedResources();
                int total = 0;
                foreach (StoredResource resource in storedResources.Values)
                {
                    total += resource.Value;
                }
                return total;
            }
        }

        public void Configure(MissionGameLoop missionOwner)
        {
            owner = missionOwner;
        }

        private void OnTriggerEnter(Collider other)
        {
            ValuableObject valuable =
                other.GetComponentInParent<ValuableObject>();
            if (valuable != null)
            {
                StoreOverlap(
                    valuable.GetInstanceID(),
                    valuable.gameObject,
                    valuable,
                    valuable.CurrentValue,
                    other);
                return;
            }

            TreasurePickup treasure = other.GetComponentInParent<TreasurePickup>();
            if (treasure != null)
            {
                StoreOverlap(
                    treasure.GetInstanceID(),
                    treasure.gameObject,
                    null,
                    treasure.Value,
                    other);
                return;
            }

            MinedOreDrop drop = other.GetComponentInParent<MinedOreDrop>();
            if (drop == null) return;
            StoreOverlap(
                drop.GetInstanceID(),
                drop.gameObject,
                null,
                drop.Value,
                other);
        }

        private void OnTriggerExit(Collider other)
        {
            ValuableObject valuable =
                other.GetComponentInParent<ValuableObject>();
            if (valuable != null)
            {
                RemoveOverlap(valuable.GetInstanceID(), other);
                return;
            }

            TreasurePickup treasure = other.GetComponentInParent<TreasurePickup>();
            if (treasure != null)
            {
                RemoveOverlap(treasure.GetInstanceID(), other);
                return;
            }

            MinedOreDrop drop = other.GetComponentInParent<MinedOreDrop>();
            if (drop != null) RemoveOverlap(drop.GetInstanceID(), other);
        }

        private void StoreOverlap(
            int id,
            GameObject resourceObject,
            ValuableObject valuable,
            int value,
            Collider overlap)
        {
            if (!storedResources.TryGetValue(id, out StoredResource resource))
            {
                resource = new StoredResource(
                    resourceObject,
                    valuable,
                    Mathf.Max(0, value));
                storedResources.Add(id, resource);
            }
            if (resource.Overlaps.Add(overlap))
            {
                owner?.NotifyStoredValueChanged(CurrentStoredValue);
            }
        }

        private void RemoveOverlap(int id, Collider overlap)
        {
            if (!storedResources.TryGetValue(id, out StoredResource resource)) return;
            resource.Overlaps.Remove(overlap);
            if (resource.Overlaps.Count == 0) storedResources.Remove(id);
            owner?.NotifyStoredValueChanged(CurrentStoredValue);
        }

        private void RemoveDestroyedResources()
        {
            var removed = new List<int>();
            foreach (KeyValuePair<int, StoredResource> pair in storedResources)
            {
                if (pair.Value.ResourceObject == null) removed.Add(pair.Key);
            }
            for (int i = 0; i < removed.Count; i++)
            {
                storedResources.Remove(removed[i]);
            }
        }

        private sealed class StoredResource
        {
            public StoredResource(
                GameObject resourceObject,
                ValuableObject valuable,
                int value)
            {
                ResourceObject = resourceObject;
                Valuable = valuable;
                fallbackValue = value;
            }

            public GameObject ResourceObject { get; }
            public ValuableObject Valuable { get; }
            public int Value => Valuable != null
                ? Valuable.CurrentValue
                : fallbackValue;
            public HashSet<Collider> Overlaps { get; } = new HashSet<Collider>();

            private readonly int fallbackValue;
        }
    }
}
