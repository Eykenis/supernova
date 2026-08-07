using UnityEngine;

namespace Supernova.PortalExample
{
    [DisallowMultipleComponent]
    public sealed class PortalExampleDoor : MonoBehaviour
    {
        [SerializeField] private Transform movingPart;
        [SerializeField] private Vector3 openLocalOffset =
            new Vector3(0f, 4.2f, 0f);
        [SerializeField, Min(0.1f)] private float speed = 3f;

        private Vector3 closedLocalPosition;
        private bool isOpen;

        public bool IsOpen => isOpen;

        private void Awake()
        {
            if (movingPart == null)
            {
                movingPart = transform;
            }

            closedLocalPosition = movingPart.localPosition;
        }

        private void Update()
        {
            Vector3 target = closedLocalPosition
                + (isOpen ? openLocalOffset : Vector3.zero);
            movingPart.localPosition = Vector3.MoveTowards(
                movingPart.localPosition,
                target,
                speed * Time.deltaTime);
        }

        public void SetOpen(bool open)
        {
            isOpen = open;
        }
    }
}
