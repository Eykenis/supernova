using System;
using UnityEngine;

namespace Supernova.Audio
{
    /// <summary>
    /// Observer channel used by gameplay and UI actions to request sound playback
    /// without knowing which object owns the AudioSources.
    /// </summary>
    public static class SoundEffectEvents
    {
        public static event Action<SoundEffectPlaybackRequest>
            PlaybackRequested;
        public static event Action<SoundEffectLoopRequest>
            LoopRequested;
        public static event Action<SoundEffectLoopStopRequest>
            LoopStopRequested;

        private static int nextLoopId;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            PlaybackRequested = null;
            LoopRequested = null;
            LoopStopRequested = null;
            nextLoopId = 0;
        }

        public static int CreateLoopId()
        {
            unchecked
            {
                nextLoopId++;
                if (nextLoopId == 0) nextLoopId++;
                return nextLoopId;
            }
        }

        public static bool RequestPlay(
            SoundEffectCue cue,
            Vector3 position,
            float volumeScale = 1f)
        {
            return Publish(
                new SoundEffectPlaybackRequest(
                    cue,
                    position,
                    volumeScale));
        }

        public static bool Publish(SoundEffectPlaybackRequest request)
        {
            if (!request.IsValid) return false;

            PlaybackRequested?.Invoke(request);
            return true;
        }

        public static bool RequestLoop(
            int loopId,
            SoundEffectCue cue,
            Transform followTarget,
            float volumeScale = 1f,
            float pitchScale = 1f,
            float fadeInSeconds = 0f)
        {
            return PublishLoop(
                new SoundEffectLoopRequest(
                    loopId,
                    cue,
                    followTarget,
                    volumeScale,
                    pitchScale,
                    fadeInSeconds));
        }

        public static bool PublishLoop(SoundEffectLoopRequest request)
        {
            if (!request.IsValid) return false;

            LoopRequested?.Invoke(request);
            return true;
        }

        public static bool RequestStopLoop(
            int loopId,
            float fadeOutSeconds = 0f)
        {
            SoundEffectLoopStopRequest request =
                new SoundEffectLoopStopRequest(
                    loopId,
                    fadeOutSeconds);
            if (!request.IsValid) return false;

            LoopStopRequested?.Invoke(request);
            return true;
        }
    }
}
