using UnityEngine;

namespace Supernova.Gameplay
{
    public enum PlayerEquipmentSlot
    {
        Back = 0,
    }

    /// <summary>
    /// Data shared by every equipped instance. Behaviour stays in a separate interaction asset,
    /// so designers can pair a visual definition with a custom gameplay script.
    /// </summary>
    [CreateAssetMenu(
        fileName = "PlayerEquipmentDefinition",
        menuName = "Supernova/Player/Equipment Definition")]
    public sealed class PlayerEquipmentDefinition : ScriptableObject
    {
        [SerializeField] private string equipmentId = "equipment";
        [SerializeField] private string displayName = "Equipment";
        [SerializeField] private PlayerEquipmentSlot slot = PlayerEquipmentSlot.Back;
        [SerializeField] private GameObject visualPrefab;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private Vector3 localScale = Vector3.one;
        [SerializeField] private PlayerEquipmentInteraction interaction;

        public string EquipmentId => string.IsNullOrWhiteSpace(equipmentId)
            ? name
            : equipmentId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName;
        public PlayerEquipmentSlot Slot => slot;
        public GameObject VisualPrefab => visualPrefab;
        public Vector3 LocalPosition => localPosition;
        public Vector3 LocalEulerAngles => localEulerAngles;
        public Vector3 LocalScale => localScale;
        public PlayerEquipmentInteraction Interaction => interaction;
        public KeyCode ActivationKey => interaction != null
            ? interaction.ActivationKey
            : KeyCode.None;
        public string InteractionHint => interaction != null
            ? interaction.InteractionHint
            : string.Empty;

        public PlayerEquipmentRuntime CreateRuntime(PlayerEquipmentController owner)
        {
            return interaction != null ? interaction.CreateRuntime(owner, this) : null;
        }
    }

    /// <summary>
    /// Extend this ScriptableObject for equipment-specific behaviour. The asset contains tuning
    /// values while CreateRuntime returns per-player state.
    /// </summary>
    public abstract class PlayerEquipmentInteraction : ScriptableObject
    {
        public abstract KeyCode ActivationKey { get; }
        public abstract string InteractionHint { get; }

        public abstract PlayerEquipmentRuntime CreateRuntime(
            PlayerEquipmentController owner,
            PlayerEquipmentDefinition definition);
    }

    /// <summary>
    /// Per-player equipment state created from a PlayerEquipmentInteraction asset.
    /// </summary>
    public abstract class PlayerEquipmentRuntime
    {
        protected PlayerEquipmentRuntime(
            PlayerEquipmentController owner,
            PlayerEquipmentDefinition definition)
        {
            Owner = owner;
            Definition = definition;
        }

        protected PlayerEquipmentController Owner { get; }
        protected PlayerEquipmentDefinition Definition { get; }

        public virtual bool OverridesLocomotion => false;
        public virtual AnimationClip LocomotionAnimation => null;

        public virtual void OnEquipped()
        {
        }

        public virtual void OnUnequipped()
        {
        }

        public virtual void TickInput()
        {
        }

        public virtual void Trigger()
        {
        }

        public virtual void CancelLocomotionOverride()
        {
        }

        public virtual bool TryHandleLocomotion(
            CharacterController characterController,
            Vector3 planarMovement,
            float moveSpeed,
            float deltaTime)
        {
            return false;
        }
    }
}
