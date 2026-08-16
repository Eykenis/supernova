using Supernova.Inputs;
using Supernova.UI;
using UnityEngine;

namespace Supernova.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class WeaponPickup : MonoBehaviour
    {
        [SerializeField] private PlayerToolDefinition definition;
        [SerializeField, Min(0.1f)] private float interactionDistance = 2.4f;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private float displaySpinDegreesPerSecond = 24f;

        private PlayerToolController player;
        private Camera viewCamera;

        public PlayerToolDefinition Definition => definition;
        public PlayerInventoryItem Item => definition != null
            ? definition.Item
            : PlayerInventoryItem.Empty;
        public float InteractionDistance => interactionDistance;

        public void Configure(
            PlayerToolDefinition value,
            Transform display,
            GameObject prompt)
        {
            definition = value;
            visualRoot = display;
            promptRoot = prompt;
            SetPromptVisible(false);
        }

        private void OnEnable()
        {
            SetPromptVisible(false);
        }

        private void Update()
        {
            if (definition == null)
            {
                SetPromptVisible(false);
                return;
            }

            if (visualRoot != null)
            {
                visualRoot.Rotate(
                    Vector3.up,
                    displaySpinDegreesPerSecond * Time.deltaTime,
                    Space.World);
            }

            ResolvePlayer();
            bool canCollect = player != null
                && !player.OwnsItem(definition.Item)
                && (player.transform.position - transform.position)
                    .sqrMagnitude
                    <= interactionDistance * interactionDistance;
            SetPromptVisible(canCollect);
            FacePromptTowardsCamera();

            if (canCollect
                && !GameHudController.IsGameplayInputBlocked
                && GameInput.Pressed(GameInputActionId.Interact))
            {
                TryCollect(player);
            }
        }

public bool TryCollect(PlayerToolController target)
        {
            if (target == null
                || !IsSupportedPickup(definition)
                || !target.TryAddOwnedItem(definition.Item))
            {
                return false;
            }

            SetPromptVisible(false);
            gameObject.SetActive(false);
            if (Application.isPlaying)
                Destroy(gameObject);
            return true;
        }

        private static bool IsSupportedPickup(PlayerToolDefinition value)
        {
            return value != null
                && (value.IsFirearm
                    || value.Item == PlayerInventoryItem.Bomb);
        }

        private void ResolvePlayer()
        {
            if (player == null)
                player = FindObjectOfType<PlayerToolController>();
        }

        private void SetPromptVisible(bool visible)
        {
            if (promptRoot != null && promptRoot.activeSelf != visible)
                promptRoot.SetActive(visible);
        }

        private void FacePromptTowardsCamera()
        {
            if (promptRoot == null || !promptRoot.activeSelf)
                return;
            if (viewCamera == null)
                viewCamera = Camera.main;
            if (viewCamera == null)
                return;

            Vector3 awayFromCamera =
                promptRoot.transform.position
                - viewCamera.transform.position;
            if (awayFromCamera.sqrMagnitude > 0.0001f)
            {
                promptRoot.transform.rotation =
                    Quaternion.LookRotation(awayFromCamera.normalized);
            }
        }
    }
}
