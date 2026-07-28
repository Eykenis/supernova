#if UNITY_EDITOR
using System.Collections.Generic;
using Supernova.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Supernova.EditorTools.PlayerSetup
{
    public static class JetpackHoverAnimationBuilder
    {
        private const string SourceClipPath =
            "Assets/3rd/P05_Aki & Mika/Anim_demo/HoverDemo.anim";
        private const string OutputClipPath =
            "Assets/Game/Animations/HoverLoop.anim";
        private const string InteractionPath =
            "Assets/Game/Config/Equipment/JetpackInteraction.asset";
        private const float LoopStartTime = 1.5f;
        private const float LoopEndTime = 6.5f;
        private const float TimeEpsilon = 0.0001f;

        [MenuItem("Supernova/Equipment/Rebuild Jetpack Hover Loop")]
        public static void Build()
        {
            AnimationClip source =
                AssetDatabase.LoadAssetAtPath<AnimationClip>(SourceClipPath);
            if (source == null)
                throw new UnityException("Missing jetpack source animation: " + SourceClipPath);

            AnimationClip output =
                AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputClipPath);
            if (output == null)
            {
                output = new AnimationClip();
                AssetDatabase.CreateAsset(output, OutputClipPath);
            }
            else
            {
                AnimationClip empty = new AnimationClip();
                EditorUtility.CopySerialized(empty, output);
                Object.DestroyImmediate(empty);
            }

            output.name = "HoverLoop";
            output.frameRate = source.frameRate;
            output.wrapMode = WrapMode.Loop;
            float duration = LoopEndTime - LoopStartTime;

            EditorCurveBinding[] curveBindings =
                AnimationUtility.GetCurveBindings(source);
            for (int i = 0; i < curveBindings.Length; i++)
            {
                AnimationCurve curve =
                    AnimationUtility.GetEditorCurve(source, curveBindings[i]);
                AnimationUtility.SetEditorCurve(
                    output,
                    curveBindings[i],
                    CropLoopCurve(curve, duration));
            }

            EditorCurveBinding[] objectBindings =
                AnimationUtility.GetObjectReferenceCurveBindings(source);
            for (int i = 0; i < objectBindings.Length; i++)
            {
                ObjectReferenceKeyframe[] keyframes =
                    AnimationUtility.GetObjectReferenceCurve(source, objectBindings[i]);
                AnimationUtility.SetObjectReferenceCurve(
                    output,
                    objectBindings[i],
                    CropObjectReferenceCurve(keyframes, duration));
            }

            AnimationEvent[] sourceEvents = AnimationUtility.GetAnimationEvents(source);
            List<AnimationEvent> loopEvents = new List<AnimationEvent>();
            for (int i = 0; i < sourceEvents.Length; i++)
            {
                AnimationEvent sourceEvent = sourceEvents[i];
                if (sourceEvent.time < LoopStartTime || sourceEvent.time >= LoopEndTime)
                    continue;
                sourceEvent.time -= LoopStartTime;
                loopEvents.Add(sourceEvent);
            }
            AnimationUtility.SetAnimationEvents(output, loopEvents.ToArray());

            AnimationClipSettings settings =
                AnimationUtility.GetAnimationClipSettings(source);
            settings.startTime = 0f;
            settings.stopTime = duration;
            settings.loopTime = true;
            settings.loopBlend = true;
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionXZ = true;
            settings.loopBlendPositionY = true;
            AnimationUtility.SetAnimationClipSettings(output, settings);
            output.EnsureQuaternionContinuity();
            EditorUtility.SetDirty(output);

            JetpackEquipmentInteraction interaction =
                AssetDatabase.LoadAssetAtPath<JetpackEquipmentInteraction>(
                    InteractionPath);
            if (interaction == null)
                throw new UnityException("Missing jetpack interaction: " + InteractionPath);

            SerializedObject serializedInteraction = new SerializedObject(interaction);
            serializedInteraction.FindProperty("launchAnimation").objectReferenceValue = source;
            serializedInteraction.FindProperty("launchAnimationDuration").floatValue =
                LoopStartTime;
            serializedInteraction.FindProperty("hoverAnimation").objectReferenceValue = output;
            serializedInteraction.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(interaction);

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Rebuilt HoverLoop from HoverDemo [{LoopStartTime:F1}s, "
                + $"{LoopEndTime:F1}s] and updated JetpackInteraction.");
        }

        private static AnimationCurve CropLoopCurve(
            AnimationCurve source,
            float duration)
        {
            if (source == null) return null;

            Keyframe start = FindOrEvaluateKey(source, LoopStartTime);
            start.time = 0f;
            List<Keyframe> keys = new List<Keyframe> { start };
            Keyframe[] sourceKeys = source.keys;
            for (int i = 0; i < sourceKeys.Length; i++)
            {
                Keyframe key = sourceKeys[i];
                if (key.time <= LoopStartTime + TimeEpsilon
                    || key.time >= LoopEndTime - TimeEpsilon)
                {
                    continue;
                }
                key.time -= LoopStartTime;
                keys.Add(key);
            }

            Keyframe end = start;
            end.time = duration;
            keys.Add(end);
            return new AnimationCurve(keys.ToArray())
            {
                preWrapMode = WrapMode.ClampForever,
                postWrapMode = WrapMode.Loop,
            };
        }

        private static Keyframe FindOrEvaluateKey(
            AnimationCurve curve,
            float time)
        {
            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (Mathf.Abs(keys[i].time - time) <= TimeEpsilon)
                    return keys[i];
            }
            return new Keyframe(time, curve.Evaluate(time));
        }

        private static ObjectReferenceKeyframe[] CropObjectReferenceCurve(
            ObjectReferenceKeyframe[] source,
            float duration)
        {
            if (source == null || source.Length == 0)
                return new ObjectReferenceKeyframe[0];

            Object startValue = source[0].value;
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].time > LoopStartTime) break;
                startValue = source[i].value;
            }

            List<ObjectReferenceKeyframe> keys =
                new List<ObjectReferenceKeyframe>
                {
                    new ObjectReferenceKeyframe { time = 0f, value = startValue },
                };
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].time <= LoopStartTime + TimeEpsilon
                    || source[i].time >= LoopEndTime - TimeEpsilon)
                {
                    continue;
                }
                ObjectReferenceKeyframe key = source[i];
                key.time -= LoopStartTime;
                keys.Add(key);
            }
            keys.Add(new ObjectReferenceKeyframe
            {
                time = duration,
                value = startValue,
            });
            return keys.ToArray();
        }
    }
}
#endif
