using UnityEngine;

namespace Supernova.PortalExample
{
    /// <summary>
    /// Forwards callbacks from a portal's child trigger to the gate component
    /// on its root. Unity does not bubble collider messages up the hierarchy.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class PortalExampleTriggerRelay : MonoBehaviour
    {
        [SerializeField] private PortalExampleGate gate;

        public PortalExampleGate Gate => gate;

        public void Configure(PortalExampleGate configuredGate)
        {
            gate = configuredGate;
        }

        private void Awake()
        {
            if (gate == null)
            {
                gate = GetComponentInParent<PortalExampleGate>();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (gate != null)
            {
                gate.HandleTriggerEnter(other);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (gate != null)
            {
                gate.HandleTriggerStay(other);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (gate != null)
            {
                gate.HandleTriggerExit(other);
            }
        }
    }
}
