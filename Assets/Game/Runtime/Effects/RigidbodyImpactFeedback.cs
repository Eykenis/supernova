using System;
using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Effects
{
    /// <summary>
    /// Describes one resolved rigidbody impact for visual and audio feedback.
    /// Gameplay damage remains independent and can use a different threshold.
    /// </summary>
    public readonly struct RigidbodyImpactFeedbackRequest
    {
        public RigidbodyImpactFeedbackRequest(
            Rigidbody sourceBody,
            Rigidbody otherBody,
            Collider sourceCollider,
            Collider otherCollider,
            Vector3 position,
            Vector3 normal,
            float specificImpulse,
            float normalizedStrength,
            int randomSeed)
        {
            SourceBody = sourceBody;
            OtherBody = otherBody;
            SourceCollider = sourceCollider;
            OtherCollider = otherCollider;
            Position = position;
            Normal = normal.sqrMagnitude > 0.0001f
                ? normal.normalized
                : Vector3.up;
            SpecificImpulse = Mathf.Max(0f, specificImpulse);
            NormalizedStrength = Mathf.Clamp01(normalizedStrength);
            RandomSeed = randomSeed;
        }

        public Rigidbody SourceBody { get; }
        public Rigidbody OtherBody { get; }
        public Collider SourceCollider { get; }
        public Collider OtherCollider { get; }
        public Vector3 Position { get; }
        public Vector3 Normal { get; }
        public float SpecificImpulse { get; }
        public float NormalizedStrength { get; }
        public int RandomSeed { get; }
        public bool IsValid => SourceBody != null && NormalizedStrength > 0f;
    }

    /// <summary>
    /// Shared impact channel. The smoke renderer is the default consumer and
    /// collision audio can subscribe without adding another physics callback.
    /// </summary>
    public static class RigidbodyImpactFeedbackEvents
    {
        public static event Action<RigidbodyImpactFeedbackRequest> Requested;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Requested = null;
        }

        public static bool Publish(RigidbodyImpactFeedbackRequest request)
        {
            if (!request.IsValid)
            {
                return false;
            }

            RigidbodyImpactSmokeEmitter.RequestPlay(request);
            Requested?.Invoke(request);
            return true;
        }
    }

    /// <summary>
    /// Converts OnCollisionEnter into one mass-normalized impact request. When
    /// two instrumented rigidbodies collide, only one side publishes the pair.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RigidbodyImpactFeedback : MonoBehaviour
    {
        public const float DefaultMinimumSpecificImpulse = 0.6f;
        public const float DefaultFullStrengthSpecificImpulse = 6f;
        public const float DefaultRepeatedCollisionCooldown = 0.1f;
        public const float MinimumVisibleStrength = 0.15f;

        [SerializeField, Min(0f)] private float minimumSpecificImpulse =
            DefaultMinimumSpecificImpulse;
        [SerializeField, Min(0f)] private float fullStrengthSpecificImpulse =
            DefaultFullStrengthSpecificImpulse;
        [SerializeField, Min(0f)] private float repeatedCollisionCooldown =
            DefaultRepeatedCollisionCooldown;

        private readonly Dictionary<int, float> lastImpactTimes =
            new Dictionary<int, float>();
        private Rigidbody cachedBody;
        private uint impactSequence;

        public float MinimumSpecificImpulse =>
            Mathf.Max(0f, minimumSpecificImpulse);
        public float FullStrengthSpecificImpulse => Mathf.Max(
            MinimumSpecificImpulse,
            fullStrengthSpecificImpulse);
        public float RepeatedCollisionCooldown =>
            Mathf.Max(0f, repeatedCollisionCooldown);

        private Rigidbody Body
        {
            get
            {
                if (cachedBody == null)
                {
                    cachedBody = GetComponent<Rigidbody>();
                }
                return cachedBody;
            }
        }

        public static RigidbodyImpactFeedback Ensure(Rigidbody body)
        {
            if (body == null)
            {
                return null;
            }

            RigidbodyImpactFeedback feedback =
                body.GetComponent<RigidbodyImpactFeedback>();
            return feedback != null
                ? feedback
                : body.gameObject.AddComponent<RigidbodyImpactFeedback>();
        }

        public static float CalculateEffectiveSpecificImpulse(
            float impulseMagnitude,
            float sourceMass,
            float otherMass = 0f)
        {
            float effectiveMass = Mathf.Max(0.0001f, sourceMass);
            if (otherMass > 0f)
            {
                effectiveMass = Mathf.Min(
                    effectiveMass,
                    Mathf.Max(0.0001f, otherMass));
            }

            return Mathf.Max(0f, impulseMagnitude) / effectiveMass;
        }

        public static float CalculateNormalizedStrength(
            float specificImpulse,
            float minimumImpulse = DefaultMinimumSpecificImpulse,
            float fullStrengthImpulse = DefaultFullStrengthSpecificImpulse)
        {
            float minimum = Mathf.Max(0f, minimumImpulse);
            if (specificImpulse + 0.0001f < minimum)
            {
                return 0f;
            }

            float maximum = Mathf.Max(minimum + 0.0001f, fullStrengthImpulse);
            float ramp = Mathf.InverseLerp(minimum, maximum, specificImpulse);
            return Mathf.Lerp(MinimumVisibleStrength, 1f, ramp);
        }

        private void Awake()
        {
            cachedBody = GetComponent<Rigidbody>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            Rigidbody body = Body;
            if (!Application.isPlaying || collision == null || body == null)
            {
                return;
            }

            Rigidbody otherBody = collision.rigidbody;
            if (!IsCanonicalReporter(body, otherBody))
            {
                return;
            }

            ContactPoint contact = collision.contactCount > 0
                ? collision.GetContact(0)
                : default;
            Collider otherCollider = collision.collider;
            int pairKey = otherCollider != null
                ? otherCollider.GetInstanceID()
                : otherBody != null
                    ? otherBody.GetInstanceID()
                    : 0;
            float specificImpulse = CalculateEffectiveSpecificImpulse(
                collision.impulse.magnitude,
                body.mass,
                otherBody != null ? otherBody.mass : 0f);
            float strength = CalculateNormalizedStrength(
                specificImpulse,
                MinimumSpecificImpulse,
                FullStrengthSpecificImpulse);
            if (strength <= 0f)
            {
                return;
            }

            if (!CanReportPair(pairKey, Time.time))
            {
                return;
            }

            Vector3 point = collision.contactCount > 0
                ? contact.point
                : transform.position;
            Vector3 normal = collision.contactCount > 0
                ? contact.normal
                : ResolveFallbackNormal(collision.relativeVelocity);
            int randomSeed = unchecked(
                body.GetInstanceID() * 397
                ^ pairKey * 7919
                ^ (int)(++impactSequence * 2654435761u));
            var request = new RigidbodyImpactFeedbackRequest(
                body,
                otherBody,
                collision.contactCount > 0 ? contact.thisCollider : null,
                otherCollider,
                point,
                normal,
                specificImpulse,
                strength,
                randomSeed);
            RigidbodyImpactFeedbackEvents.Publish(request);
        }

        private bool CanReportPair(int pairKey, float now)
        {
            if (RepeatedCollisionCooldown > 0f
                && lastImpactTimes.TryGetValue(pairKey, out float lastTime)
                && now - lastTime < RepeatedCollisionCooldown)
            {
                return false;
            }

            lastImpactTimes[pairKey] = now;
            if (lastImpactTimes.Count > 64)
            {
                lastImpactTimes.Clear();
                lastImpactTimes[pairKey] = now;
            }
            return true;
        }

        private static bool IsCanonicalReporter(
            Rigidbody body,
            Rigidbody otherBody)
        {
            if (otherBody == null || otherBody == body)
            {
                return true;
            }

            RigidbodyImpactFeedback otherFeedback =
                otherBody.GetComponent<RigidbodyImpactFeedback>();
            return otherFeedback == null
                || !otherFeedback.isActiveAndEnabled
                || body.GetInstanceID() < otherBody.GetInstanceID();
        }

        private static Vector3 ResolveFallbackNormal(Vector3 relativeVelocity)
        {
            return relativeVelocity.sqrMagnitude > 0.0001f
                ? -relativeVelocity.normalized
                : Vector3.up;
        }

        private void OnValidate()
        {
            minimumSpecificImpulse = Mathf.Max(0f, minimumSpecificImpulse);
            fullStrengthSpecificImpulse = Mathf.Max(
                minimumSpecificImpulse,
                fullStrengthSpecificImpulse);
            repeatedCollisionCooldown = Mathf.Max(
                0f,
                repeatedCollisionCooldown);
        }
    }
}
