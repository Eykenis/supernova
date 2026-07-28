using System.Collections.Generic;
using Supernova.Gameplay;
using UnityEngine;

namespace Supernova.Missions
{
    /// <summary>
    /// Marks valuables resting in the cart cargo bed as collision-safe without
    /// changing their rigidbody or collider behaviour.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class CartCargoValueZone : MonoBehaviour
    {
        private readonly Dictionary<int, ProtectedResource> resources =
            new Dictionary<int, ProtectedResource>();

        public int ProtectedResourceCount => resources.Count;

        private void OnTriggerEnter(Collider other)
        {
            ValuableObject valuable =
                other != null ? other.GetComponentInParent<ValuableObject>() : null;
            if (valuable == null)
            {
                return;
            }

            int id = valuable.GetInstanceID();
            if (!resources.TryGetValue(id, out ProtectedResource resource))
            {
                resource = new ProtectedResource(valuable);
                resources.Add(id, resource);
            }

            if (resource.Overlaps.Add(other))
            {
                valuable.SetCollisionValueLossProtected(this, true);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            ValuableObject valuable =
                other != null ? other.GetComponentInParent<ValuableObject>() : null;
            if (valuable == null
                || !resources.TryGetValue(
                    valuable.GetInstanceID(),
                    out ProtectedResource resource))
            {
                return;
            }

            resource.Overlaps.Remove(other);
            if (resource.Overlaps.Count > 0)
            {
                return;
            }

            valuable.SetCollisionValueLossProtected(this, false);
            resources.Remove(valuable.GetInstanceID());
        }

        private void OnDisable()
        {
            ReleaseAll();
        }

        private void OnDestroy()
        {
            ReleaseAll();
        }

        private void ReleaseAll()
        {
            foreach (ProtectedResource resource in resources.Values)
            {
                if (resource.Valuable != null)
                {
                    resource.Valuable.SetCollisionValueLossProtected(this, false);
                }
            }
            resources.Clear();
        }

        private sealed class ProtectedResource
        {
            public ProtectedResource(ValuableObject valuable)
            {
                Valuable = valuable;
            }

            public ValuableObject Valuable { get; }
            public HashSet<Collider> Overlaps { get; } =
                new HashSet<Collider>();
        }
    }
}
