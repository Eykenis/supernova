using System;
using UnityEngine;

namespace Supernova.Inputs
{
    /// <summary>
    /// Persisted look sensitivity multiplier shared by every camera that reads
    /// <see cref="GameInputActionId.Look"/>. Cameras keep their own tuned base
    /// value and multiply it by <see cref="Multiplier"/>, so the setting stays
    /// meaningful for rigs with different feels.
    /// </summary>
    public static class LookSensitivitySettings
    {
        public const float MinimumMultiplier = 0.1f;
        public const float MaximumMultiplier = 3f;
        public const float DefaultMultiplier = 1f;

        private const string PreferenceKey = "input.look-sensitivity";

        private static float multiplier = float.NaN;

        public static event Action Changed;

        public static float Multiplier
        {
            get
            {
                if (float.IsNaN(multiplier))
                {
                    multiplier = Clamp(
                        PlayerPrefs.GetFloat(PreferenceKey, DefaultMultiplier));
                }
                return multiplier;
            }
            set
            {
                float clamped = Clamp(value);
                if (!float.IsNaN(multiplier)
                    && Mathf.Approximately(multiplier, clamped))
                {
                    return;
                }

                multiplier = clamped;
                PlayerPrefs.SetFloat(PreferenceKey, clamped);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static void ResetToDefault()
        {
            Multiplier = DefaultMultiplier;
        }

        private static float Clamp(float value)
        {
            if (float.IsNaN(value))
                return DefaultMultiplier;
            return Mathf.Clamp(value, MinimumMultiplier, MaximumMultiplier);
        }
    }
}
