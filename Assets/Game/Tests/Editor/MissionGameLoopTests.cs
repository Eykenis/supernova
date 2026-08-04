using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Supernova.Missions;
using Supernova.UI;
using UnityEngine;

namespace Supernova.Tests
{
    public sealed class MissionGameLoopTests
    {
        private GameObject loopObject;
        private GameObject externalHudObject;

        [TearDown]
        public void TearDown()
        {
            if (loopObject != null)
            {
                Object.DestroyImmediate(loopObject);
            }
            if (externalHudObject != null)
            {
                Object.DestroyImmediate(externalHudObject);
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

            MissionUiView missionView =
                loopObject.GetComponentInChildren<MissionUiView>(true);
            Assert.That(missionView, Is.Not.Null);

            CanvasGroup fade = missionView.SceneFade;
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

        [Test]
        public void MissionPrompt_SitsAboveBottomHudHotbar()
        {
            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop = loopObject.AddComponent<MissionGameLoop>();
            MethodInfo ensureUi = typeof(MissionGameLoop).GetMethod(
                "EnsureUi",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(ensureUi, Is.Not.Null);
            ensureUi.Invoke(loop, null);

            GameHudController gameUi =
                loopObject.GetComponentInChildren<GameHudController>(true);
            Assert.That(gameUi, Is.Not.Null);
            RectTransform prompt = gameUi.transform.Find(
                UiHierarchyPaths.Mission.Prompt) as RectTransform;
            Assert.That(prompt, Is.Not.Null);
            Assert.That(prompt.anchoredPosition.y, Is.GreaterThanOrEqualTo(100f));
            Assert.That(loopObject.transform.Find("Mission UI"), Is.Null);
        }

        [Test]
        public void MissionUi_IsOwnedByPersistentMissionRoot()
        {
            externalHudObject = new GameObject("Scene HUD");
            GameHudController externalHud =
                externalHudObject.AddComponent<GameHudController>();

            loopObject = new GameObject("Mission Game Loop Test");
            loopObject.AddComponent<MissionGameLoop>();

            GameHudController ownedHud =
                loopObject.GetComponentInChildren<GameHudController>(true);
            Assert.That(ownedHud, Is.Not.Null);
            Assert.That(ownedHud, Is.Not.SameAs(externalHud));
            Assert.That(
                ownedHud.GetOrCreateMissionView().transform.IsChildOf(
                    loopObject.transform),
                Is.True,
                "Mission transitions must live below the persistent mission root.");
        }

        [Test]
        public void OreExtraction_IsOnlyEnabledForTheCaveCell()
        {
            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop = loopObject.AddComponent<MissionGameLoop>();
            MethodInfo createCellTrigger = typeof(MissionGameLoop).GetMethod(
                "CreateCellTrigger",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(createCellTrigger, Is.Not.Null);

            var homeCellObject = new GameObject("Home Cell");
            homeCellObject.transform.SetParent(loopObject.transform);
            createCellTrigger.Invoke(
                loop,
                new object[] { homeCellObject.transform, true });

            Assert.That(
                homeCellObject.GetComponentInChildren<OreExtractionZone>(),
                Is.Null,
                "Home must only provide the mission launch trigger.");
            MissionCellButton homeButton =
                homeCellObject.GetComponentInChildren<MissionCellButton>();
            Assert.That(homeButton, Is.Not.Null);
            Assert.That(homeButton.IsHomeMode, Is.True);
            Assert.That(
                homeButton.transform.parent,
                Is.EqualTo(homeCellObject.transform));

            var caveCellObject = new GameObject("Cave Cell");
            caveCellObject.transform.SetParent(loopObject.transform);
            createCellTrigger.Invoke(
                loop,
                new object[] { caveCellObject.transform, false });

            Assert.That(
                caveCellObject.GetComponentInChildren<OreExtractionZone>(),
                Is.Not.Null,
                "Ore extraction must be enabled after entering InfiniteCaves.");
            MissionCellButton caveButton =
                caveCellObject.GetComponentInChildren<MissionCellButton>();
            Assert.That(caveButton, Is.Not.Null);
            Assert.That(caveButton.IsHomeMode, Is.False);
            Assert.That(
                caveButton.transform.parent,
                Is.EqualTo(caveCellObject.transform));
        }
    }
}
