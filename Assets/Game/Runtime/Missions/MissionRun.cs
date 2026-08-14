using System;
using System.Collections.Generic;

namespace Supernova.Missions
{
    public enum MissionOutcome { None, Success, LostInCaves, Fired }

    public sealed class MissionCampaignProgress
    {
        private readonly IReadOnlyList<LevelConfiguration> levels;
        private int currentIndex;

        public MissionCampaignProgress(
            IReadOnlyList<LevelConfiguration> orderedLevels,
            LevelConfiguration startingLevel)
        {
            levels = orderedLevels;
            currentIndex = FindLevelIndex(startingLevel);
            if (currentIndex < 0 && levels != null && levels.Count > 0)
                currentIndex = 0;
        }

        public LevelConfiguration CurrentLevel =>
            levels != null
            && currentIndex >= 0
            && currentIndex < levels.Count
                ? levels[currentIndex]
                : null;
        public bool IsComplete { get; private set; }

        public bool SelectLevel(LevelConfiguration level)
        {
            int index = FindLevelIndex(level);
            if (index < 0)
                return false;

            currentIndex = index;
            IsComplete = false;
            return true;
        }

        public bool RecordOutcome(MissionOutcome outcome)
        {
            if (outcome != MissionOutcome.Success
                || IsComplete
                || CurrentLevel == null)
            {
                return false;
            }

            if (currentIndex + 1 < levels.Count)
            {
                currentIndex++;
                return true;
            }

            IsComplete = true;
            return false;
        }

        private int FindLevelIndex(LevelConfiguration level)
        {
            if (level == null || levels == null)
                return -1;

            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i] == level)
                    return i;
            }
            return -1;
        }
    }

    public sealed class MissionRun
    {
        public MissionRun(float timeLimitSeconds, int requiredValue)
        {
            TimeRemaining = Math.Max(0f, timeLimitSeconds);
            RequiredValue = Math.Max(1, requiredValue);
        }

        public float TimeRemaining { get; private set; }
        public int RequiredValue { get; }
        public int DeliveredValue { get; private set; }
        public MissionOutcome Outcome { get; private set; }
        public bool IsCountdownActive => !IsFinished && TimeRemaining > 0f;
        public bool IsEvacuationCountdownActive => IsCountdownActive;
        public bool IsFinished => Outcome != MissionOutcome.None;
        public int ExcessValue => Outcome == MissionOutcome.Success
            ? Math.Max(0, DeliveredValue - RequiredValue) : 0;

        public void AddDeliveredValue(int value)
        {
            if (!IsFinished) DeliveredValue += Math.Max(0, value);
        }

        public void Tick(float deltaTime)
        {
            Tick(deltaTime, 0);
        }

        public void Tick(float deltaTime, int extractionStoredValue)
        {
            if (IsFinished) return;
            TimeRemaining = Math.Max(0f, TimeRemaining - Math.Max(0f, deltaTime));
            if (TimeRemaining > 0f) return;

            DeliveredValue += Math.Max(0, extractionStoredValue);
            Outcome = DeliveredValue >= RequiredValue
                ? MissionOutcome.Success
                : MissionOutcome.Fired;
        }

        public bool TryStartEvacuationCountdown(int extractionStoredValue)
        {
            // Timed missions evacuate automatically when the mission clock ends.
            return false;
        }
    }
}
