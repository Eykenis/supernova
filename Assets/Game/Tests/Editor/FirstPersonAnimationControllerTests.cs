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

        [Test]
        public void MagnetHoldAnimation_BlendsAndWrapsOnlyConfiguredTail()
        {
            MethodInfo wrap = typeof(VoxelPlayerController).GetMethod(
                "WrapMagnetHoldNormalizedTime",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo getBlendStart = typeof(VoxelPlayerController).GetMethod(
                "GetMagnetHoldLoopBlendStart",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(wrap, Is.Not.Null);
            Assert.That(getBlendStart, Is.Not.Null);

            float loopStart = (float)wrap.Invoke(
                null,
                new object[] { 1f, 0.7f, 1f });
            float overshoot = (float)wrap.Invoke(
                null,
                new object[] { 1.06f, 0.7f, 1f });
            float blendStart = (float)getBlendStart.Invoke(
                null,
                new object[] { 0.7f, 1f, 0.45f });
            float cappedBlendStart = (float)getBlendStart.Invoke(
                null,
                new object[] { 0.7f, 1f, 0.5f });

            Assert.That(loopStart, Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(overshoot, Is.EqualTo(0.76f).Within(0.0001f));
            Assert.That(blendStart, Is.EqualTo(0.865f).Within(0.0001f));
            Assert.That(cappedBlendStart, Is.EqualTo(0.865f).Within(0.0001f));
        }

        [Test]
        public void MagnetHeldModel_ReappearsOnlyAfterBothToolLayersRelease()
        {
            MethodInfo hasReleased = null;
            MethodInfo[] methods = typeof(VoxelPlayerController).GetMethods(
                BindingFlags.Static | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (methods[i].ReturnType == typeof(bool)
                    && parameters.Length == 5
                    && parameters[0].ParameterType == typeof(bool)
                    && parameters[1].ParameterType == typeof(bool)
                    && parameters[2].ParameterType == typeof(int)
                    && parameters[3].ParameterType == typeof(float)
                    && parameters[4].ParameterType == typeof(float))
                {
                    hasReleased = methods[i];
                    break;
                }
            }
            Assert.That(hasReleased, Is.Not.Null);

            Assert.That(
                hasReleased.Invoke(null, new object[] { false, true, 2, 0f, 0f }),
                Is.False);
            Assert.That(
                hasReleased.Invoke(null, new object[] { false, true, -1, 0.1f, 0f }),
                Is.False);
            Assert.That(
                hasReleased.Invoke(null, new object[] { false, true, -1, 0f, 0.1f }),
                Is.False);
            Assert.That(
                hasReleased.Invoke(null, new object[] { false, true, -1, 0f, 0f }),
                Is.True);
            Assert.That(
                hasReleased.Invoke(null, new object[] { true, true, 2, 1f, 0f }),
                Is.True);
        }

    }
}
