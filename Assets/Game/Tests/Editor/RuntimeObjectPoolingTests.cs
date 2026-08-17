using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.MinecraftCaves.Creatures;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class RuntimeObjectPoolingTests
    {
        private GameObject testObject;

        [TearDown]
        public void TearDown()
        {
            if (testObject != null)
            {
                Object.DestroyImmediate(testObject);
            }
        }

        [Test]
        public void TreasurePickup_ReusesLateAddedPresentationAndPhysics()
        {
            testObject = new GameObject("Pooled Treasure Test");
            TreasurePickup pickup =
                testObject.AddComponent<TreasurePickup>();
            BoxCollider collider = testObject.AddComponent<BoxCollider>();
            MeshRenderer renderer = testObject.AddComponent<MeshRenderer>();
            Rigidbody body = testObject.GetComponent<Rigidbody>();

            body.detectCollisions = true;
            body.useGravity = true;
            body.isKinematic = false;
            pickup.PrepareForReuse();

            collider.enabled = false;
            renderer.enabled = false;
            body.detectCollisions = false;
            body.useGravity = false;
            body.isKinematic = true;
            pickup.PrepareForPool();
            pickup.PrepareForReuse();

            Assert.That(collider.enabled, Is.True);
            Assert.That(renderer.enabled, Is.True);
            Assert.That(body.detectCollisions, Is.True);
            Assert.That(body.useGravity, Is.True);
            Assert.That(body.isKinematic, Is.False);
        }

        [Test]
        public void CreatureBehaviorAgent_ReusesHealthAndPhysicsAfterDeath()
        {
            testObject = new GameObject("Pooled Monster Test");
            Rigidbody body = testObject.AddComponent<Rigidbody>();
            CapsuleCollider collider =
                testObject.AddComponent<CapsuleCollider>();
            CreatureBehaviorAgent agent =
                testObject.AddComponent<CreatureBehaviorAgent>();

            agent.ReceiveDamage(new DamageInfo(
                agent.MaximumHealth + 1f,
                null,
                testObject.transform.position,
                Vector3.forward));

            Assert.That(agent.IsAlive, Is.False);
            Assert.That(collider.enabled, Is.False);
            Assert.That(body.detectCollisions, Is.False);

            agent.PrepareForPool();
            agent.PrepareForReuse(null, null);

            Assert.That(agent.IsAlive, Is.True);
            Assert.That(
                agent.CurrentHealth,
                Is.EqualTo(agent.MaximumHealth));
            Assert.That(collider.enabled, Is.True);
            Assert.That(body.detectCollisions, Is.True);
            Assert.That(body.useGravity, Is.True);
            Assert.That(body.isKinematic, Is.False);
            Assert.That(
                testObject.GetComponent<CreaturePhysicsMotor>().enabled,
                Is.True);
        }
    }
}
