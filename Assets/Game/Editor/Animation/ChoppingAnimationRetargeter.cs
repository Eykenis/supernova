#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityAnimatorController = UnityEditor.Animations.AnimatorController;

namespace Supernova.EditorTools.Animation
{
    /// <summary>
    /// Creates P05-bound clips from the downloaded Generic upper-body chopping clips.
    /// The source FBX and its curves are never modified. Each source frame is sampled,
    /// aligned to the P05 rest pose, and written to a new model-specific clip.
    /// </summary>
    public static class ChoppingAnimationRetargeter
    {
        private const string SourcePath = ProjectAssetPaths.ThirdParty.ArmsAnimation;
        private const string PlayerPrefabPath = ProjectAssetPaths.Prefabs.Player;
        private const string ControllerPath = ProjectAssetPaths.Animations.PlayerController;
        private const string OutputFolder = ProjectAssetPaths.Folders.ChoppingAnimations;

        private sealed class BoneMap
        {
            public BoneMap(string source, string target)
            {
                Source = source;
                Target = target;
            }
            public string Source;
            public string Target;
        }

        private static readonly BoneMap[] BoneMaps =
        {
            new BoneMap("root", "Chest"),
            new BoneMap("spine_upper", "Chest"),
            new BoneMap("clavicle_l", "shoulder_L"),
            new BoneMap("arm_upper_l", "Arm_L"),
            new BoneMap("arm_lower_l", "forearm_L"),
            new BoneMap("hand_l", "hand_L"),
            new BoneMap("clavicle_r", "shoulder_R"),
            new BoneMap("arm_upper_r", "Arm_R"),
            new BoneMap("arm_lower_r", "forearm_R"),
            new BoneMap("hand_r", "hand_R"),
            new BoneMap("thumb_01_l", "finger_thumbs_proximal_L"),
            new BoneMap("thumb_02_l", "finger_thumbs_intermediate_L"),
            new BoneMap("thumb_03_l", "finger_thumbs_distal_L"),
            new BoneMap("finger_index_01_l", "finger_index_proximal_L"),
            new BoneMap("finger_index_02_l", "finger_index_intermediate_L"),
            new BoneMap("finger_index_03_l", "finger_index_distal_L"),
            new BoneMap("finger_middle_01_l", "finger_middle_proximal_L"),
            new BoneMap("finger_middle_02_l", "finger_middle_intermediate_L"),
            new BoneMap("finger_middle_03_l", "finger_middle_distal_L"),
            new BoneMap("finger_ring_01_l", "finger_ring_proximal_L"),
            new BoneMap("finger_ring_02_l", "finger_ring_intermediate_L"),
            new BoneMap("finger_ring_03_l", "finger_ring_distal_L"),
            new BoneMap("finger_pinky_01_l", "finger_little_proximal_L"),
            new BoneMap("finger_pinky_02_l", "finger_little_intermediate_L"),
            new BoneMap("finger_pinky_03_l", "finger_little_distal_L"),
            new BoneMap("thumb_01_r", "finger_thumbs_proximal_R"),
            new BoneMap("thumb_02_r", "finger_thumbs_intermediate_R"),
            new BoneMap("thumb_03_r", "finger_thumbs_distal_R"),
            new BoneMap("finger_index_01_r", "finger_index_proximal_R"),
            new BoneMap("finger_index_02_r", "finger_index_intermediate_R"),
            new BoneMap("finger_index_03_r", "finger_index_distal_R"),
            new BoneMap("finger_middle_01_r", "finger_middle_proximal_R"),
            new BoneMap("finger_middle_02_r", "finger_middle_intermediate_R"),
            new BoneMap("finger_middle_03_r", "finger_middle_distal_R"),
            new BoneMap("finger_ring_01_r", "finger_ring_proximal_R"),
            new BoneMap("finger_ring_02_r", "finger_ring_intermediate_R"),
            new BoneMap("finger_ring_03_r", "finger_ring_distal_R"),
            new BoneMap("finger_pinky_01_r", "finger_little_proximal_R"),
            new BoneMap("finger_pinky_02_r", "finger_little_intermediate_R"),
            new BoneMap("finger_pinky_03_r", "finger_little_distal_R"),
        };

        [MenuItem("Tools/Supernova/Animation/Retarget Chopping To P05")]
        public static void RetargetAndBind()
        {
            GameObject sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            if (sourceAsset == null) throw new InvalidOperationException("Missing source FBX: " + SourcePath);
            AnimationClip[] sourceClips = AssetDatabase.LoadAllAssetsAtPath(SourcePath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                .ToArray();
            if (sourceClips.Length == 0) throw new InvalidOperationException("No animation clips found in " + SourcePath);

            EnsureFolder(OutputFolder);
            GameObject sourceInstance = UnityEngine.Object.Instantiate(sourceAsset);
            sourceInstance.hideFlags = HideFlags.HideAndDontSave;
            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                Animator targetAnimator = player.GetComponentsInChildren<Animator>(true)
                    .FirstOrDefault(animator => animator.gameObject.activeInHierarchy && animator.isHuman);
                if (targetAnimator == null) throw new InvalidOperationException("Player has no active Humanoid Animator.");
                targetAnimator.enabled = false;

                Dictionary<string, Transform> sourceBones = IndexTransforms(sourceInstance.transform);
                Dictionary<string, Transform> targetBones = IndexTransforms(targetAnimator.transform);
                Dictionary<Transform, Quaternion> sourceRestLocal = CaptureLocalRotations(sourceInstance.transform);
                Dictionary<Transform, Quaternion> targetRestLocal = CaptureLocalRotations(targetAnimator.transform);

                List<BoneMap> validMaps = BoneMaps
                    .Where(map => map.Source != "root"
                        && !map.Source.StartsWith("finger_", StringComparison.Ordinal)
                        && !map.Source.StartsWith("thumb_", StringComparison.Ordinal)
                        && sourceBones.ContainsKey(map.Source)
                        && targetBones.ContainsKey(map.Target))
                    .ToList();
                if (validMaps.Count < 8)
                    throw new InvalidOperationException("Too few matching bones for chopping retarget: " + validMaps.Count);

                Dictionary<string, AnimationClip> outputs = new Dictionary<string, AnimationClip>();
                foreach (AnimationClip sourceClip in sourceClips)
                {
                    AnimationClip output = BuildRetargetedClip(
                        sourceClip,
                        sourceInstance,
                        targetAnimator,
                        sourceBones,
                        targetBones,
                        sourceRestLocal,
                        targetRestLocal,
                        validMaps);
                    string cleanName = sourceClip.name.Replace(".001", string.Empty);
                    string outputPath = OutputFolder + "/P05_" + cleanName + ".anim";
                    SaveClip(output, outputPath);
                    outputs[cleanName] = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath) ?? output;
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                BindMineState(outputs);
                AssetDatabase.SaveAssets();
                Debug.Log("Chopping animation retargeted to P05 without modifying the source FBX. Generated "
                    + outputs.Count + " clips; Mine uses P05_Chop_Start_Fast.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
                UnityEngine.Object.DestroyImmediate(sourceInstance);
            }
        }

        private static AnimationClip BuildRetargetedClip(
            AnimationClip sourceClip,
            GameObject sourceInstance,
            Animator targetAnimator,
            Dictionary<string, Transform> sourceBones,
            Dictionary<string, Transform> targetBones,
            Dictionary<Transform, Quaternion> sourceRestLocal,
            Dictionary<Transform, Quaternion> targetRestLocal,
            List<BoneMap> maps)
        {
            ResetLocalRotations(sourceRestLocal);
            ResetLocalRotations(targetRestLocal);

            Dictionary<string, Quaternion> sourceRestWorld = maps.ToDictionary(
                map => map.Source,
                map => sourceBones[map.Source].rotation);
            Dictionary<string, Quaternion> targetRestWorld = maps.ToDictionary(
                map => map.Target,
                map => targetBones[map.Target].rotation);
            Dictionary<string, Quaternion> alignments = maps.ToDictionary(
                map => map.Target,
                map => targetRestWorld[map.Target] * Quaternion.Inverse(sourceRestWorld[map.Source]));

            float frameRate = sourceClip.frameRate > 0f ? sourceClip.frameRate : 24f;
            int frameCount = Mathf.Max(1, Mathf.CeilToInt(sourceClip.length * frameRate));
            List<float> times = new List<float>(frameCount + 1);
            float[,] muscles = new float[frameCount + 1, HumanTrait.MuscleCount];
            Vector3[] rootPositions = new Vector3[frameCount + 1];
            Quaternion[] rootRotations = new Quaternion[frameCount + 1];

            HumanPoseHandler poseHandler = new HumanPoseHandler(targetAnimator.avatar, targetAnimator.transform);
            try
            {
                HumanPose restPose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
                poseHandler.GetHumanPose(ref restPose);

                for (int frame = 0; frame <= frameCount; frame++)
                {
                    float time = Mathf.Min(sourceClip.length, frame / frameRate);
                    times.Add(time);
                    ResetLocalRotations(sourceRestLocal);
                    ResetLocalRotations(targetRestLocal);
                    sourceClip.SampleAnimation(sourceInstance, time);

                    foreach (BoneMap map in maps)
                    {
                        Transform sourceBone = sourceBones[map.Source];
                        Transform targetBone = targetBones[map.Target];
                        targetBone.rotation = alignments[map.Target] * sourceBone.rotation;
                    }

                    HumanPose pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
                    poseHandler.GetHumanPose(ref pose);
                    for (int muscle = 0; muscle < HumanTrait.MuscleCount; muscle++)
                        muscles[frame, muscle] = Mathf.Clamp(
                            restPose.muscles[muscle]
                            + (pose.muscles[muscle] - restPose.muscles[muscle]) * 0.75f,
                            -1f,
                            1f);
                    // Keep the P05 root fixed. CharacterController owns all world movement.
                    rootPositions[frame] = restPose.bodyPosition;
                    rootRotations[frame] = restPose.bodyRotation;
                }
            }
            finally
            {
                poseHandler.Dispose();
                ResetLocalRotations(targetRestLocal);
            }

            AnimationClip output = new AnimationClip
            {
                name = "P05_" + sourceClip.name.Replace(".001", string.Empty),
                frameRate = frameRate,
            };
            for (int muscle = 0; muscle < HumanTrait.MuscleCount; muscle++)
            {
                string muscleName = HumanTrait.MuscleName[muscle];
                if (!IsCoreUpperBodyMuscle(muscleName)) continue;
                int captured = muscle;
                SetFloatCurve(output, muscleName, times, i => muscles[i, captured]);
            }
            output.EnsureQuaternionContinuity();
            SetLoop(output, sourceClip.name.IndexOf("Loop", StringComparison.OrdinalIgnoreCase) >= 0);
            return output;
        }

        private static bool IsCoreUpperBodyMuscle(string name)
        {
            return name.StartsWith("Spine ", StringComparison.Ordinal)
                || name.StartsWith("Chest ", StringComparison.Ordinal)
                || name.StartsWith("UpperChest ", StringComparison.Ordinal)
                || name.IndexOf(" Shoulder ", StringComparison.Ordinal) >= 0
                || name.IndexOf(" Arm ", StringComparison.Ordinal) >= 0
                || name.IndexOf(" Forearm ", StringComparison.Ordinal) >= 0
                || name.StartsWith("Left Hand ", StringComparison.Ordinal)
                || name.StartsWith("Right Hand ", StringComparison.Ordinal);
        }

        private static void SetFloatCurve(
            AnimationClip clip,
            string property,
            List<float> times,
            Func<int, float> value)
        {
            Keyframe[] keys = new Keyframe[times.Count];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(times[i], value(i));
            AnimationCurve curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), property),
                curve);
        }

        private static void MakeQuaternionContinuous(Quaternion[] values)
        {
            for (int i = 1; i < values.Length; i++)
                if (Quaternion.Dot(values[i - 1], values[i]) < 0f)
                    values[i] = new Quaternion(-values[i].x, -values[i].y, -values[i].z, -values[i].w);
        }

        private static void SetQuaternionCurve(
            AnimationClip clip,
            string path,
            string property,
            List<float> times,
            List<Quaternion> values,
            Func<Quaternion, float> selector)
        {
            Keyframe[] keys = new Keyframe[times.Count];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(times[i], selector(values[i]));
            AnimationCurve curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(path, typeof(Transform), property),
                curve);
        }

        private static void BindMineState(Dictionary<string, AnimationClip> clips)
        {
            UnityAnimatorController controller = AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(ControllerPath);
            if (controller == null) throw new InvalidOperationException("Missing controller: " + ControllerPath);
            AnimationClip selected = clips.TryGetValue("Chop_Start_Fast", out AnimationClip fast)
                ? fast
                : clips.Values.First();
            AnimatorState mine = controller.layers[0].stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(state => state.name == "Mine");
            if (mine == null) throw new InvalidOperationException("P05Player controller has no Mine state.");
            mine.motion = selected;
            EditorUtility.SetDirty(mine);
            EditorUtility.SetDirty(controller);
        }

        private static Dictionary<string, Transform> IndexTransforms(Transform root)
        {
            Dictionary<string, Transform> result = new Dictionary<string, Transform>();
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                if (!result.ContainsKey(transform.name)) result.Add(transform.name, transform);
            return result;
        }

        private static Dictionary<Transform, Quaternion> CaptureLocalRotations(Transform root)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .ToDictionary(transform => transform, transform => transform.localRotation);
        }

        private static void ResetLocalRotations(Dictionary<Transform, Quaternion> rotations)
        {
            foreach (KeyValuePair<Transform, Quaternion> pair in rotations)
                pair.Key.localRotation = pair.Value;
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

        private static void SetLoop(AnimationClip clip, bool loop)
        {
            SerializedObject serialized = new SerializedObject(clip);
            SerializedProperty settings = serialized.FindProperty("m_AnimationClipSettings");
            SerializedProperty loopTime = settings != null
                ? settings.FindPropertyRelative("m_LoopTime")
                : null;
            if (loopTime != null) loopTime.boolValue = loop;
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

