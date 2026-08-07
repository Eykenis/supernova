using System;

namespace Supernova.Missions
{
    public enum MissionOutcome { None, Success, LostInCaves, Fired }

    public sealed class MissionRun
    {
        public MissionRun(float evacuationCountdownSeconds, int requiredValue)
        {
            TimeRemaining = Math.Max(0f, evacuationCountdownSeconds);
            RequiredValue = Math.Max(1, requiredValue);
        }

        public float TimeRemaining { get; private set; }
        public int RequiredValue { get; }
        public int DeliveredValue { get; private set; }
        public MissionOutcome Outcome { get; private set; }
        public bool IsEvacuationCountdownActive { get; private set; }
        public bool IsFinished => Outcome != MissionOutcome.None;
        public int ExcessValue => Outcome == MissionOutcome.Success
            ? Math.Max(0, DeliveredValue - RequiredValue) : 0;

        public void AddDeliveredValue(int value)
        {
            if (!IsFinished) DeliveredValue += Math.Max(0, value);
        }

        public void Tick(float deltaTime)
        {
            if (IsFinished || !IsEvacuationCountdownActive) return;
            TimeRemaining = Math.Max(0f, TimeRemaining - Math.Max(0f, deltaTime));
            if (TimeRemaining <= 0f) Outcome = MissionOutcome.Success;
        }

        public bool TryStartEvacuationCountdown(int extractionStoredValue)
        {
            if (IsFinished || IsEvacuationCountdownActive) return false;

            // Direct deliveries are already banked in DeliveredValue. The Cell
            // contributes its live overlap tally when evacuation begins.
            int available = DeliveredValue + Math.Max(0, extractionStoredValue);
            if (available < RequiredValue) return false;

            DeliveredValue = available;
            IsEvacuationCountdownActive = true;
            return true;
        }
    }
}
