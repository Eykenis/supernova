using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.MinecraftCaves.Creatures;
using Supernova.Voxels;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityAnimatorController = UnityEditor.Animations.AnimatorController;

namespace Supernova.Tests
{
    public sealed class CharacterCombatStateMachineTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        private enum TestStateId
        {
            Idle,
            Attack,
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        [Test]
        public void StateMachine_ExitsOldStateBeforeEnteringNewState()
        {
            var events = new List<string>();
            var machine = new CharacterStateMachine<TestStateId>();
            machine.Add(new RecordingState(TestStateId.Idle, events));
            machine.Add(new RecordingState(TestStateId.Attack, events));

            machine.Start(TestStateId.Idle);
            machine.Tick(0.25f);
            machine.Change(TestStateId.Attack);

            CollectionAssert.AreEqual(
                new[] { "enter Idle", "tick Idle", "exit Idle", "enter Attack" },
                events);
            Assert.That(machine.Current, Is.EqualTo(TestStateId.Attack));
        }

        [Test]
        public void CharacterVitals_ClampsDamageAndReportsDeath()
        {
            var vitals = new CharacterVitals();
            vitals.Initialize(40f, true);

            Assert.That(vitals.ApplyDamage(15f), Is.True);
            Assert.That(vitals.CurrentHealth, Is.EqualTo(25f));
            Assert.That(vitals.ApplyDamage(100f), Is.True);
            Assert.That(vitals.CurrentHealth, Is.Zero);
            Assert.That(vitals.IsAlive, Is.False);
            Assert.That(vitals.ApplyDamage(1f), Is.False);
        }

        [Test]
        public void PlayerInventory_UsesTenSlotsWithThreeConfiguredTools()
        {
            var inventory = new PlayerInventory();

            Assert.That(PlayerInventory.SlotCount, Is.EqualTo(10));
            Assert.That(inventory.GetItemAtSlot(0), Is.EqualTo(PlayerInventoryItem.Pickaxe));
            Assert.That(inventory.GetItemAtSlot(1), Is.EqualTo(PlayerInventoryItem.Magnet));
            Assert.That(inventory.GetItemAtSlot(2), Is.EqualTo(PlayerInventoryItem.Flashlight));
            for (int i = 3; i < PlayerInventory.SlotCount; i++)
                Assert.That(inventory.GetItemAtSlot(i), Is.EqualTo(PlayerInventoryItem.Empty));

            Assert.That(inventory.SelectSlot(9), Is.True);
            Assert.That(inventory.SelectedSlotIndex, Is.EqualTo(9));
            Assert.That(inventory.SelectedItem, Is.EqualTo(PlayerInventoryItem.Empty));
        }

        [Test]
        public void FlashlightTool_UsesThirdSlotAndPersistentLightPrefab()
        {
            PlayerToolDefinition flashlight =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    "Assets/Game/Config/Tools/FlashlightTool.asset");
            GameObject playerPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/Player.prefab");

            Assert.That(flashlight, Is.Not.Null);
            Assert.That(flashlight.Item, Is.EqualTo(PlayerInventoryItem.Flashlight));
            Assert.That(
                flashlight.PrimaryAction,
                Is.EqualTo(PlayerToolPrimaryAction.ThrowPersistentLight));
            Assert.That(flashlight.HeldModelPrefab, Is.Not.Null);
            Assert.That(flashlight.ProjectilePrefab, Is.Not.Null);
            Assert.That(flashlight.ThrowSpeed, Is.GreaterThan(0f));
            Assert.That(
                playerPrefab.GetComponent<PlayerToolController>()
                    .GetDefinition(PlayerInventoryItem.Flashlight),
                Is.SameAs(flashlight));

            PersistentLightProjectile projectile = flashlight.ProjectilePrefab;
            Assert.That(projectile.GetComponent<Rigidbody>(), Is.Not.Null);
            Assert.That(projectile.GetComponent<Collider>(), Is.Not.Null);
            Assert.That(projectile.LightSource, Is.Not.Null);
            Assert.That(projectile.LightSource.type, Is.EqualTo(LightType.Point));
            Assert.That(projectile.LightSource.intensity, Is.EqualTo(0.55f));
            Assert.That(projectile.LightSource.range, Is.EqualTo(14f));
            Assert.That(projectile.LightSource.shadows, Is.EqualTo(LightShadows.None));
            Assert.That(projectile.GetComponent<TimedBomb>(), Is.Null);
        }

        [Test]
        public void PersistentLightProjectile_LaunchSetsVelocityWithoutArmingLifetime()
        {
            GameObject projectilePrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/Tools/FlashlightProjectile.prefab");
            GameObject instance = Object.Instantiate(projectilePrefab);
            objects.Add(instance);
            PersistentLightProjectile projectile =
                instance.GetComponent<PersistentLightProjectile>();
            Vector3 velocity = new Vector3(1f, 2f, 3f);
            Vector3 angularVelocity = new Vector3(4f, 5f, 6f);

            projectile.Launch(velocity, angularVelocity);

            Assert.That(projectile.Body.velocity, Is.EqualTo(velocity));
            Assert.That(
                projectile.Body.angularVelocity,
                Is.EqualTo(angularVelocity));
            Assert.That(projectile.LightSource.enabled, Is.True);
            Assert.That(instance.GetComponent<TimedBomb>(), Is.Null);
        }

        [Test]
        public void PlayerStateMachine_DeclaresSingleConfiguredToolActionState()
        {
            Assert.That(System.Enum.IsDefined(
                typeof(PlayerCharacterState), PlayerCharacterState.ToolAction), Is.True);
            Assert.That(System.Enum.IsDefined(
                typeof(PlayerCharacterState), "MagnetAttract"), Is.False);
        }

        [Test]
        public void MagnetActionState_StartsAndStopsAttractorThroughStateLifecycle()
        {
            GameObject playerObject = Create("Player");
            playerObject.AddComponent<CharacterController>();

            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(playerObject.transform);
            Camera camera = cameraObject.AddComponent<Camera>();
            PerspectiveCameraController perspective =
                playerObject.AddComponent<PerspectiveCameraController>();
            perspective.Bind(playerObject.transform, null, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.FirstPerson, true);

            FirstPersonCartAttractor attractor =
                playerObject.AddComponent<FirstPersonCartAttractor>();
            PlayerToolController inventory = playerObject.AddComponent<PlayerToolController>();
            PlayerToolDefinition magnet = ScriptableObject.CreateInstance<PlayerToolDefinition>();
            SetPrivateField(magnet, "item", PlayerInventoryItem.Magnet);
            SetPrivateField(magnet, "primaryAction", PlayerToolPrimaryAction.AttractCart);
            SetPrivateField(inventory, "toolDefinitions", new[] { magnet });
            inventory.SelectSlot(1);
            VoxelPlayerController player = playerObject.AddComponent<VoxelPlayerController>();

            MethodInfo awake = typeof(VoxelPlayerController).GetMethod(
                "Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(awake, Is.Not.Null);
            awake.Invoke(player, null);

            FieldInfo field = typeof(VoxelPlayerController).GetField(
                "stateMachine", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var machine = (CharacterStateMachine<PlayerCharacterState>)field.GetValue(player);
            Assert.That(machine, Is.Not.Null);

            machine.Change(PlayerCharacterState.ToolAction);
            Assert.That(player.CurrentState, Is.EqualTo(PlayerCharacterState.ToolAction));
            Assert.That(attractor.IsActionActive, Is.True);

            machine.Change(PlayerCharacterState.Idle);
            Assert.That(attractor.IsActionActive, Is.False);
            Object.DestroyImmediate(magnet);
        }

        [Test]
        public void ToolDefinitions_SelectGameplayActionAndAnimationPerInventoryItem()
        {
            GameObject playerObject = Create("Player");
            PlayerToolController inventory = playerObject.AddComponent<PlayerToolController>();
            PlayerToolDefinition pickaxe = ScriptableObject.CreateInstance<PlayerToolDefinition>();
            PlayerToolDefinition magnet = ScriptableObject.CreateInstance<PlayerToolDefinition>();
            AnimationClip pickaxeClip = new AnimationClip();
            AnimationClip magnetClip = new AnimationClip();

            SetPrivateField(pickaxe, "item", PlayerInventoryItem.Pickaxe);
            SetPrivateField(pickaxe, "primaryAction", PlayerToolPrimaryAction.MineVoxel);
            SetPrivateField(
                pickaxe,
                "animationTriggerMode",
                PlayerToolAnimationTriggerMode.Periodic);
            SetPrivateField(pickaxe, "primaryActionAnimation", pickaxeClip);
            SetPrivateField(magnet, "item", PlayerInventoryItem.Magnet);
            SetPrivateField(magnet, "primaryAction", PlayerToolPrimaryAction.AttractCart);
            SetPrivateField(
                magnet,
                "animationTriggerMode",
                PlayerToolAnimationTriggerMode.Single);
            SetPrivateField(magnet, "primaryActionAnimation", magnetClip);
            SetPrivateField(inventory, "toolDefinitions", new[] { pickaxe, magnet });

            inventory.SelectSlot(0);
            Assert.That(inventory.SelectedDefinition, Is.SameAs(pickaxe));
            Assert.That(
                inventory.SelectedDefinition.AnimationTriggerMode,
                Is.EqualTo(PlayerToolAnimationTriggerMode.Periodic));
            Assert.That(inventory.SelectedDefinition.PrimaryActionAnimation, Is.SameAs(pickaxeClip));
            inventory.SelectSlot(1);
            Assert.That(inventory.SelectedDefinition, Is.SameAs(magnet));
            Assert.That(
                inventory.SelectedDefinition.AnimationTriggerMode,
                Is.EqualTo(PlayerToolAnimationTriggerMode.Single));
            Assert.That(inventory.SelectedDefinition.PrimaryActionAnimation, Is.SameAs(magnetClip));

            Object.DestroyImmediate(pickaxeClip);
            Object.DestroyImmediate(magnetClip);
            Object.DestroyImmediate(pickaxe);
            Object.DestroyImmediate(magnet);
        }

        [Test]
        public void ToolAssets_ConfigurePickaxeAsPeriodicAndMagnetAsSingle()
        {
            PlayerToolDefinition pickaxe = AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                "Assets/Game/Config/Tools/PickaxeTool.asset");
            PlayerToolDefinition magnet = AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                "Assets/Game/Config/Tools/MagnetTool.asset");

            Assert.That(pickaxe, Is.Not.Null);
            Assert.That(magnet, Is.Not.Null);
            Assert.That(
                pickaxe.AnimationTriggerMode,
                Is.EqualTo(PlayerToolAnimationTriggerMode.Periodic));
            Assert.That(
                magnet.AnimationTriggerMode,
                Is.EqualTo(PlayerToolAnimationTriggerMode.Single));
        }

        [Test]
        public void ToolAssets_ConfigurePickaxeModelAndLeaveMagnetModelEmpty()
        {
            PlayerToolDefinition pickaxe =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    "Assets/Game/Config/Tools/PickaxeTool.asset");
            PlayerToolDefinition magnet =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    "Assets/Game/Config/Tools/MagnetTool.asset");

            Assert.That(pickaxe, Is.Not.Null);
            Assert.That(pickaxe.HeldModelPrefab, Is.Not.Null);
            Assert.That(pickaxe.HeldModelPrefab.name, Is.EqualTo("pickaxe01"));
            Assert.That(magnet, Is.Not.Null);
            Assert.That(magnet.HeldModelPrefab, Is.Null);
        }

        [Test]
        public void InventorySelection_ReplacesModelAtToolMountAndSupportsNullModel()
        {
            GameObject playerObject = Create("Player");
            PlayerToolController inventory =
                playerObject.AddComponent<PlayerToolController>();
            GameObject mountObject = Create("Tool Model Mount");
            mountObject.transform.SetParent(playerObject.transform);
            GameObject pickaxeModel = Create("Pickaxe Model Prefab");
            PlayerToolDefinition pickaxe =
                ScriptableObject.CreateInstance<PlayerToolDefinition>();
            PlayerToolDefinition magnet =
                ScriptableObject.CreateInstance<PlayerToolDefinition>();
            try
            {
                SetPrivateField(
                    pickaxe,
                    "item",
                    PlayerInventoryItem.Pickaxe);
                SetPrivateField(pickaxe, "heldModelPrefab", pickaxeModel);
                SetPrivateField(
                    magnet,
                    "item",
                    PlayerInventoryItem.Magnet);
                SetPrivateField(
                    inventory,
                    "toolDefinitions",
                    new[] { pickaxe, magnet });
                SetPrivateField(
                    inventory,
                    "toolModelMount",
                    mountObject.transform);

                inventory.SelectSlot(0);
                Assert.That(inventory.EquippedToolModel, Is.Not.Null);
                Assert.That(
                    inventory.EquippedToolModel.transform.parent,
                    Is.SameAs(mountObject.transform));

                inventory.SelectSlot(1);
                Assert.That(inventory.EquippedToolModel, Is.Null);
                Assert.That(mountObject.transform.childCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(pickaxe);
                Object.DestroyImmediate(magnet);
            }
        }

        [Test]
        public void InventorySelection_EnablesMagnetOnlyForSlotTwo()
        {
            GameObject playerObject = Create("Player");
            FirstPersonCartAttractor attractor =
                playerObject.AddComponent<FirstPersonCartAttractor>();
            PlayerToolController inventory = playerObject.AddComponent<PlayerToolController>();

            inventory.SelectSlot(1);
            Assert.That(attractor.DeviceEnabled, Is.True);

            inventory.SelectSlot(2);
            Assert.That(attractor.DeviceEnabled, Is.False);
            Assert.That(
                inventory.SelectedItem,
                Is.EqualTo(PlayerInventoryItem.Flashlight));
        }

        [Test]
        public void PlayerDamage_TransitionsThroughHurtToDead()
        {
            GameObject playerObject = Create("Player");
            playerObject.AddComponent<CharacterController>();
            VoxelPlayerController player = playerObject.AddComponent<VoxelPlayerController>();
            var hit = new DamageInfo(10f, null, Vector3.zero, Vector3.forward);

            Assert.That(player.ReceiveDamage(hit), Is.True);
            Assert.That(player.CurrentState, Is.EqualTo(PlayerCharacterState.Hurt));

            var lethal = new DamageInfo(1000f, null, Vector3.zero, Vector3.forward);
            Assert.That(player.ReceiveDamage(lethal), Is.True);
            Assert.That(player.CurrentState, Is.EqualTo(PlayerCharacterState.Dead));
            Assert.That(player.IsAlive, Is.False);
        }

        [Test]
        public void ThirdPersonFacing_InterpolatesInsteadOfSnappingToMovementDirection()
        {
            GameObject playerObject = Create("Player");
            playerObject.AddComponent<CharacterController>();
            playerObject.AddComponent<PlayerProfile>();

            GameObject head = new GameObject("Head");
            head.transform.SetParent(playerObject.transform);
            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(playerObject.transform);
            Camera camera = cameraObject.AddComponent<Camera>();

            PerspectiveCameraController perspective =
                playerObject.AddComponent<PerspectiveCameraController>();
            perspective.Bind(playerObject.transform, head.transform, camera, new Renderer[0]);
            perspective.SetMode(PlayerViewMode.ThirdPerson, true);
            VoxelPlayerController player = playerObject.AddComponent<VoxelPlayerController>();
            SetPrivateField(player, "perspectiveCamera", perspective);

            MethodInfo updateFacing = typeof(VoxelPlayerController).GetMethod(
                "UpdateThirdPersonFacing", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(updateFacing, Is.Not.Null);
            updateFacing.Invoke(player, new object[] { Vector3.right, 1f / 60f });

            float yaw = playerObject.transform.eulerAngles.y;
            Assert.That(yaw, Is.GreaterThan(0f));
            Assert.That(yaw, Is.LessThan(90f),
                "Third-person facing should approach the target instead of snapping in one frame.");
        }

        [TestCase(false, false, PlayerCharacterState.Idle)]
        [TestCase(false, true, PlayerCharacterState.Move)]
        [TestCase(true, false, PlayerCharacterState.CrouchIdle)]
        [TestCase(true, true, PlayerCharacterState.CrouchMove)]
        public void GroundedLocomotion_ResolvesCrouchStates(
            bool crouching,
            bool moving,
            PlayerCharacterState expected)
        {
            MethodInfo resolve = typeof(VoxelPlayerController).GetMethod(
                "ResolveGroundedLocomotionState",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(resolve, Is.Not.Null);

            PlayerCharacterState result = (PlayerCharacterState)resolve.Invoke(
                null,
                new object[] { crouching, moving });

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void PlayerProfile_ProvidesIndependentCrouchMovementConfiguration()
        {
            GameObject playerObject = Create("Player");
            PlayerProfile profile = playerObject.AddComponent<PlayerProfile>();

            Assert.That(profile.CrouchKey, Is.EqualTo(KeyCode.LeftControl));
            Assert.That(profile.CrouchMoveSpeed, Is.EqualTo(2f));
            Assert.That(profile.CrouchMoveSpeed, Is.LessThan(profile.MoveSpeed));
        }

        [Test]
        public void PlayerAnimator_CrouchUsesMaskedLowerBodyLayerSoUpperBodyStaysFree()
        {
            UnityAnimatorController controller = AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(
                "Assets/Game/Animations/P05Player.controller");
            Assert.That(controller, Is.Not.Null);

            Assert.That(
                controller.parameters,
                Has.Some.Matches<AnimatorControllerParameter>(parameter =>
                    parameter.name == "crouchFlag"
                    && parameter.type == AnimatorControllerParameterType.Bool));
            Assert.That(
                controller.parameters,
                Has.Some.Matches<AnimatorControllerParameter>(parameter =>
                    parameter.name == "crouchMoveFlag"
                    && parameter.type == AnimatorControllerParameterType.Bool));

            AnimatorControllerLayer lowerBodyLayer = null;
            foreach (AnimatorControllerLayer layer in controller.layers)
                if (layer.name == "LowerBody Layer") lowerBodyLayer = layer;
            Assert.That(lowerBodyLayer, Is.Not.Null,
                "Crouch must live on its own masked layer so it never overrides the upper body.");
            Assert.That(lowerBodyLayer.avatarMask, Is.Not.Null);
            Assert.That(lowerBodyLayer.blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));
            Assert.That(lowerBodyLayer.defaultWeight, Is.Zero,
                "Layer must start at weight 0; VoxelPlayerController drives it at runtime.");

            AnimatorState crouchIdle = FindAnimatorState(lowerBodyLayer.stateMachine, "CrouchIdle");
            AnimatorState crouchMove = FindAnimatorState(lowerBodyLayer.stateMachine, "CrouchMove");
            Assert.That(crouchIdle, Is.Not.Null);
            Assert.That(crouchMove, Is.Not.Null);
            Assert.That(crouchIdle.motion, Is.Not.Null);
            Assert.That(crouchMove.motion, Is.Not.Null);

            AnimatorStateMachine baseLayerMachine = controller.layers[0].stateMachine;
            Assert.That(FindAnimatorState(baseLayerMachine, "Crouch Idle"), Is.Null,
                "Crouch must no longer live on the unmasked Base Layer.");
            Assert.That(FindAnimatorState(baseLayerMachine, "Crouch Move"), Is.Null,
                "Crouch must no longer live on the unmasked Base Layer.");
        }

        [Test]
        public void PlayerAnimator_ToolActionUsesGenericStateAndConfiguredClipPlaceholder()
        {
            UnityAnimatorController controller = AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(
                "Assets/Game/Animations/P05Player.controller");
            Assert.That(controller, Is.Not.Null);
            Assert.That(
                controller.parameters,
                Has.Some.Matches<AnimatorControllerParameter>(parameter =>
                    parameter.name == "ToolAction"
                    && parameter.type == AnimatorControllerParameterType.Trigger));
            Assert.That(
                controller.parameters,
                Has.Some.Matches<AnimatorControllerParameter>(parameter =>
                    parameter.name == "ToolActionContinuous"
                    && parameter.type == AnimatorControllerParameterType.Bool));
            Assert.That(
                controller.parameters,
                Has.None.Matches<AnimatorControllerParameter>(parameter =>
                    parameter.name == "Mine"));

            AnimatorStateMachine baseLayerMachine = controller.layers[0].stateMachine;
            AnimatorState toolAction = FindAnimatorState(
                baseLayerMachine,
                "Tool Primary Action");
            Assert.That(toolAction, Is.Not.Null);
            Assert.That(toolAction.motion, Is.Not.Null);
            Assert.That(toolAction.motion.name, Is.EqualTo("ToolPrimaryActionPlaceholder"));
            Assert.That(toolAction.transitions, Has.Length.EqualTo(1));
            Assert.That(toolAction.transitions[0].hasExitTime, Is.True);
            Assert.That(toolAction.transitions[0].exitTime, Is.EqualTo(0.7f));
            Assert.That(toolAction.transitions[0].duration, Is.EqualTo(0.2f));
            Assert.That(toolAction.transitions[0].conditions, Is.Empty);

            AnimatorState continuousAction = FindAnimatorState(
                baseLayerMachine,
                "Tool Continuous Action");
            Assert.That(continuousAction, Is.Not.Null);
            Assert.That(continuousAction.motion, Is.Not.Null);
            Assert.That(
                continuousAction.motion.name,
                Is.EqualTo("ToolPrimaryActionPlaceholder"));
            Assert.That(continuousAction.transitions, Has.Length.EqualTo(1));
            Assert.That(continuousAction.transitions[0].hasExitTime, Is.False);
            Assert.That(continuousAction.transitions[0].conditions, Has.Length.EqualTo(1));
            Assert.That(
                continuousAction.transitions[0].conditions[0].parameter,
                Is.EqualTo("ToolActionContinuous"));
            Assert.That(
                continuousAction.transitions[0].conditions[0].mode,
                Is.EqualTo(AnimatorConditionMode.IfNot));

            bool hasContinuousEntry = false;
            foreach (AnimatorStateTransition transition in baseLayerMachine.anyStateTransitions)
            {
                if (transition.destinationState != continuousAction) continue;
                hasContinuousEntry = true;
                Assert.That(transition.canTransitionToSelf, Is.False);
            }
            Assert.That(hasContinuousEntry, Is.True);
            Assert.That(FindAnimatorState(baseLayerMachine, "Mine"), Is.Null);
        }

        [Test]
        public void PeriodicToolCycle_WaitsForConfiguredAnimatorStateToFinish()
        {
            RuntimeAnimatorController runtimeController =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    "Assets/Game/Animations/P05Player.controller");
            Assert.That(runtimeController, Is.Not.Null);

            GameObject playerObject = Create("Player");
            playerObject.AddComponent<CharacterController>();
            VoxelPlayerController player =
                playerObject.AddComponent<VoxelPlayerController>();
            GameObject visual = Create("Visual");
            visual.transform.SetParent(playerObject.transform);
            Animator animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = runtimeController;
            player.SetAnimator(animator);
            animator.Update(0f);

            MethodInfo cycleComplete = typeof(VoxelPlayerController).GetMethod(
                "IsPeriodicToolActionCycleComplete",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(cycleComplete, Is.Not.Null);
            SetPrivateField(player, "nextAttackTime", Time.time + 10f);

            animator.SetTrigger("ToolAction");
            animator.Update(0.05f);
            Assert.That(cycleComplete.Invoke(player, null), Is.False);

            bool completed = false;
            for (int i = 0; i < 40 && !completed; i++)
            {
                animator.Update(0.05f);
                completed = (bool)cycleComplete.Invoke(player, null);
            }

            Assert.That(
                completed,
                Is.True,
                "The next mining cycle should unlock when the tool animation exits.");
        }

        [Test]
        public void CreatureAttack_DamagesPlayerThroughSharedContract()
        {
            GameObject playerObject = Create("Player");
            playerObject.transform.position = Vector3.forward;
            playerObject.AddComponent<CharacterController>();
            VoxelPlayerController player = playerObject.AddComponent<VoxelPlayerController>();

            GameObject creatureObject = Create("Creature");
            creatureObject.AddComponent<CapsuleCollider>();
            CreatureBehaviorAgent creature = creatureObject.AddComponent<CreatureBehaviorAgent>();
            SetPrivateField(creature, "playerFoot", playerObject.transform);

            MethodInfo applyAttack = typeof(CreatureBehaviorAgent).GetMethod(
                "ApplyAttack", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(applyAttack, Is.Not.Null);
            applyAttack.Invoke(creature, new object[] { Vector3.forward });

            Assert.That(player.CurrentHealth, Is.LessThan(player.MaximumHealth));
            Assert.That(player.CurrentState, Is.EqualTo(PlayerCharacterState.Hurt));
        }

        [Test]
        public void PlayerAttack_DamagesCreatureThroughSharedContract()
        {
            GameObject playerObject = Create("Player");
            playerObject.AddComponent<CharacterController>();
            VoxelPlayerController player = playerObject.AddComponent<VoxelPlayerController>();

            GameObject creatureObject = Create("Creature");
            creatureObject.transform.position = Vector3.forward;
            creatureObject.AddComponent<CapsuleCollider>();
            CreatureBehaviorAgent creature = creatureObject.AddComponent<CreatureBehaviorAgent>();
            Physics.SyncTransforms();

            MethodInfo performAttack = typeof(VoxelPlayerController).GetMethod(
                "PerformAttack", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(performAttack, Is.Not.Null);
            performAttack.Invoke(player, null);

            Assert.That(creature.CurrentHealth, Is.LessThan(creature.MaximumHealth));
            Assert.That(creature.CurrentState, Is.EqualTo(CreatureBehaviorState.Hurt));
        }

        [Test]
        public void CreatureDamage_TransitionsThroughHurtToDead()
        {
            GameObject creatureObject = Create("Creature");
            creatureObject.AddComponent<CapsuleCollider>();
            CreatureBehaviorAgent creature = creatureObject.AddComponent<CreatureBehaviorAgent>();

            var hit = new DamageInfo(5f, null, Vector3.zero, Vector3.forward);
            Assert.That(creature.ReceiveDamage(hit), Is.True);
            Assert.That(creature.CurrentState, Is.EqualTo(CreatureBehaviorState.Hurt));

            var lethal = new DamageInfo(1000f, null, Vector3.zero, Vector3.forward);
            Assert.That(creature.ReceiveDamage(lethal), Is.True);
            Assert.That(creature.CurrentState, Is.EqualTo(CreatureBehaviorState.Dead));
            Assert.That(creature.IsAlive, Is.False);
        }

        private GameObject Create(string name)
        {
            var gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject;
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static AnimatorState FindAnimatorState(
            AnimatorStateMachine machine,
            string name)
        {
            foreach (ChildAnimatorState child in machine.states)
            {
                if (child.state.name == name) return child.state;
            }
            return null;
        }

        private sealed class RecordingState : ICharacterState<TestStateId>
        {
            private readonly List<string> events;

            public RecordingState(TestStateId id, List<string> events)
            {
                Id = id;
                this.events = events;
            }

            public TestStateId Id { get; }
            public void Enter() => events.Add($"enter {Id}");
            public void Tick(float deltaTime) => events.Add($"tick {Id}");
            public void Exit() => events.Add($"exit {Id}");
        }
    }
}
