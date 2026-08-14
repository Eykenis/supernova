using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Tests
{
    /// <summary>
    /// The rope's job is to feel like a rope, so these tests are written against the
    /// properties that produce that feel: tangential motion survives, radial motion
    /// does not, and a slack rope does nothing at all.
    /// </summary>
    public sealed class RopeConstraintTests
    {
        [Test]
        public void PositionCorrection_PullsThePlayerBackOntoTheSphere()
        {
            // Only needed when over-extended.
            Assert.That(
                RopeConstraint.CalculatePositionCorrection(
                    new Vector3(0f, -5f, 0f),
                    8f),
                Is.EqualTo(Vector3.zero),
                "A slack rope must not move the player.");

            // Stretched half a metre past an eight metre rope.
            Vector3 overExtended = new Vector3(0f, -8.5f, 0f);
            Vector3 correction = RopeConstraint.CalculatePositionCorrection(
                overExtended,
                8f);
            Assert.That(
                (overExtended + correction).magnitude,
                Is.EqualTo(8f).Within(0.0001f),
                "The correction must land exactly on the rope length.");
            // It pulls inward, along the rope.
            Assert.That(correction.y, Is.GreaterThan(0f));
            Assert.That(correction.magnitude, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void HangingOnARope_DoesNotStretchOrJitter()
        {
            // Regression: constraining velocity alone let gravity leak a sub-frame of
            // displacement every frame. The rope crept longer and the corrective
            // velocity overshot, which read as the player bobbing up and down.
            const float length = 6f;
            const float deltaTime = 1f / 60f;
            Vector3 anchor = Vector3.zero;
            Vector3 position = new Vector3(0f, -length, 0f);
            Vector3 velocity = Vector3.zero;

            float worstError = 0f;
            for (int i = 0; i < 180; i++)
            {
                velocity += Vector3.down * 20f * deltaTime;
                velocity = RopeConstraint.ApplyTautConstraint(
                    velocity,
                    position - anchor,
                    length,
                    out _);
                position += velocity * deltaTime;
                position += RopeConstraint.CalculatePositionCorrection(
                    position - anchor,
                    length);

                worstError = Mathf.Max(
                    worstError,
                    Mathf.Abs((position - anchor).magnitude - length));
            }

            Assert.That(
                worstError,
                Is.LessThan(0.001f),
                "Hanging still must hold the rope length exactly.");
        }

        [Test]
        public void HittingAWall_AbsorbsMomentumInsteadOfStickingToIt()
        {
            // Regression: CollisionFlags.Sides was never handled, so momentum kept
            // pressing into a wall after impact and the player stuck to it.
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject playerObject = new GameObject("Player");
            try
            {
                wall.transform.position = new Vector3(0f, 0f, 8f);
                wall.transform.localScale = new Vector3(40f, 40f, 1f);
                CharacterController controller =
                    playerObject.AddComponent<CharacterController>();
                controller.height = 1.8f;
                controller.radius = 0.3f;
                Physics.SyncTransforms();

                VoxelPlayerController player =
                    playerObject.AddComponent<VoxelPlayerController>();
                player.AddExternalVelocity(new Vector3(0f, 0f, 28f), 34f);

                // Charge the wall until the controller stops making progress.
                float previousZ = float.NegativeInfinity;
                for (int i = 0; i < 60; i++)
                {
                    player.StepMotor(Vector3.zero, 1f / 60f);
                    float z = playerObject.transform.position.z;
                    if (i > 20 && Mathf.Abs(z - previousZ) < 0.0001f) break;
                    previousZ = z;
                }

                Assert.That(
                    playerObject.transform.position.z,
                    Is.GreaterThan(6f),
                    "The player has to actually reach the wall.");
                Assert.That(
                    player.CombinedVelocity.z,
                    Is.LessThan(1f),
                    "Momentum into the wall must be absorbed, not held.");
            }
            finally
            {
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        public void GlancingOffAWall_KeepsTheAlongWallMomentum()
        {
            // Only the into-surface component is absorbed, so a glancing impact slides
            // along the wall rather than stopping dead.
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject playerObject = new GameObject("Player");
            try
            {
                wall.transform.position = new Vector3(0f, 0f, 4f);
                wall.transform.localScale = new Vector3(60f, 40f, 1f);
                CharacterController controller =
                    playerObject.AddComponent<CharacterController>();
                controller.height = 1.8f;
                controller.radius = 0.3f;
                Physics.SyncTransforms();

                VoxelPlayerController player =
                    playerObject.AddComponent<VoxelPlayerController>();
                // Mostly sideways, partly into the wall.
                player.AddExternalVelocity(new Vector3(20f, 0f, 14f), 34f);

                for (int i = 0; i < 40; i++)
                    player.StepMotor(Vector3.zero, 1f / 60f);

                Assert.That(
                    player.CombinedVelocity.z,
                    Is.LessThan(1f),
                    "Into-wall momentum is absorbed.");
                Assert.That(
                    player.CombinedVelocity.x,
                    Is.GreaterThan(8f),
                    "Along-wall momentum must survive so the player slides.");
                Assert.That(
                    playerObject.transform.position.x,
                    Is.GreaterThan(5f),
                    "And that momentum must actually move them along the wall.");
            }
            finally
            {
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        public void StandingOnGround_DoesNotAbsorbHorizontalMomentum()
        {
            // The blocked-displacement test must ignore vertical blocking, or simply
            // resting on a floor would eat a push the instant it was applied.
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject playerObject = new GameObject("Player");
            try
            {
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                floor.transform.localScale = new Vector3(200f, 1f, 200f);
                playerObject.transform.position = new Vector3(0f, 0.95f, 0f);
                CharacterController controller =
                    playerObject.AddComponent<CharacterController>();
                controller.height = 1.8f;
                controller.radius = 0.3f;
                Physics.SyncTransforms();

                VoxelPlayerController player =
                    playerObject.AddComponent<VoxelPlayerController>();
                for (int i = 0; i < 10; i++)
                    player.StepMotor(Vector3.zero, 1f / 60f);

                player.AddExternalVelocity(new Vector3(0f, 0f, 12f), 34f);
                player.StepMotor(Vector3.zero, 1f / 60f);

                Assert.That(
                    player.CombinedVelocity.z,
                    Is.GreaterThan(8f),
                    "Resting on the floor must not absorb a horizontal push.");
            }
            finally
            {
                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        public void ExternalVelocity_DecaysSoAPullDoesNotDriftForever()
        {
            // Regression: nothing used to reduce externalVelocity, so any momentum
            // handed to the motor kept pushing the player after the rope was released.
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject playerObject = new GameObject("Player");
            try
            {
                floor.transform.position = new Vector3(0f, -0.5f, 0f);
                floor.transform.localScale = new Vector3(60f, 1f, 60f);
                playerObject.transform.position = new Vector3(0f, 0.95f, 0f);
                CharacterController controller =
                    playerObject.AddComponent<CharacterController>();
                controller.height = 1.8f;
                controller.radius = 0.3f;
                Physics.SyncTransforms();

                VoxelPlayerController player =
                    playerObject.AddComponent<VoxelPlayerController>();

                player.AddExternalVelocity(new Vector3(0f, 0f, 10f), 34f);
                Assert.That(
                    player.CombinedVelocity.z,
                    Is.GreaterThan(5f),
                    "The push has to register in the first place.");

                // Standing on the ground, the push must be scrubbed off quickly.
                for (int i = 0; i < 120; i++)
                    player.StepMotor(Vector3.zero, 1f / 60f);

                Assert.That(
                    new Vector2(
                        player.CombinedVelocity.x,
                        player.CombinedVelocity.z).magnitude,
                    Is.LessThan(0.1f),
                    "Grounded external momentum must decay to a stop.");
            }
            finally
            {
                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        public void SlackRope_LeavesVelocityCompletelyUntouched()
        {
            Vector3 velocity = new Vector3(3f, -9f, 1f);

            Vector3 result = RopeConstraint.ApplyTautConstraint(
                velocity,
                // Five metres out on an eight metre rope.
                new Vector3(0f, -5f, 0f),
                8f,
                out bool taut);

            Assert.That(taut, Is.False);
            Assert.That(result, Is.EqualTo(velocity));
        }

        [Test]
        public void TautRope_CancelsOutwardRadialButKeepsTangential()
        {
            // Anchor above, player hanging below, moving sideways and downward.
            Vector3 anchorToPlayer = new Vector3(0f, -8f, 0f);
            Vector3 velocity = new Vector3(6f, -4f, 0f);

            Vector3 result = RopeConstraint.ApplyTautConstraint(
                velocity,
                anchorToPlayer,
                8f,
                out bool taut);

            Assert.That(taut, Is.True);
            // The downward (outward) part is gone.
            Assert.That(result.y, Is.EqualTo(0f).Within(0.0001f));
            // The sideways (tangential) part is untouched: this is the swing.
            Assert.That(result.x, Is.EqualTo(6f).Within(0.0001f));
        }

        [Test]
        public void TautRope_DoesNotInterfereWhenMovingInward()
        {
            // Swinging back towards the anchor must not be damped, or the pendulum
            // would lose all its energy at the bottom of every arc.
            Vector3 velocity = new Vector3(2f, 5f, 0f);

            Vector3 result = RopeConstraint.ApplyTautConstraint(
                velocity,
                new Vector3(0f, -8f, 0f),
                8f,
                out bool taut);

            Assert.That(taut, Is.False);
            Assert.That(result, Is.EqualTo(velocity));
        }

        [Test]
        public void Rope_ProducesAPendulumRatherThanReelingThePlayerIn()
        {
            // Integrate the constraint under gravity from a horizontal start. A rope
            // must swing through the low point and rise on the far side; a constant
            // pull towards the anchor would instead close the distance.
            Vector3 anchor = Vector3.zero;
            const float length = 8f;
            const float deltaTime = 1f / 60f;
            Vector3 position = anchor + new Vector3(length, 0f, 0f);
            Vector3 velocity = Vector3.zero;

            float lowestY = 0f;
            float furthestNegativeX = 0f;
            float maximumSpeed = 0f;
            for (int i = 0; i < 120; i++)
            {
                velocity += Vector3.down * 20f * deltaTime;
                velocity = RopeConstraint.ApplyTautConstraint(
                    velocity,
                    position - anchor,
                    length,
                    out _);
                position += velocity * deltaTime;

                // A real rope also corrects position; without this the radius drifts.
                Vector3 offset = position - anchor;
                if (offset.magnitude > length)
                    position = anchor + offset.normalized * length;

                lowestY = Mathf.Min(lowestY, position.y);
                furthestNegativeX = Mathf.Min(furthestNegativeX, position.x);
                maximumSpeed = Mathf.Max(maximumSpeed, velocity.magnitude);
            }

            // It reached the bottom of the arc.
            Assert.That(lowestY, Is.LessThan(-length * 0.9f));
            // And carried through to the opposite side, which only a swing does.
            Assert.That(furthestNegativeX, Is.LessThan(-length * 0.5f));
            // Potential energy became speed rather than being absorbed.
            Assert.That(maximumSpeed, Is.GreaterThan(10f));
            // The rope never stretched.
            Assert.That(
                (position - anchor).magnitude,
                Is.LessThanOrEqualTo(length + 0.001f));
        }

        [Test]
        public void SwingThrust_OnlyActsAlongTheArc()
        {
            // Rope hanging straight down from the anchor.
            Vector3 anchorToPlayer = new Vector3(0f, -8f, 0f);

            // Pushing across the rope drives the swing at full strength.
            Vector3 across = RopeConstraint.CalculateSwingThrust(
                Vector3.forward,
                anchorToPlayer,
                26f);
            Assert.That(across.magnitude, Is.EqualTo(26f).Within(0.001f));
            Assert.That(across.z, Is.GreaterThan(0f));

            // Pushing along the rope does nothing: a rope cannot be pushed or pulled
            // along its own length.
            Assert.That(
                RopeConstraint.CalculateSwingThrust(
                    Vector3.down,
                    anchorToPlayer,
                    26f),
                Is.EqualTo(Vector3.zero));
            Assert.That(
                RopeConstraint.CalculateSwingThrust(
                    Vector3.up,
                    anchorToPlayer,
                    26f),
                Is.EqualTo(Vector3.zero));

            // A diagonal push keeps only its across-the-arc part.
            Vector3 diagonal = RopeConstraint.CalculateSwingThrust(
                new Vector3(1f, -1f, 0f).normalized,
                anchorToPlayer,
                26f);
            Assert.That(diagonal.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(diagonal.x, Is.EqualTo(26f).Within(0.001f));
        }

        [Test]
        public void YankImpulse_ScalesWithOutwardSpeedAndIsCapped()
        {
            Vector3 anchorToPlayer = new Vector3(0f, -8f, 0f);

            // A slow drift barely registers.
            Vector3 gentle = RopeConstraint.CalculateYankImpulse(
                new Vector3(0f, -5f, 0f),
                anchorToPlayer,
                0.35f,
                7f);
            Assert.That(gentle.y, Is.EqualTo(1.75f).Within(0.001f));

            // A fast fall snaps hard, but never past the cap.
            Vector3 hard = RopeConstraint.CalculateYankImpulse(
                new Vector3(0f, -25f, 0f),
                anchorToPlayer,
                0.35f,
                7f);
            Assert.That(hard.y, Is.EqualTo(7f).Within(0.001f));

            // Already moving towards the anchor: nothing to arrest.
            Assert.That(
                RopeConstraint.CalculateYankImpulse(
                    new Vector3(0f, 5f, 0f),
                    anchorToPlayer,
                    0.35f,
                    7f),
                Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Reel_MovesTheLengthAndRespectsBothBounds()
        {
            // Positive input reels in, negative pays out.
            Assert.That(
                RopeConstraint.ApplyReel(10f, 2f, 1f, 1f, 2.5f, 25f),
                Is.EqualTo(8f).Within(0.001f));
            Assert.That(
                RopeConstraint.ApplyReel(10f, -2f, 1f, 1f, 2.5f, 25f),
                Is.EqualTo(12f).Within(0.001f));

            // The player can never be winched into the anchor.
            Assert.That(
                RopeConstraint.ApplyReel(3f, 5f, 1f, 1f, 2.5f, 25f),
                Is.EqualTo(2.5f).Within(0.001f));
            // Nor pay out past the maximum.
            Assert.That(
                RopeConstraint.ApplyReel(24f, -5f, 1f, 1f, 2.5f, 25f),
                Is.EqualTo(25f).Within(0.001f));
        }

        [Test]
        public void TangentialSpeed_ExcludesTheRadialComponent()
        {
            // Straight down the rope is entirely radial: no swing at all.
            Assert.That(
                RopeConstraint.CalculateTangentialSpeed(
                    new Vector3(0f, -10f, 0f),
                    new Vector3(0f, -8f, 0f)),
                Is.EqualTo(0f).Within(0.001f));

            // Purely sideways is entirely swing.
            Assert.That(
                RopeConstraint.CalculateTangentialSpeed(
                    new Vector3(7f, 0f, 0f),
                    new Vector3(0f, -8f, 0f)),
                Is.EqualTo(7f).Within(0.001f));
        }
    }
}
