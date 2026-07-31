using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Opens a sliding door when the player approaches and closes it after the player
    /// moves away. The Animator owns the door pose; this component only selects the
    /// playback direction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProximitySlidingDoor : MonoBehaviour
    {
        private static readonly int PlaybackSpeed =
            Animator.StringToHash("PlaybackSpeed");

        [SerializeField] private Transform doorLeaf;
        [SerializeField] private Animator doorAnimator;
        [SerializeField] private Transform player;
        [SerializeField, Min(0.1f)] private float openingDistance = 1.8f;
        [SerializeField, Min(0.1f)] private float closingDistance = 3f;
        [Tooltip("Once opened, this door stays open for the rest of the scene.")]
        [SerializeField] private bool stayOpenAfterFirstOpen;

        private Vector3 activationLocalPosition;
        private CharacterController playerController;
        private AudioSource doorAudio;
        private bool initialized;
        private bool openRequested;
        private bool hasOpened;

        public bool IsOpenRequested => openRequested;
        public Transform DoorLeaf => doorLeaf;
        public Animator DoorAnimator => doorAnimator;
        public bool StayOpenAfterFirstOpen => stayOpenAfterFirstOpen;

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
            float openDistance,
            float closeDistance)
        {
            doorLeaf = leaf;
            doorAnimator = leaf != null ? leaf.GetComponent<Animator>() : null;
            player = playerTransform;
            openingDistance = Mathf.Max(0.1f, openDistance);
            closingDistance = Mathf.Max(openingDistance + 0.1f, closeDistance);
            initialized = false;
            playerController = null;
            EnsureInitialized();
        }

        public void Tick(float unusedDeltaTime)
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
                if (!stayOpenAfterFirstOpen && distance >= closingDistance)
                {
                    SetOpenRequested(false);
                    PlayDoorSound();
                }
            }
            else if (distance <= openingDistance)
            {
                SetOpenRequested(true);
                hasOpened = true;
                PlayDoorSound();
            }

            if (stayOpenAfterFirstOpen && hasOpened)
            {
                SetOpenRequested(true);
            }
        }

        public void SetStayOpenAfterFirstOpen(bool value)
        {
            stayOpenAfterFirstOpen = value;
            if (value && hasOpened) SetOpenRequested(true);
        }

        public void CloseForLaunch()
        {
            stayOpenAfterFirstOpen = false;
            SetOpenRequested(false);
            enabled = false;
        }

        private bool EnsureInitialized()
        {
            if (initialized)
            {
                return doorLeaf != null;
            }

            if (doorLeaf == null)
            {
                doorAnimator = GetComponentInChildren<Animator>(true);
                if (doorAnimator != null)
                {
                    doorLeaf = doorAnimator.transform;
                }
            }

            if (doorLeaf == null)
            {
                return false;
            }

            if (doorAnimator == null)
            {
                doorAnimator = doorLeaf.GetComponent<Animator>();
            }

            if (doorAnimator == null
                || doorAnimator.runtimeAnimatorController == null)
            {
                return false;
            }

            doorAnimator.enabled = true;
            doorAnimator.applyRootMotion = false;
            doorAnimator.Play(0, 0, 0f);
            doorAnimator.Update(0f);
            doorAnimator.SetFloat(PlaybackSpeed, 0f);
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

        private void SetOpenRequested(bool value)
        {
            if (openRequested == value)
            {
                return;
            }

            openRequested = value;
            if (doorAnimator != null)
            {
                doorAnimator.SetFloat(PlaybackSpeed, value ? 1f : -1f);
            }
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
        }
    }
}
