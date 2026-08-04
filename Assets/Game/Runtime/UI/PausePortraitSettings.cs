using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Supernova.UI
{
    public enum PausePoseSelectionMode
    {
        Sequential,
        Random,
        Fixed
    }

    [Serializable]
    public sealed class PausePortraitAnimationCurves
    {
        [Tooltip("Target portrait local X offset over the normalized intro time.")]
        [SerializeField] private AnimationCurve horizontalOffset =
            AnimationCurve.EaseInOut(0f, -0.35f, 1f, 0f);
        [Tooltip("Target portrait local Y offset over the normalized intro time.")]
        [SerializeField] private AnimationCurve verticalOffset =
            AnimationCurve.EaseInOut(0f, 0.12f, 1f, 0f);
        [Tooltip("Target portrait local Z offset over the normalized intro time.")]
        [FormerlySerializedAs("dollyOffset")]
        [SerializeField] private AnimationCurve depthOffset =
            AnimationCurve.EaseInOut(0f, -0.65f, 1f, 0f);
        [Tooltip("Additional target portrait pitch in degrees.")]
        [SerializeField] private AnimationCurve pitch =
            AnimationCurve.EaseInOut(0f, 2f, 1f, 0f);
        [Tooltip("Additional target portrait yaw in degrees.")]
        [SerializeField] private AnimationCurve yaw =
            AnimationCurve.EaseInOut(0f, -4f, 1f, 0f);
        [Tooltip("Additional target portrait roll in degrees.")]
        [SerializeField] private AnimationCurve roll =
            AnimationCurve.EaseInOut(0f, -4f, 1f, 0f);
        [Tooltip("Uniform target portrait scale offset in percent (6 means 6% larger).")]
        [FormerlySerializedAs("fieldOfViewOffset")]
        [SerializeField] private AnimationCurve scalePercentOffset =
            AnimationCurve.EaseInOut(0f, 6f, 1f, 0f);

        public Vector3 EvaluateLocalPosition(float normalizedTime)
        {
            float time = Mathf.Clamp01(normalizedTime);
            return new Vector3(
                Evaluate(horizontalOffset, time),
                Evaluate(verticalOffset, time),
                Evaluate(depthOffset, time));
        }

        public Vector3 EvaluateLocalEulerAngles(float normalizedTime)
        {
            float time = Mathf.Clamp01(normalizedTime);
            return new Vector3(
                Evaluate(pitch, time),
                Evaluate(yaw, time),
                Evaluate(roll, time));
        }

        public float EvaluateScaleMultiplier(float normalizedTime)
        {
            float percentOffset = Evaluate(scalePercentOffset, Mathf.Clamp01(normalizedTime));
            return Mathf.Max(0f, 1f + percentOffset * 0.01f);
        }

        private static float Evaluate(AnimationCurve curve, float time)
        {
            return curve != null && curve.length > 0 ? curve.Evaluate(time) : 0f;
        }
    }

    [Serializable]
    public sealed class PausePoseDefinition
    {
        [SerializeField] private string displayName = "Pause Pose";
        [Tooltip("Any Humanoid AnimationClip can be assigned here.")]
        [SerializeField] private AnimationClip clip;
        [Tooltip("Normalized point at which the animation freezes: 0 is the start, 1 is the end.")]
        [SerializeField, Range(0f, 1f)] private float holdNormalizedTime = 0.995f;
        [Tooltip("Portrait rotation around the vertical axis for this pose.")]
        [SerializeField, Range(-180f, 180f)] private float portraitYaw = -8f;
        [Tooltip("Target portrait animation evaluated from 0 to 1 during the pause intro.")]
        [FormerlySerializedAs("cameraAnimation")]
        [SerializeField] private PausePortraitAnimationCurves portraitAnimation =
            new PausePortraitAnimationCurves();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? (clip != null ? clip.name : "Pause Pose")
            : displayName;
        public AnimationClip Clip => clip;
        public float HoldNormalizedTime => Mathf.Clamp01(holdNormalizedTime);
        public float PortraitYaw => portraitYaw;
        public PausePortraitAnimationCurves PortraitAnimation => portraitAnimation;
    }

    [CreateAssetMenu(
        fileName = "PausePortraitSettings",
        menuName = "Supernova/UI/Pause Portrait Settings")]
    public sealed class PausePortraitSettings : ScriptableObject
    {
        [Header("Portrait Assets")]
        [SerializeField] private GameObject portraitPrefab;
        [SerializeField] private RuntimeAnimatorController poseController;

        [Header("Pause Pose List")]
        [SerializeField] private List<PausePoseDefinition> pausePoses =
            new List<PausePoseDefinition>();
        [SerializeField] private PausePoseSelectionMode selectionMode =
            PausePoseSelectionMode.Sequential;
        [Tooltip("Used only when Selection Mode is Fixed.")]
        [SerializeField, Min(0)] private int fixedPoseIndex;

        public GameObject PortraitPrefab => portraitPrefab;
        public RuntimeAnimatorController PoseController => poseController;
        public int PoseCount => pausePoses != null ? pausePoses.Count : 0;

        public PausePoseDefinition SelectPose(int sequenceIndex)
        {
            if (pausePoses == null || pausePoses.Count == 0)
                return null;

            int index;
            switch (selectionMode)
            {
                case PausePoseSelectionMode.Random:
                    index = UnityEngine.Random.Range(0, pausePoses.Count);
                    break;
                case PausePoseSelectionMode.Fixed:
                    index = Mathf.Clamp(fixedPoseIndex, 0, pausePoses.Count - 1);
                    break;
                default:
                    index = Mathf.Abs(sequenceIndex) % pausePoses.Count;
                    break;
            }
            return pausePoses[index];
        }
    }
}
