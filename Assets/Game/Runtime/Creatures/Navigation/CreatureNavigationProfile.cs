using System;
using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures.Navigation
{
    /// <summary>
    /// Per-creature navigation limits and locomotion tuning. Lives on the
    /// creature prefab because it describes how that body moves, while
    /// MonsterSpawnDefinition stays focused on how the monster is placed.
    /// </summary>
    [Serializable]
    public sealed class CreatureNavigationProfile
    {
        [Header("Traversal Limits (voxels)")]
        [Tooltip("Highest layer difference the creature may climb in one step.")]
        [SerializeField, Min(0)] private int maximumJumpHeight = 1;
        [Tooltip("Deepest layer difference the creature may drop in one step.")]
        [SerializeField, Min(0)] private int maximumSafeFall = 3;
        [Tooltip(
            "Rises up to this many layers are walked over rather than jumped. "
            + "Voxels are far smaller than a creature here, so an interpolated "
            + "slope quantises into alternating layers that the collider simply "
            + "steps across. Only rises above this height request a jump.")]
        [SerializeField, Min(0)] private int stepUpHeight = 1;

        [Header("Search")]
        [Tooltip("Node expansions before the search settles for the node closest "
            + "to the target.")]
        [SerializeField, Min(16)] private int visitLimit = 256;

        [Header("Locomotion")]
        [Tooltip("Travel speed in voxels per second. The default of three matches "
            + "the example creature's 1.26 m/s animation reference speed at a "
            + "0.42 voxel size, so movement and animation agree.")]
        [SerializeField, Min(0.1f)] private float moveSpeed = 3f;
        [Tooltip("Voxels per second squared the horizontal velocity converges at. "
            + "Must be well above the move speed or the creature never reaches it "
            + "between replans.")]
        [SerializeField, Min(1f)] private float acceleration = 24f;

        [Header("Replanning")]
        [SerializeField, Min(0.05f)] private float minimumReplanInterval = 0.25f;
        [SerializeField, Min(0.05f)] private float maximumReplanInterval = 0.6f;
        [Tooltip("Voxels the target may drift before the path is rebuilt early.")]
        [SerializeField, Min(1f)] private float targetDriftThreshold = 4f;

        [Header("Wander")]
        [SerializeField, Min(1f)] private float wanderRadius = 10f;
        [SerializeField, Min(1)] private int wanderVerticalRange = 7;
        [SerializeField, Min(1)] private int wanderAttempts = 10;
        [Tooltip("Idle pause after a wander target could not be sampled.")]
        [SerializeField, Min(0f)] private float wanderRetryInterval = 1.5f;

        [Header("Stuck Recovery")]
        [Tooltip("Seconds of being blocked before a recovery jump. Long values keep "
            + "normal turning, crowding and slope contact from reading as stuck; "
            + "short values make recovery snappier at the risk of false jumps.")]
        [SerializeField, Min(0.01f)] private float stuckCheckInterval = 3f;
        [Tooltip("Jump attempts before the path is abandoned and replanned.")]
        [SerializeField, Min(0)] private int stuckJumpAttempts = 2;
        [Tooltip("Fraction of the speed this creature was commanded to travel at. "
            + "Below it the creature counts as blocked. Compared against the "
            + "commanded speed, not the animation reference speed, so the two "
            + "sides of the test share one metric.")]
        [SerializeField, Range(0.01f, 0.9f)] private float stuckSpeedFraction = 0.15f;

        public int MaximumJumpHeight => Mathf.Max(0, maximumJumpHeight);
        public int MaximumSafeFall => Mathf.Max(0, maximumSafeFall);
        public int StepUpHeight => Mathf.Clamp(stepUpHeight, 0, MaximumJumpHeight);
        public int VisitLimit => Mathf.Max(16, visitLimit);
        public float MoveSpeed => Mathf.Max(0.1f, moveSpeed);
        public float Acceleration => Mathf.Max(1f, acceleration);
        public float MinimumReplanInterval => Mathf.Max(0.05f, minimumReplanInterval);
        public float MaximumReplanInterval =>
            Mathf.Max(MinimumReplanInterval, maximumReplanInterval);
        public float TargetDriftThreshold => Mathf.Max(1f, targetDriftThreshold);
        public float WanderRadius => Mathf.Max(1f, wanderRadius);
        public int WanderVerticalRange => Mathf.Max(1, wanderVerticalRange);
        public int WanderAttempts => Mathf.Max(1, wanderAttempts);
        public float WanderRetryInterval => Mathf.Max(0f, wanderRetryInterval);
        public float StuckCheckInterval => Mathf.Max(0.01f, stuckCheckInterval);
        public int StuckJumpAttempts => Mathf.Max(0, stuckJumpAttempts);
        public float StuckSpeedFraction => Mathf.Clamp(stuckSpeedFraction, 0.01f, 0.9f);
    }
}
