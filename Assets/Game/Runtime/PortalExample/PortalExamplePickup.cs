using Supernova.Effects;
using UnityEngine;

namespace Supernova.PortalExample
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(RigidbodyImpactFeedback))]
    public sealed class PortalExamplePickup : MonoBehaviour
    {
        private void Awake()
        {
            RigidbodyImpactFeedback.Ensure(GetComponent<Rigidbody>());
        }
    }
}
