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
        private Transform[] doors;
        private Vector3[] closedPositions;

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
            owner?.SetPrompt("降落舱已锁定 · 舱门关闭");
            for (int i = 0; i < proximityDoors.Length; i++)
                proximityDoors[i].enabled = false;
            yield return CloseDoors(0.65f);
            yield return new WaitForSeconds(0.35f);
            owner?.BeginFirstMission();
        }

        private IEnumerator CloseDoors(float duration)
        {
            if (doors == null || doors.Length == 0) yield break;
            Vector3[] openPositions = new Vector3[doors.Length];
            for (int i = 0; i < doors.Length; i++) openPositions[i] = doors[i].localPosition;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                for (int i = 0; i < doors.Length; i++)
                    doors[i].localPosition = Vector3.Lerp(openPositions[i], closedPositions[i], t);
                yield return null;
            }
        }

        private void FindDoors()
        {
            proximityDoors = transform.parent
                .GetComponentsInChildren<ProximitySlidingDoor>(true);
            var found = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < proximityDoors.Length; i++)
            {
                Transform leaf = proximityDoors[i].DoorLeaf;
                if (leaf != null) found.Add(leaf);
            }
            doors = found.ToArray();
            closedPositions = new Vector3[doors.Length];
            for (int i = 0; i < doors.Length; i++) closedPositions[i] = doors[i].localPosition;
        }
    }
}
