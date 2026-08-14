using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures
{
    /// <summary>
    /// Mirrors the creature behavior state into an Animator integer parameter.
    /// This keeps animation presentation separate from behavior and combat logic.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CreatureBehaviorAgent))]
    public sealed class CreatureBehaviorAnimator : MonoBehaviour
    {
        public const string BehaviorStateParameter = "BehaviorState";

        /// <summary>Name of the attack state, shared by every creature controller.</summary>
        public const string AttackStateName = "Attack";

        private static readonly int BehaviorStateId =
            Animator.StringToHash(BehaviorStateParameter);
        private static readonly int AttackStateId =
            Animator.StringToHash(AttackStateName);

        [SerializeField] private CreatureBehaviorAgent behavior;
        [SerializeField] private Animator animator;

        private CreatureBehaviorState lastState = (CreatureBehaviorState)(-1);
        private int lastAttackSwing;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            lastState = (CreatureBehaviorState)(-1);
            lastAttackSwing = behavior != null ? behavior.AttackSwingCount : 0;
            Synchronize();
        }

        private void Update()
        {
            Synchronize();
        }

        private void ResolveReferences()
        {
            if (behavior == null)
            {
                behavior = GetComponent<CreatureBehaviorAgent>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }
        }

        private void Synchronize()
        {
            if (behavior == null || animator == null)
            {
                ResolveReferences();
            }

            if (behavior == null
                || animator == null
                || animator.runtimeAnimatorController == null)
            {
                return;
            }

            CreatureBehaviorState presentedState =
                ResolvePresentationState(
                    behavior.CurrentState,
                    behavior.IsActuallyMoving);
            if (presentedState != lastState)
            {
                lastState = presentedState;
                animator.SetInteger(BehaviorStateId, (int)lastState);
            }

            SynchronizeAttackSwing(presentedState);
        }

        /// <summary>
        /// Restarts the attack clip whenever the agent begins a new swing. The
        /// behavior state stays Attack across consecutive swings, so the integer
        /// parameter alone would leave the clip playing once while the attack keeps
        /// settling. Replaying the state keeps every hit paired with an animation.
        /// </summary>
        private void SynchronizeAttackSwing(CreatureBehaviorState presentedState)
        {
            int swing = behavior.AttackSwingCount;
            if (swing == lastAttackSwing)
            {
                return;
            }

            lastAttackSwing = swing;
            if (presentedState == CreatureBehaviorState.Attack)
            {
                animator.Play(AttackStateId, 0, 0f);
            }
        }

        public static CreatureBehaviorState ResolvePresentationState(
            CreatureBehaviorState behaviorState,
            bool isActuallyMoving)
        {
            if (behaviorState == CreatureBehaviorState.Caught)
            {
                return CreatureBehaviorState.Idle;
            }

            if (!isActuallyMoving
                && (behaviorState == CreatureBehaviorState.Wander
                    || behaviorState == CreatureBehaviorState.Pursue))
            {
                return CreatureBehaviorState.Idle;
            }
            return behaviorState;
        }
    }
}
