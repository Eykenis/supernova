using NUnit.Framework;
using Supernova.Effects;
using Supernova.Gameplay;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class RigidbodyImpactFeedbackTests
    {
        private GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EffectiveSpecificImpulse_UsesLighterDynamicBody()
        {
            float strength =
                RigidbodyImpactFeedback.CalculateEffectiveSpecificImpulse(
                    12f,
                    10f,
                    2f);

            Assert.That(strength, Is.EqualTo(6f).Within(0.0001f));
        }

        [Test]
        public void NormalizedStrength_UsesThresholdAndClampsAtFullStrength()
        {
            Assert.That(
                RigidbodyImpactFeedback.CalculateNormalizedStrength(0.59f),
                Is.Zero);
            Assert.That(
                RigidbodyImpactFeedback.CalculateNormalizedStrength(0.6f),
                Is.EqualTo(RigidbodyImpactFeedback.MinimumVisibleStrength)
                    .Within(0.0001f));
            Assert.That(
                RigidbodyImpactFeedback.CalculateNormalizedStrength(6f),
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                RigidbodyImpactFeedback.CalculateNormalizedStrength(60f),
                Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void Ensure_AddsExactlyOneFeedbackComponent()
        {
            root = new GameObject("Impact Body");
            Rigidbody body = root.AddComponent<Rigidbody>();

            RigidbodyImpactFeedback first =
                RigidbodyImpactFeedback.Ensure(body);
            RigidbodyImpactFeedback second =
                RigidbodyImpactFeedback.Ensure(body);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first));
            Assert.That(
                root.GetComponents<RigidbodyImpactFeedback>(),
                Has.Length.EqualTo(1));
        }

        [Test]
        public void ValuableObject_AutomaticallyIncludesImpactFeedback()
        {
            root = new GameObject("Valuable Impact Body");
            root.AddComponent<Rigidbody>();
            root.AddComponent<ValuableObject>();

            Assert.That(
                root.GetComponent<RigidbodyImpactFeedback>(),
                Is.Not.Null);
        }

        [Test]
        public void SmokeParticleCount_ScalesWithNormalizedStrength()
        {
            Assert.That(
                RigidbodyImpactSmokeEmitter.CalculateParticleCount(0f),
                Is.EqualTo(
                    RigidbodyImpactSmokeEmitter.MinimumParticlesPerBurst));
            Assert.That(
                RigidbodyImpactSmokeEmitter.CalculateParticleCount(0.5f),
                Is.EqualTo(9));
            Assert.That(
                RigidbodyImpactSmokeEmitter.CalculateParticleCount(1f),
                Is.EqualTo(
                    RigidbodyImpactSmokeEmitter.MaximumParticlesPerBurst));
        }

        [Test]
        public void SmokeParticles_UseVariedSizesDeceleratingFlightAndShortLifetime()
        {
            Assert.That(
                RigidbodyImpactSmokeEmitter.MinimumParticleSizeScale,
                Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(
                RigidbodyImpactSmokeEmitter.MaximumParticleSizeScale,
                Is.EqualTo(1.75f).Within(0.0001f));
            Assert.That(
                RigidbodyImpactSmokeEmitter.ParticleSpeedMultiplier,
                Is.EqualTo(2f).Within(0.0001f));
            Assert.That(
                RigidbodyImpactSmokeEmitter.ParticleLifetimeMultiplier,
                Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(
                RigidbodyImpactSmokeEmitter.ParticleDrag,
                Is.EqualTo(2.4f).Within(0.0001f));
        }

        [Test]
        public void FeedbackChannel_PublishesOneValidImpactRequest()
        {
            root = new GameObject("Published Impact Body");
            Rigidbody body = root.AddComponent<Rigidbody>();
            int requestCount = 0;
            RigidbodyImpactFeedbackRequest received = default;
            void Handle(RigidbodyImpactFeedbackRequest request)
            {
                requestCount++;
                received = request;
            }

            RigidbodyImpactFeedbackEvents.Requested += Handle;
            try
            {
                bool published = RigidbodyImpactFeedbackEvents.Publish(
                    new RigidbodyImpactFeedbackRequest(
                        body,
                        null,
                        null,
                        null,
                        new Vector3(1f, 2f, 3f),
                        Vector3.forward,
                        3f,
                        0.5f,
                        123));

                Assert.That(published, Is.True);
                Assert.That(requestCount, Is.EqualTo(1));
                Assert.That(received.SourceBody, Is.SameAs(body));
                Assert.That(received.Position, Is.EqualTo(
                    new Vector3(1f, 2f, 3f)));
                Assert.That(received.NormalizedStrength,
                    Is.EqualTo(0.5f).Within(0.0001f));
            }
            finally
            {
                RigidbodyImpactFeedbackEvents.Requested -= Handle;
            }
        }
    }
}
