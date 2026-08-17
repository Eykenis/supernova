using System;
using System.Collections;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(ValuableObject))]
    public sealed class TreasurePickup :
        MonoBehaviour,
        ValuableObject.IBreakEffect,
        ValuableObject.IBreakDespawnHandler
    {
        [SerializeField] private TreasureDefinition definition;
        private ValuableObject cachedValuable;
        private Rigidbody cachedBody;
        private Collider[] reusableColliders = new Collider[0];
        private bool[] reusableColliderEnabled = new bool[0];
        private Renderer[] reusableRenderers = new Renderer[0];
        private bool[] reusableRendererEnabled = new bool[0];
        private bool reusableStateCached;
        private bool authoredDetectCollisions = true;
        private bool authoredUseGravity = true;
        private bool authoredIsKinematic;
        private Action<TreasurePickup> poolReleaseHandler;
        private TreasureDestructionExplosion destructionExplosion;

        public TreasureDefinition Definition => definition;
        public BreakFragmentEffect LastBreakEffect { get; private set; }
        public ValuableObject Valuable
        {
            get
            {
                if (cachedValuable == null)
                {
                    cachedValuable = GetComponent<ValuableObject>();
                }
                return cachedValuable;
            }
        }
        public int Value => Valuable != null ? Valuable.CurrentValue : 0;

        private void Awake()
        {
            CacheReusableState();
        }

        public void SetPoolReleaseHandler(
            Action<TreasurePickup> handler)
        {
            poolReleaseHandler = handler;
        }

        public void PrepareForReuse()
        {
            CacheReusableState();
            StopAllCoroutines();
            LastBreakEffect = null;
            destructionExplosion?.PrepareForReuse();
            RestoreReusableState();
            // Runtime spawn safety can add colliders after Awake. Refresh the
            // authored snapshot only after restoring the previous one so those
            // late-added components also participate in subsequent pool cycles.
            reusableStateCached = false;
            CacheReusableState();
        }

        public void PrepareForPool()
        {
            StopAllCoroutines();
            poolReleaseHandler = null;
            definition = null;
            LastBreakEffect = null;
            destructionExplosion?.PrepareForPool();
            Rigidbody body = ResolveBody();
            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.detectCollisions = false;
                body.isKinematic = true;
                body.Sleep();
            }
        }

        public void Configure(TreasureDefinition value)
        {
            Configure(value, null);
        }

        public void Configure(
            TreasureDefinition value,
            IVoxelTerrain explosionTerrain)
        {
            CacheReusableState();
            StopAllCoroutines();
            definition = value;
            ConfigureDestructionExplosion(explosionTerrain);
            Valuable.Configure(
                definition != null ? definition.Value : 0,
                definition != null ? definition.Fragility : 0f);
            Rigidbody body = ResolveBody();
            if (definition != null)
            {
                body.mass = definition.Weight;
            }
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.solverIterations = Mathf.Max(body.solverIterations, 12);
            body.solverVelocityIterations =
                Mathf.Max(body.solverVelocityIterations, 6);
            body.maxAngularVelocity =
                Mathf.Min(body.maxAngularVelocity, 12f);
            body.drag = Mathf.Max(body.drag, 0.15f);
            body.angularDrag = Mathf.Max(body.angularDrag, 0.4f);
            body.detectCollisions = true;
            body.isKinematic = true;
            if (Application.isPlaying)
            {
                StartCoroutine(ArmPhysicsAfterTerrainSettles(body));
            }
        }

        private void ConfigureDestructionExplosion(
            IVoxelTerrain explosionTerrain)
        {
            PlayerToolDefinition tool = definition != null
                ? definition.DestructionExplosionTool
                : null;
            if (tool == null)
            {
                destructionExplosion?.PrepareForPool();
                return;
            }

            if (destructionExplosion == null)
            {
                destructionExplosion =
                    GetComponent<TreasureDestructionExplosion>();
            }
            if (destructionExplosion == null)
            {
                destructionExplosion =
                    gameObject.AddComponent<TreasureDestructionExplosion>();
            }
            destructionExplosion.Configure(tool, explosionTerrain);
        }

        public int ApplyProjectileImpulse(Vector3 impulse, Vector3 point)
        {
            Rigidbody body = ResolveBody();
            if (body != null && !body.isKinematic)
            {
                body.AddForceAtPosition(impulse, point, ForceMode.Impulse);
            }
            return Valuable != null
                ? Valuable.ApplyCollisionImpulse(impulse.magnitude, point)
                : 0;
        }

        public bool TrySpawnBreakEffect(
            ValuableObject.BreakContext context)
        {
            if (definition == null)
            {
                return false;
            }
            GameObject variant = definition.GetFractureVariant(
                context.RandomSeed);
            LastBreakEffect =
                BreakFragmentEffect.SpawnPrefab(variant, context);
            return LastBreakEffect != null;
        }

        bool ValuableObject.IBreakDespawnHandler.TryDespawnBrokenValuable(
            ValuableObject source)
        {
            Action<TreasurePickup> handler = poolReleaseHandler;
            if (handler == null)
            {
                return false;
            }
            handler(this);
            return true;
        }

        private Rigidbody ResolveBody()
        {
            if (cachedBody == null)
            {
                cachedBody = GetComponent<Rigidbody>();
            }
            return cachedBody;
        }

        private void CacheReusableState()
        {
            if (reusableStateCached)
            {
                return;
            }
            reusableColliders =
                GetComponentsInChildren<Collider>(true);
            reusableColliderEnabled =
                new bool[reusableColliders.Length];
            for (int i = 0; i < reusableColliders.Length; i++)
            {
                reusableColliderEnabled[i] =
                    reusableColliders[i] != null
                    && reusableColliders[i].enabled;
            }
            reusableRenderers =
                GetComponentsInChildren<Renderer>(true);
            reusableRendererEnabled =
                new bool[reusableRenderers.Length];
            for (int i = 0; i < reusableRenderers.Length; i++)
            {
                reusableRendererEnabled[i] =
                    reusableRenderers[i] != null
                    && reusableRenderers[i].enabled;
            }
            Rigidbody body = ResolveBody();
            if (body != null)
            {
                authoredDetectCollisions = body.detectCollisions;
                authoredUseGravity = body.useGravity;
                authoredIsKinematic = body.isKinematic;
            }
            reusableStateCached = true;
        }

        private void RestoreReusableState()
        {
            for (int i = 0; i < reusableColliders.Length; i++)
            {
                Collider collider = reusableColliders[i];
                if (collider != null)
                {
                    collider.enabled = reusableColliderEnabled[i];
                }
            }
            for (int i = 0; i < reusableRenderers.Length; i++)
            {
                Renderer renderer = reusableRenderers[i];
                if (renderer != null)
                {
                    renderer.enabled = reusableRendererEnabled[i];
                }
            }
            Rigidbody body = ResolveBody();
            if (body == null)
            {
                return;
            }
            body.detectCollisions = authoredDetectCollisions;
            body.useGravity = authoredUseGravity;
            body.isKinematic = authoredIsKinematic;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
        }

        private static IEnumerator ArmPhysicsAfterTerrainSettles(
            Rigidbody body)
        {
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();
            if (body != null && body.gameObject.activeInHierarchy)
            {
                body.isKinematic = false;
                body.WakeUp();
            }
        }
    }
}
