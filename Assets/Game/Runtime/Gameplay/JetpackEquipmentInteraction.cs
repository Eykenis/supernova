using UnityEngine;

namespace Supernova.Gameplay
{
    [CreateAssetMenu(
        fileName = "JetpackInteraction",
        menuName = "Supernova/Player/Equipment Interactions/Jetpack")]
    public sealed class JetpackEquipmentInteraction : PlayerEquipmentInteraction
    {
        [SerializeField] private KeyCode toggleKey = KeyCode.V;
        [SerializeField, Min(0f)] private float initialLiftDistance = 0.3f;
        [SerializeField, Min(0.01f)] private float launchDuration = 1f;
        [SerializeField, Min(0f)] private float ascendSpeed = 3.5f;
        [SerializeField, Min(0f)] private float descendSpeed = 3.5f;
        [SerializeField] private AnimationClip launchAnimation;
        [SerializeField, Min(0.01f)] private float launchAnimationDuration = 1.5f;
        [SerializeField] private AnimationClip hoverAnimation;

        public override KeyCode ActivationKey => toggleKey;
        public override string InteractionHint =>
            "V TOGGLE  //  SPACE ASCEND  //  SHIFT DESCEND";
        public float InitialLiftDistance => Mathf.Max(0f, initialLiftDistance);
        public float LaunchDuration => Mathf.Max(0.01f, launchDuration);
        public float AscendSpeed => Mathf.Max(0f, ascendSpeed);
        public float DescendSpeed => Mathf.Max(0f, descendSpeed);
        public AnimationClip LaunchAnimation => launchAnimation;
        public float LaunchAnimationDuration => Mathf.Max(0.01f, launchAnimationDuration);
        public AnimationClip HoverAnimation => hoverAnimation;

        public AnimationClip GetLocomotionAnimation(float elapsedSeconds)
        {
            if (launchAnimation != null
                && Mathf.Max(0f, elapsedSeconds) < LaunchAnimationDuration)
            {
                return launchAnimation;
            }
            return hoverAnimation != null ? hoverAnimation : launchAnimation;
        }

        public float GetLaunchHeight(float elapsedSeconds)
        {
            float normalizedTime = Mathf.Clamp01(
                Mathf.Max(0f, elapsedSeconds) / LaunchDuration);
            return InitialLiftDistance
                * Mathf.SmoothStep(0f, 1f, normalizedTime);
        }

        public override PlayerEquipmentRuntime CreateRuntime(
            PlayerEquipmentController owner,
            PlayerEquipmentDefinition definition)
        {
            return new JetpackRuntime(owner, definition, this);
        }

        public Vector3 GetHoverVelocity(
            Vector3 planarMovement,
            float moveSpeed,
            bool ascendHeld,
            bool descendHeld)
        {
            float verticalSpeed = 0f;
            if (ascendHeld) verticalSpeed += AscendSpeed;
            if (descendHeld) verticalSpeed -= DescendSpeed;
            return planarMovement * Mathf.Max(0f, moveSpeed)
                + Vector3.up * verticalSpeed;
        }

        private sealed class JetpackRuntime : PlayerEquipmentRuntime
        {
            private readonly JetpackEquipmentInteraction settings;
            private bool hovering;
            private float launchElapsed;
            private float activationElapsed;

            public JetpackRuntime(
                PlayerEquipmentController owner,
                PlayerEquipmentDefinition definition,
                JetpackEquipmentInteraction settings)
                : base(owner, definition)
            {
                this.settings = settings;
            }

            public override bool OverridesLocomotion => hovering;
            public override AnimationClip LocomotionAnimation =>
                settings.GetLocomotionAnimation(activationElapsed);

            public override void TickInput()
            {
                if (Cursor.lockState == CursorLockMode.Locked
                    && Input.GetKeyDown(settings.ActivationKey))
                {
                    Trigger();
                }
            }

            public override void Trigger()
            {
                hovering = !hovering;
                launchElapsed = 0f;
                activationElapsed = 0f;
                SetThrustersActive(hovering);
            }

            public override void CancelLocomotionOverride()
            {
                hovering = false;
                launchElapsed = 0f;
                activationElapsed = 0f;
                SetThrustersActive(false);
            }

            public override void OnUnequipped()
            {
                hovering = false;
                launchElapsed = 0f;
                activationElapsed = 0f;
                SetThrustersActive(false);
            }

            public override bool TryHandleLocomotion(
                CharacterController characterController,
                Vector3 planarMovement,
                float moveSpeed,
                float deltaTime)
            {
                if (!hovering
                    || characterController == null
                    || !characterController.enabled
                    || !characterController.gameObject.activeInHierarchy)
                    return false;

                bool ascendHeld = Input.GetKey(KeyCode.Space);
                bool descendHeld = Input.GetKey(KeyCode.LeftShift)
                    || Input.GetKey(KeyCode.RightShift);
                Vector3 velocity = settings.GetHoverVelocity(
                    planarMovement,
                    moveSpeed,
                    ascendHeld,
                    descendHeld);
                float safeDeltaTime = Mathf.Max(0f, deltaTime);
                float previousLaunchHeight = settings.GetLaunchHeight(launchElapsed);
                launchElapsed = Mathf.Min(
                    settings.LaunchDuration,
                    launchElapsed + safeDeltaTime);
                float launchStep =
                    settings.GetLaunchHeight(launchElapsed) - previousLaunchHeight;
                characterController.Move(
                    velocity * safeDeltaTime + Vector3.up * launchStep);
                activationElapsed += safeDeltaTime;
                return true;
            }

            private void SetThrustersActive(bool active)
            {
                Owner.GetBackVisualComponent<PlayerEquipmentVisual>()
                    ?.SetInteractionActive(active);
            }
        }
    }
}
