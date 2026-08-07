using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures
{
    /// <summary>
    /// Mirrors the creature behavior state into an Animator integer parameter.
    /// This keeps animation presentation separate from navigation and combat logic.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CreatureBehaviorAgent))]
    public sealed class CreatureBehaviorAnimator : MonoBehaviour
    {
        public const string BehaviorStateParameter = "BehaviorState";

        private static readonly int BehaviorStateId =
            Animator.StringToHash(BehaviorStateParameter);

        [SerializeField] private CreatureBehaviorAgent behavior;
        [SerializeField] private Animator animator;

        private CreatureBehaviorState lastState = (CreatureBehaviorState)(-1);

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
            if (presentedState == lastState)
            {
                return;
            }

            lastState = presentedState;
            animator.SetInteger(BehaviorStateId, (int)lastState);
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
