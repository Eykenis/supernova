using System.Reflection;
using NUnit.Framework;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class FirstPersonAnimationControllerTests
    {
        private GameObject player;

        [TearDown]
        public void TearDown()
        {
            if (player != null) Object.DestroyImmediate(player);
        }

        [Test]
        public void UnifiedController_DrivesMuryotaisuAnimatorContract()
        {
            RuntimeAnimatorController runtimeController =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    ProjectAssetPaths.ThirdParty.MuryotaisuController);
            Assert.That(runtimeController, Is.Not.Null);

            player = new GameObject("Player");
            player.AddComponent<CharacterController>();
            VoxelPlayerController controller = player.AddComponent<VoxelPlayerController>();
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(player.transform);
            Animator animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = runtimeController;
            controller.SetAnimator(animator);

            MethodInfo setState = typeof(VoxelPlayerController).GetMethod(
                "SetAnimationState", BindingFlags.Instance | BindingFlags.NonPublic);
            setState.Invoke(controller, new object[] { true, false, false });

            Assert.That(controller.CharacterAnimator, Is.SameAs(animator));
            Assert.That(animator.applyRootMotion, Is.False);
            Assert.That(animator.GetBool("walkFlag"), Is.True);
            Assert.That(animator.GetBool("jumpFlag"), Is.False);
            Assert.That(animator.GetBool("idleFlag"), Is.False);
        }
    }
}

