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

        public bool TryStartEvacuationCountdown(int deliveredValue)
        {
            if (IsFinished || IsEvacuationCountdownActive) return false;

            int availableValue = Math.Max(0, deliveredValue);
            if (availableValue < RequiredValue) return false;

            DeliveredValue = availableValue;
            IsEvacuationCountdownActive = true;
            return true;
        }
    }
}
