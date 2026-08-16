using UnityEngine;
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
        [SerializeField, Min(0.01f)] private float crouchBlendDuration = 0.15f;
        [Tooltip("CharacterController height used while crouching. The feet stay at the standing height.")]
        [SerializeField, Min(0.01f)] private float crouchColliderHeight = 1f;
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
        [SerializeField] private float kocchiDistance = 2f;

        [Header("Voxel interaction")]
        [SerializeField, Min(0.1f)] private float interactionReach = 3f;
        [Tooltip("Pull the mining ray origin back by this distance so blocks pressed against the camera can still be hit.")]
        [SerializeField, Min(0f)] private float mineRayBackstep = 0.35f;

        [Header("Debug fly")]
        [SerializeField, Min(0f)] private float debugFlySpeed = 12f;
        [SerializeField, Min(1f)] private float debugFlySpeedMultiplier = 3f;

        public float MoveSpeed => Mathf.Max(0f, moveSpeed);
        public float CrouchMoveSpeed => Mathf.Max(0f, crouchMoveSpeed);
        public float CrouchBlendDuration => Mathf.Max(0.01f, crouchBlendDuration);
        public float CrouchColliderHeight => Mathf.Max(0.01f, crouchColliderHeight);
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
        public float KocchiDistance => Mathf.Max(0f, kocchiDistance);
        public float InteractionReach => Mathf.Max(0.1f, interactionReach);
        public float MineRayBackstep => Mathf.Max(0f, mineRayBackstep);
        public float DebugFlySpeed => Mathf.Max(0f, debugFlySpeed);
        public float DebugFlySpeedMultiplier => Mathf.Max(1f, debugFlySpeedMultiplier);

    }
}
