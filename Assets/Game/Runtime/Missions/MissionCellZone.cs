using System.Collections;
using Supernova.Inputs;
using System.Collections.Generic;
using Supernova.Voxels;
using Supernova.UI;
using UnityEngine;

namespace Supernova.Missions
{
    [DisallowMultipleComponent]
    public sealed class MissionCellZone : MonoBehaviour
    {
        private MissionGameLoop owner;
        private bool homeMode;
        private bool tutorialExitMode;
        private bool sequenceRunning;
        private readonly HashSet<Collider> playerOverlaps =
            new HashSet<Collider>();

        public void Configure(MissionGameLoop missionOwner, bool isHome)
        {
            owner = missionOwner;
            homeMode = isHome;
            tutorialExitMode = false;
        }

        public void ConfigureTutorialExit(MissionGameLoop missionOwner)
        {
            owner = missionOwner;
            homeMode = false;
            tutorialExitMode = true;
        }

        public bool IsTutorialExitMode => tutorialExitMode;

        public void RefreshActionPrompt()
        {
            if (playerOverlaps.Count == 0)
                return;

            if (tutorialExitMode)
                owner?.ShowTutorialExitPrompt();
            else
                owner?.ShowCellActionPrompt(homeMode);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<VoxelPlayerController>() == null) return;
            if (playerOverlaps.Add(other) && playerOverlaps.Count == 1)
                RefreshActionPrompt();
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<VoxelPlayerController>() == null) return;
            playerOverlaps.Remove(other);
            if (playerOverlaps.Count == 0)
            {
                if (tutorialExitMode)
                    owner?.HideTutorialExitPrompt();
                else
                    owner?.HideCellActionPrompt(homeMode);
            }
        }

        private void Update()
        {
            if (tutorialExitMode)
            {
                if (playerOverlaps.Count == 0
                    || sequenceRunning
                    || GameHudController.IsGameplayInputBlocked
                    || !GameInput.Pressed(GameInputActionId.Interact))
                {
                    return;
                }

                sequenceRunning = owner != null && owner.EndTutorial();
                return;
            }

            if (!homeMode
                || playerOverlaps.Count == 0
                || sequenceRunning
                || !GameInput.Pressed(GameInputActionId.Interact))
                return;

            StartCoroutine(HomeLaunchSequence());
        }

        private IEnumerator HomeLaunchSequence()
        {
            if (owner == null || !owner.CanBeginCurrentMission)
            {
                owner?.ShowCellActionPrompt(true);
                yield break;
            }

            sequenceRunning = true;
            yield return new WaitForSeconds(1f);
            if (!owner.BeginFirstMission())
                sequenceRunning = false;
        }
    }
}
