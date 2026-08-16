using System.Reflection;
using NUnit.Framework;
using Supernova.MinecraftCaves.Creatures;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class CreatureNavigationAgentTests
    {
        private GameObject creatureObject;

        [TearDown]
        public void TearDown()
        {
            if (creatureObject != null)
            {
                Object.DestroyImmediate(creatureObject);
            }
        }

        /// <summary>
        /// Without a bound voxel terrain there is nothing to plan against, so the
        /// movement states must park the motor instead of steering blindly.
        /// </summary>
        [TestCase(CreatureBehaviorState.Wander, "TickWander")]
        [TestCase(CreatureBehaviorState.Pursue, "TickPursue")]
        public void MovementState_WithoutTerrain_StopsTheMotor(
            CreatureBehaviorState state,
            string tickMethodName)
        {
            creatureObject = new GameObject("Creature without terrain");
            creatureObject.AddComponent<Rigidbody>();
            CreaturePhysicsMotor motor =
                creatureObject.AddComponent<CreaturePhysicsMotor>();
            CreatureBehaviorAgent agent =
                creatureObject.AddComponent<CreatureBehaviorAgent>();

            MethodInfo setState = typeof(CreatureBehaviorAgent).GetMethod(
                "SetState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo tickState = typeof(CreatureBehaviorAgent).GetMethod(
                tickMethodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo hasFacing = typeof(CreaturePhysicsMotor).GetField(
                "hasFacing",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(setState, Is.Not.Null);
            Assert.That(tickState, Is.Not.Null);
            Assert.That(hasFacing, Is.Not.Null);

            setState.Invoke(agent, new object[] { state });
            motor.Face(Vector3.forward, Vector3.up);
            Assert.That(hasFacing.GetValue(motor), Is.True);

            tickState.Invoke(agent, new object[] { 0.016f });

            Assert.That(agent.CurrentState, Is.EqualTo(state));
            Assert.That(hasFacing.GetValue(motor), Is.False);
            Assert.That(agent.CurrentPath, Is.Null);
        }

        [Test]
        public void Agent_ExposesANavigationProfileWithUsableLimits()
        {
            creatureObject = new GameObject("Creature navigation profile");
            creatureObject.AddComponent<Rigidbody>();
            creatureObject.AddComponent<CreaturePhysicsMotor>();
            CreatureBehaviorAgent agent =
                creatureObject.AddComponent<CreatureBehaviorAgent>();

            Assert.That(agent.NavigationProfile, Is.Not.Null);
            Assert.That(
                agent.NavigationProfile.MaximumJumpHeight,
                Is.GreaterThan(0),
                "A ground creature must be able to climb at least one layer.");
            Assert.That(
                agent.NavigationProfile.MaximumSafeFall,
                Is.GreaterThanOrEqualTo(agent.NavigationProfile.MaximumJumpHeight));
            Assert.That(
                agent.NavigationProfile.VisitLimit,
                Is.GreaterThan(0),
                "The search needs a visit cap so a failed plan cannot stall a frame.");
            Assert.That(
                agent.NavigationProfile.MaximumReplanInterval,
                Is.GreaterThanOrEqualTo(
                    agent.NavigationProfile.MinimumReplanInterval));
            Assert.That(
                agent.NavigationProfile.StepUpHeight,
                Is.GreaterThan(0),
                "Small rises must be walked over, not jumped: interpolated terrain "
                    + "quantises into alternating voxel layers on flat ground.");
            Assert.That(
                agent.NavigationProfile.StepUpHeight,
                Is.LessThanOrEqualTo(agent.NavigationProfile.MaximumJumpHeight));
            Assert.That(
                agent.NavigationProfile.StuckCheckInterval,
                Is.GreaterThan(0f),
                "A zero interval would fire a recovery jump every frame.");
        }

        [Test]
        public void Motor_ResolvesJumpSpeedFromTheRequestedHeight()
        {
            // Reaching a height under scene gravity needs sqrt(2 * g * h) before
            // the surface-mismatch multiplier is applied.
            float expected = Mathf.Sqrt(
                2f * Mathf.Abs(Physics.gravity.y) * 0.42f);

            float resolved = CreaturePhysicsMotor.ResolveJumpSpeed(
                0.42f,
                Vector3.up,
                1f);

            Assert.That(resolved, Is.EqualTo(expected).Within(0.0001f));
            Assert.That(
                CreaturePhysicsMotor.ResolveJumpSpeed(0.42f, Vector3.up, 1.15f),
                Is.GreaterThan(resolved),
                "The multiplier must raise take-off speed, never lower it.");
        }

        [Test]
        public void Motor_RepeatingAJumpCommandDoesNotQueueASecondTakeOff()
        {
            CreaturePhysicsMotor motor = CreateMotor("Creature repeat jump");
            FieldInfo hasPendingJump = PrivateField("hasPendingJump");
            FieldInfo lastFired = PrivateField("lastFiredJumpCommandId");

            motor.RequestJump(0.42f, 1234);
            Assert.That(hasPendingJump.GetValue(motor), Is.True);

            // Stand in for the impulse firing, which FixedUpdate does once grounded.
            hasPendingJump.SetValue(motor, false);
            lastFired.SetValue(motor, 1234);

            // The agent re-issues the same command every frame while the climb edge
            // is still the next step. That must not re-arm the jump, otherwise the
            // creature takes off again the instant it lands.
            motor.RequestJump(0.42f, 1234);

            Assert.That(
                hasPendingJump.GetValue(motor),
                Is.False,
                "Repeating one command identifier must not queue another jump.");
        }

        [Test]
        public void Motor_FirstJumpRequestIsNotSwallowedByTheInitialIdentifier()
        {
            CreaturePhysicsMotor motor = CreateMotor("Creature first jump");

            // A node-derived identifier can legitimately be zero, so the sentinel
            // for "already fired" must not be zero.
            motor.RequestJump(0.42f, 0);

            Assert.That(PrivateField("hasPendingJump").GetValue(motor), Is.True);
        }

        [Test]
        public void Motor_StopDropsAQueuedJump()
        {
            CreaturePhysicsMotor motor = CreateMotor("Creature stop jump");
            FieldInfo hasPendingJump = PrivateField("hasPendingJump");

            motor.RequestJump(0.42f, 99);
            Assert.That(hasPendingJump.GetValue(motor), Is.True);

            motor.Stop();

            Assert.That(
                hasPendingJump.GetValue(motor),
                Is.False,
                "Leaving a movement state must not leave a jump armed.");
        }

        [Test]
        public void Motor_CommandedSpeedFractionUsesTheCommandedSpeedNotAnimation()
        {
            CreaturePhysicsMotor motor = CreateMotor("Creature speed metric");
            FieldInfo animationReference = PrivateField("animationReferenceSpeed");

            // Deliberately mismatch the animation reference and the commanded
            // speed, which is the real configuration: the reference is a
            // presentation value while navigation commands metres per second.
            animationReference.SetValue(motor, 1.26f);
            motor.MoveTowards(Vector3.forward, Vector3.up, 1.008f, 18f);

            Assert.That(
                motor.CommandedSpeed,
                Is.EqualTo(1.008f).Within(0.0001f));
            Assert.That(
                motor.CommandedSpeedFraction,
                Is.Zero.Within(0.0001f),
                "A body at rest is zero fraction of its commanded speed.");

            motor.Stop();

            Assert.That(
                motor.CommandedSpeed,
                Is.Zero,
                "A parked creature commands no speed, so it cannot read as blocked.");
            Assert.That(motor.CommandedSpeedFraction, Is.Zero);
        }

        [Test]
        public void Motor_StopClearsBothFacingAndMovement()
        {
            CreaturePhysicsMotor motor = CreateMotor("Creature motor stop");
            FieldInfo hasMoveCommand = PrivateField("hasMoveCommand");

            motor.MoveTowards(Vector3.forward, Vector3.up, 2f, 18f);
            Assert.That(hasMoveCommand.GetValue(motor), Is.True);

            motor.Stop();

            Assert.That(hasMoveCommand.GetValue(motor), Is.False);
        }

        private CreaturePhysicsMotor CreateMotor(string name)
        {
            creatureObject = new GameObject(name);
            creatureObject.AddComponent<Rigidbody>();
            return creatureObject.AddComponent<CreaturePhysicsMotor>();
        }

        private static FieldInfo PrivateField(string name)
        {
            FieldInfo field = typeof(CreaturePhysicsMotor).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name + " is expected on the motor.");
            return field;
        }
    }
}
