using Supernova.Effects;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// A physics projectile whose light remains in the world after it lands.
    /// It intentionally has no lifetime or automatic destruction.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    [RequireComponent(typeof(RigidbodyImpactFeedback))]
    public sealed class PersistentLightProjectile : MonoBehaviour
    {
        [SerializeField] private Rigidbody body;
        [SerializeField] private Light lightSource;

        public Rigidbody Body => ResolveBody();
        public Light LightSource => ResolveLight();

        private void Awake()
        {
            RigidbodyImpactFeedback.Ensure(ResolveBody());
            ResolveLight();
        }

        public void Launch(Vector3 velocity, Vector3 angularVelocity)
        {
            Rigidbody resolvedBody = ResolveBody();
            resolvedBody.velocity = velocity;
            resolvedBody.angularVelocity = angularVelocity;
            resolvedBody.WakeUp();
        }

        private Rigidbody ResolveBody()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            return body;
        }

        private Light ResolveLight()
        {
            if (lightSource == null)
                lightSource = GetComponentInChildren<Light>(true);
            return lightSource;
        }
    }
}
