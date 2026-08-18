using NUnit.Framework;
using Supernova.Missions;

namespace Supernova.Tests
{
    public sealed class MissionRunTests
    {
        [Test]
        public void Countdown_StartsImmediatelyAndTicksWithoutManualEvacuation()
        {
            var run = new MissionRun(60f, 100);
            run.Tick(30f);

            Assert.That(run.TimeRemaining, Is.EqualTo(30f));
            Assert.That(run.IsCountdownActive, Is.True);
            Assert.That(run.IsEvacuationCountdownActive, Is.True);
        }

        [Test]
        public void ManualEvacuationCannotReplaceAutomaticMissionCountdown()
        {
            var run = new MissionRun(60f, 100);

            Assert.That(run.TryStartEvacuationCountdown(135), Is.False);
            Assert.That(run.TimeRemaining, Is.EqualTo(60f));
            Assert.That(run.DeliveredValue, Is.Zero);
            Assert.That(run.Outcome, Is.EqualTo(MissionOutcome.None));
        }

        [Test]
        public void EarlyEvacuation_RequiresEnoughValueAndFinishesImmediately()
        {
            var run = new MissionRun(60f, 100);

            Assert.That(run.TryEvacuateEarly(90), Is.False);
            Assert.That(run.IsFinished, Is.False);
            Assert.That(run.DeliveredValue, Is.Zero);

            Assert.That(run.TryEvacuateEarly(135), Is.True);
            Assert.That(run.IsFinished, Is.True);
            Assert.That(run.DeliveredValue, Is.EqualTo(135));
            Assert.That(run.Outcome, Is.EqualTo(MissionOutcome.Success));
            Assert.That(run.ExcessValue, Is.EqualTo(35));
        }

        [Test]
        public void CountdownExpiry_BanksCellValueAndAwardsOnlyExcess()
        {
            var run = new MissionRun(5f, 100);
            run.Tick(5f, 135);

            Assert.That(run.Outcome, Is.EqualTo(MissionOutcome.Success));
            Assert.That(run.DeliveredValue, Is.EqualTo(135));
            Assert.That(run.ExcessValue, Is.EqualTo(35));
        }

        [Test]
        public void CountdownExpiry_WithInsufficientValueStillEndsTheMission()
        {
            var run = new MissionRun(5f, 100);
            run.Tick(5f, 90);

            Assert.That(run.Outcome, Is.EqualTo(MissionOutcome.Fired));
            Assert.That(run.DeliveredValue, Is.EqualTo(90));
            Assert.That(run.IsFinished, Is.True);
        }

        [Test]
        public void FinishedMission_IgnoresFurtherValueAndOutcomeChanges()
        {
            var run = new MissionRun(5f, 100);
            run.Tick(5f, 100);
            run.AddDeliveredValue(1000);
            run.Tick(5f, 1000);
            Assert.That(run.DeliveredValue, Is.EqualTo(100));
            Assert.That(run.Outcome, Is.EqualTo(MissionOutcome.Success));
        }
    }
}
