using System.Collections;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.Missions
{
    [DisallowMultipleComponent]
    public sealed class MissionCellZone : MonoBehaviour
    {
        private MissionGameLoop owner;
        private bool homeMode;
        private bool playerInside;
        private bool sequenceRunning;
        private ProximitySlidingDoor[] proximityDoors;

        public void Configure(MissionGameLoop missionOwner, bool isHome)
        {
            owner = missionOwner;
            homeMode = isHome;
            FindDoors();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<VoxelPlayerController>() == null) return;
            playerInside = true;
            if (homeMode && !sequenceRunning) StartCoroutine(HomeLaunchSequence());
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<VoxelPlayerController>() != null) playerInside = false;
        }

        private void Update()
        {
            if (!homeMode && playerInside && Input.GetKeyDown(KeyCode.E))
                owner?.RequestEvacuation();
        }

        private IEnumerator HomeLaunchSequence()
        {
            sequenceRunning = true;
            // owner?.SetPrompt("DROP POD LOCKED · HATCH CLOSED");
            for (int i = 0; i < proximityDoors.Length; i++)
                proximityDoors[i].CloseForLaunch();
            yield return CloseDoors(0.65f);
            yield return new WaitForSeconds(0.35f);
            owner?.BeginFirstMission();
        }

        private IEnumerator CloseDoors(float duration)
        {
            yield return new WaitForSeconds(duration);
        }

        private void FindDoors()
        {
            proximityDoors = transform.parent
                .GetComponentsInChildren<ProximitySlidingDoor>(true);
        }
    }
}
