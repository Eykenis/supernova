using System.Collections.Generic;
using UnityEngine;

namespace Supernova.PortalExample
{
    [DisallowMultipleComponent]
    public sealed class PortalExampleFloorButton : MonoBehaviour
    {
        [SerializeField] private Transform buttonTop;
        [SerializeField] private PortalExampleDoor controlledDoor;
        [SerializeField, Min(0f)] private float pressDepth = 0.12f;
        [SerializeField, Min(0.1f)] private float animationSpeed = 1.5f;

        private readonly HashSet<PortalExamplePickup> occupants =
            new HashSet<PortalExamplePickup>();
        private Vector3 raisedLocalPosition;

        public bool IsPressed => occupants.Count > 0;

        private void Awake()
        {
            if (buttonTop != null)
            {
                raisedLocalPosition = buttonTop.localPosition;
            }
        }

        private void Update()
        {
            occupants.RemoveWhere(item => item == null);
            if (controlledDoor != null)
            {
                controlledDoor.SetOpen(IsPressed);
            }

            if (buttonTop != null)
            {
                Vector3 target = raisedLocalPosition
                    + Vector3.down * (IsPressed ? pressDepth : 0f);
                buttonTop.localPosition = Vector3.MoveTowards(
                    buttonTop.localPosition,
                    target,
                    animationSpeed * Time.deltaTime);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            PortalExamplePickup pickup =
                other.GetComponentInParent<PortalExamplePickup>();
            if (pickup != null)
            {
                occupants.Add(pickup);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            PortalExamplePickup pickup =
                other.GetComponentInParent<PortalExamplePickup>();
            if (pickup != null)
            {
                occupants.Remove(pickup);
            }
        }
    }
}
