using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Maths for a rope that behaves like a rope rather than a winch.
    ///
    /// A rope is a one-sided distance constraint: inside its length it does nothing
    /// and the player falls freely, and at full extension it removes only the outward
    /// radial part of the velocity. The tangential part survives untouched, which is
    /// what produces a pendulum swing. Pulling the player towards the anchor with a
    /// steady force instead — the obvious implementation — destroys tangential motion
    /// and always reads as a tractor beam.
    ///
    /// Kept as pure static functions so the behaviour can be verified without a
    /// CharacterController, a scene, or play mode.
    /// </summary>
    public static class RopeConstraint
    {
        /// <summary>
        /// How far the player has to be moved to sit exactly on the rope's sphere.
        /// A rope is inextensible, so constraining velocity alone is not enough:
        /// gravity is integrated before the constraint runs, which leaks a sub-frame
        /// of displacement every frame. Left uncorrected the rope visibly stretches
        /// and the velocity correction then overshoots, which reads as vertical
        /// jitter at full extension.
        /// </summary>
        public static Vector3 CalculatePositionCorrection(
            Vector3 anchorToPlayer,
            float ropeLength)
        {
            float distance = anchorToPlayer.magnitude;
            if (distance <= 0.0001f || distance <= ropeLength)
                return Vector3.zero;

            // Move straight back along the rope until the player is on the sphere.
            return anchorToPlayer * (ropeLength / distance - 1f);
        }

        /// <summary>
        /// Removes the component of <paramref name="velocity"/> that would stretch the
        /// rope past <paramref name="ropeLength"/>, and reports whether the rope was
        /// taut. Tangential velocity is preserved exactly.
        /// </summary>
        /// <param name="anchorToPlayer">Vector from the anchor to the player.</param>
        public static Vector3 ApplyTautConstraint(
            Vector3 velocity,
            Vector3 anchorToPlayer,
            float ropeLength,
            out bool taut)
        {
            taut = false;
            float distance = anchorToPlayer.magnitude;
            if (distance <= 0.0001f || distance < ropeLength) return velocity;

            Vector3 outward = anchorToPlayer / distance;
            float outwardSpeed = Vector3.Dot(velocity, outward);
            // Moving inward (or exactly along the sphere) leaves the rope slack.
            if (outwardSpeed <= 0f) return velocity;

            taut = true;
            return velocity - outward * outwardSpeed;
        }

        /// <summary>
        /// Speed at which the player is currently swinging: the tangential part of
        /// the velocity, with the radial part removed.
        /// </summary>
        public static float CalculateTangentialSpeed(
            Vector3 velocity,
            Vector3 anchorToPlayer)
        {
            return CalculateTangentialVelocity(velocity, anchorToPlayer).magnitude;
        }

        public static Vector3 CalculateTangentialVelocity(
            Vector3 velocity,
            Vector3 anchorToPlayer)
        {
            float distance = anchorToPlayer.magnitude;
            if (distance <= 0.0001f) return velocity;

            Vector3 outward = anchorToPlayer / distance;
            return velocity - outward * Vector3.Dot(velocity, outward);
        }

        /// <summary>
        /// Turns a movement input into thrust along the swing arc. Input that points
        /// at or away from the anchor does nothing, because a rope cannot be pushed
        /// or pulled along its own length; only the across-the-arc part drives the
        /// pendulum, which is what lets a player pump a swing higher.
        /// </summary>
        public static Vector3 CalculateSwingThrust(
            Vector3 desiredDirection,
            Vector3 anchorToPlayer,
            float acceleration)
        {
            if (acceleration <= 0f) return Vector3.zero;

            Vector3 tangential = CalculateTangentialVelocity(
                desiredDirection,
                anchorToPlayer);
            return tangential.sqrMagnitude <= 0.0001f
                ? Vector3.zero
                : tangential.normalized * acceleration;
        }

        /// <summary>
        /// One-shot impulse applied the instant the rope snaps taut, so the catch
        /// reads as a jolt instead of a gradual acceleration. Scaled by how hard the
        /// player was already moving away from the anchor: a lazy drift barely
        /// registers, while a fast fall snaps hard.
        /// </summary>
        public static Vector3 CalculateYankImpulse(
            Vector3 velocity,
            Vector3 anchorToPlayer,
            float yankStrength,
            float maximumYankSpeed)
        {
            float distance = anchorToPlayer.magnitude;
            if (distance <= 0.0001f || yankStrength <= 0f) return Vector3.zero;

            Vector3 inward = -anchorToPlayer / distance;
            float outwardSpeed = Vector3.Dot(velocity, -inward);
            if (outwardSpeed <= 0f) return Vector3.zero;

            return inward * Mathf.Min(
                outwardSpeed * yankStrength,
                Mathf.Max(0f, maximumYankSpeed));
        }

        /// <summary>
        /// Rope length after reeling. Reeling in is clamped so the player can never be
        /// pulled inside <paramref name="minimumLength"/>, and cannot exceed the
        /// distance the rope was first attached at.
        /// </summary>
        public static float ApplyReel(
            float ropeLength,
            float reelInput,
            float reelSpeed,
            float deltaTime,
            float minimumLength,
            float maximumLength)
        {
            float lower = Mathf.Max(0.1f, minimumLength);
            float upper = Mathf.Max(lower, maximumLength);
            if (Mathf.Abs(reelInput) <= 0.0001f)
                return Mathf.Clamp(ropeLength, lower, upper);

            return Mathf.Clamp(
                ropeLength - reelInput * Mathf.Max(0f, reelSpeed) * deltaTime,
                lower,
                upper);
        }
    }
}
