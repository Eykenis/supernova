using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Supernova.Audio;
using Supernova.Gameplay;
using Supernova.Infrastructure;
using Supernova.MinecraftCaves.Creatures;
using Supernova.Shop;
using Supernova.UI;
using Supernova.Voxels;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Type = System.Type;
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
        public void PlayerInventory_LocksPickaxeToTheFirstOfFiveSlots()
        {
            var inventory = new PlayerInventory();

            Assert.That(PlayerInventory.SlotCount, Is.EqualTo(5));
            Assert.That(
                inventory.GetItemAtSlot(PlayerInventory.FixedPickaxeSlotIndex),
                Is.EqualTo(PlayerInventoryItem.Pickaxe));
            Assert.That(
                PlayerInventory.GetDefaultItemAtSlot(
                    PlayerInventory.FixedPickaxeSlotIndex),
                Is.EqualTo(PlayerInventoryItem.Pickaxe));
            for (int i = 1; i < PlayerInventory.SlotCount; i++)
                Assert.That(inventory.GetItemAtSlot(i), Is.EqualTo(PlayerInventoryItem.Empty));

            Assert.That(
                inventory.SetItemAtSlot(3, PlayerInventoryItem.Pickaxe),
                Is.False);
            Assert.That(
                inventory.SetItemAtSlot(0, PlayerInventoryItem.Empty),
                Is.False);
            Assert.That(
                inventory.SetItemAtSlot(0, PlayerInventoryItem.Bomb),
                Is.False);
            Assert.That(
                inventory.GetItemAtSlot(0),
                Is.EqualTo(PlayerInventoryItem.Pickaxe));
        }

        [Test]
        public void ToolActionTiming_ExposesIndependentDelayAndPeriod()
        {
            PlayerToolDefinition definition =
                ScriptableObject.CreateInstance<PlayerToolDefinition>();
            try
            {
                SetPrivateField(definition, "actionTriggerDelay", 0.4f);
                SetPrivateField(definition, "actionCyclePeriod", 0.1f);
                SetPrivateField(definition, "actionIsPeriodic", true);

                Assert.That(definition.ActionTriggerDelay, Is.EqualTo(0.4f));
                Assert.That(definition.ActionCyclePeriod, Is.EqualTo(0.1f));
                Assert.That(definition.ActionIsPeriodic, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void FlashlightTool_UsesThirdSlotAndPersistentLightPrefab()
        {
            PlayerToolDefinition flashlight =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.FlashlightTool);
            GameObject playerPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    ProjectAssetPaths.Prefabs.Player);

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
        }

        [Test]
        public void PersistentLightProjectile_LaunchSetsVelocityWithoutArmingLifetime()
        {
            GameObject projectilePrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    ProjectAssetPaths.Prefabs.FlashlightProjectile);
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
        public void ToolDefinitions_SelectGameplayActionAndAnimationPerInventoryItem()
        {
            GameObject playerObject = Create("Player");
            PlayerToolController inventory = playerObject.AddComponent<PlayerToolController>();
            PlayerToolDefinition pickaxe = ScriptableObject.CreateInstance<PlayerToolDefinition>();
            PlayerToolDefinition bomb = ScriptableObject.CreateInstance<PlayerToolDefinition>();
            AnimationClip pickaxeClip = new AnimationClip();
            AnimationClip bombClip = new AnimationClip();

            SetPrivateField(pickaxe, "item", PlayerInventoryItem.Pickaxe);
            SetPrivateField(pickaxe, "primaryAction", PlayerToolPrimaryAction.MineVoxel);
            SetPrivateField(
                pickaxe,
                "animationTriggerMode",
                PlayerToolAnimationTriggerMode.Periodic);
            SetPrivateField(pickaxe, "primaryActionAnimation", pickaxeClip);
            SetPrivateField(bomb, "item", PlayerInventoryItem.Bomb);
            SetPrivateField(bomb, "primaryAction", PlayerToolPrimaryAction.ThrowBomb);
            SetPrivateField(
                bomb,
                "animationTriggerMode",
                PlayerToolAnimationTriggerMode.Single);
            SetPrivateField(bomb, "primaryActionAnimation", bombClip);
            SetPrivateField(inventory, "toolDefinitions", new[] { pickaxe, bomb });
            Assert.That(
                inventory.ConfigureSlot(0, PlayerInventoryItem.Pickaxe),
                Is.False);
            Assert.That(
                inventory.ConfigureSlot(1, PlayerInventoryItem.Bomb),
                Is.True);

            inventory.SelectSlot(0);
            Assert.That(inventory.SelectedDefinition, Is.SameAs(pickaxe));
            Assert.That(
                inventory.SelectedDefinition.AnimationTriggerMode,
                Is.EqualTo(PlayerToolAnimationTriggerMode.Periodic));
            Assert.That(inventory.SelectedDefinition.PrimaryActionAnimation, Is.SameAs(pickaxeClip));
            inventory.SelectSlot(1);
            Assert.That(inventory.SelectedDefinition, Is.SameAs(bomb));
            Assert.That(
                inventory.SelectedDefinition.AnimationTriggerMode,
                Is.EqualTo(PlayerToolAnimationTriggerMode.Single));
            Assert.That(inventory.SelectedDefinition.PrimaryActionAnimation, Is.SameAs(bombClip));

            Object.DestroyImmediate(pickaxeClip);
            Object.DestroyImmediate(bombClip);
            Object.DestroyImmediate(pickaxe);
            Object.DestroyImmediate(bomb);
        }

        [Test]
        public void ToolAssets_ConfigurePickaxeCadenceAndThrowProjectile()
        {
            PlayerToolDefinition pickaxe = AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                ProjectAssetPaths.Config.PickaxeTool);

            Assert.That(pickaxe, Is.Not.Null);
            Assert.That(
                pickaxe.AnimationTriggerMode,
                Is.EqualTo(PlayerToolAnimationTriggerMode.Periodic));
            Assert.That(pickaxe.ActionTriggerDelay, Is.EqualTo(0.42f));
            Assert.That(pickaxe.ActionCyclePeriod, Is.EqualTo(0.75f));
            Assert.That(pickaxe.ActionIsPeriodic, Is.True);
            Assert.That(pickaxe.CanThrowPickaxe, Is.True);
        }

        [Test]
        public void PickaxeTool_ConfiguresMonsterHitPunchCue()
        {
            PlayerToolDefinition pickaxe =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.PickaxeTool);

            Assert.That(pickaxe, Is.Not.Null);
            Assert.That(pickaxe.MonsterHitSound, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(pickaxe.MonsterHitSound),
                Is.EqualTo(ProjectAssetPaths.Config.PickaxeMonsterHitSound));

            SerializedObject serializedCue =
                new SerializedObject(pickaxe.MonsterHitSound);
            SerializedProperty clips = serializedCue.FindProperty("clips");
            Assert.That(clips.arraySize, Is.EqualTo(3));
            Assert.That(
                AssetDatabase.GetAssetPath(
                    clips.GetArrayElementAtIndex(0).objectReferenceValue),
                Is.EqualTo(ProjectAssetPaths.Audio.Punch1));
            Assert.That(
                AssetDatabase.GetAssetPath(
                    clips.GetArrayElementAtIndex(1).objectReferenceValue),
                Is.EqualTo(ProjectAssetPaths.Audio.Punch2));
            Assert.That(
                AssetDatabase.GetAssetPath(
                    clips.GetArrayElementAtIndex(2).objectReferenceValue),
                Is.EqualTo(ProjectAssetPaths.Audio.Punch3));
        }

        [Test]
        public void PickaxeTool_ConfiguresMiningWooshAndThrownImpactCues()
        {
            PlayerToolDefinition pickaxe =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.PickaxeTool);

            Assert.That(pickaxe, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(pickaxe.PrimaryActionSound),
                Is.EqualTo(ProjectAssetPaths.Config.PickaxeWooshSound));
            Assert.That(
                AssetDatabase.GetAssetPath(pickaxe.MiningHitSound),
                Is.EqualTo(ProjectAssetPaths.Config.PickaxeMiningHitSound));
            Assert.That(
                AssetDatabase.GetAssetPath(pickaxe.ThrownTerrainHitSound),
                Is.EqualTo(
                    ProjectAssetPaths.Config.PickaxeThrownImpactSound));

            SerializedProperty mineClips = new SerializedObject(
                    pickaxe.MiningHitSound)
                .FindProperty("clips");
            string[] expectedMineClips =
            {
                ProjectAssetPaths.Audio.Mine1,
                ProjectAssetPaths.Audio.Mine2,
                ProjectAssetPaths.Audio.Mine3,
                ProjectAssetPaths.Audio.Mine4,
                ProjectAssetPaths.Audio.Mine5,
                ProjectAssetPaths.Audio.Mine6,
                ProjectAssetPaths.Audio.Mine7,
                ProjectAssetPaths.Audio.Mine8,
                ProjectAssetPaths.Audio.Mine9,
                ProjectAssetPaths.Audio.Mine10,
            };
            Assert.That(mineClips.arraySize, Is.EqualTo(10));
            for (int i = 0; i < expectedMineClips.Length; i++)
            {
                Assert.That(
                    AssetDatabase.GetAssetPath(
                        mineClips.GetArrayElementAtIndex(i)
                            .objectReferenceValue),
                    Is.EqualTo(expectedMineClips[i]));
            }

            SerializedObject serializedWoosh =
                new SerializedObject(pickaxe.PrimaryActionSound);
            SerializedProperty wooshClips =
                serializedWoosh.FindProperty("clips");
            Assert.That(wooshClips.arraySize, Is.EqualTo(1));
            Assert.That(
                AssetDatabase.GetAssetPath(
                    wooshClips.GetArrayElementAtIndex(0)
                        .objectReferenceValue),
                Is.EqualTo(ProjectAssetPaths.Audio.Woosh));
            Assert.That(
                serializedWoosh.FindProperty("volume").floatValue,
                Is.EqualTo(0.6f).Within(0.0001f));

            SerializedProperty thrownClips = new SerializedObject(
                    pickaxe.ThrownTerrainHitSound)
                .FindProperty("clips");
            Assert.That(thrownClips.arraySize, Is.EqualTo(1));
            Assert.That(
                AssetDatabase.GetAssetPath(
                    thrownClips.GetArrayElementAtIndex(0)
                        .objectReferenceValue),
                Is.EqualTo(ProjectAssetPaths.Audio.PickaxeThrown));
        }

        [Test]
        public void PickaxeAndFlashlight_ConfigureSharedThrowCue()
        {
            PlayerToolDefinition pickaxe =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.PickaxeTool);
            PlayerToolDefinition flashlight =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.FlashlightTool);

            Assert.That(pickaxe, Is.Not.Null);
            Assert.That(flashlight, Is.Not.Null);
            Assert.That(pickaxe.ThrowSound, Is.Not.Null);
            Assert.That(flashlight.ThrowSound, Is.SameAs(pickaxe.ThrowSound));
            Assert.That(
                AssetDatabase.GetAssetPath(pickaxe.ThrowSound),
                Is.EqualTo(ProjectAssetPaths.Config.ToolThrowSound));

            SerializedProperty clips = new SerializedObject(
                    pickaxe.ThrowSound)
                .FindProperty("clips");
            Assert.That(clips.arraySize, Is.EqualTo(1));
            Assert.That(
                AssetDatabase.GetAssetPath(
                    clips.GetArrayElementAtIndex(0)
                        .objectReferenceValue),
                Is.EqualTo(ProjectAssetPaths.Audio.Throw));
        }

        [Test]
        public void ToolDefinition_NoLongerDeclaresAPerToolSecondaryAction()
        {
            // Right click is unconditionally the magnet and the throw has its own
            // key, so no tool overrides the secondary action any more.
            Assert.That(
                typeof(PlayerToolDefinition).GetProperty("SecondaryAction"),
                Is.Null);
            Assert.That(
                typeof(PlayerToolDefinition).GetField(
                    "secondaryAction",
                    BindingFlags.Instance | BindingFlags.NonPublic),
                Is.Null);
        }

        [Test]
        public void ToolAssets_ConfigurePickaxeModel()
        {
            PlayerToolDefinition pickaxe =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.PickaxeTool);

            Assert.That(pickaxe, Is.Not.Null);
            Assert.That(pickaxe.HeldModelPrefab, Is.Not.Null);
            Assert.That(pickaxe.HeldModelPrefab.name, Is.EqualTo("pickaxe01"));
            Assert.That(
                pickaxe.HeldModelMountStrategy,
                Is.EqualTo(HeldToolMountStrategy.SingleHand));
        }

        [Test]
        public void SuspendItem_RestoresPickaxeToItsFixedFirstSlot()
        {
            GameObject playerObject = Create("Player");
            PlayerToolController inventory =
                playerObject.AddComponent<PlayerToolController>();
            PlayerToolDefinition pickaxe =
                ScriptableObject.CreateInstance<PlayerToolDefinition>();
            try
            {
                SetPrivateField(pickaxe, "item", PlayerInventoryItem.Pickaxe);
                SetPrivateField(
                    inventory,
                    "toolDefinitions",
                    new[] { pickaxe });
                Assert.That(
                    inventory.ConfigureSlot(2, PlayerInventoryItem.Pickaxe),
                    Is.False);
                inventory.SelectSlot(0);

                Assert.That(
                    inventory.SuspendItem(PlayerInventoryItem.Pickaxe),
                    Is.True);
                Assert.That(
                    inventory.IsItemSuspended(PlayerInventoryItem.Pickaxe),
                    Is.True);
                Assert.That(
                    inventory.GetItemAtSlot(0),
                    Is.EqualTo(PlayerInventoryItem.Empty));
                Assert.That(
                    inventory.SelectedItem,
                    Is.EqualTo(PlayerInventoryItem.Empty));
                // A suspended item cannot be dragged back in from the menu.
                Assert.That(
                    inventory.ConfigureSlot(0, PlayerInventoryItem.Pickaxe),
                    Is.False);
                // Suspending twice is refused, so a second throw cannot duplicate it.
                Assert.That(
                    inventory.SuspendItem(PlayerInventoryItem.Pickaxe),
                    Is.False);

                Assert.That(
                    inventory.RestoreSuspendedItem(PlayerInventoryItem.Pickaxe),
                    Is.True);
                Assert.That(
                    inventory.IsItemSuspended(PlayerInventoryItem.Pickaxe),
                    Is.False);
                Assert.That(
                    inventory.GetItemAtSlot(0),
                    Is.EqualTo(PlayerInventoryItem.Pickaxe));
                Assert.That(
                    inventory.SelectedItem,
                    Is.EqualTo(PlayerInventoryItem.Pickaxe));
                Assert.That(
                    inventory.RestoreSuspendedItem(PlayerInventoryItem.Pickaxe),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(pickaxe);
            }
        }

        [Test]
        public void PlayerDamage_TransitionsThroughHurtToDead()
        {
            GameObject playerObject = Create("Player");
            playerObject.transform.position = new Vector3(10000f, 10000f, 10000f);
            playerObject.AddComponent<CharacterController>();
            VoxelPlayerController player = playerObject.AddComponent<VoxelPlayerController>();
            SetPrivateField(player, "view", playerObject.transform);
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

            // The crouch key now lives in the Gameplay/Crouch binding, so the
            // profile only owns the movement and collider tuning.
            Assert.That(profile.CrouchMoveSpeed, Is.EqualTo(2f));
            Assert.That(profile.CrouchMoveSpeed, Is.LessThan(profile.MoveSpeed));
            Assert.That(profile.CrouchColliderHeight, Is.EqualTo(1f));
        }

        [Test]
        public void PlayerCrouchCollider_ShortensWithoutMovingItsFeet()
        {
            GameObject playerObject = Create("Player");
            CharacterController character =
                playerObject.AddComponent<CharacterController>();
            character.height = 1.6f;
            character.radius = 0.3f;
            character.center = new Vector3(0f, 0.8f, 0f);
            VoxelPlayerController player =
                playerObject.AddComponent<VoxelPlayerController>();

            InvokeCrouchColliderUpdate(player, true);

            Assert.That(character.height, Is.EqualTo(1f).Within(0.001f));
            Assert.That(character.center.y, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(player.IsCrouching, Is.True);
        }

        [Test]
        public void PlayerCrouchCollider_StaysLowUntilStandingSpaceIsClear()
        {
            GameObject playerObject = Create("Player");
            CharacterController character =
                playerObject.AddComponent<CharacterController>();
            character.height = 1.6f;
            character.radius = 0.3f;
            character.center = new Vector3(0f, 0.8f, 0f);
            VoxelPlayerController player =
                playerObject.AddComponent<VoxelPlayerController>();
            InvokeCrouchColliderUpdate(player, true);

            GameObject ceiling = Create("Ceiling");
            BoxCollider ceilingCollider = ceiling.AddComponent<BoxCollider>();
            ceilingCollider.size = new Vector3(2f, 0.2f, 2f);
            ceiling.transform.position = new Vector3(0f, 1.2f, 0f);
            Physics.SyncTransforms();

            InvokeCrouchColliderUpdate(player, false);
            Assert.That(character.height, Is.EqualTo(1f).Within(0.001f));
            Assert.That(player.IsCrouching, Is.True);

            ceiling.transform.position = new Vector3(0f, 3f, 0f);
            Physics.SyncTransforms();
            InvokeCrouchColliderUpdate(player, false);

            Assert.That(character.height, Is.EqualTo(1.6f).Within(0.001f));
            Assert.That(character.center.y, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(player.IsCrouching, Is.False);
        }

        [Test]
        public void PlayerAnimator_CrouchUsesFullBodyBaseStatesWithFootIk()
        {
            UnityAnimatorController controller = AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(
                ProjectAssetPaths.Animations.PlayerController);
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
            Assert.That(lowerBodyLayer, Is.Null,
                "A lower-body-only crouch separates the hips from the torso and can lift the feet.");

            AnimatorStateMachine baseLayerMachine = controller.layers[0].stateMachine;
            AnimatorState crouchIdle = FindAnimatorState(baseLayerMachine, "Crouch Idle");
            AnimatorState crouchMove = FindAnimatorState(baseLayerMachine, "Crouch Move");
            Assert.That(crouchIdle, Is.Not.Null);
            Assert.That(crouchMove, Is.Not.Null);
            Assert.That(crouchIdle.motion, Is.Not.Null);
            Assert.That(crouchMove.motion, Is.Not.Null);
            Assert.That(crouchIdle.iKOnFeet, Is.True);
            Assert.That(crouchMove.iKOnFeet, Is.True);

            AnimatorControllerLayer crouchArmsLayer = null;
            foreach (AnimatorControllerLayer layer in controller.layers)
                if (layer.name == "Crouch Arms Locomotion Layer") crouchArmsLayer = layer;
            Assert.That(crouchArmsLayer, Is.Not.Null);
            Assert.That(crouchArmsLayer.defaultWeight, Is.Zero);
            Assert.That(crouchArmsLayer.blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));
            Assert.That(crouchArmsLayer.avatarMask, Is.Not.Null);
            Assert.That(
                crouchArmsLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body),
                Is.False,
                "The full-body crouch keeps ownership of the torso and grounded feet.");
            Assert.That(
                crouchArmsLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm),
                Is.True);
            Assert.That(
                crouchArmsLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm),
                Is.True);
            Assert.That(
                crouchArmsLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg),
                Is.False);

            AnimatorState standingIdle = FindAnimatorState(baseLayerMachine, "Idle");
            AnimatorState crouchArmsIdle = FindAnimatorState(
                crouchArmsLayer.stateMachine,
                "Standing Arms Idle");
            Assert.That(crouchArmsIdle, Is.Not.Null);
            Assert.That(crouchArmsIdle.motion, Is.SameAs(standingIdle.motion));
            Assert.That(crouchArmsIdle.transitions, Is.Empty,
                "Crouch walking must not switch or animate the arms.");
            Assert.That(
                FindAnimatorState(crouchArmsLayer.stateMachine, "Standing Arms Walk"),
                Is.Null);
        }

        [Test]
        public void PlayerAnimator_ToolActionUsesGenericStateAndConfiguredClipPlaceholder()
        {
            UnityAnimatorController controller = AssetDatabase.LoadAssetAtPath<UnityAnimatorController>(
                ProjectAssetPaths.Animations.PlayerController);
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
                Has.Some.Matches<AnimatorControllerParameter>(parameter =>
                    parameter.name == "ToolActionSpeed"
                    && parameter.type == AnimatorControllerParameterType.Float
                    && Mathf.Approximately(parameter.defaultFloat, 1f)));
            Assert.That(
                controller.parameters,
                Has.None.Matches<AnimatorControllerParameter>(parameter =>
                    parameter.name == "Mine"));

            AnimatorControllerLayer toolLayer = null;
            foreach (AnimatorControllerLayer layer in controller.layers)
                if (layer.name == "Tool UpperBody Layer") toolLayer = layer;
            Assert.That(toolLayer, Is.Not.Null);
            Assert.That(toolLayer.avatarMask, Is.Not.Null);
            Assert.That(toolLayer.blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));
            Assert.That(toolLayer.defaultWeight, Is.Zero,
                "The runtime raises this layer only while a tool action is active.");
            Assert.That(
                toolLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body),
                Is.True);
            Assert.That(
                toolLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm),
                Is.True);
            Assert.That(
                toolLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm),
                Is.True);
            Assert.That(
                toolLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg),
                Is.False);
            Assert.That(
                toolLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg),
                Is.False);

            AnimatorControllerLayer crouchToolLayer = null;
            foreach (AnimatorControllerLayer layer in controller.layers)
                if (layer.name == "Crouch Tool Arms Layer") crouchToolLayer = layer;
            Assert.That(crouchToolLayer, Is.Not.Null);
            Assert.That(crouchToolLayer.avatarMask, Is.Not.Null);
            Assert.That(crouchToolLayer.defaultWeight, Is.Zero);
            Assert.That(
                crouchToolLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body),
                Is.False,
                "Crouched tools must preserve the full-body crouch torso.");
            Assert.That(
                crouchToolLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Head),
                Is.False);
            Assert.That(
                crouchToolLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm),
                Is.True);
            Assert.That(
                crouchToolLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm),
                Is.True);
            Assert.That(
                crouchToolLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg),
                Is.False);
            Assert.That(
                FindAnimatorState(crouchToolLayer.stateMachine, "Tool Primary Action"),
                Is.Not.Null);
            Assert.That(
                FindAnimatorState(crouchToolLayer.stateMachine, "Tool Continuous Action"),
                Is.Not.Null);
            AnimatorState crouchContinuousAction = FindAnimatorState(
                crouchToolLayer.stateMachine,
                "Tool Continuous Action");
            Assert.That(crouchContinuousAction.speedParameterActive, Is.True);
            Assert.That(
                crouchContinuousAction.speedParameter,
                Is.EqualTo("ToolActionSpeed"));

            AnimatorStateMachine toolMachine = toolLayer.stateMachine;
            AnimatorState toolAction = FindAnimatorState(
                toolMachine,
                "Tool Primary Action");
            Assert.That(toolAction, Is.Not.Null);
            Assert.That(toolAction.motion, Is.Not.Null);
            Assert.That(toolAction.motion.name, Is.EqualTo("ToolPrimaryActionPlaceholder"));
            Assert.That(toolAction.transitions, Has.Length.EqualTo(1));
            Assert.That(toolAction.transitions[0].hasExitTime, Is.True);
            Assert.That(toolAction.transitions[0].exitTime, Is.EqualTo(0.7f));
            Assert.That(toolAction.transitions[0].duration, Is.EqualTo(0.2f));
            Assert.That(toolAction.transitions[0].conditions, Is.Empty);
            AssertPeriodicToolActionCanRestart(toolMachine, toolAction);

            AnimatorState crouchToolAction = FindAnimatorState(
                crouchToolLayer.stateMachine,
                "Tool Primary Action");
            AssertPeriodicToolActionCanRestart(
                crouchToolLayer.stateMachine,
                crouchToolAction);

            AnimatorState continuousAction = FindAnimatorState(
                toolMachine,
                "Tool Continuous Action");
            Assert.That(continuousAction, Is.Not.Null);
            Assert.That(continuousAction.motion, Is.Not.Null);
            Assert.That(continuousAction.speedParameterActive, Is.True);
            Assert.That(
                continuousAction.speedParameter,
                Is.EqualTo("ToolActionSpeed"));
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
            foreach (AnimatorStateTransition transition in toolMachine.anyStateTransitions)
            {
                if (transition.destinationState != continuousAction) continue;
                hasContinuousEntry = true;
                Assert.That(transition.canTransitionToSelf, Is.False);
            }
            Assert.That(hasContinuousEntry, Is.True);
            AnimatorStateMachine baseLayerMachine = controller.layers[0].stateMachine;
            Assert.That(FindAnimatorState(baseLayerMachine, "Tool Primary Action"), Is.Null);
            Assert.That(FindAnimatorState(baseLayerMachine, "Tool Continuous Action"), Is.Null);
            Assert.That(FindAnimatorState(baseLayerMachine, "Mine"), Is.Null);
        }

        private static void AssertPeriodicToolActionCanRestart(
            AnimatorStateMachine stateMachine,
            AnimatorState actionState)
        {
            bool hasPeriodicEntry = false;
            foreach (AnimatorStateTransition transition
                in stateMachine.anyStateTransitions)
            {
                if (transition.destinationState != actionState) continue;
                hasPeriodicEntry = true;
                Assert.That(
                    transition.canTransitionToSelf,
                    Is.True,
                    "Periodic mining triggers must restart the current swing.");
            }

            Assert.That(hasPeriodicEntry, Is.True);
        }

        [Test]
        public void ToolCycleTimers_AreTrackedPerDefinition()
        {
            GameObject playerObject = Create("Player");
            playerObject.AddComponent<CharacterController>();
            VoxelPlayerController player =
                playerObject.AddComponent<VoxelPlayerController>();
            PlayerToolDefinition coolingDown =
                ScriptableObject.CreateInstance<PlayerToolDefinition>();
            PlayerToolDefinition ready =
                ScriptableObject.CreateInstance<PlayerToolDefinition>();
            MethodInfo cycleReady = typeof(VoxelPlayerController).GetMethod(
                "IsToolActionCycleReady",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo cycleTimesField = typeof(VoxelPlayerController).GetField(
                "nextToolActionCycleTimes",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(cycleReady, Is.Not.Null);
            Assert.That(cycleTimesField, Is.Not.Null);
            try
            {
                var cycleTimes =
                    (Dictionary<PlayerToolDefinition, float>)
                    cycleTimesField.GetValue(player);
                cycleTimes[coolingDown] = Time.time + 10f;
                cycleTimes[ready] = Time.time - 1f;

                Assert.That(
                    cycleReady.Invoke(player, new object[] { coolingDown }),
                    Is.False);
                Assert.That(
                    cycleReady.Invoke(player, new object[] { ready }),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(coolingDown);
                Object.DestroyImmediate(ready);
            }
        }

        [TestCase(PlayerInventoryItem.SolidGun)]
        [TestCase(PlayerInventoryItem.PortalGun)]
        public void FirearmCooldown_ReportsRemainingCycleForHotbar(
            PlayerInventoryItem item)
        {
            GameObject playerObject = Create("Player");
            playerObject.AddComponent<CharacterController>();
            PlayerToolController tools =
                playerObject.AddComponent<PlayerToolController>();
            VoxelPlayerController player =
                playerObject.AddComponent<VoxelPlayerController>();
            PlayerToolDefinition definition =
                ScriptableObject.CreateInstance<PlayerToolDefinition>();
            try
            {
                SetPrivateField(definition, "item", item);
                SetPrivateField(
                    definition,
                    "primaryAction",
                    PlayerToolPrimaryAction.FireProjectile);
                SetPrivateField(definition, "actionCyclePeriod", 0.8f);
                SetPrivateField(
                    tools,
                    "toolDefinitions",
                    new[] { definition });

                FieldInfo cycleTimesField = typeof(VoxelPlayerController)
                    .GetField(
                        "nextToolActionCycleTimes",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(cycleTimesField, Is.Not.Null);
                var cycleTimes =
                    (Dictionary<PlayerToolDefinition, float>)
                    cycleTimesField.GetValue(player);
                cycleTimes[definition] = Time.time + 0.5f;

                Assert.That(
                    player.TryGetToolActionCooldown(
                        item,
                        out float remainingSeconds,
                        out float durationSeconds),
                    Is.True);
                Assert.That(remainingSeconds, Is.EqualTo(0.5f).Within(0.02f));
                Assert.That(durationSeconds, Is.EqualTo(0.8f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void ToolUpperBodyLayer_ReturnsWeightToZeroAfterActionFinishes()
        {
            RuntimeAnimatorController runtimeController =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    ProjectAssetPaths.Animations.PlayerController);
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

            int layerIndex = animator.GetLayerIndex("Tool UpperBody Layer");
            Assert.That(layerIndex, Is.GreaterThanOrEqualTo(0));
            MethodInfo trigger = typeof(VoxelPlayerController).GetMethod(
                "TriggerToolActionAnimation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo tickLayer = typeof(VoxelPlayerController).GetMethod(
                "TickToolUpperBodyLayerBlend",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(trigger, Is.Not.Null);
            Assert.That(tickLayer, Is.Not.Null);

            trigger.Invoke(player, null);
            animator.Update(0.05f);
            tickLayer.Invoke(player, new object[] { 0.05f });
            Assert.That(animator.GetLayerWeight(layerIndex), Is.GreaterThan(0f));

            for (int i = 0; i < 50; i++)
            {
                animator.Update(0.05f);
                tickLayer.Invoke(player, new object[] { 0.05f });
            }

            Assert.That(animator.GetLayerWeight(layerIndex), Is.Zero.Within(0.001f),
                "Inactive tool layers must release their last pose back to locomotion.");
        }

        [Test]
        public void CreatureAttack_DamagesPlayerThroughSharedContract()
        {
            GameAssetCatalog catalog =
                AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(
                    ProjectAssetPaths.Config.GameAssetCatalog);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Audio.CreatureHitPlayer, Is.Not.Null);

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
            int soundCount = 0;
            SoundEffectPlaybackRequest received = default;
            System.Action<SoundEffectPlaybackRequest> observer = request =>
            {
                soundCount++;
                received = request;
            };
            SoundEffectEvents.PlaybackRequested += observer;
            try
            {
                applyAttack.Invoke(creature, new object[] { Vector3.forward });
            }
            finally
            {
                SoundEffectEvents.PlaybackRequested -= observer;
            }

            Assert.That(player.CurrentHealth, Is.LessThan(player.MaximumHealth));
            Assert.That(player.CurrentState, Is.EqualTo(PlayerCharacterState.Hurt));
            Assert.That(soundCount, Is.EqualTo(1));
            Assert.That(
                received.Cue,
                Is.SameAs(catalog.Audio.CreatureHitPlayer));
            Assert.That(received.Position, Is.EqualTo(playerObject.transform.position));
        }

        [Test]
        public void CreatureAttackSwing_BroadcastsWoosh()
        {
            GameAssetCatalog catalog =
                AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(
                    ProjectAssetPaths.Config.GameAssetCatalog);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Audio.CreatureAttack, Is.Not.Null);

            GameObject creatureObject = Create("Creature Swing");
            creatureObject.AddComponent<CapsuleCollider>();
            CreatureBehaviorAgent creature =
                creatureObject.AddComponent<CreatureBehaviorAgent>();
            MethodInfo enterAttack = typeof(CreatureBehaviorAgent).GetMethod(
                "EnterAttack",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(enterAttack, Is.Not.Null);

            int soundCount = 0;
            SoundEffectPlaybackRequest received = default;
            System.Action<SoundEffectPlaybackRequest> observer = request =>
            {
                soundCount++;
                received = request;
            };
            SoundEffectEvents.PlaybackRequested += observer;
            try
            {
                enterAttack.Invoke(creature, null);
            }
            finally
            {
                SoundEffectEvents.PlaybackRequested -= observer;
            }

            Assert.That(soundCount, Is.EqualTo(1));
            Assert.That(received.Cue, Is.SameAs(catalog.Audio.CreatureAttack));
            Assert.That(received.Position, Is.EqualTo(creatureObject.transform.position));
        }


        [Test]
        public void CreatureCollisionDamage_UsesSharedMassNormalizedImpulseRule()
        {
            GameObject creatureObject = Create("Creature");
            Rigidbody body = creatureObject.AddComponent<Rigidbody>();
            body.mass = 1f;
            creatureObject.AddComponent<CapsuleCollider>();
            CreatureBehaviorAgent creature =
                creatureObject.AddComponent<CreatureBehaviorAgent>();

            const float impulse = 5f;
            float damage = creature.ApplyCollisionImpulse(impulse);

            float expectedDamage = CollisionImpulseDamage.CalculateDamage(
                creature.MaximumHealth,
                impulse,
                creature.CollisionFragility,
                creature.MinimumDamageImpulse,
                creature.DamagePercentagePerSquaredImpulse,
                body.mass);
            Assert.That(
                creature.MinimumDamageImpulse,
                Is.GreaterThan(
                    CollisionImpulseDamage.DefaultMinimumDamageImpulse));
            Assert.That(damage, Is.EqualTo(expectedDamage).Within(0.0001f));
            Assert.That(
                creature.CurrentHealth,
                Is.EqualTo(creature.MaximumHealth - expectedDamage)
                    .Within(0.0001f));

            creature.RestoreFullHealth();
            body.mass = 10f;
            Assert.That(creature.ApplyCollisionImpulse(8f), Is.Zero);
        }

        [Test]
        public void CreatureHealthBar_ShowsForDamageAndCrosshairAim()
        {
            GameObject creatureObject = Create("Creature");
            creatureObject.transform.position =
                new Vector3(10000f, 10000f, 10000f);
            creatureObject.AddComponent<CapsuleCollider>();
            CreatureBehaviorAgent creature =
                creatureObject.AddComponent<CreatureBehaviorAgent>();
            MonsterHealthBar healthBar =
                creatureObject.GetComponent<MonsterHealthBar>();

            Assert.That(healthBar, Is.Not.Null);
            Assert.That(
                healthBar.WorldCanvas.renderMode,
                Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(healthBar.IsVisible, Is.False);

            creature.ReceiveDamage(new DamageInfo(
                15f,
                null,
                creatureObject.transform.position,
                Vector3.forward));
            Assert.That(healthBar.IsVisible, Is.True);
            Assert.That(
                healthBar.FillImage.rectTransform.anchorMax.x,
                Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(healthBar.FillImage.color.r, Is.EqualTo(1f));
            Assert.That(healthBar.FillImage.color.g, Is.LessThan(0.2f));
            Assert.That(
                healthBar.WorldCanvas.transform.position.y,
                Is.GreaterThan(creatureObject.transform.position.y));

            creature.RestoreFullHealth();
            healthBar.enabled = false;
            healthBar.enabled = true;
            SetPrivateField(
                healthBar,
                "visibleUntil",
                float.NegativeInfinity);
            Assert.That(healthBar.IsVisible, Is.False);

            GameObject cameraObject = Create("Aim Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position =
                creatureObject.transform.position + Vector3.back * 5f;
            cameraObject.transform.forward = Vector3.forward;
            Camera aimCamera = cameraObject.AddComponent<Camera>();
            Physics.SyncTransforms();

            Type aimQuery = typeof(MonsterHealthBar).Assembly.GetType(
                "Supernova.UI.MonsterCrosshairAimQuery");
            Assert.That(aimQuery, Is.Not.Null);
            SetStaticField(aimQuery, "lastQueryFrame", -1);
            SetStaticField(aimQuery, "aimedMonster", null);
            SetStaticField(aimQuery, "viewCamera", aimCamera);
            MethodInfo lateUpdate = typeof(MonsterHealthBar).GetMethod(
                "LateUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(lateUpdate, Is.Not.Null);
            lateUpdate.Invoke(healthBar, null);

            Assert.That(healthBar.IsVisible, Is.True);
        }

        [Test]
        public void PickaxeAttack_DamagesCreatureAndBroadcastsPunchCue()
        {
            GameObject playerObject = Create("Player");
            playerObject.AddComponent<CharacterController>();
            VoxelPlayerController player = playerObject.AddComponent<VoxelPlayerController>();
            SoundEffectCue punchCue =
                ScriptableObject.CreateInstance<SoundEffectCue>();

            GameObject creatureObject = Create("Creature");
            creatureObject.transform.position =
                playerObject.transform.position + Vector3.forward;
            creatureObject.AddComponent<CapsuleCollider>();
            CreatureBehaviorAgent creature = creatureObject.AddComponent<CreatureBehaviorAgent>();
            Physics.SyncTransforms();

            MethodInfo performAttack = typeof(VoxelPlayerController).GetMethod(
                "PerformAttack", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(performAttack, Is.Not.Null);

            int soundRequestCount = 0;
            SoundEffectPlaybackRequest received = default;
            System.Action<SoundEffectPlaybackRequest> observer = request =>
            {
                received = request;
                soundRequestCount++;
            };
            SoundEffectEvents.PlaybackRequested += observer;
            try
            {
                performAttack.Invoke(player, new object[] { punchCue });

                Assert.That(
                    creature.CurrentHealth,
                    Is.LessThan(creature.MaximumHealth));
                Assert.That(
                    creature.CurrentState,
                    Is.EqualTo(CreatureBehaviorState.Hurt));
                Assert.That(soundRequestCount, Is.EqualTo(1));
                Assert.That(received.Cue, Is.SameAs(punchCue));
            }
            finally
            {
                SoundEffectEvents.PlaybackRequested -= observer;
                Object.DestroyImmediate(punchCue);
            }
        }

        [Test]
        public void PickaxeAttack_EmptySwingDoesNotBroadcastPunchCue()
        {
            GameObject playerObject = Create("Player");
            playerObject.transform.position = new Vector3(20000f, 20000f, 20000f);
            playerObject.AddComponent<CharacterController>();
            VoxelPlayerController player =
                playerObject.AddComponent<VoxelPlayerController>();
            SetPrivateField(player, "view", playerObject.transform);
            SoundEffectCue punchCue =
                ScriptableObject.CreateInstance<SoundEffectCue>();
            MethodInfo performAttack = typeof(VoxelPlayerController).GetMethod(
                "PerformAttack",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(performAttack, Is.Not.Null);

            int soundRequestCount = 0;
            System.Action<SoundEffectPlaybackRequest> observer =
                _ => soundRequestCount++;
            SoundEffectEvents.PlaybackRequested += observer;
            try
            {
                performAttack.Invoke(player, new object[] { punchCue });
            }
            finally
            {
                SoundEffectEvents.PlaybackRequested -= observer;
                Object.DestroyImmediate(punchCue);
            }

            Assert.That(soundRequestCount, Is.Zero);
        }

        [Test]
        public void PickaxeLeftClickCycle_BroadcastsWooshWithoutAVoxelTarget()
        {
            GameObject playerObject = Create("Player");
            playerObject.transform.position =
                new Vector3(20000f, 20000f, 20000f);
            playerObject.AddComponent<CharacterController>();
            VoxelPlayerController player =
                playerObject.AddComponent<VoxelPlayerController>();
            PlayerToolDefinition pickaxe =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.PickaxeTool);
            MethodInfo startCycle = typeof(VoxelPlayerController).GetMethod(
                "StartToolActionCycle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(startCycle, Is.Not.Null);

            int soundRequestCount = 0;
            SoundEffectPlaybackRequest received = default;
            System.Action<SoundEffectPlaybackRequest> observer = request =>
            {
                received = request;
                soundRequestCount++;
            };
            SoundEffectEvents.PlaybackRequested += observer;
            try
            {
                Assert.That(
                    startCycle.Invoke(player, new object[] { pickaxe }),
                    Is.EqualTo(true));
            }
            finally
            {
                SoundEffectEvents.PlaybackRequested -= observer;
            }

            Assert.That(soundRequestCount, Is.EqualTo(1));
            Assert.That(received.Cue, Is.SameAs(pickaxe.PrimaryActionSound));
            Assert.That(received.Position, Is.EqualTo(playerObject.transform.position));
        }

        [Test]
        public void FlashlightThrow_BroadcastsThrowCueAfterProjectileSpawns()
        {
            GameObject playerObject = Create("Player");
            playerObject.transform.position =
                new Vector3(20000f, 20000f, 20000f);
            playerObject.AddComponent<CharacterController>();
            VoxelPlayerController player =
                playerObject.AddComponent<VoxelPlayerController>();
            SetPrivateField(player, "view", playerObject.transform);
            PlayerToolDefinition flashlight =
                AssetDatabase.LoadAssetAtPath<PlayerToolDefinition>(
                    ProjectAssetPaths.Config.FlashlightTool);
            MethodInfo executeAction = typeof(VoxelPlayerController).GetMethod(
                "ExecuteConfiguredToolAction",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(executeAction, Is.Not.Null);

            int soundRequestCount = 0;
            SoundEffectPlaybackRequest received = default;
            System.Action<SoundEffectPlaybackRequest> observer = request =>
            {
                received = request;
                soundRequestCount++;
            };
            SoundEffectEvents.PlaybackRequested += observer;
            try
            {
                Assert.That(
                    executeAction.Invoke(
                        player,
                        new object[] { flashlight }),
                    Is.EqualTo(true));
                PersistentLightProjectile[] projectiles =
                    Object.FindObjectsOfType<PersistentLightProjectile>();
                Assert.That(projectiles, Has.Length.EqualTo(1));
                objects.Add(projectiles[0].gameObject);
            }
            finally
            {
                SoundEffectEvents.PlaybackRequested -= observer;
            }

            Assert.That(soundRequestCount, Is.EqualTo(1));
            Assert.That(received.Cue, Is.SameAs(flashlight.ThrowSound));
            Assert.That(received.Position, Is.EqualTo(playerObject.transform.position));
            Assert.That(
                received.VolumeScale,
                Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void MiningAttack_WaitsForSharedVoxelImpactTime()
        {
            GameObject playerObject = Create("Player");
            playerObject.AddComponent<CharacterController>();
            VoxelPlayerController player =
                playerObject.AddComponent<VoxelPlayerController>();

            GameObject creatureObject = Create("Creature");
            creatureObject.transform.position = Vector3.forward;
            creatureObject.AddComponent<CapsuleCollider>();
            CreatureBehaviorAgent creature =
                creatureObject.AddComponent<CreatureBehaviorAgent>();
            Physics.SyncTransforms();

            MethodInfo scheduleAttack = typeof(VoxelPlayerController).GetMethod(
                "ScheduleMiningAttack",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo applyAttacks = typeof(VoxelPlayerController).GetMethod(
                "ApplyPendingMiningAttacksIfReady",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo pendingAttacksField =
                typeof(VoxelPlayerController).GetField(
                    "pendingMiningAttacks",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(scheduleAttack, Is.Not.Null);
            Assert.That(applyAttacks, Is.Not.Null);
            Assert.That(pendingAttacksField, Is.Not.Null);

            float healthBeforeImpact = creature.CurrentHealth;
            float scheduledAt = Time.time;
            scheduleAttack.Invoke(player, new object[] { 0.42f, null });
            object pendingAttacks = pendingAttacksField.GetValue(player);
            object pendingAttack = pendingAttacks.GetType()
                .GetMethod("Peek")
                .Invoke(pendingAttacks, null);
            float triggerTime = (float)pendingAttack.GetType()
                .GetProperty("TriggerTime")
                .GetValue(pendingAttack, null);
            Assert.That(
                triggerTime - scheduledAt,
                Is.EqualTo(0.42f).Within(0.001f));

            applyAttacks.Invoke(player, null);
            Assert.That(creature.CurrentHealth, Is.EqualTo(healthBeforeImpact));

            pendingAttacks.GetType()
                .GetMethod("Clear")
                .Invoke(pendingAttacks, null);
            scheduleAttack.Invoke(player, new object[] { 0f, null });
            applyAttacks.Invoke(player, null);
            Assert.That(creature.CurrentHealth, Is.LessThan(healthBeforeImpact));
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

        private static void InvokeCrouchColliderUpdate(
            VoxelPlayerController player,
            bool crouchRequested)
        {
            MethodInfo update = typeof(VoxelPlayerController).GetMethod(
                "UpdateCrouchCollider",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(update, Is.Not.Null);
            update.Invoke(player, new object[] { crouchRequested });
        }

        private static void SetStaticField(Type type, string name, object value)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(null, value);
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
