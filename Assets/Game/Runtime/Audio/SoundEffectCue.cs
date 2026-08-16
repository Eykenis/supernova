using UnityEngine;
using UnityEngine.Audio;

namespace Supernova.Audio
{
    /// <summary>
    /// Configures one semantic sound effect. A cue can contain variations while callers
    /// only need to broadcast the cue and the world position where it occurred.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SoundEffectCue",
        menuName = "Supernova/Audio/Sound Effect Cue")]
    public sealed class SoundEffectCue : ScriptableObject
    {
        [SerializeField] private AudioClip[] clips;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [SerializeField] private Vector2 pitchRange = Vector2.one;
        [SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;
        [SerializeField, Min(0.01f)] private float minimumDistance = 1f;
        [SerializeField, Min(0.01f)] private float maximumDistance = 30f;
        [SerializeField] private AudioRolloffMode rolloffMode =
            AudioRolloffMode.Logarithmic;
        [SerializeField] private AudioMixerGroup output;

        public float SpatialBlend => Mathf.Clamp01(spatialBlend);
        public float MinimumDistance => Mathf.Max(0.01f, minimumDistance);
        public float MaximumDistance =>
            Mathf.Max(MinimumDistance, maximumDistance);
        public AudioRolloffMode RolloffMode => rolloffMode;
        public AudioMixerGroup Output => output;
        public float MaximumClipLength
        {
            get
            {
                float maximum = 0f;
                if (clips == null) return maximum;

                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] != null)
                        maximum = Mathf.Max(maximum, clips[i].length);
                }
                return maximum;
            }
        }

        public bool TrySelectClip(
            out AudioClip clip,
            out float selectedVolume,
            out float selectedPitch)
        {
            clip = null;
            selectedVolume = Mathf.Clamp01(volume);
            selectedPitch = 1f;
            if (clips == null || clips.Length == 0 || selectedVolume <= 0f)
                return false;

            int startIndex = Random.Range(0, clips.Length);
            for (int offset = 0; offset < clips.Length; offset++)
            {
                int index = (startIndex + offset) % clips.Length;
                if (clips[index] == null) continue;
                clip = clips[index];
                break;
            }

            if (clip == null) return false;

            float minimumPitch = Mathf.Clamp(
                Mathf.Min(pitchRange.x, pitchRange.y),
                -3f,
                3f);
            float maximumPitch = Mathf.Clamp(
                Mathf.Max(pitchRange.x, pitchRange.y),
                -3f,
                3f);
            selectedPitch = Mathf.Approximately(minimumPitch, maximumPitch)
                ? minimumPitch
                : Random.Range(minimumPitch, maximumPitch);
            return true;
        }
    }
}
