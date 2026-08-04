using NUnit.Framework;
using Supernova.Missions;

namespace Supernova.Tests
{
    public sealed class MissionRunTests
    {
        [Test]
        public void Countdown_DoesNotStartUntilEnoughValueIsSubmitted()
        {
            var run = new MissionRun(60f, 100);
            run.Tick(30f);

            Assert.That(run.TimeRemaining, Is.EqualTo(60f));
            Assert.That(run.IsEvacuationCountdownActive, Is.False);
            Assert.That(run.TryStartEvacuationCountdown(90), Is.False);
            Assert.That(run.IsEvacuationCountdownActive, Is.False);
        }

        [Test]
        public void EnoughValue_StartsCountdownAndCapturesDeliveredValue()
        {
            var run = new MissionRun(60f, 100);
            Assert.That(run.TryStartEvacuationCountdown(135), Is.True);

            Assert.That(run.IsEvacuationCountdownActive, Is.True);
            Assert.That(run.DeliveredValue, Is.EqualTo(135));
            Assert.That(run.Outcome, Is.EqualTo(MissionOutcome.None));
        }

        [Test]
        public void CountdownExpiry_CompletesEvacuationAndAwardsOnlyExcess()
        {
            var run = new MissionRun(5f, 100);
            run.TryStartEvacuationCountdown(135);
            run.Tick(5f);

            Assert.That(run.Outcome, Is.EqualTo(MissionOutcome.Success));
            Assert.That(run.ExcessValue, Is.EqualTo(35));
        }

        [Test]
        public void FinishedMission_IgnoresFurtherValueAndOutcomeChanges()
        {
            var run = new MissionRun(5f, 100);
            run.TryStartEvacuationCountdown(100);
            run.Tick(5f);
            run.AddDeliveredValue(1000);
            Assert.That(run.TryStartEvacuationCountdown(1000), Is.False);
            Assert.That(run.DeliveredValue, Is.EqualTo(100));
            Assert.That(run.Outcome, Is.EqualTo(MissionOutcome.Success));
        }
    }
}
