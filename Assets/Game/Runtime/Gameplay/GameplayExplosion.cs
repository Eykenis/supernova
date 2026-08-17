using System.Collections.Generic;
using Supernova.Audio;
using Supernova.Infrastructure;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Receives blast momentum when an actor is driven by a non-Rigidbody motor,
    /// such as the player's CharacterController.
    /// </summary>
    public interface IExplosionImpulseReceiver
    {
        GameObject ExplosionImpulseOwner { get; }
        bool ApplyExplosionImpulse(Vector3 impulse);
    }

    public readonly struct ExplosionEntityImpactResult
    {
        public ExplosionEntityImpactResult(
            int impulsedBodyCount,
            int impulsedReceiverCount,
            int damagedEntityCount)
        {
            ImpulsedBodyCount = impulsedBodyCount;
            ImpulsedReceiverCount = impulsedReceiverCount;
            DamagedEntityCount = damagedEntityCount;
        }

        public int ImpulsedBodyCount { get; }
        public int ImpulsedReceiverCount { get; }
        public int DamagedEntityCount { get; }
    }

    public readonly struct GameplayExplosionResult
    {
        public GameplayExplosionResult(
            bool affectedTerrain,
            VoxelExplosionResult terrainResult,
            ExplosionEntityImpactResult entityResult,
            GameObject effect)
        {
            AffectedTerrain = affectedTerrain;
            TerrainResult = terrainResult;
            EntityResult = entityResult;
            Effect = effect;
        }

        public bool AffectedTerrain { get; }
        public VoxelExplosionResult TerrainResult { get; }
        public ExplosionEntityImpactResult EntityResult { get; }
        public GameObject Effect { get; }
    }

    /// <summary>
    /// Shared bomb-strength explosion pipeline used by thrown bombs and
    /// destruction-triggered treasure explosions.
    /// </summary>
    public static class GameplayExplosion
    {
        public static GameplayExplosionResult Detonate(
            GameObject source,
            Vector3 explosionCenter,
            IVoxelTerrain terrain,
            VoxelExplosionSettings settings,
            float entityImpulse,
            float upwardModifier,
            GameObject effectPrefab,
            float effectLifetime,
            Rigidbody ignoredBody = null)
        {
            AudioAssetReferences audio = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.Audio
                : null;
            SoundEffectEvents.RequestPlay(
                audio != null ? audio.BombExplosion : null,
                explosionCenter);

            bool affectedTerrain = false;
            VoxelExplosionResult terrainResult = default;
            if (terrain != null)
            {
                affectedTerrain = terrain.TryMineExplosion(
                    explosionCenter,
                    settings,
                    out terrainResult);
            }

            GameObject effect = SpawnEffect(
                effectPrefab,
                explosionCenter,
                effectLifetime);
            ExplosionEntityImpactResult entityResult = ApplyEntityImpact(
                source,
                explosionCenter,
                settings.Radius,
                entityImpulse,
                upwardModifier,
                ignoredBody);
            return new GameplayExplosionResult(
                affectedTerrain,
                terrainResult,
                entityResult,
                effect);
        }

        public static ExplosionEntityImpactResult ApplyEntityImpact(
            GameObject source,
            Vector3 explosionCenter,
            float radius,
            float maximumImpulse,
            float upwardModifier,
            Rigidbody ignoredBody = null)
        {
            if (maximumImpulse <= 0f || radius <= 0f)
            {
                return default;
            }

            Collider[] hits = Physics.OverlapSphere(
                explosionCenter,
                radius,
                ~0,
                QueryTriggerInteraction.Collide);
            var affectedBodies = new HashSet<Rigidbody>();
            var impulsedOwners = new HashSet<int>();
            var damagedOwners = new HashSet<int>();
            int impulsedReceiverCount = 0;
            int damagedEntityCount = 0;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null)
                {
                    continue;
                }

                Rigidbody affectedBody = hit.attachedRigidbody;
                if (affectedBody == ignoredBody)
                {
                    continue;
                }

                Vector3 targetCenter = affectedBody != null
                    ? affectedBody.worldCenterOfMass
                    : hit.bounds.center;
                Vector3 impulse = CalculateEntityImpulse(
                    explosionCenter,
                    targetCenter,
                    maximumImpulse,
                    radius,
                    upwardModifier);
                if (impulse.sqrMagnitude <= 0f)
                {
                    continue;
                }

                if (TryFindInterface(
                        hit,
                        out ICollisionImpulseDamageReceiver damageReceiver))
                {
                    GameObject owner = damageReceiver.CollisionImpulseOwner;
                    if (IsDifferentOwner(source, owner)
                        && damagedOwners.Add(owner.GetInstanceID())
                        && damageReceiver.ApplyCollisionImpulseDamage(
                            impulse.magnitude,
                            hit.ClosestPoint(explosionCenter)))
                    {
                        damagedEntityCount++;
                    }
                }

                if ((affectedBody == null || affectedBody.isKinematic)
                    && TryFindInterface(
                        hit,
                        out IExplosionImpulseReceiver impulseReceiver))
                {
                    GameObject owner = impulseReceiver.ExplosionImpulseOwner;
                    if (IsDifferentOwner(source, owner)
                        && impulsedOwners.Add(owner.GetInstanceID())
                        && impulseReceiver.ApplyExplosionImpulse(impulse))
                    {
                        impulsedReceiverCount++;
                    }
                }

                if (affectedBody != null
                    && !affectedBody.isKinematic
                    && affectedBodies.Add(affectedBody))
                {
                    affectedBody.AddForce(impulse, ForceMode.Impulse);
                }
            }

            return new ExplosionEntityImpactResult(
                affectedBodies.Count,
                impulsedReceiverCount,
                damagedEntityCount);
        }

        public static Vector3 CalculateEntityImpulse(
            Vector3 explosionCenter,
            Vector3 targetCenter,
            float maximumImpulse,
            float radius,
            float upwardModifier)
        {
            float safeRadius = Mathf.Max(0.01f, radius);
            float distance = Vector3.Distance(explosionCenter, targetCenter);
            if (distance > safeRadius || maximumImpulse <= 0f)
            {
                return Vector3.zero;
            }

            float magnitude = Mathf.Max(0f, maximumImpulse)
                * (1f - Mathf.Clamp01(distance / safeRadius));
            Vector3 apparentCenter = explosionCenter
                - Vector3.up * Mathf.Max(0f, upwardModifier);
            Vector3 direction = targetCenter - apparentCenter;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.up;
            }
            else
            {
                direction.Normalize();
            }
            return direction * magnitude;
        }

        private static GameObject SpawnEffect(
            GameObject effectPrefab,
            Vector3 position,
            float lifetime)
        {
            if (effectPrefab == null)
            {
                return null;
            }

            GameObject instance = Object.Instantiate(
                effectPrefab,
                position,
                Quaternion.identity);
            instance.name = effectPrefab.name;
            if (Application.isPlaying)
            {
                Object.Destroy(instance, Mathf.Max(0.01f, lifetime));
            }
            return instance;
        }

        private static bool IsDifferentOwner(
            GameObject source,
            GameObject owner)
        {
            return owner != null
                && (source == null
                    || owner.transform.root != source.transform.root);
        }

        private static bool TryFindInterface<T>(
            Component component,
            out T result)
            where T : class
        {
            result = null;
            Transform cursor = component != null
                ? component.transform
                : null;
            while (cursor != null)
            {
                MonoBehaviour[] behaviours =
                    cursor.GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is T candidate)
                    {
                        result = candidate;
                        return true;
                    }
                }
                cursor = cursor.parent;
            }
            return false;
        }
    }
}
