using System;
using System.Collections.Generic;
using Supernova.UI;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Runtime value state shared by every physical resource that can be damaged
    /// by collisions and carried by the player's magnet.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ValuableObject : MonoBehaviour
    {
        public readonly struct BreakContext
        {
            public BreakContext(
                Vector3 position,
                Quaternion rotation,
                Vector3 scale,
                Vector3 velocity,
                Vector3 angularVelocity,
                Vector3 impactPoint,
                float impactStrength,
                float mass,
                int layer,
                int randomSeed)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
                Velocity = velocity;
                AngularVelocity = angularVelocity;
                ImpactPoint = impactPoint;
                ImpactStrength = impactStrength;
                Mass = mass;
                Layer = layer;
                RandomSeed = randomSeed;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 Scale { get; }
            public Vector3 Velocity { get; }
            public Vector3 AngularVelocity { get; }
            public Vector3 ImpactPoint { get; }
            public float ImpactStrength { get; }
            public float Mass { get; }
            public int Layer { get; }
            public int RandomSeed { get; }
        }

        public interface IBreakEffect
        {
            bool TrySpawnBreakEffect(BreakContext context);
        }

        // Collision impulse is divided by this object's mass before damage is
        // evaluated. The resulting specific impulse is approximately the
        // collision-induced velocity change in metres per second.
        public const float DefaultMinimumDamageImpulse = 1f;
        public const float DefaultValueLossPercentagePerSquaredImpulse = 0.03f;

        [SerializeField, Min(0)] private int initialValue;
        [SerializeField, Min(0)] private int currentValue;
        [SerializeField, Range(0f, 1f)] private float fragility = 0.5f;
        [SerializeField, Min(0f)] private float minimumDamageImpulse =
            DefaultMinimumDamageImpulse;
        [Tooltip(
            "Fraction of the initial value lost per squared unit of damaging "
            + "specific impulse. 0.03 means 3%.")]
        [SerializeField, Min(0f)]
        private float valueLossPercentagePerSquaredImpulse =
            DefaultValueLossPercentagePerSquaredImpulse;

        private readonly HashSet<int> protectionSources = new HashSet<int>();
        private bool isBroken;

        public event Action<int> ValueChanged;
        public event Action<int, Vector3> ValueLost;
        public event Action Broken;

        public int InitialValue => Mathf.Max(0, initialValue);
        public int CurrentValue => Mathf.Clamp(currentValue, 0, InitialValue);
        public float CurrentValuePercentage => InitialValue > 0
            ? CurrentValue / (float)InitialValue
            : 0f;
        public float Fragility => Mathf.Clamp01(fragility);
        public float MinimumDamageImpulse => Mathf.Max(0f, minimumDamageImpulse);
        public float ValueLossPercentagePerSquaredImpulse =>
            Mathf.Max(0f, valueLossPercentagePerSquaredImpulse);
        public bool IsCollisionValueLossProtected =>
            protectionSources.Count > 0;
        public bool IsBroken => isBroken;

        public void Configure(
            int value,
            float objectFragility,
            float damageImpulseThreshold = DefaultMinimumDamageImpulse,
            float lossPercentagePerSquaredImpulse =
                DefaultValueLossPercentagePerSquaredImpulse)
        {
            initialValue = Mathf.Max(0, value);
            currentValue = initialValue;
            fragility = Mathf.Clamp01(objectFragility);
            minimumDamageImpulse = Mathf.Max(0f, damageImpulseThreshold);
            valueLossPercentagePerSquaredImpulse =
                Mathf.Max(0f, lossPercentagePerSquaredImpulse);
            isBroken = currentValue <= 0;
            protectionSources.Clear();
            ValueChanged?.Invoke(CurrentValue);

            ValuableObjectWorldUi worldUi =
                GetComponent<ValuableObjectWorldUi>();
            if (worldUi == null)
            {
                worldUi = gameObject.AddComponent<ValuableObjectWorldUi>();
            }
            worldUi.Bind(this);
        }

        public int ApplyCollisionImpulse(float impulseMagnitude)
        {
            return ApplyCollisionImpulse(impulseMagnitude, transform.position);
        }

        public int ApplyCollisionImpulse(
            float impulseMagnitude,
            Vector3 collisionPoint)
        {
            if (isBroken || IsCollisionValueLossProtected)
            {
                return 0;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            float mass = body != null ? Mathf.Max(0.0001f, body.mass) : 1f;
            float specificImpulseMagnitude =
                Mathf.Max(0f, impulseMagnitude) / mass;
            int lostValue = CalculateValueLoss(
                InitialValue,
                specificImpulseMagnitude,
                Fragility,
                MinimumDamageImpulse,
                ValueLossPercentagePerSquaredImpulse);
            if (lostValue <= 0)
            {
                return 0;
            }

            int previousValue = CurrentValue;
            currentValue = Mathf.Max(0, previousValue - lostValue);
            int actualLoss = previousValue - currentValue;
            if (actualLoss <= 0)
            {
                return 0;
            }

            ValueChanged?.Invoke(CurrentValue);
            ValueLost?.Invoke(actualLoss, collisionPoint);
            if (currentValue == 0)
            {
                Break(collisionPoint, specificImpulseMagnitude);
            }
            return actualLoss;
        }

        public void SetCollisionValueLossProtected(
            UnityEngine.Object source,
            bool protectedFromLoss)
        {
            if (source == null)
            {
                return;
            }

            int sourceId = source.GetInstanceID();
            if (protectedFromLoss)
            {
                protectionSources.Add(sourceId);
            }
            else
            {
                protectionSources.Remove(sourceId);
            }
        }

        public static int CalculateValueLoss(
            int objectInitialValue,
            float specificImpulseMagnitude,
            float objectFragility,
            float damageImpulseThreshold = DefaultMinimumDamageImpulse,
            float lossPercentagePerSquaredImpulse =
                DefaultValueLossPercentagePerSquaredImpulse)
        {
            float damagingImpulse = Mathf.Max(
                0f,
                specificImpulseMagnitude
                    - Mathf.Max(0f, damageImpulseThreshold));
            float loss = damagingImpulse
                * damagingImpulse
                * Mathf.Clamp01(objectFragility)
                * Mathf.Max(0f, lossPercentagePerSquaredImpulse)
                * Mathf.Max(0, objectInitialValue);
            return loss > 0f ? Mathf.CeilToInt(loss) : 0;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null)
            {
                return;
            }

            // Static terrain and CharacterController colliders have no attached
            // Rigidbody, but their resolved contact impulse is still a real
            // impact and must be able to damage the valuable object.
            Vector3 collisionPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : transform.position;
            ApplyCollisionImpulse(
                collision.impulse.magnitude,
                collisionPoint);
        }

        private void Break(
            Vector3 collisionPoint,
            float impactStrength)
        {
            if (isBroken)
            {
                return;
            }

            isBroken = true;
            Broken?.Invoke();
            if (!Application.isPlaying)
            {
                return;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            var context = new BreakContext(
                transform.position,
                transform.rotation,
                transform.lossyScale,
                body != null ? body.velocity : Vector3.zero,
                body != null ? body.angularVelocity : Vector3.zero,
                collisionPoint,
                impactStrength,
                body != null ? body.mass : 1f,
                gameObject.layer,
                unchecked(
                    GetInstanceID() * 397
                    ^ Mathf.RoundToInt(collisionPoint.x * 31f)
                    ^ Mathf.RoundToInt(collisionPoint.y * 127f)
                    ^ Mathf.RoundToInt(collisionPoint.z * 521f)));
            TrySpawnBreakEffect(context);

            if (body != null)
            {
                Destroy(body);
            }

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = false;
            }

            Destroy(gameObject);
        }

        private void TrySpawnBreakEffect(BreakContext context)
        {
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IBreakEffect breakEffect
                    && breakEffect.TrySpawnBreakEffect(context))
                {
                    return;
                }
            }
        }

        private void OnValidate()
        {
            initialValue = Mathf.Max(0, initialValue);
            currentValue = Mathf.Clamp(currentValue, 0, initialValue);
            fragility = Mathf.Clamp01(fragility);
            minimumDamageImpulse = Mathf.Max(0f, minimumDamageImpulse);
            valueLossPercentagePerSquaredImpulse =
                Mathf.Max(0f, valueLossPercentagePerSquaredImpulse);
        }
    }
}
