using UnityEngine;

namespace Supernova.Audio
{
    /// <summary>
    /// Immutable value broadcast by gameplay code when a sound effect should play.
    /// </summary>
    public readonly struct SoundEffectPlaybackRequest
    {
        public SoundEffectPlaybackRequest(
            SoundEffectCue cue,
            Vector3 position,
            float volumeScale = 1f)
        {
            Cue = cue;
            Position = position;
            VolumeScale = Mathf.Max(0f, volumeScale);
        }

        public SoundEffectCue Cue { get; }
        public Vector3 Position { get; }
        public float VolumeScale { get; }
        public bool IsValid => Cue != null && VolumeScale > 0f;
    }
}
