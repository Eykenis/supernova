#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityAnimatorController = UnityEditor.Animations.AnimatorController;

namespace Supernova.EditorTools.Animation
{
    /// <summary>
    /// Composes the user-authored upper-body mining clip with the current Idle state's
    /// lower-body motion, then authors inward palms and a closed fist on both hands.
    /// </summary>
    public static class MiningAkiComposer
    {
        private const string MiningPath = "Assets/Game/Animations/mining_aki.anim";
        private const string BackupPath = "Assets/Game/Animations/mining_aki_before_lowerbody_hands.anim";
        private const string HandFixBackupPath = "Assets/Game/Animations/mining_aki_before_IdleA_handfix.anim";
        private const string TwistFixBackupPath = "Assets/Game/Animations/mining_aki_before_forearm_twist_fix.anim";
        private const string FistEyeBackupPath = "Assets/Game/Animations/mining_aki_before_right_fist_eye_up.anim";
        private const string IdleSourcePath = "Assets/3rd/Suriyun/Animations/Anim@Idle_A.fbx";
        private const string ControllerPath = "Assets/Game/Animations/P05Player.controller";

        private static readonly string[] LowerBodyMuscles =
        {
            "Left Upper Leg Front-Back", "Left Upper Leg In-Out", "Left Upper Leg Twist In-Out",
            "Left Lower Leg Stretch", "Left Lower Leg Twist In-Out",
            "Left Foot Up-Down", "Left Foot Twist In-Out", "Left Toes Up-Down",
            "Right Upper Leg Front-Back", "Right Upper Leg In-Out", "Right Upper Leg Twist In-Out",
            "Right Lower Leg Stretch", "Right Lower Leg Twist In-Out",
            "Right Foot Up-Down", "Right Foot Twist In-Out", "Right Toes Up-Down",
        };

        private static readonly string[] RootAndFootGoals =
        {
            "RootT.x", "RootT.y", "RootT.z",
            "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w",
            "LeftFootT.x", "LeftFootT.y", "LeftFootT.z",
            "LeftFootQ.x", "LeftFootQ.y", "LeftFootQ.z", "LeftFootQ.w",
            "RightFootT.x", "RightFootT.y", "RightFootT.z",
            "RightFootQ.x", "RightFootQ.y", "RightFootQ.z", "RightFootQ.w",
        };

        [MenuItem("Tools/Supernova/Animation/Compose Idle Legs And Fists Into mining_aki")]
        public static void ComposeAndBind()
        {
            AnimationClip mining = AssetDatabase.LoadAssetAtPath<AnimationClip>(MiningPath);
            UnityAnimatorController controller = AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(ControllerPath);
            if (mining == null || controller == null)
                throw new InvalidOperationException("mining_aki or P05Player controller is missing.");

            AnimationClip idle = AssetDatabase.LoadAllAssetsAtPath(IdleSourcePath)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (idle == null)
                throw new InvalidOperationException("Suriyun IdleA animation could not be loaded: " + IdleSourcePath);

            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(BackupPath) == null)
                AssetDatabase.CopyAsset(MiningPath, BackupPath);
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(HandFixBackupPath) == null)
                AssetDatabase.CopyAsset(MiningPath, HandFixBackupPath);
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(TwistFixBackupPath) == null)
                AssetDatabase.CopyAsset(MiningPath, TwistFixBackupPath);
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(FistEyeBackupPath) == null)
                AssetDatabase.CopyAsset(MiningPath, FistEyeBackupPath);

            float frameRate = mining.frameRate > 0f ? mining.frameRate : 60f;
            int frameCount = Mathf.Max(1, Mathf.CeilToInt(mining.length * frameRate));
            float[] times = Enumerable.Range(0, frameCount + 1)
                .Select(frame => Mathf.Min(mining.length, frame / frameRate))
                .ToArray();

            int copied = 0;
            foreach (string property in LowerBodyMuscles.Concat(RootAndFootGoals))
            {
                AnimationCurve sourceCurve = FindAnimatorCurve(idle, property);
                if (sourceCurve == null) continue;
                AnimationCurve targetCurve = SampleCurve(sourceCurve, idle.length, times);
                SetAnimatorCurve(mining, property, targetCurve);
                copied++;
            }

            AuthorWristCurves(mining);
            AuthorFistCurves(mining);
            mining.EnsureQuaternionContinuity();
            EditorUtility.SetDirty(mining);

            AnimatorState mineState = FindState(controller, "Mine");
            if (mineState == null) throw new InvalidOperationException("P05Player Mine state is missing.");
            mineState.motion = mining;
            EditorUtility.SetDirty(mineState);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Player.prefab");
            float[] previewTimes = Enumerable.Range(0, 7)
                .Select(index => mining.length * index / 6f)
                .ToArray();
            P05ChopKeyPoseBuilder.RenderPoseStrip(
                playerPrefab, mining, previewTimes,
                "Assets/Screenshots/mining_aki_composed_front.png",
                Vector3.forward, Vector3.up);
            P05ChopKeyPoseBuilder.RenderPoseStrip(
                playerPrefab, mining, previewTimes,
                "Assets/Screenshots/mining_aki_composed_45.png",
                new Vector3(1f, 0.28f, 1f).normalized, Vector3.up);
            AssetDatabase.Refresh();
            Debug.Log($"Composed mining_aki: copied {copied} Suriyun IdleA lower-body/root curves, added inward wrists and fist curves, and bound Mine.");
        }

        private static void AuthorWristCurves(AnimationClip clip)
        {
            float end = clip.length;
            float[] normalized = { 0f, 0.18f, 0.38f, 0.58f, 0.72f, 0.86f, 1f };
            float[] times = normalized.Select(value => value * end).ToArray();

            // Palm facing is primarily forearm pronation/supination, not wrist side-bend.
            SetAnimatorCurve(clip, "Left Forearm Twist In-Out", CreateCurve(times,
                new[] { -0.72f, -0.76f, -0.80f, -0.82f, -0.78f, -0.74f, -0.72f }));
            // The previous -1 range pointed the right fist eye to character-left.
            // Adding roughly one normalized unit rotates the forearm about 90 degrees,
            // placing the thumb/index ring upward.
            SetAnimatorCurve(clip, "Right Forearm Twist In-Out", CreateCurve(times,
                new[] { 0.12f, 0.06f, 0.00f, 0.00f, 0.04f, 0.08f, 0.12f }));
            SetAnimatorCurve(clip, "Left Hand In-Out", CreateCurve(times,
                new[] { 0.08f, 0.10f, 0.12f, 0.10f, 0.08f, 0.06f, 0.08f }));
            SetAnimatorCurve(clip, "Right Hand In-Out", CreateCurve(times,
                new[] { 0.06f, 0.08f, 0.10f, 0.10f, 0.08f, 0.06f, 0.06f }));
            SetAnimatorCurve(clip, "Left Hand Down-Up", CreateCurve(times,
                new[] { 0.04f, 0.08f, 0.10f, 0.02f, -0.08f, -0.04f, 0.04f }));
            SetAnimatorCurve(clip, "Right Hand Down-Up", CreateCurve(times,
                new[] { 0.02f, -0.04f, -0.10f, -0.05f, 0.14f, 0.18f, 0.02f }));
        }

        private static void AuthorFistCurves(AnimationClip clip)
        {
            string[] sides = { "LeftHand", "RightHand" };
            string[] fingers = { "Index", "Middle", "Ring", "Little" };
            foreach (string side in sides)
            {
                foreach (string finger in fingers)
                {
                    SetConstant(clip, $"{side}.{finger}.1 Stretched", -0.82f);
                    SetConstant(clip, $"{side}.{finger}.2 Stretched", -0.90f);
                    SetConstant(clip, $"{side}.{finger}.3 Stretched", -0.94f);
                    SetConstant(clip, $"{side}.{finger}.Spread", 0f);
                }
                SetConstant(clip, $"{side}.Thumb.1 Stretched", -0.55f);
                SetConstant(clip, $"{side}.Thumb.2 Stretched", -0.68f);
                SetConstant(clip, $"{side}.Thumb.3 Stretched", -0.78f);
                SetConstant(clip, $"{side}.Thumb.Spread", side == "LeftHand" ? 0.18f : -0.18f);
            }
        }

        private static void SetConstant(AnimationClip clip, string property, float value)
        {
            SetAnimatorCurve(clip, property, AnimationCurve.Linear(0f, value, clip.length, value));
        }

        private static AnimationCurve FindAnimatorCurve(AnimationClip clip, string property)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.type == typeof(Animator)
                    && string.IsNullOrEmpty(binding.path)
                    && binding.propertyName == property)
                    return AnimationUtility.GetEditorCurve(clip, binding);
            return null;
        }

        private static AnimationCurve SampleCurve(AnimationCurve source, float sourceLength, float[] times)
        {
            Keyframe[] keys = new Keyframe[times.Length];
            float safeLength = Mathf.Max(0.0001f, sourceLength);
            for (int i = 0; i < times.Length; i++)
            {
                float sourceTime = times[i] % safeLength;
                keys[i] = new Keyframe(times[i], source.Evaluate(sourceTime));
            }
            AnimationCurve curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        private static AnimationCurve CreateCurve(float[] times, float[] values)
        {
            Keyframe[] keys = new Keyframe[times.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(times[i], values[i]);
            AnimationCurve curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        private static void SetAnimatorCurve(AnimationClip clip, string property, AnimationCurve curve)
        {
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), property),
                curve);
        }

        private static AnimatorState FindState(UnityAnimatorController controller, string name)
        {
            return controller.layers[0].stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(state => state.name == name);
        }
    }
}
#endif
