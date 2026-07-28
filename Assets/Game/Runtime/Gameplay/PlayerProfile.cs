using UnityEngine;
using UnityEngine.Serialization;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Static configuration shared by every player feature. Runtime components keep
    /// references and state, while tuning values live here on the Player root.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerProfile : MonoBehaviour
    {
        [Header("Locomotion")]
        [SerializeField, Min(0f)] private float moveSpeed = 4f;
        [SerializeField, Min(0f)] private float crouchMoveSpeed = 2f;
        [SerializeField] private KeyCode crouchKey = KeyCode.LeftControl;
        [SerializeField, Min(0.01f)] private float crouchBlendDuration = 0.15f;
        [SerializeField, Min(0f)] private float jumpHeight = 1.1f;
        [SerializeField, Min(0f)] private float gravity = 20f;
        [SerializeField, Min(0f)] private float groundedForce = 2f;
        [SerializeField, Min(0f)] private float movingThreshold = 0.05f;

        [Header("Health and melee")]
        [SerializeField, Min(0.01f)] private float maximumHealth = 100f;
        [SerializeField, Min(0f)] private float attackDamage = 25f;
        [SerializeField, Min(0.1f)] private float attackReach = 1.6f;
        [SerializeField, Range(0.05f, 1f)] private float attackRadius = 0.75f;
        [SerializeField, Range(-1f, 1f)] private float attackMinimumForwardDot = 0.15f;
        [SerializeField, Min(0f)] private float attackImpulse = 2.5f;
        [SerializeField, Min(0.01f)] private float attackWindup = 0.12f;
        [SerializeField, Min(0.02f)] private float attackDuration = 0.42f;
        [SerializeField, Min(0.02f)] private float attackCooldown = 0.55f;
        [SerializeField, Min(0.02f)] private float hurtDuration = 0.35f;
        [SerializeField] private LayerMask attackLayers = ~0;

        [Header("Animation")]
        [SerializeField, Min(0.1f)] private float alternateIdleDelay = 15f;
        [SerializeField] private KeyCode smileKey = KeyCode.Q;
        [FormerlySerializedAs("knockdownKey")]
        [SerializeField] private KeyCode hitKey = KeyCode.K;
        [SerializeField] private KeyCode dieKey = KeyCode.L;
        [SerializeField] private KeyCode recoverKey = KeyCode.R;
        [SerializeField] private float kocchiDistance = 2f;

        [Header("Voxel interaction")]
        [SerializeField, Min(0.1f)] private float interactionReach = 3f;
        [Tooltip("Delay from starting the attack animation until the targeted voxel receives the mining hit.")]
        [SerializeField, Min(0f)] private float voxelDestructionDelay = 0.05f;
        [Tooltip("Minimum time between consecutive mining hits while holding the mouse button.")]
        [SerializeField, Min(0.02f)] private float mineInterval = 0.22f;
        [Tooltip("Cooldown applied when a mining swing hits nothing, so an empty swing does not block the next attempt.")]
        [SerializeField, Min(0.02f)] private float mineWhiffCooldown = 0.08f;
        [Tooltip("Pull the mining ray origin back by this distance so blocks pressed against the camera can still be hit.")]
        [SerializeField, Min(0f)] private float mineRayBackstep = 0.35f;

        [Header("Debug fly")]
        [SerializeField] private KeyCode debugToggleKey = KeyCode.F3;
        [SerializeField, Min(0f)] private float debugFlySpeed = 12f;
        [SerializeField, Min(1f)] private float debugFlySpeedMultiplier = 3f;

        public float MoveSpeed => Mathf.Max(0f, moveSpeed);
        public float CrouchMoveSpeed => Mathf.Max(0f, crouchMoveSpeed);
        public KeyCode CrouchKey => crouchKey;
        public float CrouchBlendDuration => Mathf.Max(0.01f, crouchBlendDuration);
        public float JumpHeight => Mathf.Max(0f, jumpHeight);
        public float Gravity => Mathf.Max(0f, gravity);
        public float GroundedForce => Mathf.Max(0f, groundedForce);
        public float MovingThreshold => Mathf.Max(0f, movingThreshold);
        public float MaximumHealth => Mathf.Max(0.01f, maximumHealth);
        public float AttackDamage => Mathf.Max(0f, attackDamage);
        public float AttackReach => Mathf.Max(0.1f, attackReach);
        public float AttackRadius => Mathf.Clamp(attackRadius, 0.05f, 1f);
        public float AttackMinimumForwardDot => Mathf.Clamp(attackMinimumForwardDot, -1f, 1f);
        public float AttackImpulse => Mathf.Max(0f, attackImpulse);
        public float AttackWindup => Mathf.Max(0.01f, attackWindup);
        public float AttackDuration => Mathf.Max(0.02f, attackDuration);
        public float AttackCooldown => Mathf.Max(0.02f, attackCooldown);
        public float HurtDuration => Mathf.Max(0.02f, hurtDuration);
        public LayerMask AttackLayers => attackLayers;
        public float AlternateIdleDelay => Mathf.Max(0.1f, alternateIdleDelay);
        public KeyCode SmileKey => smileKey;
        public KeyCode HitKey => hitKey;
        public KeyCode DieKey => dieKey;
        public KeyCode RecoverKey => recoverKey;
        public float KocchiDistance => Mathf.Max(0f, kocchiDistance);
        public float InteractionReach => Mathf.Max(0.1f, interactionReach);
        public float VoxelDestructionDelay => Mathf.Max(0f, voxelDestructionDelay);
        public float MineInterval => Mathf.Max(0.02f, mineInterval);
        public float MineWhiffCooldown => Mathf.Max(0.02f, mineWhiffCooldown);
        public float MineRayBackstep => Mathf.Max(0f, mineRayBackstep);
        public KeyCode DebugToggleKey => debugToggleKey;
        public float DebugFlySpeed => Mathf.Max(0f, debugFlySpeed);
        public float DebugFlySpeedMultiplier => Mathf.Max(1f, debugFlySpeedMultiplier);

    }
}
