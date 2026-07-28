using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Supernova.Missions;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class MissionGameLoopTests
    {
        private GameObject loopObject;

        [TearDown]
        public void TearDown()
        {
            if (loopObject != null)
            {
                Object.DestroyImmediate(loopObject);
            }
        }

        [Test]
        public void InitialSceneFade_StartsInactiveAndTransparent()
        {
            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop =
                loopObject.AddComponent<MissionGameLoop>();
            MethodInfo ensureUi = typeof(MissionGameLoop).GetMethod(
                "EnsureUi",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(ensureUi, Is.Not.Null);
            ensureUi.Invoke(loop, null);

            Transform fadeTransform = loopObject.transform.Find(
                "Mission UI/Scene Fade");
            Assert.That(fadeTransform, Is.Not.Null);

            CanvasGroup fade = fadeTransform.GetComponent<CanvasGroup>();
            Assert.That(fade, Is.Not.Null);
            Assert.That(fade.alpha, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(fade.gameObject.activeSelf, Is.False);

            MethodInfo loadWithFade = typeof(MissionGameLoop).GetMethod(
                "LoadWithFade",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(loadWithFade, Is.Not.Null);
            IEnumerator transition = (IEnumerator)loadWithFade.Invoke(
                loop,
                new object[] { "Unused Test Scene" });

            Assert.That(transition.MoveNext(), Is.True);
            Assert.That(
                transition.Current,
                Is.Null,
                "The transition must present one transparent frame before "
                + "starting its fade or any scene load.");
            Assert.That(fade.gameObject.activeSelf, Is.True);
            Assert.That(fade.alpha, Is.EqualTo(0f).Within(0.0001f));
        }
    }
}
