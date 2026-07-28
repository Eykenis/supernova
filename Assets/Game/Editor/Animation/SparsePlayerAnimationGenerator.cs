#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityAnimatorController = UnityEditor.Animations.AnimatorController;
using UnityEngine;

namespace Supernova.EditorTools.Animation
{
    /// <summary>
    /// Generates full-body clips from only a handful of authored poses.
    /// Unspecified bones remain in the prefab bind pose; Unity's quaternion curves
    /// provide the in-betweens between sparse poses.
    /// </summary>
    public static class SparsePlayerAnimationGenerator
    {
        public const string PlayerPrefabPath = ProjectAssetPaths.Prefabs.Player;
        public const string ControllerPath = ProjectAssetPaths.ThirdParty.MuryotaisuController;
        public const string OutputFolder = ProjectAssetPaths.Folders.GeneratedPlayerAnimations;
        public const string RunClipPath = OutputFolder + "/SparseRun.anim";
        public const string MineClipPath = OutputFolder + "/SparseMine.anim";
        public const string KnockdownClipPath = OutputFolder + "/SparseKnockdown.anim";

        private sealed class Pose
        {
            public readonly float time;
            public readonly Dictionary<string, Vector3> rotations = new Dictionary<string, Vector3>();
            public readonly Dictionary<string, Vector3> positions = new Dictionary<string, Vector3>();

            public Pose(float time) { this.time = time; }
            public Pose R(string bone, float x, float y = 0f, float z = 0f) { rotations[bone] = new Vector3(x, y, z); return this; }
            public Pose P(string bone, float x, float y, float z) { positions[bone] = new Vector3(x, y, z); return this; }
        }

        [MenuItem("Tools/Supernova/Animation/Generate Sparse Player Animations")]
        public static void Generate()
        {
            EnsureFolder(OutputFolder);
            GameObject instance = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null) throw new InvalidOperationException("Player prefab has no child Animator.");

                AnimationClip run = BuildClip(animator, "SparseRun", true, BuildRunPoses());
                AnimationClip mine = BuildClip(animator, "SparseMine", false, BuildMinePoses());
                AnimationClip knockdown = BuildClip(animator, "SparseKnockdown", false, BuildKnockdownPoses());

                SaveClip(run, RunClipPath);
                SaveClip(mine, MineClipPath);
                SaveClip(knockdown, KnockdownClipPath);
                ConfigureController(run, mine, knockdown);
                BindPlayerController(instance, animator);
                PrefabUtility.SaveAsPrefabAsset(instance, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(instance);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Generated sparse-pose Run, Mine and Knockdown animations for Player.");
        }

        private static Pose[] BuildRunPoses()
        {
            // Four distinct poses plus a duplicated loop closure. Only 13 major bones move.
            return new[]
            {
                RunPose(0.00f,  1f, 0.00f),
                RunPose(0.12f,  0f, 0.035f),
                RunPose(0.24f, -1f, 0.00f),
                RunPose(0.36f,  0f, 0.035f),
                RunPose(0.48f,  1f, 0.00f)
            };
        }

        private static Pose RunPose(float time, float phase, float bounce)
        {
            float lead = 38f * phase;
            float trail = -30f * phase;
            float kneeLead = phase > 0f ? -10f : (phase < 0f ? -52f : -34f);
            float kneeTrail = phase > 0f ? -52f : (phase < 0f ? -10f : -34f);
            return new Pose(time)
                .P("Hips", 0f, bounce, 0f)
                .R("Hips", 8f, 0f, -5f * phase)
                .R("Spine", -9f, 0f, 5f * phase)
                .R("Chest", -10f, 0f, 7f * phase)
                .R("UpperLeg.L", lead).R("LowerLeg.L", kneeLead).R("Foot.L", -12f * phase)
                .R("UpperLeg.R", trail).R("LowerLeg.R", kneeTrail).R("Foot.R", 12f * phase)
                .R("UpperArm.L", -42f * phase, 0f, -8f).R("LowerArm.L", -28f, 0f, 0f)
                .R("UpperArm.R", 42f * phase, 0f, 8f).R("LowerArm.R", -28f, 0f, 0f);
        }

        private static Pose[] BuildMinePoses()
        {
            // Anticipation -> overhead -> contact -> follow-through -> settle.
            return new[]
            {
                new Pose(0.00f).R("Hips", 0f, -5f).R("Spine", -2f, -6f).R("Chest", -3f, -8f)
                    .R("UpperArm.R", -25f, -15f, -8f).R("LowerArm.R", -35f).R("Hand.R", 0f, 0f, -10f),
                new Pose(0.22f).R("Hips", -5f, 6f).R("Spine", -8f, 8f).R("Chest", -10f, 10f)
                     .R("UpperArm.R", -105f, -12f, -18f).R("LowerArm.R", -45f).R("Hand.R", 18f, 0f, -15f)
                    .R("UpperArm.L", -28f, 18f, 10f).R("LowerArm.L", -35f),
                new Pose(0.38f).R("Hips", 8f, -8f).R("Spine", 10f, -10f).R("Chest", 12f, -12f)
                    .R("UpperArm.R", 58f, -20f, 15f).R("LowerArm.R", -18f).R("Hand.R", -20f, 0f, 12f)
                    .R("UpperArm.L", 18f, 12f, 5f).R("LowerArm.L", -50f),
                new Pose(0.58f).R("Hips", 5f, -6f).R("Spine", 6f, -8f).R("Chest", 8f, -10f)
                    .R("UpperArm.R", 32f, -12f, 8f).R("LowerArm.R", -30f).R("Hand.R", -8f),
                new Pose(0.82f).R("Hips", 0f, -5f).R("Spine", -2f, -6f).R("Chest", -3f, -8f)
                    .R("UpperArm.R", -25f, -15f, -8f).R("LowerArm.R", -35f).R("Hand.R", 0f, 0f, -10f)
            };
        }

        private static Pose[] BuildKnockdownPoses()
        {
            // Ready -> impact recoil -> loss of balance -> floor contact -> resting pose.
            return new[]
            {
                new Pose(0.00f),
                new Pose(0.14f).P("Hips", 0f, 0.02f, -0.08f).R("Hips", -18f, 0f, 8f)
                    .R("Spine", -22f, 0f, -10f).R("Chest", -28f, 0f, -12f)
                    .R("UpperArm.L", -45f, 0f, -25f).R("UpperArm.R", -45f, 0f, 25f),
                new Pose(0.48f).P("Hips", 0.04f, -0.22f, -0.18f).R("Hips", -58f, 8f, 28f)
                    .R("Spine", -32f, 0f, -18f).R("Chest", -38f, 0f, -24f)
                    .R("UpperLeg.L", 28f, 0f, -10f).R("LowerLeg.L", -48f)
                    .R("UpperLeg.R", -18f, 0f, 8f).R("LowerLeg.R", -35f)
                    .R("UpperArm.L", -75f, 0f, -45f).R("LowerArm.L", -38f)
                    .R("UpperArm.R", 30f, 0f, 48f).R("LowerArm.R", -55f),
                new Pose(0.88f).P("Hips", 0.12f, -0.52f, -0.20f).R("Hips", -82f, 12f, 72f)
                    .R("Spine", -18f, 0f, -26f).R("Chest", -24f, 0f, -32f).R("Head", 10f, 0f, -12f)
                    .R("UpperLeg.L", 42f, 0f, -14f).R("LowerLeg.L", -72f)
                    .R("UpperLeg.R", -24f, 0f, 12f).R("LowerLeg.R", -42f)
                    .R("UpperArm.L", -92f, 0f, -55f).R("LowerArm.L", -42f)
                    .R("UpperArm.R", 48f, 0f, 62f).R("LowerArm.R", -68f),
                new Pose(1.25f).P("Hips", 0.14f, -0.55f, -0.20f).R("Hips", -84f, 12f, 78f)
                    .R("Spine", -16f, 0f, -28f).R("Chest", -22f, 0f, -34f).R("Head", 12f, 0f, -14f)
                    .R("UpperLeg.L", 42f, 0f, -14f).R("LowerLeg.L", -72f)
                    .R("UpperLeg.R", -24f, 0f, 12f).R("LowerLeg.R", -42f)
                    .R("UpperArm.L", -92f, 0f, -55f).R("LowerArm.L", -42f)
                    .R("UpperArm.R", 48f, 0f, 62f).R("LowerArm.R", -68f)
            };
        }

private static AnimationClip BuildClip(Animator animator, string name, bool loop, Pose[] poses)
        {
            if (animator.isHuman && animator.avatar != null && animator.avatar.isValid)
                return BuildHumanoidClip(animator, name, loop, poses);

            AnimationClip clip = new AnimationClip { name = name, frameRate = 60f };
            Dictionary<string, Transform> bones = CollectBones(animator);
            HashSet<string> animatedBones = CollectAnimatedBones(poses);

            foreach (string boneName in animatedBones)
            {
                if (!bones.TryGetValue(boneName, out Transform bone))
                    throw new InvalidOperationException("Missing bone: " + boneName);
                string path = AnimationUtility.CalculateTransformPath(bone, animator.transform);
                AddRotationCurves(clip, path, bone.localRotation, boneName, poses);
                if (HasPositionKeys(boneName, poses)) AddPositionCurves(clip, path, bone.localPosition, boneName, poses);
            }

            SetLoop(clip, loop);
            clip.EnsureQuaternionContinuity();
            return clip;
        }
        private static AnimationClip BuildHumanoidClip(Animator animator, string name, bool loop, Pose[] poses)
        {
            AnimationClip clip = new AnimationClip { name = name, frameRate = 60f };
            Dictionary<string, Transform> bones = CollectBones(animator);
            HashSet<string> animatedBones = CollectAnimatedBones(poses);
            Dictionary<string, Quaternion> restRotations = new Dictionary<string, Quaternion>();
            Dictionary<string, Vector3> restPositions = new Dictionary<string, Vector3>();
            foreach (string boneName in animatedBones)
            {
                if (!bones.TryGetValue(boneName, out Transform bone))
                    throw new InvalidOperationException("Missing bone: " + boneName);
                restRotations[boneName] = bone.localRotation;
                restPositions[boneName] = bone.localPosition;
            }

            float[,] muscles = new float[poses.Length, HumanTrait.MuscleCount];
            Vector3[] bodyPositions = new Vector3[poses.Length];
            Quaternion[] bodyRotations = new Quaternion[poses.Length];
            HumanPoseHandler handler = new HumanPoseHandler(animator.avatar, animator.transform);
            try
            {
                HumanPose basePose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
                handler.GetHumanPose(ref basePose);
                float humanScale = Mathf.Max(0.0001f, animator.humanScale);

                for (int poseIndex = 0; poseIndex < poses.Length; poseIndex++)
                {
                    foreach (string boneName in animatedBones)
                    {
                        Transform bone = bones[boneName];
                        bone.localRotation = restRotations[boneName];
                        bone.localPosition = restPositions[boneName];
                    }
                    Pose sparsePose = poses[poseIndex];
                    foreach (KeyValuePair<string, Vector3> rotation in sparsePose.rotations)
                        if (rotation.Key != "Hips") bones[rotation.Key].localRotation = restRotations[rotation.Key] * Quaternion.Euler(rotation.Value);
                    foreach (KeyValuePair<string, Vector3> position in sparsePose.positions)
                        if (position.Key != "Hips") bones[position.Key].localPosition = restPositions[position.Key] + position.Value;

                    HumanPose humanPose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
                    handler.GetHumanPose(ref humanPose);
                    Vector3 rootOffset = sparsePose.positions.TryGetValue("Hips", out Vector3 p) ? p / humanScale : Vector3.zero;
                    Vector3 rootEuler = sparsePose.rotations.TryGetValue("Hips", out Vector3 r) ? r : Vector3.zero;
                    bodyPositions[poseIndex] = basePose.bodyPosition + rootOffset;
                    bodyRotations[poseIndex] = basePose.bodyRotation * Quaternion.Euler(rootEuler);
                    for (int muscle = 0; muscle < HumanTrait.MuscleCount; muscle++)
                        muscles[poseIndex, muscle] = humanPose.muscles[muscle];
                }
            }
            finally
            {
                handler.Dispose();
                foreach (string boneName in animatedBones)
                {
                    bones[boneName].localRotation = restRotations[boneName];
                    bones[boneName].localPosition = restPositions[boneName];
                }
            }

            for (int muscle = 0; muscle < HumanTrait.MuscleCount; muscle++)
            {
                int captured = muscle;
                SetAnimatorCurve(clip, HumanTrait.MuscleName[muscle], poses, i => muscles[i, captured]);
            }
            SetAnimatorCurve(clip, "RootT.x", poses, i => bodyPositions[i].x);
            SetAnimatorCurve(clip, "RootT.y", poses, i => bodyPositions[i].y);
            SetAnimatorCurve(clip, "RootT.z", poses, i => bodyPositions[i].z);
            MakeQuaternionContinuous(bodyRotations);
            SetAnimatorCurve(clip, "RootQ.x", poses, i => bodyRotations[i].x);
            SetAnimatorCurve(clip, "RootQ.y", poses, i => bodyRotations[i].y);
            SetAnimatorCurve(clip, "RootQ.z", poses, i => bodyRotations[i].z);
            SetAnimatorCurve(clip, "RootQ.w", poses, i => bodyRotations[i].w);

            SetLoop(clip, loop);
            clip.EnsureQuaternionContinuity();
            return clip;
        }

        private static Dictionary<string, Transform> CollectBones(Animator animator)
        {
            Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
            foreach (Transform transform in animator.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(transform.name)) bones.Add(transform.name, transform);
            return bones;
        }

        private static HashSet<string> CollectAnimatedBones(Pose[] poses)
        {
            HashSet<string> animatedBones = new HashSet<string>();
            foreach (Pose pose in poses)
            {
                foreach (string bone in pose.rotations.Keys) animatedBones.Add(bone);
                foreach (string bone in pose.positions.Keys) animatedBones.Add(bone);
            }
            return animatedBones;
        }

        private static void SetAnimatorCurve(AnimationClip clip, string property, Pose[] poses, Func<int, float> value)
        {
            Keyframe[] keys = new Keyframe[poses.Length];
            for (int i = 0; i < poses.Length; i++) keys[i] = new Keyframe(poses[i].time, value(i));
            AnimationCurve curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), property), curve);
        }

        private static void MakeQuaternionContinuous(Quaternion[] rotations)
        {
            for (int i = 1; i < rotations.Length; i++)
                if (Quaternion.Dot(rotations[i - 1], rotations[i]) < 0f)
                    rotations[i] = new Quaternion(-rotations[i].x, -rotations[i].y, -rotations[i].z, -rotations[i].w);
        }


        private static void AddRotationCurves(AnimationClip clip, string path, Quaternion rest, string bone, Pose[] poses)
        {
            List<Quaternion> values = new List<Quaternion>(poses.Length);
            Quaternion previous = rest;
            for (int i = 0; i < poses.Length; i++)
            {
                Vector3 delta = poses[i].rotations.TryGetValue(bone, out Vector3 euler) ? euler : Vector3.zero;
                Quaternion value = rest * Quaternion.Euler(delta);
                if (Quaternion.Dot(previous, value) < 0f) value = new Quaternion(-value.x, -value.y, -value.z, -value.w);
                values.Add(value);
                previous = value;
            }
            SetCurve(clip, path, "m_LocalRotation.x", poses, i => values[i].x);
            SetCurve(clip, path, "m_LocalRotation.y", poses, i => values[i].y);
            SetCurve(clip, path, "m_LocalRotation.z", poses, i => values[i].z);
            SetCurve(clip, path, "m_LocalRotation.w", poses, i => values[i].w);
        }

        private static void AddPositionCurves(AnimationClip clip, string path, Vector3 rest, string bone, Pose[] poses)
        {
            SetCurve(clip, path, "m_LocalPosition.x", poses, i => rest.x + (poses[i].positions.TryGetValue(bone, out Vector3 d) ? d.x : 0f));
            SetCurve(clip, path, "m_LocalPosition.y", poses, i => rest.y + (poses[i].positions.TryGetValue(bone, out Vector3 d) ? d.y : 0f));
            SetCurve(clip, path, "m_LocalPosition.z", poses, i => rest.z + (poses[i].positions.TryGetValue(bone, out Vector3 d) ? d.z : 0f));
        }

        private static void SetCurve(AnimationClip clip, string path, string property, Pose[] poses, Func<int, float> value)
        {
            Keyframe[] keys = new Keyframe[poses.Length];
            for (int i = 0; i < poses.Length; i++) keys[i] = new Keyframe(poses[i].time, value(i));
            AnimationCurve curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
        }

        private static bool HasPositionKeys(string bone, Pose[] poses)
        {
            foreach (Pose pose in poses) if (pose.positions.ContainsKey(bone)) return true;
            return false;
        }

        private static void SaveClip(AnimationClip clip, string path)
        {
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null) AssetDatabase.CreateAsset(clip, path);
            else
            {
                EditorUtility.CopySerialized(clip, existing);
                EditorUtility.SetDirty(existing);
            }
        }

        private static void ConfigureController(AnimationClip run, AnimationClip mine, AnimationClip knockdown)
        {
            UnityAnimatorController controller = AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(ControllerPath);
            if (controller == null) throw new InvalidOperationException("Missing controller: " + ControllerPath);
            EnsureTrigger(controller, "Mine");
            EnsureTrigger(controller, "Knockdown");
            EnsureTrigger(controller, "Recover");

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState walk = FindState(machine, "Walk");
            if (walk != null) walk.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunClipPath) ?? run;

            AnimatorState mineState = FindState(machine, "Sparse Mine") ?? machine.AddState("Sparse Mine", new Vector3(620f, 60f));
            mineState.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(MineClipPath) ?? mine;
            AnimatorState downState = FindState(machine, "Sparse Knockdown") ?? machine.AddState("Sparse Knockdown", new Vector3(620f, 180f));
            downState.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(KnockdownClipPath) ?? knockdown;
            AnimatorState idle = FindState(machine, "Idle");

            RemoveTransitionsTo(machine, mineState, downState);
            AnimatorStateTransition mineAny = machine.AddAnyStateTransition(mineState);
            mineAny.hasExitTime = false; mineAny.duration = 0.08f; mineAny.AddCondition(AnimatorConditionMode.If, 0f, "Mine");
            AnimatorStateTransition downAny = machine.AddAnyStateTransition(downState);
            downAny.hasExitTime = false; downAny.duration = 0.06f; downAny.AddCondition(AnimatorConditionMode.If, 0f, "Knockdown");

            if (idle != null)
            {
                AnimatorStateTransition mineExit = mineState.AddTransition(idle);
                mineExit.hasExitTime = true; mineExit.exitTime = 0.96f; mineExit.duration = 0.12f;
                AnimatorStateTransition recover = downState.AddTransition(idle);
                recover.hasExitTime = false; recover.duration = 0.25f; recover.AddCondition(AnimatorConditionMode.If, 0f, "Recover");
            }
            EditorUtility.SetDirty(controller);
        }

        private static void RemoveTransitionsTo(AnimatorStateMachine machine, AnimatorState mine, AnimatorState down)
        {
            foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
                if (transition.destinationState == mine || transition.destinationState == down) machine.RemoveAnyStateTransition(transition);
            foreach (AnimatorStateTransition transition in mine.transitions) mine.RemoveTransition(transition);
            foreach (AnimatorStateTransition transition in down.transitions) down.RemoveTransition(transition);
        }

        private static AnimatorState FindState(AnimatorStateMachine machine, string name)
        {
            foreach (ChildAnimatorState child in machine.states) if (child.state.name == name) return child.state;
            return null;
        }

        private static void EnsureTrigger(UnityAnimatorController controller, string name)
        {
            foreach (AnimatorControllerParameter p in controller.parameters) if (p.name == name) return;
            controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
        }

        private static void BindPlayerController(GameObject player, Animator animator)
        {
            Supernova.Voxels.VoxelPlayerController controller =
                player.GetComponent<Supernova.Voxels.VoxelPlayerController>();
            if (controller == null)
                throw new InvalidOperationException("Player prefab has no VoxelPlayerController.");
            controller.SetAnimator(animator);
            EditorUtility.SetDirty(controller);
        }

        private static void SetLoop(AnimationClip clip, bool loop)
        {
            SerializedObject serialized = new SerializedObject(clip);
            SerializedProperty settings = serialized.FindProperty("m_AnimationClipSettings");
            if (settings != null)
            {
                SerializedProperty loopTime = settings.FindPropertyRelative("m_LoopTime");
                if (loopTime != null) loopTime.boolValue = loop;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif

