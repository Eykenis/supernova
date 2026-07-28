using System;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Owns the player's equipment slots and the runtime behaviour created by equipped assets.
    /// The first supported slot is Back; the API intentionally remains slot-based for expansion.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerEquipmentController : MonoBehaviour
    {
        [Header("Available Equipment")]
        [SerializeField] private PlayerEquipmentDefinition[] availableEquipment =
            Array.Empty<PlayerEquipmentDefinition>();
        [SerializeField] private PlayerEquipmentDefinition startingBackEquipment;

        [Header("Visual Mount")]
        [Tooltip("Optional explicit mount. A humanoid chest/spine bone is used when left empty.")]
        [SerializeField] private Transform backMountOverride;

        private CharacterController characterController;
        private Animator animator;
        private PlayerEquipmentDefinition equippedBack;
        private PlayerEquipmentRuntime backRuntime;
        private GameObject backVisual;

        public event Action<PlayerEquipmentSlot, PlayerEquipmentDefinition> EquipmentChanged;

        public CharacterController CharacterController
        {
            get
            {
                ResolveReferences();
                return characterController;
            }
        }

        public PlayerEquipmentDefinition EquippedBack => equippedBack;
        public PlayerEquipmentDefinition AvailableBack => FindFirstAvailable(PlayerEquipmentSlot.Back);
        public bool HasBackEquipment => equippedBack != null;
        public bool IsLocomotionOverrideActive =>
            backRuntime != null && backRuntime.OverridesLocomotion;
        public AnimationClip ActiveLocomotionAnimation =>
            backRuntime != null ? backRuntime.LocomotionAnimation : null;

        private void Awake()
        {
            ResolveReferences();
            if (Application.isPlaying && startingBackEquipment != null)
                Equip(startingBackEquipment);
        }

        private void OnEnable()
        {
            if (Application.isPlaying && equippedBack == null && startingBackEquipment != null)
                Equip(startingBackEquipment);
        }

        private void OnDisable()
        {
            CancelActiveLocomotionOverride();
        }

        private void OnDestroy()
        {
            ReleaseBackEquipment(false);
        }

        public bool Equip(PlayerEquipmentDefinition definition)
        {
            if (definition == null || definition.Slot != PlayerEquipmentSlot.Back)
                return false;
            if (equippedBack == definition)
                return true;

            ReleaseBackEquipment(false);
            equippedBack = definition;
            backRuntime = definition.CreateRuntime(this);
            CreateBackVisual(definition);
            backRuntime?.OnEquipped();
            EquipmentChanged?.Invoke(PlayerEquipmentSlot.Back, equippedBack);
            return true;
        }

        public bool Unequip(PlayerEquipmentSlot slot)
        {
            if (slot != PlayerEquipmentSlot.Back || equippedBack == null)
                return false;

            ReleaseBackEquipment(true);
            return true;
        }

        public bool ToggleBackEquipment()
        {
            if (equippedBack != null)
                return Unequip(PlayerEquipmentSlot.Back);

            PlayerEquipmentDefinition definition = AvailableBack;
            return definition != null && Equip(definition);
        }

        public void TickEquippedInteraction()
        {
            backRuntime?.TickInput();
        }

        public void TriggerEquippedInteraction()
        {
            backRuntime?.Trigger();
        }

        public void CancelActiveLocomotionOverride()
        {
            backRuntime?.CancelLocomotionOverride();
        }

        public T GetBackVisualComponent<T>() where T : Component
        {
            return backVisual != null ? backVisual.GetComponentInChildren<T>(true) : null;
        }

        public bool TryHandleLocomotion(
            CharacterController controller,
            Vector3 planarMovement,
            float moveSpeed,
            float deltaTime)
        {
            return backRuntime != null
                && backRuntime.TryHandleLocomotion(
                    controller,
                    planarMovement,
                    moveSpeed,
                    deltaTime);
        }

        private void ReleaseBackEquipment(bool notify)
        {
            backRuntime?.OnUnequipped();
            backRuntime = null;
            equippedBack = null;

            if (backVisual != null)
            {
                if (Application.isPlaying) Destroy(backVisual);
                else DestroyImmediate(backVisual);
                backVisual = null;
            }

            if (notify)
                EquipmentChanged?.Invoke(PlayerEquipmentSlot.Back, null);
        }

        private PlayerEquipmentDefinition FindFirstAvailable(PlayerEquipmentSlot slot)
        {
            if (availableEquipment == null)
                return null;

            for (int i = 0; i < availableEquipment.Length; i++)
            {
                PlayerEquipmentDefinition definition = availableEquipment[i];
                if (definition != null && definition.Slot == slot)
                    return definition;
            }

            return null;
        }

        private void CreateBackVisual(PlayerEquipmentDefinition definition)
        {
            if (definition.VisualPrefab == null)
                return;

            PlayerEquipmentVisual visualTemplate =
                definition.VisualPrefab.GetComponent<PlayerEquipmentVisual>();
            Transform mount = visualTemplate != null
                && visualTemplate.MountAtCharacterRoot
                && animator != null
                    ? animator.transform
                    : ResolveBackMount();
            backVisual = Instantiate(definition.VisualPrefab, mount);
            backVisual.name = definition.DisplayName + " (Equipped)";
            Transform visualTransform = backVisual.transform;
            visualTransform.localPosition = definition.LocalPosition;
            visualTransform.localRotation = Quaternion.Euler(definition.LocalEulerAngles);
            visualTransform.localScale = definition.LocalScale;
            backVisual.GetComponent<PlayerEquipmentVisual>()?.Bind(animator);
        }

        private Transform ResolveBackMount()
        {
            if (backMountOverride != null)
                return backMountOverride;

            ResolveReferences();
            if (animator != null && animator.isHuman)
            {
                Transform mount = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                if (mount == null) mount = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (mount == null) mount = animator.GetBoneTransform(HumanBodyBones.Spine);
                if (mount != null) return mount;
            }

            return transform;
        }

        private void ResolveReferences()
        {
            if (characterController == null)
                characterController = GetComponent<CharacterController>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);
        }
    }
}
