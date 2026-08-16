using UnityEngine;

namespace Supernova.Audio
{
    /// <summary>
    /// Immutable request for a keyed sound-effect loop that follows a Transform.
    /// Broadcasting the same loop id again updates its cue, volume, and pitch.
    /// </summary>
    public readonly struct SoundEffectLoopRequest
    {
        public SoundEffectLoopRequest(
            int loopId,
            SoundEffectCue cue,
            Transform followTarget,
            float volumeScale = 1f,
            float pitchScale = 1f,
            float fadeInSeconds = 0f)
        {
            LoopId = loopId;
            Cue = cue;
            FollowTarget = followTarget;
            VolumeScale = Mathf.Max(0f, volumeScale);
            PitchScale = Mathf.Max(0.01f, pitchScale);
            FadeInSeconds = Mathf.Max(0f, fadeInSeconds);
        }

        public int LoopId { get; }
        public SoundEffectCue Cue { get; }
        public Transform FollowTarget { get; }
        public float VolumeScale { get; }
        public float PitchScale { get; }
        public float FadeInSeconds { get; }
        public bool IsValid => LoopId != 0
            && Cue != null
            && FollowTarget != null
            && VolumeScale > 0f;
    }
}
