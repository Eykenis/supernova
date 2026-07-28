using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>Marks the only part of a cart that can begin player towing.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class CartHandle : MonoBehaviour
    {
        [SerializeField] private Rigidbody cartBody;
        [Tooltip("Point pulled toward the player's towing socket. Defaults to this transform.")]
        [SerializeField] private Transform attachmentPoint;

        public Rigidbody CartBody
        {
            get
            {
                if (cartBody == null) cartBody = GetComponentInParent<Rigidbody>();
                return cartBody;
            }
        }

        public Transform AttachmentPoint => attachmentPoint != null
            ? attachmentPoint
            : transform;

        public void Configure(Rigidbody body, Transform point = null)
        {
            cartBody = body;
            attachmentPoint = point != null ? point : transform;
        }
    }
}
