#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Supernova.Gameplay;
using Supernova.Voxels;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityAnimatorController = UnityEditor.Animations.AnimatorController;

namespace Supernova.EditorTools.PlayerSetup
{
    /// <summary>
    /// Rebinds the gameplay animator and camera after replacing the visual character prefab.
    /// The operation is idempotent and is also available from the Tools menu.
    /// </summary>
    public static class CurrentPlayerModelBinder
    {
        private const string PlayerPrefabPath = ProjectAssetPaths.Prefabs.Player;
        private const string ControllerPath = ProjectAssetPaths.Animations.PlayerController;
        private const string SourceControllerPath = ProjectAssetPaths.ThirdParty.MuryotaisuController;
        private const string CrouchIdleStateName = "Crouch Idle";
        private const string CrouchMoveStateName = "Crouch Move";

        [InitializeOnLoadMethod]
        private static void ScheduleCrouchControllerUpgrade()
        {
            EditorApplication.delayCall += UpgradeCrouchController;
        }

        private static void UpgradeCrouchController()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            UnityAnimatorController controller =
                AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(ControllerPath);
            if (controller == null || !ConfigureCrouchStates(controller)) return;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Supernova/Player/Rebind Current Model And Camera")]
        public static void Repair()
        {
            CloseAnimatorWindows();
            UnityAnimatorController runtimeController = BuildController();
            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                RemoveInactiveLegacyVisuals(player);
                Animator animator = FindActiveHumanoidAnimator(player);
                if (animator == null)
                    throw new InvalidOperationException("No active Humanoid Animator was found in Player.prefab.");

                Transform visualRoot = FindDirectChildContaining(player.transform, animator.transform);
                ResetVisualVisibility(animator);
                AlignVisualFeetToPlayerRoot(player.transform, visualRoot);

                animator.runtimeAnimatorController = runtimeController;
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head == null) head = FindByName(animator.transform, "Head");
                if (head == null)
                    throw new InvalidOperationException("The active model has no mapped Head bone.");

                Camera camera = RebuildCameraRig(player, head, animator);

                VoxelPlayerController playerController = player.GetComponent<VoxelPlayerController>();
                if (playerController != null)
                {
                    playerController.SetAnimator(animator);
                    SerializedObject serializedController = new SerializedObject(playerController);
                    serializedController.FindProperty("animator").objectReferenceValue = animator;
                    serializedController.FindProperty("view").objectReferenceValue = camera.transform;
                    serializedController.ApplyModifiedPropertiesWithoutUndo();
                }

                EditorUtility.SetDirty(animator);
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Player model rebound: P05 animations, root-motion isolation, collider alignment and camera head anchor are active.");
            EditorApplication.delayCall += P05AnimatorViewReset.OpenAndFrameAll;
        }

        private static void CloseAnimatorWindows()
        {
            EditorWindow[] windows = UnityEngine.Object.FindObjectsOfType<EditorWindow>();
            foreach (EditorWindow window in windows)
                if (window.GetType().FullName == "UnityEditor.Graphs.AnimatorControllerTool")
                    window.Close();
        }

        private static UnityAnimatorController BuildController()
        {
            UnityAnimatorController source = AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(SourceControllerPath);
            if (source == null)
                throw new InvalidOperationException("Missing Muryotaisu controller: " + SourceControllerPath);

            UnityAnimatorController controller = AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(ControllerPath);
            if (controller == null)
                controller = UnityAnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            for (int i = controller.parameters.Length - 1; i >= 0; i--)
                controller.RemoveParameter(i);
            foreach (AnimatorControllerParameter parameter in source.parameters)
            {
                if (parameter.name == "Mine"
                    || parameter.name == "Knockdown"
                    || parameter.name == "Hit"
                    || parameter.name == "Die"
                    || parameter.name == "Recover")
                    continue;
                AnimatorControllerParameter copy = new AnimatorControllerParameter
                {
                    name = parameter.name,
                    type = parameter.type,
                    defaultBool = parameter.defaultBool,
                    defaultFloat = parameter.defaultFloat,
                    defaultInt = parameter.defaultInt,
                };
                controller.AddParameter(copy);
            }
            EnsureParameter(controller, "Mine", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "Hit", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "Die", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "Recover", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "crouchFlag", AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, "crouchMoveFlag", AnimatorControllerParameterType.Bool);

            while (controller.layers.Length > 1)
                controller.RemoveLayer(controller.layers.Length - 1);

            AnimationClip idleClip = null;
            AnimationClip runClip = null;
            AnimationClip jumpClip = null;
            AnimationClip idleBClip = null;

            Dictionary<string, AnimatorState>[] layerStates = new Dictionary<string, AnimatorState>[source.layers.Length];
            for (int layerIndex = 0; layerIndex < source.layers.Length; layerIndex++)
            {
                AnimatorControllerLayer sourceLayer = source.layers[layerIndex];
                if (layerIndex > 0) controller.AddLayer(sourceLayer.name);
                AnimatorControllerLayer targetLayer = controller.layers[layerIndex];
                targetLayer.name = sourceLayer.name;
                targetLayer.defaultWeight = sourceLayer.defaultWeight;
                targetLayer.blendingMode = sourceLayer.blendingMode;
                targetLayer.iKPass = sourceLayer.iKPass;
                // Muryotaisu's face mask and blendshape paths do not match P05.
                // Keep the same Face Layer graph but leave its motion bindings empty.
                targetLayer.avatarMask = null;
                AnimatorControllerLayer[] updatedLayers = controller.layers;
                updatedLayers[layerIndex] = targetLayer;
                controller.layers = updatedLayers;
                targetLayer = controller.layers[layerIndex];

                AnimatorStateMachine targetMachine = targetLayer.stateMachine;
                ClearStateMachine(targetMachine);
                layerStates[layerIndex] = CloneStateMachineStructure(
                    sourceLayer.stateMachine,
                    targetMachine,
                    layerIndex,
                    idleClip,
                    runClip,
                    jumpClip,
                    idleBClip);
            }

            for (int layerIndex = 0; layerIndex < source.layers.Length; layerIndex++)
            {
                CloneTransitions(
                    source.layers[layerIndex].stateMachine,
                    controller.layers[layerIndex].stateMachine,
                    layerStates[layerIndex]);
            }

            AnimatorStateMachine baseMachine = controller.layers[0].stateMachine;
            Dictionary<string, AnimatorState> baseStates = layerStates[0];
            AnimatorState idle = baseStates.TryGetValue("Idle", out AnimatorState idleState)
                ? idleState
                : baseMachine.defaultState;
            ConfigureCrouchStates(controller);

            AnimatorState mine = baseMachine.AddState("Mine", new Vector3(720f, 40f));
            mine.motion = null;
            AnimatorState hit = baseMachine.AddState("Hit", new Vector3(720f, 200f));
            hit.motion = null;
            AnimatorState die = baseMachine.AddState("Die", new Vector3(940f, 200f));
            die.motion = null;

            AnimatorStateTransition mineAny = baseMachine.AddAnyStateTransition(mine);
            mineAny.hasExitTime = false;
            mineAny.canTransitionToSelf = false;
            mineAny.duration = 0.06f;
            mineAny.AddCondition(AnimatorConditionMode.If, 0f, "Mine");
            AnimatorStateTransition mineExit = mine.AddTransition(idle);
            mineExit.hasExitTime = true;
            mineExit.exitTime = 0.7f;
            mineExit.hasFixedDuration = false;
            mineExit.duration = 0.2f;

            AnimatorStateTransition hitAny = baseMachine.AddAnyStateTransition(hit);
            hitAny.hasExitTime = false;
            hitAny.canTransitionToSelf = false;
            hitAny.duration = 0.05f;
            hitAny.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
            AnimatorStateTransition recover = hit.AddTransition(idle);
            recover.hasExitTime = false;
            recover.duration = 0.18f;
            recover.AddCondition(AnimatorConditionMode.If, 0f, "Recover");

            AnimatorStateTransition dieAny = baseMachine.AddAnyStateTransition(die);
            dieAny.hasExitTime = false;
            dieAny.canTransitionToSelf = false;
            dieAny.duration = 0.05f;
            dieAny.AddCondition(AnimatorConditionMode.If, 0f, "Die");

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static bool EnsureParameter(
            UnityAnimatorController controller,
            string name,
            AnimatorControllerParameterType type)
        {
            if (controller.parameters.Any(parameter => parameter.name == name)) return false;
            controller.AddParameter(name, type);
            return true;
        }

        private const string LowerBodyLayerName = "LowerBody Layer";
        private const string LowerBodyMaskPath = ProjectAssetPaths.ThirdParty.LowerBodyMask;
        private const string CrouchIdleClipPath = ProjectAssetPaths.Animations.CrouchIdle;
        private const string CrouchMoveClipPath = ProjectAssetPaths.Animations.CrouchMove;

        // Crouch lives on its own masked layer (legs/feet only) so it never overrides
        // the upper body: the player can still swing a tool while crouched. The layer's
        // runtime weight is toggled by VoxelPlayerController, not by animator transitions.
        private static bool ConfigureCrouchStates(UnityAnimatorController controller)
        {
            bool changed = EnsureParameter(
                controller,
                "crouchFlag",
                AnimatorControllerParameterType.Bool);
            changed |= EnsureParameter(
                controller,
                "crouchMoveFlag",
                AnimatorControllerParameterType.Bool);

            AnimatorStateMachine baseMachine = controller.layers[0].stateMachine;
            AnimatorState legacyCrouchIdle = FindState(baseMachine, CrouchIdleStateName);
            AnimatorState legacyCrouchMove = FindState(baseMachine, CrouchMoveStateName);
            if (legacyCrouchIdle != null)
            {
                RemoveAnyStateTransitionsTo(baseMachine, legacyCrouchIdle);
                baseMachine.RemoveState(legacyCrouchIdle);
                changed = true;
            }
            if (legacyCrouchMove != null)
            {
                RemoveAnyStateTransitionsTo(baseMachine, legacyCrouchMove);
                baseMachine.RemoveState(legacyCrouchMove);
                changed = true;
            }

            changed |= EnsureLowerBodyLayer(controller);
            return changed;
        }

        private static bool EnsureLowerBodyLayer(UnityAnimatorController controller)
        {
            int layerIndex = -1;
            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].name == LowerBodyLayerName) layerIndex = i;

            if (layerIndex < 0)
            {
                controller.AddLayer(LowerBodyLayerName);
                layers = controller.layers;
                layerIndex = layers.Length - 1;
            }

            AnimatorControllerLayer lowerBodyLayer = layers[layerIndex];
            AnimatorStateMachine machine = lowerBodyLayer.stateMachine;
            AnimatorState crouchIdle = FindState(machine, "CrouchIdle");
            AnimatorState crouchMove = FindState(machine, "CrouchMove");
            if (crouchIdle != null && crouchMove != null)
                return false;

            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(LowerBodyMaskPath);
            AnimationClip crouchIdleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CrouchIdleClipPath);
            AnimationClip crouchMoveClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CrouchMoveClipPath);
            if (mask == null || crouchIdleClip == null || crouchMoveClip == null)
            {
                throw new InvalidOperationException(
                    "Missing LowerBodyMask.mask or crouch clips required to build the LowerBody Layer.");
            }

            lowerBodyLayer.avatarMask = mask;
            lowerBodyLayer.blendingMode = AnimatorLayerBlendingMode.Override;
            lowerBodyLayer.defaultWeight = 0f;

            if (crouchIdle == null)
            {
                crouchIdle = machine.AddState("CrouchIdle", new Vector3(300f, 620f));
                crouchIdle.motion = crouchIdleClip;
            }
            if (crouchMove == null)
            {
                crouchMove = machine.AddState("CrouchMove", new Vector3(560f, 620f));
                crouchMove.motion = crouchMoveClip;
            }
            machine.defaultState = crouchIdle;

            AnimatorStateTransition toMove = crouchIdle.AddTransition(crouchMove);
            ConfigureImmediateTransition(toMove);
            toMove.AddCondition(AnimatorConditionMode.If, 0f, "crouchMoveFlag");

            AnimatorStateTransition toIdle = crouchMove.AddTransition(crouchIdle);
            ConfigureImmediateTransition(toIdle);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "crouchMoveFlag");

            layers[layerIndex] = lowerBodyLayer;
            controller.layers = layers;
            return true;
        }

        private static void RemoveAnyStateTransitionsTo(AnimatorStateMachine machine, AnimatorState target)
        {
            List<AnimatorStateTransition> anyTransitions =
                new List<AnimatorStateTransition>(machine.anyStateTransitions);
            foreach (AnimatorStateTransition transition in anyTransitions)
                if (transition.destinationState == target) machine.RemoveAnyStateTransition(transition);
        }

        private static void ConfigureImmediateTransition(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.12f;
            transition.canTransitionToSelf = false;
        }

        private static AnimatorState FindState(AnimatorStateMachine machine, string name)
        {
            foreach (ChildAnimatorState child in machine.states)
            {
                if (child.state.name == name) return child.state;
            }
            return null;
        }

        private static void ClearStateMachine(AnimatorStateMachine machine)
        {
            foreach (AnimatorStateTransition transition in machine.anyStateTransitions.ToArray())
                machine.RemoveAnyStateTransition(transition);
            foreach (ChildAnimatorState child in machine.states.ToArray())
                machine.RemoveState(child.state);
            foreach (ChildAnimatorStateMachine child in machine.stateMachines.ToArray())
                machine.RemoveStateMachine(child.stateMachine);
        }

        private static Dictionary<string, AnimatorState> CloneStateMachineStructure(
            AnimatorStateMachine source,
            AnimatorStateMachine target,
            int layerIndex,
            AnimationClip idleClip,
            AnimationClip runClip,
            AnimationClip jumpClip,
            AnimationClip idleBClip)
        {
            target.entryPosition = source.entryPosition;
            target.anyStatePosition = source.anyStatePosition;
            target.exitPosition = source.exitPosition;
            target.parentStateMachinePosition = source.parentStateMachinePosition;

            Dictionary<string, AnimatorState> states = new Dictionary<string, AnimatorState>();
            foreach (ChildAnimatorState child in source.states)
            {
                AnimatorState sourceState = child.state;
                if (sourceState.name == "Sparse Mine" || sourceState.name == "Sparse Knockdown")
                    continue;
                AnimatorState targetState = target.AddState(sourceState.name, child.position);
                targetState.speed = sourceState.speed;
                targetState.mirror = sourceState.mirror;
                targetState.cycleOffset = sourceState.cycleOffset;
                targetState.iKOnFeet = sourceState.iKOnFeet;
                targetState.writeDefaultValues = sourceState.writeDefaultValues;
                targetState.motion = ResolveP05Motion(
                    layerIndex,
                    sourceState.name,
                    idleClip,
                    runClip,
                    jumpClip,
                    idleBClip);
                states.Add(sourceState.name, targetState);
            }

            if (source.defaultState != null
                && states.TryGetValue(source.defaultState.name, out AnimatorState defaultState))
                target.defaultState = defaultState;
            return states;
        }

        private static Motion ResolveP05Motion(
            int layerIndex,
            string stateName,
            AnimationClip idleClip,
            AnimationClip runClip,
            AnimationClip jumpClip,
            AnimationClip idleBClip)
        {
            // The controller owns only graph structure. Motion assignment is intentionally
            // left to the user in the Animator editor.
            return null;
        }

        private static void CloneTransitions(
            AnimatorStateMachine sourceMachine,
            AnimatorStateMachine targetMachine,
            Dictionary<string, AnimatorState> targetStates)
        {
            foreach (ChildAnimatorState child in sourceMachine.states)
            {
                if (!targetStates.TryGetValue(child.state.name, out AnimatorState sourceTarget)) continue;
                foreach (AnimatorStateTransition sourceTransition in child.state.transitions)
                {
                    AnimatorStateTransition targetTransition;
                    if (sourceTransition.isExit)
                    {
                        targetTransition = sourceTarget.AddExitTransition();
                    }
                    else if (sourceTransition.destinationState != null
                        && targetStates.TryGetValue(sourceTransition.destinationState.name, out AnimatorState destination))
                    {
                        targetTransition = sourceTarget.AddTransition(destination);
                    }
                    else
                    {
                        continue;
                    }
                    CopyTransition(sourceTransition, targetTransition);
                }
            }

            foreach (AnimatorStateTransition sourceTransition in sourceMachine.anyStateTransitions)
            {
                if (sourceTransition.destinationState == null
                    || !targetStates.TryGetValue(sourceTransition.destinationState.name, out AnimatorState destination))
                    continue;
                AnimatorStateTransition targetTransition = targetMachine.AddAnyStateTransition(destination);
                CopyTransition(sourceTransition, targetTransition);
            }
        }

        private static void CopyTransition(
            AnimatorStateTransition source,
            AnimatorStateTransition target)
        {
            bool automaticTransition = source.conditions.Length == 0;
            target.hasExitTime = automaticTransition;
            target.exitTime = automaticTransition ? 0.7f : 0f;
            target.duration = automaticTransition ? 0.2f : 0.12f;
            target.offset = source.offset;
            target.hasFixedDuration = !automaticTransition;
            target.interruptionSource = source.interruptionSource;
            target.orderedInterruption = source.orderedInterruption;
            target.canTransitionToSelf = source.canTransitionToSelf;
            target.mute = source.mute;
            target.solo = source.solo;
            foreach (AnimatorCondition condition in source.conditions)
                target.AddCondition(condition.mode, condition.threshold, condition.parameter);
        }

        private static AnimationClip LoadClip(string path, bool required)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null && required)
                throw new InvalidOperationException("Missing required animation clip: " + path);
            return clip;
        }

        private static AnimationClip LoadEmbeddedClip(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
        }

        private static void AddConditionTransition(
            AnimatorState source,
            AnimatorState destination,
            string parameter,
            bool value,
            float duration)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
        }

        private static Animator FindActiveHumanoidAnimator(GameObject player)
        {
            Animator[] animators = player.GetComponentsInChildren<Animator>(true);
            return animators.FirstOrDefault(a => a.gameObject.activeInHierarchy && a.isHuman && a.avatar != null && a.avatar.isValid)
                ?? animators.FirstOrDefault(a => a.gameObject.activeInHierarchy);
        }

        private static void RemoveInactiveLegacyVisuals(GameObject player)
        {
            Transform[] directChildren = new Transform[player.transform.childCount];
            for (int i = 0; i < directChildren.Length; i++)
                directChildren[i] = player.transform.GetChild(i);

            foreach (Transform child in directChildren)
            {
                if (child != null && child.name == "CharacterVisual" && !child.gameObject.activeSelf)
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        private static Transform FindDirectChildContaining(Transform player, Transform descendant)
        {
            Transform current = descendant;
            while (current.parent != null && current.parent != player) current = current.parent;
            return current.parent == player ? current : descendant;
        }

        private static void ResetVisualVisibility(Animator animator)
        {
            foreach (Renderer renderer in animator.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.enabled = true;
            }

            Transform head = animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Head)
                : FindByName(animator.transform, "Head");
            if (head != null) head.localScale = Vector3.one;
        }

        private static void AlignVisualFeetToPlayerRoot(Transform player, Transform visualRoot)
        {
            visualRoot.localPosition = new Vector3(0f, visualRoot.localPosition.y, 0f);
            visualRoot.localRotation = Quaternion.identity;
            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float correction = player.position.y - bounds.min.y;
            visualRoot.position += player.up * correction;
        }

        private static Camera RebuildCameraRig(GameObject player, Transform head, Animator animator)
        {
            Transform oldRig = null;
            foreach (Transform child in player.transform)
                if (child.name == "CameraRig") { oldRig = child; break; }
            if (oldRig != null) UnityEngine.Object.DestroyImmediate(oldRig.gameObject);

            GameObject rigObject = new GameObject("CameraRig");
            rigObject.transform.SetParent(player.transform, false);
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(rigObject.transform, false);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 75f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 1000f;
            cameraObject.AddComponent<AudioListener>();
            PerspectiveCameraController perspective = cameraObject.AddComponent<PerspectiveCameraController>();

            Vector3 eyePosition = head.position + player.transform.up * 0.025f + player.transform.forward * 0.075f;
            camera.transform.SetPositionAndRotation(eyePosition, player.transform.rotation);
            Renderer[] hiddenRenderers = animator.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.gameObject.activeInHierarchy && IsHeadAccessoryRenderer(r))
                .ToArray();
            perspective.Bind(player.transform, head, camera, hiddenRenderers);
            perspective.SetLookPitch(0f);
            return camera;
        }

        private static bool IsHeadAccessoryRenderer(Renderer renderer)
        {
            string name = renderer.name.ToLowerInvariant();
            return name.Contains("hair")
                || name.Contains("head")
                || name.Contains("helmet")
                || name.Contains("face")
                || name.Contains("eye");
        }

        private static Transform FindByName(Transform root, string name)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                if (transform.name == name) return transform;
            return null;
        }
    }
}
#endif
