using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Opens a sliding door when the player approaches and closes it after the player
    /// moves away. The authored Animator is disabled because this component owns the
    /// door leaf pose and keeps its collider synchronized with the visible mesh.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProximitySlidingDoor : MonoBehaviour
    {
        [SerializeField] private Transform doorLeaf;
        [SerializeField] private Transform player;
        [SerializeField] private Vector3 openLocalOffset = new Vector3(0f, -3f, 0f);
        [SerializeField, Min(0.1f)] private float openingDistance = 1.8f;
        [SerializeField, Min(0.1f)] private float closingDistance = 3f;
        [SerializeField, Min(0.1f)] private float travelSpeed = 3.5f;

        private Vector3 closedLocalPosition;
        private Vector3 activationLocalPosition;
        private CharacterController playerController;
        private AudioSource doorAudio;
        private bool initialized;
        private bool openRequested;

        public bool IsOpenRequested => openRequested;
        public Transform DoorLeaf => doorLeaf;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Configure(
            Transform leaf,
            Transform playerTransform,
            Vector3 localOpenOffset,
            float openDistance,
            float closeDistance,
            float speed)
        {
            doorLeaf = leaf;
            player = playerTransform;
            openLocalOffset = localOpenOffset;
            openingDistance = Mathf.Max(0.1f, openDistance);
            closingDistance = Mathf.Max(openingDistance + 0.1f, closeDistance);
            travelSpeed = Mathf.Max(0.1f, speed);
            initialized = false;
            playerController = null;
            EnsureInitialized();
        }

        public void Tick(float deltaTime)
        {
            if (!EnsureInitialized())
            {
                return;
            }

            ResolvePlayer();
            if (player == null)
            {
                return;
            }

            Vector3 playerPosition = playerController != null
                ? playerController.bounds.center
                : player.position;
            float distance = Vector3.Distance(
                playerPosition,
                transform.TransformPoint(activationLocalPosition));

            if (openRequested)
            {
                if (distance >= closingDistance)
                {
                    openRequested = false;
                    PlayDoorSound();
                }
            }
            else if (distance <= openingDistance)
            {
                openRequested = true;
                PlayDoorSound();
            }

            Vector3 target = closedLocalPosition
                + (openRequested ? openLocalOffset : Vector3.zero);
            doorLeaf.localPosition = Vector3.MoveTowards(
                doorLeaf.localPosition,
                target,
                travelSpeed * Mathf.Max(0f, deltaTime));
        }

        private bool EnsureInitialized()
        {
            if (initialized)
            {
                return doorLeaf != null;
            }

            if (doorLeaf == null)
            {
                Animator childAnimator = GetComponentInChildren<Animator>(true);
                if (childAnimator != null)
                {
                    doorLeaf = childAnimator.transform;
                }
            }

            if (doorLeaf == null)
            {
                return false;
            }

            Animator animator = doorLeaf.GetComponent<Animator>();
            if (animator != null)
            {
                animator.enabled = false;
            }

            closedLocalPosition = doorLeaf.localPosition;
            Collider doorCollider = doorLeaf.GetComponent<Collider>();
            Vector3 activationWorldPosition = doorCollider != null
                ? doorCollider.bounds.center
                : doorLeaf.position;
            activationLocalPosition =
                transform.InverseTransformPoint(activationWorldPosition);
            doorAudio = GetComponent<AudioSource>();
            closingDistance = Mathf.Max(openingDistance + 0.1f, closingDistance);
            initialized = true;
            return true;
        }

        private void PlayDoorSound()
        {
            if (doorAudio != null && doorAudio.clip != null)
            {
                doorAudio.Play();
            }
        }

        private void ResolvePlayer()
        {
            if (player == null)
            {
                VoxelPlayerController controller =
                    FindObjectOfType<VoxelPlayerController>();
                if (controller != null)
                {
                    player = controller.transform;
                }
            }

            if (player != null
                && (playerController == null
                    || playerController.transform != player))
            {
                playerController = player.GetComponent<CharacterController>();
            }
        }

        private void OnValidate()
        {
            openingDistance = Mathf.Max(0.1f, openingDistance);
            closingDistance = Mathf.Max(openingDistance + 0.1f, closingDistance);
            travelSpeed = Mathf.Max(0.1f, travelSpeed);
        }
    }
}
