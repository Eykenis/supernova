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
        private static void ScheduleAnimatorControllerUpgrade()
        {
            EditorApplication.delayCall += UpgradeAnimatorController;
        }

        internal static void UpgradeAnimatorController()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            UnityAnimatorController controller =
                AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(ControllerPath);
            if (controller == null) return;

            bool changed = ConfigureCrouchStates(controller);
            changed |= ConfigureFirearmLocomotionLayer(controller);
            changed |= ConfigureToolUpperBodyLayer(controller);
            if (!changed) return;

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
            EnsureParameter(controller, "ToolAction", AnimatorControllerParameterType.Trigger);
            EnsureParameter(
                controller,
                "ToolActionContinuous",
                AnimatorControllerParameterType.Bool);
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
            ConfigureFirearmLocomotionLayer(controller);

            AnimatorState hit = baseMachine.AddState("Hit", new Vector3(720f, 200f));
            hit.motion = null;
            AnimatorState die = baseMachine.AddState("Die", new Vector3(940f, 200f));
            die.motion = null;

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

            ConfigureToolUpperBodyLayer(controller);

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
        private const string CrouchArmsLocomotionLayerName =
            "Crouch Arms Locomotion Layer";
        private const string FirearmLocomotionLayerName =
            "Firearm Locomotion Layer";
        private const string FirearmArmsLayerName = "Firearm Arms Layer";
        private const string ToolUpperBodyLayerName = "Tool UpperBody Layer";
        private const string CrouchToolArmsLayerName = "Crouch Tool Arms Layer";
        private const string CrouchArmsIdleStateName = "Standing Arms Idle";
        private const string ToolInactiveStateName = "Tool Inactive";
        private const string ToolPrimaryStateName = "Tool Primary Action";
        private const string ToolContinuousStateName = "Tool Continuous Action";
        private const string ToolUpperBodyMaskPath =
            ProjectAssetPaths.Animations.ToolUpperBodyMask;
        private const string CrouchToolArmsMaskPath =
            ProjectAssetPaths.Animations.CrouchToolArmsMask;
        private const string ToolPrimaryActionClipPath =
            ProjectAssetPaths.Animations.ToolPrimaryActionPlaceholder;
        private const string CrouchIdleClipPath = ProjectAssetPaths.Animations.CrouchIdle;
        private const string CrouchMoveClipPath = ProjectAssetPaths.Animations.CrouchMove;
        private const string FirearmIdleStateName = "Firearm Idle";
        private const string FirearmMoveStateName = "Firearm Move";
        private const string FirearmArmsStateName = "Firearm Arms";

        private static bool ConfigureFirearmLocomotionLayer(
            UnityAnimatorController controller)
        {
            AnimationClip idleClip = LoadClip(
                ProjectAssetPaths.Animations.FirearmIdle,
                false);
            AnimationClip moveClip = LoadClip(
                ProjectAssetPaths.Animations.FirearmMove,
                false);
            if (idleClip == null || moveClip == null)
            {
                Debug.LogWarning(
                    "Firearm locomotion clips are missing; the firearm layer "
                    + "cannot be built.");
                return false;
            }

            bool changed = false;
            int layerIndex = FindLayerIndex(
                controller,
                FirearmLocomotionLayerName);
            if (layerIndex < 0)
            {
                controller.AddLayer(FirearmLocomotionLayerName);
                layerIndex = controller.layers.Length - 1;
                changed = true;
            }

            AnimatorControllerLayer[] layers = controller.layers;
            AnimatorControllerLayer layer = layers[layerIndex];
            AnimatorStateMachine machine = layer.stateMachine;
            AnimatorState idle = FindState(machine, FirearmIdleStateName);
            AnimatorState move = FindState(machine, FirearmMoveStateName);
            bool locomotionLayerIsCurrent = idle != null
                && move != null
                && machine.states.Length == 2
                && idle.motion == idleClip
                && move.motion == moveClip
                && idle.iKOnFeet
                && move.iKOnFeet
                && HasBooleanTransition(idle, move, "walkFlag", true)
                && HasBooleanTransition(move, idle, "walkFlag", false)
                && machine.defaultState == idle
                && layer.avatarMask == null
                && layer.blendingMode == AnimatorLayerBlendingMode.Override
                && Mathf.Approximately(layer.defaultWeight, 0f);
            if (!locomotionLayerIsCurrent)
            {
                ClearStateMachine(machine);
                idle = machine.AddState(
                    FirearmIdleStateName,
                    new Vector3(260f, 120f));
                idle.motion = idleClip;
                idle.iKOnFeet = true;
                move = machine.AddState(
                    FirearmMoveStateName,
                    new Vector3(520f, 120f));
                move.motion = moveClip;
                move.iKOnFeet = true;
                machine.defaultState = idle;

                AddConditionTransition(idle, move, "walkFlag", true, 0.12f);
                AddConditionTransition(move, idle, "walkFlag", false, 0.12f);

                layer.avatarMask = null;
                layer.blendingMode = AnimatorLayerBlendingMode.Override;
                layer.defaultWeight = 0f;
                layers[layerIndex] = layer;
                controller.layers = layers;
                changed = true;
            }

            AvatarMask armsMask = EnsureHumanoidToolMask(
                CrouchToolArmsMaskPath,
                "CrouchToolArms",
                false,
                false);
            changed |= EnsureFirearmArmsLayer(controller, idleClip, armsMask);
            changed |= EnsureCustomLayerOrder(controller);
            return changed;
        }

        private static bool EnsureFirearmArmsLayer(
            UnityAnimatorController controller,
            AnimationClip idleClip,
            AvatarMask armsMask)
        {
            int layerIndex = FindLayerIndex(controller, FirearmArmsLayerName);
            if (layerIndex < 0)
            {
                controller.AddLayer(FirearmArmsLayerName);
                layerIndex = controller.layers.Length - 1;
            }

            AnimatorControllerLayer[] layers = controller.layers;
            AnimatorControllerLayer layer = layers[layerIndex];
            AnimatorStateMachine machine = layer.stateMachine;
            AnimatorState arms = FindState(machine, FirearmArmsStateName);
            if (arms != null
                && arms.motion == idleClip
                && machine.states.Length == 1
                && arms.transitions.Length == 0
                && machine.defaultState == arms
                && layer.avatarMask == armsMask
                && layer.blendingMode == AnimatorLayerBlendingMode.Override
                && Mathf.Approximately(layer.defaultWeight, 0f))
            {
                return false;
            }

            ClearStateMachine(machine);
            arms = machine.AddState(
                FirearmArmsStateName,
                new Vector3(260f, 120f));
            arms.motion = idleClip;
            arms.iKOnFeet = false;
            machine.defaultState = arms;

            layer.avatarMask = armsMask;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layer.defaultWeight = 0f;
            layers[layerIndex] = layer;
            controller.layers = layers;
            return true;
        }

        // Crouch is a complete grounded pose on Base Layer. Keeping hips, legs and feet
        // in one animation prevents a standing torso from lifting the crouched legs.
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

            int lowerBodyLayerIndex = FindLayerIndex(controller, LowerBodyLayerName);
            if (lowerBodyLayerIndex >= 0)
            {
                controller.RemoveLayer(lowerBodyLayerIndex);
                changed = true;
            }

            AnimatorStateMachine baseMachine = controller.layers[0].stateMachine;
            AnimatorState crouchIdle = FindState(baseMachine, CrouchIdleStateName);
            AnimatorState crouchMove = FindState(baseMachine, CrouchMoveStateName);
            AnimationClip crouchIdleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CrouchIdleClipPath);
            AnimationClip crouchMoveClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CrouchMoveClipPath);
            if (crouchIdleClip == null || crouchMoveClip == null)
            {
                throw new InvalidOperationException(
                    "Missing crouch clips required to build full-body Base Layer states.");
            }
            if (crouchIdle != null && crouchMove != null
                && crouchIdle.motion == crouchIdleClip
                && crouchMove.motion == crouchMoveClip
                && crouchIdle.iKOnFeet
                && crouchMove.iKOnFeet)
            {
                return changed;
            }

            if (crouchIdle != null)
            {
                RemoveAnyStateTransitionsTo(baseMachine, crouchIdle);
                baseMachine.RemoveState(crouchIdle);
            }
            if (crouchMove != null)
            {
                RemoveAnyStateTransitionsTo(baseMachine, crouchMove);
                baseMachine.RemoveState(crouchMove);
            }

            AnimatorState idle = FindState(baseMachine, "Idle");
            AnimatorState walk = FindState(baseMachine, "Walk");
            AnimatorState jump = FindState(baseMachine, "Jump");
            if (idle == null || walk == null || jump == null)
                throw new InvalidOperationException("Base locomotion states are incomplete.");

            crouchIdle = baseMachine.AddState(CrouchIdleStateName, new Vector3(720f, 420f));
            crouchIdle.motion = crouchIdleClip;
            crouchIdle.iKOnFeet = true;
            crouchMove = baseMachine.AddState(CrouchMoveStateName, new Vector3(960f, 420f));
            crouchMove.motion = crouchMoveClip;
            crouchMove.iKOnFeet = true;

            AnimatorStateTransition idleEntry = baseMachine.AddAnyStateTransition(crouchIdle);
            ConfigureImmediateTransition(idleEntry);
            idleEntry.AddCondition(AnimatorConditionMode.If, 0f, "crouchFlag");
            idleEntry.AddCondition(AnimatorConditionMode.IfNot, 0f, "crouchMoveFlag");
            AnimatorStateTransition moveEntry = baseMachine.AddAnyStateTransition(crouchMove);
            ConfigureImmediateTransition(moveEntry);
            moveEntry.AddCondition(AnimatorConditionMode.If, 0f, "crouchFlag");
            moveEntry.AddCondition(AnimatorConditionMode.If, 0f, "crouchMoveFlag");

            AnimatorStateTransition toMove = crouchIdle.AddTransition(crouchMove);
            ConfigureImmediateTransition(toMove);
            toMove.AddCondition(AnimatorConditionMode.If, 0f, "crouchMoveFlag");

            AnimatorStateTransition toIdle = crouchMove.AddTransition(crouchIdle);
            ConfigureImmediateTransition(toIdle);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "crouchMoveFlag");

            AddCrouchExit(crouchIdle, idle, "idleFlag");
            AddCrouchExit(crouchIdle, walk, "walkFlag");
            AddCrouchExit(crouchMove, idle, "idleFlag");
            AddCrouchExit(crouchMove, walk, "walkFlag");
            AddCrouchExit(crouchIdle, jump, "jumpFlag");
            AddCrouchExit(crouchMove, jump, "jumpFlag");
            return true;
        }

        private static void AddCrouchExit(
            AnimatorState source,
            AnimatorState destination,
            string destinationFlag)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            ConfigureImmediateTransition(transition);
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, "crouchFlag");
            transition.AddCondition(AnimatorConditionMode.If, 0f, destinationFlag);
        }

        // Crouch keeps its grounded full-body pose on Base Layer, then holds the standing
        // idle arm pose on an arms-only layer. Only tool actions may animate crouched arms.
        private static bool ConfigureToolUpperBodyLayer(UnityAnimatorController controller)
        {
            bool changed = EnsureParameter(
                controller,
                "ToolAction",
                AnimatorControllerParameterType.Trigger);
            changed |= EnsureParameter(
                controller,
                "ToolActionContinuous",
                AnimatorControllerParameterType.Bool);

            AvatarMask upperBodyMask = EnsureHumanoidToolMask(
                ToolUpperBodyMaskPath,
                "ToolUpperBody",
                true,
                true);
            AvatarMask crouchArmsMask = EnsureHumanoidToolMask(
                CrouchToolArmsMaskPath,
                "CrouchToolArms",
                false,
                false);
            AnimationClip placeholder = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                ToolPrimaryActionClipPath);
            if (placeholder == null)
            {
                throw new InvalidOperationException(
                    "Missing tool primary action placeholder required to build the upper-body layer.");
            }

            AnimatorStateMachine baseMachine = controller.layers[0].stateMachine;
            AnimatorState legacyPrimary = FindState(baseMachine, ToolPrimaryStateName);
            AnimatorState legacyContinuous = FindState(baseMachine, ToolContinuousStateName);
            if (legacyPrimary != null)
            {
                RemoveAnyStateTransitionsTo(baseMachine, legacyPrimary);
                baseMachine.RemoveState(legacyPrimary);
                changed = true;
            }
            if (legacyContinuous != null)
            {
                RemoveAnyStateTransitionsTo(baseMachine, legacyContinuous);
                baseMachine.RemoveState(legacyContinuous);
                changed = true;
            }

            changed |= EnsureToolActionLayer(
                controller,
                ToolUpperBodyLayerName,
                upperBodyMask,
                placeholder);
            changed |= EnsureCrouchArmsLocomotionLayer(controller, crouchArmsMask);
            changed |= EnsureToolActionLayer(
                controller,
                CrouchToolArmsLayerName,
                crouchArmsMask,
                placeholder);
            changed |= EnsureCustomLayerOrder(controller);
            return changed;
        }

        private static bool EnsureCrouchArmsLocomotionLayer(
            UnityAnimatorController controller,
            AvatarMask armsMask)
        {
            AnimatorStateMachine baseMachine = controller.layers[0].stateMachine;
            AnimatorState baseIdle = FindState(baseMachine, "Idle");
            if (baseIdle == null)
                throw new InvalidOperationException("Base idle state is required for crouch arms.");

            int layerIndex = FindLayerIndex(controller, CrouchArmsLocomotionLayerName);
            if (layerIndex < 0)
            {
                controller.AddLayer(CrouchArmsLocomotionLayerName);
                layerIndex = controller.layers.Length - 1;
            }

            AnimatorControllerLayer[] layers = controller.layers;
            AnimatorControllerLayer layer = layers[layerIndex];
            AnimatorStateMachine machine = layer.stateMachine;
            AnimatorState armsIdle = FindState(machine, CrouchArmsIdleStateName);
            if (armsIdle != null
                && armsIdle.motion == baseIdle.motion
                && machine.states.Length == 1
                && armsIdle.transitions.Length == 0
                && machine.defaultState == armsIdle
                && layer.avatarMask == armsMask
                && layer.blendingMode == AnimatorLayerBlendingMode.Override
                && Mathf.Approximately(layer.defaultWeight, 0f))
            {
                return false;
            }

            ClearStateMachine(machine);
            armsIdle = machine.AddState(CrouchArmsIdleStateName, new Vector3(260f, 120f));
            armsIdle.motion = baseIdle.motion;
            armsIdle.iKOnFeet = false;
            machine.defaultState = armsIdle;

            layer.avatarMask = armsMask;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layer.defaultWeight = 0f;
            layers[layerIndex] = layer;
            controller.layers = layers;
            return true;
        }

        private static bool EnsureCustomLayerOrder(UnityAnimatorController controller)
        {
            string[] desiredOrder =
            {
                FirearmLocomotionLayerName,
                CrouchArmsLocomotionLayerName,
                FirearmArmsLayerName,
                ToolUpperBodyLayerName,
                CrouchToolArmsLayerName,
            };
            List<AnimatorControllerLayer> layers = controller.layers.ToList();
            List<AnimatorControllerLayer> customLayers = new List<AnimatorControllerLayer>();
            foreach (string layerName in desiredOrder)
            {
                int index = layers.FindIndex(layer => layer.name == layerName);
                if (index < 0) continue;
                customLayers.Add(layers[index]);
                layers.RemoveAt(index);
            }
            layers.AddRange(customLayers);

            AnimatorControllerLayer[] current = controller.layers;
            bool changed = current.Length != layers.Count;
            for (int i = 0; !changed && i < current.Length; i++)
                changed = current[i].name != layers[i].name;
            if (changed) controller.layers = layers.ToArray();
            return changed;
        }

        private static bool EnsureToolActionLayer(
            UnityAnimatorController controller,
            string layerName,
            AvatarMask mask,
            AnimationClip placeholder)
        {
            int layerIndex = FindLayerIndex(controller, layerName);
            if (layerIndex < 0)
            {
                controller.AddLayer(layerName);
                layerIndex = controller.layers.Length - 1;
            }

            AnimatorControllerLayer[] layers = controller.layers;
            AnimatorControllerLayer layer = layers[layerIndex];
            AnimatorStateMachine machine = layer.stateMachine;
            AnimatorState inactive = FindState(machine, ToolInactiveStateName);
            AnimatorState primary = FindState(machine, ToolPrimaryStateName);
            AnimatorState continuous = FindState(machine, ToolContinuousStateName);
            if (inactive != null && primary != null && continuous != null
                && layer.avatarMask == mask
                && layer.blendingMode == AnimatorLayerBlendingMode.Override
                && Mathf.Approximately(layer.defaultWeight, 0f))
            {
                return false;
            }

            ClearStateMachine(machine);
            inactive = machine.AddState(ToolInactiveStateName, new Vector3(260f, 180f));
            inactive.motion = null;
            inactive.writeDefaultValues = false;
            primary = machine.AddState(ToolPrimaryStateName, new Vector3(520f, 80f));
            primary.motion = placeholder;
            primary.speed = 1.5f;
            continuous = machine.AddState(ToolContinuousStateName, new Vector3(520f, 280f));
            continuous.motion = placeholder;
            continuous.speed = 1.5f;
            machine.defaultState = inactive;

            AnimatorStateTransition primaryEntry = machine.AddAnyStateTransition(primary);
            ConfigureImmediateTransition(primaryEntry);
            primaryEntry.AddCondition(AnimatorConditionMode.If, 0f, "ToolAction");
            AnimatorStateTransition primaryExit = primary.AddTransition(inactive);
            primaryExit.hasExitTime = true;
            primaryExit.exitTime = 0.7f;
            primaryExit.hasFixedDuration = false;
            primaryExit.duration = 0.2f;

            AnimatorStateTransition continuousEntry = machine.AddAnyStateTransition(continuous);
            ConfigureImmediateTransition(continuousEntry);
            continuousEntry.AddCondition(
                AnimatorConditionMode.If,
                0f,
                "ToolActionContinuous");
            AnimatorStateTransition continuousExit = continuous.AddTransition(inactive);
            ConfigureImmediateTransition(continuousExit);
            continuousExit.AddCondition(
                AnimatorConditionMode.IfNot,
                0f,
                "ToolActionContinuous");

            layer.avatarMask = mask;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layer.defaultWeight = 0f;
            layers[layerIndex] = layer;
            controller.layers = layers;
            return true;
        }

        private static AvatarMask EnsureHumanoidToolMask(
            string assetPath,
            string assetName,
            bool includeBody,
            bool includeHead)
        {
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(assetPath);
            bool created = mask == null;
            if (created) mask = new AvatarMask { name = assetName };
            bool changed = false;
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                AvatarMaskBodyPart part = (AvatarMaskBodyPart)i;
                bool active = part == AvatarMaskBodyPart.LeftArm
                    || part == AvatarMaskBodyPart.RightArm
                    || part == AvatarMaskBodyPart.LeftFingers
                    || part == AvatarMaskBodyPart.RightFingers
                    || part == AvatarMaskBodyPart.LeftHandIK
                    || part == AvatarMaskBodyPart.RightHandIK
                    || (includeBody && part == AvatarMaskBodyPart.Body)
                    || (includeHead && part == AvatarMaskBodyPart.Head);
                if (mask.GetHumanoidBodyPartActive(part) == active) continue;
                mask.SetHumanoidBodyPartActive(part, active);
                changed = true;
            }

            if (created) AssetDatabase.CreateAsset(mask, assetPath);
            else if (changed) EditorUtility.SetDirty(mask);
            return mask;
        }

        private static int FindLayerIndex(
            UnityAnimatorController controller,
            string layerName)
        {
            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].name == layerName) return i;
            return -1;
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

        private static bool HasBooleanTransition(
            AnimatorState source,
            AnimatorState destination,
            string parameter,
            bool value)
        {
            AnimatorConditionMode expectedMode = value
                ? AnimatorConditionMode.If
                : AnimatorConditionMode.IfNot;
            foreach (AnimatorStateTransition transition in source.transitions)
            {
                AnimatorCondition[] conditions = transition.conditions;
                if (transition.destinationState == destination
                    && conditions.Length == 1
                    && conditions[0].parameter == parameter
                    && conditions[0].mode == expectedMode)
                {
                    return true;
                }
            }
            return false;
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
