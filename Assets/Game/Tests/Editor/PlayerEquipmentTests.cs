using System.Reflection;
using NUnit.Framework;
using Supernova.Gameplay;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class PlayerEquipmentTests
    {
        private GameObject playerObject;
        private PlayerEquipmentDefinition definition;
        private JetpackEquipmentInteraction interaction;

        [TearDown]
        public void TearDown()
        {
            if (playerObject != null) Object.DestroyImmediate(playerObject);
            if (definition != null) Object.DestroyImmediate(definition);
            if (interaction != null) Object.DestroyImmediate(interaction);
        }

        [Test]
        public void BackEquipment_CreatesCustomRuntimeAndCanBeRemoved()
        {
            CreateEquipment();
            PlayerEquipmentController controller =
                playerObject.AddComponent<PlayerEquipmentController>();

            Assert.That(controller.Equip(definition), Is.True);
            Assert.That(controller.EquippedBack, Is.SameAs(definition));
            Assert.That(controller.HasBackEquipment, Is.True);

            Assert.That(controller.Unequip(PlayerEquipmentSlot.Back), Is.True);
            Assert.That(controller.EquippedBack, Is.Null);
            Assert.That(controller.HasBackEquipment, Is.False);
        }

        [Test]
        public void JetpackTrigger_StartsSmoothLaunchInsteadOfTeleporting()
        {
            CreateEquipment();
            PlayerEquipmentController controller =
                playerObject.AddComponent<PlayerEquipmentController>();
            controller.Equip(definition);
            float initialY = playerObject.transform.position.y;

            controller.TriggerEquippedInteraction();

            Assert.That(controller.IsLocomotionOverrideActive, Is.True);
            Assert.That(
                playerObject.transform.position.y - initialY,
                Is.EqualTo(0f).Within(0.001f));
            Assert.That(interaction.GetLaunchHeight(0f), Is.EqualTo(0f));
            Assert.That(
                interaction.GetLaunchHeight(interaction.LaunchDuration * 0.5f),
                Is.EqualTo(interaction.InitialLiftDistance * 0.5f).Within(0.001f));
            Assert.That(
                interaction.GetLaunchHeight(interaction.LaunchDuration),
                Is.EqualTo(interaction.InitialLiftDistance).Within(0.001f));

            controller.TriggerEquippedInteraction();
            Assert.That(controller.IsLocomotionOverrideActive, Is.False);
        }

        [Test]
        public void JetpackHoverVelocity_PreservesPlanarDirectionAndUsesSpaceShiftSpeeds()
        {
            interaction = ScriptableObject.CreateInstance<JetpackEquipmentInteraction>();
            Vector3 planar = new Vector3(0.6f, 0f, 0.8f);

            Vector3 ascending = interaction.GetHoverVelocity(planar, 4f, true, false);
            Vector3 descending = interaction.GetHoverVelocity(planar, 4f, false, true);

            Assert.That(ascending.x, Is.EqualTo(2.4f).Within(0.001f));
            Assert.That(ascending.z, Is.EqualTo(3.2f).Within(0.001f));
            Assert.That(ascending.y, Is.EqualTo(interaction.AscendSpeed).Within(0.001f));
            Assert.That(descending.y, Is.EqualTo(-interaction.DescendSpeed).Within(0.001f));
        }

        [Test]
        public void JetpackConfigAndPlayerPrefab_AreWired()
        {
            PlayerEquipmentDefinition asset =
                AssetDatabase.LoadAssetAtPath<PlayerEquipmentDefinition>(
                    "Assets/Game/Config/Equipment/Jetpack.asset");
            GameObject visual = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Equipment/Jetpack.prefab");
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Player.prefab");

            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.Slot, Is.EqualTo(PlayerEquipmentSlot.Back));
            Assert.That(asset.ActivationKey, Is.EqualTo(KeyCode.V));
            Assert.That(asset.VisualPrefab, Is.SameAs(visual));
            JetpackEquipmentInteraction jetpack =
                asset.Interaction as JetpackEquipmentInteraction;
            Assert.That(jetpack, Is.Not.Null);
            Assert.That(jetpack.LaunchAnimation, Is.Not.Null);
            Assert.That(jetpack.LaunchAnimation.name, Is.EqualTo("HoverDemo"));
            Assert.That(jetpack.HoverAnimation, Is.Not.Null);
            Assert.That(jetpack.HoverAnimation.name, Is.EqualTo("HoverLoop"));
            Assert.That(jetpack.LaunchDuration, Is.EqualTo(1f).Within(0.001f));
            Assert.That(jetpack.LaunchAnimationDuration, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(
                jetpack.GetLocomotionAnimation(0f),
                Is.SameAs(jetpack.LaunchAnimation));
            Assert.That(
                jetpack.GetLocomotionAnimation(jetpack.LaunchAnimationDuration),
                Is.SameAs(jetpack.HoverAnimation));
            PlayerEquipmentVisual equipmentVisual =
                visual.GetComponent<PlayerEquipmentVisual>();
            Assert.That(equipmentVisual, Is.Not.Null);
            Assert.That(equipmentVisual.MountAtCharacterRoot, Is.True);
            Assert.That(
                visual.transform.Find("P05_BackPack")
                    ?.GetComponent<SkinnedMeshRenderer>(),
                Is.Not.Null);
            Assert.That(visual.transform.Find("BackPack_Main"), Is.Not.Null);
            Assert.That(visual.transform.Find("BackPuck_VFX"), Is.Not.Null);
            Assert.That(visual.transform.Find("BackPuck_VFX").gameObject.activeSelf, Is.False);
            Assert.That(player.GetComponent<PlayerEquipmentController>(), Is.Not.Null);
            Assert.That(
                player.GetComponent<PlayerEquipmentController>().AvailableBack,
                Is.SameAs(asset));
        }

        [Test]
        public void JetpackAnimationExit_CompletesWhenStopIsRequestedEveryFrame()
        {
            GameObject playerPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/Player.prefab");
            AnimationClip hoverAnimation =
                AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Assets/3rd/P05_Aki & Mika/Anim_demo/HoverDemo.anim");
            AnimationClip hoverLoop =
                AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Assets/Game/Animations/HoverLoop.anim");
            playerObject = PrefabUtility.InstantiatePrefab(playerPrefab)
                as GameObject;
            VoxelPlayerController player =
                playerObject.GetComponent<VoxelPlayerController>();
            Animator animator = player.CharacterAnimator;
            MethodInfo startAnimation = GetPrivateMethod(
                typeof(VoxelPlayerController),
                "StartEquipmentLocomotionAnimation");
            MethodInfo stopAnimation = GetPrivateMethod(
                typeof(VoxelPlayerController),
                "StopEquipmentLocomotionAnimation");
            int equipmentState =
                Animator.StringToHash("Base Layer.Equipment Locomotion");

            animator.Rebind();
            animator.Update(0f);
            startAnimation.Invoke(player, new object[] { hoverAnimation });
            for (int i = 0; i < 5; i++)
                animator.Update(0.1f);

            Assert.That(
                animator.GetCurrentAnimatorStateInfo(0).fullPathHash,
                Is.EqualTo(equipmentState));

            startAnimation.Invoke(player, new object[] { hoverLoop });
            for (int i = 0; i < 3; i++)
                animator.Update(0.05f);

            AnimatorClipInfo[] activeClips = animator.GetCurrentAnimatorClipInfo(0);
            Assert.That(activeClips, Is.Not.Empty);
            Assert.That(activeClips[0].clip, Is.SameAs(hoverLoop));

            for (int i = 0; i < 5; i++)
            {
                stopAnimation.Invoke(player, new object[] { true });
                animator.Update(0.05f);
            }

            Assert.That(animator.IsInTransition(0), Is.False);
            Assert.That(
                animator.GetCurrentAnimatorStateInfo(0).fullPathHash,
                Is.Not.EqualTo(equipmentState));
        }

        private void CreateEquipment()
        {
            playerObject = new GameObject("Equipment Test Player");
            playerObject.AddComponent<CharacterController>();
            interaction = ScriptableObject.CreateInstance<JetpackEquipmentInteraction>();
            definition = ScriptableObject.CreateInstance<PlayerEquipmentDefinition>();
            SetField(definition, "displayName", "Jetpack");
            SetField(definition, "slot", PlayerEquipmentSlot.Back);
            SetField(definition, "interaction", interaction);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static MethodInfo GetPrivateMethod(
            System.Type type,
            string methodName)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return method;
        }
    }
}
