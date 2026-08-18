using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Supernova.Audio;
using Supernova.Gameplay;
using Supernova.Infrastructure;
using Supernova.MinecraftCaves;
using Supernova.Missions;
using Supernova.PortalExample;
using Supernova.UI;
using Supernova.Voxels;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools.Utils;

namespace Supernova.Tests
{
    public sealed class MissionGameLoopTests
    {
        private GameObject loopObject;
        private GameObject externalHudObject;
        private LevelConfiguration levelConfiguration;

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
            if (levelConfiguration != null)
            {
                Object.DestroyImmediate(levelConfiguration);
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
        public void SceneLoadFromBlack_PreservesOpaqueFrameBeforeLoading()
        {
            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop =
                loopObject.AddComponent<MissionGameLoop>();
            CanvasGroup fade = loop.PrepareSceneFadeFromTransparent();
            Assert.That(fade, Is.Not.Null);
            Canvas sceneTransitionCanvas = fade.GetComponent<Canvas>();
            Canvas overlayCanvas = fade.transform.parent.GetComponentInParent<Canvas>(true);
            Assert.That(sceneTransitionCanvas, Is.Not.Null);
            Assert.That(overlayCanvas, Is.Not.Null);

            fade.alpha = 0.42f;
            sceneTransitionCanvas.gameObject.SetActive(false);
            overlayCanvas.gameObject.SetActive(false);
            MethodInfo loadWithFadeInternal = typeof(MissionGameLoop).GetMethod(
                "LoadWithFadeInternal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(loadWithFadeInternal, Is.Not.Null);
            IEnumerator transition = (IEnumerator)loadWithFadeInternal.Invoke(
                loop,
                new object[] { "Unused Test Scene", true });

            Assert.That(transition.MoveNext(), Is.True);
            Assert.That(transition.Current, Is.Null);
            Assert.That(sceneTransitionCanvas.gameObject.activeSelf, Is.True);
            Assert.That(overlayCanvas.gameObject.activeSelf, Is.True);
            Assert.That(fade.gameObject.activeSelf, Is.True);
            Assert.That(
                fade.alpha,
                Is.EqualTo(1f).Within(0.0001f),
                "A pre-faded transition must stay black until scene loading begins.");
        }

        [Test]
        public void HomeObjective_ShowsCurrentCreditsBelowBaseLabel()
        {
            MethodInfo formatHomeObjective = typeof(MissionGameLoop).GetMethod(
                "FormatHomeObjective",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(formatHomeObjective, Is.Not.Null);

            string objective = (string)formatHomeObjective.Invoke(
                null,
                new object[] { 275 });
            Assert.That(objective, Is.EqualTo("基地\n当前存款  $275"));
        }

        [Test]
        public void ResultMessages_AreFullyLocalizedToChinese()
        {
            MethodInfo formatResult = typeof(MissionGameLoop).GetMethod(
                "FormatResultMessage",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(formatResult, Is.Not.Null);

            string success = (string)formatResult.Invoke(
                null,
                new object[]
                {
                    MissionOutcome.Success,
                    275,
                    100,
                    175,
                    true,
                    "02",
                });
            Assert.That(
                success,
                Is.EqualTo(
                    "任务完成\n\n已收集：$275\n存款增加：$175"
                    + "\n下一关：第02关"
                    + "\n\n按 {{input:UI/Submit}} 返回基地"));

            string lost = (string)formatResult.Invoke(
                null,
                new object[]
                {
                    MissionOutcome.LostInCaves,
                    0,
                    100,
                    0,
                    false,
                    "01",
                });
            Assert.That(
                lost,
                Is.EqualTo(
                    "任务失败\n\n撤离窗口已关闭。\n你被困在洞穴中。"
                    + "\n\n按 {{input:UI/Submit}} 返回基地"));

            string insufficient = (string)formatResult.Invoke(
                null,
                new object[]
                {
                    MissionOutcome.Fired,
                    80,
                    100,
                    0,
                    false,
                    "01",
                });
            Assert.That(
                insufficient,
                Is.EqualTo(
                    "任务失败\n\n收集的资源不足\n已收集：$80 / $100"
                    + "\n\n按 {{input:UI/Submit}} 返回基地"));
        }

        [Test]
        public void DirectHomeGameplayEntry_IsConsumedOnlyOnce()
        {
            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop =
                loopObject.AddComponent<MissionGameLoop>();
            FieldInfo directEntry = typeof(MissionGameLoop).GetField(
                "enterHomeGameplayDirectly",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(directEntry, Is.Not.Null);
            directEntry.SetValue(loop, true);

            Assert.That(
                MissionGameLoop.ConsumeDirectHomeGameplayEntry(),
                Is.True);
            Assert.That(
                MissionGameLoop.ConsumeDirectHomeGameplayEntry(),
                Is.False);
        }

        [Test]
        public void ConfigureNonHomeScene_ClearsHomeObjective()
        {
            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop = loopObject.AddComponent<MissionGameLoop>();
            MethodInfo ensureUi = typeof(MissionGameLoop).GetMethod(
                "EnsureUi",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(ensureUi, Is.Not.Null);
            ensureUi.Invoke(loop, null);
            levelConfiguration =
                ScriptableObject.CreateInstance<LevelConfiguration>();
            typeof(LevelConfiguration).GetField(
                    "homeSceneName",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(levelConfiguration, "Mission UI Home Test");
            typeof(MissionGameLoop).GetField(
                    "definition",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(loop, levelConfiguration);
            MissionUiView missionView =
                loopObject.GetComponentInChildren<MissionUiView>(true);
            Assert.That(missionView, Is.Not.Null);
            missionView.SetObjective("基地\n当前存款  $275");

            Scene nonHomeScene = SceneManager.GetActiveScene();
            Assert.That(nonHomeScene.IsValid(), Is.True);
            Assert.That(
                nonHomeScene.name,
                Is.Not.EqualTo(levelConfiguration.HomeSceneName));
            MethodInfo configureScene = typeof(MissionGameLoop).GetMethod(
                "ConfigureScene",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(configureScene, Is.Not.Null);
            configureScene.Invoke(loop, new object[] { nonHomeScene });

            FieldInfo objectiveLabelField = typeof(MissionUiView).GetField(
                "objectiveLabel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(objectiveLabelField, Is.Not.Null);
            TMPro.TMP_Text objectiveLabel =
                (TMPro.TMP_Text)objectiveLabelField.GetValue(missionView);
            Assert.That(objectiveLabel, Is.Not.Null);
            Assert.That(objectiveLabel.text, Is.Empty);
        }

        [Test]
        public void MissionPrompts_KeepHomeStartSeparateFromEarlyEvacuation()
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
            RectTransform evacuationPrompt = gameUi.transform.Find(
                UiHierarchyPaths.Mission.EarlyEvacuationPrompt)
                as RectTransform;
            RectTransform evacuationProgress = gameUi.transform.Find(
                UiHierarchyPaths.Mission.EarlyEvacuationProgress)
                as RectTransform;
            Assert.That(prompt, Is.Not.Null);
            Assert.That(evacuationPrompt, Is.Not.Null);
            Assert.That(evacuationProgress, Is.Not.Null);
            Assert.That(prompt.anchoredPosition.y, Is.GreaterThanOrEqualTo(100f));
            Assert.That(
                evacuationProgress.anchoredPosition.y,
                Is.GreaterThan(evacuationPrompt.anchoredPosition.y));
            loop.SetPrompt("按 {{input:Gameplay/Interact}} 开始任务");
            Assert.That(prompt.gameObject.activeSelf, Is.True);
            Assert.That(
                prompt.GetComponent<TMPro.TMP_Text>().text,
                Does.Contain("开始任务"));
            Assert.That(evacuationPrompt.gameObject.activeSelf, Is.False);
            Assert.That(evacuationProgress.gameObject.activeSelf, Is.False);

            MissionUiView missionView = gameUi.GetOrCreateMissionView();
            missionView.SetEarlyEvacuationState(true, 0.5f);

            Assert.That(prompt.gameObject.activeSelf, Is.True);
            Assert.That(evacuationPrompt.gameObject.activeSelf, Is.True);
            Assert.That(
                evacuationPrompt.GetComponent<TMPro.TMP_Text>().text,
                Does.Contain("提前撤离"));
            Assert.That(evacuationProgress.gameObject.activeSelf, Is.True);
            Assert.That(
                missionView.EarlyEvacuationProgressFill.anchorMax.x,
                Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(loopObject.transform.Find("Mission UI"), Is.Null);
        }

        [Test]
        public void EarlyEvacuationHold_UsesTwoSecondsAndResetsOnRelease()
        {
            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop = loopObject.AddComponent<MissionGameLoop>();
            var run = new MissionRun(60f, 100);
            run.AddDeliveredValue(100);
            typeof(MissionGameLoop).GetField(
                    "run",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(loop, run);
            typeof(MissionGameLoop).GetField(
                    "caveSetup",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(loop, true);
            MethodInfo tickHold = typeof(MissionGameLoop).GetMethod(
                "TickEarlyEvacuationHold",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(tickHold, Is.Not.Null);
            Assert.That(loop.EarlyEvacuationHoldDuration, Is.EqualTo(2f));

            tickHold.Invoke(loop, new object[] { 1f, true, false });

            Assert.That(loop.EarlyEvacuationHoldProgress, Is.EqualTo(0.5f));
            Assert.That(run.IsFinished, Is.False);
            MissionUiView missionView =
                loopObject.GetComponentInChildren<MissionUiView>(true);
            Assert.That(missionView, Is.Not.Null);
            Assert.That(
                missionView.EarlyEvacuationProgressFill.anchorMax.x,
                Is.EqualTo(0.5f).Within(0.001f));

            tickHold.Invoke(loop, new object[] { 0f, false, false });

            Assert.That(loop.EarlyEvacuationHoldProgress, Is.Zero);
            Assert.That(
                missionView.EarlyEvacuationProgressRoot.activeSelf,
                Is.False);

            tickHold.Invoke(loop, new object[] { 1.99f, true, false });

            Assert.That(
                loop.EarlyEvacuationHoldProgress,
                Is.EqualTo(0.995f).Within(0.001f));
            Assert.That(run.IsFinished, Is.False);
        }

        [Test]
        public void MissionUi_IsOwnedByPersistentMissionRoot()
        {
            externalHudObject = new GameObject("Scene HUD");
            GameHudController externalHud =
                externalHudObject.AddComponent<GameHudController>();

            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop = loopObject.AddComponent<MissionGameLoop>();
            MethodInfo ensureUi = typeof(MissionGameLoop).GetMethod(
                "EnsureUi",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(ensureUi, Is.Not.Null);
            ensureUi.Invoke(loop, null);

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
            Assert.That(
                homeCellObject.GetComponentInChildren<MissionCellButton>(),
                Is.Null);
            MissionCellZone homeZone =
                homeCellObject.GetComponentInChildren<MissionCellZone>();
            Assert.That(homeZone, Is.Not.Null);
            Assert.That(
                homeZone.transform.parent,
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

        [Test]
        public void TutorialExitTrigger_UsesCellBoundsWithoutOreExtraction()
        {
            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop = loopObject.AddComponent<MissionGameLoop>();
            MethodInfo createTutorialExitTrigger = typeof(MissionGameLoop)
                .GetMethod(
                    "CreateTutorialExitTrigger",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(createTutorialExitTrigger, Is.Not.Null);

            var cellObject = new GameObject("Tutorial Cell");
            cellObject.transform.SetParent(loopObject.transform);
            GameObject renderedCell = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            renderedCell.transform.SetParent(cellObject.transform, false);
            renderedCell.transform.localScale = new Vector3(6f, 3f, 5f);

            createTutorialExitTrigger.Invoke(
                loop,
                new object[] { cellObject.transform });

            MissionCellZone zone =
                cellObject.GetComponentInChildren<MissionCellZone>();
            Assert.That(zone, Is.Not.Null);
            Assert.That(zone.IsTutorialExitMode, Is.True);
            Assert.That(zone.GetComponent<BoxCollider>().isTrigger, Is.True);
            Assert.That(
                cellObject.GetComponentInChildren<OreExtractionZone>(),
                Is.Null);
            Assert.That(
                cellObject.GetComponentInChildren<MissionCellButton>(),
                Is.Null);
        }

        [Test]
        public void TutorialExitPrompt_UsesInteractGlyphEscape()
        {
            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop = loopObject.AddComponent<MissionGameLoop>();

            loop.ShowTutorialExitPrompt();

            GameHudController gameUi =
                loopObject.GetComponentInChildren<GameHudController>(true);
            RectTransform prompt = gameUi.transform.Find(
                UiHierarchyPaths.Mission.Prompt) as RectTransform;
            Assert.That(prompt, Is.Not.Null);
            Assert.That(
                prompt.GetComponent<TMPro.TMP_Text>().text,
                Is.EqualTo("按 {{input:Gameplay/Interact}} 结束教程"));
        }

        [Test]
        public void CellValueTrigger_CoversTheAuthoredCellBounds()
        {
            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop = loopObject.AddComponent<MissionGameLoop>();
            MethodInfo createCellTrigger = typeof(MissionGameLoop).GetMethod(
                "CreateCellTrigger",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(createCellTrigger, Is.Not.Null);

            var cellObject = new GameObject("Cave Cell");
            cellObject.transform.SetParent(loopObject.transform);
            cellObject.transform.SetPositionAndRotation(
                new Vector3(12f, 4f, -7f),
                Quaternion.Euler(0f, 90f, 0f));
            cellObject.transform.localScale = Vector3.one * 0.7f;

            GameObject authoredRoom = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            authoredRoom.name = "Authored Cell Bounds";
            authoredRoom.transform.SetParent(cellObject.transform, false);
            authoredRoom.transform.localPosition = new Vector3(1f, 2f, -0.5f);
            authoredRoom.transform.localScale = new Vector3(8f, 4f, 7f);
            Renderer roomRenderer = authoredRoom.GetComponent<Renderer>();

            createCellTrigger.Invoke(
                loop,
                new object[] { cellObject.transform, false });

            OreExtractionZone extraction =
                cellObject.GetComponentInChildren<OreExtractionZone>();
            Assert.That(extraction, Is.Not.Null);
            BoxCollider trigger = extraction.GetComponent<BoxCollider>();
            Assert.That(trigger, Is.Not.Null);
            Assert.That(trigger.bounds.min.x,
                Is.LessThanOrEqualTo(roomRenderer.bounds.min.x));
            Assert.That(trigger.bounds.min.y,
                Is.LessThanOrEqualTo(roomRenderer.bounds.min.y));
            Assert.That(trigger.bounds.min.z,
                Is.LessThanOrEqualTo(roomRenderer.bounds.min.z));
            Assert.That(trigger.bounds.max.x,
                Is.GreaterThanOrEqualTo(roomRenderer.bounds.max.x));
            Assert.That(trigger.bounds.max.y,
                Is.GreaterThanOrEqualTo(roomRenderer.bounds.max.y));
            Assert.That(trigger.bounds.max.z,
                Is.GreaterThanOrEqualTo(roomRenderer.bounds.max.z));
        }

        [Test]
        public void CellValueTrigger_IgnoresRemotePortalRenderers()
        {
            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop = loopObject.AddComponent<MissionGameLoop>();
            MethodInfo createCellTrigger = typeof(MissionGameLoop).GetMethod(
                "CreateCellTrigger",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(createCellTrigger, Is.Not.Null);

            var cellObject = new GameObject("Cave Cell");
            cellObject.transform.SetParent(loopObject.transform);
            cellObject.transform.position = new Vector3(300f, 0f, 0f);
            cellObject.transform.localScale = Vector3.one * 0.7f;

            GameObject authoredRoom = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            authoredRoom.name = "Authored Cell Bounds";
            authoredRoom.transform.SetParent(cellObject.transform, false);
            authoredRoom.transform.localScale = new Vector3(8f, 4f, 7f);
            Renderer roomRenderer = authoredRoom.GetComponent<Renderer>();

            var portalObject = new GameObject("Remote Checkpoint Portal");
            portalObject.SetActive(false);
            portalObject.transform.SetParent(cellObject.transform, false);
            portalObject.AddComponent<PortalExampleGate>();
            portalObject.transform.position = Vector3.zero;

            GameObject portalSurface = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            portalSurface.name = "Remote Portal Surface";
            portalSurface.transform.SetParent(portalObject.transform, false);
            Renderer portalRenderer = portalSurface.GetComponent<Renderer>();

            Physics.SyncTransforms();
            createCellTrigger.Invoke(
                loop,
                new object[] { cellObject.transform, false });

            OreExtractionZone extraction =
                cellObject.GetComponentInChildren<OreExtractionZone>();
            Assert.That(extraction, Is.Not.Null);
            BoxCollider trigger = extraction.GetComponent<BoxCollider>();
            Assert.That(trigger, Is.Not.Null);
            Assert.That(trigger.bounds.Contains(roomRenderer.bounds.center),
                Is.True);
            Assert.That(trigger.bounds.Contains(portalRenderer.bounds.center),
                Is.False,
                "A checkpoint portal placed in the cave must not stretch the "
                + "landing Cell's extraction trigger back to the checkpoint.");
            Assert.That(trigger.bounds.size.x, Is.LessThan(20f));
        }

        [Test]
        public void ConfiguredCellValueTrigger_UsesCabinExtractionBounds()
        {
            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop = loopObject.AddComponent<MissionGameLoop>();
            MethodInfo createCellTrigger = typeof(MissionGameLoop).GetMethod(
                "CreateCellTrigger",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(createCellTrigger, Is.Not.Null);

            var cellObject = new GameObject("Configured Cave Cell");
            cellObject.transform.SetParent(loopObject.transform);
            cellObject.transform.position = new Vector3(300f, 0f, 0f);
            cellObject.transform.localScale = Vector3.one * 0.7f;
            SpawnPointSceneStructure structure =
                cellObject.AddComponent<SpawnPointSceneStructure>();

            createCellTrigger.Invoke(
                loop,
                new object[] { cellObject.transform, false });

            OreExtractionZone extraction =
                cellObject.GetComponentInChildren<OreExtractionZone>();
            Assert.That(extraction, Is.Not.Null);
            BoxCollider trigger = extraction.GetComponent<BoxCollider>();
            Bounds expected = structure.MissionExtractionLocalBounds;
            Assert.That(trigger.center,
                Is.EqualTo(expected.center)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(trigger.size,
                Is.EqualTo(expected.size)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(
                trigger.bounds.Contains(new Vector3(300f, 0.5f, -7f)),
                Is.False,
                "The exterior portal approach must remain outside mission storage.");
        }

        [Test]
        public void OreExtraction_FirstPositiveResourceBroadcastsCoinCueAtPlayer()
        {
            GameAssetCatalog catalog =
                AssetDatabase.LoadAssetAtPath<GameAssetCatalog>(
                    ProjectAssetPaths.Config.GameAssetCatalog);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Audio.CoinDeposit, Is.Not.Null);
            Assert.That(
                catalog.Audio.CoinDeposit.SpatialBlend,
                Is.Zero.Within(0.0001f),
                "Coin deposit is a UI cue and must not use 3D attenuation.");

            loopObject = new GameObject("Mission Game Loop Test");
            MissionGameLoop loop = loopObject.AddComponent<MissionGameLoop>();

            var playerObject = new GameObject("Player");
            playerObject.transform.SetParent(loopObject.transform);
            playerObject.transform.position = new Vector3(-8f, 2f, 11f);
            playerObject.AddComponent<CharacterController>();
            playerObject.AddComponent<VoxelPlayerController>();

            var zoneObject = new GameObject("Extraction Zone");
            zoneObject.transform.SetParent(loopObject.transform);
            zoneObject.AddComponent<BoxCollider>().isTrigger = true;
            OreExtractionZone zone =
                zoneObject.AddComponent<OreExtractionZone>();
            zone.Configure(loop);

            var resourceObject = new GameObject("Valuable Resource");
            resourceObject.transform.SetParent(loopObject.transform);
            resourceObject.transform.position = new Vector3(3f, 4f, 5f);
            BoxCollider firstOverlap =
                resourceObject.AddComponent<BoxCollider>();
            BoxCollider secondOverlap =
                resourceObject.AddComponent<BoxCollider>();
            MethodInfo storeOverlap = typeof(OreExtractionZone).GetMethod(
                "StoreOverlap",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(storeOverlap, Is.Not.Null);

            int notificationCount = 0;
            SoundEffectPlaybackRequest received = default;
            System.Action<SoundEffectPlaybackRequest> observer = request =>
            {
                received = request;
                notificationCount++;
            };
            SoundEffectEvents.PlaybackRequested += observer;
            try
            {
                object[] firstArguments =
                {
                    resourceObject.GetInstanceID(),
                    resourceObject,
                    null,
                    125,
                    firstOverlap,
                };
                storeOverlap.Invoke(zone, firstArguments);
                storeOverlap.Invoke(zone, firstArguments);
                storeOverlap.Invoke(
                    zone,
                    new object[]
                    {
                        resourceObject.GetInstanceID(),
                        resourceObject,
                        null,
                        125,
                        secondOverlap,
                    });

                Assert.That(notificationCount, Is.EqualTo(1));
                Assert.That(
                    received.Cue,
                    Is.SameAs(catalog.Audio.CoinDeposit));
                Assert.That(
                    received.Position,
                    Is.EqualTo(playerObject.transform.position));
            }
            finally
            {
                SoundEffectEvents.PlaybackRequested -= observer;
            }
        }

        [Test]
        public void MissionAudio_UsesRequestedVolumeScales()
        {
            FieldInfo ambienceVolume = typeof(MissionGameLoop).GetField(
                "CaveAmbienceVolumeScale",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo transitionVolume = typeof(MissionGameLoop).GetField(
                "TransitionSoundVolumeScale",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(ambienceVolume, Is.Not.Null);
            Assert.That(transitionVolume, Is.Not.Null);
            Assert.That(
                (float)ambienceVolume.GetRawConstantValue(),
                Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(
                (float)transitionVolume.GetRawConstantValue(),
                Is.EqualTo(0.6f).Within(0.0001f));
        }

        [Test]
        public void ResultCountAnimation_EasesFromZeroToTarget()
        {
            MethodInfo evaluate = typeof(MissionUiView).GetMethod(
                "EvaluateResultCount",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(evaluate, Is.Not.Null);

            int start = (int)evaluate.Invoke(null, new object[] { 1000, 0f });
            int middle = (int)evaluate.Invoke(
                null,
                new object[] { 1000, 0.5f });
            int end = (int)evaluate.Invoke(null, new object[] { 1000, 1f });

            Assert.That(start, Is.Zero);
            Assert.That(middle, Is.GreaterThan(500));
            Assert.That(middle, Is.LessThan(1000));
            Assert.That(end, Is.EqualTo(1000));
        }

        [Test]
        public void ResultOverlay_HidesWorldValueLabelsUntilDismissed()
        {
            loopObject = new GameObject("Mission Result Overlay Test");
            MissionGameLoop loop = loopObject.AddComponent<MissionGameLoop>();
            MethodInfo ensureUi = typeof(MissionGameLoop).GetMethod(
                "EnsureUi",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(ensureUi, Is.Not.Null);
            ensureUi.Invoke(loop, null);

            var valuableObject = new GameObject("Visible World Value");
            valuableObject.transform.SetParent(loopObject.transform);
            ValuableObject valuable =
                valuableObject.AddComponent<ValuableObject>();
            valuable.Configure(344, 0.5f);
            ValuableObjectWorldUi worldUi =
                valuableObject.GetComponent<ValuableObjectWorldUi>();
            Assert.That(worldUi, Is.Not.Null);
            Assert.That(worldUi.WorldCanvas.enabled, Is.True);

            MissionUiView missionView =
                loopObject.GetComponentInChildren<MissionUiView>(true);
            missionView.ShowResult("任务失败");
            Assert.That(worldUi.WorldCanvas.enabled, Is.False);

            missionView.HideResult();
            Assert.That(worldUi.WorldCanvas.enabled, Is.True);
        }
    }
}
