using UnityEngine;

namespace Supernova.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerProfile))]
    public sealed class BombThrower : MonoBehaviour
    {
        [SerializeField] private Transform throwOrigin;
        private PlayerProfile profile;
        private float nextThrowTime;

        private PlayerProfile Profile
        {
            get
            {
                if (profile == null) profile = GetComponent<PlayerProfile>();
                if (profile == null) profile = gameObject.AddComponent<PlayerProfile>();
                return profile;
            }
        }

        private void Awake()
        {
            if (throwOrigin == null)
            {
                Camera camera = GetComponentInChildren<Camera>(true);
                if (camera != null) throwOrigin = camera.transform;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying || Cursor.lockState != CursorLockMode.Locked) return;
            if (Input.GetKeyDown(Profile.ThrowKey)) Throw();
        }

        public TimedBomb Throw()
        {
            if (Profile.BombPrefab == null || throwOrigin == null || Time.time < nextThrowTime) return null;
            nextThrowTime = Time.time + Profile.ThrowCooldown;
            Vector3 position = throwOrigin.position + throwOrigin.forward * 0.75f;
            TimedBomb bomb = Instantiate(Profile.BombPrefab, position, Quaternion.identity);
            Vector3 velocity = throwOrigin.forward * Profile.ThrowSpeed
                + Vector3.up * Profile.UpwardThrowSpeed;
            bomb.Launch(velocity, Random.onUnitSphere * Profile.SpinSpeed);
            return bomb;
        }
    }
}
