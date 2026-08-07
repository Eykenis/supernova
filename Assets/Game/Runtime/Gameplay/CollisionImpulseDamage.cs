using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Receives an explicitly generated impulse as durability/health damage.
    /// This is used when gameplay applies force without producing a resolved
    /// Unity collision, such as a nearby explosion.
    /// </summary>
    public interface ICollisionImpulseDamageReceiver
    {
        GameObject CollisionImpulseOwner { get; }

        bool ApplyCollisionImpulseDamage(
            float impulseMagnitude,
            Vector3 collisionPoint);
    }

    /// <summary>
    /// Converts a resolved collision impulse into durability damage. Impulse is
    /// normalized by the receiving body's mass so differently sized objects use
    /// the same impact-speed rule.
    /// </summary>
    public static class CollisionImpulseDamage
    {
        public const float DefaultMinimumDamageImpulse = 1f;
        public const float DefaultDamagePercentagePerSquaredImpulse = 0.03f;

        public static float CalculateDamage(
            float maximumDurability,
            float absoluteImpulseMagnitude,
            float fragility,
            float damageImpulseThreshold =
                DefaultMinimumDamageImpulse,
            float damagePercentagePerSquaredImpulse =
                DefaultDamagePercentagePerSquaredImpulse,
            float objectMass = 1f)
        {
            float damagingImpulse = CalculateDamagingSpecificImpulse(
                absoluteImpulseMagnitude,
                damageImpulseThreshold,
                objectMass);
            return damagingImpulse
                * damagingImpulse
                * Mathf.Clamp01(fragility)
                * Mathf.Max(0f, damagePercentagePerSquaredImpulse)
                * Mathf.Max(0f, maximumDurability);
        }

        public static float CalculateDamagingSpecificImpulse(
            float absoluteImpulseMagnitude,
            float damageImpulseThreshold,
            float objectMass)
        {
            float massNormalizedImpulse =
                Mathf.Max(0f, absoluteImpulseMagnitude)
                / Mathf.Max(0.0001f, objectMass);
            return Mathf.Max(
                0f,
                massNormalizedImpulse
                    - Mathf.Max(0f, damageImpulseThreshold));
        }
    }
}
