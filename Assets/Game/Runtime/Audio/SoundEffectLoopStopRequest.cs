using UnityEngine;

namespace Supernova.Audio
{
    /// <summary>
    /// Immutable request to stop a keyed loop immediately or after a fade-out.
    /// </summary>
    public readonly struct SoundEffectLoopStopRequest
    {
        public SoundEffectLoopStopRequest(
            int loopId,
            float fadeOutSeconds = 0f)
        {
            LoopId = loopId;
            FadeOutSeconds = Mathf.Max(0f, fadeOutSeconds);
        }

        public int LoopId { get; }
        public float FadeOutSeconds { get; }
        public bool IsValid => LoopId != 0;
    }
}
