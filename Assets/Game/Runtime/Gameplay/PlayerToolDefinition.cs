using Supernova.Audio;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Gameplay
{
    public enum PlayerToolPrimaryAction
    {
        None = 0,
        MineVoxel = 1,
        ThrowPersistentLight = 3,
        FireProjectile = 4,
        ThrowBomb = 7,
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
        TwoHanded = 1,
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

        [Header("Sound")]
        [Tooltip("Sound broadcast when this tool successfully starts its primary action.")]
        [SerializeField] private SoundEffectCue primaryActionSound;
        [Tooltip("Sound broadcast when this tool is successfully thrown.")]
        [SerializeField] private SoundEffectCue throwSound;
        [Tooltip("Sound broadcast at the voxel hit position after a mining action succeeds.")]
        [SerializeField] private SoundEffectCue miningHitSound;
        [Tooltip("Sound broadcast when this tool's melee overlap damages a monster.")]
        [SerializeField] private SoundEffectCue monsterHitSound;
        [Tooltip("Sound broadcast where a thrown instance of this tool first hits terrain.")]
        [SerializeField] private SoundEffectCue thrownTerrainHitSound;

        [Header("Magnet Hold")]
        [Tooltip("Upper-body pose used by the magnet. Playback starts directly at the configured loop start and stays in that tail segment.")]
        [SerializeField] private AnimationClip magnetHoldAnimation;
        [Tooltip("Normalized point where magnet playback starts and the tail loop begins.")]
        [SerializeField, Range(0f, 0.95f)]
        private float magnetHoldLoopStartNormalized = 0.7f;
        [Tooltip("Normalized point where the magnet pose tail finishes returning toward the loop start.")]
        [SerializeField, Range(0.05f, 1f)]
        private float magnetHoldLoopEndNormalized = 1f;
        [Tooltip("Normalized duration blended from the end of the magnet tail back into its loop start. The value is capped below half of the loop so each cycle can advance normally.")]
        [SerializeField, Range(0f, 0.45f)]
        private float magnetHoldLoopBlendNormalized = 0.45f;
        [Tooltip("Playback multiplier for the magnet tail loop. With the shared state's 1.5x base speed, 0.3333 produces a calm 0.5x loop.")]
        [SerializeField, Range(0.1f, 2f)]
        private float magnetHoldLoopSpeedMultiplier = 0.3333333f;
        [Tooltip("Single plays once on action entry. Periodic plays once per tool cycle. Continuous stays active while the action is held and requires a looping clip.")]
        [SerializeField] private PlayerToolAnimationTriggerMode animationTriggerMode;
        [SerializeField] private AnimationClip primaryActionAnimation;
        [Tooltip("Prefab instantiated at the player's tool mount while this tool is selected. Leave null to show no held model.")]
        [SerializeField] private GameObject heldModelPrefab;
        [Tooltip("Single Hand uses the general hand mount. Two Handed uses a dedicated left-hand mount and preserves the prefab's root pose.")]
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

        [Header("Bomb")]
        [Tooltip("Timed explosive spawned by the bomb tool.")]
        [SerializeField] private BombProjectile bombProjectilePrefab;
        [Tooltip("Maximum radial impulse applied to nearby dynamic bodies.")]
        [SerializeField, Min(0f)]
        private float bombEntityExplosionImpulse = 240f;
        [Tooltip("Game-owned visual effect spawned when the bomb detonates.")]
        [SerializeField] private GameObject bombExplosionEffectPrefab;
        [SerializeField, Min(0.01f)]
        private float bombExplosionEffectLifetime = 3f;

        [Header("Thrown Pickaxe")]
        [Tooltip("Projectile spawned when the pickaxe is thrown with right click.")]
        [SerializeField] private ThrownPickaxe thrownPickaxePrefab;
        [SerializeField, Min(0.1f)] private float pickaxeThrowSpeed = 22f;
        [Tooltip("Revolutions per second while the thrown pickaxe tumbles.")]
        [SerializeField, Min(0f)] private float pickaxeSpinRevolutions = 2.4f;
        [Tooltip("Range at which the player counts as being at a thrown pickaxe. Informational only: recall is triggered by the throw key.")]
        [SerializeField, Min(0.1f)] private float pickaxePickupDistance = 1.6f;
        [Tooltip("Player acceleration applied while the magnet pulls towards a thrown pickaxe.")]
        [SerializeField, Min(0f)] private float pickaxeMagnetPullAcceleration = 34f;
        [SerializeField, Min(0.1f)] private float pickaxeMagnetMaximumPullSpeed = 16f;
        [Tooltip("Maximum range at which the magnet can latch onto a thrown pickaxe.")]
        [SerializeField, Min(1f)] private float pickaxeMagnetRange = 60f;
        [Tooltip("How far off the crosshair a thrown pickaxe may sit and still be latched, in degrees. Small values require deliberate aim instead of grabbing anything on screen.")]
        [SerializeField, Range(1f, 90f)] private float pickaxeMagnetAimAngle = 20f;

        [Header("Pickaxe Rope")]
        [Tooltip("Automatic winch speed while the rope is held. Zero — the default — keeps the rope at the length it attached with, so right click swings rather than dragging the player in. The scroll wheel still reels manually.")]
        [SerializeField, Min(0f)] private float ropeReelInSpeed;
        [Tooltip("How fast the scroll wheel hauls the rope in or out, in metres per second. This is also the speed the player is drawn towards the anchor while reeling, so keep it near walking pace.")]
        [SerializeField, Min(0f)] private float ropeManualReelSpeed = 4f;
        [Tooltip("Rope can never be shortened below this, so the player is not dragged into the anchor.")]
        [SerializeField, Min(0.5f)] private float ropeMinimumLength = 2.5f;
        [Tooltip("Fraction of the outward speed converted into an inward jolt the moment the rope snaps taut.")]
        [SerializeField, Range(0f, 1f)] private float ropeYankStrength = 0.35f;
        [Tooltip("Upper bound on that initial jolt, in metres per second.")]
        [SerializeField, Min(0f)] private float ropeMaximumYankSpeed = 7f;
        [Tooltip("Acceleration the movement keys apply along the swing arc, letting the player pump the swing higher.")]
        [SerializeField, Min(0f)] private float ropeSwingAcceleration = 3f;
        [Tooltip("How quickly rope swing input eases towards the current movement-key direction. Lower values feel softer and slower.")]
        [SerializeField, Min(0.01f)] private float ropeSwingInputResponse = 5f;
        [Tooltip("Hard ceiling on rope-driven speed, so a long swing cannot accelerate without limit.")]
        [SerializeField, Min(1f)] private float ropeMaximumSpeed = 34f;
        [Tooltip("Fraction of the swing speed kept when the rope is released. High values let the player fling themselves off a swing.")]
        [SerializeField, Range(0f, 1.5f)] private float ropeReleaseMomentum = 1f;

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
        public SoundEffectCue PrimaryActionSound => primaryActionSound;
        public SoundEffectCue ThrowSound => throwSound;
        public SoundEffectCue MiningHitSound => miningHitSound;
        public SoundEffectCue MonsterHitSound => monsterHitSound;
        public SoundEffectCue ThrownTerrainHitSound => thrownTerrainHitSound;
        public AnimationClip MagnetHoldAnimation => magnetHoldAnimation;
        public float MagnetHoldLoopStartNormalized =>
            Mathf.Clamp(magnetHoldLoopStartNormalized, 0f, 0.95f);
        public float MagnetHoldLoopEndNormalized =>
            Mathf.Clamp(
                magnetHoldLoopEndNormalized,
                MagnetHoldLoopStartNormalized + 0.05f,
                1f);
        public float MagnetHoldLoopBlendNormalized =>
            Mathf.Clamp(
                magnetHoldLoopBlendNormalized,
                0f,
                (MagnetHoldLoopEndNormalized
                    - MagnetHoldLoopStartNormalized) * 0.45f);
        public float MagnetHoldLoopSpeedMultiplier =>
            Mathf.Clamp(magnetHoldLoopSpeedMultiplier, 0.1f, 2f);

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
        public BombProjectile BombProjectilePrefab => bombProjectilePrefab;
        public float BombEntityExplosionImpulse =>
            Mathf.Max(0f, bombEntityExplosionImpulse);
        public GameObject BombExplosionEffectPrefab =>
            bombExplosionEffectPrefab;
        public float BombExplosionEffectLifetime =>
            Mathf.Max(0.01f, bombExplosionEffectLifetime);
        public ThrownPickaxe ThrownPickaxePrefab => thrownPickaxePrefab;
        public float PickaxeThrowSpeed => Mathf.Max(0.1f, pickaxeThrowSpeed);
        public float PickaxeSpinRevolutions =>
            Mathf.Max(0f, pickaxeSpinRevolutions);
        public float PickaxePickupDistance =>
            Mathf.Max(0.1f, pickaxePickupDistance);
        public float PickaxeMagnetPullAcceleration =>
            Mathf.Max(0f, pickaxeMagnetPullAcceleration);
        public float PickaxeMagnetMaximumPullSpeed =>
            Mathf.Max(0.1f, pickaxeMagnetMaximumPullSpeed);
        public float PickaxeMagnetRange => Mathf.Max(1f, pickaxeMagnetRange);
        public float PickaxeMagnetAimAngle =>
            Mathf.Clamp(pickaxeMagnetAimAngle, 1f, 90f);
        public float RopeReelInSpeed => Mathf.Max(0f, ropeReelInSpeed);
        public float RopeManualReelSpeed => Mathf.Max(0f, ropeManualReelSpeed);
        public float RopeMinimumLength => Mathf.Max(0.5f, ropeMinimumLength);
        public float RopeYankStrength => Mathf.Clamp01(ropeYankStrength);
        public float RopeMaximumYankSpeed => Mathf.Max(0f, ropeMaximumYankSpeed);
        public float RopeSwingAcceleration => Mathf.Max(0f, ropeSwingAcceleration);
        public float RopeSwingInputResponse => Mathf.Max(0.01f, ropeSwingInputResponse);
        public float RopeMaximumSpeed => Mathf.Max(1f, ropeMaximumSpeed);
        public float RopeReleaseMomentum =>
            Mathf.Clamp(ropeReleaseMomentum, 0f, 1.5f);
        public bool CanThrowPickaxe => thrownPickaxePrefab != null;
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
        public bool IsFirearm =>
            primaryAction == PlayerToolPrimaryAction.FireProjectile;
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
