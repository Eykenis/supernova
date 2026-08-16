using System.Collections.Generic;
using UnityEngine;

namespace Supernova.Audio
{
    /// <summary>
    /// Persistent observer that turns sound-effect requests into pooled AudioSource
    /// playback. Gameplay code communicates with this component through
    /// SoundEffectEvents instead of locating the manager directly.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        private const int DefaultMaximumVoices = 24;

        [SerializeField, Min(1)] private int maximumVoices =
            DefaultMaximumVoices;

        private static AudioManager instance;
        private readonly List<AudioSource> voices =
            new List<AudioSource>();
        private readonly Dictionary<int, LoopVoice> loopVoices =
            new Dictionary<int, LoopVoice>();
        private readonly Dictionary<AudioClip, int> loopOffsets =
            new Dictionary<AudioClip, int>();
        private readonly List<int> expiredLoopIds =
            new List<int>();
        private int nextVoiceToSteal;

        public static AudioManager Instance => instance;
        public int VoiceCount => voices.Count;
        public int LoopCount => loopVoices.Count;

        private sealed class LoopVoice
        {
            public LoopVoice(
                AudioSource source,
                SoundEffectCue cue,
                Transform followTarget,
                float baseVolume,
                float basePitch)
            {
                Source = source;
                Cue = cue;
                FollowTarget = followTarget;
                BaseVolume = baseVolume;
                BasePitch = basePitch;
            }

            public AudioSource Source { get; }
            public SoundEffectCue Cue { get; }
            public Transform FollowTarget { get; set; }
            public float BaseVolume { get; }
            public float BasePitch { get; }
            public float FadeStartVolume { get; set; }
            public float FadeTargetVolume { get; set; }
            public float FadeDuration { get; set; }
            public float FadeElapsed { get; set; }
            public bool StopAfterFade { get; set; }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<AudioManager>(true) != null) return;

            GameObject root = new GameObject("[AudioManager]");
            root.AddComponent<AudioManager>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                enabled = false;
                Destroy(gameObject);
                return;
            }

            instance = this;
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            SoundEffectEvents.PlaybackRequested += HandlePlaybackRequested;
            SoundEffectEvents.LoopRequested += HandleLoopRequested;
            SoundEffectEvents.LoopStopRequested += HandleLoopStopRequested;
        }

        private void OnDisable()
        {
            SoundEffectEvents.PlaybackRequested -= HandlePlaybackRequested;
            SoundEffectEvents.LoopRequested -= HandleLoopRequested;
            SoundEffectEvents.LoopStopRequested -= HandleLoopStopRequested;
            StopAllLoops();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void OnValidate()
        {
            maximumVoices = Mathf.Max(1, maximumVoices);
        }

        private void Update()
        {
            UpdateLoops(Time.unscaledDeltaTime);
        }

        private void UpdateLoops(float deltaTime)
        {
            expiredLoopIds.Clear();
            foreach (KeyValuePair<int, LoopVoice> pair in loopVoices)
            {
                LoopVoice voice = pair.Value;
                if (voice.Source == null
                    || (voice.FollowTarget == null && !voice.StopAfterFade))
                {
                    expiredLoopIds.Add(pair.Key);
                    continue;
                }

                if (voice.FollowTarget != null)
                {
                    voice.Source.transform.position =
                        voice.FollowTarget.position;
                }
                if (AdvanceLoopFade(voice, deltaTime))
                    expiredLoopIds.Add(pair.Key);
            }

            for (int i = 0; i < expiredLoopIds.Count; i++)
                StopLoop(expiredLoopIds[i]);
        }

        private void HandlePlaybackRequested(
            SoundEffectPlaybackRequest request)
        {
            if (!request.IsValid
                || !request.Cue.TrySelectClip(
                    out AudioClip clip,
                    out float cueVolume,
                    out float pitch))
            {
                return;
            }

            AudioSource source = AcquireVoice();
            source.clip = clip;
            ConfigureSource(
                source,
                request.Cue,
                request.Position,
                cueVolume * request.VolumeScale,
                pitch,
                false);
            source.Play();
        }

        private void HandleLoopRequested(SoundEffectLoopRequest request)
        {
            if (!request.IsValid) return;

            if (loopVoices.TryGetValue(request.LoopId, out LoopVoice current))
            {
                if (current.Cue == request.Cue && current.Source != null)
                {
                    current.FollowTarget = request.FollowTarget;
                    float currentVolume = current.Source.volume;
                    ConfigureSource(
                        current.Source,
                        request.Cue,
                        request.FollowTarget.position,
                        currentVolume,
                        current.BasePitch * request.PitchScale,
                        true);
                    BeginLoopFade(
                        current,
                        current.BaseVolume * request.VolumeScale,
                        request.FadeInSeconds,
                        false);
                    return;
                }

                StopLoop(request.LoopId);
            }

            if (!request.Cue.TrySelectClip(
                    out AudioClip clip,
                    out float cueVolume,
                    out float basePitch))
            {
                return;
            }

            AudioSource source = CreateLoopSource(request.LoopId);
            source.clip = clip;
            float targetVolume = cueVolume * request.VolumeScale;
            ConfigureSource(
                source,
                request.Cue,
                request.FollowTarget.position,
                request.FadeInSeconds > 0f ? 0f : targetVolume,
                basePitch * request.PitchScale,
                true);
            RestoreLoopOffset(source);
            LoopVoice voice = new LoopVoice(
                source,
                request.Cue,
                request.FollowTarget,
                cueVolume,
                basePitch);
            loopVoices.Add(
                request.LoopId,
                voice);
            BeginLoopFade(
                voice,
                targetVolume,
                request.FadeInSeconds,
                false);
            source.Play();
        }

        private void HandleLoopStopRequested(
            SoundEffectLoopStopRequest request)
        {
            if (!request.IsValid) return;
            if (request.FadeOutSeconds <= 0f)
            {
                StopLoop(request.LoopId);
                return;
            }
            if (!loopVoices.TryGetValue(
                    request.LoopId,
                    out LoopVoice voice)
                || voice.Source == null
                || voice.StopAfterFade)
            {
                return;
            }

            BeginLoopFade(
                voice,
                0f,
                request.FadeOutSeconds,
                true);
        }

        private static void BeginLoopFade(
            LoopVoice voice,
            float targetVolume,
            float duration,
            bool stopAfterFade)
        {
            if (voice == null || voice.Source == null) return;

            voice.FadeStartVolume = voice.Source.volume;
            voice.FadeTargetVolume = Mathf.Clamp01(targetVolume);
            voice.FadeDuration = Mathf.Max(0f, duration);
            voice.FadeElapsed = 0f;
            voice.StopAfterFade = stopAfterFade;
            if (voice.FadeDuration <= 0f)
                voice.Source.volume = voice.FadeTargetVolume;
        }

        private static bool AdvanceLoopFade(
            LoopVoice voice,
            float deltaTime)
        {
            if (voice == null
                || voice.Source == null
                || voice.FadeDuration <= 0f)
            {
                return false;
            }

            voice.FadeElapsed = Mathf.Min(
                voice.FadeDuration,
                voice.FadeElapsed + Mathf.Max(0f, deltaTime));
            float progress = voice.FadeElapsed / voice.FadeDuration;
            voice.Source.volume = Mathf.Lerp(
                voice.FadeStartVolume,
                voice.FadeTargetVolume,
                progress);
            if (voice.FadeElapsed < voice.FadeDuration) return false;

            voice.FadeDuration = 0f;
            return voice.StopAfterFade;
        }

        private static void ConfigureSource(
            AudioSource source,
            SoundEffectCue cue,
            Vector3 position,
            float volume,
            float pitch,
            bool loop)
        {
            source.transform.position = position;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, -3f, 3f);
            source.loop = loop;
            source.spatialBlend = cue.SpatialBlend;
            source.minDistance = cue.MinimumDistance;
            source.maxDistance = cue.MaximumDistance;
            source.rolloffMode = cue.RolloffMode;
            source.outputAudioMixerGroup = cue.Output;
        }

        private AudioSource AcquireVoice()
        {
            for (int i = 0; i < voices.Count; i++)
            {
                AudioSource voice = voices[i];
                if (voice != null && !voice.isPlaying)
                    return voice;
            }

            int voiceLimit = Mathf.Max(1, maximumVoices);
            if (voices.Count < voiceLimit)
                return CreateVoice();

            nextVoiceToSteal %= voices.Count;
            AudioSource stolen = voices[nextVoiceToSteal];
            nextVoiceToSteal = (nextVoiceToSteal + 1) % voices.Count;
            stolen.Stop();
            return stolen;
        }

        private AudioSource CreateVoice()
        {
            GameObject voiceObject = new GameObject(
                "Sound Effect Voice " + (voices.Count + 1));
            voiceObject.transform.SetParent(transform, false);
            AudioSource source = voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.dopplerLevel = 0f;
            voices.Add(source);
            return source;
        }

        private AudioSource CreateLoopSource(int loopId)
        {
            GameObject voiceObject = new GameObject(
                "Sound Effect Loop " + loopId);
            voiceObject.transform.SetParent(transform, false);
            AudioSource source = voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.dopplerLevel = 0f;
            return source;
        }

        private void StopLoop(int loopId)
        {
            if (!loopVoices.TryGetValue(loopId, out LoopVoice voice))
                return;

            RememberLoopOffset(voice.Source);
            loopVoices.Remove(loopId);
            if (voice.Source == null) return;

            voice.Source.Stop();
            if (Application.isPlaying)
                Destroy(voice.Source.gameObject);
            else
                DestroyImmediate(voice.Source.gameObject);
        }

        private void RestoreLoopOffset(AudioSource source)
        {
            if (source == null || source.clip == null) return;

            AudioClip clip = source.clip;
            int offsetSamples;
            if (!TryGetActiveLoopOffset(clip, out offsetSamples)
                && !loopOffsets.TryGetValue(clip, out offsetSamples))
            {
                return;
            }

            source.timeSamples = NormalizeLoopOffset(
                offsetSamples,
                clip.samples);
        }

        private bool TryGetActiveLoopOffset(
            AudioClip clip,
            out int offsetSamples)
        {
            foreach (LoopVoice voice in loopVoices.Values)
            {
                if (voice.Source == null || voice.Source.clip != clip)
                    continue;

                offsetSamples = voice.Source.timeSamples;
                return true;
            }

            offsetSamples = 0;
            return false;
        }

        private void RememberLoopOffset(AudioSource source)
        {
            if (source == null || source.clip == null) return;

            AudioClip clip = source.clip;
            loopOffsets[clip] = NormalizeLoopOffset(
                source.timeSamples,
                clip.samples);
        }

        private static int NormalizeLoopOffset(
            int offsetSamples,
            int clipSamples)
        {
            if (clipSamples <= 0) return 0;

            int normalized = offsetSamples % clipSamples;
            return normalized < 0 ? normalized + clipSamples : normalized;
        }

        private void StopAllLoops()
        {
            expiredLoopIds.Clear();
            foreach (int loopId in loopVoices.Keys)
                expiredLoopIds.Add(loopId);
            for (int i = 0; i < expiredLoopIds.Count; i++)
                StopLoop(expiredLoopIds[i]);
            expiredLoopIds.Clear();
        }
    }
}
