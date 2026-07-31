using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Gameplay
{
    public enum PlayerToolPrimaryAction
    {
        None = 0,
        MineVoxel = 1,
        AttractCart = 2,
        ThrowPersistentLight = 3,
        FireRifle = 4,
        TowCart = 5,
    }

    public enum PlayerToolAnimationTriggerMode
    {
        Single = 0,
        Periodic = 1,
        Continuous = 2,
    }

    public enum HeldToolMountStrategy
    {
        SingleHand = 0,
        Rifle = 1,
    }

    /// <summary>
    /// Data that turns an inventory item into a usable left-click tool. The player state
    /// machine only knows that a tool action is active; this asset selects its gameplay
    /// handler, timing, animation, and whether movement remains available while it is held.
    /// </summary>
    [CreateAssetMenu(
        fileName = "PlayerToolDefinition",
        menuName = "Supernova/Player/Tool Definition")]
    public sealed class PlayerToolDefinition : ScriptableObject
    {
        public const float StandardFirearmActionPeriod = 0.1f;

        [SerializeField] private PlayerInventoryItem item;
        [SerializeField] private PlayerToolPrimaryAction primaryAction;
        [Tooltip("Single plays once on action entry. Periodic plays once per tool cycle. Continuous stays active while the action is held and requires a looping clip.")]
        [SerializeField] private PlayerToolAnimationTriggerMode animationTriggerMode;
        [SerializeField] private AnimationClip primaryActionAnimation;
        [Tooltip("Prefab instantiated at the player's tool mount while this tool is selected. Leave null to show no held model.")]
        [SerializeField] private GameObject heldModelPrefab;
        [Tooltip("Single Hand uses the general hand mount. Rifle uses a dedicated left-hand mount and preserves the weapon prefab's root pose.")]
        [SerializeField] private HeldToolMountStrategy heldModelMountStrategy;
        [SerializeField] private bool allowMovementWhileUsing;

        [Header("Primary Action Timing")]
        [Tooltip("Seconds from starting a tool cycle until its gameplay effect is applied.")]
        [SerializeField, Min(0f)] private float actionTriggerDelay;
        [Tooltip("Minimum seconds between the starts of two action cycles for this tool.")]
        [SerializeField, Min(0.02f)] private float actionCyclePeriod = 0.25f;
        [Tooltip("When enabled, holding primary action starts another cycle every Action Cycle Period.")]
        [SerializeField] private bool actionIsPeriodic;

        [Header("Thrown Projectile")]
        [Tooltip("Persistent projectile spawned by a throwing tool.")]
        [SerializeField] private PersistentLightProjectile projectilePrefab;
        [SerializeField, Min(0f)] private float throwSpeed = 8f;
        [SerializeField, Min(0f)] private float upwardThrowSpeed = 1.5f;
        [SerializeField, Min(0f)] private float throwSpinSpeed = 8f;
        [SerializeField, Min(0f)] private float throwForwardOffset = 0.75f;

        [Header("Firearm")]
        [Tooltip("Fast physical projectile spawned by this firearm.")]
        [SerializeField] private BallisticProjectile firearmProjectilePrefab;
        [SerializeField, Min(0f)] private float projectileSpeed = 180f;
        [Tooltip("Ammunition placed in the player's inventory when play starts.")]
        [SerializeField, Min(0)] private int initialAmmunition = 120;
        [Tooltip("Project-owned muzzle flash copied from the KriptoFX PC demo.")]
        [SerializeField] private GameObject muzzleFlashPrefab;
        [SerializeField, Min(0.01f)] private float muzzleFlashLifetime = 0.75f;

        [Header("Mining Brush")]
        [Tooltip("Upgradeable base damage dealt by an odd-numbered mining strike.")]
        [SerializeField, Min(0.01f)] private float miningPower = 1f;
        [Tooltip("Damage multiplier applied to every even-numbered mining strike.")]
        [SerializeField, Min(1f)] private float miningEvenHitMultiplier = 4f;
        [Tooltip("A destroyed voxel passes excess damage divided by this value to each of its 26 neighbours.")]
        [SerializeField, Min(1f)] private float miningPropagationDivisor = 2f;
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
        public HeldToolMountStrategy HeldModelMountStrategy => heldModelMountStrategy;
        public bool AllowMovementWhileUsing => allowMovementWhileUsing;
        public bool HasPrimaryAction => primaryAction != PlayerToolPrimaryAction.None;
        public float ActionTriggerDelay => Mathf.Max(0f, actionTriggerDelay);
        public float ActionCyclePeriod => Mathf.Max(0.02f, actionCyclePeriod);
        public bool ActionIsPeriodic => actionIsPeriodic;
        public PersistentLightProjectile ProjectilePrefab => projectilePrefab;
        public float ThrowSpeed => Mathf.Max(0f, throwSpeed);
        public float UpwardThrowSpeed => Mathf.Max(0f, upwardThrowSpeed);
        public float ThrowSpinSpeed => Mathf.Max(0f, throwSpinSpeed);
        public float ThrowForwardOffset => Mathf.Max(0f, throwForwardOffset);
        public BallisticProjectile FirearmProjectilePrefab =>
            firearmProjectilePrefab;
        public float ProjectileSpeed => Mathf.Max(0f, projectileSpeed);
        public float RoundsPerSecond => 1f / ActionCyclePeriod;
        public float ShotInterval => ActionCyclePeriod;
        public float FirearmAnimationSpeedMultiplier => IsFirearm
            ? StandardFirearmActionPeriod / ActionCyclePeriod
            : 1f;
        public int InitialAmmunition => Mathf.Max(0, initialAmmunition);
        public GameObject MuzzleFlashPrefab => muzzleFlashPrefab;
        public float MuzzleFlashLifetime => Mathf.Max(0.01f, muzzleFlashLifetime);
        public bool IsFirearm => primaryAction == PlayerToolPrimaryAction.FireRifle;
        public VoxelMiningBrushSettings MiningBrush =>
            new VoxelMiningBrushSettings(
                miningPower,
                miningRadius,
                miningDepth,
                miningFalloffExponent,
                miningMinimumPowerFraction,
                miningMaxAffectedSamples,
                miningPropagationDivisor);
        public float MiningEvenHitMultiplier =>
            Mathf.Max(1f, miningEvenHitMultiplier);

        public VoxelMiningBrushSettings GetMiningBrushForStrike(int strikeNumber)
        {
            float multiplier = strikeNumber > 0 && strikeNumber % 2 == 0
                ? MiningEvenHitMultiplier
                : 1f;
            return MiningBrush.WithPower(MiningBrush.Power * multiplier);
        }
    }
}
