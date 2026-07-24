using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Effects
{
    [Serializable]
    public struct AreaEffectContext
    {
        public Vector3 Origin;
        public float Radius;
        public float Damage;
        public float Impulse;
        [Range(0f, 1f)] public float TerrainRandomness;
        public int Seed;
        public GameObject Source;

        public AreaEffectContext(Vector3 origin, float radius, float damage, float impulse,
            float terrainRandomness, int seed, GameObject source)
        {
            Origin = origin;
            Radius = Mathf.Max(0f, radius);
            Damage = damage;
            Impulse = Mathf.Max(0f, impulse);
            TerrainRandomness = Mathf.Clamp01(terrainRandomness);
            Seed = seed;
            Source = source;
        }
    }

    public interface IAreaEffectReceiver
    {
        void ReceiveAreaEffect(in AreaEffectContext context);
    }

    /// <summary>
    /// Registration-based receiver base. Dispatch cost depends on receiver count rather than scene size,
    /// and works for targets (such as streamed voxel worlds) that do not own a stable collider.
    /// </summary>
    public abstract class AreaEffectReceiverBehaviour : MonoBehaviour, IAreaEffectReceiver
    {
        protected virtual void OnEnable() => AreaEffectDispatcher.Register(this);
        protected virtual void OnDisable() => AreaEffectDispatcher.Unregister(this);
        public abstract void ReceiveAreaEffect(in AreaEffectContext context);
    }

    public static class AreaEffectDispatcher
    {
        private static readonly List<AreaEffectReceiverBehaviour> Receivers =
            new List<AreaEffectReceiverBehaviour>(16);
        private static readonly HashSet<Rigidbody> AffectedBodies = new HashSet<Rigidbody>();
        private static Collider[] overlapBuffer = new Collider[64];

        internal static void Register(AreaEffectReceiverBehaviour receiver)
        {
            if (receiver != null && !Receivers.Contains(receiver)) Receivers.Add(receiver);
        }

        internal static void Unregister(AreaEffectReceiverBehaviour receiver)
        {
            Receivers.Remove(receiver);
        }

        public static void Dispatch(in AreaEffectContext context, int physicsLayerMask = ~0)
        {
            // Reverse iteration tolerates a receiver disabling itself while processing the effect.
            for (int i = Receivers.Count - 1; i >= 0; i--)
            {
                AreaEffectReceiverBehaviour receiver = Receivers[i];
                if (receiver == null)
                {
                    Receivers.RemoveAt(i);
                    continue;
                }

                if (receiver.isActiveAndEnabled) receiver.ReceiveAreaEffect(context);
            }

            if (context.Impulse <= 0f || context.Radius <= 0f) return;

            AffectedBodies.Clear();
            int count = Physics.OverlapSphereNonAlloc(
                context.Origin, context.Radius, overlapBuffer, physicsLayerMask,
                QueryTriggerInteraction.Ignore);
            if (count == overlapBuffer.Length)
            {
                overlapBuffer = new Collider[overlapBuffer.Length * 2];
                count = Physics.OverlapSphereNonAlloc(
                    context.Origin, context.Radius, overlapBuffer, physicsLayerMask,
                    QueryTriggerInteraction.Ignore);
            }

            for (int i = 0; i < count; i++)
            {
                Rigidbody body = overlapBuffer[i].attachedRigidbody;
                if (body == null || body.isKinematic || !AffectedBodies.Add(body)) continue;
                body.AddExplosionForce(context.Impulse, context.Origin, context.Radius, 0.2f,
                    ForceMode.Impulse);
            }
        }
    }

    /// <summary>Reusable non-voxel example receiver for actors, props and future damage sources.</summary>
    public sealed class DestructibleHealth : AreaEffectReceiverBehaviour
    {
        [SerializeField, Min(0f)] private float health = 100f;
        [SerializeField] private bool destroyOnDepleted = true;
        public float Health => health;

        public override void ReceiveAreaEffect(in AreaEffectContext context)
        {
            if (context.Damage <= 0f || context.Radius <= 0f) return;
            float distance = Vector3.Distance(transform.position, context.Origin);
            if (distance > context.Radius) return;
            health -= context.Damage * (1f - distance / context.Radius);
            if (health <= 0f && destroyOnDepleted) Destroy(gameObject);
        }
    }
}
