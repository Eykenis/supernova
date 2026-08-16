using Supernova.Effects;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Marks a dynamic Rigidbody as a valid target for a player physics-attractor.
    /// The marker does not move the body by itself.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(RigidbodyImpactFeedback))]
    public sealed class PhysicsAttractable : MonoBehaviour
    {
        [SerializeField] private bool canBeAttracted = true;

        private Rigidbody cachedBody;

        public bool CanBeAttracted => canBeAttracted
            && Body != null
            && !Body.isKinematic;

        public Rigidbody Body
        {
            get
            {
                if (cachedBody == null) cachedBody = GetComponent<Rigidbody>();
                return cachedBody;
            }
        }

        private void Awake()
        {
            RigidbodyImpactFeedback.Ensure(Body);
        }

        public void SetCanBeAttracted(bool value)
        {
            canBeAttracted = value;
        }
    }
}
