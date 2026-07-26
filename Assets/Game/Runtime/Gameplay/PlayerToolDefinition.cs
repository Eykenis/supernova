using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Gameplay
{
    public enum PlayerToolPrimaryAction
    {
        None = 0,
        MineVoxel = 1,
        AttractCart = 2,
    }

    public enum PlayerToolAnimationTriggerMode
    {
        Single = 0,
        Periodic = 1,
        Continuous = 2,
    }

    /// <summary>
    /// Data that turns an inventory item into a usable left-click tool. The player state
    /// machine only knows that a tool action is active; this asset selects the gameplay
    /// action, animation clip, and whether movement remains available while it is held.
    /// </summary>
    [CreateAssetMenu(
        fileName = "PlayerToolDefinition",
        menuName = "Supernova/Player/Tool Definition")]
    public sealed class PlayerToolDefinition : ScriptableObject
    {
        [SerializeField] private PlayerInventoryItem item;
        [SerializeField] private PlayerToolPrimaryAction primaryAction;
        [Tooltip("Single plays once on action entry. Periodic plays once per tool cycle. Continuous stays active while the action is held and requires a looping clip.")]
        [SerializeField] private PlayerToolAnimationTriggerMode animationTriggerMode;
        [SerializeField] private AnimationClip primaryActionAnimation;
        [Tooltip("Prefab instantiated at the player's tool mount while this tool is selected. Leave null to show no held model.")]
        [SerializeField] private GameObject heldModelPrefab;
        [SerializeField] private bool allowMovementWhileUsing;

        [Header("Mining Brush")]
        [SerializeField, Min(0.01f)] private float miningPower = 2f;
        [SerializeField, Min(0f)] private float miningRadius = 0.55f;
        [SerializeField, Min(0f)] private float miningDepth = 0.75f;
        [SerializeField, Min(0.01f)] private float miningFalloffExponent = 1.5f;
        [SerializeField, Range(0f, 1f)]
        private float miningMinimumPowerFraction = 0.25f;
        [SerializeField, Range(1, 128)] private int miningMaxAffectedSamples = 24;

        public PlayerInventoryItem Item => item;
        public PlayerToolPrimaryAction PrimaryAction => primaryAction;
        public PlayerToolAnimationTriggerMode AnimationTriggerMode => animationTriggerMode;
        public AnimationClip PrimaryActionAnimation => primaryActionAnimation;
        public GameObject HeldModelPrefab => heldModelPrefab;
        public bool AllowMovementWhileUsing => allowMovementWhileUsing;
        public bool HasPrimaryAction => primaryAction != PlayerToolPrimaryAction.None;
        public VoxelMiningBrushSettings MiningBrush =>
            new VoxelMiningBrushSettings(
                miningPower,
                miningRadius,
                miningDepth,
                miningFalloffExponent,
                miningMinimumPowerFraction,
                miningMaxAffectedSamples);
    }
}
