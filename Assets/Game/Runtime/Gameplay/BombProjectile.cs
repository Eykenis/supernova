using Supernova.Voxels;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Physics-thrown timed bomb. Once launched it detonates exactly once and
    /// delegates all voxel durability, BFS propagation, and mesh rebuilding to
    /// the terrain that launched it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public sealed class BombProjectile : MonoBehaviour
    {
        [SerializeField] private Rigidbody body;
        [SerializeField, Min(0f)] private float fuseSeconds = 2f;
        [SerializeField, Min(0.01f)] private float explosionRadius = 2f;
        [SerializeField, Min(0f)] private float innerRadius = 1f;
        [SerializeField, Min(0.01f)] private float innerMiningPower = 30f;
        [SerializeField, Min(0.01f)] private float outerMiningPower = 10f;
        [SerializeField, Min(1f)] private float propagationDivisor = 2f;

        [Header("Entity Impulse")]
        [Tooltip("Radial impulse applied once to each dynamic Rigidbody inside the blast.")]
        [SerializeField, Min(0f)] private float entityExplosionImpulse = 240f;
        [Tooltip("Raises the explosion origin to give nearby bodies an upward kick.")]
        [SerializeField, Min(0f)] private float entityUpwardModifier = 0.6f;
        [SerializeField, HideInInspector] private int configurationVersion = 3;

        private IVoxelTerrain terrain;
        private float detonationTime;
        private float activeEntityExplosionImpulse;
        private bool isArmed;
        private bool hasExploded;

        public Rigidbody Body => ResolveBody();
        public float FuseSeconds => Mathf.Max(0f, fuseSeconds);
        public float ExplosionRadius => Mathf.Max(0.01f, explosionRadius);
        public float InnerRadius => Mathf.Clamp(
            innerRadius,
            0f,
            ExplosionRadius);
        public float InnerMiningPower => Mathf.Max(0.01f, innerMiningPower);
        public float OuterMiningPower => Mathf.Max(0.01f, outerMiningPower);
        public float PropagationDivisor => Mathf.Max(1f, propagationDivisor);
        public float EntityExplosionImpulse =>
            Mathf.Max(0f, entityExplosionImpulse);
        public float ActiveEntityExplosionImpulse =>
            Mathf.Max(0f, activeEntityExplosionImpulse);
        public float EntityUpwardModifier =>
            Mathf.Max(0f, entityUpwardModifier);
        public bool IsArmed => isArmed;
        public bool HasExploded => hasExploded;
        public int ConfigurationVersion => configurationVersion;
        public bool LastExplosionAffectedTerrain { get; private set; }
        public int LastImpulsedBodyCount { get; private set; }
        public VoxelExplosionResult LastExplosionResult { get; private set; }
        public VoxelExplosionSettings ExplosionSettings =>
            new VoxelExplosionSettings(
                ExplosionRadius,
                InnerRadius,
                InnerMiningPower,
                OuterMiningPower,
                PropagationDivisor);

        private void Awake()
        {
            ResolveBody();
        }

        private void OnEnable()
        {
            terrain = null;
            isArmed = false;
            hasExploded = false;
            LastExplosionAffectedTerrain = false;
            LastImpulsedBodyCount = 0;
            LastExplosionResult = default;
            activeEntityExplosionImpulse = EntityExplosionImpulse;
        }

        private void Update()
        {
            if (isArmed && !hasExploded && Time.time >= detonationTime)
                Detonate();
        }

        public void Launch(
            Vector3 velocity,
            Vector3 angularVelocity,
            IVoxelTerrain voxelTerrain,
            float configuredEntityExplosionImpulse = -1f)
        {
            terrain = voxelTerrain;
            activeEntityExplosionImpulse = configuredEntityExplosionImpulse >= 0f
                ? configuredEntityExplosionImpulse
                : EntityExplosionImpulse;
            Rigidbody resolvedBody = ResolveBody();
            resolvedBody.velocity = velocity;
            resolvedBody.angularVelocity = angularVelocity;
            resolvedBody.WakeUp();
            detonationTime = Time.time + FuseSeconds;
            isArmed = true;
        }

        public bool Detonate()
        {
            if (hasExploded)
                return false;

            hasExploded = true;
            isArmed = false;
            if (terrain != null)
            {
                LastExplosionAffectedTerrain = terrain.TryMineExplosion(
                    transform.position,
                    ExplosionSettings,
                    out VoxelExplosionResult result);
                LastExplosionResult = result;
            }

            LastImpulsedBodyCount = ApplyEntityExplosionImpulse(
                transform.position);

            DisableAndDestroy();
            return true;
        }

        private int ApplyEntityExplosionImpulse(Vector3 explosionCenter)
        {
            float impulse = ActiveEntityExplosionImpulse;
            if (impulse <= 0f)
                return 0;

            Collider[] hits = Physics.OverlapSphere(
                explosionCenter,
                ExplosionRadius,
                ~0,
                QueryTriggerInteraction.Collide);
            var affectedBodies = new HashSet<Rigidbody>();
            Rigidbody bombBody = ResolveBody();
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                Rigidbody affectedBody = hit != null
                    ? hit.attachedRigidbody
                    : null;
                if (affectedBody == null
                    || affectedBody == bombBody
                    || affectedBody.isKinematic
                    || affectedBodies.Contains(affectedBody))
                {
                    continue;
                }

                Vector3 bodyImpulse = CalculateEntityImpulse(
                    explosionCenter,
                    affectedBody.worldCenterOfMass,
                    impulse,
                    ExplosionRadius,
                    EntityUpwardModifier);
                if (bodyImpulse.sqrMagnitude <= 0f)
                    continue;

                affectedBodies.Add(affectedBody);
                affectedBody.AddForce(bodyImpulse, ForceMode.Impulse);
            }
            return affectedBodies.Count;
        }

        public static Vector3 CalculateEntityImpulse(
            Vector3 explosionCenter,
            Vector3 bodyCenter,
            float maximumImpulse,
            float radius,
            float upwardModifier)
        {
            float safeRadius = Mathf.Max(0.01f, radius);
            float distance = Vector3.Distance(explosionCenter, bodyCenter);
            if (distance > safeRadius || maximumImpulse <= 0f)
                return Vector3.zero;

            float magnitude = Mathf.Max(0f, maximumImpulse)
                * (1f - Mathf.Clamp01(distance / safeRadius));
            Vector3 apparentCenter = explosionCenter
                - Vector3.up * Mathf.Max(0f, upwardModifier);
            Vector3 direction = bodyCenter - apparentCenter;
            if (direction.sqrMagnitude <= 0.0001f)
                direction = Vector3.up;
            else
                direction.Normalize();
            return direction * magnitude;
        }

        private void DisableAndDestroy()
        {
            Rigidbody resolvedBody = ResolveBody();
            resolvedBody.velocity = Vector3.zero;
            resolvedBody.angularVelocity = Vector3.zero;
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = false;
            if (Application.isPlaying)
                Destroy(gameObject);
        }

        private Rigidbody ResolveBody()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            return body;
        }
    }
}
