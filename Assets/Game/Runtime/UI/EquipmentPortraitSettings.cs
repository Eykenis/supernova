using UnityEngine;

namespace Supernova.UI
{
    /// <summary>
    /// Presentation-only animation settings for the TAB equipment portrait.
    /// The clip is played through an independent playable graph and does not
    /// read or mutate the live player's animator state machine.
    /// </summary>
    [CreateAssetMenu(
        fileName = "EquipmentPortraitSettings",
        menuName = "Supernova/UI/Equipment Portrait Settings")]
    public sealed class EquipmentPortraitSettings : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Looping animation used only by the TAB character portrait.")]
        private AnimationClip[] animationClips;

        public AnimationClip[] AnimationClips => animationClips;
    }
}
