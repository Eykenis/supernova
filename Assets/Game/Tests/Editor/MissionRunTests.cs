using NUnit.Framework;
using Supernova.Missions;

namespace Supernova.Tests
{
    public sealed class MissionRunTests
    {
        [Test]
        public void EvacuatingWithRequiredValue_SucceedsAndAwardsOnlyExcess()
        {
            var run = new MissionRun(60f, 100);
            run.AddDeliveredValue(135);
            run.Evacuate();
            Assert.That(run.Outcome, Is.EqualTo(MissionOutcome.Success));
            Assert.That(run.ExcessValue, Is.EqualTo(35));
        }

        [Test]
        public void EvacuatingBelowRequiredValue_FiresPlayer()
        {
            var run = new MissionRun(60f, 100);
            run.AddDeliveredValue(90);
            run.Evacuate();
            Assert.That(run.Outcome, Is.EqualTo(MissionOutcome.Fired));
            Assert.That(run.ExcessValue, Is.Zero);
        }

        [Test]
        public void TimeExpiry_AlwaysMeansLostInCaves()
        {
            var run = new MissionRun(5f, 100);
            run.AddDeliveredValue(150);
            run.Tick(5f);
            Assert.That(run.Outcome, Is.EqualTo(MissionOutcome.LostInCaves));
            Assert.That(run.ExcessValue, Is.Zero);
        }

        [Test]
        public void FinishedMission_IgnoresFurtherValueAndOutcomeChanges()
        {
            var run = new MissionRun(5f, 100);
            run.Tick(5f);
            run.AddDeliveredValue(1000);
            run.Evacuate();
            Assert.That(run.DeliveredValue, Is.Zero);
            Assert.That(run.Outcome, Is.EqualTo(MissionOutcome.LostInCaves));
        }
    }
}
