using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// A fast physical projectile that is destroyed on first contact. It transfers
    /// momentum to treasures and deals configured damage only to monsters.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public class BallisticProjectile : MonoBehaviour
    {
        [SerializeField] private Rigidbody body;
        [SerializeField, Min(0f)] private float damage = 20f;
        [SerializeField, Min(0f)] private float treasureImpulseMultiplier = 2f;
        [SerializeField, Min(0.1f)] private float maximumLifetime = 5f;

        private GameObject owner;
        private bool hasImpacted;

        public Rigidbody Body => ResolveBody();
        public GameObject Owner => owner;
        public bool HasImpacted => hasImpacted;
        public float Damage => Mathf.Max(0f, damage);
        public float TreasureImpulseMultiplier =>
            Mathf.Max(0f, treasureImpulseMultiplier);

        private void Awake()
        {
            ConfigureBody();
        }

        private void OnEnable()
        {
            hasImpacted = false;
            if (Application.isPlaying)
                Destroy(gameObject, Mathf.Max(0.1f, maximumLifetime));
        }

        public void Launch(Vector3 velocity, GameObject projectileOwner)
        {
            owner = projectileOwner;
            Rigidbody resolvedBody = ConfigureBody();
            resolvedBody.velocity = velocity;
            resolvedBody.angularVelocity = Vector3.zero;
            resolvedBody.WakeUp();
        }

        private void OnCollisionEnter(Collision collision)
        {
            Collider hit = collision.collider;
            ContactPoint contact = collision.contactCount > 0
                ? collision.GetContact(0)
                : default;
            Vector3 point = collision.contactCount > 0
                ? contact.point
                : transform.position;
            Vector3 normal = collision.contactCount > 0
                ? contact.normal
                : ResolveFallbackImpactNormal();
            Impact(hit, point, normal);
        }

        private void OnTriggerEnter(Collider other)
        {
            Impact(other, transform.position, ResolveFallbackImpactNormal());
        }

        private void Impact(Collider hit, Vector3 point, Vector3 normal)
        {
            if (hasImpacted || IsOwnerCollider(hit)) return;
            hasImpacted = true;

            Vector3 impactVelocity = Body.velocity;
            Vector3 direction = impactVelocity.sqrMagnitude > 0.0001f
                ? impactVelocity.normalized
                : transform.forward;

            ProcessImpact(hit, point, normal, impactVelocity, direction);
            DisableAndDestroy();
        }

        /// <summary>
        /// Applies the projectile-specific result of a valid first contact. Derived
        /// projectiles can replace the offensive behavior while retaining launch,
        /// owner filtering, continuous collision detection, and immediate cleanup.
        /// </summary>
        protected virtual void ProcessImpact(
            Collider hit,
            Vector3 point,
            Vector3 normal,
            Vector3 impactVelocity,
            Vector3 direction)
        {
            TreasurePickup treasure = hit != null
                ? hit.GetComponentInParent<TreasurePickup>()
                : null;
            if (treasure != null)
            {
                treasure.ApplyProjectileImpulse(
                    CalculateTreasureImpulse(impactVelocity),
                    point);
            }
            else if (MeleeCombat.TryFindDamageable(hit, out IDamageable damageable)
                && damageable is IMonsterDamageable
                && damageable.Owner != null
                && (owner == null
                    || damageable.Owner.transform.root != owner.transform.root))
            {
                damageable.ReceiveDamage(new DamageInfo(
                    Damage,
                    owner,
                    point,
                    direction));
            }
        }

        private void DisableAndDestroy()
        {
            Body.velocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = false;
            Destroy(gameObject);
        }

        /// <summary>
        /// Converts the projectile's momentum into the impulse applied to treasure.
        /// </summary>
        public Vector3 CalculateTreasureImpulse(Vector3 impactVelocity)
        {
            return impactVelocity
                * Mathf.Max(0f, Body.mass)
                * TreasureImpulseMultiplier;
        }

        private bool IsOwnerCollider(Collider hit)
        {
            return hit != null
                && owner != null
                && hit.transform.root == owner.transform.root;
        }

        private Vector3 ResolveFallbackImpactNormal()
        {
            Vector3 velocity = Body.velocity;
            return velocity.sqrMagnitude > 0.0001f
                ? -velocity.normalized
                : -transform.forward;
        }

        private Rigidbody ConfigureBody()
        {
            Rigidbody resolvedBody = ResolveBody();
            resolvedBody.useGravity = false;
            resolvedBody.interpolation = RigidbodyInterpolation.Interpolate;
            resolvedBody.collisionDetectionMode =
                CollisionDetectionMode.ContinuousDynamic;
            return resolvedBody;
        }

        private Rigidbody ResolveBody()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            return body;
        }
    }
}
