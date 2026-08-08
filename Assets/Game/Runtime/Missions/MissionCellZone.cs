using System.Collections;
using System.Collections.Generic;
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
        private bool sequenceRunning;
        private ProximitySlidingDoor[] proximityDoors;
        private readonly HashSet<Collider> playerOverlaps =
            new HashSet<Collider>();

        public void Configure(MissionGameLoop missionOwner, bool isHome)
        {
            owner = missionOwner;
            homeMode = isHome;
            FindDoors();
        }

        public void RefreshActionPrompt()
        {
            if (playerOverlaps.Count > 0)
                owner?.ShowCellActionPrompt(homeMode);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<VoxelPlayerController>() == null) return;
            if (playerOverlaps.Add(other) && playerOverlaps.Count == 1)
                owner?.ShowCellActionPrompt(homeMode);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<VoxelPlayerController>() == null) return;
            playerOverlaps.Remove(other);
            if (playerOverlaps.Count == 0)
                owner?.HideCellActionPrompt(homeMode);
        }

        private void Update()
        {
            if (playerOverlaps.Count == 0
                || sequenceRunning
                || !Input.GetKeyDown(KeyCode.E))
                return;

            if (homeMode)
            {
                StartCoroutine(HomeLaunchSequence());
                return;
            }

            owner?.RequestEvacuation();
        }

        private IEnumerator HomeLaunchSequence()
        {
            if (owner == null || !owner.CanBeginCurrentMission)
            {
                owner?.ShowCellActionPrompt(true);
                yield break;
            }

            sequenceRunning = true;
            // owner?.SetPrompt("DROP POD LOCKED · HATCH CLOSED");
            for (int i = 0; i < proximityDoors.Length; i++)
                proximityDoors[i].CloseForLaunch();
            yield return CloseDoors(0.65f);
            yield return new WaitForSeconds(0.35f);
            if (!owner.BeginFirstMission())
                sequenceRunning = false;
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
