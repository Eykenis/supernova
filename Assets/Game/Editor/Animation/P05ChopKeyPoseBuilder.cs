#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityAnimatorController = UnityEditor.Animations.AnimatorController;

namespace Supernova.EditorTools.Animation
{
    /// <summary>
    /// Rebuilds the downloaded chopping motion as seven semantic P05 poses.
    /// The source clip is reference-only: hand/elbow trajectories are read in chest
    /// space, then P05 arms are solved with two-bone IK before capturing Humanoid muscles.
    /// </summary>
    public static class P05ChopKeyPoseBuilder
    {
        private const string SourcePath = "Assets/3rd/Sketchfab/Arms_Animation_a.fbx";
        private const string PlayerPrefabPath = "Assets/Game/Prefabs/Player.prefab";
        private const string P05PrefabPath = "Assets/3rd/P05_Aki & Mika/Model_DATA/Prefab/Physics_MagicaCloth2/P05_ASTRO_Aki Variant.prefab";
        private const string ControllerPath = "Assets/Game/Animations/P05Player.controller";
        private const string OutputPath = "Assets/Game/Animations/P05Custom/P05_Chop_Reauthored.anim";
        private const string PreviewPath = "Assets/Screenshots/P05_Chop_Reauthored_KeyPoses.png";
        private const string IdleClipPath = "Assets/3rd/P05_Aki & Mika/Anim_demo/movetest_WAIT01.anim";

        private static readonly int[] SourceFrames = { 0, 5, 10, 15, 20, 24, 29 };

        private struct PoseDefinition
        {
            public PoseDefinition(Vector3 chestEuler, Vector3 leftHand, Vector3 rightHand, float elbowLift)
            {
                ChestEuler = chestEuler;
                LeftHand = leftHand;
                RightHand = rightHand;
                ElbowLift = elbowLift;
            }
            public Vector3 ChestEuler;
            public Vector3 LeftHand;
            public Vector3 RightHand;
            public float ElbowLift;
        }

        // Hand targets are expressed in P05 character-root space relative to Chest.
        // They encode the seven readable phases of a forward two-handed chop.
        private static readonly PoseDefinition[] Poses =
        {
            new PoseDefinition(new Vector3(0f, 0f, 0f),      new Vector3(-0.12f, -0.02f, 0.28f), new Vector3(0.16f, -0.06f, 0.34f), 0.02f),
            new PoseDefinition(new Vector3(-8f, -14f, 3f),   new Vector3(-0.04f, 0.12f, 0.12f),  new Vector3(0.34f, 0.18f, -0.02f), 0.12f),
            new PoseDefinition(new Vector3(-12f, -8f, 2f),   new Vector3(-0.10f, 0.42f, 0.16f),  new Vector3(0.24f, 0.55f, 0.06f), 0.18f),
            new PoseDefinition(new Vector3(5f, 4f, -2f),     new Vector3(-0.08f, 0.27f, 0.34f),  new Vector3(0.22f, 0.37f, 0.30f), 0.12f),
            new PoseDefinition(new Vector3(18f, 8f, -4f),    new Vector3(-0.08f, -0.38f, 0.58f), new Vector3(0.18f, -0.44f, 0.68f), 0.02f),
            new PoseDefinition(new Vector3(22f, 10f, -6f),   new Vector3(-0.10f, -0.52f, 0.50f), new Vector3(0.20f, -0.58f, 0.60f), -0.02f),
            new PoseDefinition(new Vector3(4f, 0f, 0f),      new Vector3(-0.12f, -0.05f, 0.30f), new Vector3(0.16f, -0.08f, 0.36f), 0.02f),
        };

        [MenuItem("Tools/Supernova/Animation/Build P05 Chopping From Key Poses")]
        public static void BuildAndBind()
        {
            GameObject sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            AnimationClip sourceClip = AssetDatabase.LoadAllAssetsAtPath(SourcePath)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => clip.name == "Chop_Start_Fast.001")
                ?? AssetDatabase.LoadAllAssetsAtPath(SourcePath)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (sourceAsset == null || sourceClip == null)
                throw new InvalidOperationException("Chopping source asset or clip is missing.");

            EnsureFolder(Path.GetDirectoryName(OutputPath).Replace('\\', '/'));
            EnsureFolder(Path.GetDirectoryName(PreviewPath).Replace('\\', '/'));

            GameObject source = UnityEngine.Object.Instantiate(sourceAsset);
            source.hideFlags = HideFlags.HideAndDontSave;
            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                Animator animator = player.GetComponentsInChildren<Animator>(true)
                    .FirstOrDefault(candidate => candidate.gameObject.activeInHierarchy && candidate.isHuman);
                if (animator == null || animator.avatar == null || !animator.avatar.isValid)
                    throw new InvalidOperationException("Player P05 Animator is not a valid Humanoid.");
                animator.enabled = false;

                Dictionary<string, Transform> sourceBones = IndexTransforms(source.transform);
                Dictionary<string, Transform> targetBones = IndexTransforms(animator.transform);
                ArmRig left = CreateArmRig(sourceBones, targetBones, true);
                ArmRig right = CreateArmRig(sourceBones, targetBones, false);
                Transform sourceChest = Require(sourceBones, "spine_upper");
                Transform targetChest = Require(targetBones, "Chest");
                Transform targetUpperChest = Require(targetBones, "UpperChest");

                Dictionary<Transform, Quaternion> sourceRest = CaptureLocalRotations(source.transform);
                Dictionary<Transform, Quaternion> targetRest = CaptureLocalRotations(animator.transform);
                ResetLocalRotations(sourceRest);
                ResetLocalRotations(targetRest);

                Quaternion sourceChestRest = sourceChest.rotation;
                Quaternion targetChestRest = targetChest.rotation;
                Quaternion targetUpperChestRest = targetUpperChest.rotation;
                Quaternion chestAlignment = targetChestRest * Quaternion.Inverse(sourceChestRest);
                left.CaptureRest();
                right.CaptureRest();

                float[] keyTimes = SourceFrames
                    .Select(frame => Mathf.Min(sourceClip.length, frame / sourceClip.frameRate))
                    .ToArray();
                float[,] muscleValues = new float[keyTimes.Length, HumanTrait.MuscleCount];
                AnimationClip idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);
                if (idleClip == null) throw new InvalidOperationException("P05 idle clip is missing: " + IdleClipPath);
                ResetLocalRotations(targetRest);
                idleClip.SampleAnimation(animator.gameObject, 0f);
                HumanPoseHandler baselineHandler = new HumanPoseHandler(animator.avatar, animator.transform);
                HumanPose standingPose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
                baselineHandler.GetHumanPose(ref standingPose);
                baselineHandler.Dispose();
                for (int poseIndex = 0; poseIndex < keyTimes.Length; poseIndex++)
                {
                    for (int muscle = 0; muscle < HumanTrait.MuscleCount; muscle++)
                        muscleValues[poseIndex, muscle] = standingPose.muscles[muscle];
                    WriteSemanticMusclePose(muscleValues, poseIndex);
                }
                ResetLocalRotations(targetRest);

                AnimationClip output = BuildHumanoidClip(
                    sourceClip,
                    keyTimes,
                    muscleValues,
                    standingPose.bodyPosition,
                    standingPose.bodyRotation);
                SaveClip(output, OutputPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                AnimationClip saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutputPath);
                BindMine(saved);
                source.SetActive(false);
                RenderKeyPosePreview(saved, keyTimes);
                RenderFourViewComparison(sourceAsset, sourceClip, saved, keyTimes);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("Built P05_Chop_Reauthored from 7 semantic poses and bound it to Mine. Preview: " + PreviewPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static float PoseChestWeight(int poseIndex)
        {
            // Preparation is restrained; acceleration/impact carries most torso rotation.
            float[] weights = { 0.2f, 0.45f, 0.65f, 0.8f, 1f, 0.85f, 0.55f };
            return weights[Mathf.Clamp(poseIndex, 0, weights.Length - 1)];
        }

        private static void WriteSemanticMusclePose(float[,] values, int pose)
        {
            // Right hand is the dominant pickaxe hand. Left hand stays nearer the
            // body as a supporting grip, so the two arms never mirror each other.
            float[] leftDownUp   = { -0.52f, -0.46f, -0.38f, -0.36f, -0.58f, -0.64f, -0.52f };
            float[] rightDownUp  = { -0.58f,  0.22f,  0.92f,  0.72f, -0.42f, -0.76f, -0.56f };
            float[] leftFront    = {  0.24f,  0.14f,  0.10f,  0.06f, -0.08f, -0.16f,  0.20f };
            float[] rightFront   = {  0.16f, -0.62f, -0.38f,  0.24f,  0.88f,  0.68f,  0.20f };
            float[] leftForearm  = { -0.74f, -0.78f, -0.82f, -0.80f, -0.66f, -0.58f, -0.72f };
            float[] rightForearm = { -0.48f, -0.58f, -0.64f, -0.34f,  0.42f,  0.68f, -0.44f };
            float[] chestForward = {  0.00f, -0.10f, -0.16f, -0.08f,  0.26f,  0.34f,  0.06f };
            float[] chestTwist   = {  0.00f, -0.18f, -0.12f, -0.04f,  0.12f,  0.16f,  0.00f };
            float[] shoulderLift = { -0.05f,  0.04f,  0.18f,  0.22f, -0.08f, -0.14f, -0.04f };

            SetMuscle(values, pose, "Spine Front-Back", chestForward[pose] * 0.3f);
            SetMuscle(values, pose, "Chest Front-Back", chestForward[pose] * 0.6f);
            SetMuscle(values, pose, "UpperChest Front-Back", chestForward[pose] * 0.8f);
            SetMuscle(values, pose, "Chest Twist Left-Right", chestTwist[pose] * 0.55f);
            SetMuscle(values, pose, "UpperChest Twist Left-Right", chestTwist[pose]);

            SetMuscle(values, pose, "Left Shoulder Down-Up", Mathf.Min(0.04f, shoulderLift[pose] * 0.35f));
            SetMuscle(values, pose, "Right Shoulder Down-Up", shoulderLift[pose]);
            SetMuscle(values, pose, "Left Shoulder Front-Back", leftFront[pose] * 0.16f);
            SetMuscle(values, pose, "Right Shoulder Front-Back", rightFront[pose] * 0.20f);
            SetMuscle(values, pose, "Left Arm Down-Up", leftDownUp[pose]);
            SetMuscle(values, pose, "Right Arm Down-Up", rightDownUp[pose]);
            SetMuscle(values, pose, "Left Arm Front-Back", leftFront[pose]);
            SetMuscle(values, pose, "Right Arm Front-Back", rightFront[pose]);
            SetMuscle(values, pose, "Left Arm Twist In-Out", -0.08f);
            SetMuscle(values, pose, "Right Arm Twist In-Out", 0.10f);
            SetMuscle(values, pose, "Left Forearm Stretch", leftForearm[pose]);
            SetMuscle(values, pose, "Right Forearm Stretch", rightForearm[pose]);
            SetMuscle(values, pose, "Left Forearm Twist In-Out", -0.10f);
            SetMuscle(values, pose, "Right Forearm Twist In-Out", 0.08f);
            SetMuscle(values, pose, "Left Hand Down-Up", pose >= 4 && pose <= 5 ? 0.04f : 0.12f);
            SetMuscle(values, pose, "Right Hand Down-Up", pose >= 4 && pose <= 5 ? -0.24f : 0.02f);
            SetMuscle(values, pose, "Left Hand In-Out", -0.16f);
            SetMuscle(values, pose, "Right Hand In-Out", 0.10f);
        }

        private static void SetMuscle(float[,] values, int pose, string muscleName, float value)
        {
            int index = Array.IndexOf(HumanTrait.MuscleName, muscleName);
            if (index >= 0) values[pose, index] = Mathf.Clamp(value, -1f, 1f);
        }

        private static AnimationClip BuildHumanoidClip(
            AnimationClip source,
            float[] keyTimes,
            float[,] muscles,
            Vector3 rootPosition,
            Quaternion rootRotation)
        {
            AnimationClip output = new AnimationClip
            {
                name = "P05_Chop_Reauthored",
                frameRate = source.frameRate,
            };
            for (int muscle = 0; muscle < HumanTrait.MuscleCount; muscle++)
            {
                string muscleName = HumanTrait.MuscleName[muscle];
                int captured = muscle;
                SetAnimatorCurve(output, muscleName, keyTimes, i => muscles[i, captured]);
            }
            SetAnimatorCurve(output, "RootT.x", keyTimes, i => rootPosition.x);
            SetAnimatorCurve(output, "RootT.y", keyTimes, i => rootPosition.y);
            SetAnimatorCurve(output, "RootT.z", keyTimes, i => rootPosition.z);
            SetAnimatorCurve(output, "RootQ.x", keyTimes, i => rootRotation.x);
            SetAnimatorCurve(output, "RootQ.y", keyTimes, i => rootRotation.y);
            SetAnimatorCurve(output, "RootQ.z", keyTimes, i => rootRotation.z);
            SetAnimatorCurve(output, "RootQ.w", keyTimes, i => rootRotation.w);
            SetLoop(output, false);
            return output;
        }

        private static void SetAnimatorCurve(
            AnimationClip clip,
            string property,
            float[] keyTimes,
            Func<int, float> value)
        {
            Keyframe[] keys = new Keyframe[keyTimes.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(keyTimes[i], value(i));
            AnimationCurve curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), property),
                curve);
        }

        private static bool IsAuthoredMuscle(string name)
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

        private static void BindMine(AnimationClip clip)
        {
            if (clip == null) throw new InvalidOperationException("Generated P05 chopping clip could not be loaded.");
            UnityAnimatorController controller = AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(ControllerPath);
            AnimatorState mine = controller.layers[0].stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(state => state.name == "Mine");
            if (mine == null) throw new InvalidOperationException("Mine state is missing from P05Player.controller.");
            mine.motion = clip;
            EditorUtility.SetDirty(mine);
            EditorUtility.SetDirty(controller);
        }

        private static void RenderFourViewComparison(
            GameObject sourcePrefab,
            AnimationClip sourceClip,
            AnimationClip targetClip,
            float[] keyTimes)
        {
            GameObject targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var views = new[]
            {
                new { Name = "Front", Direction = Vector3.forward, Up = Vector3.up },
                new { Name = "Side", Direction = Vector3.right, Up = Vector3.up },
                new { Name = "Top", Direction = Vector3.up, Up = Vector3.forward },
                new { Name = "Diagonal45", Direction = new Vector3(1f, 0.28f, 1f).normalized, Up = Vector3.up },
            };
            foreach (var view in views)
            {
                RenderPoseStrip(
                    sourcePrefab,
                    sourceClip,
                    keyTimes,
                    "Assets/Screenshots/ChopCompare_Source_" + view.Name + ".png",
                    view.Direction,
                    view.Up);
                RenderPoseStrip(
                    targetPrefab,
                    targetClip,
                    keyTimes,
                    "Assets/Screenshots/ChopCompare_P05_" + view.Name + ".png",
                    view.Direction,
                    view.Up);
            }
        }

        public static void RenderPoseStrip(
            GameObject prefab,
            AnimationClip clip,
            float[] keyTimes,
            string outputPath,
            Vector3 viewDirection,
            Vector3 cameraUp)
        {
            if (prefab == null || clip == null) return;
            List<GameObject> roots = new List<GameObject>();
            GameObject cameraObject = null;
            GameObject lightObject = null;
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            try
            {
                Vector3 stripDirection = Vector3.Cross(cameraUp, viewDirection).normalized;
                for (int i = 0; i < keyTimes.Length; i++)
                {
                    GameObject wrapper = new GameObject("ComparePose_" + i) { hideFlags = HideFlags.HideAndDontSave };
                    wrapper.transform.position = stripDirection * ((3 - i) * 1.35f);
                    GameObject instance = UnityEngine.Object.Instantiate(prefab);
                    instance.hideFlags = HideFlags.HideAndDontSave;
                    instance.transform.SetParent(wrapper.transform, false);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    Transform cameraRig = instance.transform.Find("CameraRig");
                    if (cameraRig != null) cameraRig.gameObject.SetActive(false);
                    SetLayerRecursively(wrapper, 31);
                    Animator animator = instance.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    clip.SampleAnimation(animator != null ? animator.gameObject : instance, keyTimes[i]);
                    roots.Add(wrapper);
                }

                Renderer[] renderers = roots
                    .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                    .Where(renderer => renderer.gameObject.activeInHierarchy)
                    .ToArray();
                if (renderers.Length == 0) return;
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                cameraObject = new GameObject("CompareCamera") { hideFlags = HideFlags.HideAndDontSave };
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.cullingMask = 1 << 31;
                camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
                camera.orthographic = true;
                float aspect = 1800f / 520f;
                camera.orthographicSize = Mathf.Max(bounds.extents.y * 1.25f, bounds.extents.x / aspect * 1.25f);
                camera.transform.position = bounds.center + viewDirection.normalized * 12f;
                camera.transform.rotation = Quaternion.LookRotation(-viewDirection.normalized, cameraUp);

                lightObject = new GameObject("CompareLight") { hideFlags = HideFlags.HideAndDontSave };
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.4f;
                light.transform.rotation = Quaternion.Euler(35f, -35f, 0f);

                renderTexture = new RenderTexture(1800, 520, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture = new Texture2D(1800, 520, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0f, 0f, 1800f, 520f), 0, 0);
                texture.Apply();
                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = null;
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (renderTexture != null) UnityEngine.Object.DestroyImmediate(renderTexture);
                if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
                if (lightObject != null) UnityEngine.Object.DestroyImmediate(lightObject);
                foreach (GameObject root in roots) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void RenderKeyPosePreview(AnimationClip clip, float[] keyTimes)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(P05PrefabPath);
            if (prefab == null || clip == null) return;
            List<GameObject> instances = new List<GameObject>();
            GameObject cameraObject = null;
            GameObject lightObject = null;
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            try
            {
                for (int i = 0; i < keyTimes.Length; i++)
                {
                    GameObject wrapper = new GameObject("Pose_" + i) { hideFlags = HideFlags.HideAndDontSave };
                    wrapper.transform.position = new Vector3((3 - i) * 1.35f, 0f, 0f);
                    GameObject instance = UnityEngine.Object.Instantiate(prefab);
                    instance.hideFlags = HideFlags.HideAndDontSave;
                    instance.transform.SetParent(wrapper.transform, false);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    Transform cameraRig = instance.transform.Find("CameraRig");
                    if (cameraRig != null) cameraRig.gameObject.SetActive(false);
                    SetLayerRecursively(wrapper, 31);
                    Animator animator = instance.GetComponentInChildren<Animator>(true);
                    if (animator != null) animator.enabled = false;
                    clip.SampleAnimation(animator != null ? animator.gameObject : instance, keyTimes[i]);
                    instances.Add(wrapper);
                }

                cameraObject = new GameObject("P05ChopPreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.cullingMask = 1 << 31;
                camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
                Renderer[] previewRenderers = instances
                    .SelectMany(instance => instance.GetComponentsInChildren<Renderer>(true))
                    .Where(renderer => renderer.gameObject.activeInHierarchy)
                    .ToArray();
                Bounds previewBounds = previewRenderers[0].bounds;
                for (int i = 1; i < previewRenderers.Length; i++) previewBounds.Encapsulate(previewRenderers[i].bounds);
                camera.orthographic = true;
                float aspect = 1800f / 520f;
                camera.orthographicSize = Mathf.Max(
                    previewBounds.extents.y * 1.25f,
                    previewBounds.extents.x / aspect * 1.25f);
                camera.transform.position = previewBounds.center + Vector3.forward * 12f;
                camera.transform.LookAt(previewBounds.center);

                lightObject = new GameObject("P05ChopPreviewLight") { hideFlags = HideFlags.HideAndDontSave };
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.4f;
                light.transform.rotation = Quaternion.Euler(35f, -35f, 0f);

                renderTexture = new RenderTexture(1800, 520, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(PreviewPath, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = null;
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (renderTexture != null) UnityEngine.Object.DestroyImmediate(renderTexture);
                if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
                if (lightObject != null) UnityEngine.Object.DestroyImmediate(lightObject);
                foreach (GameObject instance in instances) UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private sealed class ArmRig
        {
            public Transform SourceChest;
            public Transform SourceShoulder;
            public Transform SourceElbow;
            public Transform SourceHand;
            public Transform TargetChest;
            public Transform TargetUpper;
            public Transform TargetLower;
            public Transform TargetHand;
            public Quaternion SourceHandRest;
            public Quaternion TargetHandRest;
            public float SourceLength;
            public float TargetLength;

            public void CaptureRest()
            {
                SourceHandRest = SourceHand.rotation;
                TargetHandRest = TargetHand.rotation;
                SourceLength = Vector3.Distance(SourceShoulder.position, SourceElbow.position)
                    + Vector3.Distance(SourceElbow.position, SourceHand.position);
                TargetLength = Vector3.Distance(TargetUpper.position, TargetLower.position)
                    + Vector3.Distance(TargetLower.position, TargetHand.position);
            }

            public void SolveManual(Vector3 desiredHand, Vector3 desiredPole)
            {
                SolveTwoBone(TargetUpper, TargetLower, TargetHand, desiredHand, desiredPole);
                // Preserve P05's authored hand axes; the arm chain supplies the swing.
                TargetHand.localRotation = Quaternion.Slerp(TargetHand.localRotation, Quaternion.identity, 0.08f);
            }

            public void Solve(Quaternion chestAlignment, int poseIndex)
            {
                float scale = TargetLength / Mathf.Max(0.0001f, SourceLength);
                Vector3 sourceHandOffset = SourceHand.position - SourceShoulder.position;
                Vector3 sourceElbowOffset = SourceElbow.position - SourceShoulder.position;
                Vector3 desiredHand = TargetUpper.position + chestAlignment * sourceHandOffset * scale;
                Vector3 desiredPole = TargetUpper.position + chestAlignment * sourceElbowOffset * scale;
                SolveTwoBone(TargetUpper, TargetLower, TargetHand, desiredHand, desiredPole);

                Quaternion handAlignment = TargetHandRest * Quaternion.Inverse(SourceHandRest);
                Quaternion desiredRotation = handAlignment * SourceHand.rotation;
                float handWeight = poseIndex == 4 ? 0.9f : 0.72f;
                TargetHand.rotation = Quaternion.Slerp(TargetHand.rotation, desiredRotation, handWeight);
            }
        }

        private static ArmRig CreateArmRig(
            Dictionary<string, Transform> source,
            Dictionary<string, Transform> target,
            bool left)
        {
            string suffix = left ? "l" : "r";
            string targetSuffix = left ? "L" : "R";
            return new ArmRig
            {
                SourceChest = Require(source, "spine_upper"),
                SourceShoulder = Require(source, "arm_upper_" + suffix),
                SourceElbow = Require(source, "arm_lower_" + suffix),
                SourceHand = Require(source, "hand_" + suffix),
                TargetChest = Require(target, "Chest"),
                TargetUpper = Require(target, "Arm_" + targetSuffix),
                TargetLower = Require(target, "forearm_" + targetSuffix),
                TargetHand = Require(target, "hand_" + targetSuffix),
            };
        }

        private static void SolveTwoBone(
            Transform upper,
            Transform lower,
            Transform hand,
            Vector3 desiredHand,
            Vector3 pole)
        {
            Vector3 shoulder = upper.position;
            float upperLength = Vector3.Distance(upper.position, lower.position);
            float lowerLength = Vector3.Distance(lower.position, hand.position);
            Vector3 toHand = desiredHand - shoulder;
            float distance = Mathf.Clamp(toHand.magnitude, 0.001f, (upperLength + lowerLength) * 0.985f);
            Vector3 direction = toHand.normalized;
            Vector3 poleDirection = pole - shoulder;
            poleDirection -= direction * Vector3.Dot(poleDirection, direction);
            if (poleDirection.sqrMagnitude < 0.000001f) poleDirection = Vector3.Cross(direction, Vector3.up);
            poleDirection.Normalize();

            float cosShoulder = Mathf.Clamp(
                (upperLength * upperLength + distance * distance - lowerLength * lowerLength)
                / (2f * upperLength * distance),
                -1f,
                1f);
            float sinShoulder = Mathf.Sqrt(Mathf.Max(0f, 1f - cosShoulder * cosShoulder));
            Vector3 desiredElbow = shoulder
                + direction * (cosShoulder * upperLength)
                + poleDirection * (sinShoulder * upperLength);

            Vector3 currentUpperDirection = lower.position - upper.position;
            Vector3 desiredUpperDirection = desiredElbow - upper.position;
            upper.rotation = Quaternion.FromToRotation(currentUpperDirection, desiredUpperDirection) * upper.rotation;

            Vector3 currentLowerDirection = hand.position - lower.position;
            Vector3 desiredLowerDirection = desiredHand - lower.position;
            lower.rotation = Quaternion.FromToRotation(currentLowerDirection, desiredLowerDirection) * lower.rotation;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
        }

        private static Transform Require(Dictionary<string, Transform> transforms, string name)
        {
            if (!transforms.TryGetValue(name, out Transform value))
                throw new InvalidOperationException("Required bone is missing: " + name);
            return value;
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
            foreach (KeyValuePair<Transform, Quaternion> pair in rotations) pair.Key.localRotation = pair.Value;
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
            SerializedProperty loopTime = settings != null ? settings.FindPropertyRelative("m_LoopTime") : null;
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
