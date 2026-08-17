using Supernova.Audio;
using Supernova.Effects;
using Supernova.Infrastructure;
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
    [RequireComponent(typeof(RigidbodyImpactFeedback))]
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

        [Header("Explosion Effect")]
        [SerializeField] private GameObject explosionEffectPrefab;
        [SerializeField, Min(0.01f)]
        private float explosionEffectLifetime = 3f;
        [SerializeField, HideInInspector] private int configurationVersion = 4;

        private IVoxelTerrain terrain;
        private float detonationTime;
        private float activeEntityExplosionImpulse;
        private GameObject activeExplosionEffectPrefab;
        private float activeExplosionEffectLifetime;
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
        public GameObject ExplosionEffectPrefab => explosionEffectPrefab;
        public GameObject ActiveExplosionEffectPrefab =>
            activeExplosionEffectPrefab;
        public float ExplosionEffectLifetime =>
            Mathf.Max(0.01f, explosionEffectLifetime);
        public float ActiveExplosionEffectLifetime =>
            Mathf.Max(0.01f, activeExplosionEffectLifetime);
        public bool IsArmed => isArmed;
        public bool HasExploded => hasExploded;
        public int ConfigurationVersion => configurationVersion;
        public bool LastExplosionAffectedTerrain { get; private set; }
        public int LastImpulsedBodyCount { get; private set; }
        public int LastDamagedEntityCount { get; private set; }
        public GameObject LastExplosionEffect { get; private set; }
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
            RigidbodyImpactFeedback.Ensure(ResolveBody());
        }

        private void OnEnable()
        {
            terrain = null;
            isArmed = false;
            hasExploded = false;
            LastExplosionAffectedTerrain = false;
            LastImpulsedBodyCount = 0;
            LastDamagedEntityCount = 0;
            LastExplosionEffect = null;
            LastExplosionResult = default;
            activeEntityExplosionImpulse = EntityExplosionImpulse;
            activeExplosionEffectPrefab = ExplosionEffectPrefab;
            activeExplosionEffectLifetime = ExplosionEffectLifetime;
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
            float configuredEntityExplosionImpulse = -1f,
            GameObject configuredExplosionEffectPrefab = null,
            float configuredExplosionEffectLifetime = -1f)
        {
            terrain = voxelTerrain;
            activeEntityExplosionImpulse = configuredEntityExplosionImpulse >= 0f
                ? configuredEntityExplosionImpulse
                : EntityExplosionImpulse;
            activeExplosionEffectPrefab = configuredExplosionEffectPrefab != null
                ? configuredExplosionEffectPrefab
                : ExplosionEffectPrefab;
            activeExplosionEffectLifetime = configuredExplosionEffectLifetime >= 0f
                ? configuredExplosionEffectLifetime
                : ExplosionEffectLifetime;
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
            GameplayExplosionResult result = GameplayExplosion.Detonate(
                gameObject,
                transform.position,
                terrain,
                ExplosionSettings,
                ActiveEntityExplosionImpulse,
                EntityUpwardModifier,
                ActiveExplosionEffectPrefab,
                ActiveExplosionEffectLifetime,
                ResolveBody());
            LastExplosionAffectedTerrain = result.AffectedTerrain;
            LastExplosionResult = result.TerrainResult;
            LastImpulsedBodyCount =
                result.EntityResult.ImpulsedBodyCount;
            LastDamagedEntityCount =
                result.EntityResult.DamagedEntityCount;
            LastExplosionEffect = result.Effect;

            DisableAndDestroy();
            return true;
        }


        public static Vector3 CalculateEntityImpulse(
            Vector3 explosionCenter,
            Vector3 bodyCenter,
            float maximumImpulse,
            float radius,
            float upwardModifier)
        {
            return GameplayExplosion.CalculateEntityImpulse(
                explosionCenter,
                bodyCenter,
                maximumImpulse,
                radius,
                upwardModifier);
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
